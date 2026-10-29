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
    float2 texCoord1_0 [[user(TEXCOORD_5)]];
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
    packed_uint4 lightLinkMask_0;
    packed_uint4 shadowLinkMask_0;
    packed_float4 detailUvTransformRow0_0;
    packed_float4 detailUvTransformRow1_0;
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

[[fragment]] pixelOutput_0 fragmentMain_uv_material_normal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> diffuseValuesTexture_1 [[texture(22)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> normalValuesTexture_1 [[texture(20)]], texture2d<float, access::sample> detailNormalTexture_1 [[texture(9)]], sampler detailNormalSampler_1 [[sampler(4)]], texture2d<float, access::sample> detailNormalValuesTexture_1 [[texture(21)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> emissiveValuesTexture_1 [[texture(23)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_0;
    FragmentFrameParameters_0 _S2;
    uint4 _S3;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    float3 color_0;
    float3 sampleDirection_0;
    bool _S4;
    bool _S5;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_0)->baseColorSampler_0 = baseColorSampler_1;
    (&kernelContext_0)->diffuseValuesTexture_0 = diffuseValuesTexture_1;
    (&kernelContext_0)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_0)->compositeSampler_0 = compositeSampler_1;
    (&kernelContext_0)->normalTexture_0 = normalTexture_1;
    (&kernelContext_0)->normalSampler_0 = normalSampler_1;
    (&kernelContext_0)->normalValuesTexture_0 = normalValuesTexture_1;
    (&kernelContext_0)->detailNormalTexture_0 = detailNormalTexture_1;
    (&kernelContext_0)->detailNormalSampler_0 = detailNormalSampler_1;
    (&kernelContext_0)->detailNormalValuesTexture_0 = detailNormalValuesTexture_1;
    (&kernelContext_0)->roughnessMetallicTexture_0 = roughnessMetallicTexture_1;
    (&kernelContext_0)->roughnessMetallicSampler_0 = roughnessMetallicSampler_1;
    (&kernelContext_0)->metallicTexture_0 = metallicTexture_1;
    (&kernelContext_0)->metallicSampler_0 = metallicSampler_1;
    (&kernelContext_0)->emissiveTexture_0 = emissiveTexture_1;
    (&kernelContext_0)->emissiveSampler_0 = emissiveSampler_1;
    (&kernelContext_0)->emissiveValuesTexture_0 = emissiveValuesTexture_1;
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
    SurfaceParameters_natural_0 device* _S6 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        uint _S7 = as_type<uint>((&kernelContext_0)->frameParameters_0[(64U)>>2]);
        uint _S8 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
        uint _S9 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
        uint _S10 = as_type<uint>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S7, _S8, _S9, _S10);
        index_0 = 0U;
        for(;;)
        {
            if(index_0 < 8U)
            {
            }
            else
            {
                break;
            }
            uint _S11 = 80U + index_0 * 16U;
            float _S12 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S11)>>2]);
            float _S13 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S11 + 4U)>>2]);
            float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S11 + 8U)>>2]);
            float _S15 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S11 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_0] = float4(_S12, _S13, _S14, _S15);
            index_0 = index_0 + 1U;
        }
        float _S16 = as_type<float>((&kernelContext_0)->frameParameters_0[(208U)>>2]);
        float _S17 = as_type<float>((&kernelContext_0)->frameParameters_0[(212U)>>2]);
        float _S18 = as_type<float>((&kernelContext_0)->frameParameters_0[(216U)>>2]);
        float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S16, _S17, _S18, _S19);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 12960U;
        float _S20 = as_type<float>((&kernelContext_0)->frameParameters_0[(12512U)>>2]);
        float _S21 = as_type<float>((&kernelContext_0)->frameParameters_0[(12516U)>>2]);
        float _S22 = as_type<float>((&kernelContext_0)->frameParameters_0[(12520U)>>2]);
        float _S23 = as_type<float>((&kernelContext_0)->frameParameters_0[(12524U)>>2]);
        float4 _S24 = float4(_S20, _S21, _S22, _S23);
        float _S25 = as_type<float>((&kernelContext_0)->frameParameters_0[(12528U)>>2]);
        float _S26 = as_type<float>((&kernelContext_0)->frameParameters_0[(12532U)>>2]);
        float _S27 = as_type<float>((&kernelContext_0)->frameParameters_0[(12536U)>>2]);
        float _S28 = as_type<float>((&kernelContext_0)->frameParameters_0[(12540U)>>2]);
        float4 _S29 = float4(_S25, _S26, _S27, _S28);
        float _S30 = as_type<float>((&kernelContext_0)->frameParameters_0[(12544U)>>2]);
        float _S31 = as_type<float>((&kernelContext_0)->frameParameters_0[(12548U)>>2]);
        float _S32 = as_type<float>((&kernelContext_0)->frameParameters_0[(12552U)>>2]);
        float _S33 = as_type<float>((&kernelContext_0)->frameParameters_0[(12556U)>>2]);
        float4 _S34 = float4(_S30, _S31, _S32, _S33);
        float _S35 = as_type<float>((&kernelContext_0)->frameParameters_0[(12560U)>>2]);
        float _S36 = as_type<float>((&kernelContext_0)->frameParameters_0[(12564U)>>2]);
        float _S37 = as_type<float>((&kernelContext_0)->frameParameters_0[(12568U)>>2]);
        float _S38 = as_type<float>((&kernelContext_0)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S24, _S29, _S34, float4(_S35, _S36, _S37, _S38));
        index_0 = 0U;
        for(;;)
        {
            if(index_0 < 4U)
            {
            }
            else
            {
                break;
            }
            uint _S39 = 12576U + index_0 * 64U;
            float _S40 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39)>>2]);
            float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 4U)>>2]);
            float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 8U)>>2]);
            float _S43 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 12U)>>2]);
            float4 _S44 = float4(_S40, _S41, _S42, _S43);
            float _S45 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 16U)>>2]);
            float _S46 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 20U)>>2]);
            float _S47 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 24U)>>2]);
            float _S48 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 28U)>>2]);
            float4 _S49 = float4(_S45, _S46, _S47, _S48);
            float _S50 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 32U)>>2]);
            float _S51 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 36U)>>2]);
            float _S52 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 40U)>>2]);
            float _S53 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 44U)>>2]);
            float4 _S54 = float4(_S50, _S51, _S52, _S53);
            float _S55 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 48U)>>2]);
            float _S56 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 52U)>>2]);
            float _S57 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 56U)>>2]);
            float _S58 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 60U)>>2]);
            (&frame_0)->shadowWorldToLightClip_0[index_0] = matrix<float,int(4),int(4)> (_S44, _S49, _S54, float4(_S55, _S56, _S57, _S58));
            uint _S59 = index_0 * 16U;
            uint _S60 = 12832U + _S59;
            float _S61 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S60)>>2]);
            float _S62 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S60 + 4U)>>2]);
            float _S63 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S60 + 8U)>>2]);
            float _S64 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S60 + 12U)>>2]);
            (&frame_0)->shadowTile_0[index_0] = float4(_S61, _S62, _S63, _S64);
            uint _S65 = 12896U + _S59;
            float _S66 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S65)>>2]);
            float _S67 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S65 + 4U)>>2]);
            float _S68 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S65 + 8U)>>2]);
            float _S69 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S65 + 12U)>>2]);
            (&frame_0)->shadowControls_0[index_0] = float4(_S66, _S67, _S68, _S69);
            index_0 = index_0 + 1U;
        }
        float _S70 = as_type<float>((&kernelContext_0)->frameParameters_0[(15008U)>>2]);
        float _S71 = as_type<float>((&kernelContext_0)->frameParameters_0[(15012U)>>2]);
        float _S72 = as_type<float>((&kernelContext_0)->frameParameters_0[(15016U)>>2]);
        float _S73 = as_type<float>((&kernelContext_0)->frameParameters_0[(15020U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S70, _S71, _S72, _S73);
        float _S74 = as_type<float>((&kernelContext_0)->frameParameters_0[(15024U)>>2]);
        float _S75 = as_type<float>((&kernelContext_0)->frameParameters_0[(15028U)>>2]);
        float _S76 = as_type<float>((&kernelContext_0)->frameParameters_0[(15032U)>>2]);
        float _S77 = as_type<float>((&kernelContext_0)->frameParameters_0[(15036U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S74, _S75, _S76, _S77);
        float _S78 = as_type<float>((&kernelContext_0)->frameParameters_0[(15040U)>>2]);
        float _S79 = as_type<float>((&kernelContext_0)->frameParameters_0[(15044U)>>2]);
        float _S80 = as_type<float>((&kernelContext_0)->frameParameters_0[(15048U)>>2]);
        float _S81 = as_type<float>((&kernelContext_0)->frameParameters_0[(15052U)>>2]);
        float4 _S82 = float4(_S78, _S79, _S80, _S81);
        float _S83 = as_type<float>((&kernelContext_0)->frameParameters_0[(15056U)>>2]);
        float _S84 = as_type<float>((&kernelContext_0)->frameParameters_0[(15060U)>>2]);
        float _S85 = as_type<float>((&kernelContext_0)->frameParameters_0[(15064U)>>2]);
        float _S86 = as_type<float>((&kernelContext_0)->frameParameters_0[(15068U)>>2]);
        float4 _S87 = float4(_S83, _S84, _S85, _S86);
        float _S88 = as_type<float>((&kernelContext_0)->frameParameters_0[(15072U)>>2]);
        float _S89 = as_type<float>((&kernelContext_0)->frameParameters_0[(15076U)>>2]);
        float _S90 = as_type<float>((&kernelContext_0)->frameParameters_0[(15080U)>>2]);
        float _S91 = as_type<float>((&kernelContext_0)->frameParameters_0[(15084U)>>2]);
        float4 _S92 = float4(_S88, _S89, _S90, _S91);
        float _S93 = as_type<float>((&kernelContext_0)->frameParameters_0[(15088U)>>2]);
        float _S94 = as_type<float>((&kernelContext_0)->frameParameters_0[(15092U)>>2]);
        float _S95 = as_type<float>((&kernelContext_0)->frameParameters_0[(15096U)>>2]);
        float _S96 = as_type<float>((&kernelContext_0)->frameParameters_0[(15100U)>>2]);
        float4 _S97 = float4(_S93, _S94, _S95, _S96);
        float _S98 = as_type<float>((&kernelContext_0)->frameParameters_0[(15104U)>>2]);
        float _S99 = as_type<float>((&kernelContext_0)->frameParameters_0[(15108U)>>2]);
        float _S100 = as_type<float>((&kernelContext_0)->frameParameters_0[(15112U)>>2]);
        float _S101 = as_type<float>((&kernelContext_0)->frameParameters_0[(15116U)>>2]);
        float4 _S102 = float4(_S98, _S99, _S100, _S101);
        float _S103 = as_type<float>((&kernelContext_0)->frameParameters_0[(15120U)>>2]);
        float _S104 = as_type<float>((&kernelContext_0)->frameParameters_0[(15124U)>>2]);
        float _S105 = as_type<float>((&kernelContext_0)->frameParameters_0[(15128U)>>2]);
        float _S106 = as_type<float>((&kernelContext_0)->frameParameters_0[(15132U)>>2]);
        float4 _S107 = float4(_S103, _S104, _S105, _S106);
        float _S108 = as_type<float>((&kernelContext_0)->frameParameters_0[(15136U)>>2]);
        float _S109 = as_type<float>((&kernelContext_0)->frameParameters_0[(15140U)>>2]);
        float _S110 = as_type<float>((&kernelContext_0)->frameParameters_0[(15144U)>>2]);
        float _S111 = as_type<float>((&kernelContext_0)->frameParameters_0[(15148U)>>2]);
        float4 _S112 = float4(_S108, _S109, _S110, _S111);
        float _S113 = as_type<float>((&kernelContext_0)->frameParameters_0[(15152U)>>2]);
        float _S114 = as_type<float>((&kernelContext_0)->frameParameters_0[(15156U)>>2]);
        float _S115 = as_type<float>((&kernelContext_0)->frameParameters_0[(15160U)>>2]);
        float _S116 = as_type<float>((&kernelContext_0)->frameParameters_0[(15164U)>>2]);
        float4 _S117 = float4(_S113, _S114, _S115, _S116);
        float _S118 = as_type<float>((&kernelContext_0)->frameParameters_0[(15168U)>>2]);
        float _S119 = as_type<float>((&kernelContext_0)->frameParameters_0[(15172U)>>2]);
        float _S120 = as_type<float>((&kernelContext_0)->frameParameters_0[(15176U)>>2]);
        float _S121 = as_type<float>((&kernelContext_0)->frameParameters_0[(15180U)>>2]);
        float4 _S122 = float4(_S118, _S119, _S120, _S121);
        float _S123 = as_type<float>((&kernelContext_0)->frameParameters_0[(15184U)>>2]);
        float _S124 = as_type<float>((&kernelContext_0)->frameParameters_0[(15188U)>>2]);
        float _S125 = as_type<float>((&kernelContext_0)->frameParameters_0[(15192U)>>2]);
        float _S126 = as_type<float>((&kernelContext_0)->frameParameters_0[(15196U)>>2]);
        float4 _S127 = float4(_S123, _S124, _S125, _S126);
        float _S128 = as_type<float>((&kernelContext_0)->frameParameters_0[(15200U)>>2]);
        float _S129 = as_type<float>((&kernelContext_0)->frameParameters_0[(15204U)>>2]);
        float _S130 = as_type<float>((&kernelContext_0)->frameParameters_0[(15208U)>>2]);
        float _S131 = as_type<float>((&kernelContext_0)->frameParameters_0[(15212U)>>2]);
        float4 _S132 = float4(_S128, _S129, _S130, _S131);
        float _S133 = as_type<float>((&kernelContext_0)->frameParameters_0[(15216U)>>2]);
        float _S134 = as_type<float>((&kernelContext_0)->frameParameters_0[(15220U)>>2]);
        float _S135 = as_type<float>((&kernelContext_0)->frameParameters_0[(15224U)>>2]);
        float _S136 = as_type<float>((&kernelContext_0)->frameParameters_0[(15228U)>>2]);
        float4 _S137 = float4(_S133, _S134, _S135, _S136);
        float _S138 = as_type<float>((&kernelContext_0)->frameParameters_0[(15232U)>>2]);
        float _S139 = as_type<float>((&kernelContext_0)->frameParameters_0[(15236U)>>2]);
        float _S140 = as_type<float>((&kernelContext_0)->frameParameters_0[(15240U)>>2]);
        float _S141 = as_type<float>((&kernelContext_0)->frameParameters_0[(15244U)>>2]);
        float4 _S142 = float4(_S138, _S139, _S140, _S141);
        float _S143 = as_type<float>((&kernelContext_0)->frameParameters_0[(15248U)>>2]);
        float _S144 = as_type<float>((&kernelContext_0)->frameParameters_0[(15252U)>>2]);
        float _S145 = as_type<float>((&kernelContext_0)->frameParameters_0[(15256U)>>2]);
        float _S146 = as_type<float>((&kernelContext_0)->frameParameters_0[(15260U)>>2]);
        float4 _S147 = float4(_S143, _S144, _S145, _S146);
        float _S148 = as_type<float>((&kernelContext_0)->frameParameters_0[(15264U)>>2]);
        float _S149 = as_type<float>((&kernelContext_0)->frameParameters_0[(15268U)>>2]);
        float _S150 = as_type<float>((&kernelContext_0)->frameParameters_0[(15272U)>>2]);
        float _S151 = as_type<float>((&kernelContext_0)->frameParameters_0[(15276U)>>2]);
        float4 _S152 = float4(_S148, _S149, _S150, _S151);
        float _S153 = as_type<float>((&kernelContext_0)->frameParameters_0[(15280U)>>2]);
        float _S154 = as_type<float>((&kernelContext_0)->frameParameters_0[(15284U)>>2]);
        float _S155 = as_type<float>((&kernelContext_0)->frameParameters_0[(15288U)>>2]);
        float _S156 = as_type<float>((&kernelContext_0)->frameParameters_0[(15292U)>>2]);
        float4 _S157 = float4(_S153, _S154, _S155, _S156);
        (&frame_0)->domeAmbient_0[int(0)] = _S82;
        (&frame_0)->domeAmbient_0[int(1)] = _S87;
        (&frame_0)->domeAmbient_0[int(2)] = _S92;
        (&frame_0)->domeAmbient_0[int(3)] = _S97;
        (&frame_0)->domeAmbient_0[int(4)] = _S102;
        (&frame_0)->domeAmbient_0[int(5)] = _S107;
        (&frame_0)->domeAmbient_0[int(6)] = _S112;
        (&frame_0)->domeAmbient_0[int(7)] = _S117;
        (&frame_0)->domeEnvironment_0[int(0)] = _S122;
        (&frame_0)->domeEnvironment_0[int(1)] = _S127;
        (&frame_0)->domeEnvironment_0[int(2)] = _S132;
        (&frame_0)->domeEnvironment_0[int(3)] = _S137;
        (&frame_0)->domeEnvironment_0[int(4)] = _S142;
        (&frame_0)->domeEnvironment_0[int(5)] = _S147;
        (&frame_0)->domeEnvironment_0[int(6)] = _S152;
        (&frame_0)->domeEnvironment_0[int(7)] = _S157;
        _S2 = frame_0;
        break;
    }
    for(;;)
    {
        _S3 = _S2.clipPlaneCount_0;
        uint _S158 = min(_S2.clipPlaneCount_0.x, 8U);
        index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S158)
            {
            }
            else
            {
                break;
            }
            if((dot(_S2.clipPlanes_0[index_0].xyz, _S1.eyePosition_0) + _S2.clipPlanes_0[index_0].w) < 0.0f)
            {
                discard_fragment();
            }
            index_0 = index_0 + 1U;
        }
        break;
    }
    float4 _S159 = float4(_S6->clearcoatShaded_0) ;
    float shadedMode_0 = _S159.z;
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
    float4 _S160 = float4(_S6->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S160.xyz;
    float _S161 = _S160.w;
    float3 unlitColor_0;
    if(unlit_0)
    {
        float3 unlitColor_1 = (diffuseColor_0 + emissiveColor_0) * float3(exp2((as_type<float>((_S3.z))))) ;
        if((_S3.y) == 1U)
        {
            unlitColor_0 = unlitColor_1 / (float3(1.0f)  + max(unlitColor_1, float3(0.0f, 0.0f, 0.0f)));
        }
        else
        {
            unlitColor_0 = unlitColor_1;
        }
        pixelOutput_0 _S162 = { float4(unlitColor_0, opacity_0) };
        return _S162;
    }
    float4 _S163 = float4(_S6->reserved_0) ;
    if((_S163.x) >= 0.5f)
    {
        pixelOutput_0 _S164 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S163.y) * max(0.0f, _S163.z)))) , 1.0f) };
        return _S164;
    }
    float4 _S165 = float4(_S6->metallicRoughnessThresholdWorkflow_0) ;
    float _S166 = saturate(_S165.x);
    float _S167 = clamp(_S165.y, 0.00999999977648258f, 1.0f);
    float4 _S168 = float4(_S6->specularIor_0) ;
    float3 _S169 = _S168.xyz;
    float _S170 = _S168.w;
    float _S171 = _S159.x;
    float _S172 = _S159.y;
    float4 _S173 = float4(_S6->textureControls_0) ;
    uint textureMask_0 = uint(round(_S173.x));
    uint udimMask_0 = uint(round(_S173.y));
    float4 _S174 = float4(_S6->uvTransformRow0_0) ;
    float2 _S175 = _S174.xy;
    float _S176 = _S174.z;
    float4 _S177 = float4(_S6->uvTransformRow1_0) ;
    float2 _S178 = _S177.xy;
    float _S179 = _S177.z;
    float2 _S180 = float2(dot(_S175, _S1.texCoord_0) + _S176, dot(_S178, _S1.texCoord_0) + _S179);
    uint secondaryMask_0 = uint(round(_S177.w));
    float2 _S181 = float2(dot(_S175, _S1.texCoord1_0) + _S176, dot(_S178, _S1.texCoord1_0) + _S179);
    float2 detailSource_0;
    bool hasSceneLighting_0;
    float4 _S182;
    uint3 _S183;
    if((textureMask_0 & 2U) != 0U)
    {
        if((secondaryMask_0 & 2U) != 0U)
        {
            detailSource_0 = _S181;
        }
        else
        {
            detailSource_0 = _S180;
        }
        thread float4 diffuseSample_0;
        bool _S184 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S184)
            {
                _S182 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S185 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S185).get_width(0)),(*((&atlasHeight_0)) = (_S185).get_height(0));
            int3 _S186 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S186)).xy), uint(((_S186)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S187 = atlasWidth_0;
                uint _S188 = 2U % atlasWidth_0;
                int _S189 = int(_S188);
                uint _S190 = 2U / _S187;
                int3 _S191 = int3(_S189, int(_S190), int(0));
                int _S192 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S191)).xy), uint(((_S191)).z))).x * 255.0f)));
                uint _S193 = atlasWidth_0;
                uint _S194 = 3U % atlasWidth_0;
                int _S195 = int(_S194);
                uint _S196 = 3U / _S193;
                int3 _S197 = int3(_S195, int(_S196), int(0));
                int2 minimumTile_0 = int2(_S192, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S197)).xy), uint(((_S197)).z))).x * 255.0f))));
                uint _S198 = atlasWidth_0;
                uint _S199 = 4U % atlasWidth_0;
                int _S200 = int(_S199);
                uint _S201 = 4U / _S198;
                int3 _S202 = int3(_S200, int(_S201), int(0));
                uint _S203 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S202)).xy), uint(((_S202)).z))).x * 255.0f));
                uint _S204 = atlasWidth_0;
                uint _S205 = 5U % atlasWidth_0;
                int _S206 = int(_S205);
                uint _S207 = 5U / _S204;
                int3 _S208 = int3(_S206, int(_S207), int(0));
                uint _S209 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S208)).xy), uint(((_S208)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S203, _S209);
                int2 tile_0 = int2(floor(detailSource_0)) - minimumTile_0;
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
                    int3 _S210 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S210)).xy), uint(((_S210)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S203 + uint(tile_0.x));
                uint _S211 = atlasWidth_0;
                uint _S212 = offset_0 % atlasWidth_0;
                int _S213 = int(_S212);
                uint _S214 = offset_0 / _S211;
                int3 _S215 = int3(_S213, int(_S214), int(0));
                uint _S216 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S215)).xy), uint(((_S215)).z))).x * 255.0f));
                uint _S217 = atlasWidth_0;
                uint _S218 = offset_0 + 1U;
                uint _S219 = _S218 % atlasWidth_0;
                int _S220 = int(_S219);
                uint _S221 = _S218 / _S217;
                int3 _S222 = int3(_S220, int(_S221), int(0));
                uint slot_0 = _S216 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S222)).xy), uint(((_S222)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S223 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S223)).xy), uint(((_S223)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S224 = atlasWidth_0;
                uint _S225 = 6U % atlasWidth_0;
                int _S226 = int(_S225);
                uint _S227 = 6U / _S224;
                int3 _S228 = int3(_S226, int(_S227), int(0));
                uint _S229 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S228)).xy), uint(((_S228)).z))).x * 255.0f));
                uint _S230 = atlasWidth_0;
                uint _S231 = 7U % atlasWidth_0;
                int _S232 = int(_S231);
                uint _S233 = 7U / _S230;
                int3 _S234 = int3(_S232, int(_S233), int(0));
                uint columns_0 = _S229 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S234)).xy), uint(((_S234)).z))).x * 255.0f)) << 8U);
                uint _S235 = atlasWidth_0;
                uint _S236 = 8U % atlasWidth_0;
                int _S237 = int(_S236);
                uint _S238 = 8U / _S235;
                int3 _S239 = int3(_S237, int(_S238), int(0));
                uint _S240 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S239)).xy), uint(((_S239)).z))).x * 255.0f));
                uint _S241 = atlasWidth_0;
                uint _S242 = 9U % atlasWidth_0;
                int _S243 = int(_S242);
                uint _S244 = 9U / _S241;
                int3 _S245 = int3(_S243, int(_S244), int(0));
                uint rows_0 = ((_S240 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S245)).xy), uint(((_S245)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S203 * _S209 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S246 = atlasWidth_0 / columns_0;
                float _S247 = float(_S246);
                uint _S248 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S247, float(_S248));
                uint _S249 = slot_1 % columns_0;
                float _S250 = float(_S249);
                uint _S251 = slot_1 / columns_0;
                _S182 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S250, float(_S251)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(detailSource_0) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S252 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(detailSource_0)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S252);
            }
            if(hasSceneLighting_0)
            {
                int3 _S253 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S253)).xy), uint(((_S253)).z)));
                break;
            }
            uint _S254 = atlasWidth_0 / uint(_S252.x);
            float _S255 = float(_S254);
            uint _S256 = (atlasHeight_0 - 1U) / uint(_S252.y);
            float2 cellSize_1 = float2(_S255, float(_S256));
            _S182 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S182;
        float4 _S257 = float4(_S6->compositeControls_0) ;
        float _S258 = _S257.x;
        if(_S258 == 0.0f)
        {
            hasSceneLighting_0 = (_S257.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S259 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S259).get_width(0)),(*((&sourceHeight_0)) = (_S259).get_height(0));
            texture2d<float, access::sample> _S260 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S260).get_width(0)),(*((&valueHeight_0)) = (_S260).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S261 = diffuseSample_0;
                for(;;)
                {
                    float _S262 = _S261.w;
                    if(_S262 > 1.0f)
                    {
                        diffuseColor_0 = _S261.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S263 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S263).get_width(0)),(*((&height_0)) = (_S263).get_height(0));
                    uint3 _S264 = uint3(round(_S261.xyz * float3(255.0f) ));
                    uint index_1 = ((_S264.x) | ((_S264.y) << 8U)) | ((_S264.z) << 16U);
                    if(_S262 == 0.0f)
                    {
                        _S183 = _S264;
                    }
                    else
                    {
                        _S183 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S183 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S265 = address_0.x;
                    uint _S266 = _S265 % width_0;
                    int _S267 = int(_S266);
                    uint _S268 = _S265 / width_0;
                    int3 _S269 = int3(_S267, int(_S268), int(0));
                    float _S270 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S269)).xy), uint(((_S269)).z))).x;
                    uint _S271 = address_0.y;
                    uint _S272 = _S271 % width_0;
                    int _S273 = int(_S272);
                    uint _S274 = _S271 / width_0;
                    int3 _S275 = int3(_S273, int(_S274), int(0));
                    float _S276 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S275)).xy), uint(((_S275)).z))).x;
                    uint _S277 = address_0.z;
                    uint _S278 = _S277 % width_0;
                    int _S279 = int(_S278);
                    uint _S280 = _S277 / width_0;
                    int3 _S281 = int3(_S279, int(_S280), int(0));
                    diffuseColor_0 = float3(_S270, _S276, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S281)).xy), uint(((_S281)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                diffuseSample_0.z = (((&kernelContext_0)->diffuseValuesTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (detailSource_0))).x;
            }
        }
        float4 _S282 = diffuseSample_0;
        for(;;)
        {
            if(_S258 != 2.0f)
            {
                _S182 = _S282;
                break;
            }
            bool _S283 = (_S257.w) >= 0.5f;
            for(;;)
            {
                if(!_S283)
                {
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S284 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S284).get_width(0)),(*((&atlasHeight_1)) = (_S284).get_height(0));
                int3 _S285 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S285)).xy), uint(((_S285)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S286 = atlasWidth_1;
                    uint _S287 = 2U % atlasWidth_1;
                    int _S288 = int(_S287);
                    uint _S289 = 2U / _S286;
                    int3 _S290 = int3(_S288, int(_S289), int(0));
                    int _S291 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S290)).xy), uint(((_S290)).z))).x * 255.0f)));
                    uint _S292 = atlasWidth_1;
                    uint _S293 = 3U % atlasWidth_1;
                    int _S294 = int(_S293);
                    uint _S295 = 3U / _S292;
                    int3 _S296 = int3(_S294, int(_S295), int(0));
                    int2 minimumTile_1 = int2(_S291, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S296)).xy), uint(((_S296)).z))).x * 255.0f))));
                    uint _S297 = atlasWidth_1;
                    uint _S298 = 4U % atlasWidth_1;
                    int _S299 = int(_S298);
                    uint _S300 = 4U / _S297;
                    int3 _S301 = int3(_S299, int(_S300), int(0));
                    uint _S302 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S301)).xy), uint(((_S301)).z))).x * 255.0f));
                    uint _S303 = atlasWidth_1;
                    uint _S304 = 5U % atlasWidth_1;
                    int _S305 = int(_S304);
                    uint _S306 = 5U / _S303;
                    int3 _S307 = int3(_S305, int(_S306), int(0));
                    uint _S308 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S307)).xy), uint(((_S307)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S302, _S308);
                    int2 tile_2 = int2(floor(detailSource_0)) - minimumTile_1;
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
                        int3 _S309 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S309)).xy), uint(((_S309)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S302 + uint(tile_2.x));
                    uint _S310 = atlasWidth_1;
                    uint _S311 = offset_1 % atlasWidth_1;
                    int _S312 = int(_S311);
                    uint _S313 = offset_1 / _S310;
                    int3 _S314 = int3(_S312, int(_S313), int(0));
                    uint _S315 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S314)).xy), uint(((_S314)).z))).x * 255.0f));
                    uint _S316 = atlasWidth_1;
                    uint _S317 = offset_1 + 1U;
                    uint _S318 = _S317 % atlasWidth_1;
                    int _S319 = int(_S318);
                    uint _S320 = _S317 / _S316;
                    int3 _S321 = int3(_S319, int(_S320), int(0));
                    uint slot_2 = _S315 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S321)).xy), uint(((_S321)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S322 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S322)).xy), uint(((_S322)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S323 = atlasWidth_1;
                    uint _S324 = 6U % atlasWidth_1;
                    int _S325 = int(_S324);
                    uint _S326 = 6U / _S323;
                    int3 _S327 = int3(_S325, int(_S326), int(0));
                    uint _S328 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S327)).xy), uint(((_S327)).z))).x * 255.0f));
                    uint _S329 = atlasWidth_1;
                    uint _S330 = 7U % atlasWidth_1;
                    int _S331 = int(_S330);
                    uint _S332 = 7U / _S329;
                    int3 _S333 = int3(_S331, int(_S332), int(0));
                    uint columns_1 = _S328 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S333)).xy), uint(((_S333)).z))).x * 255.0f)) << 8U);
                    uint _S334 = atlasWidth_1;
                    uint _S335 = 8U % atlasWidth_1;
                    int _S336 = int(_S335);
                    uint _S337 = 8U / _S334;
                    int3 _S338 = int3(_S336, int(_S337), int(0));
                    uint _S339 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S338)).xy), uint(((_S338)).z))).x * 255.0f));
                    uint _S340 = atlasWidth_1;
                    uint _S341 = 9U % atlasWidth_1;
                    int _S342 = int(_S341);
                    uint _S343 = 9U / _S340;
                    int3 _S344 = int3(_S342, int(_S343), int(0));
                    uint rows_1 = ((_S339 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S344)).xy), uint(((_S344)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S302 * _S308 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S345 = atlasWidth_1 / columns_1;
                    float _S346 = float(_S345);
                    uint _S347 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S346, float(_S347));
                    uint _S348 = slot_3 % columns_1;
                    float _S349 = float(_S348);
                    uint _S350 = slot_3 / columns_1;
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S349, float(_S350)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailSource_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S351 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(detailSource_0)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S351);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S352 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S352)).xy), uint(((_S352)).z)));
                    break;
                }
                uint _S353 = atlasWidth_1 / uint(_S351.x);
                float _S354 = float(_S353);
                uint _S355 = (atlasHeight_1 - 1U) / uint(_S351.y);
                float2 cellSize_3 = float2(_S354, float(_S355));
                _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S257.y));
            if(operation_0 == 1U)
            {
                _S182 = _S282 * _S182;
                break;
            }
            if(operation_0 == 2U)
            {
                _S182 = _S282 + _S182;
                break;
            }
            if(operation_0 == 3U)
            {
                _S182 = _S282 - _S182;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S257.z;
                _S182 = _S282 * float4((1.0f - factor_0))  + _S182 * float4(factor_0) ;
                break;
            }
            _S182 = _S282;
            break;
        }
        diffuseColor_0 = _S182.xyz;
    }
    thread float4 baseNormalSample_0;
    if((secondaryMask_0 & 4U) != 0U)
    {
        detailSource_0 = _S181;
    }
    else
    {
        detailSource_0 = _S180;
    }
    bool _S356 = (udimMask_0 & 4U) != 0U;
    for(;;)
    {
        if(!_S356)
        {
            _S182 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (detailSource_0)));
            break;
        }
        texture2d<float, access::sample> _S357 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S357).get_width(0)),(*((&atlasHeight_2)) = (_S357).get_height(0));
        int3 _S358 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S358)).xy), uint(((_S358)).z))) * float4(255.0f) );
        if((metadata_2.x) == 255.0f)
        {
            uint _S359 = atlasWidth_2;
            uint _S360 = 2U % atlasWidth_2;
            int _S361 = int(_S360);
            uint _S362 = 2U / _S359;
            int3 _S363 = int3(_S361, int(_S362), int(0));
            int _S364 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S363)).xy), uint(((_S363)).z))).x * 255.0f)));
            uint _S365 = atlasWidth_2;
            uint _S366 = 3U % atlasWidth_2;
            int _S367 = int(_S366);
            uint _S368 = 3U / _S365;
            int3 _S369 = int3(_S367, int(_S368), int(0));
            int2 minimumTile_2 = int2(_S364, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S369)).xy), uint(((_S369)).z))).x * 255.0f))));
            uint _S370 = atlasWidth_2;
            uint _S371 = 4U % atlasWidth_2;
            int _S372 = int(_S371);
            uint _S373 = 4U / _S370;
            int3 _S374 = int3(_S372, int(_S373), int(0));
            uint _S375 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S374)).xy), uint(((_S374)).z))).x * 255.0f));
            uint _S376 = atlasWidth_2;
            uint _S377 = 5U % atlasWidth_2;
            int _S378 = int(_S377);
            uint _S379 = 5U / _S376;
            int3 _S380 = int3(_S378, int(_S379), int(0));
            uint _S381 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S380)).xy), uint(((_S380)).z))).x * 255.0f));
            uint2 grid_2 = uint2(_S375, _S381);
            int2 tile_4 = int2(floor(detailSource_0)) - minimumTile_2;
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
                int3 _S382 = int3(int(1), int(0), int(0));
                _S182 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S382)).xy), uint(((_S382)).z)));
                break;
            }
            uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S375 + uint(tile_4.x));
            uint _S383 = atlasWidth_2;
            uint _S384 = offset_2 % atlasWidth_2;
            int _S385 = int(_S384);
            uint _S386 = offset_2 / _S383;
            int3 _S387 = int3(_S385, int(_S386), int(0));
            uint _S388 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S387)).xy), uint(((_S387)).z))).x * 255.0f));
            uint _S389 = atlasWidth_2;
            uint _S390 = offset_2 + 1U;
            uint _S391 = _S390 % atlasWidth_2;
            int _S392 = int(_S391);
            uint _S393 = _S390 / _S389;
            int3 _S394 = int3(_S392, int(_S393), int(0));
            uint slot_4 = _S388 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S394)).xy), uint(((_S394)).z))).x * 255.0f)) << 8U);
            if(slot_4 == 0U)
            {
                int3 _S395 = int3(int(1), int(0), int(0));
                _S182 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S395)).xy), uint(((_S395)).z)));
                break;
            }
            uint slot_5 = slot_4 - 1U;
            uint _S396 = atlasWidth_2;
            uint _S397 = 6U % atlasWidth_2;
            int _S398 = int(_S397);
            uint _S399 = 6U / _S396;
            int3 _S400 = int3(_S398, int(_S399), int(0));
            uint _S401 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S400)).xy), uint(((_S400)).z))).x * 255.0f));
            uint _S402 = atlasWidth_2;
            uint _S403 = 7U % atlasWidth_2;
            int _S404 = int(_S403);
            uint _S405 = 7U / _S402;
            int3 _S406 = int3(_S404, int(_S405), int(0));
            uint columns_2 = _S401 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S406)).xy), uint(((_S406)).z))).x * 255.0f)) << 8U);
            uint _S407 = atlasWidth_2;
            uint _S408 = 8U % atlasWidth_2;
            int _S409 = int(_S408);
            uint _S410 = 8U / _S407;
            int3 _S411 = int3(_S409, int(_S410), int(0));
            uint _S412 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S411)).xy), uint(((_S411)).z))).x * 255.0f));
            uint _S413 = atlasWidth_2;
            uint _S414 = 9U % atlasWidth_2;
            int _S415 = int(_S414);
            uint _S416 = 9U / _S413;
            int3 _S417 = int3(_S415, int(_S416), int(0));
            uint rows_2 = ((_S412 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S417)).xy), uint(((_S417)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
            uint metadataRows_2 = (10U + 2U * _S375 * _S381 + atlasWidth_2 - 1U) / atlasWidth_2;
            uint _S418 = atlasWidth_2 / columns_2;
            float _S419 = float(_S418);
            uint _S420 = (atlasHeight_2 - metadataRows_2) / rows_2;
            float2 cellSize_4 = float2(_S419, float(_S420));
            uint _S421 = slot_5 % columns_2;
            float _S422 = float(_S421);
            uint _S423 = slot_5 / columns_2;
            _S182 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S422, float(_S423)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(detailSource_0) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        int2 _S424 = int2(metadata_2.zw);
        int2 tile_5 = int2(floor(detailSource_0)) - int2(metadata_2.xy);
        if(any(tile_5 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_5 >= _S424);
        }
        if(hasSceneLighting_0)
        {
            int3 _S425 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S182 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S425)).xy), uint(((_S425)).z)));
            break;
        }
        uint _S426 = atlasWidth_2 / uint(_S424.x);
        float _S427 = float(_S426);
        uint _S428 = (atlasHeight_2 - 1U) / uint(_S424.y);
        float2 cellSize_5 = float2(_S427, float(_S428));
        _S182 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    baseNormalSample_0 = _S182;
    float4 _S429 = float4(_S6->detailUvTransformRow0_0) ;
    if((_S429.w) != 0.0f)
    {
        float4 _S430 = baseNormalSample_0;
        for(;;)
        {
            float _S431 = _S430.w;
            if(_S431 > 1.0f)
            {
                unlitColor_0 = _S430.xyz;
                break;
            }
            texture2d<float, access::sample> _S432 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_1;
            thread uint height_1;
            (*((&width_1)) = (_S432).get_width(0)),(*((&height_1)) = (_S432).get_height(0));
            uint3 _S433 = uint3(round(_S430.xyz * float3(255.0f) ));
            uint index_2 = ((_S433.x) | ((_S433.y) << 8U)) | ((_S433.z) << 16U);
            if(_S431 == 0.0f)
            {
                _S183 = _S433;
            }
            else
            {
                _S183 = uint3(index_2, index_2, index_2);
            }
            uint3 address_1 = _S183 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S434 = address_1.x;
            uint _S435 = _S434 % width_1;
            int _S436 = int(_S435);
            uint _S437 = _S434 / width_1;
            int3 _S438 = int3(_S436, int(_S437), int(0));
            float _S439 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S438)).xy), uint(((_S438)).z))).x;
            uint _S440 = address_1.y;
            uint _S441 = _S440 % width_1;
            int _S442 = int(_S441);
            uint _S443 = _S440 / width_1;
            int3 _S444 = int3(_S442, int(_S443), int(0));
            float _S445 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S444)).xy), uint(((_S444)).z))).x;
            uint _S446 = address_1.z;
            uint _S447 = _S446 % width_1;
            int _S448 = int(_S447);
            uint _S449 = _S446 / width_1;
            int3 _S450 = int3(_S448, int(_S449), int(0));
            unlitColor_0 = float3(_S439, _S445, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S450)).xy), uint(((_S450)).z))).x);
            break;
        }
        baseNormalSample_0.xyz = unlitColor_0;
    }
    if((textureMask_0 & 4U) == 0U)
    {
        unlitColor_0 = float3(0.0f, 0.0f, 1.0f);
    }
    else
    {
        unlitColor_0 = baseNormalSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
    }
    thread float3 _S451 = unlitColor_0;
    float2 _S452 = float2(0.0f, 0.0f);
    if(any(_S178 != _S452))
    {
        float _S453 = _S178.x;
        float _S454 = _S178.y;
        float2 tangent_1 = normalize(float2(_S454, - _S453) / float2(max(abs(_S453), abs(_S454))) );
        float2 bitangent_0 = float2(- tangent_1.y, tangent_1.x);
        if((dot(_S175, tangent_1)) < 0.0f)
        {
            detailSource_0 = - tangent_1;
        }
        else
        {
            detailSource_0 = tangent_1;
        }
        _S451.xy = detailSource_0 * float2(_S451.x)  + bitangent_0 * float2(_S451.y) ;
    }
    float3 _S455 = _S451;
    if((textureMask_0 & 4096U) != 0U)
    {
        if((secondaryMask_0 & 4096U) != 0U)
        {
            detailSource_0 = _S1.texCoord1_0;
        }
        else
        {
            detailSource_0 = _S1.texCoord_0;
        }
        float2 _S456 = _S429.xy;
        float4 _S457 = float4(_S6->detailUvTransformRow1_0) ;
        float2 _S458 = _S457.xy;
        float2 detailUv_0 = float2(dot(_S456, detailSource_0) + _S429.z, dot(_S458, detailSource_0) + _S457.z);
        thread float4 detailSample_0;
        bool _S459 = (udimMask_0 & 4096U) != 0U;
        for(;;)
        {
            if(!_S459)
            {
                _S182 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailUv_0)));
                break;
            }
            texture2d<float, access::sample> _S460 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S460).get_width(0)),(*((&atlasHeight_3)) = (_S460).get_height(0));
            int3 _S461 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S461)).xy), uint(((_S461)).z))) * float4(255.0f) );
            if((metadata_3.x) == 255.0f)
            {
                uint _S462 = atlasWidth_3;
                uint _S463 = 2U % atlasWidth_3;
                int _S464 = int(_S463);
                uint _S465 = 2U / _S462;
                int3 _S466 = int3(_S464, int(_S465), int(0));
                int _S467 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S466)).xy), uint(((_S466)).z))).x * 255.0f)));
                uint _S468 = atlasWidth_3;
                uint _S469 = 3U % atlasWidth_3;
                int _S470 = int(_S469);
                uint _S471 = 3U / _S468;
                int3 _S472 = int3(_S470, int(_S471), int(0));
                int2 minimumTile_3 = int2(_S467, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S472)).xy), uint(((_S472)).z))).x * 255.0f))));
                uint _S473 = atlasWidth_3;
                uint _S474 = 4U % atlasWidth_3;
                int _S475 = int(_S474);
                uint _S476 = 4U / _S473;
                int3 _S477 = int3(_S475, int(_S476), int(0));
                uint _S478 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S477)).xy), uint(((_S477)).z))).x * 255.0f));
                uint _S479 = atlasWidth_3;
                uint _S480 = 5U % atlasWidth_3;
                int _S481 = int(_S480);
                uint _S482 = 5U / _S479;
                int3 _S483 = int3(_S481, int(_S482), int(0));
                uint _S484 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S483)).xy), uint(((_S483)).z))).x * 255.0f));
                uint2 grid_3 = uint2(_S478, _S484);
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
                    int3 _S485 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S485)).xy), uint(((_S485)).z)));
                    break;
                }
                uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S478 + uint(tile_6.x));
                uint _S486 = atlasWidth_3;
                uint _S487 = offset_3 % atlasWidth_3;
                int _S488 = int(_S487);
                uint _S489 = offset_3 / _S486;
                int3 _S490 = int3(_S488, int(_S489), int(0));
                uint _S491 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S490)).xy), uint(((_S490)).z))).x * 255.0f));
                uint _S492 = atlasWidth_3;
                uint _S493 = offset_3 + 1U;
                uint _S494 = _S493 % atlasWidth_3;
                int _S495 = int(_S494);
                uint _S496 = _S493 / _S492;
                int3 _S497 = int3(_S495, int(_S496), int(0));
                uint slot_6 = _S491 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S497)).xy), uint(((_S497)).z))).x * 255.0f)) << 8U);
                if(slot_6 == 0U)
                {
                    int3 _S498 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S498)).xy), uint(((_S498)).z)));
                    break;
                }
                uint slot_7 = slot_6 - 1U;
                uint _S499 = atlasWidth_3;
                uint _S500 = 6U % atlasWidth_3;
                int _S501 = int(_S500);
                uint _S502 = 6U / _S499;
                int3 _S503 = int3(_S501, int(_S502), int(0));
                uint _S504 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S503)).xy), uint(((_S503)).z))).x * 255.0f));
                uint _S505 = atlasWidth_3;
                uint _S506 = 7U % atlasWidth_3;
                int _S507 = int(_S506);
                uint _S508 = 7U / _S505;
                int3 _S509 = int3(_S507, int(_S508), int(0));
                uint columns_3 = _S504 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S509)).xy), uint(((_S509)).z))).x * 255.0f)) << 8U);
                uint _S510 = atlasWidth_3;
                uint _S511 = 8U % atlasWidth_3;
                int _S512 = int(_S511);
                uint _S513 = 8U / _S510;
                int3 _S514 = int3(_S512, int(_S513), int(0));
                uint _S515 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S514)).xy), uint(((_S514)).z))).x * 255.0f));
                uint _S516 = atlasWidth_3;
                uint _S517 = 9U % atlasWidth_3;
                int _S518 = int(_S517);
                uint _S519 = 9U / _S516;
                int3 _S520 = int3(_S518, int(_S519), int(0));
                uint rows_3 = ((_S515 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S520)).xy), uint(((_S520)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                uint metadataRows_3 = (10U + 2U * _S478 * _S484 + atlasWidth_3 - 1U) / atlasWidth_3;
                uint _S521 = atlasWidth_3 / columns_3;
                float _S522 = float(_S521);
                uint _S523 = (atlasHeight_3 - metadataRows_3) / rows_3;
                float2 cellSize_6 = float2(_S522, float(_S523));
                uint _S524 = slot_7 % columns_3;
                float _S525 = float(_S524);
                uint _S526 = slot_7 / columns_3;
                _S182 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S525, float(_S526)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(detailUv_0) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            int2 _S527 = int2(metadata_3.zw);
            int2 tile_7 = int2(floor(detailUv_0)) - int2(metadata_3.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S527);
            }
            if(hasSceneLighting_0)
            {
                int3 _S528 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S528)).xy), uint(((_S528)).z)));
                break;
            }
            uint _S529 = atlasWidth_3 / uint(_S527.x);
            float _S530 = float(_S529);
            uint _S531 = (atlasHeight_3 - 1U) / uint(_S527.y);
            float2 cellSize_7 = float2(_S530, float(_S531));
            _S182 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        detailSample_0 = _S182;
        if((_S457.w) != 0.0f)
        {
            float4 _S532 = detailSample_0;
            for(;;)
            {
                float _S533 = _S532.w;
                if(_S533 > 1.0f)
                {
                    unlitColor_0 = _S532.xyz;
                    break;
                }
                texture2d<float, access::sample> _S534 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_2;
                thread uint height_2;
                (*((&width_2)) = (_S534).get_width(0)),(*((&height_2)) = (_S534).get_height(0));
                uint3 _S535 = uint3(round(_S532.xyz * float3(255.0f) ));
                uint index_3 = ((_S535.x) | ((_S535.y) << 8U)) | ((_S535.z) << 16U);
                if(_S533 == 0.0f)
                {
                    _S183 = _S535;
                }
                else
                {
                    _S183 = uint3(index_3, index_3, index_3);
                }
                uint3 address_2 = _S183 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S536 = address_2.x;
                uint _S537 = _S536 % width_2;
                int _S538 = int(_S537);
                uint _S539 = _S536 / width_2;
                int3 _S540 = int3(_S538, int(_S539), int(0));
                float _S541 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S540)).xy), uint(((_S540)).z))).x;
                uint _S542 = address_2.y;
                uint _S543 = _S542 % width_2;
                int _S544 = int(_S543);
                uint _S545 = _S542 / width_2;
                int3 _S546 = int3(_S544, int(_S545), int(0));
                float _S547 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S546)).xy), uint(((_S546)).z))).x;
                uint _S548 = address_2.z;
                uint _S549 = _S548 % width_2;
                int _S550 = int(_S549);
                uint _S551 = _S548 / width_2;
                int3 _S552 = int3(_S550, int(_S551), int(0));
                unlitColor_0 = float3(_S541, _S547, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S552)).xy), uint(((_S552)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S553 = detail_0;
        if(any(_S458 != _S452))
        {
            float _S554 = _S458.x;
            float _S555 = _S458.y;
            float2 tangent_2 = normalize(float2(_S555, - _S554) / float2(max(abs(_S554), abs(_S555))) );
            float2 bitangent_1 = float2(- tangent_2.y, tangent_2.x);
            float2 tangent_3;
            if((dot(_S456, tangent_2)) < 0.0f)
            {
                tangent_3 = - tangent_2;
            }
            else
            {
                tangent_3 = tangent_2;
            }
            _S553.xy = tangent_3 * float2(_S553.x)  + bitangent_1 * float2(_S553.y) ;
        }
        float3 _S556 = _S553;
        for(;;)
        {
            float3 t_0 = normalize(_S455) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S556) * float3(-1.0f, -1.0f, 1.0f);
            float _S557 = t_0.z;
            if(_S557 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S557))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S455;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S558 = float3(unlitColor_0.x) ;
    float3 _S559 = float3(unlitColor_0.y) ;
    float3 _S560 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S558 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S559 + _S1.normal_0 * _S560);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S558 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S559 + worldNormalBasis_0 * _S560);
    float roughness_0;
    float4 _S561;
    if((textureMask_0 & 8U) != 0U)
    {
        if((secondaryMask_0 & 8U) != 0U)
        {
            detailSource_0 = _S181;
        }
        else
        {
            detailSource_0 = _S180;
        }
        bool _S562 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S562)
            {
                _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S563 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S563).get_width(0)),(*((&atlasHeight_4)) = (_S563).get_height(0));
            int3 _S564 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S564)).xy), uint(((_S564)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S565 = atlasWidth_4;
                uint _S566 = 2U % atlasWidth_4;
                int _S567 = int(_S566);
                uint _S568 = 2U / _S565;
                int3 _S569 = int3(_S567, int(_S568), int(0));
                int _S570 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S569)).xy), uint(((_S569)).z))).x * 255.0f)));
                uint _S571 = atlasWidth_4;
                uint _S572 = 3U % atlasWidth_4;
                int _S573 = int(_S572);
                uint _S574 = 3U / _S571;
                int3 _S575 = int3(_S573, int(_S574), int(0));
                int2 minimumTile_4 = int2(_S570, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S575)).xy), uint(((_S575)).z))).x * 255.0f))));
                uint _S576 = atlasWidth_4;
                uint _S577 = 4U % atlasWidth_4;
                int _S578 = int(_S577);
                uint _S579 = 4U / _S576;
                int3 _S580 = int3(_S578, int(_S579), int(0));
                uint _S581 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S580)).xy), uint(((_S580)).z))).x * 255.0f));
                uint _S582 = atlasWidth_4;
                uint _S583 = 5U % atlasWidth_4;
                int _S584 = int(_S583);
                uint _S585 = 5U / _S582;
                int3 _S586 = int3(_S584, int(_S585), int(0));
                uint _S587 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S586)).xy), uint(((_S586)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S581, _S587);
                int2 tile_8 = int2(floor(detailSource_0)) - minimumTile_4;
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
                    int3 _S588 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S588)).xy), uint(((_S588)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S581 + uint(tile_8.x));
                uint _S589 = atlasWidth_4;
                uint _S590 = offset_4 % atlasWidth_4;
                int _S591 = int(_S590);
                uint _S592 = offset_4 / _S589;
                int3 _S593 = int3(_S591, int(_S592), int(0));
                uint _S594 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S593)).xy), uint(((_S593)).z))).x * 255.0f));
                uint _S595 = atlasWidth_4;
                uint _S596 = offset_4 + 1U;
                uint _S597 = _S596 % atlasWidth_4;
                int _S598 = int(_S597);
                uint _S599 = _S596 / _S595;
                int3 _S600 = int3(_S598, int(_S599), int(0));
                uint slot_8 = _S594 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S600)).xy), uint(((_S600)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S601 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S601)).xy), uint(((_S601)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S602 = atlasWidth_4;
                uint _S603 = 6U % atlasWidth_4;
                int _S604 = int(_S603);
                uint _S605 = 6U / _S602;
                int3 _S606 = int3(_S604, int(_S605), int(0));
                uint _S607 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S606)).xy), uint(((_S606)).z))).x * 255.0f));
                uint _S608 = atlasWidth_4;
                uint _S609 = 7U % atlasWidth_4;
                int _S610 = int(_S609);
                uint _S611 = 7U / _S608;
                int3 _S612 = int3(_S610, int(_S611), int(0));
                uint columns_4 = _S607 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S612)).xy), uint(((_S612)).z))).x * 255.0f)) << 8U);
                uint _S613 = atlasWidth_4;
                uint _S614 = 8U % atlasWidth_4;
                int _S615 = int(_S614);
                uint _S616 = 8U / _S613;
                int3 _S617 = int3(_S615, int(_S616), int(0));
                uint _S618 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S617)).xy), uint(((_S617)).z))).x * 255.0f));
                uint _S619 = atlasWidth_4;
                uint _S620 = 9U % atlasWidth_4;
                int _S621 = int(_S620);
                uint _S622 = 9U / _S619;
                int3 _S623 = int3(_S621, int(_S622), int(0));
                uint rows_4 = ((_S618 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S623)).xy), uint(((_S623)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S581 * _S587 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S624 = atlasWidth_4 / columns_4;
                float _S625 = float(_S624);
                uint _S626 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S625, float(_S626));
                uint _S627 = slot_9 % columns_4;
                float _S628 = float(_S627);
                uint _S629 = slot_9 / columns_4;
                _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S628, float(_S629)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(detailSource_0) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S630 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(detailSource_0)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S630);
            }
            if(hasSceneLighting_0)
            {
                int3 _S631 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S631)).xy), uint(((_S631)).z)));
                break;
            }
            uint _S632 = atlasWidth_4 / uint(_S630.x);
            float _S633 = float(_S632);
            uint _S634 = (atlasHeight_4 - 1U) / uint(_S630.y);
            float2 cellSize_9 = float2(_S633, float(_S634));
            _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S635 = float4(_S6->compositeControls_0) ;
            if((_S635.x) != 8.0f)
            {
                break;
            }
            bool _S636 = (_S635.w) >= 0.5f;
            for(;;)
            {
                if(!_S636)
                {
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S637 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S637).get_width(0)),(*((&atlasHeight_5)) = (_S637).get_height(0));
                int3 _S638 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S638)).xy), uint(((_S638)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S639 = atlasWidth_5;
                    uint _S640 = 2U % atlasWidth_5;
                    int _S641 = int(_S640);
                    uint _S642 = 2U / _S639;
                    int3 _S643 = int3(_S641, int(_S642), int(0));
                    int _S644 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S643)).xy), uint(((_S643)).z))).x * 255.0f)));
                    uint _S645 = atlasWidth_5;
                    uint _S646 = 3U % atlasWidth_5;
                    int _S647 = int(_S646);
                    uint _S648 = 3U / _S645;
                    int3 _S649 = int3(_S647, int(_S648), int(0));
                    int2 minimumTile_5 = int2(_S644, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S649)).xy), uint(((_S649)).z))).x * 255.0f))));
                    uint _S650 = atlasWidth_5;
                    uint _S651 = 4U % atlasWidth_5;
                    int _S652 = int(_S651);
                    uint _S653 = 4U / _S650;
                    int3 _S654 = int3(_S652, int(_S653), int(0));
                    uint _S655 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S654)).xy), uint(((_S654)).z))).x * 255.0f));
                    uint _S656 = atlasWidth_5;
                    uint _S657 = 5U % atlasWidth_5;
                    int _S658 = int(_S657);
                    uint _S659 = 5U / _S656;
                    int3 _S660 = int3(_S658, int(_S659), int(0));
                    uint _S661 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S660)).xy), uint(((_S660)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S655, _S661);
                    int2 tile_10 = int2(floor(detailSource_0)) - minimumTile_5;
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
                        int3 _S662 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S662)).xy), uint(((_S662)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S655 + uint(tile_10.x));
                    uint _S663 = atlasWidth_5;
                    uint _S664 = offset_5 % atlasWidth_5;
                    int _S665 = int(_S664);
                    uint _S666 = offset_5 / _S663;
                    int3 _S667 = int3(_S665, int(_S666), int(0));
                    uint _S668 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S667)).xy), uint(((_S667)).z))).x * 255.0f));
                    uint _S669 = atlasWidth_5;
                    uint _S670 = offset_5 + 1U;
                    uint _S671 = _S670 % atlasWidth_5;
                    int _S672 = int(_S671);
                    uint _S673 = _S670 / _S669;
                    int3 _S674 = int3(_S672, int(_S673), int(0));
                    uint slot_10 = _S668 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S674)).xy), uint(((_S674)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S675 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S675)).xy), uint(((_S675)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S676 = atlasWidth_5;
                    uint _S677 = 6U % atlasWidth_5;
                    int _S678 = int(_S677);
                    uint _S679 = 6U / _S676;
                    int3 _S680 = int3(_S678, int(_S679), int(0));
                    uint _S681 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S680)).xy), uint(((_S680)).z))).x * 255.0f));
                    uint _S682 = atlasWidth_5;
                    uint _S683 = 7U % atlasWidth_5;
                    int _S684 = int(_S683);
                    uint _S685 = 7U / _S682;
                    int3 _S686 = int3(_S684, int(_S685), int(0));
                    uint columns_5 = _S681 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S686)).xy), uint(((_S686)).z))).x * 255.0f)) << 8U);
                    uint _S687 = atlasWidth_5;
                    uint _S688 = 8U % atlasWidth_5;
                    int _S689 = int(_S688);
                    uint _S690 = 8U / _S687;
                    int3 _S691 = int3(_S689, int(_S690), int(0));
                    uint _S692 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S691)).xy), uint(((_S691)).z))).x * 255.0f));
                    uint _S693 = atlasWidth_5;
                    uint _S694 = 9U % atlasWidth_5;
                    int _S695 = int(_S694);
                    uint _S696 = 9U / _S693;
                    int3 _S697 = int3(_S695, int(_S696), int(0));
                    uint rows_5 = ((_S692 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S697)).xy), uint(((_S697)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S655 * _S661 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S698 = atlasWidth_5 / columns_5;
                    float _S699 = float(_S698);
                    uint _S700 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S699, float(_S700));
                    uint _S701 = slot_11 % columns_5;
                    float _S702 = float(_S701);
                    uint _S703 = slot_11 / columns_5;
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S702, float(_S703)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(detailSource_0) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S704 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(detailSource_0)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S704);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S705 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S705)).xy), uint(((_S705)).z)));
                    break;
                }
                uint _S706 = atlasWidth_5 / uint(_S704.x);
                float _S707 = float(_S706);
                uint _S708 = (atlasHeight_5 - 1U) / uint(_S704.y);
                float2 cellSize_11 = float2(_S707, float(_S708));
                _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_1 = uint(round(_S635.y));
            if(operation_1 == 1U)
            {
                _S182 = _S182 * _S561;
                break;
            }
            if(operation_1 == 2U)
            {
                _S182 = _S182 + _S561;
                break;
            }
            if(operation_1 == 3U)
            {
                _S182 = _S182 - _S561;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S635.z;
                _S182 = _S182 * float4((1.0f - factor_1))  + _S561 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S182.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S167;
    }
    float metallic_0;
    if((textureMask_0 & 32U) != 0U)
    {
        if((secondaryMask_0 & 32U) != 0U)
        {
            detailSource_0 = _S181;
        }
        else
        {
            detailSource_0 = _S180;
        }
        bool _S709 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S709)
            {
                _S182 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S710 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S710).get_width(0)),(*((&atlasHeight_6)) = (_S710).get_height(0));
            int3 _S711 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S711)).xy), uint(((_S711)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S712 = atlasWidth_6;
                uint _S713 = 2U % atlasWidth_6;
                int _S714 = int(_S713);
                uint _S715 = 2U / _S712;
                int3 _S716 = int3(_S714, int(_S715), int(0));
                int _S717 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S716)).xy), uint(((_S716)).z))).x * 255.0f)));
                uint _S718 = atlasWidth_6;
                uint _S719 = 3U % atlasWidth_6;
                int _S720 = int(_S719);
                uint _S721 = 3U / _S718;
                int3 _S722 = int3(_S720, int(_S721), int(0));
                int2 minimumTile_6 = int2(_S717, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S722)).xy), uint(((_S722)).z))).x * 255.0f))));
                uint _S723 = atlasWidth_6;
                uint _S724 = 4U % atlasWidth_6;
                int _S725 = int(_S724);
                uint _S726 = 4U / _S723;
                int3 _S727 = int3(_S725, int(_S726), int(0));
                uint _S728 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S727)).xy), uint(((_S727)).z))).x * 255.0f));
                uint _S729 = atlasWidth_6;
                uint _S730 = 5U % atlasWidth_6;
                int _S731 = int(_S730);
                uint _S732 = 5U / _S729;
                int3 _S733 = int3(_S731, int(_S732), int(0));
                uint _S734 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S733)).xy), uint(((_S733)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S728, _S734);
                int2 tile_12 = int2(floor(detailSource_0)) - minimumTile_6;
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
                    int3 _S735 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S735)).xy), uint(((_S735)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S728 + uint(tile_12.x));
                uint _S736 = atlasWidth_6;
                uint _S737 = offset_6 % atlasWidth_6;
                int _S738 = int(_S737);
                uint _S739 = offset_6 / _S736;
                int3 _S740 = int3(_S738, int(_S739), int(0));
                uint _S741 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S740)).xy), uint(((_S740)).z))).x * 255.0f));
                uint _S742 = atlasWidth_6;
                uint _S743 = offset_6 + 1U;
                uint _S744 = _S743 % atlasWidth_6;
                int _S745 = int(_S744);
                uint _S746 = _S743 / _S742;
                int3 _S747 = int3(_S745, int(_S746), int(0));
                uint slot_12 = _S741 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S747)).xy), uint(((_S747)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S748 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S748)).xy), uint(((_S748)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S749 = atlasWidth_6;
                uint _S750 = 6U % atlasWidth_6;
                int _S751 = int(_S750);
                uint _S752 = 6U / _S749;
                int3 _S753 = int3(_S751, int(_S752), int(0));
                uint _S754 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S753)).xy), uint(((_S753)).z))).x * 255.0f));
                uint _S755 = atlasWidth_6;
                uint _S756 = 7U % atlasWidth_6;
                int _S757 = int(_S756);
                uint _S758 = 7U / _S755;
                int3 _S759 = int3(_S757, int(_S758), int(0));
                uint columns_6 = _S754 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S759)).xy), uint(((_S759)).z))).x * 255.0f)) << 8U);
                uint _S760 = atlasWidth_6;
                uint _S761 = 8U % atlasWidth_6;
                int _S762 = int(_S761);
                uint _S763 = 8U / _S760;
                int3 _S764 = int3(_S762, int(_S763), int(0));
                uint _S765 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S764)).xy), uint(((_S764)).z))).x * 255.0f));
                uint _S766 = atlasWidth_6;
                uint _S767 = 9U % atlasWidth_6;
                int _S768 = int(_S767);
                uint _S769 = 9U / _S766;
                int3 _S770 = int3(_S768, int(_S769), int(0));
                uint rows_6 = ((_S765 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S770)).xy), uint(((_S770)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S728 * _S734 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S771 = atlasWidth_6 / columns_6;
                float _S772 = float(_S771);
                uint _S773 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S772, float(_S773));
                uint _S774 = slot_13 % columns_6;
                float _S775 = float(_S774);
                uint _S776 = slot_13 / columns_6;
                _S182 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S775, float(_S776)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(detailSource_0) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S777 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(detailSource_0)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S777);
            }
            if(hasSceneLighting_0)
            {
                int3 _S778 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S778)).xy), uint(((_S778)).z)));
                break;
            }
            uint _S779 = atlasWidth_6 / uint(_S777.x);
            float _S780 = float(_S779);
            uint _S781 = (atlasHeight_6 - 1U) / uint(_S777.y);
            float2 cellSize_13 = float2(_S780, float(_S781));
            _S182 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        for(;;)
        {
            float4 _S782 = float4(_S6->compositeControls_0) ;
            if((_S782.x) != 32.0f)
            {
                break;
            }
            bool _S783 = (_S782.w) >= 0.5f;
            for(;;)
            {
                if(!_S783)
                {
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S784 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S784).get_width(0)),(*((&atlasHeight_7)) = (_S784).get_height(0));
                int3 _S785 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S785)).xy), uint(((_S785)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S786 = atlasWidth_7;
                    uint _S787 = 2U % atlasWidth_7;
                    int _S788 = int(_S787);
                    uint _S789 = 2U / _S786;
                    int3 _S790 = int3(_S788, int(_S789), int(0));
                    int _S791 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S790)).xy), uint(((_S790)).z))).x * 255.0f)));
                    uint _S792 = atlasWidth_7;
                    uint _S793 = 3U % atlasWidth_7;
                    int _S794 = int(_S793);
                    uint _S795 = 3U / _S792;
                    int3 _S796 = int3(_S794, int(_S795), int(0));
                    int2 minimumTile_7 = int2(_S791, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S796)).xy), uint(((_S796)).z))).x * 255.0f))));
                    uint _S797 = atlasWidth_7;
                    uint _S798 = 4U % atlasWidth_7;
                    int _S799 = int(_S798);
                    uint _S800 = 4U / _S797;
                    int3 _S801 = int3(_S799, int(_S800), int(0));
                    uint _S802 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S801)).xy), uint(((_S801)).z))).x * 255.0f));
                    uint _S803 = atlasWidth_7;
                    uint _S804 = 5U % atlasWidth_7;
                    int _S805 = int(_S804);
                    uint _S806 = 5U / _S803;
                    int3 _S807 = int3(_S805, int(_S806), int(0));
                    uint _S808 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S807)).xy), uint(((_S807)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S802, _S808);
                    int2 tile_14 = int2(floor(detailSource_0)) - minimumTile_7;
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
                        int3 _S809 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S809)).xy), uint(((_S809)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S802 + uint(tile_14.x));
                    uint _S810 = atlasWidth_7;
                    uint _S811 = offset_7 % atlasWidth_7;
                    int _S812 = int(_S811);
                    uint _S813 = offset_7 / _S810;
                    int3 _S814 = int3(_S812, int(_S813), int(0));
                    uint _S815 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S814)).xy), uint(((_S814)).z))).x * 255.0f));
                    uint _S816 = atlasWidth_7;
                    uint _S817 = offset_7 + 1U;
                    uint _S818 = _S817 % atlasWidth_7;
                    int _S819 = int(_S818);
                    uint _S820 = _S817 / _S816;
                    int3 _S821 = int3(_S819, int(_S820), int(0));
                    uint slot_14 = _S815 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S821)).xy), uint(((_S821)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S822 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S822)).xy), uint(((_S822)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S823 = atlasWidth_7;
                    uint _S824 = 6U % atlasWidth_7;
                    int _S825 = int(_S824);
                    uint _S826 = 6U / _S823;
                    int3 _S827 = int3(_S825, int(_S826), int(0));
                    uint _S828 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S827)).xy), uint(((_S827)).z))).x * 255.0f));
                    uint _S829 = atlasWidth_7;
                    uint _S830 = 7U % atlasWidth_7;
                    int _S831 = int(_S830);
                    uint _S832 = 7U / _S829;
                    int3 _S833 = int3(_S831, int(_S832), int(0));
                    uint columns_7 = _S828 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S833)).xy), uint(((_S833)).z))).x * 255.0f)) << 8U);
                    uint _S834 = atlasWidth_7;
                    uint _S835 = 8U % atlasWidth_7;
                    int _S836 = int(_S835);
                    uint _S837 = 8U / _S834;
                    int3 _S838 = int3(_S836, int(_S837), int(0));
                    uint _S839 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S838)).xy), uint(((_S838)).z))).x * 255.0f));
                    uint _S840 = atlasWidth_7;
                    uint _S841 = 9U % atlasWidth_7;
                    int _S842 = int(_S841);
                    uint _S843 = 9U / _S840;
                    int3 _S844 = int3(_S842, int(_S843), int(0));
                    uint rows_7 = ((_S839 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S844)).xy), uint(((_S844)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S802 * _S808 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S845 = atlasWidth_7 / columns_7;
                    float _S846 = float(_S845);
                    uint _S847 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S846, float(_S847));
                    uint _S848 = slot_15 % columns_7;
                    float _S849 = float(_S848);
                    uint _S850 = slot_15 / columns_7;
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S849, float(_S850)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(detailSource_0) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S851 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(detailSource_0)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S851);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S852 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S852)).xy), uint(((_S852)).z)));
                    break;
                }
                uint _S853 = atlasWidth_7 / uint(_S851.x);
                float _S854 = float(_S853);
                uint _S855 = (atlasHeight_7 - 1U) / uint(_S851.y);
                float2 cellSize_15 = float2(_S854, float(_S855));
                _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_2 = uint(round(_S782.y));
            if(operation_2 == 1U)
            {
                _S182 = _S182 * _S561;
                break;
            }
            if(operation_2 == 2U)
            {
                _S182 = _S182 + _S561;
                break;
            }
            if(operation_2 == 3U)
            {
                _S182 = _S182 - _S561;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S782.z;
                _S182 = _S182 * float4((1.0f - factor_2))  + _S561 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S182.x);
    }
    else
    {
        metallic_0 = _S166;
    }
    float3 emissiveColor_1;
    if((textureMask_0 & 16U) != 0U)
    {
        if((secondaryMask_0 & 16U) != 0U)
        {
            detailSource_0 = _S181;
        }
        else
        {
            detailSource_0 = _S180;
        }
        thread float4 emissionSample_0;
        bool _S856 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S856)
            {
                _S182 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S857 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S857).get_width(0)),(*((&atlasHeight_8)) = (_S857).get_height(0));
            int3 _S858 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S858)).xy), uint(((_S858)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S859 = atlasWidth_8;
                uint _S860 = 2U % atlasWidth_8;
                int _S861 = int(_S860);
                uint _S862 = 2U / _S859;
                int3 _S863 = int3(_S861, int(_S862), int(0));
                int _S864 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S863)).xy), uint(((_S863)).z))).x * 255.0f)));
                uint _S865 = atlasWidth_8;
                uint _S866 = 3U % atlasWidth_8;
                int _S867 = int(_S866);
                uint _S868 = 3U / _S865;
                int3 _S869 = int3(_S867, int(_S868), int(0));
                int2 minimumTile_8 = int2(_S864, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S869)).xy), uint(((_S869)).z))).x * 255.0f))));
                uint _S870 = atlasWidth_8;
                uint _S871 = 4U % atlasWidth_8;
                int _S872 = int(_S871);
                uint _S873 = 4U / _S870;
                int3 _S874 = int3(_S872, int(_S873), int(0));
                uint _S875 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S874)).xy), uint(((_S874)).z))).x * 255.0f));
                uint _S876 = atlasWidth_8;
                uint _S877 = 5U % atlasWidth_8;
                int _S878 = int(_S877);
                uint _S879 = 5U / _S876;
                int3 _S880 = int3(_S878, int(_S879), int(0));
                uint _S881 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S880)).xy), uint(((_S880)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S875, _S881);
                int2 tile_16 = int2(floor(detailSource_0)) - minimumTile_8;
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
                    int3 _S882 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S882)).xy), uint(((_S882)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S875 + uint(tile_16.x));
                uint _S883 = atlasWidth_8;
                uint _S884 = offset_8 % atlasWidth_8;
                int _S885 = int(_S884);
                uint _S886 = offset_8 / _S883;
                int3 _S887 = int3(_S885, int(_S886), int(0));
                uint _S888 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S887)).xy), uint(((_S887)).z))).x * 255.0f));
                uint _S889 = atlasWidth_8;
                uint _S890 = offset_8 + 1U;
                uint _S891 = _S890 % atlasWidth_8;
                int _S892 = int(_S891);
                uint _S893 = _S890 / _S889;
                int3 _S894 = int3(_S892, int(_S893), int(0));
                uint slot_16 = _S888 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S894)).xy), uint(((_S894)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S895 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S895)).xy), uint(((_S895)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S896 = atlasWidth_8;
                uint _S897 = 6U % atlasWidth_8;
                int _S898 = int(_S897);
                uint _S899 = 6U / _S896;
                int3 _S900 = int3(_S898, int(_S899), int(0));
                uint _S901 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S900)).xy), uint(((_S900)).z))).x * 255.0f));
                uint _S902 = atlasWidth_8;
                uint _S903 = 7U % atlasWidth_8;
                int _S904 = int(_S903);
                uint _S905 = 7U / _S902;
                int3 _S906 = int3(_S904, int(_S905), int(0));
                uint columns_8 = _S901 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S906)).xy), uint(((_S906)).z))).x * 255.0f)) << 8U);
                uint _S907 = atlasWidth_8;
                uint _S908 = 8U % atlasWidth_8;
                int _S909 = int(_S908);
                uint _S910 = 8U / _S907;
                int3 _S911 = int3(_S909, int(_S910), int(0));
                uint _S912 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S911)).xy), uint(((_S911)).z))).x * 255.0f));
                uint _S913 = atlasWidth_8;
                uint _S914 = 9U % atlasWidth_8;
                int _S915 = int(_S914);
                uint _S916 = 9U / _S913;
                int3 _S917 = int3(_S915, int(_S916), int(0));
                uint rows_8 = ((_S912 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S917)).xy), uint(((_S917)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S875 * _S881 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S918 = atlasWidth_8 / columns_8;
                float _S919 = float(_S918);
                uint _S920 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S919, float(_S920));
                uint _S921 = slot_17 % columns_8;
                float _S922 = float(_S921);
                uint _S923 = slot_17 / columns_8;
                _S182 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S922, float(_S923)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(detailSource_0) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S924 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(detailSource_0)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S924);
            }
            if(hasSceneLighting_0)
            {
                int3 _S925 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S925)).xy), uint(((_S925)).z)));
                break;
            }
            uint _S926 = atlasWidth_8 / uint(_S924.x);
            float _S927 = float(_S926);
            uint _S928 = (atlasHeight_8 - 1U) / uint(_S924.y);
            float2 cellSize_17 = float2(_S927, float(_S928));
            _S182 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        emissionSample_0 = _S182;
        if((_S174.w) != 0.0f)
        {
            texture2d<float, access::sample> _S929 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S929).get_width(0)),(*((&sourceHeight_1)) = (_S929).get_height(0));
            texture2d<float, access::sample> _S930 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S930).get_width(0)),(*((&valueHeight_1)) = (_S930).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S931 = emissionSample_0;
                for(;;)
                {
                    float _S932 = _S931.w;
                    if(_S932 > 1.0f)
                    {
                        unlitColor_0 = _S931.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S933 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_3;
                    thread uint height_3;
                    (*((&width_3)) = (_S933).get_width(0)),(*((&height_3)) = (_S933).get_height(0));
                    uint3 _S934 = uint3(round(_S931.xyz * float3(255.0f) ));
                    uint index_4 = ((_S934.x) | ((_S934.y) << 8U)) | ((_S934.z) << 16U);
                    if(_S932 == 0.0f)
                    {
                        _S183 = _S934;
                    }
                    else
                    {
                        _S183 = uint3(index_4, index_4, index_4);
                    }
                    uint3 address_3 = _S183 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S935 = address_3.x;
                    uint _S936 = _S935 % width_3;
                    int _S937 = int(_S936);
                    uint _S938 = _S935 / width_3;
                    int3 _S939 = int3(_S937, int(_S938), int(0));
                    float _S940 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S939)).xy), uint(((_S939)).z))).x;
                    uint _S941 = address_3.y;
                    uint _S942 = _S941 % width_3;
                    int _S943 = int(_S942);
                    uint _S944 = _S941 / width_3;
                    int3 _S945 = int3(_S943, int(_S944), int(0));
                    float _S946 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S945)).xy), uint(((_S945)).z))).x;
                    uint _S947 = address_3.z;
                    uint _S948 = _S947 % width_3;
                    int _S949 = int(_S948);
                    uint _S950 = _S947 / width_3;
                    int3 _S951 = int3(_S949, int(_S950), int(0));
                    unlitColor_0 = float3(_S940, _S946, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S951)).xy), uint(((_S951)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                emissionSample_0.z = (((&kernelContext_0)->emissiveValuesTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (detailSource_0))).x;
            }
        }
        float4 _S952 = emissionSample_0;
        for(;;)
        {
            float4 _S953 = float4(_S6->compositeControls_0) ;
            if((_S953.x) != 16.0f)
            {
                _S182 = _S952;
                break;
            }
            bool _S954 = (_S953.w) >= 0.5f;
            for(;;)
            {
                if(!_S954)
                {
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S955 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S955).get_width(0)),(*((&atlasHeight_9)) = (_S955).get_height(0));
                int3 _S956 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S956)).xy), uint(((_S956)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S957 = atlasWidth_9;
                    uint _S958 = 2U % atlasWidth_9;
                    int _S959 = int(_S958);
                    uint _S960 = 2U / _S957;
                    int3 _S961 = int3(_S959, int(_S960), int(0));
                    int _S962 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S961)).xy), uint(((_S961)).z))).x * 255.0f)));
                    uint _S963 = atlasWidth_9;
                    uint _S964 = 3U % atlasWidth_9;
                    int _S965 = int(_S964);
                    uint _S966 = 3U / _S963;
                    int3 _S967 = int3(_S965, int(_S966), int(0));
                    int2 minimumTile_9 = int2(_S962, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S967)).xy), uint(((_S967)).z))).x * 255.0f))));
                    uint _S968 = atlasWidth_9;
                    uint _S969 = 4U % atlasWidth_9;
                    int _S970 = int(_S969);
                    uint _S971 = 4U / _S968;
                    int3 _S972 = int3(_S970, int(_S971), int(0));
                    uint _S973 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S972)).xy), uint(((_S972)).z))).x * 255.0f));
                    uint _S974 = atlasWidth_9;
                    uint _S975 = 5U % atlasWidth_9;
                    int _S976 = int(_S975);
                    uint _S977 = 5U / _S974;
                    int3 _S978 = int3(_S976, int(_S977), int(0));
                    uint _S979 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S978)).xy), uint(((_S978)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S973, _S979);
                    int2 tile_18 = int2(floor(detailSource_0)) - minimumTile_9;
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
                        int3 _S980 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S980)).xy), uint(((_S980)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S973 + uint(tile_18.x));
                    uint _S981 = atlasWidth_9;
                    uint _S982 = offset_9 % atlasWidth_9;
                    int _S983 = int(_S982);
                    uint _S984 = offset_9 / _S981;
                    int3 _S985 = int3(_S983, int(_S984), int(0));
                    uint _S986 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S985)).xy), uint(((_S985)).z))).x * 255.0f));
                    uint _S987 = atlasWidth_9;
                    uint _S988 = offset_9 + 1U;
                    uint _S989 = _S988 % atlasWidth_9;
                    int _S990 = int(_S989);
                    uint _S991 = _S988 / _S987;
                    int3 _S992 = int3(_S990, int(_S991), int(0));
                    uint slot_18 = _S986 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S992)).xy), uint(((_S992)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S993 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S993)).xy), uint(((_S993)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S994 = atlasWidth_9;
                    uint _S995 = 6U % atlasWidth_9;
                    int _S996 = int(_S995);
                    uint _S997 = 6U / _S994;
                    int3 _S998 = int3(_S996, int(_S997), int(0));
                    uint _S999 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S998)).xy), uint(((_S998)).z))).x * 255.0f));
                    uint _S1000 = atlasWidth_9;
                    uint _S1001 = 7U % atlasWidth_9;
                    int _S1002 = int(_S1001);
                    uint _S1003 = 7U / _S1000;
                    int3 _S1004 = int3(_S1002, int(_S1003), int(0));
                    uint columns_9 = _S999 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1004)).xy), uint(((_S1004)).z))).x * 255.0f)) << 8U);
                    uint _S1005 = atlasWidth_9;
                    uint _S1006 = 8U % atlasWidth_9;
                    int _S1007 = int(_S1006);
                    uint _S1008 = 8U / _S1005;
                    int3 _S1009 = int3(_S1007, int(_S1008), int(0));
                    uint _S1010 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1009)).xy), uint(((_S1009)).z))).x * 255.0f));
                    uint _S1011 = atlasWidth_9;
                    uint _S1012 = 9U % atlasWidth_9;
                    int _S1013 = int(_S1012);
                    uint _S1014 = 9U / _S1011;
                    int3 _S1015 = int3(_S1013, int(_S1014), int(0));
                    uint rows_9 = ((_S1010 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1015)).xy), uint(((_S1015)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S973 * _S979 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S1016 = atlasWidth_9 / columns_9;
                    float _S1017 = float(_S1016);
                    uint _S1018 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S1017, float(_S1018));
                    uint _S1019 = slot_19 % columns_9;
                    float _S1020 = float(_S1019);
                    uint _S1021 = slot_19 / columns_9;
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1020, float(_S1021)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(detailSource_0) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S1022 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(detailSource_0)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S1022);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1023 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1023)).xy), uint(((_S1023)).z)));
                    break;
                }
                uint _S1024 = atlasWidth_9 / uint(_S1022.x);
                float _S1025 = float(_S1024);
                uint _S1026 = (atlasHeight_9 - 1U) / uint(_S1022.y);
                float2 cellSize_19 = float2(_S1025, float(_S1026));
                _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_3 = uint(round(_S953.y));
            if(operation_3 == 1U)
            {
                _S182 = _S952 * _S182;
                break;
            }
            if(operation_3 == 2U)
            {
                _S182 = _S952 + _S182;
                break;
            }
            if(operation_3 == 3U)
            {
                _S182 = _S952 - _S182;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S953.z;
                _S182 = _S952 * float4((1.0f - factor_3))  + _S182 * float4(factor_3) ;
                break;
            }
            _S182 = _S952;
            break;
        }
        emissiveColor_1 = _S182.xyz;
    }
    else
    {
        emissiveColor_1 = emissiveColor_0;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        if((secondaryMask_0 & 64U) != 0U)
        {
            detailSource_0 = _S181;
        }
        else
        {
            detailSource_0 = _S180;
        }
        bool _S1027 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S1027)
            {
                _S182 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S1028 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S1028).get_width(0)),(*((&atlasHeight_10)) = (_S1028).get_height(0));
            int3 _S1029 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1029)).xy), uint(((_S1029)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S1030 = atlasWidth_10;
                uint _S1031 = 2U % atlasWidth_10;
                int _S1032 = int(_S1031);
                uint _S1033 = 2U / _S1030;
                int3 _S1034 = int3(_S1032, int(_S1033), int(0));
                int _S1035 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1034)).xy), uint(((_S1034)).z))).x * 255.0f)));
                uint _S1036 = atlasWidth_10;
                uint _S1037 = 3U % atlasWidth_10;
                int _S1038 = int(_S1037);
                uint _S1039 = 3U / _S1036;
                int3 _S1040 = int3(_S1038, int(_S1039), int(0));
                int2 minimumTile_10 = int2(_S1035, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1040)).xy), uint(((_S1040)).z))).x * 255.0f))));
                uint _S1041 = atlasWidth_10;
                uint _S1042 = 4U % atlasWidth_10;
                int _S1043 = int(_S1042);
                uint _S1044 = 4U / _S1041;
                int3 _S1045 = int3(_S1043, int(_S1044), int(0));
                uint _S1046 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1045)).xy), uint(((_S1045)).z))).x * 255.0f));
                uint _S1047 = atlasWidth_10;
                uint _S1048 = 5U % atlasWidth_10;
                int _S1049 = int(_S1048);
                uint _S1050 = 5U / _S1047;
                int3 _S1051 = int3(_S1049, int(_S1050), int(0));
                uint _S1052 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1051)).xy), uint(((_S1051)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S1046, _S1052);
                int2 tile_20 = int2(floor(detailSource_0)) - minimumTile_10;
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
                    int3 _S1053 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1053)).xy), uint(((_S1053)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1046 + uint(tile_20.x));
                uint _S1054 = atlasWidth_10;
                uint _S1055 = offset_10 % atlasWidth_10;
                int _S1056 = int(_S1055);
                uint _S1057 = offset_10 / _S1054;
                int3 _S1058 = int3(_S1056, int(_S1057), int(0));
                uint _S1059 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1058)).xy), uint(((_S1058)).z))).x * 255.0f));
                uint _S1060 = atlasWidth_10;
                uint _S1061 = offset_10 + 1U;
                uint _S1062 = _S1061 % atlasWidth_10;
                int _S1063 = int(_S1062);
                uint _S1064 = _S1061 / _S1060;
                int3 _S1065 = int3(_S1063, int(_S1064), int(0));
                uint slot_20 = _S1059 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1065)).xy), uint(((_S1065)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1066 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1066)).xy), uint(((_S1066)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1067 = atlasWidth_10;
                uint _S1068 = 6U % atlasWidth_10;
                int _S1069 = int(_S1068);
                uint _S1070 = 6U / _S1067;
                int3 _S1071 = int3(_S1069, int(_S1070), int(0));
                uint _S1072 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1071)).xy), uint(((_S1071)).z))).x * 255.0f));
                uint _S1073 = atlasWidth_10;
                uint _S1074 = 7U % atlasWidth_10;
                int _S1075 = int(_S1074);
                uint _S1076 = 7U / _S1073;
                int3 _S1077 = int3(_S1075, int(_S1076), int(0));
                uint columns_10 = _S1072 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1077)).xy), uint(((_S1077)).z))).x * 255.0f)) << 8U);
                uint _S1078 = atlasWidth_10;
                uint _S1079 = 8U % atlasWidth_10;
                int _S1080 = int(_S1079);
                uint _S1081 = 8U / _S1078;
                int3 _S1082 = int3(_S1080, int(_S1081), int(0));
                uint _S1083 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1082)).xy), uint(((_S1082)).z))).x * 255.0f));
                uint _S1084 = atlasWidth_10;
                uint _S1085 = 9U % atlasWidth_10;
                int _S1086 = int(_S1085);
                uint _S1087 = 9U / _S1084;
                int3 _S1088 = int3(_S1086, int(_S1087), int(0));
                uint rows_10 = ((_S1083 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1088)).xy), uint(((_S1088)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S1046 * _S1052 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1089 = atlasWidth_10 / columns_10;
                float _S1090 = float(_S1089);
                uint _S1091 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1090, float(_S1091));
                uint _S1092 = slot_21 % columns_10;
                float _S1093 = float(_S1092);
                uint _S1094 = slot_21 / columns_10;
                _S182 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S1093, float(_S1094)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(detailSource_0) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1095 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(detailSource_0)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1095);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1096 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1096)).xy), uint(((_S1096)).z)));
                break;
            }
            uint _S1097 = atlasWidth_10 / uint(_S1095.x);
            float _S1098 = float(_S1097);
            uint _S1099 = (atlasHeight_10 - 1U) / uint(_S1095.y);
            float2 cellSize_21 = float2(_S1098, float(_S1099));
            _S182 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1100 = float4(_S6->compositeControls_0) ;
            if((_S1100.x) != 64.0f)
            {
                break;
            }
            bool _S1101 = (_S1100.w) >= 0.5f;
            for(;;)
            {
                if(!_S1101)
                {
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1102 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1102).get_width(0)),(*((&atlasHeight_11)) = (_S1102).get_height(0));
                int3 _S1103 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1103)).xy), uint(((_S1103)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1104 = atlasWidth_11;
                    uint _S1105 = 2U % atlasWidth_11;
                    int _S1106 = int(_S1105);
                    uint _S1107 = 2U / _S1104;
                    int3 _S1108 = int3(_S1106, int(_S1107), int(0));
                    int _S1109 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1108)).xy), uint(((_S1108)).z))).x * 255.0f)));
                    uint _S1110 = atlasWidth_11;
                    uint _S1111 = 3U % atlasWidth_11;
                    int _S1112 = int(_S1111);
                    uint _S1113 = 3U / _S1110;
                    int3 _S1114 = int3(_S1112, int(_S1113), int(0));
                    int2 minimumTile_11 = int2(_S1109, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1114)).xy), uint(((_S1114)).z))).x * 255.0f))));
                    uint _S1115 = atlasWidth_11;
                    uint _S1116 = 4U % atlasWidth_11;
                    int _S1117 = int(_S1116);
                    uint _S1118 = 4U / _S1115;
                    int3 _S1119 = int3(_S1117, int(_S1118), int(0));
                    uint _S1120 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1119)).xy), uint(((_S1119)).z))).x * 255.0f));
                    uint _S1121 = atlasWidth_11;
                    uint _S1122 = 5U % atlasWidth_11;
                    int _S1123 = int(_S1122);
                    uint _S1124 = 5U / _S1121;
                    int3 _S1125 = int3(_S1123, int(_S1124), int(0));
                    uint _S1126 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1125)).xy), uint(((_S1125)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1120, _S1126);
                    int2 tile_22 = int2(floor(detailSource_0)) - minimumTile_11;
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
                        int3 _S1127 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1127)).xy), uint(((_S1127)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1120 + uint(tile_22.x));
                    uint _S1128 = atlasWidth_11;
                    uint _S1129 = offset_11 % atlasWidth_11;
                    int _S1130 = int(_S1129);
                    uint _S1131 = offset_11 / _S1128;
                    int3 _S1132 = int3(_S1130, int(_S1131), int(0));
                    uint _S1133 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1132)).xy), uint(((_S1132)).z))).x * 255.0f));
                    uint _S1134 = atlasWidth_11;
                    uint _S1135 = offset_11 + 1U;
                    uint _S1136 = _S1135 % atlasWidth_11;
                    int _S1137 = int(_S1136);
                    uint _S1138 = _S1135 / _S1134;
                    int3 _S1139 = int3(_S1137, int(_S1138), int(0));
                    uint slot_22 = _S1133 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1139)).xy), uint(((_S1139)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1140 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1140)).xy), uint(((_S1140)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1141 = atlasWidth_11;
                    uint _S1142 = 6U % atlasWidth_11;
                    int _S1143 = int(_S1142);
                    uint _S1144 = 6U / _S1141;
                    int3 _S1145 = int3(_S1143, int(_S1144), int(0));
                    uint _S1146 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1145)).xy), uint(((_S1145)).z))).x * 255.0f));
                    uint _S1147 = atlasWidth_11;
                    uint _S1148 = 7U % atlasWidth_11;
                    int _S1149 = int(_S1148);
                    uint _S1150 = 7U / _S1147;
                    int3 _S1151 = int3(_S1149, int(_S1150), int(0));
                    uint columns_11 = _S1146 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1151)).xy), uint(((_S1151)).z))).x * 255.0f)) << 8U);
                    uint _S1152 = atlasWidth_11;
                    uint _S1153 = 8U % atlasWidth_11;
                    int _S1154 = int(_S1153);
                    uint _S1155 = 8U / _S1152;
                    int3 _S1156 = int3(_S1154, int(_S1155), int(0));
                    uint _S1157 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1156)).xy), uint(((_S1156)).z))).x * 255.0f));
                    uint _S1158 = atlasWidth_11;
                    uint _S1159 = 9U % atlasWidth_11;
                    int _S1160 = int(_S1159);
                    uint _S1161 = 9U / _S1158;
                    int3 _S1162 = int3(_S1160, int(_S1161), int(0));
                    uint rows_11 = ((_S1157 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1162)).xy), uint(((_S1162)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1120 * _S1126 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1163 = atlasWidth_11 / columns_11;
                    float _S1164 = float(_S1163);
                    uint _S1165 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1164, float(_S1165));
                    uint _S1166 = slot_23 % columns_11;
                    float _S1167 = float(_S1166);
                    uint _S1168 = slot_23 / columns_11;
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1167, float(_S1168)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(detailSource_0) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1169 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(detailSource_0)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1169);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1170 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1170)).xy), uint(((_S1170)).z)));
                    break;
                }
                uint _S1171 = atlasWidth_11 / uint(_S1169.x);
                float _S1172 = float(_S1171);
                uint _S1173 = (atlasHeight_11 - 1U) / uint(_S1169.y);
                float2 cellSize_23 = float2(_S1172, float(_S1173));
                _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_4 = uint(round(_S1100.y));
            if(operation_4 == 1U)
            {
                _S182 = _S182 * _S561;
                break;
            }
            if(operation_4 == 2U)
            {
                _S182 = _S182 + _S561;
                break;
            }
            if(operation_4 == 3U)
            {
                _S182 = _S182 - _S561;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S1100.z;
                _S182 = _S182 * float4((1.0f - factor_4))  + _S561 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S182.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        if((secondaryMask_0 & 128U) != 0U)
        {
            detailSource_0 = _S181;
        }
        else
        {
            detailSource_0 = _S180;
        }
        bool _S1174 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S1174)
            {
                _S182 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S1175 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1175).get_width(0)),(*((&atlasHeight_12)) = (_S1175).get_height(0));
            int3 _S1176 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1176)).xy), uint(((_S1176)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1177 = atlasWidth_12;
                uint _S1178 = 2U % atlasWidth_12;
                int _S1179 = int(_S1178);
                uint _S1180 = 2U / _S1177;
                int3 _S1181 = int3(_S1179, int(_S1180), int(0));
                int _S1182 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1181)).xy), uint(((_S1181)).z))).x * 255.0f)));
                uint _S1183 = atlasWidth_12;
                uint _S1184 = 3U % atlasWidth_12;
                int _S1185 = int(_S1184);
                uint _S1186 = 3U / _S1183;
                int3 _S1187 = int3(_S1185, int(_S1186), int(0));
                int2 minimumTile_12 = int2(_S1182, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1187)).xy), uint(((_S1187)).z))).x * 255.0f))));
                uint _S1188 = atlasWidth_12;
                uint _S1189 = 4U % atlasWidth_12;
                int _S1190 = int(_S1189);
                uint _S1191 = 4U / _S1188;
                int3 _S1192 = int3(_S1190, int(_S1191), int(0));
                uint _S1193 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1192)).xy), uint(((_S1192)).z))).x * 255.0f));
                uint _S1194 = atlasWidth_12;
                uint _S1195 = 5U % atlasWidth_12;
                int _S1196 = int(_S1195);
                uint _S1197 = 5U / _S1194;
                int3 _S1198 = int3(_S1196, int(_S1197), int(0));
                uint _S1199 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1198)).xy), uint(((_S1198)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1193, _S1199);
                int2 tile_24 = int2(floor(detailSource_0)) - minimumTile_12;
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
                    int3 _S1200 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1200)).xy), uint(((_S1200)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1193 + uint(tile_24.x));
                uint _S1201 = atlasWidth_12;
                uint _S1202 = offset_12 % atlasWidth_12;
                int _S1203 = int(_S1202);
                uint _S1204 = offset_12 / _S1201;
                int3 _S1205 = int3(_S1203, int(_S1204), int(0));
                uint _S1206 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1205)).xy), uint(((_S1205)).z))).x * 255.0f));
                uint _S1207 = atlasWidth_12;
                uint _S1208 = offset_12 + 1U;
                uint _S1209 = _S1208 % atlasWidth_12;
                int _S1210 = int(_S1209);
                uint _S1211 = _S1208 / _S1207;
                int3 _S1212 = int3(_S1210, int(_S1211), int(0));
                uint slot_24 = _S1206 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1212)).xy), uint(((_S1212)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1213 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1213)).xy), uint(((_S1213)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1214 = atlasWidth_12;
                uint _S1215 = 6U % atlasWidth_12;
                int _S1216 = int(_S1215);
                uint _S1217 = 6U / _S1214;
                int3 _S1218 = int3(_S1216, int(_S1217), int(0));
                uint _S1219 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1218)).xy), uint(((_S1218)).z))).x * 255.0f));
                uint _S1220 = atlasWidth_12;
                uint _S1221 = 7U % atlasWidth_12;
                int _S1222 = int(_S1221);
                uint _S1223 = 7U / _S1220;
                int3 _S1224 = int3(_S1222, int(_S1223), int(0));
                uint columns_12 = _S1219 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1224)).xy), uint(((_S1224)).z))).x * 255.0f)) << 8U);
                uint _S1225 = atlasWidth_12;
                uint _S1226 = 8U % atlasWidth_12;
                int _S1227 = int(_S1226);
                uint _S1228 = 8U / _S1225;
                int3 _S1229 = int3(_S1227, int(_S1228), int(0));
                uint _S1230 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1229)).xy), uint(((_S1229)).z))).x * 255.0f));
                uint _S1231 = atlasWidth_12;
                uint _S1232 = 9U % atlasWidth_12;
                int _S1233 = int(_S1232);
                uint _S1234 = 9U / _S1231;
                int3 _S1235 = int3(_S1233, int(_S1234), int(0));
                uint rows_12 = ((_S1230 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1235)).xy), uint(((_S1235)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1193 * _S1199 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1236 = atlasWidth_12 / columns_12;
                float _S1237 = float(_S1236);
                uint _S1238 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1237, float(_S1238));
                uint _S1239 = slot_25 % columns_12;
                float _S1240 = float(_S1239);
                uint _S1241 = slot_25 / columns_12;
                _S182 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1240, float(_S1241)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(detailSource_0) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1242 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(detailSource_0)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1242);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1243 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1243)).xy), uint(((_S1243)).z)));
                break;
            }
            uint _S1244 = atlasWidth_12 / uint(_S1242.x);
            float _S1245 = float(_S1244);
            uint _S1246 = (atlasHeight_12 - 1U) / uint(_S1242.y);
            float2 cellSize_25 = float2(_S1245, float(_S1246));
            _S182 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1247 = float4(_S6->compositeControls_0) ;
            if((_S1247.x) != 128.0f)
            {
                break;
            }
            bool _S1248 = (_S1247.w) >= 0.5f;
            for(;;)
            {
                if(!_S1248)
                {
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1249 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1249).get_width(0)),(*((&atlasHeight_13)) = (_S1249).get_height(0));
                int3 _S1250 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1250)).xy), uint(((_S1250)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1251 = atlasWidth_13;
                    uint _S1252 = 2U % atlasWidth_13;
                    int _S1253 = int(_S1252);
                    uint _S1254 = 2U / _S1251;
                    int3 _S1255 = int3(_S1253, int(_S1254), int(0));
                    int _S1256 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1255)).xy), uint(((_S1255)).z))).x * 255.0f)));
                    uint _S1257 = atlasWidth_13;
                    uint _S1258 = 3U % atlasWidth_13;
                    int _S1259 = int(_S1258);
                    uint _S1260 = 3U / _S1257;
                    int3 _S1261 = int3(_S1259, int(_S1260), int(0));
                    int2 minimumTile_13 = int2(_S1256, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1261)).xy), uint(((_S1261)).z))).x * 255.0f))));
                    uint _S1262 = atlasWidth_13;
                    uint _S1263 = 4U % atlasWidth_13;
                    int _S1264 = int(_S1263);
                    uint _S1265 = 4U / _S1262;
                    int3 _S1266 = int3(_S1264, int(_S1265), int(0));
                    uint _S1267 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1266)).xy), uint(((_S1266)).z))).x * 255.0f));
                    uint _S1268 = atlasWidth_13;
                    uint _S1269 = 5U % atlasWidth_13;
                    int _S1270 = int(_S1269);
                    uint _S1271 = 5U / _S1268;
                    int3 _S1272 = int3(_S1270, int(_S1271), int(0));
                    uint _S1273 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1272)).xy), uint(((_S1272)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1267, _S1273);
                    int2 tile_26 = int2(floor(detailSource_0)) - minimumTile_13;
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
                        int3 _S1274 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1274)).xy), uint(((_S1274)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1267 + uint(tile_26.x));
                    uint _S1275 = atlasWidth_13;
                    uint _S1276 = offset_13 % atlasWidth_13;
                    int _S1277 = int(_S1276);
                    uint _S1278 = offset_13 / _S1275;
                    int3 _S1279 = int3(_S1277, int(_S1278), int(0));
                    uint _S1280 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1279)).xy), uint(((_S1279)).z))).x * 255.0f));
                    uint _S1281 = atlasWidth_13;
                    uint _S1282 = offset_13 + 1U;
                    uint _S1283 = _S1282 % atlasWidth_13;
                    int _S1284 = int(_S1283);
                    uint _S1285 = _S1282 / _S1281;
                    int3 _S1286 = int3(_S1284, int(_S1285), int(0));
                    uint slot_26 = _S1280 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1286)).xy), uint(((_S1286)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1287 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1287)).xy), uint(((_S1287)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1288 = atlasWidth_13;
                    uint _S1289 = 6U % atlasWidth_13;
                    int _S1290 = int(_S1289);
                    uint _S1291 = 6U / _S1288;
                    int3 _S1292 = int3(_S1290, int(_S1291), int(0));
                    uint _S1293 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1292)).xy), uint(((_S1292)).z))).x * 255.0f));
                    uint _S1294 = atlasWidth_13;
                    uint _S1295 = 7U % atlasWidth_13;
                    int _S1296 = int(_S1295);
                    uint _S1297 = 7U / _S1294;
                    int3 _S1298 = int3(_S1296, int(_S1297), int(0));
                    uint columns_13 = _S1293 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1298)).xy), uint(((_S1298)).z))).x * 255.0f)) << 8U);
                    uint _S1299 = atlasWidth_13;
                    uint _S1300 = 8U % atlasWidth_13;
                    int _S1301 = int(_S1300);
                    uint _S1302 = 8U / _S1299;
                    int3 _S1303 = int3(_S1301, int(_S1302), int(0));
                    uint _S1304 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1303)).xy), uint(((_S1303)).z))).x * 255.0f));
                    uint _S1305 = atlasWidth_13;
                    uint _S1306 = 9U % atlasWidth_13;
                    int _S1307 = int(_S1306);
                    uint _S1308 = 9U / _S1305;
                    int3 _S1309 = int3(_S1307, int(_S1308), int(0));
                    uint rows_13 = ((_S1304 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1309)).xy), uint(((_S1309)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1267 * _S1273 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1310 = atlasWidth_13 / columns_13;
                    float _S1311 = float(_S1310);
                    uint _S1312 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1311, float(_S1312));
                    uint _S1313 = slot_27 % columns_13;
                    float _S1314 = float(_S1313);
                    uint _S1315 = slot_27 / columns_13;
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1314, float(_S1315)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(detailSource_0) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1316 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(detailSource_0)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1316);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1317 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1317)).xy), uint(((_S1317)).z)));
                    break;
                }
                uint _S1318 = atlasWidth_13 / uint(_S1316.x);
                float _S1319 = float(_S1318);
                uint _S1320 = (atlasHeight_13 - 1U) / uint(_S1316.y);
                float2 cellSize_27 = float2(_S1319, float(_S1320));
                _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_5 = uint(round(_S1247.y));
            if(operation_5 == 1U)
            {
                _S182 = _S182 * _S561;
                break;
            }
            if(operation_5 == 2U)
            {
                _S182 = _S182 + _S561;
                break;
            }
            if(operation_5 == 3U)
            {
                _S182 = _S182 - _S561;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1247.z;
                _S182 = _S182 * float4((1.0f - factor_5))  + _S561 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S182.x);
    }
    else
    {
        occlusion_0 = _S161;
    }
    float3 specularColor_0;
    if((textureMask_0 & 256U) != 0U)
    {
        if((secondaryMask_0 & 256U) != 0U)
        {
            detailSource_0 = _S181;
        }
        else
        {
            detailSource_0 = _S180;
        }
        bool _S1321 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S1321)
            {
                _S182 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S1322 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1322).get_width(0)),(*((&atlasHeight_14)) = (_S1322).get_height(0));
            int3 _S1323 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1323)).xy), uint(((_S1323)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1324 = atlasWidth_14;
                uint _S1325 = 2U % atlasWidth_14;
                int _S1326 = int(_S1325);
                uint _S1327 = 2U / _S1324;
                int3 _S1328 = int3(_S1326, int(_S1327), int(0));
                int _S1329 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1328)).xy), uint(((_S1328)).z))).x * 255.0f)));
                uint _S1330 = atlasWidth_14;
                uint _S1331 = 3U % atlasWidth_14;
                int _S1332 = int(_S1331);
                uint _S1333 = 3U / _S1330;
                int3 _S1334 = int3(_S1332, int(_S1333), int(0));
                int2 minimumTile_14 = int2(_S1329, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1334)).xy), uint(((_S1334)).z))).x * 255.0f))));
                uint _S1335 = atlasWidth_14;
                uint _S1336 = 4U % atlasWidth_14;
                int _S1337 = int(_S1336);
                uint _S1338 = 4U / _S1335;
                int3 _S1339 = int3(_S1337, int(_S1338), int(0));
                uint _S1340 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1339)).xy), uint(((_S1339)).z))).x * 255.0f));
                uint _S1341 = atlasWidth_14;
                uint _S1342 = 5U % atlasWidth_14;
                int _S1343 = int(_S1342);
                uint _S1344 = 5U / _S1341;
                int3 _S1345 = int3(_S1343, int(_S1344), int(0));
                uint _S1346 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1345)).xy), uint(((_S1345)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1340, _S1346);
                int2 tile_28 = int2(floor(detailSource_0)) - minimumTile_14;
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
                    int3 _S1347 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1347)).xy), uint(((_S1347)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1340 + uint(tile_28.x));
                uint _S1348 = atlasWidth_14;
                uint _S1349 = offset_14 % atlasWidth_14;
                int _S1350 = int(_S1349);
                uint _S1351 = offset_14 / _S1348;
                int3 _S1352 = int3(_S1350, int(_S1351), int(0));
                uint _S1353 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1352)).xy), uint(((_S1352)).z))).x * 255.0f));
                uint _S1354 = atlasWidth_14;
                uint _S1355 = offset_14 + 1U;
                uint _S1356 = _S1355 % atlasWidth_14;
                int _S1357 = int(_S1356);
                uint _S1358 = _S1355 / _S1354;
                int3 _S1359 = int3(_S1357, int(_S1358), int(0));
                uint slot_28 = _S1353 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1359)).xy), uint(((_S1359)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1360 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1360)).xy), uint(((_S1360)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1361 = atlasWidth_14;
                uint _S1362 = 6U % atlasWidth_14;
                int _S1363 = int(_S1362);
                uint _S1364 = 6U / _S1361;
                int3 _S1365 = int3(_S1363, int(_S1364), int(0));
                uint _S1366 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1365)).xy), uint(((_S1365)).z))).x * 255.0f));
                uint _S1367 = atlasWidth_14;
                uint _S1368 = 7U % atlasWidth_14;
                int _S1369 = int(_S1368);
                uint _S1370 = 7U / _S1367;
                int3 _S1371 = int3(_S1369, int(_S1370), int(0));
                uint columns_14 = _S1366 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1371)).xy), uint(((_S1371)).z))).x * 255.0f)) << 8U);
                uint _S1372 = atlasWidth_14;
                uint _S1373 = 8U % atlasWidth_14;
                int _S1374 = int(_S1373);
                uint _S1375 = 8U / _S1372;
                int3 _S1376 = int3(_S1374, int(_S1375), int(0));
                uint _S1377 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1376)).xy), uint(((_S1376)).z))).x * 255.0f));
                uint _S1378 = atlasWidth_14;
                uint _S1379 = 9U % atlasWidth_14;
                int _S1380 = int(_S1379);
                uint _S1381 = 9U / _S1378;
                int3 _S1382 = int3(_S1380, int(_S1381), int(0));
                uint rows_14 = ((_S1377 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1382)).xy), uint(((_S1382)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1340 * _S1346 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1383 = atlasWidth_14 / columns_14;
                float _S1384 = float(_S1383);
                uint _S1385 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1384, float(_S1385));
                uint _S1386 = slot_29 % columns_14;
                float _S1387 = float(_S1386);
                uint _S1388 = slot_29 / columns_14;
                _S182 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1387, float(_S1388)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(detailSource_0) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1389 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(detailSource_0)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1389);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1390 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1390)).xy), uint(((_S1390)).z)));
                break;
            }
            uint _S1391 = atlasWidth_14 / uint(_S1389.x);
            float _S1392 = float(_S1391);
            uint _S1393 = (atlasHeight_14 - 1U) / uint(_S1389.y);
            float2 cellSize_29 = float2(_S1392, float(_S1393));
            _S182 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1394 = float4(_S6->compositeControls_0) ;
            if((_S1394.x) != 256.0f)
            {
                break;
            }
            bool _S1395 = (_S1394.w) >= 0.5f;
            for(;;)
            {
                if(!_S1395)
                {
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1396 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1396).get_width(0)),(*((&atlasHeight_15)) = (_S1396).get_height(0));
                int3 _S1397 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1397)).xy), uint(((_S1397)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1398 = atlasWidth_15;
                    uint _S1399 = 2U % atlasWidth_15;
                    int _S1400 = int(_S1399);
                    uint _S1401 = 2U / _S1398;
                    int3 _S1402 = int3(_S1400, int(_S1401), int(0));
                    int _S1403 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1402)).xy), uint(((_S1402)).z))).x * 255.0f)));
                    uint _S1404 = atlasWidth_15;
                    uint _S1405 = 3U % atlasWidth_15;
                    int _S1406 = int(_S1405);
                    uint _S1407 = 3U / _S1404;
                    int3 _S1408 = int3(_S1406, int(_S1407), int(0));
                    int2 minimumTile_15 = int2(_S1403, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1408)).xy), uint(((_S1408)).z))).x * 255.0f))));
                    uint _S1409 = atlasWidth_15;
                    uint _S1410 = 4U % atlasWidth_15;
                    int _S1411 = int(_S1410);
                    uint _S1412 = 4U / _S1409;
                    int3 _S1413 = int3(_S1411, int(_S1412), int(0));
                    uint _S1414 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1413)).xy), uint(((_S1413)).z))).x * 255.0f));
                    uint _S1415 = atlasWidth_15;
                    uint _S1416 = 5U % atlasWidth_15;
                    int _S1417 = int(_S1416);
                    uint _S1418 = 5U / _S1415;
                    int3 _S1419 = int3(_S1417, int(_S1418), int(0));
                    uint _S1420 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1419)).xy), uint(((_S1419)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1414, _S1420);
                    int2 tile_30 = int2(floor(detailSource_0)) - minimumTile_15;
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
                        int3 _S1421 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1421)).xy), uint(((_S1421)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1414 + uint(tile_30.x));
                    uint _S1422 = atlasWidth_15;
                    uint _S1423 = offset_15 % atlasWidth_15;
                    int _S1424 = int(_S1423);
                    uint _S1425 = offset_15 / _S1422;
                    int3 _S1426 = int3(_S1424, int(_S1425), int(0));
                    uint _S1427 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1426)).xy), uint(((_S1426)).z))).x * 255.0f));
                    uint _S1428 = atlasWidth_15;
                    uint _S1429 = offset_15 + 1U;
                    uint _S1430 = _S1429 % atlasWidth_15;
                    int _S1431 = int(_S1430);
                    uint _S1432 = _S1429 / _S1428;
                    int3 _S1433 = int3(_S1431, int(_S1432), int(0));
                    uint slot_30 = _S1427 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1433)).xy), uint(((_S1433)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1434 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1434)).xy), uint(((_S1434)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1435 = atlasWidth_15;
                    uint _S1436 = 6U % atlasWidth_15;
                    int _S1437 = int(_S1436);
                    uint _S1438 = 6U / _S1435;
                    int3 _S1439 = int3(_S1437, int(_S1438), int(0));
                    uint _S1440 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1439)).xy), uint(((_S1439)).z))).x * 255.0f));
                    uint _S1441 = atlasWidth_15;
                    uint _S1442 = 7U % atlasWidth_15;
                    int _S1443 = int(_S1442);
                    uint _S1444 = 7U / _S1441;
                    int3 _S1445 = int3(_S1443, int(_S1444), int(0));
                    uint columns_15 = _S1440 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1445)).xy), uint(((_S1445)).z))).x * 255.0f)) << 8U);
                    uint _S1446 = atlasWidth_15;
                    uint _S1447 = 8U % atlasWidth_15;
                    int _S1448 = int(_S1447);
                    uint _S1449 = 8U / _S1446;
                    int3 _S1450 = int3(_S1448, int(_S1449), int(0));
                    uint _S1451 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1450)).xy), uint(((_S1450)).z))).x * 255.0f));
                    uint _S1452 = atlasWidth_15;
                    uint _S1453 = 9U % atlasWidth_15;
                    int _S1454 = int(_S1453);
                    uint _S1455 = 9U / _S1452;
                    int3 _S1456 = int3(_S1454, int(_S1455), int(0));
                    uint rows_15 = ((_S1451 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1456)).xy), uint(((_S1456)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1414 * _S1420 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1457 = atlasWidth_15 / columns_15;
                    float _S1458 = float(_S1457);
                    uint _S1459 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1458, float(_S1459));
                    uint _S1460 = slot_31 % columns_15;
                    float _S1461 = float(_S1460);
                    uint _S1462 = slot_31 / columns_15;
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1461, float(_S1462)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(detailSource_0) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1463 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(detailSource_0)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1463);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1464 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1464)).xy), uint(((_S1464)).z)));
                    break;
                }
                uint _S1465 = atlasWidth_15 / uint(_S1463.x);
                float _S1466 = float(_S1465);
                uint _S1467 = (atlasHeight_15 - 1U) / uint(_S1463.y);
                float2 cellSize_31 = float2(_S1466, float(_S1467));
                _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_6 = uint(round(_S1394.y));
            if(operation_6 == 1U)
            {
                _S182 = _S182 * _S561;
                break;
            }
            if(operation_6 == 2U)
            {
                _S182 = _S182 + _S561;
                break;
            }
            if(operation_6 == 3U)
            {
                _S182 = _S182 - _S561;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1394.z;
                _S182 = _S182 * float4((1.0f - factor_6))  + _S561 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S182.xyz);
    }
    else
    {
        specularColor_0 = _S169;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 512U) != 0U)
    {
        if((secondaryMask_0 & 512U) != 0U)
        {
            detailSource_0 = _S181;
        }
        else
        {
            detailSource_0 = _S180;
        }
        bool _S1468 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S1468)
            {
                _S182 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S1469 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1469).get_width(0)),(*((&atlasHeight_16)) = (_S1469).get_height(0));
            int3 _S1470 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1470)).xy), uint(((_S1470)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1471 = atlasWidth_16;
                uint _S1472 = 2U % atlasWidth_16;
                int _S1473 = int(_S1472);
                uint _S1474 = 2U / _S1471;
                int3 _S1475 = int3(_S1473, int(_S1474), int(0));
                int _S1476 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1475)).xy), uint(((_S1475)).z))).x * 255.0f)));
                uint _S1477 = atlasWidth_16;
                uint _S1478 = 3U % atlasWidth_16;
                int _S1479 = int(_S1478);
                uint _S1480 = 3U / _S1477;
                int3 _S1481 = int3(_S1479, int(_S1480), int(0));
                int2 minimumTile_16 = int2(_S1476, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1481)).xy), uint(((_S1481)).z))).x * 255.0f))));
                uint _S1482 = atlasWidth_16;
                uint _S1483 = 4U % atlasWidth_16;
                int _S1484 = int(_S1483);
                uint _S1485 = 4U / _S1482;
                int3 _S1486 = int3(_S1484, int(_S1485), int(0));
                uint _S1487 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1486)).xy), uint(((_S1486)).z))).x * 255.0f));
                uint _S1488 = atlasWidth_16;
                uint _S1489 = 5U % atlasWidth_16;
                int _S1490 = int(_S1489);
                uint _S1491 = 5U / _S1488;
                int3 _S1492 = int3(_S1490, int(_S1491), int(0));
                uint _S1493 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1492)).xy), uint(((_S1492)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1487, _S1493);
                int2 tile_32 = int2(floor(detailSource_0)) - minimumTile_16;
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
                    int3 _S1494 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1494)).xy), uint(((_S1494)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1487 + uint(tile_32.x));
                uint _S1495 = atlasWidth_16;
                uint _S1496 = offset_16 % atlasWidth_16;
                int _S1497 = int(_S1496);
                uint _S1498 = offset_16 / _S1495;
                int3 _S1499 = int3(_S1497, int(_S1498), int(0));
                uint _S1500 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1499)).xy), uint(((_S1499)).z))).x * 255.0f));
                uint _S1501 = atlasWidth_16;
                uint _S1502 = offset_16 + 1U;
                uint _S1503 = _S1502 % atlasWidth_16;
                int _S1504 = int(_S1503);
                uint _S1505 = _S1502 / _S1501;
                int3 _S1506 = int3(_S1504, int(_S1505), int(0));
                uint slot_32 = _S1500 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1506)).xy), uint(((_S1506)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1507 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1507)).xy), uint(((_S1507)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1508 = atlasWidth_16;
                uint _S1509 = 6U % atlasWidth_16;
                int _S1510 = int(_S1509);
                uint _S1511 = 6U / _S1508;
                int3 _S1512 = int3(_S1510, int(_S1511), int(0));
                uint _S1513 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1512)).xy), uint(((_S1512)).z))).x * 255.0f));
                uint _S1514 = atlasWidth_16;
                uint _S1515 = 7U % atlasWidth_16;
                int _S1516 = int(_S1515);
                uint _S1517 = 7U / _S1514;
                int3 _S1518 = int3(_S1516, int(_S1517), int(0));
                uint columns_16 = _S1513 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1518)).xy), uint(((_S1518)).z))).x * 255.0f)) << 8U);
                uint _S1519 = atlasWidth_16;
                uint _S1520 = 8U % atlasWidth_16;
                int _S1521 = int(_S1520);
                uint _S1522 = 8U / _S1519;
                int3 _S1523 = int3(_S1521, int(_S1522), int(0));
                uint _S1524 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1523)).xy), uint(((_S1523)).z))).x * 255.0f));
                uint _S1525 = atlasWidth_16;
                uint _S1526 = 9U % atlasWidth_16;
                int _S1527 = int(_S1526);
                uint _S1528 = 9U / _S1525;
                int3 _S1529 = int3(_S1527, int(_S1528), int(0));
                uint rows_16 = ((_S1524 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1529)).xy), uint(((_S1529)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1487 * _S1493 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1530 = atlasWidth_16 / columns_16;
                float _S1531 = float(_S1530);
                uint _S1532 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1531, float(_S1532));
                uint _S1533 = slot_33 % columns_16;
                float _S1534 = float(_S1533);
                uint _S1535 = slot_33 / columns_16;
                _S182 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1534, float(_S1535)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(detailSource_0) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1536 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(detailSource_0)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1536);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1537 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1537)).xy), uint(((_S1537)).z)));
                break;
            }
            uint _S1538 = atlasWidth_16 / uint(_S1536.x);
            float _S1539 = float(_S1538);
            uint _S1540 = (atlasHeight_16 - 1U) / uint(_S1536.y);
            float2 cellSize_33 = float2(_S1539, float(_S1540));
            _S182 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1541 = float4(_S6->compositeControls_0) ;
            if((_S1541.x) != 512.0f)
            {
                break;
            }
            bool _S1542 = (_S1541.w) >= 0.5f;
            for(;;)
            {
                if(!_S1542)
                {
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1543 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1543).get_width(0)),(*((&atlasHeight_17)) = (_S1543).get_height(0));
                int3 _S1544 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1544)).xy), uint(((_S1544)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1545 = atlasWidth_17;
                    uint _S1546 = 2U % atlasWidth_17;
                    int _S1547 = int(_S1546);
                    uint _S1548 = 2U / _S1545;
                    int3 _S1549 = int3(_S1547, int(_S1548), int(0));
                    int _S1550 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1549)).xy), uint(((_S1549)).z))).x * 255.0f)));
                    uint _S1551 = atlasWidth_17;
                    uint _S1552 = 3U % atlasWidth_17;
                    int _S1553 = int(_S1552);
                    uint _S1554 = 3U / _S1551;
                    int3 _S1555 = int3(_S1553, int(_S1554), int(0));
                    int2 minimumTile_17 = int2(_S1550, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1555)).xy), uint(((_S1555)).z))).x * 255.0f))));
                    uint _S1556 = atlasWidth_17;
                    uint _S1557 = 4U % atlasWidth_17;
                    int _S1558 = int(_S1557);
                    uint _S1559 = 4U / _S1556;
                    int3 _S1560 = int3(_S1558, int(_S1559), int(0));
                    uint _S1561 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1560)).xy), uint(((_S1560)).z))).x * 255.0f));
                    uint _S1562 = atlasWidth_17;
                    uint _S1563 = 5U % atlasWidth_17;
                    int _S1564 = int(_S1563);
                    uint _S1565 = 5U / _S1562;
                    int3 _S1566 = int3(_S1564, int(_S1565), int(0));
                    uint _S1567 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1566)).xy), uint(((_S1566)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1561, _S1567);
                    int2 tile_34 = int2(floor(detailSource_0)) - minimumTile_17;
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
                        int3 _S1568 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1568)).xy), uint(((_S1568)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1561 + uint(tile_34.x));
                    uint _S1569 = atlasWidth_17;
                    uint _S1570 = offset_17 % atlasWidth_17;
                    int _S1571 = int(_S1570);
                    uint _S1572 = offset_17 / _S1569;
                    int3 _S1573 = int3(_S1571, int(_S1572), int(0));
                    uint _S1574 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1573)).xy), uint(((_S1573)).z))).x * 255.0f));
                    uint _S1575 = atlasWidth_17;
                    uint _S1576 = offset_17 + 1U;
                    uint _S1577 = _S1576 % atlasWidth_17;
                    int _S1578 = int(_S1577);
                    uint _S1579 = _S1576 / _S1575;
                    int3 _S1580 = int3(_S1578, int(_S1579), int(0));
                    uint slot_34 = _S1574 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1580)).xy), uint(((_S1580)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1581 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1581)).xy), uint(((_S1581)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1582 = atlasWidth_17;
                    uint _S1583 = 6U % atlasWidth_17;
                    int _S1584 = int(_S1583);
                    uint _S1585 = 6U / _S1582;
                    int3 _S1586 = int3(_S1584, int(_S1585), int(0));
                    uint _S1587 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1586)).xy), uint(((_S1586)).z))).x * 255.0f));
                    uint _S1588 = atlasWidth_17;
                    uint _S1589 = 7U % atlasWidth_17;
                    int _S1590 = int(_S1589);
                    uint _S1591 = 7U / _S1588;
                    int3 _S1592 = int3(_S1590, int(_S1591), int(0));
                    uint columns_17 = _S1587 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1592)).xy), uint(((_S1592)).z))).x * 255.0f)) << 8U);
                    uint _S1593 = atlasWidth_17;
                    uint _S1594 = 8U % atlasWidth_17;
                    int _S1595 = int(_S1594);
                    uint _S1596 = 8U / _S1593;
                    int3 _S1597 = int3(_S1595, int(_S1596), int(0));
                    uint _S1598 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1597)).xy), uint(((_S1597)).z))).x * 255.0f));
                    uint _S1599 = atlasWidth_17;
                    uint _S1600 = 9U % atlasWidth_17;
                    int _S1601 = int(_S1600);
                    uint _S1602 = 9U / _S1599;
                    int3 _S1603 = int3(_S1601, int(_S1602), int(0));
                    uint rows_17 = ((_S1598 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1603)).xy), uint(((_S1603)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1561 * _S1567 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1604 = atlasWidth_17 / columns_17;
                    float _S1605 = float(_S1604);
                    uint _S1606 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1605, float(_S1606));
                    uint _S1607 = slot_35 % columns_17;
                    float _S1608 = float(_S1607);
                    uint _S1609 = slot_35 / columns_17;
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1608, float(_S1609)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(detailSource_0) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1610 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(detailSource_0)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1610);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1611 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1611)).xy), uint(((_S1611)).z)));
                    break;
                }
                uint _S1612 = atlasWidth_17 / uint(_S1610.x);
                float _S1613 = float(_S1612);
                uint _S1614 = (atlasHeight_17 - 1U) / uint(_S1610.y);
                float2 cellSize_35 = float2(_S1613, float(_S1614));
                _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_7 = uint(round(_S1541.y));
            if(operation_7 == 1U)
            {
                _S182 = _S182 * _S561;
                break;
            }
            if(operation_7 == 2U)
            {
                _S182 = _S182 + _S561;
                break;
            }
            if(operation_7 == 3U)
            {
                _S182 = _S182 - _S561;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1541.z;
                _S182 = _S182 * float4((1.0f - factor_7))  + _S561 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S182.x);
    }
    else
    {
        clearcoatAmount_0 = _S171;
    }
    float clearcoatRoughness_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        if((secondaryMask_0 & 1024U) != 0U)
        {
            detailSource_0 = _S181;
        }
        else
        {
            detailSource_0 = _S180;
        }
        bool _S1615 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S1615)
            {
                _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S1616 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1616).get_width(0)),(*((&atlasHeight_18)) = (_S1616).get_height(0));
            int3 _S1617 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1617)).xy), uint(((_S1617)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1618 = atlasWidth_18;
                uint _S1619 = 2U % atlasWidth_18;
                int _S1620 = int(_S1619);
                uint _S1621 = 2U / _S1618;
                int3 _S1622 = int3(_S1620, int(_S1621), int(0));
                int _S1623 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1622)).xy), uint(((_S1622)).z))).x * 255.0f)));
                uint _S1624 = atlasWidth_18;
                uint _S1625 = 3U % atlasWidth_18;
                int _S1626 = int(_S1625);
                uint _S1627 = 3U / _S1624;
                int3 _S1628 = int3(_S1626, int(_S1627), int(0));
                int2 minimumTile_18 = int2(_S1623, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1628)).xy), uint(((_S1628)).z))).x * 255.0f))));
                uint _S1629 = atlasWidth_18;
                uint _S1630 = 4U % atlasWidth_18;
                int _S1631 = int(_S1630);
                uint _S1632 = 4U / _S1629;
                int3 _S1633 = int3(_S1631, int(_S1632), int(0));
                uint _S1634 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1633)).xy), uint(((_S1633)).z))).x * 255.0f));
                uint _S1635 = atlasWidth_18;
                uint _S1636 = 5U % atlasWidth_18;
                int _S1637 = int(_S1636);
                uint _S1638 = 5U / _S1635;
                int3 _S1639 = int3(_S1637, int(_S1638), int(0));
                uint _S1640 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1639)).xy), uint(((_S1639)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1634, _S1640);
                int2 tile_36 = int2(floor(detailSource_0)) - minimumTile_18;
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
                    int3 _S1641 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1641)).xy), uint(((_S1641)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1634 + uint(tile_36.x));
                uint _S1642 = atlasWidth_18;
                uint _S1643 = offset_18 % atlasWidth_18;
                int _S1644 = int(_S1643);
                uint _S1645 = offset_18 / _S1642;
                int3 _S1646 = int3(_S1644, int(_S1645), int(0));
                uint _S1647 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1646)).xy), uint(((_S1646)).z))).x * 255.0f));
                uint _S1648 = atlasWidth_18;
                uint _S1649 = offset_18 + 1U;
                uint _S1650 = _S1649 % atlasWidth_18;
                int _S1651 = int(_S1650);
                uint _S1652 = _S1649 / _S1648;
                int3 _S1653 = int3(_S1651, int(_S1652), int(0));
                uint slot_36 = _S1647 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1653)).xy), uint(((_S1653)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1654 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1654)).xy), uint(((_S1654)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1655 = atlasWidth_18;
                uint _S1656 = 6U % atlasWidth_18;
                int _S1657 = int(_S1656);
                uint _S1658 = 6U / _S1655;
                int3 _S1659 = int3(_S1657, int(_S1658), int(0));
                uint _S1660 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1659)).xy), uint(((_S1659)).z))).x * 255.0f));
                uint _S1661 = atlasWidth_18;
                uint _S1662 = 7U % atlasWidth_18;
                int _S1663 = int(_S1662);
                uint _S1664 = 7U / _S1661;
                int3 _S1665 = int3(_S1663, int(_S1664), int(0));
                uint columns_18 = _S1660 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1665)).xy), uint(((_S1665)).z))).x * 255.0f)) << 8U);
                uint _S1666 = atlasWidth_18;
                uint _S1667 = 8U % atlasWidth_18;
                int _S1668 = int(_S1667);
                uint _S1669 = 8U / _S1666;
                int3 _S1670 = int3(_S1668, int(_S1669), int(0));
                uint _S1671 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1670)).xy), uint(((_S1670)).z))).x * 255.0f));
                uint _S1672 = atlasWidth_18;
                uint _S1673 = 9U % atlasWidth_18;
                int _S1674 = int(_S1673);
                uint _S1675 = 9U / _S1672;
                int3 _S1676 = int3(_S1674, int(_S1675), int(0));
                uint rows_18 = ((_S1671 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1676)).xy), uint(((_S1676)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1634 * _S1640 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1677 = atlasWidth_18 / columns_18;
                float _S1678 = float(_S1677);
                uint _S1679 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1678, float(_S1679));
                uint _S1680 = slot_37 % columns_18;
                float _S1681 = float(_S1680);
                uint _S1682 = slot_37 / columns_18;
                _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1681, float(_S1682)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(detailSource_0) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1683 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(detailSource_0)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1683);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1684 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1684)).xy), uint(((_S1684)).z)));
                break;
            }
            uint _S1685 = atlasWidth_18 / uint(_S1683.x);
            float _S1686 = float(_S1685);
            uint _S1687 = (atlasHeight_18 - 1U) / uint(_S1683.y);
            float2 cellSize_37 = float2(_S1686, float(_S1687));
            _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1688 = float4(_S6->compositeControls_0) ;
            if((_S1688.x) != 1024.0f)
            {
                break;
            }
            bool _S1689 = (_S1688.w) >= 0.5f;
            for(;;)
            {
                if(!_S1689)
                {
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1690 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1690).get_width(0)),(*((&atlasHeight_19)) = (_S1690).get_height(0));
                int3 _S1691 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1691)).xy), uint(((_S1691)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1692 = atlasWidth_19;
                    uint _S1693 = 2U % atlasWidth_19;
                    int _S1694 = int(_S1693);
                    uint _S1695 = 2U / _S1692;
                    int3 _S1696 = int3(_S1694, int(_S1695), int(0));
                    int _S1697 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1696)).xy), uint(((_S1696)).z))).x * 255.0f)));
                    uint _S1698 = atlasWidth_19;
                    uint _S1699 = 3U % atlasWidth_19;
                    int _S1700 = int(_S1699);
                    uint _S1701 = 3U / _S1698;
                    int3 _S1702 = int3(_S1700, int(_S1701), int(0));
                    int2 minimumTile_19 = int2(_S1697, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1702)).xy), uint(((_S1702)).z))).x * 255.0f))));
                    uint _S1703 = atlasWidth_19;
                    uint _S1704 = 4U % atlasWidth_19;
                    int _S1705 = int(_S1704);
                    uint _S1706 = 4U / _S1703;
                    int3 _S1707 = int3(_S1705, int(_S1706), int(0));
                    uint _S1708 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1707)).xy), uint(((_S1707)).z))).x * 255.0f));
                    uint _S1709 = atlasWidth_19;
                    uint _S1710 = 5U % atlasWidth_19;
                    int _S1711 = int(_S1710);
                    uint _S1712 = 5U / _S1709;
                    int3 _S1713 = int3(_S1711, int(_S1712), int(0));
                    uint _S1714 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1713)).xy), uint(((_S1713)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1708, _S1714);
                    int2 tile_38 = int2(floor(detailSource_0)) - minimumTile_19;
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
                        int3 _S1715 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1715)).xy), uint(((_S1715)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1708 + uint(tile_38.x));
                    uint _S1716 = atlasWidth_19;
                    uint _S1717 = offset_19 % atlasWidth_19;
                    int _S1718 = int(_S1717);
                    uint _S1719 = offset_19 / _S1716;
                    int3 _S1720 = int3(_S1718, int(_S1719), int(0));
                    uint _S1721 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1720)).xy), uint(((_S1720)).z))).x * 255.0f));
                    uint _S1722 = atlasWidth_19;
                    uint _S1723 = offset_19 + 1U;
                    uint _S1724 = _S1723 % atlasWidth_19;
                    int _S1725 = int(_S1724);
                    uint _S1726 = _S1723 / _S1722;
                    int3 _S1727 = int3(_S1725, int(_S1726), int(0));
                    uint slot_38 = _S1721 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1727)).xy), uint(((_S1727)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1728 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1728)).xy), uint(((_S1728)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1729 = atlasWidth_19;
                    uint _S1730 = 6U % atlasWidth_19;
                    int _S1731 = int(_S1730);
                    uint _S1732 = 6U / _S1729;
                    int3 _S1733 = int3(_S1731, int(_S1732), int(0));
                    uint _S1734 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1733)).xy), uint(((_S1733)).z))).x * 255.0f));
                    uint _S1735 = atlasWidth_19;
                    uint _S1736 = 7U % atlasWidth_19;
                    int _S1737 = int(_S1736);
                    uint _S1738 = 7U / _S1735;
                    int3 _S1739 = int3(_S1737, int(_S1738), int(0));
                    uint columns_19 = _S1734 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1739)).xy), uint(((_S1739)).z))).x * 255.0f)) << 8U);
                    uint _S1740 = atlasWidth_19;
                    uint _S1741 = 8U % atlasWidth_19;
                    int _S1742 = int(_S1741);
                    uint _S1743 = 8U / _S1740;
                    int3 _S1744 = int3(_S1742, int(_S1743), int(0));
                    uint _S1745 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1744)).xy), uint(((_S1744)).z))).x * 255.0f));
                    uint _S1746 = atlasWidth_19;
                    uint _S1747 = 9U % atlasWidth_19;
                    int _S1748 = int(_S1747);
                    uint _S1749 = 9U / _S1746;
                    int3 _S1750 = int3(_S1748, int(_S1749), int(0));
                    uint rows_19 = ((_S1745 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1750)).xy), uint(((_S1750)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1708 * _S1714 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1751 = atlasWidth_19 / columns_19;
                    float _S1752 = float(_S1751);
                    uint _S1753 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1752, float(_S1753));
                    uint _S1754 = slot_39 % columns_19;
                    float _S1755 = float(_S1754);
                    uint _S1756 = slot_39 / columns_19;
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1755, float(_S1756)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(detailSource_0) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1757 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(detailSource_0)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1757);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1758 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1758)).xy), uint(((_S1758)).z)));
                    break;
                }
                uint _S1759 = atlasWidth_19 / uint(_S1757.x);
                float _S1760 = float(_S1759);
                uint _S1761 = (atlasHeight_19 - 1U) / uint(_S1757.y);
                float2 cellSize_39 = float2(_S1760, float(_S1761));
                _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_8 = uint(round(_S1688.y));
            if(operation_8 == 1U)
            {
                _S182 = _S182 * _S561;
                break;
            }
            if(operation_8 == 2U)
            {
                _S182 = _S182 + _S561;
                break;
            }
            if(operation_8 == 3U)
            {
                _S182 = _S182 - _S561;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1688.z;
                _S182 = _S182 * float4((1.0f - factor_8))  + _S561 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S182.x);
    }
    else
    {
        clearcoatRoughness_0 = _S172;
    }
    float ior_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        if((secondaryMask_0 & 2048U) != 0U)
        {
            detailSource_0 = _S181;
        }
        else
        {
            detailSource_0 = _S180;
        }
        bool _S1762 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S1762)
            {
                _S182 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S1763 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_20;
            thread uint atlasHeight_20;
            (*((&atlasWidth_20)) = (_S1763).get_width(0)),(*((&atlasHeight_20)) = (_S1763).get_height(0));
            int3 _S1764 = int3(int(0), int(0), int(0));
            float4 metadata_20 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1764)).xy), uint(((_S1764)).z))) * float4(255.0f) );
            if((metadata_20.x) == 255.0f)
            {
                uint _S1765 = atlasWidth_20;
                uint _S1766 = 2U % atlasWidth_20;
                int _S1767 = int(_S1766);
                uint _S1768 = 2U / _S1765;
                int3 _S1769 = int3(_S1767, int(_S1768), int(0));
                int _S1770 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1769)).xy), uint(((_S1769)).z))).x * 255.0f)));
                uint _S1771 = atlasWidth_20;
                uint _S1772 = 3U % atlasWidth_20;
                int _S1773 = int(_S1772);
                uint _S1774 = 3U / _S1771;
                int3 _S1775 = int3(_S1773, int(_S1774), int(0));
                int2 minimumTile_20 = int2(_S1770, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1775)).xy), uint(((_S1775)).z))).x * 255.0f))));
                uint _S1776 = atlasWidth_20;
                uint _S1777 = 4U % atlasWidth_20;
                int _S1778 = int(_S1777);
                uint _S1779 = 4U / _S1776;
                int3 _S1780 = int3(_S1778, int(_S1779), int(0));
                uint _S1781 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1780)).xy), uint(((_S1780)).z))).x * 255.0f));
                uint _S1782 = atlasWidth_20;
                uint _S1783 = 5U % atlasWidth_20;
                int _S1784 = int(_S1783);
                uint _S1785 = 5U / _S1782;
                int3 _S1786 = int3(_S1784, int(_S1785), int(0));
                uint _S1787 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1786)).xy), uint(((_S1786)).z))).x * 255.0f));
                uint2 grid_20 = uint2(_S1781, _S1787);
                int2 tile_40 = int2(floor(detailSource_0)) - minimumTile_20;
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
                    int3 _S1788 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1788)).xy), uint(((_S1788)).z)));
                    break;
                }
                uint offset_20 = 10U + 2U * (uint(tile_40.y) * _S1781 + uint(tile_40.x));
                uint _S1789 = atlasWidth_20;
                uint _S1790 = offset_20 % atlasWidth_20;
                int _S1791 = int(_S1790);
                uint _S1792 = offset_20 / _S1789;
                int3 _S1793 = int3(_S1791, int(_S1792), int(0));
                uint _S1794 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1793)).xy), uint(((_S1793)).z))).x * 255.0f));
                uint _S1795 = atlasWidth_20;
                uint _S1796 = offset_20 + 1U;
                uint _S1797 = _S1796 % atlasWidth_20;
                int _S1798 = int(_S1797);
                uint _S1799 = _S1796 / _S1795;
                int3 _S1800 = int3(_S1798, int(_S1799), int(0));
                uint slot_40 = _S1794 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1800)).xy), uint(((_S1800)).z))).x * 255.0f)) << 8U);
                if(slot_40 == 0U)
                {
                    int3 _S1801 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1801)).xy), uint(((_S1801)).z)));
                    break;
                }
                uint slot_41 = slot_40 - 1U;
                uint _S1802 = atlasWidth_20;
                uint _S1803 = 6U % atlasWidth_20;
                int _S1804 = int(_S1803);
                uint _S1805 = 6U / _S1802;
                int3 _S1806 = int3(_S1804, int(_S1805), int(0));
                uint _S1807 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1806)).xy), uint(((_S1806)).z))).x * 255.0f));
                uint _S1808 = atlasWidth_20;
                uint _S1809 = 7U % atlasWidth_20;
                int _S1810 = int(_S1809);
                uint _S1811 = 7U / _S1808;
                int3 _S1812 = int3(_S1810, int(_S1811), int(0));
                uint columns_20 = _S1807 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1812)).xy), uint(((_S1812)).z))).x * 255.0f)) << 8U);
                uint _S1813 = atlasWidth_20;
                uint _S1814 = 8U % atlasWidth_20;
                int _S1815 = int(_S1814);
                uint _S1816 = 8U / _S1813;
                int3 _S1817 = int3(_S1815, int(_S1816), int(0));
                uint _S1818 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1817)).xy), uint(((_S1817)).z))).x * 255.0f));
                uint _S1819 = atlasWidth_20;
                uint _S1820 = 9U % atlasWidth_20;
                int _S1821 = int(_S1820);
                uint _S1822 = 9U / _S1819;
                int3 _S1823 = int3(_S1821, int(_S1822), int(0));
                uint rows_20 = ((_S1818 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1823)).xy), uint(((_S1823)).z))).x * 255.0f)) << 8U)) + columns_20 - 1U) / columns_20;
                uint metadataRows_20 = (10U + 2U * _S1781 * _S1787 + atlasWidth_20 - 1U) / atlasWidth_20;
                uint _S1824 = atlasWidth_20 / columns_20;
                float _S1825 = float(_S1824);
                uint _S1826 = (atlasHeight_20 - metadataRows_20) / rows_20;
                float2 cellSize_40 = float2(_S1825, float(_S1826));
                uint _S1827 = slot_41 % columns_20;
                float _S1828 = float(_S1827);
                uint _S1829 = slot_41 / columns_20;
                _S182 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1828, float(_S1829)) * cellSize_40 + float2(1.5f, float(metadataRows_20) + 1.5f) + fract(detailSource_0) * max(cellSize_40 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
                break;
            }
            int2 _S1830 = int2(metadata_20.zw);
            int2 tile_41 = int2(floor(detailSource_0)) - int2(metadata_20.xy);
            if(any(tile_41 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_41 >= _S1830);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1831 = int3(int(min(1U, atlasWidth_20 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1831)).xy), uint(((_S1831)).z)));
                break;
            }
            uint _S1832 = atlasWidth_20 / uint(_S1830.x);
            float _S1833 = float(_S1832);
            uint _S1834 = (atlasHeight_20 - 1U) / uint(_S1830.y);
            float2 cellSize_41 = float2(_S1833, float(_S1834));
            _S182 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_41) * cellSize_41 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_41 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
            break;
        }
        for(;;)
        {
            float4 _S1835 = float4(_S6->compositeControls_0) ;
            if((_S1835.x) != 2048.0f)
            {
                break;
            }
            bool _S1836 = (_S1835.w) >= 0.5f;
            for(;;)
            {
                if(!_S1836)
                {
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1837 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_21;
                thread uint atlasHeight_21;
                (*((&atlasWidth_21)) = (_S1837).get_width(0)),(*((&atlasHeight_21)) = (_S1837).get_height(0));
                int3 _S1838 = int3(int(0), int(0), int(0));
                float4 metadata_21 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1838)).xy), uint(((_S1838)).z))) * float4(255.0f) );
                if((metadata_21.x) == 255.0f)
                {
                    uint _S1839 = atlasWidth_21;
                    uint _S1840 = 2U % atlasWidth_21;
                    int _S1841 = int(_S1840);
                    uint _S1842 = 2U / _S1839;
                    int3 _S1843 = int3(_S1841, int(_S1842), int(0));
                    int _S1844 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1843)).xy), uint(((_S1843)).z))).x * 255.0f)));
                    uint _S1845 = atlasWidth_21;
                    uint _S1846 = 3U % atlasWidth_21;
                    int _S1847 = int(_S1846);
                    uint _S1848 = 3U / _S1845;
                    int3 _S1849 = int3(_S1847, int(_S1848), int(0));
                    int2 minimumTile_21 = int2(_S1844, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1849)).xy), uint(((_S1849)).z))).x * 255.0f))));
                    uint _S1850 = atlasWidth_21;
                    uint _S1851 = 4U % atlasWidth_21;
                    int _S1852 = int(_S1851);
                    uint _S1853 = 4U / _S1850;
                    int3 _S1854 = int3(_S1852, int(_S1853), int(0));
                    uint _S1855 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1854)).xy), uint(((_S1854)).z))).x * 255.0f));
                    uint _S1856 = atlasWidth_21;
                    uint _S1857 = 5U % atlasWidth_21;
                    int _S1858 = int(_S1857);
                    uint _S1859 = 5U / _S1856;
                    int3 _S1860 = int3(_S1858, int(_S1859), int(0));
                    uint _S1861 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1860)).xy), uint(((_S1860)).z))).x * 255.0f));
                    uint2 grid_21 = uint2(_S1855, _S1861);
                    int2 tile_42 = int2(floor(detailSource_0)) - minimumTile_21;
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
                        int3 _S1862 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1862)).xy), uint(((_S1862)).z)));
                        break;
                    }
                    uint offset_21 = 10U + 2U * (uint(tile_42.y) * _S1855 + uint(tile_42.x));
                    uint _S1863 = atlasWidth_21;
                    uint _S1864 = offset_21 % atlasWidth_21;
                    int _S1865 = int(_S1864);
                    uint _S1866 = offset_21 / _S1863;
                    int3 _S1867 = int3(_S1865, int(_S1866), int(0));
                    uint _S1868 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1867)).xy), uint(((_S1867)).z))).x * 255.0f));
                    uint _S1869 = atlasWidth_21;
                    uint _S1870 = offset_21 + 1U;
                    uint _S1871 = _S1870 % atlasWidth_21;
                    int _S1872 = int(_S1871);
                    uint _S1873 = _S1870 / _S1869;
                    int3 _S1874 = int3(_S1872, int(_S1873), int(0));
                    uint slot_42 = _S1868 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1874)).xy), uint(((_S1874)).z))).x * 255.0f)) << 8U);
                    if(slot_42 == 0U)
                    {
                        int3 _S1875 = int3(int(1), int(0), int(0));
                        _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1875)).xy), uint(((_S1875)).z)));
                        break;
                    }
                    uint slot_43 = slot_42 - 1U;
                    uint _S1876 = atlasWidth_21;
                    uint _S1877 = 6U % atlasWidth_21;
                    int _S1878 = int(_S1877);
                    uint _S1879 = 6U / _S1876;
                    int3 _S1880 = int3(_S1878, int(_S1879), int(0));
                    uint _S1881 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1880)).xy), uint(((_S1880)).z))).x * 255.0f));
                    uint _S1882 = atlasWidth_21;
                    uint _S1883 = 7U % atlasWidth_21;
                    int _S1884 = int(_S1883);
                    uint _S1885 = 7U / _S1882;
                    int3 _S1886 = int3(_S1884, int(_S1885), int(0));
                    uint columns_21 = _S1881 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1886)).xy), uint(((_S1886)).z))).x * 255.0f)) << 8U);
                    uint _S1887 = atlasWidth_21;
                    uint _S1888 = 8U % atlasWidth_21;
                    int _S1889 = int(_S1888);
                    uint _S1890 = 8U / _S1887;
                    int3 _S1891 = int3(_S1889, int(_S1890), int(0));
                    uint _S1892 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1891)).xy), uint(((_S1891)).z))).x * 255.0f));
                    uint _S1893 = atlasWidth_21;
                    uint _S1894 = 9U % atlasWidth_21;
                    int _S1895 = int(_S1894);
                    uint _S1896 = 9U / _S1893;
                    int3 _S1897 = int3(_S1895, int(_S1896), int(0));
                    uint rows_21 = ((_S1892 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1897)).xy), uint(((_S1897)).z))).x * 255.0f)) << 8U)) + columns_21 - 1U) / columns_21;
                    uint metadataRows_21 = (10U + 2U * _S1855 * _S1861 + atlasWidth_21 - 1U) / atlasWidth_21;
                    uint _S1898 = atlasWidth_21 / columns_21;
                    float _S1899 = float(_S1898);
                    uint _S1900 = (atlasHeight_21 - metadataRows_21) / rows_21;
                    float2 cellSize_42 = float2(_S1899, float(_S1900));
                    uint _S1901 = slot_43 % columns_21;
                    float _S1902 = float(_S1901);
                    uint _S1903 = slot_43 / columns_21;
                    _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1902, float(_S1903)) * cellSize_42 + float2(1.5f, float(metadataRows_21) + 1.5f) + fract(detailSource_0) * max(cellSize_42 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                    break;
                }
                int2 _S1904 = int2(metadata_21.zw);
                int2 tile_43 = int2(floor(detailSource_0)) - int2(metadata_21.xy);
                if(any(tile_43 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_43 >= _S1904);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1905 = int3(int(min(1U, atlasWidth_21 - 1U)), int(0), int(0));
                    _S561 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1905)).xy), uint(((_S1905)).z)));
                    break;
                }
                uint _S1906 = atlasWidth_21 / uint(_S1904.x);
                float _S1907 = float(_S1906);
                uint _S1908 = (atlasHeight_21 - 1U) / uint(_S1904.y);
                float2 cellSize_43 = float2(_S1907, float(_S1908));
                _S561 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_43) * cellSize_43 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_43 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                break;
            }
            uint operation_9 = uint(round(_S1835.y));
            if(operation_9 == 1U)
            {
                _S182 = _S182 * _S561;
                break;
            }
            if(operation_9 == 2U)
            {
                _S182 = _S182 + _S561;
                break;
            }
            if(operation_9 == 3U)
            {
                _S182 = _S182 - _S561;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1835.z;
                _S182 = _S182 * float4((1.0f - factor_9))  + _S561 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S182.x;
    }
    else
    {
        ior_0 = _S170;
    }
    float opacityThreshold_0 = _S165.z;
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
    float _S1909 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S1910 = max(0.00100000004749745f, roughness_0);
    float _S1911 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1912 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1912;
    bool omniPbrSpecular_0 = (_S173.w) >= 0.5f;
    float dielectricLevel_0 = _S159.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1913 = float3(dielectricLevel_0) ;
        float3 _S1914 = float3(metallic_0) ;
        float3 _S1915 = mix(_S1913, diffuseColor_0, _S1914);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1913, diffuseColor_0, _S1914);
        grazingIncidence_0 = _S1915;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S165.w) >= 0.5f)
        {
            float3 _S1916 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1916;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1917 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1917);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1917);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1918 = _S2;
    float _S1919 = _S2.ambientLight_0.w;
    if(_S1919 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1920 = _S1918.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1920, _S1920)) > 0.0f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S2.environmentControls_0.x) >= 0.5f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S2.environmentControls_0.w) >= 0.5f;
    }
    uint _S1921 = min(uint(_S1919), 128U);
    float3 sceneEye_0 = normalize((((float4(unlitColor_0, 0.0f)) * (_S2.eyeToWorld_0))).xyz);
    float3 worldPosition_0 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S2.eyeToWorld_0))).xyz;
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
    float4 _S1922 = float4(_S6->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S1922.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S6->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S1923 = _S2;
    uint _S1924 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S1924) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S1925 = _S1918.ambientLight_0.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S1926 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S1926;
        for(;;)
        {
            if(lightCount_0 < _S1924)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << lightCount_0)) != 0U)
            {
                domeAmbient_1 = domeAmbient_1 + _S2.domeAmbient_0[lightCount_0].xyz;
            }
            lightCount_0 = lightCount_0 + 1U;
        }
    }
    else
    {
        domeAmbient_1 = _S1925;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S1927 = !hasSceneLighting_0;
    if(_S1927)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1921;
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
        bool _S1928;
        if(hasSceneLighting_0)
        {
            _S1928 = (((uint4(_S6->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S1928 = false;
        }
        if(_S1928)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S1929 = lightIndex_0 == 0U;
        bool _S1930;
        if(_S1929)
        {
            _S1930 = _S1927;
        }
        else
        {
            _S1930 = false;
        }
        float lightType_0;
        if(_S1930)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S1931 = 224U + lightIndex_0 * 16U;
            float _S1932 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1931)>>2]);
            float _S1933 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1931 + 4U)>>2]);
            float _S1934 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1931 + 8U)>>2]);
            float _S1935 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1931 + 12U)>>2]);
            lightType_0 = _S1935;
        }
        bool _S1936;
        if(_S1929)
        {
            _S1936 = _S1927;
        }
        else
        {
            _S1936 = false;
        }
        if(_S1936)
        {
            lightDirection_0 = normalize((float4(_S6->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S1937 = 2272U + lightIndex_0 * 16U;
            float _S1938 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1937)>>2]);
            float _S1939 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1937 + 4U)>>2]);
            float _S1940 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1937 + 8U)>>2]);
            float _S1941 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1937 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S1938, _S1939, _S1940));
        }
        bool _S1942;
        if(_S1929)
        {
            _S1942 = _S1927;
        }
        else
        {
            _S1942 = false;
        }
        if(_S1942)
        {
            roughness_0 = (float4(_S6->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S1943 = 4320U + lightIndex_0 * 16U;
            float _S1944 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1943)>>2]);
            float _S1945 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1943 + 4U)>>2]);
            float _S1946 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1943 + 8U)>>2]);
            float _S1947 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1943 + 12U)>>2]);
            roughness_0 = _S1947;
        }
        bool _S1948;
        if(_S1929)
        {
            _S1948 = _S1927;
        }
        else
        {
            _S1948 = false;
        }
        if(_S1948)
        {
            diffuseColor_0 = _S1922.xyz;
        }
        else
        {
            uint _S1949 = 4320U + lightIndex_0 * 16U;
            float _S1950 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1949)>>2]);
            float _S1951 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1949 + 4U)>>2]);
            float _S1952 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1949 + 8U)>>2]);
            float _S1953 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1949 + 12U)>>2]);
            diffuseColor_0 = float3(_S1950, _S1951, _S1952);
        }
        bool _S1954;
        if(_S1929)
        {
            _S1954 = _S1927;
        }
        else
        {
            _S1954 = false;
        }
        if(_S1954)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S1955 = 6368U + lightIndex_0 * 16U;
            float _S1956 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1955)>>2]);
            float _S1957 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1955 + 4U)>>2]);
            float _S1958 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1955 + 8U)>>2]);
            float _S1959 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1955 + 12U)>>2]);
            metallic_0 = _S1956;
        }
        bool _S1960;
        if(_S1929)
        {
            _S1960 = _S1927;
        }
        else
        {
            _S1960 = false;
        }
        if(_S1960)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S1961 = 6368U + lightIndex_0 * 16U;
            float _S1962 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1961)>>2]);
            float _S1963 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1961 + 4U)>>2]);
            float _S1964 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1961 + 8U)>>2]);
            float _S1965 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1961 + 12U)>>2]);
            clearcoatRoughness_0 = _S1963;
        }
        bool _S1966;
        if(_S1929)
        {
            _S1966 = _S1927;
        }
        else
        {
            _S1966 = false;
        }
        if(_S1966)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1967 = 8416U + lightIndex_0 * 16U;
            float _S1968 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1967)>>2]);
            float _S1969 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1967 + 4U)>>2]);
            float _S1970 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1967 + 8U)>>2]);
            float _S1971 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1967 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S1968, _S1969, _S1970));
        }
        bool _S1972;
        if(_S1929)
        {
            _S1972 = _S1927;
        }
        else
        {
            _S1972 = false;
        }
        if(_S1972)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S1973 = 10464U + lightIndex_0 * 16U;
            float _S1974 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1973)>>2]);
            float _S1975 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1973 + 4U)>>2]);
            float _S1976 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1973 + 8U)>>2]);
            float _S1977 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1973 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S1974, _S1975, _S1976));
        }
        bool _S1978;
        if(_S1929)
        {
            _S1978 = _S1927;
        }
        else
        {
            _S1978 = false;
        }
        float shapeX_0;
        if(_S1978)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S1979 = 8416U + lightIndex_0 * 16U;
            float _S1980 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1979)>>2]);
            float _S1981 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1979 + 4U)>>2]);
            float _S1982 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1979 + 8U)>>2]);
            float _S1983 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1979 + 12U)>>2]);
            shapeX_0 = _S1983;
        }
        bool _S1984;
        if(_S1929)
        {
            _S1984 = _S1927;
        }
        else
        {
            _S1984 = false;
        }
        float shapeY_0;
        if(_S1984)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S1985 = 10464U + lightIndex_0 * 16U;
            float _S1986 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985)>>2]);
            float _S1987 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 4U)>>2]);
            float _S1988 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 8U)>>2]);
            float _S1989 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 12U)>>2]);
            shapeY_0 = _S1989;
        }
        bool _S1990;
        if(_S1929)
        {
            _S1990 = _S1927;
        }
        else
        {
            _S1990 = false;
        }
        float lightRadius_0;
        if(_S1990)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S1991 = 2272U + lightIndex_0 * 16U;
            float _S1992 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1991)>>2]);
            float _S1993 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1991 + 4U)>>2]);
            float _S1994 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1991 + 8U)>>2]);
            float _S1995 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1991 + 12U)>>2]);
            lightRadius_0 = _S1995;
        }
        bool _S1996;
        if(_S1929)
        {
            _S1996 = _S1927;
        }
        else
        {
            _S1996 = false;
        }
        if(_S1996)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S1997;
        if(_S1929)
        {
            _S1997 = _S1927;
        }
        else
        {
            _S1997 = false;
        }
        if(_S1997)
        {
            _S182 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1998 = 15296U + lightIndex_0 * 16U;
            float _S1999 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1998)>>2]);
            float _S2000 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1998 + 4U)>>2]);
            float _S2001 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1998 + 8U)>>2]);
            float _S2002 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1998 + 12U)>>2]);
            _S182 = float4(_S1999, _S2000, _S2001, _S2002);
        }
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S2003 = 12960U + lightIndex_0 * 16U;
            float _S2004 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2003)>>2]);
            float _S2005 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2003 + 4U)>>2]);
            float _S2006 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2003 + 8U)>>2]);
            float _S2007 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2003 + 12U)>>2]);
            int shadowSlot_0 = int(_S2004);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S2008 = _S2;
                    FragmentFrameParameters_0 _S2009 = _S2;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - specularColor_0;
                    }
                    else
                    {
                        color_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S2008.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S2010 = lightClip_0.w;
                    if(_S2010 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S2010) ;
                    bool _S2011;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S2011 = true;
                    }
                    else
                    {
                        _S2011 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S2012;
                    if(_S2011)
                    {
                        _S2012 = true;
                    }
                    else
                    {
                        _S2012 = (ndc_0.z) < 0.0f;
                    }
                    bool _S2013;
                    if(_S2012)
                    {
                        _S2013 = true;
                    }
                    else
                    {
                        _S2013 = (ndc_0.z) > 1.0f;
                    }
                    if(_S2013)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S2014 = _S2009.shadowTile_0[shadowSlot_0].xy;
                    float2 _S2015 = _S2009.shadowTile_0[shadowSlot_0].zw;
                    float2 _S2016 = _S2014 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S2015;
                    float texel_0 = _S2008.shadowControls_0[shadowSlot_0].w;
                    float _S2017 = max(_S2008.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S2018 = ndc_0.z - _S2008.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S2019 = float2((texel_0 * 0.5f)) ;
                    float2 _S2020 = _S2014 + _S2019;
                    float2 _S2021 = _S2014 + _S2015 - _S2019;
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
                            if(_S2018 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S2016 + float2(float(x_0), float(y_0)) * float2((_S2017 * texel_0)) , _S2020, _S2021)), level((0.0f))).x))
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
        float3 _S2022 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S2022;
        sampleOffsets_0[int(1)] = _S2022;
        sampleOffsets_0[int(2)] = _S2022;
        sampleOffsets_0[int(3)] = _S2022;
        sampleOffsets_0[int(4)] = _S2022;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S2023 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S2023 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S2023 - halfHeight_0;
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
            float sampleIntensity_1;
            float emissionScale_0;
            if(lightType_0 >= 2.0f)
            {
                uint _S2024 = 224U + lightIndex_0 * 16U;
                float _S2025 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2024)>>2]);
                float _S2026 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2024 + 4U)>>2]);
                float _S2027 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2024 + 8U)>>2]);
                float _S2028 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2024 + 12U)>>2]);
                float3 toLight_0 = float3(_S2025, _S2026, _S2027) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S2029 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S2029)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S2029;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S182.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S182.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S2030 = _S182.y;
                        if(_S2030 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S2030);
                        }
                        else
                        {
                            ior_0 = 1.0f;
                        }
                        emissionScale_0 = ior_0;
                    }
                    emissionScale_0 = sampleIntensity_1 * emissionScale_0;
                }
                else
                {
                    emissionScale_0 = sampleIntensity_1;
                }
                sampleDirection_0 = sampleDirection_1;
                sampleIntensity_1 = sampleIntensity_2;
            }
            else
            {
                sampleDirection_0 = lightDirection_0;
                sampleIntensity_1 = sampleIntensity_0;
                emissionScale_0 = 1.0f;
            }
            float3 half_0 = normalize(sampleDirection_0 + domeAmbient_1);
            float normalDotLight_0 = saturate(dot(normal_2, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(normal_2, half_0));
            float _S2031 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_0;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_0 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S2031)) ;
            }
            else
            {
                diffuseFresnel_0 = mix(normalIncidence_0, grazingIncidence_0, float3(_S2031) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_0);
            float3 _S2032 = float3(_S2031) ;
            float _S2033 = max(_S1910, 0.00100000004749745f);
            float alpha_0 = _S2033 * _S2033;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S2034 = 1.0f - k_0;
            float3 _S2035 = float3(max(4.0f * normalDotLight_0 * _S1909, 1.00000000317107685e-30f)) ;
            float3 _S2036 = mix(normalIncidence_0, grazingIncidence_0, _S2032) * float3((_S1909 / (_S1909 * _S2034 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S2034 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S2035;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S2037 = max(_S1911, 0.00100000004749745f);
                float alpha_1 = _S2037 * _S2037;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S2038 = 1.0f - k_1;
                directSpecular_0 = _S2036 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S2032) * float3((_S1909 / (_S1909 * _S2038 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S2038 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S2035);
            }
            else
            {
                directSpecular_0 = _S2036;
            }
            float3 _S2039 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S2039 * _S1912) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S2039);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S2040 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S2041 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S2042 = saturate(_S1910);
        float2 _S2043 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2041, _S2042)), level((0.0f)))).xy;
        float _S2044 = _S2043.x;
        float _S2045 = _S2043.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S2044)  + grazingIncidence_0 * float3(_S2045) ;
        if(omniPbrSpecular_0)
        {
            lightDirection_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S2044 + _S2045)) ;
        }
        else
        {
            lightDirection_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - lightDirection_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S2046 = max(_S1923.domeControls_0.y, 1.0f);
        float3 _S2047 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S2046 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1923.domeControls_0.z;
            float _S2048 = _S1923.domeControls_0.w;
            for(;;)
            {
                bool _S2049 = _S2046 <= 1.0f;
                _S4 = _S2049;
                if(_S2049)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S2048, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S2046)), level((0.0f)))).xyz;
                break;
            }
            float _S2050 = _S2040.environmentControls_0.y;
            float _S2051 = _S2040.environmentControls_0.z;
            for(;;)
            {
                if(_S4)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2052 = max(_S2050, 1.0f);
                    float _S2053 = _S2052 - 1.0f;
                    float slice_0 = _S2042 * max(_S2053, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S2051, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S2052)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S2053) + v_0) / _S2052)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S2054 = max(_S2050, 1.0f);
                float total_0 = _S2054 * _S2046;
                float _S2055 = _S2054 - 1.0f;
                float slice_1 = _S2042 * max(_S2055, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S2051, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S2054;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S2055) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S4)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_3 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2056 = max(_S2050, 1.0f);
                        float _S2057 = _S2056 - 1.0f;
                        float slice_2 = saturate(_S1911) * max(_S2057, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S2051, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S2056)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S2057) + v_2) / _S2056)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2058 = max(_S2050, 1.0f);
                    float total_1 = _S2058 * _S2046;
                    float _S2059 = _S2058 - 1.0f;
                    float slice_3 = saturate(_S1911) * max(_S2059, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S2051, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S2058;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S2059) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                color_0 = specularColor_0;
            }
            else
            {
                color_0 = _S2047;
            }
            lightTangent_0 = diffuseColor_0;
            lightBitangent_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightTangent_0 = _S2047;
            lightBitangent_0 = _S2047;
            color_0 = _S2047;
            for(;;)
            {
                if(sampleIndex_0 < _S1924)
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
                float domeGroup_0 = _S2.domeEnvironment_0[sampleIndex_0].x;
                if(domeGroup_0 < 0.0f)
                {
                    sampleIndex_0 = sampleIndex_0 + 1U;
                    continue;
                }
                float _S2060 = _S1923.domeControls_0.w;
                for(;;)
                {
                    bool _S2061 = _S2046 <= 1.0f;
                    _S5 = _S2061;
                    if(_S2061)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S2060, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S2046)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightTangent_0 + diffuseColor_0;
                float _S2062 = _S2040.environmentControls_0.y;
                float _S2063 = _S2040.environmentControls_0.z;
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2064 = max(_S2062, 1.0f);
                        float _S2065 = _S2064 - 1.0f;
                        float slice_4 = _S2042 * max(_S2065, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S2063, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S2064)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S2065) + v_4) / _S2064)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2066 = max(_S2062, 1.0f);
                    float total_2 = _S2066 * _S2046;
                    float _S2067 = _S2066 - 1.0f;
                    float slice_5 = _S2042 * max(_S2067, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S2063, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S2066;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S2067) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightBitangent_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S5)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_7 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S2068 = max(_S2062, 1.0f);
                            float _S2069 = _S2068 - 1.0f;
                            float slice_6 = saturate(_S1911) * max(_S2069, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S2063, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S2068)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S2069) + v_6) / _S2068)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2070 = max(_S2062, 1.0f);
                        float total_3 = _S2070 * _S2046;
                        float _S2071 = _S2070 - 1.0f;
                        float slice_7 = saturate(_S1911) * max(_S2071, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S2063, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S2070;
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S2071) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S2072 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2041, saturate(_S1911))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * color_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S2072.x)  + float3(_S2072.y) );
        }
        else
        {
            color_3 = color_4;
        }
    }
    float3 color_5 = (color_3 + emissiveColor_1) * float3(exp2((as_type<float>((_S3.z))))) ;
    if((_S3.y) == 1U)
    {
        color_3 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_3 = color_5;
    }
    pixelOutput_0 _S2073 = { float4(color_3, opacity_0) };
    return _S2073;
}
