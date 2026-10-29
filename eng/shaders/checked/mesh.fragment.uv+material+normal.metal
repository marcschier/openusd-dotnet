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
    uint lightCount_0;
    float roughness_0;
    float metallic_0;
    float2 detailSource_0;
    float2 tangent_1;
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
        float4 _S185 = float4(_S6->domeLinkControls_0) ;
        uint maskU_0 = uint(round(_S185.y));
        uint maskV_0 = uint(round(_S185.z));
        bool _S186 = (maskU_0 & 2U) != 0U;
        if(_S186)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S187 = (maskV_0 & 2U) != 0U;
        if(_S187)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S188 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S184)
            {
                for(;;)
                {
                    if(_S188 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S189 = (&kernelContext_0)->baseColorTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S189).get_width(0)),(*((&height_0)) = (_S189).get_height(0));
                    float2 size_0 = float2(float(width_0), float(height_0));
                    float2 _S190 = float2(0.5f) ;
                    float2 remapped_0 = clamp((detailSource_0 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S190 / size_0, (size_0 - _S190) / size_0);
                    if((_S188 & 1U) != 0U)
                    {
                        roughness_0 = remapped_0.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S188 & 2U) != 0U)
                    {
                        metallic_0 = remapped_0.y;
                    }
                    else
                    {
                        metallic_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(roughness_0, metallic_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S191 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S191).get_width(0)),(*((&atlasHeight_0)) = (_S191).get_height(0));
            int3 _S192 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S192)).xy), uint(((_S192)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S193 = atlasWidth_0;
                uint _S194 = 2U % atlasWidth_0;
                int _S195 = int(_S194);
                uint _S196 = 2U / _S193;
                int3 _S197 = int3(_S195, int(_S196), int(0));
                int _S198 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S197)).xy), uint(((_S197)).z))).x * 255.0f)));
                uint _S199 = atlasWidth_0;
                uint _S200 = 3U % atlasWidth_0;
                int _S201 = int(_S200);
                uint _S202 = 3U / _S199;
                int3 _S203 = int3(_S201, int(_S202), int(0));
                int2 minimumTile_0 = int2(_S198, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S203)).xy), uint(((_S203)).z))).x * 255.0f))));
                uint _S204 = atlasWidth_0;
                uint _S205 = 4U % atlasWidth_0;
                int _S206 = int(_S205);
                uint _S207 = 4U / _S204;
                int3 _S208 = int3(_S206, int(_S207), int(0));
                uint _S209 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S208)).xy), uint(((_S208)).z))).x * 255.0f));
                uint _S210 = atlasWidth_0;
                uint _S211 = 5U % atlasWidth_0;
                int _S212 = int(_S211);
                uint _S213 = 5U / _S210;
                int3 _S214 = int3(_S212, int(_S213), int(0));
                uint _S215 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S214)).xy), uint(((_S214)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S209, _S215);
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
                    int3 _S216 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S216)).xy), uint(((_S216)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S209 + uint(tile_0.x));
                uint _S217 = atlasWidth_0;
                uint _S218 = offset_0 % atlasWidth_0;
                int _S219 = int(_S218);
                uint _S220 = offset_0 / _S217;
                int3 _S221 = int3(_S219, int(_S220), int(0));
                uint _S222 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S221)).xy), uint(((_S221)).z))).x * 255.0f));
                uint _S223 = atlasWidth_0;
                uint _S224 = offset_0 + 1U;
                uint _S225 = _S224 % atlasWidth_0;
                int _S226 = int(_S225);
                uint _S227 = _S224 / _S223;
                int3 _S228 = int3(_S226, int(_S227), int(0));
                uint slot_0 = _S222 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S228)).xy), uint(((_S228)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S229 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S229)).xy), uint(((_S229)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S230 = atlasWidth_0;
                uint _S231 = 6U % atlasWidth_0;
                int _S232 = int(_S231);
                uint _S233 = 6U / _S230;
                int3 _S234 = int3(_S232, int(_S233), int(0));
                uint _S235 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S234)).xy), uint(((_S234)).z))).x * 255.0f));
                uint _S236 = atlasWidth_0;
                uint _S237 = 7U % atlasWidth_0;
                int _S238 = int(_S237);
                uint _S239 = 7U / _S236;
                int3 _S240 = int3(_S238, int(_S239), int(0));
                uint columns_0 = _S235 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S240)).xy), uint(((_S240)).z))).x * 255.0f)) << 8U);
                uint _S241 = atlasWidth_0;
                uint _S242 = 8U % atlasWidth_0;
                int _S243 = int(_S242);
                uint _S244 = 8U / _S241;
                int3 _S245 = int3(_S243, int(_S244), int(0));
                uint _S246 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S245)).xy), uint(((_S245)).z))).x * 255.0f));
                uint _S247 = atlasWidth_0;
                uint _S248 = 9U % atlasWidth_0;
                int _S249 = int(_S248);
                uint _S250 = 9U / _S247;
                int3 _S251 = int3(_S249, int(_S250), int(0));
                uint rows_0 = ((_S246 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S251)).xy), uint(((_S251)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S209 * _S215 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S252 = atlasWidth_0 / columns_0;
                float _S253 = float(_S252);
                uint _S254 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S253, float(_S254));
                uint _S255 = slot_1 % columns_0;
                float _S256 = float(_S255);
                uint _S257 = slot_1 / columns_0;
                _S182 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S256, float(_S257)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(detailSource_0) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S258 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(detailSource_0)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S258);
            }
            if(hasSceneLighting_0)
            {
                int3 _S259 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S259)).xy), uint(((_S259)).z)));
                break;
            }
            uint _S260 = atlasWidth_0 / uint(_S258.x);
            float _S261 = float(_S260);
            uint _S262 = (atlasHeight_0 - 1U) / uint(_S258.y);
            float2 cellSize_1 = float2(_S261, float(_S262));
            _S182 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S182;
        float4 _S263 = float4(_S6->compositeControls_0) ;
        float _S264 = _S263.x;
        if(_S264 == 0.0f)
        {
            hasSceneLighting_0 = (_S263.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S265 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S265).get_width(0)),(*((&sourceHeight_0)) = (_S265).get_height(0));
            texture2d<float, access::sample> _S266 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S266).get_width(0)),(*((&valueHeight_0)) = (_S266).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S267 = diffuseSample_0;
                for(;;)
                {
                    float _S268 = _S267.w;
                    if(_S268 > 1.0f)
                    {
                        diffuseColor_0 = _S267.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S269 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S269).get_width(0)),(*((&height_1)) = (_S269).get_height(0));
                    uint3 _S270 = uint3(round(_S267.xyz * float3(255.0f) ));
                    uint index_1 = ((_S270.x) | ((_S270.y) << 8U)) | ((_S270.z) << 16U);
                    if(_S268 == 0.0f)
                    {
                        _S183 = _S270;
                    }
                    else
                    {
                        _S183 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S183 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S271 = address_0.x;
                    uint _S272 = _S271 % width_1;
                    int _S273 = int(_S272);
                    uint _S274 = _S271 / width_1;
                    int3 _S275 = int3(_S273, int(_S274), int(0));
                    float _S276 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S275)).xy), uint(((_S275)).z))).x;
                    uint _S277 = address_0.y;
                    uint _S278 = _S277 % width_1;
                    int _S279 = int(_S278);
                    uint _S280 = _S277 / width_1;
                    int3 _S281 = int3(_S279, int(_S280), int(0));
                    float _S282 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S281)).xy), uint(((_S281)).z))).x;
                    uint _S283 = address_0.z;
                    uint _S284 = _S283 % width_1;
                    int _S285 = int(_S284);
                    uint _S286 = _S283 / width_1;
                    int3 _S287 = int3(_S285, int(_S286), int(0));
                    diffuseColor_0 = float3(_S276, _S282, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S287)).xy), uint(((_S287)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                if(_S186)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S187)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S288 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S288 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S289 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S289).get_width(0)),(*((&height_2)) = (_S289).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S290 = float2(0.5f) ;
                    float2 remapped_1 = clamp((detailSource_0 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S290 / size_1, (size_1 - _S290) / size_1);
                    if((_S288 & 1U) != 0U)
                    {
                        roughness_0 = remapped_1.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S288 & 2U) != 0U)
                    {
                        metallic_0 = remapped_1.y;
                    }
                    else
                    {
                        metallic_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(roughness_0, metallic_0);
                    break;
                }
                diffuseSample_0.z = (((&kernelContext_0)->diffuseValuesTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (tangent_1))).x;
            }
        }
        float4 _S291 = diffuseSample_0;
        for(;;)
        {
            if(_S264 != 2.0f)
            {
                _S182 = _S291;
                break;
            }
            bool _S292 = (_S263.w) >= 0.5f;
            if((maskU_0 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_0 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S293 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S292)
                {
                    for(;;)
                    {
                        if(_S293 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S294 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_3;
                        thread uint height_3;
                        (*((&width_3)) = (_S294).get_width(0)),(*((&height_3)) = (_S294).get_height(0));
                        float2 size_2 = float2(float(width_3), float(height_3));
                        float2 _S295 = float2(0.5f) ;
                        float2 remapped_2 = clamp((detailSource_0 * (size_2 - float2(2.0f) ) + float2(1.0f) ) / size_2, _S295 / size_2, (size_2 - _S295) / size_2);
                        if((_S293 & 1U) != 0U)
                        {
                            roughness_0 = remapped_2.x;
                        }
                        else
                        {
                            roughness_0 = detailSource_0.x;
                        }
                        if((_S293 & 2U) != 0U)
                        {
                            metallic_0 = remapped_2.y;
                        }
                        else
                        {
                            metallic_0 = detailSource_0.y;
                        }
                        detailSource_0 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S296 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S296).get_width(0)),(*((&atlasHeight_1)) = (_S296).get_height(0));
                int3 _S297 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S297)).xy), uint(((_S297)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S298 = atlasWidth_1;
                    uint _S299 = 2U % atlasWidth_1;
                    int _S300 = int(_S299);
                    uint _S301 = 2U / _S298;
                    int3 _S302 = int3(_S300, int(_S301), int(0));
                    int _S303 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S302)).xy), uint(((_S302)).z))).x * 255.0f)));
                    uint _S304 = atlasWidth_1;
                    uint _S305 = 3U % atlasWidth_1;
                    int _S306 = int(_S305);
                    uint _S307 = 3U / _S304;
                    int3 _S308 = int3(_S306, int(_S307), int(0));
                    int2 minimumTile_1 = int2(_S303, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S308)).xy), uint(((_S308)).z))).x * 255.0f))));
                    uint _S309 = atlasWidth_1;
                    uint _S310 = 4U % atlasWidth_1;
                    int _S311 = int(_S310);
                    uint _S312 = 4U / _S309;
                    int3 _S313 = int3(_S311, int(_S312), int(0));
                    uint _S314 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S313)).xy), uint(((_S313)).z))).x * 255.0f));
                    uint _S315 = atlasWidth_1;
                    uint _S316 = 5U % atlasWidth_1;
                    int _S317 = int(_S316);
                    uint _S318 = 5U / _S315;
                    int3 _S319 = int3(_S317, int(_S318), int(0));
                    uint _S320 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S319)).xy), uint(((_S319)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S314, _S320);
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
                        int3 _S321 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S321)).xy), uint(((_S321)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S314 + uint(tile_2.x));
                    uint _S322 = atlasWidth_1;
                    uint _S323 = offset_1 % atlasWidth_1;
                    int _S324 = int(_S323);
                    uint _S325 = offset_1 / _S322;
                    int3 _S326 = int3(_S324, int(_S325), int(0));
                    uint _S327 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S326)).xy), uint(((_S326)).z))).x * 255.0f));
                    uint _S328 = atlasWidth_1;
                    uint _S329 = offset_1 + 1U;
                    uint _S330 = _S329 % atlasWidth_1;
                    int _S331 = int(_S330);
                    uint _S332 = _S329 / _S328;
                    int3 _S333 = int3(_S331, int(_S332), int(0));
                    uint slot_2 = _S327 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S333)).xy), uint(((_S333)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S334 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S334)).xy), uint(((_S334)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S335 = atlasWidth_1;
                    uint _S336 = 6U % atlasWidth_1;
                    int _S337 = int(_S336);
                    uint _S338 = 6U / _S335;
                    int3 _S339 = int3(_S337, int(_S338), int(0));
                    uint _S340 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S339)).xy), uint(((_S339)).z))).x * 255.0f));
                    uint _S341 = atlasWidth_1;
                    uint _S342 = 7U % atlasWidth_1;
                    int _S343 = int(_S342);
                    uint _S344 = 7U / _S341;
                    int3 _S345 = int3(_S343, int(_S344), int(0));
                    uint columns_1 = _S340 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S345)).xy), uint(((_S345)).z))).x * 255.0f)) << 8U);
                    uint _S346 = atlasWidth_1;
                    uint _S347 = 8U % atlasWidth_1;
                    int _S348 = int(_S347);
                    uint _S349 = 8U / _S346;
                    int3 _S350 = int3(_S348, int(_S349), int(0));
                    uint _S351 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S350)).xy), uint(((_S350)).z))).x * 255.0f));
                    uint _S352 = atlasWidth_1;
                    uint _S353 = 9U % atlasWidth_1;
                    int _S354 = int(_S353);
                    uint _S355 = 9U / _S352;
                    int3 _S356 = int3(_S354, int(_S355), int(0));
                    uint rows_1 = ((_S351 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S356)).xy), uint(((_S356)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S314 * _S320 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S357 = atlasWidth_1 / columns_1;
                    float _S358 = float(_S357);
                    uint _S359 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S358, float(_S359));
                    uint _S360 = slot_3 % columns_1;
                    float _S361 = float(_S360);
                    uint _S362 = slot_3 / columns_1;
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S361, float(_S362)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailSource_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S363 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(detailSource_0)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S363);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S364 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S364)).xy), uint(((_S364)).z)));
                    break;
                }
                uint _S365 = atlasWidth_1 / uint(_S363.x);
                float _S366 = float(_S365);
                uint _S367 = (atlasHeight_1 - 1U) / uint(_S363.y);
                float2 cellSize_3 = float2(_S366, float(_S367));
                _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S263.y));
            if(operation_0 == 1U)
            {
                _S182 = _S291 * _S182;
                break;
            }
            if(operation_0 == 2U)
            {
                _S182 = _S291 + _S182;
                break;
            }
            if(operation_0 == 3U)
            {
                _S182 = _S291 - _S182;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S263.z;
                _S182 = _S291 * float4((1.0f - factor_0))  + _S182 * float4(factor_0) ;
                break;
            }
            _S182 = _S291;
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
    bool _S368 = (udimMask_0 & 4U) != 0U;
    float4 _S369 = float4(_S6->domeLinkControls_0) ;
    uint maskU_1 = uint(round(_S369.y));
    uint maskV_1 = uint(round(_S369.z));
    if((maskU_1 & 4U) != 0U)
    {
        index_0 = 1U;
    }
    else
    {
        index_0 = 0U;
    }
    if((maskV_1 & 4U) != 0U)
    {
        lightCount_0 = 2U;
    }
    else
    {
        lightCount_0 = 0U;
    }
    uint _S370 = index_0 | lightCount_0;
    for(;;)
    {
        if(!_S368)
        {
            for(;;)
            {
                if(_S370 == 0U)
                {
                    break;
                }
                texture2d<float, access::sample> _S371 = (&kernelContext_0)->normalTexture_0;
                thread uint width_4;
                thread uint height_4;
                (*((&width_4)) = (_S371).get_width(0)),(*((&height_4)) = (_S371).get_height(0));
                float2 size_3 = float2(float(width_4), float(height_4));
                float2 _S372 = float2(0.5f) ;
                float2 remapped_3 = clamp((detailSource_0 * (size_3 - float2(2.0f) ) + float2(1.0f) ) / size_3, _S372 / size_3, (size_3 - _S372) / size_3);
                if((_S370 & 1U) != 0U)
                {
                    roughness_0 = remapped_3.x;
                }
                else
                {
                    roughness_0 = detailSource_0.x;
                }
                if((_S370 & 2U) != 0U)
                {
                    metallic_0 = remapped_3.y;
                }
                else
                {
                    metallic_0 = detailSource_0.y;
                }
                detailSource_0 = float2(roughness_0, metallic_0);
                break;
            }
            _S182 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (detailSource_0)));
            break;
        }
        texture2d<float, access::sample> _S373 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S373).get_width(0)),(*((&atlasHeight_2)) = (_S373).get_height(0));
        int3 _S374 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S374)).xy), uint(((_S374)).z))) * float4(255.0f) );
        if((metadata_2.x) == 255.0f)
        {
            uint _S375 = atlasWidth_2;
            uint _S376 = 2U % atlasWidth_2;
            int _S377 = int(_S376);
            uint _S378 = 2U / _S375;
            int3 _S379 = int3(_S377, int(_S378), int(0));
            int _S380 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S379)).xy), uint(((_S379)).z))).x * 255.0f)));
            uint _S381 = atlasWidth_2;
            uint _S382 = 3U % atlasWidth_2;
            int _S383 = int(_S382);
            uint _S384 = 3U / _S381;
            int3 _S385 = int3(_S383, int(_S384), int(0));
            int2 minimumTile_2 = int2(_S380, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S385)).xy), uint(((_S385)).z))).x * 255.0f))));
            uint _S386 = atlasWidth_2;
            uint _S387 = 4U % atlasWidth_2;
            int _S388 = int(_S387);
            uint _S389 = 4U / _S386;
            int3 _S390 = int3(_S388, int(_S389), int(0));
            uint _S391 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S390)).xy), uint(((_S390)).z))).x * 255.0f));
            uint _S392 = atlasWidth_2;
            uint _S393 = 5U % atlasWidth_2;
            int _S394 = int(_S393);
            uint _S395 = 5U / _S392;
            int3 _S396 = int3(_S394, int(_S395), int(0));
            uint _S397 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S396)).xy), uint(((_S396)).z))).x * 255.0f));
            uint2 grid_2 = uint2(_S391, _S397);
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
                int3 _S398 = int3(int(1), int(0), int(0));
                _S182 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S398)).xy), uint(((_S398)).z)));
                break;
            }
            uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S391 + uint(tile_4.x));
            uint _S399 = atlasWidth_2;
            uint _S400 = offset_2 % atlasWidth_2;
            int _S401 = int(_S400);
            uint _S402 = offset_2 / _S399;
            int3 _S403 = int3(_S401, int(_S402), int(0));
            uint _S404 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S403)).xy), uint(((_S403)).z))).x * 255.0f));
            uint _S405 = atlasWidth_2;
            uint _S406 = offset_2 + 1U;
            uint _S407 = _S406 % atlasWidth_2;
            int _S408 = int(_S407);
            uint _S409 = _S406 / _S405;
            int3 _S410 = int3(_S408, int(_S409), int(0));
            uint slot_4 = _S404 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S410)).xy), uint(((_S410)).z))).x * 255.0f)) << 8U);
            if(slot_4 == 0U)
            {
                int3 _S411 = int3(int(1), int(0), int(0));
                _S182 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S411)).xy), uint(((_S411)).z)));
                break;
            }
            uint slot_5 = slot_4 - 1U;
            uint _S412 = atlasWidth_2;
            uint _S413 = 6U % atlasWidth_2;
            int _S414 = int(_S413);
            uint _S415 = 6U / _S412;
            int3 _S416 = int3(_S414, int(_S415), int(0));
            uint _S417 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S416)).xy), uint(((_S416)).z))).x * 255.0f));
            uint _S418 = atlasWidth_2;
            uint _S419 = 7U % atlasWidth_2;
            int _S420 = int(_S419);
            uint _S421 = 7U / _S418;
            int3 _S422 = int3(_S420, int(_S421), int(0));
            uint columns_2 = _S417 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S422)).xy), uint(((_S422)).z))).x * 255.0f)) << 8U);
            uint _S423 = atlasWidth_2;
            uint _S424 = 8U % atlasWidth_2;
            int _S425 = int(_S424);
            uint _S426 = 8U / _S423;
            int3 _S427 = int3(_S425, int(_S426), int(0));
            uint _S428 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S427)).xy), uint(((_S427)).z))).x * 255.0f));
            uint _S429 = atlasWidth_2;
            uint _S430 = 9U % atlasWidth_2;
            int _S431 = int(_S430);
            uint _S432 = 9U / _S429;
            int3 _S433 = int3(_S431, int(_S432), int(0));
            uint rows_2 = ((_S428 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S433)).xy), uint(((_S433)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
            uint metadataRows_2 = (10U + 2U * _S391 * _S397 + atlasWidth_2 - 1U) / atlasWidth_2;
            uint _S434 = atlasWidth_2 / columns_2;
            float _S435 = float(_S434);
            uint _S436 = (atlasHeight_2 - metadataRows_2) / rows_2;
            float2 cellSize_4 = float2(_S435, float(_S436));
            uint _S437 = slot_5 % columns_2;
            float _S438 = float(_S437);
            uint _S439 = slot_5 / columns_2;
            _S182 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S438, float(_S439)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(detailSource_0) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        int2 _S440 = int2(metadata_2.zw);
        int2 tile_5 = int2(floor(detailSource_0)) - int2(metadata_2.xy);
        if(any(tile_5 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_5 >= _S440);
        }
        if(hasSceneLighting_0)
        {
            int3 _S441 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S182 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S441)).xy), uint(((_S441)).z)));
            break;
        }
        uint _S442 = atlasWidth_2 / uint(_S440.x);
        float _S443 = float(_S442);
        uint _S444 = (atlasHeight_2 - 1U) / uint(_S440.y);
        float2 cellSize_5 = float2(_S443, float(_S444));
        _S182 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    baseNormalSample_0 = _S182;
    float4 _S445 = float4(_S6->detailUvTransformRow0_0) ;
    if((_S445.w) != 0.0f)
    {
        float4 _S446 = baseNormalSample_0;
        for(;;)
        {
            float _S447 = _S446.w;
            if(_S447 > 1.0f)
            {
                unlitColor_0 = _S446.xyz;
                break;
            }
            texture2d<float, access::sample> _S448 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_5;
            thread uint height_5;
            (*((&width_5)) = (_S448).get_width(0)),(*((&height_5)) = (_S448).get_height(0));
            uint3 _S449 = uint3(round(_S446.xyz * float3(255.0f) ));
            uint index_2 = ((_S449.x) | ((_S449.y) << 8U)) | ((_S449.z) << 16U);
            if(_S447 == 0.0f)
            {
                _S183 = _S449;
            }
            else
            {
                _S183 = uint3(index_2, index_2, index_2);
            }
            uint3 address_1 = _S183 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S450 = address_1.x;
            uint _S451 = _S450 % width_5;
            int _S452 = int(_S451);
            uint _S453 = _S450 / width_5;
            int3 _S454 = int3(_S452, int(_S453), int(0));
            float _S455 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S454)).xy), uint(((_S454)).z))).x;
            uint _S456 = address_1.y;
            uint _S457 = _S456 % width_5;
            int _S458 = int(_S457);
            uint _S459 = _S456 / width_5;
            int3 _S460 = int3(_S458, int(_S459), int(0));
            float _S461 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S460)).xy), uint(((_S460)).z))).x;
            uint _S462 = address_1.z;
            uint _S463 = _S462 % width_5;
            int _S464 = int(_S463);
            uint _S465 = _S462 / width_5;
            int3 _S466 = int3(_S464, int(_S465), int(0));
            unlitColor_0 = float3(_S455, _S461, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S466)).xy), uint(((_S466)).z))).x);
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
    thread float3 _S467 = unlitColor_0;
    float2 _S468 = float2(0.0f, 0.0f);
    if(any(_S178 != _S468))
    {
        float _S469 = _S178.x;
        float _S470 = _S178.y;
        float2 tangent_2 = normalize(float2(_S470, - _S469) / float2(max(abs(_S469), abs(_S470))) );
        float2 bitangent_0 = float2(- tangent_2.y, tangent_2.x);
        if((dot(_S175, tangent_2)) < 0.0f)
        {
            detailSource_0 = - tangent_2;
        }
        else
        {
            detailSource_0 = tangent_2;
        }
        _S467.xy = detailSource_0 * float2(_S467.x)  + bitangent_0 * float2(_S467.y) ;
    }
    float3 _S471 = _S467;
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
        float2 _S472 = _S445.xy;
        float _S473 = dot(_S472, detailSource_0) + _S445.z;
        float4 _S474 = float4(_S6->detailUvTransformRow1_0) ;
        float2 _S475 = _S474.xy;
        float _S476 = dot(_S475, detailSource_0) + _S474.z;
        float2 detailUv_0 = float2(_S473, _S476);
        thread float4 detailSample_0;
        bool _S477 = (udimMask_0 & 4096U) != 0U;
        if((maskU_1 & 4096U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_1 & 4096U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S478 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S477)
            {
                for(;;)
                {
                    if(_S478 == 0U)
                    {
                        detailSource_0 = detailUv_0;
                        break;
                    }
                    texture2d<float, access::sample> _S479 = (&kernelContext_0)->detailNormalTexture_0;
                    thread uint width_6;
                    thread uint height_6;
                    (*((&width_6)) = (_S479).get_width(0)),(*((&height_6)) = (_S479).get_height(0));
                    float2 size_4 = float2(float(width_6), float(height_6));
                    float2 _S480 = float2(0.5f) ;
                    float2 remapped_4 = clamp((detailUv_0 * (size_4 - float2(2.0f) ) + float2(1.0f) ) / size_4, _S480 / size_4, (size_4 - _S480) / size_4);
                    if((_S478 & 1U) != 0U)
                    {
                        roughness_0 = remapped_4.x;
                    }
                    else
                    {
                        roughness_0 = _S473;
                    }
                    if((_S478 & 2U) != 0U)
                    {
                        metallic_0 = remapped_4.y;
                    }
                    else
                    {
                        metallic_0 = _S476;
                    }
                    detailSource_0 = float2(roughness_0, metallic_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S481 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S481).get_width(0)),(*((&atlasHeight_3)) = (_S481).get_height(0));
            int3 _S482 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S482)).xy), uint(((_S482)).z))) * float4(255.0f) );
            if((metadata_3.x) == 255.0f)
            {
                uint _S483 = atlasWidth_3;
                uint _S484 = 2U % atlasWidth_3;
                int _S485 = int(_S484);
                uint _S486 = 2U / _S483;
                int3 _S487 = int3(_S485, int(_S486), int(0));
                int _S488 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S487)).xy), uint(((_S487)).z))).x * 255.0f)));
                uint _S489 = atlasWidth_3;
                uint _S490 = 3U % atlasWidth_3;
                int _S491 = int(_S490);
                uint _S492 = 3U / _S489;
                int3 _S493 = int3(_S491, int(_S492), int(0));
                int2 minimumTile_3 = int2(_S488, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S493)).xy), uint(((_S493)).z))).x * 255.0f))));
                uint _S494 = atlasWidth_3;
                uint _S495 = 4U % atlasWidth_3;
                int _S496 = int(_S495);
                uint _S497 = 4U / _S494;
                int3 _S498 = int3(_S496, int(_S497), int(0));
                uint _S499 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S498)).xy), uint(((_S498)).z))).x * 255.0f));
                uint _S500 = atlasWidth_3;
                uint _S501 = 5U % atlasWidth_3;
                int _S502 = int(_S501);
                uint _S503 = 5U / _S500;
                int3 _S504 = int3(_S502, int(_S503), int(0));
                uint _S505 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S504)).xy), uint(((_S504)).z))).x * 255.0f));
                uint2 grid_3 = uint2(_S499, _S505);
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
                    int3 _S506 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S506)).xy), uint(((_S506)).z)));
                    break;
                }
                uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S499 + uint(tile_6.x));
                uint _S507 = atlasWidth_3;
                uint _S508 = offset_3 % atlasWidth_3;
                int _S509 = int(_S508);
                uint _S510 = offset_3 / _S507;
                int3 _S511 = int3(_S509, int(_S510), int(0));
                uint _S512 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S511)).xy), uint(((_S511)).z))).x * 255.0f));
                uint _S513 = atlasWidth_3;
                uint _S514 = offset_3 + 1U;
                uint _S515 = _S514 % atlasWidth_3;
                int _S516 = int(_S515);
                uint _S517 = _S514 / _S513;
                int3 _S518 = int3(_S516, int(_S517), int(0));
                uint slot_6 = _S512 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S518)).xy), uint(((_S518)).z))).x * 255.0f)) << 8U);
                if(slot_6 == 0U)
                {
                    int3 _S519 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S519)).xy), uint(((_S519)).z)));
                    break;
                }
                uint slot_7 = slot_6 - 1U;
                uint _S520 = atlasWidth_3;
                uint _S521 = 6U % atlasWidth_3;
                int _S522 = int(_S521);
                uint _S523 = 6U / _S520;
                int3 _S524 = int3(_S522, int(_S523), int(0));
                uint _S525 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S524)).xy), uint(((_S524)).z))).x * 255.0f));
                uint _S526 = atlasWidth_3;
                uint _S527 = 7U % atlasWidth_3;
                int _S528 = int(_S527);
                uint _S529 = 7U / _S526;
                int3 _S530 = int3(_S528, int(_S529), int(0));
                uint columns_3 = _S525 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S530)).xy), uint(((_S530)).z))).x * 255.0f)) << 8U);
                uint _S531 = atlasWidth_3;
                uint _S532 = 8U % atlasWidth_3;
                int _S533 = int(_S532);
                uint _S534 = 8U / _S531;
                int3 _S535 = int3(_S533, int(_S534), int(0));
                uint _S536 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S535)).xy), uint(((_S535)).z))).x * 255.0f));
                uint _S537 = atlasWidth_3;
                uint _S538 = 9U % atlasWidth_3;
                int _S539 = int(_S538);
                uint _S540 = 9U / _S537;
                int3 _S541 = int3(_S539, int(_S540), int(0));
                uint rows_3 = ((_S536 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S541)).xy), uint(((_S541)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                uint metadataRows_3 = (10U + 2U * _S499 * _S505 + atlasWidth_3 - 1U) / atlasWidth_3;
                uint _S542 = atlasWidth_3 / columns_3;
                float _S543 = float(_S542);
                uint _S544 = (atlasHeight_3 - metadataRows_3) / rows_3;
                float2 cellSize_6 = float2(_S543, float(_S544));
                uint _S545 = slot_7 % columns_3;
                float _S546 = float(_S545);
                uint _S547 = slot_7 / columns_3;
                _S182 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S546, float(_S547)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(detailUv_0) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            int2 _S548 = int2(metadata_3.zw);
            int2 tile_7 = int2(floor(detailUv_0)) - int2(metadata_3.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S548);
            }
            if(hasSceneLighting_0)
            {
                int3 _S549 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S549)).xy), uint(((_S549)).z)));
                break;
            }
            uint _S550 = atlasWidth_3 / uint(_S548.x);
            float _S551 = float(_S550);
            uint _S552 = (atlasHeight_3 - 1U) / uint(_S548.y);
            float2 cellSize_7 = float2(_S551, float(_S552));
            _S182 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        detailSample_0 = _S182;
        if((_S474.w) != 0.0f)
        {
            float4 _S553 = detailSample_0;
            for(;;)
            {
                float _S554 = _S553.w;
                if(_S554 > 1.0f)
                {
                    unlitColor_0 = _S553.xyz;
                    break;
                }
                texture2d<float, access::sample> _S555 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_7;
                thread uint height_7;
                (*((&width_7)) = (_S555).get_width(0)),(*((&height_7)) = (_S555).get_height(0));
                uint3 _S556 = uint3(round(_S553.xyz * float3(255.0f) ));
                uint index_3 = ((_S556.x) | ((_S556.y) << 8U)) | ((_S556.z) << 16U);
                if(_S554 == 0.0f)
                {
                    _S183 = _S556;
                }
                else
                {
                    _S183 = uint3(index_3, index_3, index_3);
                }
                uint3 address_2 = _S183 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S557 = address_2.x;
                uint _S558 = _S557 % width_7;
                int _S559 = int(_S558);
                uint _S560 = _S557 / width_7;
                int3 _S561 = int3(_S559, int(_S560), int(0));
                float _S562 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S561)).xy), uint(((_S561)).z))).x;
                uint _S563 = address_2.y;
                uint _S564 = _S563 % width_7;
                int _S565 = int(_S564);
                uint _S566 = _S563 / width_7;
                int3 _S567 = int3(_S565, int(_S566), int(0));
                float _S568 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S567)).xy), uint(((_S567)).z))).x;
                uint _S569 = address_2.z;
                uint _S570 = _S569 % width_7;
                int _S571 = int(_S570);
                uint _S572 = _S569 / width_7;
                int3 _S573 = int3(_S571, int(_S572), int(0));
                unlitColor_0 = float3(_S562, _S568, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S573)).xy), uint(((_S573)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S574 = detail_0;
        if(any(_S475 != _S468))
        {
            float _S575 = _S475.x;
            float _S576 = _S475.y;
            float2 tangent_3 = normalize(float2(_S576, - _S575) / float2(max(abs(_S575), abs(_S576))) );
            float2 bitangent_1 = float2(- tangent_3.y, tangent_3.x);
            if((dot(_S472, tangent_3)) < 0.0f)
            {
                tangent_1 = - tangent_3;
            }
            else
            {
                tangent_1 = tangent_3;
            }
            _S574.xy = tangent_1 * float2(_S574.x)  + bitangent_1 * float2(_S574.y) ;
        }
        float3 _S577 = _S574;
        for(;;)
        {
            float3 t_0 = normalize(_S471) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S577) * float3(-1.0f, -1.0f, 1.0f);
            float _S578 = t_0.z;
            if(_S578 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S578))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S471;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S579 = float3(unlitColor_0.x) ;
    float3 _S580 = float3(unlitColor_0.y) ;
    float3 _S581 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S579 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S580 + _S1.normal_0 * _S581);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S579 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S580 + worldNormalBasis_0 * _S581);
    float4 _S582;
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
        bool _S583 = (udimMask_0 & 8U) != 0U;
        if((maskU_1 & 8U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_1 & 8U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S584 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S583)
            {
                for(;;)
                {
                    if(_S584 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S585 = (&kernelContext_0)->roughnessMetallicTexture_0;
                    thread uint width_8;
                    thread uint height_8;
                    (*((&width_8)) = (_S585).get_width(0)),(*((&height_8)) = (_S585).get_height(0));
                    float2 size_5 = float2(float(width_8), float(height_8));
                    float2 _S586 = float2(0.5f) ;
                    float2 remapped_5 = clamp((detailSource_0 * (size_5 - float2(2.0f) ) + float2(1.0f) ) / size_5, _S586 / size_5, (size_5 - _S586) / size_5);
                    if((_S584 & 1U) != 0U)
                    {
                        roughness_0 = remapped_5.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S584 & 2U) != 0U)
                    {
                        metallic_0 = remapped_5.y;
                    }
                    else
                    {
                        metallic_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(roughness_0, metallic_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S587 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S587).get_width(0)),(*((&atlasHeight_4)) = (_S587).get_height(0));
            int3 _S588 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S588)).xy), uint(((_S588)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S589 = atlasWidth_4;
                uint _S590 = 2U % atlasWidth_4;
                int _S591 = int(_S590);
                uint _S592 = 2U / _S589;
                int3 _S593 = int3(_S591, int(_S592), int(0));
                int _S594 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S593)).xy), uint(((_S593)).z))).x * 255.0f)));
                uint _S595 = atlasWidth_4;
                uint _S596 = 3U % atlasWidth_4;
                int _S597 = int(_S596);
                uint _S598 = 3U / _S595;
                int3 _S599 = int3(_S597, int(_S598), int(0));
                int2 minimumTile_4 = int2(_S594, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S599)).xy), uint(((_S599)).z))).x * 255.0f))));
                uint _S600 = atlasWidth_4;
                uint _S601 = 4U % atlasWidth_4;
                int _S602 = int(_S601);
                uint _S603 = 4U / _S600;
                int3 _S604 = int3(_S602, int(_S603), int(0));
                uint _S605 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S604)).xy), uint(((_S604)).z))).x * 255.0f));
                uint _S606 = atlasWidth_4;
                uint _S607 = 5U % atlasWidth_4;
                int _S608 = int(_S607);
                uint _S609 = 5U / _S606;
                int3 _S610 = int3(_S608, int(_S609), int(0));
                uint _S611 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S610)).xy), uint(((_S610)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S605, _S611);
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
                    int3 _S612 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S612)).xy), uint(((_S612)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S605 + uint(tile_8.x));
                uint _S613 = atlasWidth_4;
                uint _S614 = offset_4 % atlasWidth_4;
                int _S615 = int(_S614);
                uint _S616 = offset_4 / _S613;
                int3 _S617 = int3(_S615, int(_S616), int(0));
                uint _S618 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S617)).xy), uint(((_S617)).z))).x * 255.0f));
                uint _S619 = atlasWidth_4;
                uint _S620 = offset_4 + 1U;
                uint _S621 = _S620 % atlasWidth_4;
                int _S622 = int(_S621);
                uint _S623 = _S620 / _S619;
                int3 _S624 = int3(_S622, int(_S623), int(0));
                uint slot_8 = _S618 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S624)).xy), uint(((_S624)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S625 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S625)).xy), uint(((_S625)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S626 = atlasWidth_4;
                uint _S627 = 6U % atlasWidth_4;
                int _S628 = int(_S627);
                uint _S629 = 6U / _S626;
                int3 _S630 = int3(_S628, int(_S629), int(0));
                uint _S631 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S630)).xy), uint(((_S630)).z))).x * 255.0f));
                uint _S632 = atlasWidth_4;
                uint _S633 = 7U % atlasWidth_4;
                int _S634 = int(_S633);
                uint _S635 = 7U / _S632;
                int3 _S636 = int3(_S634, int(_S635), int(0));
                uint columns_4 = _S631 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S636)).xy), uint(((_S636)).z))).x * 255.0f)) << 8U);
                uint _S637 = atlasWidth_4;
                uint _S638 = 8U % atlasWidth_4;
                int _S639 = int(_S638);
                uint _S640 = 8U / _S637;
                int3 _S641 = int3(_S639, int(_S640), int(0));
                uint _S642 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S641)).xy), uint(((_S641)).z))).x * 255.0f));
                uint _S643 = atlasWidth_4;
                uint _S644 = 9U % atlasWidth_4;
                int _S645 = int(_S644);
                uint _S646 = 9U / _S643;
                int3 _S647 = int3(_S645, int(_S646), int(0));
                uint rows_4 = ((_S642 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S647)).xy), uint(((_S647)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S605 * _S611 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S648 = atlasWidth_4 / columns_4;
                float _S649 = float(_S648);
                uint _S650 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S649, float(_S650));
                uint _S651 = slot_9 % columns_4;
                float _S652 = float(_S651);
                uint _S653 = slot_9 / columns_4;
                _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S652, float(_S653)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(detailSource_0) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S654 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(detailSource_0)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S654);
            }
            if(hasSceneLighting_0)
            {
                int3 _S655 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S655)).xy), uint(((_S655)).z)));
                break;
            }
            uint _S656 = atlasWidth_4 / uint(_S654.x);
            float _S657 = float(_S656);
            uint _S658 = (atlasHeight_4 - 1U) / uint(_S654.y);
            float2 cellSize_9 = float2(_S657, float(_S658));
            _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S659 = float4(_S6->compositeControls_0) ;
            if((_S659.x) != 8.0f)
            {
                break;
            }
            bool _S660 = (_S659.w) >= 0.5f;
            if((maskU_1 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_1 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S661 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S660)
                {
                    for(;;)
                    {
                        if(_S661 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S662 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_9;
                        thread uint height_9;
                        (*((&width_9)) = (_S662).get_width(0)),(*((&height_9)) = (_S662).get_height(0));
                        float2 size_6 = float2(float(width_9), float(height_9));
                        float2 _S663 = float2(0.5f) ;
                        float2 remapped_6 = clamp((detailSource_0 * (size_6 - float2(2.0f) ) + float2(1.0f) ) / size_6, _S663 / size_6, (size_6 - _S663) / size_6);
                        if((_S661 & 1U) != 0U)
                        {
                            roughness_0 = remapped_6.x;
                        }
                        else
                        {
                            roughness_0 = detailSource_0.x;
                        }
                        if((_S661 & 2U) != 0U)
                        {
                            metallic_0 = remapped_6.y;
                        }
                        else
                        {
                            metallic_0 = detailSource_0.y;
                        }
                        detailSource_0 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S664 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S664).get_width(0)),(*((&atlasHeight_5)) = (_S664).get_height(0));
                int3 _S665 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S665)).xy), uint(((_S665)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S666 = atlasWidth_5;
                    uint _S667 = 2U % atlasWidth_5;
                    int _S668 = int(_S667);
                    uint _S669 = 2U / _S666;
                    int3 _S670 = int3(_S668, int(_S669), int(0));
                    int _S671 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S670)).xy), uint(((_S670)).z))).x * 255.0f)));
                    uint _S672 = atlasWidth_5;
                    uint _S673 = 3U % atlasWidth_5;
                    int _S674 = int(_S673);
                    uint _S675 = 3U / _S672;
                    int3 _S676 = int3(_S674, int(_S675), int(0));
                    int2 minimumTile_5 = int2(_S671, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S676)).xy), uint(((_S676)).z))).x * 255.0f))));
                    uint _S677 = atlasWidth_5;
                    uint _S678 = 4U % atlasWidth_5;
                    int _S679 = int(_S678);
                    uint _S680 = 4U / _S677;
                    int3 _S681 = int3(_S679, int(_S680), int(0));
                    uint _S682 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S681)).xy), uint(((_S681)).z))).x * 255.0f));
                    uint _S683 = atlasWidth_5;
                    uint _S684 = 5U % atlasWidth_5;
                    int _S685 = int(_S684);
                    uint _S686 = 5U / _S683;
                    int3 _S687 = int3(_S685, int(_S686), int(0));
                    uint _S688 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S687)).xy), uint(((_S687)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S682, _S688);
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
                        int3 _S689 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S689)).xy), uint(((_S689)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S682 + uint(tile_10.x));
                    uint _S690 = atlasWidth_5;
                    uint _S691 = offset_5 % atlasWidth_5;
                    int _S692 = int(_S691);
                    uint _S693 = offset_5 / _S690;
                    int3 _S694 = int3(_S692, int(_S693), int(0));
                    uint _S695 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S694)).xy), uint(((_S694)).z))).x * 255.0f));
                    uint _S696 = atlasWidth_5;
                    uint _S697 = offset_5 + 1U;
                    uint _S698 = _S697 % atlasWidth_5;
                    int _S699 = int(_S698);
                    uint _S700 = _S697 / _S696;
                    int3 _S701 = int3(_S699, int(_S700), int(0));
                    uint slot_10 = _S695 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S701)).xy), uint(((_S701)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S702 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S702)).xy), uint(((_S702)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S703 = atlasWidth_5;
                    uint _S704 = 6U % atlasWidth_5;
                    int _S705 = int(_S704);
                    uint _S706 = 6U / _S703;
                    int3 _S707 = int3(_S705, int(_S706), int(0));
                    uint _S708 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S707)).xy), uint(((_S707)).z))).x * 255.0f));
                    uint _S709 = atlasWidth_5;
                    uint _S710 = 7U % atlasWidth_5;
                    int _S711 = int(_S710);
                    uint _S712 = 7U / _S709;
                    int3 _S713 = int3(_S711, int(_S712), int(0));
                    uint columns_5 = _S708 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S713)).xy), uint(((_S713)).z))).x * 255.0f)) << 8U);
                    uint _S714 = atlasWidth_5;
                    uint _S715 = 8U % atlasWidth_5;
                    int _S716 = int(_S715);
                    uint _S717 = 8U / _S714;
                    int3 _S718 = int3(_S716, int(_S717), int(0));
                    uint _S719 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S718)).xy), uint(((_S718)).z))).x * 255.0f));
                    uint _S720 = atlasWidth_5;
                    uint _S721 = 9U % atlasWidth_5;
                    int _S722 = int(_S721);
                    uint _S723 = 9U / _S720;
                    int3 _S724 = int3(_S722, int(_S723), int(0));
                    uint rows_5 = ((_S719 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S724)).xy), uint(((_S724)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S682 * _S688 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S725 = atlasWidth_5 / columns_5;
                    float _S726 = float(_S725);
                    uint _S727 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S726, float(_S727));
                    uint _S728 = slot_11 % columns_5;
                    float _S729 = float(_S728);
                    uint _S730 = slot_11 / columns_5;
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S729, float(_S730)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(detailSource_0) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S731 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(detailSource_0)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S731);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S732 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S732)).xy), uint(((_S732)).z)));
                    break;
                }
                uint _S733 = atlasWidth_5 / uint(_S731.x);
                float _S734 = float(_S733);
                uint _S735 = (atlasHeight_5 - 1U) / uint(_S731.y);
                float2 cellSize_11 = float2(_S734, float(_S735));
                _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_1 = uint(round(_S659.y));
            if(operation_1 == 1U)
            {
                _S182 = _S182 * _S582;
                break;
            }
            if(operation_1 == 2U)
            {
                _S182 = _S182 + _S582;
                break;
            }
            if(operation_1 == 3U)
            {
                _S182 = _S182 - _S582;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S659.z;
                _S182 = _S182 * float4((1.0f - factor_1))  + _S582 * float4(factor_1) ;
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
    float occlusion_0;
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
        bool _S736 = (udimMask_0 & 32U) != 0U;
        if((maskU_1 & 32U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_1 & 32U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S737 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S736)
            {
                for(;;)
                {
                    if(_S737 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S738 = (&kernelContext_0)->metallicTexture_0;
                    thread uint width_10;
                    thread uint height_10;
                    (*((&width_10)) = (_S738).get_width(0)),(*((&height_10)) = (_S738).get_height(0));
                    float2 size_7 = float2(float(width_10), float(height_10));
                    float2 _S739 = float2(0.5f) ;
                    float2 remapped_7 = clamp((detailSource_0 * (size_7 - float2(2.0f) ) + float2(1.0f) ) / size_7, _S739 / size_7, (size_7 - _S739) / size_7);
                    if((_S737 & 1U) != 0U)
                    {
                        metallic_0 = remapped_7.x;
                    }
                    else
                    {
                        metallic_0 = detailSource_0.x;
                    }
                    if((_S737 & 2U) != 0U)
                    {
                        occlusion_0 = remapped_7.y;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(metallic_0, occlusion_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S740 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S740).get_width(0)),(*((&atlasHeight_6)) = (_S740).get_height(0));
            int3 _S741 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S741)).xy), uint(((_S741)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S742 = atlasWidth_6;
                uint _S743 = 2U % atlasWidth_6;
                int _S744 = int(_S743);
                uint _S745 = 2U / _S742;
                int3 _S746 = int3(_S744, int(_S745), int(0));
                int _S747 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S746)).xy), uint(((_S746)).z))).x * 255.0f)));
                uint _S748 = atlasWidth_6;
                uint _S749 = 3U % atlasWidth_6;
                int _S750 = int(_S749);
                uint _S751 = 3U / _S748;
                int3 _S752 = int3(_S750, int(_S751), int(0));
                int2 minimumTile_6 = int2(_S747, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S752)).xy), uint(((_S752)).z))).x * 255.0f))));
                uint _S753 = atlasWidth_6;
                uint _S754 = 4U % atlasWidth_6;
                int _S755 = int(_S754);
                uint _S756 = 4U / _S753;
                int3 _S757 = int3(_S755, int(_S756), int(0));
                uint _S758 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S757)).xy), uint(((_S757)).z))).x * 255.0f));
                uint _S759 = atlasWidth_6;
                uint _S760 = 5U % atlasWidth_6;
                int _S761 = int(_S760);
                uint _S762 = 5U / _S759;
                int3 _S763 = int3(_S761, int(_S762), int(0));
                uint _S764 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S763)).xy), uint(((_S763)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S758, _S764);
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
                    int3 _S765 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S765)).xy), uint(((_S765)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S758 + uint(tile_12.x));
                uint _S766 = atlasWidth_6;
                uint _S767 = offset_6 % atlasWidth_6;
                int _S768 = int(_S767);
                uint _S769 = offset_6 / _S766;
                int3 _S770 = int3(_S768, int(_S769), int(0));
                uint _S771 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S770)).xy), uint(((_S770)).z))).x * 255.0f));
                uint _S772 = atlasWidth_6;
                uint _S773 = offset_6 + 1U;
                uint _S774 = _S773 % atlasWidth_6;
                int _S775 = int(_S774);
                uint _S776 = _S773 / _S772;
                int3 _S777 = int3(_S775, int(_S776), int(0));
                uint slot_12 = _S771 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S777)).xy), uint(((_S777)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S778 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S778)).xy), uint(((_S778)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S779 = atlasWidth_6;
                uint _S780 = 6U % atlasWidth_6;
                int _S781 = int(_S780);
                uint _S782 = 6U / _S779;
                int3 _S783 = int3(_S781, int(_S782), int(0));
                uint _S784 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S783)).xy), uint(((_S783)).z))).x * 255.0f));
                uint _S785 = atlasWidth_6;
                uint _S786 = 7U % atlasWidth_6;
                int _S787 = int(_S786);
                uint _S788 = 7U / _S785;
                int3 _S789 = int3(_S787, int(_S788), int(0));
                uint columns_6 = _S784 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S789)).xy), uint(((_S789)).z))).x * 255.0f)) << 8U);
                uint _S790 = atlasWidth_6;
                uint _S791 = 8U % atlasWidth_6;
                int _S792 = int(_S791);
                uint _S793 = 8U / _S790;
                int3 _S794 = int3(_S792, int(_S793), int(0));
                uint _S795 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S794)).xy), uint(((_S794)).z))).x * 255.0f));
                uint _S796 = atlasWidth_6;
                uint _S797 = 9U % atlasWidth_6;
                int _S798 = int(_S797);
                uint _S799 = 9U / _S796;
                int3 _S800 = int3(_S798, int(_S799), int(0));
                uint rows_6 = ((_S795 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S800)).xy), uint(((_S800)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S758 * _S764 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S801 = atlasWidth_6 / columns_6;
                float _S802 = float(_S801);
                uint _S803 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S802, float(_S803));
                uint _S804 = slot_13 % columns_6;
                float _S805 = float(_S804);
                uint _S806 = slot_13 / columns_6;
                _S182 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S805, float(_S806)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(detailSource_0) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S807 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(detailSource_0)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S807);
            }
            if(hasSceneLighting_0)
            {
                int3 _S808 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S808)).xy), uint(((_S808)).z)));
                break;
            }
            uint _S809 = atlasWidth_6 / uint(_S807.x);
            float _S810 = float(_S809);
            uint _S811 = (atlasHeight_6 - 1U) / uint(_S807.y);
            float2 cellSize_13 = float2(_S810, float(_S811));
            _S182 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        for(;;)
        {
            float4 _S812 = float4(_S6->compositeControls_0) ;
            if((_S812.x) != 32.0f)
            {
                break;
            }
            bool _S813 = (_S812.w) >= 0.5f;
            if((maskU_1 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_1 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S814 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S813)
                {
                    for(;;)
                    {
                        if(_S814 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S815 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_11;
                        thread uint height_11;
                        (*((&width_11)) = (_S815).get_width(0)),(*((&height_11)) = (_S815).get_height(0));
                        float2 size_8 = float2(float(width_11), float(height_11));
                        float2 _S816 = float2(0.5f) ;
                        float2 remapped_8 = clamp((detailSource_0 * (size_8 - float2(2.0f) ) + float2(1.0f) ) / size_8, _S816 / size_8, (size_8 - _S816) / size_8);
                        if((_S814 & 1U) != 0U)
                        {
                            metallic_0 = remapped_8.x;
                        }
                        else
                        {
                            metallic_0 = detailSource_0.x;
                        }
                        if((_S814 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_8.y;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.y;
                        }
                        detailSource_0 = float2(metallic_0, occlusion_0);
                        break;
                    }
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S817 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S817).get_width(0)),(*((&atlasHeight_7)) = (_S817).get_height(0));
                int3 _S818 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S818)).xy), uint(((_S818)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S819 = atlasWidth_7;
                    uint _S820 = 2U % atlasWidth_7;
                    int _S821 = int(_S820);
                    uint _S822 = 2U / _S819;
                    int3 _S823 = int3(_S821, int(_S822), int(0));
                    int _S824 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S823)).xy), uint(((_S823)).z))).x * 255.0f)));
                    uint _S825 = atlasWidth_7;
                    uint _S826 = 3U % atlasWidth_7;
                    int _S827 = int(_S826);
                    uint _S828 = 3U / _S825;
                    int3 _S829 = int3(_S827, int(_S828), int(0));
                    int2 minimumTile_7 = int2(_S824, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S829)).xy), uint(((_S829)).z))).x * 255.0f))));
                    uint _S830 = atlasWidth_7;
                    uint _S831 = 4U % atlasWidth_7;
                    int _S832 = int(_S831);
                    uint _S833 = 4U / _S830;
                    int3 _S834 = int3(_S832, int(_S833), int(0));
                    uint _S835 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S834)).xy), uint(((_S834)).z))).x * 255.0f));
                    uint _S836 = atlasWidth_7;
                    uint _S837 = 5U % atlasWidth_7;
                    int _S838 = int(_S837);
                    uint _S839 = 5U / _S836;
                    int3 _S840 = int3(_S838, int(_S839), int(0));
                    uint _S841 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S840)).xy), uint(((_S840)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S835, _S841);
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
                        int3 _S842 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S842)).xy), uint(((_S842)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S835 + uint(tile_14.x));
                    uint _S843 = atlasWidth_7;
                    uint _S844 = offset_7 % atlasWidth_7;
                    int _S845 = int(_S844);
                    uint _S846 = offset_7 / _S843;
                    int3 _S847 = int3(_S845, int(_S846), int(0));
                    uint _S848 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S847)).xy), uint(((_S847)).z))).x * 255.0f));
                    uint _S849 = atlasWidth_7;
                    uint _S850 = offset_7 + 1U;
                    uint _S851 = _S850 % atlasWidth_7;
                    int _S852 = int(_S851);
                    uint _S853 = _S850 / _S849;
                    int3 _S854 = int3(_S852, int(_S853), int(0));
                    uint slot_14 = _S848 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S854)).xy), uint(((_S854)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S855 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S855)).xy), uint(((_S855)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S856 = atlasWidth_7;
                    uint _S857 = 6U % atlasWidth_7;
                    int _S858 = int(_S857);
                    uint _S859 = 6U / _S856;
                    int3 _S860 = int3(_S858, int(_S859), int(0));
                    uint _S861 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S860)).xy), uint(((_S860)).z))).x * 255.0f));
                    uint _S862 = atlasWidth_7;
                    uint _S863 = 7U % atlasWidth_7;
                    int _S864 = int(_S863);
                    uint _S865 = 7U / _S862;
                    int3 _S866 = int3(_S864, int(_S865), int(0));
                    uint columns_7 = _S861 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S866)).xy), uint(((_S866)).z))).x * 255.0f)) << 8U);
                    uint _S867 = atlasWidth_7;
                    uint _S868 = 8U % atlasWidth_7;
                    int _S869 = int(_S868);
                    uint _S870 = 8U / _S867;
                    int3 _S871 = int3(_S869, int(_S870), int(0));
                    uint _S872 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S871)).xy), uint(((_S871)).z))).x * 255.0f));
                    uint _S873 = atlasWidth_7;
                    uint _S874 = 9U % atlasWidth_7;
                    int _S875 = int(_S874);
                    uint _S876 = 9U / _S873;
                    int3 _S877 = int3(_S875, int(_S876), int(0));
                    uint rows_7 = ((_S872 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S877)).xy), uint(((_S877)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S835 * _S841 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S878 = atlasWidth_7 / columns_7;
                    float _S879 = float(_S878);
                    uint _S880 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S879, float(_S880));
                    uint _S881 = slot_15 % columns_7;
                    float _S882 = float(_S881);
                    uint _S883 = slot_15 / columns_7;
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S882, float(_S883)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(detailSource_0) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S884 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(detailSource_0)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S884);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S885 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S885)).xy), uint(((_S885)).z)));
                    break;
                }
                uint _S886 = atlasWidth_7 / uint(_S884.x);
                float _S887 = float(_S886);
                uint _S888 = (atlasHeight_7 - 1U) / uint(_S884.y);
                float2 cellSize_15 = float2(_S887, float(_S888));
                _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_2 = uint(round(_S812.y));
            if(operation_2 == 1U)
            {
                _S182 = _S182 * _S582;
                break;
            }
            if(operation_2 == 2U)
            {
                _S182 = _S182 + _S582;
                break;
            }
            if(operation_2 == 3U)
            {
                _S182 = _S182 - _S582;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S812.z;
                _S182 = _S182 * float4((1.0f - factor_2))  + _S582 * float4(factor_2) ;
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
    float clearcoatAmount_0;
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
        bool _S889 = (udimMask_0 & 16U) != 0U;
        bool _S890 = (maskU_1 & 16U) != 0U;
        if(_S890)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S891 = (maskV_1 & 16U) != 0U;
        if(_S891)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S892 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S889)
            {
                for(;;)
                {
                    if(_S892 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S893 = (&kernelContext_0)->emissiveTexture_0;
                    thread uint width_12;
                    thread uint height_12;
                    (*((&width_12)) = (_S893).get_width(0)),(*((&height_12)) = (_S893).get_height(0));
                    float2 size_9 = float2(float(width_12), float(height_12));
                    float2 _S894 = float2(0.5f) ;
                    float2 remapped_9 = clamp((detailSource_0 * (size_9 - float2(2.0f) ) + float2(1.0f) ) / size_9, _S894 / size_9, (size_9 - _S894) / size_9);
                    if((_S892 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_9.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S892 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_9.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S895 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S895).get_width(0)),(*((&atlasHeight_8)) = (_S895).get_height(0));
            int3 _S896 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S896)).xy), uint(((_S896)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S897 = atlasWidth_8;
                uint _S898 = 2U % atlasWidth_8;
                int _S899 = int(_S898);
                uint _S900 = 2U / _S897;
                int3 _S901 = int3(_S899, int(_S900), int(0));
                int _S902 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S901)).xy), uint(((_S901)).z))).x * 255.0f)));
                uint _S903 = atlasWidth_8;
                uint _S904 = 3U % atlasWidth_8;
                int _S905 = int(_S904);
                uint _S906 = 3U / _S903;
                int3 _S907 = int3(_S905, int(_S906), int(0));
                int2 minimumTile_8 = int2(_S902, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S907)).xy), uint(((_S907)).z))).x * 255.0f))));
                uint _S908 = atlasWidth_8;
                uint _S909 = 4U % atlasWidth_8;
                int _S910 = int(_S909);
                uint _S911 = 4U / _S908;
                int3 _S912 = int3(_S910, int(_S911), int(0));
                uint _S913 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S912)).xy), uint(((_S912)).z))).x * 255.0f));
                uint _S914 = atlasWidth_8;
                uint _S915 = 5U % atlasWidth_8;
                int _S916 = int(_S915);
                uint _S917 = 5U / _S914;
                int3 _S918 = int3(_S916, int(_S917), int(0));
                uint _S919 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S918)).xy), uint(((_S918)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S913, _S919);
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
                    int3 _S920 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S920)).xy), uint(((_S920)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S913 + uint(tile_16.x));
                uint _S921 = atlasWidth_8;
                uint _S922 = offset_8 % atlasWidth_8;
                int _S923 = int(_S922);
                uint _S924 = offset_8 / _S921;
                int3 _S925 = int3(_S923, int(_S924), int(0));
                uint _S926 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S925)).xy), uint(((_S925)).z))).x * 255.0f));
                uint _S927 = atlasWidth_8;
                uint _S928 = offset_8 + 1U;
                uint _S929 = _S928 % atlasWidth_8;
                int _S930 = int(_S929);
                uint _S931 = _S928 / _S927;
                int3 _S932 = int3(_S930, int(_S931), int(0));
                uint slot_16 = _S926 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S932)).xy), uint(((_S932)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S933 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S933)).xy), uint(((_S933)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S934 = atlasWidth_8;
                uint _S935 = 6U % atlasWidth_8;
                int _S936 = int(_S935);
                uint _S937 = 6U / _S934;
                int3 _S938 = int3(_S936, int(_S937), int(0));
                uint _S939 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S938)).xy), uint(((_S938)).z))).x * 255.0f));
                uint _S940 = atlasWidth_8;
                uint _S941 = 7U % atlasWidth_8;
                int _S942 = int(_S941);
                uint _S943 = 7U / _S940;
                int3 _S944 = int3(_S942, int(_S943), int(0));
                uint columns_8 = _S939 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S944)).xy), uint(((_S944)).z))).x * 255.0f)) << 8U);
                uint _S945 = atlasWidth_8;
                uint _S946 = 8U % atlasWidth_8;
                int _S947 = int(_S946);
                uint _S948 = 8U / _S945;
                int3 _S949 = int3(_S947, int(_S948), int(0));
                uint _S950 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S949)).xy), uint(((_S949)).z))).x * 255.0f));
                uint _S951 = atlasWidth_8;
                uint _S952 = 9U % atlasWidth_8;
                int _S953 = int(_S952);
                uint _S954 = 9U / _S951;
                int3 _S955 = int3(_S953, int(_S954), int(0));
                uint rows_8 = ((_S950 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S955)).xy), uint(((_S955)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S913 * _S919 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S956 = atlasWidth_8 / columns_8;
                float _S957 = float(_S956);
                uint _S958 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S957, float(_S958));
                uint _S959 = slot_17 % columns_8;
                float _S960 = float(_S959);
                uint _S961 = slot_17 / columns_8;
                _S182 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S960, float(_S961)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(detailSource_0) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S962 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(detailSource_0)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S962);
            }
            if(hasSceneLighting_0)
            {
                int3 _S963 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S963)).xy), uint(((_S963)).z)));
                break;
            }
            uint _S964 = atlasWidth_8 / uint(_S962.x);
            float _S965 = float(_S964);
            uint _S966 = (atlasHeight_8 - 1U) / uint(_S962.y);
            float2 cellSize_17 = float2(_S965, float(_S966));
            _S182 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        emissionSample_0 = _S182;
        if((_S174.w) != 0.0f)
        {
            texture2d<float, access::sample> _S967 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S967).get_width(0)),(*((&sourceHeight_1)) = (_S967).get_height(0));
            texture2d<float, access::sample> _S968 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S968).get_width(0)),(*((&valueHeight_1)) = (_S968).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S969 = emissionSample_0;
                for(;;)
                {
                    float _S970 = _S969.w;
                    if(_S970 > 1.0f)
                    {
                        unlitColor_0 = _S969.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S971 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_13;
                    thread uint height_13;
                    (*((&width_13)) = (_S971).get_width(0)),(*((&height_13)) = (_S971).get_height(0));
                    uint3 _S972 = uint3(round(_S969.xyz * float3(255.0f) ));
                    uint index_4 = ((_S972.x) | ((_S972.y) << 8U)) | ((_S972.z) << 16U);
                    if(_S970 == 0.0f)
                    {
                        _S183 = _S972;
                    }
                    else
                    {
                        _S183 = uint3(index_4, index_4, index_4);
                    }
                    uint3 address_3 = _S183 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S973 = address_3.x;
                    uint _S974 = _S973 % width_13;
                    int _S975 = int(_S974);
                    uint _S976 = _S973 / width_13;
                    int3 _S977 = int3(_S975, int(_S976), int(0));
                    float _S978 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S977)).xy), uint(((_S977)).z))).x;
                    uint _S979 = address_3.y;
                    uint _S980 = _S979 % width_13;
                    int _S981 = int(_S980);
                    uint _S982 = _S979 / width_13;
                    int3 _S983 = int3(_S981, int(_S982), int(0));
                    float _S984 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S983)).xy), uint(((_S983)).z))).x;
                    uint _S985 = address_3.z;
                    uint _S986 = _S985 % width_13;
                    int _S987 = int(_S986);
                    uint _S988 = _S985 / width_13;
                    int3 _S989 = int3(_S987, int(_S988), int(0));
                    unlitColor_0 = float3(_S978, _S984, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S989)).xy), uint(((_S989)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                if(_S890)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S891)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S990 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S990 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S991 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_14;
                    thread uint height_14;
                    (*((&width_14)) = (_S991).get_width(0)),(*((&height_14)) = (_S991).get_height(0));
                    float2 size_10 = float2(float(width_14), float(height_14));
                    float2 _S992 = float2(0.5f) ;
                    float2 remapped_10 = clamp((detailSource_0 * (size_10 - float2(2.0f) ) + float2(1.0f) ) / size_10, _S992 / size_10, (size_10 - _S992) / size_10);
                    if((_S990 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_10.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S990 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_10.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                emissionSample_0.z = (((&kernelContext_0)->emissiveValuesTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (tangent_1))).x;
            }
        }
        float4 _S993 = emissionSample_0;
        for(;;)
        {
            float4 _S994 = float4(_S6->compositeControls_0) ;
            if((_S994.x) != 16.0f)
            {
                _S182 = _S993;
                break;
            }
            bool _S995 = (_S994.w) >= 0.5f;
            if((maskU_1 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_1 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S996 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S995)
                {
                    for(;;)
                    {
                        if(_S996 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S997 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_15;
                        thread uint height_15;
                        (*((&width_15)) = (_S997).get_width(0)),(*((&height_15)) = (_S997).get_height(0));
                        float2 size_11 = float2(float(width_15), float(height_15));
                        float2 _S998 = float2(0.5f) ;
                        float2 remapped_11 = clamp((detailSource_0 * (size_11 - float2(2.0f) ) + float2(1.0f) ) / size_11, _S998 / size_11, (size_11 - _S998) / size_11);
                        if((_S996 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_11.x;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.x;
                        }
                        if((_S996 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_11.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.y;
                        }
                        detailSource_0 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S999 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S999).get_width(0)),(*((&atlasHeight_9)) = (_S999).get_height(0));
                int3 _S1000 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1000)).xy), uint(((_S1000)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S1001 = atlasWidth_9;
                    uint _S1002 = 2U % atlasWidth_9;
                    int _S1003 = int(_S1002);
                    uint _S1004 = 2U / _S1001;
                    int3 _S1005 = int3(_S1003, int(_S1004), int(0));
                    int _S1006 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1005)).xy), uint(((_S1005)).z))).x * 255.0f)));
                    uint _S1007 = atlasWidth_9;
                    uint _S1008 = 3U % atlasWidth_9;
                    int _S1009 = int(_S1008);
                    uint _S1010 = 3U / _S1007;
                    int3 _S1011 = int3(_S1009, int(_S1010), int(0));
                    int2 minimumTile_9 = int2(_S1006, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1011)).xy), uint(((_S1011)).z))).x * 255.0f))));
                    uint _S1012 = atlasWidth_9;
                    uint _S1013 = 4U % atlasWidth_9;
                    int _S1014 = int(_S1013);
                    uint _S1015 = 4U / _S1012;
                    int3 _S1016 = int3(_S1014, int(_S1015), int(0));
                    uint _S1017 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1016)).xy), uint(((_S1016)).z))).x * 255.0f));
                    uint _S1018 = atlasWidth_9;
                    uint _S1019 = 5U % atlasWidth_9;
                    int _S1020 = int(_S1019);
                    uint _S1021 = 5U / _S1018;
                    int3 _S1022 = int3(_S1020, int(_S1021), int(0));
                    uint _S1023 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1022)).xy), uint(((_S1022)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S1017, _S1023);
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
                        int3 _S1024 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1024)).xy), uint(((_S1024)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S1017 + uint(tile_18.x));
                    uint _S1025 = atlasWidth_9;
                    uint _S1026 = offset_9 % atlasWidth_9;
                    int _S1027 = int(_S1026);
                    uint _S1028 = offset_9 / _S1025;
                    int3 _S1029 = int3(_S1027, int(_S1028), int(0));
                    uint _S1030 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1029)).xy), uint(((_S1029)).z))).x * 255.0f));
                    uint _S1031 = atlasWidth_9;
                    uint _S1032 = offset_9 + 1U;
                    uint _S1033 = _S1032 % atlasWidth_9;
                    int _S1034 = int(_S1033);
                    uint _S1035 = _S1032 / _S1031;
                    int3 _S1036 = int3(_S1034, int(_S1035), int(0));
                    uint slot_18 = _S1030 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1036)).xy), uint(((_S1036)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S1037 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1037)).xy), uint(((_S1037)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S1038 = atlasWidth_9;
                    uint _S1039 = 6U % atlasWidth_9;
                    int _S1040 = int(_S1039);
                    uint _S1041 = 6U / _S1038;
                    int3 _S1042 = int3(_S1040, int(_S1041), int(0));
                    uint _S1043 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1042)).xy), uint(((_S1042)).z))).x * 255.0f));
                    uint _S1044 = atlasWidth_9;
                    uint _S1045 = 7U % atlasWidth_9;
                    int _S1046 = int(_S1045);
                    uint _S1047 = 7U / _S1044;
                    int3 _S1048 = int3(_S1046, int(_S1047), int(0));
                    uint columns_9 = _S1043 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1048)).xy), uint(((_S1048)).z))).x * 255.0f)) << 8U);
                    uint _S1049 = atlasWidth_9;
                    uint _S1050 = 8U % atlasWidth_9;
                    int _S1051 = int(_S1050);
                    uint _S1052 = 8U / _S1049;
                    int3 _S1053 = int3(_S1051, int(_S1052), int(0));
                    uint _S1054 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1053)).xy), uint(((_S1053)).z))).x * 255.0f));
                    uint _S1055 = atlasWidth_9;
                    uint _S1056 = 9U % atlasWidth_9;
                    int _S1057 = int(_S1056);
                    uint _S1058 = 9U / _S1055;
                    int3 _S1059 = int3(_S1057, int(_S1058), int(0));
                    uint rows_9 = ((_S1054 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1059)).xy), uint(((_S1059)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S1017 * _S1023 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S1060 = atlasWidth_9 / columns_9;
                    float _S1061 = float(_S1060);
                    uint _S1062 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S1061, float(_S1062));
                    uint _S1063 = slot_19 % columns_9;
                    float _S1064 = float(_S1063);
                    uint _S1065 = slot_19 / columns_9;
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1064, float(_S1065)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(detailSource_0) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S1066 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(detailSource_0)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S1066);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1067 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1067)).xy), uint(((_S1067)).z)));
                    break;
                }
                uint _S1068 = atlasWidth_9 / uint(_S1066.x);
                float _S1069 = float(_S1068);
                uint _S1070 = (atlasHeight_9 - 1U) / uint(_S1066.y);
                float2 cellSize_19 = float2(_S1069, float(_S1070));
                _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_3 = uint(round(_S994.y));
            if(operation_3 == 1U)
            {
                _S182 = _S993 * _S182;
                break;
            }
            if(operation_3 == 2U)
            {
                _S182 = _S993 + _S182;
                break;
            }
            if(operation_3 == 3U)
            {
                _S182 = _S993 - _S182;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S994.z;
                _S182 = _S993 * float4((1.0f - factor_3))  + _S182 * float4(factor_3) ;
                break;
            }
            _S182 = _S993;
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
        bool _S1071 = (udimMask_0 & 64U) != 0U;
        if((maskU_1 & 64U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_1 & 64U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1072 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1071)
            {
                for(;;)
                {
                    if(_S1072 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1073 = (&kernelContext_0)->opacityTexture_0;
                    thread uint width_16;
                    thread uint height_16;
                    (*((&width_16)) = (_S1073).get_width(0)),(*((&height_16)) = (_S1073).get_height(0));
                    float2 size_12 = float2(float(width_16), float(height_16));
                    float2 _S1074 = float2(0.5f) ;
                    float2 remapped_12 = clamp((detailSource_0 * (size_12 - float2(2.0f) ) + float2(1.0f) ) / size_12, _S1074 / size_12, (size_12 - _S1074) / size_12);
                    if((_S1072 & 1U) != 0U)
                    {
                        opacity_0 = remapped_12.x;
                    }
                    else
                    {
                        opacity_0 = detailSource_0.x;
                    }
                    if((_S1072 & 2U) != 0U)
                    {
                        occlusion_0 = remapped_12.y;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(opacity_0, occlusion_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1075 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S1075).get_width(0)),(*((&atlasHeight_10)) = (_S1075).get_height(0));
            int3 _S1076 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1076)).xy), uint(((_S1076)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S1077 = atlasWidth_10;
                uint _S1078 = 2U % atlasWidth_10;
                int _S1079 = int(_S1078);
                uint _S1080 = 2U / _S1077;
                int3 _S1081 = int3(_S1079, int(_S1080), int(0));
                int _S1082 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1081)).xy), uint(((_S1081)).z))).x * 255.0f)));
                uint _S1083 = atlasWidth_10;
                uint _S1084 = 3U % atlasWidth_10;
                int _S1085 = int(_S1084);
                uint _S1086 = 3U / _S1083;
                int3 _S1087 = int3(_S1085, int(_S1086), int(0));
                int2 minimumTile_10 = int2(_S1082, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1087)).xy), uint(((_S1087)).z))).x * 255.0f))));
                uint _S1088 = atlasWidth_10;
                uint _S1089 = 4U % atlasWidth_10;
                int _S1090 = int(_S1089);
                uint _S1091 = 4U / _S1088;
                int3 _S1092 = int3(_S1090, int(_S1091), int(0));
                uint _S1093 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1092)).xy), uint(((_S1092)).z))).x * 255.0f));
                uint _S1094 = atlasWidth_10;
                uint _S1095 = 5U % atlasWidth_10;
                int _S1096 = int(_S1095);
                uint _S1097 = 5U / _S1094;
                int3 _S1098 = int3(_S1096, int(_S1097), int(0));
                uint _S1099 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1098)).xy), uint(((_S1098)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S1093, _S1099);
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
                    int3 _S1100 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1100)).xy), uint(((_S1100)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1093 + uint(tile_20.x));
                uint _S1101 = atlasWidth_10;
                uint _S1102 = offset_10 % atlasWidth_10;
                int _S1103 = int(_S1102);
                uint _S1104 = offset_10 / _S1101;
                int3 _S1105 = int3(_S1103, int(_S1104), int(0));
                uint _S1106 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1105)).xy), uint(((_S1105)).z))).x * 255.0f));
                uint _S1107 = atlasWidth_10;
                uint _S1108 = offset_10 + 1U;
                uint _S1109 = _S1108 % atlasWidth_10;
                int _S1110 = int(_S1109);
                uint _S1111 = _S1108 / _S1107;
                int3 _S1112 = int3(_S1110, int(_S1111), int(0));
                uint slot_20 = _S1106 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1112)).xy), uint(((_S1112)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1113 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1113)).xy), uint(((_S1113)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1114 = atlasWidth_10;
                uint _S1115 = 6U % atlasWidth_10;
                int _S1116 = int(_S1115);
                uint _S1117 = 6U / _S1114;
                int3 _S1118 = int3(_S1116, int(_S1117), int(0));
                uint _S1119 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1118)).xy), uint(((_S1118)).z))).x * 255.0f));
                uint _S1120 = atlasWidth_10;
                uint _S1121 = 7U % atlasWidth_10;
                int _S1122 = int(_S1121);
                uint _S1123 = 7U / _S1120;
                int3 _S1124 = int3(_S1122, int(_S1123), int(0));
                uint columns_10 = _S1119 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1124)).xy), uint(((_S1124)).z))).x * 255.0f)) << 8U);
                uint _S1125 = atlasWidth_10;
                uint _S1126 = 8U % atlasWidth_10;
                int _S1127 = int(_S1126);
                uint _S1128 = 8U / _S1125;
                int3 _S1129 = int3(_S1127, int(_S1128), int(0));
                uint _S1130 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1129)).xy), uint(((_S1129)).z))).x * 255.0f));
                uint _S1131 = atlasWidth_10;
                uint _S1132 = 9U % atlasWidth_10;
                int _S1133 = int(_S1132);
                uint _S1134 = 9U / _S1131;
                int3 _S1135 = int3(_S1133, int(_S1134), int(0));
                uint rows_10 = ((_S1130 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1135)).xy), uint(((_S1135)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S1093 * _S1099 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1136 = atlasWidth_10 / columns_10;
                float _S1137 = float(_S1136);
                uint _S1138 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1137, float(_S1138));
                uint _S1139 = slot_21 % columns_10;
                float _S1140 = float(_S1139);
                uint _S1141 = slot_21 / columns_10;
                _S182 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S1140, float(_S1141)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(detailSource_0) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1142 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(detailSource_0)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1142);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1143 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1143)).xy), uint(((_S1143)).z)));
                break;
            }
            uint _S1144 = atlasWidth_10 / uint(_S1142.x);
            float _S1145 = float(_S1144);
            uint _S1146 = (atlasHeight_10 - 1U) / uint(_S1142.y);
            float2 cellSize_21 = float2(_S1145, float(_S1146));
            _S182 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1147 = float4(_S6->compositeControls_0) ;
            if((_S1147.x) != 64.0f)
            {
                break;
            }
            bool _S1148 = (_S1147.w) >= 0.5f;
            if((maskU_1 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_1 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1149 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1148)
                {
                    for(;;)
                    {
                        if(_S1149 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1150 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_17;
                        thread uint height_17;
                        (*((&width_17)) = (_S1150).get_width(0)),(*((&height_17)) = (_S1150).get_height(0));
                        float2 size_13 = float2(float(width_17), float(height_17));
                        float2 _S1151 = float2(0.5f) ;
                        float2 remapped_13 = clamp((detailSource_0 * (size_13 - float2(2.0f) ) + float2(1.0f) ) / size_13, _S1151 / size_13, (size_13 - _S1151) / size_13);
                        if((_S1149 & 1U) != 0U)
                        {
                            opacity_0 = remapped_13.x;
                        }
                        else
                        {
                            opacity_0 = detailSource_0.x;
                        }
                        if((_S1149 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_13.y;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.y;
                        }
                        detailSource_0 = float2(opacity_0, occlusion_0);
                        break;
                    }
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1152 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1152).get_width(0)),(*((&atlasHeight_11)) = (_S1152).get_height(0));
                int3 _S1153 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1153)).xy), uint(((_S1153)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1154 = atlasWidth_11;
                    uint _S1155 = 2U % atlasWidth_11;
                    int _S1156 = int(_S1155);
                    uint _S1157 = 2U / _S1154;
                    int3 _S1158 = int3(_S1156, int(_S1157), int(0));
                    int _S1159 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1158)).xy), uint(((_S1158)).z))).x * 255.0f)));
                    uint _S1160 = atlasWidth_11;
                    uint _S1161 = 3U % atlasWidth_11;
                    int _S1162 = int(_S1161);
                    uint _S1163 = 3U / _S1160;
                    int3 _S1164 = int3(_S1162, int(_S1163), int(0));
                    int2 minimumTile_11 = int2(_S1159, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1164)).xy), uint(((_S1164)).z))).x * 255.0f))));
                    uint _S1165 = atlasWidth_11;
                    uint _S1166 = 4U % atlasWidth_11;
                    int _S1167 = int(_S1166);
                    uint _S1168 = 4U / _S1165;
                    int3 _S1169 = int3(_S1167, int(_S1168), int(0));
                    uint _S1170 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1169)).xy), uint(((_S1169)).z))).x * 255.0f));
                    uint _S1171 = atlasWidth_11;
                    uint _S1172 = 5U % atlasWidth_11;
                    int _S1173 = int(_S1172);
                    uint _S1174 = 5U / _S1171;
                    int3 _S1175 = int3(_S1173, int(_S1174), int(0));
                    uint _S1176 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1175)).xy), uint(((_S1175)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1170, _S1176);
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
                        int3 _S1177 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1177)).xy), uint(((_S1177)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1170 + uint(tile_22.x));
                    uint _S1178 = atlasWidth_11;
                    uint _S1179 = offset_11 % atlasWidth_11;
                    int _S1180 = int(_S1179);
                    uint _S1181 = offset_11 / _S1178;
                    int3 _S1182 = int3(_S1180, int(_S1181), int(0));
                    uint _S1183 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1182)).xy), uint(((_S1182)).z))).x * 255.0f));
                    uint _S1184 = atlasWidth_11;
                    uint _S1185 = offset_11 + 1U;
                    uint _S1186 = _S1185 % atlasWidth_11;
                    int _S1187 = int(_S1186);
                    uint _S1188 = _S1185 / _S1184;
                    int3 _S1189 = int3(_S1187, int(_S1188), int(0));
                    uint slot_22 = _S1183 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1189)).xy), uint(((_S1189)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1190 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1190)).xy), uint(((_S1190)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1191 = atlasWidth_11;
                    uint _S1192 = 6U % atlasWidth_11;
                    int _S1193 = int(_S1192);
                    uint _S1194 = 6U / _S1191;
                    int3 _S1195 = int3(_S1193, int(_S1194), int(0));
                    uint _S1196 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1195)).xy), uint(((_S1195)).z))).x * 255.0f));
                    uint _S1197 = atlasWidth_11;
                    uint _S1198 = 7U % atlasWidth_11;
                    int _S1199 = int(_S1198);
                    uint _S1200 = 7U / _S1197;
                    int3 _S1201 = int3(_S1199, int(_S1200), int(0));
                    uint columns_11 = _S1196 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1201)).xy), uint(((_S1201)).z))).x * 255.0f)) << 8U);
                    uint _S1202 = atlasWidth_11;
                    uint _S1203 = 8U % atlasWidth_11;
                    int _S1204 = int(_S1203);
                    uint _S1205 = 8U / _S1202;
                    int3 _S1206 = int3(_S1204, int(_S1205), int(0));
                    uint _S1207 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1206)).xy), uint(((_S1206)).z))).x * 255.0f));
                    uint _S1208 = atlasWidth_11;
                    uint _S1209 = 9U % atlasWidth_11;
                    int _S1210 = int(_S1209);
                    uint _S1211 = 9U / _S1208;
                    int3 _S1212 = int3(_S1210, int(_S1211), int(0));
                    uint rows_11 = ((_S1207 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1212)).xy), uint(((_S1212)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1170 * _S1176 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1213 = atlasWidth_11 / columns_11;
                    float _S1214 = float(_S1213);
                    uint _S1215 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1214, float(_S1215));
                    uint _S1216 = slot_23 % columns_11;
                    float _S1217 = float(_S1216);
                    uint _S1218 = slot_23 / columns_11;
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1217, float(_S1218)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(detailSource_0) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1219 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(detailSource_0)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1219);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1220 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1220)).xy), uint(((_S1220)).z)));
                    break;
                }
                uint _S1221 = atlasWidth_11 / uint(_S1219.x);
                float _S1222 = float(_S1221);
                uint _S1223 = (atlasHeight_11 - 1U) / uint(_S1219.y);
                float2 cellSize_23 = float2(_S1222, float(_S1223));
                _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_4 = uint(round(_S1147.y));
            if(operation_4 == 1U)
            {
                _S182 = _S182 * _S582;
                break;
            }
            if(operation_4 == 2U)
            {
                _S182 = _S182 + _S582;
                break;
            }
            if(operation_4 == 3U)
            {
                _S182 = _S182 - _S582;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S1147.z;
                _S182 = _S182 * float4((1.0f - factor_4))  + _S582 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S182.x);
    }
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
        bool _S1224 = (udimMask_0 & 128U) != 0U;
        if((maskU_1 & 128U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_1 & 128U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1225 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1224)
            {
                for(;;)
                {
                    if(_S1225 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1226 = (&kernelContext_0)->occlusionTexture_0;
                    thread uint width_18;
                    thread uint height_18;
                    (*((&width_18)) = (_S1226).get_width(0)),(*((&height_18)) = (_S1226).get_height(0));
                    float2 size_14 = float2(float(width_18), float(height_18));
                    float2 _S1227 = float2(0.5f) ;
                    float2 remapped_14 = clamp((detailSource_0 * (size_14 - float2(2.0f) ) + float2(1.0f) ) / size_14, _S1227 / size_14, (size_14 - _S1227) / size_14);
                    if((_S1225 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_14.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S1225 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_14.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1228 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1228).get_width(0)),(*((&atlasHeight_12)) = (_S1228).get_height(0));
            int3 _S1229 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1229)).xy), uint(((_S1229)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1230 = atlasWidth_12;
                uint _S1231 = 2U % atlasWidth_12;
                int _S1232 = int(_S1231);
                uint _S1233 = 2U / _S1230;
                int3 _S1234 = int3(_S1232, int(_S1233), int(0));
                int _S1235 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1234)).xy), uint(((_S1234)).z))).x * 255.0f)));
                uint _S1236 = atlasWidth_12;
                uint _S1237 = 3U % atlasWidth_12;
                int _S1238 = int(_S1237);
                uint _S1239 = 3U / _S1236;
                int3 _S1240 = int3(_S1238, int(_S1239), int(0));
                int2 minimumTile_12 = int2(_S1235, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1240)).xy), uint(((_S1240)).z))).x * 255.0f))));
                uint _S1241 = atlasWidth_12;
                uint _S1242 = 4U % atlasWidth_12;
                int _S1243 = int(_S1242);
                uint _S1244 = 4U / _S1241;
                int3 _S1245 = int3(_S1243, int(_S1244), int(0));
                uint _S1246 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1245)).xy), uint(((_S1245)).z))).x * 255.0f));
                uint _S1247 = atlasWidth_12;
                uint _S1248 = 5U % atlasWidth_12;
                int _S1249 = int(_S1248);
                uint _S1250 = 5U / _S1247;
                int3 _S1251 = int3(_S1249, int(_S1250), int(0));
                uint _S1252 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1251)).xy), uint(((_S1251)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1246, _S1252);
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
                    int3 _S1253 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1253)).xy), uint(((_S1253)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1246 + uint(tile_24.x));
                uint _S1254 = atlasWidth_12;
                uint _S1255 = offset_12 % atlasWidth_12;
                int _S1256 = int(_S1255);
                uint _S1257 = offset_12 / _S1254;
                int3 _S1258 = int3(_S1256, int(_S1257), int(0));
                uint _S1259 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1258)).xy), uint(((_S1258)).z))).x * 255.0f));
                uint _S1260 = atlasWidth_12;
                uint _S1261 = offset_12 + 1U;
                uint _S1262 = _S1261 % atlasWidth_12;
                int _S1263 = int(_S1262);
                uint _S1264 = _S1261 / _S1260;
                int3 _S1265 = int3(_S1263, int(_S1264), int(0));
                uint slot_24 = _S1259 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1265)).xy), uint(((_S1265)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1266 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1266)).xy), uint(((_S1266)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1267 = atlasWidth_12;
                uint _S1268 = 6U % atlasWidth_12;
                int _S1269 = int(_S1268);
                uint _S1270 = 6U / _S1267;
                int3 _S1271 = int3(_S1269, int(_S1270), int(0));
                uint _S1272 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1271)).xy), uint(((_S1271)).z))).x * 255.0f));
                uint _S1273 = atlasWidth_12;
                uint _S1274 = 7U % atlasWidth_12;
                int _S1275 = int(_S1274);
                uint _S1276 = 7U / _S1273;
                int3 _S1277 = int3(_S1275, int(_S1276), int(0));
                uint columns_12 = _S1272 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1277)).xy), uint(((_S1277)).z))).x * 255.0f)) << 8U);
                uint _S1278 = atlasWidth_12;
                uint _S1279 = 8U % atlasWidth_12;
                int _S1280 = int(_S1279);
                uint _S1281 = 8U / _S1278;
                int3 _S1282 = int3(_S1280, int(_S1281), int(0));
                uint _S1283 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1282)).xy), uint(((_S1282)).z))).x * 255.0f));
                uint _S1284 = atlasWidth_12;
                uint _S1285 = 9U % atlasWidth_12;
                int _S1286 = int(_S1285);
                uint _S1287 = 9U / _S1284;
                int3 _S1288 = int3(_S1286, int(_S1287), int(0));
                uint rows_12 = ((_S1283 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1288)).xy), uint(((_S1288)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1246 * _S1252 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1289 = atlasWidth_12 / columns_12;
                float _S1290 = float(_S1289);
                uint _S1291 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1290, float(_S1291));
                uint _S1292 = slot_25 % columns_12;
                float _S1293 = float(_S1292);
                uint _S1294 = slot_25 / columns_12;
                _S182 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1293, float(_S1294)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(detailSource_0) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1295 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(detailSource_0)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1295);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1296 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1296)).xy), uint(((_S1296)).z)));
                break;
            }
            uint _S1297 = atlasWidth_12 / uint(_S1295.x);
            float _S1298 = float(_S1297);
            uint _S1299 = (atlasHeight_12 - 1U) / uint(_S1295.y);
            float2 cellSize_25 = float2(_S1298, float(_S1299));
            _S182 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1300 = float4(_S6->compositeControls_0) ;
            if((_S1300.x) != 128.0f)
            {
                break;
            }
            bool _S1301 = (_S1300.w) >= 0.5f;
            if((maskU_1 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_1 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1302 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1301)
                {
                    for(;;)
                    {
                        if(_S1302 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1303 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_19;
                        thread uint height_19;
                        (*((&width_19)) = (_S1303).get_width(0)),(*((&height_19)) = (_S1303).get_height(0));
                        float2 size_15 = float2(float(width_19), float(height_19));
                        float2 _S1304 = float2(0.5f) ;
                        float2 remapped_15 = clamp((detailSource_0 * (size_15 - float2(2.0f) ) + float2(1.0f) ) / size_15, _S1304 / size_15, (size_15 - _S1304) / size_15);
                        if((_S1302 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_15.x;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.x;
                        }
                        if((_S1302 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_15.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.y;
                        }
                        detailSource_0 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1305 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1305).get_width(0)),(*((&atlasHeight_13)) = (_S1305).get_height(0));
                int3 _S1306 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1306)).xy), uint(((_S1306)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1307 = atlasWidth_13;
                    uint _S1308 = 2U % atlasWidth_13;
                    int _S1309 = int(_S1308);
                    uint _S1310 = 2U / _S1307;
                    int3 _S1311 = int3(_S1309, int(_S1310), int(0));
                    int _S1312 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1311)).xy), uint(((_S1311)).z))).x * 255.0f)));
                    uint _S1313 = atlasWidth_13;
                    uint _S1314 = 3U % atlasWidth_13;
                    int _S1315 = int(_S1314);
                    uint _S1316 = 3U / _S1313;
                    int3 _S1317 = int3(_S1315, int(_S1316), int(0));
                    int2 minimumTile_13 = int2(_S1312, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1317)).xy), uint(((_S1317)).z))).x * 255.0f))));
                    uint _S1318 = atlasWidth_13;
                    uint _S1319 = 4U % atlasWidth_13;
                    int _S1320 = int(_S1319);
                    uint _S1321 = 4U / _S1318;
                    int3 _S1322 = int3(_S1320, int(_S1321), int(0));
                    uint _S1323 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1322)).xy), uint(((_S1322)).z))).x * 255.0f));
                    uint _S1324 = atlasWidth_13;
                    uint _S1325 = 5U % atlasWidth_13;
                    int _S1326 = int(_S1325);
                    uint _S1327 = 5U / _S1324;
                    int3 _S1328 = int3(_S1326, int(_S1327), int(0));
                    uint _S1329 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1328)).xy), uint(((_S1328)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1323, _S1329);
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
                        int3 _S1330 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1330)).xy), uint(((_S1330)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1323 + uint(tile_26.x));
                    uint _S1331 = atlasWidth_13;
                    uint _S1332 = offset_13 % atlasWidth_13;
                    int _S1333 = int(_S1332);
                    uint _S1334 = offset_13 / _S1331;
                    int3 _S1335 = int3(_S1333, int(_S1334), int(0));
                    uint _S1336 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1335)).xy), uint(((_S1335)).z))).x * 255.0f));
                    uint _S1337 = atlasWidth_13;
                    uint _S1338 = offset_13 + 1U;
                    uint _S1339 = _S1338 % atlasWidth_13;
                    int _S1340 = int(_S1339);
                    uint _S1341 = _S1338 / _S1337;
                    int3 _S1342 = int3(_S1340, int(_S1341), int(0));
                    uint slot_26 = _S1336 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1342)).xy), uint(((_S1342)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1343 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1343)).xy), uint(((_S1343)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1344 = atlasWidth_13;
                    uint _S1345 = 6U % atlasWidth_13;
                    int _S1346 = int(_S1345);
                    uint _S1347 = 6U / _S1344;
                    int3 _S1348 = int3(_S1346, int(_S1347), int(0));
                    uint _S1349 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1348)).xy), uint(((_S1348)).z))).x * 255.0f));
                    uint _S1350 = atlasWidth_13;
                    uint _S1351 = 7U % atlasWidth_13;
                    int _S1352 = int(_S1351);
                    uint _S1353 = 7U / _S1350;
                    int3 _S1354 = int3(_S1352, int(_S1353), int(0));
                    uint columns_13 = _S1349 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1354)).xy), uint(((_S1354)).z))).x * 255.0f)) << 8U);
                    uint _S1355 = atlasWidth_13;
                    uint _S1356 = 8U % atlasWidth_13;
                    int _S1357 = int(_S1356);
                    uint _S1358 = 8U / _S1355;
                    int3 _S1359 = int3(_S1357, int(_S1358), int(0));
                    uint _S1360 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1359)).xy), uint(((_S1359)).z))).x * 255.0f));
                    uint _S1361 = atlasWidth_13;
                    uint _S1362 = 9U % atlasWidth_13;
                    int _S1363 = int(_S1362);
                    uint _S1364 = 9U / _S1361;
                    int3 _S1365 = int3(_S1363, int(_S1364), int(0));
                    uint rows_13 = ((_S1360 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1365)).xy), uint(((_S1365)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1323 * _S1329 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1366 = atlasWidth_13 / columns_13;
                    float _S1367 = float(_S1366);
                    uint _S1368 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1367, float(_S1368));
                    uint _S1369 = slot_27 % columns_13;
                    float _S1370 = float(_S1369);
                    uint _S1371 = slot_27 / columns_13;
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1370, float(_S1371)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(detailSource_0) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1372 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(detailSource_0)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1372);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1373 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1373)).xy), uint(((_S1373)).z)));
                    break;
                }
                uint _S1374 = atlasWidth_13 / uint(_S1372.x);
                float _S1375 = float(_S1374);
                uint _S1376 = (atlasHeight_13 - 1U) / uint(_S1372.y);
                float2 cellSize_27 = float2(_S1375, float(_S1376));
                _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_5 = uint(round(_S1300.y));
            if(operation_5 == 1U)
            {
                _S182 = _S182 * _S582;
                break;
            }
            if(operation_5 == 2U)
            {
                _S182 = _S182 + _S582;
                break;
            }
            if(operation_5 == 3U)
            {
                _S182 = _S182 - _S582;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1300.z;
                _S182 = _S182 * float4((1.0f - factor_5))  + _S582 * float4(factor_5) ;
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
    float clearcoatRoughness_0;
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
        bool _S1377 = (udimMask_0 & 256U) != 0U;
        if((maskU_1 & 256U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_1 & 256U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1378 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1377)
            {
                for(;;)
                {
                    if(_S1378 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1379 = (&kernelContext_0)->specularColorTexture_0;
                    thread uint width_20;
                    thread uint height_20;
                    (*((&width_20)) = (_S1379).get_width(0)),(*((&height_20)) = (_S1379).get_height(0));
                    float2 size_16 = float2(float(width_20), float(height_20));
                    float2 _S1380 = float2(0.5f) ;
                    float2 remapped_16 = clamp((detailSource_0 * (size_16 - float2(2.0f) ) + float2(1.0f) ) / size_16, _S1380 / size_16, (size_16 - _S1380) / size_16);
                    if((_S1378 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_16.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.x;
                    }
                    if((_S1378 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_16.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1381 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1381).get_width(0)),(*((&atlasHeight_14)) = (_S1381).get_height(0));
            int3 _S1382 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1382)).xy), uint(((_S1382)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1383 = atlasWidth_14;
                uint _S1384 = 2U % atlasWidth_14;
                int _S1385 = int(_S1384);
                uint _S1386 = 2U / _S1383;
                int3 _S1387 = int3(_S1385, int(_S1386), int(0));
                int _S1388 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1387)).xy), uint(((_S1387)).z))).x * 255.0f)));
                uint _S1389 = atlasWidth_14;
                uint _S1390 = 3U % atlasWidth_14;
                int _S1391 = int(_S1390);
                uint _S1392 = 3U / _S1389;
                int3 _S1393 = int3(_S1391, int(_S1392), int(0));
                int2 minimumTile_14 = int2(_S1388, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1393)).xy), uint(((_S1393)).z))).x * 255.0f))));
                uint _S1394 = atlasWidth_14;
                uint _S1395 = 4U % atlasWidth_14;
                int _S1396 = int(_S1395);
                uint _S1397 = 4U / _S1394;
                int3 _S1398 = int3(_S1396, int(_S1397), int(0));
                uint _S1399 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1398)).xy), uint(((_S1398)).z))).x * 255.0f));
                uint _S1400 = atlasWidth_14;
                uint _S1401 = 5U % atlasWidth_14;
                int _S1402 = int(_S1401);
                uint _S1403 = 5U / _S1400;
                int3 _S1404 = int3(_S1402, int(_S1403), int(0));
                uint _S1405 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1404)).xy), uint(((_S1404)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1399, _S1405);
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
                    int3 _S1406 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1406)).xy), uint(((_S1406)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1399 + uint(tile_28.x));
                uint _S1407 = atlasWidth_14;
                uint _S1408 = offset_14 % atlasWidth_14;
                int _S1409 = int(_S1408);
                uint _S1410 = offset_14 / _S1407;
                int3 _S1411 = int3(_S1409, int(_S1410), int(0));
                uint _S1412 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1411)).xy), uint(((_S1411)).z))).x * 255.0f));
                uint _S1413 = atlasWidth_14;
                uint _S1414 = offset_14 + 1U;
                uint _S1415 = _S1414 % atlasWidth_14;
                int _S1416 = int(_S1415);
                uint _S1417 = _S1414 / _S1413;
                int3 _S1418 = int3(_S1416, int(_S1417), int(0));
                uint slot_28 = _S1412 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1418)).xy), uint(((_S1418)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1419 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1419)).xy), uint(((_S1419)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1420 = atlasWidth_14;
                uint _S1421 = 6U % atlasWidth_14;
                int _S1422 = int(_S1421);
                uint _S1423 = 6U / _S1420;
                int3 _S1424 = int3(_S1422, int(_S1423), int(0));
                uint _S1425 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1424)).xy), uint(((_S1424)).z))).x * 255.0f));
                uint _S1426 = atlasWidth_14;
                uint _S1427 = 7U % atlasWidth_14;
                int _S1428 = int(_S1427);
                uint _S1429 = 7U / _S1426;
                int3 _S1430 = int3(_S1428, int(_S1429), int(0));
                uint columns_14 = _S1425 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1430)).xy), uint(((_S1430)).z))).x * 255.0f)) << 8U);
                uint _S1431 = atlasWidth_14;
                uint _S1432 = 8U % atlasWidth_14;
                int _S1433 = int(_S1432);
                uint _S1434 = 8U / _S1431;
                int3 _S1435 = int3(_S1433, int(_S1434), int(0));
                uint _S1436 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1435)).xy), uint(((_S1435)).z))).x * 255.0f));
                uint _S1437 = atlasWidth_14;
                uint _S1438 = 9U % atlasWidth_14;
                int _S1439 = int(_S1438);
                uint _S1440 = 9U / _S1437;
                int3 _S1441 = int3(_S1439, int(_S1440), int(0));
                uint rows_14 = ((_S1436 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1441)).xy), uint(((_S1441)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1399 * _S1405 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1442 = atlasWidth_14 / columns_14;
                float _S1443 = float(_S1442);
                uint _S1444 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1443, float(_S1444));
                uint _S1445 = slot_29 % columns_14;
                float _S1446 = float(_S1445);
                uint _S1447 = slot_29 / columns_14;
                _S182 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1446, float(_S1447)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(detailSource_0) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1448 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(detailSource_0)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1448);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1449 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1449)).xy), uint(((_S1449)).z)));
                break;
            }
            uint _S1450 = atlasWidth_14 / uint(_S1448.x);
            float _S1451 = float(_S1450);
            uint _S1452 = (atlasHeight_14 - 1U) / uint(_S1448.y);
            float2 cellSize_29 = float2(_S1451, float(_S1452));
            _S182 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1453 = float4(_S6->compositeControls_0) ;
            if((_S1453.x) != 256.0f)
            {
                break;
            }
            bool _S1454 = (_S1453.w) >= 0.5f;
            if((maskU_1 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_1 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1455 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1454)
                {
                    for(;;)
                    {
                        if(_S1455 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1456 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_21;
                        thread uint height_21;
                        (*((&width_21)) = (_S1456).get_width(0)),(*((&height_21)) = (_S1456).get_height(0));
                        float2 size_17 = float2(float(width_21), float(height_21));
                        float2 _S1457 = float2(0.5f) ;
                        float2 remapped_17 = clamp((detailSource_0 * (size_17 - float2(2.0f) ) + float2(1.0f) ) / size_17, _S1457 / size_17, (size_17 - _S1457) / size_17);
                        if((_S1455 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_17.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.x;
                        }
                        if((_S1455 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_17.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = detailSource_0.y;
                        }
                        detailSource_0 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1458 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1458).get_width(0)),(*((&atlasHeight_15)) = (_S1458).get_height(0));
                int3 _S1459 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1459)).xy), uint(((_S1459)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1460 = atlasWidth_15;
                    uint _S1461 = 2U % atlasWidth_15;
                    int _S1462 = int(_S1461);
                    uint _S1463 = 2U / _S1460;
                    int3 _S1464 = int3(_S1462, int(_S1463), int(0));
                    int _S1465 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1464)).xy), uint(((_S1464)).z))).x * 255.0f)));
                    uint _S1466 = atlasWidth_15;
                    uint _S1467 = 3U % atlasWidth_15;
                    int _S1468 = int(_S1467);
                    uint _S1469 = 3U / _S1466;
                    int3 _S1470 = int3(_S1468, int(_S1469), int(0));
                    int2 minimumTile_15 = int2(_S1465, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1470)).xy), uint(((_S1470)).z))).x * 255.0f))));
                    uint _S1471 = atlasWidth_15;
                    uint _S1472 = 4U % atlasWidth_15;
                    int _S1473 = int(_S1472);
                    uint _S1474 = 4U / _S1471;
                    int3 _S1475 = int3(_S1473, int(_S1474), int(0));
                    uint _S1476 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1475)).xy), uint(((_S1475)).z))).x * 255.0f));
                    uint _S1477 = atlasWidth_15;
                    uint _S1478 = 5U % atlasWidth_15;
                    int _S1479 = int(_S1478);
                    uint _S1480 = 5U / _S1477;
                    int3 _S1481 = int3(_S1479, int(_S1480), int(0));
                    uint _S1482 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1481)).xy), uint(((_S1481)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1476, _S1482);
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
                        int3 _S1483 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1483)).xy), uint(((_S1483)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1476 + uint(tile_30.x));
                    uint _S1484 = atlasWidth_15;
                    uint _S1485 = offset_15 % atlasWidth_15;
                    int _S1486 = int(_S1485);
                    uint _S1487 = offset_15 / _S1484;
                    int3 _S1488 = int3(_S1486, int(_S1487), int(0));
                    uint _S1489 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1488)).xy), uint(((_S1488)).z))).x * 255.0f));
                    uint _S1490 = atlasWidth_15;
                    uint _S1491 = offset_15 + 1U;
                    uint _S1492 = _S1491 % atlasWidth_15;
                    int _S1493 = int(_S1492);
                    uint _S1494 = _S1491 / _S1490;
                    int3 _S1495 = int3(_S1493, int(_S1494), int(0));
                    uint slot_30 = _S1489 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1495)).xy), uint(((_S1495)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1496 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1496)).xy), uint(((_S1496)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1497 = atlasWidth_15;
                    uint _S1498 = 6U % atlasWidth_15;
                    int _S1499 = int(_S1498);
                    uint _S1500 = 6U / _S1497;
                    int3 _S1501 = int3(_S1499, int(_S1500), int(0));
                    uint _S1502 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1501)).xy), uint(((_S1501)).z))).x * 255.0f));
                    uint _S1503 = atlasWidth_15;
                    uint _S1504 = 7U % atlasWidth_15;
                    int _S1505 = int(_S1504);
                    uint _S1506 = 7U / _S1503;
                    int3 _S1507 = int3(_S1505, int(_S1506), int(0));
                    uint columns_15 = _S1502 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1507)).xy), uint(((_S1507)).z))).x * 255.0f)) << 8U);
                    uint _S1508 = atlasWidth_15;
                    uint _S1509 = 8U % atlasWidth_15;
                    int _S1510 = int(_S1509);
                    uint _S1511 = 8U / _S1508;
                    int3 _S1512 = int3(_S1510, int(_S1511), int(0));
                    uint _S1513 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1512)).xy), uint(((_S1512)).z))).x * 255.0f));
                    uint _S1514 = atlasWidth_15;
                    uint _S1515 = 9U % atlasWidth_15;
                    int _S1516 = int(_S1515);
                    uint _S1517 = 9U / _S1514;
                    int3 _S1518 = int3(_S1516, int(_S1517), int(0));
                    uint rows_15 = ((_S1513 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1518)).xy), uint(((_S1518)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1476 * _S1482 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1519 = atlasWidth_15 / columns_15;
                    float _S1520 = float(_S1519);
                    uint _S1521 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1520, float(_S1521));
                    uint _S1522 = slot_31 % columns_15;
                    float _S1523 = float(_S1522);
                    uint _S1524 = slot_31 / columns_15;
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1523, float(_S1524)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(detailSource_0) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1525 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(detailSource_0)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1525);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1526 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1526)).xy), uint(((_S1526)).z)));
                    break;
                }
                uint _S1527 = atlasWidth_15 / uint(_S1525.x);
                float _S1528 = float(_S1527);
                uint _S1529 = (atlasHeight_15 - 1U) / uint(_S1525.y);
                float2 cellSize_31 = float2(_S1528, float(_S1529));
                _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_6 = uint(round(_S1453.y));
            if(operation_6 == 1U)
            {
                _S182 = _S182 * _S582;
                break;
            }
            if(operation_6 == 2U)
            {
                _S182 = _S182 + _S582;
                break;
            }
            if(operation_6 == 3U)
            {
                _S182 = _S182 - _S582;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1453.z;
                _S182 = _S182 * float4((1.0f - factor_6))  + _S582 * float4(factor_6) ;
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
        bool _S1530 = (udimMask_0 & 512U) != 0U;
        if((maskU_1 & 512U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_1 & 512U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1531 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1530)
            {
                for(;;)
                {
                    if(_S1531 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1532 = (&kernelContext_0)->clearcoatTexture_0;
                    thread uint width_22;
                    thread uint height_22;
                    (*((&width_22)) = (_S1532).get_width(0)),(*((&height_22)) = (_S1532).get_height(0));
                    float2 size_18 = float2(float(width_22), float(height_22));
                    float2 _S1533 = float2(0.5f) ;
                    float2 remapped_18 = clamp((detailSource_0 * (size_18 - float2(2.0f) ) + float2(1.0f) ) / size_18, _S1533 / size_18, (size_18 - _S1533) / size_18);
                    if((_S1531 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_18.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.x;
                    }
                    if((_S1531 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_18.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1534 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1534).get_width(0)),(*((&atlasHeight_16)) = (_S1534).get_height(0));
            int3 _S1535 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1535)).xy), uint(((_S1535)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1536 = atlasWidth_16;
                uint _S1537 = 2U % atlasWidth_16;
                int _S1538 = int(_S1537);
                uint _S1539 = 2U / _S1536;
                int3 _S1540 = int3(_S1538, int(_S1539), int(0));
                int _S1541 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1540)).xy), uint(((_S1540)).z))).x * 255.0f)));
                uint _S1542 = atlasWidth_16;
                uint _S1543 = 3U % atlasWidth_16;
                int _S1544 = int(_S1543);
                uint _S1545 = 3U / _S1542;
                int3 _S1546 = int3(_S1544, int(_S1545), int(0));
                int2 minimumTile_16 = int2(_S1541, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1546)).xy), uint(((_S1546)).z))).x * 255.0f))));
                uint _S1547 = atlasWidth_16;
                uint _S1548 = 4U % atlasWidth_16;
                int _S1549 = int(_S1548);
                uint _S1550 = 4U / _S1547;
                int3 _S1551 = int3(_S1549, int(_S1550), int(0));
                uint _S1552 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1551)).xy), uint(((_S1551)).z))).x * 255.0f));
                uint _S1553 = atlasWidth_16;
                uint _S1554 = 5U % atlasWidth_16;
                int _S1555 = int(_S1554);
                uint _S1556 = 5U / _S1553;
                int3 _S1557 = int3(_S1555, int(_S1556), int(0));
                uint _S1558 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1557)).xy), uint(((_S1557)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1552, _S1558);
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
                    int3 _S1559 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1559)).xy), uint(((_S1559)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1552 + uint(tile_32.x));
                uint _S1560 = atlasWidth_16;
                uint _S1561 = offset_16 % atlasWidth_16;
                int _S1562 = int(_S1561);
                uint _S1563 = offset_16 / _S1560;
                int3 _S1564 = int3(_S1562, int(_S1563), int(0));
                uint _S1565 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1564)).xy), uint(((_S1564)).z))).x * 255.0f));
                uint _S1566 = atlasWidth_16;
                uint _S1567 = offset_16 + 1U;
                uint _S1568 = _S1567 % atlasWidth_16;
                int _S1569 = int(_S1568);
                uint _S1570 = _S1567 / _S1566;
                int3 _S1571 = int3(_S1569, int(_S1570), int(0));
                uint slot_32 = _S1565 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1571)).xy), uint(((_S1571)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1572 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1572)).xy), uint(((_S1572)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1573 = atlasWidth_16;
                uint _S1574 = 6U % atlasWidth_16;
                int _S1575 = int(_S1574);
                uint _S1576 = 6U / _S1573;
                int3 _S1577 = int3(_S1575, int(_S1576), int(0));
                uint _S1578 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1577)).xy), uint(((_S1577)).z))).x * 255.0f));
                uint _S1579 = atlasWidth_16;
                uint _S1580 = 7U % atlasWidth_16;
                int _S1581 = int(_S1580);
                uint _S1582 = 7U / _S1579;
                int3 _S1583 = int3(_S1581, int(_S1582), int(0));
                uint columns_16 = _S1578 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1583)).xy), uint(((_S1583)).z))).x * 255.0f)) << 8U);
                uint _S1584 = atlasWidth_16;
                uint _S1585 = 8U % atlasWidth_16;
                int _S1586 = int(_S1585);
                uint _S1587 = 8U / _S1584;
                int3 _S1588 = int3(_S1586, int(_S1587), int(0));
                uint _S1589 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1588)).xy), uint(((_S1588)).z))).x * 255.0f));
                uint _S1590 = atlasWidth_16;
                uint _S1591 = 9U % atlasWidth_16;
                int _S1592 = int(_S1591);
                uint _S1593 = 9U / _S1590;
                int3 _S1594 = int3(_S1592, int(_S1593), int(0));
                uint rows_16 = ((_S1589 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1594)).xy), uint(((_S1594)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1552 * _S1558 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1595 = atlasWidth_16 / columns_16;
                float _S1596 = float(_S1595);
                uint _S1597 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1596, float(_S1597));
                uint _S1598 = slot_33 % columns_16;
                float _S1599 = float(_S1598);
                uint _S1600 = slot_33 / columns_16;
                _S182 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1599, float(_S1600)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(detailSource_0) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1601 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(detailSource_0)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1601);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1602 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1602)).xy), uint(((_S1602)).z)));
                break;
            }
            uint _S1603 = atlasWidth_16 / uint(_S1601.x);
            float _S1604 = float(_S1603);
            uint _S1605 = (atlasHeight_16 - 1U) / uint(_S1601.y);
            float2 cellSize_33 = float2(_S1604, float(_S1605));
            _S182 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1606 = float4(_S6->compositeControls_0) ;
            if((_S1606.x) != 512.0f)
            {
                break;
            }
            bool _S1607 = (_S1606.w) >= 0.5f;
            if((maskU_1 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_1 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1608 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1607)
                {
                    for(;;)
                    {
                        if(_S1608 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1609 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_23;
                        thread uint height_23;
                        (*((&width_23)) = (_S1609).get_width(0)),(*((&height_23)) = (_S1609).get_height(0));
                        float2 size_19 = float2(float(width_23), float(height_23));
                        float2 _S1610 = float2(0.5f) ;
                        float2 remapped_19 = clamp((detailSource_0 * (size_19 - float2(2.0f) ) + float2(1.0f) ) / size_19, _S1610 / size_19, (size_19 - _S1610) / size_19);
                        if((_S1608 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_19.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.x;
                        }
                        if((_S1608 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_19.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = detailSource_0.y;
                        }
                        detailSource_0 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1611 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1611).get_width(0)),(*((&atlasHeight_17)) = (_S1611).get_height(0));
                int3 _S1612 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1612)).xy), uint(((_S1612)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1613 = atlasWidth_17;
                    uint _S1614 = 2U % atlasWidth_17;
                    int _S1615 = int(_S1614);
                    uint _S1616 = 2U / _S1613;
                    int3 _S1617 = int3(_S1615, int(_S1616), int(0));
                    int _S1618 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1617)).xy), uint(((_S1617)).z))).x * 255.0f)));
                    uint _S1619 = atlasWidth_17;
                    uint _S1620 = 3U % atlasWidth_17;
                    int _S1621 = int(_S1620);
                    uint _S1622 = 3U / _S1619;
                    int3 _S1623 = int3(_S1621, int(_S1622), int(0));
                    int2 minimumTile_17 = int2(_S1618, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1623)).xy), uint(((_S1623)).z))).x * 255.0f))));
                    uint _S1624 = atlasWidth_17;
                    uint _S1625 = 4U % atlasWidth_17;
                    int _S1626 = int(_S1625);
                    uint _S1627 = 4U / _S1624;
                    int3 _S1628 = int3(_S1626, int(_S1627), int(0));
                    uint _S1629 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1628)).xy), uint(((_S1628)).z))).x * 255.0f));
                    uint _S1630 = atlasWidth_17;
                    uint _S1631 = 5U % atlasWidth_17;
                    int _S1632 = int(_S1631);
                    uint _S1633 = 5U / _S1630;
                    int3 _S1634 = int3(_S1632, int(_S1633), int(0));
                    uint _S1635 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1634)).xy), uint(((_S1634)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1629, _S1635);
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
                        int3 _S1636 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1636)).xy), uint(((_S1636)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1629 + uint(tile_34.x));
                    uint _S1637 = atlasWidth_17;
                    uint _S1638 = offset_17 % atlasWidth_17;
                    int _S1639 = int(_S1638);
                    uint _S1640 = offset_17 / _S1637;
                    int3 _S1641 = int3(_S1639, int(_S1640), int(0));
                    uint _S1642 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1641)).xy), uint(((_S1641)).z))).x * 255.0f));
                    uint _S1643 = atlasWidth_17;
                    uint _S1644 = offset_17 + 1U;
                    uint _S1645 = _S1644 % atlasWidth_17;
                    int _S1646 = int(_S1645);
                    uint _S1647 = _S1644 / _S1643;
                    int3 _S1648 = int3(_S1646, int(_S1647), int(0));
                    uint slot_34 = _S1642 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1648)).xy), uint(((_S1648)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1649 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1649)).xy), uint(((_S1649)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1650 = atlasWidth_17;
                    uint _S1651 = 6U % atlasWidth_17;
                    int _S1652 = int(_S1651);
                    uint _S1653 = 6U / _S1650;
                    int3 _S1654 = int3(_S1652, int(_S1653), int(0));
                    uint _S1655 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1654)).xy), uint(((_S1654)).z))).x * 255.0f));
                    uint _S1656 = atlasWidth_17;
                    uint _S1657 = 7U % atlasWidth_17;
                    int _S1658 = int(_S1657);
                    uint _S1659 = 7U / _S1656;
                    int3 _S1660 = int3(_S1658, int(_S1659), int(0));
                    uint columns_17 = _S1655 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1660)).xy), uint(((_S1660)).z))).x * 255.0f)) << 8U);
                    uint _S1661 = atlasWidth_17;
                    uint _S1662 = 8U % atlasWidth_17;
                    int _S1663 = int(_S1662);
                    uint _S1664 = 8U / _S1661;
                    int3 _S1665 = int3(_S1663, int(_S1664), int(0));
                    uint _S1666 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1665)).xy), uint(((_S1665)).z))).x * 255.0f));
                    uint _S1667 = atlasWidth_17;
                    uint _S1668 = 9U % atlasWidth_17;
                    int _S1669 = int(_S1668);
                    uint _S1670 = 9U / _S1667;
                    int3 _S1671 = int3(_S1669, int(_S1670), int(0));
                    uint rows_17 = ((_S1666 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1671)).xy), uint(((_S1671)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1629 * _S1635 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1672 = atlasWidth_17 / columns_17;
                    float _S1673 = float(_S1672);
                    uint _S1674 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1673, float(_S1674));
                    uint _S1675 = slot_35 % columns_17;
                    float _S1676 = float(_S1675);
                    uint _S1677 = slot_35 / columns_17;
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1676, float(_S1677)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(detailSource_0) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1678 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(detailSource_0)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1678);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1679 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1679)).xy), uint(((_S1679)).z)));
                    break;
                }
                uint _S1680 = atlasWidth_17 / uint(_S1678.x);
                float _S1681 = float(_S1680);
                uint _S1682 = (atlasHeight_17 - 1U) / uint(_S1678.y);
                float2 cellSize_35 = float2(_S1681, float(_S1682));
                _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_7 = uint(round(_S1606.y));
            if(operation_7 == 1U)
            {
                _S182 = _S182 * _S582;
                break;
            }
            if(operation_7 == 2U)
            {
                _S182 = _S182 + _S582;
                break;
            }
            if(operation_7 == 3U)
            {
                _S182 = _S182 - _S582;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1606.z;
                _S182 = _S182 * float4((1.0f - factor_7))  + _S582 * float4(factor_7) ;
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
    float ior_0;
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
        bool _S1683 = (udimMask_0 & 1024U) != 0U;
        if((maskU_1 & 1024U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_1 & 1024U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1684 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1683)
            {
                for(;;)
                {
                    if(_S1684 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1685 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
                    thread uint width_24;
                    thread uint height_24;
                    (*((&width_24)) = (_S1685).get_width(0)),(*((&height_24)) = (_S1685).get_height(0));
                    float2 size_20 = float2(float(width_24), float(height_24));
                    float2 _S1686 = float2(0.5f) ;
                    float2 remapped_20 = clamp((detailSource_0 * (size_20 - float2(2.0f) ) + float2(1.0f) ) / size_20, _S1686 / size_20, (size_20 - _S1686) / size_20);
                    if((_S1684 & 1U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_20.x;
                    }
                    else
                    {
                        clearcoatRoughness_0 = detailSource_0.x;
                    }
                    if((_S1684 & 2U) != 0U)
                    {
                        ior_0 = remapped_20.y;
                    }
                    else
                    {
                        ior_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(clearcoatRoughness_0, ior_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1687 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1687).get_width(0)),(*((&atlasHeight_18)) = (_S1687).get_height(0));
            int3 _S1688 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1688)).xy), uint(((_S1688)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1689 = atlasWidth_18;
                uint _S1690 = 2U % atlasWidth_18;
                int _S1691 = int(_S1690);
                uint _S1692 = 2U / _S1689;
                int3 _S1693 = int3(_S1691, int(_S1692), int(0));
                int _S1694 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1693)).xy), uint(((_S1693)).z))).x * 255.0f)));
                uint _S1695 = atlasWidth_18;
                uint _S1696 = 3U % atlasWidth_18;
                int _S1697 = int(_S1696);
                uint _S1698 = 3U / _S1695;
                int3 _S1699 = int3(_S1697, int(_S1698), int(0));
                int2 minimumTile_18 = int2(_S1694, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1699)).xy), uint(((_S1699)).z))).x * 255.0f))));
                uint _S1700 = atlasWidth_18;
                uint _S1701 = 4U % atlasWidth_18;
                int _S1702 = int(_S1701);
                uint _S1703 = 4U / _S1700;
                int3 _S1704 = int3(_S1702, int(_S1703), int(0));
                uint _S1705 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1704)).xy), uint(((_S1704)).z))).x * 255.0f));
                uint _S1706 = atlasWidth_18;
                uint _S1707 = 5U % atlasWidth_18;
                int _S1708 = int(_S1707);
                uint _S1709 = 5U / _S1706;
                int3 _S1710 = int3(_S1708, int(_S1709), int(0));
                uint _S1711 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1710)).xy), uint(((_S1710)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1705, _S1711);
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
                    int3 _S1712 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1712)).xy), uint(((_S1712)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1705 + uint(tile_36.x));
                uint _S1713 = atlasWidth_18;
                uint _S1714 = offset_18 % atlasWidth_18;
                int _S1715 = int(_S1714);
                uint _S1716 = offset_18 / _S1713;
                int3 _S1717 = int3(_S1715, int(_S1716), int(0));
                uint _S1718 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1717)).xy), uint(((_S1717)).z))).x * 255.0f));
                uint _S1719 = atlasWidth_18;
                uint _S1720 = offset_18 + 1U;
                uint _S1721 = _S1720 % atlasWidth_18;
                int _S1722 = int(_S1721);
                uint _S1723 = _S1720 / _S1719;
                int3 _S1724 = int3(_S1722, int(_S1723), int(0));
                uint slot_36 = _S1718 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1724)).xy), uint(((_S1724)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1725 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1725)).xy), uint(((_S1725)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1726 = atlasWidth_18;
                uint _S1727 = 6U % atlasWidth_18;
                int _S1728 = int(_S1727);
                uint _S1729 = 6U / _S1726;
                int3 _S1730 = int3(_S1728, int(_S1729), int(0));
                uint _S1731 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1730)).xy), uint(((_S1730)).z))).x * 255.0f));
                uint _S1732 = atlasWidth_18;
                uint _S1733 = 7U % atlasWidth_18;
                int _S1734 = int(_S1733);
                uint _S1735 = 7U / _S1732;
                int3 _S1736 = int3(_S1734, int(_S1735), int(0));
                uint columns_18 = _S1731 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1736)).xy), uint(((_S1736)).z))).x * 255.0f)) << 8U);
                uint _S1737 = atlasWidth_18;
                uint _S1738 = 8U % atlasWidth_18;
                int _S1739 = int(_S1738);
                uint _S1740 = 8U / _S1737;
                int3 _S1741 = int3(_S1739, int(_S1740), int(0));
                uint _S1742 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1741)).xy), uint(((_S1741)).z))).x * 255.0f));
                uint _S1743 = atlasWidth_18;
                uint _S1744 = 9U % atlasWidth_18;
                int _S1745 = int(_S1744);
                uint _S1746 = 9U / _S1743;
                int3 _S1747 = int3(_S1745, int(_S1746), int(0));
                uint rows_18 = ((_S1742 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1747)).xy), uint(((_S1747)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1705 * _S1711 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1748 = atlasWidth_18 / columns_18;
                float _S1749 = float(_S1748);
                uint _S1750 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1749, float(_S1750));
                uint _S1751 = slot_37 % columns_18;
                float _S1752 = float(_S1751);
                uint _S1753 = slot_37 / columns_18;
                _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1752, float(_S1753)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(detailSource_0) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1754 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(detailSource_0)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1754);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1755 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1755)).xy), uint(((_S1755)).z)));
                break;
            }
            uint _S1756 = atlasWidth_18 / uint(_S1754.x);
            float _S1757 = float(_S1756);
            uint _S1758 = (atlasHeight_18 - 1U) / uint(_S1754.y);
            float2 cellSize_37 = float2(_S1757, float(_S1758));
            _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1759 = float4(_S6->compositeControls_0) ;
            if((_S1759.x) != 1024.0f)
            {
                break;
            }
            bool _S1760 = (_S1759.w) >= 0.5f;
            if((maskU_1 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_1 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1761 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1760)
                {
                    for(;;)
                    {
                        if(_S1761 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1762 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_25;
                        thread uint height_25;
                        (*((&width_25)) = (_S1762).get_width(0)),(*((&height_25)) = (_S1762).get_height(0));
                        float2 size_21 = float2(float(width_25), float(height_25));
                        float2 _S1763 = float2(0.5f) ;
                        float2 remapped_21 = clamp((detailSource_0 * (size_21 - float2(2.0f) ) + float2(1.0f) ) / size_21, _S1763 / size_21, (size_21 - _S1763) / size_21);
                        if((_S1761 & 1U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_21.x;
                        }
                        else
                        {
                            clearcoatRoughness_0 = detailSource_0.x;
                        }
                        if((_S1761 & 2U) != 0U)
                        {
                            ior_0 = remapped_21.y;
                        }
                        else
                        {
                            ior_0 = detailSource_0.y;
                        }
                        detailSource_0 = float2(clearcoatRoughness_0, ior_0);
                        break;
                    }
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1764 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1764).get_width(0)),(*((&atlasHeight_19)) = (_S1764).get_height(0));
                int3 _S1765 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1765)).xy), uint(((_S1765)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1766 = atlasWidth_19;
                    uint _S1767 = 2U % atlasWidth_19;
                    int _S1768 = int(_S1767);
                    uint _S1769 = 2U / _S1766;
                    int3 _S1770 = int3(_S1768, int(_S1769), int(0));
                    int _S1771 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1770)).xy), uint(((_S1770)).z))).x * 255.0f)));
                    uint _S1772 = atlasWidth_19;
                    uint _S1773 = 3U % atlasWidth_19;
                    int _S1774 = int(_S1773);
                    uint _S1775 = 3U / _S1772;
                    int3 _S1776 = int3(_S1774, int(_S1775), int(0));
                    int2 minimumTile_19 = int2(_S1771, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1776)).xy), uint(((_S1776)).z))).x * 255.0f))));
                    uint _S1777 = atlasWidth_19;
                    uint _S1778 = 4U % atlasWidth_19;
                    int _S1779 = int(_S1778);
                    uint _S1780 = 4U / _S1777;
                    int3 _S1781 = int3(_S1779, int(_S1780), int(0));
                    uint _S1782 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1781)).xy), uint(((_S1781)).z))).x * 255.0f));
                    uint _S1783 = atlasWidth_19;
                    uint _S1784 = 5U % atlasWidth_19;
                    int _S1785 = int(_S1784);
                    uint _S1786 = 5U / _S1783;
                    int3 _S1787 = int3(_S1785, int(_S1786), int(0));
                    uint _S1788 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1787)).xy), uint(((_S1787)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1782, _S1788);
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
                        int3 _S1789 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1789)).xy), uint(((_S1789)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1782 + uint(tile_38.x));
                    uint _S1790 = atlasWidth_19;
                    uint _S1791 = offset_19 % atlasWidth_19;
                    int _S1792 = int(_S1791);
                    uint _S1793 = offset_19 / _S1790;
                    int3 _S1794 = int3(_S1792, int(_S1793), int(0));
                    uint _S1795 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1794)).xy), uint(((_S1794)).z))).x * 255.0f));
                    uint _S1796 = atlasWidth_19;
                    uint _S1797 = offset_19 + 1U;
                    uint _S1798 = _S1797 % atlasWidth_19;
                    int _S1799 = int(_S1798);
                    uint _S1800 = _S1797 / _S1796;
                    int3 _S1801 = int3(_S1799, int(_S1800), int(0));
                    uint slot_38 = _S1795 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1801)).xy), uint(((_S1801)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1802 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1802)).xy), uint(((_S1802)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1803 = atlasWidth_19;
                    uint _S1804 = 6U % atlasWidth_19;
                    int _S1805 = int(_S1804);
                    uint _S1806 = 6U / _S1803;
                    int3 _S1807 = int3(_S1805, int(_S1806), int(0));
                    uint _S1808 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1807)).xy), uint(((_S1807)).z))).x * 255.0f));
                    uint _S1809 = atlasWidth_19;
                    uint _S1810 = 7U % atlasWidth_19;
                    int _S1811 = int(_S1810);
                    uint _S1812 = 7U / _S1809;
                    int3 _S1813 = int3(_S1811, int(_S1812), int(0));
                    uint columns_19 = _S1808 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1813)).xy), uint(((_S1813)).z))).x * 255.0f)) << 8U);
                    uint _S1814 = atlasWidth_19;
                    uint _S1815 = 8U % atlasWidth_19;
                    int _S1816 = int(_S1815);
                    uint _S1817 = 8U / _S1814;
                    int3 _S1818 = int3(_S1816, int(_S1817), int(0));
                    uint _S1819 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1818)).xy), uint(((_S1818)).z))).x * 255.0f));
                    uint _S1820 = atlasWidth_19;
                    uint _S1821 = 9U % atlasWidth_19;
                    int _S1822 = int(_S1821);
                    uint _S1823 = 9U / _S1820;
                    int3 _S1824 = int3(_S1822, int(_S1823), int(0));
                    uint rows_19 = ((_S1819 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1824)).xy), uint(((_S1824)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1782 * _S1788 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1825 = atlasWidth_19 / columns_19;
                    float _S1826 = float(_S1825);
                    uint _S1827 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1826, float(_S1827));
                    uint _S1828 = slot_39 % columns_19;
                    float _S1829 = float(_S1828);
                    uint _S1830 = slot_39 / columns_19;
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1829, float(_S1830)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(detailSource_0) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1831 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(detailSource_0)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1831);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1832 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1832)).xy), uint(((_S1832)).z)));
                    break;
                }
                uint _S1833 = atlasWidth_19 / uint(_S1831.x);
                float _S1834 = float(_S1833);
                uint _S1835 = (atlasHeight_19 - 1U) / uint(_S1831.y);
                float2 cellSize_39 = float2(_S1834, float(_S1835));
                _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_8 = uint(round(_S1759.y));
            if(operation_8 == 1U)
            {
                _S182 = _S182 * _S582;
                break;
            }
            if(operation_8 == 2U)
            {
                _S182 = _S182 + _S582;
                break;
            }
            if(operation_8 == 3U)
            {
                _S182 = _S182 - _S582;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1759.z;
                _S182 = _S182 * float4((1.0f - factor_8))  + _S582 * float4(factor_8) ;
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
    float lightType_0;
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
        bool _S1836 = (udimMask_0 & 2048U) != 0U;
        if((maskU_1 & 2048U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_1 & 2048U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1837 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1836)
            {
                for(;;)
                {
                    if(_S1837 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1838 = (&kernelContext_0)->iorTexture_0;
                    thread uint width_26;
                    thread uint height_26;
                    (*((&width_26)) = (_S1838).get_width(0)),(*((&height_26)) = (_S1838).get_height(0));
                    float2 size_22 = float2(float(width_26), float(height_26));
                    float2 _S1839 = float2(0.5f) ;
                    float2 remapped_22 = clamp((detailSource_0 * (size_22 - float2(2.0f) ) + float2(1.0f) ) / size_22, _S1839 / size_22, (size_22 - _S1839) / size_22);
                    if((_S1837 & 1U) != 0U)
                    {
                        ior_0 = remapped_22.x;
                    }
                    else
                    {
                        ior_0 = detailSource_0.x;
                    }
                    if((_S1837 & 2U) != 0U)
                    {
                        lightType_0 = remapped_22.y;
                    }
                    else
                    {
                        lightType_0 = detailSource_0.y;
                    }
                    tangent_1 = float2(ior_0, lightType_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1840 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_20;
            thread uint atlasHeight_20;
            (*((&atlasWidth_20)) = (_S1840).get_width(0)),(*((&atlasHeight_20)) = (_S1840).get_height(0));
            int3 _S1841 = int3(int(0), int(0), int(0));
            float4 metadata_20 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1841)).xy), uint(((_S1841)).z))) * float4(255.0f) );
            if((metadata_20.x) == 255.0f)
            {
                uint _S1842 = atlasWidth_20;
                uint _S1843 = 2U % atlasWidth_20;
                int _S1844 = int(_S1843);
                uint _S1845 = 2U / _S1842;
                int3 _S1846 = int3(_S1844, int(_S1845), int(0));
                int _S1847 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1846)).xy), uint(((_S1846)).z))).x * 255.0f)));
                uint _S1848 = atlasWidth_20;
                uint _S1849 = 3U % atlasWidth_20;
                int _S1850 = int(_S1849);
                uint _S1851 = 3U / _S1848;
                int3 _S1852 = int3(_S1850, int(_S1851), int(0));
                int2 minimumTile_20 = int2(_S1847, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1852)).xy), uint(((_S1852)).z))).x * 255.0f))));
                uint _S1853 = atlasWidth_20;
                uint _S1854 = 4U % atlasWidth_20;
                int _S1855 = int(_S1854);
                uint _S1856 = 4U / _S1853;
                int3 _S1857 = int3(_S1855, int(_S1856), int(0));
                uint _S1858 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1857)).xy), uint(((_S1857)).z))).x * 255.0f));
                uint _S1859 = atlasWidth_20;
                uint _S1860 = 5U % atlasWidth_20;
                int _S1861 = int(_S1860);
                uint _S1862 = 5U / _S1859;
                int3 _S1863 = int3(_S1861, int(_S1862), int(0));
                uint _S1864 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1863)).xy), uint(((_S1863)).z))).x * 255.0f));
                uint2 grid_20 = uint2(_S1858, _S1864);
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
                    int3 _S1865 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1865)).xy), uint(((_S1865)).z)));
                    break;
                }
                uint offset_20 = 10U + 2U * (uint(tile_40.y) * _S1858 + uint(tile_40.x));
                uint _S1866 = atlasWidth_20;
                uint _S1867 = offset_20 % atlasWidth_20;
                int _S1868 = int(_S1867);
                uint _S1869 = offset_20 / _S1866;
                int3 _S1870 = int3(_S1868, int(_S1869), int(0));
                uint _S1871 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1870)).xy), uint(((_S1870)).z))).x * 255.0f));
                uint _S1872 = atlasWidth_20;
                uint _S1873 = offset_20 + 1U;
                uint _S1874 = _S1873 % atlasWidth_20;
                int _S1875 = int(_S1874);
                uint _S1876 = _S1873 / _S1872;
                int3 _S1877 = int3(_S1875, int(_S1876), int(0));
                uint slot_40 = _S1871 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1877)).xy), uint(((_S1877)).z))).x * 255.0f)) << 8U);
                if(slot_40 == 0U)
                {
                    int3 _S1878 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1878)).xy), uint(((_S1878)).z)));
                    break;
                }
                uint slot_41 = slot_40 - 1U;
                uint _S1879 = atlasWidth_20;
                uint _S1880 = 6U % atlasWidth_20;
                int _S1881 = int(_S1880);
                uint _S1882 = 6U / _S1879;
                int3 _S1883 = int3(_S1881, int(_S1882), int(0));
                uint _S1884 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1883)).xy), uint(((_S1883)).z))).x * 255.0f));
                uint _S1885 = atlasWidth_20;
                uint _S1886 = 7U % atlasWidth_20;
                int _S1887 = int(_S1886);
                uint _S1888 = 7U / _S1885;
                int3 _S1889 = int3(_S1887, int(_S1888), int(0));
                uint columns_20 = _S1884 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1889)).xy), uint(((_S1889)).z))).x * 255.0f)) << 8U);
                uint _S1890 = atlasWidth_20;
                uint _S1891 = 8U % atlasWidth_20;
                int _S1892 = int(_S1891);
                uint _S1893 = 8U / _S1890;
                int3 _S1894 = int3(_S1892, int(_S1893), int(0));
                uint _S1895 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1894)).xy), uint(((_S1894)).z))).x * 255.0f));
                uint _S1896 = atlasWidth_20;
                uint _S1897 = 9U % atlasWidth_20;
                int _S1898 = int(_S1897);
                uint _S1899 = 9U / _S1896;
                int3 _S1900 = int3(_S1898, int(_S1899), int(0));
                uint rows_20 = ((_S1895 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1900)).xy), uint(((_S1900)).z))).x * 255.0f)) << 8U)) + columns_20 - 1U) / columns_20;
                uint metadataRows_20 = (10U + 2U * _S1858 * _S1864 + atlasWidth_20 - 1U) / atlasWidth_20;
                uint _S1901 = atlasWidth_20 / columns_20;
                float _S1902 = float(_S1901);
                uint _S1903 = (atlasHeight_20 - metadataRows_20) / rows_20;
                float2 cellSize_40 = float2(_S1902, float(_S1903));
                uint _S1904 = slot_41 % columns_20;
                float _S1905 = float(_S1904);
                uint _S1906 = slot_41 / columns_20;
                _S182 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1905, float(_S1906)) * cellSize_40 + float2(1.5f, float(metadataRows_20) + 1.5f) + fract(detailSource_0) * max(cellSize_40 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
                break;
            }
            int2 _S1907 = int2(metadata_20.zw);
            int2 tile_41 = int2(floor(detailSource_0)) - int2(metadata_20.xy);
            if(any(tile_41 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_41 >= _S1907);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1908 = int3(int(min(1U, atlasWidth_20 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1908)).xy), uint(((_S1908)).z)));
                break;
            }
            uint _S1909 = atlasWidth_20 / uint(_S1907.x);
            float _S1910 = float(_S1909);
            uint _S1911 = (atlasHeight_20 - 1U) / uint(_S1907.y);
            float2 cellSize_41 = float2(_S1910, float(_S1911));
            _S182 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_41) * cellSize_41 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_41 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
            break;
        }
        for(;;)
        {
            float4 _S1912 = float4(_S6->compositeControls_0) ;
            if((_S1912.x) != 2048.0f)
            {
                break;
            }
            bool _S1913 = (_S1912.w) >= 0.5f;
            if((maskU_1 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_1 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1914 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1913)
                {
                    for(;;)
                    {
                        if(_S1914 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1915 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_27;
                        thread uint height_27;
                        (*((&width_27)) = (_S1915).get_width(0)),(*((&height_27)) = (_S1915).get_height(0));
                        float2 size_23 = float2(float(width_27), float(height_27));
                        float2 _S1916 = float2(0.5f) ;
                        float2 remapped_23 = clamp((detailSource_0 * (size_23 - float2(2.0f) ) + float2(1.0f) ) / size_23, _S1916 / size_23, (size_23 - _S1916) / size_23);
                        if((_S1914 & 1U) != 0U)
                        {
                            ior_0 = remapped_23.x;
                        }
                        else
                        {
                            ior_0 = detailSource_0.x;
                        }
                        if((_S1914 & 2U) != 0U)
                        {
                            lightType_0 = remapped_23.y;
                        }
                        else
                        {
                            lightType_0 = detailSource_0.y;
                        }
                        detailSource_0 = float2(ior_0, lightType_0);
                        break;
                    }
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1917 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_21;
                thread uint atlasHeight_21;
                (*((&atlasWidth_21)) = (_S1917).get_width(0)),(*((&atlasHeight_21)) = (_S1917).get_height(0));
                int3 _S1918 = int3(int(0), int(0), int(0));
                float4 metadata_21 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1918)).xy), uint(((_S1918)).z))) * float4(255.0f) );
                if((metadata_21.x) == 255.0f)
                {
                    uint _S1919 = atlasWidth_21;
                    uint _S1920 = 2U % atlasWidth_21;
                    int _S1921 = int(_S1920);
                    uint _S1922 = 2U / _S1919;
                    int3 _S1923 = int3(_S1921, int(_S1922), int(0));
                    int _S1924 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1923)).xy), uint(((_S1923)).z))).x * 255.0f)));
                    uint _S1925 = atlasWidth_21;
                    uint _S1926 = 3U % atlasWidth_21;
                    int _S1927 = int(_S1926);
                    uint _S1928 = 3U / _S1925;
                    int3 _S1929 = int3(_S1927, int(_S1928), int(0));
                    int2 minimumTile_21 = int2(_S1924, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1929)).xy), uint(((_S1929)).z))).x * 255.0f))));
                    uint _S1930 = atlasWidth_21;
                    uint _S1931 = 4U % atlasWidth_21;
                    int _S1932 = int(_S1931);
                    uint _S1933 = 4U / _S1930;
                    int3 _S1934 = int3(_S1932, int(_S1933), int(0));
                    uint _S1935 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1934)).xy), uint(((_S1934)).z))).x * 255.0f));
                    uint _S1936 = atlasWidth_21;
                    uint _S1937 = 5U % atlasWidth_21;
                    int _S1938 = int(_S1937);
                    uint _S1939 = 5U / _S1936;
                    int3 _S1940 = int3(_S1938, int(_S1939), int(0));
                    uint _S1941 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1940)).xy), uint(((_S1940)).z))).x * 255.0f));
                    uint2 grid_21 = uint2(_S1935, _S1941);
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
                        int3 _S1942 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1942)).xy), uint(((_S1942)).z)));
                        break;
                    }
                    uint offset_21 = 10U + 2U * (uint(tile_42.y) * _S1935 + uint(tile_42.x));
                    uint _S1943 = atlasWidth_21;
                    uint _S1944 = offset_21 % atlasWidth_21;
                    int _S1945 = int(_S1944);
                    uint _S1946 = offset_21 / _S1943;
                    int3 _S1947 = int3(_S1945, int(_S1946), int(0));
                    uint _S1948 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1947)).xy), uint(((_S1947)).z))).x * 255.0f));
                    uint _S1949 = atlasWidth_21;
                    uint _S1950 = offset_21 + 1U;
                    uint _S1951 = _S1950 % atlasWidth_21;
                    int _S1952 = int(_S1951);
                    uint _S1953 = _S1950 / _S1949;
                    int3 _S1954 = int3(_S1952, int(_S1953), int(0));
                    uint slot_42 = _S1948 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1954)).xy), uint(((_S1954)).z))).x * 255.0f)) << 8U);
                    if(slot_42 == 0U)
                    {
                        int3 _S1955 = int3(int(1), int(0), int(0));
                        _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1955)).xy), uint(((_S1955)).z)));
                        break;
                    }
                    uint slot_43 = slot_42 - 1U;
                    uint _S1956 = atlasWidth_21;
                    uint _S1957 = 6U % atlasWidth_21;
                    int _S1958 = int(_S1957);
                    uint _S1959 = 6U / _S1956;
                    int3 _S1960 = int3(_S1958, int(_S1959), int(0));
                    uint _S1961 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1960)).xy), uint(((_S1960)).z))).x * 255.0f));
                    uint _S1962 = atlasWidth_21;
                    uint _S1963 = 7U % atlasWidth_21;
                    int _S1964 = int(_S1963);
                    uint _S1965 = 7U / _S1962;
                    int3 _S1966 = int3(_S1964, int(_S1965), int(0));
                    uint columns_21 = _S1961 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1966)).xy), uint(((_S1966)).z))).x * 255.0f)) << 8U);
                    uint _S1967 = atlasWidth_21;
                    uint _S1968 = 8U % atlasWidth_21;
                    int _S1969 = int(_S1968);
                    uint _S1970 = 8U / _S1967;
                    int3 _S1971 = int3(_S1969, int(_S1970), int(0));
                    uint _S1972 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1971)).xy), uint(((_S1971)).z))).x * 255.0f));
                    uint _S1973 = atlasWidth_21;
                    uint _S1974 = 9U % atlasWidth_21;
                    int _S1975 = int(_S1974);
                    uint _S1976 = 9U / _S1973;
                    int3 _S1977 = int3(_S1975, int(_S1976), int(0));
                    uint rows_21 = ((_S1972 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1977)).xy), uint(((_S1977)).z))).x * 255.0f)) << 8U)) + columns_21 - 1U) / columns_21;
                    uint metadataRows_21 = (10U + 2U * _S1935 * _S1941 + atlasWidth_21 - 1U) / atlasWidth_21;
                    uint _S1978 = atlasWidth_21 / columns_21;
                    float _S1979 = float(_S1978);
                    uint _S1980 = (atlasHeight_21 - metadataRows_21) / rows_21;
                    float2 cellSize_42 = float2(_S1979, float(_S1980));
                    uint _S1981 = slot_43 % columns_21;
                    float _S1982 = float(_S1981);
                    uint _S1983 = slot_43 / columns_21;
                    _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1982, float(_S1983)) * cellSize_42 + float2(1.5f, float(metadataRows_21) + 1.5f) + fract(detailSource_0) * max(cellSize_42 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                    break;
                }
                int2 _S1984 = int2(metadata_21.zw);
                int2 tile_43 = int2(floor(detailSource_0)) - int2(metadata_21.xy);
                if(any(tile_43 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_43 >= _S1984);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1985 = int3(int(min(1U, atlasWidth_21 - 1U)), int(0), int(0));
                    _S582 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1985)).xy), uint(((_S1985)).z)));
                    break;
                }
                uint _S1986 = atlasWidth_21 / uint(_S1984.x);
                float _S1987 = float(_S1986);
                uint _S1988 = (atlasHeight_21 - 1U) / uint(_S1984.y);
                float2 cellSize_43 = float2(_S1987, float(_S1988));
                _S582 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_43) * cellSize_43 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_43 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                break;
            }
            uint operation_9 = uint(round(_S1912.y));
            if(operation_9 == 1U)
            {
                _S182 = _S182 * _S582;
                break;
            }
            if(operation_9 == 2U)
            {
                _S182 = _S182 + _S582;
                break;
            }
            if(operation_9 == 3U)
            {
                _S182 = _S182 - _S582;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1912.z;
                _S182 = _S182 * float4((1.0f - factor_9))  + _S582 * float4(factor_9) ;
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
    float _S1989 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S1990 = max(0.00100000004749745f, roughness_0);
    float _S1991 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1992 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1992;
    bool omniPbrSpecular_0 = (_S173.w) >= 0.5f;
    float dielectricLevel_0 = _S159.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1993 = float3(dielectricLevel_0) ;
        float3 _S1994 = float3(metallic_0) ;
        float3 _S1995 = mix(_S1993, diffuseColor_0, _S1994);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1993, diffuseColor_0, _S1994);
        grazingIncidence_0 = _S1995;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S165.w) >= 0.5f)
        {
            float3 _S1996 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1996;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1997 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1997);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1997);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1998 = _S2;
    float _S1999 = _S2.ambientLight_0.w;
    if(_S1999 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S2000 = _S1998.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S2000, _S2000)) > 0.0f;
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
    uint _S2001 = min(uint(_S1999), 128U);
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
    float4 _S2002 = float4(_S6->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S2002.w) ;
    uint domeLinkMask_0 = uint(max(_S369.x, 0.0f));
    FragmentFrameParameters_0 _S2003 = _S2;
    uint _S2004 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S2004) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S2005 = _S1998.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S2006 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S2006;
        for(;;)
        {
            if(lightCount_0 < _S2004)
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
        domeAmbient_1 = _S2005;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S2007 = !hasSceneLighting_0;
    if(_S2007)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S2001;
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
        bool _S2008;
        if(hasSceneLighting_0)
        {
            _S2008 = (((uint4(_S6->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S2008 = false;
        }
        if(_S2008)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S2009 = lightIndex_0 == 0U;
        bool _S2010;
        if(_S2009)
        {
            _S2010 = _S2007;
        }
        else
        {
            _S2010 = false;
        }
        if(_S2010)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S2011 = 224U + lightIndex_0 * 16U;
            float _S2012 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2011)>>2]);
            float _S2013 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2011 + 4U)>>2]);
            float _S2014 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2011 + 8U)>>2]);
            float _S2015 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2011 + 12U)>>2]);
            lightType_0 = _S2015;
        }
        bool _S2016;
        if(_S2009)
        {
            _S2016 = _S2007;
        }
        else
        {
            _S2016 = false;
        }
        if(_S2016)
        {
            lightDirection_0 = normalize((float4(_S6->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S2017 = 2272U + lightIndex_0 * 16U;
            float _S2018 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2017)>>2]);
            float _S2019 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2017 + 4U)>>2]);
            float _S2020 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2017 + 8U)>>2]);
            float _S2021 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2017 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S2018, _S2019, _S2020));
        }
        bool _S2022;
        if(_S2009)
        {
            _S2022 = _S2007;
        }
        else
        {
            _S2022 = false;
        }
        if(_S2022)
        {
            roughness_0 = (float4(_S6->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S2023 = 4320U + lightIndex_0 * 16U;
            float _S2024 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2023)>>2]);
            float _S2025 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2023 + 4U)>>2]);
            float _S2026 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2023 + 8U)>>2]);
            float _S2027 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2023 + 12U)>>2]);
            roughness_0 = _S2027;
        }
        bool _S2028;
        if(_S2009)
        {
            _S2028 = _S2007;
        }
        else
        {
            _S2028 = false;
        }
        if(_S2028)
        {
            diffuseColor_0 = _S2002.xyz;
        }
        else
        {
            uint _S2029 = 4320U + lightIndex_0 * 16U;
            float _S2030 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2029)>>2]);
            float _S2031 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2029 + 4U)>>2]);
            float _S2032 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2029 + 8U)>>2]);
            float _S2033 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2029 + 12U)>>2]);
            diffuseColor_0 = float3(_S2030, _S2031, _S2032);
        }
        bool _S2034;
        if(_S2009)
        {
            _S2034 = _S2007;
        }
        else
        {
            _S2034 = false;
        }
        if(_S2034)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S2035 = 6368U + lightIndex_0 * 16U;
            float _S2036 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2035)>>2]);
            float _S2037 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2035 + 4U)>>2]);
            float _S2038 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2035 + 8U)>>2]);
            float _S2039 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2035 + 12U)>>2]);
            metallic_0 = _S2036;
        }
        bool _S2040;
        if(_S2009)
        {
            _S2040 = _S2007;
        }
        else
        {
            _S2040 = false;
        }
        if(_S2040)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S2041 = 6368U + lightIndex_0 * 16U;
            float _S2042 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2041)>>2]);
            float _S2043 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2041 + 4U)>>2]);
            float _S2044 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2041 + 8U)>>2]);
            float _S2045 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2041 + 12U)>>2]);
            clearcoatRoughness_0 = _S2043;
        }
        bool _S2046;
        if(_S2009)
        {
            _S2046 = _S2007;
        }
        else
        {
            _S2046 = false;
        }
        if(_S2046)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S2047 = 8416U + lightIndex_0 * 16U;
            float _S2048 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2047)>>2]);
            float _S2049 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2047 + 4U)>>2]);
            float _S2050 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2047 + 8U)>>2]);
            float _S2051 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2047 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S2048, _S2049, _S2050));
        }
        bool _S2052;
        if(_S2009)
        {
            _S2052 = _S2007;
        }
        else
        {
            _S2052 = false;
        }
        if(_S2052)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S2053 = 10464U + lightIndex_0 * 16U;
            float _S2054 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2053)>>2]);
            float _S2055 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2053 + 4U)>>2]);
            float _S2056 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2053 + 8U)>>2]);
            float _S2057 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2053 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S2054, _S2055, _S2056));
        }
        bool _S2058;
        if(_S2009)
        {
            _S2058 = _S2007;
        }
        else
        {
            _S2058 = false;
        }
        float shapeX_0;
        if(_S2058)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S2059 = 8416U + lightIndex_0 * 16U;
            float _S2060 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2059)>>2]);
            float _S2061 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2059 + 4U)>>2]);
            float _S2062 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2059 + 8U)>>2]);
            float _S2063 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2059 + 12U)>>2]);
            shapeX_0 = _S2063;
        }
        bool _S2064;
        if(_S2009)
        {
            _S2064 = _S2007;
        }
        else
        {
            _S2064 = false;
        }
        float shapeY_0;
        if(_S2064)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S2065 = 10464U + lightIndex_0 * 16U;
            float _S2066 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2065)>>2]);
            float _S2067 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2065 + 4U)>>2]);
            float _S2068 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2065 + 8U)>>2]);
            float _S2069 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2065 + 12U)>>2]);
            shapeY_0 = _S2069;
        }
        bool _S2070;
        if(_S2009)
        {
            _S2070 = _S2007;
        }
        else
        {
            _S2070 = false;
        }
        float lightRadius_0;
        if(_S2070)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S2071 = 2272U + lightIndex_0 * 16U;
            float _S2072 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2071)>>2]);
            float _S2073 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2071 + 4U)>>2]);
            float _S2074 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2071 + 8U)>>2]);
            float _S2075 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2071 + 12U)>>2]);
            lightRadius_0 = _S2075;
        }
        bool _S2076;
        if(_S2009)
        {
            _S2076 = _S2007;
        }
        else
        {
            _S2076 = false;
        }
        if(_S2076)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S2077;
        if(_S2009)
        {
            _S2077 = _S2007;
        }
        else
        {
            _S2077 = false;
        }
        if(_S2077)
        {
            _S182 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S2078 = 15296U + lightIndex_0 * 16U;
            float _S2079 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2078)>>2]);
            float _S2080 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2078 + 4U)>>2]);
            float _S2081 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2078 + 8U)>>2]);
            float _S2082 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2078 + 12U)>>2]);
            _S182 = float4(_S2079, _S2080, _S2081, _S2082);
        }
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S2083 = 12960U + lightIndex_0 * 16U;
            float _S2084 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2083)>>2]);
            float _S2085 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2083 + 4U)>>2]);
            float _S2086 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2083 + 8U)>>2]);
            float _S2087 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2083 + 12U)>>2]);
            int shadowSlot_0 = int(_S2084);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S2088 = _S2;
                    FragmentFrameParameters_0 _S2089 = _S2;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - specularColor_0;
                    }
                    else
                    {
                        color_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S2088.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S2090 = lightClip_0.w;
                    if(_S2090 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S2090) ;
                    bool _S2091;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S2091 = true;
                    }
                    else
                    {
                        _S2091 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S2092;
                    if(_S2091)
                    {
                        _S2092 = true;
                    }
                    else
                    {
                        _S2092 = (ndc_0.z) < 0.0f;
                    }
                    bool _S2093;
                    if(_S2092)
                    {
                        _S2093 = true;
                    }
                    else
                    {
                        _S2093 = (ndc_0.z) > 1.0f;
                    }
                    if(_S2093)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S2094 = _S2089.shadowTile_0[shadowSlot_0].xy;
                    float2 _S2095 = _S2089.shadowTile_0[shadowSlot_0].zw;
                    float2 _S2096 = _S2094 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S2095;
                    float texel_0 = _S2088.shadowControls_0[shadowSlot_0].w;
                    float _S2097 = max(_S2088.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S2098 = ndc_0.z - _S2088.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S2099 = float2((texel_0 * 0.5f)) ;
                    float2 _S2100 = _S2094 + _S2099;
                    float2 _S2101 = _S2094 + _S2095 - _S2099;
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
                            if(_S2098 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S2096 + float2(float(x_0), float(y_0)) * float2((_S2097 * texel_0)) , _S2100, _S2101)), level((0.0f))).x))
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
        float3 _S2102 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S2102;
        sampleOffsets_0[int(1)] = _S2102;
        sampleOffsets_0[int(2)] = _S2102;
        sampleOffsets_0[int(3)] = _S2102;
        sampleOffsets_0[int(4)] = _S2102;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S2103 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S2103 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S2103 - halfHeight_0;
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
                uint _S2104 = 224U + lightIndex_0 * 16U;
                float _S2105 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2104)>>2]);
                float _S2106 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2104 + 4U)>>2]);
                float _S2107 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2104 + 8U)>>2]);
                float _S2108 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2104 + 12U)>>2]);
                float3 toLight_0 = float3(_S2105, _S2106, _S2107) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S2109 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S2109)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S2109;
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
                        float _S2110 = _S182.y;
                        if(_S2110 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S2110);
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
            float _S2111 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_0;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_0 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S2111)) ;
            }
            else
            {
                diffuseFresnel_0 = mix(normalIncidence_0, grazingIncidence_0, float3(_S2111) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_0);
            float3 _S2112 = float3(_S2111) ;
            float _S2113 = max(_S1990, 0.00100000004749745f);
            float alpha_0 = _S2113 * _S2113;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S2114 = 1.0f - k_0;
            float3 _S2115 = float3(max(4.0f * normalDotLight_0 * _S1989, 1.00000000317107685e-30f)) ;
            float3 _S2116 = mix(normalIncidence_0, grazingIncidence_0, _S2112) * float3((_S1989 / (_S1989 * _S2114 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S2114 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S2115;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S2117 = max(_S1991, 0.00100000004749745f);
                float alpha_1 = _S2117 * _S2117;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S2118 = 1.0f - k_1;
                directSpecular_0 = _S2116 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S2112) * float3((_S1989 / (_S1989 * _S2118 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S2118 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S2115);
            }
            else
            {
                directSpecular_0 = _S2116;
            }
            float3 _S2119 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S2119 * _S1992) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S2119);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S2120 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S2121 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S2122 = saturate(_S1990);
        float2 _S2123 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2121, _S2122)), level((0.0f)))).xy;
        float _S2124 = _S2123.x;
        float _S2125 = _S2123.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S2124)  + grazingIncidence_0 * float3(_S2125) ;
        if(omniPbrSpecular_0)
        {
            lightDirection_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S2124 + _S2125)) ;
        }
        else
        {
            lightDirection_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - lightDirection_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S2126 = max(_S2003.domeControls_0.y, 1.0f);
        float3 _S2127 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S2126 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S2003.domeControls_0.z;
            float _S2128 = _S2003.domeControls_0.w;
            for(;;)
            {
                bool _S2129 = _S2126 <= 1.0f;
                _S4 = _S2129;
                if(_S2129)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S2128, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S2126)), level((0.0f)))).xyz;
                break;
            }
            float _S2130 = _S2120.environmentControls_0.y;
            float _S2131 = _S2120.environmentControls_0.z;
            for(;;)
            {
                if(_S4)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2132 = max(_S2130, 1.0f);
                    float _S2133 = _S2132 - 1.0f;
                    float slice_0 = _S2122 * max(_S2133, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S2131, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S2132)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S2133) + v_0) / _S2132)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S2134 = max(_S2130, 1.0f);
                float total_0 = _S2134 * _S2126;
                float _S2135 = _S2134 - 1.0f;
                float slice_1 = _S2122 * max(_S2135, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S2131, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S2134;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S2135) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S2136 = max(_S2130, 1.0f);
                        float _S2137 = _S2136 - 1.0f;
                        float slice_2 = saturate(_S1991) * max(_S2137, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S2131, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S2136)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S2137) + v_2) / _S2136)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2138 = max(_S2130, 1.0f);
                    float total_1 = _S2138 * _S2126;
                    float _S2139 = _S2138 - 1.0f;
                    float slice_3 = saturate(_S1991) * max(_S2139, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S2131, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S2138;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S2139) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                color_0 = specularColor_0;
            }
            else
            {
                color_0 = _S2127;
            }
            lightTangent_0 = diffuseColor_0;
            lightBitangent_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightTangent_0 = _S2127;
            lightBitangent_0 = _S2127;
            color_0 = _S2127;
            for(;;)
            {
                if(sampleIndex_0 < _S2004)
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
                float _S2140 = _S2003.domeControls_0.w;
                for(;;)
                {
                    bool _S2141 = _S2126 <= 1.0f;
                    _S5 = _S2141;
                    if(_S2141)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S2140, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S2126)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightTangent_0 + diffuseColor_0;
                float _S2142 = _S2120.environmentControls_0.y;
                float _S2143 = _S2120.environmentControls_0.z;
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2144 = max(_S2142, 1.0f);
                        float _S2145 = _S2144 - 1.0f;
                        float slice_4 = _S2122 * max(_S2145, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S2143, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S2144)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S2145) + v_4) / _S2144)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2146 = max(_S2142, 1.0f);
                    float total_2 = _S2146 * _S2126;
                    float _S2147 = _S2146 - 1.0f;
                    float slice_5 = _S2122 * max(_S2147, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S2143, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S2146;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S2147) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
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
                            float _S2148 = max(_S2142, 1.0f);
                            float _S2149 = _S2148 - 1.0f;
                            float slice_6 = saturate(_S1991) * max(_S2149, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S2143, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S2148)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S2149) + v_6) / _S2148)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2150 = max(_S2142, 1.0f);
                        float total_3 = _S2150 * _S2126;
                        float _S2151 = _S2150 - 1.0f;
                        float slice_7 = saturate(_S1991) * max(_S2151, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S2143, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S2150;
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S2151) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S2152 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2121, saturate(_S1991))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * color_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S2152.x)  + float3(_S2152.y) );
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
    pixelOutput_0 _S2153 = { float4(color_3, opacity_0) };
    return _S2153;
}
