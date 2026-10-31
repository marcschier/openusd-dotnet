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
    packed_float4 alternateUvTransformRow0_0;
    packed_float4 alternateUvTransformRow1_0;
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
    float4 _S174 = float4(_S6->uvTransformRow1_0) ;
    uint secondaryMask_0 = uint(round(_S174.w));
    float4 _S175 = float4(_S6->domeLinkControls_0) ;
    uint alternateMask_0 = uint(round(_S175.w));
    float4 _S176 = float4(_S6->uvTransformRow0_0) ;
    float2 _S177 = _S176.xy;
    float _S178 = _S176.z;
    float2 _S179 = _S174.xy;
    float _S180 = _S174.z;
    float2 _S181 = float2(dot(_S177, _S1.texCoord_0) + _S178, dot(_S179, _S1.texCoord_0) + _S180);
    float2 _S182 = float2(dot(_S177, _S1.texCoord1_0) + _S178, dot(_S179, _S1.texCoord1_0) + _S180);
    float4 _S183 = float4(_S6->alternateUvTransformRow0_0) ;
    float4 _S184 = float4(_S6->alternateUvTransformRow1_0) ;
    float2 _S185 = _S183.xy;
    float _S186 = _S183.z;
    float2 _S187 = _S184.xy;
    float _S188 = _S184.z;
    float2 _S189 = float2(dot(_S185, _S1.texCoord_0) + _S186, dot(_S187, _S1.texCoord_0) + _S188);
    float2 _S190 = float2(dot(_S185, _S1.texCoord1_0) + _S186, dot(_S187, _S1.texCoord1_0) + _S188);
    uint lightCount_0;
    float roughness_0;
    float metallic_0;
    float2 detailSource_0;
    float2 tangent_1;
    bool hasSceneLighting_0;
    float4 _S191;
    uint3 _S192;
    if((textureMask_0 & 2U) != 0U)
    {
        bool useSecondary_0 = (secondaryMask_0 & 2U) != 0U;
        if((alternateMask_0 & 2U) != 0U)
        {
            if(useSecondary_0)
            {
                detailSource_0 = _S190;
            }
            else
            {
                detailSource_0 = _S189;
            }
        }
        else
        {
            if(useSecondary_0)
            {
                detailSource_0 = _S182;
            }
            else
            {
                detailSource_0 = _S181;
            }
        }
        thread float4 diffuseSample_0;
        bool _S193 = (udimMask_0 & 2U) != 0U;
        uint maskU_0 = uint(round(_S175.y));
        uint maskV_0 = uint(round(_S175.z));
        bool _S194 = (maskU_0 & 2U) != 0U;
        if(_S194)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S195 = (maskV_0 & 2U) != 0U;
        if(_S195)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S196 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S193)
            {
                for(;;)
                {
                    if(_S196 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S197 = (&kernelContext_0)->baseColorTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S197).get_width(0)),(*((&height_0)) = (_S197).get_height(0));
                    float2 size_0 = float2(float(width_0), float(height_0));
                    float2 _S198 = float2(0.5f) ;
                    float2 remapped_0 = clamp((detailSource_0 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S198 / size_0, (size_0 - _S198) / size_0);
                    if((_S196 & 1U) != 0U)
                    {
                        roughness_0 = remapped_0.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S196 & 2U) != 0U)
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
                _S191 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S199 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S199).get_width(0)),(*((&atlasHeight_0)) = (_S199).get_height(0));
            int3 _S200 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S200)).xy), uint(((_S200)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S201 = atlasWidth_0;
                uint _S202 = 2U % atlasWidth_0;
                int _S203 = int(_S202);
                uint _S204 = 2U / _S201;
                int3 _S205 = int3(_S203, int(_S204), int(0));
                int _S206 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S205)).xy), uint(((_S205)).z))).x * 255.0f)));
                uint _S207 = atlasWidth_0;
                uint _S208 = 3U % atlasWidth_0;
                int _S209 = int(_S208);
                uint _S210 = 3U / _S207;
                int3 _S211 = int3(_S209, int(_S210), int(0));
                int2 minimumTile_0 = int2(_S206, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S211)).xy), uint(((_S211)).z))).x * 255.0f))));
                uint _S212 = atlasWidth_0;
                uint _S213 = 4U % atlasWidth_0;
                int _S214 = int(_S213);
                uint _S215 = 4U / _S212;
                int3 _S216 = int3(_S214, int(_S215), int(0));
                uint _S217 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S216)).xy), uint(((_S216)).z))).x * 255.0f));
                uint _S218 = atlasWidth_0;
                uint _S219 = 5U % atlasWidth_0;
                int _S220 = int(_S219);
                uint _S221 = 5U / _S218;
                int3 _S222 = int3(_S220, int(_S221), int(0));
                uint _S223 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S222)).xy), uint(((_S222)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S217, _S223);
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
                    int3 _S224 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S224)).xy), uint(((_S224)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S217 + uint(tile_0.x));
                uint _S225 = atlasWidth_0;
                uint _S226 = offset_0 % atlasWidth_0;
                int _S227 = int(_S226);
                uint _S228 = offset_0 / _S225;
                int3 _S229 = int3(_S227, int(_S228), int(0));
                uint _S230 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S229)).xy), uint(((_S229)).z))).x * 255.0f));
                uint _S231 = atlasWidth_0;
                uint _S232 = offset_0 + 1U;
                uint _S233 = _S232 % atlasWidth_0;
                int _S234 = int(_S233);
                uint _S235 = _S232 / _S231;
                int3 _S236 = int3(_S234, int(_S235), int(0));
                uint slot_0 = _S230 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S236)).xy), uint(((_S236)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S237 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S237)).xy), uint(((_S237)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S238 = atlasWidth_0;
                uint _S239 = 6U % atlasWidth_0;
                int _S240 = int(_S239);
                uint _S241 = 6U / _S238;
                int3 _S242 = int3(_S240, int(_S241), int(0));
                uint _S243 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S242)).xy), uint(((_S242)).z))).x * 255.0f));
                uint _S244 = atlasWidth_0;
                uint _S245 = 7U % atlasWidth_0;
                int _S246 = int(_S245);
                uint _S247 = 7U / _S244;
                int3 _S248 = int3(_S246, int(_S247), int(0));
                uint columns_0 = _S243 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S248)).xy), uint(((_S248)).z))).x * 255.0f)) << 8U);
                uint _S249 = atlasWidth_0;
                uint _S250 = 8U % atlasWidth_0;
                int _S251 = int(_S250);
                uint _S252 = 8U / _S249;
                int3 _S253 = int3(_S251, int(_S252), int(0));
                uint _S254 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S253)).xy), uint(((_S253)).z))).x * 255.0f));
                uint _S255 = atlasWidth_0;
                uint _S256 = 9U % atlasWidth_0;
                int _S257 = int(_S256);
                uint _S258 = 9U / _S255;
                int3 _S259 = int3(_S257, int(_S258), int(0));
                uint rows_0 = ((_S254 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S259)).xy), uint(((_S259)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S217 * _S223 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S260 = atlasWidth_0 / columns_0;
                float _S261 = float(_S260);
                uint _S262 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S261, float(_S262));
                uint _S263 = slot_1 % columns_0;
                float _S264 = float(_S263);
                uint _S265 = slot_1 / columns_0;
                _S191 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S264, float(_S265)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(detailSource_0) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S266 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(detailSource_0)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S266);
            }
            if(hasSceneLighting_0)
            {
                int3 _S267 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S191 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S267)).xy), uint(((_S267)).z)));
                break;
            }
            uint _S268 = atlasWidth_0 / uint(_S266.x);
            float _S269 = float(_S268);
            uint _S270 = (atlasHeight_0 - 1U) / uint(_S266.y);
            float2 cellSize_1 = float2(_S269, float(_S270));
            _S191 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S191;
        float4 _S271 = float4(_S6->compositeControls_0) ;
        float _S272 = _S271.x;
        if(_S272 == 0.0f)
        {
            hasSceneLighting_0 = (_S271.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S273 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S273).get_width(0)),(*((&sourceHeight_0)) = (_S273).get_height(0));
            texture2d<float, access::sample> _S274 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S274).get_width(0)),(*((&valueHeight_0)) = (_S274).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S275 = diffuseSample_0;
                for(;;)
                {
                    float _S276 = _S275.w;
                    if(_S276 > 1.0f)
                    {
                        diffuseColor_0 = _S275.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S277 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S277).get_width(0)),(*((&height_1)) = (_S277).get_height(0));
                    uint3 _S278 = uint3(round(_S275.xyz * float3(255.0f) ));
                    uint index_1 = ((_S278.x) | ((_S278.y) << 8U)) | ((_S278.z) << 16U);
                    if(_S276 == 0.0f)
                    {
                        _S192 = _S278;
                    }
                    else
                    {
                        _S192 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S192 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S279 = address_0.x;
                    uint _S280 = _S279 % width_1;
                    int _S281 = int(_S280);
                    uint _S282 = _S279 / width_1;
                    int3 _S283 = int3(_S281, int(_S282), int(0));
                    float _S284 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S283)).xy), uint(((_S283)).z))).x;
                    uint _S285 = address_0.y;
                    uint _S286 = _S285 % width_1;
                    int _S287 = int(_S286);
                    uint _S288 = _S285 / width_1;
                    int3 _S289 = int3(_S287, int(_S288), int(0));
                    float _S290 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S289)).xy), uint(((_S289)).z))).x;
                    uint _S291 = address_0.z;
                    uint _S292 = _S291 % width_1;
                    int _S293 = int(_S292);
                    uint _S294 = _S291 / width_1;
                    int3 _S295 = int3(_S293, int(_S294), int(0));
                    diffuseColor_0 = float3(_S284, _S290, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S295)).xy), uint(((_S295)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                if(_S194)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S195)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S296 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S296 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S297 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S297).get_width(0)),(*((&height_2)) = (_S297).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S298 = float2(0.5f) ;
                    float2 remapped_1 = clamp((detailSource_0 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S298 / size_1, (size_1 - _S298) / size_1);
                    if((_S296 & 1U) != 0U)
                    {
                        roughness_0 = remapped_1.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S296 & 2U) != 0U)
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
        float4 _S299 = diffuseSample_0;
        for(;;)
        {
            if(_S272 != 2.0f)
            {
                _S191 = _S299;
                break;
            }
            bool _S300 = (_S271.w) >= 0.5f;
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
            uint _S301 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S300)
                {
                    for(;;)
                    {
                        if(_S301 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S302 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_3;
                        thread uint height_3;
                        (*((&width_3)) = (_S302).get_width(0)),(*((&height_3)) = (_S302).get_height(0));
                        float2 size_2 = float2(float(width_3), float(height_3));
                        float2 _S303 = float2(0.5f) ;
                        float2 remapped_2 = clamp((detailSource_0 * (size_2 - float2(2.0f) ) + float2(1.0f) ) / size_2, _S303 / size_2, (size_2 - _S303) / size_2);
                        if((_S301 & 1U) != 0U)
                        {
                            roughness_0 = remapped_2.x;
                        }
                        else
                        {
                            roughness_0 = detailSource_0.x;
                        }
                        if((_S301 & 2U) != 0U)
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
                    _S191 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S304 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S304).get_width(0)),(*((&atlasHeight_1)) = (_S304).get_height(0));
                int3 _S305 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S305)).xy), uint(((_S305)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S306 = atlasWidth_1;
                    uint _S307 = 2U % atlasWidth_1;
                    int _S308 = int(_S307);
                    uint _S309 = 2U / _S306;
                    int3 _S310 = int3(_S308, int(_S309), int(0));
                    int _S311 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S310)).xy), uint(((_S310)).z))).x * 255.0f)));
                    uint _S312 = atlasWidth_1;
                    uint _S313 = 3U % atlasWidth_1;
                    int _S314 = int(_S313);
                    uint _S315 = 3U / _S312;
                    int3 _S316 = int3(_S314, int(_S315), int(0));
                    int2 minimumTile_1 = int2(_S311, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S316)).xy), uint(((_S316)).z))).x * 255.0f))));
                    uint _S317 = atlasWidth_1;
                    uint _S318 = 4U % atlasWidth_1;
                    int _S319 = int(_S318);
                    uint _S320 = 4U / _S317;
                    int3 _S321 = int3(_S319, int(_S320), int(0));
                    uint _S322 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S321)).xy), uint(((_S321)).z))).x * 255.0f));
                    uint _S323 = atlasWidth_1;
                    uint _S324 = 5U % atlasWidth_1;
                    int _S325 = int(_S324);
                    uint _S326 = 5U / _S323;
                    int3 _S327 = int3(_S325, int(_S326), int(0));
                    uint _S328 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S327)).xy), uint(((_S327)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S322, _S328);
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
                        int3 _S329 = int3(int(1), int(0), int(0));
                        _S191 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S329)).xy), uint(((_S329)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S322 + uint(tile_2.x));
                    uint _S330 = atlasWidth_1;
                    uint _S331 = offset_1 % atlasWidth_1;
                    int _S332 = int(_S331);
                    uint _S333 = offset_1 / _S330;
                    int3 _S334 = int3(_S332, int(_S333), int(0));
                    uint _S335 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S334)).xy), uint(((_S334)).z))).x * 255.0f));
                    uint _S336 = atlasWidth_1;
                    uint _S337 = offset_1 + 1U;
                    uint _S338 = _S337 % atlasWidth_1;
                    int _S339 = int(_S338);
                    uint _S340 = _S337 / _S336;
                    int3 _S341 = int3(_S339, int(_S340), int(0));
                    uint slot_2 = _S335 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S341)).xy), uint(((_S341)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S342 = int3(int(1), int(0), int(0));
                        _S191 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S342)).xy), uint(((_S342)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S343 = atlasWidth_1;
                    uint _S344 = 6U % atlasWidth_1;
                    int _S345 = int(_S344);
                    uint _S346 = 6U / _S343;
                    int3 _S347 = int3(_S345, int(_S346), int(0));
                    uint _S348 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S347)).xy), uint(((_S347)).z))).x * 255.0f));
                    uint _S349 = atlasWidth_1;
                    uint _S350 = 7U % atlasWidth_1;
                    int _S351 = int(_S350);
                    uint _S352 = 7U / _S349;
                    int3 _S353 = int3(_S351, int(_S352), int(0));
                    uint columns_1 = _S348 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S353)).xy), uint(((_S353)).z))).x * 255.0f)) << 8U);
                    uint _S354 = atlasWidth_1;
                    uint _S355 = 8U % atlasWidth_1;
                    int _S356 = int(_S355);
                    uint _S357 = 8U / _S354;
                    int3 _S358 = int3(_S356, int(_S357), int(0));
                    uint _S359 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S358)).xy), uint(((_S358)).z))).x * 255.0f));
                    uint _S360 = atlasWidth_1;
                    uint _S361 = 9U % atlasWidth_1;
                    int _S362 = int(_S361);
                    uint _S363 = 9U / _S360;
                    int3 _S364 = int3(_S362, int(_S363), int(0));
                    uint rows_1 = ((_S359 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S364)).xy), uint(((_S364)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S322 * _S328 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S365 = atlasWidth_1 / columns_1;
                    float _S366 = float(_S365);
                    uint _S367 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S366, float(_S367));
                    uint _S368 = slot_3 % columns_1;
                    float _S369 = float(_S368);
                    uint _S370 = slot_3 / columns_1;
                    _S191 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S369, float(_S370)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailSource_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S371 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(detailSource_0)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S371);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S372 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S191 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S372)).xy), uint(((_S372)).z)));
                    break;
                }
                uint _S373 = atlasWidth_1 / uint(_S371.x);
                float _S374 = float(_S373);
                uint _S375 = (atlasHeight_1 - 1U) / uint(_S371.y);
                float2 cellSize_3 = float2(_S374, float(_S375));
                _S191 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S271.y));
            if(operation_0 == 1U)
            {
                _S191 = _S299 * _S191;
                break;
            }
            if(operation_0 == 2U)
            {
                _S191 = _S299 + _S191;
                break;
            }
            if(operation_0 == 3U)
            {
                _S191 = _S299 - _S191;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S271.z;
                _S191 = _S299 * float4((1.0f - factor_0))  + _S191 * float4(factor_0) ;
                break;
            }
            _S191 = _S299;
            break;
        }
        diffuseColor_0 = _S191.xyz;
    }
    thread float4 baseNormalSample_0;
    bool useSecondary_1 = (secondaryMask_0 & 4U) != 0U;
    bool _S376 = (alternateMask_0 & 4U) != 0U;
    if(_S376)
    {
        if(useSecondary_1)
        {
            detailSource_0 = _S190;
        }
        else
        {
            detailSource_0 = _S189;
        }
    }
    else
    {
        if(useSecondary_1)
        {
            detailSource_0 = _S182;
        }
        else
        {
            detailSource_0 = _S181;
        }
    }
    bool _S377 = (udimMask_0 & 4U) != 0U;
    uint maskU_1 = uint(round(_S175.y));
    uint maskV_1 = uint(round(_S175.z));
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
    uint _S378 = index_0 | lightCount_0;
    for(;;)
    {
        if(!_S377)
        {
            for(;;)
            {
                if(_S378 == 0U)
                {
                    break;
                }
                texture2d<float, access::sample> _S379 = (&kernelContext_0)->normalTexture_0;
                thread uint width_4;
                thread uint height_4;
                (*((&width_4)) = (_S379).get_width(0)),(*((&height_4)) = (_S379).get_height(0));
                float2 size_3 = float2(float(width_4), float(height_4));
                float2 _S380 = float2(0.5f) ;
                float2 remapped_3 = clamp((detailSource_0 * (size_3 - float2(2.0f) ) + float2(1.0f) ) / size_3, _S380 / size_3, (size_3 - _S380) / size_3);
                if((_S378 & 1U) != 0U)
                {
                    roughness_0 = remapped_3.x;
                }
                else
                {
                    roughness_0 = detailSource_0.x;
                }
                if((_S378 & 2U) != 0U)
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
            _S191 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (detailSource_0)));
            break;
        }
        texture2d<float, access::sample> _S381 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S381).get_width(0)),(*((&atlasHeight_2)) = (_S381).get_height(0));
        int3 _S382 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S382)).xy), uint(((_S382)).z))) * float4(255.0f) );
        if((metadata_2.x) == 255.0f)
        {
            uint _S383 = atlasWidth_2;
            uint _S384 = 2U % atlasWidth_2;
            int _S385 = int(_S384);
            uint _S386 = 2U / _S383;
            int3 _S387 = int3(_S385, int(_S386), int(0));
            int _S388 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S387)).xy), uint(((_S387)).z))).x * 255.0f)));
            uint _S389 = atlasWidth_2;
            uint _S390 = 3U % atlasWidth_2;
            int _S391 = int(_S390);
            uint _S392 = 3U / _S389;
            int3 _S393 = int3(_S391, int(_S392), int(0));
            int2 minimumTile_2 = int2(_S388, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S393)).xy), uint(((_S393)).z))).x * 255.0f))));
            uint _S394 = atlasWidth_2;
            uint _S395 = 4U % atlasWidth_2;
            int _S396 = int(_S395);
            uint _S397 = 4U / _S394;
            int3 _S398 = int3(_S396, int(_S397), int(0));
            uint _S399 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S398)).xy), uint(((_S398)).z))).x * 255.0f));
            uint _S400 = atlasWidth_2;
            uint _S401 = 5U % atlasWidth_2;
            int _S402 = int(_S401);
            uint _S403 = 5U / _S400;
            int3 _S404 = int3(_S402, int(_S403), int(0));
            uint _S405 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S404)).xy), uint(((_S404)).z))).x * 255.0f));
            uint2 grid_2 = uint2(_S399, _S405);
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
                int3 _S406 = int3(int(1), int(0), int(0));
                _S191 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S406)).xy), uint(((_S406)).z)));
                break;
            }
            uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S399 + uint(tile_4.x));
            uint _S407 = atlasWidth_2;
            uint _S408 = offset_2 % atlasWidth_2;
            int _S409 = int(_S408);
            uint _S410 = offset_2 / _S407;
            int3 _S411 = int3(_S409, int(_S410), int(0));
            uint _S412 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S411)).xy), uint(((_S411)).z))).x * 255.0f));
            uint _S413 = atlasWidth_2;
            uint _S414 = offset_2 + 1U;
            uint _S415 = _S414 % atlasWidth_2;
            int _S416 = int(_S415);
            uint _S417 = _S414 / _S413;
            int3 _S418 = int3(_S416, int(_S417), int(0));
            uint slot_4 = _S412 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S418)).xy), uint(((_S418)).z))).x * 255.0f)) << 8U);
            if(slot_4 == 0U)
            {
                int3 _S419 = int3(int(1), int(0), int(0));
                _S191 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S419)).xy), uint(((_S419)).z)));
                break;
            }
            uint slot_5 = slot_4 - 1U;
            uint _S420 = atlasWidth_2;
            uint _S421 = 6U % atlasWidth_2;
            int _S422 = int(_S421);
            uint _S423 = 6U / _S420;
            int3 _S424 = int3(_S422, int(_S423), int(0));
            uint _S425 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S424)).xy), uint(((_S424)).z))).x * 255.0f));
            uint _S426 = atlasWidth_2;
            uint _S427 = 7U % atlasWidth_2;
            int _S428 = int(_S427);
            uint _S429 = 7U / _S426;
            int3 _S430 = int3(_S428, int(_S429), int(0));
            uint columns_2 = _S425 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S430)).xy), uint(((_S430)).z))).x * 255.0f)) << 8U);
            uint _S431 = atlasWidth_2;
            uint _S432 = 8U % atlasWidth_2;
            int _S433 = int(_S432);
            uint _S434 = 8U / _S431;
            int3 _S435 = int3(_S433, int(_S434), int(0));
            uint _S436 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S435)).xy), uint(((_S435)).z))).x * 255.0f));
            uint _S437 = atlasWidth_2;
            uint _S438 = 9U % atlasWidth_2;
            int _S439 = int(_S438);
            uint _S440 = 9U / _S437;
            int3 _S441 = int3(_S439, int(_S440), int(0));
            uint rows_2 = ((_S436 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S441)).xy), uint(((_S441)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
            uint metadataRows_2 = (10U + 2U * _S399 * _S405 + atlasWidth_2 - 1U) / atlasWidth_2;
            uint _S442 = atlasWidth_2 / columns_2;
            float _S443 = float(_S442);
            uint _S444 = (atlasHeight_2 - metadataRows_2) / rows_2;
            float2 cellSize_4 = float2(_S443, float(_S444));
            uint _S445 = slot_5 % columns_2;
            float _S446 = float(_S445);
            uint _S447 = slot_5 / columns_2;
            _S191 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S446, float(_S447)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(detailSource_0) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        int2 _S448 = int2(metadata_2.zw);
        int2 tile_5 = int2(floor(detailSource_0)) - int2(metadata_2.xy);
        if(any(tile_5 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_5 >= _S448);
        }
        if(hasSceneLighting_0)
        {
            int3 _S449 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S191 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S449)).xy), uint(((_S449)).z)));
            break;
        }
        uint _S450 = atlasWidth_2 / uint(_S448.x);
        float _S451 = float(_S450);
        uint _S452 = (atlasHeight_2 - 1U) / uint(_S448.y);
        float2 cellSize_5 = float2(_S451, float(_S452));
        _S191 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    baseNormalSample_0 = _S191;
    float4 _S453 = float4(_S6->detailUvTransformRow0_0) ;
    if((_S453.w) != 0.0f)
    {
        float4 _S454 = baseNormalSample_0;
        for(;;)
        {
            float _S455 = _S454.w;
            if(_S455 > 1.0f)
            {
                unlitColor_0 = _S454.xyz;
                break;
            }
            texture2d<float, access::sample> _S456 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_5;
            thread uint height_5;
            (*((&width_5)) = (_S456).get_width(0)),(*((&height_5)) = (_S456).get_height(0));
            uint3 _S457 = uint3(round(_S454.xyz * float3(255.0f) ));
            uint index_2 = ((_S457.x) | ((_S457.y) << 8U)) | ((_S457.z) << 16U);
            if(_S455 == 0.0f)
            {
                _S192 = _S457;
            }
            else
            {
                _S192 = uint3(index_2, index_2, index_2);
            }
            uint3 address_1 = _S192 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S458 = address_1.x;
            uint _S459 = _S458 % width_5;
            int _S460 = int(_S459);
            uint _S461 = _S458 / width_5;
            int3 _S462 = int3(_S460, int(_S461), int(0));
            float _S463 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S462)).xy), uint(((_S462)).z))).x;
            uint _S464 = address_1.y;
            uint _S465 = _S464 % width_5;
            int _S466 = int(_S465);
            uint _S467 = _S464 / width_5;
            int3 _S468 = int3(_S466, int(_S467), int(0));
            float _S469 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S468)).xy), uint(((_S468)).z))).x;
            uint _S470 = address_1.z;
            uint _S471 = _S470 % width_5;
            int _S472 = int(_S471);
            uint _S473 = _S470 / width_5;
            int3 _S474 = int3(_S472, int(_S473), int(0));
            unlitColor_0 = float3(_S463, _S469, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S474)).xy), uint(((_S474)).z))).x);
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
    if(_S376)
    {
        detailSource_0 = _S185;
    }
    else
    {
        detailSource_0 = _S177;
    }
    if(_S376)
    {
        tangent_1 = _S187;
    }
    else
    {
        tangent_1 = _S179;
    }
    thread float3 _S475 = unlitColor_0;
    float2 _S476 = float2(0.0f, 0.0f);
    if(any(tangent_1 != _S476))
    {
        float _S477 = tangent_1.x;
        float _S478 = tangent_1.y;
        float2 tangent_2 = normalize(float2(_S478, - _S477) / float2(max(abs(_S477), abs(_S478))) );
        float2 bitangent_0 = float2(- tangent_2.y, tangent_2.x);
        if((dot(detailSource_0, tangent_2)) < 0.0f)
        {
            detailSource_0 = - tangent_2;
        }
        else
        {
            detailSource_0 = tangent_2;
        }
        _S475.xy = detailSource_0 * float2(_S475.x)  + bitangent_0 * float2(_S475.y) ;
    }
    float3 _S479 = _S475;
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
        float2 _S480 = _S453.xy;
        float _S481 = dot(_S480, detailSource_0) + _S453.z;
        float4 _S482 = float4(_S6->detailUvTransformRow1_0) ;
        float2 _S483 = _S482.xy;
        float _S484 = dot(_S483, detailSource_0) + _S482.z;
        float2 detailUv_0 = float2(_S481, _S484);
        thread float4 detailSample_0;
        bool _S485 = (udimMask_0 & 4096U) != 0U;
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
        uint _S486 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S485)
            {
                for(;;)
                {
                    if(_S486 == 0U)
                    {
                        detailSource_0 = detailUv_0;
                        break;
                    }
                    texture2d<float, access::sample> _S487 = (&kernelContext_0)->detailNormalTexture_0;
                    thread uint width_6;
                    thread uint height_6;
                    (*((&width_6)) = (_S487).get_width(0)),(*((&height_6)) = (_S487).get_height(0));
                    float2 size_4 = float2(float(width_6), float(height_6));
                    float2 _S488 = float2(0.5f) ;
                    float2 remapped_4 = clamp((detailUv_0 * (size_4 - float2(2.0f) ) + float2(1.0f) ) / size_4, _S488 / size_4, (size_4 - _S488) / size_4);
                    if((_S486 & 1U) != 0U)
                    {
                        roughness_0 = remapped_4.x;
                    }
                    else
                    {
                        roughness_0 = _S481;
                    }
                    if((_S486 & 2U) != 0U)
                    {
                        metallic_0 = remapped_4.y;
                    }
                    else
                    {
                        metallic_0 = _S484;
                    }
                    detailSource_0 = float2(roughness_0, metallic_0);
                    break;
                }
                _S191 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S489 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S489).get_width(0)),(*((&atlasHeight_3)) = (_S489).get_height(0));
            int3 _S490 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S490)).xy), uint(((_S490)).z))) * float4(255.0f) );
            if((metadata_3.x) == 255.0f)
            {
                uint _S491 = atlasWidth_3;
                uint _S492 = 2U % atlasWidth_3;
                int _S493 = int(_S492);
                uint _S494 = 2U / _S491;
                int3 _S495 = int3(_S493, int(_S494), int(0));
                int _S496 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S495)).xy), uint(((_S495)).z))).x * 255.0f)));
                uint _S497 = atlasWidth_3;
                uint _S498 = 3U % atlasWidth_3;
                int _S499 = int(_S498);
                uint _S500 = 3U / _S497;
                int3 _S501 = int3(_S499, int(_S500), int(0));
                int2 minimumTile_3 = int2(_S496, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S501)).xy), uint(((_S501)).z))).x * 255.0f))));
                uint _S502 = atlasWidth_3;
                uint _S503 = 4U % atlasWidth_3;
                int _S504 = int(_S503);
                uint _S505 = 4U / _S502;
                int3 _S506 = int3(_S504, int(_S505), int(0));
                uint _S507 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S506)).xy), uint(((_S506)).z))).x * 255.0f));
                uint _S508 = atlasWidth_3;
                uint _S509 = 5U % atlasWidth_3;
                int _S510 = int(_S509);
                uint _S511 = 5U / _S508;
                int3 _S512 = int3(_S510, int(_S511), int(0));
                uint _S513 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S512)).xy), uint(((_S512)).z))).x * 255.0f));
                uint2 grid_3 = uint2(_S507, _S513);
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
                    int3 _S514 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S514)).xy), uint(((_S514)).z)));
                    break;
                }
                uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S507 + uint(tile_6.x));
                uint _S515 = atlasWidth_3;
                uint _S516 = offset_3 % atlasWidth_3;
                int _S517 = int(_S516);
                uint _S518 = offset_3 / _S515;
                int3 _S519 = int3(_S517, int(_S518), int(0));
                uint _S520 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S519)).xy), uint(((_S519)).z))).x * 255.0f));
                uint _S521 = atlasWidth_3;
                uint _S522 = offset_3 + 1U;
                uint _S523 = _S522 % atlasWidth_3;
                int _S524 = int(_S523);
                uint _S525 = _S522 / _S521;
                int3 _S526 = int3(_S524, int(_S525), int(0));
                uint slot_6 = _S520 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S526)).xy), uint(((_S526)).z))).x * 255.0f)) << 8U);
                if(slot_6 == 0U)
                {
                    int3 _S527 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S527)).xy), uint(((_S527)).z)));
                    break;
                }
                uint slot_7 = slot_6 - 1U;
                uint _S528 = atlasWidth_3;
                uint _S529 = 6U % atlasWidth_3;
                int _S530 = int(_S529);
                uint _S531 = 6U / _S528;
                int3 _S532 = int3(_S530, int(_S531), int(0));
                uint _S533 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S532)).xy), uint(((_S532)).z))).x * 255.0f));
                uint _S534 = atlasWidth_3;
                uint _S535 = 7U % atlasWidth_3;
                int _S536 = int(_S535);
                uint _S537 = 7U / _S534;
                int3 _S538 = int3(_S536, int(_S537), int(0));
                uint columns_3 = _S533 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S538)).xy), uint(((_S538)).z))).x * 255.0f)) << 8U);
                uint _S539 = atlasWidth_3;
                uint _S540 = 8U % atlasWidth_3;
                int _S541 = int(_S540);
                uint _S542 = 8U / _S539;
                int3 _S543 = int3(_S541, int(_S542), int(0));
                uint _S544 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S543)).xy), uint(((_S543)).z))).x * 255.0f));
                uint _S545 = atlasWidth_3;
                uint _S546 = 9U % atlasWidth_3;
                int _S547 = int(_S546);
                uint _S548 = 9U / _S545;
                int3 _S549 = int3(_S547, int(_S548), int(0));
                uint rows_3 = ((_S544 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S549)).xy), uint(((_S549)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                uint metadataRows_3 = (10U + 2U * _S507 * _S513 + atlasWidth_3 - 1U) / atlasWidth_3;
                uint _S550 = atlasWidth_3 / columns_3;
                float _S551 = float(_S550);
                uint _S552 = (atlasHeight_3 - metadataRows_3) / rows_3;
                float2 cellSize_6 = float2(_S551, float(_S552));
                uint _S553 = slot_7 % columns_3;
                float _S554 = float(_S553);
                uint _S555 = slot_7 / columns_3;
                _S191 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S554, float(_S555)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(detailUv_0) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            int2 _S556 = int2(metadata_3.zw);
            int2 tile_7 = int2(floor(detailUv_0)) - int2(metadata_3.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S556);
            }
            if(hasSceneLighting_0)
            {
                int3 _S557 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S191 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S557)).xy), uint(((_S557)).z)));
                break;
            }
            uint _S558 = atlasWidth_3 / uint(_S556.x);
            float _S559 = float(_S558);
            uint _S560 = (atlasHeight_3 - 1U) / uint(_S556.y);
            float2 cellSize_7 = float2(_S559, float(_S560));
            _S191 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        detailSample_0 = _S191;
        if((_S482.w) != 0.0f)
        {
            float4 _S561 = detailSample_0;
            for(;;)
            {
                float _S562 = _S561.w;
                if(_S562 > 1.0f)
                {
                    unlitColor_0 = _S561.xyz;
                    break;
                }
                texture2d<float, access::sample> _S563 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_7;
                thread uint height_7;
                (*((&width_7)) = (_S563).get_width(0)),(*((&height_7)) = (_S563).get_height(0));
                uint3 _S564 = uint3(round(_S561.xyz * float3(255.0f) ));
                uint index_3 = ((_S564.x) | ((_S564.y) << 8U)) | ((_S564.z) << 16U);
                if(_S562 == 0.0f)
                {
                    _S192 = _S564;
                }
                else
                {
                    _S192 = uint3(index_3, index_3, index_3);
                }
                uint3 address_2 = _S192 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S565 = address_2.x;
                uint _S566 = _S565 % width_7;
                int _S567 = int(_S566);
                uint _S568 = _S565 / width_7;
                int3 _S569 = int3(_S567, int(_S568), int(0));
                float _S570 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S569)).xy), uint(((_S569)).z))).x;
                uint _S571 = address_2.y;
                uint _S572 = _S571 % width_7;
                int _S573 = int(_S572);
                uint _S574 = _S571 / width_7;
                int3 _S575 = int3(_S573, int(_S574), int(0));
                float _S576 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S575)).xy), uint(((_S575)).z))).x;
                uint _S577 = address_2.z;
                uint _S578 = _S577 % width_7;
                int _S579 = int(_S578);
                uint _S580 = _S577 / width_7;
                int3 _S581 = int3(_S579, int(_S580), int(0));
                unlitColor_0 = float3(_S570, _S576, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S581)).xy), uint(((_S581)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S582 = detail_0;
        if(any(_S483 != _S476))
        {
            float _S583 = _S483.x;
            float _S584 = _S483.y;
            float2 tangent_3 = normalize(float2(_S584, - _S583) / float2(max(abs(_S583), abs(_S584))) );
            float2 bitangent_1 = float2(- tangent_3.y, tangent_3.x);
            if((dot(_S480, tangent_3)) < 0.0f)
            {
                tangent_1 = - tangent_3;
            }
            else
            {
                tangent_1 = tangent_3;
            }
            _S582.xy = tangent_1 * float2(_S582.x)  + bitangent_1 * float2(_S582.y) ;
        }
        float3 _S585 = _S582;
        for(;;)
        {
            float3 t_0 = normalize(_S479) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S585) * float3(-1.0f, -1.0f, 1.0f);
            float _S586 = t_0.z;
            if(_S586 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S586))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S479;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S587 = float3(unlitColor_0.x) ;
    float3 _S588 = float3(unlitColor_0.y) ;
    float3 _S589 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S587 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S588 + _S1.normal_0 * _S589);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S587 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S588 + worldNormalBasis_0 * _S589);
    float4 _S590;
    if((textureMask_0 & 8U) != 0U)
    {
        bool useSecondary_2 = (secondaryMask_0 & 8U) != 0U;
        if((alternateMask_0 & 8U) != 0U)
        {
            if(useSecondary_2)
            {
                detailSource_0 = _S190;
            }
            else
            {
                detailSource_0 = _S189;
            }
        }
        else
        {
            if(useSecondary_2)
            {
                detailSource_0 = _S182;
            }
            else
            {
                detailSource_0 = _S181;
            }
        }
        bool _S591 = (udimMask_0 & 8U) != 0U;
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
        uint _S592 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S591)
            {
                for(;;)
                {
                    if(_S592 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S593 = (&kernelContext_0)->roughnessMetallicTexture_0;
                    thread uint width_8;
                    thread uint height_8;
                    (*((&width_8)) = (_S593).get_width(0)),(*((&height_8)) = (_S593).get_height(0));
                    float2 size_5 = float2(float(width_8), float(height_8));
                    float2 _S594 = float2(0.5f) ;
                    float2 remapped_5 = clamp((detailSource_0 * (size_5 - float2(2.0f) ) + float2(1.0f) ) / size_5, _S594 / size_5, (size_5 - _S594) / size_5);
                    if((_S592 & 1U) != 0U)
                    {
                        roughness_0 = remapped_5.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S592 & 2U) != 0U)
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
                _S191 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S595 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S595).get_width(0)),(*((&atlasHeight_4)) = (_S595).get_height(0));
            int3 _S596 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S596)).xy), uint(((_S596)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S597 = atlasWidth_4;
                uint _S598 = 2U % atlasWidth_4;
                int _S599 = int(_S598);
                uint _S600 = 2U / _S597;
                int3 _S601 = int3(_S599, int(_S600), int(0));
                int _S602 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S601)).xy), uint(((_S601)).z))).x * 255.0f)));
                uint _S603 = atlasWidth_4;
                uint _S604 = 3U % atlasWidth_4;
                int _S605 = int(_S604);
                uint _S606 = 3U / _S603;
                int3 _S607 = int3(_S605, int(_S606), int(0));
                int2 minimumTile_4 = int2(_S602, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S607)).xy), uint(((_S607)).z))).x * 255.0f))));
                uint _S608 = atlasWidth_4;
                uint _S609 = 4U % atlasWidth_4;
                int _S610 = int(_S609);
                uint _S611 = 4U / _S608;
                int3 _S612 = int3(_S610, int(_S611), int(0));
                uint _S613 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S612)).xy), uint(((_S612)).z))).x * 255.0f));
                uint _S614 = atlasWidth_4;
                uint _S615 = 5U % atlasWidth_4;
                int _S616 = int(_S615);
                uint _S617 = 5U / _S614;
                int3 _S618 = int3(_S616, int(_S617), int(0));
                uint _S619 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S618)).xy), uint(((_S618)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S613, _S619);
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
                    int3 _S620 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S620)).xy), uint(((_S620)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S613 + uint(tile_8.x));
                uint _S621 = atlasWidth_4;
                uint _S622 = offset_4 % atlasWidth_4;
                int _S623 = int(_S622);
                uint _S624 = offset_4 / _S621;
                int3 _S625 = int3(_S623, int(_S624), int(0));
                uint _S626 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S625)).xy), uint(((_S625)).z))).x * 255.0f));
                uint _S627 = atlasWidth_4;
                uint _S628 = offset_4 + 1U;
                uint _S629 = _S628 % atlasWidth_4;
                int _S630 = int(_S629);
                uint _S631 = _S628 / _S627;
                int3 _S632 = int3(_S630, int(_S631), int(0));
                uint slot_8 = _S626 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S632)).xy), uint(((_S632)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S633 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S633)).xy), uint(((_S633)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S634 = atlasWidth_4;
                uint _S635 = 6U % atlasWidth_4;
                int _S636 = int(_S635);
                uint _S637 = 6U / _S634;
                int3 _S638 = int3(_S636, int(_S637), int(0));
                uint _S639 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S638)).xy), uint(((_S638)).z))).x * 255.0f));
                uint _S640 = atlasWidth_4;
                uint _S641 = 7U % atlasWidth_4;
                int _S642 = int(_S641);
                uint _S643 = 7U / _S640;
                int3 _S644 = int3(_S642, int(_S643), int(0));
                uint columns_4 = _S639 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S644)).xy), uint(((_S644)).z))).x * 255.0f)) << 8U);
                uint _S645 = atlasWidth_4;
                uint _S646 = 8U % atlasWidth_4;
                int _S647 = int(_S646);
                uint _S648 = 8U / _S645;
                int3 _S649 = int3(_S647, int(_S648), int(0));
                uint _S650 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S649)).xy), uint(((_S649)).z))).x * 255.0f));
                uint _S651 = atlasWidth_4;
                uint _S652 = 9U % atlasWidth_4;
                int _S653 = int(_S652);
                uint _S654 = 9U / _S651;
                int3 _S655 = int3(_S653, int(_S654), int(0));
                uint rows_4 = ((_S650 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S655)).xy), uint(((_S655)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S613 * _S619 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S656 = atlasWidth_4 / columns_4;
                float _S657 = float(_S656);
                uint _S658 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S657, float(_S658));
                uint _S659 = slot_9 % columns_4;
                float _S660 = float(_S659);
                uint _S661 = slot_9 / columns_4;
                _S191 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S660, float(_S661)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(detailSource_0) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S662 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(detailSource_0)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S662);
            }
            if(hasSceneLighting_0)
            {
                int3 _S663 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S191 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S663)).xy), uint(((_S663)).z)));
                break;
            }
            uint _S664 = atlasWidth_4 / uint(_S662.x);
            float _S665 = float(_S664);
            uint _S666 = (atlasHeight_4 - 1U) / uint(_S662.y);
            float2 cellSize_9 = float2(_S665, float(_S666));
            _S191 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S667 = float4(_S6->compositeControls_0) ;
            if((_S667.x) != 8.0f)
            {
                break;
            }
            bool _S668 = (_S667.w) >= 0.5f;
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
            uint _S669 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S668)
                {
                    for(;;)
                    {
                        if(_S669 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S670 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_9;
                        thread uint height_9;
                        (*((&width_9)) = (_S670).get_width(0)),(*((&height_9)) = (_S670).get_height(0));
                        float2 size_6 = float2(float(width_9), float(height_9));
                        float2 _S671 = float2(0.5f) ;
                        float2 remapped_6 = clamp((detailSource_0 * (size_6 - float2(2.0f) ) + float2(1.0f) ) / size_6, _S671 / size_6, (size_6 - _S671) / size_6);
                        if((_S669 & 1U) != 0U)
                        {
                            roughness_0 = remapped_6.x;
                        }
                        else
                        {
                            roughness_0 = detailSource_0.x;
                        }
                        if((_S669 & 2U) != 0U)
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
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S672 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S672).get_width(0)),(*((&atlasHeight_5)) = (_S672).get_height(0));
                int3 _S673 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S673)).xy), uint(((_S673)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S674 = atlasWidth_5;
                    uint _S675 = 2U % atlasWidth_5;
                    int _S676 = int(_S675);
                    uint _S677 = 2U / _S674;
                    int3 _S678 = int3(_S676, int(_S677), int(0));
                    int _S679 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S678)).xy), uint(((_S678)).z))).x * 255.0f)));
                    uint _S680 = atlasWidth_5;
                    uint _S681 = 3U % atlasWidth_5;
                    int _S682 = int(_S681);
                    uint _S683 = 3U / _S680;
                    int3 _S684 = int3(_S682, int(_S683), int(0));
                    int2 minimumTile_5 = int2(_S679, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S684)).xy), uint(((_S684)).z))).x * 255.0f))));
                    uint _S685 = atlasWidth_5;
                    uint _S686 = 4U % atlasWidth_5;
                    int _S687 = int(_S686);
                    uint _S688 = 4U / _S685;
                    int3 _S689 = int3(_S687, int(_S688), int(0));
                    uint _S690 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S689)).xy), uint(((_S689)).z))).x * 255.0f));
                    uint _S691 = atlasWidth_5;
                    uint _S692 = 5U % atlasWidth_5;
                    int _S693 = int(_S692);
                    uint _S694 = 5U / _S691;
                    int3 _S695 = int3(_S693, int(_S694), int(0));
                    uint _S696 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S695)).xy), uint(((_S695)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S690, _S696);
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
                        int3 _S697 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S697)).xy), uint(((_S697)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S690 + uint(tile_10.x));
                    uint _S698 = atlasWidth_5;
                    uint _S699 = offset_5 % atlasWidth_5;
                    int _S700 = int(_S699);
                    uint _S701 = offset_5 / _S698;
                    int3 _S702 = int3(_S700, int(_S701), int(0));
                    uint _S703 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S702)).xy), uint(((_S702)).z))).x * 255.0f));
                    uint _S704 = atlasWidth_5;
                    uint _S705 = offset_5 + 1U;
                    uint _S706 = _S705 % atlasWidth_5;
                    int _S707 = int(_S706);
                    uint _S708 = _S705 / _S704;
                    int3 _S709 = int3(_S707, int(_S708), int(0));
                    uint slot_10 = _S703 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S709)).xy), uint(((_S709)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S710 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S710)).xy), uint(((_S710)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S711 = atlasWidth_5;
                    uint _S712 = 6U % atlasWidth_5;
                    int _S713 = int(_S712);
                    uint _S714 = 6U / _S711;
                    int3 _S715 = int3(_S713, int(_S714), int(0));
                    uint _S716 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S715)).xy), uint(((_S715)).z))).x * 255.0f));
                    uint _S717 = atlasWidth_5;
                    uint _S718 = 7U % atlasWidth_5;
                    int _S719 = int(_S718);
                    uint _S720 = 7U / _S717;
                    int3 _S721 = int3(_S719, int(_S720), int(0));
                    uint columns_5 = _S716 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S721)).xy), uint(((_S721)).z))).x * 255.0f)) << 8U);
                    uint _S722 = atlasWidth_5;
                    uint _S723 = 8U % atlasWidth_5;
                    int _S724 = int(_S723);
                    uint _S725 = 8U / _S722;
                    int3 _S726 = int3(_S724, int(_S725), int(0));
                    uint _S727 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S726)).xy), uint(((_S726)).z))).x * 255.0f));
                    uint _S728 = atlasWidth_5;
                    uint _S729 = 9U % atlasWidth_5;
                    int _S730 = int(_S729);
                    uint _S731 = 9U / _S728;
                    int3 _S732 = int3(_S730, int(_S731), int(0));
                    uint rows_5 = ((_S727 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S732)).xy), uint(((_S732)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S690 * _S696 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S733 = atlasWidth_5 / columns_5;
                    float _S734 = float(_S733);
                    uint _S735 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S734, float(_S735));
                    uint _S736 = slot_11 % columns_5;
                    float _S737 = float(_S736);
                    uint _S738 = slot_11 / columns_5;
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S737, float(_S738)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(detailSource_0) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S739 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(detailSource_0)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S739);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S740 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S740)).xy), uint(((_S740)).z)));
                    break;
                }
                uint _S741 = atlasWidth_5 / uint(_S739.x);
                float _S742 = float(_S741);
                uint _S743 = (atlasHeight_5 - 1U) / uint(_S739.y);
                float2 cellSize_11 = float2(_S742, float(_S743));
                _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_1 = uint(round(_S667.y));
            if(operation_1 == 1U)
            {
                _S191 = _S191 * _S590;
                break;
            }
            if(operation_1 == 2U)
            {
                _S191 = _S191 + _S590;
                break;
            }
            if(operation_1 == 3U)
            {
                _S191 = _S191 - _S590;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S667.z;
                _S191 = _S191 * float4((1.0f - factor_1))  + _S590 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S191.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S167;
    }
    float occlusion_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool useSecondary_3 = (secondaryMask_0 & 32U) != 0U;
        if((alternateMask_0 & 32U) != 0U)
        {
            if(useSecondary_3)
            {
                detailSource_0 = _S190;
            }
            else
            {
                detailSource_0 = _S189;
            }
        }
        else
        {
            if(useSecondary_3)
            {
                detailSource_0 = _S182;
            }
            else
            {
                detailSource_0 = _S181;
            }
        }
        bool _S744 = (udimMask_0 & 32U) != 0U;
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
        uint _S745 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S744)
            {
                for(;;)
                {
                    if(_S745 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S746 = (&kernelContext_0)->metallicTexture_0;
                    thread uint width_10;
                    thread uint height_10;
                    (*((&width_10)) = (_S746).get_width(0)),(*((&height_10)) = (_S746).get_height(0));
                    float2 size_7 = float2(float(width_10), float(height_10));
                    float2 _S747 = float2(0.5f) ;
                    float2 remapped_7 = clamp((detailSource_0 * (size_7 - float2(2.0f) ) + float2(1.0f) ) / size_7, _S747 / size_7, (size_7 - _S747) / size_7);
                    if((_S745 & 1U) != 0U)
                    {
                        metallic_0 = remapped_7.x;
                    }
                    else
                    {
                        metallic_0 = detailSource_0.x;
                    }
                    if((_S745 & 2U) != 0U)
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
                _S191 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S748 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S748).get_width(0)),(*((&atlasHeight_6)) = (_S748).get_height(0));
            int3 _S749 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S749)).xy), uint(((_S749)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S750 = atlasWidth_6;
                uint _S751 = 2U % atlasWidth_6;
                int _S752 = int(_S751);
                uint _S753 = 2U / _S750;
                int3 _S754 = int3(_S752, int(_S753), int(0));
                int _S755 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S754)).xy), uint(((_S754)).z))).x * 255.0f)));
                uint _S756 = atlasWidth_6;
                uint _S757 = 3U % atlasWidth_6;
                int _S758 = int(_S757);
                uint _S759 = 3U / _S756;
                int3 _S760 = int3(_S758, int(_S759), int(0));
                int2 minimumTile_6 = int2(_S755, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S760)).xy), uint(((_S760)).z))).x * 255.0f))));
                uint _S761 = atlasWidth_6;
                uint _S762 = 4U % atlasWidth_6;
                int _S763 = int(_S762);
                uint _S764 = 4U / _S761;
                int3 _S765 = int3(_S763, int(_S764), int(0));
                uint _S766 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S765)).xy), uint(((_S765)).z))).x * 255.0f));
                uint _S767 = atlasWidth_6;
                uint _S768 = 5U % atlasWidth_6;
                int _S769 = int(_S768);
                uint _S770 = 5U / _S767;
                int3 _S771 = int3(_S769, int(_S770), int(0));
                uint _S772 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S771)).xy), uint(((_S771)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S766, _S772);
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
                    int3 _S773 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S773)).xy), uint(((_S773)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S766 + uint(tile_12.x));
                uint _S774 = atlasWidth_6;
                uint _S775 = offset_6 % atlasWidth_6;
                int _S776 = int(_S775);
                uint _S777 = offset_6 / _S774;
                int3 _S778 = int3(_S776, int(_S777), int(0));
                uint _S779 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S778)).xy), uint(((_S778)).z))).x * 255.0f));
                uint _S780 = atlasWidth_6;
                uint _S781 = offset_6 + 1U;
                uint _S782 = _S781 % atlasWidth_6;
                int _S783 = int(_S782);
                uint _S784 = _S781 / _S780;
                int3 _S785 = int3(_S783, int(_S784), int(0));
                uint slot_12 = _S779 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S785)).xy), uint(((_S785)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S786 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S786)).xy), uint(((_S786)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S787 = atlasWidth_6;
                uint _S788 = 6U % atlasWidth_6;
                int _S789 = int(_S788);
                uint _S790 = 6U / _S787;
                int3 _S791 = int3(_S789, int(_S790), int(0));
                uint _S792 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S791)).xy), uint(((_S791)).z))).x * 255.0f));
                uint _S793 = atlasWidth_6;
                uint _S794 = 7U % atlasWidth_6;
                int _S795 = int(_S794);
                uint _S796 = 7U / _S793;
                int3 _S797 = int3(_S795, int(_S796), int(0));
                uint columns_6 = _S792 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S797)).xy), uint(((_S797)).z))).x * 255.0f)) << 8U);
                uint _S798 = atlasWidth_6;
                uint _S799 = 8U % atlasWidth_6;
                int _S800 = int(_S799);
                uint _S801 = 8U / _S798;
                int3 _S802 = int3(_S800, int(_S801), int(0));
                uint _S803 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S802)).xy), uint(((_S802)).z))).x * 255.0f));
                uint _S804 = atlasWidth_6;
                uint _S805 = 9U % atlasWidth_6;
                int _S806 = int(_S805);
                uint _S807 = 9U / _S804;
                int3 _S808 = int3(_S806, int(_S807), int(0));
                uint rows_6 = ((_S803 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S808)).xy), uint(((_S808)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S766 * _S772 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S809 = atlasWidth_6 / columns_6;
                float _S810 = float(_S809);
                uint _S811 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S810, float(_S811));
                uint _S812 = slot_13 % columns_6;
                float _S813 = float(_S812);
                uint _S814 = slot_13 / columns_6;
                _S191 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S813, float(_S814)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(detailSource_0) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S815 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(detailSource_0)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S815);
            }
            if(hasSceneLighting_0)
            {
                int3 _S816 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S191 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S816)).xy), uint(((_S816)).z)));
                break;
            }
            uint _S817 = atlasWidth_6 / uint(_S815.x);
            float _S818 = float(_S817);
            uint _S819 = (atlasHeight_6 - 1U) / uint(_S815.y);
            float2 cellSize_13 = float2(_S818, float(_S819));
            _S191 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        for(;;)
        {
            float4 _S820 = float4(_S6->compositeControls_0) ;
            if((_S820.x) != 32.0f)
            {
                break;
            }
            bool _S821 = (_S820.w) >= 0.5f;
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
            uint _S822 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S821)
                {
                    for(;;)
                    {
                        if(_S822 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S823 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_11;
                        thread uint height_11;
                        (*((&width_11)) = (_S823).get_width(0)),(*((&height_11)) = (_S823).get_height(0));
                        float2 size_8 = float2(float(width_11), float(height_11));
                        float2 _S824 = float2(0.5f) ;
                        float2 remapped_8 = clamp((detailSource_0 * (size_8 - float2(2.0f) ) + float2(1.0f) ) / size_8, _S824 / size_8, (size_8 - _S824) / size_8);
                        if((_S822 & 1U) != 0U)
                        {
                            metallic_0 = remapped_8.x;
                        }
                        else
                        {
                            metallic_0 = detailSource_0.x;
                        }
                        if((_S822 & 2U) != 0U)
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
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S825 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S825).get_width(0)),(*((&atlasHeight_7)) = (_S825).get_height(0));
                int3 _S826 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S826)).xy), uint(((_S826)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S827 = atlasWidth_7;
                    uint _S828 = 2U % atlasWidth_7;
                    int _S829 = int(_S828);
                    uint _S830 = 2U / _S827;
                    int3 _S831 = int3(_S829, int(_S830), int(0));
                    int _S832 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S831)).xy), uint(((_S831)).z))).x * 255.0f)));
                    uint _S833 = atlasWidth_7;
                    uint _S834 = 3U % atlasWidth_7;
                    int _S835 = int(_S834);
                    uint _S836 = 3U / _S833;
                    int3 _S837 = int3(_S835, int(_S836), int(0));
                    int2 minimumTile_7 = int2(_S832, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S837)).xy), uint(((_S837)).z))).x * 255.0f))));
                    uint _S838 = atlasWidth_7;
                    uint _S839 = 4U % atlasWidth_7;
                    int _S840 = int(_S839);
                    uint _S841 = 4U / _S838;
                    int3 _S842 = int3(_S840, int(_S841), int(0));
                    uint _S843 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S842)).xy), uint(((_S842)).z))).x * 255.0f));
                    uint _S844 = atlasWidth_7;
                    uint _S845 = 5U % atlasWidth_7;
                    int _S846 = int(_S845);
                    uint _S847 = 5U / _S844;
                    int3 _S848 = int3(_S846, int(_S847), int(0));
                    uint _S849 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S848)).xy), uint(((_S848)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S843, _S849);
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
                        int3 _S850 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S850)).xy), uint(((_S850)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S843 + uint(tile_14.x));
                    uint _S851 = atlasWidth_7;
                    uint _S852 = offset_7 % atlasWidth_7;
                    int _S853 = int(_S852);
                    uint _S854 = offset_7 / _S851;
                    int3 _S855 = int3(_S853, int(_S854), int(0));
                    uint _S856 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S855)).xy), uint(((_S855)).z))).x * 255.0f));
                    uint _S857 = atlasWidth_7;
                    uint _S858 = offset_7 + 1U;
                    uint _S859 = _S858 % atlasWidth_7;
                    int _S860 = int(_S859);
                    uint _S861 = _S858 / _S857;
                    int3 _S862 = int3(_S860, int(_S861), int(0));
                    uint slot_14 = _S856 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S862)).xy), uint(((_S862)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S863 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S863)).xy), uint(((_S863)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S864 = atlasWidth_7;
                    uint _S865 = 6U % atlasWidth_7;
                    int _S866 = int(_S865);
                    uint _S867 = 6U / _S864;
                    int3 _S868 = int3(_S866, int(_S867), int(0));
                    uint _S869 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S868)).xy), uint(((_S868)).z))).x * 255.0f));
                    uint _S870 = atlasWidth_7;
                    uint _S871 = 7U % atlasWidth_7;
                    int _S872 = int(_S871);
                    uint _S873 = 7U / _S870;
                    int3 _S874 = int3(_S872, int(_S873), int(0));
                    uint columns_7 = _S869 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S874)).xy), uint(((_S874)).z))).x * 255.0f)) << 8U);
                    uint _S875 = atlasWidth_7;
                    uint _S876 = 8U % atlasWidth_7;
                    int _S877 = int(_S876);
                    uint _S878 = 8U / _S875;
                    int3 _S879 = int3(_S877, int(_S878), int(0));
                    uint _S880 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S879)).xy), uint(((_S879)).z))).x * 255.0f));
                    uint _S881 = atlasWidth_7;
                    uint _S882 = 9U % atlasWidth_7;
                    int _S883 = int(_S882);
                    uint _S884 = 9U / _S881;
                    int3 _S885 = int3(_S883, int(_S884), int(0));
                    uint rows_7 = ((_S880 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S885)).xy), uint(((_S885)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S843 * _S849 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S886 = atlasWidth_7 / columns_7;
                    float _S887 = float(_S886);
                    uint _S888 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S887, float(_S888));
                    uint _S889 = slot_15 % columns_7;
                    float _S890 = float(_S889);
                    uint _S891 = slot_15 / columns_7;
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S890, float(_S891)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(detailSource_0) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S892 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(detailSource_0)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S892);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S893 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S893)).xy), uint(((_S893)).z)));
                    break;
                }
                uint _S894 = atlasWidth_7 / uint(_S892.x);
                float _S895 = float(_S894);
                uint _S896 = (atlasHeight_7 - 1U) / uint(_S892.y);
                float2 cellSize_15 = float2(_S895, float(_S896));
                _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_2 = uint(round(_S820.y));
            if(operation_2 == 1U)
            {
                _S191 = _S191 * _S590;
                break;
            }
            if(operation_2 == 2U)
            {
                _S191 = _S191 + _S590;
                break;
            }
            if(operation_2 == 3U)
            {
                _S191 = _S191 - _S590;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S820.z;
                _S191 = _S191 * float4((1.0f - factor_2))  + _S590 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S191.x);
    }
    else
    {
        metallic_0 = _S166;
    }
    float3 emissiveColor_1;
    float clearcoatAmount_0;
    if((textureMask_0 & 16U) != 0U)
    {
        bool useSecondary_4 = (secondaryMask_0 & 16U) != 0U;
        if((alternateMask_0 & 16U) != 0U)
        {
            if(useSecondary_4)
            {
                detailSource_0 = _S190;
            }
            else
            {
                detailSource_0 = _S189;
            }
        }
        else
        {
            if(useSecondary_4)
            {
                detailSource_0 = _S182;
            }
            else
            {
                detailSource_0 = _S181;
            }
        }
        thread float4 emissionSample_0;
        bool _S897 = (udimMask_0 & 16U) != 0U;
        bool _S898 = (maskU_1 & 16U) != 0U;
        if(_S898)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S899 = (maskV_1 & 16U) != 0U;
        if(_S899)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S900 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S897)
            {
                for(;;)
                {
                    if(_S900 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S901 = (&kernelContext_0)->emissiveTexture_0;
                    thread uint width_12;
                    thread uint height_12;
                    (*((&width_12)) = (_S901).get_width(0)),(*((&height_12)) = (_S901).get_height(0));
                    float2 size_9 = float2(float(width_12), float(height_12));
                    float2 _S902 = float2(0.5f) ;
                    float2 remapped_9 = clamp((detailSource_0 * (size_9 - float2(2.0f) ) + float2(1.0f) ) / size_9, _S902 / size_9, (size_9 - _S902) / size_9);
                    if((_S900 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_9.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S900 & 2U) != 0U)
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
                _S191 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S903 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S903).get_width(0)),(*((&atlasHeight_8)) = (_S903).get_height(0));
            int3 _S904 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S904)).xy), uint(((_S904)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S905 = atlasWidth_8;
                uint _S906 = 2U % atlasWidth_8;
                int _S907 = int(_S906);
                uint _S908 = 2U / _S905;
                int3 _S909 = int3(_S907, int(_S908), int(0));
                int _S910 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S909)).xy), uint(((_S909)).z))).x * 255.0f)));
                uint _S911 = atlasWidth_8;
                uint _S912 = 3U % atlasWidth_8;
                int _S913 = int(_S912);
                uint _S914 = 3U / _S911;
                int3 _S915 = int3(_S913, int(_S914), int(0));
                int2 minimumTile_8 = int2(_S910, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S915)).xy), uint(((_S915)).z))).x * 255.0f))));
                uint _S916 = atlasWidth_8;
                uint _S917 = 4U % atlasWidth_8;
                int _S918 = int(_S917);
                uint _S919 = 4U / _S916;
                int3 _S920 = int3(_S918, int(_S919), int(0));
                uint _S921 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S920)).xy), uint(((_S920)).z))).x * 255.0f));
                uint _S922 = atlasWidth_8;
                uint _S923 = 5U % atlasWidth_8;
                int _S924 = int(_S923);
                uint _S925 = 5U / _S922;
                int3 _S926 = int3(_S924, int(_S925), int(0));
                uint _S927 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S926)).xy), uint(((_S926)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S921, _S927);
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
                    int3 _S928 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S928)).xy), uint(((_S928)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S921 + uint(tile_16.x));
                uint _S929 = atlasWidth_8;
                uint _S930 = offset_8 % atlasWidth_8;
                int _S931 = int(_S930);
                uint _S932 = offset_8 / _S929;
                int3 _S933 = int3(_S931, int(_S932), int(0));
                uint _S934 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S933)).xy), uint(((_S933)).z))).x * 255.0f));
                uint _S935 = atlasWidth_8;
                uint _S936 = offset_8 + 1U;
                uint _S937 = _S936 % atlasWidth_8;
                int _S938 = int(_S937);
                uint _S939 = _S936 / _S935;
                int3 _S940 = int3(_S938, int(_S939), int(0));
                uint slot_16 = _S934 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S940)).xy), uint(((_S940)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S941 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S941)).xy), uint(((_S941)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S942 = atlasWidth_8;
                uint _S943 = 6U % atlasWidth_8;
                int _S944 = int(_S943);
                uint _S945 = 6U / _S942;
                int3 _S946 = int3(_S944, int(_S945), int(0));
                uint _S947 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S946)).xy), uint(((_S946)).z))).x * 255.0f));
                uint _S948 = atlasWidth_8;
                uint _S949 = 7U % atlasWidth_8;
                int _S950 = int(_S949);
                uint _S951 = 7U / _S948;
                int3 _S952 = int3(_S950, int(_S951), int(0));
                uint columns_8 = _S947 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S952)).xy), uint(((_S952)).z))).x * 255.0f)) << 8U);
                uint _S953 = atlasWidth_8;
                uint _S954 = 8U % atlasWidth_8;
                int _S955 = int(_S954);
                uint _S956 = 8U / _S953;
                int3 _S957 = int3(_S955, int(_S956), int(0));
                uint _S958 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S957)).xy), uint(((_S957)).z))).x * 255.0f));
                uint _S959 = atlasWidth_8;
                uint _S960 = 9U % atlasWidth_8;
                int _S961 = int(_S960);
                uint _S962 = 9U / _S959;
                int3 _S963 = int3(_S961, int(_S962), int(0));
                uint rows_8 = ((_S958 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S963)).xy), uint(((_S963)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S921 * _S927 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S964 = atlasWidth_8 / columns_8;
                float _S965 = float(_S964);
                uint _S966 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S965, float(_S966));
                uint _S967 = slot_17 % columns_8;
                float _S968 = float(_S967);
                uint _S969 = slot_17 / columns_8;
                _S191 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S968, float(_S969)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(detailSource_0) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S970 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(detailSource_0)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S970);
            }
            if(hasSceneLighting_0)
            {
                int3 _S971 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S191 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S971)).xy), uint(((_S971)).z)));
                break;
            }
            uint _S972 = atlasWidth_8 / uint(_S970.x);
            float _S973 = float(_S972);
            uint _S974 = (atlasHeight_8 - 1U) / uint(_S970.y);
            float2 cellSize_17 = float2(_S973, float(_S974));
            _S191 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        emissionSample_0 = _S191;
        if((_S176.w) != 0.0f)
        {
            texture2d<float, access::sample> _S975 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S975).get_width(0)),(*((&sourceHeight_1)) = (_S975).get_height(0));
            texture2d<float, access::sample> _S976 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S976).get_width(0)),(*((&valueHeight_1)) = (_S976).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S977 = emissionSample_0;
                for(;;)
                {
                    float _S978 = _S977.w;
                    if(_S978 > 1.0f)
                    {
                        unlitColor_0 = _S977.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S979 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_13;
                    thread uint height_13;
                    (*((&width_13)) = (_S979).get_width(0)),(*((&height_13)) = (_S979).get_height(0));
                    uint3 _S980 = uint3(round(_S977.xyz * float3(255.0f) ));
                    uint index_4 = ((_S980.x) | ((_S980.y) << 8U)) | ((_S980.z) << 16U);
                    if(_S978 == 0.0f)
                    {
                        _S192 = _S980;
                    }
                    else
                    {
                        _S192 = uint3(index_4, index_4, index_4);
                    }
                    uint3 address_3 = _S192 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S981 = address_3.x;
                    uint _S982 = _S981 % width_13;
                    int _S983 = int(_S982);
                    uint _S984 = _S981 / width_13;
                    int3 _S985 = int3(_S983, int(_S984), int(0));
                    float _S986 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S985)).xy), uint(((_S985)).z))).x;
                    uint _S987 = address_3.y;
                    uint _S988 = _S987 % width_13;
                    int _S989 = int(_S988);
                    uint _S990 = _S987 / width_13;
                    int3 _S991 = int3(_S989, int(_S990), int(0));
                    float _S992 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S991)).xy), uint(((_S991)).z))).x;
                    uint _S993 = address_3.z;
                    uint _S994 = _S993 % width_13;
                    int _S995 = int(_S994);
                    uint _S996 = _S993 / width_13;
                    int3 _S997 = int3(_S995, int(_S996), int(0));
                    unlitColor_0 = float3(_S986, _S992, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S997)).xy), uint(((_S997)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                if(_S898)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S899)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S998 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S998 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S999 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_14;
                    thread uint height_14;
                    (*((&width_14)) = (_S999).get_width(0)),(*((&height_14)) = (_S999).get_height(0));
                    float2 size_10 = float2(float(width_14), float(height_14));
                    float2 _S1000 = float2(0.5f) ;
                    float2 remapped_10 = clamp((detailSource_0 * (size_10 - float2(2.0f) ) + float2(1.0f) ) / size_10, _S1000 / size_10, (size_10 - _S1000) / size_10);
                    if((_S998 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_10.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S998 & 2U) != 0U)
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
        float4 _S1001 = emissionSample_0;
        for(;;)
        {
            float4 _S1002 = float4(_S6->compositeControls_0) ;
            if((_S1002.x) != 16.0f)
            {
                _S191 = _S1001;
                break;
            }
            bool _S1003 = (_S1002.w) >= 0.5f;
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
            uint _S1004 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1003)
                {
                    for(;;)
                    {
                        if(_S1004 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1005 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_15;
                        thread uint height_15;
                        (*((&width_15)) = (_S1005).get_width(0)),(*((&height_15)) = (_S1005).get_height(0));
                        float2 size_11 = float2(float(width_15), float(height_15));
                        float2 _S1006 = float2(0.5f) ;
                        float2 remapped_11 = clamp((detailSource_0 * (size_11 - float2(2.0f) ) + float2(1.0f) ) / size_11, _S1006 / size_11, (size_11 - _S1006) / size_11);
                        if((_S1004 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_11.x;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.x;
                        }
                        if((_S1004 & 2U) != 0U)
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
                    _S191 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1007 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S1007).get_width(0)),(*((&atlasHeight_9)) = (_S1007).get_height(0));
                int3 _S1008 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1008)).xy), uint(((_S1008)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S1009 = atlasWidth_9;
                    uint _S1010 = 2U % atlasWidth_9;
                    int _S1011 = int(_S1010);
                    uint _S1012 = 2U / _S1009;
                    int3 _S1013 = int3(_S1011, int(_S1012), int(0));
                    int _S1014 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1013)).xy), uint(((_S1013)).z))).x * 255.0f)));
                    uint _S1015 = atlasWidth_9;
                    uint _S1016 = 3U % atlasWidth_9;
                    int _S1017 = int(_S1016);
                    uint _S1018 = 3U / _S1015;
                    int3 _S1019 = int3(_S1017, int(_S1018), int(0));
                    int2 minimumTile_9 = int2(_S1014, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1019)).xy), uint(((_S1019)).z))).x * 255.0f))));
                    uint _S1020 = atlasWidth_9;
                    uint _S1021 = 4U % atlasWidth_9;
                    int _S1022 = int(_S1021);
                    uint _S1023 = 4U / _S1020;
                    int3 _S1024 = int3(_S1022, int(_S1023), int(0));
                    uint _S1025 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1024)).xy), uint(((_S1024)).z))).x * 255.0f));
                    uint _S1026 = atlasWidth_9;
                    uint _S1027 = 5U % atlasWidth_9;
                    int _S1028 = int(_S1027);
                    uint _S1029 = 5U / _S1026;
                    int3 _S1030 = int3(_S1028, int(_S1029), int(0));
                    uint _S1031 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1030)).xy), uint(((_S1030)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S1025, _S1031);
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
                        int3 _S1032 = int3(int(1), int(0), int(0));
                        _S191 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1032)).xy), uint(((_S1032)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S1025 + uint(tile_18.x));
                    uint _S1033 = atlasWidth_9;
                    uint _S1034 = offset_9 % atlasWidth_9;
                    int _S1035 = int(_S1034);
                    uint _S1036 = offset_9 / _S1033;
                    int3 _S1037 = int3(_S1035, int(_S1036), int(0));
                    uint _S1038 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1037)).xy), uint(((_S1037)).z))).x * 255.0f));
                    uint _S1039 = atlasWidth_9;
                    uint _S1040 = offset_9 + 1U;
                    uint _S1041 = _S1040 % atlasWidth_9;
                    int _S1042 = int(_S1041);
                    uint _S1043 = _S1040 / _S1039;
                    int3 _S1044 = int3(_S1042, int(_S1043), int(0));
                    uint slot_18 = _S1038 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1044)).xy), uint(((_S1044)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S1045 = int3(int(1), int(0), int(0));
                        _S191 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1045)).xy), uint(((_S1045)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S1046 = atlasWidth_9;
                    uint _S1047 = 6U % atlasWidth_9;
                    int _S1048 = int(_S1047);
                    uint _S1049 = 6U / _S1046;
                    int3 _S1050 = int3(_S1048, int(_S1049), int(0));
                    uint _S1051 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1050)).xy), uint(((_S1050)).z))).x * 255.0f));
                    uint _S1052 = atlasWidth_9;
                    uint _S1053 = 7U % atlasWidth_9;
                    int _S1054 = int(_S1053);
                    uint _S1055 = 7U / _S1052;
                    int3 _S1056 = int3(_S1054, int(_S1055), int(0));
                    uint columns_9 = _S1051 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1056)).xy), uint(((_S1056)).z))).x * 255.0f)) << 8U);
                    uint _S1057 = atlasWidth_9;
                    uint _S1058 = 8U % atlasWidth_9;
                    int _S1059 = int(_S1058);
                    uint _S1060 = 8U / _S1057;
                    int3 _S1061 = int3(_S1059, int(_S1060), int(0));
                    uint _S1062 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1061)).xy), uint(((_S1061)).z))).x * 255.0f));
                    uint _S1063 = atlasWidth_9;
                    uint _S1064 = 9U % atlasWidth_9;
                    int _S1065 = int(_S1064);
                    uint _S1066 = 9U / _S1063;
                    int3 _S1067 = int3(_S1065, int(_S1066), int(0));
                    uint rows_9 = ((_S1062 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1067)).xy), uint(((_S1067)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S1025 * _S1031 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S1068 = atlasWidth_9 / columns_9;
                    float _S1069 = float(_S1068);
                    uint _S1070 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S1069, float(_S1070));
                    uint _S1071 = slot_19 % columns_9;
                    float _S1072 = float(_S1071);
                    uint _S1073 = slot_19 / columns_9;
                    _S191 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1072, float(_S1073)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(detailSource_0) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S1074 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(detailSource_0)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S1074);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1075 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S191 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1075)).xy), uint(((_S1075)).z)));
                    break;
                }
                uint _S1076 = atlasWidth_9 / uint(_S1074.x);
                float _S1077 = float(_S1076);
                uint _S1078 = (atlasHeight_9 - 1U) / uint(_S1074.y);
                float2 cellSize_19 = float2(_S1077, float(_S1078));
                _S191 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_3 = uint(round(_S1002.y));
            if(operation_3 == 1U)
            {
                _S191 = _S1001 * _S191;
                break;
            }
            if(operation_3 == 2U)
            {
                _S191 = _S1001 + _S191;
                break;
            }
            if(operation_3 == 3U)
            {
                _S191 = _S1001 - _S191;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S1002.z;
                _S191 = _S1001 * float4((1.0f - factor_3))  + _S191 * float4(factor_3) ;
                break;
            }
            _S191 = _S1001;
            break;
        }
        emissiveColor_1 = _S191.xyz;
    }
    else
    {
        emissiveColor_1 = emissiveColor_0;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool useSecondary_5 = (secondaryMask_0 & 64U) != 0U;
        if((alternateMask_0 & 64U) != 0U)
        {
            if(useSecondary_5)
            {
                detailSource_0 = _S190;
            }
            else
            {
                detailSource_0 = _S189;
            }
        }
        else
        {
            if(useSecondary_5)
            {
                detailSource_0 = _S182;
            }
            else
            {
                detailSource_0 = _S181;
            }
        }
        bool _S1079 = (udimMask_0 & 64U) != 0U;
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
        uint _S1080 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1079)
            {
                for(;;)
                {
                    if(_S1080 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1081 = (&kernelContext_0)->opacityTexture_0;
                    thread uint width_16;
                    thread uint height_16;
                    (*((&width_16)) = (_S1081).get_width(0)),(*((&height_16)) = (_S1081).get_height(0));
                    float2 size_12 = float2(float(width_16), float(height_16));
                    float2 _S1082 = float2(0.5f) ;
                    float2 remapped_12 = clamp((detailSource_0 * (size_12 - float2(2.0f) ) + float2(1.0f) ) / size_12, _S1082 / size_12, (size_12 - _S1082) / size_12);
                    if((_S1080 & 1U) != 0U)
                    {
                        opacity_0 = remapped_12.x;
                    }
                    else
                    {
                        opacity_0 = detailSource_0.x;
                    }
                    if((_S1080 & 2U) != 0U)
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
                _S191 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1083 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S1083).get_width(0)),(*((&atlasHeight_10)) = (_S1083).get_height(0));
            int3 _S1084 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1084)).xy), uint(((_S1084)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S1085 = atlasWidth_10;
                uint _S1086 = 2U % atlasWidth_10;
                int _S1087 = int(_S1086);
                uint _S1088 = 2U / _S1085;
                int3 _S1089 = int3(_S1087, int(_S1088), int(0));
                int _S1090 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1089)).xy), uint(((_S1089)).z))).x * 255.0f)));
                uint _S1091 = atlasWidth_10;
                uint _S1092 = 3U % atlasWidth_10;
                int _S1093 = int(_S1092);
                uint _S1094 = 3U / _S1091;
                int3 _S1095 = int3(_S1093, int(_S1094), int(0));
                int2 minimumTile_10 = int2(_S1090, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1095)).xy), uint(((_S1095)).z))).x * 255.0f))));
                uint _S1096 = atlasWidth_10;
                uint _S1097 = 4U % atlasWidth_10;
                int _S1098 = int(_S1097);
                uint _S1099 = 4U / _S1096;
                int3 _S1100 = int3(_S1098, int(_S1099), int(0));
                uint _S1101 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1100)).xy), uint(((_S1100)).z))).x * 255.0f));
                uint _S1102 = atlasWidth_10;
                uint _S1103 = 5U % atlasWidth_10;
                int _S1104 = int(_S1103);
                uint _S1105 = 5U / _S1102;
                int3 _S1106 = int3(_S1104, int(_S1105), int(0));
                uint _S1107 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1106)).xy), uint(((_S1106)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S1101, _S1107);
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
                    int3 _S1108 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1108)).xy), uint(((_S1108)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1101 + uint(tile_20.x));
                uint _S1109 = atlasWidth_10;
                uint _S1110 = offset_10 % atlasWidth_10;
                int _S1111 = int(_S1110);
                uint _S1112 = offset_10 / _S1109;
                int3 _S1113 = int3(_S1111, int(_S1112), int(0));
                uint _S1114 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1113)).xy), uint(((_S1113)).z))).x * 255.0f));
                uint _S1115 = atlasWidth_10;
                uint _S1116 = offset_10 + 1U;
                uint _S1117 = _S1116 % atlasWidth_10;
                int _S1118 = int(_S1117);
                uint _S1119 = _S1116 / _S1115;
                int3 _S1120 = int3(_S1118, int(_S1119), int(0));
                uint slot_20 = _S1114 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1120)).xy), uint(((_S1120)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1121 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1121)).xy), uint(((_S1121)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1122 = atlasWidth_10;
                uint _S1123 = 6U % atlasWidth_10;
                int _S1124 = int(_S1123);
                uint _S1125 = 6U / _S1122;
                int3 _S1126 = int3(_S1124, int(_S1125), int(0));
                uint _S1127 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1126)).xy), uint(((_S1126)).z))).x * 255.0f));
                uint _S1128 = atlasWidth_10;
                uint _S1129 = 7U % atlasWidth_10;
                int _S1130 = int(_S1129);
                uint _S1131 = 7U / _S1128;
                int3 _S1132 = int3(_S1130, int(_S1131), int(0));
                uint columns_10 = _S1127 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1132)).xy), uint(((_S1132)).z))).x * 255.0f)) << 8U);
                uint _S1133 = atlasWidth_10;
                uint _S1134 = 8U % atlasWidth_10;
                int _S1135 = int(_S1134);
                uint _S1136 = 8U / _S1133;
                int3 _S1137 = int3(_S1135, int(_S1136), int(0));
                uint _S1138 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1137)).xy), uint(((_S1137)).z))).x * 255.0f));
                uint _S1139 = atlasWidth_10;
                uint _S1140 = 9U % atlasWidth_10;
                int _S1141 = int(_S1140);
                uint _S1142 = 9U / _S1139;
                int3 _S1143 = int3(_S1141, int(_S1142), int(0));
                uint rows_10 = ((_S1138 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1143)).xy), uint(((_S1143)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S1101 * _S1107 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1144 = atlasWidth_10 / columns_10;
                float _S1145 = float(_S1144);
                uint _S1146 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1145, float(_S1146));
                uint _S1147 = slot_21 % columns_10;
                float _S1148 = float(_S1147);
                uint _S1149 = slot_21 / columns_10;
                _S191 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S1148, float(_S1149)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(detailSource_0) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1150 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(detailSource_0)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1150);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1151 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S191 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1151)).xy), uint(((_S1151)).z)));
                break;
            }
            uint _S1152 = atlasWidth_10 / uint(_S1150.x);
            float _S1153 = float(_S1152);
            uint _S1154 = (atlasHeight_10 - 1U) / uint(_S1150.y);
            float2 cellSize_21 = float2(_S1153, float(_S1154));
            _S191 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1155 = float4(_S6->compositeControls_0) ;
            if((_S1155.x) != 64.0f)
            {
                break;
            }
            bool _S1156 = (_S1155.w) >= 0.5f;
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
            uint _S1157 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1156)
                {
                    for(;;)
                    {
                        if(_S1157 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1158 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_17;
                        thread uint height_17;
                        (*((&width_17)) = (_S1158).get_width(0)),(*((&height_17)) = (_S1158).get_height(0));
                        float2 size_13 = float2(float(width_17), float(height_17));
                        float2 _S1159 = float2(0.5f) ;
                        float2 remapped_13 = clamp((detailSource_0 * (size_13 - float2(2.0f) ) + float2(1.0f) ) / size_13, _S1159 / size_13, (size_13 - _S1159) / size_13);
                        if((_S1157 & 1U) != 0U)
                        {
                            opacity_0 = remapped_13.x;
                        }
                        else
                        {
                            opacity_0 = detailSource_0.x;
                        }
                        if((_S1157 & 2U) != 0U)
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
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1160 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1160).get_width(0)),(*((&atlasHeight_11)) = (_S1160).get_height(0));
                int3 _S1161 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1161)).xy), uint(((_S1161)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1162 = atlasWidth_11;
                    uint _S1163 = 2U % atlasWidth_11;
                    int _S1164 = int(_S1163);
                    uint _S1165 = 2U / _S1162;
                    int3 _S1166 = int3(_S1164, int(_S1165), int(0));
                    int _S1167 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1166)).xy), uint(((_S1166)).z))).x * 255.0f)));
                    uint _S1168 = atlasWidth_11;
                    uint _S1169 = 3U % atlasWidth_11;
                    int _S1170 = int(_S1169);
                    uint _S1171 = 3U / _S1168;
                    int3 _S1172 = int3(_S1170, int(_S1171), int(0));
                    int2 minimumTile_11 = int2(_S1167, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1172)).xy), uint(((_S1172)).z))).x * 255.0f))));
                    uint _S1173 = atlasWidth_11;
                    uint _S1174 = 4U % atlasWidth_11;
                    int _S1175 = int(_S1174);
                    uint _S1176 = 4U / _S1173;
                    int3 _S1177 = int3(_S1175, int(_S1176), int(0));
                    uint _S1178 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1177)).xy), uint(((_S1177)).z))).x * 255.0f));
                    uint _S1179 = atlasWidth_11;
                    uint _S1180 = 5U % atlasWidth_11;
                    int _S1181 = int(_S1180);
                    uint _S1182 = 5U / _S1179;
                    int3 _S1183 = int3(_S1181, int(_S1182), int(0));
                    uint _S1184 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1183)).xy), uint(((_S1183)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1178, _S1184);
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
                        int3 _S1185 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1185)).xy), uint(((_S1185)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1178 + uint(tile_22.x));
                    uint _S1186 = atlasWidth_11;
                    uint _S1187 = offset_11 % atlasWidth_11;
                    int _S1188 = int(_S1187);
                    uint _S1189 = offset_11 / _S1186;
                    int3 _S1190 = int3(_S1188, int(_S1189), int(0));
                    uint _S1191 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1190)).xy), uint(((_S1190)).z))).x * 255.0f));
                    uint _S1192 = atlasWidth_11;
                    uint _S1193 = offset_11 + 1U;
                    uint _S1194 = _S1193 % atlasWidth_11;
                    int _S1195 = int(_S1194);
                    uint _S1196 = _S1193 / _S1192;
                    int3 _S1197 = int3(_S1195, int(_S1196), int(0));
                    uint slot_22 = _S1191 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1197)).xy), uint(((_S1197)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1198 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1198)).xy), uint(((_S1198)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1199 = atlasWidth_11;
                    uint _S1200 = 6U % atlasWidth_11;
                    int _S1201 = int(_S1200);
                    uint _S1202 = 6U / _S1199;
                    int3 _S1203 = int3(_S1201, int(_S1202), int(0));
                    uint _S1204 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1203)).xy), uint(((_S1203)).z))).x * 255.0f));
                    uint _S1205 = atlasWidth_11;
                    uint _S1206 = 7U % atlasWidth_11;
                    int _S1207 = int(_S1206);
                    uint _S1208 = 7U / _S1205;
                    int3 _S1209 = int3(_S1207, int(_S1208), int(0));
                    uint columns_11 = _S1204 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1209)).xy), uint(((_S1209)).z))).x * 255.0f)) << 8U);
                    uint _S1210 = atlasWidth_11;
                    uint _S1211 = 8U % atlasWidth_11;
                    int _S1212 = int(_S1211);
                    uint _S1213 = 8U / _S1210;
                    int3 _S1214 = int3(_S1212, int(_S1213), int(0));
                    uint _S1215 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1214)).xy), uint(((_S1214)).z))).x * 255.0f));
                    uint _S1216 = atlasWidth_11;
                    uint _S1217 = 9U % atlasWidth_11;
                    int _S1218 = int(_S1217);
                    uint _S1219 = 9U / _S1216;
                    int3 _S1220 = int3(_S1218, int(_S1219), int(0));
                    uint rows_11 = ((_S1215 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1220)).xy), uint(((_S1220)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1178 * _S1184 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1221 = atlasWidth_11 / columns_11;
                    float _S1222 = float(_S1221);
                    uint _S1223 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1222, float(_S1223));
                    uint _S1224 = slot_23 % columns_11;
                    float _S1225 = float(_S1224);
                    uint _S1226 = slot_23 / columns_11;
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1225, float(_S1226)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(detailSource_0) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1227 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(detailSource_0)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1227);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1228 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1228)).xy), uint(((_S1228)).z)));
                    break;
                }
                uint _S1229 = atlasWidth_11 / uint(_S1227.x);
                float _S1230 = float(_S1229);
                uint _S1231 = (atlasHeight_11 - 1U) / uint(_S1227.y);
                float2 cellSize_23 = float2(_S1230, float(_S1231));
                _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_4 = uint(round(_S1155.y));
            if(operation_4 == 1U)
            {
                _S191 = _S191 * _S590;
                break;
            }
            if(operation_4 == 2U)
            {
                _S191 = _S191 + _S590;
                break;
            }
            if(operation_4 == 3U)
            {
                _S191 = _S191 - _S590;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S1155.z;
                _S191 = _S191 * float4((1.0f - factor_4))  + _S590 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S191.x);
    }
    if((textureMask_0 & 128U) != 0U)
    {
        bool useSecondary_6 = (secondaryMask_0 & 128U) != 0U;
        if((alternateMask_0 & 128U) != 0U)
        {
            if(useSecondary_6)
            {
                detailSource_0 = _S190;
            }
            else
            {
                detailSource_0 = _S189;
            }
        }
        else
        {
            if(useSecondary_6)
            {
                detailSource_0 = _S182;
            }
            else
            {
                detailSource_0 = _S181;
            }
        }
        bool _S1232 = (udimMask_0 & 128U) != 0U;
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
        uint _S1233 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1232)
            {
                for(;;)
                {
                    if(_S1233 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1234 = (&kernelContext_0)->occlusionTexture_0;
                    thread uint width_18;
                    thread uint height_18;
                    (*((&width_18)) = (_S1234).get_width(0)),(*((&height_18)) = (_S1234).get_height(0));
                    float2 size_14 = float2(float(width_18), float(height_18));
                    float2 _S1235 = float2(0.5f) ;
                    float2 remapped_14 = clamp((detailSource_0 * (size_14 - float2(2.0f) ) + float2(1.0f) ) / size_14, _S1235 / size_14, (size_14 - _S1235) / size_14);
                    if((_S1233 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_14.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S1233 & 2U) != 0U)
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
                _S191 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1236 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1236).get_width(0)),(*((&atlasHeight_12)) = (_S1236).get_height(0));
            int3 _S1237 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1237)).xy), uint(((_S1237)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1238 = atlasWidth_12;
                uint _S1239 = 2U % atlasWidth_12;
                int _S1240 = int(_S1239);
                uint _S1241 = 2U / _S1238;
                int3 _S1242 = int3(_S1240, int(_S1241), int(0));
                int _S1243 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1242)).xy), uint(((_S1242)).z))).x * 255.0f)));
                uint _S1244 = atlasWidth_12;
                uint _S1245 = 3U % atlasWidth_12;
                int _S1246 = int(_S1245);
                uint _S1247 = 3U / _S1244;
                int3 _S1248 = int3(_S1246, int(_S1247), int(0));
                int2 minimumTile_12 = int2(_S1243, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1248)).xy), uint(((_S1248)).z))).x * 255.0f))));
                uint _S1249 = atlasWidth_12;
                uint _S1250 = 4U % atlasWidth_12;
                int _S1251 = int(_S1250);
                uint _S1252 = 4U / _S1249;
                int3 _S1253 = int3(_S1251, int(_S1252), int(0));
                uint _S1254 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1253)).xy), uint(((_S1253)).z))).x * 255.0f));
                uint _S1255 = atlasWidth_12;
                uint _S1256 = 5U % atlasWidth_12;
                int _S1257 = int(_S1256);
                uint _S1258 = 5U / _S1255;
                int3 _S1259 = int3(_S1257, int(_S1258), int(0));
                uint _S1260 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1259)).xy), uint(((_S1259)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1254, _S1260);
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
                    int3 _S1261 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1261)).xy), uint(((_S1261)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1254 + uint(tile_24.x));
                uint _S1262 = atlasWidth_12;
                uint _S1263 = offset_12 % atlasWidth_12;
                int _S1264 = int(_S1263);
                uint _S1265 = offset_12 / _S1262;
                int3 _S1266 = int3(_S1264, int(_S1265), int(0));
                uint _S1267 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1266)).xy), uint(((_S1266)).z))).x * 255.0f));
                uint _S1268 = atlasWidth_12;
                uint _S1269 = offset_12 + 1U;
                uint _S1270 = _S1269 % atlasWidth_12;
                int _S1271 = int(_S1270);
                uint _S1272 = _S1269 / _S1268;
                int3 _S1273 = int3(_S1271, int(_S1272), int(0));
                uint slot_24 = _S1267 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1273)).xy), uint(((_S1273)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1274 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1274)).xy), uint(((_S1274)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1275 = atlasWidth_12;
                uint _S1276 = 6U % atlasWidth_12;
                int _S1277 = int(_S1276);
                uint _S1278 = 6U / _S1275;
                int3 _S1279 = int3(_S1277, int(_S1278), int(0));
                uint _S1280 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1279)).xy), uint(((_S1279)).z))).x * 255.0f));
                uint _S1281 = atlasWidth_12;
                uint _S1282 = 7U % atlasWidth_12;
                int _S1283 = int(_S1282);
                uint _S1284 = 7U / _S1281;
                int3 _S1285 = int3(_S1283, int(_S1284), int(0));
                uint columns_12 = _S1280 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1285)).xy), uint(((_S1285)).z))).x * 255.0f)) << 8U);
                uint _S1286 = atlasWidth_12;
                uint _S1287 = 8U % atlasWidth_12;
                int _S1288 = int(_S1287);
                uint _S1289 = 8U / _S1286;
                int3 _S1290 = int3(_S1288, int(_S1289), int(0));
                uint _S1291 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1290)).xy), uint(((_S1290)).z))).x * 255.0f));
                uint _S1292 = atlasWidth_12;
                uint _S1293 = 9U % atlasWidth_12;
                int _S1294 = int(_S1293);
                uint _S1295 = 9U / _S1292;
                int3 _S1296 = int3(_S1294, int(_S1295), int(0));
                uint rows_12 = ((_S1291 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1296)).xy), uint(((_S1296)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1254 * _S1260 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1297 = atlasWidth_12 / columns_12;
                float _S1298 = float(_S1297);
                uint _S1299 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1298, float(_S1299));
                uint _S1300 = slot_25 % columns_12;
                float _S1301 = float(_S1300);
                uint _S1302 = slot_25 / columns_12;
                _S191 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1301, float(_S1302)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(detailSource_0) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1303 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(detailSource_0)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1303);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1304 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S191 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1304)).xy), uint(((_S1304)).z)));
                break;
            }
            uint _S1305 = atlasWidth_12 / uint(_S1303.x);
            float _S1306 = float(_S1305);
            uint _S1307 = (atlasHeight_12 - 1U) / uint(_S1303.y);
            float2 cellSize_25 = float2(_S1306, float(_S1307));
            _S191 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1308 = float4(_S6->compositeControls_0) ;
            if((_S1308.x) != 128.0f)
            {
                break;
            }
            bool _S1309 = (_S1308.w) >= 0.5f;
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
            uint _S1310 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1309)
                {
                    for(;;)
                    {
                        if(_S1310 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1311 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_19;
                        thread uint height_19;
                        (*((&width_19)) = (_S1311).get_width(0)),(*((&height_19)) = (_S1311).get_height(0));
                        float2 size_15 = float2(float(width_19), float(height_19));
                        float2 _S1312 = float2(0.5f) ;
                        float2 remapped_15 = clamp((detailSource_0 * (size_15 - float2(2.0f) ) + float2(1.0f) ) / size_15, _S1312 / size_15, (size_15 - _S1312) / size_15);
                        if((_S1310 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_15.x;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.x;
                        }
                        if((_S1310 & 2U) != 0U)
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
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1313 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1313).get_width(0)),(*((&atlasHeight_13)) = (_S1313).get_height(0));
                int3 _S1314 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1314)).xy), uint(((_S1314)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1315 = atlasWidth_13;
                    uint _S1316 = 2U % atlasWidth_13;
                    int _S1317 = int(_S1316);
                    uint _S1318 = 2U / _S1315;
                    int3 _S1319 = int3(_S1317, int(_S1318), int(0));
                    int _S1320 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1319)).xy), uint(((_S1319)).z))).x * 255.0f)));
                    uint _S1321 = atlasWidth_13;
                    uint _S1322 = 3U % atlasWidth_13;
                    int _S1323 = int(_S1322);
                    uint _S1324 = 3U / _S1321;
                    int3 _S1325 = int3(_S1323, int(_S1324), int(0));
                    int2 minimumTile_13 = int2(_S1320, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1325)).xy), uint(((_S1325)).z))).x * 255.0f))));
                    uint _S1326 = atlasWidth_13;
                    uint _S1327 = 4U % atlasWidth_13;
                    int _S1328 = int(_S1327);
                    uint _S1329 = 4U / _S1326;
                    int3 _S1330 = int3(_S1328, int(_S1329), int(0));
                    uint _S1331 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1330)).xy), uint(((_S1330)).z))).x * 255.0f));
                    uint _S1332 = atlasWidth_13;
                    uint _S1333 = 5U % atlasWidth_13;
                    int _S1334 = int(_S1333);
                    uint _S1335 = 5U / _S1332;
                    int3 _S1336 = int3(_S1334, int(_S1335), int(0));
                    uint _S1337 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1336)).xy), uint(((_S1336)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1331, _S1337);
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
                        int3 _S1338 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1338)).xy), uint(((_S1338)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1331 + uint(tile_26.x));
                    uint _S1339 = atlasWidth_13;
                    uint _S1340 = offset_13 % atlasWidth_13;
                    int _S1341 = int(_S1340);
                    uint _S1342 = offset_13 / _S1339;
                    int3 _S1343 = int3(_S1341, int(_S1342), int(0));
                    uint _S1344 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1343)).xy), uint(((_S1343)).z))).x * 255.0f));
                    uint _S1345 = atlasWidth_13;
                    uint _S1346 = offset_13 + 1U;
                    uint _S1347 = _S1346 % atlasWidth_13;
                    int _S1348 = int(_S1347);
                    uint _S1349 = _S1346 / _S1345;
                    int3 _S1350 = int3(_S1348, int(_S1349), int(0));
                    uint slot_26 = _S1344 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1350)).xy), uint(((_S1350)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1351 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1351)).xy), uint(((_S1351)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1352 = atlasWidth_13;
                    uint _S1353 = 6U % atlasWidth_13;
                    int _S1354 = int(_S1353);
                    uint _S1355 = 6U / _S1352;
                    int3 _S1356 = int3(_S1354, int(_S1355), int(0));
                    uint _S1357 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1356)).xy), uint(((_S1356)).z))).x * 255.0f));
                    uint _S1358 = atlasWidth_13;
                    uint _S1359 = 7U % atlasWidth_13;
                    int _S1360 = int(_S1359);
                    uint _S1361 = 7U / _S1358;
                    int3 _S1362 = int3(_S1360, int(_S1361), int(0));
                    uint columns_13 = _S1357 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1362)).xy), uint(((_S1362)).z))).x * 255.0f)) << 8U);
                    uint _S1363 = atlasWidth_13;
                    uint _S1364 = 8U % atlasWidth_13;
                    int _S1365 = int(_S1364);
                    uint _S1366 = 8U / _S1363;
                    int3 _S1367 = int3(_S1365, int(_S1366), int(0));
                    uint _S1368 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1367)).xy), uint(((_S1367)).z))).x * 255.0f));
                    uint _S1369 = atlasWidth_13;
                    uint _S1370 = 9U % atlasWidth_13;
                    int _S1371 = int(_S1370);
                    uint _S1372 = 9U / _S1369;
                    int3 _S1373 = int3(_S1371, int(_S1372), int(0));
                    uint rows_13 = ((_S1368 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1373)).xy), uint(((_S1373)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1331 * _S1337 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1374 = atlasWidth_13 / columns_13;
                    float _S1375 = float(_S1374);
                    uint _S1376 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1375, float(_S1376));
                    uint _S1377 = slot_27 % columns_13;
                    float _S1378 = float(_S1377);
                    uint _S1379 = slot_27 / columns_13;
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1378, float(_S1379)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(detailSource_0) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1380 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(detailSource_0)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1380);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1381 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1381)).xy), uint(((_S1381)).z)));
                    break;
                }
                uint _S1382 = atlasWidth_13 / uint(_S1380.x);
                float _S1383 = float(_S1382);
                uint _S1384 = (atlasHeight_13 - 1U) / uint(_S1380.y);
                float2 cellSize_27 = float2(_S1383, float(_S1384));
                _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_5 = uint(round(_S1308.y));
            if(operation_5 == 1U)
            {
                _S191 = _S191 * _S590;
                break;
            }
            if(operation_5 == 2U)
            {
                _S191 = _S191 + _S590;
                break;
            }
            if(operation_5 == 3U)
            {
                _S191 = _S191 - _S590;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1308.z;
                _S191 = _S191 * float4((1.0f - factor_5))  + _S590 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S191.x);
    }
    else
    {
        occlusion_0 = _S161;
    }
    float3 specularColor_0;
    float clearcoatRoughness_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool useSecondary_7 = (secondaryMask_0 & 256U) != 0U;
        if((alternateMask_0 & 256U) != 0U)
        {
            if(useSecondary_7)
            {
                detailSource_0 = _S190;
            }
            else
            {
                detailSource_0 = _S189;
            }
        }
        else
        {
            if(useSecondary_7)
            {
                detailSource_0 = _S182;
            }
            else
            {
                detailSource_0 = _S181;
            }
        }
        bool _S1385 = (udimMask_0 & 256U) != 0U;
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
        uint _S1386 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1385)
            {
                for(;;)
                {
                    if(_S1386 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1387 = (&kernelContext_0)->specularColorTexture_0;
                    thread uint width_20;
                    thread uint height_20;
                    (*((&width_20)) = (_S1387).get_width(0)),(*((&height_20)) = (_S1387).get_height(0));
                    float2 size_16 = float2(float(width_20), float(height_20));
                    float2 _S1388 = float2(0.5f) ;
                    float2 remapped_16 = clamp((detailSource_0 * (size_16 - float2(2.0f) ) + float2(1.0f) ) / size_16, _S1388 / size_16, (size_16 - _S1388) / size_16);
                    if((_S1386 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_16.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.x;
                    }
                    if((_S1386 & 2U) != 0U)
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
                _S191 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1389 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1389).get_width(0)),(*((&atlasHeight_14)) = (_S1389).get_height(0));
            int3 _S1390 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1390)).xy), uint(((_S1390)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1391 = atlasWidth_14;
                uint _S1392 = 2U % atlasWidth_14;
                int _S1393 = int(_S1392);
                uint _S1394 = 2U / _S1391;
                int3 _S1395 = int3(_S1393, int(_S1394), int(0));
                int _S1396 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1395)).xy), uint(((_S1395)).z))).x * 255.0f)));
                uint _S1397 = atlasWidth_14;
                uint _S1398 = 3U % atlasWidth_14;
                int _S1399 = int(_S1398);
                uint _S1400 = 3U / _S1397;
                int3 _S1401 = int3(_S1399, int(_S1400), int(0));
                int2 minimumTile_14 = int2(_S1396, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1401)).xy), uint(((_S1401)).z))).x * 255.0f))));
                uint _S1402 = atlasWidth_14;
                uint _S1403 = 4U % atlasWidth_14;
                int _S1404 = int(_S1403);
                uint _S1405 = 4U / _S1402;
                int3 _S1406 = int3(_S1404, int(_S1405), int(0));
                uint _S1407 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1406)).xy), uint(((_S1406)).z))).x * 255.0f));
                uint _S1408 = atlasWidth_14;
                uint _S1409 = 5U % atlasWidth_14;
                int _S1410 = int(_S1409);
                uint _S1411 = 5U / _S1408;
                int3 _S1412 = int3(_S1410, int(_S1411), int(0));
                uint _S1413 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1412)).xy), uint(((_S1412)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1407, _S1413);
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
                    int3 _S1414 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1414)).xy), uint(((_S1414)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1407 + uint(tile_28.x));
                uint _S1415 = atlasWidth_14;
                uint _S1416 = offset_14 % atlasWidth_14;
                int _S1417 = int(_S1416);
                uint _S1418 = offset_14 / _S1415;
                int3 _S1419 = int3(_S1417, int(_S1418), int(0));
                uint _S1420 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1419)).xy), uint(((_S1419)).z))).x * 255.0f));
                uint _S1421 = atlasWidth_14;
                uint _S1422 = offset_14 + 1U;
                uint _S1423 = _S1422 % atlasWidth_14;
                int _S1424 = int(_S1423);
                uint _S1425 = _S1422 / _S1421;
                int3 _S1426 = int3(_S1424, int(_S1425), int(0));
                uint slot_28 = _S1420 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1426)).xy), uint(((_S1426)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1427 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1427)).xy), uint(((_S1427)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1428 = atlasWidth_14;
                uint _S1429 = 6U % atlasWidth_14;
                int _S1430 = int(_S1429);
                uint _S1431 = 6U / _S1428;
                int3 _S1432 = int3(_S1430, int(_S1431), int(0));
                uint _S1433 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1432)).xy), uint(((_S1432)).z))).x * 255.0f));
                uint _S1434 = atlasWidth_14;
                uint _S1435 = 7U % atlasWidth_14;
                int _S1436 = int(_S1435);
                uint _S1437 = 7U / _S1434;
                int3 _S1438 = int3(_S1436, int(_S1437), int(0));
                uint columns_14 = _S1433 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1438)).xy), uint(((_S1438)).z))).x * 255.0f)) << 8U);
                uint _S1439 = atlasWidth_14;
                uint _S1440 = 8U % atlasWidth_14;
                int _S1441 = int(_S1440);
                uint _S1442 = 8U / _S1439;
                int3 _S1443 = int3(_S1441, int(_S1442), int(0));
                uint _S1444 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1443)).xy), uint(((_S1443)).z))).x * 255.0f));
                uint _S1445 = atlasWidth_14;
                uint _S1446 = 9U % atlasWidth_14;
                int _S1447 = int(_S1446);
                uint _S1448 = 9U / _S1445;
                int3 _S1449 = int3(_S1447, int(_S1448), int(0));
                uint rows_14 = ((_S1444 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1449)).xy), uint(((_S1449)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1407 * _S1413 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1450 = atlasWidth_14 / columns_14;
                float _S1451 = float(_S1450);
                uint _S1452 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1451, float(_S1452));
                uint _S1453 = slot_29 % columns_14;
                float _S1454 = float(_S1453);
                uint _S1455 = slot_29 / columns_14;
                _S191 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1454, float(_S1455)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(detailSource_0) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1456 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(detailSource_0)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1456);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1457 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S191 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1457)).xy), uint(((_S1457)).z)));
                break;
            }
            uint _S1458 = atlasWidth_14 / uint(_S1456.x);
            float _S1459 = float(_S1458);
            uint _S1460 = (atlasHeight_14 - 1U) / uint(_S1456.y);
            float2 cellSize_29 = float2(_S1459, float(_S1460));
            _S191 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1461 = float4(_S6->compositeControls_0) ;
            if((_S1461.x) != 256.0f)
            {
                break;
            }
            bool _S1462 = (_S1461.w) >= 0.5f;
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
            uint _S1463 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1462)
                {
                    for(;;)
                    {
                        if(_S1463 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1464 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_21;
                        thread uint height_21;
                        (*((&width_21)) = (_S1464).get_width(0)),(*((&height_21)) = (_S1464).get_height(0));
                        float2 size_17 = float2(float(width_21), float(height_21));
                        float2 _S1465 = float2(0.5f) ;
                        float2 remapped_17 = clamp((detailSource_0 * (size_17 - float2(2.0f) ) + float2(1.0f) ) / size_17, _S1465 / size_17, (size_17 - _S1465) / size_17);
                        if((_S1463 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_17.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.x;
                        }
                        if((_S1463 & 2U) != 0U)
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
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1466 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1466).get_width(0)),(*((&atlasHeight_15)) = (_S1466).get_height(0));
                int3 _S1467 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1467)).xy), uint(((_S1467)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1468 = atlasWidth_15;
                    uint _S1469 = 2U % atlasWidth_15;
                    int _S1470 = int(_S1469);
                    uint _S1471 = 2U / _S1468;
                    int3 _S1472 = int3(_S1470, int(_S1471), int(0));
                    int _S1473 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1472)).xy), uint(((_S1472)).z))).x * 255.0f)));
                    uint _S1474 = atlasWidth_15;
                    uint _S1475 = 3U % atlasWidth_15;
                    int _S1476 = int(_S1475);
                    uint _S1477 = 3U / _S1474;
                    int3 _S1478 = int3(_S1476, int(_S1477), int(0));
                    int2 minimumTile_15 = int2(_S1473, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1478)).xy), uint(((_S1478)).z))).x * 255.0f))));
                    uint _S1479 = atlasWidth_15;
                    uint _S1480 = 4U % atlasWidth_15;
                    int _S1481 = int(_S1480);
                    uint _S1482 = 4U / _S1479;
                    int3 _S1483 = int3(_S1481, int(_S1482), int(0));
                    uint _S1484 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1483)).xy), uint(((_S1483)).z))).x * 255.0f));
                    uint _S1485 = atlasWidth_15;
                    uint _S1486 = 5U % atlasWidth_15;
                    int _S1487 = int(_S1486);
                    uint _S1488 = 5U / _S1485;
                    int3 _S1489 = int3(_S1487, int(_S1488), int(0));
                    uint _S1490 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1489)).xy), uint(((_S1489)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1484, _S1490);
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
                        int3 _S1491 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1491)).xy), uint(((_S1491)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1484 + uint(tile_30.x));
                    uint _S1492 = atlasWidth_15;
                    uint _S1493 = offset_15 % atlasWidth_15;
                    int _S1494 = int(_S1493);
                    uint _S1495 = offset_15 / _S1492;
                    int3 _S1496 = int3(_S1494, int(_S1495), int(0));
                    uint _S1497 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1496)).xy), uint(((_S1496)).z))).x * 255.0f));
                    uint _S1498 = atlasWidth_15;
                    uint _S1499 = offset_15 + 1U;
                    uint _S1500 = _S1499 % atlasWidth_15;
                    int _S1501 = int(_S1500);
                    uint _S1502 = _S1499 / _S1498;
                    int3 _S1503 = int3(_S1501, int(_S1502), int(0));
                    uint slot_30 = _S1497 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1503)).xy), uint(((_S1503)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1504 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1504)).xy), uint(((_S1504)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1505 = atlasWidth_15;
                    uint _S1506 = 6U % atlasWidth_15;
                    int _S1507 = int(_S1506);
                    uint _S1508 = 6U / _S1505;
                    int3 _S1509 = int3(_S1507, int(_S1508), int(0));
                    uint _S1510 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1509)).xy), uint(((_S1509)).z))).x * 255.0f));
                    uint _S1511 = atlasWidth_15;
                    uint _S1512 = 7U % atlasWidth_15;
                    int _S1513 = int(_S1512);
                    uint _S1514 = 7U / _S1511;
                    int3 _S1515 = int3(_S1513, int(_S1514), int(0));
                    uint columns_15 = _S1510 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1515)).xy), uint(((_S1515)).z))).x * 255.0f)) << 8U);
                    uint _S1516 = atlasWidth_15;
                    uint _S1517 = 8U % atlasWidth_15;
                    int _S1518 = int(_S1517);
                    uint _S1519 = 8U / _S1516;
                    int3 _S1520 = int3(_S1518, int(_S1519), int(0));
                    uint _S1521 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1520)).xy), uint(((_S1520)).z))).x * 255.0f));
                    uint _S1522 = atlasWidth_15;
                    uint _S1523 = 9U % atlasWidth_15;
                    int _S1524 = int(_S1523);
                    uint _S1525 = 9U / _S1522;
                    int3 _S1526 = int3(_S1524, int(_S1525), int(0));
                    uint rows_15 = ((_S1521 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1526)).xy), uint(((_S1526)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1484 * _S1490 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1527 = atlasWidth_15 / columns_15;
                    float _S1528 = float(_S1527);
                    uint _S1529 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1528, float(_S1529));
                    uint _S1530 = slot_31 % columns_15;
                    float _S1531 = float(_S1530);
                    uint _S1532 = slot_31 / columns_15;
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1531, float(_S1532)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(detailSource_0) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1533 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(detailSource_0)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1533);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1534 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1534)).xy), uint(((_S1534)).z)));
                    break;
                }
                uint _S1535 = atlasWidth_15 / uint(_S1533.x);
                float _S1536 = float(_S1535);
                uint _S1537 = (atlasHeight_15 - 1U) / uint(_S1533.y);
                float2 cellSize_31 = float2(_S1536, float(_S1537));
                _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_6 = uint(round(_S1461.y));
            if(operation_6 == 1U)
            {
                _S191 = _S191 * _S590;
                break;
            }
            if(operation_6 == 2U)
            {
                _S191 = _S191 + _S590;
                break;
            }
            if(operation_6 == 3U)
            {
                _S191 = _S191 - _S590;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1461.z;
                _S191 = _S191 * float4((1.0f - factor_6))  + _S590 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S191.xyz);
    }
    else
    {
        specularColor_0 = _S169;
    }
    if((textureMask_0 & 512U) != 0U)
    {
        bool useSecondary_8 = (secondaryMask_0 & 512U) != 0U;
        if((alternateMask_0 & 512U) != 0U)
        {
            if(useSecondary_8)
            {
                detailSource_0 = _S190;
            }
            else
            {
                detailSource_0 = _S189;
            }
        }
        else
        {
            if(useSecondary_8)
            {
                detailSource_0 = _S182;
            }
            else
            {
                detailSource_0 = _S181;
            }
        }
        bool _S1538 = (udimMask_0 & 512U) != 0U;
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
        uint _S1539 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1538)
            {
                for(;;)
                {
                    if(_S1539 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1540 = (&kernelContext_0)->clearcoatTexture_0;
                    thread uint width_22;
                    thread uint height_22;
                    (*((&width_22)) = (_S1540).get_width(0)),(*((&height_22)) = (_S1540).get_height(0));
                    float2 size_18 = float2(float(width_22), float(height_22));
                    float2 _S1541 = float2(0.5f) ;
                    float2 remapped_18 = clamp((detailSource_0 * (size_18 - float2(2.0f) ) + float2(1.0f) ) / size_18, _S1541 / size_18, (size_18 - _S1541) / size_18);
                    if((_S1539 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_18.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.x;
                    }
                    if((_S1539 & 2U) != 0U)
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
                _S191 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1542 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1542).get_width(0)),(*((&atlasHeight_16)) = (_S1542).get_height(0));
            int3 _S1543 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1543)).xy), uint(((_S1543)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1544 = atlasWidth_16;
                uint _S1545 = 2U % atlasWidth_16;
                int _S1546 = int(_S1545);
                uint _S1547 = 2U / _S1544;
                int3 _S1548 = int3(_S1546, int(_S1547), int(0));
                int _S1549 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1548)).xy), uint(((_S1548)).z))).x * 255.0f)));
                uint _S1550 = atlasWidth_16;
                uint _S1551 = 3U % atlasWidth_16;
                int _S1552 = int(_S1551);
                uint _S1553 = 3U / _S1550;
                int3 _S1554 = int3(_S1552, int(_S1553), int(0));
                int2 minimumTile_16 = int2(_S1549, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1554)).xy), uint(((_S1554)).z))).x * 255.0f))));
                uint _S1555 = atlasWidth_16;
                uint _S1556 = 4U % atlasWidth_16;
                int _S1557 = int(_S1556);
                uint _S1558 = 4U / _S1555;
                int3 _S1559 = int3(_S1557, int(_S1558), int(0));
                uint _S1560 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1559)).xy), uint(((_S1559)).z))).x * 255.0f));
                uint _S1561 = atlasWidth_16;
                uint _S1562 = 5U % atlasWidth_16;
                int _S1563 = int(_S1562);
                uint _S1564 = 5U / _S1561;
                int3 _S1565 = int3(_S1563, int(_S1564), int(0));
                uint _S1566 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1565)).xy), uint(((_S1565)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1560, _S1566);
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
                    int3 _S1567 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1567)).xy), uint(((_S1567)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1560 + uint(tile_32.x));
                uint _S1568 = atlasWidth_16;
                uint _S1569 = offset_16 % atlasWidth_16;
                int _S1570 = int(_S1569);
                uint _S1571 = offset_16 / _S1568;
                int3 _S1572 = int3(_S1570, int(_S1571), int(0));
                uint _S1573 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1572)).xy), uint(((_S1572)).z))).x * 255.0f));
                uint _S1574 = atlasWidth_16;
                uint _S1575 = offset_16 + 1U;
                uint _S1576 = _S1575 % atlasWidth_16;
                int _S1577 = int(_S1576);
                uint _S1578 = _S1575 / _S1574;
                int3 _S1579 = int3(_S1577, int(_S1578), int(0));
                uint slot_32 = _S1573 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1579)).xy), uint(((_S1579)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1580 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1580)).xy), uint(((_S1580)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1581 = atlasWidth_16;
                uint _S1582 = 6U % atlasWidth_16;
                int _S1583 = int(_S1582);
                uint _S1584 = 6U / _S1581;
                int3 _S1585 = int3(_S1583, int(_S1584), int(0));
                uint _S1586 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1585)).xy), uint(((_S1585)).z))).x * 255.0f));
                uint _S1587 = atlasWidth_16;
                uint _S1588 = 7U % atlasWidth_16;
                int _S1589 = int(_S1588);
                uint _S1590 = 7U / _S1587;
                int3 _S1591 = int3(_S1589, int(_S1590), int(0));
                uint columns_16 = _S1586 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1591)).xy), uint(((_S1591)).z))).x * 255.0f)) << 8U);
                uint _S1592 = atlasWidth_16;
                uint _S1593 = 8U % atlasWidth_16;
                int _S1594 = int(_S1593);
                uint _S1595 = 8U / _S1592;
                int3 _S1596 = int3(_S1594, int(_S1595), int(0));
                uint _S1597 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1596)).xy), uint(((_S1596)).z))).x * 255.0f));
                uint _S1598 = atlasWidth_16;
                uint _S1599 = 9U % atlasWidth_16;
                int _S1600 = int(_S1599);
                uint _S1601 = 9U / _S1598;
                int3 _S1602 = int3(_S1600, int(_S1601), int(0));
                uint rows_16 = ((_S1597 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1602)).xy), uint(((_S1602)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1560 * _S1566 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1603 = atlasWidth_16 / columns_16;
                float _S1604 = float(_S1603);
                uint _S1605 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1604, float(_S1605));
                uint _S1606 = slot_33 % columns_16;
                float _S1607 = float(_S1606);
                uint _S1608 = slot_33 / columns_16;
                _S191 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1607, float(_S1608)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(detailSource_0) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1609 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(detailSource_0)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1609);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1610 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S191 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1610)).xy), uint(((_S1610)).z)));
                break;
            }
            uint _S1611 = atlasWidth_16 / uint(_S1609.x);
            float _S1612 = float(_S1611);
            uint _S1613 = (atlasHeight_16 - 1U) / uint(_S1609.y);
            float2 cellSize_33 = float2(_S1612, float(_S1613));
            _S191 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1614 = float4(_S6->compositeControls_0) ;
            if((_S1614.x) != 512.0f)
            {
                break;
            }
            bool _S1615 = (_S1614.w) >= 0.5f;
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
            uint _S1616 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1615)
                {
                    for(;;)
                    {
                        if(_S1616 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1617 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_23;
                        thread uint height_23;
                        (*((&width_23)) = (_S1617).get_width(0)),(*((&height_23)) = (_S1617).get_height(0));
                        float2 size_19 = float2(float(width_23), float(height_23));
                        float2 _S1618 = float2(0.5f) ;
                        float2 remapped_19 = clamp((detailSource_0 * (size_19 - float2(2.0f) ) + float2(1.0f) ) / size_19, _S1618 / size_19, (size_19 - _S1618) / size_19);
                        if((_S1616 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_19.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.x;
                        }
                        if((_S1616 & 2U) != 0U)
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
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1619 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1619).get_width(0)),(*((&atlasHeight_17)) = (_S1619).get_height(0));
                int3 _S1620 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1620)).xy), uint(((_S1620)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1621 = atlasWidth_17;
                    uint _S1622 = 2U % atlasWidth_17;
                    int _S1623 = int(_S1622);
                    uint _S1624 = 2U / _S1621;
                    int3 _S1625 = int3(_S1623, int(_S1624), int(0));
                    int _S1626 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1625)).xy), uint(((_S1625)).z))).x * 255.0f)));
                    uint _S1627 = atlasWidth_17;
                    uint _S1628 = 3U % atlasWidth_17;
                    int _S1629 = int(_S1628);
                    uint _S1630 = 3U / _S1627;
                    int3 _S1631 = int3(_S1629, int(_S1630), int(0));
                    int2 minimumTile_17 = int2(_S1626, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1631)).xy), uint(((_S1631)).z))).x * 255.0f))));
                    uint _S1632 = atlasWidth_17;
                    uint _S1633 = 4U % atlasWidth_17;
                    int _S1634 = int(_S1633);
                    uint _S1635 = 4U / _S1632;
                    int3 _S1636 = int3(_S1634, int(_S1635), int(0));
                    uint _S1637 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1636)).xy), uint(((_S1636)).z))).x * 255.0f));
                    uint _S1638 = atlasWidth_17;
                    uint _S1639 = 5U % atlasWidth_17;
                    int _S1640 = int(_S1639);
                    uint _S1641 = 5U / _S1638;
                    int3 _S1642 = int3(_S1640, int(_S1641), int(0));
                    uint _S1643 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1642)).xy), uint(((_S1642)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1637, _S1643);
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
                        int3 _S1644 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1644)).xy), uint(((_S1644)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1637 + uint(tile_34.x));
                    uint _S1645 = atlasWidth_17;
                    uint _S1646 = offset_17 % atlasWidth_17;
                    int _S1647 = int(_S1646);
                    uint _S1648 = offset_17 / _S1645;
                    int3 _S1649 = int3(_S1647, int(_S1648), int(0));
                    uint _S1650 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1649)).xy), uint(((_S1649)).z))).x * 255.0f));
                    uint _S1651 = atlasWidth_17;
                    uint _S1652 = offset_17 + 1U;
                    uint _S1653 = _S1652 % atlasWidth_17;
                    int _S1654 = int(_S1653);
                    uint _S1655 = _S1652 / _S1651;
                    int3 _S1656 = int3(_S1654, int(_S1655), int(0));
                    uint slot_34 = _S1650 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1656)).xy), uint(((_S1656)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1657 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1657)).xy), uint(((_S1657)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1658 = atlasWidth_17;
                    uint _S1659 = 6U % atlasWidth_17;
                    int _S1660 = int(_S1659);
                    uint _S1661 = 6U / _S1658;
                    int3 _S1662 = int3(_S1660, int(_S1661), int(0));
                    uint _S1663 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1662)).xy), uint(((_S1662)).z))).x * 255.0f));
                    uint _S1664 = atlasWidth_17;
                    uint _S1665 = 7U % atlasWidth_17;
                    int _S1666 = int(_S1665);
                    uint _S1667 = 7U / _S1664;
                    int3 _S1668 = int3(_S1666, int(_S1667), int(0));
                    uint columns_17 = _S1663 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1668)).xy), uint(((_S1668)).z))).x * 255.0f)) << 8U);
                    uint _S1669 = atlasWidth_17;
                    uint _S1670 = 8U % atlasWidth_17;
                    int _S1671 = int(_S1670);
                    uint _S1672 = 8U / _S1669;
                    int3 _S1673 = int3(_S1671, int(_S1672), int(0));
                    uint _S1674 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1673)).xy), uint(((_S1673)).z))).x * 255.0f));
                    uint _S1675 = atlasWidth_17;
                    uint _S1676 = 9U % atlasWidth_17;
                    int _S1677 = int(_S1676);
                    uint _S1678 = 9U / _S1675;
                    int3 _S1679 = int3(_S1677, int(_S1678), int(0));
                    uint rows_17 = ((_S1674 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1679)).xy), uint(((_S1679)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1637 * _S1643 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1680 = atlasWidth_17 / columns_17;
                    float _S1681 = float(_S1680);
                    uint _S1682 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1681, float(_S1682));
                    uint _S1683 = slot_35 % columns_17;
                    float _S1684 = float(_S1683);
                    uint _S1685 = slot_35 / columns_17;
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1684, float(_S1685)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(detailSource_0) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1686 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(detailSource_0)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1686);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1687 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1687)).xy), uint(((_S1687)).z)));
                    break;
                }
                uint _S1688 = atlasWidth_17 / uint(_S1686.x);
                float _S1689 = float(_S1688);
                uint _S1690 = (atlasHeight_17 - 1U) / uint(_S1686.y);
                float2 cellSize_35 = float2(_S1689, float(_S1690));
                _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_7 = uint(round(_S1614.y));
            if(operation_7 == 1U)
            {
                _S191 = _S191 * _S590;
                break;
            }
            if(operation_7 == 2U)
            {
                _S191 = _S191 + _S590;
                break;
            }
            if(operation_7 == 3U)
            {
                _S191 = _S191 - _S590;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1614.z;
                _S191 = _S191 * float4((1.0f - factor_7))  + _S590 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S191.x);
    }
    else
    {
        clearcoatAmount_0 = _S171;
    }
    float ior_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool useSecondary_9 = (secondaryMask_0 & 1024U) != 0U;
        if((alternateMask_0 & 1024U) != 0U)
        {
            if(useSecondary_9)
            {
                detailSource_0 = _S190;
            }
            else
            {
                detailSource_0 = _S189;
            }
        }
        else
        {
            if(useSecondary_9)
            {
                detailSource_0 = _S182;
            }
            else
            {
                detailSource_0 = _S181;
            }
        }
        bool _S1691 = (udimMask_0 & 1024U) != 0U;
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
        uint _S1692 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1691)
            {
                for(;;)
                {
                    if(_S1692 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1693 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
                    thread uint width_24;
                    thread uint height_24;
                    (*((&width_24)) = (_S1693).get_width(0)),(*((&height_24)) = (_S1693).get_height(0));
                    float2 size_20 = float2(float(width_24), float(height_24));
                    float2 _S1694 = float2(0.5f) ;
                    float2 remapped_20 = clamp((detailSource_0 * (size_20 - float2(2.0f) ) + float2(1.0f) ) / size_20, _S1694 / size_20, (size_20 - _S1694) / size_20);
                    if((_S1692 & 1U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_20.x;
                    }
                    else
                    {
                        clearcoatRoughness_0 = detailSource_0.x;
                    }
                    if((_S1692 & 2U) != 0U)
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
                _S191 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1695 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1695).get_width(0)),(*((&atlasHeight_18)) = (_S1695).get_height(0));
            int3 _S1696 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1696)).xy), uint(((_S1696)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1697 = atlasWidth_18;
                uint _S1698 = 2U % atlasWidth_18;
                int _S1699 = int(_S1698);
                uint _S1700 = 2U / _S1697;
                int3 _S1701 = int3(_S1699, int(_S1700), int(0));
                int _S1702 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1701)).xy), uint(((_S1701)).z))).x * 255.0f)));
                uint _S1703 = atlasWidth_18;
                uint _S1704 = 3U % atlasWidth_18;
                int _S1705 = int(_S1704);
                uint _S1706 = 3U / _S1703;
                int3 _S1707 = int3(_S1705, int(_S1706), int(0));
                int2 minimumTile_18 = int2(_S1702, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1707)).xy), uint(((_S1707)).z))).x * 255.0f))));
                uint _S1708 = atlasWidth_18;
                uint _S1709 = 4U % atlasWidth_18;
                int _S1710 = int(_S1709);
                uint _S1711 = 4U / _S1708;
                int3 _S1712 = int3(_S1710, int(_S1711), int(0));
                uint _S1713 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1712)).xy), uint(((_S1712)).z))).x * 255.0f));
                uint _S1714 = atlasWidth_18;
                uint _S1715 = 5U % atlasWidth_18;
                int _S1716 = int(_S1715);
                uint _S1717 = 5U / _S1714;
                int3 _S1718 = int3(_S1716, int(_S1717), int(0));
                uint _S1719 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1718)).xy), uint(((_S1718)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1713, _S1719);
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
                    int3 _S1720 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1720)).xy), uint(((_S1720)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1713 + uint(tile_36.x));
                uint _S1721 = atlasWidth_18;
                uint _S1722 = offset_18 % atlasWidth_18;
                int _S1723 = int(_S1722);
                uint _S1724 = offset_18 / _S1721;
                int3 _S1725 = int3(_S1723, int(_S1724), int(0));
                uint _S1726 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1725)).xy), uint(((_S1725)).z))).x * 255.0f));
                uint _S1727 = atlasWidth_18;
                uint _S1728 = offset_18 + 1U;
                uint _S1729 = _S1728 % atlasWidth_18;
                int _S1730 = int(_S1729);
                uint _S1731 = _S1728 / _S1727;
                int3 _S1732 = int3(_S1730, int(_S1731), int(0));
                uint slot_36 = _S1726 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1732)).xy), uint(((_S1732)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1733 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1733)).xy), uint(((_S1733)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1734 = atlasWidth_18;
                uint _S1735 = 6U % atlasWidth_18;
                int _S1736 = int(_S1735);
                uint _S1737 = 6U / _S1734;
                int3 _S1738 = int3(_S1736, int(_S1737), int(0));
                uint _S1739 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1738)).xy), uint(((_S1738)).z))).x * 255.0f));
                uint _S1740 = atlasWidth_18;
                uint _S1741 = 7U % atlasWidth_18;
                int _S1742 = int(_S1741);
                uint _S1743 = 7U / _S1740;
                int3 _S1744 = int3(_S1742, int(_S1743), int(0));
                uint columns_18 = _S1739 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1744)).xy), uint(((_S1744)).z))).x * 255.0f)) << 8U);
                uint _S1745 = atlasWidth_18;
                uint _S1746 = 8U % atlasWidth_18;
                int _S1747 = int(_S1746);
                uint _S1748 = 8U / _S1745;
                int3 _S1749 = int3(_S1747, int(_S1748), int(0));
                uint _S1750 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1749)).xy), uint(((_S1749)).z))).x * 255.0f));
                uint _S1751 = atlasWidth_18;
                uint _S1752 = 9U % atlasWidth_18;
                int _S1753 = int(_S1752);
                uint _S1754 = 9U / _S1751;
                int3 _S1755 = int3(_S1753, int(_S1754), int(0));
                uint rows_18 = ((_S1750 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1755)).xy), uint(((_S1755)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1713 * _S1719 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1756 = atlasWidth_18 / columns_18;
                float _S1757 = float(_S1756);
                uint _S1758 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1757, float(_S1758));
                uint _S1759 = slot_37 % columns_18;
                float _S1760 = float(_S1759);
                uint _S1761 = slot_37 / columns_18;
                _S191 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1760, float(_S1761)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(detailSource_0) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1762 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(detailSource_0)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1762);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1763 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S191 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1763)).xy), uint(((_S1763)).z)));
                break;
            }
            uint _S1764 = atlasWidth_18 / uint(_S1762.x);
            float _S1765 = float(_S1764);
            uint _S1766 = (atlasHeight_18 - 1U) / uint(_S1762.y);
            float2 cellSize_37 = float2(_S1765, float(_S1766));
            _S191 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1767 = float4(_S6->compositeControls_0) ;
            if((_S1767.x) != 1024.0f)
            {
                break;
            }
            bool _S1768 = (_S1767.w) >= 0.5f;
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
            uint _S1769 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1768)
                {
                    for(;;)
                    {
                        if(_S1769 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1770 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_25;
                        thread uint height_25;
                        (*((&width_25)) = (_S1770).get_width(0)),(*((&height_25)) = (_S1770).get_height(0));
                        float2 size_21 = float2(float(width_25), float(height_25));
                        float2 _S1771 = float2(0.5f) ;
                        float2 remapped_21 = clamp((detailSource_0 * (size_21 - float2(2.0f) ) + float2(1.0f) ) / size_21, _S1771 / size_21, (size_21 - _S1771) / size_21);
                        if((_S1769 & 1U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_21.x;
                        }
                        else
                        {
                            clearcoatRoughness_0 = detailSource_0.x;
                        }
                        if((_S1769 & 2U) != 0U)
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
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1772 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1772).get_width(0)),(*((&atlasHeight_19)) = (_S1772).get_height(0));
                int3 _S1773 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1773)).xy), uint(((_S1773)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1774 = atlasWidth_19;
                    uint _S1775 = 2U % atlasWidth_19;
                    int _S1776 = int(_S1775);
                    uint _S1777 = 2U / _S1774;
                    int3 _S1778 = int3(_S1776, int(_S1777), int(0));
                    int _S1779 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1778)).xy), uint(((_S1778)).z))).x * 255.0f)));
                    uint _S1780 = atlasWidth_19;
                    uint _S1781 = 3U % atlasWidth_19;
                    int _S1782 = int(_S1781);
                    uint _S1783 = 3U / _S1780;
                    int3 _S1784 = int3(_S1782, int(_S1783), int(0));
                    int2 minimumTile_19 = int2(_S1779, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1784)).xy), uint(((_S1784)).z))).x * 255.0f))));
                    uint _S1785 = atlasWidth_19;
                    uint _S1786 = 4U % atlasWidth_19;
                    int _S1787 = int(_S1786);
                    uint _S1788 = 4U / _S1785;
                    int3 _S1789 = int3(_S1787, int(_S1788), int(0));
                    uint _S1790 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1789)).xy), uint(((_S1789)).z))).x * 255.0f));
                    uint _S1791 = atlasWidth_19;
                    uint _S1792 = 5U % atlasWidth_19;
                    int _S1793 = int(_S1792);
                    uint _S1794 = 5U / _S1791;
                    int3 _S1795 = int3(_S1793, int(_S1794), int(0));
                    uint _S1796 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1795)).xy), uint(((_S1795)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1790, _S1796);
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
                        int3 _S1797 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1797)).xy), uint(((_S1797)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1790 + uint(tile_38.x));
                    uint _S1798 = atlasWidth_19;
                    uint _S1799 = offset_19 % atlasWidth_19;
                    int _S1800 = int(_S1799);
                    uint _S1801 = offset_19 / _S1798;
                    int3 _S1802 = int3(_S1800, int(_S1801), int(0));
                    uint _S1803 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1802)).xy), uint(((_S1802)).z))).x * 255.0f));
                    uint _S1804 = atlasWidth_19;
                    uint _S1805 = offset_19 + 1U;
                    uint _S1806 = _S1805 % atlasWidth_19;
                    int _S1807 = int(_S1806);
                    uint _S1808 = _S1805 / _S1804;
                    int3 _S1809 = int3(_S1807, int(_S1808), int(0));
                    uint slot_38 = _S1803 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1809)).xy), uint(((_S1809)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1810 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1810)).xy), uint(((_S1810)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1811 = atlasWidth_19;
                    uint _S1812 = 6U % atlasWidth_19;
                    int _S1813 = int(_S1812);
                    uint _S1814 = 6U / _S1811;
                    int3 _S1815 = int3(_S1813, int(_S1814), int(0));
                    uint _S1816 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1815)).xy), uint(((_S1815)).z))).x * 255.0f));
                    uint _S1817 = atlasWidth_19;
                    uint _S1818 = 7U % atlasWidth_19;
                    int _S1819 = int(_S1818);
                    uint _S1820 = 7U / _S1817;
                    int3 _S1821 = int3(_S1819, int(_S1820), int(0));
                    uint columns_19 = _S1816 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1821)).xy), uint(((_S1821)).z))).x * 255.0f)) << 8U);
                    uint _S1822 = atlasWidth_19;
                    uint _S1823 = 8U % atlasWidth_19;
                    int _S1824 = int(_S1823);
                    uint _S1825 = 8U / _S1822;
                    int3 _S1826 = int3(_S1824, int(_S1825), int(0));
                    uint _S1827 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1826)).xy), uint(((_S1826)).z))).x * 255.0f));
                    uint _S1828 = atlasWidth_19;
                    uint _S1829 = 9U % atlasWidth_19;
                    int _S1830 = int(_S1829);
                    uint _S1831 = 9U / _S1828;
                    int3 _S1832 = int3(_S1830, int(_S1831), int(0));
                    uint rows_19 = ((_S1827 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1832)).xy), uint(((_S1832)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1790 * _S1796 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1833 = atlasWidth_19 / columns_19;
                    float _S1834 = float(_S1833);
                    uint _S1835 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1834, float(_S1835));
                    uint _S1836 = slot_39 % columns_19;
                    float _S1837 = float(_S1836);
                    uint _S1838 = slot_39 / columns_19;
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1837, float(_S1838)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(detailSource_0) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1839 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(detailSource_0)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1839);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1840 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1840)).xy), uint(((_S1840)).z)));
                    break;
                }
                uint _S1841 = atlasWidth_19 / uint(_S1839.x);
                float _S1842 = float(_S1841);
                uint _S1843 = (atlasHeight_19 - 1U) / uint(_S1839.y);
                float2 cellSize_39 = float2(_S1842, float(_S1843));
                _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_8 = uint(round(_S1767.y));
            if(operation_8 == 1U)
            {
                _S191 = _S191 * _S590;
                break;
            }
            if(operation_8 == 2U)
            {
                _S191 = _S191 + _S590;
                break;
            }
            if(operation_8 == 3U)
            {
                _S191 = _S191 - _S590;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1767.z;
                _S191 = _S191 * float4((1.0f - factor_8))  + _S590 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S191.x);
    }
    else
    {
        clearcoatRoughness_0 = _S172;
    }
    float lightType_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool useSecondary_10 = (secondaryMask_0 & 2048U) != 0U;
        if((alternateMask_0 & 2048U) != 0U)
        {
            if(useSecondary_10)
            {
                detailSource_0 = _S190;
            }
            else
            {
                detailSource_0 = _S189;
            }
        }
        else
        {
            if(useSecondary_10)
            {
                detailSource_0 = _S182;
            }
            else
            {
                detailSource_0 = _S181;
            }
        }
        bool _S1844 = (udimMask_0 & 2048U) != 0U;
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
        uint _S1845 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1844)
            {
                for(;;)
                {
                    if(_S1845 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1846 = (&kernelContext_0)->iorTexture_0;
                    thread uint width_26;
                    thread uint height_26;
                    (*((&width_26)) = (_S1846).get_width(0)),(*((&height_26)) = (_S1846).get_height(0));
                    float2 size_22 = float2(float(width_26), float(height_26));
                    float2 _S1847 = float2(0.5f) ;
                    float2 remapped_22 = clamp((detailSource_0 * (size_22 - float2(2.0f) ) + float2(1.0f) ) / size_22, _S1847 / size_22, (size_22 - _S1847) / size_22);
                    if((_S1845 & 1U) != 0U)
                    {
                        ior_0 = remapped_22.x;
                    }
                    else
                    {
                        ior_0 = detailSource_0.x;
                    }
                    if((_S1845 & 2U) != 0U)
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
                _S191 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1848 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_20;
            thread uint atlasHeight_20;
            (*((&atlasWidth_20)) = (_S1848).get_width(0)),(*((&atlasHeight_20)) = (_S1848).get_height(0));
            int3 _S1849 = int3(int(0), int(0), int(0));
            float4 metadata_20 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1849)).xy), uint(((_S1849)).z))) * float4(255.0f) );
            if((metadata_20.x) == 255.0f)
            {
                uint _S1850 = atlasWidth_20;
                uint _S1851 = 2U % atlasWidth_20;
                int _S1852 = int(_S1851);
                uint _S1853 = 2U / _S1850;
                int3 _S1854 = int3(_S1852, int(_S1853), int(0));
                int _S1855 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1854)).xy), uint(((_S1854)).z))).x * 255.0f)));
                uint _S1856 = atlasWidth_20;
                uint _S1857 = 3U % atlasWidth_20;
                int _S1858 = int(_S1857);
                uint _S1859 = 3U / _S1856;
                int3 _S1860 = int3(_S1858, int(_S1859), int(0));
                int2 minimumTile_20 = int2(_S1855, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1860)).xy), uint(((_S1860)).z))).x * 255.0f))));
                uint _S1861 = atlasWidth_20;
                uint _S1862 = 4U % atlasWidth_20;
                int _S1863 = int(_S1862);
                uint _S1864 = 4U / _S1861;
                int3 _S1865 = int3(_S1863, int(_S1864), int(0));
                uint _S1866 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1865)).xy), uint(((_S1865)).z))).x * 255.0f));
                uint _S1867 = atlasWidth_20;
                uint _S1868 = 5U % atlasWidth_20;
                int _S1869 = int(_S1868);
                uint _S1870 = 5U / _S1867;
                int3 _S1871 = int3(_S1869, int(_S1870), int(0));
                uint _S1872 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1871)).xy), uint(((_S1871)).z))).x * 255.0f));
                uint2 grid_20 = uint2(_S1866, _S1872);
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
                    int3 _S1873 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1873)).xy), uint(((_S1873)).z)));
                    break;
                }
                uint offset_20 = 10U + 2U * (uint(tile_40.y) * _S1866 + uint(tile_40.x));
                uint _S1874 = atlasWidth_20;
                uint _S1875 = offset_20 % atlasWidth_20;
                int _S1876 = int(_S1875);
                uint _S1877 = offset_20 / _S1874;
                int3 _S1878 = int3(_S1876, int(_S1877), int(0));
                uint _S1879 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1878)).xy), uint(((_S1878)).z))).x * 255.0f));
                uint _S1880 = atlasWidth_20;
                uint _S1881 = offset_20 + 1U;
                uint _S1882 = _S1881 % atlasWidth_20;
                int _S1883 = int(_S1882);
                uint _S1884 = _S1881 / _S1880;
                int3 _S1885 = int3(_S1883, int(_S1884), int(0));
                uint slot_40 = _S1879 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1885)).xy), uint(((_S1885)).z))).x * 255.0f)) << 8U);
                if(slot_40 == 0U)
                {
                    int3 _S1886 = int3(int(1), int(0), int(0));
                    _S191 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1886)).xy), uint(((_S1886)).z)));
                    break;
                }
                uint slot_41 = slot_40 - 1U;
                uint _S1887 = atlasWidth_20;
                uint _S1888 = 6U % atlasWidth_20;
                int _S1889 = int(_S1888);
                uint _S1890 = 6U / _S1887;
                int3 _S1891 = int3(_S1889, int(_S1890), int(0));
                uint _S1892 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1891)).xy), uint(((_S1891)).z))).x * 255.0f));
                uint _S1893 = atlasWidth_20;
                uint _S1894 = 7U % atlasWidth_20;
                int _S1895 = int(_S1894);
                uint _S1896 = 7U / _S1893;
                int3 _S1897 = int3(_S1895, int(_S1896), int(0));
                uint columns_20 = _S1892 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1897)).xy), uint(((_S1897)).z))).x * 255.0f)) << 8U);
                uint _S1898 = atlasWidth_20;
                uint _S1899 = 8U % atlasWidth_20;
                int _S1900 = int(_S1899);
                uint _S1901 = 8U / _S1898;
                int3 _S1902 = int3(_S1900, int(_S1901), int(0));
                uint _S1903 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1902)).xy), uint(((_S1902)).z))).x * 255.0f));
                uint _S1904 = atlasWidth_20;
                uint _S1905 = 9U % atlasWidth_20;
                int _S1906 = int(_S1905);
                uint _S1907 = 9U / _S1904;
                int3 _S1908 = int3(_S1906, int(_S1907), int(0));
                uint rows_20 = ((_S1903 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1908)).xy), uint(((_S1908)).z))).x * 255.0f)) << 8U)) + columns_20 - 1U) / columns_20;
                uint metadataRows_20 = (10U + 2U * _S1866 * _S1872 + atlasWidth_20 - 1U) / atlasWidth_20;
                uint _S1909 = atlasWidth_20 / columns_20;
                float _S1910 = float(_S1909);
                uint _S1911 = (atlasHeight_20 - metadataRows_20) / rows_20;
                float2 cellSize_40 = float2(_S1910, float(_S1911));
                uint _S1912 = slot_41 % columns_20;
                float _S1913 = float(_S1912);
                uint _S1914 = slot_41 / columns_20;
                _S191 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1913, float(_S1914)) * cellSize_40 + float2(1.5f, float(metadataRows_20) + 1.5f) + fract(detailSource_0) * max(cellSize_40 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
                break;
            }
            int2 _S1915 = int2(metadata_20.zw);
            int2 tile_41 = int2(floor(detailSource_0)) - int2(metadata_20.xy);
            if(any(tile_41 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_41 >= _S1915);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1916 = int3(int(min(1U, atlasWidth_20 - 1U)), int(0), int(0));
                _S191 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1916)).xy), uint(((_S1916)).z)));
                break;
            }
            uint _S1917 = atlasWidth_20 / uint(_S1915.x);
            float _S1918 = float(_S1917);
            uint _S1919 = (atlasHeight_20 - 1U) / uint(_S1915.y);
            float2 cellSize_41 = float2(_S1918, float(_S1919));
            _S191 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_41) * cellSize_41 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_41 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
            break;
        }
        for(;;)
        {
            float4 _S1920 = float4(_S6->compositeControls_0) ;
            if((_S1920.x) != 2048.0f)
            {
                break;
            }
            bool _S1921 = (_S1920.w) >= 0.5f;
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
            uint _S1922 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1921)
                {
                    for(;;)
                    {
                        if(_S1922 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1923 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_27;
                        thread uint height_27;
                        (*((&width_27)) = (_S1923).get_width(0)),(*((&height_27)) = (_S1923).get_height(0));
                        float2 size_23 = float2(float(width_27), float(height_27));
                        float2 _S1924 = float2(0.5f) ;
                        float2 remapped_23 = clamp((detailSource_0 * (size_23 - float2(2.0f) ) + float2(1.0f) ) / size_23, _S1924 / size_23, (size_23 - _S1924) / size_23);
                        if((_S1922 & 1U) != 0U)
                        {
                            ior_0 = remapped_23.x;
                        }
                        else
                        {
                            ior_0 = detailSource_0.x;
                        }
                        if((_S1922 & 2U) != 0U)
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
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1925 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_21;
                thread uint atlasHeight_21;
                (*((&atlasWidth_21)) = (_S1925).get_width(0)),(*((&atlasHeight_21)) = (_S1925).get_height(0));
                int3 _S1926 = int3(int(0), int(0), int(0));
                float4 metadata_21 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1926)).xy), uint(((_S1926)).z))) * float4(255.0f) );
                if((metadata_21.x) == 255.0f)
                {
                    uint _S1927 = atlasWidth_21;
                    uint _S1928 = 2U % atlasWidth_21;
                    int _S1929 = int(_S1928);
                    uint _S1930 = 2U / _S1927;
                    int3 _S1931 = int3(_S1929, int(_S1930), int(0));
                    int _S1932 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1931)).xy), uint(((_S1931)).z))).x * 255.0f)));
                    uint _S1933 = atlasWidth_21;
                    uint _S1934 = 3U % atlasWidth_21;
                    int _S1935 = int(_S1934);
                    uint _S1936 = 3U / _S1933;
                    int3 _S1937 = int3(_S1935, int(_S1936), int(0));
                    int2 minimumTile_21 = int2(_S1932, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1937)).xy), uint(((_S1937)).z))).x * 255.0f))));
                    uint _S1938 = atlasWidth_21;
                    uint _S1939 = 4U % atlasWidth_21;
                    int _S1940 = int(_S1939);
                    uint _S1941 = 4U / _S1938;
                    int3 _S1942 = int3(_S1940, int(_S1941), int(0));
                    uint _S1943 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1942)).xy), uint(((_S1942)).z))).x * 255.0f));
                    uint _S1944 = atlasWidth_21;
                    uint _S1945 = 5U % atlasWidth_21;
                    int _S1946 = int(_S1945);
                    uint _S1947 = 5U / _S1944;
                    int3 _S1948 = int3(_S1946, int(_S1947), int(0));
                    uint _S1949 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1948)).xy), uint(((_S1948)).z))).x * 255.0f));
                    uint2 grid_21 = uint2(_S1943, _S1949);
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
                        int3 _S1950 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1950)).xy), uint(((_S1950)).z)));
                        break;
                    }
                    uint offset_21 = 10U + 2U * (uint(tile_42.y) * _S1943 + uint(tile_42.x));
                    uint _S1951 = atlasWidth_21;
                    uint _S1952 = offset_21 % atlasWidth_21;
                    int _S1953 = int(_S1952);
                    uint _S1954 = offset_21 / _S1951;
                    int3 _S1955 = int3(_S1953, int(_S1954), int(0));
                    uint _S1956 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1955)).xy), uint(((_S1955)).z))).x * 255.0f));
                    uint _S1957 = atlasWidth_21;
                    uint _S1958 = offset_21 + 1U;
                    uint _S1959 = _S1958 % atlasWidth_21;
                    int _S1960 = int(_S1959);
                    uint _S1961 = _S1958 / _S1957;
                    int3 _S1962 = int3(_S1960, int(_S1961), int(0));
                    uint slot_42 = _S1956 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1962)).xy), uint(((_S1962)).z))).x * 255.0f)) << 8U);
                    if(slot_42 == 0U)
                    {
                        int3 _S1963 = int3(int(1), int(0), int(0));
                        _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1963)).xy), uint(((_S1963)).z)));
                        break;
                    }
                    uint slot_43 = slot_42 - 1U;
                    uint _S1964 = atlasWidth_21;
                    uint _S1965 = 6U % atlasWidth_21;
                    int _S1966 = int(_S1965);
                    uint _S1967 = 6U / _S1964;
                    int3 _S1968 = int3(_S1966, int(_S1967), int(0));
                    uint _S1969 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1968)).xy), uint(((_S1968)).z))).x * 255.0f));
                    uint _S1970 = atlasWidth_21;
                    uint _S1971 = 7U % atlasWidth_21;
                    int _S1972 = int(_S1971);
                    uint _S1973 = 7U / _S1970;
                    int3 _S1974 = int3(_S1972, int(_S1973), int(0));
                    uint columns_21 = _S1969 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1974)).xy), uint(((_S1974)).z))).x * 255.0f)) << 8U);
                    uint _S1975 = atlasWidth_21;
                    uint _S1976 = 8U % atlasWidth_21;
                    int _S1977 = int(_S1976);
                    uint _S1978 = 8U / _S1975;
                    int3 _S1979 = int3(_S1977, int(_S1978), int(0));
                    uint _S1980 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1979)).xy), uint(((_S1979)).z))).x * 255.0f));
                    uint _S1981 = atlasWidth_21;
                    uint _S1982 = 9U % atlasWidth_21;
                    int _S1983 = int(_S1982);
                    uint _S1984 = 9U / _S1981;
                    int3 _S1985 = int3(_S1983, int(_S1984), int(0));
                    uint rows_21 = ((_S1980 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1985)).xy), uint(((_S1985)).z))).x * 255.0f)) << 8U)) + columns_21 - 1U) / columns_21;
                    uint metadataRows_21 = (10U + 2U * _S1943 * _S1949 + atlasWidth_21 - 1U) / atlasWidth_21;
                    uint _S1986 = atlasWidth_21 / columns_21;
                    float _S1987 = float(_S1986);
                    uint _S1988 = (atlasHeight_21 - metadataRows_21) / rows_21;
                    float2 cellSize_42 = float2(_S1987, float(_S1988));
                    uint _S1989 = slot_43 % columns_21;
                    float _S1990 = float(_S1989);
                    uint _S1991 = slot_43 / columns_21;
                    _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1990, float(_S1991)) * cellSize_42 + float2(1.5f, float(metadataRows_21) + 1.5f) + fract(detailSource_0) * max(cellSize_42 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                    break;
                }
                int2 _S1992 = int2(metadata_21.zw);
                int2 tile_43 = int2(floor(detailSource_0)) - int2(metadata_21.xy);
                if(any(tile_43 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_43 >= _S1992);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1993 = int3(int(min(1U, atlasWidth_21 - 1U)), int(0), int(0));
                    _S590 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1993)).xy), uint(((_S1993)).z)));
                    break;
                }
                uint _S1994 = atlasWidth_21 / uint(_S1992.x);
                float _S1995 = float(_S1994);
                uint _S1996 = (atlasHeight_21 - 1U) / uint(_S1992.y);
                float2 cellSize_43 = float2(_S1995, float(_S1996));
                _S590 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_43) * cellSize_43 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_43 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                break;
            }
            uint operation_9 = uint(round(_S1920.y));
            if(operation_9 == 1U)
            {
                _S191 = _S191 * _S590;
                break;
            }
            if(operation_9 == 2U)
            {
                _S191 = _S191 + _S590;
                break;
            }
            if(operation_9 == 3U)
            {
                _S191 = _S191 - _S590;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1920.z;
                _S191 = _S191 * float4((1.0f - factor_9))  + _S590 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S191.x;
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
    float _S1997 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S1998 = max(0.00100000004749745f, roughness_0);
    float _S1999 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S2000 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S2000;
    bool omniPbrSpecular_0 = (_S173.w) >= 0.5f;
    float dielectricLevel_0 = _S159.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S2001 = float3(dielectricLevel_0) ;
        float3 _S2002 = float3(metallic_0) ;
        float3 _S2003 = mix(_S2001, diffuseColor_0, _S2002);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S2001, diffuseColor_0, _S2002);
        grazingIncidence_0 = _S2003;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S165.w) >= 0.5f)
        {
            float3 _S2004 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S2004;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S2005 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S2005);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S2005);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S2006 = _S2;
    float _S2007 = _S2.ambientLight_0.w;
    if(_S2007 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S2008 = _S2006.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S2008, _S2008)) > 0.0f;
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
    uint _S2009 = min(uint(_S2007), 128U);
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
    float4 _S2010 = float4(_S6->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S2010.w) ;
    uint domeLinkMask_0 = uint(max(_S175.x, 0.0f));
    FragmentFrameParameters_0 _S2011 = _S2;
    uint _S2012 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S2012) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S2013 = _S2006.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S2014 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S2014;
        for(;;)
        {
            if(lightCount_0 < _S2012)
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
        domeAmbient_1 = _S2013;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S2015 = !hasSceneLighting_0;
    if(_S2015)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S2009;
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
        bool _S2016;
        if(hasSceneLighting_0)
        {
            _S2016 = (((uint4(_S6->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S2016 = false;
        }
        if(_S2016)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S2017 = lightIndex_0 == 0U;
        bool _S2018;
        if(_S2017)
        {
            _S2018 = _S2015;
        }
        else
        {
            _S2018 = false;
        }
        if(_S2018)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S2019 = 224U + lightIndex_0 * 16U;
            float _S2020 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2019)>>2]);
            float _S2021 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2019 + 4U)>>2]);
            float _S2022 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2019 + 8U)>>2]);
            float _S2023 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2019 + 12U)>>2]);
            lightType_0 = _S2023;
        }
        bool _S2024;
        if(_S2017)
        {
            _S2024 = _S2015;
        }
        else
        {
            _S2024 = false;
        }
        if(_S2024)
        {
            lightDirection_0 = normalize((float4(_S6->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S2025 = 2272U + lightIndex_0 * 16U;
            float _S2026 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2025)>>2]);
            float _S2027 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2025 + 4U)>>2]);
            float _S2028 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2025 + 8U)>>2]);
            float _S2029 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2025 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S2026, _S2027, _S2028));
        }
        bool _S2030;
        if(_S2017)
        {
            _S2030 = _S2015;
        }
        else
        {
            _S2030 = false;
        }
        if(_S2030)
        {
            roughness_0 = (float4(_S6->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S2031 = 4320U + lightIndex_0 * 16U;
            float _S2032 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2031)>>2]);
            float _S2033 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2031 + 4U)>>2]);
            float _S2034 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2031 + 8U)>>2]);
            float _S2035 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2031 + 12U)>>2]);
            roughness_0 = _S2035;
        }
        bool _S2036;
        if(_S2017)
        {
            _S2036 = _S2015;
        }
        else
        {
            _S2036 = false;
        }
        if(_S2036)
        {
            diffuseColor_0 = _S2010.xyz;
        }
        else
        {
            uint _S2037 = 4320U + lightIndex_0 * 16U;
            float _S2038 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2037)>>2]);
            float _S2039 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2037 + 4U)>>2]);
            float _S2040 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2037 + 8U)>>2]);
            float _S2041 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2037 + 12U)>>2]);
            diffuseColor_0 = float3(_S2038, _S2039, _S2040);
        }
        bool _S2042;
        if(_S2017)
        {
            _S2042 = _S2015;
        }
        else
        {
            _S2042 = false;
        }
        if(_S2042)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S2043 = 6368U + lightIndex_0 * 16U;
            float _S2044 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2043)>>2]);
            float _S2045 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2043 + 4U)>>2]);
            float _S2046 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2043 + 8U)>>2]);
            float _S2047 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2043 + 12U)>>2]);
            metallic_0 = _S2044;
        }
        bool _S2048;
        if(_S2017)
        {
            _S2048 = _S2015;
        }
        else
        {
            _S2048 = false;
        }
        if(_S2048)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S2049 = 6368U + lightIndex_0 * 16U;
            float _S2050 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2049)>>2]);
            float _S2051 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2049 + 4U)>>2]);
            float _S2052 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2049 + 8U)>>2]);
            float _S2053 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2049 + 12U)>>2]);
            clearcoatRoughness_0 = _S2051;
        }
        bool _S2054;
        if(_S2017)
        {
            _S2054 = _S2015;
        }
        else
        {
            _S2054 = false;
        }
        if(_S2054)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S2055 = 8416U + lightIndex_0 * 16U;
            float _S2056 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2055)>>2]);
            float _S2057 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2055 + 4U)>>2]);
            float _S2058 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2055 + 8U)>>2]);
            float _S2059 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2055 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S2056, _S2057, _S2058));
        }
        bool _S2060;
        if(_S2017)
        {
            _S2060 = _S2015;
        }
        else
        {
            _S2060 = false;
        }
        if(_S2060)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S2061 = 10464U + lightIndex_0 * 16U;
            float _S2062 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2061)>>2]);
            float _S2063 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2061 + 4U)>>2]);
            float _S2064 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2061 + 8U)>>2]);
            float _S2065 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2061 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S2062, _S2063, _S2064));
        }
        bool _S2066;
        if(_S2017)
        {
            _S2066 = _S2015;
        }
        else
        {
            _S2066 = false;
        }
        float shapeX_0;
        if(_S2066)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S2067 = 8416U + lightIndex_0 * 16U;
            float _S2068 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2067)>>2]);
            float _S2069 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2067 + 4U)>>2]);
            float _S2070 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2067 + 8U)>>2]);
            float _S2071 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2067 + 12U)>>2]);
            shapeX_0 = _S2071;
        }
        bool _S2072;
        if(_S2017)
        {
            _S2072 = _S2015;
        }
        else
        {
            _S2072 = false;
        }
        float shapeY_0;
        if(_S2072)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S2073 = 10464U + lightIndex_0 * 16U;
            float _S2074 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2073)>>2]);
            float _S2075 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2073 + 4U)>>2]);
            float _S2076 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2073 + 8U)>>2]);
            float _S2077 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2073 + 12U)>>2]);
            shapeY_0 = _S2077;
        }
        bool _S2078;
        if(_S2017)
        {
            _S2078 = _S2015;
        }
        else
        {
            _S2078 = false;
        }
        float lightRadius_0;
        if(_S2078)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S2079 = 2272U + lightIndex_0 * 16U;
            float _S2080 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2079)>>2]);
            float _S2081 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2079 + 4U)>>2]);
            float _S2082 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2079 + 8U)>>2]);
            float _S2083 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2079 + 12U)>>2]);
            lightRadius_0 = _S2083;
        }
        bool _S2084;
        if(_S2017)
        {
            _S2084 = _S2015;
        }
        else
        {
            _S2084 = false;
        }
        if(_S2084)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S2085;
        if(_S2017)
        {
            _S2085 = _S2015;
        }
        else
        {
            _S2085 = false;
        }
        if(_S2085)
        {
            _S191 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S2086 = 15296U + lightIndex_0 * 16U;
            float _S2087 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2086)>>2]);
            float _S2088 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2086 + 4U)>>2]);
            float _S2089 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2086 + 8U)>>2]);
            float _S2090 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2086 + 12U)>>2]);
            _S191 = float4(_S2087, _S2088, _S2089, _S2090);
        }
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S2091 = 12960U + lightIndex_0 * 16U;
            float _S2092 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2091)>>2]);
            float _S2093 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2091 + 4U)>>2]);
            float _S2094 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2091 + 8U)>>2]);
            float _S2095 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2091 + 12U)>>2]);
            int shadowSlot_0 = int(_S2092);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S2096 = _S2;
                    FragmentFrameParameters_0 _S2097 = _S2;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - specularColor_0;
                    }
                    else
                    {
                        color_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S2096.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S2098 = lightClip_0.w;
                    if(_S2098 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S2098) ;
                    bool _S2099;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S2099 = true;
                    }
                    else
                    {
                        _S2099 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S2100;
                    if(_S2099)
                    {
                        _S2100 = true;
                    }
                    else
                    {
                        _S2100 = (ndc_0.z) < 0.0f;
                    }
                    bool _S2101;
                    if(_S2100)
                    {
                        _S2101 = true;
                    }
                    else
                    {
                        _S2101 = (ndc_0.z) > 1.0f;
                    }
                    if(_S2101)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S2102 = _S2097.shadowTile_0[shadowSlot_0].xy;
                    float2 _S2103 = _S2097.shadowTile_0[shadowSlot_0].zw;
                    float2 _S2104 = _S2102 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S2103;
                    float texel_0 = _S2096.shadowControls_0[shadowSlot_0].w;
                    float _S2105 = max(_S2096.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S2106 = ndc_0.z - _S2096.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S2107 = float2((texel_0 * 0.5f)) ;
                    float2 _S2108 = _S2102 + _S2107;
                    float2 _S2109 = _S2102 + _S2103 - _S2107;
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
                            if(_S2106 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S2104 + float2(float(x_0), float(y_0)) * float2((_S2105 * texel_0)) , _S2108, _S2109)), level((0.0f))).x))
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
        float3 _S2110 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S2110;
        sampleOffsets_0[int(1)] = _S2110;
        sampleOffsets_0[int(2)] = _S2110;
        sampleOffsets_0[int(3)] = _S2110;
        sampleOffsets_0[int(4)] = _S2110;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S2111 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S2111 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S2111 - halfHeight_0;
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
                uint _S2112 = 224U + lightIndex_0 * 16U;
                float _S2113 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2112)>>2]);
                float _S2114 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2112 + 4U)>>2]);
                float _S2115 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2112 + 8U)>>2]);
                float _S2116 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2112 + 12U)>>2]);
                float3 toLight_0 = float3(_S2113, _S2114, _S2115) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S2117 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S2117)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S2117;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S191.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S191.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S2118 = _S191.y;
                        if(_S2118 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S2118);
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
            float _S2119 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_0;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_0 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S2119)) ;
            }
            else
            {
                diffuseFresnel_0 = mix(normalIncidence_0, grazingIncidence_0, float3(_S2119) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_0);
            float3 _S2120 = float3(_S2119) ;
            float _S2121 = max(_S1998, 0.00100000004749745f);
            float alpha_0 = _S2121 * _S2121;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S2122 = 1.0f - k_0;
            float3 _S2123 = float3(max(4.0f * normalDotLight_0 * _S1997, 1.00000000317107685e-30f)) ;
            float3 _S2124 = mix(normalIncidence_0, grazingIncidence_0, _S2120) * float3((_S1997 / (_S1997 * _S2122 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S2122 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S2123;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S2125 = max(_S1999, 0.00100000004749745f);
                float alpha_1 = _S2125 * _S2125;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S2126 = 1.0f - k_1;
                directSpecular_0 = _S2124 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S2120) * float3((_S1997 / (_S1997 * _S2126 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S2126 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S2123);
            }
            else
            {
                directSpecular_0 = _S2124;
            }
            float3 _S2127 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S2127 * _S2000) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S2127);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S2128 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S2129 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S2130 = saturate(_S1998);
        float2 _S2131 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2129, _S2130)), level((0.0f)))).xy;
        float _S2132 = _S2131.x;
        float _S2133 = _S2131.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S2132)  + grazingIncidence_0 * float3(_S2133) ;
        if(omniPbrSpecular_0)
        {
            lightDirection_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S2132 + _S2133)) ;
        }
        else
        {
            lightDirection_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - lightDirection_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S2134 = max(_S2011.domeControls_0.y, 1.0f);
        float3 _S2135 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S2134 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S2011.domeControls_0.z;
            float _S2136 = _S2011.domeControls_0.w;
            for(;;)
            {
                bool _S2137 = _S2134 <= 1.0f;
                _S4 = _S2137;
                if(_S2137)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S2136, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S2134)), level((0.0f)))).xyz;
                break;
            }
            float _S2138 = _S2128.environmentControls_0.y;
            float _S2139 = _S2128.environmentControls_0.z;
            for(;;)
            {
                if(_S4)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2140 = max(_S2138, 1.0f);
                    float _S2141 = _S2140 - 1.0f;
                    float slice_0 = _S2130 * max(_S2141, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S2139, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S2140)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S2141) + v_0) / _S2140)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S2142 = max(_S2138, 1.0f);
                float total_0 = _S2142 * _S2134;
                float _S2143 = _S2142 - 1.0f;
                float slice_1 = _S2130 * max(_S2143, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S2139, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S2142;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S2143) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S2144 = max(_S2138, 1.0f);
                        float _S2145 = _S2144 - 1.0f;
                        float slice_2 = saturate(_S1999) * max(_S2145, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S2139, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S2144)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S2145) + v_2) / _S2144)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2146 = max(_S2138, 1.0f);
                    float total_1 = _S2146 * _S2134;
                    float _S2147 = _S2146 - 1.0f;
                    float slice_3 = saturate(_S1999) * max(_S2147, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S2139, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S2146;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S2147) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                color_0 = specularColor_0;
            }
            else
            {
                color_0 = _S2135;
            }
            lightTangent_0 = diffuseColor_0;
            lightBitangent_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightTangent_0 = _S2135;
            lightBitangent_0 = _S2135;
            color_0 = _S2135;
            for(;;)
            {
                if(sampleIndex_0 < _S2012)
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
                float _S2148 = _S2011.domeControls_0.w;
                for(;;)
                {
                    bool _S2149 = _S2134 <= 1.0f;
                    _S5 = _S2149;
                    if(_S2149)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S2148, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S2134)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightTangent_0 + diffuseColor_0;
                float _S2150 = _S2128.environmentControls_0.y;
                float _S2151 = _S2128.environmentControls_0.z;
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2152 = max(_S2150, 1.0f);
                        float _S2153 = _S2152 - 1.0f;
                        float slice_4 = _S2130 * max(_S2153, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S2151, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S2152)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S2153) + v_4) / _S2152)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2154 = max(_S2150, 1.0f);
                    float total_2 = _S2154 * _S2134;
                    float _S2155 = _S2154 - 1.0f;
                    float slice_5 = _S2130 * max(_S2155, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S2151, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S2154;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S2155) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
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
                            float _S2156 = max(_S2150, 1.0f);
                            float _S2157 = _S2156 - 1.0f;
                            float slice_6 = saturate(_S1999) * max(_S2157, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S2151, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S2156)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S2157) + v_6) / _S2156)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2158 = max(_S2150, 1.0f);
                        float total_3 = _S2158 * _S2134;
                        float _S2159 = _S2158 - 1.0f;
                        float slice_7 = saturate(_S1999) * max(_S2159, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S2151, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S2158;
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S2159) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S2160 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2129, saturate(_S1999))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * color_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S2160.x)  + float3(_S2160.y) );
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
    pixelOutput_0 _S2161 = { float4(color_3, opacity_0) };
    return _S2161;
}
