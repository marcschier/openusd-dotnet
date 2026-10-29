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
    uint lightCount_0;
    float roughness_0;
    float metallic_0;
    float2 _S182;
    float2 _S183;
    bool hasSceneLighting_0;
    float4 _S184;
    uint3 _S185;
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
        bool _S186 = (udimMask_0 & 2U) != 0U;
        float4 _S187 = float4(_S6->domeLinkControls_0) ;
        uint maskU_0 = uint(round(_S187.y));
        uint maskV_0 = uint(round(_S187.z));
        bool _S188 = (maskU_0 & 2U) != 0U;
        if(_S188)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S189 = (maskV_0 & 2U) != 0U;
        if(_S189)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S190 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S186)
            {
                for(;;)
                {
                    if(_S190 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S191 = (&kernelContext_0)->baseColorTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S191).get_width(0)),(*((&height_0)) = (_S191).get_height(0));
                    float2 size_0 = float2(float(width_0), float(height_0));
                    float2 _S192 = float2(0.5f) ;
                    float2 remapped_0 = clamp((_S182 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S192 / size_0, (size_0 - _S192) / size_0);
                    if((_S190 & 1U) != 0U)
                    {
                        roughness_0 = remapped_0.x;
                    }
                    else
                    {
                        roughness_0 = _S182.x;
                    }
                    if((_S190 & 2U) != 0U)
                    {
                        metallic_0 = remapped_0.y;
                    }
                    else
                    {
                        metallic_0 = _S182.y;
                    }
                    _S183 = float2(roughness_0, metallic_0);
                    break;
                }
                _S184 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S183)));
                break;
            }
            texture2d<float, access::sample> _S193 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S193).get_width(0)),(*((&atlasHeight_0)) = (_S193).get_height(0));
            int3 _S194 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S194)).xy), uint(((_S194)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S195 = atlasWidth_0;
                uint _S196 = 2U % atlasWidth_0;
                int _S197 = int(_S196);
                uint _S198 = 2U / _S195;
                int3 _S199 = int3(_S197, int(_S198), int(0));
                int _S200 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S199)).xy), uint(((_S199)).z))).x * 255.0f)));
                uint _S201 = atlasWidth_0;
                uint _S202 = 3U % atlasWidth_0;
                int _S203 = int(_S202);
                uint _S204 = 3U / _S201;
                int3 _S205 = int3(_S203, int(_S204), int(0));
                int2 minimumTile_0 = int2(_S200, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S205)).xy), uint(((_S205)).z))).x * 255.0f))));
                uint _S206 = atlasWidth_0;
                uint _S207 = 4U % atlasWidth_0;
                int _S208 = int(_S207);
                uint _S209 = 4U / _S206;
                int3 _S210 = int3(_S208, int(_S209), int(0));
                uint _S211 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S210)).xy), uint(((_S210)).z))).x * 255.0f));
                uint _S212 = atlasWidth_0;
                uint _S213 = 5U % atlasWidth_0;
                int _S214 = int(_S213);
                uint _S215 = 5U / _S212;
                int3 _S216 = int3(_S214, int(_S215), int(0));
                uint _S217 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S216)).xy), uint(((_S216)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S211, _S217);
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
                    int3 _S218 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S218)).xy), uint(((_S218)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S211 + uint(tile_0.x));
                uint _S219 = atlasWidth_0;
                uint _S220 = offset_0 % atlasWidth_0;
                int _S221 = int(_S220);
                uint _S222 = offset_0 / _S219;
                int3 _S223 = int3(_S221, int(_S222), int(0));
                uint _S224 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S223)).xy), uint(((_S223)).z))).x * 255.0f));
                uint _S225 = atlasWidth_0;
                uint _S226 = offset_0 + 1U;
                uint _S227 = _S226 % atlasWidth_0;
                int _S228 = int(_S227);
                uint _S229 = _S226 / _S225;
                int3 _S230 = int3(_S228, int(_S229), int(0));
                uint slot_0 = _S224 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S230)).xy), uint(((_S230)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S231 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S231)).xy), uint(((_S231)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S232 = atlasWidth_0;
                uint _S233 = 6U % atlasWidth_0;
                int _S234 = int(_S233);
                uint _S235 = 6U / _S232;
                int3 _S236 = int3(_S234, int(_S235), int(0));
                uint _S237 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S236)).xy), uint(((_S236)).z))).x * 255.0f));
                uint _S238 = atlasWidth_0;
                uint _S239 = 7U % atlasWidth_0;
                int _S240 = int(_S239);
                uint _S241 = 7U / _S238;
                int3 _S242 = int3(_S240, int(_S241), int(0));
                uint columns_0 = _S237 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S242)).xy), uint(((_S242)).z))).x * 255.0f)) << 8U);
                uint _S243 = atlasWidth_0;
                uint _S244 = 8U % atlasWidth_0;
                int _S245 = int(_S244);
                uint _S246 = 8U / _S243;
                int3 _S247 = int3(_S245, int(_S246), int(0));
                uint _S248 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S247)).xy), uint(((_S247)).z))).x * 255.0f));
                uint _S249 = atlasWidth_0;
                uint _S250 = 9U % atlasWidth_0;
                int _S251 = int(_S250);
                uint _S252 = 9U / _S249;
                int3 _S253 = int3(_S251, int(_S252), int(0));
                uint rows_0 = ((_S248 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S253)).xy), uint(((_S253)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S211 * _S217 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S254 = atlasWidth_0 / columns_0;
                float _S255 = float(_S254);
                uint _S256 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S255, float(_S256));
                uint _S257 = slot_1 % columns_0;
                float _S258 = float(_S257);
                uint _S259 = slot_1 / columns_0;
                _S184 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S258, float(_S259)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S182) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S260 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(_S182)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S260);
            }
            if(hasSceneLighting_0)
            {
                int3 _S261 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S184 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S261)).xy), uint(((_S261)).z)));
                break;
            }
            uint _S262 = atlasWidth_0 / uint(_S260.x);
            float _S263 = float(_S262);
            uint _S264 = (atlasHeight_0 - 1U) / uint(_S260.y);
            float2 cellSize_1 = float2(_S263, float(_S264));
            _S184 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S184;
        float4 _S265 = float4(_S6->compositeControls_0) ;
        float _S266 = _S265.x;
        if(_S266 == 0.0f)
        {
            hasSceneLighting_0 = (_S265.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S267 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S267).get_width(0)),(*((&sourceHeight_0)) = (_S267).get_height(0));
            texture2d<float, access::sample> _S268 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S268).get_width(0)),(*((&valueHeight_0)) = (_S268).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S269 = diffuseSample_0;
                for(;;)
                {
                    float _S270 = _S269.w;
                    if(_S270 > 1.0f)
                    {
                        diffuseColor_0 = _S269.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S271 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S271).get_width(0)),(*((&height_1)) = (_S271).get_height(0));
                    uint3 _S272 = uint3(round(_S269.xyz * float3(255.0f) ));
                    uint index_1 = ((_S272.x) | ((_S272.y) << 8U)) | ((_S272.z) << 16U);
                    if(_S270 == 0.0f)
                    {
                        _S185 = _S272;
                    }
                    else
                    {
                        _S185 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S185 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S273 = address_0.x;
                    uint _S274 = _S273 % width_1;
                    int _S275 = int(_S274);
                    uint _S276 = _S273 / width_1;
                    int3 _S277 = int3(_S275, int(_S276), int(0));
                    float _S278 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S277)).xy), uint(((_S277)).z))).x;
                    uint _S279 = address_0.y;
                    uint _S280 = _S279 % width_1;
                    int _S281 = int(_S280);
                    uint _S282 = _S279 / width_1;
                    int3 _S283 = int3(_S281, int(_S282), int(0));
                    float _S284 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S283)).xy), uint(((_S283)).z))).x;
                    uint _S285 = address_0.z;
                    uint _S286 = _S285 % width_1;
                    int _S287 = int(_S286);
                    uint _S288 = _S285 / width_1;
                    int3 _S289 = int3(_S287, int(_S288), int(0));
                    diffuseColor_0 = float3(_S278, _S284, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S289)).xy), uint(((_S289)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                if(_S188)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S189)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S290 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S290 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S291 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S291).get_width(0)),(*((&height_2)) = (_S291).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S292 = float2(0.5f) ;
                    float2 remapped_1 = clamp((_S182 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S292 / size_1, (size_1 - _S292) / size_1);
                    if((_S290 & 1U) != 0U)
                    {
                        roughness_0 = remapped_1.x;
                    }
                    else
                    {
                        roughness_0 = _S182.x;
                    }
                    if((_S290 & 2U) != 0U)
                    {
                        metallic_0 = remapped_1.y;
                    }
                    else
                    {
                        metallic_0 = _S182.y;
                    }
                    _S183 = float2(roughness_0, metallic_0);
                    break;
                }
                diffuseSample_0.z = (((&kernelContext_0)->diffuseValuesTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S183))).x;
            }
        }
        float4 _S293 = diffuseSample_0;
        for(;;)
        {
            if(_S266 != 2.0f)
            {
                _S184 = _S293;
                break;
            }
            bool _S294 = (_S265.w) >= 0.5f;
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
            uint _S295 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S294)
                {
                    for(;;)
                    {
                        if(_S295 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S296 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_3;
                        thread uint height_3;
                        (*((&width_3)) = (_S296).get_width(0)),(*((&height_3)) = (_S296).get_height(0));
                        float2 size_2 = float2(float(width_3), float(height_3));
                        float2 _S297 = float2(0.5f) ;
                        float2 remapped_2 = clamp((_S182 * (size_2 - float2(2.0f) ) + float2(1.0f) ) / size_2, _S297 / size_2, (size_2 - _S297) / size_2);
                        if((_S295 & 1U) != 0U)
                        {
                            roughness_0 = remapped_2.x;
                        }
                        else
                        {
                            roughness_0 = _S182.x;
                        }
                        if((_S295 & 2U) != 0U)
                        {
                            metallic_0 = remapped_2.y;
                        }
                        else
                        {
                            metallic_0 = _S182.y;
                        }
                        _S182 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S184 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S298 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S298).get_width(0)),(*((&atlasHeight_1)) = (_S298).get_height(0));
                int3 _S299 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S299)).xy), uint(((_S299)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S300 = atlasWidth_1;
                    uint _S301 = 2U % atlasWidth_1;
                    int _S302 = int(_S301);
                    uint _S303 = 2U / _S300;
                    int3 _S304 = int3(_S302, int(_S303), int(0));
                    int _S305 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S304)).xy), uint(((_S304)).z))).x * 255.0f)));
                    uint _S306 = atlasWidth_1;
                    uint _S307 = 3U % atlasWidth_1;
                    int _S308 = int(_S307);
                    uint _S309 = 3U / _S306;
                    int3 _S310 = int3(_S308, int(_S309), int(0));
                    int2 minimumTile_1 = int2(_S305, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S310)).xy), uint(((_S310)).z))).x * 255.0f))));
                    uint _S311 = atlasWidth_1;
                    uint _S312 = 4U % atlasWidth_1;
                    int _S313 = int(_S312);
                    uint _S314 = 4U / _S311;
                    int3 _S315 = int3(_S313, int(_S314), int(0));
                    uint _S316 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S315)).xy), uint(((_S315)).z))).x * 255.0f));
                    uint _S317 = atlasWidth_1;
                    uint _S318 = 5U % atlasWidth_1;
                    int _S319 = int(_S318);
                    uint _S320 = 5U / _S317;
                    int3 _S321 = int3(_S319, int(_S320), int(0));
                    uint _S322 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S321)).xy), uint(((_S321)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S316, _S322);
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
                        int3 _S323 = int3(int(1), int(0), int(0));
                        _S184 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S323)).xy), uint(((_S323)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S316 + uint(tile_2.x));
                    uint _S324 = atlasWidth_1;
                    uint _S325 = offset_1 % atlasWidth_1;
                    int _S326 = int(_S325);
                    uint _S327 = offset_1 / _S324;
                    int3 _S328 = int3(_S326, int(_S327), int(0));
                    uint _S329 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S328)).xy), uint(((_S328)).z))).x * 255.0f));
                    uint _S330 = atlasWidth_1;
                    uint _S331 = offset_1 + 1U;
                    uint _S332 = _S331 % atlasWidth_1;
                    int _S333 = int(_S332);
                    uint _S334 = _S331 / _S330;
                    int3 _S335 = int3(_S333, int(_S334), int(0));
                    uint slot_2 = _S329 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S335)).xy), uint(((_S335)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S336 = int3(int(1), int(0), int(0));
                        _S184 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S336)).xy), uint(((_S336)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S337 = atlasWidth_1;
                    uint _S338 = 6U % atlasWidth_1;
                    int _S339 = int(_S338);
                    uint _S340 = 6U / _S337;
                    int3 _S341 = int3(_S339, int(_S340), int(0));
                    uint _S342 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S341)).xy), uint(((_S341)).z))).x * 255.0f));
                    uint _S343 = atlasWidth_1;
                    uint _S344 = 7U % atlasWidth_1;
                    int _S345 = int(_S344);
                    uint _S346 = 7U / _S343;
                    int3 _S347 = int3(_S345, int(_S346), int(0));
                    uint columns_1 = _S342 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S347)).xy), uint(((_S347)).z))).x * 255.0f)) << 8U);
                    uint _S348 = atlasWidth_1;
                    uint _S349 = 8U % atlasWidth_1;
                    int _S350 = int(_S349);
                    uint _S351 = 8U / _S348;
                    int3 _S352 = int3(_S350, int(_S351), int(0));
                    uint _S353 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S352)).xy), uint(((_S352)).z))).x * 255.0f));
                    uint _S354 = atlasWidth_1;
                    uint _S355 = 9U % atlasWidth_1;
                    int _S356 = int(_S355);
                    uint _S357 = 9U / _S354;
                    int3 _S358 = int3(_S356, int(_S357), int(0));
                    uint rows_1 = ((_S353 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S358)).xy), uint(((_S358)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S316 * _S322 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S359 = atlasWidth_1 / columns_1;
                    float _S360 = float(_S359);
                    uint _S361 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S360, float(_S361));
                    uint _S362 = slot_3 % columns_1;
                    float _S363 = float(_S362);
                    uint _S364 = slot_3 / columns_1;
                    _S184 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S363, float(_S364)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S182) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S365 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(_S182)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S365);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S366 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S184 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S366)).xy), uint(((_S366)).z)));
                    break;
                }
                uint _S367 = atlasWidth_1 / uint(_S365.x);
                float _S368 = float(_S367);
                uint _S369 = (atlasHeight_1 - 1U) / uint(_S365.y);
                float2 cellSize_3 = float2(_S368, float(_S369));
                _S184 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S265.y));
            if(operation_0 == 1U)
            {
                _S184 = _S293 * _S184;
                break;
            }
            if(operation_0 == 2U)
            {
                _S184 = _S293 + _S184;
                break;
            }
            if(operation_0 == 3U)
            {
                _S184 = _S293 - _S184;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S265.z;
                _S184 = _S293 * float4((1.0f - factor_0))  + _S184 * float4(factor_0) ;
                break;
            }
            _S184 = _S293;
            break;
        }
        diffuseColor_0 = _S184.xyz;
    }
    float4 _S370;
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
        bool _S371 = (udimMask_0 & 8U) != 0U;
        float4 _S372 = float4(_S6->domeLinkControls_0) ;
        uint maskU_1 = uint(round(_S372.y));
        uint maskV_1 = uint(round(_S372.z));
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
        uint _S373 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S371)
            {
                for(;;)
                {
                    if(_S373 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S374 = (&kernelContext_0)->roughnessMetallicTexture_0;
                    thread uint width_4;
                    thread uint height_4;
                    (*((&width_4)) = (_S374).get_width(0)),(*((&height_4)) = (_S374).get_height(0));
                    float2 size_3 = float2(float(width_4), float(height_4));
                    float2 _S375 = float2(0.5f) ;
                    float2 remapped_3 = clamp((_S182 * (size_3 - float2(2.0f) ) + float2(1.0f) ) / size_3, _S375 / size_3, (size_3 - _S375) / size_3);
                    if((_S373 & 1U) != 0U)
                    {
                        roughness_0 = remapped_3.x;
                    }
                    else
                    {
                        roughness_0 = _S182.x;
                    }
                    if((_S373 & 2U) != 0U)
                    {
                        metallic_0 = remapped_3.y;
                    }
                    else
                    {
                        metallic_0 = _S182.y;
                    }
                    _S183 = float2(roughness_0, metallic_0);
                    break;
                }
                _S184 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S183)));
                break;
            }
            texture2d<float, access::sample> _S376 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S376).get_width(0)),(*((&atlasHeight_2)) = (_S376).get_height(0));
            int3 _S377 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S377)).xy), uint(((_S377)).z))) * float4(255.0f) );
            if((metadata_2.x) == 255.0f)
            {
                uint _S378 = atlasWidth_2;
                uint _S379 = 2U % atlasWidth_2;
                int _S380 = int(_S379);
                uint _S381 = 2U / _S378;
                int3 _S382 = int3(_S380, int(_S381), int(0));
                int _S383 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S382)).xy), uint(((_S382)).z))).x * 255.0f)));
                uint _S384 = atlasWidth_2;
                uint _S385 = 3U % atlasWidth_2;
                int _S386 = int(_S385);
                uint _S387 = 3U / _S384;
                int3 _S388 = int3(_S386, int(_S387), int(0));
                int2 minimumTile_2 = int2(_S383, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S388)).xy), uint(((_S388)).z))).x * 255.0f))));
                uint _S389 = atlasWidth_2;
                uint _S390 = 4U % atlasWidth_2;
                int _S391 = int(_S390);
                uint _S392 = 4U / _S389;
                int3 _S393 = int3(_S391, int(_S392), int(0));
                uint _S394 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S393)).xy), uint(((_S393)).z))).x * 255.0f));
                uint _S395 = atlasWidth_2;
                uint _S396 = 5U % atlasWidth_2;
                int _S397 = int(_S396);
                uint _S398 = 5U / _S395;
                int3 _S399 = int3(_S397, int(_S398), int(0));
                uint _S400 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S399)).xy), uint(((_S399)).z))).x * 255.0f));
                uint2 grid_2 = uint2(_S394, _S400);
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
                    int3 _S401 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S401)).xy), uint(((_S401)).z)));
                    break;
                }
                uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S394 + uint(tile_4.x));
                uint _S402 = atlasWidth_2;
                uint _S403 = offset_2 % atlasWidth_2;
                int _S404 = int(_S403);
                uint _S405 = offset_2 / _S402;
                int3 _S406 = int3(_S404, int(_S405), int(0));
                uint _S407 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S406)).xy), uint(((_S406)).z))).x * 255.0f));
                uint _S408 = atlasWidth_2;
                uint _S409 = offset_2 + 1U;
                uint _S410 = _S409 % atlasWidth_2;
                int _S411 = int(_S410);
                uint _S412 = _S409 / _S408;
                int3 _S413 = int3(_S411, int(_S412), int(0));
                uint slot_4 = _S407 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S413)).xy), uint(((_S413)).z))).x * 255.0f)) << 8U);
                if(slot_4 == 0U)
                {
                    int3 _S414 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S414)).xy), uint(((_S414)).z)));
                    break;
                }
                uint slot_5 = slot_4 - 1U;
                uint _S415 = atlasWidth_2;
                uint _S416 = 6U % atlasWidth_2;
                int _S417 = int(_S416);
                uint _S418 = 6U / _S415;
                int3 _S419 = int3(_S417, int(_S418), int(0));
                uint _S420 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S419)).xy), uint(((_S419)).z))).x * 255.0f));
                uint _S421 = atlasWidth_2;
                uint _S422 = 7U % atlasWidth_2;
                int _S423 = int(_S422);
                uint _S424 = 7U / _S421;
                int3 _S425 = int3(_S423, int(_S424), int(0));
                uint columns_2 = _S420 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S425)).xy), uint(((_S425)).z))).x * 255.0f)) << 8U);
                uint _S426 = atlasWidth_2;
                uint _S427 = 8U % atlasWidth_2;
                int _S428 = int(_S427);
                uint _S429 = 8U / _S426;
                int3 _S430 = int3(_S428, int(_S429), int(0));
                uint _S431 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S430)).xy), uint(((_S430)).z))).x * 255.0f));
                uint _S432 = atlasWidth_2;
                uint _S433 = 9U % atlasWidth_2;
                int _S434 = int(_S433);
                uint _S435 = 9U / _S432;
                int3 _S436 = int3(_S434, int(_S435), int(0));
                uint rows_2 = ((_S431 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S436)).xy), uint(((_S436)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
                uint metadataRows_2 = (10U + 2U * _S394 * _S400 + atlasWidth_2 - 1U) / atlasWidth_2;
                uint _S437 = atlasWidth_2 / columns_2;
                float _S438 = float(_S437);
                uint _S439 = (atlasHeight_2 - metadataRows_2) / rows_2;
                float2 cellSize_4 = float2(_S438, float(_S439));
                uint _S440 = slot_5 % columns_2;
                float _S441 = float(_S440);
                uint _S442 = slot_5 / columns_2;
                _S184 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S441, float(_S442)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(_S182) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
                break;
            }
            int2 _S443 = int2(metadata_2.zw);
            int2 tile_5 = int2(floor(_S182)) - int2(metadata_2.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S443);
            }
            if(hasSceneLighting_0)
            {
                int3 _S444 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S184 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S444)).xy), uint(((_S444)).z)));
                break;
            }
            uint _S445 = atlasWidth_2 / uint(_S443.x);
            float _S446 = float(_S445);
            uint _S447 = (atlasHeight_2 - 1U) / uint(_S443.y);
            float2 cellSize_5 = float2(_S446, float(_S447));
            _S184 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        for(;;)
        {
            float4 _S448 = float4(_S6->compositeControls_0) ;
            if((_S448.x) != 8.0f)
            {
                break;
            }
            bool _S449 = (_S448.w) >= 0.5f;
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
            uint _S450 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S449)
                {
                    for(;;)
                    {
                        if(_S450 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S451 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_5;
                        thread uint height_5;
                        (*((&width_5)) = (_S451).get_width(0)),(*((&height_5)) = (_S451).get_height(0));
                        float2 size_4 = float2(float(width_5), float(height_5));
                        float2 _S452 = float2(0.5f) ;
                        float2 remapped_4 = clamp((_S182 * (size_4 - float2(2.0f) ) + float2(1.0f) ) / size_4, _S452 / size_4, (size_4 - _S452) / size_4);
                        if((_S450 & 1U) != 0U)
                        {
                            roughness_0 = remapped_4.x;
                        }
                        else
                        {
                            roughness_0 = _S182.x;
                        }
                        if((_S450 & 2U) != 0U)
                        {
                            metallic_0 = remapped_4.y;
                        }
                        else
                        {
                            metallic_0 = _S182.y;
                        }
                        _S182 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S453 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_3;
                thread uint atlasHeight_3;
                (*((&atlasWidth_3)) = (_S453).get_width(0)),(*((&atlasHeight_3)) = (_S453).get_height(0));
                int3 _S454 = int3(int(0), int(0), int(0));
                float4 metadata_3 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S454)).xy), uint(((_S454)).z))) * float4(255.0f) );
                if((metadata_3.x) == 255.0f)
                {
                    uint _S455 = atlasWidth_3;
                    uint _S456 = 2U % atlasWidth_3;
                    int _S457 = int(_S456);
                    uint _S458 = 2U / _S455;
                    int3 _S459 = int3(_S457, int(_S458), int(0));
                    int _S460 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S459)).xy), uint(((_S459)).z))).x * 255.0f)));
                    uint _S461 = atlasWidth_3;
                    uint _S462 = 3U % atlasWidth_3;
                    int _S463 = int(_S462);
                    uint _S464 = 3U / _S461;
                    int3 _S465 = int3(_S463, int(_S464), int(0));
                    int2 minimumTile_3 = int2(_S460, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S465)).xy), uint(((_S465)).z))).x * 255.0f))));
                    uint _S466 = atlasWidth_3;
                    uint _S467 = 4U % atlasWidth_3;
                    int _S468 = int(_S467);
                    uint _S469 = 4U / _S466;
                    int3 _S470 = int3(_S468, int(_S469), int(0));
                    uint _S471 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S470)).xy), uint(((_S470)).z))).x * 255.0f));
                    uint _S472 = atlasWidth_3;
                    uint _S473 = 5U % atlasWidth_3;
                    int _S474 = int(_S473);
                    uint _S475 = 5U / _S472;
                    int3 _S476 = int3(_S474, int(_S475), int(0));
                    uint _S477 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S476)).xy), uint(((_S476)).z))).x * 255.0f));
                    uint2 grid_3 = uint2(_S471, _S477);
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
                        int3 _S478 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S478)).xy), uint(((_S478)).z)));
                        break;
                    }
                    uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S471 + uint(tile_6.x));
                    uint _S479 = atlasWidth_3;
                    uint _S480 = offset_3 % atlasWidth_3;
                    int _S481 = int(_S480);
                    uint _S482 = offset_3 / _S479;
                    int3 _S483 = int3(_S481, int(_S482), int(0));
                    uint _S484 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S483)).xy), uint(((_S483)).z))).x * 255.0f));
                    uint _S485 = atlasWidth_3;
                    uint _S486 = offset_3 + 1U;
                    uint _S487 = _S486 % atlasWidth_3;
                    int _S488 = int(_S487);
                    uint _S489 = _S486 / _S485;
                    int3 _S490 = int3(_S488, int(_S489), int(0));
                    uint slot_6 = _S484 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S490)).xy), uint(((_S490)).z))).x * 255.0f)) << 8U);
                    if(slot_6 == 0U)
                    {
                        int3 _S491 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S491)).xy), uint(((_S491)).z)));
                        break;
                    }
                    uint slot_7 = slot_6 - 1U;
                    uint _S492 = atlasWidth_3;
                    uint _S493 = 6U % atlasWidth_3;
                    int _S494 = int(_S493);
                    uint _S495 = 6U / _S492;
                    int3 _S496 = int3(_S494, int(_S495), int(0));
                    uint _S497 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S496)).xy), uint(((_S496)).z))).x * 255.0f));
                    uint _S498 = atlasWidth_3;
                    uint _S499 = 7U % atlasWidth_3;
                    int _S500 = int(_S499);
                    uint _S501 = 7U / _S498;
                    int3 _S502 = int3(_S500, int(_S501), int(0));
                    uint columns_3 = _S497 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S502)).xy), uint(((_S502)).z))).x * 255.0f)) << 8U);
                    uint _S503 = atlasWidth_3;
                    uint _S504 = 8U % atlasWidth_3;
                    int _S505 = int(_S504);
                    uint _S506 = 8U / _S503;
                    int3 _S507 = int3(_S505, int(_S506), int(0));
                    uint _S508 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S507)).xy), uint(((_S507)).z))).x * 255.0f));
                    uint _S509 = atlasWidth_3;
                    uint _S510 = 9U % atlasWidth_3;
                    int _S511 = int(_S510);
                    uint _S512 = 9U / _S509;
                    int3 _S513 = int3(_S511, int(_S512), int(0));
                    uint rows_3 = ((_S508 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S513)).xy), uint(((_S513)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                    uint metadataRows_3 = (10U + 2U * _S471 * _S477 + atlasWidth_3 - 1U) / atlasWidth_3;
                    uint _S514 = atlasWidth_3 / columns_3;
                    float _S515 = float(_S514);
                    uint _S516 = (atlasHeight_3 - metadataRows_3) / rows_3;
                    float2 cellSize_6 = float2(_S515, float(_S516));
                    uint _S517 = slot_7 % columns_3;
                    float _S518 = float(_S517);
                    uint _S519 = slot_7 / columns_3;
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S518, float(_S519)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(_S182) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                    break;
                }
                int2 _S520 = int2(metadata_3.zw);
                int2 tile_7 = int2(floor(_S182)) - int2(metadata_3.xy);
                if(any(tile_7 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_7 >= _S520);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S521 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                    _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S521)).xy), uint(((_S521)).z)));
                    break;
                }
                uint _S522 = atlasWidth_3 / uint(_S520.x);
                float _S523 = float(_S522);
                uint _S524 = (atlasHeight_3 - 1U) / uint(_S520.y);
                float2 cellSize_7 = float2(_S523, float(_S524));
                _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            uint operation_1 = uint(round(_S448.y));
            if(operation_1 == 1U)
            {
                _S184 = _S184 * _S370;
                break;
            }
            if(operation_1 == 2U)
            {
                _S184 = _S184 + _S370;
                break;
            }
            if(operation_1 == 3U)
            {
                _S184 = _S184 - _S370;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S448.z;
                _S184 = _S184 * float4((1.0f - factor_1))  + _S370 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S184.x, 0.00999999977648258f, 1.0f);
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
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S525 = (udimMask_0 & 32U) != 0U;
        float4 _S526 = float4(_S6->domeLinkControls_0) ;
        uint maskU_2 = uint(round(_S526.y));
        uint maskV_2 = uint(round(_S526.z));
        if((maskU_2 & 32U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_2 & 32U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S527 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S525)
            {
                for(;;)
                {
                    if(_S527 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S528 = (&kernelContext_0)->metallicTexture_0;
                    thread uint width_6;
                    thread uint height_6;
                    (*((&width_6)) = (_S528).get_width(0)),(*((&height_6)) = (_S528).get_height(0));
                    float2 size_5 = float2(float(width_6), float(height_6));
                    float2 _S529 = float2(0.5f) ;
                    float2 remapped_5 = clamp((_S182 * (size_5 - float2(2.0f) ) + float2(1.0f) ) / size_5, _S529 / size_5, (size_5 - _S529) / size_5);
                    if((_S527 & 1U) != 0U)
                    {
                        metallic_0 = remapped_5.x;
                    }
                    else
                    {
                        metallic_0 = _S182.x;
                    }
                    if((_S527 & 2U) != 0U)
                    {
                        occlusion_0 = remapped_5.y;
                    }
                    else
                    {
                        occlusion_0 = _S182.y;
                    }
                    _S183 = float2(metallic_0, occlusion_0);
                    break;
                }
                _S184 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S183)));
                break;
            }
            texture2d<float, access::sample> _S530 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S530).get_width(0)),(*((&atlasHeight_4)) = (_S530).get_height(0));
            int3 _S531 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S531)).xy), uint(((_S531)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S532 = atlasWidth_4;
                uint _S533 = 2U % atlasWidth_4;
                int _S534 = int(_S533);
                uint _S535 = 2U / _S532;
                int3 _S536 = int3(_S534, int(_S535), int(0));
                int _S537 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S536)).xy), uint(((_S536)).z))).x * 255.0f)));
                uint _S538 = atlasWidth_4;
                uint _S539 = 3U % atlasWidth_4;
                int _S540 = int(_S539);
                uint _S541 = 3U / _S538;
                int3 _S542 = int3(_S540, int(_S541), int(0));
                int2 minimumTile_4 = int2(_S537, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S542)).xy), uint(((_S542)).z))).x * 255.0f))));
                uint _S543 = atlasWidth_4;
                uint _S544 = 4U % atlasWidth_4;
                int _S545 = int(_S544);
                uint _S546 = 4U / _S543;
                int3 _S547 = int3(_S545, int(_S546), int(0));
                uint _S548 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S547)).xy), uint(((_S547)).z))).x * 255.0f));
                uint _S549 = atlasWidth_4;
                uint _S550 = 5U % atlasWidth_4;
                int _S551 = int(_S550);
                uint _S552 = 5U / _S549;
                int3 _S553 = int3(_S551, int(_S552), int(0));
                uint _S554 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S553)).xy), uint(((_S553)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S548, _S554);
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
                    int3 _S555 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S555)).xy), uint(((_S555)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S548 + uint(tile_8.x));
                uint _S556 = atlasWidth_4;
                uint _S557 = offset_4 % atlasWidth_4;
                int _S558 = int(_S557);
                uint _S559 = offset_4 / _S556;
                int3 _S560 = int3(_S558, int(_S559), int(0));
                uint _S561 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S560)).xy), uint(((_S560)).z))).x * 255.0f));
                uint _S562 = atlasWidth_4;
                uint _S563 = offset_4 + 1U;
                uint _S564 = _S563 % atlasWidth_4;
                int _S565 = int(_S564);
                uint _S566 = _S563 / _S562;
                int3 _S567 = int3(_S565, int(_S566), int(0));
                uint slot_8 = _S561 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S567)).xy), uint(((_S567)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S568 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S568)).xy), uint(((_S568)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S569 = atlasWidth_4;
                uint _S570 = 6U % atlasWidth_4;
                int _S571 = int(_S570);
                uint _S572 = 6U / _S569;
                int3 _S573 = int3(_S571, int(_S572), int(0));
                uint _S574 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S573)).xy), uint(((_S573)).z))).x * 255.0f));
                uint _S575 = atlasWidth_4;
                uint _S576 = 7U % atlasWidth_4;
                int _S577 = int(_S576);
                uint _S578 = 7U / _S575;
                int3 _S579 = int3(_S577, int(_S578), int(0));
                uint columns_4 = _S574 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S579)).xy), uint(((_S579)).z))).x * 255.0f)) << 8U);
                uint _S580 = atlasWidth_4;
                uint _S581 = 8U % atlasWidth_4;
                int _S582 = int(_S581);
                uint _S583 = 8U / _S580;
                int3 _S584 = int3(_S582, int(_S583), int(0));
                uint _S585 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S584)).xy), uint(((_S584)).z))).x * 255.0f));
                uint _S586 = atlasWidth_4;
                uint _S587 = 9U % atlasWidth_4;
                int _S588 = int(_S587);
                uint _S589 = 9U / _S586;
                int3 _S590 = int3(_S588, int(_S589), int(0));
                uint rows_4 = ((_S585 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S590)).xy), uint(((_S590)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S548 * _S554 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S591 = atlasWidth_4 / columns_4;
                float _S592 = float(_S591);
                uint _S593 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S592, float(_S593));
                uint _S594 = slot_9 % columns_4;
                float _S595 = float(_S594);
                uint _S596 = slot_9 / columns_4;
                _S184 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S595, float(_S596)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(_S182) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S597 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(_S182)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S597);
            }
            if(hasSceneLighting_0)
            {
                int3 _S598 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S184 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S598)).xy), uint(((_S598)).z)));
                break;
            }
            uint _S599 = atlasWidth_4 / uint(_S597.x);
            float _S600 = float(_S599);
            uint _S601 = (atlasHeight_4 - 1U) / uint(_S597.y);
            float2 cellSize_9 = float2(_S600, float(_S601));
            _S184 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S602 = float4(_S6->compositeControls_0) ;
            if((_S602.x) != 32.0f)
            {
                break;
            }
            bool _S603 = (_S602.w) >= 0.5f;
            if((maskU_2 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_2 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S604 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S603)
                {
                    for(;;)
                    {
                        if(_S604 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S605 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_7;
                        thread uint height_7;
                        (*((&width_7)) = (_S605).get_width(0)),(*((&height_7)) = (_S605).get_height(0));
                        float2 size_6 = float2(float(width_7), float(height_7));
                        float2 _S606 = float2(0.5f) ;
                        float2 remapped_6 = clamp((_S182 * (size_6 - float2(2.0f) ) + float2(1.0f) ) / size_6, _S606 / size_6, (size_6 - _S606) / size_6);
                        if((_S604 & 1U) != 0U)
                        {
                            metallic_0 = remapped_6.x;
                        }
                        else
                        {
                            metallic_0 = _S182.x;
                        }
                        if((_S604 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_6.y;
                        }
                        else
                        {
                            occlusion_0 = _S182.y;
                        }
                        _S182 = float2(metallic_0, occlusion_0);
                        break;
                    }
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S607 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S607).get_width(0)),(*((&atlasHeight_5)) = (_S607).get_height(0));
                int3 _S608 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S608)).xy), uint(((_S608)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S609 = atlasWidth_5;
                    uint _S610 = 2U % atlasWidth_5;
                    int _S611 = int(_S610);
                    uint _S612 = 2U / _S609;
                    int3 _S613 = int3(_S611, int(_S612), int(0));
                    int _S614 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S613)).xy), uint(((_S613)).z))).x * 255.0f)));
                    uint _S615 = atlasWidth_5;
                    uint _S616 = 3U % atlasWidth_5;
                    int _S617 = int(_S616);
                    uint _S618 = 3U / _S615;
                    int3 _S619 = int3(_S617, int(_S618), int(0));
                    int2 minimumTile_5 = int2(_S614, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S619)).xy), uint(((_S619)).z))).x * 255.0f))));
                    uint _S620 = atlasWidth_5;
                    uint _S621 = 4U % atlasWidth_5;
                    int _S622 = int(_S621);
                    uint _S623 = 4U / _S620;
                    int3 _S624 = int3(_S622, int(_S623), int(0));
                    uint _S625 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S624)).xy), uint(((_S624)).z))).x * 255.0f));
                    uint _S626 = atlasWidth_5;
                    uint _S627 = 5U % atlasWidth_5;
                    int _S628 = int(_S627);
                    uint _S629 = 5U / _S626;
                    int3 _S630 = int3(_S628, int(_S629), int(0));
                    uint _S631 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S630)).xy), uint(((_S630)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S625, _S631);
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
                        int3 _S632 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S632)).xy), uint(((_S632)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S625 + uint(tile_10.x));
                    uint _S633 = atlasWidth_5;
                    uint _S634 = offset_5 % atlasWidth_5;
                    int _S635 = int(_S634);
                    uint _S636 = offset_5 / _S633;
                    int3 _S637 = int3(_S635, int(_S636), int(0));
                    uint _S638 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S637)).xy), uint(((_S637)).z))).x * 255.0f));
                    uint _S639 = atlasWidth_5;
                    uint _S640 = offset_5 + 1U;
                    uint _S641 = _S640 % atlasWidth_5;
                    int _S642 = int(_S641);
                    uint _S643 = _S640 / _S639;
                    int3 _S644 = int3(_S642, int(_S643), int(0));
                    uint slot_10 = _S638 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S644)).xy), uint(((_S644)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S645 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S645)).xy), uint(((_S645)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S646 = atlasWidth_5;
                    uint _S647 = 6U % atlasWidth_5;
                    int _S648 = int(_S647);
                    uint _S649 = 6U / _S646;
                    int3 _S650 = int3(_S648, int(_S649), int(0));
                    uint _S651 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S650)).xy), uint(((_S650)).z))).x * 255.0f));
                    uint _S652 = atlasWidth_5;
                    uint _S653 = 7U % atlasWidth_5;
                    int _S654 = int(_S653);
                    uint _S655 = 7U / _S652;
                    int3 _S656 = int3(_S654, int(_S655), int(0));
                    uint columns_5 = _S651 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S656)).xy), uint(((_S656)).z))).x * 255.0f)) << 8U);
                    uint _S657 = atlasWidth_5;
                    uint _S658 = 8U % atlasWidth_5;
                    int _S659 = int(_S658);
                    uint _S660 = 8U / _S657;
                    int3 _S661 = int3(_S659, int(_S660), int(0));
                    uint _S662 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S661)).xy), uint(((_S661)).z))).x * 255.0f));
                    uint _S663 = atlasWidth_5;
                    uint _S664 = 9U % atlasWidth_5;
                    int _S665 = int(_S664);
                    uint _S666 = 9U / _S663;
                    int3 _S667 = int3(_S665, int(_S666), int(0));
                    uint rows_5 = ((_S662 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S667)).xy), uint(((_S667)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S625 * _S631 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S668 = atlasWidth_5 / columns_5;
                    float _S669 = float(_S668);
                    uint _S670 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S669, float(_S670));
                    uint _S671 = slot_11 % columns_5;
                    float _S672 = float(_S671);
                    uint _S673 = slot_11 / columns_5;
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S672, float(_S673)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(_S182) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S674 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(_S182)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S674);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S675 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S675)).xy), uint(((_S675)).z)));
                    break;
                }
                uint _S676 = atlasWidth_5 / uint(_S674.x);
                float _S677 = float(_S676);
                uint _S678 = (atlasHeight_5 - 1U) / uint(_S674.y);
                float2 cellSize_11 = float2(_S677, float(_S678));
                _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_2 = uint(round(_S602.y));
            if(operation_2 == 1U)
            {
                _S184 = _S184 * _S370;
                break;
            }
            if(operation_2 == 2U)
            {
                _S184 = _S184 + _S370;
                break;
            }
            if(operation_2 == 3U)
            {
                _S184 = _S184 - _S370;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S602.z;
                _S184 = _S184 * float4((1.0f - factor_2))  + _S370 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S184.x);
    }
    else
    {
        metallic_0 = _S166;
    }
    float clearcoatAmount_0;
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
        bool _S679 = (udimMask_0 & 16U) != 0U;
        float4 _S680 = float4(_S6->domeLinkControls_0) ;
        uint maskU_3 = uint(round(_S680.y));
        uint maskV_3 = uint(round(_S680.z));
        bool _S681 = (maskU_3 & 16U) != 0U;
        if(_S681)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S682 = (maskV_3 & 16U) != 0U;
        if(_S682)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S683 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S679)
            {
                for(;;)
                {
                    if(_S683 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S684 = (&kernelContext_0)->emissiveTexture_0;
                    thread uint width_8;
                    thread uint height_8;
                    (*((&width_8)) = (_S684).get_width(0)),(*((&height_8)) = (_S684).get_height(0));
                    float2 size_7 = float2(float(width_8), float(height_8));
                    float2 _S685 = float2(0.5f) ;
                    float2 remapped_7 = clamp((_S182 * (size_7 - float2(2.0f) ) + float2(1.0f) ) / size_7, _S685 / size_7, (size_7 - _S685) / size_7);
                    if((_S683 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_7.x;
                    }
                    else
                    {
                        occlusion_0 = _S182.x;
                    }
                    if((_S683 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_7.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S182.y;
                    }
                    _S183 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S184 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S183)));
                break;
            }
            texture2d<float, access::sample> _S686 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S686).get_width(0)),(*((&atlasHeight_6)) = (_S686).get_height(0));
            int3 _S687 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S687)).xy), uint(((_S687)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S688 = atlasWidth_6;
                uint _S689 = 2U % atlasWidth_6;
                int _S690 = int(_S689);
                uint _S691 = 2U / _S688;
                int3 _S692 = int3(_S690, int(_S691), int(0));
                int _S693 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S692)).xy), uint(((_S692)).z))).x * 255.0f)));
                uint _S694 = atlasWidth_6;
                uint _S695 = 3U % atlasWidth_6;
                int _S696 = int(_S695);
                uint _S697 = 3U / _S694;
                int3 _S698 = int3(_S696, int(_S697), int(0));
                int2 minimumTile_6 = int2(_S693, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S698)).xy), uint(((_S698)).z))).x * 255.0f))));
                uint _S699 = atlasWidth_6;
                uint _S700 = 4U % atlasWidth_6;
                int _S701 = int(_S700);
                uint _S702 = 4U / _S699;
                int3 _S703 = int3(_S701, int(_S702), int(0));
                uint _S704 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S703)).xy), uint(((_S703)).z))).x * 255.0f));
                uint _S705 = atlasWidth_6;
                uint _S706 = 5U % atlasWidth_6;
                int _S707 = int(_S706);
                uint _S708 = 5U / _S705;
                int3 _S709 = int3(_S707, int(_S708), int(0));
                uint _S710 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S709)).xy), uint(((_S709)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S704, _S710);
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
                    int3 _S711 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S711)).xy), uint(((_S711)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S704 + uint(tile_12.x));
                uint _S712 = atlasWidth_6;
                uint _S713 = offset_6 % atlasWidth_6;
                int _S714 = int(_S713);
                uint _S715 = offset_6 / _S712;
                int3 _S716 = int3(_S714, int(_S715), int(0));
                uint _S717 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S716)).xy), uint(((_S716)).z))).x * 255.0f));
                uint _S718 = atlasWidth_6;
                uint _S719 = offset_6 + 1U;
                uint _S720 = _S719 % atlasWidth_6;
                int _S721 = int(_S720);
                uint _S722 = _S719 / _S718;
                int3 _S723 = int3(_S721, int(_S722), int(0));
                uint slot_12 = _S717 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S723)).xy), uint(((_S723)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S724 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S724)).xy), uint(((_S724)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S725 = atlasWidth_6;
                uint _S726 = 6U % atlasWidth_6;
                int _S727 = int(_S726);
                uint _S728 = 6U / _S725;
                int3 _S729 = int3(_S727, int(_S728), int(0));
                uint _S730 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S729)).xy), uint(((_S729)).z))).x * 255.0f));
                uint _S731 = atlasWidth_6;
                uint _S732 = 7U % atlasWidth_6;
                int _S733 = int(_S732);
                uint _S734 = 7U / _S731;
                int3 _S735 = int3(_S733, int(_S734), int(0));
                uint columns_6 = _S730 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S735)).xy), uint(((_S735)).z))).x * 255.0f)) << 8U);
                uint _S736 = atlasWidth_6;
                uint _S737 = 8U % atlasWidth_6;
                int _S738 = int(_S737);
                uint _S739 = 8U / _S736;
                int3 _S740 = int3(_S738, int(_S739), int(0));
                uint _S741 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S740)).xy), uint(((_S740)).z))).x * 255.0f));
                uint _S742 = atlasWidth_6;
                uint _S743 = 9U % atlasWidth_6;
                int _S744 = int(_S743);
                uint _S745 = 9U / _S742;
                int3 _S746 = int3(_S744, int(_S745), int(0));
                uint rows_6 = ((_S741 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S746)).xy), uint(((_S746)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S704 * _S710 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S747 = atlasWidth_6 / columns_6;
                float _S748 = float(_S747);
                uint _S749 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S748, float(_S749));
                uint _S750 = slot_13 % columns_6;
                float _S751 = float(_S750);
                uint _S752 = slot_13 / columns_6;
                _S184 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S751, float(_S752)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(_S182) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S753 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(_S182)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S753);
            }
            if(hasSceneLighting_0)
            {
                int3 _S754 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S184 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S754)).xy), uint(((_S754)).z)));
                break;
            }
            uint _S755 = atlasWidth_6 / uint(_S753.x);
            float _S756 = float(_S755);
            uint _S757 = (atlasHeight_6 - 1U) / uint(_S753.y);
            float2 cellSize_13 = float2(_S756, float(_S757));
            _S184 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        emissionSample_0 = _S184;
        if((_S174.w) != 0.0f)
        {
            texture2d<float, access::sample> _S758 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S758).get_width(0)),(*((&sourceHeight_1)) = (_S758).get_height(0));
            texture2d<float, access::sample> _S759 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S759).get_width(0)),(*((&valueHeight_1)) = (_S759).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S760 = emissionSample_0;
                for(;;)
                {
                    float _S761 = _S760.w;
                    if(_S761 > 1.0f)
                    {
                        unlitColor_0 = _S760.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S762 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_9;
                    thread uint height_9;
                    (*((&width_9)) = (_S762).get_width(0)),(*((&height_9)) = (_S762).get_height(0));
                    uint3 _S763 = uint3(round(_S760.xyz * float3(255.0f) ));
                    uint index_2 = ((_S763.x) | ((_S763.y) << 8U)) | ((_S763.z) << 16U);
                    if(_S761 == 0.0f)
                    {
                        _S185 = _S763;
                    }
                    else
                    {
                        _S185 = uint3(index_2, index_2, index_2);
                    }
                    uint3 address_1 = _S185 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S764 = address_1.x;
                    uint _S765 = _S764 % width_9;
                    int _S766 = int(_S765);
                    uint _S767 = _S764 / width_9;
                    int3 _S768 = int3(_S766, int(_S767), int(0));
                    float _S769 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S768)).xy), uint(((_S768)).z))).x;
                    uint _S770 = address_1.y;
                    uint _S771 = _S770 % width_9;
                    int _S772 = int(_S771);
                    uint _S773 = _S770 / width_9;
                    int3 _S774 = int3(_S772, int(_S773), int(0));
                    float _S775 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S774)).xy), uint(((_S774)).z))).x;
                    uint _S776 = address_1.z;
                    uint _S777 = _S776 % width_9;
                    int _S778 = int(_S777);
                    uint _S779 = _S776 / width_9;
                    int3 _S780 = int3(_S778, int(_S779), int(0));
                    unlitColor_0 = float3(_S769, _S775, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S780)).xy), uint(((_S780)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                if(_S681)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S682)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S781 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S781 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S782 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_10;
                    thread uint height_10;
                    (*((&width_10)) = (_S782).get_width(0)),(*((&height_10)) = (_S782).get_height(0));
                    float2 size_8 = float2(float(width_10), float(height_10));
                    float2 _S783 = float2(0.5f) ;
                    float2 remapped_8 = clamp((_S182 * (size_8 - float2(2.0f) ) + float2(1.0f) ) / size_8, _S783 / size_8, (size_8 - _S783) / size_8);
                    if((_S781 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_8.x;
                    }
                    else
                    {
                        occlusion_0 = _S182.x;
                    }
                    if((_S781 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_8.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S182.y;
                    }
                    _S183 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                emissionSample_0.z = (((&kernelContext_0)->emissiveValuesTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S183))).x;
            }
        }
        float4 _S784 = emissionSample_0;
        for(;;)
        {
            float4 _S785 = float4(_S6->compositeControls_0) ;
            if((_S785.x) != 16.0f)
            {
                _S184 = _S784;
                break;
            }
            bool _S786 = (_S785.w) >= 0.5f;
            if((maskU_3 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_3 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S787 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S786)
                {
                    for(;;)
                    {
                        if(_S787 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S788 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_11;
                        thread uint height_11;
                        (*((&width_11)) = (_S788).get_width(0)),(*((&height_11)) = (_S788).get_height(0));
                        float2 size_9 = float2(float(width_11), float(height_11));
                        float2 _S789 = float2(0.5f) ;
                        float2 remapped_9 = clamp((_S182 * (size_9 - float2(2.0f) ) + float2(1.0f) ) / size_9, _S789 / size_9, (size_9 - _S789) / size_9);
                        if((_S787 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_9.x;
                        }
                        else
                        {
                            occlusion_0 = _S182.x;
                        }
                        if((_S787 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_9.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S182.y;
                        }
                        _S182 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S184 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S790 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S790).get_width(0)),(*((&atlasHeight_7)) = (_S790).get_height(0));
                int3 _S791 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S791)).xy), uint(((_S791)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S792 = atlasWidth_7;
                    uint _S793 = 2U % atlasWidth_7;
                    int _S794 = int(_S793);
                    uint _S795 = 2U / _S792;
                    int3 _S796 = int3(_S794, int(_S795), int(0));
                    int _S797 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S796)).xy), uint(((_S796)).z))).x * 255.0f)));
                    uint _S798 = atlasWidth_7;
                    uint _S799 = 3U % atlasWidth_7;
                    int _S800 = int(_S799);
                    uint _S801 = 3U / _S798;
                    int3 _S802 = int3(_S800, int(_S801), int(0));
                    int2 minimumTile_7 = int2(_S797, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S802)).xy), uint(((_S802)).z))).x * 255.0f))));
                    uint _S803 = atlasWidth_7;
                    uint _S804 = 4U % atlasWidth_7;
                    int _S805 = int(_S804);
                    uint _S806 = 4U / _S803;
                    int3 _S807 = int3(_S805, int(_S806), int(0));
                    uint _S808 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S807)).xy), uint(((_S807)).z))).x * 255.0f));
                    uint _S809 = atlasWidth_7;
                    uint _S810 = 5U % atlasWidth_7;
                    int _S811 = int(_S810);
                    uint _S812 = 5U / _S809;
                    int3 _S813 = int3(_S811, int(_S812), int(0));
                    uint _S814 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S813)).xy), uint(((_S813)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S808, _S814);
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
                        int3 _S815 = int3(int(1), int(0), int(0));
                        _S184 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S815)).xy), uint(((_S815)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S808 + uint(tile_14.x));
                    uint _S816 = atlasWidth_7;
                    uint _S817 = offset_7 % atlasWidth_7;
                    int _S818 = int(_S817);
                    uint _S819 = offset_7 / _S816;
                    int3 _S820 = int3(_S818, int(_S819), int(0));
                    uint _S821 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S820)).xy), uint(((_S820)).z))).x * 255.0f));
                    uint _S822 = atlasWidth_7;
                    uint _S823 = offset_7 + 1U;
                    uint _S824 = _S823 % atlasWidth_7;
                    int _S825 = int(_S824);
                    uint _S826 = _S823 / _S822;
                    int3 _S827 = int3(_S825, int(_S826), int(0));
                    uint slot_14 = _S821 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S827)).xy), uint(((_S827)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S828 = int3(int(1), int(0), int(0));
                        _S184 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S828)).xy), uint(((_S828)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S829 = atlasWidth_7;
                    uint _S830 = 6U % atlasWidth_7;
                    int _S831 = int(_S830);
                    uint _S832 = 6U / _S829;
                    int3 _S833 = int3(_S831, int(_S832), int(0));
                    uint _S834 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S833)).xy), uint(((_S833)).z))).x * 255.0f));
                    uint _S835 = atlasWidth_7;
                    uint _S836 = 7U % atlasWidth_7;
                    int _S837 = int(_S836);
                    uint _S838 = 7U / _S835;
                    int3 _S839 = int3(_S837, int(_S838), int(0));
                    uint columns_7 = _S834 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S839)).xy), uint(((_S839)).z))).x * 255.0f)) << 8U);
                    uint _S840 = atlasWidth_7;
                    uint _S841 = 8U % atlasWidth_7;
                    int _S842 = int(_S841);
                    uint _S843 = 8U / _S840;
                    int3 _S844 = int3(_S842, int(_S843), int(0));
                    uint _S845 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S844)).xy), uint(((_S844)).z))).x * 255.0f));
                    uint _S846 = atlasWidth_7;
                    uint _S847 = 9U % atlasWidth_7;
                    int _S848 = int(_S847);
                    uint _S849 = 9U / _S846;
                    int3 _S850 = int3(_S848, int(_S849), int(0));
                    uint rows_7 = ((_S845 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S850)).xy), uint(((_S850)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S808 * _S814 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S851 = atlasWidth_7 / columns_7;
                    float _S852 = float(_S851);
                    uint _S853 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S852, float(_S853));
                    uint _S854 = slot_15 % columns_7;
                    float _S855 = float(_S854);
                    uint _S856 = slot_15 / columns_7;
                    _S184 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S855, float(_S856)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(_S182) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S857 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(_S182)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S857);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S858 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S184 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S858)).xy), uint(((_S858)).z)));
                    break;
                }
                uint _S859 = atlasWidth_7 / uint(_S857.x);
                float _S860 = float(_S859);
                uint _S861 = (atlasHeight_7 - 1U) / uint(_S857.y);
                float2 cellSize_15 = float2(_S860, float(_S861));
                _S184 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_3 = uint(round(_S785.y));
            if(operation_3 == 1U)
            {
                _S184 = _S784 * _S184;
                break;
            }
            if(operation_3 == 2U)
            {
                _S184 = _S784 + _S184;
                break;
            }
            if(operation_3 == 3U)
            {
                _S184 = _S784 - _S184;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S785.z;
                _S184 = _S784 * float4((1.0f - factor_3))  + _S184 * float4(factor_3) ;
                break;
            }
            _S184 = _S784;
            break;
        }
        unlitColor_0 = _S184.xyz;
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
        bool _S862 = (udimMask_0 & 64U) != 0U;
        float4 _S863 = float4(_S6->domeLinkControls_0) ;
        uint maskU_4 = uint(round(_S863.y));
        uint maskV_4 = uint(round(_S863.z));
        if((maskU_4 & 64U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_4 & 64U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S864 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S862)
            {
                for(;;)
                {
                    if(_S864 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S865 = (&kernelContext_0)->opacityTexture_0;
                    thread uint width_12;
                    thread uint height_12;
                    (*((&width_12)) = (_S865).get_width(0)),(*((&height_12)) = (_S865).get_height(0));
                    float2 size_10 = float2(float(width_12), float(height_12));
                    float2 _S866 = float2(0.5f) ;
                    float2 remapped_10 = clamp((_S182 * (size_10 - float2(2.0f) ) + float2(1.0f) ) / size_10, _S866 / size_10, (size_10 - _S866) / size_10);
                    if((_S864 & 1U) != 0U)
                    {
                        opacity_0 = remapped_10.x;
                    }
                    else
                    {
                        opacity_0 = _S182.x;
                    }
                    if((_S864 & 2U) != 0U)
                    {
                        occlusion_0 = remapped_10.y;
                    }
                    else
                    {
                        occlusion_0 = _S182.y;
                    }
                    _S183 = float2(opacity_0, occlusion_0);
                    break;
                }
                _S184 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S183)));
                break;
            }
            texture2d<float, access::sample> _S867 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S867).get_width(0)),(*((&atlasHeight_8)) = (_S867).get_height(0));
            int3 _S868 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S868)).xy), uint(((_S868)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S869 = atlasWidth_8;
                uint _S870 = 2U % atlasWidth_8;
                int _S871 = int(_S870);
                uint _S872 = 2U / _S869;
                int3 _S873 = int3(_S871, int(_S872), int(0));
                int _S874 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S873)).xy), uint(((_S873)).z))).x * 255.0f)));
                uint _S875 = atlasWidth_8;
                uint _S876 = 3U % atlasWidth_8;
                int _S877 = int(_S876);
                uint _S878 = 3U / _S875;
                int3 _S879 = int3(_S877, int(_S878), int(0));
                int2 minimumTile_8 = int2(_S874, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S879)).xy), uint(((_S879)).z))).x * 255.0f))));
                uint _S880 = atlasWidth_8;
                uint _S881 = 4U % atlasWidth_8;
                int _S882 = int(_S881);
                uint _S883 = 4U / _S880;
                int3 _S884 = int3(_S882, int(_S883), int(0));
                uint _S885 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S884)).xy), uint(((_S884)).z))).x * 255.0f));
                uint _S886 = atlasWidth_8;
                uint _S887 = 5U % atlasWidth_8;
                int _S888 = int(_S887);
                uint _S889 = 5U / _S886;
                int3 _S890 = int3(_S888, int(_S889), int(0));
                uint _S891 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S890)).xy), uint(((_S890)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S885, _S891);
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
                    int3 _S892 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S892)).xy), uint(((_S892)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S885 + uint(tile_16.x));
                uint _S893 = atlasWidth_8;
                uint _S894 = offset_8 % atlasWidth_8;
                int _S895 = int(_S894);
                uint _S896 = offset_8 / _S893;
                int3 _S897 = int3(_S895, int(_S896), int(0));
                uint _S898 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S897)).xy), uint(((_S897)).z))).x * 255.0f));
                uint _S899 = atlasWidth_8;
                uint _S900 = offset_8 + 1U;
                uint _S901 = _S900 % atlasWidth_8;
                int _S902 = int(_S901);
                uint _S903 = _S900 / _S899;
                int3 _S904 = int3(_S902, int(_S903), int(0));
                uint slot_16 = _S898 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S904)).xy), uint(((_S904)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S905 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S905)).xy), uint(((_S905)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S906 = atlasWidth_8;
                uint _S907 = 6U % atlasWidth_8;
                int _S908 = int(_S907);
                uint _S909 = 6U / _S906;
                int3 _S910 = int3(_S908, int(_S909), int(0));
                uint _S911 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S910)).xy), uint(((_S910)).z))).x * 255.0f));
                uint _S912 = atlasWidth_8;
                uint _S913 = 7U % atlasWidth_8;
                int _S914 = int(_S913);
                uint _S915 = 7U / _S912;
                int3 _S916 = int3(_S914, int(_S915), int(0));
                uint columns_8 = _S911 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S916)).xy), uint(((_S916)).z))).x * 255.0f)) << 8U);
                uint _S917 = atlasWidth_8;
                uint _S918 = 8U % atlasWidth_8;
                int _S919 = int(_S918);
                uint _S920 = 8U / _S917;
                int3 _S921 = int3(_S919, int(_S920), int(0));
                uint _S922 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S921)).xy), uint(((_S921)).z))).x * 255.0f));
                uint _S923 = atlasWidth_8;
                uint _S924 = 9U % atlasWidth_8;
                int _S925 = int(_S924);
                uint _S926 = 9U / _S923;
                int3 _S927 = int3(_S925, int(_S926), int(0));
                uint rows_8 = ((_S922 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S927)).xy), uint(((_S927)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S885 * _S891 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S928 = atlasWidth_8 / columns_8;
                float _S929 = float(_S928);
                uint _S930 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S929, float(_S930));
                uint _S931 = slot_17 % columns_8;
                float _S932 = float(_S931);
                uint _S933 = slot_17 / columns_8;
                _S184 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S932, float(_S933)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(_S182) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S934 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(_S182)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S934);
            }
            if(hasSceneLighting_0)
            {
                int3 _S935 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S184 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S935)).xy), uint(((_S935)).z)));
                break;
            }
            uint _S936 = atlasWidth_8 / uint(_S934.x);
            float _S937 = float(_S936);
            uint _S938 = (atlasHeight_8 - 1U) / uint(_S934.y);
            float2 cellSize_17 = float2(_S937, float(_S938));
            _S184 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        for(;;)
        {
            float4 _S939 = float4(_S6->compositeControls_0) ;
            if((_S939.x) != 64.0f)
            {
                break;
            }
            bool _S940 = (_S939.w) >= 0.5f;
            if((maskU_4 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_4 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S941 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S940)
                {
                    for(;;)
                    {
                        if(_S941 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S942 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_13;
                        thread uint height_13;
                        (*((&width_13)) = (_S942).get_width(0)),(*((&height_13)) = (_S942).get_height(0));
                        float2 size_11 = float2(float(width_13), float(height_13));
                        float2 _S943 = float2(0.5f) ;
                        float2 remapped_11 = clamp((_S182 * (size_11 - float2(2.0f) ) + float2(1.0f) ) / size_11, _S943 / size_11, (size_11 - _S943) / size_11);
                        if((_S941 & 1U) != 0U)
                        {
                            opacity_0 = remapped_11.x;
                        }
                        else
                        {
                            opacity_0 = _S182.x;
                        }
                        if((_S941 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_11.y;
                        }
                        else
                        {
                            occlusion_0 = _S182.y;
                        }
                        _S182 = float2(opacity_0, occlusion_0);
                        break;
                    }
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S944 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S944).get_width(0)),(*((&atlasHeight_9)) = (_S944).get_height(0));
                int3 _S945 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S945)).xy), uint(((_S945)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S946 = atlasWidth_9;
                    uint _S947 = 2U % atlasWidth_9;
                    int _S948 = int(_S947);
                    uint _S949 = 2U / _S946;
                    int3 _S950 = int3(_S948, int(_S949), int(0));
                    int _S951 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S950)).xy), uint(((_S950)).z))).x * 255.0f)));
                    uint _S952 = atlasWidth_9;
                    uint _S953 = 3U % atlasWidth_9;
                    int _S954 = int(_S953);
                    uint _S955 = 3U / _S952;
                    int3 _S956 = int3(_S954, int(_S955), int(0));
                    int2 minimumTile_9 = int2(_S951, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S956)).xy), uint(((_S956)).z))).x * 255.0f))));
                    uint _S957 = atlasWidth_9;
                    uint _S958 = 4U % atlasWidth_9;
                    int _S959 = int(_S958);
                    uint _S960 = 4U / _S957;
                    int3 _S961 = int3(_S959, int(_S960), int(0));
                    uint _S962 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S961)).xy), uint(((_S961)).z))).x * 255.0f));
                    uint _S963 = atlasWidth_9;
                    uint _S964 = 5U % atlasWidth_9;
                    int _S965 = int(_S964);
                    uint _S966 = 5U / _S963;
                    int3 _S967 = int3(_S965, int(_S966), int(0));
                    uint _S968 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S967)).xy), uint(((_S967)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S962, _S968);
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
                        int3 _S969 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S969)).xy), uint(((_S969)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S962 + uint(tile_18.x));
                    uint _S970 = atlasWidth_9;
                    uint _S971 = offset_9 % atlasWidth_9;
                    int _S972 = int(_S971);
                    uint _S973 = offset_9 / _S970;
                    int3 _S974 = int3(_S972, int(_S973), int(0));
                    uint _S975 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S974)).xy), uint(((_S974)).z))).x * 255.0f));
                    uint _S976 = atlasWidth_9;
                    uint _S977 = offset_9 + 1U;
                    uint _S978 = _S977 % atlasWidth_9;
                    int _S979 = int(_S978);
                    uint _S980 = _S977 / _S976;
                    int3 _S981 = int3(_S979, int(_S980), int(0));
                    uint slot_18 = _S975 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S981)).xy), uint(((_S981)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S982 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S982)).xy), uint(((_S982)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S983 = atlasWidth_9;
                    uint _S984 = 6U % atlasWidth_9;
                    int _S985 = int(_S984);
                    uint _S986 = 6U / _S983;
                    int3 _S987 = int3(_S985, int(_S986), int(0));
                    uint _S988 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S987)).xy), uint(((_S987)).z))).x * 255.0f));
                    uint _S989 = atlasWidth_9;
                    uint _S990 = 7U % atlasWidth_9;
                    int _S991 = int(_S990);
                    uint _S992 = 7U / _S989;
                    int3 _S993 = int3(_S991, int(_S992), int(0));
                    uint columns_9 = _S988 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S993)).xy), uint(((_S993)).z))).x * 255.0f)) << 8U);
                    uint _S994 = atlasWidth_9;
                    uint _S995 = 8U % atlasWidth_9;
                    int _S996 = int(_S995);
                    uint _S997 = 8U / _S994;
                    int3 _S998 = int3(_S996, int(_S997), int(0));
                    uint _S999 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S998)).xy), uint(((_S998)).z))).x * 255.0f));
                    uint _S1000 = atlasWidth_9;
                    uint _S1001 = 9U % atlasWidth_9;
                    int _S1002 = int(_S1001);
                    uint _S1003 = 9U / _S1000;
                    int3 _S1004 = int3(_S1002, int(_S1003), int(0));
                    uint rows_9 = ((_S999 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1004)).xy), uint(((_S1004)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S962 * _S968 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S1005 = atlasWidth_9 / columns_9;
                    float _S1006 = float(_S1005);
                    uint _S1007 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S1006, float(_S1007));
                    uint _S1008 = slot_19 % columns_9;
                    float _S1009 = float(_S1008);
                    uint _S1010 = slot_19 / columns_9;
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1009, float(_S1010)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(_S182) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S1011 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(_S182)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S1011);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1012 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1012)).xy), uint(((_S1012)).z)));
                    break;
                }
                uint _S1013 = atlasWidth_9 / uint(_S1011.x);
                float _S1014 = float(_S1013);
                uint _S1015 = (atlasHeight_9 - 1U) / uint(_S1011.y);
                float2 cellSize_19 = float2(_S1014, float(_S1015));
                _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_4 = uint(round(_S939.y));
            if(operation_4 == 1U)
            {
                _S184 = _S184 * _S370;
                break;
            }
            if(operation_4 == 2U)
            {
                _S184 = _S184 + _S370;
                break;
            }
            if(operation_4 == 3U)
            {
                _S184 = _S184 - _S370;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S939.z;
                _S184 = _S184 * float4((1.0f - factor_4))  + _S370 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S184.x);
    }
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
        bool _S1016 = (udimMask_0 & 128U) != 0U;
        float4 _S1017 = float4(_S6->domeLinkControls_0) ;
        uint maskU_5 = uint(round(_S1017.y));
        uint maskV_5 = uint(round(_S1017.z));
        if((maskU_5 & 128U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_5 & 128U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1018 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1016)
            {
                for(;;)
                {
                    if(_S1018 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S1019 = (&kernelContext_0)->occlusionTexture_0;
                    thread uint width_14;
                    thread uint height_14;
                    (*((&width_14)) = (_S1019).get_width(0)),(*((&height_14)) = (_S1019).get_height(0));
                    float2 size_12 = float2(float(width_14), float(height_14));
                    float2 _S1020 = float2(0.5f) ;
                    float2 remapped_12 = clamp((_S182 * (size_12 - float2(2.0f) ) + float2(1.0f) ) / size_12, _S1020 / size_12, (size_12 - _S1020) / size_12);
                    if((_S1018 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_12.x;
                    }
                    else
                    {
                        occlusion_0 = _S182.x;
                    }
                    if((_S1018 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_12.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S182.y;
                    }
                    _S183 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S184 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S183)));
                break;
            }
            texture2d<float, access::sample> _S1021 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S1021).get_width(0)),(*((&atlasHeight_10)) = (_S1021).get_height(0));
            int3 _S1022 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1022)).xy), uint(((_S1022)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S1023 = atlasWidth_10;
                uint _S1024 = 2U % atlasWidth_10;
                int _S1025 = int(_S1024);
                uint _S1026 = 2U / _S1023;
                int3 _S1027 = int3(_S1025, int(_S1026), int(0));
                int _S1028 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1027)).xy), uint(((_S1027)).z))).x * 255.0f)));
                uint _S1029 = atlasWidth_10;
                uint _S1030 = 3U % atlasWidth_10;
                int _S1031 = int(_S1030);
                uint _S1032 = 3U / _S1029;
                int3 _S1033 = int3(_S1031, int(_S1032), int(0));
                int2 minimumTile_10 = int2(_S1028, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1033)).xy), uint(((_S1033)).z))).x * 255.0f))));
                uint _S1034 = atlasWidth_10;
                uint _S1035 = 4U % atlasWidth_10;
                int _S1036 = int(_S1035);
                uint _S1037 = 4U / _S1034;
                int3 _S1038 = int3(_S1036, int(_S1037), int(0));
                uint _S1039 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1038)).xy), uint(((_S1038)).z))).x * 255.0f));
                uint _S1040 = atlasWidth_10;
                uint _S1041 = 5U % atlasWidth_10;
                int _S1042 = int(_S1041);
                uint _S1043 = 5U / _S1040;
                int3 _S1044 = int3(_S1042, int(_S1043), int(0));
                uint _S1045 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1044)).xy), uint(((_S1044)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S1039, _S1045);
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
                    int3 _S1046 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1046)).xy), uint(((_S1046)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1039 + uint(tile_20.x));
                uint _S1047 = atlasWidth_10;
                uint _S1048 = offset_10 % atlasWidth_10;
                int _S1049 = int(_S1048);
                uint _S1050 = offset_10 / _S1047;
                int3 _S1051 = int3(_S1049, int(_S1050), int(0));
                uint _S1052 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1051)).xy), uint(((_S1051)).z))).x * 255.0f));
                uint _S1053 = atlasWidth_10;
                uint _S1054 = offset_10 + 1U;
                uint _S1055 = _S1054 % atlasWidth_10;
                int _S1056 = int(_S1055);
                uint _S1057 = _S1054 / _S1053;
                int3 _S1058 = int3(_S1056, int(_S1057), int(0));
                uint slot_20 = _S1052 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1058)).xy), uint(((_S1058)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1059 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1059)).xy), uint(((_S1059)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1060 = atlasWidth_10;
                uint _S1061 = 6U % atlasWidth_10;
                int _S1062 = int(_S1061);
                uint _S1063 = 6U / _S1060;
                int3 _S1064 = int3(_S1062, int(_S1063), int(0));
                uint _S1065 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1064)).xy), uint(((_S1064)).z))).x * 255.0f));
                uint _S1066 = atlasWidth_10;
                uint _S1067 = 7U % atlasWidth_10;
                int _S1068 = int(_S1067);
                uint _S1069 = 7U / _S1066;
                int3 _S1070 = int3(_S1068, int(_S1069), int(0));
                uint columns_10 = _S1065 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1070)).xy), uint(((_S1070)).z))).x * 255.0f)) << 8U);
                uint _S1071 = atlasWidth_10;
                uint _S1072 = 8U % atlasWidth_10;
                int _S1073 = int(_S1072);
                uint _S1074 = 8U / _S1071;
                int3 _S1075 = int3(_S1073, int(_S1074), int(0));
                uint _S1076 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1075)).xy), uint(((_S1075)).z))).x * 255.0f));
                uint _S1077 = atlasWidth_10;
                uint _S1078 = 9U % atlasWidth_10;
                int _S1079 = int(_S1078);
                uint _S1080 = 9U / _S1077;
                int3 _S1081 = int3(_S1079, int(_S1080), int(0));
                uint rows_10 = ((_S1076 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1081)).xy), uint(((_S1081)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S1039 * _S1045 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1082 = atlasWidth_10 / columns_10;
                float _S1083 = float(_S1082);
                uint _S1084 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1083, float(_S1084));
                uint _S1085 = slot_21 % columns_10;
                float _S1086 = float(_S1085);
                uint _S1087 = slot_21 / columns_10;
                _S184 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1086, float(_S1087)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(_S182) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1088 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(_S182)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1088);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1089 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S184 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1089)).xy), uint(((_S1089)).z)));
                break;
            }
            uint _S1090 = atlasWidth_10 / uint(_S1088.x);
            float _S1091 = float(_S1090);
            uint _S1092 = (atlasHeight_10 - 1U) / uint(_S1088.y);
            float2 cellSize_21 = float2(_S1091, float(_S1092));
            _S184 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1093 = float4(_S6->compositeControls_0) ;
            if((_S1093.x) != 128.0f)
            {
                break;
            }
            bool _S1094 = (_S1093.w) >= 0.5f;
            if((maskU_5 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_5 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1095 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1094)
                {
                    for(;;)
                    {
                        if(_S1095 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1096 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_15;
                        thread uint height_15;
                        (*((&width_15)) = (_S1096).get_width(0)),(*((&height_15)) = (_S1096).get_height(0));
                        float2 size_13 = float2(float(width_15), float(height_15));
                        float2 _S1097 = float2(0.5f) ;
                        float2 remapped_13 = clamp((_S182 * (size_13 - float2(2.0f) ) + float2(1.0f) ) / size_13, _S1097 / size_13, (size_13 - _S1097) / size_13);
                        if((_S1095 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_13.x;
                        }
                        else
                        {
                            occlusion_0 = _S182.x;
                        }
                        if((_S1095 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_13.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S182.y;
                        }
                        _S182 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S1098 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1098).get_width(0)),(*((&atlasHeight_11)) = (_S1098).get_height(0));
                int3 _S1099 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1099)).xy), uint(((_S1099)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1100 = atlasWidth_11;
                    uint _S1101 = 2U % atlasWidth_11;
                    int _S1102 = int(_S1101);
                    uint _S1103 = 2U / _S1100;
                    int3 _S1104 = int3(_S1102, int(_S1103), int(0));
                    int _S1105 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1104)).xy), uint(((_S1104)).z))).x * 255.0f)));
                    uint _S1106 = atlasWidth_11;
                    uint _S1107 = 3U % atlasWidth_11;
                    int _S1108 = int(_S1107);
                    uint _S1109 = 3U / _S1106;
                    int3 _S1110 = int3(_S1108, int(_S1109), int(0));
                    int2 minimumTile_11 = int2(_S1105, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1110)).xy), uint(((_S1110)).z))).x * 255.0f))));
                    uint _S1111 = atlasWidth_11;
                    uint _S1112 = 4U % atlasWidth_11;
                    int _S1113 = int(_S1112);
                    uint _S1114 = 4U / _S1111;
                    int3 _S1115 = int3(_S1113, int(_S1114), int(0));
                    uint _S1116 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1115)).xy), uint(((_S1115)).z))).x * 255.0f));
                    uint _S1117 = atlasWidth_11;
                    uint _S1118 = 5U % atlasWidth_11;
                    int _S1119 = int(_S1118);
                    uint _S1120 = 5U / _S1117;
                    int3 _S1121 = int3(_S1119, int(_S1120), int(0));
                    uint _S1122 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1121)).xy), uint(((_S1121)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1116, _S1122);
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
                        int3 _S1123 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1123)).xy), uint(((_S1123)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1116 + uint(tile_22.x));
                    uint _S1124 = atlasWidth_11;
                    uint _S1125 = offset_11 % atlasWidth_11;
                    int _S1126 = int(_S1125);
                    uint _S1127 = offset_11 / _S1124;
                    int3 _S1128 = int3(_S1126, int(_S1127), int(0));
                    uint _S1129 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1128)).xy), uint(((_S1128)).z))).x * 255.0f));
                    uint _S1130 = atlasWidth_11;
                    uint _S1131 = offset_11 + 1U;
                    uint _S1132 = _S1131 % atlasWidth_11;
                    int _S1133 = int(_S1132);
                    uint _S1134 = _S1131 / _S1130;
                    int3 _S1135 = int3(_S1133, int(_S1134), int(0));
                    uint slot_22 = _S1129 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1135)).xy), uint(((_S1135)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1136 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1136)).xy), uint(((_S1136)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1137 = atlasWidth_11;
                    uint _S1138 = 6U % atlasWidth_11;
                    int _S1139 = int(_S1138);
                    uint _S1140 = 6U / _S1137;
                    int3 _S1141 = int3(_S1139, int(_S1140), int(0));
                    uint _S1142 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1141)).xy), uint(((_S1141)).z))).x * 255.0f));
                    uint _S1143 = atlasWidth_11;
                    uint _S1144 = 7U % atlasWidth_11;
                    int _S1145 = int(_S1144);
                    uint _S1146 = 7U / _S1143;
                    int3 _S1147 = int3(_S1145, int(_S1146), int(0));
                    uint columns_11 = _S1142 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1147)).xy), uint(((_S1147)).z))).x * 255.0f)) << 8U);
                    uint _S1148 = atlasWidth_11;
                    uint _S1149 = 8U % atlasWidth_11;
                    int _S1150 = int(_S1149);
                    uint _S1151 = 8U / _S1148;
                    int3 _S1152 = int3(_S1150, int(_S1151), int(0));
                    uint _S1153 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1152)).xy), uint(((_S1152)).z))).x * 255.0f));
                    uint _S1154 = atlasWidth_11;
                    uint _S1155 = 9U % atlasWidth_11;
                    int _S1156 = int(_S1155);
                    uint _S1157 = 9U / _S1154;
                    int3 _S1158 = int3(_S1156, int(_S1157), int(0));
                    uint rows_11 = ((_S1153 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1158)).xy), uint(((_S1158)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1116 * _S1122 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1159 = atlasWidth_11 / columns_11;
                    float _S1160 = float(_S1159);
                    uint _S1161 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1160, float(_S1161));
                    uint _S1162 = slot_23 % columns_11;
                    float _S1163 = float(_S1162);
                    uint _S1164 = slot_23 / columns_11;
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1163, float(_S1164)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(_S182) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1165 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(_S182)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1165);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1166 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1166)).xy), uint(((_S1166)).z)));
                    break;
                }
                uint _S1167 = atlasWidth_11 / uint(_S1165.x);
                float _S1168 = float(_S1167);
                uint _S1169 = (atlasHeight_11 - 1U) / uint(_S1165.y);
                float2 cellSize_23 = float2(_S1168, float(_S1169));
                _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_5 = uint(round(_S1093.y));
            if(operation_5 == 1U)
            {
                _S184 = _S184 * _S370;
                break;
            }
            if(operation_5 == 2U)
            {
                _S184 = _S184 + _S370;
                break;
            }
            if(operation_5 == 3U)
            {
                _S184 = _S184 - _S370;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1093.z;
                _S184 = _S184 * float4((1.0f - factor_5))  + _S370 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S184.x);
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
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S1170 = (udimMask_0 & 256U) != 0U;
        float4 _S1171 = float4(_S6->domeLinkControls_0) ;
        uint maskU_6 = uint(round(_S1171.y));
        uint maskV_6 = uint(round(_S1171.z));
        if((maskU_6 & 256U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_6 & 256U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1172 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1170)
            {
                for(;;)
                {
                    if(_S1172 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S1173 = (&kernelContext_0)->specularColorTexture_0;
                    thread uint width_16;
                    thread uint height_16;
                    (*((&width_16)) = (_S1173).get_width(0)),(*((&height_16)) = (_S1173).get_height(0));
                    float2 size_14 = float2(float(width_16), float(height_16));
                    float2 _S1174 = float2(0.5f) ;
                    float2 remapped_14 = clamp((_S182 * (size_14 - float2(2.0f) ) + float2(1.0f) ) / size_14, _S1174 / size_14, (size_14 - _S1174) / size_14);
                    if((_S1172 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_14.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S182.x;
                    }
                    if((_S1172 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_14.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S182.y;
                    }
                    _S183 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S184 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S183)));
                break;
            }
            texture2d<float, access::sample> _S1175 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1175).get_width(0)),(*((&atlasHeight_12)) = (_S1175).get_height(0));
            int3 _S1176 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1176)).xy), uint(((_S1176)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1177 = atlasWidth_12;
                uint _S1178 = 2U % atlasWidth_12;
                int _S1179 = int(_S1178);
                uint _S1180 = 2U / _S1177;
                int3 _S1181 = int3(_S1179, int(_S1180), int(0));
                int _S1182 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1181)).xy), uint(((_S1181)).z))).x * 255.0f)));
                uint _S1183 = atlasWidth_12;
                uint _S1184 = 3U % atlasWidth_12;
                int _S1185 = int(_S1184);
                uint _S1186 = 3U / _S1183;
                int3 _S1187 = int3(_S1185, int(_S1186), int(0));
                int2 minimumTile_12 = int2(_S1182, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1187)).xy), uint(((_S1187)).z))).x * 255.0f))));
                uint _S1188 = atlasWidth_12;
                uint _S1189 = 4U % atlasWidth_12;
                int _S1190 = int(_S1189);
                uint _S1191 = 4U / _S1188;
                int3 _S1192 = int3(_S1190, int(_S1191), int(0));
                uint _S1193 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1192)).xy), uint(((_S1192)).z))).x * 255.0f));
                uint _S1194 = atlasWidth_12;
                uint _S1195 = 5U % atlasWidth_12;
                int _S1196 = int(_S1195);
                uint _S1197 = 5U / _S1194;
                int3 _S1198 = int3(_S1196, int(_S1197), int(0));
                uint _S1199 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1198)).xy), uint(((_S1198)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1193, _S1199);
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
                    int3 _S1200 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1200)).xy), uint(((_S1200)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1193 + uint(tile_24.x));
                uint _S1201 = atlasWidth_12;
                uint _S1202 = offset_12 % atlasWidth_12;
                int _S1203 = int(_S1202);
                uint _S1204 = offset_12 / _S1201;
                int3 _S1205 = int3(_S1203, int(_S1204), int(0));
                uint _S1206 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1205)).xy), uint(((_S1205)).z))).x * 255.0f));
                uint _S1207 = atlasWidth_12;
                uint _S1208 = offset_12 + 1U;
                uint _S1209 = _S1208 % atlasWidth_12;
                int _S1210 = int(_S1209);
                uint _S1211 = _S1208 / _S1207;
                int3 _S1212 = int3(_S1210, int(_S1211), int(0));
                uint slot_24 = _S1206 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1212)).xy), uint(((_S1212)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1213 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1213)).xy), uint(((_S1213)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1214 = atlasWidth_12;
                uint _S1215 = 6U % atlasWidth_12;
                int _S1216 = int(_S1215);
                uint _S1217 = 6U / _S1214;
                int3 _S1218 = int3(_S1216, int(_S1217), int(0));
                uint _S1219 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1218)).xy), uint(((_S1218)).z))).x * 255.0f));
                uint _S1220 = atlasWidth_12;
                uint _S1221 = 7U % atlasWidth_12;
                int _S1222 = int(_S1221);
                uint _S1223 = 7U / _S1220;
                int3 _S1224 = int3(_S1222, int(_S1223), int(0));
                uint columns_12 = _S1219 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1224)).xy), uint(((_S1224)).z))).x * 255.0f)) << 8U);
                uint _S1225 = atlasWidth_12;
                uint _S1226 = 8U % atlasWidth_12;
                int _S1227 = int(_S1226);
                uint _S1228 = 8U / _S1225;
                int3 _S1229 = int3(_S1227, int(_S1228), int(0));
                uint _S1230 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1229)).xy), uint(((_S1229)).z))).x * 255.0f));
                uint _S1231 = atlasWidth_12;
                uint _S1232 = 9U % atlasWidth_12;
                int _S1233 = int(_S1232);
                uint _S1234 = 9U / _S1231;
                int3 _S1235 = int3(_S1233, int(_S1234), int(0));
                uint rows_12 = ((_S1230 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1235)).xy), uint(((_S1235)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1193 * _S1199 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1236 = atlasWidth_12 / columns_12;
                float _S1237 = float(_S1236);
                uint _S1238 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1237, float(_S1238));
                uint _S1239 = slot_25 % columns_12;
                float _S1240 = float(_S1239);
                uint _S1241 = slot_25 / columns_12;
                _S184 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1240, float(_S1241)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(_S182) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1242 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(_S182)) - int2(metadata_12.xy);
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
                _S184 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1243)).xy), uint(((_S1243)).z)));
                break;
            }
            uint _S1244 = atlasWidth_12 / uint(_S1242.x);
            float _S1245 = float(_S1244);
            uint _S1246 = (atlasHeight_12 - 1U) / uint(_S1242.y);
            float2 cellSize_25 = float2(_S1245, float(_S1246));
            _S184 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1247 = float4(_S6->compositeControls_0) ;
            if((_S1247.x) != 256.0f)
            {
                break;
            }
            bool _S1248 = (_S1247.w) >= 0.5f;
            if((maskU_6 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_6 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1249 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1248)
                {
                    for(;;)
                    {
                        if(_S1249 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1250 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_17;
                        thread uint height_17;
                        (*((&width_17)) = (_S1250).get_width(0)),(*((&height_17)) = (_S1250).get_height(0));
                        float2 size_15 = float2(float(width_17), float(height_17));
                        float2 _S1251 = float2(0.5f) ;
                        float2 remapped_15 = clamp((_S182 * (size_15 - float2(2.0f) ) + float2(1.0f) ) / size_15, _S1251 / size_15, (size_15 - _S1251) / size_15);
                        if((_S1249 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_15.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S182.x;
                        }
                        if((_S1249 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_15.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S182.y;
                        }
                        _S182 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S1252 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1252).get_width(0)),(*((&atlasHeight_13)) = (_S1252).get_height(0));
                int3 _S1253 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1253)).xy), uint(((_S1253)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1254 = atlasWidth_13;
                    uint _S1255 = 2U % atlasWidth_13;
                    int _S1256 = int(_S1255);
                    uint _S1257 = 2U / _S1254;
                    int3 _S1258 = int3(_S1256, int(_S1257), int(0));
                    int _S1259 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1258)).xy), uint(((_S1258)).z))).x * 255.0f)));
                    uint _S1260 = atlasWidth_13;
                    uint _S1261 = 3U % atlasWidth_13;
                    int _S1262 = int(_S1261);
                    uint _S1263 = 3U / _S1260;
                    int3 _S1264 = int3(_S1262, int(_S1263), int(0));
                    int2 minimumTile_13 = int2(_S1259, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1264)).xy), uint(((_S1264)).z))).x * 255.0f))));
                    uint _S1265 = atlasWidth_13;
                    uint _S1266 = 4U % atlasWidth_13;
                    int _S1267 = int(_S1266);
                    uint _S1268 = 4U / _S1265;
                    int3 _S1269 = int3(_S1267, int(_S1268), int(0));
                    uint _S1270 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1269)).xy), uint(((_S1269)).z))).x * 255.0f));
                    uint _S1271 = atlasWidth_13;
                    uint _S1272 = 5U % atlasWidth_13;
                    int _S1273 = int(_S1272);
                    uint _S1274 = 5U / _S1271;
                    int3 _S1275 = int3(_S1273, int(_S1274), int(0));
                    uint _S1276 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1275)).xy), uint(((_S1275)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1270, _S1276);
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
                        int3 _S1277 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1277)).xy), uint(((_S1277)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1270 + uint(tile_26.x));
                    uint _S1278 = atlasWidth_13;
                    uint _S1279 = offset_13 % atlasWidth_13;
                    int _S1280 = int(_S1279);
                    uint _S1281 = offset_13 / _S1278;
                    int3 _S1282 = int3(_S1280, int(_S1281), int(0));
                    uint _S1283 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1282)).xy), uint(((_S1282)).z))).x * 255.0f));
                    uint _S1284 = atlasWidth_13;
                    uint _S1285 = offset_13 + 1U;
                    uint _S1286 = _S1285 % atlasWidth_13;
                    int _S1287 = int(_S1286);
                    uint _S1288 = _S1285 / _S1284;
                    int3 _S1289 = int3(_S1287, int(_S1288), int(0));
                    uint slot_26 = _S1283 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1289)).xy), uint(((_S1289)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1290 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1290)).xy), uint(((_S1290)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1291 = atlasWidth_13;
                    uint _S1292 = 6U % atlasWidth_13;
                    int _S1293 = int(_S1292);
                    uint _S1294 = 6U / _S1291;
                    int3 _S1295 = int3(_S1293, int(_S1294), int(0));
                    uint _S1296 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1295)).xy), uint(((_S1295)).z))).x * 255.0f));
                    uint _S1297 = atlasWidth_13;
                    uint _S1298 = 7U % atlasWidth_13;
                    int _S1299 = int(_S1298);
                    uint _S1300 = 7U / _S1297;
                    int3 _S1301 = int3(_S1299, int(_S1300), int(0));
                    uint columns_13 = _S1296 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1301)).xy), uint(((_S1301)).z))).x * 255.0f)) << 8U);
                    uint _S1302 = atlasWidth_13;
                    uint _S1303 = 8U % atlasWidth_13;
                    int _S1304 = int(_S1303);
                    uint _S1305 = 8U / _S1302;
                    int3 _S1306 = int3(_S1304, int(_S1305), int(0));
                    uint _S1307 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1306)).xy), uint(((_S1306)).z))).x * 255.0f));
                    uint _S1308 = atlasWidth_13;
                    uint _S1309 = 9U % atlasWidth_13;
                    int _S1310 = int(_S1309);
                    uint _S1311 = 9U / _S1308;
                    int3 _S1312 = int3(_S1310, int(_S1311), int(0));
                    uint rows_13 = ((_S1307 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1312)).xy), uint(((_S1312)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1270 * _S1276 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1313 = atlasWidth_13 / columns_13;
                    float _S1314 = float(_S1313);
                    uint _S1315 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1314, float(_S1315));
                    uint _S1316 = slot_27 % columns_13;
                    float _S1317 = float(_S1316);
                    uint _S1318 = slot_27 / columns_13;
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1317, float(_S1318)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(_S182) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1319 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(_S182)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1319);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1320 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1320)).xy), uint(((_S1320)).z)));
                    break;
                }
                uint _S1321 = atlasWidth_13 / uint(_S1319.x);
                float _S1322 = float(_S1321);
                uint _S1323 = (atlasHeight_13 - 1U) / uint(_S1319.y);
                float2 cellSize_27 = float2(_S1322, float(_S1323));
                _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_6 = uint(round(_S1247.y));
            if(operation_6 == 1U)
            {
                _S184 = _S184 * _S370;
                break;
            }
            if(operation_6 == 2U)
            {
                _S184 = _S184 + _S370;
                break;
            }
            if(operation_6 == 3U)
            {
                _S184 = _S184 - _S370;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1247.z;
                _S184 = _S184 * float4((1.0f - factor_6))  + _S370 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S184.xyz);
    }
    else
    {
        specularColor_0 = _S169;
    }
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
        bool _S1324 = (udimMask_0 & 512U) != 0U;
        float4 _S1325 = float4(_S6->domeLinkControls_0) ;
        uint maskU_7 = uint(round(_S1325.y));
        uint maskV_7 = uint(round(_S1325.z));
        if((maskU_7 & 512U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_7 & 512U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1326 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1324)
            {
                for(;;)
                {
                    if(_S1326 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S1327 = (&kernelContext_0)->clearcoatTexture_0;
                    thread uint width_18;
                    thread uint height_18;
                    (*((&width_18)) = (_S1327).get_width(0)),(*((&height_18)) = (_S1327).get_height(0));
                    float2 size_16 = float2(float(width_18), float(height_18));
                    float2 _S1328 = float2(0.5f) ;
                    float2 remapped_16 = clamp((_S182 * (size_16 - float2(2.0f) ) + float2(1.0f) ) / size_16, _S1328 / size_16, (size_16 - _S1328) / size_16);
                    if((_S1326 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_16.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S182.x;
                    }
                    if((_S1326 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_16.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S182.y;
                    }
                    _S183 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S184 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S183)));
                break;
            }
            texture2d<float, access::sample> _S1329 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1329).get_width(0)),(*((&atlasHeight_14)) = (_S1329).get_height(0));
            int3 _S1330 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1330)).xy), uint(((_S1330)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1331 = atlasWidth_14;
                uint _S1332 = 2U % atlasWidth_14;
                int _S1333 = int(_S1332);
                uint _S1334 = 2U / _S1331;
                int3 _S1335 = int3(_S1333, int(_S1334), int(0));
                int _S1336 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1335)).xy), uint(((_S1335)).z))).x * 255.0f)));
                uint _S1337 = atlasWidth_14;
                uint _S1338 = 3U % atlasWidth_14;
                int _S1339 = int(_S1338);
                uint _S1340 = 3U / _S1337;
                int3 _S1341 = int3(_S1339, int(_S1340), int(0));
                int2 minimumTile_14 = int2(_S1336, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1341)).xy), uint(((_S1341)).z))).x * 255.0f))));
                uint _S1342 = atlasWidth_14;
                uint _S1343 = 4U % atlasWidth_14;
                int _S1344 = int(_S1343);
                uint _S1345 = 4U / _S1342;
                int3 _S1346 = int3(_S1344, int(_S1345), int(0));
                uint _S1347 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1346)).xy), uint(((_S1346)).z))).x * 255.0f));
                uint _S1348 = atlasWidth_14;
                uint _S1349 = 5U % atlasWidth_14;
                int _S1350 = int(_S1349);
                uint _S1351 = 5U / _S1348;
                int3 _S1352 = int3(_S1350, int(_S1351), int(0));
                uint _S1353 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1352)).xy), uint(((_S1352)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1347, _S1353);
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
                    int3 _S1354 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1354)).xy), uint(((_S1354)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1347 + uint(tile_28.x));
                uint _S1355 = atlasWidth_14;
                uint _S1356 = offset_14 % atlasWidth_14;
                int _S1357 = int(_S1356);
                uint _S1358 = offset_14 / _S1355;
                int3 _S1359 = int3(_S1357, int(_S1358), int(0));
                uint _S1360 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1359)).xy), uint(((_S1359)).z))).x * 255.0f));
                uint _S1361 = atlasWidth_14;
                uint _S1362 = offset_14 + 1U;
                uint _S1363 = _S1362 % atlasWidth_14;
                int _S1364 = int(_S1363);
                uint _S1365 = _S1362 / _S1361;
                int3 _S1366 = int3(_S1364, int(_S1365), int(0));
                uint slot_28 = _S1360 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1366)).xy), uint(((_S1366)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1367 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1367)).xy), uint(((_S1367)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1368 = atlasWidth_14;
                uint _S1369 = 6U % atlasWidth_14;
                int _S1370 = int(_S1369);
                uint _S1371 = 6U / _S1368;
                int3 _S1372 = int3(_S1370, int(_S1371), int(0));
                uint _S1373 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1372)).xy), uint(((_S1372)).z))).x * 255.0f));
                uint _S1374 = atlasWidth_14;
                uint _S1375 = 7U % atlasWidth_14;
                int _S1376 = int(_S1375);
                uint _S1377 = 7U / _S1374;
                int3 _S1378 = int3(_S1376, int(_S1377), int(0));
                uint columns_14 = _S1373 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1378)).xy), uint(((_S1378)).z))).x * 255.0f)) << 8U);
                uint _S1379 = atlasWidth_14;
                uint _S1380 = 8U % atlasWidth_14;
                int _S1381 = int(_S1380);
                uint _S1382 = 8U / _S1379;
                int3 _S1383 = int3(_S1381, int(_S1382), int(0));
                uint _S1384 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1383)).xy), uint(((_S1383)).z))).x * 255.0f));
                uint _S1385 = atlasWidth_14;
                uint _S1386 = 9U % atlasWidth_14;
                int _S1387 = int(_S1386);
                uint _S1388 = 9U / _S1385;
                int3 _S1389 = int3(_S1387, int(_S1388), int(0));
                uint rows_14 = ((_S1384 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1389)).xy), uint(((_S1389)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1347 * _S1353 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1390 = atlasWidth_14 / columns_14;
                float _S1391 = float(_S1390);
                uint _S1392 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1391, float(_S1392));
                uint _S1393 = slot_29 % columns_14;
                float _S1394 = float(_S1393);
                uint _S1395 = slot_29 / columns_14;
                _S184 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1394, float(_S1395)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(_S182) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1396 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(_S182)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1396);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1397 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S184 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1397)).xy), uint(((_S1397)).z)));
                break;
            }
            uint _S1398 = atlasWidth_14 / uint(_S1396.x);
            float _S1399 = float(_S1398);
            uint _S1400 = (atlasHeight_14 - 1U) / uint(_S1396.y);
            float2 cellSize_29 = float2(_S1399, float(_S1400));
            _S184 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1401 = float4(_S6->compositeControls_0) ;
            if((_S1401.x) != 512.0f)
            {
                break;
            }
            bool _S1402 = (_S1401.w) >= 0.5f;
            if((maskU_7 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_7 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1403 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1402)
                {
                    for(;;)
                    {
                        if(_S1403 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1404 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_19;
                        thread uint height_19;
                        (*((&width_19)) = (_S1404).get_width(0)),(*((&height_19)) = (_S1404).get_height(0));
                        float2 size_17 = float2(float(width_19), float(height_19));
                        float2 _S1405 = float2(0.5f) ;
                        float2 remapped_17 = clamp((_S182 * (size_17 - float2(2.0f) ) + float2(1.0f) ) / size_17, _S1405 / size_17, (size_17 - _S1405) / size_17);
                        if((_S1403 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_17.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S182.x;
                        }
                        if((_S1403 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_17.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S182.y;
                        }
                        _S182 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S1406 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1406).get_width(0)),(*((&atlasHeight_15)) = (_S1406).get_height(0));
                int3 _S1407 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1407)).xy), uint(((_S1407)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1408 = atlasWidth_15;
                    uint _S1409 = 2U % atlasWidth_15;
                    int _S1410 = int(_S1409);
                    uint _S1411 = 2U / _S1408;
                    int3 _S1412 = int3(_S1410, int(_S1411), int(0));
                    int _S1413 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1412)).xy), uint(((_S1412)).z))).x * 255.0f)));
                    uint _S1414 = atlasWidth_15;
                    uint _S1415 = 3U % atlasWidth_15;
                    int _S1416 = int(_S1415);
                    uint _S1417 = 3U / _S1414;
                    int3 _S1418 = int3(_S1416, int(_S1417), int(0));
                    int2 minimumTile_15 = int2(_S1413, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1418)).xy), uint(((_S1418)).z))).x * 255.0f))));
                    uint _S1419 = atlasWidth_15;
                    uint _S1420 = 4U % atlasWidth_15;
                    int _S1421 = int(_S1420);
                    uint _S1422 = 4U / _S1419;
                    int3 _S1423 = int3(_S1421, int(_S1422), int(0));
                    uint _S1424 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1423)).xy), uint(((_S1423)).z))).x * 255.0f));
                    uint _S1425 = atlasWidth_15;
                    uint _S1426 = 5U % atlasWidth_15;
                    int _S1427 = int(_S1426);
                    uint _S1428 = 5U / _S1425;
                    int3 _S1429 = int3(_S1427, int(_S1428), int(0));
                    uint _S1430 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1429)).xy), uint(((_S1429)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1424, _S1430);
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
                        int3 _S1431 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1431)).xy), uint(((_S1431)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1424 + uint(tile_30.x));
                    uint _S1432 = atlasWidth_15;
                    uint _S1433 = offset_15 % atlasWidth_15;
                    int _S1434 = int(_S1433);
                    uint _S1435 = offset_15 / _S1432;
                    int3 _S1436 = int3(_S1434, int(_S1435), int(0));
                    uint _S1437 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1436)).xy), uint(((_S1436)).z))).x * 255.0f));
                    uint _S1438 = atlasWidth_15;
                    uint _S1439 = offset_15 + 1U;
                    uint _S1440 = _S1439 % atlasWidth_15;
                    int _S1441 = int(_S1440);
                    uint _S1442 = _S1439 / _S1438;
                    int3 _S1443 = int3(_S1441, int(_S1442), int(0));
                    uint slot_30 = _S1437 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1443)).xy), uint(((_S1443)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1444 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1444)).xy), uint(((_S1444)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1445 = atlasWidth_15;
                    uint _S1446 = 6U % atlasWidth_15;
                    int _S1447 = int(_S1446);
                    uint _S1448 = 6U / _S1445;
                    int3 _S1449 = int3(_S1447, int(_S1448), int(0));
                    uint _S1450 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1449)).xy), uint(((_S1449)).z))).x * 255.0f));
                    uint _S1451 = atlasWidth_15;
                    uint _S1452 = 7U % atlasWidth_15;
                    int _S1453 = int(_S1452);
                    uint _S1454 = 7U / _S1451;
                    int3 _S1455 = int3(_S1453, int(_S1454), int(0));
                    uint columns_15 = _S1450 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1455)).xy), uint(((_S1455)).z))).x * 255.0f)) << 8U);
                    uint _S1456 = atlasWidth_15;
                    uint _S1457 = 8U % atlasWidth_15;
                    int _S1458 = int(_S1457);
                    uint _S1459 = 8U / _S1456;
                    int3 _S1460 = int3(_S1458, int(_S1459), int(0));
                    uint _S1461 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1460)).xy), uint(((_S1460)).z))).x * 255.0f));
                    uint _S1462 = atlasWidth_15;
                    uint _S1463 = 9U % atlasWidth_15;
                    int _S1464 = int(_S1463);
                    uint _S1465 = 9U / _S1462;
                    int3 _S1466 = int3(_S1464, int(_S1465), int(0));
                    uint rows_15 = ((_S1461 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1466)).xy), uint(((_S1466)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1424 * _S1430 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1467 = atlasWidth_15 / columns_15;
                    float _S1468 = float(_S1467);
                    uint _S1469 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1468, float(_S1469));
                    uint _S1470 = slot_31 % columns_15;
                    float _S1471 = float(_S1470);
                    uint _S1472 = slot_31 / columns_15;
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1471, float(_S1472)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(_S182) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1473 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(_S182)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1473);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1474 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1474)).xy), uint(((_S1474)).z)));
                    break;
                }
                uint _S1475 = atlasWidth_15 / uint(_S1473.x);
                float _S1476 = float(_S1475);
                uint _S1477 = (atlasHeight_15 - 1U) / uint(_S1473.y);
                float2 cellSize_31 = float2(_S1476, float(_S1477));
                _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_7 = uint(round(_S1401.y));
            if(operation_7 == 1U)
            {
                _S184 = _S184 * _S370;
                break;
            }
            if(operation_7 == 2U)
            {
                _S184 = _S184 + _S370;
                break;
            }
            if(operation_7 == 3U)
            {
                _S184 = _S184 - _S370;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1401.z;
                _S184 = _S184 * float4((1.0f - factor_7))  + _S370 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S184.x);
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
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S1478 = (udimMask_0 & 1024U) != 0U;
        float4 _S1479 = float4(_S6->domeLinkControls_0) ;
        uint maskU_8 = uint(round(_S1479.y));
        uint maskV_8 = uint(round(_S1479.z));
        if((maskU_8 & 1024U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_8 & 1024U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1480 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1478)
            {
                for(;;)
                {
                    if(_S1480 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S1481 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
                    thread uint width_20;
                    thread uint height_20;
                    (*((&width_20)) = (_S1481).get_width(0)),(*((&height_20)) = (_S1481).get_height(0));
                    float2 size_18 = float2(float(width_20), float(height_20));
                    float2 _S1482 = float2(0.5f) ;
                    float2 remapped_18 = clamp((_S182 * (size_18 - float2(2.0f) ) + float2(1.0f) ) / size_18, _S1482 / size_18, (size_18 - _S1482) / size_18);
                    if((_S1480 & 1U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_18.x;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S182.x;
                    }
                    if((_S1480 & 2U) != 0U)
                    {
                        ior_0 = remapped_18.y;
                    }
                    else
                    {
                        ior_0 = _S182.y;
                    }
                    _S183 = float2(clearcoatRoughness_0, ior_0);
                    break;
                }
                _S184 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S183)));
                break;
            }
            texture2d<float, access::sample> _S1483 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1483).get_width(0)),(*((&atlasHeight_16)) = (_S1483).get_height(0));
            int3 _S1484 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1484)).xy), uint(((_S1484)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1485 = atlasWidth_16;
                uint _S1486 = 2U % atlasWidth_16;
                int _S1487 = int(_S1486);
                uint _S1488 = 2U / _S1485;
                int3 _S1489 = int3(_S1487, int(_S1488), int(0));
                int _S1490 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1489)).xy), uint(((_S1489)).z))).x * 255.0f)));
                uint _S1491 = atlasWidth_16;
                uint _S1492 = 3U % atlasWidth_16;
                int _S1493 = int(_S1492);
                uint _S1494 = 3U / _S1491;
                int3 _S1495 = int3(_S1493, int(_S1494), int(0));
                int2 minimumTile_16 = int2(_S1490, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1495)).xy), uint(((_S1495)).z))).x * 255.0f))));
                uint _S1496 = atlasWidth_16;
                uint _S1497 = 4U % atlasWidth_16;
                int _S1498 = int(_S1497);
                uint _S1499 = 4U / _S1496;
                int3 _S1500 = int3(_S1498, int(_S1499), int(0));
                uint _S1501 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1500)).xy), uint(((_S1500)).z))).x * 255.0f));
                uint _S1502 = atlasWidth_16;
                uint _S1503 = 5U % atlasWidth_16;
                int _S1504 = int(_S1503);
                uint _S1505 = 5U / _S1502;
                int3 _S1506 = int3(_S1504, int(_S1505), int(0));
                uint _S1507 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1506)).xy), uint(((_S1506)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1501, _S1507);
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
                    int3 _S1508 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1508)).xy), uint(((_S1508)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1501 + uint(tile_32.x));
                uint _S1509 = atlasWidth_16;
                uint _S1510 = offset_16 % atlasWidth_16;
                int _S1511 = int(_S1510);
                uint _S1512 = offset_16 / _S1509;
                int3 _S1513 = int3(_S1511, int(_S1512), int(0));
                uint _S1514 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1513)).xy), uint(((_S1513)).z))).x * 255.0f));
                uint _S1515 = atlasWidth_16;
                uint _S1516 = offset_16 + 1U;
                uint _S1517 = _S1516 % atlasWidth_16;
                int _S1518 = int(_S1517);
                uint _S1519 = _S1516 / _S1515;
                int3 _S1520 = int3(_S1518, int(_S1519), int(0));
                uint slot_32 = _S1514 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1520)).xy), uint(((_S1520)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1521 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1521)).xy), uint(((_S1521)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1522 = atlasWidth_16;
                uint _S1523 = 6U % atlasWidth_16;
                int _S1524 = int(_S1523);
                uint _S1525 = 6U / _S1522;
                int3 _S1526 = int3(_S1524, int(_S1525), int(0));
                uint _S1527 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1526)).xy), uint(((_S1526)).z))).x * 255.0f));
                uint _S1528 = atlasWidth_16;
                uint _S1529 = 7U % atlasWidth_16;
                int _S1530 = int(_S1529);
                uint _S1531 = 7U / _S1528;
                int3 _S1532 = int3(_S1530, int(_S1531), int(0));
                uint columns_16 = _S1527 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1532)).xy), uint(((_S1532)).z))).x * 255.0f)) << 8U);
                uint _S1533 = atlasWidth_16;
                uint _S1534 = 8U % atlasWidth_16;
                int _S1535 = int(_S1534);
                uint _S1536 = 8U / _S1533;
                int3 _S1537 = int3(_S1535, int(_S1536), int(0));
                uint _S1538 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1537)).xy), uint(((_S1537)).z))).x * 255.0f));
                uint _S1539 = atlasWidth_16;
                uint _S1540 = 9U % atlasWidth_16;
                int _S1541 = int(_S1540);
                uint _S1542 = 9U / _S1539;
                int3 _S1543 = int3(_S1541, int(_S1542), int(0));
                uint rows_16 = ((_S1538 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1543)).xy), uint(((_S1543)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1501 * _S1507 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1544 = atlasWidth_16 / columns_16;
                float _S1545 = float(_S1544);
                uint _S1546 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1545, float(_S1546));
                uint _S1547 = slot_33 % columns_16;
                float _S1548 = float(_S1547);
                uint _S1549 = slot_33 / columns_16;
                _S184 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1548, float(_S1549)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(_S182) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1550 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(_S182)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1550);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1551 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S184 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1551)).xy), uint(((_S1551)).z)));
                break;
            }
            uint _S1552 = atlasWidth_16 / uint(_S1550.x);
            float _S1553 = float(_S1552);
            uint _S1554 = (atlasHeight_16 - 1U) / uint(_S1550.y);
            float2 cellSize_33 = float2(_S1553, float(_S1554));
            _S184 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1555 = float4(_S6->compositeControls_0) ;
            if((_S1555.x) != 1024.0f)
            {
                break;
            }
            bool _S1556 = (_S1555.w) >= 0.5f;
            if((maskU_8 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_8 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1557 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1556)
                {
                    for(;;)
                    {
                        if(_S1557 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1558 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_21;
                        thread uint height_21;
                        (*((&width_21)) = (_S1558).get_width(0)),(*((&height_21)) = (_S1558).get_height(0));
                        float2 size_19 = float2(float(width_21), float(height_21));
                        float2 _S1559 = float2(0.5f) ;
                        float2 remapped_19 = clamp((_S182 * (size_19 - float2(2.0f) ) + float2(1.0f) ) / size_19, _S1559 / size_19, (size_19 - _S1559) / size_19);
                        if((_S1557 & 1U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_19.x;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S182.x;
                        }
                        if((_S1557 & 2U) != 0U)
                        {
                            ior_0 = remapped_19.y;
                        }
                        else
                        {
                            ior_0 = _S182.y;
                        }
                        _S182 = float2(clearcoatRoughness_0, ior_0);
                        break;
                    }
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S1560 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1560).get_width(0)),(*((&atlasHeight_17)) = (_S1560).get_height(0));
                int3 _S1561 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1561)).xy), uint(((_S1561)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1562 = atlasWidth_17;
                    uint _S1563 = 2U % atlasWidth_17;
                    int _S1564 = int(_S1563);
                    uint _S1565 = 2U / _S1562;
                    int3 _S1566 = int3(_S1564, int(_S1565), int(0));
                    int _S1567 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1566)).xy), uint(((_S1566)).z))).x * 255.0f)));
                    uint _S1568 = atlasWidth_17;
                    uint _S1569 = 3U % atlasWidth_17;
                    int _S1570 = int(_S1569);
                    uint _S1571 = 3U / _S1568;
                    int3 _S1572 = int3(_S1570, int(_S1571), int(0));
                    int2 minimumTile_17 = int2(_S1567, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1572)).xy), uint(((_S1572)).z))).x * 255.0f))));
                    uint _S1573 = atlasWidth_17;
                    uint _S1574 = 4U % atlasWidth_17;
                    int _S1575 = int(_S1574);
                    uint _S1576 = 4U / _S1573;
                    int3 _S1577 = int3(_S1575, int(_S1576), int(0));
                    uint _S1578 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1577)).xy), uint(((_S1577)).z))).x * 255.0f));
                    uint _S1579 = atlasWidth_17;
                    uint _S1580 = 5U % atlasWidth_17;
                    int _S1581 = int(_S1580);
                    uint _S1582 = 5U / _S1579;
                    int3 _S1583 = int3(_S1581, int(_S1582), int(0));
                    uint _S1584 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1583)).xy), uint(((_S1583)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1578, _S1584);
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
                        int3 _S1585 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1585)).xy), uint(((_S1585)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1578 + uint(tile_34.x));
                    uint _S1586 = atlasWidth_17;
                    uint _S1587 = offset_17 % atlasWidth_17;
                    int _S1588 = int(_S1587);
                    uint _S1589 = offset_17 / _S1586;
                    int3 _S1590 = int3(_S1588, int(_S1589), int(0));
                    uint _S1591 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1590)).xy), uint(((_S1590)).z))).x * 255.0f));
                    uint _S1592 = atlasWidth_17;
                    uint _S1593 = offset_17 + 1U;
                    uint _S1594 = _S1593 % atlasWidth_17;
                    int _S1595 = int(_S1594);
                    uint _S1596 = _S1593 / _S1592;
                    int3 _S1597 = int3(_S1595, int(_S1596), int(0));
                    uint slot_34 = _S1591 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1597)).xy), uint(((_S1597)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1598 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1598)).xy), uint(((_S1598)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1599 = atlasWidth_17;
                    uint _S1600 = 6U % atlasWidth_17;
                    int _S1601 = int(_S1600);
                    uint _S1602 = 6U / _S1599;
                    int3 _S1603 = int3(_S1601, int(_S1602), int(0));
                    uint _S1604 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1603)).xy), uint(((_S1603)).z))).x * 255.0f));
                    uint _S1605 = atlasWidth_17;
                    uint _S1606 = 7U % atlasWidth_17;
                    int _S1607 = int(_S1606);
                    uint _S1608 = 7U / _S1605;
                    int3 _S1609 = int3(_S1607, int(_S1608), int(0));
                    uint columns_17 = _S1604 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1609)).xy), uint(((_S1609)).z))).x * 255.0f)) << 8U);
                    uint _S1610 = atlasWidth_17;
                    uint _S1611 = 8U % atlasWidth_17;
                    int _S1612 = int(_S1611);
                    uint _S1613 = 8U / _S1610;
                    int3 _S1614 = int3(_S1612, int(_S1613), int(0));
                    uint _S1615 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1614)).xy), uint(((_S1614)).z))).x * 255.0f));
                    uint _S1616 = atlasWidth_17;
                    uint _S1617 = 9U % atlasWidth_17;
                    int _S1618 = int(_S1617);
                    uint _S1619 = 9U / _S1616;
                    int3 _S1620 = int3(_S1618, int(_S1619), int(0));
                    uint rows_17 = ((_S1615 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1620)).xy), uint(((_S1620)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1578 * _S1584 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1621 = atlasWidth_17 / columns_17;
                    float _S1622 = float(_S1621);
                    uint _S1623 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1622, float(_S1623));
                    uint _S1624 = slot_35 % columns_17;
                    float _S1625 = float(_S1624);
                    uint _S1626 = slot_35 / columns_17;
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1625, float(_S1626)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(_S182) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1627 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(_S182)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1627);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1628 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1628)).xy), uint(((_S1628)).z)));
                    break;
                }
                uint _S1629 = atlasWidth_17 / uint(_S1627.x);
                float _S1630 = float(_S1629);
                uint _S1631 = (atlasHeight_17 - 1U) / uint(_S1627.y);
                float2 cellSize_35 = float2(_S1630, float(_S1631));
                _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_8 = uint(round(_S1555.y));
            if(operation_8 == 1U)
            {
                _S184 = _S184 * _S370;
                break;
            }
            if(operation_8 == 2U)
            {
                _S184 = _S184 + _S370;
                break;
            }
            if(operation_8 == 3U)
            {
                _S184 = _S184 - _S370;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1555.z;
                _S184 = _S184 * float4((1.0f - factor_8))  + _S370 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S184.x);
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
            _S182 = _S181;
        }
        else
        {
            _S182 = _S180;
        }
        bool _S1632 = (udimMask_0 & 2048U) != 0U;
        float4 _S1633 = float4(_S6->domeLinkControls_0) ;
        uint maskU_9 = uint(round(_S1633.y));
        uint maskV_9 = uint(round(_S1633.z));
        if((maskU_9 & 2048U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_9 & 2048U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1634 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1632)
            {
                for(;;)
                {
                    if(_S1634 == 0U)
                    {
                        _S183 = _S182;
                        break;
                    }
                    texture2d<float, access::sample> _S1635 = (&kernelContext_0)->iorTexture_0;
                    thread uint width_22;
                    thread uint height_22;
                    (*((&width_22)) = (_S1635).get_width(0)),(*((&height_22)) = (_S1635).get_height(0));
                    float2 size_20 = float2(float(width_22), float(height_22));
                    float2 _S1636 = float2(0.5f) ;
                    float2 remapped_20 = clamp((_S182 * (size_20 - float2(2.0f) ) + float2(1.0f) ) / size_20, _S1636 / size_20, (size_20 - _S1636) / size_20);
                    if((_S1634 & 1U) != 0U)
                    {
                        ior_0 = remapped_20.x;
                    }
                    else
                    {
                        ior_0 = _S182.x;
                    }
                    if((_S1634 & 2U) != 0U)
                    {
                        lightType_0 = remapped_20.y;
                    }
                    else
                    {
                        lightType_0 = _S182.y;
                    }
                    _S183 = float2(ior_0, lightType_0);
                    break;
                }
                _S184 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S183)));
                break;
            }
            texture2d<float, access::sample> _S1637 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1637).get_width(0)),(*((&atlasHeight_18)) = (_S1637).get_height(0));
            int3 _S1638 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1638)).xy), uint(((_S1638)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1639 = atlasWidth_18;
                uint _S1640 = 2U % atlasWidth_18;
                int _S1641 = int(_S1640);
                uint _S1642 = 2U / _S1639;
                int3 _S1643 = int3(_S1641, int(_S1642), int(0));
                int _S1644 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1643)).xy), uint(((_S1643)).z))).x * 255.0f)));
                uint _S1645 = atlasWidth_18;
                uint _S1646 = 3U % atlasWidth_18;
                int _S1647 = int(_S1646);
                uint _S1648 = 3U / _S1645;
                int3 _S1649 = int3(_S1647, int(_S1648), int(0));
                int2 minimumTile_18 = int2(_S1644, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1649)).xy), uint(((_S1649)).z))).x * 255.0f))));
                uint _S1650 = atlasWidth_18;
                uint _S1651 = 4U % atlasWidth_18;
                int _S1652 = int(_S1651);
                uint _S1653 = 4U / _S1650;
                int3 _S1654 = int3(_S1652, int(_S1653), int(0));
                uint _S1655 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1654)).xy), uint(((_S1654)).z))).x * 255.0f));
                uint _S1656 = atlasWidth_18;
                uint _S1657 = 5U % atlasWidth_18;
                int _S1658 = int(_S1657);
                uint _S1659 = 5U / _S1656;
                int3 _S1660 = int3(_S1658, int(_S1659), int(0));
                uint _S1661 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1660)).xy), uint(((_S1660)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1655, _S1661);
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
                    int3 _S1662 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1662)).xy), uint(((_S1662)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1655 + uint(tile_36.x));
                uint _S1663 = atlasWidth_18;
                uint _S1664 = offset_18 % atlasWidth_18;
                int _S1665 = int(_S1664);
                uint _S1666 = offset_18 / _S1663;
                int3 _S1667 = int3(_S1665, int(_S1666), int(0));
                uint _S1668 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1667)).xy), uint(((_S1667)).z))).x * 255.0f));
                uint _S1669 = atlasWidth_18;
                uint _S1670 = offset_18 + 1U;
                uint _S1671 = _S1670 % atlasWidth_18;
                int _S1672 = int(_S1671);
                uint _S1673 = _S1670 / _S1669;
                int3 _S1674 = int3(_S1672, int(_S1673), int(0));
                uint slot_36 = _S1668 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1674)).xy), uint(((_S1674)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1675 = int3(int(1), int(0), int(0));
                    _S184 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1675)).xy), uint(((_S1675)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1676 = atlasWidth_18;
                uint _S1677 = 6U % atlasWidth_18;
                int _S1678 = int(_S1677);
                uint _S1679 = 6U / _S1676;
                int3 _S1680 = int3(_S1678, int(_S1679), int(0));
                uint _S1681 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1680)).xy), uint(((_S1680)).z))).x * 255.0f));
                uint _S1682 = atlasWidth_18;
                uint _S1683 = 7U % atlasWidth_18;
                int _S1684 = int(_S1683);
                uint _S1685 = 7U / _S1682;
                int3 _S1686 = int3(_S1684, int(_S1685), int(0));
                uint columns_18 = _S1681 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1686)).xy), uint(((_S1686)).z))).x * 255.0f)) << 8U);
                uint _S1687 = atlasWidth_18;
                uint _S1688 = 8U % atlasWidth_18;
                int _S1689 = int(_S1688);
                uint _S1690 = 8U / _S1687;
                int3 _S1691 = int3(_S1689, int(_S1690), int(0));
                uint _S1692 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1691)).xy), uint(((_S1691)).z))).x * 255.0f));
                uint _S1693 = atlasWidth_18;
                uint _S1694 = 9U % atlasWidth_18;
                int _S1695 = int(_S1694);
                uint _S1696 = 9U / _S1693;
                int3 _S1697 = int3(_S1695, int(_S1696), int(0));
                uint rows_18 = ((_S1692 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1697)).xy), uint(((_S1697)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1655 * _S1661 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1698 = atlasWidth_18 / columns_18;
                float _S1699 = float(_S1698);
                uint _S1700 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1699, float(_S1700));
                uint _S1701 = slot_37 % columns_18;
                float _S1702 = float(_S1701);
                uint _S1703 = slot_37 / columns_18;
                _S184 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1702, float(_S1703)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(_S182) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1704 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(_S182)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1704);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1705 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S184 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1705)).xy), uint(((_S1705)).z)));
                break;
            }
            uint _S1706 = atlasWidth_18 / uint(_S1704.x);
            float _S1707 = float(_S1706);
            uint _S1708 = (atlasHeight_18 - 1U) / uint(_S1704.y);
            float2 cellSize_37 = float2(_S1707, float(_S1708));
            _S184 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1709 = float4(_S6->compositeControls_0) ;
            if((_S1709.x) != 2048.0f)
            {
                break;
            }
            bool _S1710 = (_S1709.w) >= 0.5f;
            if((maskU_9 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_9 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1711 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1710)
                {
                    for(;;)
                    {
                        if(_S1711 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1712 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_23;
                        thread uint height_23;
                        (*((&width_23)) = (_S1712).get_width(0)),(*((&height_23)) = (_S1712).get_height(0));
                        float2 size_21 = float2(float(width_23), float(height_23));
                        float2 _S1713 = float2(0.5f) ;
                        float2 remapped_21 = clamp((_S182 * (size_21 - float2(2.0f) ) + float2(1.0f) ) / size_21, _S1713 / size_21, (size_21 - _S1713) / size_21);
                        if((_S1711 & 1U) != 0U)
                        {
                            ior_0 = remapped_21.x;
                        }
                        else
                        {
                            ior_0 = _S182.x;
                        }
                        if((_S1711 & 2U) != 0U)
                        {
                            lightType_0 = remapped_21.y;
                        }
                        else
                        {
                            lightType_0 = _S182.y;
                        }
                        _S182 = float2(ior_0, lightType_0);
                        break;
                    }
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S182)));
                    break;
                }
                texture2d<float, access::sample> _S1714 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1714).get_width(0)),(*((&atlasHeight_19)) = (_S1714).get_height(0));
                int3 _S1715 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1715)).xy), uint(((_S1715)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1716 = atlasWidth_19;
                    uint _S1717 = 2U % atlasWidth_19;
                    int _S1718 = int(_S1717);
                    uint _S1719 = 2U / _S1716;
                    int3 _S1720 = int3(_S1718, int(_S1719), int(0));
                    int _S1721 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1720)).xy), uint(((_S1720)).z))).x * 255.0f)));
                    uint _S1722 = atlasWidth_19;
                    uint _S1723 = 3U % atlasWidth_19;
                    int _S1724 = int(_S1723);
                    uint _S1725 = 3U / _S1722;
                    int3 _S1726 = int3(_S1724, int(_S1725), int(0));
                    int2 minimumTile_19 = int2(_S1721, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1726)).xy), uint(((_S1726)).z))).x * 255.0f))));
                    uint _S1727 = atlasWidth_19;
                    uint _S1728 = 4U % atlasWidth_19;
                    int _S1729 = int(_S1728);
                    uint _S1730 = 4U / _S1727;
                    int3 _S1731 = int3(_S1729, int(_S1730), int(0));
                    uint _S1732 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1731)).xy), uint(((_S1731)).z))).x * 255.0f));
                    uint _S1733 = atlasWidth_19;
                    uint _S1734 = 5U % atlasWidth_19;
                    int _S1735 = int(_S1734);
                    uint _S1736 = 5U / _S1733;
                    int3 _S1737 = int3(_S1735, int(_S1736), int(0));
                    uint _S1738 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1737)).xy), uint(((_S1737)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1732, _S1738);
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
                        int3 _S1739 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1739)).xy), uint(((_S1739)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1732 + uint(tile_38.x));
                    uint _S1740 = atlasWidth_19;
                    uint _S1741 = offset_19 % atlasWidth_19;
                    int _S1742 = int(_S1741);
                    uint _S1743 = offset_19 / _S1740;
                    int3 _S1744 = int3(_S1742, int(_S1743), int(0));
                    uint _S1745 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1744)).xy), uint(((_S1744)).z))).x * 255.0f));
                    uint _S1746 = atlasWidth_19;
                    uint _S1747 = offset_19 + 1U;
                    uint _S1748 = _S1747 % atlasWidth_19;
                    int _S1749 = int(_S1748);
                    uint _S1750 = _S1747 / _S1746;
                    int3 _S1751 = int3(_S1749, int(_S1750), int(0));
                    uint slot_38 = _S1745 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1751)).xy), uint(((_S1751)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1752 = int3(int(1), int(0), int(0));
                        _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1752)).xy), uint(((_S1752)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1753 = atlasWidth_19;
                    uint _S1754 = 6U % atlasWidth_19;
                    int _S1755 = int(_S1754);
                    uint _S1756 = 6U / _S1753;
                    int3 _S1757 = int3(_S1755, int(_S1756), int(0));
                    uint _S1758 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1757)).xy), uint(((_S1757)).z))).x * 255.0f));
                    uint _S1759 = atlasWidth_19;
                    uint _S1760 = 7U % atlasWidth_19;
                    int _S1761 = int(_S1760);
                    uint _S1762 = 7U / _S1759;
                    int3 _S1763 = int3(_S1761, int(_S1762), int(0));
                    uint columns_19 = _S1758 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1763)).xy), uint(((_S1763)).z))).x * 255.0f)) << 8U);
                    uint _S1764 = atlasWidth_19;
                    uint _S1765 = 8U % atlasWidth_19;
                    int _S1766 = int(_S1765);
                    uint _S1767 = 8U / _S1764;
                    int3 _S1768 = int3(_S1766, int(_S1767), int(0));
                    uint _S1769 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1768)).xy), uint(((_S1768)).z))).x * 255.0f));
                    uint _S1770 = atlasWidth_19;
                    uint _S1771 = 9U % atlasWidth_19;
                    int _S1772 = int(_S1771);
                    uint _S1773 = 9U / _S1770;
                    int3 _S1774 = int3(_S1772, int(_S1773), int(0));
                    uint rows_19 = ((_S1769 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1774)).xy), uint(((_S1774)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1732 * _S1738 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1775 = atlasWidth_19 / columns_19;
                    float _S1776 = float(_S1775);
                    uint _S1777 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1776, float(_S1777));
                    uint _S1778 = slot_39 % columns_19;
                    float _S1779 = float(_S1778);
                    uint _S1780 = slot_39 / columns_19;
                    _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1779, float(_S1780)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(_S182) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1781 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(_S182)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1781);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1782 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S370 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1782)).xy), uint(((_S1782)).z)));
                    break;
                }
                uint _S1783 = atlasWidth_19 / uint(_S1781.x);
                float _S1784 = float(_S1783);
                uint _S1785 = (atlasHeight_19 - 1U) / uint(_S1781.y);
                float2 cellSize_39 = float2(_S1784, float(_S1785));
                _S370 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(_S182) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_9 = uint(round(_S1709.y));
            if(operation_9 == 1U)
            {
                _S184 = _S184 * _S370;
                break;
            }
            if(operation_9 == 2U)
            {
                _S184 = _S184 + _S370;
                break;
            }
            if(operation_9 == 3U)
            {
                _S184 = _S184 - _S370;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1709.z;
                _S184 = _S184 * float4((1.0f - factor_9))  + _S370 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S184.x;
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
    float _S1786 = saturate(abs(dot(normal_2, diffuseFresnel_0)) + 0.00000999999974738f);
    float _S1787 = max(0.00100000004749745f, roughness_0);
    float _S1788 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1789 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1789;
    bool omniPbrSpecular_0 = (_S173.w) >= 0.5f;
    float dielectricLevel_0 = _S159.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1790 = float3(dielectricLevel_0) ;
        float3 _S1791 = float3(metallic_0) ;
        float3 _S1792 = mix(_S1790, diffuseColor_0, _S1791);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1790, diffuseColor_0, _S1791);
        grazingIncidence_0 = _S1792;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S165.w) >= 0.5f)
        {
            float3 _S1793 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1793;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1794 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1794);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1794);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1795 = _S2;
    float _S1796 = _S2.ambientLight_0.w;
    if(_S1796 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1797 = _S1795.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1797, _S1797)) > 0.0f;
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
    uint _S1798 = min(uint(_S1796), 128U);
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
    float4 _S1799 = float4(_S6->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S1799.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S6->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S1800 = _S2;
    uint _S1801 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S1801) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S1802 = _S1795.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S1803 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S1803;
        for(;;)
        {
            if(lightCount_0 < _S1801)
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
        domeAmbient_1 = _S1802;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S1804 = !hasSceneLighting_0;
    if(_S1804)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1798;
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
        bool _S1805;
        if(hasSceneLighting_0)
        {
            _S1805 = (((uint4(_S6->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S1805 = false;
        }
        if(_S1805)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S1806 = lightIndex_0 == 0U;
        bool _S1807;
        if(_S1806)
        {
            _S1807 = _S1804;
        }
        else
        {
            _S1807 = false;
        }
        if(_S1807)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S1808 = 224U + lightIndex_0 * 16U;
            float _S1809 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1808)>>2]);
            float _S1810 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1808 + 4U)>>2]);
            float _S1811 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1808 + 8U)>>2]);
            float _S1812 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1808 + 12U)>>2]);
            lightType_0 = _S1812;
        }
        bool _S1813;
        if(_S1806)
        {
            _S1813 = _S1804;
        }
        else
        {
            _S1813 = false;
        }
        if(_S1813)
        {
            lightDirection_0 = normalize((float4(_S6->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S1814 = 2272U + lightIndex_0 * 16U;
            float _S1815 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1814)>>2]);
            float _S1816 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1814 + 4U)>>2]);
            float _S1817 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1814 + 8U)>>2]);
            float _S1818 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1814 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S1815, _S1816, _S1817));
        }
        bool _S1819;
        if(_S1806)
        {
            _S1819 = _S1804;
        }
        else
        {
            _S1819 = false;
        }
        if(_S1819)
        {
            roughness_0 = (float4(_S6->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S1820 = 4320U + lightIndex_0 * 16U;
            float _S1821 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1820)>>2]);
            float _S1822 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1820 + 4U)>>2]);
            float _S1823 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1820 + 8U)>>2]);
            float _S1824 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1820 + 12U)>>2]);
            roughness_0 = _S1824;
        }
        bool _S1825;
        if(_S1806)
        {
            _S1825 = _S1804;
        }
        else
        {
            _S1825 = false;
        }
        if(_S1825)
        {
            diffuseColor_0 = _S1799.xyz;
        }
        else
        {
            uint _S1826 = 4320U + lightIndex_0 * 16U;
            float _S1827 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1826)>>2]);
            float _S1828 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1826 + 4U)>>2]);
            float _S1829 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1826 + 8U)>>2]);
            float _S1830 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1826 + 12U)>>2]);
            diffuseColor_0 = float3(_S1827, _S1828, _S1829);
        }
        bool _S1831;
        if(_S1806)
        {
            _S1831 = _S1804;
        }
        else
        {
            _S1831 = false;
        }
        if(_S1831)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S1832 = 6368U + lightIndex_0 * 16U;
            float _S1833 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1832)>>2]);
            float _S1834 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1832 + 4U)>>2]);
            float _S1835 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1832 + 8U)>>2]);
            float _S1836 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1832 + 12U)>>2]);
            metallic_0 = _S1833;
        }
        bool _S1837;
        if(_S1806)
        {
            _S1837 = _S1804;
        }
        else
        {
            _S1837 = false;
        }
        if(_S1837)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S1838 = 6368U + lightIndex_0 * 16U;
            float _S1839 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1838)>>2]);
            float _S1840 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1838 + 4U)>>2]);
            float _S1841 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1838 + 8U)>>2]);
            float _S1842 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1838 + 12U)>>2]);
            clearcoatRoughness_0 = _S1840;
        }
        bool _S1843;
        if(_S1806)
        {
            _S1843 = _S1804;
        }
        else
        {
            _S1843 = false;
        }
        if(_S1843)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1844 = 8416U + lightIndex_0 * 16U;
            float _S1845 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1844)>>2]);
            float _S1846 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1844 + 4U)>>2]);
            float _S1847 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1844 + 8U)>>2]);
            float _S1848 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1844 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S1845, _S1846, _S1847));
        }
        bool _S1849;
        if(_S1806)
        {
            _S1849 = _S1804;
        }
        else
        {
            _S1849 = false;
        }
        if(_S1849)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S1850 = 10464U + lightIndex_0 * 16U;
            float _S1851 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1850)>>2]);
            float _S1852 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1850 + 4U)>>2]);
            float _S1853 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1850 + 8U)>>2]);
            float _S1854 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1850 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S1851, _S1852, _S1853));
        }
        bool _S1855;
        if(_S1806)
        {
            _S1855 = _S1804;
        }
        else
        {
            _S1855 = false;
        }
        float shapeX_0;
        if(_S1855)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S1856 = 8416U + lightIndex_0 * 16U;
            float _S1857 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856)>>2]);
            float _S1858 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 4U)>>2]);
            float _S1859 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 8U)>>2]);
            float _S1860 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 12U)>>2]);
            shapeX_0 = _S1860;
        }
        bool _S1861;
        if(_S1806)
        {
            _S1861 = _S1804;
        }
        else
        {
            _S1861 = false;
        }
        float shapeY_0;
        if(_S1861)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S1862 = 10464U + lightIndex_0 * 16U;
            float _S1863 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1862)>>2]);
            float _S1864 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1862 + 4U)>>2]);
            float _S1865 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1862 + 8U)>>2]);
            float _S1866 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1862 + 12U)>>2]);
            shapeY_0 = _S1866;
        }
        bool _S1867;
        if(_S1806)
        {
            _S1867 = _S1804;
        }
        else
        {
            _S1867 = false;
        }
        float lightRadius_0;
        if(_S1867)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S1868 = 2272U + lightIndex_0 * 16U;
            float _S1869 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1868)>>2]);
            float _S1870 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1868 + 4U)>>2]);
            float _S1871 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1868 + 8U)>>2]);
            float _S1872 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1868 + 12U)>>2]);
            lightRadius_0 = _S1872;
        }
        bool _S1873;
        if(_S1806)
        {
            _S1873 = _S1804;
        }
        else
        {
            _S1873 = false;
        }
        if(_S1873)
        {
            domeAmbient_1 = diffuseFresnel_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S1874;
        if(_S1806)
        {
            _S1874 = _S1804;
        }
        else
        {
            _S1874 = false;
        }
        if(_S1874)
        {
            _S184 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1875 = 15296U + lightIndex_0 * 16U;
            float _S1876 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1875)>>2]);
            float _S1877 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1875 + 4U)>>2]);
            float _S1878 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1875 + 8U)>>2]);
            float _S1879 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1875 + 12U)>>2]);
            _S184 = float4(_S1876, _S1877, _S1878, _S1879);
        }
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S1880 = 12960U + lightIndex_0 * 16U;
            float _S1881 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1880)>>2]);
            float _S1882 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1880 + 4U)>>2]);
            float _S1883 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1880 + 8U)>>2]);
            float _S1884 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1880 + 12U)>>2]);
            int shadowSlot_0 = int(_S1881);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S1885 = _S2;
                    FragmentFrameParameters_0 _S1886 = _S2;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - specularColor_0;
                    }
                    else
                    {
                        color_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S1885.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S1887 = lightClip_0.w;
                    if(_S1887 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S1887) ;
                    bool _S1888;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S1888 = true;
                    }
                    else
                    {
                        _S1888 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S1889;
                    if(_S1888)
                    {
                        _S1889 = true;
                    }
                    else
                    {
                        _S1889 = (ndc_0.z) < 0.0f;
                    }
                    bool _S1890;
                    if(_S1889)
                    {
                        _S1890 = true;
                    }
                    else
                    {
                        _S1890 = (ndc_0.z) > 1.0f;
                    }
                    if(_S1890)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S1891 = _S1886.shadowTile_0[shadowSlot_0].xy;
                    float2 _S1892 = _S1886.shadowTile_0[shadowSlot_0].zw;
                    float2 _S1893 = _S1891 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S1892;
                    float texel_0 = _S1885.shadowControls_0[shadowSlot_0].w;
                    float _S1894 = max(_S1885.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S1895 = ndc_0.z - _S1885.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S1896 = float2((texel_0 * 0.5f)) ;
                    float2 _S1897 = _S1891 + _S1896;
                    float2 _S1898 = _S1891 + _S1892 - _S1896;
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
                            if(_S1895 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S1893 + float2(float(x_0), float(y_0)) * float2((_S1894 * texel_0)) , _S1897, _S1898)), level((0.0f))).x))
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
        float3 _S1899 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S1899;
        sampleOffsets_0[int(1)] = _S1899;
        sampleOffsets_0[int(2)] = _S1899;
        sampleOffsets_0[int(3)] = _S1899;
        sampleOffsets_0[int(4)] = _S1899;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S1900 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S1900 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S1900 - halfHeight_0;
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
                uint _S1901 = 224U + lightIndex_0 * 16U;
                float _S1902 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1901)>>2]);
                float _S1903 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1901 + 4U)>>2]);
                float _S1904 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1901 + 8U)>>2]);
                float _S1905 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1901 + 12U)>>2]);
                float3 toLight_0 = float3(_S1902, _S1903, _S1904) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S1906 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S1906)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S1906;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S184.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S184.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S1907 = _S184.y;
                        if(_S1907 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S1907);
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
            float _S1908 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S1908)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S1908) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S1909 = float3(_S1908) ;
            float _S1910 = max(_S1787, 0.00100000004749745f);
            float alpha_0 = _S1910 * _S1910;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S1911 = 1.0f - k_0;
            float3 _S1912 = float3(max(4.0f * normalDotLight_0 * _S1786, 1.00000000317107685e-30f)) ;
            float3 _S1913 = mix(normalIncidence_0, grazingIncidence_0, _S1909) * float3((_S1786 / (_S1786 * _S1911 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S1911 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S1912;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S1914 = max(_S1788, 0.00100000004749745f);
                float alpha_1 = _S1914 * _S1914;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S1915 = 1.0f - k_1;
                directSpecular_0 = _S1913 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S1909) * float3((_S1786 / (_S1786 * _S1915 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S1915 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S1912);
            }
            else
            {
                directSpecular_0 = _S1913;
            }
            float3 _S1916 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S1916 * _S1789) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S1916);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S1917 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S1918 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S1919 = saturate(_S1787);
        float2 _S1920 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S1918, _S1919)), level((0.0f)))).xy;
        float _S1921 = _S1920.x;
        float _S1922 = _S1920.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S1921)  + grazingIncidence_0 * float3(_S1922) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S1921 + _S1922)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S1923 = max(_S1800.domeControls_0.y, 1.0f);
        float3 _S1924 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S1923 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1800.domeControls_0.z;
            float _S1925 = _S1800.domeControls_0.w;
            for(;;)
            {
                bool _S1926 = _S1923 <= 1.0f;
                _S4 = _S1926;
                if(_S1926)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S1925, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S1923)), level((0.0f)))).xyz;
                break;
            }
            float _S1927 = _S1917.environmentControls_0.y;
            float _S1928 = _S1917.environmentControls_0.z;
            for(;;)
            {
                if(_S4)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1929 = max(_S1927, 1.0f);
                    float _S1930 = _S1929 - 1.0f;
                    float slice_0 = _S1919 * max(_S1930, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S1928, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S1929)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S1930) + v_0) / _S1929)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S1931 = max(_S1927, 1.0f);
                float total_0 = _S1931 * _S1923;
                float _S1932 = _S1931 - 1.0f;
                float slice_1 = _S1919 * max(_S1932, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S1928, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S1931;
                specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S1932) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S1933 = max(_S1927, 1.0f);
                        float _S1934 = _S1933 - 1.0f;
                        float slice_2 = saturate(_S1788) * max(_S1934, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S1928, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S1933)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S1934) + v_2) / _S1933)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1935 = max(_S1927, 1.0f);
                    float total_1 = _S1935 * _S1923;
                    float _S1936 = _S1935 - 1.0f;
                    float slice_3 = saturate(_S1788) * max(_S1936, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S1928, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S1935;
                    diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S1936) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightBitangent_0 = diffuseFresnel_0;
            }
            else
            {
                lightBitangent_0 = _S1924;
            }
            lightDirection_0 = diffuseColor_0;
            lightTangent_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightDirection_0 = _S1924;
            lightTangent_0 = _S1924;
            lightBitangent_0 = _S1924;
            for(;;)
            {
                if(sampleIndex_0 < _S1801)
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
                float _S1937 = _S1800.domeControls_0.w;
                for(;;)
                {
                    bool _S1938 = _S1923 <= 1.0f;
                    _S5 = _S1938;
                    if(_S1938)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S1937, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S1923)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightDirection_0 + diffuseColor_0;
                float _S1939 = _S1917.environmentControls_0.y;
                float _S1940 = _S1917.environmentControls_0.z;
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1941 = max(_S1939, 1.0f);
                        float _S1942 = _S1941 - 1.0f;
                        float slice_4 = _S1919 * max(_S1942, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S1940, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S1941)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S1942) + v_4) / _S1941)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1943 = max(_S1939, 1.0f);
                    float total_2 = _S1943 * _S1923;
                    float _S1944 = _S1943 - 1.0f;
                    float slice_5 = _S1919 * max(_S1944, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S1940, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S1943;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S1944) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
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
                            float _S1945 = max(_S1939, 1.0f);
                            float _S1946 = _S1945 - 1.0f;
                            float slice_6 = saturate(_S1788) * max(_S1946, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S1940, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S1945)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S1946) + v_6) / _S1945)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1947 = max(_S1939, 1.0f);
                        float total_3 = _S1947 * _S1923;
                        float _S1948 = _S1947 - 1.0f;
                        float slice_7 = saturate(_S1788) * max(_S1948, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S1940, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S1947;
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S1948) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S1949 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S1918, saturate(_S1788))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightBitangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S1949.x)  + float3(_S1949.y) );
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
    pixelOutput_0 _S1950 = { float4(color_3, opacity_0) };
    return _S1950;
}
