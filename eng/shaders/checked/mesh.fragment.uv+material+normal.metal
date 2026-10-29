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
    float4 _S176 = float4(_S6->uvTransformRow1_0) ;
    float2 _S177 = _S176.xy;
    float2 _S178 = float2(dot(_S175, _S1.texCoord_0) + _S174.z, dot(_S177, _S1.texCoord_0) + _S176.z);
    bool hasSceneLighting_0;
    float4 _S179;
    uint3 _S180;
    if((textureMask_0 & 2U) != 0U)
    {
        thread float4 diffuseSample_0;
        bool _S181 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S181)
            {
                _S179 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S178)));
                break;
            }
            texture2d<float, access::sample> _S182 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S182).get_width(0)),(*((&atlasHeight_0)) = (_S182).get_height(0));
            int3 _S183 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S183)).xy), uint(((_S183)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S184 = atlasWidth_0;
                uint _S185 = 2U % atlasWidth_0;
                int _S186 = int(_S185);
                uint _S187 = 2U / _S184;
                int3 _S188 = int3(_S186, int(_S187), int(0));
                int _S189 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S188)).xy), uint(((_S188)).z))).x * 255.0f)));
                uint _S190 = atlasWidth_0;
                uint _S191 = 3U % atlasWidth_0;
                int _S192 = int(_S191);
                uint _S193 = 3U / _S190;
                int3 _S194 = int3(_S192, int(_S193), int(0));
                int2 minimumTile_0 = int2(_S189, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S194)).xy), uint(((_S194)).z))).x * 255.0f))));
                uint _S195 = atlasWidth_0;
                uint _S196 = 4U % atlasWidth_0;
                int _S197 = int(_S196);
                uint _S198 = 4U / _S195;
                int3 _S199 = int3(_S197, int(_S198), int(0));
                uint _S200 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S199)).xy), uint(((_S199)).z))).x * 255.0f));
                uint _S201 = atlasWidth_0;
                uint _S202 = 5U % atlasWidth_0;
                int _S203 = int(_S202);
                uint _S204 = 5U / _S201;
                int3 _S205 = int3(_S203, int(_S204), int(0));
                uint _S206 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S205)).xy), uint(((_S205)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S200, _S206);
                int2 tile_0 = int2(floor(_S178)) - minimumTile_0;
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
                    int3 _S207 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S207)).xy), uint(((_S207)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S200 + uint(tile_0.x));
                uint _S208 = atlasWidth_0;
                uint _S209 = offset_0 % atlasWidth_0;
                int _S210 = int(_S209);
                uint _S211 = offset_0 / _S208;
                int3 _S212 = int3(_S210, int(_S211), int(0));
                uint _S213 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S212)).xy), uint(((_S212)).z))).x * 255.0f));
                uint _S214 = atlasWidth_0;
                uint _S215 = offset_0 + 1U;
                uint _S216 = _S215 % atlasWidth_0;
                int _S217 = int(_S216);
                uint _S218 = _S215 / _S214;
                int3 _S219 = int3(_S217, int(_S218), int(0));
                uint slot_0 = _S213 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S219)).xy), uint(((_S219)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S220 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S220)).xy), uint(((_S220)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S221 = atlasWidth_0;
                uint _S222 = 6U % atlasWidth_0;
                int _S223 = int(_S222);
                uint _S224 = 6U / _S221;
                int3 _S225 = int3(_S223, int(_S224), int(0));
                uint _S226 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S225)).xy), uint(((_S225)).z))).x * 255.0f));
                uint _S227 = atlasWidth_0;
                uint _S228 = 7U % atlasWidth_0;
                int _S229 = int(_S228);
                uint _S230 = 7U / _S227;
                int3 _S231 = int3(_S229, int(_S230), int(0));
                uint columns_0 = _S226 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S231)).xy), uint(((_S231)).z))).x * 255.0f)) << 8U);
                uint _S232 = atlasWidth_0;
                uint _S233 = 8U % atlasWidth_0;
                int _S234 = int(_S233);
                uint _S235 = 8U / _S232;
                int3 _S236 = int3(_S234, int(_S235), int(0));
                uint _S237 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S236)).xy), uint(((_S236)).z))).x * 255.0f));
                uint _S238 = atlasWidth_0;
                uint _S239 = 9U % atlasWidth_0;
                int _S240 = int(_S239);
                uint _S241 = 9U / _S238;
                int3 _S242 = int3(_S240, int(_S241), int(0));
                uint rows_0 = ((_S237 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S242)).xy), uint(((_S242)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S200 * _S206 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S243 = atlasWidth_0 / columns_0;
                float _S244 = float(_S243);
                uint _S245 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S244, float(_S245));
                uint _S246 = slot_1 % columns_0;
                float _S247 = float(_S246);
                uint _S248 = slot_1 / columns_0;
                _S179 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S247, float(_S248)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S178) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S249 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(_S178)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S249);
            }
            if(hasSceneLighting_0)
            {
                int3 _S250 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S179 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S250)).xy), uint(((_S250)).z)));
                break;
            }
            uint _S251 = atlasWidth_0 / uint(_S249.x);
            float _S252 = float(_S251);
            uint _S253 = (atlasHeight_0 - 1U) / uint(_S249.y);
            float2 cellSize_1 = float2(_S252, float(_S253));
            _S179 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S179;
        float4 _S254 = float4(_S6->compositeControls_0) ;
        float _S255 = _S254.x;
        if(_S255 == 0.0f)
        {
            hasSceneLighting_0 = (_S254.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S256 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S256).get_width(0)),(*((&sourceHeight_0)) = (_S256).get_height(0));
            texture2d<float, access::sample> _S257 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S257).get_width(0)),(*((&valueHeight_0)) = (_S257).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S258 = diffuseSample_0;
                for(;;)
                {
                    float _S259 = _S258.w;
                    if(_S259 > 1.0f)
                    {
                        diffuseColor_0 = _S258.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S260 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S260).get_width(0)),(*((&height_0)) = (_S260).get_height(0));
                    uint3 _S261 = uint3(round(_S258.xyz * float3(255.0f) ));
                    uint index_1 = ((_S261.x) | ((_S261.y) << 8U)) | ((_S261.z) << 16U);
                    if(_S259 == 0.0f)
                    {
                        _S180 = _S261;
                    }
                    else
                    {
                        _S180 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S180 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S262 = address_0.x;
                    uint _S263 = _S262 % width_0;
                    int _S264 = int(_S263);
                    uint _S265 = _S262 / width_0;
                    int3 _S266 = int3(_S264, int(_S265), int(0));
                    float _S267 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S266)).xy), uint(((_S266)).z))).x;
                    uint _S268 = address_0.y;
                    uint _S269 = _S268 % width_0;
                    int _S270 = int(_S269);
                    uint _S271 = _S268 / width_0;
                    int3 _S272 = int3(_S270, int(_S271), int(0));
                    float _S273 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S272)).xy), uint(((_S272)).z))).x;
                    uint _S274 = address_0.z;
                    uint _S275 = _S274 % width_0;
                    int _S276 = int(_S275);
                    uint _S277 = _S274 / width_0;
                    int3 _S278 = int3(_S276, int(_S277), int(0));
                    diffuseColor_0 = float3(_S267, _S273, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S278)).xy), uint(((_S278)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                diffuseSample_0.z = (((&kernelContext_0)->diffuseValuesTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S178))).x;
            }
        }
        float4 _S279 = diffuseSample_0;
        for(;;)
        {
            if(_S255 != 2.0f)
            {
                _S179 = _S279;
                break;
            }
            bool _S280 = (_S254.w) >= 0.5f;
            for(;;)
            {
                if(!_S280)
                {
                    _S179 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S178)));
                    break;
                }
                texture2d<float, access::sample> _S281 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S281).get_width(0)),(*((&atlasHeight_1)) = (_S281).get_height(0));
                int3 _S282 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S282)).xy), uint(((_S282)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S283 = atlasWidth_1;
                    uint _S284 = 2U % atlasWidth_1;
                    int _S285 = int(_S284);
                    uint _S286 = 2U / _S283;
                    int3 _S287 = int3(_S285, int(_S286), int(0));
                    int _S288 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S287)).xy), uint(((_S287)).z))).x * 255.0f)));
                    uint _S289 = atlasWidth_1;
                    uint _S290 = 3U % atlasWidth_1;
                    int _S291 = int(_S290);
                    uint _S292 = 3U / _S289;
                    int3 _S293 = int3(_S291, int(_S292), int(0));
                    int2 minimumTile_1 = int2(_S288, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S293)).xy), uint(((_S293)).z))).x * 255.0f))));
                    uint _S294 = atlasWidth_1;
                    uint _S295 = 4U % atlasWidth_1;
                    int _S296 = int(_S295);
                    uint _S297 = 4U / _S294;
                    int3 _S298 = int3(_S296, int(_S297), int(0));
                    uint _S299 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S298)).xy), uint(((_S298)).z))).x * 255.0f));
                    uint _S300 = atlasWidth_1;
                    uint _S301 = 5U % atlasWidth_1;
                    int _S302 = int(_S301);
                    uint _S303 = 5U / _S300;
                    int3 _S304 = int3(_S302, int(_S303), int(0));
                    uint _S305 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S304)).xy), uint(((_S304)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S299, _S305);
                    int2 tile_2 = int2(floor(_S178)) - minimumTile_1;
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
                        int3 _S306 = int3(int(1), int(0), int(0));
                        _S179 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S306)).xy), uint(((_S306)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S299 + uint(tile_2.x));
                    uint _S307 = atlasWidth_1;
                    uint _S308 = offset_1 % atlasWidth_1;
                    int _S309 = int(_S308);
                    uint _S310 = offset_1 / _S307;
                    int3 _S311 = int3(_S309, int(_S310), int(0));
                    uint _S312 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S311)).xy), uint(((_S311)).z))).x * 255.0f));
                    uint _S313 = atlasWidth_1;
                    uint _S314 = offset_1 + 1U;
                    uint _S315 = _S314 % atlasWidth_1;
                    int _S316 = int(_S315);
                    uint _S317 = _S314 / _S313;
                    int3 _S318 = int3(_S316, int(_S317), int(0));
                    uint slot_2 = _S312 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S318)).xy), uint(((_S318)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S319 = int3(int(1), int(0), int(0));
                        _S179 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S319)).xy), uint(((_S319)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S320 = atlasWidth_1;
                    uint _S321 = 6U % atlasWidth_1;
                    int _S322 = int(_S321);
                    uint _S323 = 6U / _S320;
                    int3 _S324 = int3(_S322, int(_S323), int(0));
                    uint _S325 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S324)).xy), uint(((_S324)).z))).x * 255.0f));
                    uint _S326 = atlasWidth_1;
                    uint _S327 = 7U % atlasWidth_1;
                    int _S328 = int(_S327);
                    uint _S329 = 7U / _S326;
                    int3 _S330 = int3(_S328, int(_S329), int(0));
                    uint columns_1 = _S325 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S330)).xy), uint(((_S330)).z))).x * 255.0f)) << 8U);
                    uint _S331 = atlasWidth_1;
                    uint _S332 = 8U % atlasWidth_1;
                    int _S333 = int(_S332);
                    uint _S334 = 8U / _S331;
                    int3 _S335 = int3(_S333, int(_S334), int(0));
                    uint _S336 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S335)).xy), uint(((_S335)).z))).x * 255.0f));
                    uint _S337 = atlasWidth_1;
                    uint _S338 = 9U % atlasWidth_1;
                    int _S339 = int(_S338);
                    uint _S340 = 9U / _S337;
                    int3 _S341 = int3(_S339, int(_S340), int(0));
                    uint rows_1 = ((_S336 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S341)).xy), uint(((_S341)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S299 * _S305 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S342 = atlasWidth_1 / columns_1;
                    float _S343 = float(_S342);
                    uint _S344 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S343, float(_S344));
                    uint _S345 = slot_3 % columns_1;
                    float _S346 = float(_S345);
                    uint _S347 = slot_3 / columns_1;
                    _S179 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S346, float(_S347)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S178) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S348 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(_S178)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S348);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S349 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S179 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S349)).xy), uint(((_S349)).z)));
                    break;
                }
                uint _S350 = atlasWidth_1 / uint(_S348.x);
                float _S351 = float(_S350);
                uint _S352 = (atlasHeight_1 - 1U) / uint(_S348.y);
                float2 cellSize_3 = float2(_S351, float(_S352));
                _S179 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S254.y));
            if(operation_0 == 1U)
            {
                _S179 = _S279 * _S179;
                break;
            }
            if(operation_0 == 2U)
            {
                _S179 = _S279 + _S179;
                break;
            }
            if(operation_0 == 3U)
            {
                _S179 = _S279 - _S179;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S254.z;
                _S179 = _S279 * float4((1.0f - factor_0))  + _S179 * float4(factor_0) ;
                break;
            }
            _S179 = _S279;
            break;
        }
        diffuseColor_0 = _S179.xyz;
    }
    thread float4 baseNormalSample_0;
    bool _S353 = (udimMask_0 & 4U) != 0U;
    for(;;)
    {
        if(!_S353)
        {
            _S179 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (_S178)));
            break;
        }
        texture2d<float, access::sample> _S354 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S354).get_width(0)),(*((&atlasHeight_2)) = (_S354).get_height(0));
        int3 _S355 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S355)).xy), uint(((_S355)).z))) * float4(255.0f) );
        if((metadata_2.x) == 255.0f)
        {
            uint _S356 = atlasWidth_2;
            uint _S357 = 2U % atlasWidth_2;
            int _S358 = int(_S357);
            uint _S359 = 2U / _S356;
            int3 _S360 = int3(_S358, int(_S359), int(0));
            int _S361 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S360)).xy), uint(((_S360)).z))).x * 255.0f)));
            uint _S362 = atlasWidth_2;
            uint _S363 = 3U % atlasWidth_2;
            int _S364 = int(_S363);
            uint _S365 = 3U / _S362;
            int3 _S366 = int3(_S364, int(_S365), int(0));
            int2 minimumTile_2 = int2(_S361, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S366)).xy), uint(((_S366)).z))).x * 255.0f))));
            uint _S367 = atlasWidth_2;
            uint _S368 = 4U % atlasWidth_2;
            int _S369 = int(_S368);
            uint _S370 = 4U / _S367;
            int3 _S371 = int3(_S369, int(_S370), int(0));
            uint _S372 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S371)).xy), uint(((_S371)).z))).x * 255.0f));
            uint _S373 = atlasWidth_2;
            uint _S374 = 5U % atlasWidth_2;
            int _S375 = int(_S374);
            uint _S376 = 5U / _S373;
            int3 _S377 = int3(_S375, int(_S376), int(0));
            uint _S378 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S377)).xy), uint(((_S377)).z))).x * 255.0f));
            uint2 grid_2 = uint2(_S372, _S378);
            int2 tile_4 = int2(floor(_S178)) - minimumTile_2;
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
                int3 _S379 = int3(int(1), int(0), int(0));
                _S179 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S379)).xy), uint(((_S379)).z)));
                break;
            }
            uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S372 + uint(tile_4.x));
            uint _S380 = atlasWidth_2;
            uint _S381 = offset_2 % atlasWidth_2;
            int _S382 = int(_S381);
            uint _S383 = offset_2 / _S380;
            int3 _S384 = int3(_S382, int(_S383), int(0));
            uint _S385 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S384)).xy), uint(((_S384)).z))).x * 255.0f));
            uint _S386 = atlasWidth_2;
            uint _S387 = offset_2 + 1U;
            uint _S388 = _S387 % atlasWidth_2;
            int _S389 = int(_S388);
            uint _S390 = _S387 / _S386;
            int3 _S391 = int3(_S389, int(_S390), int(0));
            uint slot_4 = _S385 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S391)).xy), uint(((_S391)).z))).x * 255.0f)) << 8U);
            if(slot_4 == 0U)
            {
                int3 _S392 = int3(int(1), int(0), int(0));
                _S179 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S392)).xy), uint(((_S392)).z)));
                break;
            }
            uint slot_5 = slot_4 - 1U;
            uint _S393 = atlasWidth_2;
            uint _S394 = 6U % atlasWidth_2;
            int _S395 = int(_S394);
            uint _S396 = 6U / _S393;
            int3 _S397 = int3(_S395, int(_S396), int(0));
            uint _S398 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S397)).xy), uint(((_S397)).z))).x * 255.0f));
            uint _S399 = atlasWidth_2;
            uint _S400 = 7U % atlasWidth_2;
            int _S401 = int(_S400);
            uint _S402 = 7U / _S399;
            int3 _S403 = int3(_S401, int(_S402), int(0));
            uint columns_2 = _S398 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S403)).xy), uint(((_S403)).z))).x * 255.0f)) << 8U);
            uint _S404 = atlasWidth_2;
            uint _S405 = 8U % atlasWidth_2;
            int _S406 = int(_S405);
            uint _S407 = 8U / _S404;
            int3 _S408 = int3(_S406, int(_S407), int(0));
            uint _S409 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S408)).xy), uint(((_S408)).z))).x * 255.0f));
            uint _S410 = atlasWidth_2;
            uint _S411 = 9U % atlasWidth_2;
            int _S412 = int(_S411);
            uint _S413 = 9U / _S410;
            int3 _S414 = int3(_S412, int(_S413), int(0));
            uint rows_2 = ((_S409 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S414)).xy), uint(((_S414)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
            uint metadataRows_2 = (10U + 2U * _S372 * _S378 + atlasWidth_2 - 1U) / atlasWidth_2;
            uint _S415 = atlasWidth_2 / columns_2;
            float _S416 = float(_S415);
            uint _S417 = (atlasHeight_2 - metadataRows_2) / rows_2;
            float2 cellSize_4 = float2(_S416, float(_S417));
            uint _S418 = slot_5 % columns_2;
            float _S419 = float(_S418);
            uint _S420 = slot_5 / columns_2;
            _S179 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S419, float(_S420)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(_S178) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        int2 _S421 = int2(metadata_2.zw);
        int2 tile_5 = int2(floor(_S178)) - int2(metadata_2.xy);
        if(any(tile_5 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_5 >= _S421);
        }
        if(hasSceneLighting_0)
        {
            int3 _S422 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S179 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S422)).xy), uint(((_S422)).z)));
            break;
        }
        uint _S423 = atlasWidth_2 / uint(_S421.x);
        float _S424 = float(_S423);
        uint _S425 = (atlasHeight_2 - 1U) / uint(_S421.y);
        float2 cellSize_5 = float2(_S424, float(_S425));
        _S179 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    baseNormalSample_0 = _S179;
    float4 _S426 = float4(_S6->detailUvTransformRow0_0) ;
    if((_S426.w) != 0.0f)
    {
        float4 _S427 = baseNormalSample_0;
        for(;;)
        {
            float _S428 = _S427.w;
            if(_S428 > 1.0f)
            {
                unlitColor_0 = _S427.xyz;
                break;
            }
            texture2d<float, access::sample> _S429 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_1;
            thread uint height_1;
            (*((&width_1)) = (_S429).get_width(0)),(*((&height_1)) = (_S429).get_height(0));
            uint3 _S430 = uint3(round(_S427.xyz * float3(255.0f) ));
            uint index_2 = ((_S430.x) | ((_S430.y) << 8U)) | ((_S430.z) << 16U);
            if(_S428 == 0.0f)
            {
                _S180 = _S430;
            }
            else
            {
                _S180 = uint3(index_2, index_2, index_2);
            }
            uint3 address_1 = _S180 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S431 = address_1.x;
            uint _S432 = _S431 % width_1;
            int _S433 = int(_S432);
            uint _S434 = _S431 / width_1;
            int3 _S435 = int3(_S433, int(_S434), int(0));
            float _S436 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S435)).xy), uint(((_S435)).z))).x;
            uint _S437 = address_1.y;
            uint _S438 = _S437 % width_1;
            int _S439 = int(_S438);
            uint _S440 = _S437 / width_1;
            int3 _S441 = int3(_S439, int(_S440), int(0));
            float _S442 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S441)).xy), uint(((_S441)).z))).x;
            uint _S443 = address_1.z;
            uint _S444 = _S443 % width_1;
            int _S445 = int(_S444);
            uint _S446 = _S443 / width_1;
            int3 _S447 = int3(_S445, int(_S446), int(0));
            unlitColor_0 = float3(_S436, _S442, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S447)).xy), uint(((_S447)).z))).x);
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
    thread float3 _S448 = unlitColor_0;
    float2 _S449 = float2(0.0f, 0.0f);
    float2 tangent_1;
    if(any(_S177 != _S449))
    {
        float _S450 = _S177.x;
        float _S451 = _S177.y;
        float2 tangent_2 = normalize(float2(_S451, - _S450) / float2(max(abs(_S450), abs(_S451))) );
        float2 bitangent_0 = float2(- tangent_2.y, tangent_2.x);
        if((dot(_S175, tangent_2)) < 0.0f)
        {
            tangent_1 = - tangent_2;
        }
        else
        {
            tangent_1 = tangent_2;
        }
        _S448.xy = tangent_1 * float2(_S448.x)  + bitangent_0 * float2(_S448.y) ;
    }
    float3 _S452 = _S448;
    if((textureMask_0 & 4096U) != 0U)
    {
        float2 _S453 = _S426.xy;
        float4 _S454 = float4(_S6->detailUvTransformRow1_0) ;
        float2 _S455 = _S454.xy;
        float2 detailUv_0 = float2(dot(_S453, _S1.texCoord_0) + _S426.z, dot(_S455, _S1.texCoord_0) + _S454.z);
        thread float4 detailSample_0;
        bool _S456 = (udimMask_0 & 4096U) != 0U;
        for(;;)
        {
            if(!_S456)
            {
                _S179 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailUv_0)));
                break;
            }
            texture2d<float, access::sample> _S457 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S457).get_width(0)),(*((&atlasHeight_3)) = (_S457).get_height(0));
            int3 _S458 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S458)).xy), uint(((_S458)).z))) * float4(255.0f) );
            if((metadata_3.x) == 255.0f)
            {
                uint _S459 = atlasWidth_3;
                uint _S460 = 2U % atlasWidth_3;
                int _S461 = int(_S460);
                uint _S462 = 2U / _S459;
                int3 _S463 = int3(_S461, int(_S462), int(0));
                int _S464 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S463)).xy), uint(((_S463)).z))).x * 255.0f)));
                uint _S465 = atlasWidth_3;
                uint _S466 = 3U % atlasWidth_3;
                int _S467 = int(_S466);
                uint _S468 = 3U / _S465;
                int3 _S469 = int3(_S467, int(_S468), int(0));
                int2 minimumTile_3 = int2(_S464, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S469)).xy), uint(((_S469)).z))).x * 255.0f))));
                uint _S470 = atlasWidth_3;
                uint _S471 = 4U % atlasWidth_3;
                int _S472 = int(_S471);
                uint _S473 = 4U / _S470;
                int3 _S474 = int3(_S472, int(_S473), int(0));
                uint _S475 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S474)).xy), uint(((_S474)).z))).x * 255.0f));
                uint _S476 = atlasWidth_3;
                uint _S477 = 5U % atlasWidth_3;
                int _S478 = int(_S477);
                uint _S479 = 5U / _S476;
                int3 _S480 = int3(_S478, int(_S479), int(0));
                uint _S481 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S480)).xy), uint(((_S480)).z))).x * 255.0f));
                uint2 grid_3 = uint2(_S475, _S481);
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
                    int3 _S482 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S482)).xy), uint(((_S482)).z)));
                    break;
                }
                uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S475 + uint(tile_6.x));
                uint _S483 = atlasWidth_3;
                uint _S484 = offset_3 % atlasWidth_3;
                int _S485 = int(_S484);
                uint _S486 = offset_3 / _S483;
                int3 _S487 = int3(_S485, int(_S486), int(0));
                uint _S488 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S487)).xy), uint(((_S487)).z))).x * 255.0f));
                uint _S489 = atlasWidth_3;
                uint _S490 = offset_3 + 1U;
                uint _S491 = _S490 % atlasWidth_3;
                int _S492 = int(_S491);
                uint _S493 = _S490 / _S489;
                int3 _S494 = int3(_S492, int(_S493), int(0));
                uint slot_6 = _S488 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S494)).xy), uint(((_S494)).z))).x * 255.0f)) << 8U);
                if(slot_6 == 0U)
                {
                    int3 _S495 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S495)).xy), uint(((_S495)).z)));
                    break;
                }
                uint slot_7 = slot_6 - 1U;
                uint _S496 = atlasWidth_3;
                uint _S497 = 6U % atlasWidth_3;
                int _S498 = int(_S497);
                uint _S499 = 6U / _S496;
                int3 _S500 = int3(_S498, int(_S499), int(0));
                uint _S501 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S500)).xy), uint(((_S500)).z))).x * 255.0f));
                uint _S502 = atlasWidth_3;
                uint _S503 = 7U % atlasWidth_3;
                int _S504 = int(_S503);
                uint _S505 = 7U / _S502;
                int3 _S506 = int3(_S504, int(_S505), int(0));
                uint columns_3 = _S501 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S506)).xy), uint(((_S506)).z))).x * 255.0f)) << 8U);
                uint _S507 = atlasWidth_3;
                uint _S508 = 8U % atlasWidth_3;
                int _S509 = int(_S508);
                uint _S510 = 8U / _S507;
                int3 _S511 = int3(_S509, int(_S510), int(0));
                uint _S512 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S511)).xy), uint(((_S511)).z))).x * 255.0f));
                uint _S513 = atlasWidth_3;
                uint _S514 = 9U % atlasWidth_3;
                int _S515 = int(_S514);
                uint _S516 = 9U / _S513;
                int3 _S517 = int3(_S515, int(_S516), int(0));
                uint rows_3 = ((_S512 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S517)).xy), uint(((_S517)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                uint metadataRows_3 = (10U + 2U * _S475 * _S481 + atlasWidth_3 - 1U) / atlasWidth_3;
                uint _S518 = atlasWidth_3 / columns_3;
                float _S519 = float(_S518);
                uint _S520 = (atlasHeight_3 - metadataRows_3) / rows_3;
                float2 cellSize_6 = float2(_S519, float(_S520));
                uint _S521 = slot_7 % columns_3;
                float _S522 = float(_S521);
                uint _S523 = slot_7 / columns_3;
                _S179 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S522, float(_S523)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(detailUv_0) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            int2 _S524 = int2(metadata_3.zw);
            int2 tile_7 = int2(floor(detailUv_0)) - int2(metadata_3.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S524);
            }
            if(hasSceneLighting_0)
            {
                int3 _S525 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S179 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S525)).xy), uint(((_S525)).z)));
                break;
            }
            uint _S526 = atlasWidth_3 / uint(_S524.x);
            float _S527 = float(_S526);
            uint _S528 = (atlasHeight_3 - 1U) / uint(_S524.y);
            float2 cellSize_7 = float2(_S527, float(_S528));
            _S179 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        detailSample_0 = _S179;
        if((_S454.w) != 0.0f)
        {
            float4 _S529 = detailSample_0;
            for(;;)
            {
                float _S530 = _S529.w;
                if(_S530 > 1.0f)
                {
                    unlitColor_0 = _S529.xyz;
                    break;
                }
                texture2d<float, access::sample> _S531 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_2;
                thread uint height_2;
                (*((&width_2)) = (_S531).get_width(0)),(*((&height_2)) = (_S531).get_height(0));
                uint3 _S532 = uint3(round(_S529.xyz * float3(255.0f) ));
                uint index_3 = ((_S532.x) | ((_S532.y) << 8U)) | ((_S532.z) << 16U);
                if(_S530 == 0.0f)
                {
                    _S180 = _S532;
                }
                else
                {
                    _S180 = uint3(index_3, index_3, index_3);
                }
                uint3 address_2 = _S180 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S533 = address_2.x;
                uint _S534 = _S533 % width_2;
                int _S535 = int(_S534);
                uint _S536 = _S533 / width_2;
                int3 _S537 = int3(_S535, int(_S536), int(0));
                float _S538 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S537)).xy), uint(((_S537)).z))).x;
                uint _S539 = address_2.y;
                uint _S540 = _S539 % width_2;
                int _S541 = int(_S540);
                uint _S542 = _S539 / width_2;
                int3 _S543 = int3(_S541, int(_S542), int(0));
                float _S544 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S543)).xy), uint(((_S543)).z))).x;
                uint _S545 = address_2.z;
                uint _S546 = _S545 % width_2;
                int _S547 = int(_S546);
                uint _S548 = _S545 / width_2;
                int3 _S549 = int3(_S547, int(_S548), int(0));
                unlitColor_0 = float3(_S538, _S544, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S549)).xy), uint(((_S549)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S550 = detail_0;
        if(any(_S455 != _S449))
        {
            float _S551 = _S455.x;
            float _S552 = _S455.y;
            float2 tangent_3 = normalize(float2(_S552, - _S551) / float2(max(abs(_S551), abs(_S552))) );
            float2 bitangent_1 = float2(- tangent_3.y, tangent_3.x);
            if((dot(_S453, tangent_3)) < 0.0f)
            {
                tangent_1 = - tangent_3;
            }
            else
            {
                tangent_1 = tangent_3;
            }
            _S550.xy = tangent_1 * float2(_S550.x)  + bitangent_1 * float2(_S550.y) ;
        }
        float3 _S553 = _S550;
        for(;;)
        {
            float3 t_0 = normalize(_S452) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S553) * float3(-1.0f, -1.0f, 1.0f);
            float _S554 = t_0.z;
            if(_S554 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S554))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S452;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S555 = float3(unlitColor_0.x) ;
    float3 _S556 = float3(unlitColor_0.y) ;
    float3 _S557 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S555 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S556 + _S1.normal_0 * _S557);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S555 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S556 + worldNormalBasis_0 * _S557);
    float roughness_0;
    float4 _S558;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S559 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S559)
            {
                _S179 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S178)));
                break;
            }
            texture2d<float, access::sample> _S560 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S560).get_width(0)),(*((&atlasHeight_4)) = (_S560).get_height(0));
            int3 _S561 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S561)).xy), uint(((_S561)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S562 = atlasWidth_4;
                uint _S563 = 2U % atlasWidth_4;
                int _S564 = int(_S563);
                uint _S565 = 2U / _S562;
                int3 _S566 = int3(_S564, int(_S565), int(0));
                int _S567 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S566)).xy), uint(((_S566)).z))).x * 255.0f)));
                uint _S568 = atlasWidth_4;
                uint _S569 = 3U % atlasWidth_4;
                int _S570 = int(_S569);
                uint _S571 = 3U / _S568;
                int3 _S572 = int3(_S570, int(_S571), int(0));
                int2 minimumTile_4 = int2(_S567, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S572)).xy), uint(((_S572)).z))).x * 255.0f))));
                uint _S573 = atlasWidth_4;
                uint _S574 = 4U % atlasWidth_4;
                int _S575 = int(_S574);
                uint _S576 = 4U / _S573;
                int3 _S577 = int3(_S575, int(_S576), int(0));
                uint _S578 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S577)).xy), uint(((_S577)).z))).x * 255.0f));
                uint _S579 = atlasWidth_4;
                uint _S580 = 5U % atlasWidth_4;
                int _S581 = int(_S580);
                uint _S582 = 5U / _S579;
                int3 _S583 = int3(_S581, int(_S582), int(0));
                uint _S584 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S583)).xy), uint(((_S583)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S578, _S584);
                int2 tile_8 = int2(floor(_S178)) - minimumTile_4;
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
                    int3 _S585 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S585)).xy), uint(((_S585)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S578 + uint(tile_8.x));
                uint _S586 = atlasWidth_4;
                uint _S587 = offset_4 % atlasWidth_4;
                int _S588 = int(_S587);
                uint _S589 = offset_4 / _S586;
                int3 _S590 = int3(_S588, int(_S589), int(0));
                uint _S591 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S590)).xy), uint(((_S590)).z))).x * 255.0f));
                uint _S592 = atlasWidth_4;
                uint _S593 = offset_4 + 1U;
                uint _S594 = _S593 % atlasWidth_4;
                int _S595 = int(_S594);
                uint _S596 = _S593 / _S592;
                int3 _S597 = int3(_S595, int(_S596), int(0));
                uint slot_8 = _S591 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S597)).xy), uint(((_S597)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S598 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S598)).xy), uint(((_S598)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S599 = atlasWidth_4;
                uint _S600 = 6U % atlasWidth_4;
                int _S601 = int(_S600);
                uint _S602 = 6U / _S599;
                int3 _S603 = int3(_S601, int(_S602), int(0));
                uint _S604 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S603)).xy), uint(((_S603)).z))).x * 255.0f));
                uint _S605 = atlasWidth_4;
                uint _S606 = 7U % atlasWidth_4;
                int _S607 = int(_S606);
                uint _S608 = 7U / _S605;
                int3 _S609 = int3(_S607, int(_S608), int(0));
                uint columns_4 = _S604 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S609)).xy), uint(((_S609)).z))).x * 255.0f)) << 8U);
                uint _S610 = atlasWidth_4;
                uint _S611 = 8U % atlasWidth_4;
                int _S612 = int(_S611);
                uint _S613 = 8U / _S610;
                int3 _S614 = int3(_S612, int(_S613), int(0));
                uint _S615 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S614)).xy), uint(((_S614)).z))).x * 255.0f));
                uint _S616 = atlasWidth_4;
                uint _S617 = 9U % atlasWidth_4;
                int _S618 = int(_S617);
                uint _S619 = 9U / _S616;
                int3 _S620 = int3(_S618, int(_S619), int(0));
                uint rows_4 = ((_S615 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S620)).xy), uint(((_S620)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S578 * _S584 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S621 = atlasWidth_4 / columns_4;
                float _S622 = float(_S621);
                uint _S623 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S622, float(_S623));
                uint _S624 = slot_9 % columns_4;
                float _S625 = float(_S624);
                uint _S626 = slot_9 / columns_4;
                _S179 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S625, float(_S626)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(_S178) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S627 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(_S178)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S627);
            }
            if(hasSceneLighting_0)
            {
                int3 _S628 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S179 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S628)).xy), uint(((_S628)).z)));
                break;
            }
            uint _S629 = atlasWidth_4 / uint(_S627.x);
            float _S630 = float(_S629);
            uint _S631 = (atlasHeight_4 - 1U) / uint(_S627.y);
            float2 cellSize_9 = float2(_S630, float(_S631));
            _S179 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S632 = float4(_S6->compositeControls_0) ;
            if((_S632.x) != 8.0f)
            {
                break;
            }
            bool _S633 = (_S632.w) >= 0.5f;
            for(;;)
            {
                if(!_S633)
                {
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S178)));
                    break;
                }
                texture2d<float, access::sample> _S634 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S634).get_width(0)),(*((&atlasHeight_5)) = (_S634).get_height(0));
                int3 _S635 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S635)).xy), uint(((_S635)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S636 = atlasWidth_5;
                    uint _S637 = 2U % atlasWidth_5;
                    int _S638 = int(_S637);
                    uint _S639 = 2U / _S636;
                    int3 _S640 = int3(_S638, int(_S639), int(0));
                    int _S641 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S640)).xy), uint(((_S640)).z))).x * 255.0f)));
                    uint _S642 = atlasWidth_5;
                    uint _S643 = 3U % atlasWidth_5;
                    int _S644 = int(_S643);
                    uint _S645 = 3U / _S642;
                    int3 _S646 = int3(_S644, int(_S645), int(0));
                    int2 minimumTile_5 = int2(_S641, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S646)).xy), uint(((_S646)).z))).x * 255.0f))));
                    uint _S647 = atlasWidth_5;
                    uint _S648 = 4U % atlasWidth_5;
                    int _S649 = int(_S648);
                    uint _S650 = 4U / _S647;
                    int3 _S651 = int3(_S649, int(_S650), int(0));
                    uint _S652 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S651)).xy), uint(((_S651)).z))).x * 255.0f));
                    uint _S653 = atlasWidth_5;
                    uint _S654 = 5U % atlasWidth_5;
                    int _S655 = int(_S654);
                    uint _S656 = 5U / _S653;
                    int3 _S657 = int3(_S655, int(_S656), int(0));
                    uint _S658 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S657)).xy), uint(((_S657)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S652, _S658);
                    int2 tile_10 = int2(floor(_S178)) - minimumTile_5;
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
                        int3 _S659 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S659)).xy), uint(((_S659)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S652 + uint(tile_10.x));
                    uint _S660 = atlasWidth_5;
                    uint _S661 = offset_5 % atlasWidth_5;
                    int _S662 = int(_S661);
                    uint _S663 = offset_5 / _S660;
                    int3 _S664 = int3(_S662, int(_S663), int(0));
                    uint _S665 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S664)).xy), uint(((_S664)).z))).x * 255.0f));
                    uint _S666 = atlasWidth_5;
                    uint _S667 = offset_5 + 1U;
                    uint _S668 = _S667 % atlasWidth_5;
                    int _S669 = int(_S668);
                    uint _S670 = _S667 / _S666;
                    int3 _S671 = int3(_S669, int(_S670), int(0));
                    uint slot_10 = _S665 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S671)).xy), uint(((_S671)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S672 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S672)).xy), uint(((_S672)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S673 = atlasWidth_5;
                    uint _S674 = 6U % atlasWidth_5;
                    int _S675 = int(_S674);
                    uint _S676 = 6U / _S673;
                    int3 _S677 = int3(_S675, int(_S676), int(0));
                    uint _S678 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S677)).xy), uint(((_S677)).z))).x * 255.0f));
                    uint _S679 = atlasWidth_5;
                    uint _S680 = 7U % atlasWidth_5;
                    int _S681 = int(_S680);
                    uint _S682 = 7U / _S679;
                    int3 _S683 = int3(_S681, int(_S682), int(0));
                    uint columns_5 = _S678 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S683)).xy), uint(((_S683)).z))).x * 255.0f)) << 8U);
                    uint _S684 = atlasWidth_5;
                    uint _S685 = 8U % atlasWidth_5;
                    int _S686 = int(_S685);
                    uint _S687 = 8U / _S684;
                    int3 _S688 = int3(_S686, int(_S687), int(0));
                    uint _S689 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S688)).xy), uint(((_S688)).z))).x * 255.0f));
                    uint _S690 = atlasWidth_5;
                    uint _S691 = 9U % atlasWidth_5;
                    int _S692 = int(_S691);
                    uint _S693 = 9U / _S690;
                    int3 _S694 = int3(_S692, int(_S693), int(0));
                    uint rows_5 = ((_S689 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S694)).xy), uint(((_S694)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S652 * _S658 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S695 = atlasWidth_5 / columns_5;
                    float _S696 = float(_S695);
                    uint _S697 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S696, float(_S697));
                    uint _S698 = slot_11 % columns_5;
                    float _S699 = float(_S698);
                    uint _S700 = slot_11 / columns_5;
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S699, float(_S700)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(_S178) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S701 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(_S178)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S701);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S702 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S702)).xy), uint(((_S702)).z)));
                    break;
                }
                uint _S703 = atlasWidth_5 / uint(_S701.x);
                float _S704 = float(_S703);
                uint _S705 = (atlasHeight_5 - 1U) / uint(_S701.y);
                float2 cellSize_11 = float2(_S704, float(_S705));
                _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_1 = uint(round(_S632.y));
            if(operation_1 == 1U)
            {
                _S179 = _S179 * _S558;
                break;
            }
            if(operation_1 == 2U)
            {
                _S179 = _S179 + _S558;
                break;
            }
            if(operation_1 == 3U)
            {
                _S179 = _S179 - _S558;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S632.z;
                _S179 = _S179 * float4((1.0f - factor_1))  + _S558 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S179.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S167;
    }
    float metallic_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool _S706 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S706)
            {
                _S179 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S178)));
                break;
            }
            texture2d<float, access::sample> _S707 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S707).get_width(0)),(*((&atlasHeight_6)) = (_S707).get_height(0));
            int3 _S708 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S708)).xy), uint(((_S708)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S709 = atlasWidth_6;
                uint _S710 = 2U % atlasWidth_6;
                int _S711 = int(_S710);
                uint _S712 = 2U / _S709;
                int3 _S713 = int3(_S711, int(_S712), int(0));
                int _S714 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S713)).xy), uint(((_S713)).z))).x * 255.0f)));
                uint _S715 = atlasWidth_6;
                uint _S716 = 3U % atlasWidth_6;
                int _S717 = int(_S716);
                uint _S718 = 3U / _S715;
                int3 _S719 = int3(_S717, int(_S718), int(0));
                int2 minimumTile_6 = int2(_S714, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S719)).xy), uint(((_S719)).z))).x * 255.0f))));
                uint _S720 = atlasWidth_6;
                uint _S721 = 4U % atlasWidth_6;
                int _S722 = int(_S721);
                uint _S723 = 4U / _S720;
                int3 _S724 = int3(_S722, int(_S723), int(0));
                uint _S725 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S724)).xy), uint(((_S724)).z))).x * 255.0f));
                uint _S726 = atlasWidth_6;
                uint _S727 = 5U % atlasWidth_6;
                int _S728 = int(_S727);
                uint _S729 = 5U / _S726;
                int3 _S730 = int3(_S728, int(_S729), int(0));
                uint _S731 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S730)).xy), uint(((_S730)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S725, _S731);
                int2 tile_12 = int2(floor(_S178)) - minimumTile_6;
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
                    int3 _S732 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S732)).xy), uint(((_S732)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S725 + uint(tile_12.x));
                uint _S733 = atlasWidth_6;
                uint _S734 = offset_6 % atlasWidth_6;
                int _S735 = int(_S734);
                uint _S736 = offset_6 / _S733;
                int3 _S737 = int3(_S735, int(_S736), int(0));
                uint _S738 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S737)).xy), uint(((_S737)).z))).x * 255.0f));
                uint _S739 = atlasWidth_6;
                uint _S740 = offset_6 + 1U;
                uint _S741 = _S740 % atlasWidth_6;
                int _S742 = int(_S741);
                uint _S743 = _S740 / _S739;
                int3 _S744 = int3(_S742, int(_S743), int(0));
                uint slot_12 = _S738 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S744)).xy), uint(((_S744)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S745 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S745)).xy), uint(((_S745)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S746 = atlasWidth_6;
                uint _S747 = 6U % atlasWidth_6;
                int _S748 = int(_S747);
                uint _S749 = 6U / _S746;
                int3 _S750 = int3(_S748, int(_S749), int(0));
                uint _S751 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S750)).xy), uint(((_S750)).z))).x * 255.0f));
                uint _S752 = atlasWidth_6;
                uint _S753 = 7U % atlasWidth_6;
                int _S754 = int(_S753);
                uint _S755 = 7U / _S752;
                int3 _S756 = int3(_S754, int(_S755), int(0));
                uint columns_6 = _S751 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S756)).xy), uint(((_S756)).z))).x * 255.0f)) << 8U);
                uint _S757 = atlasWidth_6;
                uint _S758 = 8U % atlasWidth_6;
                int _S759 = int(_S758);
                uint _S760 = 8U / _S757;
                int3 _S761 = int3(_S759, int(_S760), int(0));
                uint _S762 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S761)).xy), uint(((_S761)).z))).x * 255.0f));
                uint _S763 = atlasWidth_6;
                uint _S764 = 9U % atlasWidth_6;
                int _S765 = int(_S764);
                uint _S766 = 9U / _S763;
                int3 _S767 = int3(_S765, int(_S766), int(0));
                uint rows_6 = ((_S762 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S767)).xy), uint(((_S767)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S725 * _S731 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S768 = atlasWidth_6 / columns_6;
                float _S769 = float(_S768);
                uint _S770 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S769, float(_S770));
                uint _S771 = slot_13 % columns_6;
                float _S772 = float(_S771);
                uint _S773 = slot_13 / columns_6;
                _S179 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S772, float(_S773)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(_S178) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S774 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(_S178)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S774);
            }
            if(hasSceneLighting_0)
            {
                int3 _S775 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S179 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S775)).xy), uint(((_S775)).z)));
                break;
            }
            uint _S776 = atlasWidth_6 / uint(_S774.x);
            float _S777 = float(_S776);
            uint _S778 = (atlasHeight_6 - 1U) / uint(_S774.y);
            float2 cellSize_13 = float2(_S777, float(_S778));
            _S179 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        for(;;)
        {
            float4 _S779 = float4(_S6->compositeControls_0) ;
            if((_S779.x) != 32.0f)
            {
                break;
            }
            bool _S780 = (_S779.w) >= 0.5f;
            for(;;)
            {
                if(!_S780)
                {
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S178)));
                    break;
                }
                texture2d<float, access::sample> _S781 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S781).get_width(0)),(*((&atlasHeight_7)) = (_S781).get_height(0));
                int3 _S782 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S782)).xy), uint(((_S782)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S783 = atlasWidth_7;
                    uint _S784 = 2U % atlasWidth_7;
                    int _S785 = int(_S784);
                    uint _S786 = 2U / _S783;
                    int3 _S787 = int3(_S785, int(_S786), int(0));
                    int _S788 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S787)).xy), uint(((_S787)).z))).x * 255.0f)));
                    uint _S789 = atlasWidth_7;
                    uint _S790 = 3U % atlasWidth_7;
                    int _S791 = int(_S790);
                    uint _S792 = 3U / _S789;
                    int3 _S793 = int3(_S791, int(_S792), int(0));
                    int2 minimumTile_7 = int2(_S788, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S793)).xy), uint(((_S793)).z))).x * 255.0f))));
                    uint _S794 = atlasWidth_7;
                    uint _S795 = 4U % atlasWidth_7;
                    int _S796 = int(_S795);
                    uint _S797 = 4U / _S794;
                    int3 _S798 = int3(_S796, int(_S797), int(0));
                    uint _S799 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S798)).xy), uint(((_S798)).z))).x * 255.0f));
                    uint _S800 = atlasWidth_7;
                    uint _S801 = 5U % atlasWidth_7;
                    int _S802 = int(_S801);
                    uint _S803 = 5U / _S800;
                    int3 _S804 = int3(_S802, int(_S803), int(0));
                    uint _S805 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S804)).xy), uint(((_S804)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S799, _S805);
                    int2 tile_14 = int2(floor(_S178)) - minimumTile_7;
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
                        int3 _S806 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S806)).xy), uint(((_S806)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S799 + uint(tile_14.x));
                    uint _S807 = atlasWidth_7;
                    uint _S808 = offset_7 % atlasWidth_7;
                    int _S809 = int(_S808);
                    uint _S810 = offset_7 / _S807;
                    int3 _S811 = int3(_S809, int(_S810), int(0));
                    uint _S812 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S811)).xy), uint(((_S811)).z))).x * 255.0f));
                    uint _S813 = atlasWidth_7;
                    uint _S814 = offset_7 + 1U;
                    uint _S815 = _S814 % atlasWidth_7;
                    int _S816 = int(_S815);
                    uint _S817 = _S814 / _S813;
                    int3 _S818 = int3(_S816, int(_S817), int(0));
                    uint slot_14 = _S812 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S818)).xy), uint(((_S818)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S819 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S819)).xy), uint(((_S819)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S820 = atlasWidth_7;
                    uint _S821 = 6U % atlasWidth_7;
                    int _S822 = int(_S821);
                    uint _S823 = 6U / _S820;
                    int3 _S824 = int3(_S822, int(_S823), int(0));
                    uint _S825 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S824)).xy), uint(((_S824)).z))).x * 255.0f));
                    uint _S826 = atlasWidth_7;
                    uint _S827 = 7U % atlasWidth_7;
                    int _S828 = int(_S827);
                    uint _S829 = 7U / _S826;
                    int3 _S830 = int3(_S828, int(_S829), int(0));
                    uint columns_7 = _S825 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S830)).xy), uint(((_S830)).z))).x * 255.0f)) << 8U);
                    uint _S831 = atlasWidth_7;
                    uint _S832 = 8U % atlasWidth_7;
                    int _S833 = int(_S832);
                    uint _S834 = 8U / _S831;
                    int3 _S835 = int3(_S833, int(_S834), int(0));
                    uint _S836 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S835)).xy), uint(((_S835)).z))).x * 255.0f));
                    uint _S837 = atlasWidth_7;
                    uint _S838 = 9U % atlasWidth_7;
                    int _S839 = int(_S838);
                    uint _S840 = 9U / _S837;
                    int3 _S841 = int3(_S839, int(_S840), int(0));
                    uint rows_7 = ((_S836 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S841)).xy), uint(((_S841)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S799 * _S805 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S842 = atlasWidth_7 / columns_7;
                    float _S843 = float(_S842);
                    uint _S844 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S843, float(_S844));
                    uint _S845 = slot_15 % columns_7;
                    float _S846 = float(_S845);
                    uint _S847 = slot_15 / columns_7;
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S846, float(_S847)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(_S178) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S848 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(_S178)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S848);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S849 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S849)).xy), uint(((_S849)).z)));
                    break;
                }
                uint _S850 = atlasWidth_7 / uint(_S848.x);
                float _S851 = float(_S850);
                uint _S852 = (atlasHeight_7 - 1U) / uint(_S848.y);
                float2 cellSize_15 = float2(_S851, float(_S852));
                _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_2 = uint(round(_S779.y));
            if(operation_2 == 1U)
            {
                _S179 = _S179 * _S558;
                break;
            }
            if(operation_2 == 2U)
            {
                _S179 = _S179 + _S558;
                break;
            }
            if(operation_2 == 3U)
            {
                _S179 = _S179 - _S558;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S779.z;
                _S179 = _S179 * float4((1.0f - factor_2))  + _S558 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S179.x);
    }
    else
    {
        metallic_0 = _S166;
    }
    float3 emissiveColor_1;
    if((textureMask_0 & 16U) != 0U)
    {
        thread float4 emissionSample_0;
        bool _S853 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S853)
            {
                _S179 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S178)));
                break;
            }
            texture2d<float, access::sample> _S854 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S854).get_width(0)),(*((&atlasHeight_8)) = (_S854).get_height(0));
            int3 _S855 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S855)).xy), uint(((_S855)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S856 = atlasWidth_8;
                uint _S857 = 2U % atlasWidth_8;
                int _S858 = int(_S857);
                uint _S859 = 2U / _S856;
                int3 _S860 = int3(_S858, int(_S859), int(0));
                int _S861 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S860)).xy), uint(((_S860)).z))).x * 255.0f)));
                uint _S862 = atlasWidth_8;
                uint _S863 = 3U % atlasWidth_8;
                int _S864 = int(_S863);
                uint _S865 = 3U / _S862;
                int3 _S866 = int3(_S864, int(_S865), int(0));
                int2 minimumTile_8 = int2(_S861, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S866)).xy), uint(((_S866)).z))).x * 255.0f))));
                uint _S867 = atlasWidth_8;
                uint _S868 = 4U % atlasWidth_8;
                int _S869 = int(_S868);
                uint _S870 = 4U / _S867;
                int3 _S871 = int3(_S869, int(_S870), int(0));
                uint _S872 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S871)).xy), uint(((_S871)).z))).x * 255.0f));
                uint _S873 = atlasWidth_8;
                uint _S874 = 5U % atlasWidth_8;
                int _S875 = int(_S874);
                uint _S876 = 5U / _S873;
                int3 _S877 = int3(_S875, int(_S876), int(0));
                uint _S878 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S877)).xy), uint(((_S877)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S872, _S878);
                int2 tile_16 = int2(floor(_S178)) - minimumTile_8;
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
                    int3 _S879 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S879)).xy), uint(((_S879)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S872 + uint(tile_16.x));
                uint _S880 = atlasWidth_8;
                uint _S881 = offset_8 % atlasWidth_8;
                int _S882 = int(_S881);
                uint _S883 = offset_8 / _S880;
                int3 _S884 = int3(_S882, int(_S883), int(0));
                uint _S885 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S884)).xy), uint(((_S884)).z))).x * 255.0f));
                uint _S886 = atlasWidth_8;
                uint _S887 = offset_8 + 1U;
                uint _S888 = _S887 % atlasWidth_8;
                int _S889 = int(_S888);
                uint _S890 = _S887 / _S886;
                int3 _S891 = int3(_S889, int(_S890), int(0));
                uint slot_16 = _S885 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S891)).xy), uint(((_S891)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S892 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S892)).xy), uint(((_S892)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S893 = atlasWidth_8;
                uint _S894 = 6U % atlasWidth_8;
                int _S895 = int(_S894);
                uint _S896 = 6U / _S893;
                int3 _S897 = int3(_S895, int(_S896), int(0));
                uint _S898 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S897)).xy), uint(((_S897)).z))).x * 255.0f));
                uint _S899 = atlasWidth_8;
                uint _S900 = 7U % atlasWidth_8;
                int _S901 = int(_S900);
                uint _S902 = 7U / _S899;
                int3 _S903 = int3(_S901, int(_S902), int(0));
                uint columns_8 = _S898 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S903)).xy), uint(((_S903)).z))).x * 255.0f)) << 8U);
                uint _S904 = atlasWidth_8;
                uint _S905 = 8U % atlasWidth_8;
                int _S906 = int(_S905);
                uint _S907 = 8U / _S904;
                int3 _S908 = int3(_S906, int(_S907), int(0));
                uint _S909 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S908)).xy), uint(((_S908)).z))).x * 255.0f));
                uint _S910 = atlasWidth_8;
                uint _S911 = 9U % atlasWidth_8;
                int _S912 = int(_S911);
                uint _S913 = 9U / _S910;
                int3 _S914 = int3(_S912, int(_S913), int(0));
                uint rows_8 = ((_S909 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S914)).xy), uint(((_S914)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S872 * _S878 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S915 = atlasWidth_8 / columns_8;
                float _S916 = float(_S915);
                uint _S917 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S916, float(_S917));
                uint _S918 = slot_17 % columns_8;
                float _S919 = float(_S918);
                uint _S920 = slot_17 / columns_8;
                _S179 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S919, float(_S920)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(_S178) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S921 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(_S178)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S921);
            }
            if(hasSceneLighting_0)
            {
                int3 _S922 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S179 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S922)).xy), uint(((_S922)).z)));
                break;
            }
            uint _S923 = atlasWidth_8 / uint(_S921.x);
            float _S924 = float(_S923);
            uint _S925 = (atlasHeight_8 - 1U) / uint(_S921.y);
            float2 cellSize_17 = float2(_S924, float(_S925));
            _S179 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        emissionSample_0 = _S179;
        if((_S174.w) != 0.0f)
        {
            texture2d<float, access::sample> _S926 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S926).get_width(0)),(*((&sourceHeight_1)) = (_S926).get_height(0));
            texture2d<float, access::sample> _S927 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S927).get_width(0)),(*((&valueHeight_1)) = (_S927).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S928 = emissionSample_0;
                for(;;)
                {
                    float _S929 = _S928.w;
                    if(_S929 > 1.0f)
                    {
                        unlitColor_0 = _S928.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S930 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_3;
                    thread uint height_3;
                    (*((&width_3)) = (_S930).get_width(0)),(*((&height_3)) = (_S930).get_height(0));
                    uint3 _S931 = uint3(round(_S928.xyz * float3(255.0f) ));
                    uint index_4 = ((_S931.x) | ((_S931.y) << 8U)) | ((_S931.z) << 16U);
                    if(_S929 == 0.0f)
                    {
                        _S180 = _S931;
                    }
                    else
                    {
                        _S180 = uint3(index_4, index_4, index_4);
                    }
                    uint3 address_3 = _S180 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S932 = address_3.x;
                    uint _S933 = _S932 % width_3;
                    int _S934 = int(_S933);
                    uint _S935 = _S932 / width_3;
                    int3 _S936 = int3(_S934, int(_S935), int(0));
                    float _S937 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S936)).xy), uint(((_S936)).z))).x;
                    uint _S938 = address_3.y;
                    uint _S939 = _S938 % width_3;
                    int _S940 = int(_S939);
                    uint _S941 = _S938 / width_3;
                    int3 _S942 = int3(_S940, int(_S941), int(0));
                    float _S943 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S942)).xy), uint(((_S942)).z))).x;
                    uint _S944 = address_3.z;
                    uint _S945 = _S944 % width_3;
                    int _S946 = int(_S945);
                    uint _S947 = _S944 / width_3;
                    int3 _S948 = int3(_S946, int(_S947), int(0));
                    unlitColor_0 = float3(_S937, _S943, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S948)).xy), uint(((_S948)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                emissionSample_0.z = (((&kernelContext_0)->emissiveValuesTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S178))).x;
            }
        }
        float4 _S949 = emissionSample_0;
        for(;;)
        {
            float4 _S950 = float4(_S6->compositeControls_0) ;
            if((_S950.x) != 16.0f)
            {
                _S179 = _S949;
                break;
            }
            bool _S951 = (_S950.w) >= 0.5f;
            for(;;)
            {
                if(!_S951)
                {
                    _S179 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S178)));
                    break;
                }
                texture2d<float, access::sample> _S952 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S952).get_width(0)),(*((&atlasHeight_9)) = (_S952).get_height(0));
                int3 _S953 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S953)).xy), uint(((_S953)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S954 = atlasWidth_9;
                    uint _S955 = 2U % atlasWidth_9;
                    int _S956 = int(_S955);
                    uint _S957 = 2U / _S954;
                    int3 _S958 = int3(_S956, int(_S957), int(0));
                    int _S959 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S958)).xy), uint(((_S958)).z))).x * 255.0f)));
                    uint _S960 = atlasWidth_9;
                    uint _S961 = 3U % atlasWidth_9;
                    int _S962 = int(_S961);
                    uint _S963 = 3U / _S960;
                    int3 _S964 = int3(_S962, int(_S963), int(0));
                    int2 minimumTile_9 = int2(_S959, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S964)).xy), uint(((_S964)).z))).x * 255.0f))));
                    uint _S965 = atlasWidth_9;
                    uint _S966 = 4U % atlasWidth_9;
                    int _S967 = int(_S966);
                    uint _S968 = 4U / _S965;
                    int3 _S969 = int3(_S967, int(_S968), int(0));
                    uint _S970 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S969)).xy), uint(((_S969)).z))).x * 255.0f));
                    uint _S971 = atlasWidth_9;
                    uint _S972 = 5U % atlasWidth_9;
                    int _S973 = int(_S972);
                    uint _S974 = 5U / _S971;
                    int3 _S975 = int3(_S973, int(_S974), int(0));
                    uint _S976 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S975)).xy), uint(((_S975)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S970, _S976);
                    int2 tile_18 = int2(floor(_S178)) - minimumTile_9;
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
                        int3 _S977 = int3(int(1), int(0), int(0));
                        _S179 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S977)).xy), uint(((_S977)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S970 + uint(tile_18.x));
                    uint _S978 = atlasWidth_9;
                    uint _S979 = offset_9 % atlasWidth_9;
                    int _S980 = int(_S979);
                    uint _S981 = offset_9 / _S978;
                    int3 _S982 = int3(_S980, int(_S981), int(0));
                    uint _S983 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S982)).xy), uint(((_S982)).z))).x * 255.0f));
                    uint _S984 = atlasWidth_9;
                    uint _S985 = offset_9 + 1U;
                    uint _S986 = _S985 % atlasWidth_9;
                    int _S987 = int(_S986);
                    uint _S988 = _S985 / _S984;
                    int3 _S989 = int3(_S987, int(_S988), int(0));
                    uint slot_18 = _S983 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S989)).xy), uint(((_S989)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S990 = int3(int(1), int(0), int(0));
                        _S179 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S990)).xy), uint(((_S990)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S991 = atlasWidth_9;
                    uint _S992 = 6U % atlasWidth_9;
                    int _S993 = int(_S992);
                    uint _S994 = 6U / _S991;
                    int3 _S995 = int3(_S993, int(_S994), int(0));
                    uint _S996 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S995)).xy), uint(((_S995)).z))).x * 255.0f));
                    uint _S997 = atlasWidth_9;
                    uint _S998 = 7U % atlasWidth_9;
                    int _S999 = int(_S998);
                    uint _S1000 = 7U / _S997;
                    int3 _S1001 = int3(_S999, int(_S1000), int(0));
                    uint columns_9 = _S996 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1001)).xy), uint(((_S1001)).z))).x * 255.0f)) << 8U);
                    uint _S1002 = atlasWidth_9;
                    uint _S1003 = 8U % atlasWidth_9;
                    int _S1004 = int(_S1003);
                    uint _S1005 = 8U / _S1002;
                    int3 _S1006 = int3(_S1004, int(_S1005), int(0));
                    uint _S1007 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1006)).xy), uint(((_S1006)).z))).x * 255.0f));
                    uint _S1008 = atlasWidth_9;
                    uint _S1009 = 9U % atlasWidth_9;
                    int _S1010 = int(_S1009);
                    uint _S1011 = 9U / _S1008;
                    int3 _S1012 = int3(_S1010, int(_S1011), int(0));
                    uint rows_9 = ((_S1007 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1012)).xy), uint(((_S1012)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S970 * _S976 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S1013 = atlasWidth_9 / columns_9;
                    float _S1014 = float(_S1013);
                    uint _S1015 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S1014, float(_S1015));
                    uint _S1016 = slot_19 % columns_9;
                    float _S1017 = float(_S1016);
                    uint _S1018 = slot_19 / columns_9;
                    _S179 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1017, float(_S1018)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(_S178) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S1019 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(_S178)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S1019);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1020 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S179 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1020)).xy), uint(((_S1020)).z)));
                    break;
                }
                uint _S1021 = atlasWidth_9 / uint(_S1019.x);
                float _S1022 = float(_S1021);
                uint _S1023 = (atlasHeight_9 - 1U) / uint(_S1019.y);
                float2 cellSize_19 = float2(_S1022, float(_S1023));
                _S179 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_3 = uint(round(_S950.y));
            if(operation_3 == 1U)
            {
                _S179 = _S949 * _S179;
                break;
            }
            if(operation_3 == 2U)
            {
                _S179 = _S949 + _S179;
                break;
            }
            if(operation_3 == 3U)
            {
                _S179 = _S949 - _S179;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S950.z;
                _S179 = _S949 * float4((1.0f - factor_3))  + _S179 * float4(factor_3) ;
                break;
            }
            _S179 = _S949;
            break;
        }
        emissiveColor_1 = _S179.xyz;
    }
    else
    {
        emissiveColor_1 = emissiveColor_0;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool _S1024 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S1024)
            {
                _S179 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S178)));
                break;
            }
            texture2d<float, access::sample> _S1025 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S1025).get_width(0)),(*((&atlasHeight_10)) = (_S1025).get_height(0));
            int3 _S1026 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1026)).xy), uint(((_S1026)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S1027 = atlasWidth_10;
                uint _S1028 = 2U % atlasWidth_10;
                int _S1029 = int(_S1028);
                uint _S1030 = 2U / _S1027;
                int3 _S1031 = int3(_S1029, int(_S1030), int(0));
                int _S1032 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1031)).xy), uint(((_S1031)).z))).x * 255.0f)));
                uint _S1033 = atlasWidth_10;
                uint _S1034 = 3U % atlasWidth_10;
                int _S1035 = int(_S1034);
                uint _S1036 = 3U / _S1033;
                int3 _S1037 = int3(_S1035, int(_S1036), int(0));
                int2 minimumTile_10 = int2(_S1032, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1037)).xy), uint(((_S1037)).z))).x * 255.0f))));
                uint _S1038 = atlasWidth_10;
                uint _S1039 = 4U % atlasWidth_10;
                int _S1040 = int(_S1039);
                uint _S1041 = 4U / _S1038;
                int3 _S1042 = int3(_S1040, int(_S1041), int(0));
                uint _S1043 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1042)).xy), uint(((_S1042)).z))).x * 255.0f));
                uint _S1044 = atlasWidth_10;
                uint _S1045 = 5U % atlasWidth_10;
                int _S1046 = int(_S1045);
                uint _S1047 = 5U / _S1044;
                int3 _S1048 = int3(_S1046, int(_S1047), int(0));
                uint _S1049 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1048)).xy), uint(((_S1048)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S1043, _S1049);
                int2 tile_20 = int2(floor(_S178)) - minimumTile_10;
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
                    int3 _S1050 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1050)).xy), uint(((_S1050)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1043 + uint(tile_20.x));
                uint _S1051 = atlasWidth_10;
                uint _S1052 = offset_10 % atlasWidth_10;
                int _S1053 = int(_S1052);
                uint _S1054 = offset_10 / _S1051;
                int3 _S1055 = int3(_S1053, int(_S1054), int(0));
                uint _S1056 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1055)).xy), uint(((_S1055)).z))).x * 255.0f));
                uint _S1057 = atlasWidth_10;
                uint _S1058 = offset_10 + 1U;
                uint _S1059 = _S1058 % atlasWidth_10;
                int _S1060 = int(_S1059);
                uint _S1061 = _S1058 / _S1057;
                int3 _S1062 = int3(_S1060, int(_S1061), int(0));
                uint slot_20 = _S1056 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1062)).xy), uint(((_S1062)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1063 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1063)).xy), uint(((_S1063)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1064 = atlasWidth_10;
                uint _S1065 = 6U % atlasWidth_10;
                int _S1066 = int(_S1065);
                uint _S1067 = 6U / _S1064;
                int3 _S1068 = int3(_S1066, int(_S1067), int(0));
                uint _S1069 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1068)).xy), uint(((_S1068)).z))).x * 255.0f));
                uint _S1070 = atlasWidth_10;
                uint _S1071 = 7U % atlasWidth_10;
                int _S1072 = int(_S1071);
                uint _S1073 = 7U / _S1070;
                int3 _S1074 = int3(_S1072, int(_S1073), int(0));
                uint columns_10 = _S1069 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1074)).xy), uint(((_S1074)).z))).x * 255.0f)) << 8U);
                uint _S1075 = atlasWidth_10;
                uint _S1076 = 8U % atlasWidth_10;
                int _S1077 = int(_S1076);
                uint _S1078 = 8U / _S1075;
                int3 _S1079 = int3(_S1077, int(_S1078), int(0));
                uint _S1080 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1079)).xy), uint(((_S1079)).z))).x * 255.0f));
                uint _S1081 = atlasWidth_10;
                uint _S1082 = 9U % atlasWidth_10;
                int _S1083 = int(_S1082);
                uint _S1084 = 9U / _S1081;
                int3 _S1085 = int3(_S1083, int(_S1084), int(0));
                uint rows_10 = ((_S1080 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1085)).xy), uint(((_S1085)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S1043 * _S1049 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1086 = atlasWidth_10 / columns_10;
                float _S1087 = float(_S1086);
                uint _S1088 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1087, float(_S1088));
                uint _S1089 = slot_21 % columns_10;
                float _S1090 = float(_S1089);
                uint _S1091 = slot_21 / columns_10;
                _S179 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S1090, float(_S1091)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(_S178) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1092 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(_S178)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1092);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1093 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S179 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1093)).xy), uint(((_S1093)).z)));
                break;
            }
            uint _S1094 = atlasWidth_10 / uint(_S1092.x);
            float _S1095 = float(_S1094);
            uint _S1096 = (atlasHeight_10 - 1U) / uint(_S1092.y);
            float2 cellSize_21 = float2(_S1095, float(_S1096));
            _S179 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1097 = float4(_S6->compositeControls_0) ;
            if((_S1097.x) != 64.0f)
            {
                break;
            }
            bool _S1098 = (_S1097.w) >= 0.5f;
            for(;;)
            {
                if(!_S1098)
                {
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S178)));
                    break;
                }
                texture2d<float, access::sample> _S1099 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1099).get_width(0)),(*((&atlasHeight_11)) = (_S1099).get_height(0));
                int3 _S1100 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1100)).xy), uint(((_S1100)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1101 = atlasWidth_11;
                    uint _S1102 = 2U % atlasWidth_11;
                    int _S1103 = int(_S1102);
                    uint _S1104 = 2U / _S1101;
                    int3 _S1105 = int3(_S1103, int(_S1104), int(0));
                    int _S1106 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1105)).xy), uint(((_S1105)).z))).x * 255.0f)));
                    uint _S1107 = atlasWidth_11;
                    uint _S1108 = 3U % atlasWidth_11;
                    int _S1109 = int(_S1108);
                    uint _S1110 = 3U / _S1107;
                    int3 _S1111 = int3(_S1109, int(_S1110), int(0));
                    int2 minimumTile_11 = int2(_S1106, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1111)).xy), uint(((_S1111)).z))).x * 255.0f))));
                    uint _S1112 = atlasWidth_11;
                    uint _S1113 = 4U % atlasWidth_11;
                    int _S1114 = int(_S1113);
                    uint _S1115 = 4U / _S1112;
                    int3 _S1116 = int3(_S1114, int(_S1115), int(0));
                    uint _S1117 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1116)).xy), uint(((_S1116)).z))).x * 255.0f));
                    uint _S1118 = atlasWidth_11;
                    uint _S1119 = 5U % atlasWidth_11;
                    int _S1120 = int(_S1119);
                    uint _S1121 = 5U / _S1118;
                    int3 _S1122 = int3(_S1120, int(_S1121), int(0));
                    uint _S1123 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1122)).xy), uint(((_S1122)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1117, _S1123);
                    int2 tile_22 = int2(floor(_S178)) - minimumTile_11;
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
                        int3 _S1124 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1124)).xy), uint(((_S1124)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1117 + uint(tile_22.x));
                    uint _S1125 = atlasWidth_11;
                    uint _S1126 = offset_11 % atlasWidth_11;
                    int _S1127 = int(_S1126);
                    uint _S1128 = offset_11 / _S1125;
                    int3 _S1129 = int3(_S1127, int(_S1128), int(0));
                    uint _S1130 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1129)).xy), uint(((_S1129)).z))).x * 255.0f));
                    uint _S1131 = atlasWidth_11;
                    uint _S1132 = offset_11 + 1U;
                    uint _S1133 = _S1132 % atlasWidth_11;
                    int _S1134 = int(_S1133);
                    uint _S1135 = _S1132 / _S1131;
                    int3 _S1136 = int3(_S1134, int(_S1135), int(0));
                    uint slot_22 = _S1130 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1136)).xy), uint(((_S1136)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1137 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1137)).xy), uint(((_S1137)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1138 = atlasWidth_11;
                    uint _S1139 = 6U % atlasWidth_11;
                    int _S1140 = int(_S1139);
                    uint _S1141 = 6U / _S1138;
                    int3 _S1142 = int3(_S1140, int(_S1141), int(0));
                    uint _S1143 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1142)).xy), uint(((_S1142)).z))).x * 255.0f));
                    uint _S1144 = atlasWidth_11;
                    uint _S1145 = 7U % atlasWidth_11;
                    int _S1146 = int(_S1145);
                    uint _S1147 = 7U / _S1144;
                    int3 _S1148 = int3(_S1146, int(_S1147), int(0));
                    uint columns_11 = _S1143 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1148)).xy), uint(((_S1148)).z))).x * 255.0f)) << 8U);
                    uint _S1149 = atlasWidth_11;
                    uint _S1150 = 8U % atlasWidth_11;
                    int _S1151 = int(_S1150);
                    uint _S1152 = 8U / _S1149;
                    int3 _S1153 = int3(_S1151, int(_S1152), int(0));
                    uint _S1154 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1153)).xy), uint(((_S1153)).z))).x * 255.0f));
                    uint _S1155 = atlasWidth_11;
                    uint _S1156 = 9U % atlasWidth_11;
                    int _S1157 = int(_S1156);
                    uint _S1158 = 9U / _S1155;
                    int3 _S1159 = int3(_S1157, int(_S1158), int(0));
                    uint rows_11 = ((_S1154 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1159)).xy), uint(((_S1159)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1117 * _S1123 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1160 = atlasWidth_11 / columns_11;
                    float _S1161 = float(_S1160);
                    uint _S1162 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1161, float(_S1162));
                    uint _S1163 = slot_23 % columns_11;
                    float _S1164 = float(_S1163);
                    uint _S1165 = slot_23 / columns_11;
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1164, float(_S1165)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(_S178) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1166 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(_S178)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1166);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1167 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1167)).xy), uint(((_S1167)).z)));
                    break;
                }
                uint _S1168 = atlasWidth_11 / uint(_S1166.x);
                float _S1169 = float(_S1168);
                uint _S1170 = (atlasHeight_11 - 1U) / uint(_S1166.y);
                float2 cellSize_23 = float2(_S1169, float(_S1170));
                _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_4 = uint(round(_S1097.y));
            if(operation_4 == 1U)
            {
                _S179 = _S179 * _S558;
                break;
            }
            if(operation_4 == 2U)
            {
                _S179 = _S179 + _S558;
                break;
            }
            if(operation_4 == 3U)
            {
                _S179 = _S179 - _S558;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S1097.z;
                _S179 = _S179 * float4((1.0f - factor_4))  + _S558 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S179.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        bool _S1171 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S1171)
            {
                _S179 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S178)));
                break;
            }
            texture2d<float, access::sample> _S1172 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1172).get_width(0)),(*((&atlasHeight_12)) = (_S1172).get_height(0));
            int3 _S1173 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1173)).xy), uint(((_S1173)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1174 = atlasWidth_12;
                uint _S1175 = 2U % atlasWidth_12;
                int _S1176 = int(_S1175);
                uint _S1177 = 2U / _S1174;
                int3 _S1178 = int3(_S1176, int(_S1177), int(0));
                int _S1179 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1178)).xy), uint(((_S1178)).z))).x * 255.0f)));
                uint _S1180 = atlasWidth_12;
                uint _S1181 = 3U % atlasWidth_12;
                int _S1182 = int(_S1181);
                uint _S1183 = 3U / _S1180;
                int3 _S1184 = int3(_S1182, int(_S1183), int(0));
                int2 minimumTile_12 = int2(_S1179, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1184)).xy), uint(((_S1184)).z))).x * 255.0f))));
                uint _S1185 = atlasWidth_12;
                uint _S1186 = 4U % atlasWidth_12;
                int _S1187 = int(_S1186);
                uint _S1188 = 4U / _S1185;
                int3 _S1189 = int3(_S1187, int(_S1188), int(0));
                uint _S1190 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1189)).xy), uint(((_S1189)).z))).x * 255.0f));
                uint _S1191 = atlasWidth_12;
                uint _S1192 = 5U % atlasWidth_12;
                int _S1193 = int(_S1192);
                uint _S1194 = 5U / _S1191;
                int3 _S1195 = int3(_S1193, int(_S1194), int(0));
                uint _S1196 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1195)).xy), uint(((_S1195)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1190, _S1196);
                int2 tile_24 = int2(floor(_S178)) - minimumTile_12;
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
                    int3 _S1197 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1197)).xy), uint(((_S1197)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1190 + uint(tile_24.x));
                uint _S1198 = atlasWidth_12;
                uint _S1199 = offset_12 % atlasWidth_12;
                int _S1200 = int(_S1199);
                uint _S1201 = offset_12 / _S1198;
                int3 _S1202 = int3(_S1200, int(_S1201), int(0));
                uint _S1203 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1202)).xy), uint(((_S1202)).z))).x * 255.0f));
                uint _S1204 = atlasWidth_12;
                uint _S1205 = offset_12 + 1U;
                uint _S1206 = _S1205 % atlasWidth_12;
                int _S1207 = int(_S1206);
                uint _S1208 = _S1205 / _S1204;
                int3 _S1209 = int3(_S1207, int(_S1208), int(0));
                uint slot_24 = _S1203 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1209)).xy), uint(((_S1209)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1210 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1210)).xy), uint(((_S1210)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1211 = atlasWidth_12;
                uint _S1212 = 6U % atlasWidth_12;
                int _S1213 = int(_S1212);
                uint _S1214 = 6U / _S1211;
                int3 _S1215 = int3(_S1213, int(_S1214), int(0));
                uint _S1216 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1215)).xy), uint(((_S1215)).z))).x * 255.0f));
                uint _S1217 = atlasWidth_12;
                uint _S1218 = 7U % atlasWidth_12;
                int _S1219 = int(_S1218);
                uint _S1220 = 7U / _S1217;
                int3 _S1221 = int3(_S1219, int(_S1220), int(0));
                uint columns_12 = _S1216 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1221)).xy), uint(((_S1221)).z))).x * 255.0f)) << 8U);
                uint _S1222 = atlasWidth_12;
                uint _S1223 = 8U % atlasWidth_12;
                int _S1224 = int(_S1223);
                uint _S1225 = 8U / _S1222;
                int3 _S1226 = int3(_S1224, int(_S1225), int(0));
                uint _S1227 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1226)).xy), uint(((_S1226)).z))).x * 255.0f));
                uint _S1228 = atlasWidth_12;
                uint _S1229 = 9U % atlasWidth_12;
                int _S1230 = int(_S1229);
                uint _S1231 = 9U / _S1228;
                int3 _S1232 = int3(_S1230, int(_S1231), int(0));
                uint rows_12 = ((_S1227 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1232)).xy), uint(((_S1232)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1190 * _S1196 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1233 = atlasWidth_12 / columns_12;
                float _S1234 = float(_S1233);
                uint _S1235 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1234, float(_S1235));
                uint _S1236 = slot_25 % columns_12;
                float _S1237 = float(_S1236);
                uint _S1238 = slot_25 / columns_12;
                _S179 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1237, float(_S1238)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(_S178) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1239 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(_S178)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1239);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1240 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S179 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1240)).xy), uint(((_S1240)).z)));
                break;
            }
            uint _S1241 = atlasWidth_12 / uint(_S1239.x);
            float _S1242 = float(_S1241);
            uint _S1243 = (atlasHeight_12 - 1U) / uint(_S1239.y);
            float2 cellSize_25 = float2(_S1242, float(_S1243));
            _S179 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1244 = float4(_S6->compositeControls_0) ;
            if((_S1244.x) != 128.0f)
            {
                break;
            }
            bool _S1245 = (_S1244.w) >= 0.5f;
            for(;;)
            {
                if(!_S1245)
                {
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S178)));
                    break;
                }
                texture2d<float, access::sample> _S1246 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1246).get_width(0)),(*((&atlasHeight_13)) = (_S1246).get_height(0));
                int3 _S1247 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1247)).xy), uint(((_S1247)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1248 = atlasWidth_13;
                    uint _S1249 = 2U % atlasWidth_13;
                    int _S1250 = int(_S1249);
                    uint _S1251 = 2U / _S1248;
                    int3 _S1252 = int3(_S1250, int(_S1251), int(0));
                    int _S1253 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1252)).xy), uint(((_S1252)).z))).x * 255.0f)));
                    uint _S1254 = atlasWidth_13;
                    uint _S1255 = 3U % atlasWidth_13;
                    int _S1256 = int(_S1255);
                    uint _S1257 = 3U / _S1254;
                    int3 _S1258 = int3(_S1256, int(_S1257), int(0));
                    int2 minimumTile_13 = int2(_S1253, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1258)).xy), uint(((_S1258)).z))).x * 255.0f))));
                    uint _S1259 = atlasWidth_13;
                    uint _S1260 = 4U % atlasWidth_13;
                    int _S1261 = int(_S1260);
                    uint _S1262 = 4U / _S1259;
                    int3 _S1263 = int3(_S1261, int(_S1262), int(0));
                    uint _S1264 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1263)).xy), uint(((_S1263)).z))).x * 255.0f));
                    uint _S1265 = atlasWidth_13;
                    uint _S1266 = 5U % atlasWidth_13;
                    int _S1267 = int(_S1266);
                    uint _S1268 = 5U / _S1265;
                    int3 _S1269 = int3(_S1267, int(_S1268), int(0));
                    uint _S1270 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1269)).xy), uint(((_S1269)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1264, _S1270);
                    int2 tile_26 = int2(floor(_S178)) - minimumTile_13;
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
                        int3 _S1271 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1271)).xy), uint(((_S1271)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1264 + uint(tile_26.x));
                    uint _S1272 = atlasWidth_13;
                    uint _S1273 = offset_13 % atlasWidth_13;
                    int _S1274 = int(_S1273);
                    uint _S1275 = offset_13 / _S1272;
                    int3 _S1276 = int3(_S1274, int(_S1275), int(0));
                    uint _S1277 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1276)).xy), uint(((_S1276)).z))).x * 255.0f));
                    uint _S1278 = atlasWidth_13;
                    uint _S1279 = offset_13 + 1U;
                    uint _S1280 = _S1279 % atlasWidth_13;
                    int _S1281 = int(_S1280);
                    uint _S1282 = _S1279 / _S1278;
                    int3 _S1283 = int3(_S1281, int(_S1282), int(0));
                    uint slot_26 = _S1277 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1283)).xy), uint(((_S1283)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1284 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1284)).xy), uint(((_S1284)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1285 = atlasWidth_13;
                    uint _S1286 = 6U % atlasWidth_13;
                    int _S1287 = int(_S1286);
                    uint _S1288 = 6U / _S1285;
                    int3 _S1289 = int3(_S1287, int(_S1288), int(0));
                    uint _S1290 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1289)).xy), uint(((_S1289)).z))).x * 255.0f));
                    uint _S1291 = atlasWidth_13;
                    uint _S1292 = 7U % atlasWidth_13;
                    int _S1293 = int(_S1292);
                    uint _S1294 = 7U / _S1291;
                    int3 _S1295 = int3(_S1293, int(_S1294), int(0));
                    uint columns_13 = _S1290 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1295)).xy), uint(((_S1295)).z))).x * 255.0f)) << 8U);
                    uint _S1296 = atlasWidth_13;
                    uint _S1297 = 8U % atlasWidth_13;
                    int _S1298 = int(_S1297);
                    uint _S1299 = 8U / _S1296;
                    int3 _S1300 = int3(_S1298, int(_S1299), int(0));
                    uint _S1301 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1300)).xy), uint(((_S1300)).z))).x * 255.0f));
                    uint _S1302 = atlasWidth_13;
                    uint _S1303 = 9U % atlasWidth_13;
                    int _S1304 = int(_S1303);
                    uint _S1305 = 9U / _S1302;
                    int3 _S1306 = int3(_S1304, int(_S1305), int(0));
                    uint rows_13 = ((_S1301 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1306)).xy), uint(((_S1306)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1264 * _S1270 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1307 = atlasWidth_13 / columns_13;
                    float _S1308 = float(_S1307);
                    uint _S1309 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1308, float(_S1309));
                    uint _S1310 = slot_27 % columns_13;
                    float _S1311 = float(_S1310);
                    uint _S1312 = slot_27 / columns_13;
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1311, float(_S1312)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(_S178) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1313 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(_S178)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1313);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1314 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1314)).xy), uint(((_S1314)).z)));
                    break;
                }
                uint _S1315 = atlasWidth_13 / uint(_S1313.x);
                float _S1316 = float(_S1315);
                uint _S1317 = (atlasHeight_13 - 1U) / uint(_S1313.y);
                float2 cellSize_27 = float2(_S1316, float(_S1317));
                _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_5 = uint(round(_S1244.y));
            if(operation_5 == 1U)
            {
                _S179 = _S179 * _S558;
                break;
            }
            if(operation_5 == 2U)
            {
                _S179 = _S179 + _S558;
                break;
            }
            if(operation_5 == 3U)
            {
                _S179 = _S179 - _S558;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1244.z;
                _S179 = _S179 * float4((1.0f - factor_5))  + _S558 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S179.x);
    }
    else
    {
        occlusion_0 = _S161;
    }
    float3 specularColor_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool _S1318 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S1318)
            {
                _S179 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S178)));
                break;
            }
            texture2d<float, access::sample> _S1319 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1319).get_width(0)),(*((&atlasHeight_14)) = (_S1319).get_height(0));
            int3 _S1320 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1320)).xy), uint(((_S1320)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1321 = atlasWidth_14;
                uint _S1322 = 2U % atlasWidth_14;
                int _S1323 = int(_S1322);
                uint _S1324 = 2U / _S1321;
                int3 _S1325 = int3(_S1323, int(_S1324), int(0));
                int _S1326 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1325)).xy), uint(((_S1325)).z))).x * 255.0f)));
                uint _S1327 = atlasWidth_14;
                uint _S1328 = 3U % atlasWidth_14;
                int _S1329 = int(_S1328);
                uint _S1330 = 3U / _S1327;
                int3 _S1331 = int3(_S1329, int(_S1330), int(0));
                int2 minimumTile_14 = int2(_S1326, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1331)).xy), uint(((_S1331)).z))).x * 255.0f))));
                uint _S1332 = atlasWidth_14;
                uint _S1333 = 4U % atlasWidth_14;
                int _S1334 = int(_S1333);
                uint _S1335 = 4U / _S1332;
                int3 _S1336 = int3(_S1334, int(_S1335), int(0));
                uint _S1337 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1336)).xy), uint(((_S1336)).z))).x * 255.0f));
                uint _S1338 = atlasWidth_14;
                uint _S1339 = 5U % atlasWidth_14;
                int _S1340 = int(_S1339);
                uint _S1341 = 5U / _S1338;
                int3 _S1342 = int3(_S1340, int(_S1341), int(0));
                uint _S1343 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1342)).xy), uint(((_S1342)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1337, _S1343);
                int2 tile_28 = int2(floor(_S178)) - minimumTile_14;
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
                    int3 _S1344 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1344)).xy), uint(((_S1344)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1337 + uint(tile_28.x));
                uint _S1345 = atlasWidth_14;
                uint _S1346 = offset_14 % atlasWidth_14;
                int _S1347 = int(_S1346);
                uint _S1348 = offset_14 / _S1345;
                int3 _S1349 = int3(_S1347, int(_S1348), int(0));
                uint _S1350 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1349)).xy), uint(((_S1349)).z))).x * 255.0f));
                uint _S1351 = atlasWidth_14;
                uint _S1352 = offset_14 + 1U;
                uint _S1353 = _S1352 % atlasWidth_14;
                int _S1354 = int(_S1353);
                uint _S1355 = _S1352 / _S1351;
                int3 _S1356 = int3(_S1354, int(_S1355), int(0));
                uint slot_28 = _S1350 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1356)).xy), uint(((_S1356)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1357 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1357)).xy), uint(((_S1357)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1358 = atlasWidth_14;
                uint _S1359 = 6U % atlasWidth_14;
                int _S1360 = int(_S1359);
                uint _S1361 = 6U / _S1358;
                int3 _S1362 = int3(_S1360, int(_S1361), int(0));
                uint _S1363 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1362)).xy), uint(((_S1362)).z))).x * 255.0f));
                uint _S1364 = atlasWidth_14;
                uint _S1365 = 7U % atlasWidth_14;
                int _S1366 = int(_S1365);
                uint _S1367 = 7U / _S1364;
                int3 _S1368 = int3(_S1366, int(_S1367), int(0));
                uint columns_14 = _S1363 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1368)).xy), uint(((_S1368)).z))).x * 255.0f)) << 8U);
                uint _S1369 = atlasWidth_14;
                uint _S1370 = 8U % atlasWidth_14;
                int _S1371 = int(_S1370);
                uint _S1372 = 8U / _S1369;
                int3 _S1373 = int3(_S1371, int(_S1372), int(0));
                uint _S1374 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1373)).xy), uint(((_S1373)).z))).x * 255.0f));
                uint _S1375 = atlasWidth_14;
                uint _S1376 = 9U % atlasWidth_14;
                int _S1377 = int(_S1376);
                uint _S1378 = 9U / _S1375;
                int3 _S1379 = int3(_S1377, int(_S1378), int(0));
                uint rows_14 = ((_S1374 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1379)).xy), uint(((_S1379)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1337 * _S1343 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1380 = atlasWidth_14 / columns_14;
                float _S1381 = float(_S1380);
                uint _S1382 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1381, float(_S1382));
                uint _S1383 = slot_29 % columns_14;
                float _S1384 = float(_S1383);
                uint _S1385 = slot_29 / columns_14;
                _S179 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1384, float(_S1385)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(_S178) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1386 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(_S178)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1386);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1387 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S179 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1387)).xy), uint(((_S1387)).z)));
                break;
            }
            uint _S1388 = atlasWidth_14 / uint(_S1386.x);
            float _S1389 = float(_S1388);
            uint _S1390 = (atlasHeight_14 - 1U) / uint(_S1386.y);
            float2 cellSize_29 = float2(_S1389, float(_S1390));
            _S179 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1391 = float4(_S6->compositeControls_0) ;
            if((_S1391.x) != 256.0f)
            {
                break;
            }
            bool _S1392 = (_S1391.w) >= 0.5f;
            for(;;)
            {
                if(!_S1392)
                {
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S178)));
                    break;
                }
                texture2d<float, access::sample> _S1393 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1393).get_width(0)),(*((&atlasHeight_15)) = (_S1393).get_height(0));
                int3 _S1394 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1394)).xy), uint(((_S1394)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1395 = atlasWidth_15;
                    uint _S1396 = 2U % atlasWidth_15;
                    int _S1397 = int(_S1396);
                    uint _S1398 = 2U / _S1395;
                    int3 _S1399 = int3(_S1397, int(_S1398), int(0));
                    int _S1400 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1399)).xy), uint(((_S1399)).z))).x * 255.0f)));
                    uint _S1401 = atlasWidth_15;
                    uint _S1402 = 3U % atlasWidth_15;
                    int _S1403 = int(_S1402);
                    uint _S1404 = 3U / _S1401;
                    int3 _S1405 = int3(_S1403, int(_S1404), int(0));
                    int2 minimumTile_15 = int2(_S1400, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1405)).xy), uint(((_S1405)).z))).x * 255.0f))));
                    uint _S1406 = atlasWidth_15;
                    uint _S1407 = 4U % atlasWidth_15;
                    int _S1408 = int(_S1407);
                    uint _S1409 = 4U / _S1406;
                    int3 _S1410 = int3(_S1408, int(_S1409), int(0));
                    uint _S1411 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1410)).xy), uint(((_S1410)).z))).x * 255.0f));
                    uint _S1412 = atlasWidth_15;
                    uint _S1413 = 5U % atlasWidth_15;
                    int _S1414 = int(_S1413);
                    uint _S1415 = 5U / _S1412;
                    int3 _S1416 = int3(_S1414, int(_S1415), int(0));
                    uint _S1417 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1416)).xy), uint(((_S1416)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1411, _S1417);
                    int2 tile_30 = int2(floor(_S178)) - minimumTile_15;
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
                        int3 _S1418 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1418)).xy), uint(((_S1418)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1411 + uint(tile_30.x));
                    uint _S1419 = atlasWidth_15;
                    uint _S1420 = offset_15 % atlasWidth_15;
                    int _S1421 = int(_S1420);
                    uint _S1422 = offset_15 / _S1419;
                    int3 _S1423 = int3(_S1421, int(_S1422), int(0));
                    uint _S1424 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1423)).xy), uint(((_S1423)).z))).x * 255.0f));
                    uint _S1425 = atlasWidth_15;
                    uint _S1426 = offset_15 + 1U;
                    uint _S1427 = _S1426 % atlasWidth_15;
                    int _S1428 = int(_S1427);
                    uint _S1429 = _S1426 / _S1425;
                    int3 _S1430 = int3(_S1428, int(_S1429), int(0));
                    uint slot_30 = _S1424 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1430)).xy), uint(((_S1430)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1431 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1431)).xy), uint(((_S1431)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1432 = atlasWidth_15;
                    uint _S1433 = 6U % atlasWidth_15;
                    int _S1434 = int(_S1433);
                    uint _S1435 = 6U / _S1432;
                    int3 _S1436 = int3(_S1434, int(_S1435), int(0));
                    uint _S1437 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1436)).xy), uint(((_S1436)).z))).x * 255.0f));
                    uint _S1438 = atlasWidth_15;
                    uint _S1439 = 7U % atlasWidth_15;
                    int _S1440 = int(_S1439);
                    uint _S1441 = 7U / _S1438;
                    int3 _S1442 = int3(_S1440, int(_S1441), int(0));
                    uint columns_15 = _S1437 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1442)).xy), uint(((_S1442)).z))).x * 255.0f)) << 8U);
                    uint _S1443 = atlasWidth_15;
                    uint _S1444 = 8U % atlasWidth_15;
                    int _S1445 = int(_S1444);
                    uint _S1446 = 8U / _S1443;
                    int3 _S1447 = int3(_S1445, int(_S1446), int(0));
                    uint _S1448 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1447)).xy), uint(((_S1447)).z))).x * 255.0f));
                    uint _S1449 = atlasWidth_15;
                    uint _S1450 = 9U % atlasWidth_15;
                    int _S1451 = int(_S1450);
                    uint _S1452 = 9U / _S1449;
                    int3 _S1453 = int3(_S1451, int(_S1452), int(0));
                    uint rows_15 = ((_S1448 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1453)).xy), uint(((_S1453)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1411 * _S1417 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1454 = atlasWidth_15 / columns_15;
                    float _S1455 = float(_S1454);
                    uint _S1456 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1455, float(_S1456));
                    uint _S1457 = slot_31 % columns_15;
                    float _S1458 = float(_S1457);
                    uint _S1459 = slot_31 / columns_15;
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1458, float(_S1459)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(_S178) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1460 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(_S178)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1460);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1461 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1461)).xy), uint(((_S1461)).z)));
                    break;
                }
                uint _S1462 = atlasWidth_15 / uint(_S1460.x);
                float _S1463 = float(_S1462);
                uint _S1464 = (atlasHeight_15 - 1U) / uint(_S1460.y);
                float2 cellSize_31 = float2(_S1463, float(_S1464));
                _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_6 = uint(round(_S1391.y));
            if(operation_6 == 1U)
            {
                _S179 = _S179 * _S558;
                break;
            }
            if(operation_6 == 2U)
            {
                _S179 = _S179 + _S558;
                break;
            }
            if(operation_6 == 3U)
            {
                _S179 = _S179 - _S558;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1391.z;
                _S179 = _S179 * float4((1.0f - factor_6))  + _S558 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S179.xyz);
    }
    else
    {
        specularColor_0 = _S169;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 512U) != 0U)
    {
        bool _S1465 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S1465)
            {
                _S179 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S178)));
                break;
            }
            texture2d<float, access::sample> _S1466 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1466).get_width(0)),(*((&atlasHeight_16)) = (_S1466).get_height(0));
            int3 _S1467 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1467)).xy), uint(((_S1467)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1468 = atlasWidth_16;
                uint _S1469 = 2U % atlasWidth_16;
                int _S1470 = int(_S1469);
                uint _S1471 = 2U / _S1468;
                int3 _S1472 = int3(_S1470, int(_S1471), int(0));
                int _S1473 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1472)).xy), uint(((_S1472)).z))).x * 255.0f)));
                uint _S1474 = atlasWidth_16;
                uint _S1475 = 3U % atlasWidth_16;
                int _S1476 = int(_S1475);
                uint _S1477 = 3U / _S1474;
                int3 _S1478 = int3(_S1476, int(_S1477), int(0));
                int2 minimumTile_16 = int2(_S1473, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1478)).xy), uint(((_S1478)).z))).x * 255.0f))));
                uint _S1479 = atlasWidth_16;
                uint _S1480 = 4U % atlasWidth_16;
                int _S1481 = int(_S1480);
                uint _S1482 = 4U / _S1479;
                int3 _S1483 = int3(_S1481, int(_S1482), int(0));
                uint _S1484 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1483)).xy), uint(((_S1483)).z))).x * 255.0f));
                uint _S1485 = atlasWidth_16;
                uint _S1486 = 5U % atlasWidth_16;
                int _S1487 = int(_S1486);
                uint _S1488 = 5U / _S1485;
                int3 _S1489 = int3(_S1487, int(_S1488), int(0));
                uint _S1490 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1489)).xy), uint(((_S1489)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1484, _S1490);
                int2 tile_32 = int2(floor(_S178)) - minimumTile_16;
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
                    int3 _S1491 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1491)).xy), uint(((_S1491)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1484 + uint(tile_32.x));
                uint _S1492 = atlasWidth_16;
                uint _S1493 = offset_16 % atlasWidth_16;
                int _S1494 = int(_S1493);
                uint _S1495 = offset_16 / _S1492;
                int3 _S1496 = int3(_S1494, int(_S1495), int(0));
                uint _S1497 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1496)).xy), uint(((_S1496)).z))).x * 255.0f));
                uint _S1498 = atlasWidth_16;
                uint _S1499 = offset_16 + 1U;
                uint _S1500 = _S1499 % atlasWidth_16;
                int _S1501 = int(_S1500);
                uint _S1502 = _S1499 / _S1498;
                int3 _S1503 = int3(_S1501, int(_S1502), int(0));
                uint slot_32 = _S1497 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1503)).xy), uint(((_S1503)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1504 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1504)).xy), uint(((_S1504)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1505 = atlasWidth_16;
                uint _S1506 = 6U % atlasWidth_16;
                int _S1507 = int(_S1506);
                uint _S1508 = 6U / _S1505;
                int3 _S1509 = int3(_S1507, int(_S1508), int(0));
                uint _S1510 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1509)).xy), uint(((_S1509)).z))).x * 255.0f));
                uint _S1511 = atlasWidth_16;
                uint _S1512 = 7U % atlasWidth_16;
                int _S1513 = int(_S1512);
                uint _S1514 = 7U / _S1511;
                int3 _S1515 = int3(_S1513, int(_S1514), int(0));
                uint columns_16 = _S1510 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1515)).xy), uint(((_S1515)).z))).x * 255.0f)) << 8U);
                uint _S1516 = atlasWidth_16;
                uint _S1517 = 8U % atlasWidth_16;
                int _S1518 = int(_S1517);
                uint _S1519 = 8U / _S1516;
                int3 _S1520 = int3(_S1518, int(_S1519), int(0));
                uint _S1521 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1520)).xy), uint(((_S1520)).z))).x * 255.0f));
                uint _S1522 = atlasWidth_16;
                uint _S1523 = 9U % atlasWidth_16;
                int _S1524 = int(_S1523);
                uint _S1525 = 9U / _S1522;
                int3 _S1526 = int3(_S1524, int(_S1525), int(0));
                uint rows_16 = ((_S1521 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1526)).xy), uint(((_S1526)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1484 * _S1490 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1527 = atlasWidth_16 / columns_16;
                float _S1528 = float(_S1527);
                uint _S1529 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1528, float(_S1529));
                uint _S1530 = slot_33 % columns_16;
                float _S1531 = float(_S1530);
                uint _S1532 = slot_33 / columns_16;
                _S179 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1531, float(_S1532)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(_S178) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1533 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(_S178)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1533);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1534 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S179 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1534)).xy), uint(((_S1534)).z)));
                break;
            }
            uint _S1535 = atlasWidth_16 / uint(_S1533.x);
            float _S1536 = float(_S1535);
            uint _S1537 = (atlasHeight_16 - 1U) / uint(_S1533.y);
            float2 cellSize_33 = float2(_S1536, float(_S1537));
            _S179 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1538 = float4(_S6->compositeControls_0) ;
            if((_S1538.x) != 512.0f)
            {
                break;
            }
            bool _S1539 = (_S1538.w) >= 0.5f;
            for(;;)
            {
                if(!_S1539)
                {
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S178)));
                    break;
                }
                texture2d<float, access::sample> _S1540 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1540).get_width(0)),(*((&atlasHeight_17)) = (_S1540).get_height(0));
                int3 _S1541 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1541)).xy), uint(((_S1541)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1542 = atlasWidth_17;
                    uint _S1543 = 2U % atlasWidth_17;
                    int _S1544 = int(_S1543);
                    uint _S1545 = 2U / _S1542;
                    int3 _S1546 = int3(_S1544, int(_S1545), int(0));
                    int _S1547 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1546)).xy), uint(((_S1546)).z))).x * 255.0f)));
                    uint _S1548 = atlasWidth_17;
                    uint _S1549 = 3U % atlasWidth_17;
                    int _S1550 = int(_S1549);
                    uint _S1551 = 3U / _S1548;
                    int3 _S1552 = int3(_S1550, int(_S1551), int(0));
                    int2 minimumTile_17 = int2(_S1547, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1552)).xy), uint(((_S1552)).z))).x * 255.0f))));
                    uint _S1553 = atlasWidth_17;
                    uint _S1554 = 4U % atlasWidth_17;
                    int _S1555 = int(_S1554);
                    uint _S1556 = 4U / _S1553;
                    int3 _S1557 = int3(_S1555, int(_S1556), int(0));
                    uint _S1558 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1557)).xy), uint(((_S1557)).z))).x * 255.0f));
                    uint _S1559 = atlasWidth_17;
                    uint _S1560 = 5U % atlasWidth_17;
                    int _S1561 = int(_S1560);
                    uint _S1562 = 5U / _S1559;
                    int3 _S1563 = int3(_S1561, int(_S1562), int(0));
                    uint _S1564 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1563)).xy), uint(((_S1563)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1558, _S1564);
                    int2 tile_34 = int2(floor(_S178)) - minimumTile_17;
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
                        int3 _S1565 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1565)).xy), uint(((_S1565)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1558 + uint(tile_34.x));
                    uint _S1566 = atlasWidth_17;
                    uint _S1567 = offset_17 % atlasWidth_17;
                    int _S1568 = int(_S1567);
                    uint _S1569 = offset_17 / _S1566;
                    int3 _S1570 = int3(_S1568, int(_S1569), int(0));
                    uint _S1571 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1570)).xy), uint(((_S1570)).z))).x * 255.0f));
                    uint _S1572 = atlasWidth_17;
                    uint _S1573 = offset_17 + 1U;
                    uint _S1574 = _S1573 % atlasWidth_17;
                    int _S1575 = int(_S1574);
                    uint _S1576 = _S1573 / _S1572;
                    int3 _S1577 = int3(_S1575, int(_S1576), int(0));
                    uint slot_34 = _S1571 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1577)).xy), uint(((_S1577)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1578 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1578)).xy), uint(((_S1578)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1579 = atlasWidth_17;
                    uint _S1580 = 6U % atlasWidth_17;
                    int _S1581 = int(_S1580);
                    uint _S1582 = 6U / _S1579;
                    int3 _S1583 = int3(_S1581, int(_S1582), int(0));
                    uint _S1584 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1583)).xy), uint(((_S1583)).z))).x * 255.0f));
                    uint _S1585 = atlasWidth_17;
                    uint _S1586 = 7U % atlasWidth_17;
                    int _S1587 = int(_S1586);
                    uint _S1588 = 7U / _S1585;
                    int3 _S1589 = int3(_S1587, int(_S1588), int(0));
                    uint columns_17 = _S1584 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1589)).xy), uint(((_S1589)).z))).x * 255.0f)) << 8U);
                    uint _S1590 = atlasWidth_17;
                    uint _S1591 = 8U % atlasWidth_17;
                    int _S1592 = int(_S1591);
                    uint _S1593 = 8U / _S1590;
                    int3 _S1594 = int3(_S1592, int(_S1593), int(0));
                    uint _S1595 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1594)).xy), uint(((_S1594)).z))).x * 255.0f));
                    uint _S1596 = atlasWidth_17;
                    uint _S1597 = 9U % atlasWidth_17;
                    int _S1598 = int(_S1597);
                    uint _S1599 = 9U / _S1596;
                    int3 _S1600 = int3(_S1598, int(_S1599), int(0));
                    uint rows_17 = ((_S1595 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1600)).xy), uint(((_S1600)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1558 * _S1564 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1601 = atlasWidth_17 / columns_17;
                    float _S1602 = float(_S1601);
                    uint _S1603 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1602, float(_S1603));
                    uint _S1604 = slot_35 % columns_17;
                    float _S1605 = float(_S1604);
                    uint _S1606 = slot_35 / columns_17;
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1605, float(_S1606)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(_S178) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1607 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(_S178)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1607);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1608 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1608)).xy), uint(((_S1608)).z)));
                    break;
                }
                uint _S1609 = atlasWidth_17 / uint(_S1607.x);
                float _S1610 = float(_S1609);
                uint _S1611 = (atlasHeight_17 - 1U) / uint(_S1607.y);
                float2 cellSize_35 = float2(_S1610, float(_S1611));
                _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_7 = uint(round(_S1538.y));
            if(operation_7 == 1U)
            {
                _S179 = _S179 * _S558;
                break;
            }
            if(operation_7 == 2U)
            {
                _S179 = _S179 + _S558;
                break;
            }
            if(operation_7 == 3U)
            {
                _S179 = _S179 - _S558;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1538.z;
                _S179 = _S179 * float4((1.0f - factor_7))  + _S558 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S179.x);
    }
    else
    {
        clearcoatAmount_0 = _S171;
    }
    float clearcoatRoughness_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool _S1612 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S1612)
            {
                _S179 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S178)));
                break;
            }
            texture2d<float, access::sample> _S1613 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1613).get_width(0)),(*((&atlasHeight_18)) = (_S1613).get_height(0));
            int3 _S1614 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1614)).xy), uint(((_S1614)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1615 = atlasWidth_18;
                uint _S1616 = 2U % atlasWidth_18;
                int _S1617 = int(_S1616);
                uint _S1618 = 2U / _S1615;
                int3 _S1619 = int3(_S1617, int(_S1618), int(0));
                int _S1620 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1619)).xy), uint(((_S1619)).z))).x * 255.0f)));
                uint _S1621 = atlasWidth_18;
                uint _S1622 = 3U % atlasWidth_18;
                int _S1623 = int(_S1622);
                uint _S1624 = 3U / _S1621;
                int3 _S1625 = int3(_S1623, int(_S1624), int(0));
                int2 minimumTile_18 = int2(_S1620, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1625)).xy), uint(((_S1625)).z))).x * 255.0f))));
                uint _S1626 = atlasWidth_18;
                uint _S1627 = 4U % atlasWidth_18;
                int _S1628 = int(_S1627);
                uint _S1629 = 4U / _S1626;
                int3 _S1630 = int3(_S1628, int(_S1629), int(0));
                uint _S1631 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1630)).xy), uint(((_S1630)).z))).x * 255.0f));
                uint _S1632 = atlasWidth_18;
                uint _S1633 = 5U % atlasWidth_18;
                int _S1634 = int(_S1633);
                uint _S1635 = 5U / _S1632;
                int3 _S1636 = int3(_S1634, int(_S1635), int(0));
                uint _S1637 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1636)).xy), uint(((_S1636)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1631, _S1637);
                int2 tile_36 = int2(floor(_S178)) - minimumTile_18;
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
                    int3 _S1638 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1638)).xy), uint(((_S1638)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1631 + uint(tile_36.x));
                uint _S1639 = atlasWidth_18;
                uint _S1640 = offset_18 % atlasWidth_18;
                int _S1641 = int(_S1640);
                uint _S1642 = offset_18 / _S1639;
                int3 _S1643 = int3(_S1641, int(_S1642), int(0));
                uint _S1644 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1643)).xy), uint(((_S1643)).z))).x * 255.0f));
                uint _S1645 = atlasWidth_18;
                uint _S1646 = offset_18 + 1U;
                uint _S1647 = _S1646 % atlasWidth_18;
                int _S1648 = int(_S1647);
                uint _S1649 = _S1646 / _S1645;
                int3 _S1650 = int3(_S1648, int(_S1649), int(0));
                uint slot_36 = _S1644 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1650)).xy), uint(((_S1650)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1651 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1651)).xy), uint(((_S1651)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1652 = atlasWidth_18;
                uint _S1653 = 6U % atlasWidth_18;
                int _S1654 = int(_S1653);
                uint _S1655 = 6U / _S1652;
                int3 _S1656 = int3(_S1654, int(_S1655), int(0));
                uint _S1657 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1656)).xy), uint(((_S1656)).z))).x * 255.0f));
                uint _S1658 = atlasWidth_18;
                uint _S1659 = 7U % atlasWidth_18;
                int _S1660 = int(_S1659);
                uint _S1661 = 7U / _S1658;
                int3 _S1662 = int3(_S1660, int(_S1661), int(0));
                uint columns_18 = _S1657 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1662)).xy), uint(((_S1662)).z))).x * 255.0f)) << 8U);
                uint _S1663 = atlasWidth_18;
                uint _S1664 = 8U % atlasWidth_18;
                int _S1665 = int(_S1664);
                uint _S1666 = 8U / _S1663;
                int3 _S1667 = int3(_S1665, int(_S1666), int(0));
                uint _S1668 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1667)).xy), uint(((_S1667)).z))).x * 255.0f));
                uint _S1669 = atlasWidth_18;
                uint _S1670 = 9U % atlasWidth_18;
                int _S1671 = int(_S1670);
                uint _S1672 = 9U / _S1669;
                int3 _S1673 = int3(_S1671, int(_S1672), int(0));
                uint rows_18 = ((_S1668 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1673)).xy), uint(((_S1673)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1631 * _S1637 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1674 = atlasWidth_18 / columns_18;
                float _S1675 = float(_S1674);
                uint _S1676 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1675, float(_S1676));
                uint _S1677 = slot_37 % columns_18;
                float _S1678 = float(_S1677);
                uint _S1679 = slot_37 / columns_18;
                _S179 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1678, float(_S1679)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(_S178) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1680 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(_S178)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1680);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1681 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S179 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1681)).xy), uint(((_S1681)).z)));
                break;
            }
            uint _S1682 = atlasWidth_18 / uint(_S1680.x);
            float _S1683 = float(_S1682);
            uint _S1684 = (atlasHeight_18 - 1U) / uint(_S1680.y);
            float2 cellSize_37 = float2(_S1683, float(_S1684));
            _S179 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1685 = float4(_S6->compositeControls_0) ;
            if((_S1685.x) != 1024.0f)
            {
                break;
            }
            bool _S1686 = (_S1685.w) >= 0.5f;
            for(;;)
            {
                if(!_S1686)
                {
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S178)));
                    break;
                }
                texture2d<float, access::sample> _S1687 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1687).get_width(0)),(*((&atlasHeight_19)) = (_S1687).get_height(0));
                int3 _S1688 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1688)).xy), uint(((_S1688)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1689 = atlasWidth_19;
                    uint _S1690 = 2U % atlasWidth_19;
                    int _S1691 = int(_S1690);
                    uint _S1692 = 2U / _S1689;
                    int3 _S1693 = int3(_S1691, int(_S1692), int(0));
                    int _S1694 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1693)).xy), uint(((_S1693)).z))).x * 255.0f)));
                    uint _S1695 = atlasWidth_19;
                    uint _S1696 = 3U % atlasWidth_19;
                    int _S1697 = int(_S1696);
                    uint _S1698 = 3U / _S1695;
                    int3 _S1699 = int3(_S1697, int(_S1698), int(0));
                    int2 minimumTile_19 = int2(_S1694, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1699)).xy), uint(((_S1699)).z))).x * 255.0f))));
                    uint _S1700 = atlasWidth_19;
                    uint _S1701 = 4U % atlasWidth_19;
                    int _S1702 = int(_S1701);
                    uint _S1703 = 4U / _S1700;
                    int3 _S1704 = int3(_S1702, int(_S1703), int(0));
                    uint _S1705 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1704)).xy), uint(((_S1704)).z))).x * 255.0f));
                    uint _S1706 = atlasWidth_19;
                    uint _S1707 = 5U % atlasWidth_19;
                    int _S1708 = int(_S1707);
                    uint _S1709 = 5U / _S1706;
                    int3 _S1710 = int3(_S1708, int(_S1709), int(0));
                    uint _S1711 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1710)).xy), uint(((_S1710)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1705, _S1711);
                    int2 tile_38 = int2(floor(_S178)) - minimumTile_19;
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
                        int3 _S1712 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1712)).xy), uint(((_S1712)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1705 + uint(tile_38.x));
                    uint _S1713 = atlasWidth_19;
                    uint _S1714 = offset_19 % atlasWidth_19;
                    int _S1715 = int(_S1714);
                    uint _S1716 = offset_19 / _S1713;
                    int3 _S1717 = int3(_S1715, int(_S1716), int(0));
                    uint _S1718 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1717)).xy), uint(((_S1717)).z))).x * 255.0f));
                    uint _S1719 = atlasWidth_19;
                    uint _S1720 = offset_19 + 1U;
                    uint _S1721 = _S1720 % atlasWidth_19;
                    int _S1722 = int(_S1721);
                    uint _S1723 = _S1720 / _S1719;
                    int3 _S1724 = int3(_S1722, int(_S1723), int(0));
                    uint slot_38 = _S1718 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1724)).xy), uint(((_S1724)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1725 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1725)).xy), uint(((_S1725)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1726 = atlasWidth_19;
                    uint _S1727 = 6U % atlasWidth_19;
                    int _S1728 = int(_S1727);
                    uint _S1729 = 6U / _S1726;
                    int3 _S1730 = int3(_S1728, int(_S1729), int(0));
                    uint _S1731 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1730)).xy), uint(((_S1730)).z))).x * 255.0f));
                    uint _S1732 = atlasWidth_19;
                    uint _S1733 = 7U % atlasWidth_19;
                    int _S1734 = int(_S1733);
                    uint _S1735 = 7U / _S1732;
                    int3 _S1736 = int3(_S1734, int(_S1735), int(0));
                    uint columns_19 = _S1731 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1736)).xy), uint(((_S1736)).z))).x * 255.0f)) << 8U);
                    uint _S1737 = atlasWidth_19;
                    uint _S1738 = 8U % atlasWidth_19;
                    int _S1739 = int(_S1738);
                    uint _S1740 = 8U / _S1737;
                    int3 _S1741 = int3(_S1739, int(_S1740), int(0));
                    uint _S1742 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1741)).xy), uint(((_S1741)).z))).x * 255.0f));
                    uint _S1743 = atlasWidth_19;
                    uint _S1744 = 9U % atlasWidth_19;
                    int _S1745 = int(_S1744);
                    uint _S1746 = 9U / _S1743;
                    int3 _S1747 = int3(_S1745, int(_S1746), int(0));
                    uint rows_19 = ((_S1742 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1747)).xy), uint(((_S1747)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1705 * _S1711 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1748 = atlasWidth_19 / columns_19;
                    float _S1749 = float(_S1748);
                    uint _S1750 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1749, float(_S1750));
                    uint _S1751 = slot_39 % columns_19;
                    float _S1752 = float(_S1751);
                    uint _S1753 = slot_39 / columns_19;
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1752, float(_S1753)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(_S178) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1754 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(_S178)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1754);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1755 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1755)).xy), uint(((_S1755)).z)));
                    break;
                }
                uint _S1756 = atlasWidth_19 / uint(_S1754.x);
                float _S1757 = float(_S1756);
                uint _S1758 = (atlasHeight_19 - 1U) / uint(_S1754.y);
                float2 cellSize_39 = float2(_S1757, float(_S1758));
                _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_8 = uint(round(_S1685.y));
            if(operation_8 == 1U)
            {
                _S179 = _S179 * _S558;
                break;
            }
            if(operation_8 == 2U)
            {
                _S179 = _S179 + _S558;
                break;
            }
            if(operation_8 == 3U)
            {
                _S179 = _S179 - _S558;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1685.z;
                _S179 = _S179 * float4((1.0f - factor_8))  + _S558 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S179.x);
    }
    else
    {
        clearcoatRoughness_0 = _S172;
    }
    float ior_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool _S1759 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S1759)
            {
                _S179 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S178)));
                break;
            }
            texture2d<float, access::sample> _S1760 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_20;
            thread uint atlasHeight_20;
            (*((&atlasWidth_20)) = (_S1760).get_width(0)),(*((&atlasHeight_20)) = (_S1760).get_height(0));
            int3 _S1761 = int3(int(0), int(0), int(0));
            float4 metadata_20 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1761)).xy), uint(((_S1761)).z))) * float4(255.0f) );
            if((metadata_20.x) == 255.0f)
            {
                uint _S1762 = atlasWidth_20;
                uint _S1763 = 2U % atlasWidth_20;
                int _S1764 = int(_S1763);
                uint _S1765 = 2U / _S1762;
                int3 _S1766 = int3(_S1764, int(_S1765), int(0));
                int _S1767 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1766)).xy), uint(((_S1766)).z))).x * 255.0f)));
                uint _S1768 = atlasWidth_20;
                uint _S1769 = 3U % atlasWidth_20;
                int _S1770 = int(_S1769);
                uint _S1771 = 3U / _S1768;
                int3 _S1772 = int3(_S1770, int(_S1771), int(0));
                int2 minimumTile_20 = int2(_S1767, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1772)).xy), uint(((_S1772)).z))).x * 255.0f))));
                uint _S1773 = atlasWidth_20;
                uint _S1774 = 4U % atlasWidth_20;
                int _S1775 = int(_S1774);
                uint _S1776 = 4U / _S1773;
                int3 _S1777 = int3(_S1775, int(_S1776), int(0));
                uint _S1778 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1777)).xy), uint(((_S1777)).z))).x * 255.0f));
                uint _S1779 = atlasWidth_20;
                uint _S1780 = 5U % atlasWidth_20;
                int _S1781 = int(_S1780);
                uint _S1782 = 5U / _S1779;
                int3 _S1783 = int3(_S1781, int(_S1782), int(0));
                uint _S1784 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1783)).xy), uint(((_S1783)).z))).x * 255.0f));
                uint2 grid_20 = uint2(_S1778, _S1784);
                int2 tile_40 = int2(floor(_S178)) - minimumTile_20;
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
                    int3 _S1785 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1785)).xy), uint(((_S1785)).z)));
                    break;
                }
                uint offset_20 = 10U + 2U * (uint(tile_40.y) * _S1778 + uint(tile_40.x));
                uint _S1786 = atlasWidth_20;
                uint _S1787 = offset_20 % atlasWidth_20;
                int _S1788 = int(_S1787);
                uint _S1789 = offset_20 / _S1786;
                int3 _S1790 = int3(_S1788, int(_S1789), int(0));
                uint _S1791 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1790)).xy), uint(((_S1790)).z))).x * 255.0f));
                uint _S1792 = atlasWidth_20;
                uint _S1793 = offset_20 + 1U;
                uint _S1794 = _S1793 % atlasWidth_20;
                int _S1795 = int(_S1794);
                uint _S1796 = _S1793 / _S1792;
                int3 _S1797 = int3(_S1795, int(_S1796), int(0));
                uint slot_40 = _S1791 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1797)).xy), uint(((_S1797)).z))).x * 255.0f)) << 8U);
                if(slot_40 == 0U)
                {
                    int3 _S1798 = int3(int(1), int(0), int(0));
                    _S179 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1798)).xy), uint(((_S1798)).z)));
                    break;
                }
                uint slot_41 = slot_40 - 1U;
                uint _S1799 = atlasWidth_20;
                uint _S1800 = 6U % atlasWidth_20;
                int _S1801 = int(_S1800);
                uint _S1802 = 6U / _S1799;
                int3 _S1803 = int3(_S1801, int(_S1802), int(0));
                uint _S1804 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1803)).xy), uint(((_S1803)).z))).x * 255.0f));
                uint _S1805 = atlasWidth_20;
                uint _S1806 = 7U % atlasWidth_20;
                int _S1807 = int(_S1806);
                uint _S1808 = 7U / _S1805;
                int3 _S1809 = int3(_S1807, int(_S1808), int(0));
                uint columns_20 = _S1804 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1809)).xy), uint(((_S1809)).z))).x * 255.0f)) << 8U);
                uint _S1810 = atlasWidth_20;
                uint _S1811 = 8U % atlasWidth_20;
                int _S1812 = int(_S1811);
                uint _S1813 = 8U / _S1810;
                int3 _S1814 = int3(_S1812, int(_S1813), int(0));
                uint _S1815 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1814)).xy), uint(((_S1814)).z))).x * 255.0f));
                uint _S1816 = atlasWidth_20;
                uint _S1817 = 9U % atlasWidth_20;
                int _S1818 = int(_S1817);
                uint _S1819 = 9U / _S1816;
                int3 _S1820 = int3(_S1818, int(_S1819), int(0));
                uint rows_20 = ((_S1815 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1820)).xy), uint(((_S1820)).z))).x * 255.0f)) << 8U)) + columns_20 - 1U) / columns_20;
                uint metadataRows_20 = (10U + 2U * _S1778 * _S1784 + atlasWidth_20 - 1U) / atlasWidth_20;
                uint _S1821 = atlasWidth_20 / columns_20;
                float _S1822 = float(_S1821);
                uint _S1823 = (atlasHeight_20 - metadataRows_20) / rows_20;
                float2 cellSize_40 = float2(_S1822, float(_S1823));
                uint _S1824 = slot_41 % columns_20;
                float _S1825 = float(_S1824);
                uint _S1826 = slot_41 / columns_20;
                _S179 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1825, float(_S1826)) * cellSize_40 + float2(1.5f, float(metadataRows_20) + 1.5f) + fract(_S178) * max(cellSize_40 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
                break;
            }
            int2 _S1827 = int2(metadata_20.zw);
            int2 tile_41 = int2(floor(_S178)) - int2(metadata_20.xy);
            if(any(tile_41 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_41 >= _S1827);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1828 = int3(int(min(1U, atlasWidth_20 - 1U)), int(0), int(0));
                _S179 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1828)).xy), uint(((_S1828)).z)));
                break;
            }
            uint _S1829 = atlasWidth_20 / uint(_S1827.x);
            float _S1830 = float(_S1829);
            uint _S1831 = (atlasHeight_20 - 1U) / uint(_S1827.y);
            float2 cellSize_41 = float2(_S1830, float(_S1831));
            _S179 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_41) * cellSize_41 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_41 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
            break;
        }
        for(;;)
        {
            float4 _S1832 = float4(_S6->compositeControls_0) ;
            if((_S1832.x) != 2048.0f)
            {
                break;
            }
            bool _S1833 = (_S1832.w) >= 0.5f;
            for(;;)
            {
                if(!_S1833)
                {
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S178)));
                    break;
                }
                texture2d<float, access::sample> _S1834 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_21;
                thread uint atlasHeight_21;
                (*((&atlasWidth_21)) = (_S1834).get_width(0)),(*((&atlasHeight_21)) = (_S1834).get_height(0));
                int3 _S1835 = int3(int(0), int(0), int(0));
                float4 metadata_21 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1835)).xy), uint(((_S1835)).z))) * float4(255.0f) );
                if((metadata_21.x) == 255.0f)
                {
                    uint _S1836 = atlasWidth_21;
                    uint _S1837 = 2U % atlasWidth_21;
                    int _S1838 = int(_S1837);
                    uint _S1839 = 2U / _S1836;
                    int3 _S1840 = int3(_S1838, int(_S1839), int(0));
                    int _S1841 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1840)).xy), uint(((_S1840)).z))).x * 255.0f)));
                    uint _S1842 = atlasWidth_21;
                    uint _S1843 = 3U % atlasWidth_21;
                    int _S1844 = int(_S1843);
                    uint _S1845 = 3U / _S1842;
                    int3 _S1846 = int3(_S1844, int(_S1845), int(0));
                    int2 minimumTile_21 = int2(_S1841, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1846)).xy), uint(((_S1846)).z))).x * 255.0f))));
                    uint _S1847 = atlasWidth_21;
                    uint _S1848 = 4U % atlasWidth_21;
                    int _S1849 = int(_S1848);
                    uint _S1850 = 4U / _S1847;
                    int3 _S1851 = int3(_S1849, int(_S1850), int(0));
                    uint _S1852 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1851)).xy), uint(((_S1851)).z))).x * 255.0f));
                    uint _S1853 = atlasWidth_21;
                    uint _S1854 = 5U % atlasWidth_21;
                    int _S1855 = int(_S1854);
                    uint _S1856 = 5U / _S1853;
                    int3 _S1857 = int3(_S1855, int(_S1856), int(0));
                    uint _S1858 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1857)).xy), uint(((_S1857)).z))).x * 255.0f));
                    uint2 grid_21 = uint2(_S1852, _S1858);
                    int2 tile_42 = int2(floor(_S178)) - minimumTile_21;
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
                        int3 _S1859 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1859)).xy), uint(((_S1859)).z)));
                        break;
                    }
                    uint offset_21 = 10U + 2U * (uint(tile_42.y) * _S1852 + uint(tile_42.x));
                    uint _S1860 = atlasWidth_21;
                    uint _S1861 = offset_21 % atlasWidth_21;
                    int _S1862 = int(_S1861);
                    uint _S1863 = offset_21 / _S1860;
                    int3 _S1864 = int3(_S1862, int(_S1863), int(0));
                    uint _S1865 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1864)).xy), uint(((_S1864)).z))).x * 255.0f));
                    uint _S1866 = atlasWidth_21;
                    uint _S1867 = offset_21 + 1U;
                    uint _S1868 = _S1867 % atlasWidth_21;
                    int _S1869 = int(_S1868);
                    uint _S1870 = _S1867 / _S1866;
                    int3 _S1871 = int3(_S1869, int(_S1870), int(0));
                    uint slot_42 = _S1865 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1871)).xy), uint(((_S1871)).z))).x * 255.0f)) << 8U);
                    if(slot_42 == 0U)
                    {
                        int3 _S1872 = int3(int(1), int(0), int(0));
                        _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1872)).xy), uint(((_S1872)).z)));
                        break;
                    }
                    uint slot_43 = slot_42 - 1U;
                    uint _S1873 = atlasWidth_21;
                    uint _S1874 = 6U % atlasWidth_21;
                    int _S1875 = int(_S1874);
                    uint _S1876 = 6U / _S1873;
                    int3 _S1877 = int3(_S1875, int(_S1876), int(0));
                    uint _S1878 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1877)).xy), uint(((_S1877)).z))).x * 255.0f));
                    uint _S1879 = atlasWidth_21;
                    uint _S1880 = 7U % atlasWidth_21;
                    int _S1881 = int(_S1880);
                    uint _S1882 = 7U / _S1879;
                    int3 _S1883 = int3(_S1881, int(_S1882), int(0));
                    uint columns_21 = _S1878 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1883)).xy), uint(((_S1883)).z))).x * 255.0f)) << 8U);
                    uint _S1884 = atlasWidth_21;
                    uint _S1885 = 8U % atlasWidth_21;
                    int _S1886 = int(_S1885);
                    uint _S1887 = 8U / _S1884;
                    int3 _S1888 = int3(_S1886, int(_S1887), int(0));
                    uint _S1889 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1888)).xy), uint(((_S1888)).z))).x * 255.0f));
                    uint _S1890 = atlasWidth_21;
                    uint _S1891 = 9U % atlasWidth_21;
                    int _S1892 = int(_S1891);
                    uint _S1893 = 9U / _S1890;
                    int3 _S1894 = int3(_S1892, int(_S1893), int(0));
                    uint rows_21 = ((_S1889 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1894)).xy), uint(((_S1894)).z))).x * 255.0f)) << 8U)) + columns_21 - 1U) / columns_21;
                    uint metadataRows_21 = (10U + 2U * _S1852 * _S1858 + atlasWidth_21 - 1U) / atlasWidth_21;
                    uint _S1895 = atlasWidth_21 / columns_21;
                    float _S1896 = float(_S1895);
                    uint _S1897 = (atlasHeight_21 - metadataRows_21) / rows_21;
                    float2 cellSize_42 = float2(_S1896, float(_S1897));
                    uint _S1898 = slot_43 % columns_21;
                    float _S1899 = float(_S1898);
                    uint _S1900 = slot_43 / columns_21;
                    _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1899, float(_S1900)) * cellSize_42 + float2(1.5f, float(metadataRows_21) + 1.5f) + fract(_S178) * max(cellSize_42 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                    break;
                }
                int2 _S1901 = int2(metadata_21.zw);
                int2 tile_43 = int2(floor(_S178)) - int2(metadata_21.xy);
                if(any(tile_43 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_43 >= _S1901);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1902 = int3(int(min(1U, atlasWidth_21 - 1U)), int(0), int(0));
                    _S558 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1902)).xy), uint(((_S1902)).z)));
                    break;
                }
                uint _S1903 = atlasWidth_21 / uint(_S1901.x);
                float _S1904 = float(_S1903);
                uint _S1905 = (atlasHeight_21 - 1U) / uint(_S1901.y);
                float2 cellSize_43 = float2(_S1904, float(_S1905));
                _S558 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_43) * cellSize_43 + float2(1.5f, 2.5f) + fract(_S178) * max(cellSize_43 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                break;
            }
            uint operation_9 = uint(round(_S1832.y));
            if(operation_9 == 1U)
            {
                _S179 = _S179 * _S558;
                break;
            }
            if(operation_9 == 2U)
            {
                _S179 = _S179 + _S558;
                break;
            }
            if(operation_9 == 3U)
            {
                _S179 = _S179 - _S558;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1832.z;
                _S179 = _S179 * float4((1.0f - factor_9))  + _S558 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S179.x;
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
    float _S1906 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S1907 = max(0.00100000004749745f, roughness_0);
    float _S1908 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1909 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1909;
    bool omniPbrSpecular_0 = (_S173.w) >= 0.5f;
    float dielectricLevel_0 = _S159.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1910 = float3(dielectricLevel_0) ;
        float3 _S1911 = float3(metallic_0) ;
        float3 _S1912 = mix(_S1910, diffuseColor_0, _S1911);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1910, diffuseColor_0, _S1911);
        grazingIncidence_0 = _S1912;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S165.w) >= 0.5f)
        {
            float3 _S1913 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1913;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1914 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1914);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1914);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1915 = _S2;
    float _S1916 = _S2.ambientLight_0.w;
    if(_S1916 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1917 = _S1915.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1917, _S1917)) > 0.0f;
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
    uint _S1918 = min(uint(_S1916), 128U);
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
    float4 _S1919 = float4(_S6->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S1919.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S6->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S1920 = _S2;
    uint _S1921 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S1921) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S1922 = _S1915.ambientLight_0.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S1923 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S1923;
        for(;;)
        {
            if(lightCount_0 < _S1921)
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
        domeAmbient_1 = _S1922;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S1924 = !hasSceneLighting_0;
    if(_S1924)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1918;
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
        bool _S1925;
        if(hasSceneLighting_0)
        {
            _S1925 = (((uint4(_S6->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S1925 = false;
        }
        if(_S1925)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S1926 = lightIndex_0 == 0U;
        bool _S1927;
        if(_S1926)
        {
            _S1927 = _S1924;
        }
        else
        {
            _S1927 = false;
        }
        float lightType_0;
        if(_S1927)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S1928 = 224U + lightIndex_0 * 16U;
            float _S1929 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1928)>>2]);
            float _S1930 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1928 + 4U)>>2]);
            float _S1931 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1928 + 8U)>>2]);
            float _S1932 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1928 + 12U)>>2]);
            lightType_0 = _S1932;
        }
        bool _S1933;
        if(_S1926)
        {
            _S1933 = _S1924;
        }
        else
        {
            _S1933 = false;
        }
        if(_S1933)
        {
            lightDirection_0 = normalize((float4(_S6->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S1934 = 2272U + lightIndex_0 * 16U;
            float _S1935 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1934)>>2]);
            float _S1936 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1934 + 4U)>>2]);
            float _S1937 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1934 + 8U)>>2]);
            float _S1938 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1934 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S1935, _S1936, _S1937));
        }
        bool _S1939;
        if(_S1926)
        {
            _S1939 = _S1924;
        }
        else
        {
            _S1939 = false;
        }
        if(_S1939)
        {
            roughness_0 = (float4(_S6->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S1940 = 4320U + lightIndex_0 * 16U;
            float _S1941 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1940)>>2]);
            float _S1942 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1940 + 4U)>>2]);
            float _S1943 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1940 + 8U)>>2]);
            float _S1944 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1940 + 12U)>>2]);
            roughness_0 = _S1944;
        }
        bool _S1945;
        if(_S1926)
        {
            _S1945 = _S1924;
        }
        else
        {
            _S1945 = false;
        }
        if(_S1945)
        {
            diffuseColor_0 = _S1919.xyz;
        }
        else
        {
            uint _S1946 = 4320U + lightIndex_0 * 16U;
            float _S1947 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1946)>>2]);
            float _S1948 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1946 + 4U)>>2]);
            float _S1949 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1946 + 8U)>>2]);
            float _S1950 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1946 + 12U)>>2]);
            diffuseColor_0 = float3(_S1947, _S1948, _S1949);
        }
        bool _S1951;
        if(_S1926)
        {
            _S1951 = _S1924;
        }
        else
        {
            _S1951 = false;
        }
        if(_S1951)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S1952 = 6368U + lightIndex_0 * 16U;
            float _S1953 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1952)>>2]);
            float _S1954 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1952 + 4U)>>2]);
            float _S1955 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1952 + 8U)>>2]);
            float _S1956 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1952 + 12U)>>2]);
            metallic_0 = _S1953;
        }
        bool _S1957;
        if(_S1926)
        {
            _S1957 = _S1924;
        }
        else
        {
            _S1957 = false;
        }
        if(_S1957)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S1958 = 6368U + lightIndex_0 * 16U;
            float _S1959 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1958)>>2]);
            float _S1960 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1958 + 4U)>>2]);
            float _S1961 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1958 + 8U)>>2]);
            float _S1962 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1958 + 12U)>>2]);
            clearcoatRoughness_0 = _S1960;
        }
        bool _S1963;
        if(_S1926)
        {
            _S1963 = _S1924;
        }
        else
        {
            _S1963 = false;
        }
        if(_S1963)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1964 = 8416U + lightIndex_0 * 16U;
            float _S1965 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1964)>>2]);
            float _S1966 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1964 + 4U)>>2]);
            float _S1967 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1964 + 8U)>>2]);
            float _S1968 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1964 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S1965, _S1966, _S1967));
        }
        bool _S1969;
        if(_S1926)
        {
            _S1969 = _S1924;
        }
        else
        {
            _S1969 = false;
        }
        if(_S1969)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S1970 = 10464U + lightIndex_0 * 16U;
            float _S1971 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1970)>>2]);
            float _S1972 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1970 + 4U)>>2]);
            float _S1973 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1970 + 8U)>>2]);
            float _S1974 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1970 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S1971, _S1972, _S1973));
        }
        bool _S1975;
        if(_S1926)
        {
            _S1975 = _S1924;
        }
        else
        {
            _S1975 = false;
        }
        float shapeX_0;
        if(_S1975)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S1976 = 8416U + lightIndex_0 * 16U;
            float _S1977 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1976)>>2]);
            float _S1978 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1976 + 4U)>>2]);
            float _S1979 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1976 + 8U)>>2]);
            float _S1980 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1976 + 12U)>>2]);
            shapeX_0 = _S1980;
        }
        bool _S1981;
        if(_S1926)
        {
            _S1981 = _S1924;
        }
        else
        {
            _S1981 = false;
        }
        float shapeY_0;
        if(_S1981)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S1982 = 10464U + lightIndex_0 * 16U;
            float _S1983 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1982)>>2]);
            float _S1984 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1982 + 4U)>>2]);
            float _S1985 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1982 + 8U)>>2]);
            float _S1986 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1982 + 12U)>>2]);
            shapeY_0 = _S1986;
        }
        bool _S1987;
        if(_S1926)
        {
            _S1987 = _S1924;
        }
        else
        {
            _S1987 = false;
        }
        float lightRadius_0;
        if(_S1987)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S1988 = 2272U + lightIndex_0 * 16U;
            float _S1989 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1988)>>2]);
            float _S1990 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1988 + 4U)>>2]);
            float _S1991 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1988 + 8U)>>2]);
            float _S1992 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1988 + 12U)>>2]);
            lightRadius_0 = _S1992;
        }
        bool _S1993;
        if(_S1926)
        {
            _S1993 = _S1924;
        }
        else
        {
            _S1993 = false;
        }
        if(_S1993)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S1994;
        if(_S1926)
        {
            _S1994 = _S1924;
        }
        else
        {
            _S1994 = false;
        }
        if(_S1994)
        {
            _S179 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1995 = 15296U + lightIndex_0 * 16U;
            float _S1996 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1995)>>2]);
            float _S1997 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1995 + 4U)>>2]);
            float _S1998 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1995 + 8U)>>2]);
            float _S1999 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1995 + 12U)>>2]);
            _S179 = float4(_S1996, _S1997, _S1998, _S1999);
        }
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S2000 = 12960U + lightIndex_0 * 16U;
            float _S2001 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2000)>>2]);
            float _S2002 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2000 + 4U)>>2]);
            float _S2003 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2000 + 8U)>>2]);
            float _S2004 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2000 + 12U)>>2]);
            int shadowSlot_0 = int(_S2001);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S2005 = _S2;
                    FragmentFrameParameters_0 _S2006 = _S2;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - specularColor_0;
                    }
                    else
                    {
                        color_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S2005.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S2007 = lightClip_0.w;
                    if(_S2007 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S2007) ;
                    bool _S2008;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S2008 = true;
                    }
                    else
                    {
                        _S2008 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S2009;
                    if(_S2008)
                    {
                        _S2009 = true;
                    }
                    else
                    {
                        _S2009 = (ndc_0.z) < 0.0f;
                    }
                    bool _S2010;
                    if(_S2009)
                    {
                        _S2010 = true;
                    }
                    else
                    {
                        _S2010 = (ndc_0.z) > 1.0f;
                    }
                    if(_S2010)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S2011 = _S2006.shadowTile_0[shadowSlot_0].xy;
                    float2 _S2012 = _S2006.shadowTile_0[shadowSlot_0].zw;
                    float2 _S2013 = _S2011 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S2012;
                    float texel_0 = _S2005.shadowControls_0[shadowSlot_0].w;
                    float _S2014 = max(_S2005.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S2015 = ndc_0.z - _S2005.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S2016 = float2((texel_0 * 0.5f)) ;
                    float2 _S2017 = _S2011 + _S2016;
                    float2 _S2018 = _S2011 + _S2012 - _S2016;
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
                            if(_S2015 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S2013 + float2(float(x_0), float(y_0)) * float2((_S2014 * texel_0)) , _S2017, _S2018)), level((0.0f))).x))
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
        float3 _S2019 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S2019;
        sampleOffsets_0[int(1)] = _S2019;
        sampleOffsets_0[int(2)] = _S2019;
        sampleOffsets_0[int(3)] = _S2019;
        sampleOffsets_0[int(4)] = _S2019;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S2020 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S2020 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S2020 - halfHeight_0;
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
                uint _S2021 = 224U + lightIndex_0 * 16U;
                float _S2022 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2021)>>2]);
                float _S2023 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2021 + 4U)>>2]);
                float _S2024 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2021 + 8U)>>2]);
                float _S2025 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2021 + 12U)>>2]);
                float3 toLight_0 = float3(_S2022, _S2023, _S2024) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S2026 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S2026)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S2026;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S179.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S179.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S2027 = _S179.y;
                        if(_S2027 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S2027);
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
            float _S2028 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_0;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_0 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S2028)) ;
            }
            else
            {
                diffuseFresnel_0 = mix(normalIncidence_0, grazingIncidence_0, float3(_S2028) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_0);
            float3 _S2029 = float3(_S2028) ;
            float _S2030 = max(_S1907, 0.00100000004749745f);
            float alpha_0 = _S2030 * _S2030;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S2031 = 1.0f - k_0;
            float3 _S2032 = float3(max(4.0f * normalDotLight_0 * _S1906, 1.00000000317107685e-30f)) ;
            float3 _S2033 = mix(normalIncidence_0, grazingIncidence_0, _S2029) * float3((_S1906 / (_S1906 * _S2031 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S2031 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S2032;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S2034 = max(_S1908, 0.00100000004749745f);
                float alpha_1 = _S2034 * _S2034;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S2035 = 1.0f - k_1;
                directSpecular_0 = _S2033 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S2029) * float3((_S1906 / (_S1906 * _S2035 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S2035 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S2032);
            }
            else
            {
                directSpecular_0 = _S2033;
            }
            float3 _S2036 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S2036 * _S1909) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S2036);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S2037 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S2038 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S2039 = saturate(_S1907);
        float2 _S2040 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2038, _S2039)), level((0.0f)))).xy;
        float _S2041 = _S2040.x;
        float _S2042 = _S2040.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S2041)  + grazingIncidence_0 * float3(_S2042) ;
        if(omniPbrSpecular_0)
        {
            lightDirection_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S2041 + _S2042)) ;
        }
        else
        {
            lightDirection_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - lightDirection_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S2043 = max(_S1920.domeControls_0.y, 1.0f);
        float3 _S2044 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S2043 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1920.domeControls_0.z;
            float _S2045 = _S1920.domeControls_0.w;
            for(;;)
            {
                bool _S2046 = _S2043 <= 1.0f;
                _S4 = _S2046;
                if(_S2046)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S2045, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S2043)), level((0.0f)))).xyz;
                break;
            }
            float _S2047 = _S2037.environmentControls_0.y;
            float _S2048 = _S2037.environmentControls_0.z;
            for(;;)
            {
                if(_S4)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2049 = max(_S2047, 1.0f);
                    float _S2050 = _S2049 - 1.0f;
                    float slice_0 = _S2039 * max(_S2050, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S2048, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S2049)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S2050) + v_0) / _S2049)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S2051 = max(_S2047, 1.0f);
                float total_0 = _S2051 * _S2043;
                float _S2052 = _S2051 - 1.0f;
                float slice_1 = _S2039 * max(_S2052, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S2048, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S2051;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S2052) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S2053 = max(_S2047, 1.0f);
                        float _S2054 = _S2053 - 1.0f;
                        float slice_2 = saturate(_S1908) * max(_S2054, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S2048, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S2053)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S2054) + v_2) / _S2053)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2055 = max(_S2047, 1.0f);
                    float total_1 = _S2055 * _S2043;
                    float _S2056 = _S2055 - 1.0f;
                    float slice_3 = saturate(_S1908) * max(_S2056, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S2048, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S2055;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S2056) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                color_0 = specularColor_0;
            }
            else
            {
                color_0 = _S2044;
            }
            lightTangent_0 = diffuseColor_0;
            lightBitangent_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightTangent_0 = _S2044;
            lightBitangent_0 = _S2044;
            color_0 = _S2044;
            for(;;)
            {
                if(sampleIndex_0 < _S1921)
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
                float _S2057 = _S1920.domeControls_0.w;
                for(;;)
                {
                    bool _S2058 = _S2043 <= 1.0f;
                    _S5 = _S2058;
                    if(_S2058)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S2057, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S2043)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightTangent_0 + diffuseColor_0;
                float _S2059 = _S2037.environmentControls_0.y;
                float _S2060 = _S2037.environmentControls_0.z;
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2061 = max(_S2059, 1.0f);
                        float _S2062 = _S2061 - 1.0f;
                        float slice_4 = _S2039 * max(_S2062, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S2060, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S2061)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S2062) + v_4) / _S2061)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2063 = max(_S2059, 1.0f);
                    float total_2 = _S2063 * _S2043;
                    float _S2064 = _S2063 - 1.0f;
                    float slice_5 = _S2039 * max(_S2064, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S2060, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S2063;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S2064) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
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
                            float _S2065 = max(_S2059, 1.0f);
                            float _S2066 = _S2065 - 1.0f;
                            float slice_6 = saturate(_S1908) * max(_S2066, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S2060, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S2065)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S2066) + v_6) / _S2065)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2067 = max(_S2059, 1.0f);
                        float total_3 = _S2067 * _S2043;
                        float _S2068 = _S2067 - 1.0f;
                        float slice_7 = saturate(_S1908) * max(_S2068, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S2060, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S2067;
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S2068) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S2069 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2038, saturate(_S1908))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * color_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S2069.x)  + float3(_S2069.y) );
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
    pixelOutput_0 _S2070 = { float4(color_3, opacity_0) };
    return _S2070;
}
