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
    float4 _S175 = float4(_S6->uvTransformRow1_0) ;
    float2 _S176 = float2(dot(_S174.xy, _S1.texCoord_0) + _S174.z, dot(_S175.xy, _S1.texCoord_0) + _S175.z);
    bool hasSceneLighting_0;
    float4 _S177;
    uint3 _S178;
    if((textureMask_0 & 2U) != 0U)
    {
        thread float4 diffuseSample_0;
        bool _S179 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S179)
            {
                _S177 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S180 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S180).get_width(0)),(*((&atlasHeight_0)) = (_S180).get_height(0));
            int3 _S181 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S181)).xy), uint(((_S181)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S182 = atlasWidth_0;
                uint _S183 = 2U % atlasWidth_0;
                int _S184 = int(_S183);
                uint _S185 = 2U / _S182;
                int3 _S186 = int3(_S184, int(_S185), int(0));
                int _S187 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S186)).xy), uint(((_S186)).z))).x * 255.0f)));
                uint _S188 = atlasWidth_0;
                uint _S189 = 3U % atlasWidth_0;
                int _S190 = int(_S189);
                uint _S191 = 3U / _S188;
                int3 _S192 = int3(_S190, int(_S191), int(0));
                int2 minimumTile_0 = int2(_S187, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S192)).xy), uint(((_S192)).z))).x * 255.0f))));
                uint _S193 = atlasWidth_0;
                uint _S194 = 4U % atlasWidth_0;
                int _S195 = int(_S194);
                uint _S196 = 4U / _S193;
                int3 _S197 = int3(_S195, int(_S196), int(0));
                uint _S198 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S197)).xy), uint(((_S197)).z))).x * 255.0f));
                uint _S199 = atlasWidth_0;
                uint _S200 = 5U % atlasWidth_0;
                int _S201 = int(_S200);
                uint _S202 = 5U / _S199;
                int3 _S203 = int3(_S201, int(_S202), int(0));
                uint _S204 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S203)).xy), uint(((_S203)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S198, _S204);
                int2 tile_0 = int2(floor(_S176)) - minimumTile_0;
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
                    int3 _S205 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S205)).xy), uint(((_S205)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S198 + uint(tile_0.x));
                uint _S206 = atlasWidth_0;
                uint _S207 = offset_0 % atlasWidth_0;
                int _S208 = int(_S207);
                uint _S209 = offset_0 / _S206;
                int3 _S210 = int3(_S208, int(_S209), int(0));
                uint _S211 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S210)).xy), uint(((_S210)).z))).x * 255.0f));
                uint _S212 = atlasWidth_0;
                uint _S213 = offset_0 + 1U;
                uint _S214 = _S213 % atlasWidth_0;
                int _S215 = int(_S214);
                uint _S216 = _S213 / _S212;
                int3 _S217 = int3(_S215, int(_S216), int(0));
                uint slot_0 = _S211 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S217)).xy), uint(((_S217)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S218 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S218)).xy), uint(((_S218)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S219 = atlasWidth_0;
                uint _S220 = 6U % atlasWidth_0;
                int _S221 = int(_S220);
                uint _S222 = 6U / _S219;
                int3 _S223 = int3(_S221, int(_S222), int(0));
                uint _S224 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S223)).xy), uint(((_S223)).z))).x * 255.0f));
                uint _S225 = atlasWidth_0;
                uint _S226 = 7U % atlasWidth_0;
                int _S227 = int(_S226);
                uint _S228 = 7U / _S225;
                int3 _S229 = int3(_S227, int(_S228), int(0));
                uint columns_0 = _S224 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S229)).xy), uint(((_S229)).z))).x * 255.0f)) << 8U);
                uint _S230 = atlasWidth_0;
                uint _S231 = 8U % atlasWidth_0;
                int _S232 = int(_S231);
                uint _S233 = 8U / _S230;
                int3 _S234 = int3(_S232, int(_S233), int(0));
                uint _S235 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S234)).xy), uint(((_S234)).z))).x * 255.0f));
                uint _S236 = atlasWidth_0;
                uint _S237 = 9U % atlasWidth_0;
                int _S238 = int(_S237);
                uint _S239 = 9U / _S236;
                int3 _S240 = int3(_S238, int(_S239), int(0));
                uint rows_0 = ((_S235 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S240)).xy), uint(((_S240)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S198 * _S204 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S241 = atlasWidth_0 / columns_0;
                float _S242 = float(_S241);
                uint _S243 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S242, float(_S243));
                uint _S244 = slot_1 % columns_0;
                float _S245 = float(_S244);
                uint _S246 = slot_1 / columns_0;
                _S177 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S245, float(_S246)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S176) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S247 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(_S176)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S247);
            }
            if(hasSceneLighting_0)
            {
                int3 _S248 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S248)).xy), uint(((_S248)).z)));
                break;
            }
            uint _S249 = atlasWidth_0 / uint(_S247.x);
            float _S250 = float(_S249);
            uint _S251 = (atlasHeight_0 - 1U) / uint(_S247.y);
            float2 cellSize_1 = float2(_S250, float(_S251));
            _S177 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S177;
        float4 _S252 = float4(_S6->compositeControls_0) ;
        float _S253 = _S252.x;
        if(_S253 == 0.0f)
        {
            hasSceneLighting_0 = (_S252.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S254 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S254).get_width(0)),(*((&sourceHeight_0)) = (_S254).get_height(0));
            texture2d<float, access::sample> _S255 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S255).get_width(0)),(*((&valueHeight_0)) = (_S255).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S256 = diffuseSample_0;
                for(;;)
                {
                    float _S257 = _S256.w;
                    if(_S257 > 1.0f)
                    {
                        diffuseColor_0 = _S256.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S258 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S258).get_width(0)),(*((&height_0)) = (_S258).get_height(0));
                    uint3 _S259 = uint3(round(_S256.xyz * float3(255.0f) ));
                    uint index_1 = ((_S259.x) | ((_S259.y) << 8U)) | ((_S259.z) << 16U);
                    if(_S257 == 0.0f)
                    {
                        _S178 = _S259;
                    }
                    else
                    {
                        _S178 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S178 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S260 = address_0.x;
                    uint _S261 = _S260 % width_0;
                    int _S262 = int(_S261);
                    uint _S263 = _S260 / width_0;
                    int3 _S264 = int3(_S262, int(_S263), int(0));
                    float _S265 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S264)).xy), uint(((_S264)).z))).x;
                    uint _S266 = address_0.y;
                    uint _S267 = _S266 % width_0;
                    int _S268 = int(_S267);
                    uint _S269 = _S266 / width_0;
                    int3 _S270 = int3(_S268, int(_S269), int(0));
                    float _S271 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S270)).xy), uint(((_S270)).z))).x;
                    uint _S272 = address_0.z;
                    uint _S273 = _S272 % width_0;
                    int _S274 = int(_S273);
                    uint _S275 = _S272 / width_0;
                    int3 _S276 = int3(_S274, int(_S275), int(0));
                    diffuseColor_0 = float3(_S265, _S271, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S276)).xy), uint(((_S276)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                diffuseSample_0.z = (((&kernelContext_0)->diffuseValuesTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S176))).x;
            }
        }
        float4 _S277 = diffuseSample_0;
        for(;;)
        {
            if(_S253 != 2.0f)
            {
                _S177 = _S277;
                break;
            }
            bool _S278 = (_S252.w) >= 0.5f;
            for(;;)
            {
                if(!_S278)
                {
                    _S177 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S279 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S279).get_width(0)),(*((&atlasHeight_1)) = (_S279).get_height(0));
                int3 _S280 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S280)).xy), uint(((_S280)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S281 = atlasWidth_1;
                    uint _S282 = 2U % atlasWidth_1;
                    int _S283 = int(_S282);
                    uint _S284 = 2U / _S281;
                    int3 _S285 = int3(_S283, int(_S284), int(0));
                    int _S286 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S285)).xy), uint(((_S285)).z))).x * 255.0f)));
                    uint _S287 = atlasWidth_1;
                    uint _S288 = 3U % atlasWidth_1;
                    int _S289 = int(_S288);
                    uint _S290 = 3U / _S287;
                    int3 _S291 = int3(_S289, int(_S290), int(0));
                    int2 minimumTile_1 = int2(_S286, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S291)).xy), uint(((_S291)).z))).x * 255.0f))));
                    uint _S292 = atlasWidth_1;
                    uint _S293 = 4U % atlasWidth_1;
                    int _S294 = int(_S293);
                    uint _S295 = 4U / _S292;
                    int3 _S296 = int3(_S294, int(_S295), int(0));
                    uint _S297 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S296)).xy), uint(((_S296)).z))).x * 255.0f));
                    uint _S298 = atlasWidth_1;
                    uint _S299 = 5U % atlasWidth_1;
                    int _S300 = int(_S299);
                    uint _S301 = 5U / _S298;
                    int3 _S302 = int3(_S300, int(_S301), int(0));
                    uint _S303 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S302)).xy), uint(((_S302)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S297, _S303);
                    int2 tile_2 = int2(floor(_S176)) - minimumTile_1;
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
                        int3 _S304 = int3(int(1), int(0), int(0));
                        _S177 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S304)).xy), uint(((_S304)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S297 + uint(tile_2.x));
                    uint _S305 = atlasWidth_1;
                    uint _S306 = offset_1 % atlasWidth_1;
                    int _S307 = int(_S306);
                    uint _S308 = offset_1 / _S305;
                    int3 _S309 = int3(_S307, int(_S308), int(0));
                    uint _S310 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S309)).xy), uint(((_S309)).z))).x * 255.0f));
                    uint _S311 = atlasWidth_1;
                    uint _S312 = offset_1 + 1U;
                    uint _S313 = _S312 % atlasWidth_1;
                    int _S314 = int(_S313);
                    uint _S315 = _S312 / _S311;
                    int3 _S316 = int3(_S314, int(_S315), int(0));
                    uint slot_2 = _S310 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S316)).xy), uint(((_S316)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S317 = int3(int(1), int(0), int(0));
                        _S177 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S317)).xy), uint(((_S317)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S318 = atlasWidth_1;
                    uint _S319 = 6U % atlasWidth_1;
                    int _S320 = int(_S319);
                    uint _S321 = 6U / _S318;
                    int3 _S322 = int3(_S320, int(_S321), int(0));
                    uint _S323 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S322)).xy), uint(((_S322)).z))).x * 255.0f));
                    uint _S324 = atlasWidth_1;
                    uint _S325 = 7U % atlasWidth_1;
                    int _S326 = int(_S325);
                    uint _S327 = 7U / _S324;
                    int3 _S328 = int3(_S326, int(_S327), int(0));
                    uint columns_1 = _S323 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S328)).xy), uint(((_S328)).z))).x * 255.0f)) << 8U);
                    uint _S329 = atlasWidth_1;
                    uint _S330 = 8U % atlasWidth_1;
                    int _S331 = int(_S330);
                    uint _S332 = 8U / _S329;
                    int3 _S333 = int3(_S331, int(_S332), int(0));
                    uint _S334 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S333)).xy), uint(((_S333)).z))).x * 255.0f));
                    uint _S335 = atlasWidth_1;
                    uint _S336 = 9U % atlasWidth_1;
                    int _S337 = int(_S336);
                    uint _S338 = 9U / _S335;
                    int3 _S339 = int3(_S337, int(_S338), int(0));
                    uint rows_1 = ((_S334 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S339)).xy), uint(((_S339)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S297 * _S303 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S340 = atlasWidth_1 / columns_1;
                    float _S341 = float(_S340);
                    uint _S342 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S341, float(_S342));
                    uint _S343 = slot_3 % columns_1;
                    float _S344 = float(_S343);
                    uint _S345 = slot_3 / columns_1;
                    _S177 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S344, float(_S345)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S176) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S346 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(_S176)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S346);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S347 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S177 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S347)).xy), uint(((_S347)).z)));
                    break;
                }
                uint _S348 = atlasWidth_1 / uint(_S346.x);
                float _S349 = float(_S348);
                uint _S350 = (atlasHeight_1 - 1U) / uint(_S346.y);
                float2 cellSize_3 = float2(_S349, float(_S350));
                _S177 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S252.y));
            if(operation_0 == 1U)
            {
                _S177 = _S277 * _S177;
                break;
            }
            if(operation_0 == 2U)
            {
                _S177 = _S277 + _S177;
                break;
            }
            if(operation_0 == 3U)
            {
                _S177 = _S277 - _S177;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S252.z;
                _S177 = _S277 * float4((1.0f - factor_0))  + _S177 * float4(factor_0) ;
                break;
            }
            _S177 = _S277;
            break;
        }
        diffuseColor_0 = _S177.xyz;
    }
    float roughness_0;
    float4 _S351;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S352 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S352)
            {
                _S177 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S353 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S353).get_width(0)),(*((&atlasHeight_2)) = (_S353).get_height(0));
            int3 _S354 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S354)).xy), uint(((_S354)).z))) * float4(255.0f) );
            if((metadata_2.x) == 255.0f)
            {
                uint _S355 = atlasWidth_2;
                uint _S356 = 2U % atlasWidth_2;
                int _S357 = int(_S356);
                uint _S358 = 2U / _S355;
                int3 _S359 = int3(_S357, int(_S358), int(0));
                int _S360 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S359)).xy), uint(((_S359)).z))).x * 255.0f)));
                uint _S361 = atlasWidth_2;
                uint _S362 = 3U % atlasWidth_2;
                int _S363 = int(_S362);
                uint _S364 = 3U / _S361;
                int3 _S365 = int3(_S363, int(_S364), int(0));
                int2 minimumTile_2 = int2(_S360, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S365)).xy), uint(((_S365)).z))).x * 255.0f))));
                uint _S366 = atlasWidth_2;
                uint _S367 = 4U % atlasWidth_2;
                int _S368 = int(_S367);
                uint _S369 = 4U / _S366;
                int3 _S370 = int3(_S368, int(_S369), int(0));
                uint _S371 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S370)).xy), uint(((_S370)).z))).x * 255.0f));
                uint _S372 = atlasWidth_2;
                uint _S373 = 5U % atlasWidth_2;
                int _S374 = int(_S373);
                uint _S375 = 5U / _S372;
                int3 _S376 = int3(_S374, int(_S375), int(0));
                uint _S377 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S376)).xy), uint(((_S376)).z))).x * 255.0f));
                uint2 grid_2 = uint2(_S371, _S377);
                int2 tile_4 = int2(floor(_S176)) - minimumTile_2;
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
                    int3 _S378 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S378)).xy), uint(((_S378)).z)));
                    break;
                }
                uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S371 + uint(tile_4.x));
                uint _S379 = atlasWidth_2;
                uint _S380 = offset_2 % atlasWidth_2;
                int _S381 = int(_S380);
                uint _S382 = offset_2 / _S379;
                int3 _S383 = int3(_S381, int(_S382), int(0));
                uint _S384 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S383)).xy), uint(((_S383)).z))).x * 255.0f));
                uint _S385 = atlasWidth_2;
                uint _S386 = offset_2 + 1U;
                uint _S387 = _S386 % atlasWidth_2;
                int _S388 = int(_S387);
                uint _S389 = _S386 / _S385;
                int3 _S390 = int3(_S388, int(_S389), int(0));
                uint slot_4 = _S384 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S390)).xy), uint(((_S390)).z))).x * 255.0f)) << 8U);
                if(slot_4 == 0U)
                {
                    int3 _S391 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S391)).xy), uint(((_S391)).z)));
                    break;
                }
                uint slot_5 = slot_4 - 1U;
                uint _S392 = atlasWidth_2;
                uint _S393 = 6U % atlasWidth_2;
                int _S394 = int(_S393);
                uint _S395 = 6U / _S392;
                int3 _S396 = int3(_S394, int(_S395), int(0));
                uint _S397 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S396)).xy), uint(((_S396)).z))).x * 255.0f));
                uint _S398 = atlasWidth_2;
                uint _S399 = 7U % atlasWidth_2;
                int _S400 = int(_S399);
                uint _S401 = 7U / _S398;
                int3 _S402 = int3(_S400, int(_S401), int(0));
                uint columns_2 = _S397 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S402)).xy), uint(((_S402)).z))).x * 255.0f)) << 8U);
                uint _S403 = atlasWidth_2;
                uint _S404 = 8U % atlasWidth_2;
                int _S405 = int(_S404);
                uint _S406 = 8U / _S403;
                int3 _S407 = int3(_S405, int(_S406), int(0));
                uint _S408 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S407)).xy), uint(((_S407)).z))).x * 255.0f));
                uint _S409 = atlasWidth_2;
                uint _S410 = 9U % atlasWidth_2;
                int _S411 = int(_S410);
                uint _S412 = 9U / _S409;
                int3 _S413 = int3(_S411, int(_S412), int(0));
                uint rows_2 = ((_S408 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S413)).xy), uint(((_S413)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
                uint metadataRows_2 = (10U + 2U * _S371 * _S377 + atlasWidth_2 - 1U) / atlasWidth_2;
                uint _S414 = atlasWidth_2 / columns_2;
                float _S415 = float(_S414);
                uint _S416 = (atlasHeight_2 - metadataRows_2) / rows_2;
                float2 cellSize_4 = float2(_S415, float(_S416));
                uint _S417 = slot_5 % columns_2;
                float _S418 = float(_S417);
                uint _S419 = slot_5 / columns_2;
                _S177 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S418, float(_S419)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(_S176) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
                break;
            }
            int2 _S420 = int2(metadata_2.zw);
            int2 tile_5 = int2(floor(_S176)) - int2(metadata_2.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S420);
            }
            if(hasSceneLighting_0)
            {
                int3 _S421 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S421)).xy), uint(((_S421)).z)));
                break;
            }
            uint _S422 = atlasWidth_2 / uint(_S420.x);
            float _S423 = float(_S422);
            uint _S424 = (atlasHeight_2 - 1U) / uint(_S420.y);
            float2 cellSize_5 = float2(_S423, float(_S424));
            _S177 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        for(;;)
        {
            float4 _S425 = float4(_S6->compositeControls_0) ;
            if((_S425.x) != 8.0f)
            {
                break;
            }
            bool _S426 = (_S425.w) >= 0.5f;
            for(;;)
            {
                if(!_S426)
                {
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S427 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_3;
                thread uint atlasHeight_3;
                (*((&atlasWidth_3)) = (_S427).get_width(0)),(*((&atlasHeight_3)) = (_S427).get_height(0));
                int3 _S428 = int3(int(0), int(0), int(0));
                float4 metadata_3 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S428)).xy), uint(((_S428)).z))) * float4(255.0f) );
                if((metadata_3.x) == 255.0f)
                {
                    uint _S429 = atlasWidth_3;
                    uint _S430 = 2U % atlasWidth_3;
                    int _S431 = int(_S430);
                    uint _S432 = 2U / _S429;
                    int3 _S433 = int3(_S431, int(_S432), int(0));
                    int _S434 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S433)).xy), uint(((_S433)).z))).x * 255.0f)));
                    uint _S435 = atlasWidth_3;
                    uint _S436 = 3U % atlasWidth_3;
                    int _S437 = int(_S436);
                    uint _S438 = 3U / _S435;
                    int3 _S439 = int3(_S437, int(_S438), int(0));
                    int2 minimumTile_3 = int2(_S434, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S439)).xy), uint(((_S439)).z))).x * 255.0f))));
                    uint _S440 = atlasWidth_3;
                    uint _S441 = 4U % atlasWidth_3;
                    int _S442 = int(_S441);
                    uint _S443 = 4U / _S440;
                    int3 _S444 = int3(_S442, int(_S443), int(0));
                    uint _S445 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S444)).xy), uint(((_S444)).z))).x * 255.0f));
                    uint _S446 = atlasWidth_3;
                    uint _S447 = 5U % atlasWidth_3;
                    int _S448 = int(_S447);
                    uint _S449 = 5U / _S446;
                    int3 _S450 = int3(_S448, int(_S449), int(0));
                    uint _S451 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S450)).xy), uint(((_S450)).z))).x * 255.0f));
                    uint2 grid_3 = uint2(_S445, _S451);
                    int2 tile_6 = int2(floor(_S176)) - minimumTile_3;
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
                        int3 _S452 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S452)).xy), uint(((_S452)).z)));
                        break;
                    }
                    uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S445 + uint(tile_6.x));
                    uint _S453 = atlasWidth_3;
                    uint _S454 = offset_3 % atlasWidth_3;
                    int _S455 = int(_S454);
                    uint _S456 = offset_3 / _S453;
                    int3 _S457 = int3(_S455, int(_S456), int(0));
                    uint _S458 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S457)).xy), uint(((_S457)).z))).x * 255.0f));
                    uint _S459 = atlasWidth_3;
                    uint _S460 = offset_3 + 1U;
                    uint _S461 = _S460 % atlasWidth_3;
                    int _S462 = int(_S461);
                    uint _S463 = _S460 / _S459;
                    int3 _S464 = int3(_S462, int(_S463), int(0));
                    uint slot_6 = _S458 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S464)).xy), uint(((_S464)).z))).x * 255.0f)) << 8U);
                    if(slot_6 == 0U)
                    {
                        int3 _S465 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S465)).xy), uint(((_S465)).z)));
                        break;
                    }
                    uint slot_7 = slot_6 - 1U;
                    uint _S466 = atlasWidth_3;
                    uint _S467 = 6U % atlasWidth_3;
                    int _S468 = int(_S467);
                    uint _S469 = 6U / _S466;
                    int3 _S470 = int3(_S468, int(_S469), int(0));
                    uint _S471 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S470)).xy), uint(((_S470)).z))).x * 255.0f));
                    uint _S472 = atlasWidth_3;
                    uint _S473 = 7U % atlasWidth_3;
                    int _S474 = int(_S473);
                    uint _S475 = 7U / _S472;
                    int3 _S476 = int3(_S474, int(_S475), int(0));
                    uint columns_3 = _S471 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S476)).xy), uint(((_S476)).z))).x * 255.0f)) << 8U);
                    uint _S477 = atlasWidth_3;
                    uint _S478 = 8U % atlasWidth_3;
                    int _S479 = int(_S478);
                    uint _S480 = 8U / _S477;
                    int3 _S481 = int3(_S479, int(_S480), int(0));
                    uint _S482 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S481)).xy), uint(((_S481)).z))).x * 255.0f));
                    uint _S483 = atlasWidth_3;
                    uint _S484 = 9U % atlasWidth_3;
                    int _S485 = int(_S484);
                    uint _S486 = 9U / _S483;
                    int3 _S487 = int3(_S485, int(_S486), int(0));
                    uint rows_3 = ((_S482 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S487)).xy), uint(((_S487)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                    uint metadataRows_3 = (10U + 2U * _S445 * _S451 + atlasWidth_3 - 1U) / atlasWidth_3;
                    uint _S488 = atlasWidth_3 / columns_3;
                    float _S489 = float(_S488);
                    uint _S490 = (atlasHeight_3 - metadataRows_3) / rows_3;
                    float2 cellSize_6 = float2(_S489, float(_S490));
                    uint _S491 = slot_7 % columns_3;
                    float _S492 = float(_S491);
                    uint _S493 = slot_7 / columns_3;
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S492, float(_S493)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(_S176) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                    break;
                }
                int2 _S494 = int2(metadata_3.zw);
                int2 tile_7 = int2(floor(_S176)) - int2(metadata_3.xy);
                if(any(tile_7 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_7 >= _S494);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S495 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                    _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S495)).xy), uint(((_S495)).z)));
                    break;
                }
                uint _S496 = atlasWidth_3 / uint(_S494.x);
                float _S497 = float(_S496);
                uint _S498 = (atlasHeight_3 - 1U) / uint(_S494.y);
                float2 cellSize_7 = float2(_S497, float(_S498));
                _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            uint operation_1 = uint(round(_S425.y));
            if(operation_1 == 1U)
            {
                _S177 = _S177 * _S351;
                break;
            }
            if(operation_1 == 2U)
            {
                _S177 = _S177 + _S351;
                break;
            }
            if(operation_1 == 3U)
            {
                _S177 = _S177 - _S351;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S425.z;
                _S177 = _S177 * float4((1.0f - factor_1))  + _S351 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S177.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S167;
    }
    float metallic_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool _S499 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S499)
            {
                _S177 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S500 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S500).get_width(0)),(*((&atlasHeight_4)) = (_S500).get_height(0));
            int3 _S501 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S501)).xy), uint(((_S501)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S502 = atlasWidth_4;
                uint _S503 = 2U % atlasWidth_4;
                int _S504 = int(_S503);
                uint _S505 = 2U / _S502;
                int3 _S506 = int3(_S504, int(_S505), int(0));
                int _S507 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S506)).xy), uint(((_S506)).z))).x * 255.0f)));
                uint _S508 = atlasWidth_4;
                uint _S509 = 3U % atlasWidth_4;
                int _S510 = int(_S509);
                uint _S511 = 3U / _S508;
                int3 _S512 = int3(_S510, int(_S511), int(0));
                int2 minimumTile_4 = int2(_S507, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S512)).xy), uint(((_S512)).z))).x * 255.0f))));
                uint _S513 = atlasWidth_4;
                uint _S514 = 4U % atlasWidth_4;
                int _S515 = int(_S514);
                uint _S516 = 4U / _S513;
                int3 _S517 = int3(_S515, int(_S516), int(0));
                uint _S518 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S517)).xy), uint(((_S517)).z))).x * 255.0f));
                uint _S519 = atlasWidth_4;
                uint _S520 = 5U % atlasWidth_4;
                int _S521 = int(_S520);
                uint _S522 = 5U / _S519;
                int3 _S523 = int3(_S521, int(_S522), int(0));
                uint _S524 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S523)).xy), uint(((_S523)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S518, _S524);
                int2 tile_8 = int2(floor(_S176)) - minimumTile_4;
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
                    int3 _S525 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S525)).xy), uint(((_S525)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S518 + uint(tile_8.x));
                uint _S526 = atlasWidth_4;
                uint _S527 = offset_4 % atlasWidth_4;
                int _S528 = int(_S527);
                uint _S529 = offset_4 / _S526;
                int3 _S530 = int3(_S528, int(_S529), int(0));
                uint _S531 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S530)).xy), uint(((_S530)).z))).x * 255.0f));
                uint _S532 = atlasWidth_4;
                uint _S533 = offset_4 + 1U;
                uint _S534 = _S533 % atlasWidth_4;
                int _S535 = int(_S534);
                uint _S536 = _S533 / _S532;
                int3 _S537 = int3(_S535, int(_S536), int(0));
                uint slot_8 = _S531 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S537)).xy), uint(((_S537)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S538 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S538)).xy), uint(((_S538)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S539 = atlasWidth_4;
                uint _S540 = 6U % atlasWidth_4;
                int _S541 = int(_S540);
                uint _S542 = 6U / _S539;
                int3 _S543 = int3(_S541, int(_S542), int(0));
                uint _S544 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S543)).xy), uint(((_S543)).z))).x * 255.0f));
                uint _S545 = atlasWidth_4;
                uint _S546 = 7U % atlasWidth_4;
                int _S547 = int(_S546);
                uint _S548 = 7U / _S545;
                int3 _S549 = int3(_S547, int(_S548), int(0));
                uint columns_4 = _S544 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S549)).xy), uint(((_S549)).z))).x * 255.0f)) << 8U);
                uint _S550 = atlasWidth_4;
                uint _S551 = 8U % atlasWidth_4;
                int _S552 = int(_S551);
                uint _S553 = 8U / _S550;
                int3 _S554 = int3(_S552, int(_S553), int(0));
                uint _S555 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S554)).xy), uint(((_S554)).z))).x * 255.0f));
                uint _S556 = atlasWidth_4;
                uint _S557 = 9U % atlasWidth_4;
                int _S558 = int(_S557);
                uint _S559 = 9U / _S556;
                int3 _S560 = int3(_S558, int(_S559), int(0));
                uint rows_4 = ((_S555 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S560)).xy), uint(((_S560)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S518 * _S524 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S561 = atlasWidth_4 / columns_4;
                float _S562 = float(_S561);
                uint _S563 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S562, float(_S563));
                uint _S564 = slot_9 % columns_4;
                float _S565 = float(_S564);
                uint _S566 = slot_9 / columns_4;
                _S177 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S565, float(_S566)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(_S176) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S567 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(_S176)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S567);
            }
            if(hasSceneLighting_0)
            {
                int3 _S568 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S568)).xy), uint(((_S568)).z)));
                break;
            }
            uint _S569 = atlasWidth_4 / uint(_S567.x);
            float _S570 = float(_S569);
            uint _S571 = (atlasHeight_4 - 1U) / uint(_S567.y);
            float2 cellSize_9 = float2(_S570, float(_S571));
            _S177 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S572 = float4(_S6->compositeControls_0) ;
            if((_S572.x) != 32.0f)
            {
                break;
            }
            bool _S573 = (_S572.w) >= 0.5f;
            for(;;)
            {
                if(!_S573)
                {
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S574 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S574).get_width(0)),(*((&atlasHeight_5)) = (_S574).get_height(0));
                int3 _S575 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S575)).xy), uint(((_S575)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S576 = atlasWidth_5;
                    uint _S577 = 2U % atlasWidth_5;
                    int _S578 = int(_S577);
                    uint _S579 = 2U / _S576;
                    int3 _S580 = int3(_S578, int(_S579), int(0));
                    int _S581 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S580)).xy), uint(((_S580)).z))).x * 255.0f)));
                    uint _S582 = atlasWidth_5;
                    uint _S583 = 3U % atlasWidth_5;
                    int _S584 = int(_S583);
                    uint _S585 = 3U / _S582;
                    int3 _S586 = int3(_S584, int(_S585), int(0));
                    int2 minimumTile_5 = int2(_S581, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S586)).xy), uint(((_S586)).z))).x * 255.0f))));
                    uint _S587 = atlasWidth_5;
                    uint _S588 = 4U % atlasWidth_5;
                    int _S589 = int(_S588);
                    uint _S590 = 4U / _S587;
                    int3 _S591 = int3(_S589, int(_S590), int(0));
                    uint _S592 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S591)).xy), uint(((_S591)).z))).x * 255.0f));
                    uint _S593 = atlasWidth_5;
                    uint _S594 = 5U % atlasWidth_5;
                    int _S595 = int(_S594);
                    uint _S596 = 5U / _S593;
                    int3 _S597 = int3(_S595, int(_S596), int(0));
                    uint _S598 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S597)).xy), uint(((_S597)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S592, _S598);
                    int2 tile_10 = int2(floor(_S176)) - minimumTile_5;
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
                        int3 _S599 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S599)).xy), uint(((_S599)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S592 + uint(tile_10.x));
                    uint _S600 = atlasWidth_5;
                    uint _S601 = offset_5 % atlasWidth_5;
                    int _S602 = int(_S601);
                    uint _S603 = offset_5 / _S600;
                    int3 _S604 = int3(_S602, int(_S603), int(0));
                    uint _S605 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S604)).xy), uint(((_S604)).z))).x * 255.0f));
                    uint _S606 = atlasWidth_5;
                    uint _S607 = offset_5 + 1U;
                    uint _S608 = _S607 % atlasWidth_5;
                    int _S609 = int(_S608);
                    uint _S610 = _S607 / _S606;
                    int3 _S611 = int3(_S609, int(_S610), int(0));
                    uint slot_10 = _S605 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S611)).xy), uint(((_S611)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S612 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S612)).xy), uint(((_S612)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S613 = atlasWidth_5;
                    uint _S614 = 6U % atlasWidth_5;
                    int _S615 = int(_S614);
                    uint _S616 = 6U / _S613;
                    int3 _S617 = int3(_S615, int(_S616), int(0));
                    uint _S618 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S617)).xy), uint(((_S617)).z))).x * 255.0f));
                    uint _S619 = atlasWidth_5;
                    uint _S620 = 7U % atlasWidth_5;
                    int _S621 = int(_S620);
                    uint _S622 = 7U / _S619;
                    int3 _S623 = int3(_S621, int(_S622), int(0));
                    uint columns_5 = _S618 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S623)).xy), uint(((_S623)).z))).x * 255.0f)) << 8U);
                    uint _S624 = atlasWidth_5;
                    uint _S625 = 8U % atlasWidth_5;
                    int _S626 = int(_S625);
                    uint _S627 = 8U / _S624;
                    int3 _S628 = int3(_S626, int(_S627), int(0));
                    uint _S629 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S628)).xy), uint(((_S628)).z))).x * 255.0f));
                    uint _S630 = atlasWidth_5;
                    uint _S631 = 9U % atlasWidth_5;
                    int _S632 = int(_S631);
                    uint _S633 = 9U / _S630;
                    int3 _S634 = int3(_S632, int(_S633), int(0));
                    uint rows_5 = ((_S629 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S634)).xy), uint(((_S634)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S592 * _S598 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S635 = atlasWidth_5 / columns_5;
                    float _S636 = float(_S635);
                    uint _S637 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S636, float(_S637));
                    uint _S638 = slot_11 % columns_5;
                    float _S639 = float(_S638);
                    uint _S640 = slot_11 / columns_5;
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S639, float(_S640)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(_S176) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S641 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(_S176)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S641);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S642 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S642)).xy), uint(((_S642)).z)));
                    break;
                }
                uint _S643 = atlasWidth_5 / uint(_S641.x);
                float _S644 = float(_S643);
                uint _S645 = (atlasHeight_5 - 1U) / uint(_S641.y);
                float2 cellSize_11 = float2(_S644, float(_S645));
                _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_2 = uint(round(_S572.y));
            if(operation_2 == 1U)
            {
                _S177 = _S177 * _S351;
                break;
            }
            if(operation_2 == 2U)
            {
                _S177 = _S177 + _S351;
                break;
            }
            if(operation_2 == 3U)
            {
                _S177 = _S177 - _S351;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S572.z;
                _S177 = _S177 * float4((1.0f - factor_2))  + _S351 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S177.x);
    }
    else
    {
        metallic_0 = _S166;
    }
    if((textureMask_0 & 16U) != 0U)
    {
        thread float4 emissionSample_0;
        bool _S646 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S646)
            {
                _S177 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S647 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S647).get_width(0)),(*((&atlasHeight_6)) = (_S647).get_height(0));
            int3 _S648 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S648)).xy), uint(((_S648)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S649 = atlasWidth_6;
                uint _S650 = 2U % atlasWidth_6;
                int _S651 = int(_S650);
                uint _S652 = 2U / _S649;
                int3 _S653 = int3(_S651, int(_S652), int(0));
                int _S654 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S653)).xy), uint(((_S653)).z))).x * 255.0f)));
                uint _S655 = atlasWidth_6;
                uint _S656 = 3U % atlasWidth_6;
                int _S657 = int(_S656);
                uint _S658 = 3U / _S655;
                int3 _S659 = int3(_S657, int(_S658), int(0));
                int2 minimumTile_6 = int2(_S654, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S659)).xy), uint(((_S659)).z))).x * 255.0f))));
                uint _S660 = atlasWidth_6;
                uint _S661 = 4U % atlasWidth_6;
                int _S662 = int(_S661);
                uint _S663 = 4U / _S660;
                int3 _S664 = int3(_S662, int(_S663), int(0));
                uint _S665 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S664)).xy), uint(((_S664)).z))).x * 255.0f));
                uint _S666 = atlasWidth_6;
                uint _S667 = 5U % atlasWidth_6;
                int _S668 = int(_S667);
                uint _S669 = 5U / _S666;
                int3 _S670 = int3(_S668, int(_S669), int(0));
                uint _S671 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S670)).xy), uint(((_S670)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S665, _S671);
                int2 tile_12 = int2(floor(_S176)) - minimumTile_6;
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
                    int3 _S672 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S672)).xy), uint(((_S672)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S665 + uint(tile_12.x));
                uint _S673 = atlasWidth_6;
                uint _S674 = offset_6 % atlasWidth_6;
                int _S675 = int(_S674);
                uint _S676 = offset_6 / _S673;
                int3 _S677 = int3(_S675, int(_S676), int(0));
                uint _S678 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S677)).xy), uint(((_S677)).z))).x * 255.0f));
                uint _S679 = atlasWidth_6;
                uint _S680 = offset_6 + 1U;
                uint _S681 = _S680 % atlasWidth_6;
                int _S682 = int(_S681);
                uint _S683 = _S680 / _S679;
                int3 _S684 = int3(_S682, int(_S683), int(0));
                uint slot_12 = _S678 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S684)).xy), uint(((_S684)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S685 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S685)).xy), uint(((_S685)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S686 = atlasWidth_6;
                uint _S687 = 6U % atlasWidth_6;
                int _S688 = int(_S687);
                uint _S689 = 6U / _S686;
                int3 _S690 = int3(_S688, int(_S689), int(0));
                uint _S691 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S690)).xy), uint(((_S690)).z))).x * 255.0f));
                uint _S692 = atlasWidth_6;
                uint _S693 = 7U % atlasWidth_6;
                int _S694 = int(_S693);
                uint _S695 = 7U / _S692;
                int3 _S696 = int3(_S694, int(_S695), int(0));
                uint columns_6 = _S691 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S696)).xy), uint(((_S696)).z))).x * 255.0f)) << 8U);
                uint _S697 = atlasWidth_6;
                uint _S698 = 8U % atlasWidth_6;
                int _S699 = int(_S698);
                uint _S700 = 8U / _S697;
                int3 _S701 = int3(_S699, int(_S700), int(0));
                uint _S702 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S701)).xy), uint(((_S701)).z))).x * 255.0f));
                uint _S703 = atlasWidth_6;
                uint _S704 = 9U % atlasWidth_6;
                int _S705 = int(_S704);
                uint _S706 = 9U / _S703;
                int3 _S707 = int3(_S705, int(_S706), int(0));
                uint rows_6 = ((_S702 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S707)).xy), uint(((_S707)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S665 * _S671 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S708 = atlasWidth_6 / columns_6;
                float _S709 = float(_S708);
                uint _S710 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S709, float(_S710));
                uint _S711 = slot_13 % columns_6;
                float _S712 = float(_S711);
                uint _S713 = slot_13 / columns_6;
                _S177 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S712, float(_S713)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(_S176) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S714 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(_S176)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S714);
            }
            if(hasSceneLighting_0)
            {
                int3 _S715 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S715)).xy), uint(((_S715)).z)));
                break;
            }
            uint _S716 = atlasWidth_6 / uint(_S714.x);
            float _S717 = float(_S716);
            uint _S718 = (atlasHeight_6 - 1U) / uint(_S714.y);
            float2 cellSize_13 = float2(_S717, float(_S718));
            _S177 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        emissionSample_0 = _S177;
        if((_S174.w) != 0.0f)
        {
            texture2d<float, access::sample> _S719 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S719).get_width(0)),(*((&sourceHeight_1)) = (_S719).get_height(0));
            texture2d<float, access::sample> _S720 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S720).get_width(0)),(*((&valueHeight_1)) = (_S720).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S721 = emissionSample_0;
                for(;;)
                {
                    float _S722 = _S721.w;
                    if(_S722 > 1.0f)
                    {
                        unlitColor_0 = _S721.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S723 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S723).get_width(0)),(*((&height_1)) = (_S723).get_height(0));
                    uint3 _S724 = uint3(round(_S721.xyz * float3(255.0f) ));
                    uint index_2 = ((_S724.x) | ((_S724.y) << 8U)) | ((_S724.z) << 16U);
                    if(_S722 == 0.0f)
                    {
                        _S178 = _S724;
                    }
                    else
                    {
                        _S178 = uint3(index_2, index_2, index_2);
                    }
                    uint3 address_1 = _S178 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S725 = address_1.x;
                    uint _S726 = _S725 % width_1;
                    int _S727 = int(_S726);
                    uint _S728 = _S725 / width_1;
                    int3 _S729 = int3(_S727, int(_S728), int(0));
                    float _S730 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S729)).xy), uint(((_S729)).z))).x;
                    uint _S731 = address_1.y;
                    uint _S732 = _S731 % width_1;
                    int _S733 = int(_S732);
                    uint _S734 = _S731 / width_1;
                    int3 _S735 = int3(_S733, int(_S734), int(0));
                    float _S736 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S735)).xy), uint(((_S735)).z))).x;
                    uint _S737 = address_1.z;
                    uint _S738 = _S737 % width_1;
                    int _S739 = int(_S738);
                    uint _S740 = _S737 / width_1;
                    int3 _S741 = int3(_S739, int(_S740), int(0));
                    unlitColor_0 = float3(_S730, _S736, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S741)).xy), uint(((_S741)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                emissionSample_0.z = (((&kernelContext_0)->emissiveValuesTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S176))).x;
            }
        }
        float4 _S742 = emissionSample_0;
        for(;;)
        {
            float4 _S743 = float4(_S6->compositeControls_0) ;
            if((_S743.x) != 16.0f)
            {
                _S177 = _S742;
                break;
            }
            bool _S744 = (_S743.w) >= 0.5f;
            for(;;)
            {
                if(!_S744)
                {
                    _S177 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S745 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S745).get_width(0)),(*((&atlasHeight_7)) = (_S745).get_height(0));
                int3 _S746 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S746)).xy), uint(((_S746)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S747 = atlasWidth_7;
                    uint _S748 = 2U % atlasWidth_7;
                    int _S749 = int(_S748);
                    uint _S750 = 2U / _S747;
                    int3 _S751 = int3(_S749, int(_S750), int(0));
                    int _S752 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S751)).xy), uint(((_S751)).z))).x * 255.0f)));
                    uint _S753 = atlasWidth_7;
                    uint _S754 = 3U % atlasWidth_7;
                    int _S755 = int(_S754);
                    uint _S756 = 3U / _S753;
                    int3 _S757 = int3(_S755, int(_S756), int(0));
                    int2 minimumTile_7 = int2(_S752, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S757)).xy), uint(((_S757)).z))).x * 255.0f))));
                    uint _S758 = atlasWidth_7;
                    uint _S759 = 4U % atlasWidth_7;
                    int _S760 = int(_S759);
                    uint _S761 = 4U / _S758;
                    int3 _S762 = int3(_S760, int(_S761), int(0));
                    uint _S763 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S762)).xy), uint(((_S762)).z))).x * 255.0f));
                    uint _S764 = atlasWidth_7;
                    uint _S765 = 5U % atlasWidth_7;
                    int _S766 = int(_S765);
                    uint _S767 = 5U / _S764;
                    int3 _S768 = int3(_S766, int(_S767), int(0));
                    uint _S769 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S768)).xy), uint(((_S768)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S763, _S769);
                    int2 tile_14 = int2(floor(_S176)) - minimumTile_7;
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
                        int3 _S770 = int3(int(1), int(0), int(0));
                        _S177 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S770)).xy), uint(((_S770)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S763 + uint(tile_14.x));
                    uint _S771 = atlasWidth_7;
                    uint _S772 = offset_7 % atlasWidth_7;
                    int _S773 = int(_S772);
                    uint _S774 = offset_7 / _S771;
                    int3 _S775 = int3(_S773, int(_S774), int(0));
                    uint _S776 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S775)).xy), uint(((_S775)).z))).x * 255.0f));
                    uint _S777 = atlasWidth_7;
                    uint _S778 = offset_7 + 1U;
                    uint _S779 = _S778 % atlasWidth_7;
                    int _S780 = int(_S779);
                    uint _S781 = _S778 / _S777;
                    int3 _S782 = int3(_S780, int(_S781), int(0));
                    uint slot_14 = _S776 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S782)).xy), uint(((_S782)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S783 = int3(int(1), int(0), int(0));
                        _S177 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S783)).xy), uint(((_S783)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S784 = atlasWidth_7;
                    uint _S785 = 6U % atlasWidth_7;
                    int _S786 = int(_S785);
                    uint _S787 = 6U / _S784;
                    int3 _S788 = int3(_S786, int(_S787), int(0));
                    uint _S789 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S788)).xy), uint(((_S788)).z))).x * 255.0f));
                    uint _S790 = atlasWidth_7;
                    uint _S791 = 7U % atlasWidth_7;
                    int _S792 = int(_S791);
                    uint _S793 = 7U / _S790;
                    int3 _S794 = int3(_S792, int(_S793), int(0));
                    uint columns_7 = _S789 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S794)).xy), uint(((_S794)).z))).x * 255.0f)) << 8U);
                    uint _S795 = atlasWidth_7;
                    uint _S796 = 8U % atlasWidth_7;
                    int _S797 = int(_S796);
                    uint _S798 = 8U / _S795;
                    int3 _S799 = int3(_S797, int(_S798), int(0));
                    uint _S800 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S799)).xy), uint(((_S799)).z))).x * 255.0f));
                    uint _S801 = atlasWidth_7;
                    uint _S802 = 9U % atlasWidth_7;
                    int _S803 = int(_S802);
                    uint _S804 = 9U / _S801;
                    int3 _S805 = int3(_S803, int(_S804), int(0));
                    uint rows_7 = ((_S800 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S805)).xy), uint(((_S805)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S763 * _S769 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S806 = atlasWidth_7 / columns_7;
                    float _S807 = float(_S806);
                    uint _S808 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S807, float(_S808));
                    uint _S809 = slot_15 % columns_7;
                    float _S810 = float(_S809);
                    uint _S811 = slot_15 / columns_7;
                    _S177 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S810, float(_S811)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(_S176) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S812 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(_S176)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S812);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S813 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S177 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S813)).xy), uint(((_S813)).z)));
                    break;
                }
                uint _S814 = atlasWidth_7 / uint(_S812.x);
                float _S815 = float(_S814);
                uint _S816 = (atlasHeight_7 - 1U) / uint(_S812.y);
                float2 cellSize_15 = float2(_S815, float(_S816));
                _S177 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_3 = uint(round(_S743.y));
            if(operation_3 == 1U)
            {
                _S177 = _S742 * _S177;
                break;
            }
            if(operation_3 == 2U)
            {
                _S177 = _S742 + _S177;
                break;
            }
            if(operation_3 == 3U)
            {
                _S177 = _S742 - _S177;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S743.z;
                _S177 = _S742 * float4((1.0f - factor_3))  + _S177 * float4(factor_3) ;
                break;
            }
            _S177 = _S742;
            break;
        }
        unlitColor_0 = _S177.xyz;
    }
    else
    {
        unlitColor_0 = emissiveColor_0;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool _S817 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S817)
            {
                _S177 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S818 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S818).get_width(0)),(*((&atlasHeight_8)) = (_S818).get_height(0));
            int3 _S819 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S819)).xy), uint(((_S819)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S820 = atlasWidth_8;
                uint _S821 = 2U % atlasWidth_8;
                int _S822 = int(_S821);
                uint _S823 = 2U / _S820;
                int3 _S824 = int3(_S822, int(_S823), int(0));
                int _S825 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S824)).xy), uint(((_S824)).z))).x * 255.0f)));
                uint _S826 = atlasWidth_8;
                uint _S827 = 3U % atlasWidth_8;
                int _S828 = int(_S827);
                uint _S829 = 3U / _S826;
                int3 _S830 = int3(_S828, int(_S829), int(0));
                int2 minimumTile_8 = int2(_S825, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S830)).xy), uint(((_S830)).z))).x * 255.0f))));
                uint _S831 = atlasWidth_8;
                uint _S832 = 4U % atlasWidth_8;
                int _S833 = int(_S832);
                uint _S834 = 4U / _S831;
                int3 _S835 = int3(_S833, int(_S834), int(0));
                uint _S836 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S835)).xy), uint(((_S835)).z))).x * 255.0f));
                uint _S837 = atlasWidth_8;
                uint _S838 = 5U % atlasWidth_8;
                int _S839 = int(_S838);
                uint _S840 = 5U / _S837;
                int3 _S841 = int3(_S839, int(_S840), int(0));
                uint _S842 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S841)).xy), uint(((_S841)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S836, _S842);
                int2 tile_16 = int2(floor(_S176)) - minimumTile_8;
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
                    int3 _S843 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S843)).xy), uint(((_S843)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S836 + uint(tile_16.x));
                uint _S844 = atlasWidth_8;
                uint _S845 = offset_8 % atlasWidth_8;
                int _S846 = int(_S845);
                uint _S847 = offset_8 / _S844;
                int3 _S848 = int3(_S846, int(_S847), int(0));
                uint _S849 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S848)).xy), uint(((_S848)).z))).x * 255.0f));
                uint _S850 = atlasWidth_8;
                uint _S851 = offset_8 + 1U;
                uint _S852 = _S851 % atlasWidth_8;
                int _S853 = int(_S852);
                uint _S854 = _S851 / _S850;
                int3 _S855 = int3(_S853, int(_S854), int(0));
                uint slot_16 = _S849 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S855)).xy), uint(((_S855)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S856 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S856)).xy), uint(((_S856)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S857 = atlasWidth_8;
                uint _S858 = 6U % atlasWidth_8;
                int _S859 = int(_S858);
                uint _S860 = 6U / _S857;
                int3 _S861 = int3(_S859, int(_S860), int(0));
                uint _S862 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S861)).xy), uint(((_S861)).z))).x * 255.0f));
                uint _S863 = atlasWidth_8;
                uint _S864 = 7U % atlasWidth_8;
                int _S865 = int(_S864);
                uint _S866 = 7U / _S863;
                int3 _S867 = int3(_S865, int(_S866), int(0));
                uint columns_8 = _S862 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S867)).xy), uint(((_S867)).z))).x * 255.0f)) << 8U);
                uint _S868 = atlasWidth_8;
                uint _S869 = 8U % atlasWidth_8;
                int _S870 = int(_S869);
                uint _S871 = 8U / _S868;
                int3 _S872 = int3(_S870, int(_S871), int(0));
                uint _S873 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S872)).xy), uint(((_S872)).z))).x * 255.0f));
                uint _S874 = atlasWidth_8;
                uint _S875 = 9U % atlasWidth_8;
                int _S876 = int(_S875);
                uint _S877 = 9U / _S874;
                int3 _S878 = int3(_S876, int(_S877), int(0));
                uint rows_8 = ((_S873 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S878)).xy), uint(((_S878)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S836 * _S842 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S879 = atlasWidth_8 / columns_8;
                float _S880 = float(_S879);
                uint _S881 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S880, float(_S881));
                uint _S882 = slot_17 % columns_8;
                float _S883 = float(_S882);
                uint _S884 = slot_17 / columns_8;
                _S177 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S883, float(_S884)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(_S176) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S885 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(_S176)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S885);
            }
            if(hasSceneLighting_0)
            {
                int3 _S886 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S886)).xy), uint(((_S886)).z)));
                break;
            }
            uint _S887 = atlasWidth_8 / uint(_S885.x);
            float _S888 = float(_S887);
            uint _S889 = (atlasHeight_8 - 1U) / uint(_S885.y);
            float2 cellSize_17 = float2(_S888, float(_S889));
            _S177 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        for(;;)
        {
            float4 _S890 = float4(_S6->compositeControls_0) ;
            if((_S890.x) != 64.0f)
            {
                break;
            }
            bool _S891 = (_S890.w) >= 0.5f;
            for(;;)
            {
                if(!_S891)
                {
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S892 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S892).get_width(0)),(*((&atlasHeight_9)) = (_S892).get_height(0));
                int3 _S893 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S893)).xy), uint(((_S893)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S894 = atlasWidth_9;
                    uint _S895 = 2U % atlasWidth_9;
                    int _S896 = int(_S895);
                    uint _S897 = 2U / _S894;
                    int3 _S898 = int3(_S896, int(_S897), int(0));
                    int _S899 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S898)).xy), uint(((_S898)).z))).x * 255.0f)));
                    uint _S900 = atlasWidth_9;
                    uint _S901 = 3U % atlasWidth_9;
                    int _S902 = int(_S901);
                    uint _S903 = 3U / _S900;
                    int3 _S904 = int3(_S902, int(_S903), int(0));
                    int2 minimumTile_9 = int2(_S899, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S904)).xy), uint(((_S904)).z))).x * 255.0f))));
                    uint _S905 = atlasWidth_9;
                    uint _S906 = 4U % atlasWidth_9;
                    int _S907 = int(_S906);
                    uint _S908 = 4U / _S905;
                    int3 _S909 = int3(_S907, int(_S908), int(0));
                    uint _S910 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S909)).xy), uint(((_S909)).z))).x * 255.0f));
                    uint _S911 = atlasWidth_9;
                    uint _S912 = 5U % atlasWidth_9;
                    int _S913 = int(_S912);
                    uint _S914 = 5U / _S911;
                    int3 _S915 = int3(_S913, int(_S914), int(0));
                    uint _S916 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S915)).xy), uint(((_S915)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S910, _S916);
                    int2 tile_18 = int2(floor(_S176)) - minimumTile_9;
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
                        int3 _S917 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S917)).xy), uint(((_S917)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S910 + uint(tile_18.x));
                    uint _S918 = atlasWidth_9;
                    uint _S919 = offset_9 % atlasWidth_9;
                    int _S920 = int(_S919);
                    uint _S921 = offset_9 / _S918;
                    int3 _S922 = int3(_S920, int(_S921), int(0));
                    uint _S923 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S922)).xy), uint(((_S922)).z))).x * 255.0f));
                    uint _S924 = atlasWidth_9;
                    uint _S925 = offset_9 + 1U;
                    uint _S926 = _S925 % atlasWidth_9;
                    int _S927 = int(_S926);
                    uint _S928 = _S925 / _S924;
                    int3 _S929 = int3(_S927, int(_S928), int(0));
                    uint slot_18 = _S923 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S929)).xy), uint(((_S929)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S930 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S930)).xy), uint(((_S930)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S931 = atlasWidth_9;
                    uint _S932 = 6U % atlasWidth_9;
                    int _S933 = int(_S932);
                    uint _S934 = 6U / _S931;
                    int3 _S935 = int3(_S933, int(_S934), int(0));
                    uint _S936 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S935)).xy), uint(((_S935)).z))).x * 255.0f));
                    uint _S937 = atlasWidth_9;
                    uint _S938 = 7U % atlasWidth_9;
                    int _S939 = int(_S938);
                    uint _S940 = 7U / _S937;
                    int3 _S941 = int3(_S939, int(_S940), int(0));
                    uint columns_9 = _S936 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S941)).xy), uint(((_S941)).z))).x * 255.0f)) << 8U);
                    uint _S942 = atlasWidth_9;
                    uint _S943 = 8U % atlasWidth_9;
                    int _S944 = int(_S943);
                    uint _S945 = 8U / _S942;
                    int3 _S946 = int3(_S944, int(_S945), int(0));
                    uint _S947 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S946)).xy), uint(((_S946)).z))).x * 255.0f));
                    uint _S948 = atlasWidth_9;
                    uint _S949 = 9U % atlasWidth_9;
                    int _S950 = int(_S949);
                    uint _S951 = 9U / _S948;
                    int3 _S952 = int3(_S950, int(_S951), int(0));
                    uint rows_9 = ((_S947 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S952)).xy), uint(((_S952)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S910 * _S916 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S953 = atlasWidth_9 / columns_9;
                    float _S954 = float(_S953);
                    uint _S955 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S954, float(_S955));
                    uint _S956 = slot_19 % columns_9;
                    float _S957 = float(_S956);
                    uint _S958 = slot_19 / columns_9;
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S957, float(_S958)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(_S176) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S959 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(_S176)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S959);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S960 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S960)).xy), uint(((_S960)).z)));
                    break;
                }
                uint _S961 = atlasWidth_9 / uint(_S959.x);
                float _S962 = float(_S961);
                uint _S963 = (atlasHeight_9 - 1U) / uint(_S959.y);
                float2 cellSize_19 = float2(_S962, float(_S963));
                _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_4 = uint(round(_S890.y));
            if(operation_4 == 1U)
            {
                _S177 = _S177 * _S351;
                break;
            }
            if(operation_4 == 2U)
            {
                _S177 = _S177 + _S351;
                break;
            }
            if(operation_4 == 3U)
            {
                _S177 = _S177 - _S351;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S890.z;
                _S177 = _S177 * float4((1.0f - factor_4))  + _S351 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S177.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        bool _S964 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S964)
            {
                _S177 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S965 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S965).get_width(0)),(*((&atlasHeight_10)) = (_S965).get_height(0));
            int3 _S966 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S966)).xy), uint(((_S966)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S967 = atlasWidth_10;
                uint _S968 = 2U % atlasWidth_10;
                int _S969 = int(_S968);
                uint _S970 = 2U / _S967;
                int3 _S971 = int3(_S969, int(_S970), int(0));
                int _S972 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S971)).xy), uint(((_S971)).z))).x * 255.0f)));
                uint _S973 = atlasWidth_10;
                uint _S974 = 3U % atlasWidth_10;
                int _S975 = int(_S974);
                uint _S976 = 3U / _S973;
                int3 _S977 = int3(_S975, int(_S976), int(0));
                int2 minimumTile_10 = int2(_S972, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S977)).xy), uint(((_S977)).z))).x * 255.0f))));
                uint _S978 = atlasWidth_10;
                uint _S979 = 4U % atlasWidth_10;
                int _S980 = int(_S979);
                uint _S981 = 4U / _S978;
                int3 _S982 = int3(_S980, int(_S981), int(0));
                uint _S983 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S982)).xy), uint(((_S982)).z))).x * 255.0f));
                uint _S984 = atlasWidth_10;
                uint _S985 = 5U % atlasWidth_10;
                int _S986 = int(_S985);
                uint _S987 = 5U / _S984;
                int3 _S988 = int3(_S986, int(_S987), int(0));
                uint _S989 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S988)).xy), uint(((_S988)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S983, _S989);
                int2 tile_20 = int2(floor(_S176)) - minimumTile_10;
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
                    int3 _S990 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S990)).xy), uint(((_S990)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S983 + uint(tile_20.x));
                uint _S991 = atlasWidth_10;
                uint _S992 = offset_10 % atlasWidth_10;
                int _S993 = int(_S992);
                uint _S994 = offset_10 / _S991;
                int3 _S995 = int3(_S993, int(_S994), int(0));
                uint _S996 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S995)).xy), uint(((_S995)).z))).x * 255.0f));
                uint _S997 = atlasWidth_10;
                uint _S998 = offset_10 + 1U;
                uint _S999 = _S998 % atlasWidth_10;
                int _S1000 = int(_S999);
                uint _S1001 = _S998 / _S997;
                int3 _S1002 = int3(_S1000, int(_S1001), int(0));
                uint slot_20 = _S996 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1002)).xy), uint(((_S1002)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1003 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1003)).xy), uint(((_S1003)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1004 = atlasWidth_10;
                uint _S1005 = 6U % atlasWidth_10;
                int _S1006 = int(_S1005);
                uint _S1007 = 6U / _S1004;
                int3 _S1008 = int3(_S1006, int(_S1007), int(0));
                uint _S1009 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1008)).xy), uint(((_S1008)).z))).x * 255.0f));
                uint _S1010 = atlasWidth_10;
                uint _S1011 = 7U % atlasWidth_10;
                int _S1012 = int(_S1011);
                uint _S1013 = 7U / _S1010;
                int3 _S1014 = int3(_S1012, int(_S1013), int(0));
                uint columns_10 = _S1009 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1014)).xy), uint(((_S1014)).z))).x * 255.0f)) << 8U);
                uint _S1015 = atlasWidth_10;
                uint _S1016 = 8U % atlasWidth_10;
                int _S1017 = int(_S1016);
                uint _S1018 = 8U / _S1015;
                int3 _S1019 = int3(_S1017, int(_S1018), int(0));
                uint _S1020 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1019)).xy), uint(((_S1019)).z))).x * 255.0f));
                uint _S1021 = atlasWidth_10;
                uint _S1022 = 9U % atlasWidth_10;
                int _S1023 = int(_S1022);
                uint _S1024 = 9U / _S1021;
                int3 _S1025 = int3(_S1023, int(_S1024), int(0));
                uint rows_10 = ((_S1020 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1025)).xy), uint(((_S1025)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S983 * _S989 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1026 = atlasWidth_10 / columns_10;
                float _S1027 = float(_S1026);
                uint _S1028 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1027, float(_S1028));
                uint _S1029 = slot_21 % columns_10;
                float _S1030 = float(_S1029);
                uint _S1031 = slot_21 / columns_10;
                _S177 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1030, float(_S1031)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(_S176) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1032 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(_S176)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1032);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1033 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1033)).xy), uint(((_S1033)).z)));
                break;
            }
            uint _S1034 = atlasWidth_10 / uint(_S1032.x);
            float _S1035 = float(_S1034);
            uint _S1036 = (atlasHeight_10 - 1U) / uint(_S1032.y);
            float2 cellSize_21 = float2(_S1035, float(_S1036));
            _S177 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1037 = float4(_S6->compositeControls_0) ;
            if((_S1037.x) != 128.0f)
            {
                break;
            }
            bool _S1038 = (_S1037.w) >= 0.5f;
            for(;;)
            {
                if(!_S1038)
                {
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S1039 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1039).get_width(0)),(*((&atlasHeight_11)) = (_S1039).get_height(0));
                int3 _S1040 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1040)).xy), uint(((_S1040)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1041 = atlasWidth_11;
                    uint _S1042 = 2U % atlasWidth_11;
                    int _S1043 = int(_S1042);
                    uint _S1044 = 2U / _S1041;
                    int3 _S1045 = int3(_S1043, int(_S1044), int(0));
                    int _S1046 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1045)).xy), uint(((_S1045)).z))).x * 255.0f)));
                    uint _S1047 = atlasWidth_11;
                    uint _S1048 = 3U % atlasWidth_11;
                    int _S1049 = int(_S1048);
                    uint _S1050 = 3U / _S1047;
                    int3 _S1051 = int3(_S1049, int(_S1050), int(0));
                    int2 minimumTile_11 = int2(_S1046, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1051)).xy), uint(((_S1051)).z))).x * 255.0f))));
                    uint _S1052 = atlasWidth_11;
                    uint _S1053 = 4U % atlasWidth_11;
                    int _S1054 = int(_S1053);
                    uint _S1055 = 4U / _S1052;
                    int3 _S1056 = int3(_S1054, int(_S1055), int(0));
                    uint _S1057 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1056)).xy), uint(((_S1056)).z))).x * 255.0f));
                    uint _S1058 = atlasWidth_11;
                    uint _S1059 = 5U % atlasWidth_11;
                    int _S1060 = int(_S1059);
                    uint _S1061 = 5U / _S1058;
                    int3 _S1062 = int3(_S1060, int(_S1061), int(0));
                    uint _S1063 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1062)).xy), uint(((_S1062)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1057, _S1063);
                    int2 tile_22 = int2(floor(_S176)) - minimumTile_11;
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
                        int3 _S1064 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1064)).xy), uint(((_S1064)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1057 + uint(tile_22.x));
                    uint _S1065 = atlasWidth_11;
                    uint _S1066 = offset_11 % atlasWidth_11;
                    int _S1067 = int(_S1066);
                    uint _S1068 = offset_11 / _S1065;
                    int3 _S1069 = int3(_S1067, int(_S1068), int(0));
                    uint _S1070 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1069)).xy), uint(((_S1069)).z))).x * 255.0f));
                    uint _S1071 = atlasWidth_11;
                    uint _S1072 = offset_11 + 1U;
                    uint _S1073 = _S1072 % atlasWidth_11;
                    int _S1074 = int(_S1073);
                    uint _S1075 = _S1072 / _S1071;
                    int3 _S1076 = int3(_S1074, int(_S1075), int(0));
                    uint slot_22 = _S1070 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1076)).xy), uint(((_S1076)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1077 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1077)).xy), uint(((_S1077)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1078 = atlasWidth_11;
                    uint _S1079 = 6U % atlasWidth_11;
                    int _S1080 = int(_S1079);
                    uint _S1081 = 6U / _S1078;
                    int3 _S1082 = int3(_S1080, int(_S1081), int(0));
                    uint _S1083 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1082)).xy), uint(((_S1082)).z))).x * 255.0f));
                    uint _S1084 = atlasWidth_11;
                    uint _S1085 = 7U % atlasWidth_11;
                    int _S1086 = int(_S1085);
                    uint _S1087 = 7U / _S1084;
                    int3 _S1088 = int3(_S1086, int(_S1087), int(0));
                    uint columns_11 = _S1083 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1088)).xy), uint(((_S1088)).z))).x * 255.0f)) << 8U);
                    uint _S1089 = atlasWidth_11;
                    uint _S1090 = 8U % atlasWidth_11;
                    int _S1091 = int(_S1090);
                    uint _S1092 = 8U / _S1089;
                    int3 _S1093 = int3(_S1091, int(_S1092), int(0));
                    uint _S1094 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1093)).xy), uint(((_S1093)).z))).x * 255.0f));
                    uint _S1095 = atlasWidth_11;
                    uint _S1096 = 9U % atlasWidth_11;
                    int _S1097 = int(_S1096);
                    uint _S1098 = 9U / _S1095;
                    int3 _S1099 = int3(_S1097, int(_S1098), int(0));
                    uint rows_11 = ((_S1094 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1099)).xy), uint(((_S1099)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1057 * _S1063 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1100 = atlasWidth_11 / columns_11;
                    float _S1101 = float(_S1100);
                    uint _S1102 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1101, float(_S1102));
                    uint _S1103 = slot_23 % columns_11;
                    float _S1104 = float(_S1103);
                    uint _S1105 = slot_23 / columns_11;
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1104, float(_S1105)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(_S176) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1106 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(_S176)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1106);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1107 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1107)).xy), uint(((_S1107)).z)));
                    break;
                }
                uint _S1108 = atlasWidth_11 / uint(_S1106.x);
                float _S1109 = float(_S1108);
                uint _S1110 = (atlasHeight_11 - 1U) / uint(_S1106.y);
                float2 cellSize_23 = float2(_S1109, float(_S1110));
                _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_5 = uint(round(_S1037.y));
            if(operation_5 == 1U)
            {
                _S177 = _S177 * _S351;
                break;
            }
            if(operation_5 == 2U)
            {
                _S177 = _S177 + _S351;
                break;
            }
            if(operation_5 == 3U)
            {
                _S177 = _S177 - _S351;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1037.z;
                _S177 = _S177 * float4((1.0f - factor_5))  + _S351 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S177.x);
    }
    else
    {
        occlusion_0 = _S161;
    }
    float3 specularColor_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool _S1111 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S1111)
            {
                _S177 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S1112 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1112).get_width(0)),(*((&atlasHeight_12)) = (_S1112).get_height(0));
            int3 _S1113 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1113)).xy), uint(((_S1113)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1114 = atlasWidth_12;
                uint _S1115 = 2U % atlasWidth_12;
                int _S1116 = int(_S1115);
                uint _S1117 = 2U / _S1114;
                int3 _S1118 = int3(_S1116, int(_S1117), int(0));
                int _S1119 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1118)).xy), uint(((_S1118)).z))).x * 255.0f)));
                uint _S1120 = atlasWidth_12;
                uint _S1121 = 3U % atlasWidth_12;
                int _S1122 = int(_S1121);
                uint _S1123 = 3U / _S1120;
                int3 _S1124 = int3(_S1122, int(_S1123), int(0));
                int2 minimumTile_12 = int2(_S1119, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1124)).xy), uint(((_S1124)).z))).x * 255.0f))));
                uint _S1125 = atlasWidth_12;
                uint _S1126 = 4U % atlasWidth_12;
                int _S1127 = int(_S1126);
                uint _S1128 = 4U / _S1125;
                int3 _S1129 = int3(_S1127, int(_S1128), int(0));
                uint _S1130 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1129)).xy), uint(((_S1129)).z))).x * 255.0f));
                uint _S1131 = atlasWidth_12;
                uint _S1132 = 5U % atlasWidth_12;
                int _S1133 = int(_S1132);
                uint _S1134 = 5U / _S1131;
                int3 _S1135 = int3(_S1133, int(_S1134), int(0));
                uint _S1136 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1135)).xy), uint(((_S1135)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1130, _S1136);
                int2 tile_24 = int2(floor(_S176)) - minimumTile_12;
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
                    int3 _S1137 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1137)).xy), uint(((_S1137)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1130 + uint(tile_24.x));
                uint _S1138 = atlasWidth_12;
                uint _S1139 = offset_12 % atlasWidth_12;
                int _S1140 = int(_S1139);
                uint _S1141 = offset_12 / _S1138;
                int3 _S1142 = int3(_S1140, int(_S1141), int(0));
                uint _S1143 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1142)).xy), uint(((_S1142)).z))).x * 255.0f));
                uint _S1144 = atlasWidth_12;
                uint _S1145 = offset_12 + 1U;
                uint _S1146 = _S1145 % atlasWidth_12;
                int _S1147 = int(_S1146);
                uint _S1148 = _S1145 / _S1144;
                int3 _S1149 = int3(_S1147, int(_S1148), int(0));
                uint slot_24 = _S1143 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1149)).xy), uint(((_S1149)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1150 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1150)).xy), uint(((_S1150)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1151 = atlasWidth_12;
                uint _S1152 = 6U % atlasWidth_12;
                int _S1153 = int(_S1152);
                uint _S1154 = 6U / _S1151;
                int3 _S1155 = int3(_S1153, int(_S1154), int(0));
                uint _S1156 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1155)).xy), uint(((_S1155)).z))).x * 255.0f));
                uint _S1157 = atlasWidth_12;
                uint _S1158 = 7U % atlasWidth_12;
                int _S1159 = int(_S1158);
                uint _S1160 = 7U / _S1157;
                int3 _S1161 = int3(_S1159, int(_S1160), int(0));
                uint columns_12 = _S1156 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1161)).xy), uint(((_S1161)).z))).x * 255.0f)) << 8U);
                uint _S1162 = atlasWidth_12;
                uint _S1163 = 8U % atlasWidth_12;
                int _S1164 = int(_S1163);
                uint _S1165 = 8U / _S1162;
                int3 _S1166 = int3(_S1164, int(_S1165), int(0));
                uint _S1167 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1166)).xy), uint(((_S1166)).z))).x * 255.0f));
                uint _S1168 = atlasWidth_12;
                uint _S1169 = 9U % atlasWidth_12;
                int _S1170 = int(_S1169);
                uint _S1171 = 9U / _S1168;
                int3 _S1172 = int3(_S1170, int(_S1171), int(0));
                uint rows_12 = ((_S1167 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1172)).xy), uint(((_S1172)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1130 * _S1136 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1173 = atlasWidth_12 / columns_12;
                float _S1174 = float(_S1173);
                uint _S1175 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1174, float(_S1175));
                uint _S1176 = slot_25 % columns_12;
                float _S1177 = float(_S1176);
                uint _S1178 = slot_25 / columns_12;
                _S177 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1177, float(_S1178)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(_S176) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1179 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(_S176)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1179);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1180 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1180)).xy), uint(((_S1180)).z)));
                break;
            }
            uint _S1181 = atlasWidth_12 / uint(_S1179.x);
            float _S1182 = float(_S1181);
            uint _S1183 = (atlasHeight_12 - 1U) / uint(_S1179.y);
            float2 cellSize_25 = float2(_S1182, float(_S1183));
            _S177 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1184 = float4(_S6->compositeControls_0) ;
            if((_S1184.x) != 256.0f)
            {
                break;
            }
            bool _S1185 = (_S1184.w) >= 0.5f;
            for(;;)
            {
                if(!_S1185)
                {
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S1186 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1186).get_width(0)),(*((&atlasHeight_13)) = (_S1186).get_height(0));
                int3 _S1187 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1187)).xy), uint(((_S1187)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1188 = atlasWidth_13;
                    uint _S1189 = 2U % atlasWidth_13;
                    int _S1190 = int(_S1189);
                    uint _S1191 = 2U / _S1188;
                    int3 _S1192 = int3(_S1190, int(_S1191), int(0));
                    int _S1193 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1192)).xy), uint(((_S1192)).z))).x * 255.0f)));
                    uint _S1194 = atlasWidth_13;
                    uint _S1195 = 3U % atlasWidth_13;
                    int _S1196 = int(_S1195);
                    uint _S1197 = 3U / _S1194;
                    int3 _S1198 = int3(_S1196, int(_S1197), int(0));
                    int2 minimumTile_13 = int2(_S1193, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1198)).xy), uint(((_S1198)).z))).x * 255.0f))));
                    uint _S1199 = atlasWidth_13;
                    uint _S1200 = 4U % atlasWidth_13;
                    int _S1201 = int(_S1200);
                    uint _S1202 = 4U / _S1199;
                    int3 _S1203 = int3(_S1201, int(_S1202), int(0));
                    uint _S1204 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1203)).xy), uint(((_S1203)).z))).x * 255.0f));
                    uint _S1205 = atlasWidth_13;
                    uint _S1206 = 5U % atlasWidth_13;
                    int _S1207 = int(_S1206);
                    uint _S1208 = 5U / _S1205;
                    int3 _S1209 = int3(_S1207, int(_S1208), int(0));
                    uint _S1210 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1209)).xy), uint(((_S1209)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1204, _S1210);
                    int2 tile_26 = int2(floor(_S176)) - minimumTile_13;
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
                        int3 _S1211 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1211)).xy), uint(((_S1211)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1204 + uint(tile_26.x));
                    uint _S1212 = atlasWidth_13;
                    uint _S1213 = offset_13 % atlasWidth_13;
                    int _S1214 = int(_S1213);
                    uint _S1215 = offset_13 / _S1212;
                    int3 _S1216 = int3(_S1214, int(_S1215), int(0));
                    uint _S1217 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1216)).xy), uint(((_S1216)).z))).x * 255.0f));
                    uint _S1218 = atlasWidth_13;
                    uint _S1219 = offset_13 + 1U;
                    uint _S1220 = _S1219 % atlasWidth_13;
                    int _S1221 = int(_S1220);
                    uint _S1222 = _S1219 / _S1218;
                    int3 _S1223 = int3(_S1221, int(_S1222), int(0));
                    uint slot_26 = _S1217 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1223)).xy), uint(((_S1223)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1224 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1224)).xy), uint(((_S1224)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1225 = atlasWidth_13;
                    uint _S1226 = 6U % atlasWidth_13;
                    int _S1227 = int(_S1226);
                    uint _S1228 = 6U / _S1225;
                    int3 _S1229 = int3(_S1227, int(_S1228), int(0));
                    uint _S1230 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1229)).xy), uint(((_S1229)).z))).x * 255.0f));
                    uint _S1231 = atlasWidth_13;
                    uint _S1232 = 7U % atlasWidth_13;
                    int _S1233 = int(_S1232);
                    uint _S1234 = 7U / _S1231;
                    int3 _S1235 = int3(_S1233, int(_S1234), int(0));
                    uint columns_13 = _S1230 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1235)).xy), uint(((_S1235)).z))).x * 255.0f)) << 8U);
                    uint _S1236 = atlasWidth_13;
                    uint _S1237 = 8U % atlasWidth_13;
                    int _S1238 = int(_S1237);
                    uint _S1239 = 8U / _S1236;
                    int3 _S1240 = int3(_S1238, int(_S1239), int(0));
                    uint _S1241 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1240)).xy), uint(((_S1240)).z))).x * 255.0f));
                    uint _S1242 = atlasWidth_13;
                    uint _S1243 = 9U % atlasWidth_13;
                    int _S1244 = int(_S1243);
                    uint _S1245 = 9U / _S1242;
                    int3 _S1246 = int3(_S1244, int(_S1245), int(0));
                    uint rows_13 = ((_S1241 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1246)).xy), uint(((_S1246)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1204 * _S1210 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1247 = atlasWidth_13 / columns_13;
                    float _S1248 = float(_S1247);
                    uint _S1249 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1248, float(_S1249));
                    uint _S1250 = slot_27 % columns_13;
                    float _S1251 = float(_S1250);
                    uint _S1252 = slot_27 / columns_13;
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1251, float(_S1252)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(_S176) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1253 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(_S176)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1253);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1254 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1254)).xy), uint(((_S1254)).z)));
                    break;
                }
                uint _S1255 = atlasWidth_13 / uint(_S1253.x);
                float _S1256 = float(_S1255);
                uint _S1257 = (atlasHeight_13 - 1U) / uint(_S1253.y);
                float2 cellSize_27 = float2(_S1256, float(_S1257));
                _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_6 = uint(round(_S1184.y));
            if(operation_6 == 1U)
            {
                _S177 = _S177 * _S351;
                break;
            }
            if(operation_6 == 2U)
            {
                _S177 = _S177 + _S351;
                break;
            }
            if(operation_6 == 3U)
            {
                _S177 = _S177 - _S351;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1184.z;
                _S177 = _S177 * float4((1.0f - factor_6))  + _S351 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S177.xyz);
    }
    else
    {
        specularColor_0 = _S169;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 512U) != 0U)
    {
        bool _S1258 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S1258)
            {
                _S177 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S1259 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1259).get_width(0)),(*((&atlasHeight_14)) = (_S1259).get_height(0));
            int3 _S1260 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1260)).xy), uint(((_S1260)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1261 = atlasWidth_14;
                uint _S1262 = 2U % atlasWidth_14;
                int _S1263 = int(_S1262);
                uint _S1264 = 2U / _S1261;
                int3 _S1265 = int3(_S1263, int(_S1264), int(0));
                int _S1266 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1265)).xy), uint(((_S1265)).z))).x * 255.0f)));
                uint _S1267 = atlasWidth_14;
                uint _S1268 = 3U % atlasWidth_14;
                int _S1269 = int(_S1268);
                uint _S1270 = 3U / _S1267;
                int3 _S1271 = int3(_S1269, int(_S1270), int(0));
                int2 minimumTile_14 = int2(_S1266, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1271)).xy), uint(((_S1271)).z))).x * 255.0f))));
                uint _S1272 = atlasWidth_14;
                uint _S1273 = 4U % atlasWidth_14;
                int _S1274 = int(_S1273);
                uint _S1275 = 4U / _S1272;
                int3 _S1276 = int3(_S1274, int(_S1275), int(0));
                uint _S1277 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1276)).xy), uint(((_S1276)).z))).x * 255.0f));
                uint _S1278 = atlasWidth_14;
                uint _S1279 = 5U % atlasWidth_14;
                int _S1280 = int(_S1279);
                uint _S1281 = 5U / _S1278;
                int3 _S1282 = int3(_S1280, int(_S1281), int(0));
                uint _S1283 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1282)).xy), uint(((_S1282)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1277, _S1283);
                int2 tile_28 = int2(floor(_S176)) - minimumTile_14;
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
                    int3 _S1284 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1284)).xy), uint(((_S1284)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1277 + uint(tile_28.x));
                uint _S1285 = atlasWidth_14;
                uint _S1286 = offset_14 % atlasWidth_14;
                int _S1287 = int(_S1286);
                uint _S1288 = offset_14 / _S1285;
                int3 _S1289 = int3(_S1287, int(_S1288), int(0));
                uint _S1290 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1289)).xy), uint(((_S1289)).z))).x * 255.0f));
                uint _S1291 = atlasWidth_14;
                uint _S1292 = offset_14 + 1U;
                uint _S1293 = _S1292 % atlasWidth_14;
                int _S1294 = int(_S1293);
                uint _S1295 = _S1292 / _S1291;
                int3 _S1296 = int3(_S1294, int(_S1295), int(0));
                uint slot_28 = _S1290 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1296)).xy), uint(((_S1296)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1297 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1297)).xy), uint(((_S1297)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1298 = atlasWidth_14;
                uint _S1299 = 6U % atlasWidth_14;
                int _S1300 = int(_S1299);
                uint _S1301 = 6U / _S1298;
                int3 _S1302 = int3(_S1300, int(_S1301), int(0));
                uint _S1303 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1302)).xy), uint(((_S1302)).z))).x * 255.0f));
                uint _S1304 = atlasWidth_14;
                uint _S1305 = 7U % atlasWidth_14;
                int _S1306 = int(_S1305);
                uint _S1307 = 7U / _S1304;
                int3 _S1308 = int3(_S1306, int(_S1307), int(0));
                uint columns_14 = _S1303 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1308)).xy), uint(((_S1308)).z))).x * 255.0f)) << 8U);
                uint _S1309 = atlasWidth_14;
                uint _S1310 = 8U % atlasWidth_14;
                int _S1311 = int(_S1310);
                uint _S1312 = 8U / _S1309;
                int3 _S1313 = int3(_S1311, int(_S1312), int(0));
                uint _S1314 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1313)).xy), uint(((_S1313)).z))).x * 255.0f));
                uint _S1315 = atlasWidth_14;
                uint _S1316 = 9U % atlasWidth_14;
                int _S1317 = int(_S1316);
                uint _S1318 = 9U / _S1315;
                int3 _S1319 = int3(_S1317, int(_S1318), int(0));
                uint rows_14 = ((_S1314 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1319)).xy), uint(((_S1319)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1277 * _S1283 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1320 = atlasWidth_14 / columns_14;
                float _S1321 = float(_S1320);
                uint _S1322 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1321, float(_S1322));
                uint _S1323 = slot_29 % columns_14;
                float _S1324 = float(_S1323);
                uint _S1325 = slot_29 / columns_14;
                _S177 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1324, float(_S1325)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(_S176) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1326 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(_S176)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1326);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1327 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1327)).xy), uint(((_S1327)).z)));
                break;
            }
            uint _S1328 = atlasWidth_14 / uint(_S1326.x);
            float _S1329 = float(_S1328);
            uint _S1330 = (atlasHeight_14 - 1U) / uint(_S1326.y);
            float2 cellSize_29 = float2(_S1329, float(_S1330));
            _S177 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1331 = float4(_S6->compositeControls_0) ;
            if((_S1331.x) != 512.0f)
            {
                break;
            }
            bool _S1332 = (_S1331.w) >= 0.5f;
            for(;;)
            {
                if(!_S1332)
                {
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S1333 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1333).get_width(0)),(*((&atlasHeight_15)) = (_S1333).get_height(0));
                int3 _S1334 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1334)).xy), uint(((_S1334)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1335 = atlasWidth_15;
                    uint _S1336 = 2U % atlasWidth_15;
                    int _S1337 = int(_S1336);
                    uint _S1338 = 2U / _S1335;
                    int3 _S1339 = int3(_S1337, int(_S1338), int(0));
                    int _S1340 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1339)).xy), uint(((_S1339)).z))).x * 255.0f)));
                    uint _S1341 = atlasWidth_15;
                    uint _S1342 = 3U % atlasWidth_15;
                    int _S1343 = int(_S1342);
                    uint _S1344 = 3U / _S1341;
                    int3 _S1345 = int3(_S1343, int(_S1344), int(0));
                    int2 minimumTile_15 = int2(_S1340, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1345)).xy), uint(((_S1345)).z))).x * 255.0f))));
                    uint _S1346 = atlasWidth_15;
                    uint _S1347 = 4U % atlasWidth_15;
                    int _S1348 = int(_S1347);
                    uint _S1349 = 4U / _S1346;
                    int3 _S1350 = int3(_S1348, int(_S1349), int(0));
                    uint _S1351 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1350)).xy), uint(((_S1350)).z))).x * 255.0f));
                    uint _S1352 = atlasWidth_15;
                    uint _S1353 = 5U % atlasWidth_15;
                    int _S1354 = int(_S1353);
                    uint _S1355 = 5U / _S1352;
                    int3 _S1356 = int3(_S1354, int(_S1355), int(0));
                    uint _S1357 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1356)).xy), uint(((_S1356)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1351, _S1357);
                    int2 tile_30 = int2(floor(_S176)) - minimumTile_15;
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
                        int3 _S1358 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1358)).xy), uint(((_S1358)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1351 + uint(tile_30.x));
                    uint _S1359 = atlasWidth_15;
                    uint _S1360 = offset_15 % atlasWidth_15;
                    int _S1361 = int(_S1360);
                    uint _S1362 = offset_15 / _S1359;
                    int3 _S1363 = int3(_S1361, int(_S1362), int(0));
                    uint _S1364 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1363)).xy), uint(((_S1363)).z))).x * 255.0f));
                    uint _S1365 = atlasWidth_15;
                    uint _S1366 = offset_15 + 1U;
                    uint _S1367 = _S1366 % atlasWidth_15;
                    int _S1368 = int(_S1367);
                    uint _S1369 = _S1366 / _S1365;
                    int3 _S1370 = int3(_S1368, int(_S1369), int(0));
                    uint slot_30 = _S1364 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1370)).xy), uint(((_S1370)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1371 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1371)).xy), uint(((_S1371)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1372 = atlasWidth_15;
                    uint _S1373 = 6U % atlasWidth_15;
                    int _S1374 = int(_S1373);
                    uint _S1375 = 6U / _S1372;
                    int3 _S1376 = int3(_S1374, int(_S1375), int(0));
                    uint _S1377 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1376)).xy), uint(((_S1376)).z))).x * 255.0f));
                    uint _S1378 = atlasWidth_15;
                    uint _S1379 = 7U % atlasWidth_15;
                    int _S1380 = int(_S1379);
                    uint _S1381 = 7U / _S1378;
                    int3 _S1382 = int3(_S1380, int(_S1381), int(0));
                    uint columns_15 = _S1377 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1382)).xy), uint(((_S1382)).z))).x * 255.0f)) << 8U);
                    uint _S1383 = atlasWidth_15;
                    uint _S1384 = 8U % atlasWidth_15;
                    int _S1385 = int(_S1384);
                    uint _S1386 = 8U / _S1383;
                    int3 _S1387 = int3(_S1385, int(_S1386), int(0));
                    uint _S1388 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1387)).xy), uint(((_S1387)).z))).x * 255.0f));
                    uint _S1389 = atlasWidth_15;
                    uint _S1390 = 9U % atlasWidth_15;
                    int _S1391 = int(_S1390);
                    uint _S1392 = 9U / _S1389;
                    int3 _S1393 = int3(_S1391, int(_S1392), int(0));
                    uint rows_15 = ((_S1388 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1393)).xy), uint(((_S1393)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1351 * _S1357 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1394 = atlasWidth_15 / columns_15;
                    float _S1395 = float(_S1394);
                    uint _S1396 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1395, float(_S1396));
                    uint _S1397 = slot_31 % columns_15;
                    float _S1398 = float(_S1397);
                    uint _S1399 = slot_31 / columns_15;
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1398, float(_S1399)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(_S176) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1400 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(_S176)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1400);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1401 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1401)).xy), uint(((_S1401)).z)));
                    break;
                }
                uint _S1402 = atlasWidth_15 / uint(_S1400.x);
                float _S1403 = float(_S1402);
                uint _S1404 = (atlasHeight_15 - 1U) / uint(_S1400.y);
                float2 cellSize_31 = float2(_S1403, float(_S1404));
                _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_7 = uint(round(_S1331.y));
            if(operation_7 == 1U)
            {
                _S177 = _S177 * _S351;
                break;
            }
            if(operation_7 == 2U)
            {
                _S177 = _S177 + _S351;
                break;
            }
            if(operation_7 == 3U)
            {
                _S177 = _S177 - _S351;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1331.z;
                _S177 = _S177 * float4((1.0f - factor_7))  + _S351 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S177.x);
    }
    else
    {
        clearcoatAmount_0 = _S171;
    }
    float clearcoatRoughness_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool _S1405 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S1405)
            {
                _S177 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S1406 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1406).get_width(0)),(*((&atlasHeight_16)) = (_S1406).get_height(0));
            int3 _S1407 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1407)).xy), uint(((_S1407)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1408 = atlasWidth_16;
                uint _S1409 = 2U % atlasWidth_16;
                int _S1410 = int(_S1409);
                uint _S1411 = 2U / _S1408;
                int3 _S1412 = int3(_S1410, int(_S1411), int(0));
                int _S1413 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1412)).xy), uint(((_S1412)).z))).x * 255.0f)));
                uint _S1414 = atlasWidth_16;
                uint _S1415 = 3U % atlasWidth_16;
                int _S1416 = int(_S1415);
                uint _S1417 = 3U / _S1414;
                int3 _S1418 = int3(_S1416, int(_S1417), int(0));
                int2 minimumTile_16 = int2(_S1413, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1418)).xy), uint(((_S1418)).z))).x * 255.0f))));
                uint _S1419 = atlasWidth_16;
                uint _S1420 = 4U % atlasWidth_16;
                int _S1421 = int(_S1420);
                uint _S1422 = 4U / _S1419;
                int3 _S1423 = int3(_S1421, int(_S1422), int(0));
                uint _S1424 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1423)).xy), uint(((_S1423)).z))).x * 255.0f));
                uint _S1425 = atlasWidth_16;
                uint _S1426 = 5U % atlasWidth_16;
                int _S1427 = int(_S1426);
                uint _S1428 = 5U / _S1425;
                int3 _S1429 = int3(_S1427, int(_S1428), int(0));
                uint _S1430 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1429)).xy), uint(((_S1429)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1424, _S1430);
                int2 tile_32 = int2(floor(_S176)) - minimumTile_16;
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
                    int3 _S1431 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1431)).xy), uint(((_S1431)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1424 + uint(tile_32.x));
                uint _S1432 = atlasWidth_16;
                uint _S1433 = offset_16 % atlasWidth_16;
                int _S1434 = int(_S1433);
                uint _S1435 = offset_16 / _S1432;
                int3 _S1436 = int3(_S1434, int(_S1435), int(0));
                uint _S1437 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1436)).xy), uint(((_S1436)).z))).x * 255.0f));
                uint _S1438 = atlasWidth_16;
                uint _S1439 = offset_16 + 1U;
                uint _S1440 = _S1439 % atlasWidth_16;
                int _S1441 = int(_S1440);
                uint _S1442 = _S1439 / _S1438;
                int3 _S1443 = int3(_S1441, int(_S1442), int(0));
                uint slot_32 = _S1437 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1443)).xy), uint(((_S1443)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1444 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1444)).xy), uint(((_S1444)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1445 = atlasWidth_16;
                uint _S1446 = 6U % atlasWidth_16;
                int _S1447 = int(_S1446);
                uint _S1448 = 6U / _S1445;
                int3 _S1449 = int3(_S1447, int(_S1448), int(0));
                uint _S1450 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1449)).xy), uint(((_S1449)).z))).x * 255.0f));
                uint _S1451 = atlasWidth_16;
                uint _S1452 = 7U % atlasWidth_16;
                int _S1453 = int(_S1452);
                uint _S1454 = 7U / _S1451;
                int3 _S1455 = int3(_S1453, int(_S1454), int(0));
                uint columns_16 = _S1450 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1455)).xy), uint(((_S1455)).z))).x * 255.0f)) << 8U);
                uint _S1456 = atlasWidth_16;
                uint _S1457 = 8U % atlasWidth_16;
                int _S1458 = int(_S1457);
                uint _S1459 = 8U / _S1456;
                int3 _S1460 = int3(_S1458, int(_S1459), int(0));
                uint _S1461 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1460)).xy), uint(((_S1460)).z))).x * 255.0f));
                uint _S1462 = atlasWidth_16;
                uint _S1463 = 9U % atlasWidth_16;
                int _S1464 = int(_S1463);
                uint _S1465 = 9U / _S1462;
                int3 _S1466 = int3(_S1464, int(_S1465), int(0));
                uint rows_16 = ((_S1461 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1466)).xy), uint(((_S1466)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1424 * _S1430 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1467 = atlasWidth_16 / columns_16;
                float _S1468 = float(_S1467);
                uint _S1469 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1468, float(_S1469));
                uint _S1470 = slot_33 % columns_16;
                float _S1471 = float(_S1470);
                uint _S1472 = slot_33 / columns_16;
                _S177 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1471, float(_S1472)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(_S176) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1473 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(_S176)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1473);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1474 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1474)).xy), uint(((_S1474)).z)));
                break;
            }
            uint _S1475 = atlasWidth_16 / uint(_S1473.x);
            float _S1476 = float(_S1475);
            uint _S1477 = (atlasHeight_16 - 1U) / uint(_S1473.y);
            float2 cellSize_33 = float2(_S1476, float(_S1477));
            _S177 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1478 = float4(_S6->compositeControls_0) ;
            if((_S1478.x) != 1024.0f)
            {
                break;
            }
            bool _S1479 = (_S1478.w) >= 0.5f;
            for(;;)
            {
                if(!_S1479)
                {
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S1480 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1480).get_width(0)),(*((&atlasHeight_17)) = (_S1480).get_height(0));
                int3 _S1481 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1481)).xy), uint(((_S1481)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1482 = atlasWidth_17;
                    uint _S1483 = 2U % atlasWidth_17;
                    int _S1484 = int(_S1483);
                    uint _S1485 = 2U / _S1482;
                    int3 _S1486 = int3(_S1484, int(_S1485), int(0));
                    int _S1487 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1486)).xy), uint(((_S1486)).z))).x * 255.0f)));
                    uint _S1488 = atlasWidth_17;
                    uint _S1489 = 3U % atlasWidth_17;
                    int _S1490 = int(_S1489);
                    uint _S1491 = 3U / _S1488;
                    int3 _S1492 = int3(_S1490, int(_S1491), int(0));
                    int2 minimumTile_17 = int2(_S1487, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1492)).xy), uint(((_S1492)).z))).x * 255.0f))));
                    uint _S1493 = atlasWidth_17;
                    uint _S1494 = 4U % atlasWidth_17;
                    int _S1495 = int(_S1494);
                    uint _S1496 = 4U / _S1493;
                    int3 _S1497 = int3(_S1495, int(_S1496), int(0));
                    uint _S1498 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1497)).xy), uint(((_S1497)).z))).x * 255.0f));
                    uint _S1499 = atlasWidth_17;
                    uint _S1500 = 5U % atlasWidth_17;
                    int _S1501 = int(_S1500);
                    uint _S1502 = 5U / _S1499;
                    int3 _S1503 = int3(_S1501, int(_S1502), int(0));
                    uint _S1504 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1503)).xy), uint(((_S1503)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1498, _S1504);
                    int2 tile_34 = int2(floor(_S176)) - minimumTile_17;
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
                        int3 _S1505 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1505)).xy), uint(((_S1505)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1498 + uint(tile_34.x));
                    uint _S1506 = atlasWidth_17;
                    uint _S1507 = offset_17 % atlasWidth_17;
                    int _S1508 = int(_S1507);
                    uint _S1509 = offset_17 / _S1506;
                    int3 _S1510 = int3(_S1508, int(_S1509), int(0));
                    uint _S1511 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1510)).xy), uint(((_S1510)).z))).x * 255.0f));
                    uint _S1512 = atlasWidth_17;
                    uint _S1513 = offset_17 + 1U;
                    uint _S1514 = _S1513 % atlasWidth_17;
                    int _S1515 = int(_S1514);
                    uint _S1516 = _S1513 / _S1512;
                    int3 _S1517 = int3(_S1515, int(_S1516), int(0));
                    uint slot_34 = _S1511 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1517)).xy), uint(((_S1517)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1518 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1518)).xy), uint(((_S1518)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1519 = atlasWidth_17;
                    uint _S1520 = 6U % atlasWidth_17;
                    int _S1521 = int(_S1520);
                    uint _S1522 = 6U / _S1519;
                    int3 _S1523 = int3(_S1521, int(_S1522), int(0));
                    uint _S1524 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1523)).xy), uint(((_S1523)).z))).x * 255.0f));
                    uint _S1525 = atlasWidth_17;
                    uint _S1526 = 7U % atlasWidth_17;
                    int _S1527 = int(_S1526);
                    uint _S1528 = 7U / _S1525;
                    int3 _S1529 = int3(_S1527, int(_S1528), int(0));
                    uint columns_17 = _S1524 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1529)).xy), uint(((_S1529)).z))).x * 255.0f)) << 8U);
                    uint _S1530 = atlasWidth_17;
                    uint _S1531 = 8U % atlasWidth_17;
                    int _S1532 = int(_S1531);
                    uint _S1533 = 8U / _S1530;
                    int3 _S1534 = int3(_S1532, int(_S1533), int(0));
                    uint _S1535 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1534)).xy), uint(((_S1534)).z))).x * 255.0f));
                    uint _S1536 = atlasWidth_17;
                    uint _S1537 = 9U % atlasWidth_17;
                    int _S1538 = int(_S1537);
                    uint _S1539 = 9U / _S1536;
                    int3 _S1540 = int3(_S1538, int(_S1539), int(0));
                    uint rows_17 = ((_S1535 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1540)).xy), uint(((_S1540)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1498 * _S1504 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1541 = atlasWidth_17 / columns_17;
                    float _S1542 = float(_S1541);
                    uint _S1543 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1542, float(_S1543));
                    uint _S1544 = slot_35 % columns_17;
                    float _S1545 = float(_S1544);
                    uint _S1546 = slot_35 / columns_17;
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1545, float(_S1546)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(_S176) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1547 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(_S176)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1547);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1548 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1548)).xy), uint(((_S1548)).z)));
                    break;
                }
                uint _S1549 = atlasWidth_17 / uint(_S1547.x);
                float _S1550 = float(_S1549);
                uint _S1551 = (atlasHeight_17 - 1U) / uint(_S1547.y);
                float2 cellSize_35 = float2(_S1550, float(_S1551));
                _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_8 = uint(round(_S1478.y));
            if(operation_8 == 1U)
            {
                _S177 = _S177 * _S351;
                break;
            }
            if(operation_8 == 2U)
            {
                _S177 = _S177 + _S351;
                break;
            }
            if(operation_8 == 3U)
            {
                _S177 = _S177 - _S351;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1478.z;
                _S177 = _S177 * float4((1.0f - factor_8))  + _S351 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S177.x);
    }
    else
    {
        clearcoatRoughness_0 = _S172;
    }
    float ior_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool _S1552 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S1552)
            {
                _S177 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S1553 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1553).get_width(0)),(*((&atlasHeight_18)) = (_S1553).get_height(0));
            int3 _S1554 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1554)).xy), uint(((_S1554)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1555 = atlasWidth_18;
                uint _S1556 = 2U % atlasWidth_18;
                int _S1557 = int(_S1556);
                uint _S1558 = 2U / _S1555;
                int3 _S1559 = int3(_S1557, int(_S1558), int(0));
                int _S1560 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1559)).xy), uint(((_S1559)).z))).x * 255.0f)));
                uint _S1561 = atlasWidth_18;
                uint _S1562 = 3U % atlasWidth_18;
                int _S1563 = int(_S1562);
                uint _S1564 = 3U / _S1561;
                int3 _S1565 = int3(_S1563, int(_S1564), int(0));
                int2 minimumTile_18 = int2(_S1560, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1565)).xy), uint(((_S1565)).z))).x * 255.0f))));
                uint _S1566 = atlasWidth_18;
                uint _S1567 = 4U % atlasWidth_18;
                int _S1568 = int(_S1567);
                uint _S1569 = 4U / _S1566;
                int3 _S1570 = int3(_S1568, int(_S1569), int(0));
                uint _S1571 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1570)).xy), uint(((_S1570)).z))).x * 255.0f));
                uint _S1572 = atlasWidth_18;
                uint _S1573 = 5U % atlasWidth_18;
                int _S1574 = int(_S1573);
                uint _S1575 = 5U / _S1572;
                int3 _S1576 = int3(_S1574, int(_S1575), int(0));
                uint _S1577 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1576)).xy), uint(((_S1576)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1571, _S1577);
                int2 tile_36 = int2(floor(_S176)) - minimumTile_18;
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
                    int3 _S1578 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1578)).xy), uint(((_S1578)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1571 + uint(tile_36.x));
                uint _S1579 = atlasWidth_18;
                uint _S1580 = offset_18 % atlasWidth_18;
                int _S1581 = int(_S1580);
                uint _S1582 = offset_18 / _S1579;
                int3 _S1583 = int3(_S1581, int(_S1582), int(0));
                uint _S1584 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1583)).xy), uint(((_S1583)).z))).x * 255.0f));
                uint _S1585 = atlasWidth_18;
                uint _S1586 = offset_18 + 1U;
                uint _S1587 = _S1586 % atlasWidth_18;
                int _S1588 = int(_S1587);
                uint _S1589 = _S1586 / _S1585;
                int3 _S1590 = int3(_S1588, int(_S1589), int(0));
                uint slot_36 = _S1584 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1590)).xy), uint(((_S1590)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1591 = int3(int(1), int(0), int(0));
                    _S177 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1591)).xy), uint(((_S1591)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1592 = atlasWidth_18;
                uint _S1593 = 6U % atlasWidth_18;
                int _S1594 = int(_S1593);
                uint _S1595 = 6U / _S1592;
                int3 _S1596 = int3(_S1594, int(_S1595), int(0));
                uint _S1597 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1596)).xy), uint(((_S1596)).z))).x * 255.0f));
                uint _S1598 = atlasWidth_18;
                uint _S1599 = 7U % atlasWidth_18;
                int _S1600 = int(_S1599);
                uint _S1601 = 7U / _S1598;
                int3 _S1602 = int3(_S1600, int(_S1601), int(0));
                uint columns_18 = _S1597 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1602)).xy), uint(((_S1602)).z))).x * 255.0f)) << 8U);
                uint _S1603 = atlasWidth_18;
                uint _S1604 = 8U % atlasWidth_18;
                int _S1605 = int(_S1604);
                uint _S1606 = 8U / _S1603;
                int3 _S1607 = int3(_S1605, int(_S1606), int(0));
                uint _S1608 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1607)).xy), uint(((_S1607)).z))).x * 255.0f));
                uint _S1609 = atlasWidth_18;
                uint _S1610 = 9U % atlasWidth_18;
                int _S1611 = int(_S1610);
                uint _S1612 = 9U / _S1609;
                int3 _S1613 = int3(_S1611, int(_S1612), int(0));
                uint rows_18 = ((_S1608 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1613)).xy), uint(((_S1613)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1571 * _S1577 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1614 = atlasWidth_18 / columns_18;
                float _S1615 = float(_S1614);
                uint _S1616 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1615, float(_S1616));
                uint _S1617 = slot_37 % columns_18;
                float _S1618 = float(_S1617);
                uint _S1619 = slot_37 / columns_18;
                _S177 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1618, float(_S1619)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(_S176) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1620 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(_S176)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1620);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1621 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1621)).xy), uint(((_S1621)).z)));
                break;
            }
            uint _S1622 = atlasWidth_18 / uint(_S1620.x);
            float _S1623 = float(_S1622);
            uint _S1624 = (atlasHeight_18 - 1U) / uint(_S1620.y);
            float2 cellSize_37 = float2(_S1623, float(_S1624));
            _S177 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1625 = float4(_S6->compositeControls_0) ;
            if((_S1625.x) != 2048.0f)
            {
                break;
            }
            bool _S1626 = (_S1625.w) >= 0.5f;
            for(;;)
            {
                if(!_S1626)
                {
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S1627 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1627).get_width(0)),(*((&atlasHeight_19)) = (_S1627).get_height(0));
                int3 _S1628 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1628)).xy), uint(((_S1628)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1629 = atlasWidth_19;
                    uint _S1630 = 2U % atlasWidth_19;
                    int _S1631 = int(_S1630);
                    uint _S1632 = 2U / _S1629;
                    int3 _S1633 = int3(_S1631, int(_S1632), int(0));
                    int _S1634 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1633)).xy), uint(((_S1633)).z))).x * 255.0f)));
                    uint _S1635 = atlasWidth_19;
                    uint _S1636 = 3U % atlasWidth_19;
                    int _S1637 = int(_S1636);
                    uint _S1638 = 3U / _S1635;
                    int3 _S1639 = int3(_S1637, int(_S1638), int(0));
                    int2 minimumTile_19 = int2(_S1634, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1639)).xy), uint(((_S1639)).z))).x * 255.0f))));
                    uint _S1640 = atlasWidth_19;
                    uint _S1641 = 4U % atlasWidth_19;
                    int _S1642 = int(_S1641);
                    uint _S1643 = 4U / _S1640;
                    int3 _S1644 = int3(_S1642, int(_S1643), int(0));
                    uint _S1645 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1644)).xy), uint(((_S1644)).z))).x * 255.0f));
                    uint _S1646 = atlasWidth_19;
                    uint _S1647 = 5U % atlasWidth_19;
                    int _S1648 = int(_S1647);
                    uint _S1649 = 5U / _S1646;
                    int3 _S1650 = int3(_S1648, int(_S1649), int(0));
                    uint _S1651 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1650)).xy), uint(((_S1650)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1645, _S1651);
                    int2 tile_38 = int2(floor(_S176)) - minimumTile_19;
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
                        int3 _S1652 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1652)).xy), uint(((_S1652)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1645 + uint(tile_38.x));
                    uint _S1653 = atlasWidth_19;
                    uint _S1654 = offset_19 % atlasWidth_19;
                    int _S1655 = int(_S1654);
                    uint _S1656 = offset_19 / _S1653;
                    int3 _S1657 = int3(_S1655, int(_S1656), int(0));
                    uint _S1658 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1657)).xy), uint(((_S1657)).z))).x * 255.0f));
                    uint _S1659 = atlasWidth_19;
                    uint _S1660 = offset_19 + 1U;
                    uint _S1661 = _S1660 % atlasWidth_19;
                    int _S1662 = int(_S1661);
                    uint _S1663 = _S1660 / _S1659;
                    int3 _S1664 = int3(_S1662, int(_S1663), int(0));
                    uint slot_38 = _S1658 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1664)).xy), uint(((_S1664)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1665 = int3(int(1), int(0), int(0));
                        _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1665)).xy), uint(((_S1665)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1666 = atlasWidth_19;
                    uint _S1667 = 6U % atlasWidth_19;
                    int _S1668 = int(_S1667);
                    uint _S1669 = 6U / _S1666;
                    int3 _S1670 = int3(_S1668, int(_S1669), int(0));
                    uint _S1671 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1670)).xy), uint(((_S1670)).z))).x * 255.0f));
                    uint _S1672 = atlasWidth_19;
                    uint _S1673 = 7U % atlasWidth_19;
                    int _S1674 = int(_S1673);
                    uint _S1675 = 7U / _S1672;
                    int3 _S1676 = int3(_S1674, int(_S1675), int(0));
                    uint columns_19 = _S1671 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1676)).xy), uint(((_S1676)).z))).x * 255.0f)) << 8U);
                    uint _S1677 = atlasWidth_19;
                    uint _S1678 = 8U % atlasWidth_19;
                    int _S1679 = int(_S1678);
                    uint _S1680 = 8U / _S1677;
                    int3 _S1681 = int3(_S1679, int(_S1680), int(0));
                    uint _S1682 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1681)).xy), uint(((_S1681)).z))).x * 255.0f));
                    uint _S1683 = atlasWidth_19;
                    uint _S1684 = 9U % atlasWidth_19;
                    int _S1685 = int(_S1684);
                    uint _S1686 = 9U / _S1683;
                    int3 _S1687 = int3(_S1685, int(_S1686), int(0));
                    uint rows_19 = ((_S1682 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1687)).xy), uint(((_S1687)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1645 * _S1651 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1688 = atlasWidth_19 / columns_19;
                    float _S1689 = float(_S1688);
                    uint _S1690 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1689, float(_S1690));
                    uint _S1691 = slot_39 % columns_19;
                    float _S1692 = float(_S1691);
                    uint _S1693 = slot_39 / columns_19;
                    _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1692, float(_S1693)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(_S176) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1694 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(_S176)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1694);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1695 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S351 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1695)).xy), uint(((_S1695)).z)));
                    break;
                }
                uint _S1696 = atlasWidth_19 / uint(_S1694.x);
                float _S1697 = float(_S1696);
                uint _S1698 = (atlasHeight_19 - 1U) / uint(_S1694.y);
                float2 cellSize_39 = float2(_S1697, float(_S1698));
                _S351 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_9 = uint(round(_S1625.y));
            if(operation_9 == 1U)
            {
                _S177 = _S177 * _S351;
                break;
            }
            if(operation_9 == 2U)
            {
                _S177 = _S177 + _S351;
                break;
            }
            if(operation_9 == 3U)
            {
                _S177 = _S177 - _S351;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1625.z;
                _S177 = _S177 * float4((1.0f - factor_9))  + _S351 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S177.x;
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
    float _S1699 = saturate(abs(dot(normal_2, diffuseFresnel_0)) + 0.00000999999974738f);
    float _S1700 = max(0.00100000004749745f, roughness_0);
    float _S1701 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1702 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1702;
    bool omniPbrSpecular_0 = (_S173.w) >= 0.5f;
    float dielectricLevel_0 = _S159.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1703 = float3(dielectricLevel_0) ;
        float3 _S1704 = float3(metallic_0) ;
        float3 _S1705 = mix(_S1703, diffuseColor_0, _S1704);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1703, diffuseColor_0, _S1704);
        grazingIncidence_0 = _S1705;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S165.w) >= 0.5f)
        {
            float3 _S1706 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1706;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1707 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1707);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1707);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1708 = _S2;
    float _S1709 = _S2.ambientLight_0.w;
    if(_S1709 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1710 = _S1708.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1710, _S1710)) > 0.0f;
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
    uint _S1711 = min(uint(_S1709), 128U);
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
    float4 _S1712 = float4(_S6->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S1712.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S6->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S1713 = _S2;
    uint _S1714 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S1714) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S1715 = _S1708.ambientLight_0.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S1716 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S1716;
        for(;;)
        {
            if(lightCount_0 < _S1714)
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
        domeAmbient_1 = _S1715;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S1717 = !hasSceneLighting_0;
    if(_S1717)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1711;
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
        bool _S1718;
        if(hasSceneLighting_0)
        {
            _S1718 = (((uint4(_S6->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S1718 = false;
        }
        if(_S1718)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S1719 = lightIndex_0 == 0U;
        bool _S1720;
        if(_S1719)
        {
            _S1720 = _S1717;
        }
        else
        {
            _S1720 = false;
        }
        float lightType_0;
        if(_S1720)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S1721 = 224U + lightIndex_0 * 16U;
            float _S1722 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1721)>>2]);
            float _S1723 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1721 + 4U)>>2]);
            float _S1724 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1721 + 8U)>>2]);
            float _S1725 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1721 + 12U)>>2]);
            lightType_0 = _S1725;
        }
        bool _S1726;
        if(_S1719)
        {
            _S1726 = _S1717;
        }
        else
        {
            _S1726 = false;
        }
        if(_S1726)
        {
            lightDirection_0 = normalize((float4(_S6->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S1727 = 2272U + lightIndex_0 * 16U;
            float _S1728 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1727)>>2]);
            float _S1729 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1727 + 4U)>>2]);
            float _S1730 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1727 + 8U)>>2]);
            float _S1731 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1727 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S1728, _S1729, _S1730));
        }
        bool _S1732;
        if(_S1719)
        {
            _S1732 = _S1717;
        }
        else
        {
            _S1732 = false;
        }
        if(_S1732)
        {
            roughness_0 = (float4(_S6->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S1733 = 4320U + lightIndex_0 * 16U;
            float _S1734 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1733)>>2]);
            float _S1735 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1733 + 4U)>>2]);
            float _S1736 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1733 + 8U)>>2]);
            float _S1737 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1733 + 12U)>>2]);
            roughness_0 = _S1737;
        }
        bool _S1738;
        if(_S1719)
        {
            _S1738 = _S1717;
        }
        else
        {
            _S1738 = false;
        }
        if(_S1738)
        {
            diffuseColor_0 = _S1712.xyz;
        }
        else
        {
            uint _S1739 = 4320U + lightIndex_0 * 16U;
            float _S1740 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1739)>>2]);
            float _S1741 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1739 + 4U)>>2]);
            float _S1742 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1739 + 8U)>>2]);
            float _S1743 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1739 + 12U)>>2]);
            diffuseColor_0 = float3(_S1740, _S1741, _S1742);
        }
        bool _S1744;
        if(_S1719)
        {
            _S1744 = _S1717;
        }
        else
        {
            _S1744 = false;
        }
        if(_S1744)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S1745 = 6368U + lightIndex_0 * 16U;
            float _S1746 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1745)>>2]);
            float _S1747 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1745 + 4U)>>2]);
            float _S1748 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1745 + 8U)>>2]);
            float _S1749 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1745 + 12U)>>2]);
            metallic_0 = _S1746;
        }
        bool _S1750;
        if(_S1719)
        {
            _S1750 = _S1717;
        }
        else
        {
            _S1750 = false;
        }
        if(_S1750)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S1751 = 6368U + lightIndex_0 * 16U;
            float _S1752 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1751)>>2]);
            float _S1753 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1751 + 4U)>>2]);
            float _S1754 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1751 + 8U)>>2]);
            float _S1755 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1751 + 12U)>>2]);
            clearcoatRoughness_0 = _S1753;
        }
        bool _S1756;
        if(_S1719)
        {
            _S1756 = _S1717;
        }
        else
        {
            _S1756 = false;
        }
        if(_S1756)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1757 = 8416U + lightIndex_0 * 16U;
            float _S1758 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1757)>>2]);
            float _S1759 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1757 + 4U)>>2]);
            float _S1760 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1757 + 8U)>>2]);
            float _S1761 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1757 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S1758, _S1759, _S1760));
        }
        bool _S1762;
        if(_S1719)
        {
            _S1762 = _S1717;
        }
        else
        {
            _S1762 = false;
        }
        if(_S1762)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S1763 = 10464U + lightIndex_0 * 16U;
            float _S1764 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1763)>>2]);
            float _S1765 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1763 + 4U)>>2]);
            float _S1766 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1763 + 8U)>>2]);
            float _S1767 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1763 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S1764, _S1765, _S1766));
        }
        bool _S1768;
        if(_S1719)
        {
            _S1768 = _S1717;
        }
        else
        {
            _S1768 = false;
        }
        float shapeX_0;
        if(_S1768)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S1769 = 8416U + lightIndex_0 * 16U;
            float _S1770 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1769)>>2]);
            float _S1771 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1769 + 4U)>>2]);
            float _S1772 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1769 + 8U)>>2]);
            float _S1773 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1769 + 12U)>>2]);
            shapeX_0 = _S1773;
        }
        bool _S1774;
        if(_S1719)
        {
            _S1774 = _S1717;
        }
        else
        {
            _S1774 = false;
        }
        float shapeY_0;
        if(_S1774)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S1775 = 10464U + lightIndex_0 * 16U;
            float _S1776 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1775)>>2]);
            float _S1777 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1775 + 4U)>>2]);
            float _S1778 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1775 + 8U)>>2]);
            float _S1779 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1775 + 12U)>>2]);
            shapeY_0 = _S1779;
        }
        bool _S1780;
        if(_S1719)
        {
            _S1780 = _S1717;
        }
        else
        {
            _S1780 = false;
        }
        float lightRadius_0;
        if(_S1780)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S1781 = 2272U + lightIndex_0 * 16U;
            float _S1782 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1781)>>2]);
            float _S1783 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1781 + 4U)>>2]);
            float _S1784 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1781 + 8U)>>2]);
            float _S1785 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1781 + 12U)>>2]);
            lightRadius_0 = _S1785;
        }
        bool _S1786;
        if(_S1719)
        {
            _S1786 = _S1717;
        }
        else
        {
            _S1786 = false;
        }
        if(_S1786)
        {
            domeAmbient_1 = diffuseFresnel_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S1787;
        if(_S1719)
        {
            _S1787 = _S1717;
        }
        else
        {
            _S1787 = false;
        }
        if(_S1787)
        {
            _S177 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1788 = 15296U + lightIndex_0 * 16U;
            float _S1789 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1788)>>2]);
            float _S1790 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1788 + 4U)>>2]);
            float _S1791 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1788 + 8U)>>2]);
            float _S1792 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1788 + 12U)>>2]);
            _S177 = float4(_S1789, _S1790, _S1791, _S1792);
        }
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S1793 = 12960U + lightIndex_0 * 16U;
            float _S1794 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1793)>>2]);
            float _S1795 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1793 + 4U)>>2]);
            float _S1796 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1793 + 8U)>>2]);
            float _S1797 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1793 + 12U)>>2]);
            int shadowSlot_0 = int(_S1794);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S1798 = _S2;
                    FragmentFrameParameters_0 _S1799 = _S2;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - specularColor_0;
                    }
                    else
                    {
                        color_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S1798.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S1800 = lightClip_0.w;
                    if(_S1800 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S1800) ;
                    bool _S1801;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S1801 = true;
                    }
                    else
                    {
                        _S1801 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S1802;
                    if(_S1801)
                    {
                        _S1802 = true;
                    }
                    else
                    {
                        _S1802 = (ndc_0.z) < 0.0f;
                    }
                    bool _S1803;
                    if(_S1802)
                    {
                        _S1803 = true;
                    }
                    else
                    {
                        _S1803 = (ndc_0.z) > 1.0f;
                    }
                    if(_S1803)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S1804 = _S1799.shadowTile_0[shadowSlot_0].xy;
                    float2 _S1805 = _S1799.shadowTile_0[shadowSlot_0].zw;
                    float2 _S1806 = _S1804 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S1805;
                    float texel_0 = _S1798.shadowControls_0[shadowSlot_0].w;
                    float _S1807 = max(_S1798.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S1808 = ndc_0.z - _S1798.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S1809 = float2((texel_0 * 0.5f)) ;
                    float2 _S1810 = _S1804 + _S1809;
                    float2 _S1811 = _S1804 + _S1805 - _S1809;
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
                            if(_S1808 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S1806 + float2(float(x_0), float(y_0)) * float2((_S1807 * texel_0)) , _S1810, _S1811)), level((0.0f))).x))
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
        float3 _S1812 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S1812;
        sampleOffsets_0[int(1)] = _S1812;
        sampleOffsets_0[int(2)] = _S1812;
        sampleOffsets_0[int(3)] = _S1812;
        sampleOffsets_0[int(4)] = _S1812;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S1813 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S1813 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S1813 - halfHeight_0;
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
                uint _S1814 = 224U + lightIndex_0 * 16U;
                float _S1815 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1814)>>2]);
                float _S1816 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1814 + 4U)>>2]);
                float _S1817 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1814 + 8U)>>2]);
                float _S1818 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1814 + 12U)>>2]);
                float3 toLight_0 = float3(_S1815, _S1816, _S1817) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S1819 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S1819)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S1819;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S177.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S177.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S1820 = _S177.y;
                        if(_S1820 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S1820);
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
            float _S1821 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S1821)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S1821) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S1822 = float3(_S1821) ;
            float _S1823 = max(_S1700, 0.00100000004749745f);
            float alpha_0 = _S1823 * _S1823;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S1824 = 1.0f - k_0;
            float3 _S1825 = float3(max(4.0f * normalDotLight_0 * _S1699, 1.00000000317107685e-30f)) ;
            float3 _S1826 = mix(normalIncidence_0, grazingIncidence_0, _S1822) * float3((_S1699 / (_S1699 * _S1824 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S1824 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S1825;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S1827 = max(_S1701, 0.00100000004749745f);
                float alpha_1 = _S1827 * _S1827;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S1828 = 1.0f - k_1;
                directSpecular_0 = _S1826 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S1822) * float3((_S1699 / (_S1699 * _S1828 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S1828 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S1825);
            }
            else
            {
                directSpecular_0 = _S1826;
            }
            float3 _S1829 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S1829 * _S1702) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S1829);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S1830 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S1831 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S1832 = saturate(_S1700);
        float2 _S1833 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S1831, _S1832)), level((0.0f)))).xy;
        float _S1834 = _S1833.x;
        float _S1835 = _S1833.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S1834)  + grazingIncidence_0 * float3(_S1835) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S1834 + _S1835)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S1836 = max(_S1713.domeControls_0.y, 1.0f);
        float3 _S1837 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S1836 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1713.domeControls_0.z;
            float _S1838 = _S1713.domeControls_0.w;
            for(;;)
            {
                bool _S1839 = _S1836 <= 1.0f;
                _S4 = _S1839;
                if(_S1839)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S1838, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S1836)), level((0.0f)))).xyz;
                break;
            }
            float _S1840 = _S1830.environmentControls_0.y;
            float _S1841 = _S1830.environmentControls_0.z;
            for(;;)
            {
                if(_S4)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1842 = max(_S1840, 1.0f);
                    float _S1843 = _S1842 - 1.0f;
                    float slice_0 = _S1832 * max(_S1843, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S1841, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S1842)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S1843) + v_0) / _S1842)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S1844 = max(_S1840, 1.0f);
                float total_0 = _S1844 * _S1836;
                float _S1845 = _S1844 - 1.0f;
                float slice_1 = _S1832 * max(_S1845, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S1841, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S1844;
                specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S1845) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S1846 = max(_S1840, 1.0f);
                        float _S1847 = _S1846 - 1.0f;
                        float slice_2 = saturate(_S1701) * max(_S1847, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S1841, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S1846)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S1847) + v_2) / _S1846)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1848 = max(_S1840, 1.0f);
                    float total_1 = _S1848 * _S1836;
                    float _S1849 = _S1848 - 1.0f;
                    float slice_3 = saturate(_S1701) * max(_S1849, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S1841, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S1848;
                    diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S1849) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightBitangent_0 = diffuseFresnel_0;
            }
            else
            {
                lightBitangent_0 = _S1837;
            }
            lightDirection_0 = diffuseColor_0;
            lightTangent_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightDirection_0 = _S1837;
            lightTangent_0 = _S1837;
            lightBitangent_0 = _S1837;
            for(;;)
            {
                if(sampleIndex_0 < _S1714)
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
                float _S1850 = _S1713.domeControls_0.w;
                for(;;)
                {
                    bool _S1851 = _S1836 <= 1.0f;
                    _S5 = _S1851;
                    if(_S1851)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S1850, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S1836)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightDirection_0 + diffuseColor_0;
                float _S1852 = _S1830.environmentControls_0.y;
                float _S1853 = _S1830.environmentControls_0.z;
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1854 = max(_S1852, 1.0f);
                        float _S1855 = _S1854 - 1.0f;
                        float slice_4 = _S1832 * max(_S1855, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S1853, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S1854)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S1855) + v_4) / _S1854)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1856 = max(_S1852, 1.0f);
                    float total_2 = _S1856 * _S1836;
                    float _S1857 = _S1856 - 1.0f;
                    float slice_5 = _S1832 * max(_S1857, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S1853, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S1856;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S1857) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
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
                            float _S1858 = max(_S1852, 1.0f);
                            float _S1859 = _S1858 - 1.0f;
                            float slice_6 = saturate(_S1701) * max(_S1859, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S1853, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S1858)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S1859) + v_6) / _S1858)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1860 = max(_S1852, 1.0f);
                        float total_3 = _S1860 * _S1836;
                        float _S1861 = _S1860 - 1.0f;
                        float slice_7 = saturate(_S1701) * max(_S1861, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S1853, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S1860;
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S1861) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S1862 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S1831, saturate(_S1701))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightBitangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S1862.x)  + float3(_S1862.y) );
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
    pixelOutput_0 _S1863 = { float4(color_3, opacity_0) };
    return _S1863;
}
