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

[[fragment]] pixelOutput_0 fragmentMain_uv_material(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> diffuseValuesTexture_1 [[texture(22)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> emissiveValuesTexture_1 [[texture(23)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_0;
    FragmentFrameParameters_0 _S2;
    uint4 _S3;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    float3 color_0;
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
    float2 _S182;
    bool hasSceneLighting_0;
    float4 _S183;
    uint3 _S184;
    if((textureMask_0 & 2U) != 0U)
    {
        if((secondaryMask_0 & 2U) != 0U)
        {
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        thread float4 diffuseSample_0;
        bool _S185 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S185)
            {
                _S183 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S182)));
                break;
            }
            texture2d<float, access::sample> _S186 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S186).get_width(0)),(*((&atlasHeight_0)) = (_S186).get_height(0));
            int3 _S187 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S187)).xy), uint(((_S187)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S188 = atlasWidth_0;
                uint _S189 = 2U % atlasWidth_0;
                int _S190 = int(_S189);
                uint _S191 = 2U / _S188;
                int3 _S192 = int3(_S190, int(_S191), int(0));
                int _S193 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S192)).xy), uint(((_S192)).z))).x * 255.0f)));
                uint _S194 = atlasWidth_0;
                uint _S195 = 3U % atlasWidth_0;
                int _S196 = int(_S195);
                uint _S197 = 3U / _S194;
                int3 _S198 = int3(_S196, int(_S197), int(0));
                int2 minimumTile_0 = int2(_S193, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S198)).xy), uint(((_S198)).z))).x * 255.0f))));
                uint _S199 = atlasWidth_0;
                uint _S200 = 4U % atlasWidth_0;
                int _S201 = int(_S200);
                uint _S202 = 4U / _S199;
                int3 _S203 = int3(_S201, int(_S202), int(0));
                uint _S204 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S203)).xy), uint(((_S203)).z))).x * 255.0f));
                uint _S205 = atlasWidth_0;
                uint _S206 = 5U % atlasWidth_0;
                int _S207 = int(_S206);
                uint _S208 = 5U / _S205;
                int3 _S209 = int3(_S207, int(_S208), int(0));
                uint _S210 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S209)).xy), uint(((_S209)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S204, _S210);
                int2 tile_0 = int2(floor(_S182)) - minimumTile_0;
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
                    int3 _S211 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S211)).xy), uint(((_S211)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S204 + uint(tile_0.x));
                uint _S212 = atlasWidth_0;
                uint _S213 = offset_0 % atlasWidth_0;
                int _S214 = int(_S213);
                uint _S215 = offset_0 / _S212;
                int3 _S216 = int3(_S214, int(_S215), int(0));
                uint _S217 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S216)).xy), uint(((_S216)).z))).x * 255.0f));
                uint _S218 = atlasWidth_0;
                uint _S219 = offset_0 + 1U;
                uint _S220 = _S219 % atlasWidth_0;
                int _S221 = int(_S220);
                uint _S222 = _S219 / _S218;
                int3 _S223 = int3(_S221, int(_S222), int(0));
                uint slot_0 = _S217 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S223)).xy), uint(((_S223)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S224 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S224)).xy), uint(((_S224)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S225 = atlasWidth_0;
                uint _S226 = 6U % atlasWidth_0;
                int _S227 = int(_S226);
                uint _S228 = 6U / _S225;
                int3 _S229 = int3(_S227, int(_S228), int(0));
                uint _S230 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S229)).xy), uint(((_S229)).z))).x * 255.0f));
                uint _S231 = atlasWidth_0;
                uint _S232 = 7U % atlasWidth_0;
                int _S233 = int(_S232);
                uint _S234 = 7U / _S231;
                int3 _S235 = int3(_S233, int(_S234), int(0));
                uint columns_0 = _S230 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S235)).xy), uint(((_S235)).z))).x * 255.0f)) << 8U);
                uint _S236 = atlasWidth_0;
                uint _S237 = 8U % atlasWidth_0;
                int _S238 = int(_S237);
                uint _S239 = 8U / _S236;
                int3 _S240 = int3(_S238, int(_S239), int(0));
                uint _S241 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S240)).xy), uint(((_S240)).z))).x * 255.0f));
                uint _S242 = atlasWidth_0;
                uint _S243 = 9U % atlasWidth_0;
                int _S244 = int(_S243);
                uint _S245 = 9U / _S242;
                int3 _S246 = int3(_S244, int(_S245), int(0));
                uint rows_0 = ((_S241 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S246)).xy), uint(((_S246)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S204 * _S210 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S247 = atlasWidth_0 / columns_0;
                float _S248 = float(_S247);
                uint _S249 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S248, float(_S249));
                uint _S250 = slot_1 % columns_0;
                float _S251 = float(_S250);
                uint _S252 = slot_1 / columns_0;
                _S183 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S251, float(_S252)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S182) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S253 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(_S182)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S253);
            }
            if(hasSceneLighting_0)
            {
                int3 _S254 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S183 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S254)).xy), uint(((_S254)).z)));
                break;
            }
            uint _S255 = atlasWidth_0 / uint(_S253.x);
            float _S256 = float(_S255);
            uint _S257 = (atlasHeight_0 - 1U) / uint(_S253.y);
            float2 cellSize_1 = float2(_S256, float(_S257));
            _S183 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S183;
        float4 _S258 = float4(_S6->compositeControls_0) ;
        float _S259 = _S258.x;
        if(_S259 == 0.0f)
        {
            hasSceneLighting_0 = (_S258.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S260 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S260).get_width(0)),(*((&sourceHeight_0)) = (_S260).get_height(0));
            texture2d<float, access::sample> _S261 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S261).get_width(0)),(*((&valueHeight_0)) = (_S261).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S262 = diffuseSample_0;
                for(;;)
                {
                    float _S263 = _S262.w;
                    if(_S263 > 1.0f)
                    {
                        diffuseColor_0 = _S262.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S264 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S264).get_width(0)),(*((&height_0)) = (_S264).get_height(0));
                    uint3 _S265 = uint3(round(_S262.xyz * float3(255.0f) ));
                    uint index_1 = ((_S265.x) | ((_S265.y) << 8U)) | ((_S265.z) << 16U);
                    if(_S263 == 0.0f)
                    {
                        _S184 = _S265;
                    }
                    else
                    {
                        _S184 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S184 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S266 = address_0.x;
                    uint _S267 = _S266 % width_0;
                    int _S268 = int(_S267);
                    uint _S269 = _S266 / width_0;
                    int3 _S270 = int3(_S268, int(_S269), int(0));
                    float _S271 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S270)).xy), uint(((_S270)).z))).x;
                    uint _S272 = address_0.y;
                    uint _S273 = _S272 % width_0;
                    int _S274 = int(_S273);
                    uint _S275 = _S272 / width_0;
                    int3 _S276 = int3(_S274, int(_S275), int(0));
                    float _S277 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S276)).xy), uint(((_S276)).z))).x;
                    uint _S278 = address_0.z;
                    uint _S279 = _S278 % width_0;
                    int _S280 = int(_S279);
                    uint _S281 = _S278 / width_0;
                    int3 _S282 = int3(_S280, int(_S281), int(0));
                    diffuseColor_0 = float3(_S271, _S277, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S282)).xy), uint(((_S282)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                diffuseSample_0.z = (((&kernelContext_0)->diffuseValuesTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S182))).x;
            }
        }
        float4 _S283 = diffuseSample_0;
        for(;;)
        {
            if(_S259 != 2.0f)
            {
                _S183 = _S283;
                break;
            }
            bool _S284 = (_S258.w) >= 0.5f;
            for(;;)
            {
                if(!_S284)
                {
                    _S183 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S285 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S285).get_width(0)),(*((&atlasHeight_1)) = (_S285).get_height(0));
                int3 _S286 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S286)).xy), uint(((_S286)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S287 = atlasWidth_1;
                    uint _S288 = 2U % atlasWidth_1;
                    int _S289 = int(_S288);
                    uint _S290 = 2U / _S287;
                    int3 _S291 = int3(_S289, int(_S290), int(0));
                    int _S292 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S291)).xy), uint(((_S291)).z))).x * 255.0f)));
                    uint _S293 = atlasWidth_1;
                    uint _S294 = 3U % atlasWidth_1;
                    int _S295 = int(_S294);
                    uint _S296 = 3U / _S293;
                    int3 _S297 = int3(_S295, int(_S296), int(0));
                    int2 minimumTile_1 = int2(_S292, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S297)).xy), uint(((_S297)).z))).x * 255.0f))));
                    uint _S298 = atlasWidth_1;
                    uint _S299 = 4U % atlasWidth_1;
                    int _S300 = int(_S299);
                    uint _S301 = 4U / _S298;
                    int3 _S302 = int3(_S300, int(_S301), int(0));
                    uint _S303 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S302)).xy), uint(((_S302)).z))).x * 255.0f));
                    uint _S304 = atlasWidth_1;
                    uint _S305 = 5U % atlasWidth_1;
                    int _S306 = int(_S305);
                    uint _S307 = 5U / _S304;
                    int3 _S308 = int3(_S306, int(_S307), int(0));
                    uint _S309 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S308)).xy), uint(((_S308)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S303, _S309);
                    int2 tile_2 = int2(floor(_S182)) - minimumTile_1;
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
                        int3 _S310 = int3(int(1), int(0), int(0));
                        _S183 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S310)).xy), uint(((_S310)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S303 + uint(tile_2.x));
                    uint _S311 = atlasWidth_1;
                    uint _S312 = offset_1 % atlasWidth_1;
                    int _S313 = int(_S312);
                    uint _S314 = offset_1 / _S311;
                    int3 _S315 = int3(_S313, int(_S314), int(0));
                    uint _S316 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S315)).xy), uint(((_S315)).z))).x * 255.0f));
                    uint _S317 = atlasWidth_1;
                    uint _S318 = offset_1 + 1U;
                    uint _S319 = _S318 % atlasWidth_1;
                    int _S320 = int(_S319);
                    uint _S321 = _S318 / _S317;
                    int3 _S322 = int3(_S320, int(_S321), int(0));
                    uint slot_2 = _S316 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S322)).xy), uint(((_S322)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S323 = int3(int(1), int(0), int(0));
                        _S183 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S323)).xy), uint(((_S323)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S324 = atlasWidth_1;
                    uint _S325 = 6U % atlasWidth_1;
                    int _S326 = int(_S325);
                    uint _S327 = 6U / _S324;
                    int3 _S328 = int3(_S326, int(_S327), int(0));
                    uint _S329 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S328)).xy), uint(((_S328)).z))).x * 255.0f));
                    uint _S330 = atlasWidth_1;
                    uint _S331 = 7U % atlasWidth_1;
                    int _S332 = int(_S331);
                    uint _S333 = 7U / _S330;
                    int3 _S334 = int3(_S332, int(_S333), int(0));
                    uint columns_1 = _S329 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S334)).xy), uint(((_S334)).z))).x * 255.0f)) << 8U);
                    uint _S335 = atlasWidth_1;
                    uint _S336 = 8U % atlasWidth_1;
                    int _S337 = int(_S336);
                    uint _S338 = 8U / _S335;
                    int3 _S339 = int3(_S337, int(_S338), int(0));
                    uint _S340 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S339)).xy), uint(((_S339)).z))).x * 255.0f));
                    uint _S341 = atlasWidth_1;
                    uint _S342 = 9U % atlasWidth_1;
                    int _S343 = int(_S342);
                    uint _S344 = 9U / _S341;
                    int3 _S345 = int3(_S343, int(_S344), int(0));
                    uint rows_1 = ((_S340 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S345)).xy), uint(((_S345)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S303 * _S309 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S346 = atlasWidth_1 / columns_1;
                    float _S347 = float(_S346);
                    uint _S348 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S347, float(_S348));
                    uint _S349 = slot_3 % columns_1;
                    float _S350 = float(_S349);
                    uint _S351 = slot_3 / columns_1;
                    _S183 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S350, float(_S351)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S182) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S352 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(_S182)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S352);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S353 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S353)).xy), uint(((_S353)).z)));
                    break;
                }
                uint _S354 = atlasWidth_1 / uint(_S352.x);
                float _S355 = float(_S354);
                uint _S356 = (atlasHeight_1 - 1U) / uint(_S352.y);
                float2 cellSize_3 = float2(_S355, float(_S356));
                _S183 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S258.y));
            if(operation_0 == 1U)
            {
                _S183 = _S283 * _S183;
                break;
            }
            if(operation_0 == 2U)
            {
                _S183 = _S283 + _S183;
                break;
            }
            if(operation_0 == 3U)
            {
                _S183 = _S283 - _S183;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S258.z;
                _S183 = _S283 * float4((1.0f - factor_0))  + _S183 * float4(factor_0) ;
                break;
            }
            _S183 = _S283;
            break;
        }
        diffuseColor_0 = _S183.xyz;
    }
    float roughness_0;
    float4 _S357;
    if((textureMask_0 & 8U) != 0U)
    {
        if((secondaryMask_0 & 8U) != 0U)
        {
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S358 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S358)
            {
                _S183 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S182)));
                break;
            }
            texture2d<float, access::sample> _S359 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S359).get_width(0)),(*((&atlasHeight_2)) = (_S359).get_height(0));
            int3 _S360 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S360)).xy), uint(((_S360)).z))) * float4(255.0f) );
            if((metadata_2.x) == 255.0f)
            {
                uint _S361 = atlasWidth_2;
                uint _S362 = 2U % atlasWidth_2;
                int _S363 = int(_S362);
                uint _S364 = 2U / _S361;
                int3 _S365 = int3(_S363, int(_S364), int(0));
                int _S366 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S365)).xy), uint(((_S365)).z))).x * 255.0f)));
                uint _S367 = atlasWidth_2;
                uint _S368 = 3U % atlasWidth_2;
                int _S369 = int(_S368);
                uint _S370 = 3U / _S367;
                int3 _S371 = int3(_S369, int(_S370), int(0));
                int2 minimumTile_2 = int2(_S366, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S371)).xy), uint(((_S371)).z))).x * 255.0f))));
                uint _S372 = atlasWidth_2;
                uint _S373 = 4U % atlasWidth_2;
                int _S374 = int(_S373);
                uint _S375 = 4U / _S372;
                int3 _S376 = int3(_S374, int(_S375), int(0));
                uint _S377 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S376)).xy), uint(((_S376)).z))).x * 255.0f));
                uint _S378 = atlasWidth_2;
                uint _S379 = 5U % atlasWidth_2;
                int _S380 = int(_S379);
                uint _S381 = 5U / _S378;
                int3 _S382 = int3(_S380, int(_S381), int(0));
                uint _S383 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S382)).xy), uint(((_S382)).z))).x * 255.0f));
                uint2 grid_2 = uint2(_S377, _S383);
                int2 tile_4 = int2(floor(_S182)) - minimumTile_2;
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
                    int3 _S384 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S384)).xy), uint(((_S384)).z)));
                    break;
                }
                uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S377 + uint(tile_4.x));
                uint _S385 = atlasWidth_2;
                uint _S386 = offset_2 % atlasWidth_2;
                int _S387 = int(_S386);
                uint _S388 = offset_2 / _S385;
                int3 _S389 = int3(_S387, int(_S388), int(0));
                uint _S390 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S389)).xy), uint(((_S389)).z))).x * 255.0f));
                uint _S391 = atlasWidth_2;
                uint _S392 = offset_2 + 1U;
                uint _S393 = _S392 % atlasWidth_2;
                int _S394 = int(_S393);
                uint _S395 = _S392 / _S391;
                int3 _S396 = int3(_S394, int(_S395), int(0));
                uint slot_4 = _S390 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S396)).xy), uint(((_S396)).z))).x * 255.0f)) << 8U);
                if(slot_4 == 0U)
                {
                    int3 _S397 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S397)).xy), uint(((_S397)).z)));
                    break;
                }
                uint slot_5 = slot_4 - 1U;
                uint _S398 = atlasWidth_2;
                uint _S399 = 6U % atlasWidth_2;
                int _S400 = int(_S399);
                uint _S401 = 6U / _S398;
                int3 _S402 = int3(_S400, int(_S401), int(0));
                uint _S403 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S402)).xy), uint(((_S402)).z))).x * 255.0f));
                uint _S404 = atlasWidth_2;
                uint _S405 = 7U % atlasWidth_2;
                int _S406 = int(_S405);
                uint _S407 = 7U / _S404;
                int3 _S408 = int3(_S406, int(_S407), int(0));
                uint columns_2 = _S403 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S408)).xy), uint(((_S408)).z))).x * 255.0f)) << 8U);
                uint _S409 = atlasWidth_2;
                uint _S410 = 8U % atlasWidth_2;
                int _S411 = int(_S410);
                uint _S412 = 8U / _S409;
                int3 _S413 = int3(_S411, int(_S412), int(0));
                uint _S414 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S413)).xy), uint(((_S413)).z))).x * 255.0f));
                uint _S415 = atlasWidth_2;
                uint _S416 = 9U % atlasWidth_2;
                int _S417 = int(_S416);
                uint _S418 = 9U / _S415;
                int3 _S419 = int3(_S417, int(_S418), int(0));
                uint rows_2 = ((_S414 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S419)).xy), uint(((_S419)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
                uint metadataRows_2 = (10U + 2U * _S377 * _S383 + atlasWidth_2 - 1U) / atlasWidth_2;
                uint _S420 = atlasWidth_2 / columns_2;
                float _S421 = float(_S420);
                uint _S422 = (atlasHeight_2 - metadataRows_2) / rows_2;
                float2 cellSize_4 = float2(_S421, float(_S422));
                uint _S423 = slot_5 % columns_2;
                float _S424 = float(_S423);
                uint _S425 = slot_5 / columns_2;
                _S183 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S424, float(_S425)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(_S182) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
                break;
            }
            int2 _S426 = int2(metadata_2.zw);
            int2 tile_5 = int2(floor(_S182)) - int2(metadata_2.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S426);
            }
            if(hasSceneLighting_0)
            {
                int3 _S427 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S183 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S427)).xy), uint(((_S427)).z)));
                break;
            }
            uint _S428 = atlasWidth_2 / uint(_S426.x);
            float _S429 = float(_S428);
            uint _S430 = (atlasHeight_2 - 1U) / uint(_S426.y);
            float2 cellSize_5 = float2(_S429, float(_S430));
            _S183 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        for(;;)
        {
            float4 _S431 = float4(_S6->compositeControls_0) ;
            if((_S431.x) != 8.0f)
            {
                break;
            }
            bool _S432 = (_S431.w) >= 0.5f;
            for(;;)
            {
                if(!_S432)
                {
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S433 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_3;
                thread uint atlasHeight_3;
                (*((&atlasWidth_3)) = (_S433).get_width(0)),(*((&atlasHeight_3)) = (_S433).get_height(0));
                int3 _S434 = int3(int(0), int(0), int(0));
                float4 metadata_3 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S434)).xy), uint(((_S434)).z))) * float4(255.0f) );
                if((metadata_3.x) == 255.0f)
                {
                    uint _S435 = atlasWidth_3;
                    uint _S436 = 2U % atlasWidth_3;
                    int _S437 = int(_S436);
                    uint _S438 = 2U / _S435;
                    int3 _S439 = int3(_S437, int(_S438), int(0));
                    int _S440 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S439)).xy), uint(((_S439)).z))).x * 255.0f)));
                    uint _S441 = atlasWidth_3;
                    uint _S442 = 3U % atlasWidth_3;
                    int _S443 = int(_S442);
                    uint _S444 = 3U / _S441;
                    int3 _S445 = int3(_S443, int(_S444), int(0));
                    int2 minimumTile_3 = int2(_S440, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S445)).xy), uint(((_S445)).z))).x * 255.0f))));
                    uint _S446 = atlasWidth_3;
                    uint _S447 = 4U % atlasWidth_3;
                    int _S448 = int(_S447);
                    uint _S449 = 4U / _S446;
                    int3 _S450 = int3(_S448, int(_S449), int(0));
                    uint _S451 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S450)).xy), uint(((_S450)).z))).x * 255.0f));
                    uint _S452 = atlasWidth_3;
                    uint _S453 = 5U % atlasWidth_3;
                    int _S454 = int(_S453);
                    uint _S455 = 5U / _S452;
                    int3 _S456 = int3(_S454, int(_S455), int(0));
                    uint _S457 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S456)).xy), uint(((_S456)).z))).x * 255.0f));
                    uint2 grid_3 = uint2(_S451, _S457);
                    int2 tile_6 = int2(floor(_S182)) - minimumTile_3;
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
                        int3 _S458 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S458)).xy), uint(((_S458)).z)));
                        break;
                    }
                    uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S451 + uint(tile_6.x));
                    uint _S459 = atlasWidth_3;
                    uint _S460 = offset_3 % atlasWidth_3;
                    int _S461 = int(_S460);
                    uint _S462 = offset_3 / _S459;
                    int3 _S463 = int3(_S461, int(_S462), int(0));
                    uint _S464 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S463)).xy), uint(((_S463)).z))).x * 255.0f));
                    uint _S465 = atlasWidth_3;
                    uint _S466 = offset_3 + 1U;
                    uint _S467 = _S466 % atlasWidth_3;
                    int _S468 = int(_S467);
                    uint _S469 = _S466 / _S465;
                    int3 _S470 = int3(_S468, int(_S469), int(0));
                    uint slot_6 = _S464 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S470)).xy), uint(((_S470)).z))).x * 255.0f)) << 8U);
                    if(slot_6 == 0U)
                    {
                        int3 _S471 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S471)).xy), uint(((_S471)).z)));
                        break;
                    }
                    uint slot_7 = slot_6 - 1U;
                    uint _S472 = atlasWidth_3;
                    uint _S473 = 6U % atlasWidth_3;
                    int _S474 = int(_S473);
                    uint _S475 = 6U / _S472;
                    int3 _S476 = int3(_S474, int(_S475), int(0));
                    uint _S477 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S476)).xy), uint(((_S476)).z))).x * 255.0f));
                    uint _S478 = atlasWidth_3;
                    uint _S479 = 7U % atlasWidth_3;
                    int _S480 = int(_S479);
                    uint _S481 = 7U / _S478;
                    int3 _S482 = int3(_S480, int(_S481), int(0));
                    uint columns_3 = _S477 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S482)).xy), uint(((_S482)).z))).x * 255.0f)) << 8U);
                    uint _S483 = atlasWidth_3;
                    uint _S484 = 8U % atlasWidth_3;
                    int _S485 = int(_S484);
                    uint _S486 = 8U / _S483;
                    int3 _S487 = int3(_S485, int(_S486), int(0));
                    uint _S488 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S487)).xy), uint(((_S487)).z))).x * 255.0f));
                    uint _S489 = atlasWidth_3;
                    uint _S490 = 9U % atlasWidth_3;
                    int _S491 = int(_S490);
                    uint _S492 = 9U / _S489;
                    int3 _S493 = int3(_S491, int(_S492), int(0));
                    uint rows_3 = ((_S488 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S493)).xy), uint(((_S493)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                    uint metadataRows_3 = (10U + 2U * _S451 * _S457 + atlasWidth_3 - 1U) / atlasWidth_3;
                    uint _S494 = atlasWidth_3 / columns_3;
                    float _S495 = float(_S494);
                    uint _S496 = (atlasHeight_3 - metadataRows_3) / rows_3;
                    float2 cellSize_6 = float2(_S495, float(_S496));
                    uint _S497 = slot_7 % columns_3;
                    float _S498 = float(_S497);
                    uint _S499 = slot_7 / columns_3;
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S498, float(_S499)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(_S182) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                    break;
                }
                int2 _S500 = int2(metadata_3.zw);
                int2 tile_7 = int2(floor(_S182)) - int2(metadata_3.xy);
                if(any(tile_7 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_7 >= _S500);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S501 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                    _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S501)).xy), uint(((_S501)).z)));
                    break;
                }
                uint _S502 = atlasWidth_3 / uint(_S500.x);
                float _S503 = float(_S502);
                uint _S504 = (atlasHeight_3 - 1U) / uint(_S500.y);
                float2 cellSize_7 = float2(_S503, float(_S504));
                _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            uint operation_1 = uint(round(_S431.y));
            if(operation_1 == 1U)
            {
                _S183 = _S183 * _S357;
                break;
            }
            if(operation_1 == 2U)
            {
                _S183 = _S183 + _S357;
                break;
            }
            if(operation_1 == 3U)
            {
                _S183 = _S183 - _S357;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S431.z;
                _S183 = _S183 * float4((1.0f - factor_1))  + _S357 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S183.x, 0.00999999977648258f, 1.0f);
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
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S505 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S505)
            {
                _S183 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S182)));
                break;
            }
            texture2d<float, access::sample> _S506 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S506).get_width(0)),(*((&atlasHeight_4)) = (_S506).get_height(0));
            int3 _S507 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S507)).xy), uint(((_S507)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S508 = atlasWidth_4;
                uint _S509 = 2U % atlasWidth_4;
                int _S510 = int(_S509);
                uint _S511 = 2U / _S508;
                int3 _S512 = int3(_S510, int(_S511), int(0));
                int _S513 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S512)).xy), uint(((_S512)).z))).x * 255.0f)));
                uint _S514 = atlasWidth_4;
                uint _S515 = 3U % atlasWidth_4;
                int _S516 = int(_S515);
                uint _S517 = 3U / _S514;
                int3 _S518 = int3(_S516, int(_S517), int(0));
                int2 minimumTile_4 = int2(_S513, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S518)).xy), uint(((_S518)).z))).x * 255.0f))));
                uint _S519 = atlasWidth_4;
                uint _S520 = 4U % atlasWidth_4;
                int _S521 = int(_S520);
                uint _S522 = 4U / _S519;
                int3 _S523 = int3(_S521, int(_S522), int(0));
                uint _S524 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S523)).xy), uint(((_S523)).z))).x * 255.0f));
                uint _S525 = atlasWidth_4;
                uint _S526 = 5U % atlasWidth_4;
                int _S527 = int(_S526);
                uint _S528 = 5U / _S525;
                int3 _S529 = int3(_S527, int(_S528), int(0));
                uint _S530 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S529)).xy), uint(((_S529)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S524, _S530);
                int2 tile_8 = int2(floor(_S182)) - minimumTile_4;
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
                    int3 _S531 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S531)).xy), uint(((_S531)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S524 + uint(tile_8.x));
                uint _S532 = atlasWidth_4;
                uint _S533 = offset_4 % atlasWidth_4;
                int _S534 = int(_S533);
                uint _S535 = offset_4 / _S532;
                int3 _S536 = int3(_S534, int(_S535), int(0));
                uint _S537 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S536)).xy), uint(((_S536)).z))).x * 255.0f));
                uint _S538 = atlasWidth_4;
                uint _S539 = offset_4 + 1U;
                uint _S540 = _S539 % atlasWidth_4;
                int _S541 = int(_S540);
                uint _S542 = _S539 / _S538;
                int3 _S543 = int3(_S541, int(_S542), int(0));
                uint slot_8 = _S537 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S543)).xy), uint(((_S543)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S544 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S544)).xy), uint(((_S544)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S545 = atlasWidth_4;
                uint _S546 = 6U % atlasWidth_4;
                int _S547 = int(_S546);
                uint _S548 = 6U / _S545;
                int3 _S549 = int3(_S547, int(_S548), int(0));
                uint _S550 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S549)).xy), uint(((_S549)).z))).x * 255.0f));
                uint _S551 = atlasWidth_4;
                uint _S552 = 7U % atlasWidth_4;
                int _S553 = int(_S552);
                uint _S554 = 7U / _S551;
                int3 _S555 = int3(_S553, int(_S554), int(0));
                uint columns_4 = _S550 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S555)).xy), uint(((_S555)).z))).x * 255.0f)) << 8U);
                uint _S556 = atlasWidth_4;
                uint _S557 = 8U % atlasWidth_4;
                int _S558 = int(_S557);
                uint _S559 = 8U / _S556;
                int3 _S560 = int3(_S558, int(_S559), int(0));
                uint _S561 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S560)).xy), uint(((_S560)).z))).x * 255.0f));
                uint _S562 = atlasWidth_4;
                uint _S563 = 9U % atlasWidth_4;
                int _S564 = int(_S563);
                uint _S565 = 9U / _S562;
                int3 _S566 = int3(_S564, int(_S565), int(0));
                uint rows_4 = ((_S561 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S566)).xy), uint(((_S566)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S524 * _S530 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S567 = atlasWidth_4 / columns_4;
                float _S568 = float(_S567);
                uint _S569 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S568, float(_S569));
                uint _S570 = slot_9 % columns_4;
                float _S571 = float(_S570);
                uint _S572 = slot_9 / columns_4;
                _S183 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S571, float(_S572)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(_S182) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S573 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(_S182)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S573);
            }
            if(hasSceneLighting_0)
            {
                int3 _S574 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S183 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S574)).xy), uint(((_S574)).z)));
                break;
            }
            uint _S575 = atlasWidth_4 / uint(_S573.x);
            float _S576 = float(_S575);
            uint _S577 = (atlasHeight_4 - 1U) / uint(_S573.y);
            float2 cellSize_9 = float2(_S576, float(_S577));
            _S183 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S578 = float4(_S6->compositeControls_0) ;
            if((_S578.x) != 32.0f)
            {
                break;
            }
            bool _S579 = (_S578.w) >= 0.5f;
            for(;;)
            {
                if(!_S579)
                {
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S580 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S580).get_width(0)),(*((&atlasHeight_5)) = (_S580).get_height(0));
                int3 _S581 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S581)).xy), uint(((_S581)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S582 = atlasWidth_5;
                    uint _S583 = 2U % atlasWidth_5;
                    int _S584 = int(_S583);
                    uint _S585 = 2U / _S582;
                    int3 _S586 = int3(_S584, int(_S585), int(0));
                    int _S587 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S586)).xy), uint(((_S586)).z))).x * 255.0f)));
                    uint _S588 = atlasWidth_5;
                    uint _S589 = 3U % atlasWidth_5;
                    int _S590 = int(_S589);
                    uint _S591 = 3U / _S588;
                    int3 _S592 = int3(_S590, int(_S591), int(0));
                    int2 minimumTile_5 = int2(_S587, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S592)).xy), uint(((_S592)).z))).x * 255.0f))));
                    uint _S593 = atlasWidth_5;
                    uint _S594 = 4U % atlasWidth_5;
                    int _S595 = int(_S594);
                    uint _S596 = 4U / _S593;
                    int3 _S597 = int3(_S595, int(_S596), int(0));
                    uint _S598 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S597)).xy), uint(((_S597)).z))).x * 255.0f));
                    uint _S599 = atlasWidth_5;
                    uint _S600 = 5U % atlasWidth_5;
                    int _S601 = int(_S600);
                    uint _S602 = 5U / _S599;
                    int3 _S603 = int3(_S601, int(_S602), int(0));
                    uint _S604 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S603)).xy), uint(((_S603)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S598, _S604);
                    int2 tile_10 = int2(floor(_S182)) - minimumTile_5;
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
                        int3 _S605 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S605)).xy), uint(((_S605)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S598 + uint(tile_10.x));
                    uint _S606 = atlasWidth_5;
                    uint _S607 = offset_5 % atlasWidth_5;
                    int _S608 = int(_S607);
                    uint _S609 = offset_5 / _S606;
                    int3 _S610 = int3(_S608, int(_S609), int(0));
                    uint _S611 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S610)).xy), uint(((_S610)).z))).x * 255.0f));
                    uint _S612 = atlasWidth_5;
                    uint _S613 = offset_5 + 1U;
                    uint _S614 = _S613 % atlasWidth_5;
                    int _S615 = int(_S614);
                    uint _S616 = _S613 / _S612;
                    int3 _S617 = int3(_S615, int(_S616), int(0));
                    uint slot_10 = _S611 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S617)).xy), uint(((_S617)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S618 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S618)).xy), uint(((_S618)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S619 = atlasWidth_5;
                    uint _S620 = 6U % atlasWidth_5;
                    int _S621 = int(_S620);
                    uint _S622 = 6U / _S619;
                    int3 _S623 = int3(_S621, int(_S622), int(0));
                    uint _S624 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S623)).xy), uint(((_S623)).z))).x * 255.0f));
                    uint _S625 = atlasWidth_5;
                    uint _S626 = 7U % atlasWidth_5;
                    int _S627 = int(_S626);
                    uint _S628 = 7U / _S625;
                    int3 _S629 = int3(_S627, int(_S628), int(0));
                    uint columns_5 = _S624 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S629)).xy), uint(((_S629)).z))).x * 255.0f)) << 8U);
                    uint _S630 = atlasWidth_5;
                    uint _S631 = 8U % atlasWidth_5;
                    int _S632 = int(_S631);
                    uint _S633 = 8U / _S630;
                    int3 _S634 = int3(_S632, int(_S633), int(0));
                    uint _S635 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S634)).xy), uint(((_S634)).z))).x * 255.0f));
                    uint _S636 = atlasWidth_5;
                    uint _S637 = 9U % atlasWidth_5;
                    int _S638 = int(_S637);
                    uint _S639 = 9U / _S636;
                    int3 _S640 = int3(_S638, int(_S639), int(0));
                    uint rows_5 = ((_S635 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S640)).xy), uint(((_S640)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S598 * _S604 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S641 = atlasWidth_5 / columns_5;
                    float _S642 = float(_S641);
                    uint _S643 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S642, float(_S643));
                    uint _S644 = slot_11 % columns_5;
                    float _S645 = float(_S644);
                    uint _S646 = slot_11 / columns_5;
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S645, float(_S646)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(_S182) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S647 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(_S182)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S647);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S648 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S648)).xy), uint(((_S648)).z)));
                    break;
                }
                uint _S649 = atlasWidth_5 / uint(_S647.x);
                float _S650 = float(_S649);
                uint _S651 = (atlasHeight_5 - 1U) / uint(_S647.y);
                float2 cellSize_11 = float2(_S650, float(_S651));
                _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_2 = uint(round(_S578.y));
            if(operation_2 == 1U)
            {
                _S183 = _S183 * _S357;
                break;
            }
            if(operation_2 == 2U)
            {
                _S183 = _S183 + _S357;
                break;
            }
            if(operation_2 == 3U)
            {
                _S183 = _S183 - _S357;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S578.z;
                _S183 = _S183 * float4((1.0f - factor_2))  + _S357 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S183.x);
    }
    else
    {
        metallic_0 = _S166;
    }
    if((textureMask_0 & 16U) != 0U)
    {
        if((secondaryMask_0 & 16U) != 0U)
        {
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        thread float4 emissionSample_0;
        bool _S652 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S652)
            {
                _S183 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S182)));
                break;
            }
            texture2d<float, access::sample> _S653 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S653).get_width(0)),(*((&atlasHeight_6)) = (_S653).get_height(0));
            int3 _S654 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S654)).xy), uint(((_S654)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S655 = atlasWidth_6;
                uint _S656 = 2U % atlasWidth_6;
                int _S657 = int(_S656);
                uint _S658 = 2U / _S655;
                int3 _S659 = int3(_S657, int(_S658), int(0));
                int _S660 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S659)).xy), uint(((_S659)).z))).x * 255.0f)));
                uint _S661 = atlasWidth_6;
                uint _S662 = 3U % atlasWidth_6;
                int _S663 = int(_S662);
                uint _S664 = 3U / _S661;
                int3 _S665 = int3(_S663, int(_S664), int(0));
                int2 minimumTile_6 = int2(_S660, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S665)).xy), uint(((_S665)).z))).x * 255.0f))));
                uint _S666 = atlasWidth_6;
                uint _S667 = 4U % atlasWidth_6;
                int _S668 = int(_S667);
                uint _S669 = 4U / _S666;
                int3 _S670 = int3(_S668, int(_S669), int(0));
                uint _S671 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S670)).xy), uint(((_S670)).z))).x * 255.0f));
                uint _S672 = atlasWidth_6;
                uint _S673 = 5U % atlasWidth_6;
                int _S674 = int(_S673);
                uint _S675 = 5U / _S672;
                int3 _S676 = int3(_S674, int(_S675), int(0));
                uint _S677 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S676)).xy), uint(((_S676)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S671, _S677);
                int2 tile_12 = int2(floor(_S182)) - minimumTile_6;
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
                    int3 _S678 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S678)).xy), uint(((_S678)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S671 + uint(tile_12.x));
                uint _S679 = atlasWidth_6;
                uint _S680 = offset_6 % atlasWidth_6;
                int _S681 = int(_S680);
                uint _S682 = offset_6 / _S679;
                int3 _S683 = int3(_S681, int(_S682), int(0));
                uint _S684 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S683)).xy), uint(((_S683)).z))).x * 255.0f));
                uint _S685 = atlasWidth_6;
                uint _S686 = offset_6 + 1U;
                uint _S687 = _S686 % atlasWidth_6;
                int _S688 = int(_S687);
                uint _S689 = _S686 / _S685;
                int3 _S690 = int3(_S688, int(_S689), int(0));
                uint slot_12 = _S684 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S690)).xy), uint(((_S690)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S691 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S691)).xy), uint(((_S691)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S692 = atlasWidth_6;
                uint _S693 = 6U % atlasWidth_6;
                int _S694 = int(_S693);
                uint _S695 = 6U / _S692;
                int3 _S696 = int3(_S694, int(_S695), int(0));
                uint _S697 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S696)).xy), uint(((_S696)).z))).x * 255.0f));
                uint _S698 = atlasWidth_6;
                uint _S699 = 7U % atlasWidth_6;
                int _S700 = int(_S699);
                uint _S701 = 7U / _S698;
                int3 _S702 = int3(_S700, int(_S701), int(0));
                uint columns_6 = _S697 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S702)).xy), uint(((_S702)).z))).x * 255.0f)) << 8U);
                uint _S703 = atlasWidth_6;
                uint _S704 = 8U % atlasWidth_6;
                int _S705 = int(_S704);
                uint _S706 = 8U / _S703;
                int3 _S707 = int3(_S705, int(_S706), int(0));
                uint _S708 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S707)).xy), uint(((_S707)).z))).x * 255.0f));
                uint _S709 = atlasWidth_6;
                uint _S710 = 9U % atlasWidth_6;
                int _S711 = int(_S710);
                uint _S712 = 9U / _S709;
                int3 _S713 = int3(_S711, int(_S712), int(0));
                uint rows_6 = ((_S708 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S713)).xy), uint(((_S713)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S671 * _S677 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S714 = atlasWidth_6 / columns_6;
                float _S715 = float(_S714);
                uint _S716 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S715, float(_S716));
                uint _S717 = slot_13 % columns_6;
                float _S718 = float(_S717);
                uint _S719 = slot_13 / columns_6;
                _S183 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S718, float(_S719)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(_S182) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S720 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(_S182)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S720);
            }
            if(hasSceneLighting_0)
            {
                int3 _S721 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S183 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S721)).xy), uint(((_S721)).z)));
                break;
            }
            uint _S722 = atlasWidth_6 / uint(_S720.x);
            float _S723 = float(_S722);
            uint _S724 = (atlasHeight_6 - 1U) / uint(_S720.y);
            float2 cellSize_13 = float2(_S723, float(_S724));
            _S183 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        emissionSample_0 = _S183;
        if((_S174.w) != 0.0f)
        {
            texture2d<float, access::sample> _S725 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S725).get_width(0)),(*((&sourceHeight_1)) = (_S725).get_height(0));
            texture2d<float, access::sample> _S726 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S726).get_width(0)),(*((&valueHeight_1)) = (_S726).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S727 = emissionSample_0;
                for(;;)
                {
                    float _S728 = _S727.w;
                    if(_S728 > 1.0f)
                    {
                        unlitColor_0 = _S727.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S729 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S729).get_width(0)),(*((&height_1)) = (_S729).get_height(0));
                    uint3 _S730 = uint3(round(_S727.xyz * float3(255.0f) ));
                    uint index_2 = ((_S730.x) | ((_S730.y) << 8U)) | ((_S730.z) << 16U);
                    if(_S728 == 0.0f)
                    {
                        _S184 = _S730;
                    }
                    else
                    {
                        _S184 = uint3(index_2, index_2, index_2);
                    }
                    uint3 address_1 = _S184 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S731 = address_1.x;
                    uint _S732 = _S731 % width_1;
                    int _S733 = int(_S732);
                    uint _S734 = _S731 / width_1;
                    int3 _S735 = int3(_S733, int(_S734), int(0));
                    float _S736 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S735)).xy), uint(((_S735)).z))).x;
                    uint _S737 = address_1.y;
                    uint _S738 = _S737 % width_1;
                    int _S739 = int(_S738);
                    uint _S740 = _S737 / width_1;
                    int3 _S741 = int3(_S739, int(_S740), int(0));
                    float _S742 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S741)).xy), uint(((_S741)).z))).x;
                    uint _S743 = address_1.z;
                    uint _S744 = _S743 % width_1;
                    int _S745 = int(_S744);
                    uint _S746 = _S743 / width_1;
                    int3 _S747 = int3(_S745, int(_S746), int(0));
                    unlitColor_0 = float3(_S736, _S742, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S747)).xy), uint(((_S747)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                emissionSample_0.z = (((&kernelContext_0)->emissiveValuesTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S182))).x;
            }
        }
        float4 _S748 = emissionSample_0;
        for(;;)
        {
            float4 _S749 = float4(_S6->compositeControls_0) ;
            if((_S749.x) != 16.0f)
            {
                _S183 = _S748;
                break;
            }
            bool _S750 = (_S749.w) >= 0.5f;
            for(;;)
            {
                if(!_S750)
                {
                    _S183 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S751 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S751).get_width(0)),(*((&atlasHeight_7)) = (_S751).get_height(0));
                int3 _S752 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S752)).xy), uint(((_S752)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S753 = atlasWidth_7;
                    uint _S754 = 2U % atlasWidth_7;
                    int _S755 = int(_S754);
                    uint _S756 = 2U / _S753;
                    int3 _S757 = int3(_S755, int(_S756), int(0));
                    int _S758 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S757)).xy), uint(((_S757)).z))).x * 255.0f)));
                    uint _S759 = atlasWidth_7;
                    uint _S760 = 3U % atlasWidth_7;
                    int _S761 = int(_S760);
                    uint _S762 = 3U / _S759;
                    int3 _S763 = int3(_S761, int(_S762), int(0));
                    int2 minimumTile_7 = int2(_S758, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S763)).xy), uint(((_S763)).z))).x * 255.0f))));
                    uint _S764 = atlasWidth_7;
                    uint _S765 = 4U % atlasWidth_7;
                    int _S766 = int(_S765);
                    uint _S767 = 4U / _S764;
                    int3 _S768 = int3(_S766, int(_S767), int(0));
                    uint _S769 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S768)).xy), uint(((_S768)).z))).x * 255.0f));
                    uint _S770 = atlasWidth_7;
                    uint _S771 = 5U % atlasWidth_7;
                    int _S772 = int(_S771);
                    uint _S773 = 5U / _S770;
                    int3 _S774 = int3(_S772, int(_S773), int(0));
                    uint _S775 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S774)).xy), uint(((_S774)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S769, _S775);
                    int2 tile_14 = int2(floor(_S182)) - minimumTile_7;
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
                        int3 _S776 = int3(int(1), int(0), int(0));
                        _S183 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S776)).xy), uint(((_S776)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S769 + uint(tile_14.x));
                    uint _S777 = atlasWidth_7;
                    uint _S778 = offset_7 % atlasWidth_7;
                    int _S779 = int(_S778);
                    uint _S780 = offset_7 / _S777;
                    int3 _S781 = int3(_S779, int(_S780), int(0));
                    uint _S782 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S781)).xy), uint(((_S781)).z))).x * 255.0f));
                    uint _S783 = atlasWidth_7;
                    uint _S784 = offset_7 + 1U;
                    uint _S785 = _S784 % atlasWidth_7;
                    int _S786 = int(_S785);
                    uint _S787 = _S784 / _S783;
                    int3 _S788 = int3(_S786, int(_S787), int(0));
                    uint slot_14 = _S782 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S788)).xy), uint(((_S788)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S789 = int3(int(1), int(0), int(0));
                        _S183 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S789)).xy), uint(((_S789)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S790 = atlasWidth_7;
                    uint _S791 = 6U % atlasWidth_7;
                    int _S792 = int(_S791);
                    uint _S793 = 6U / _S790;
                    int3 _S794 = int3(_S792, int(_S793), int(0));
                    uint _S795 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S794)).xy), uint(((_S794)).z))).x * 255.0f));
                    uint _S796 = atlasWidth_7;
                    uint _S797 = 7U % atlasWidth_7;
                    int _S798 = int(_S797);
                    uint _S799 = 7U / _S796;
                    int3 _S800 = int3(_S798, int(_S799), int(0));
                    uint columns_7 = _S795 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S800)).xy), uint(((_S800)).z))).x * 255.0f)) << 8U);
                    uint _S801 = atlasWidth_7;
                    uint _S802 = 8U % atlasWidth_7;
                    int _S803 = int(_S802);
                    uint _S804 = 8U / _S801;
                    int3 _S805 = int3(_S803, int(_S804), int(0));
                    uint _S806 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S805)).xy), uint(((_S805)).z))).x * 255.0f));
                    uint _S807 = atlasWidth_7;
                    uint _S808 = 9U % atlasWidth_7;
                    int _S809 = int(_S808);
                    uint _S810 = 9U / _S807;
                    int3 _S811 = int3(_S809, int(_S810), int(0));
                    uint rows_7 = ((_S806 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S811)).xy), uint(((_S811)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S769 * _S775 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S812 = atlasWidth_7 / columns_7;
                    float _S813 = float(_S812);
                    uint _S814 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S813, float(_S814));
                    uint _S815 = slot_15 % columns_7;
                    float _S816 = float(_S815);
                    uint _S817 = slot_15 / columns_7;
                    _S183 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S816, float(_S817)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(_S182) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S818 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(_S182)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S818);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S819 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S819)).xy), uint(((_S819)).z)));
                    break;
                }
                uint _S820 = atlasWidth_7 / uint(_S818.x);
                float _S821 = float(_S820);
                uint _S822 = (atlasHeight_7 - 1U) / uint(_S818.y);
                float2 cellSize_15 = float2(_S821, float(_S822));
                _S183 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_3 = uint(round(_S749.y));
            if(operation_3 == 1U)
            {
                _S183 = _S748 * _S183;
                break;
            }
            if(operation_3 == 2U)
            {
                _S183 = _S748 + _S183;
                break;
            }
            if(operation_3 == 3U)
            {
                _S183 = _S748 - _S183;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S749.z;
                _S183 = _S748 * float4((1.0f - factor_3))  + _S183 * float4(factor_3) ;
                break;
            }
            _S183 = _S748;
            break;
        }
        unlitColor_0 = _S183.xyz;
    }
    else
    {
        unlitColor_0 = emissiveColor_0;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        if((secondaryMask_0 & 64U) != 0U)
        {
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S823 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S823)
            {
                _S183 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S182)));
                break;
            }
            texture2d<float, access::sample> _S824 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S824).get_width(0)),(*((&atlasHeight_8)) = (_S824).get_height(0));
            int3 _S825 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S825)).xy), uint(((_S825)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S826 = atlasWidth_8;
                uint _S827 = 2U % atlasWidth_8;
                int _S828 = int(_S827);
                uint _S829 = 2U / _S826;
                int3 _S830 = int3(_S828, int(_S829), int(0));
                int _S831 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S830)).xy), uint(((_S830)).z))).x * 255.0f)));
                uint _S832 = atlasWidth_8;
                uint _S833 = 3U % atlasWidth_8;
                int _S834 = int(_S833);
                uint _S835 = 3U / _S832;
                int3 _S836 = int3(_S834, int(_S835), int(0));
                int2 minimumTile_8 = int2(_S831, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S836)).xy), uint(((_S836)).z))).x * 255.0f))));
                uint _S837 = atlasWidth_8;
                uint _S838 = 4U % atlasWidth_8;
                int _S839 = int(_S838);
                uint _S840 = 4U / _S837;
                int3 _S841 = int3(_S839, int(_S840), int(0));
                uint _S842 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S841)).xy), uint(((_S841)).z))).x * 255.0f));
                uint _S843 = atlasWidth_8;
                uint _S844 = 5U % atlasWidth_8;
                int _S845 = int(_S844);
                uint _S846 = 5U / _S843;
                int3 _S847 = int3(_S845, int(_S846), int(0));
                uint _S848 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S847)).xy), uint(((_S847)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S842, _S848);
                int2 tile_16 = int2(floor(_S182)) - minimumTile_8;
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
                    int3 _S849 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S849)).xy), uint(((_S849)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S842 + uint(tile_16.x));
                uint _S850 = atlasWidth_8;
                uint _S851 = offset_8 % atlasWidth_8;
                int _S852 = int(_S851);
                uint _S853 = offset_8 / _S850;
                int3 _S854 = int3(_S852, int(_S853), int(0));
                uint _S855 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S854)).xy), uint(((_S854)).z))).x * 255.0f));
                uint _S856 = atlasWidth_8;
                uint _S857 = offset_8 + 1U;
                uint _S858 = _S857 % atlasWidth_8;
                int _S859 = int(_S858);
                uint _S860 = _S857 / _S856;
                int3 _S861 = int3(_S859, int(_S860), int(0));
                uint slot_16 = _S855 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S861)).xy), uint(((_S861)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S862 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S862)).xy), uint(((_S862)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S863 = atlasWidth_8;
                uint _S864 = 6U % atlasWidth_8;
                int _S865 = int(_S864);
                uint _S866 = 6U / _S863;
                int3 _S867 = int3(_S865, int(_S866), int(0));
                uint _S868 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S867)).xy), uint(((_S867)).z))).x * 255.0f));
                uint _S869 = atlasWidth_8;
                uint _S870 = 7U % atlasWidth_8;
                int _S871 = int(_S870);
                uint _S872 = 7U / _S869;
                int3 _S873 = int3(_S871, int(_S872), int(0));
                uint columns_8 = _S868 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S873)).xy), uint(((_S873)).z))).x * 255.0f)) << 8U);
                uint _S874 = atlasWidth_8;
                uint _S875 = 8U % atlasWidth_8;
                int _S876 = int(_S875);
                uint _S877 = 8U / _S874;
                int3 _S878 = int3(_S876, int(_S877), int(0));
                uint _S879 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S878)).xy), uint(((_S878)).z))).x * 255.0f));
                uint _S880 = atlasWidth_8;
                uint _S881 = 9U % atlasWidth_8;
                int _S882 = int(_S881);
                uint _S883 = 9U / _S880;
                int3 _S884 = int3(_S882, int(_S883), int(0));
                uint rows_8 = ((_S879 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S884)).xy), uint(((_S884)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S842 * _S848 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S885 = atlasWidth_8 / columns_8;
                float _S886 = float(_S885);
                uint _S887 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S886, float(_S887));
                uint _S888 = slot_17 % columns_8;
                float _S889 = float(_S888);
                uint _S890 = slot_17 / columns_8;
                _S183 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S889, float(_S890)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(_S182) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S891 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(_S182)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S891);
            }
            if(hasSceneLighting_0)
            {
                int3 _S892 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S183 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S892)).xy), uint(((_S892)).z)));
                break;
            }
            uint _S893 = atlasWidth_8 / uint(_S891.x);
            float _S894 = float(_S893);
            uint _S895 = (atlasHeight_8 - 1U) / uint(_S891.y);
            float2 cellSize_17 = float2(_S894, float(_S895));
            _S183 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        for(;;)
        {
            float4 _S896 = float4(_S6->compositeControls_0) ;
            if((_S896.x) != 64.0f)
            {
                break;
            }
            bool _S897 = (_S896.w) >= 0.5f;
            for(;;)
            {
                if(!_S897)
                {
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S898 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S898).get_width(0)),(*((&atlasHeight_9)) = (_S898).get_height(0));
                int3 _S899 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S899)).xy), uint(((_S899)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S900 = atlasWidth_9;
                    uint _S901 = 2U % atlasWidth_9;
                    int _S902 = int(_S901);
                    uint _S903 = 2U / _S900;
                    int3 _S904 = int3(_S902, int(_S903), int(0));
                    int _S905 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S904)).xy), uint(((_S904)).z))).x * 255.0f)));
                    uint _S906 = atlasWidth_9;
                    uint _S907 = 3U % atlasWidth_9;
                    int _S908 = int(_S907);
                    uint _S909 = 3U / _S906;
                    int3 _S910 = int3(_S908, int(_S909), int(0));
                    int2 minimumTile_9 = int2(_S905, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S910)).xy), uint(((_S910)).z))).x * 255.0f))));
                    uint _S911 = atlasWidth_9;
                    uint _S912 = 4U % atlasWidth_9;
                    int _S913 = int(_S912);
                    uint _S914 = 4U / _S911;
                    int3 _S915 = int3(_S913, int(_S914), int(0));
                    uint _S916 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S915)).xy), uint(((_S915)).z))).x * 255.0f));
                    uint _S917 = atlasWidth_9;
                    uint _S918 = 5U % atlasWidth_9;
                    int _S919 = int(_S918);
                    uint _S920 = 5U / _S917;
                    int3 _S921 = int3(_S919, int(_S920), int(0));
                    uint _S922 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S921)).xy), uint(((_S921)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S916, _S922);
                    int2 tile_18 = int2(floor(_S182)) - minimumTile_9;
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
                        int3 _S923 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S923)).xy), uint(((_S923)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S916 + uint(tile_18.x));
                    uint _S924 = atlasWidth_9;
                    uint _S925 = offset_9 % atlasWidth_9;
                    int _S926 = int(_S925);
                    uint _S927 = offset_9 / _S924;
                    int3 _S928 = int3(_S926, int(_S927), int(0));
                    uint _S929 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S928)).xy), uint(((_S928)).z))).x * 255.0f));
                    uint _S930 = atlasWidth_9;
                    uint _S931 = offset_9 + 1U;
                    uint _S932 = _S931 % atlasWidth_9;
                    int _S933 = int(_S932);
                    uint _S934 = _S931 / _S930;
                    int3 _S935 = int3(_S933, int(_S934), int(0));
                    uint slot_18 = _S929 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S935)).xy), uint(((_S935)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S936 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S936)).xy), uint(((_S936)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S937 = atlasWidth_9;
                    uint _S938 = 6U % atlasWidth_9;
                    int _S939 = int(_S938);
                    uint _S940 = 6U / _S937;
                    int3 _S941 = int3(_S939, int(_S940), int(0));
                    uint _S942 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S941)).xy), uint(((_S941)).z))).x * 255.0f));
                    uint _S943 = atlasWidth_9;
                    uint _S944 = 7U % atlasWidth_9;
                    int _S945 = int(_S944);
                    uint _S946 = 7U / _S943;
                    int3 _S947 = int3(_S945, int(_S946), int(0));
                    uint columns_9 = _S942 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S947)).xy), uint(((_S947)).z))).x * 255.0f)) << 8U);
                    uint _S948 = atlasWidth_9;
                    uint _S949 = 8U % atlasWidth_9;
                    int _S950 = int(_S949);
                    uint _S951 = 8U / _S948;
                    int3 _S952 = int3(_S950, int(_S951), int(0));
                    uint _S953 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S952)).xy), uint(((_S952)).z))).x * 255.0f));
                    uint _S954 = atlasWidth_9;
                    uint _S955 = 9U % atlasWidth_9;
                    int _S956 = int(_S955);
                    uint _S957 = 9U / _S954;
                    int3 _S958 = int3(_S956, int(_S957), int(0));
                    uint rows_9 = ((_S953 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S958)).xy), uint(((_S958)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S916 * _S922 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S959 = atlasWidth_9 / columns_9;
                    float _S960 = float(_S959);
                    uint _S961 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S960, float(_S961));
                    uint _S962 = slot_19 % columns_9;
                    float _S963 = float(_S962);
                    uint _S964 = slot_19 / columns_9;
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S963, float(_S964)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(_S182) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S965 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(_S182)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S965);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S966 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S966)).xy), uint(((_S966)).z)));
                    break;
                }
                uint _S967 = atlasWidth_9 / uint(_S965.x);
                float _S968 = float(_S967);
                uint _S969 = (atlasHeight_9 - 1U) / uint(_S965.y);
                float2 cellSize_19 = float2(_S968, float(_S969));
                _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_4 = uint(round(_S896.y));
            if(operation_4 == 1U)
            {
                _S183 = _S183 * _S357;
                break;
            }
            if(operation_4 == 2U)
            {
                _S183 = _S183 + _S357;
                break;
            }
            if(operation_4 == 3U)
            {
                _S183 = _S183 - _S357;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S896.z;
                _S183 = _S183 * float4((1.0f - factor_4))  + _S357 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S183.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        if((secondaryMask_0 & 128U) != 0U)
        {
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S970 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S970)
            {
                _S183 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S182)));
                break;
            }
            texture2d<float, access::sample> _S971 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S971).get_width(0)),(*((&atlasHeight_10)) = (_S971).get_height(0));
            int3 _S972 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S972)).xy), uint(((_S972)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S973 = atlasWidth_10;
                uint _S974 = 2U % atlasWidth_10;
                int _S975 = int(_S974);
                uint _S976 = 2U / _S973;
                int3 _S977 = int3(_S975, int(_S976), int(0));
                int _S978 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S977)).xy), uint(((_S977)).z))).x * 255.0f)));
                uint _S979 = atlasWidth_10;
                uint _S980 = 3U % atlasWidth_10;
                int _S981 = int(_S980);
                uint _S982 = 3U / _S979;
                int3 _S983 = int3(_S981, int(_S982), int(0));
                int2 minimumTile_10 = int2(_S978, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S983)).xy), uint(((_S983)).z))).x * 255.0f))));
                uint _S984 = atlasWidth_10;
                uint _S985 = 4U % atlasWidth_10;
                int _S986 = int(_S985);
                uint _S987 = 4U / _S984;
                int3 _S988 = int3(_S986, int(_S987), int(0));
                uint _S989 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S988)).xy), uint(((_S988)).z))).x * 255.0f));
                uint _S990 = atlasWidth_10;
                uint _S991 = 5U % atlasWidth_10;
                int _S992 = int(_S991);
                uint _S993 = 5U / _S990;
                int3 _S994 = int3(_S992, int(_S993), int(0));
                uint _S995 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S994)).xy), uint(((_S994)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S989, _S995);
                int2 tile_20 = int2(floor(_S182)) - minimumTile_10;
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
                    int3 _S996 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S996)).xy), uint(((_S996)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S989 + uint(tile_20.x));
                uint _S997 = atlasWidth_10;
                uint _S998 = offset_10 % atlasWidth_10;
                int _S999 = int(_S998);
                uint _S1000 = offset_10 / _S997;
                int3 _S1001 = int3(_S999, int(_S1000), int(0));
                uint _S1002 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1001)).xy), uint(((_S1001)).z))).x * 255.0f));
                uint _S1003 = atlasWidth_10;
                uint _S1004 = offset_10 + 1U;
                uint _S1005 = _S1004 % atlasWidth_10;
                int _S1006 = int(_S1005);
                uint _S1007 = _S1004 / _S1003;
                int3 _S1008 = int3(_S1006, int(_S1007), int(0));
                uint slot_20 = _S1002 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1008)).xy), uint(((_S1008)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1009 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1009)).xy), uint(((_S1009)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1010 = atlasWidth_10;
                uint _S1011 = 6U % atlasWidth_10;
                int _S1012 = int(_S1011);
                uint _S1013 = 6U / _S1010;
                int3 _S1014 = int3(_S1012, int(_S1013), int(0));
                uint _S1015 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1014)).xy), uint(((_S1014)).z))).x * 255.0f));
                uint _S1016 = atlasWidth_10;
                uint _S1017 = 7U % atlasWidth_10;
                int _S1018 = int(_S1017);
                uint _S1019 = 7U / _S1016;
                int3 _S1020 = int3(_S1018, int(_S1019), int(0));
                uint columns_10 = _S1015 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1020)).xy), uint(((_S1020)).z))).x * 255.0f)) << 8U);
                uint _S1021 = atlasWidth_10;
                uint _S1022 = 8U % atlasWidth_10;
                int _S1023 = int(_S1022);
                uint _S1024 = 8U / _S1021;
                int3 _S1025 = int3(_S1023, int(_S1024), int(0));
                uint _S1026 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1025)).xy), uint(((_S1025)).z))).x * 255.0f));
                uint _S1027 = atlasWidth_10;
                uint _S1028 = 9U % atlasWidth_10;
                int _S1029 = int(_S1028);
                uint _S1030 = 9U / _S1027;
                int3 _S1031 = int3(_S1029, int(_S1030), int(0));
                uint rows_10 = ((_S1026 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1031)).xy), uint(((_S1031)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S989 * _S995 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1032 = atlasWidth_10 / columns_10;
                float _S1033 = float(_S1032);
                uint _S1034 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1033, float(_S1034));
                uint _S1035 = slot_21 % columns_10;
                float _S1036 = float(_S1035);
                uint _S1037 = slot_21 / columns_10;
                _S183 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1036, float(_S1037)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(_S182) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1038 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(_S182)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1038);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1039 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S183 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1039)).xy), uint(((_S1039)).z)));
                break;
            }
            uint _S1040 = atlasWidth_10 / uint(_S1038.x);
            float _S1041 = float(_S1040);
            uint _S1042 = (atlasHeight_10 - 1U) / uint(_S1038.y);
            float2 cellSize_21 = float2(_S1041, float(_S1042));
            _S183 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1043 = float4(_S6->compositeControls_0) ;
            if((_S1043.x) != 128.0f)
            {
                break;
            }
            bool _S1044 = (_S1043.w) >= 0.5f;
            for(;;)
            {
                if(!_S1044)
                {
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S1045 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1045).get_width(0)),(*((&atlasHeight_11)) = (_S1045).get_height(0));
                int3 _S1046 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1046)).xy), uint(((_S1046)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1047 = atlasWidth_11;
                    uint _S1048 = 2U % atlasWidth_11;
                    int _S1049 = int(_S1048);
                    uint _S1050 = 2U / _S1047;
                    int3 _S1051 = int3(_S1049, int(_S1050), int(0));
                    int _S1052 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1051)).xy), uint(((_S1051)).z))).x * 255.0f)));
                    uint _S1053 = atlasWidth_11;
                    uint _S1054 = 3U % atlasWidth_11;
                    int _S1055 = int(_S1054);
                    uint _S1056 = 3U / _S1053;
                    int3 _S1057 = int3(_S1055, int(_S1056), int(0));
                    int2 minimumTile_11 = int2(_S1052, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1057)).xy), uint(((_S1057)).z))).x * 255.0f))));
                    uint _S1058 = atlasWidth_11;
                    uint _S1059 = 4U % atlasWidth_11;
                    int _S1060 = int(_S1059);
                    uint _S1061 = 4U / _S1058;
                    int3 _S1062 = int3(_S1060, int(_S1061), int(0));
                    uint _S1063 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1062)).xy), uint(((_S1062)).z))).x * 255.0f));
                    uint _S1064 = atlasWidth_11;
                    uint _S1065 = 5U % atlasWidth_11;
                    int _S1066 = int(_S1065);
                    uint _S1067 = 5U / _S1064;
                    int3 _S1068 = int3(_S1066, int(_S1067), int(0));
                    uint _S1069 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1068)).xy), uint(((_S1068)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1063, _S1069);
                    int2 tile_22 = int2(floor(_S182)) - minimumTile_11;
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
                        int3 _S1070 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1070)).xy), uint(((_S1070)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1063 + uint(tile_22.x));
                    uint _S1071 = atlasWidth_11;
                    uint _S1072 = offset_11 % atlasWidth_11;
                    int _S1073 = int(_S1072);
                    uint _S1074 = offset_11 / _S1071;
                    int3 _S1075 = int3(_S1073, int(_S1074), int(0));
                    uint _S1076 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1075)).xy), uint(((_S1075)).z))).x * 255.0f));
                    uint _S1077 = atlasWidth_11;
                    uint _S1078 = offset_11 + 1U;
                    uint _S1079 = _S1078 % atlasWidth_11;
                    int _S1080 = int(_S1079);
                    uint _S1081 = _S1078 / _S1077;
                    int3 _S1082 = int3(_S1080, int(_S1081), int(0));
                    uint slot_22 = _S1076 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1082)).xy), uint(((_S1082)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1083 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1083)).xy), uint(((_S1083)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1084 = atlasWidth_11;
                    uint _S1085 = 6U % atlasWidth_11;
                    int _S1086 = int(_S1085);
                    uint _S1087 = 6U / _S1084;
                    int3 _S1088 = int3(_S1086, int(_S1087), int(0));
                    uint _S1089 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1088)).xy), uint(((_S1088)).z))).x * 255.0f));
                    uint _S1090 = atlasWidth_11;
                    uint _S1091 = 7U % atlasWidth_11;
                    int _S1092 = int(_S1091);
                    uint _S1093 = 7U / _S1090;
                    int3 _S1094 = int3(_S1092, int(_S1093), int(0));
                    uint columns_11 = _S1089 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1094)).xy), uint(((_S1094)).z))).x * 255.0f)) << 8U);
                    uint _S1095 = atlasWidth_11;
                    uint _S1096 = 8U % atlasWidth_11;
                    int _S1097 = int(_S1096);
                    uint _S1098 = 8U / _S1095;
                    int3 _S1099 = int3(_S1097, int(_S1098), int(0));
                    uint _S1100 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1099)).xy), uint(((_S1099)).z))).x * 255.0f));
                    uint _S1101 = atlasWidth_11;
                    uint _S1102 = 9U % atlasWidth_11;
                    int _S1103 = int(_S1102);
                    uint _S1104 = 9U / _S1101;
                    int3 _S1105 = int3(_S1103, int(_S1104), int(0));
                    uint rows_11 = ((_S1100 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1105)).xy), uint(((_S1105)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1063 * _S1069 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1106 = atlasWidth_11 / columns_11;
                    float _S1107 = float(_S1106);
                    uint _S1108 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1107, float(_S1108));
                    uint _S1109 = slot_23 % columns_11;
                    float _S1110 = float(_S1109);
                    uint _S1111 = slot_23 / columns_11;
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1110, float(_S1111)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(_S182) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1112 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(_S182)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1112);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1113 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1113)).xy), uint(((_S1113)).z)));
                    break;
                }
                uint _S1114 = atlasWidth_11 / uint(_S1112.x);
                float _S1115 = float(_S1114);
                uint _S1116 = (atlasHeight_11 - 1U) / uint(_S1112.y);
                float2 cellSize_23 = float2(_S1115, float(_S1116));
                _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_5 = uint(round(_S1043.y));
            if(operation_5 == 1U)
            {
                _S183 = _S183 * _S357;
                break;
            }
            if(operation_5 == 2U)
            {
                _S183 = _S183 + _S357;
                break;
            }
            if(operation_5 == 3U)
            {
                _S183 = _S183 - _S357;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1043.z;
                _S183 = _S183 * float4((1.0f - factor_5))  + _S357 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S183.x);
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
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S1117 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S1117)
            {
                _S183 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S182)));
                break;
            }
            texture2d<float, access::sample> _S1118 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1118).get_width(0)),(*((&atlasHeight_12)) = (_S1118).get_height(0));
            int3 _S1119 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1119)).xy), uint(((_S1119)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1120 = atlasWidth_12;
                uint _S1121 = 2U % atlasWidth_12;
                int _S1122 = int(_S1121);
                uint _S1123 = 2U / _S1120;
                int3 _S1124 = int3(_S1122, int(_S1123), int(0));
                int _S1125 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1124)).xy), uint(((_S1124)).z))).x * 255.0f)));
                uint _S1126 = atlasWidth_12;
                uint _S1127 = 3U % atlasWidth_12;
                int _S1128 = int(_S1127);
                uint _S1129 = 3U / _S1126;
                int3 _S1130 = int3(_S1128, int(_S1129), int(0));
                int2 minimumTile_12 = int2(_S1125, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1130)).xy), uint(((_S1130)).z))).x * 255.0f))));
                uint _S1131 = atlasWidth_12;
                uint _S1132 = 4U % atlasWidth_12;
                int _S1133 = int(_S1132);
                uint _S1134 = 4U / _S1131;
                int3 _S1135 = int3(_S1133, int(_S1134), int(0));
                uint _S1136 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1135)).xy), uint(((_S1135)).z))).x * 255.0f));
                uint _S1137 = atlasWidth_12;
                uint _S1138 = 5U % atlasWidth_12;
                int _S1139 = int(_S1138);
                uint _S1140 = 5U / _S1137;
                int3 _S1141 = int3(_S1139, int(_S1140), int(0));
                uint _S1142 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1141)).xy), uint(((_S1141)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1136, _S1142);
                int2 tile_24 = int2(floor(_S182)) - minimumTile_12;
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
                    int3 _S1143 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1143)).xy), uint(((_S1143)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1136 + uint(tile_24.x));
                uint _S1144 = atlasWidth_12;
                uint _S1145 = offset_12 % atlasWidth_12;
                int _S1146 = int(_S1145);
                uint _S1147 = offset_12 / _S1144;
                int3 _S1148 = int3(_S1146, int(_S1147), int(0));
                uint _S1149 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1148)).xy), uint(((_S1148)).z))).x * 255.0f));
                uint _S1150 = atlasWidth_12;
                uint _S1151 = offset_12 + 1U;
                uint _S1152 = _S1151 % atlasWidth_12;
                int _S1153 = int(_S1152);
                uint _S1154 = _S1151 / _S1150;
                int3 _S1155 = int3(_S1153, int(_S1154), int(0));
                uint slot_24 = _S1149 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1155)).xy), uint(((_S1155)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1156 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1156)).xy), uint(((_S1156)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1157 = atlasWidth_12;
                uint _S1158 = 6U % atlasWidth_12;
                int _S1159 = int(_S1158);
                uint _S1160 = 6U / _S1157;
                int3 _S1161 = int3(_S1159, int(_S1160), int(0));
                uint _S1162 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1161)).xy), uint(((_S1161)).z))).x * 255.0f));
                uint _S1163 = atlasWidth_12;
                uint _S1164 = 7U % atlasWidth_12;
                int _S1165 = int(_S1164);
                uint _S1166 = 7U / _S1163;
                int3 _S1167 = int3(_S1165, int(_S1166), int(0));
                uint columns_12 = _S1162 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1167)).xy), uint(((_S1167)).z))).x * 255.0f)) << 8U);
                uint _S1168 = atlasWidth_12;
                uint _S1169 = 8U % atlasWidth_12;
                int _S1170 = int(_S1169);
                uint _S1171 = 8U / _S1168;
                int3 _S1172 = int3(_S1170, int(_S1171), int(0));
                uint _S1173 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1172)).xy), uint(((_S1172)).z))).x * 255.0f));
                uint _S1174 = atlasWidth_12;
                uint _S1175 = 9U % atlasWidth_12;
                int _S1176 = int(_S1175);
                uint _S1177 = 9U / _S1174;
                int3 _S1178 = int3(_S1176, int(_S1177), int(0));
                uint rows_12 = ((_S1173 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1178)).xy), uint(((_S1178)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1136 * _S1142 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1179 = atlasWidth_12 / columns_12;
                float _S1180 = float(_S1179);
                uint _S1181 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1180, float(_S1181));
                uint _S1182 = slot_25 % columns_12;
                float _S1183 = float(_S1182);
                uint _S1184 = slot_25 / columns_12;
                _S183 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1183, float(_S1184)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(_S182) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1185 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(_S182)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1185);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1186 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S183 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1186)).xy), uint(((_S1186)).z)));
                break;
            }
            uint _S1187 = atlasWidth_12 / uint(_S1185.x);
            float _S1188 = float(_S1187);
            uint _S1189 = (atlasHeight_12 - 1U) / uint(_S1185.y);
            float2 cellSize_25 = float2(_S1188, float(_S1189));
            _S183 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1190 = float4(_S6->compositeControls_0) ;
            if((_S1190.x) != 256.0f)
            {
                break;
            }
            bool _S1191 = (_S1190.w) >= 0.5f;
            for(;;)
            {
                if(!_S1191)
                {
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S1192 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1192).get_width(0)),(*((&atlasHeight_13)) = (_S1192).get_height(0));
                int3 _S1193 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1193)).xy), uint(((_S1193)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1194 = atlasWidth_13;
                    uint _S1195 = 2U % atlasWidth_13;
                    int _S1196 = int(_S1195);
                    uint _S1197 = 2U / _S1194;
                    int3 _S1198 = int3(_S1196, int(_S1197), int(0));
                    int _S1199 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1198)).xy), uint(((_S1198)).z))).x * 255.0f)));
                    uint _S1200 = atlasWidth_13;
                    uint _S1201 = 3U % atlasWidth_13;
                    int _S1202 = int(_S1201);
                    uint _S1203 = 3U / _S1200;
                    int3 _S1204 = int3(_S1202, int(_S1203), int(0));
                    int2 minimumTile_13 = int2(_S1199, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1204)).xy), uint(((_S1204)).z))).x * 255.0f))));
                    uint _S1205 = atlasWidth_13;
                    uint _S1206 = 4U % atlasWidth_13;
                    int _S1207 = int(_S1206);
                    uint _S1208 = 4U / _S1205;
                    int3 _S1209 = int3(_S1207, int(_S1208), int(0));
                    uint _S1210 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1209)).xy), uint(((_S1209)).z))).x * 255.0f));
                    uint _S1211 = atlasWidth_13;
                    uint _S1212 = 5U % atlasWidth_13;
                    int _S1213 = int(_S1212);
                    uint _S1214 = 5U / _S1211;
                    int3 _S1215 = int3(_S1213, int(_S1214), int(0));
                    uint _S1216 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1215)).xy), uint(((_S1215)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1210, _S1216);
                    int2 tile_26 = int2(floor(_S182)) - minimumTile_13;
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
                        int3 _S1217 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1217)).xy), uint(((_S1217)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1210 + uint(tile_26.x));
                    uint _S1218 = atlasWidth_13;
                    uint _S1219 = offset_13 % atlasWidth_13;
                    int _S1220 = int(_S1219);
                    uint _S1221 = offset_13 / _S1218;
                    int3 _S1222 = int3(_S1220, int(_S1221), int(0));
                    uint _S1223 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1222)).xy), uint(((_S1222)).z))).x * 255.0f));
                    uint _S1224 = atlasWidth_13;
                    uint _S1225 = offset_13 + 1U;
                    uint _S1226 = _S1225 % atlasWidth_13;
                    int _S1227 = int(_S1226);
                    uint _S1228 = _S1225 / _S1224;
                    int3 _S1229 = int3(_S1227, int(_S1228), int(0));
                    uint slot_26 = _S1223 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1229)).xy), uint(((_S1229)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1230 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1230)).xy), uint(((_S1230)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1231 = atlasWidth_13;
                    uint _S1232 = 6U % atlasWidth_13;
                    int _S1233 = int(_S1232);
                    uint _S1234 = 6U / _S1231;
                    int3 _S1235 = int3(_S1233, int(_S1234), int(0));
                    uint _S1236 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1235)).xy), uint(((_S1235)).z))).x * 255.0f));
                    uint _S1237 = atlasWidth_13;
                    uint _S1238 = 7U % atlasWidth_13;
                    int _S1239 = int(_S1238);
                    uint _S1240 = 7U / _S1237;
                    int3 _S1241 = int3(_S1239, int(_S1240), int(0));
                    uint columns_13 = _S1236 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1241)).xy), uint(((_S1241)).z))).x * 255.0f)) << 8U);
                    uint _S1242 = atlasWidth_13;
                    uint _S1243 = 8U % atlasWidth_13;
                    int _S1244 = int(_S1243);
                    uint _S1245 = 8U / _S1242;
                    int3 _S1246 = int3(_S1244, int(_S1245), int(0));
                    uint _S1247 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1246)).xy), uint(((_S1246)).z))).x * 255.0f));
                    uint _S1248 = atlasWidth_13;
                    uint _S1249 = 9U % atlasWidth_13;
                    int _S1250 = int(_S1249);
                    uint _S1251 = 9U / _S1248;
                    int3 _S1252 = int3(_S1250, int(_S1251), int(0));
                    uint rows_13 = ((_S1247 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1252)).xy), uint(((_S1252)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1210 * _S1216 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1253 = atlasWidth_13 / columns_13;
                    float _S1254 = float(_S1253);
                    uint _S1255 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1254, float(_S1255));
                    uint _S1256 = slot_27 % columns_13;
                    float _S1257 = float(_S1256);
                    uint _S1258 = slot_27 / columns_13;
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1257, float(_S1258)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(_S182) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1259 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(_S182)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1259);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1260 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1260)).xy), uint(((_S1260)).z)));
                    break;
                }
                uint _S1261 = atlasWidth_13 / uint(_S1259.x);
                float _S1262 = float(_S1261);
                uint _S1263 = (atlasHeight_13 - 1U) / uint(_S1259.y);
                float2 cellSize_27 = float2(_S1262, float(_S1263));
                _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_6 = uint(round(_S1190.y));
            if(operation_6 == 1U)
            {
                _S183 = _S183 * _S357;
                break;
            }
            if(operation_6 == 2U)
            {
                _S183 = _S183 + _S357;
                break;
            }
            if(operation_6 == 3U)
            {
                _S183 = _S183 - _S357;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1190.z;
                _S183 = _S183 * float4((1.0f - factor_6))  + _S357 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S183.xyz);
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
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S1264 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S1264)
            {
                _S183 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S182)));
                break;
            }
            texture2d<float, access::sample> _S1265 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1265).get_width(0)),(*((&atlasHeight_14)) = (_S1265).get_height(0));
            int3 _S1266 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1266)).xy), uint(((_S1266)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1267 = atlasWidth_14;
                uint _S1268 = 2U % atlasWidth_14;
                int _S1269 = int(_S1268);
                uint _S1270 = 2U / _S1267;
                int3 _S1271 = int3(_S1269, int(_S1270), int(0));
                int _S1272 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1271)).xy), uint(((_S1271)).z))).x * 255.0f)));
                uint _S1273 = atlasWidth_14;
                uint _S1274 = 3U % atlasWidth_14;
                int _S1275 = int(_S1274);
                uint _S1276 = 3U / _S1273;
                int3 _S1277 = int3(_S1275, int(_S1276), int(0));
                int2 minimumTile_14 = int2(_S1272, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1277)).xy), uint(((_S1277)).z))).x * 255.0f))));
                uint _S1278 = atlasWidth_14;
                uint _S1279 = 4U % atlasWidth_14;
                int _S1280 = int(_S1279);
                uint _S1281 = 4U / _S1278;
                int3 _S1282 = int3(_S1280, int(_S1281), int(0));
                uint _S1283 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1282)).xy), uint(((_S1282)).z))).x * 255.0f));
                uint _S1284 = atlasWidth_14;
                uint _S1285 = 5U % atlasWidth_14;
                int _S1286 = int(_S1285);
                uint _S1287 = 5U / _S1284;
                int3 _S1288 = int3(_S1286, int(_S1287), int(0));
                uint _S1289 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1288)).xy), uint(((_S1288)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1283, _S1289);
                int2 tile_28 = int2(floor(_S182)) - minimumTile_14;
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
                    int3 _S1290 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1290)).xy), uint(((_S1290)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1283 + uint(tile_28.x));
                uint _S1291 = atlasWidth_14;
                uint _S1292 = offset_14 % atlasWidth_14;
                int _S1293 = int(_S1292);
                uint _S1294 = offset_14 / _S1291;
                int3 _S1295 = int3(_S1293, int(_S1294), int(0));
                uint _S1296 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1295)).xy), uint(((_S1295)).z))).x * 255.0f));
                uint _S1297 = atlasWidth_14;
                uint _S1298 = offset_14 + 1U;
                uint _S1299 = _S1298 % atlasWidth_14;
                int _S1300 = int(_S1299);
                uint _S1301 = _S1298 / _S1297;
                int3 _S1302 = int3(_S1300, int(_S1301), int(0));
                uint slot_28 = _S1296 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1302)).xy), uint(((_S1302)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1303 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1303)).xy), uint(((_S1303)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1304 = atlasWidth_14;
                uint _S1305 = 6U % atlasWidth_14;
                int _S1306 = int(_S1305);
                uint _S1307 = 6U / _S1304;
                int3 _S1308 = int3(_S1306, int(_S1307), int(0));
                uint _S1309 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1308)).xy), uint(((_S1308)).z))).x * 255.0f));
                uint _S1310 = atlasWidth_14;
                uint _S1311 = 7U % atlasWidth_14;
                int _S1312 = int(_S1311);
                uint _S1313 = 7U / _S1310;
                int3 _S1314 = int3(_S1312, int(_S1313), int(0));
                uint columns_14 = _S1309 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1314)).xy), uint(((_S1314)).z))).x * 255.0f)) << 8U);
                uint _S1315 = atlasWidth_14;
                uint _S1316 = 8U % atlasWidth_14;
                int _S1317 = int(_S1316);
                uint _S1318 = 8U / _S1315;
                int3 _S1319 = int3(_S1317, int(_S1318), int(0));
                uint _S1320 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1319)).xy), uint(((_S1319)).z))).x * 255.0f));
                uint _S1321 = atlasWidth_14;
                uint _S1322 = 9U % atlasWidth_14;
                int _S1323 = int(_S1322);
                uint _S1324 = 9U / _S1321;
                int3 _S1325 = int3(_S1323, int(_S1324), int(0));
                uint rows_14 = ((_S1320 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1325)).xy), uint(((_S1325)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1283 * _S1289 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1326 = atlasWidth_14 / columns_14;
                float _S1327 = float(_S1326);
                uint _S1328 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1327, float(_S1328));
                uint _S1329 = slot_29 % columns_14;
                float _S1330 = float(_S1329);
                uint _S1331 = slot_29 / columns_14;
                _S183 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1330, float(_S1331)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(_S182) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1332 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(_S182)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1332);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1333 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S183 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1333)).xy), uint(((_S1333)).z)));
                break;
            }
            uint _S1334 = atlasWidth_14 / uint(_S1332.x);
            float _S1335 = float(_S1334);
            uint _S1336 = (atlasHeight_14 - 1U) / uint(_S1332.y);
            float2 cellSize_29 = float2(_S1335, float(_S1336));
            _S183 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1337 = float4(_S6->compositeControls_0) ;
            if((_S1337.x) != 512.0f)
            {
                break;
            }
            bool _S1338 = (_S1337.w) >= 0.5f;
            for(;;)
            {
                if(!_S1338)
                {
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S1339 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1339).get_width(0)),(*((&atlasHeight_15)) = (_S1339).get_height(0));
                int3 _S1340 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1340)).xy), uint(((_S1340)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1341 = atlasWidth_15;
                    uint _S1342 = 2U % atlasWidth_15;
                    int _S1343 = int(_S1342);
                    uint _S1344 = 2U / _S1341;
                    int3 _S1345 = int3(_S1343, int(_S1344), int(0));
                    int _S1346 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1345)).xy), uint(((_S1345)).z))).x * 255.0f)));
                    uint _S1347 = atlasWidth_15;
                    uint _S1348 = 3U % atlasWidth_15;
                    int _S1349 = int(_S1348);
                    uint _S1350 = 3U / _S1347;
                    int3 _S1351 = int3(_S1349, int(_S1350), int(0));
                    int2 minimumTile_15 = int2(_S1346, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1351)).xy), uint(((_S1351)).z))).x * 255.0f))));
                    uint _S1352 = atlasWidth_15;
                    uint _S1353 = 4U % atlasWidth_15;
                    int _S1354 = int(_S1353);
                    uint _S1355 = 4U / _S1352;
                    int3 _S1356 = int3(_S1354, int(_S1355), int(0));
                    uint _S1357 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1356)).xy), uint(((_S1356)).z))).x * 255.0f));
                    uint _S1358 = atlasWidth_15;
                    uint _S1359 = 5U % atlasWidth_15;
                    int _S1360 = int(_S1359);
                    uint _S1361 = 5U / _S1358;
                    int3 _S1362 = int3(_S1360, int(_S1361), int(0));
                    uint _S1363 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1362)).xy), uint(((_S1362)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1357, _S1363);
                    int2 tile_30 = int2(floor(_S182)) - minimumTile_15;
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
                        int3 _S1364 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1364)).xy), uint(((_S1364)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1357 + uint(tile_30.x));
                    uint _S1365 = atlasWidth_15;
                    uint _S1366 = offset_15 % atlasWidth_15;
                    int _S1367 = int(_S1366);
                    uint _S1368 = offset_15 / _S1365;
                    int3 _S1369 = int3(_S1367, int(_S1368), int(0));
                    uint _S1370 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1369)).xy), uint(((_S1369)).z))).x * 255.0f));
                    uint _S1371 = atlasWidth_15;
                    uint _S1372 = offset_15 + 1U;
                    uint _S1373 = _S1372 % atlasWidth_15;
                    int _S1374 = int(_S1373);
                    uint _S1375 = _S1372 / _S1371;
                    int3 _S1376 = int3(_S1374, int(_S1375), int(0));
                    uint slot_30 = _S1370 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1376)).xy), uint(((_S1376)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1377 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1377)).xy), uint(((_S1377)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1378 = atlasWidth_15;
                    uint _S1379 = 6U % atlasWidth_15;
                    int _S1380 = int(_S1379);
                    uint _S1381 = 6U / _S1378;
                    int3 _S1382 = int3(_S1380, int(_S1381), int(0));
                    uint _S1383 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1382)).xy), uint(((_S1382)).z))).x * 255.0f));
                    uint _S1384 = atlasWidth_15;
                    uint _S1385 = 7U % atlasWidth_15;
                    int _S1386 = int(_S1385);
                    uint _S1387 = 7U / _S1384;
                    int3 _S1388 = int3(_S1386, int(_S1387), int(0));
                    uint columns_15 = _S1383 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1388)).xy), uint(((_S1388)).z))).x * 255.0f)) << 8U);
                    uint _S1389 = atlasWidth_15;
                    uint _S1390 = 8U % atlasWidth_15;
                    int _S1391 = int(_S1390);
                    uint _S1392 = 8U / _S1389;
                    int3 _S1393 = int3(_S1391, int(_S1392), int(0));
                    uint _S1394 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1393)).xy), uint(((_S1393)).z))).x * 255.0f));
                    uint _S1395 = atlasWidth_15;
                    uint _S1396 = 9U % atlasWidth_15;
                    int _S1397 = int(_S1396);
                    uint _S1398 = 9U / _S1395;
                    int3 _S1399 = int3(_S1397, int(_S1398), int(0));
                    uint rows_15 = ((_S1394 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1399)).xy), uint(((_S1399)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1357 * _S1363 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1400 = atlasWidth_15 / columns_15;
                    float _S1401 = float(_S1400);
                    uint _S1402 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1401, float(_S1402));
                    uint _S1403 = slot_31 % columns_15;
                    float _S1404 = float(_S1403);
                    uint _S1405 = slot_31 / columns_15;
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1404, float(_S1405)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(_S182) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1406 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(_S182)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1406);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1407 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1407)).xy), uint(((_S1407)).z)));
                    break;
                }
                uint _S1408 = atlasWidth_15 / uint(_S1406.x);
                float _S1409 = float(_S1408);
                uint _S1410 = (atlasHeight_15 - 1U) / uint(_S1406.y);
                float2 cellSize_31 = float2(_S1409, float(_S1410));
                _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_7 = uint(round(_S1337.y));
            if(operation_7 == 1U)
            {
                _S183 = _S183 * _S357;
                break;
            }
            if(operation_7 == 2U)
            {
                _S183 = _S183 + _S357;
                break;
            }
            if(operation_7 == 3U)
            {
                _S183 = _S183 - _S357;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1337.z;
                _S183 = _S183 * float4((1.0f - factor_7))  + _S357 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S183.x);
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
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S1411 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S1411)
            {
                _S183 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S182)));
                break;
            }
            texture2d<float, access::sample> _S1412 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1412).get_width(0)),(*((&atlasHeight_16)) = (_S1412).get_height(0));
            int3 _S1413 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1413)).xy), uint(((_S1413)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1414 = atlasWidth_16;
                uint _S1415 = 2U % atlasWidth_16;
                int _S1416 = int(_S1415);
                uint _S1417 = 2U / _S1414;
                int3 _S1418 = int3(_S1416, int(_S1417), int(0));
                int _S1419 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1418)).xy), uint(((_S1418)).z))).x * 255.0f)));
                uint _S1420 = atlasWidth_16;
                uint _S1421 = 3U % atlasWidth_16;
                int _S1422 = int(_S1421);
                uint _S1423 = 3U / _S1420;
                int3 _S1424 = int3(_S1422, int(_S1423), int(0));
                int2 minimumTile_16 = int2(_S1419, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1424)).xy), uint(((_S1424)).z))).x * 255.0f))));
                uint _S1425 = atlasWidth_16;
                uint _S1426 = 4U % atlasWidth_16;
                int _S1427 = int(_S1426);
                uint _S1428 = 4U / _S1425;
                int3 _S1429 = int3(_S1427, int(_S1428), int(0));
                uint _S1430 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1429)).xy), uint(((_S1429)).z))).x * 255.0f));
                uint _S1431 = atlasWidth_16;
                uint _S1432 = 5U % atlasWidth_16;
                int _S1433 = int(_S1432);
                uint _S1434 = 5U / _S1431;
                int3 _S1435 = int3(_S1433, int(_S1434), int(0));
                uint _S1436 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1435)).xy), uint(((_S1435)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1430, _S1436);
                int2 tile_32 = int2(floor(_S182)) - minimumTile_16;
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
                    int3 _S1437 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1437)).xy), uint(((_S1437)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1430 + uint(tile_32.x));
                uint _S1438 = atlasWidth_16;
                uint _S1439 = offset_16 % atlasWidth_16;
                int _S1440 = int(_S1439);
                uint _S1441 = offset_16 / _S1438;
                int3 _S1442 = int3(_S1440, int(_S1441), int(0));
                uint _S1443 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1442)).xy), uint(((_S1442)).z))).x * 255.0f));
                uint _S1444 = atlasWidth_16;
                uint _S1445 = offset_16 + 1U;
                uint _S1446 = _S1445 % atlasWidth_16;
                int _S1447 = int(_S1446);
                uint _S1448 = _S1445 / _S1444;
                int3 _S1449 = int3(_S1447, int(_S1448), int(0));
                uint slot_32 = _S1443 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1449)).xy), uint(((_S1449)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1450 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1450)).xy), uint(((_S1450)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1451 = atlasWidth_16;
                uint _S1452 = 6U % atlasWidth_16;
                int _S1453 = int(_S1452);
                uint _S1454 = 6U / _S1451;
                int3 _S1455 = int3(_S1453, int(_S1454), int(0));
                uint _S1456 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1455)).xy), uint(((_S1455)).z))).x * 255.0f));
                uint _S1457 = atlasWidth_16;
                uint _S1458 = 7U % atlasWidth_16;
                int _S1459 = int(_S1458);
                uint _S1460 = 7U / _S1457;
                int3 _S1461 = int3(_S1459, int(_S1460), int(0));
                uint columns_16 = _S1456 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1461)).xy), uint(((_S1461)).z))).x * 255.0f)) << 8U);
                uint _S1462 = atlasWidth_16;
                uint _S1463 = 8U % atlasWidth_16;
                int _S1464 = int(_S1463);
                uint _S1465 = 8U / _S1462;
                int3 _S1466 = int3(_S1464, int(_S1465), int(0));
                uint _S1467 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1466)).xy), uint(((_S1466)).z))).x * 255.0f));
                uint _S1468 = atlasWidth_16;
                uint _S1469 = 9U % atlasWidth_16;
                int _S1470 = int(_S1469);
                uint _S1471 = 9U / _S1468;
                int3 _S1472 = int3(_S1470, int(_S1471), int(0));
                uint rows_16 = ((_S1467 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1472)).xy), uint(((_S1472)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1430 * _S1436 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1473 = atlasWidth_16 / columns_16;
                float _S1474 = float(_S1473);
                uint _S1475 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1474, float(_S1475));
                uint _S1476 = slot_33 % columns_16;
                float _S1477 = float(_S1476);
                uint _S1478 = slot_33 / columns_16;
                _S183 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1477, float(_S1478)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(_S182) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1479 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(_S182)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1479);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1480 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S183 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1480)).xy), uint(((_S1480)).z)));
                break;
            }
            uint _S1481 = atlasWidth_16 / uint(_S1479.x);
            float _S1482 = float(_S1481);
            uint _S1483 = (atlasHeight_16 - 1U) / uint(_S1479.y);
            float2 cellSize_33 = float2(_S1482, float(_S1483));
            _S183 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1484 = float4(_S6->compositeControls_0) ;
            if((_S1484.x) != 1024.0f)
            {
                break;
            }
            bool _S1485 = (_S1484.w) >= 0.5f;
            for(;;)
            {
                if(!_S1485)
                {
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S1486 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1486).get_width(0)),(*((&atlasHeight_17)) = (_S1486).get_height(0));
                int3 _S1487 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1487)).xy), uint(((_S1487)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1488 = atlasWidth_17;
                    uint _S1489 = 2U % atlasWidth_17;
                    int _S1490 = int(_S1489);
                    uint _S1491 = 2U / _S1488;
                    int3 _S1492 = int3(_S1490, int(_S1491), int(0));
                    int _S1493 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1492)).xy), uint(((_S1492)).z))).x * 255.0f)));
                    uint _S1494 = atlasWidth_17;
                    uint _S1495 = 3U % atlasWidth_17;
                    int _S1496 = int(_S1495);
                    uint _S1497 = 3U / _S1494;
                    int3 _S1498 = int3(_S1496, int(_S1497), int(0));
                    int2 minimumTile_17 = int2(_S1493, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1498)).xy), uint(((_S1498)).z))).x * 255.0f))));
                    uint _S1499 = atlasWidth_17;
                    uint _S1500 = 4U % atlasWidth_17;
                    int _S1501 = int(_S1500);
                    uint _S1502 = 4U / _S1499;
                    int3 _S1503 = int3(_S1501, int(_S1502), int(0));
                    uint _S1504 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1503)).xy), uint(((_S1503)).z))).x * 255.0f));
                    uint _S1505 = atlasWidth_17;
                    uint _S1506 = 5U % atlasWidth_17;
                    int _S1507 = int(_S1506);
                    uint _S1508 = 5U / _S1505;
                    int3 _S1509 = int3(_S1507, int(_S1508), int(0));
                    uint _S1510 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1509)).xy), uint(((_S1509)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1504, _S1510);
                    int2 tile_34 = int2(floor(_S182)) - minimumTile_17;
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
                        int3 _S1511 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1511)).xy), uint(((_S1511)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1504 + uint(tile_34.x));
                    uint _S1512 = atlasWidth_17;
                    uint _S1513 = offset_17 % atlasWidth_17;
                    int _S1514 = int(_S1513);
                    uint _S1515 = offset_17 / _S1512;
                    int3 _S1516 = int3(_S1514, int(_S1515), int(0));
                    uint _S1517 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1516)).xy), uint(((_S1516)).z))).x * 255.0f));
                    uint _S1518 = atlasWidth_17;
                    uint _S1519 = offset_17 + 1U;
                    uint _S1520 = _S1519 % atlasWidth_17;
                    int _S1521 = int(_S1520);
                    uint _S1522 = _S1519 / _S1518;
                    int3 _S1523 = int3(_S1521, int(_S1522), int(0));
                    uint slot_34 = _S1517 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1523)).xy), uint(((_S1523)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1524 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1524)).xy), uint(((_S1524)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1525 = atlasWidth_17;
                    uint _S1526 = 6U % atlasWidth_17;
                    int _S1527 = int(_S1526);
                    uint _S1528 = 6U / _S1525;
                    int3 _S1529 = int3(_S1527, int(_S1528), int(0));
                    uint _S1530 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1529)).xy), uint(((_S1529)).z))).x * 255.0f));
                    uint _S1531 = atlasWidth_17;
                    uint _S1532 = 7U % atlasWidth_17;
                    int _S1533 = int(_S1532);
                    uint _S1534 = 7U / _S1531;
                    int3 _S1535 = int3(_S1533, int(_S1534), int(0));
                    uint columns_17 = _S1530 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1535)).xy), uint(((_S1535)).z))).x * 255.0f)) << 8U);
                    uint _S1536 = atlasWidth_17;
                    uint _S1537 = 8U % atlasWidth_17;
                    int _S1538 = int(_S1537);
                    uint _S1539 = 8U / _S1536;
                    int3 _S1540 = int3(_S1538, int(_S1539), int(0));
                    uint _S1541 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1540)).xy), uint(((_S1540)).z))).x * 255.0f));
                    uint _S1542 = atlasWidth_17;
                    uint _S1543 = 9U % atlasWidth_17;
                    int _S1544 = int(_S1543);
                    uint _S1545 = 9U / _S1542;
                    int3 _S1546 = int3(_S1544, int(_S1545), int(0));
                    uint rows_17 = ((_S1541 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1546)).xy), uint(((_S1546)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1504 * _S1510 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1547 = atlasWidth_17 / columns_17;
                    float _S1548 = float(_S1547);
                    uint _S1549 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1548, float(_S1549));
                    uint _S1550 = slot_35 % columns_17;
                    float _S1551 = float(_S1550);
                    uint _S1552 = slot_35 / columns_17;
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1551, float(_S1552)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(_S182) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1553 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(_S182)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1553);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1554 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1554)).xy), uint(((_S1554)).z)));
                    break;
                }
                uint _S1555 = atlasWidth_17 / uint(_S1553.x);
                float _S1556 = float(_S1555);
                uint _S1557 = (atlasHeight_17 - 1U) / uint(_S1553.y);
                float2 cellSize_35 = float2(_S1556, float(_S1557));
                _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_8 = uint(round(_S1484.y));
            if(operation_8 == 1U)
            {
                _S183 = _S183 * _S357;
                break;
            }
            if(operation_8 == 2U)
            {
                _S183 = _S183 + _S357;
                break;
            }
            if(operation_8 == 3U)
            {
                _S183 = _S183 - _S357;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1484.z;
                _S183 = _S183 * float4((1.0f - factor_8))  + _S357 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S183.x);
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
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S1558 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S1558)
            {
                _S183 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S182)));
                break;
            }
            texture2d<float, access::sample> _S1559 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1559).get_width(0)),(*((&atlasHeight_18)) = (_S1559).get_height(0));
            int3 _S1560 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1560)).xy), uint(((_S1560)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1561 = atlasWidth_18;
                uint _S1562 = 2U % atlasWidth_18;
                int _S1563 = int(_S1562);
                uint _S1564 = 2U / _S1561;
                int3 _S1565 = int3(_S1563, int(_S1564), int(0));
                int _S1566 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1565)).xy), uint(((_S1565)).z))).x * 255.0f)));
                uint _S1567 = atlasWidth_18;
                uint _S1568 = 3U % atlasWidth_18;
                int _S1569 = int(_S1568);
                uint _S1570 = 3U / _S1567;
                int3 _S1571 = int3(_S1569, int(_S1570), int(0));
                int2 minimumTile_18 = int2(_S1566, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1571)).xy), uint(((_S1571)).z))).x * 255.0f))));
                uint _S1572 = atlasWidth_18;
                uint _S1573 = 4U % atlasWidth_18;
                int _S1574 = int(_S1573);
                uint _S1575 = 4U / _S1572;
                int3 _S1576 = int3(_S1574, int(_S1575), int(0));
                uint _S1577 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1576)).xy), uint(((_S1576)).z))).x * 255.0f));
                uint _S1578 = atlasWidth_18;
                uint _S1579 = 5U % atlasWidth_18;
                int _S1580 = int(_S1579);
                uint _S1581 = 5U / _S1578;
                int3 _S1582 = int3(_S1580, int(_S1581), int(0));
                uint _S1583 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1582)).xy), uint(((_S1582)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1577, _S1583);
                int2 tile_36 = int2(floor(_S182)) - minimumTile_18;
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
                    int3 _S1584 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1584)).xy), uint(((_S1584)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1577 + uint(tile_36.x));
                uint _S1585 = atlasWidth_18;
                uint _S1586 = offset_18 % atlasWidth_18;
                int _S1587 = int(_S1586);
                uint _S1588 = offset_18 / _S1585;
                int3 _S1589 = int3(_S1587, int(_S1588), int(0));
                uint _S1590 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1589)).xy), uint(((_S1589)).z))).x * 255.0f));
                uint _S1591 = atlasWidth_18;
                uint _S1592 = offset_18 + 1U;
                uint _S1593 = _S1592 % atlasWidth_18;
                int _S1594 = int(_S1593);
                uint _S1595 = _S1592 / _S1591;
                int3 _S1596 = int3(_S1594, int(_S1595), int(0));
                uint slot_36 = _S1590 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1596)).xy), uint(((_S1596)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1597 = int3(int(1), int(0), int(0));
                    _S183 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1597)).xy), uint(((_S1597)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1598 = atlasWidth_18;
                uint _S1599 = 6U % atlasWidth_18;
                int _S1600 = int(_S1599);
                uint _S1601 = 6U / _S1598;
                int3 _S1602 = int3(_S1600, int(_S1601), int(0));
                uint _S1603 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1602)).xy), uint(((_S1602)).z))).x * 255.0f));
                uint _S1604 = atlasWidth_18;
                uint _S1605 = 7U % atlasWidth_18;
                int _S1606 = int(_S1605);
                uint _S1607 = 7U / _S1604;
                int3 _S1608 = int3(_S1606, int(_S1607), int(0));
                uint columns_18 = _S1603 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1608)).xy), uint(((_S1608)).z))).x * 255.0f)) << 8U);
                uint _S1609 = atlasWidth_18;
                uint _S1610 = 8U % atlasWidth_18;
                int _S1611 = int(_S1610);
                uint _S1612 = 8U / _S1609;
                int3 _S1613 = int3(_S1611, int(_S1612), int(0));
                uint _S1614 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1613)).xy), uint(((_S1613)).z))).x * 255.0f));
                uint _S1615 = atlasWidth_18;
                uint _S1616 = 9U % atlasWidth_18;
                int _S1617 = int(_S1616);
                uint _S1618 = 9U / _S1615;
                int3 _S1619 = int3(_S1617, int(_S1618), int(0));
                uint rows_18 = ((_S1614 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1619)).xy), uint(((_S1619)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1577 * _S1583 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1620 = atlasWidth_18 / columns_18;
                float _S1621 = float(_S1620);
                uint _S1622 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1621, float(_S1622));
                uint _S1623 = slot_37 % columns_18;
                float _S1624 = float(_S1623);
                uint _S1625 = slot_37 / columns_18;
                _S183 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1624, float(_S1625)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(_S182) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1626 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(_S182)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1626);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1627 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S183 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1627)).xy), uint(((_S1627)).z)));
                break;
            }
            uint _S1628 = atlasWidth_18 / uint(_S1626.x);
            float _S1629 = float(_S1628);
            uint _S1630 = (atlasHeight_18 - 1U) / uint(_S1626.y);
            float2 cellSize_37 = float2(_S1629, float(_S1630));
            _S183 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1631 = float4(_S6->compositeControls_0) ;
            if((_S1631.x) != 2048.0f)
            {
                break;
            }
            bool _S1632 = (_S1631.w) >= 0.5f;
            for(;;)
            {
                if(!_S1632)
                {
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S1633 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1633).get_width(0)),(*((&atlasHeight_19)) = (_S1633).get_height(0));
                int3 _S1634 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1634)).xy), uint(((_S1634)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1635 = atlasWidth_19;
                    uint _S1636 = 2U % atlasWidth_19;
                    int _S1637 = int(_S1636);
                    uint _S1638 = 2U / _S1635;
                    int3 _S1639 = int3(_S1637, int(_S1638), int(0));
                    int _S1640 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1639)).xy), uint(((_S1639)).z))).x * 255.0f)));
                    uint _S1641 = atlasWidth_19;
                    uint _S1642 = 3U % atlasWidth_19;
                    int _S1643 = int(_S1642);
                    uint _S1644 = 3U / _S1641;
                    int3 _S1645 = int3(_S1643, int(_S1644), int(0));
                    int2 minimumTile_19 = int2(_S1640, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1645)).xy), uint(((_S1645)).z))).x * 255.0f))));
                    uint _S1646 = atlasWidth_19;
                    uint _S1647 = 4U % atlasWidth_19;
                    int _S1648 = int(_S1647);
                    uint _S1649 = 4U / _S1646;
                    int3 _S1650 = int3(_S1648, int(_S1649), int(0));
                    uint _S1651 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1650)).xy), uint(((_S1650)).z))).x * 255.0f));
                    uint _S1652 = atlasWidth_19;
                    uint _S1653 = 5U % atlasWidth_19;
                    int _S1654 = int(_S1653);
                    uint _S1655 = 5U / _S1652;
                    int3 _S1656 = int3(_S1654, int(_S1655), int(0));
                    uint _S1657 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1656)).xy), uint(((_S1656)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1651, _S1657);
                    int2 tile_38 = int2(floor(_S182)) - minimumTile_19;
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
                        int3 _S1658 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1658)).xy), uint(((_S1658)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1651 + uint(tile_38.x));
                    uint _S1659 = atlasWidth_19;
                    uint _S1660 = offset_19 % atlasWidth_19;
                    int _S1661 = int(_S1660);
                    uint _S1662 = offset_19 / _S1659;
                    int3 _S1663 = int3(_S1661, int(_S1662), int(0));
                    uint _S1664 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1663)).xy), uint(((_S1663)).z))).x * 255.0f));
                    uint _S1665 = atlasWidth_19;
                    uint _S1666 = offset_19 + 1U;
                    uint _S1667 = _S1666 % atlasWidth_19;
                    int _S1668 = int(_S1667);
                    uint _S1669 = _S1666 / _S1665;
                    int3 _S1670 = int3(_S1668, int(_S1669), int(0));
                    uint slot_38 = _S1664 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1670)).xy), uint(((_S1670)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1671 = int3(int(1), int(0), int(0));
                        _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1671)).xy), uint(((_S1671)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1672 = atlasWidth_19;
                    uint _S1673 = 6U % atlasWidth_19;
                    int _S1674 = int(_S1673);
                    uint _S1675 = 6U / _S1672;
                    int3 _S1676 = int3(_S1674, int(_S1675), int(0));
                    uint _S1677 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1676)).xy), uint(((_S1676)).z))).x * 255.0f));
                    uint _S1678 = atlasWidth_19;
                    uint _S1679 = 7U % atlasWidth_19;
                    int _S1680 = int(_S1679);
                    uint _S1681 = 7U / _S1678;
                    int3 _S1682 = int3(_S1680, int(_S1681), int(0));
                    uint columns_19 = _S1677 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1682)).xy), uint(((_S1682)).z))).x * 255.0f)) << 8U);
                    uint _S1683 = atlasWidth_19;
                    uint _S1684 = 8U % atlasWidth_19;
                    int _S1685 = int(_S1684);
                    uint _S1686 = 8U / _S1683;
                    int3 _S1687 = int3(_S1685, int(_S1686), int(0));
                    uint _S1688 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1687)).xy), uint(((_S1687)).z))).x * 255.0f));
                    uint _S1689 = atlasWidth_19;
                    uint _S1690 = 9U % atlasWidth_19;
                    int _S1691 = int(_S1690);
                    uint _S1692 = 9U / _S1689;
                    int3 _S1693 = int3(_S1691, int(_S1692), int(0));
                    uint rows_19 = ((_S1688 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1693)).xy), uint(((_S1693)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1651 * _S1657 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1694 = atlasWidth_19 / columns_19;
                    float _S1695 = float(_S1694);
                    uint _S1696 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1695, float(_S1696));
                    uint _S1697 = slot_39 % columns_19;
                    float _S1698 = float(_S1697);
                    uint _S1699 = slot_39 / columns_19;
                    _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1698, float(_S1699)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(_S182) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1700 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(_S182)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1700);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1701 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S357 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1701)).xy), uint(((_S1701)).z)));
                    break;
                }
                uint _S1702 = atlasWidth_19 / uint(_S1700.x);
                float _S1703 = float(_S1702);
                uint _S1704 = (atlasHeight_19 - 1U) / uint(_S1700.y);
                float2 cellSize_39 = float2(_S1703, float(_S1704));
                _S357 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_9 = uint(round(_S1631.y));
            if(operation_9 == 1U)
            {
                _S183 = _S183 * _S357;
                break;
            }
            if(operation_9 == 2U)
            {
                _S183 = _S183 + _S357;
                break;
            }
            if(operation_9 == 3U)
            {
                _S183 = _S183 - _S357;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1631.z;
                _S183 = _S183 * float4((1.0f - factor_9))  + _S357 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S183.x;
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
    float3 normal_1 = normalize(_S1.normal_0);
    float3 worldNormal_1 = normalize(_S1.worldNormal_0);
    float lengthSquared_0 = dot(_S1.eyePosition_0, _S1.eyePosition_0);
    float3 diffuseFresnel_0;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        diffuseFresnel_0 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
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
    float _S1705 = saturate(abs(dot(normal_2, diffuseFresnel_0)) + 0.00000999999974738f);
    float _S1706 = max(0.00100000004749745f, roughness_0);
    float _S1707 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1708 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1708;
    bool omniPbrSpecular_0 = (_S173.w) >= 0.5f;
    float dielectricLevel_0 = _S159.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1709 = float3(dielectricLevel_0) ;
        float3 _S1710 = float3(metallic_0) ;
        float3 _S1711 = mix(_S1709, diffuseColor_0, _S1710);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1709, diffuseColor_0, _S1710);
        grazingIncidence_0 = _S1711;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S165.w) >= 0.5f)
        {
            float3 _S1712 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1712;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1713 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1713);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1713);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1714 = _S2;
    float _S1715 = _S2.ambientLight_0.w;
    if(_S1715 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1716 = _S1714.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1716, _S1716)) > 0.0f;
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
    uint _S1717 = min(uint(_S1715), 128U);
    float3 sceneEye_0 = normalize((((float4(diffuseFresnel_0, 0.0f)) * (_S2.eyeToWorld_0))).xyz);
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
    float4 _S1718 = float4(_S6->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S1718.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S6->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S1719 = _S2;
    uint _S1720 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S1720) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S1721 = _S1714.ambientLight_0.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S1722 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S1722;
        for(;;)
        {
            if(lightCount_0 < _S1720)
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
        domeAmbient_1 = _S1721;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S1723 = !hasSceneLighting_0;
    if(_S1723)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1717;
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
        bool _S1724;
        if(hasSceneLighting_0)
        {
            _S1724 = (((uint4(_S6->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S1724 = false;
        }
        if(_S1724)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S1725 = lightIndex_0 == 0U;
        bool _S1726;
        if(_S1725)
        {
            _S1726 = _S1723;
        }
        else
        {
            _S1726 = false;
        }
        float lightType_0;
        if(_S1726)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S1727 = 224U + lightIndex_0 * 16U;
            float _S1728 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1727)>>2]);
            float _S1729 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1727 + 4U)>>2]);
            float _S1730 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1727 + 8U)>>2]);
            float _S1731 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1727 + 12U)>>2]);
            lightType_0 = _S1731;
        }
        bool _S1732;
        if(_S1725)
        {
            _S1732 = _S1723;
        }
        else
        {
            _S1732 = false;
        }
        if(_S1732)
        {
            lightDirection_0 = normalize((float4(_S6->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S1733 = 2272U + lightIndex_0 * 16U;
            float _S1734 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1733)>>2]);
            float _S1735 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1733 + 4U)>>2]);
            float _S1736 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1733 + 8U)>>2]);
            float _S1737 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1733 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S1734, _S1735, _S1736));
        }
        bool _S1738;
        if(_S1725)
        {
            _S1738 = _S1723;
        }
        else
        {
            _S1738 = false;
        }
        if(_S1738)
        {
            roughness_0 = (float4(_S6->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S1739 = 4320U + lightIndex_0 * 16U;
            float _S1740 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1739)>>2]);
            float _S1741 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1739 + 4U)>>2]);
            float _S1742 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1739 + 8U)>>2]);
            float _S1743 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1739 + 12U)>>2]);
            roughness_0 = _S1743;
        }
        bool _S1744;
        if(_S1725)
        {
            _S1744 = _S1723;
        }
        else
        {
            _S1744 = false;
        }
        if(_S1744)
        {
            diffuseColor_0 = _S1718.xyz;
        }
        else
        {
            uint _S1745 = 4320U + lightIndex_0 * 16U;
            float _S1746 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1745)>>2]);
            float _S1747 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1745 + 4U)>>2]);
            float _S1748 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1745 + 8U)>>2]);
            float _S1749 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1745 + 12U)>>2]);
            diffuseColor_0 = float3(_S1746, _S1747, _S1748);
        }
        bool _S1750;
        if(_S1725)
        {
            _S1750 = _S1723;
        }
        else
        {
            _S1750 = false;
        }
        if(_S1750)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S1751 = 6368U + lightIndex_0 * 16U;
            float _S1752 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1751)>>2]);
            float _S1753 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1751 + 4U)>>2]);
            float _S1754 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1751 + 8U)>>2]);
            float _S1755 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1751 + 12U)>>2]);
            metallic_0 = _S1752;
        }
        bool _S1756;
        if(_S1725)
        {
            _S1756 = _S1723;
        }
        else
        {
            _S1756 = false;
        }
        if(_S1756)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S1757 = 6368U + lightIndex_0 * 16U;
            float _S1758 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1757)>>2]);
            float _S1759 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1757 + 4U)>>2]);
            float _S1760 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1757 + 8U)>>2]);
            float _S1761 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1757 + 12U)>>2]);
            clearcoatRoughness_0 = _S1759;
        }
        bool _S1762;
        if(_S1725)
        {
            _S1762 = _S1723;
        }
        else
        {
            _S1762 = false;
        }
        if(_S1762)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1763 = 8416U + lightIndex_0 * 16U;
            float _S1764 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1763)>>2]);
            float _S1765 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1763 + 4U)>>2]);
            float _S1766 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1763 + 8U)>>2]);
            float _S1767 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1763 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S1764, _S1765, _S1766));
        }
        bool _S1768;
        if(_S1725)
        {
            _S1768 = _S1723;
        }
        else
        {
            _S1768 = false;
        }
        if(_S1768)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S1769 = 10464U + lightIndex_0 * 16U;
            float _S1770 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1769)>>2]);
            float _S1771 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1769 + 4U)>>2]);
            float _S1772 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1769 + 8U)>>2]);
            float _S1773 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1769 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S1770, _S1771, _S1772));
        }
        bool _S1774;
        if(_S1725)
        {
            _S1774 = _S1723;
        }
        else
        {
            _S1774 = false;
        }
        float shapeX_0;
        if(_S1774)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S1775 = 8416U + lightIndex_0 * 16U;
            float _S1776 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1775)>>2]);
            float _S1777 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1775 + 4U)>>2]);
            float _S1778 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1775 + 8U)>>2]);
            float _S1779 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1775 + 12U)>>2]);
            shapeX_0 = _S1779;
        }
        bool _S1780;
        if(_S1725)
        {
            _S1780 = _S1723;
        }
        else
        {
            _S1780 = false;
        }
        float shapeY_0;
        if(_S1780)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S1781 = 10464U + lightIndex_0 * 16U;
            float _S1782 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1781)>>2]);
            float _S1783 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1781 + 4U)>>2]);
            float _S1784 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1781 + 8U)>>2]);
            float _S1785 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1781 + 12U)>>2]);
            shapeY_0 = _S1785;
        }
        bool _S1786;
        if(_S1725)
        {
            _S1786 = _S1723;
        }
        else
        {
            _S1786 = false;
        }
        float lightRadius_0;
        if(_S1786)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S1787 = 2272U + lightIndex_0 * 16U;
            float _S1788 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1787)>>2]);
            float _S1789 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1787 + 4U)>>2]);
            float _S1790 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1787 + 8U)>>2]);
            float _S1791 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1787 + 12U)>>2]);
            lightRadius_0 = _S1791;
        }
        bool _S1792;
        if(_S1725)
        {
            _S1792 = _S1723;
        }
        else
        {
            _S1792 = false;
        }
        if(_S1792)
        {
            domeAmbient_1 = diffuseFresnel_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S1793;
        if(_S1725)
        {
            _S1793 = _S1723;
        }
        else
        {
            _S1793 = false;
        }
        if(_S1793)
        {
            _S183 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1794 = 15296U + lightIndex_0 * 16U;
            float _S1795 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1794)>>2]);
            float _S1796 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1794 + 4U)>>2]);
            float _S1797 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1794 + 8U)>>2]);
            float _S1798 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1794 + 12U)>>2]);
            _S183 = float4(_S1795, _S1796, _S1797, _S1798);
        }
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S1799 = 12960U + lightIndex_0 * 16U;
            float _S1800 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1799)>>2]);
            float _S1801 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1799 + 4U)>>2]);
            float _S1802 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1799 + 8U)>>2]);
            float _S1803 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1799 + 12U)>>2]);
            int shadowSlot_0 = int(_S1800);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S1804 = _S2;
                    FragmentFrameParameters_0 _S1805 = _S2;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - specularColor_0;
                    }
                    else
                    {
                        color_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S1804.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S1806 = lightClip_0.w;
                    if(_S1806 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S1806) ;
                    bool _S1807;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S1807 = true;
                    }
                    else
                    {
                        _S1807 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S1808;
                    if(_S1807)
                    {
                        _S1808 = true;
                    }
                    else
                    {
                        _S1808 = (ndc_0.z) < 0.0f;
                    }
                    bool _S1809;
                    if(_S1808)
                    {
                        _S1809 = true;
                    }
                    else
                    {
                        _S1809 = (ndc_0.z) > 1.0f;
                    }
                    if(_S1809)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S1810 = _S1805.shadowTile_0[shadowSlot_0].xy;
                    float2 _S1811 = _S1805.shadowTile_0[shadowSlot_0].zw;
                    float2 _S1812 = _S1810 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S1811;
                    float texel_0 = _S1804.shadowControls_0[shadowSlot_0].w;
                    float _S1813 = max(_S1804.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S1814 = ndc_0.z - _S1804.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S1815 = float2((texel_0 * 0.5f)) ;
                    float2 _S1816 = _S1810 + _S1815;
                    float2 _S1817 = _S1810 + _S1811 - _S1815;
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
                            if(_S1814 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S1812 + float2(float(x_0), float(y_0)) * float2((_S1813 * texel_0)) , _S1816, _S1817)), level((0.0f))).x))
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
        float3 _S1818 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S1818;
        sampleOffsets_0[int(1)] = _S1818;
        sampleOffsets_0[int(2)] = _S1818;
        sampleOffsets_0[int(3)] = _S1818;
        sampleOffsets_0[int(4)] = _S1818;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S1819 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S1819 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S1819 - halfHeight_0;
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
            float sampleIntensity_1;
            float emissionScale_0;
            if(lightType_0 >= 2.0f)
            {
                uint _S1820 = 224U + lightIndex_0 * 16U;
                float _S1821 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1820)>>2]);
                float _S1822 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1820 + 4U)>>2]);
                float _S1823 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1820 + 8U)>>2]);
                float _S1824 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1820 + 12U)>>2]);
                float3 toLight_0 = float3(_S1821, _S1822, _S1823) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S1825 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S1825)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S1825;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S183.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S183.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S1826 = _S183.y;
                        if(_S1826 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S1826);
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
            float _S1827 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S1827)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S1827) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S1828 = float3(_S1827) ;
            float _S1829 = max(_S1706, 0.00100000004749745f);
            float alpha_0 = _S1829 * _S1829;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S1830 = 1.0f - k_0;
            float3 _S1831 = float3(max(4.0f * normalDotLight_0 * _S1705, 1.00000000317107685e-30f)) ;
            float3 _S1832 = mix(normalIncidence_0, grazingIncidence_0, _S1828) * float3((_S1705 / (_S1705 * _S1830 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S1830 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S1831;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S1833 = max(_S1707, 0.00100000004749745f);
                float alpha_1 = _S1833 * _S1833;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S1834 = 1.0f - k_1;
                directSpecular_0 = _S1832 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S1828) * float3((_S1705 / (_S1705 * _S1834 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S1834 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S1831);
            }
            else
            {
                directSpecular_0 = _S1832;
            }
            float3 _S1835 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S1835 * _S1708) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S1835);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S1836 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S1837 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S1838 = saturate(_S1706);
        float2 _S1839 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S1837, _S1838)), level((0.0f)))).xy;
        float _S1840 = _S1839.x;
        float _S1841 = _S1839.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S1840)  + grazingIncidence_0 * float3(_S1841) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S1840 + _S1841)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S1842 = max(_S1719.domeControls_0.y, 1.0f);
        float3 _S1843 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S1842 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1719.domeControls_0.z;
            float _S1844 = _S1719.domeControls_0.w;
            for(;;)
            {
                bool _S1845 = _S1842 <= 1.0f;
                _S4 = _S1845;
                if(_S1845)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S1844, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S1842)), level((0.0f)))).xyz;
                break;
            }
            float _S1846 = _S1836.environmentControls_0.y;
            float _S1847 = _S1836.environmentControls_0.z;
            for(;;)
            {
                if(_S4)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1848 = max(_S1846, 1.0f);
                    float _S1849 = _S1848 - 1.0f;
                    float slice_0 = _S1838 * max(_S1849, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S1847, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S1848)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S1849) + v_0) / _S1848)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S1850 = max(_S1846, 1.0f);
                float total_0 = _S1850 * _S1842;
                float _S1851 = _S1850 - 1.0f;
                float slice_1 = _S1838 * max(_S1851, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S1847, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S1850;
                specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S1851) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S1852 = max(_S1846, 1.0f);
                        float _S1853 = _S1852 - 1.0f;
                        float slice_2 = saturate(_S1707) * max(_S1853, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S1847, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S1852)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S1853) + v_2) / _S1852)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1854 = max(_S1846, 1.0f);
                    float total_1 = _S1854 * _S1842;
                    float _S1855 = _S1854 - 1.0f;
                    float slice_3 = saturate(_S1707) * max(_S1855, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S1847, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S1854;
                    diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S1855) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightBitangent_0 = diffuseFresnel_0;
            }
            else
            {
                lightBitangent_0 = _S1843;
            }
            lightDirection_0 = diffuseColor_0;
            lightTangent_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightDirection_0 = _S1843;
            lightTangent_0 = _S1843;
            lightBitangent_0 = _S1843;
            for(;;)
            {
                if(sampleIndex_0 < _S1720)
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
                float _S1856 = _S1719.domeControls_0.w;
                for(;;)
                {
                    bool _S1857 = _S1842 <= 1.0f;
                    _S5 = _S1857;
                    if(_S1857)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S1856, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S1842)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightDirection_0 + diffuseColor_0;
                float _S1858 = _S1836.environmentControls_0.y;
                float _S1859 = _S1836.environmentControls_0.z;
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1860 = max(_S1858, 1.0f);
                        float _S1861 = _S1860 - 1.0f;
                        float slice_4 = _S1838 * max(_S1861, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S1859, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S1860)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S1861) + v_4) / _S1860)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1862 = max(_S1858, 1.0f);
                    float total_2 = _S1862 * _S1842;
                    float _S1863 = _S1862 - 1.0f;
                    float slice_5 = _S1838 * max(_S1863, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S1859, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S1862;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S1863) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightTangent_0 + specularColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S5)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S1864 = max(_S1858, 1.0f);
                            float _S1865 = _S1864 - 1.0f;
                            float slice_6 = saturate(_S1707) * max(_S1865, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S1859, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S1864)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S1865) + v_6) / _S1864)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1866 = max(_S1858, 1.0f);
                        float total_3 = _S1866 * _S1842;
                        float _S1867 = _S1866 - 1.0f;
                        float slice_7 = saturate(_S1707) * max(_S1867, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S1859, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S1866;
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S1867) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S1868 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S1837, saturate(_S1707))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightBitangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S1868.x)  + float3(_S1868.y) );
        }
        else
        {
            color_3 = color_4;
        }
    }
    float3 color_5 = (color_3 + unlitColor_0) * float3(exp2((as_type<float>((_S3.z))))) ;
    if((_S3.y) == 1U)
    {
        color_3 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_3 = color_5;
    }
    pixelOutput_0 _S1869 = { float4(color_3, opacity_0) };
    return _S1869;
}
