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
    float2 _S191;
    float2 _S192;
    bool hasSceneLighting_0;
    float4 _S193;
    uint3 _S194;
    if((textureMask_0 & 2U) != 0U)
    {
        bool useSecondary_0 = (secondaryMask_0 & 2U) != 0U;
        if((alternateMask_0 & 2U) != 0U)
        {
            if(useSecondary_0)
            {
                _S191 = _S190;
            }
            else
            {
                _S191 = _S189;
            }
        }
        else
        {
            if(useSecondary_0)
            {
                _S191 = _S182;
            }
            else
            {
                _S191 = _S181;
            }
        }
        thread float4 diffuseSample_0;
        bool _S195 = (udimMask_0 & 2U) != 0U;
        uint maskU_0 = uint(round(_S175.y));
        uint maskV_0 = uint(round(_S175.z));
        bool _S196 = (maskU_0 & 2U) != 0U;
        if(_S196)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S197 = (maskV_0 & 2U) != 0U;
        if(_S197)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S198 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S195)
            {
                for(;;)
                {
                    if(_S198 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S199 = (&kernelContext_0)->baseColorTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S199).get_width(0)),(*((&height_0)) = (_S199).get_height(0));
                    float2 size_0 = float2(float(width_0), float(height_0));
                    float2 _S200 = float2(0.5f) ;
                    float2 remapped_0 = clamp((_S191 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S200 / size_0, (size_0 - _S200) / size_0);
                    if((_S198 & 1U) != 0U)
                    {
                        roughness_0 = remapped_0.x;
                    }
                    else
                    {
                        roughness_0 = _S191.x;
                    }
                    if((_S198 & 2U) != 0U)
                    {
                        metallic_0 = remapped_0.y;
                    }
                    else
                    {
                        metallic_0 = _S191.y;
                    }
                    _S192 = float2(roughness_0, metallic_0);
                    break;
                }
                _S193 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S192)));
                break;
            }
            texture2d<float, access::sample> _S201 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S201).get_width(0)),(*((&atlasHeight_0)) = (_S201).get_height(0));
            int3 _S202 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S202)).xy), uint(((_S202)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S203 = atlasWidth_0;
                uint _S204 = 2U % atlasWidth_0;
                int _S205 = int(_S204);
                uint _S206 = 2U / _S203;
                int3 _S207 = int3(_S205, int(_S206), int(0));
                int _S208 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S207)).xy), uint(((_S207)).z))).x * 255.0f)));
                uint _S209 = atlasWidth_0;
                uint _S210 = 3U % atlasWidth_0;
                int _S211 = int(_S210);
                uint _S212 = 3U / _S209;
                int3 _S213 = int3(_S211, int(_S212), int(0));
                int2 minimumTile_0 = int2(_S208, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S213)).xy), uint(((_S213)).z))).x * 255.0f))));
                uint _S214 = atlasWidth_0;
                uint _S215 = 4U % atlasWidth_0;
                int _S216 = int(_S215);
                uint _S217 = 4U / _S214;
                int3 _S218 = int3(_S216, int(_S217), int(0));
                uint _S219 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S218)).xy), uint(((_S218)).z))).x * 255.0f));
                uint _S220 = atlasWidth_0;
                uint _S221 = 5U % atlasWidth_0;
                int _S222 = int(_S221);
                uint _S223 = 5U / _S220;
                int3 _S224 = int3(_S222, int(_S223), int(0));
                uint _S225 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S224)).xy), uint(((_S224)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S219, _S225);
                int2 tile_0 = int2(floor(_S191)) - minimumTile_0;
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
                    int3 _S226 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S226)).xy), uint(((_S226)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S219 + uint(tile_0.x));
                uint _S227 = atlasWidth_0;
                uint _S228 = offset_0 % atlasWidth_0;
                int _S229 = int(_S228);
                uint _S230 = offset_0 / _S227;
                int3 _S231 = int3(_S229, int(_S230), int(0));
                uint _S232 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S231)).xy), uint(((_S231)).z))).x * 255.0f));
                uint _S233 = atlasWidth_0;
                uint _S234 = offset_0 + 1U;
                uint _S235 = _S234 % atlasWidth_0;
                int _S236 = int(_S235);
                uint _S237 = _S234 / _S233;
                int3 _S238 = int3(_S236, int(_S237), int(0));
                uint slot_0 = _S232 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S238)).xy), uint(((_S238)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S239 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S239)).xy), uint(((_S239)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S240 = atlasWidth_0;
                uint _S241 = 6U % atlasWidth_0;
                int _S242 = int(_S241);
                uint _S243 = 6U / _S240;
                int3 _S244 = int3(_S242, int(_S243), int(0));
                uint _S245 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S244)).xy), uint(((_S244)).z))).x * 255.0f));
                uint _S246 = atlasWidth_0;
                uint _S247 = 7U % atlasWidth_0;
                int _S248 = int(_S247);
                uint _S249 = 7U / _S246;
                int3 _S250 = int3(_S248, int(_S249), int(0));
                uint columns_0 = _S245 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S250)).xy), uint(((_S250)).z))).x * 255.0f)) << 8U);
                uint _S251 = atlasWidth_0;
                uint _S252 = 8U % atlasWidth_0;
                int _S253 = int(_S252);
                uint _S254 = 8U / _S251;
                int3 _S255 = int3(_S253, int(_S254), int(0));
                uint _S256 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S255)).xy), uint(((_S255)).z))).x * 255.0f));
                uint _S257 = atlasWidth_0;
                uint _S258 = 9U % atlasWidth_0;
                int _S259 = int(_S258);
                uint _S260 = 9U / _S257;
                int3 _S261 = int3(_S259, int(_S260), int(0));
                uint rows_0 = ((_S256 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S261)).xy), uint(((_S261)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S219 * _S225 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S262 = atlasWidth_0 / columns_0;
                float _S263 = float(_S262);
                uint _S264 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S263, float(_S264));
                uint _S265 = slot_1 % columns_0;
                float _S266 = float(_S265);
                uint _S267 = slot_1 / columns_0;
                _S193 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S266, float(_S267)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S191) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S268 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(_S191)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S268);
            }
            if(hasSceneLighting_0)
            {
                int3 _S269 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S193 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S269)).xy), uint(((_S269)).z)));
                break;
            }
            uint _S270 = atlasWidth_0 / uint(_S268.x);
            float _S271 = float(_S270);
            uint _S272 = (atlasHeight_0 - 1U) / uint(_S268.y);
            float2 cellSize_1 = float2(_S271, float(_S272));
            _S193 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S193;
        float4 _S273 = float4(_S6->compositeControls_0) ;
        float _S274 = _S273.x;
        if(_S274 == 0.0f)
        {
            hasSceneLighting_0 = (_S273.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S275 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S275).get_width(0)),(*((&sourceHeight_0)) = (_S275).get_height(0));
            texture2d<float, access::sample> _S276 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S276).get_width(0)),(*((&valueHeight_0)) = (_S276).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S277 = diffuseSample_0;
                for(;;)
                {
                    float _S278 = _S277.w;
                    if(_S278 > 1.0f)
                    {
                        diffuseColor_0 = _S277.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S279 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S279).get_width(0)),(*((&height_1)) = (_S279).get_height(0));
                    uint3 _S280 = uint3(round(_S277.xyz * float3(255.0f) ));
                    uint index_1 = ((_S280.x) | ((_S280.y) << 8U)) | ((_S280.z) << 16U);
                    if(_S278 == 0.0f)
                    {
                        _S194 = _S280;
                    }
                    else
                    {
                        _S194 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S194 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S281 = address_0.x;
                    uint _S282 = _S281 % width_1;
                    int _S283 = int(_S282);
                    uint _S284 = _S281 / width_1;
                    int3 _S285 = int3(_S283, int(_S284), int(0));
                    float _S286 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S285)).xy), uint(((_S285)).z))).x;
                    uint _S287 = address_0.y;
                    uint _S288 = _S287 % width_1;
                    int _S289 = int(_S288);
                    uint _S290 = _S287 / width_1;
                    int3 _S291 = int3(_S289, int(_S290), int(0));
                    float _S292 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S291)).xy), uint(((_S291)).z))).x;
                    uint _S293 = address_0.z;
                    uint _S294 = _S293 % width_1;
                    int _S295 = int(_S294);
                    uint _S296 = _S293 / width_1;
                    int3 _S297 = int3(_S295, int(_S296), int(0));
                    diffuseColor_0 = float3(_S286, _S292, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S297)).xy), uint(((_S297)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                if(_S196)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S197)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S298 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S298 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S299 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S299).get_width(0)),(*((&height_2)) = (_S299).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S300 = float2(0.5f) ;
                    float2 remapped_1 = clamp((_S191 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S300 / size_1, (size_1 - _S300) / size_1);
                    if((_S298 & 1U) != 0U)
                    {
                        roughness_0 = remapped_1.x;
                    }
                    else
                    {
                        roughness_0 = _S191.x;
                    }
                    if((_S298 & 2U) != 0U)
                    {
                        metallic_0 = remapped_1.y;
                    }
                    else
                    {
                        metallic_0 = _S191.y;
                    }
                    _S192 = float2(roughness_0, metallic_0);
                    break;
                }
                diffuseSample_0.z = (((&kernelContext_0)->diffuseValuesTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S192))).x;
            }
        }
        float4 _S301 = diffuseSample_0;
        for(;;)
        {
            if(_S274 != 2.0f)
            {
                _S193 = _S301;
                break;
            }
            bool _S302 = (_S273.w) >= 0.5f;
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
            uint _S303 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S302)
                {
                    for(;;)
                    {
                        if(_S303 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S304 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_3;
                        thread uint height_3;
                        (*((&width_3)) = (_S304).get_width(0)),(*((&height_3)) = (_S304).get_height(0));
                        float2 size_2 = float2(float(width_3), float(height_3));
                        float2 _S305 = float2(0.5f) ;
                        float2 remapped_2 = clamp((_S191 * (size_2 - float2(2.0f) ) + float2(1.0f) ) / size_2, _S305 / size_2, (size_2 - _S305) / size_2);
                        if((_S303 & 1U) != 0U)
                        {
                            roughness_0 = remapped_2.x;
                        }
                        else
                        {
                            roughness_0 = _S191.x;
                        }
                        if((_S303 & 2U) != 0U)
                        {
                            metallic_0 = remapped_2.y;
                        }
                        else
                        {
                            metallic_0 = _S191.y;
                        }
                        _S191 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S193 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S191)));
                    break;
                }
                texture2d<float, access::sample> _S306 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S306).get_width(0)),(*((&atlasHeight_1)) = (_S306).get_height(0));
                int3 _S307 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S307)).xy), uint(((_S307)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S308 = atlasWidth_1;
                    uint _S309 = 2U % atlasWidth_1;
                    int _S310 = int(_S309);
                    uint _S311 = 2U / _S308;
                    int3 _S312 = int3(_S310, int(_S311), int(0));
                    int _S313 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S312)).xy), uint(((_S312)).z))).x * 255.0f)));
                    uint _S314 = atlasWidth_1;
                    uint _S315 = 3U % atlasWidth_1;
                    int _S316 = int(_S315);
                    uint _S317 = 3U / _S314;
                    int3 _S318 = int3(_S316, int(_S317), int(0));
                    int2 minimumTile_1 = int2(_S313, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S318)).xy), uint(((_S318)).z))).x * 255.0f))));
                    uint _S319 = atlasWidth_1;
                    uint _S320 = 4U % atlasWidth_1;
                    int _S321 = int(_S320);
                    uint _S322 = 4U / _S319;
                    int3 _S323 = int3(_S321, int(_S322), int(0));
                    uint _S324 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S323)).xy), uint(((_S323)).z))).x * 255.0f));
                    uint _S325 = atlasWidth_1;
                    uint _S326 = 5U % atlasWidth_1;
                    int _S327 = int(_S326);
                    uint _S328 = 5U / _S325;
                    int3 _S329 = int3(_S327, int(_S328), int(0));
                    uint _S330 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S329)).xy), uint(((_S329)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S324, _S330);
                    int2 tile_2 = int2(floor(_S191)) - minimumTile_1;
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
                        int3 _S331 = int3(int(1), int(0), int(0));
                        _S193 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S331)).xy), uint(((_S331)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S324 + uint(tile_2.x));
                    uint _S332 = atlasWidth_1;
                    uint _S333 = offset_1 % atlasWidth_1;
                    int _S334 = int(_S333);
                    uint _S335 = offset_1 / _S332;
                    int3 _S336 = int3(_S334, int(_S335), int(0));
                    uint _S337 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S336)).xy), uint(((_S336)).z))).x * 255.0f));
                    uint _S338 = atlasWidth_1;
                    uint _S339 = offset_1 + 1U;
                    uint _S340 = _S339 % atlasWidth_1;
                    int _S341 = int(_S340);
                    uint _S342 = _S339 / _S338;
                    int3 _S343 = int3(_S341, int(_S342), int(0));
                    uint slot_2 = _S337 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S343)).xy), uint(((_S343)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S344 = int3(int(1), int(0), int(0));
                        _S193 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S344)).xy), uint(((_S344)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S345 = atlasWidth_1;
                    uint _S346 = 6U % atlasWidth_1;
                    int _S347 = int(_S346);
                    uint _S348 = 6U / _S345;
                    int3 _S349 = int3(_S347, int(_S348), int(0));
                    uint _S350 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S349)).xy), uint(((_S349)).z))).x * 255.0f));
                    uint _S351 = atlasWidth_1;
                    uint _S352 = 7U % atlasWidth_1;
                    int _S353 = int(_S352);
                    uint _S354 = 7U / _S351;
                    int3 _S355 = int3(_S353, int(_S354), int(0));
                    uint columns_1 = _S350 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S355)).xy), uint(((_S355)).z))).x * 255.0f)) << 8U);
                    uint _S356 = atlasWidth_1;
                    uint _S357 = 8U % atlasWidth_1;
                    int _S358 = int(_S357);
                    uint _S359 = 8U / _S356;
                    int3 _S360 = int3(_S358, int(_S359), int(0));
                    uint _S361 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S360)).xy), uint(((_S360)).z))).x * 255.0f));
                    uint _S362 = atlasWidth_1;
                    uint _S363 = 9U % atlasWidth_1;
                    int _S364 = int(_S363);
                    uint _S365 = 9U / _S362;
                    int3 _S366 = int3(_S364, int(_S365), int(0));
                    uint rows_1 = ((_S361 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S366)).xy), uint(((_S366)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S324 * _S330 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S367 = atlasWidth_1 / columns_1;
                    float _S368 = float(_S367);
                    uint _S369 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S368, float(_S369));
                    uint _S370 = slot_3 % columns_1;
                    float _S371 = float(_S370);
                    uint _S372 = slot_3 / columns_1;
                    _S193 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S371, float(_S372)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S191) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S373 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(_S191)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S373);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S374 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S193 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S374)).xy), uint(((_S374)).z)));
                    break;
                }
                uint _S375 = atlasWidth_1 / uint(_S373.x);
                float _S376 = float(_S375);
                uint _S377 = (atlasHeight_1 - 1U) / uint(_S373.y);
                float2 cellSize_3 = float2(_S376, float(_S377));
                _S193 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S273.y));
            if(operation_0 == 1U)
            {
                _S193 = _S301 * _S193;
                break;
            }
            if(operation_0 == 2U)
            {
                _S193 = _S301 + _S193;
                break;
            }
            if(operation_0 == 3U)
            {
                _S193 = _S301 - _S193;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S273.z;
                _S193 = _S301 * float4((1.0f - factor_0))  + _S193 * float4(factor_0) ;
                break;
            }
            _S193 = _S301;
            break;
        }
        diffuseColor_0 = _S193.xyz;
    }
    float4 _S378;
    if((textureMask_0 & 8U) != 0U)
    {
        bool useSecondary_1 = (secondaryMask_0 & 8U) != 0U;
        if((alternateMask_0 & 8U) != 0U)
        {
            if(useSecondary_1)
            {
                _S191 = _S190;
            }
            else
            {
                _S191 = _S189;
            }
        }
        else
        {
            if(useSecondary_1)
            {
                _S191 = _S182;
            }
            else
            {
                _S191 = _S181;
            }
        }
        bool _S379 = (udimMask_0 & 8U) != 0U;
        uint maskU_1 = uint(round(_S175.y));
        uint maskV_1 = uint(round(_S175.z));
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
        uint _S380 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S379)
            {
                for(;;)
                {
                    if(_S380 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S381 = (&kernelContext_0)->roughnessMetallicTexture_0;
                    thread uint width_4;
                    thread uint height_4;
                    (*((&width_4)) = (_S381).get_width(0)),(*((&height_4)) = (_S381).get_height(0));
                    float2 size_3 = float2(float(width_4), float(height_4));
                    float2 _S382 = float2(0.5f) ;
                    float2 remapped_3 = clamp((_S191 * (size_3 - float2(2.0f) ) + float2(1.0f) ) / size_3, _S382 / size_3, (size_3 - _S382) / size_3);
                    if((_S380 & 1U) != 0U)
                    {
                        roughness_0 = remapped_3.x;
                    }
                    else
                    {
                        roughness_0 = _S191.x;
                    }
                    if((_S380 & 2U) != 0U)
                    {
                        metallic_0 = remapped_3.y;
                    }
                    else
                    {
                        metallic_0 = _S191.y;
                    }
                    _S192 = float2(roughness_0, metallic_0);
                    break;
                }
                _S193 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S192)));
                break;
            }
            texture2d<float, access::sample> _S383 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S383).get_width(0)),(*((&atlasHeight_2)) = (_S383).get_height(0));
            int3 _S384 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S384)).xy), uint(((_S384)).z))) * float4(255.0f) );
            if((metadata_2.x) == 255.0f)
            {
                uint _S385 = atlasWidth_2;
                uint _S386 = 2U % atlasWidth_2;
                int _S387 = int(_S386);
                uint _S388 = 2U / _S385;
                int3 _S389 = int3(_S387, int(_S388), int(0));
                int _S390 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S389)).xy), uint(((_S389)).z))).x * 255.0f)));
                uint _S391 = atlasWidth_2;
                uint _S392 = 3U % atlasWidth_2;
                int _S393 = int(_S392);
                uint _S394 = 3U / _S391;
                int3 _S395 = int3(_S393, int(_S394), int(0));
                int2 minimumTile_2 = int2(_S390, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S395)).xy), uint(((_S395)).z))).x * 255.0f))));
                uint _S396 = atlasWidth_2;
                uint _S397 = 4U % atlasWidth_2;
                int _S398 = int(_S397);
                uint _S399 = 4U / _S396;
                int3 _S400 = int3(_S398, int(_S399), int(0));
                uint _S401 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S400)).xy), uint(((_S400)).z))).x * 255.0f));
                uint _S402 = atlasWidth_2;
                uint _S403 = 5U % atlasWidth_2;
                int _S404 = int(_S403);
                uint _S405 = 5U / _S402;
                int3 _S406 = int3(_S404, int(_S405), int(0));
                uint _S407 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S406)).xy), uint(((_S406)).z))).x * 255.0f));
                uint2 grid_2 = uint2(_S401, _S407);
                int2 tile_4 = int2(floor(_S191)) - minimumTile_2;
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
                    int3 _S408 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S408)).xy), uint(((_S408)).z)));
                    break;
                }
                uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S401 + uint(tile_4.x));
                uint _S409 = atlasWidth_2;
                uint _S410 = offset_2 % atlasWidth_2;
                int _S411 = int(_S410);
                uint _S412 = offset_2 / _S409;
                int3 _S413 = int3(_S411, int(_S412), int(0));
                uint _S414 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S413)).xy), uint(((_S413)).z))).x * 255.0f));
                uint _S415 = atlasWidth_2;
                uint _S416 = offset_2 + 1U;
                uint _S417 = _S416 % atlasWidth_2;
                int _S418 = int(_S417);
                uint _S419 = _S416 / _S415;
                int3 _S420 = int3(_S418, int(_S419), int(0));
                uint slot_4 = _S414 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S420)).xy), uint(((_S420)).z))).x * 255.0f)) << 8U);
                if(slot_4 == 0U)
                {
                    int3 _S421 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S421)).xy), uint(((_S421)).z)));
                    break;
                }
                uint slot_5 = slot_4 - 1U;
                uint _S422 = atlasWidth_2;
                uint _S423 = 6U % atlasWidth_2;
                int _S424 = int(_S423);
                uint _S425 = 6U / _S422;
                int3 _S426 = int3(_S424, int(_S425), int(0));
                uint _S427 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S426)).xy), uint(((_S426)).z))).x * 255.0f));
                uint _S428 = atlasWidth_2;
                uint _S429 = 7U % atlasWidth_2;
                int _S430 = int(_S429);
                uint _S431 = 7U / _S428;
                int3 _S432 = int3(_S430, int(_S431), int(0));
                uint columns_2 = _S427 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S432)).xy), uint(((_S432)).z))).x * 255.0f)) << 8U);
                uint _S433 = atlasWidth_2;
                uint _S434 = 8U % atlasWidth_2;
                int _S435 = int(_S434);
                uint _S436 = 8U / _S433;
                int3 _S437 = int3(_S435, int(_S436), int(0));
                uint _S438 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S437)).xy), uint(((_S437)).z))).x * 255.0f));
                uint _S439 = atlasWidth_2;
                uint _S440 = 9U % atlasWidth_2;
                int _S441 = int(_S440);
                uint _S442 = 9U / _S439;
                int3 _S443 = int3(_S441, int(_S442), int(0));
                uint rows_2 = ((_S438 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S443)).xy), uint(((_S443)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
                uint metadataRows_2 = (10U + 2U * _S401 * _S407 + atlasWidth_2 - 1U) / atlasWidth_2;
                uint _S444 = atlasWidth_2 / columns_2;
                float _S445 = float(_S444);
                uint _S446 = (atlasHeight_2 - metadataRows_2) / rows_2;
                float2 cellSize_4 = float2(_S445, float(_S446));
                uint _S447 = slot_5 % columns_2;
                float _S448 = float(_S447);
                uint _S449 = slot_5 / columns_2;
                _S193 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S448, float(_S449)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(_S191) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
                break;
            }
            int2 _S450 = int2(metadata_2.zw);
            int2 tile_5 = int2(floor(_S191)) - int2(metadata_2.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S450);
            }
            if(hasSceneLighting_0)
            {
                int3 _S451 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S193 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S451)).xy), uint(((_S451)).z)));
                break;
            }
            uint _S452 = atlasWidth_2 / uint(_S450.x);
            float _S453 = float(_S452);
            uint _S454 = (atlasHeight_2 - 1U) / uint(_S450.y);
            float2 cellSize_5 = float2(_S453, float(_S454));
            _S193 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        for(;;)
        {
            float4 _S455 = float4(_S6->compositeControls_0) ;
            if((_S455.x) != 8.0f)
            {
                break;
            }
            bool _S456 = (_S455.w) >= 0.5f;
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
            uint _S457 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S456)
                {
                    for(;;)
                    {
                        if(_S457 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S458 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_5;
                        thread uint height_5;
                        (*((&width_5)) = (_S458).get_width(0)),(*((&height_5)) = (_S458).get_height(0));
                        float2 size_4 = float2(float(width_5), float(height_5));
                        float2 _S459 = float2(0.5f) ;
                        float2 remapped_4 = clamp((_S191 * (size_4 - float2(2.0f) ) + float2(1.0f) ) / size_4, _S459 / size_4, (size_4 - _S459) / size_4);
                        if((_S457 & 1U) != 0U)
                        {
                            roughness_0 = remapped_4.x;
                        }
                        else
                        {
                            roughness_0 = _S191.x;
                        }
                        if((_S457 & 2U) != 0U)
                        {
                            metallic_0 = remapped_4.y;
                        }
                        else
                        {
                            metallic_0 = _S191.y;
                        }
                        _S191 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S191)));
                    break;
                }
                texture2d<float, access::sample> _S460 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_3;
                thread uint atlasHeight_3;
                (*((&atlasWidth_3)) = (_S460).get_width(0)),(*((&atlasHeight_3)) = (_S460).get_height(0));
                int3 _S461 = int3(int(0), int(0), int(0));
                float4 metadata_3 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S461)).xy), uint(((_S461)).z))) * float4(255.0f) );
                if((metadata_3.x) == 255.0f)
                {
                    uint _S462 = atlasWidth_3;
                    uint _S463 = 2U % atlasWidth_3;
                    int _S464 = int(_S463);
                    uint _S465 = 2U / _S462;
                    int3 _S466 = int3(_S464, int(_S465), int(0));
                    int _S467 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S466)).xy), uint(((_S466)).z))).x * 255.0f)));
                    uint _S468 = atlasWidth_3;
                    uint _S469 = 3U % atlasWidth_3;
                    int _S470 = int(_S469);
                    uint _S471 = 3U / _S468;
                    int3 _S472 = int3(_S470, int(_S471), int(0));
                    int2 minimumTile_3 = int2(_S467, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S472)).xy), uint(((_S472)).z))).x * 255.0f))));
                    uint _S473 = atlasWidth_3;
                    uint _S474 = 4U % atlasWidth_3;
                    int _S475 = int(_S474);
                    uint _S476 = 4U / _S473;
                    int3 _S477 = int3(_S475, int(_S476), int(0));
                    uint _S478 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S477)).xy), uint(((_S477)).z))).x * 255.0f));
                    uint _S479 = atlasWidth_3;
                    uint _S480 = 5U % atlasWidth_3;
                    int _S481 = int(_S480);
                    uint _S482 = 5U / _S479;
                    int3 _S483 = int3(_S481, int(_S482), int(0));
                    uint _S484 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S483)).xy), uint(((_S483)).z))).x * 255.0f));
                    uint2 grid_3 = uint2(_S478, _S484);
                    int2 tile_6 = int2(floor(_S191)) - minimumTile_3;
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
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S485)).xy), uint(((_S485)).z)));
                        break;
                    }
                    uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S478 + uint(tile_6.x));
                    uint _S486 = atlasWidth_3;
                    uint _S487 = offset_3 % atlasWidth_3;
                    int _S488 = int(_S487);
                    uint _S489 = offset_3 / _S486;
                    int3 _S490 = int3(_S488, int(_S489), int(0));
                    uint _S491 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S490)).xy), uint(((_S490)).z))).x * 255.0f));
                    uint _S492 = atlasWidth_3;
                    uint _S493 = offset_3 + 1U;
                    uint _S494 = _S493 % atlasWidth_3;
                    int _S495 = int(_S494);
                    uint _S496 = _S493 / _S492;
                    int3 _S497 = int3(_S495, int(_S496), int(0));
                    uint slot_6 = _S491 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S497)).xy), uint(((_S497)).z))).x * 255.0f)) << 8U);
                    if(slot_6 == 0U)
                    {
                        int3 _S498 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S498)).xy), uint(((_S498)).z)));
                        break;
                    }
                    uint slot_7 = slot_6 - 1U;
                    uint _S499 = atlasWidth_3;
                    uint _S500 = 6U % atlasWidth_3;
                    int _S501 = int(_S500);
                    uint _S502 = 6U / _S499;
                    int3 _S503 = int3(_S501, int(_S502), int(0));
                    uint _S504 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S503)).xy), uint(((_S503)).z))).x * 255.0f));
                    uint _S505 = atlasWidth_3;
                    uint _S506 = 7U % atlasWidth_3;
                    int _S507 = int(_S506);
                    uint _S508 = 7U / _S505;
                    int3 _S509 = int3(_S507, int(_S508), int(0));
                    uint columns_3 = _S504 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S509)).xy), uint(((_S509)).z))).x * 255.0f)) << 8U);
                    uint _S510 = atlasWidth_3;
                    uint _S511 = 8U % atlasWidth_3;
                    int _S512 = int(_S511);
                    uint _S513 = 8U / _S510;
                    int3 _S514 = int3(_S512, int(_S513), int(0));
                    uint _S515 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S514)).xy), uint(((_S514)).z))).x * 255.0f));
                    uint _S516 = atlasWidth_3;
                    uint _S517 = 9U % atlasWidth_3;
                    int _S518 = int(_S517);
                    uint _S519 = 9U / _S516;
                    int3 _S520 = int3(_S518, int(_S519), int(0));
                    uint rows_3 = ((_S515 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S520)).xy), uint(((_S520)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                    uint metadataRows_3 = (10U + 2U * _S478 * _S484 + atlasWidth_3 - 1U) / atlasWidth_3;
                    uint _S521 = atlasWidth_3 / columns_3;
                    float _S522 = float(_S521);
                    uint _S523 = (atlasHeight_3 - metadataRows_3) / rows_3;
                    float2 cellSize_6 = float2(_S522, float(_S523));
                    uint _S524 = slot_7 % columns_3;
                    float _S525 = float(_S524);
                    uint _S526 = slot_7 / columns_3;
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S525, float(_S526)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(_S191) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                    break;
                }
                int2 _S527 = int2(metadata_3.zw);
                int2 tile_7 = int2(floor(_S191)) - int2(metadata_3.xy);
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
                    _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S528)).xy), uint(((_S528)).z)));
                    break;
                }
                uint _S529 = atlasWidth_3 / uint(_S527.x);
                float _S530 = float(_S529);
                uint _S531 = (atlasHeight_3 - 1U) / uint(_S527.y);
                float2 cellSize_7 = float2(_S530, float(_S531));
                _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            uint operation_1 = uint(round(_S455.y));
            if(operation_1 == 1U)
            {
                _S193 = _S193 * _S378;
                break;
            }
            if(operation_1 == 2U)
            {
                _S193 = _S193 + _S378;
                break;
            }
            if(operation_1 == 3U)
            {
                _S193 = _S193 - _S378;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S455.z;
                _S193 = _S193 * float4((1.0f - factor_1))  + _S378 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S193.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S167;
    }
    float occlusion_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool useSecondary_2 = (secondaryMask_0 & 32U) != 0U;
        if((alternateMask_0 & 32U) != 0U)
        {
            if(useSecondary_2)
            {
                _S191 = _S190;
            }
            else
            {
                _S191 = _S189;
            }
        }
        else
        {
            if(useSecondary_2)
            {
                _S191 = _S182;
            }
            else
            {
                _S191 = _S181;
            }
        }
        bool _S532 = (udimMask_0 & 32U) != 0U;
        uint maskU_2 = uint(round(_S175.y));
        uint maskV_2 = uint(round(_S175.z));
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
        uint _S533 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S532)
            {
                for(;;)
                {
                    if(_S533 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S534 = (&kernelContext_0)->metallicTexture_0;
                    thread uint width_6;
                    thread uint height_6;
                    (*((&width_6)) = (_S534).get_width(0)),(*((&height_6)) = (_S534).get_height(0));
                    float2 size_5 = float2(float(width_6), float(height_6));
                    float2 _S535 = float2(0.5f) ;
                    float2 remapped_5 = clamp((_S191 * (size_5 - float2(2.0f) ) + float2(1.0f) ) / size_5, _S535 / size_5, (size_5 - _S535) / size_5);
                    if((_S533 & 1U) != 0U)
                    {
                        metallic_0 = remapped_5.x;
                    }
                    else
                    {
                        metallic_0 = _S191.x;
                    }
                    if((_S533 & 2U) != 0U)
                    {
                        occlusion_0 = remapped_5.y;
                    }
                    else
                    {
                        occlusion_0 = _S191.y;
                    }
                    _S192 = float2(metallic_0, occlusion_0);
                    break;
                }
                _S193 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S192)));
                break;
            }
            texture2d<float, access::sample> _S536 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S536).get_width(0)),(*((&atlasHeight_4)) = (_S536).get_height(0));
            int3 _S537 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S537)).xy), uint(((_S537)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S538 = atlasWidth_4;
                uint _S539 = 2U % atlasWidth_4;
                int _S540 = int(_S539);
                uint _S541 = 2U / _S538;
                int3 _S542 = int3(_S540, int(_S541), int(0));
                int _S543 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S542)).xy), uint(((_S542)).z))).x * 255.0f)));
                uint _S544 = atlasWidth_4;
                uint _S545 = 3U % atlasWidth_4;
                int _S546 = int(_S545);
                uint _S547 = 3U / _S544;
                int3 _S548 = int3(_S546, int(_S547), int(0));
                int2 minimumTile_4 = int2(_S543, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S548)).xy), uint(((_S548)).z))).x * 255.0f))));
                uint _S549 = atlasWidth_4;
                uint _S550 = 4U % atlasWidth_4;
                int _S551 = int(_S550);
                uint _S552 = 4U / _S549;
                int3 _S553 = int3(_S551, int(_S552), int(0));
                uint _S554 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S553)).xy), uint(((_S553)).z))).x * 255.0f));
                uint _S555 = atlasWidth_4;
                uint _S556 = 5U % atlasWidth_4;
                int _S557 = int(_S556);
                uint _S558 = 5U / _S555;
                int3 _S559 = int3(_S557, int(_S558), int(0));
                uint _S560 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S559)).xy), uint(((_S559)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S554, _S560);
                int2 tile_8 = int2(floor(_S191)) - minimumTile_4;
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
                    int3 _S561 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S561)).xy), uint(((_S561)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S554 + uint(tile_8.x));
                uint _S562 = atlasWidth_4;
                uint _S563 = offset_4 % atlasWidth_4;
                int _S564 = int(_S563);
                uint _S565 = offset_4 / _S562;
                int3 _S566 = int3(_S564, int(_S565), int(0));
                uint _S567 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S566)).xy), uint(((_S566)).z))).x * 255.0f));
                uint _S568 = atlasWidth_4;
                uint _S569 = offset_4 + 1U;
                uint _S570 = _S569 % atlasWidth_4;
                int _S571 = int(_S570);
                uint _S572 = _S569 / _S568;
                int3 _S573 = int3(_S571, int(_S572), int(0));
                uint slot_8 = _S567 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S573)).xy), uint(((_S573)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S574 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S574)).xy), uint(((_S574)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S575 = atlasWidth_4;
                uint _S576 = 6U % atlasWidth_4;
                int _S577 = int(_S576);
                uint _S578 = 6U / _S575;
                int3 _S579 = int3(_S577, int(_S578), int(0));
                uint _S580 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S579)).xy), uint(((_S579)).z))).x * 255.0f));
                uint _S581 = atlasWidth_4;
                uint _S582 = 7U % atlasWidth_4;
                int _S583 = int(_S582);
                uint _S584 = 7U / _S581;
                int3 _S585 = int3(_S583, int(_S584), int(0));
                uint columns_4 = _S580 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S585)).xy), uint(((_S585)).z))).x * 255.0f)) << 8U);
                uint _S586 = atlasWidth_4;
                uint _S587 = 8U % atlasWidth_4;
                int _S588 = int(_S587);
                uint _S589 = 8U / _S586;
                int3 _S590 = int3(_S588, int(_S589), int(0));
                uint _S591 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S590)).xy), uint(((_S590)).z))).x * 255.0f));
                uint _S592 = atlasWidth_4;
                uint _S593 = 9U % atlasWidth_4;
                int _S594 = int(_S593);
                uint _S595 = 9U / _S592;
                int3 _S596 = int3(_S594, int(_S595), int(0));
                uint rows_4 = ((_S591 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S596)).xy), uint(((_S596)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S554 * _S560 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S597 = atlasWidth_4 / columns_4;
                float _S598 = float(_S597);
                uint _S599 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S598, float(_S599));
                uint _S600 = slot_9 % columns_4;
                float _S601 = float(_S600);
                uint _S602 = slot_9 / columns_4;
                _S193 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S601, float(_S602)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(_S191) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S603 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(_S191)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S603);
            }
            if(hasSceneLighting_0)
            {
                int3 _S604 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S193 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S604)).xy), uint(((_S604)).z)));
                break;
            }
            uint _S605 = atlasWidth_4 / uint(_S603.x);
            float _S606 = float(_S605);
            uint _S607 = (atlasHeight_4 - 1U) / uint(_S603.y);
            float2 cellSize_9 = float2(_S606, float(_S607));
            _S193 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S608 = float4(_S6->compositeControls_0) ;
            if((_S608.x) != 32.0f)
            {
                break;
            }
            bool _S609 = (_S608.w) >= 0.5f;
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
            uint _S610 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S609)
                {
                    for(;;)
                    {
                        if(_S610 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S611 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_7;
                        thread uint height_7;
                        (*((&width_7)) = (_S611).get_width(0)),(*((&height_7)) = (_S611).get_height(0));
                        float2 size_6 = float2(float(width_7), float(height_7));
                        float2 _S612 = float2(0.5f) ;
                        float2 remapped_6 = clamp((_S191 * (size_6 - float2(2.0f) ) + float2(1.0f) ) / size_6, _S612 / size_6, (size_6 - _S612) / size_6);
                        if((_S610 & 1U) != 0U)
                        {
                            metallic_0 = remapped_6.x;
                        }
                        else
                        {
                            metallic_0 = _S191.x;
                        }
                        if((_S610 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_6.y;
                        }
                        else
                        {
                            occlusion_0 = _S191.y;
                        }
                        _S191 = float2(metallic_0, occlusion_0);
                        break;
                    }
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S191)));
                    break;
                }
                texture2d<float, access::sample> _S613 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S613).get_width(0)),(*((&atlasHeight_5)) = (_S613).get_height(0));
                int3 _S614 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S614)).xy), uint(((_S614)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S615 = atlasWidth_5;
                    uint _S616 = 2U % atlasWidth_5;
                    int _S617 = int(_S616);
                    uint _S618 = 2U / _S615;
                    int3 _S619 = int3(_S617, int(_S618), int(0));
                    int _S620 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S619)).xy), uint(((_S619)).z))).x * 255.0f)));
                    uint _S621 = atlasWidth_5;
                    uint _S622 = 3U % atlasWidth_5;
                    int _S623 = int(_S622);
                    uint _S624 = 3U / _S621;
                    int3 _S625 = int3(_S623, int(_S624), int(0));
                    int2 minimumTile_5 = int2(_S620, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S625)).xy), uint(((_S625)).z))).x * 255.0f))));
                    uint _S626 = atlasWidth_5;
                    uint _S627 = 4U % atlasWidth_5;
                    int _S628 = int(_S627);
                    uint _S629 = 4U / _S626;
                    int3 _S630 = int3(_S628, int(_S629), int(0));
                    uint _S631 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S630)).xy), uint(((_S630)).z))).x * 255.0f));
                    uint _S632 = atlasWidth_5;
                    uint _S633 = 5U % atlasWidth_5;
                    int _S634 = int(_S633);
                    uint _S635 = 5U / _S632;
                    int3 _S636 = int3(_S634, int(_S635), int(0));
                    uint _S637 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S636)).xy), uint(((_S636)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S631, _S637);
                    int2 tile_10 = int2(floor(_S191)) - minimumTile_5;
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
                        int3 _S638 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S638)).xy), uint(((_S638)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S631 + uint(tile_10.x));
                    uint _S639 = atlasWidth_5;
                    uint _S640 = offset_5 % atlasWidth_5;
                    int _S641 = int(_S640);
                    uint _S642 = offset_5 / _S639;
                    int3 _S643 = int3(_S641, int(_S642), int(0));
                    uint _S644 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S643)).xy), uint(((_S643)).z))).x * 255.0f));
                    uint _S645 = atlasWidth_5;
                    uint _S646 = offset_5 + 1U;
                    uint _S647 = _S646 % atlasWidth_5;
                    int _S648 = int(_S647);
                    uint _S649 = _S646 / _S645;
                    int3 _S650 = int3(_S648, int(_S649), int(0));
                    uint slot_10 = _S644 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S650)).xy), uint(((_S650)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S651 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S651)).xy), uint(((_S651)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S652 = atlasWidth_5;
                    uint _S653 = 6U % atlasWidth_5;
                    int _S654 = int(_S653);
                    uint _S655 = 6U / _S652;
                    int3 _S656 = int3(_S654, int(_S655), int(0));
                    uint _S657 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S656)).xy), uint(((_S656)).z))).x * 255.0f));
                    uint _S658 = atlasWidth_5;
                    uint _S659 = 7U % atlasWidth_5;
                    int _S660 = int(_S659);
                    uint _S661 = 7U / _S658;
                    int3 _S662 = int3(_S660, int(_S661), int(0));
                    uint columns_5 = _S657 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S662)).xy), uint(((_S662)).z))).x * 255.0f)) << 8U);
                    uint _S663 = atlasWidth_5;
                    uint _S664 = 8U % atlasWidth_5;
                    int _S665 = int(_S664);
                    uint _S666 = 8U / _S663;
                    int3 _S667 = int3(_S665, int(_S666), int(0));
                    uint _S668 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S667)).xy), uint(((_S667)).z))).x * 255.0f));
                    uint _S669 = atlasWidth_5;
                    uint _S670 = 9U % atlasWidth_5;
                    int _S671 = int(_S670);
                    uint _S672 = 9U / _S669;
                    int3 _S673 = int3(_S671, int(_S672), int(0));
                    uint rows_5 = ((_S668 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S673)).xy), uint(((_S673)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S631 * _S637 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S674 = atlasWidth_5 / columns_5;
                    float _S675 = float(_S674);
                    uint _S676 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S675, float(_S676));
                    uint _S677 = slot_11 % columns_5;
                    float _S678 = float(_S677);
                    uint _S679 = slot_11 / columns_5;
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S678, float(_S679)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(_S191) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S680 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(_S191)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S680);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S681 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S681)).xy), uint(((_S681)).z)));
                    break;
                }
                uint _S682 = atlasWidth_5 / uint(_S680.x);
                float _S683 = float(_S682);
                uint _S684 = (atlasHeight_5 - 1U) / uint(_S680.y);
                float2 cellSize_11 = float2(_S683, float(_S684));
                _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_2 = uint(round(_S608.y));
            if(operation_2 == 1U)
            {
                _S193 = _S193 * _S378;
                break;
            }
            if(operation_2 == 2U)
            {
                _S193 = _S193 + _S378;
                break;
            }
            if(operation_2 == 3U)
            {
                _S193 = _S193 - _S378;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S608.z;
                _S193 = _S193 * float4((1.0f - factor_2))  + _S378 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S193.x);
    }
    else
    {
        metallic_0 = _S166;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 16U) != 0U)
    {
        bool useSecondary_3 = (secondaryMask_0 & 16U) != 0U;
        if((alternateMask_0 & 16U) != 0U)
        {
            if(useSecondary_3)
            {
                _S191 = _S190;
            }
            else
            {
                _S191 = _S189;
            }
        }
        else
        {
            if(useSecondary_3)
            {
                _S191 = _S182;
            }
            else
            {
                _S191 = _S181;
            }
        }
        thread float4 emissionSample_0;
        bool _S685 = (udimMask_0 & 16U) != 0U;
        uint maskU_3 = uint(round(_S175.y));
        uint maskV_3 = uint(round(_S175.z));
        bool _S686 = (maskU_3 & 16U) != 0U;
        if(_S686)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S687 = (maskV_3 & 16U) != 0U;
        if(_S687)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S688 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S685)
            {
                for(;;)
                {
                    if(_S688 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S689 = (&kernelContext_0)->emissiveTexture_0;
                    thread uint width_8;
                    thread uint height_8;
                    (*((&width_8)) = (_S689).get_width(0)),(*((&height_8)) = (_S689).get_height(0));
                    float2 size_7 = float2(float(width_8), float(height_8));
                    float2 _S690 = float2(0.5f) ;
                    float2 remapped_7 = clamp((_S191 * (size_7 - float2(2.0f) ) + float2(1.0f) ) / size_7, _S690 / size_7, (size_7 - _S690) / size_7);
                    if((_S688 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_7.x;
                    }
                    else
                    {
                        occlusion_0 = _S191.x;
                    }
                    if((_S688 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_7.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S191.y;
                    }
                    _S192 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S193 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S192)));
                break;
            }
            texture2d<float, access::sample> _S691 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S691).get_width(0)),(*((&atlasHeight_6)) = (_S691).get_height(0));
            int3 _S692 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S692)).xy), uint(((_S692)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S693 = atlasWidth_6;
                uint _S694 = 2U % atlasWidth_6;
                int _S695 = int(_S694);
                uint _S696 = 2U / _S693;
                int3 _S697 = int3(_S695, int(_S696), int(0));
                int _S698 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S697)).xy), uint(((_S697)).z))).x * 255.0f)));
                uint _S699 = atlasWidth_6;
                uint _S700 = 3U % atlasWidth_6;
                int _S701 = int(_S700);
                uint _S702 = 3U / _S699;
                int3 _S703 = int3(_S701, int(_S702), int(0));
                int2 minimumTile_6 = int2(_S698, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S703)).xy), uint(((_S703)).z))).x * 255.0f))));
                uint _S704 = atlasWidth_6;
                uint _S705 = 4U % atlasWidth_6;
                int _S706 = int(_S705);
                uint _S707 = 4U / _S704;
                int3 _S708 = int3(_S706, int(_S707), int(0));
                uint _S709 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S708)).xy), uint(((_S708)).z))).x * 255.0f));
                uint _S710 = atlasWidth_6;
                uint _S711 = 5U % atlasWidth_6;
                int _S712 = int(_S711);
                uint _S713 = 5U / _S710;
                int3 _S714 = int3(_S712, int(_S713), int(0));
                uint _S715 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S714)).xy), uint(((_S714)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S709, _S715);
                int2 tile_12 = int2(floor(_S191)) - minimumTile_6;
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
                    int3 _S716 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S716)).xy), uint(((_S716)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S709 + uint(tile_12.x));
                uint _S717 = atlasWidth_6;
                uint _S718 = offset_6 % atlasWidth_6;
                int _S719 = int(_S718);
                uint _S720 = offset_6 / _S717;
                int3 _S721 = int3(_S719, int(_S720), int(0));
                uint _S722 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S721)).xy), uint(((_S721)).z))).x * 255.0f));
                uint _S723 = atlasWidth_6;
                uint _S724 = offset_6 + 1U;
                uint _S725 = _S724 % atlasWidth_6;
                int _S726 = int(_S725);
                uint _S727 = _S724 / _S723;
                int3 _S728 = int3(_S726, int(_S727), int(0));
                uint slot_12 = _S722 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S728)).xy), uint(((_S728)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S729 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S729)).xy), uint(((_S729)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S730 = atlasWidth_6;
                uint _S731 = 6U % atlasWidth_6;
                int _S732 = int(_S731);
                uint _S733 = 6U / _S730;
                int3 _S734 = int3(_S732, int(_S733), int(0));
                uint _S735 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S734)).xy), uint(((_S734)).z))).x * 255.0f));
                uint _S736 = atlasWidth_6;
                uint _S737 = 7U % atlasWidth_6;
                int _S738 = int(_S737);
                uint _S739 = 7U / _S736;
                int3 _S740 = int3(_S738, int(_S739), int(0));
                uint columns_6 = _S735 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S740)).xy), uint(((_S740)).z))).x * 255.0f)) << 8U);
                uint _S741 = atlasWidth_6;
                uint _S742 = 8U % atlasWidth_6;
                int _S743 = int(_S742);
                uint _S744 = 8U / _S741;
                int3 _S745 = int3(_S743, int(_S744), int(0));
                uint _S746 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S745)).xy), uint(((_S745)).z))).x * 255.0f));
                uint _S747 = atlasWidth_6;
                uint _S748 = 9U % atlasWidth_6;
                int _S749 = int(_S748);
                uint _S750 = 9U / _S747;
                int3 _S751 = int3(_S749, int(_S750), int(0));
                uint rows_6 = ((_S746 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S751)).xy), uint(((_S751)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S709 * _S715 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S752 = atlasWidth_6 / columns_6;
                float _S753 = float(_S752);
                uint _S754 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S753, float(_S754));
                uint _S755 = slot_13 % columns_6;
                float _S756 = float(_S755);
                uint _S757 = slot_13 / columns_6;
                _S193 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S756, float(_S757)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(_S191) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S758 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(_S191)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S758);
            }
            if(hasSceneLighting_0)
            {
                int3 _S759 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S193 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S759)).xy), uint(((_S759)).z)));
                break;
            }
            uint _S760 = atlasWidth_6 / uint(_S758.x);
            float _S761 = float(_S760);
            uint _S762 = (atlasHeight_6 - 1U) / uint(_S758.y);
            float2 cellSize_13 = float2(_S761, float(_S762));
            _S193 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        emissionSample_0 = _S193;
        if((_S176.w) != 0.0f)
        {
            texture2d<float, access::sample> _S763 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S763).get_width(0)),(*((&sourceHeight_1)) = (_S763).get_height(0));
            texture2d<float, access::sample> _S764 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S764).get_width(0)),(*((&valueHeight_1)) = (_S764).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S765 = emissionSample_0;
                for(;;)
                {
                    float _S766 = _S765.w;
                    if(_S766 > 1.0f)
                    {
                        unlitColor_0 = _S765.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S767 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_9;
                    thread uint height_9;
                    (*((&width_9)) = (_S767).get_width(0)),(*((&height_9)) = (_S767).get_height(0));
                    uint3 _S768 = uint3(round(_S765.xyz * float3(255.0f) ));
                    uint index_2 = ((_S768.x) | ((_S768.y) << 8U)) | ((_S768.z) << 16U);
                    if(_S766 == 0.0f)
                    {
                        _S194 = _S768;
                    }
                    else
                    {
                        _S194 = uint3(index_2, index_2, index_2);
                    }
                    uint3 address_1 = _S194 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S769 = address_1.x;
                    uint _S770 = _S769 % width_9;
                    int _S771 = int(_S770);
                    uint _S772 = _S769 / width_9;
                    int3 _S773 = int3(_S771, int(_S772), int(0));
                    float _S774 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S773)).xy), uint(((_S773)).z))).x;
                    uint _S775 = address_1.y;
                    uint _S776 = _S775 % width_9;
                    int _S777 = int(_S776);
                    uint _S778 = _S775 / width_9;
                    int3 _S779 = int3(_S777, int(_S778), int(0));
                    float _S780 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S779)).xy), uint(((_S779)).z))).x;
                    uint _S781 = address_1.z;
                    uint _S782 = _S781 % width_9;
                    int _S783 = int(_S782);
                    uint _S784 = _S781 / width_9;
                    int3 _S785 = int3(_S783, int(_S784), int(0));
                    unlitColor_0 = float3(_S774, _S780, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S785)).xy), uint(((_S785)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                if(_S686)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S687)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S786 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S786 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S787 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_10;
                    thread uint height_10;
                    (*((&width_10)) = (_S787).get_width(0)),(*((&height_10)) = (_S787).get_height(0));
                    float2 size_8 = float2(float(width_10), float(height_10));
                    float2 _S788 = float2(0.5f) ;
                    float2 remapped_8 = clamp((_S191 * (size_8 - float2(2.0f) ) + float2(1.0f) ) / size_8, _S788 / size_8, (size_8 - _S788) / size_8);
                    if((_S786 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_8.x;
                    }
                    else
                    {
                        occlusion_0 = _S191.x;
                    }
                    if((_S786 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_8.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S191.y;
                    }
                    _S192 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                emissionSample_0.z = (((&kernelContext_0)->emissiveValuesTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S192))).x;
            }
        }
        float4 _S789 = emissionSample_0;
        for(;;)
        {
            float4 _S790 = float4(_S6->compositeControls_0) ;
            if((_S790.x) != 16.0f)
            {
                _S193 = _S789;
                break;
            }
            bool _S791 = (_S790.w) >= 0.5f;
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
            uint _S792 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S791)
                {
                    for(;;)
                    {
                        if(_S792 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S793 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_11;
                        thread uint height_11;
                        (*((&width_11)) = (_S793).get_width(0)),(*((&height_11)) = (_S793).get_height(0));
                        float2 size_9 = float2(float(width_11), float(height_11));
                        float2 _S794 = float2(0.5f) ;
                        float2 remapped_9 = clamp((_S191 * (size_9 - float2(2.0f) ) + float2(1.0f) ) / size_9, _S794 / size_9, (size_9 - _S794) / size_9);
                        if((_S792 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_9.x;
                        }
                        else
                        {
                            occlusion_0 = _S191.x;
                        }
                        if((_S792 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_9.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S191.y;
                        }
                        _S191 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S193 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S191)));
                    break;
                }
                texture2d<float, access::sample> _S795 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S795).get_width(0)),(*((&atlasHeight_7)) = (_S795).get_height(0));
                int3 _S796 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S796)).xy), uint(((_S796)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S797 = atlasWidth_7;
                    uint _S798 = 2U % atlasWidth_7;
                    int _S799 = int(_S798);
                    uint _S800 = 2U / _S797;
                    int3 _S801 = int3(_S799, int(_S800), int(0));
                    int _S802 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S801)).xy), uint(((_S801)).z))).x * 255.0f)));
                    uint _S803 = atlasWidth_7;
                    uint _S804 = 3U % atlasWidth_7;
                    int _S805 = int(_S804);
                    uint _S806 = 3U / _S803;
                    int3 _S807 = int3(_S805, int(_S806), int(0));
                    int2 minimumTile_7 = int2(_S802, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S807)).xy), uint(((_S807)).z))).x * 255.0f))));
                    uint _S808 = atlasWidth_7;
                    uint _S809 = 4U % atlasWidth_7;
                    int _S810 = int(_S809);
                    uint _S811 = 4U / _S808;
                    int3 _S812 = int3(_S810, int(_S811), int(0));
                    uint _S813 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S812)).xy), uint(((_S812)).z))).x * 255.0f));
                    uint _S814 = atlasWidth_7;
                    uint _S815 = 5U % atlasWidth_7;
                    int _S816 = int(_S815);
                    uint _S817 = 5U / _S814;
                    int3 _S818 = int3(_S816, int(_S817), int(0));
                    uint _S819 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S818)).xy), uint(((_S818)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S813, _S819);
                    int2 tile_14 = int2(floor(_S191)) - minimumTile_7;
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
                        int3 _S820 = int3(int(1), int(0), int(0));
                        _S193 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S820)).xy), uint(((_S820)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S813 + uint(tile_14.x));
                    uint _S821 = atlasWidth_7;
                    uint _S822 = offset_7 % atlasWidth_7;
                    int _S823 = int(_S822);
                    uint _S824 = offset_7 / _S821;
                    int3 _S825 = int3(_S823, int(_S824), int(0));
                    uint _S826 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S825)).xy), uint(((_S825)).z))).x * 255.0f));
                    uint _S827 = atlasWidth_7;
                    uint _S828 = offset_7 + 1U;
                    uint _S829 = _S828 % atlasWidth_7;
                    int _S830 = int(_S829);
                    uint _S831 = _S828 / _S827;
                    int3 _S832 = int3(_S830, int(_S831), int(0));
                    uint slot_14 = _S826 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S832)).xy), uint(((_S832)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S833 = int3(int(1), int(0), int(0));
                        _S193 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S833)).xy), uint(((_S833)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S834 = atlasWidth_7;
                    uint _S835 = 6U % atlasWidth_7;
                    int _S836 = int(_S835);
                    uint _S837 = 6U / _S834;
                    int3 _S838 = int3(_S836, int(_S837), int(0));
                    uint _S839 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S838)).xy), uint(((_S838)).z))).x * 255.0f));
                    uint _S840 = atlasWidth_7;
                    uint _S841 = 7U % atlasWidth_7;
                    int _S842 = int(_S841);
                    uint _S843 = 7U / _S840;
                    int3 _S844 = int3(_S842, int(_S843), int(0));
                    uint columns_7 = _S839 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S844)).xy), uint(((_S844)).z))).x * 255.0f)) << 8U);
                    uint _S845 = atlasWidth_7;
                    uint _S846 = 8U % atlasWidth_7;
                    int _S847 = int(_S846);
                    uint _S848 = 8U / _S845;
                    int3 _S849 = int3(_S847, int(_S848), int(0));
                    uint _S850 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S849)).xy), uint(((_S849)).z))).x * 255.0f));
                    uint _S851 = atlasWidth_7;
                    uint _S852 = 9U % atlasWidth_7;
                    int _S853 = int(_S852);
                    uint _S854 = 9U / _S851;
                    int3 _S855 = int3(_S853, int(_S854), int(0));
                    uint rows_7 = ((_S850 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S855)).xy), uint(((_S855)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S813 * _S819 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S856 = atlasWidth_7 / columns_7;
                    float _S857 = float(_S856);
                    uint _S858 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S857, float(_S858));
                    uint _S859 = slot_15 % columns_7;
                    float _S860 = float(_S859);
                    uint _S861 = slot_15 / columns_7;
                    _S193 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S860, float(_S861)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(_S191) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S862 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(_S191)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S862);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S863 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S193 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S863)).xy), uint(((_S863)).z)));
                    break;
                }
                uint _S864 = atlasWidth_7 / uint(_S862.x);
                float _S865 = float(_S864);
                uint _S866 = (atlasHeight_7 - 1U) / uint(_S862.y);
                float2 cellSize_15 = float2(_S865, float(_S866));
                _S193 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_3 = uint(round(_S790.y));
            if(operation_3 == 1U)
            {
                _S193 = _S789 * _S193;
                break;
            }
            if(operation_3 == 2U)
            {
                _S193 = _S789 + _S193;
                break;
            }
            if(operation_3 == 3U)
            {
                _S193 = _S789 - _S193;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S790.z;
                _S193 = _S789 * float4((1.0f - factor_3))  + _S193 * float4(factor_3) ;
                break;
            }
            _S193 = _S789;
            break;
        }
        unlitColor_0 = _S193.xyz;
    }
    else
    {
        unlitColor_0 = emissiveColor_0;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool useSecondary_4 = (secondaryMask_0 & 64U) != 0U;
        if((alternateMask_0 & 64U) != 0U)
        {
            if(useSecondary_4)
            {
                _S191 = _S190;
            }
            else
            {
                _S191 = _S189;
            }
        }
        else
        {
            if(useSecondary_4)
            {
                _S191 = _S182;
            }
            else
            {
                _S191 = _S181;
            }
        }
        bool _S867 = (udimMask_0 & 64U) != 0U;
        uint maskU_4 = uint(round(_S175.y));
        uint maskV_4 = uint(round(_S175.z));
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
        uint _S868 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S867)
            {
                for(;;)
                {
                    if(_S868 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S869 = (&kernelContext_0)->opacityTexture_0;
                    thread uint width_12;
                    thread uint height_12;
                    (*((&width_12)) = (_S869).get_width(0)),(*((&height_12)) = (_S869).get_height(0));
                    float2 size_10 = float2(float(width_12), float(height_12));
                    float2 _S870 = float2(0.5f) ;
                    float2 remapped_10 = clamp((_S191 * (size_10 - float2(2.0f) ) + float2(1.0f) ) / size_10, _S870 / size_10, (size_10 - _S870) / size_10);
                    if((_S868 & 1U) != 0U)
                    {
                        opacity_0 = remapped_10.x;
                    }
                    else
                    {
                        opacity_0 = _S191.x;
                    }
                    if((_S868 & 2U) != 0U)
                    {
                        occlusion_0 = remapped_10.y;
                    }
                    else
                    {
                        occlusion_0 = _S191.y;
                    }
                    _S192 = float2(opacity_0, occlusion_0);
                    break;
                }
                _S193 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S192)));
                break;
            }
            texture2d<float, access::sample> _S871 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S871).get_width(0)),(*((&atlasHeight_8)) = (_S871).get_height(0));
            int3 _S872 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S872)).xy), uint(((_S872)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S873 = atlasWidth_8;
                uint _S874 = 2U % atlasWidth_8;
                int _S875 = int(_S874);
                uint _S876 = 2U / _S873;
                int3 _S877 = int3(_S875, int(_S876), int(0));
                int _S878 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S877)).xy), uint(((_S877)).z))).x * 255.0f)));
                uint _S879 = atlasWidth_8;
                uint _S880 = 3U % atlasWidth_8;
                int _S881 = int(_S880);
                uint _S882 = 3U / _S879;
                int3 _S883 = int3(_S881, int(_S882), int(0));
                int2 minimumTile_8 = int2(_S878, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S883)).xy), uint(((_S883)).z))).x * 255.0f))));
                uint _S884 = atlasWidth_8;
                uint _S885 = 4U % atlasWidth_8;
                int _S886 = int(_S885);
                uint _S887 = 4U / _S884;
                int3 _S888 = int3(_S886, int(_S887), int(0));
                uint _S889 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S888)).xy), uint(((_S888)).z))).x * 255.0f));
                uint _S890 = atlasWidth_8;
                uint _S891 = 5U % atlasWidth_8;
                int _S892 = int(_S891);
                uint _S893 = 5U / _S890;
                int3 _S894 = int3(_S892, int(_S893), int(0));
                uint _S895 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S894)).xy), uint(((_S894)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S889, _S895);
                int2 tile_16 = int2(floor(_S191)) - minimumTile_8;
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
                    int3 _S896 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S896)).xy), uint(((_S896)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S889 + uint(tile_16.x));
                uint _S897 = atlasWidth_8;
                uint _S898 = offset_8 % atlasWidth_8;
                int _S899 = int(_S898);
                uint _S900 = offset_8 / _S897;
                int3 _S901 = int3(_S899, int(_S900), int(0));
                uint _S902 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S901)).xy), uint(((_S901)).z))).x * 255.0f));
                uint _S903 = atlasWidth_8;
                uint _S904 = offset_8 + 1U;
                uint _S905 = _S904 % atlasWidth_8;
                int _S906 = int(_S905);
                uint _S907 = _S904 / _S903;
                int3 _S908 = int3(_S906, int(_S907), int(0));
                uint slot_16 = _S902 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S908)).xy), uint(((_S908)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S909 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S909)).xy), uint(((_S909)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S910 = atlasWidth_8;
                uint _S911 = 6U % atlasWidth_8;
                int _S912 = int(_S911);
                uint _S913 = 6U / _S910;
                int3 _S914 = int3(_S912, int(_S913), int(0));
                uint _S915 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S914)).xy), uint(((_S914)).z))).x * 255.0f));
                uint _S916 = atlasWidth_8;
                uint _S917 = 7U % atlasWidth_8;
                int _S918 = int(_S917);
                uint _S919 = 7U / _S916;
                int3 _S920 = int3(_S918, int(_S919), int(0));
                uint columns_8 = _S915 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S920)).xy), uint(((_S920)).z))).x * 255.0f)) << 8U);
                uint _S921 = atlasWidth_8;
                uint _S922 = 8U % atlasWidth_8;
                int _S923 = int(_S922);
                uint _S924 = 8U / _S921;
                int3 _S925 = int3(_S923, int(_S924), int(0));
                uint _S926 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S925)).xy), uint(((_S925)).z))).x * 255.0f));
                uint _S927 = atlasWidth_8;
                uint _S928 = 9U % atlasWidth_8;
                int _S929 = int(_S928);
                uint _S930 = 9U / _S927;
                int3 _S931 = int3(_S929, int(_S930), int(0));
                uint rows_8 = ((_S926 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S931)).xy), uint(((_S931)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S889 * _S895 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S932 = atlasWidth_8 / columns_8;
                float _S933 = float(_S932);
                uint _S934 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S933, float(_S934));
                uint _S935 = slot_17 % columns_8;
                float _S936 = float(_S935);
                uint _S937 = slot_17 / columns_8;
                _S193 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S936, float(_S937)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(_S191) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S938 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(_S191)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S938);
            }
            if(hasSceneLighting_0)
            {
                int3 _S939 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S193 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S939)).xy), uint(((_S939)).z)));
                break;
            }
            uint _S940 = atlasWidth_8 / uint(_S938.x);
            float _S941 = float(_S940);
            uint _S942 = (atlasHeight_8 - 1U) / uint(_S938.y);
            float2 cellSize_17 = float2(_S941, float(_S942));
            _S193 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        for(;;)
        {
            float4 _S943 = float4(_S6->compositeControls_0) ;
            if((_S943.x) != 64.0f)
            {
                break;
            }
            bool _S944 = (_S943.w) >= 0.5f;
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
            uint _S945 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S944)
                {
                    for(;;)
                    {
                        if(_S945 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S946 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_13;
                        thread uint height_13;
                        (*((&width_13)) = (_S946).get_width(0)),(*((&height_13)) = (_S946).get_height(0));
                        float2 size_11 = float2(float(width_13), float(height_13));
                        float2 _S947 = float2(0.5f) ;
                        float2 remapped_11 = clamp((_S191 * (size_11 - float2(2.0f) ) + float2(1.0f) ) / size_11, _S947 / size_11, (size_11 - _S947) / size_11);
                        if((_S945 & 1U) != 0U)
                        {
                            opacity_0 = remapped_11.x;
                        }
                        else
                        {
                            opacity_0 = _S191.x;
                        }
                        if((_S945 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_11.y;
                        }
                        else
                        {
                            occlusion_0 = _S191.y;
                        }
                        _S191 = float2(opacity_0, occlusion_0);
                        break;
                    }
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S191)));
                    break;
                }
                texture2d<float, access::sample> _S948 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S948).get_width(0)),(*((&atlasHeight_9)) = (_S948).get_height(0));
                int3 _S949 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S949)).xy), uint(((_S949)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S950 = atlasWidth_9;
                    uint _S951 = 2U % atlasWidth_9;
                    int _S952 = int(_S951);
                    uint _S953 = 2U / _S950;
                    int3 _S954 = int3(_S952, int(_S953), int(0));
                    int _S955 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S954)).xy), uint(((_S954)).z))).x * 255.0f)));
                    uint _S956 = atlasWidth_9;
                    uint _S957 = 3U % atlasWidth_9;
                    int _S958 = int(_S957);
                    uint _S959 = 3U / _S956;
                    int3 _S960 = int3(_S958, int(_S959), int(0));
                    int2 minimumTile_9 = int2(_S955, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S960)).xy), uint(((_S960)).z))).x * 255.0f))));
                    uint _S961 = atlasWidth_9;
                    uint _S962 = 4U % atlasWidth_9;
                    int _S963 = int(_S962);
                    uint _S964 = 4U / _S961;
                    int3 _S965 = int3(_S963, int(_S964), int(0));
                    uint _S966 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S965)).xy), uint(((_S965)).z))).x * 255.0f));
                    uint _S967 = atlasWidth_9;
                    uint _S968 = 5U % atlasWidth_9;
                    int _S969 = int(_S968);
                    uint _S970 = 5U / _S967;
                    int3 _S971 = int3(_S969, int(_S970), int(0));
                    uint _S972 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S971)).xy), uint(((_S971)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S966, _S972);
                    int2 tile_18 = int2(floor(_S191)) - minimumTile_9;
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
                        int3 _S973 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S973)).xy), uint(((_S973)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S966 + uint(tile_18.x));
                    uint _S974 = atlasWidth_9;
                    uint _S975 = offset_9 % atlasWidth_9;
                    int _S976 = int(_S975);
                    uint _S977 = offset_9 / _S974;
                    int3 _S978 = int3(_S976, int(_S977), int(0));
                    uint _S979 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S978)).xy), uint(((_S978)).z))).x * 255.0f));
                    uint _S980 = atlasWidth_9;
                    uint _S981 = offset_9 + 1U;
                    uint _S982 = _S981 % atlasWidth_9;
                    int _S983 = int(_S982);
                    uint _S984 = _S981 / _S980;
                    int3 _S985 = int3(_S983, int(_S984), int(0));
                    uint slot_18 = _S979 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S985)).xy), uint(((_S985)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S986 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S986)).xy), uint(((_S986)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S987 = atlasWidth_9;
                    uint _S988 = 6U % atlasWidth_9;
                    int _S989 = int(_S988);
                    uint _S990 = 6U / _S987;
                    int3 _S991 = int3(_S989, int(_S990), int(0));
                    uint _S992 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S991)).xy), uint(((_S991)).z))).x * 255.0f));
                    uint _S993 = atlasWidth_9;
                    uint _S994 = 7U % atlasWidth_9;
                    int _S995 = int(_S994);
                    uint _S996 = 7U / _S993;
                    int3 _S997 = int3(_S995, int(_S996), int(0));
                    uint columns_9 = _S992 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S997)).xy), uint(((_S997)).z))).x * 255.0f)) << 8U);
                    uint _S998 = atlasWidth_9;
                    uint _S999 = 8U % atlasWidth_9;
                    int _S1000 = int(_S999);
                    uint _S1001 = 8U / _S998;
                    int3 _S1002 = int3(_S1000, int(_S1001), int(0));
                    uint _S1003 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1002)).xy), uint(((_S1002)).z))).x * 255.0f));
                    uint _S1004 = atlasWidth_9;
                    uint _S1005 = 9U % atlasWidth_9;
                    int _S1006 = int(_S1005);
                    uint _S1007 = 9U / _S1004;
                    int3 _S1008 = int3(_S1006, int(_S1007), int(0));
                    uint rows_9 = ((_S1003 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1008)).xy), uint(((_S1008)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S966 * _S972 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S1009 = atlasWidth_9 / columns_9;
                    float _S1010 = float(_S1009);
                    uint _S1011 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S1010, float(_S1011));
                    uint _S1012 = slot_19 % columns_9;
                    float _S1013 = float(_S1012);
                    uint _S1014 = slot_19 / columns_9;
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1013, float(_S1014)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(_S191) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S1015 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(_S191)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S1015);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1016 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1016)).xy), uint(((_S1016)).z)));
                    break;
                }
                uint _S1017 = atlasWidth_9 / uint(_S1015.x);
                float _S1018 = float(_S1017);
                uint _S1019 = (atlasHeight_9 - 1U) / uint(_S1015.y);
                float2 cellSize_19 = float2(_S1018, float(_S1019));
                _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_4 = uint(round(_S943.y));
            if(operation_4 == 1U)
            {
                _S193 = _S193 * _S378;
                break;
            }
            if(operation_4 == 2U)
            {
                _S193 = _S193 + _S378;
                break;
            }
            if(operation_4 == 3U)
            {
                _S193 = _S193 - _S378;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S943.z;
                _S193 = _S193 * float4((1.0f - factor_4))  + _S378 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S193.x);
    }
    if((textureMask_0 & 128U) != 0U)
    {
        bool useSecondary_5 = (secondaryMask_0 & 128U) != 0U;
        if((alternateMask_0 & 128U) != 0U)
        {
            if(useSecondary_5)
            {
                _S191 = _S190;
            }
            else
            {
                _S191 = _S189;
            }
        }
        else
        {
            if(useSecondary_5)
            {
                _S191 = _S182;
            }
            else
            {
                _S191 = _S181;
            }
        }
        bool _S1020 = (udimMask_0 & 128U) != 0U;
        uint maskU_5 = uint(round(_S175.y));
        uint maskV_5 = uint(round(_S175.z));
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
        uint _S1021 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1020)
            {
                for(;;)
                {
                    if(_S1021 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S1022 = (&kernelContext_0)->occlusionTexture_0;
                    thread uint width_14;
                    thread uint height_14;
                    (*((&width_14)) = (_S1022).get_width(0)),(*((&height_14)) = (_S1022).get_height(0));
                    float2 size_12 = float2(float(width_14), float(height_14));
                    float2 _S1023 = float2(0.5f) ;
                    float2 remapped_12 = clamp((_S191 * (size_12 - float2(2.0f) ) + float2(1.0f) ) / size_12, _S1023 / size_12, (size_12 - _S1023) / size_12);
                    if((_S1021 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_12.x;
                    }
                    else
                    {
                        occlusion_0 = _S191.x;
                    }
                    if((_S1021 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_12.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S191.y;
                    }
                    _S192 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S193 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S192)));
                break;
            }
            texture2d<float, access::sample> _S1024 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S1024).get_width(0)),(*((&atlasHeight_10)) = (_S1024).get_height(0));
            int3 _S1025 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1025)).xy), uint(((_S1025)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S1026 = atlasWidth_10;
                uint _S1027 = 2U % atlasWidth_10;
                int _S1028 = int(_S1027);
                uint _S1029 = 2U / _S1026;
                int3 _S1030 = int3(_S1028, int(_S1029), int(0));
                int _S1031 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1030)).xy), uint(((_S1030)).z))).x * 255.0f)));
                uint _S1032 = atlasWidth_10;
                uint _S1033 = 3U % atlasWidth_10;
                int _S1034 = int(_S1033);
                uint _S1035 = 3U / _S1032;
                int3 _S1036 = int3(_S1034, int(_S1035), int(0));
                int2 minimumTile_10 = int2(_S1031, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1036)).xy), uint(((_S1036)).z))).x * 255.0f))));
                uint _S1037 = atlasWidth_10;
                uint _S1038 = 4U % atlasWidth_10;
                int _S1039 = int(_S1038);
                uint _S1040 = 4U / _S1037;
                int3 _S1041 = int3(_S1039, int(_S1040), int(0));
                uint _S1042 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1041)).xy), uint(((_S1041)).z))).x * 255.0f));
                uint _S1043 = atlasWidth_10;
                uint _S1044 = 5U % atlasWidth_10;
                int _S1045 = int(_S1044);
                uint _S1046 = 5U / _S1043;
                int3 _S1047 = int3(_S1045, int(_S1046), int(0));
                uint _S1048 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1047)).xy), uint(((_S1047)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S1042, _S1048);
                int2 tile_20 = int2(floor(_S191)) - minimumTile_10;
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
                    int3 _S1049 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1049)).xy), uint(((_S1049)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1042 + uint(tile_20.x));
                uint _S1050 = atlasWidth_10;
                uint _S1051 = offset_10 % atlasWidth_10;
                int _S1052 = int(_S1051);
                uint _S1053 = offset_10 / _S1050;
                int3 _S1054 = int3(_S1052, int(_S1053), int(0));
                uint _S1055 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1054)).xy), uint(((_S1054)).z))).x * 255.0f));
                uint _S1056 = atlasWidth_10;
                uint _S1057 = offset_10 + 1U;
                uint _S1058 = _S1057 % atlasWidth_10;
                int _S1059 = int(_S1058);
                uint _S1060 = _S1057 / _S1056;
                int3 _S1061 = int3(_S1059, int(_S1060), int(0));
                uint slot_20 = _S1055 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1061)).xy), uint(((_S1061)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1062 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1062)).xy), uint(((_S1062)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1063 = atlasWidth_10;
                uint _S1064 = 6U % atlasWidth_10;
                int _S1065 = int(_S1064);
                uint _S1066 = 6U / _S1063;
                int3 _S1067 = int3(_S1065, int(_S1066), int(0));
                uint _S1068 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1067)).xy), uint(((_S1067)).z))).x * 255.0f));
                uint _S1069 = atlasWidth_10;
                uint _S1070 = 7U % atlasWidth_10;
                int _S1071 = int(_S1070);
                uint _S1072 = 7U / _S1069;
                int3 _S1073 = int3(_S1071, int(_S1072), int(0));
                uint columns_10 = _S1068 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1073)).xy), uint(((_S1073)).z))).x * 255.0f)) << 8U);
                uint _S1074 = atlasWidth_10;
                uint _S1075 = 8U % atlasWidth_10;
                int _S1076 = int(_S1075);
                uint _S1077 = 8U / _S1074;
                int3 _S1078 = int3(_S1076, int(_S1077), int(0));
                uint _S1079 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1078)).xy), uint(((_S1078)).z))).x * 255.0f));
                uint _S1080 = atlasWidth_10;
                uint _S1081 = 9U % atlasWidth_10;
                int _S1082 = int(_S1081);
                uint _S1083 = 9U / _S1080;
                int3 _S1084 = int3(_S1082, int(_S1083), int(0));
                uint rows_10 = ((_S1079 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1084)).xy), uint(((_S1084)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S1042 * _S1048 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1085 = atlasWidth_10 / columns_10;
                float _S1086 = float(_S1085);
                uint _S1087 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1086, float(_S1087));
                uint _S1088 = slot_21 % columns_10;
                float _S1089 = float(_S1088);
                uint _S1090 = slot_21 / columns_10;
                _S193 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1089, float(_S1090)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(_S191) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1091 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(_S191)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1091);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1092 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S193 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1092)).xy), uint(((_S1092)).z)));
                break;
            }
            uint _S1093 = atlasWidth_10 / uint(_S1091.x);
            float _S1094 = float(_S1093);
            uint _S1095 = (atlasHeight_10 - 1U) / uint(_S1091.y);
            float2 cellSize_21 = float2(_S1094, float(_S1095));
            _S193 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1096 = float4(_S6->compositeControls_0) ;
            if((_S1096.x) != 128.0f)
            {
                break;
            }
            bool _S1097 = (_S1096.w) >= 0.5f;
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
            uint _S1098 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1097)
                {
                    for(;;)
                    {
                        if(_S1098 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1099 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_15;
                        thread uint height_15;
                        (*((&width_15)) = (_S1099).get_width(0)),(*((&height_15)) = (_S1099).get_height(0));
                        float2 size_13 = float2(float(width_15), float(height_15));
                        float2 _S1100 = float2(0.5f) ;
                        float2 remapped_13 = clamp((_S191 * (size_13 - float2(2.0f) ) + float2(1.0f) ) / size_13, _S1100 / size_13, (size_13 - _S1100) / size_13);
                        if((_S1098 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_13.x;
                        }
                        else
                        {
                            occlusion_0 = _S191.x;
                        }
                        if((_S1098 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_13.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S191.y;
                        }
                        _S191 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S191)));
                    break;
                }
                texture2d<float, access::sample> _S1101 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1101).get_width(0)),(*((&atlasHeight_11)) = (_S1101).get_height(0));
                int3 _S1102 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1102)).xy), uint(((_S1102)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1103 = atlasWidth_11;
                    uint _S1104 = 2U % atlasWidth_11;
                    int _S1105 = int(_S1104);
                    uint _S1106 = 2U / _S1103;
                    int3 _S1107 = int3(_S1105, int(_S1106), int(0));
                    int _S1108 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1107)).xy), uint(((_S1107)).z))).x * 255.0f)));
                    uint _S1109 = atlasWidth_11;
                    uint _S1110 = 3U % atlasWidth_11;
                    int _S1111 = int(_S1110);
                    uint _S1112 = 3U / _S1109;
                    int3 _S1113 = int3(_S1111, int(_S1112), int(0));
                    int2 minimumTile_11 = int2(_S1108, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1113)).xy), uint(((_S1113)).z))).x * 255.0f))));
                    uint _S1114 = atlasWidth_11;
                    uint _S1115 = 4U % atlasWidth_11;
                    int _S1116 = int(_S1115);
                    uint _S1117 = 4U / _S1114;
                    int3 _S1118 = int3(_S1116, int(_S1117), int(0));
                    uint _S1119 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1118)).xy), uint(((_S1118)).z))).x * 255.0f));
                    uint _S1120 = atlasWidth_11;
                    uint _S1121 = 5U % atlasWidth_11;
                    int _S1122 = int(_S1121);
                    uint _S1123 = 5U / _S1120;
                    int3 _S1124 = int3(_S1122, int(_S1123), int(0));
                    uint _S1125 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1124)).xy), uint(((_S1124)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1119, _S1125);
                    int2 tile_22 = int2(floor(_S191)) - minimumTile_11;
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
                        int3 _S1126 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1126)).xy), uint(((_S1126)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1119 + uint(tile_22.x));
                    uint _S1127 = atlasWidth_11;
                    uint _S1128 = offset_11 % atlasWidth_11;
                    int _S1129 = int(_S1128);
                    uint _S1130 = offset_11 / _S1127;
                    int3 _S1131 = int3(_S1129, int(_S1130), int(0));
                    uint _S1132 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1131)).xy), uint(((_S1131)).z))).x * 255.0f));
                    uint _S1133 = atlasWidth_11;
                    uint _S1134 = offset_11 + 1U;
                    uint _S1135 = _S1134 % atlasWidth_11;
                    int _S1136 = int(_S1135);
                    uint _S1137 = _S1134 / _S1133;
                    int3 _S1138 = int3(_S1136, int(_S1137), int(0));
                    uint slot_22 = _S1132 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1138)).xy), uint(((_S1138)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1139 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1139)).xy), uint(((_S1139)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1140 = atlasWidth_11;
                    uint _S1141 = 6U % atlasWidth_11;
                    int _S1142 = int(_S1141);
                    uint _S1143 = 6U / _S1140;
                    int3 _S1144 = int3(_S1142, int(_S1143), int(0));
                    uint _S1145 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1144)).xy), uint(((_S1144)).z))).x * 255.0f));
                    uint _S1146 = atlasWidth_11;
                    uint _S1147 = 7U % atlasWidth_11;
                    int _S1148 = int(_S1147);
                    uint _S1149 = 7U / _S1146;
                    int3 _S1150 = int3(_S1148, int(_S1149), int(0));
                    uint columns_11 = _S1145 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1150)).xy), uint(((_S1150)).z))).x * 255.0f)) << 8U);
                    uint _S1151 = atlasWidth_11;
                    uint _S1152 = 8U % atlasWidth_11;
                    int _S1153 = int(_S1152);
                    uint _S1154 = 8U / _S1151;
                    int3 _S1155 = int3(_S1153, int(_S1154), int(0));
                    uint _S1156 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1155)).xy), uint(((_S1155)).z))).x * 255.0f));
                    uint _S1157 = atlasWidth_11;
                    uint _S1158 = 9U % atlasWidth_11;
                    int _S1159 = int(_S1158);
                    uint _S1160 = 9U / _S1157;
                    int3 _S1161 = int3(_S1159, int(_S1160), int(0));
                    uint rows_11 = ((_S1156 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1161)).xy), uint(((_S1161)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1119 * _S1125 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1162 = atlasWidth_11 / columns_11;
                    float _S1163 = float(_S1162);
                    uint _S1164 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1163, float(_S1164));
                    uint _S1165 = slot_23 % columns_11;
                    float _S1166 = float(_S1165);
                    uint _S1167 = slot_23 / columns_11;
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1166, float(_S1167)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(_S191) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1168 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(_S191)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1168);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1169 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1169)).xy), uint(((_S1169)).z)));
                    break;
                }
                uint _S1170 = atlasWidth_11 / uint(_S1168.x);
                float _S1171 = float(_S1170);
                uint _S1172 = (atlasHeight_11 - 1U) / uint(_S1168.y);
                float2 cellSize_23 = float2(_S1171, float(_S1172));
                _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_5 = uint(round(_S1096.y));
            if(operation_5 == 1U)
            {
                _S193 = _S193 * _S378;
                break;
            }
            if(operation_5 == 2U)
            {
                _S193 = _S193 + _S378;
                break;
            }
            if(operation_5 == 3U)
            {
                _S193 = _S193 - _S378;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1096.z;
                _S193 = _S193 * float4((1.0f - factor_5))  + _S378 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S193.x);
    }
    else
    {
        occlusion_0 = _S161;
    }
    float3 specularColor_0;
    float clearcoatRoughness_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool useSecondary_6 = (secondaryMask_0 & 256U) != 0U;
        if((alternateMask_0 & 256U) != 0U)
        {
            if(useSecondary_6)
            {
                _S191 = _S190;
            }
            else
            {
                _S191 = _S189;
            }
        }
        else
        {
            if(useSecondary_6)
            {
                _S191 = _S182;
            }
            else
            {
                _S191 = _S181;
            }
        }
        bool _S1173 = (udimMask_0 & 256U) != 0U;
        uint maskU_6 = uint(round(_S175.y));
        uint maskV_6 = uint(round(_S175.z));
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
        uint _S1174 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1173)
            {
                for(;;)
                {
                    if(_S1174 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S1175 = (&kernelContext_0)->specularColorTexture_0;
                    thread uint width_16;
                    thread uint height_16;
                    (*((&width_16)) = (_S1175).get_width(0)),(*((&height_16)) = (_S1175).get_height(0));
                    float2 size_14 = float2(float(width_16), float(height_16));
                    float2 _S1176 = float2(0.5f) ;
                    float2 remapped_14 = clamp((_S191 * (size_14 - float2(2.0f) ) + float2(1.0f) ) / size_14, _S1176 / size_14, (size_14 - _S1176) / size_14);
                    if((_S1174 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_14.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S191.x;
                    }
                    if((_S1174 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_14.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S191.y;
                    }
                    _S192 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S193 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S192)));
                break;
            }
            texture2d<float, access::sample> _S1177 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1177).get_width(0)),(*((&atlasHeight_12)) = (_S1177).get_height(0));
            int3 _S1178 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1178)).xy), uint(((_S1178)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1179 = atlasWidth_12;
                uint _S1180 = 2U % atlasWidth_12;
                int _S1181 = int(_S1180);
                uint _S1182 = 2U / _S1179;
                int3 _S1183 = int3(_S1181, int(_S1182), int(0));
                int _S1184 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1183)).xy), uint(((_S1183)).z))).x * 255.0f)));
                uint _S1185 = atlasWidth_12;
                uint _S1186 = 3U % atlasWidth_12;
                int _S1187 = int(_S1186);
                uint _S1188 = 3U / _S1185;
                int3 _S1189 = int3(_S1187, int(_S1188), int(0));
                int2 minimumTile_12 = int2(_S1184, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1189)).xy), uint(((_S1189)).z))).x * 255.0f))));
                uint _S1190 = atlasWidth_12;
                uint _S1191 = 4U % atlasWidth_12;
                int _S1192 = int(_S1191);
                uint _S1193 = 4U / _S1190;
                int3 _S1194 = int3(_S1192, int(_S1193), int(0));
                uint _S1195 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1194)).xy), uint(((_S1194)).z))).x * 255.0f));
                uint _S1196 = atlasWidth_12;
                uint _S1197 = 5U % atlasWidth_12;
                int _S1198 = int(_S1197);
                uint _S1199 = 5U / _S1196;
                int3 _S1200 = int3(_S1198, int(_S1199), int(0));
                uint _S1201 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1200)).xy), uint(((_S1200)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1195, _S1201);
                int2 tile_24 = int2(floor(_S191)) - minimumTile_12;
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
                    int3 _S1202 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1202)).xy), uint(((_S1202)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1195 + uint(tile_24.x));
                uint _S1203 = atlasWidth_12;
                uint _S1204 = offset_12 % atlasWidth_12;
                int _S1205 = int(_S1204);
                uint _S1206 = offset_12 / _S1203;
                int3 _S1207 = int3(_S1205, int(_S1206), int(0));
                uint _S1208 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1207)).xy), uint(((_S1207)).z))).x * 255.0f));
                uint _S1209 = atlasWidth_12;
                uint _S1210 = offset_12 + 1U;
                uint _S1211 = _S1210 % atlasWidth_12;
                int _S1212 = int(_S1211);
                uint _S1213 = _S1210 / _S1209;
                int3 _S1214 = int3(_S1212, int(_S1213), int(0));
                uint slot_24 = _S1208 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1214)).xy), uint(((_S1214)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1215 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1215)).xy), uint(((_S1215)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1216 = atlasWidth_12;
                uint _S1217 = 6U % atlasWidth_12;
                int _S1218 = int(_S1217);
                uint _S1219 = 6U / _S1216;
                int3 _S1220 = int3(_S1218, int(_S1219), int(0));
                uint _S1221 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1220)).xy), uint(((_S1220)).z))).x * 255.0f));
                uint _S1222 = atlasWidth_12;
                uint _S1223 = 7U % atlasWidth_12;
                int _S1224 = int(_S1223);
                uint _S1225 = 7U / _S1222;
                int3 _S1226 = int3(_S1224, int(_S1225), int(0));
                uint columns_12 = _S1221 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1226)).xy), uint(((_S1226)).z))).x * 255.0f)) << 8U);
                uint _S1227 = atlasWidth_12;
                uint _S1228 = 8U % atlasWidth_12;
                int _S1229 = int(_S1228);
                uint _S1230 = 8U / _S1227;
                int3 _S1231 = int3(_S1229, int(_S1230), int(0));
                uint _S1232 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1231)).xy), uint(((_S1231)).z))).x * 255.0f));
                uint _S1233 = atlasWidth_12;
                uint _S1234 = 9U % atlasWidth_12;
                int _S1235 = int(_S1234);
                uint _S1236 = 9U / _S1233;
                int3 _S1237 = int3(_S1235, int(_S1236), int(0));
                uint rows_12 = ((_S1232 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1237)).xy), uint(((_S1237)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1195 * _S1201 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1238 = atlasWidth_12 / columns_12;
                float _S1239 = float(_S1238);
                uint _S1240 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1239, float(_S1240));
                uint _S1241 = slot_25 % columns_12;
                float _S1242 = float(_S1241);
                uint _S1243 = slot_25 / columns_12;
                _S193 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1242, float(_S1243)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(_S191) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1244 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(_S191)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1244);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1245 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S193 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1245)).xy), uint(((_S1245)).z)));
                break;
            }
            uint _S1246 = atlasWidth_12 / uint(_S1244.x);
            float _S1247 = float(_S1246);
            uint _S1248 = (atlasHeight_12 - 1U) / uint(_S1244.y);
            float2 cellSize_25 = float2(_S1247, float(_S1248));
            _S193 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1249 = float4(_S6->compositeControls_0) ;
            if((_S1249.x) != 256.0f)
            {
                break;
            }
            bool _S1250 = (_S1249.w) >= 0.5f;
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
            uint _S1251 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1250)
                {
                    for(;;)
                    {
                        if(_S1251 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1252 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_17;
                        thread uint height_17;
                        (*((&width_17)) = (_S1252).get_width(0)),(*((&height_17)) = (_S1252).get_height(0));
                        float2 size_15 = float2(float(width_17), float(height_17));
                        float2 _S1253 = float2(0.5f) ;
                        float2 remapped_15 = clamp((_S191 * (size_15 - float2(2.0f) ) + float2(1.0f) ) / size_15, _S1253 / size_15, (size_15 - _S1253) / size_15);
                        if((_S1251 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_15.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S191.x;
                        }
                        if((_S1251 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_15.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S191.y;
                        }
                        _S191 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S191)));
                    break;
                }
                texture2d<float, access::sample> _S1254 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1254).get_width(0)),(*((&atlasHeight_13)) = (_S1254).get_height(0));
                int3 _S1255 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1255)).xy), uint(((_S1255)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1256 = atlasWidth_13;
                    uint _S1257 = 2U % atlasWidth_13;
                    int _S1258 = int(_S1257);
                    uint _S1259 = 2U / _S1256;
                    int3 _S1260 = int3(_S1258, int(_S1259), int(0));
                    int _S1261 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1260)).xy), uint(((_S1260)).z))).x * 255.0f)));
                    uint _S1262 = atlasWidth_13;
                    uint _S1263 = 3U % atlasWidth_13;
                    int _S1264 = int(_S1263);
                    uint _S1265 = 3U / _S1262;
                    int3 _S1266 = int3(_S1264, int(_S1265), int(0));
                    int2 minimumTile_13 = int2(_S1261, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1266)).xy), uint(((_S1266)).z))).x * 255.0f))));
                    uint _S1267 = atlasWidth_13;
                    uint _S1268 = 4U % atlasWidth_13;
                    int _S1269 = int(_S1268);
                    uint _S1270 = 4U / _S1267;
                    int3 _S1271 = int3(_S1269, int(_S1270), int(0));
                    uint _S1272 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1271)).xy), uint(((_S1271)).z))).x * 255.0f));
                    uint _S1273 = atlasWidth_13;
                    uint _S1274 = 5U % atlasWidth_13;
                    int _S1275 = int(_S1274);
                    uint _S1276 = 5U / _S1273;
                    int3 _S1277 = int3(_S1275, int(_S1276), int(0));
                    uint _S1278 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1277)).xy), uint(((_S1277)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1272, _S1278);
                    int2 tile_26 = int2(floor(_S191)) - minimumTile_13;
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
                        int3 _S1279 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1279)).xy), uint(((_S1279)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1272 + uint(tile_26.x));
                    uint _S1280 = atlasWidth_13;
                    uint _S1281 = offset_13 % atlasWidth_13;
                    int _S1282 = int(_S1281);
                    uint _S1283 = offset_13 / _S1280;
                    int3 _S1284 = int3(_S1282, int(_S1283), int(0));
                    uint _S1285 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1284)).xy), uint(((_S1284)).z))).x * 255.0f));
                    uint _S1286 = atlasWidth_13;
                    uint _S1287 = offset_13 + 1U;
                    uint _S1288 = _S1287 % atlasWidth_13;
                    int _S1289 = int(_S1288);
                    uint _S1290 = _S1287 / _S1286;
                    int3 _S1291 = int3(_S1289, int(_S1290), int(0));
                    uint slot_26 = _S1285 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1291)).xy), uint(((_S1291)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1292 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1292)).xy), uint(((_S1292)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1293 = atlasWidth_13;
                    uint _S1294 = 6U % atlasWidth_13;
                    int _S1295 = int(_S1294);
                    uint _S1296 = 6U / _S1293;
                    int3 _S1297 = int3(_S1295, int(_S1296), int(0));
                    uint _S1298 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1297)).xy), uint(((_S1297)).z))).x * 255.0f));
                    uint _S1299 = atlasWidth_13;
                    uint _S1300 = 7U % atlasWidth_13;
                    int _S1301 = int(_S1300);
                    uint _S1302 = 7U / _S1299;
                    int3 _S1303 = int3(_S1301, int(_S1302), int(0));
                    uint columns_13 = _S1298 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1303)).xy), uint(((_S1303)).z))).x * 255.0f)) << 8U);
                    uint _S1304 = atlasWidth_13;
                    uint _S1305 = 8U % atlasWidth_13;
                    int _S1306 = int(_S1305);
                    uint _S1307 = 8U / _S1304;
                    int3 _S1308 = int3(_S1306, int(_S1307), int(0));
                    uint _S1309 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1308)).xy), uint(((_S1308)).z))).x * 255.0f));
                    uint _S1310 = atlasWidth_13;
                    uint _S1311 = 9U % atlasWidth_13;
                    int _S1312 = int(_S1311);
                    uint _S1313 = 9U / _S1310;
                    int3 _S1314 = int3(_S1312, int(_S1313), int(0));
                    uint rows_13 = ((_S1309 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1314)).xy), uint(((_S1314)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1272 * _S1278 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1315 = atlasWidth_13 / columns_13;
                    float _S1316 = float(_S1315);
                    uint _S1317 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1316, float(_S1317));
                    uint _S1318 = slot_27 % columns_13;
                    float _S1319 = float(_S1318);
                    uint _S1320 = slot_27 / columns_13;
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1319, float(_S1320)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(_S191) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1321 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(_S191)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1321);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1322 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1322)).xy), uint(((_S1322)).z)));
                    break;
                }
                uint _S1323 = atlasWidth_13 / uint(_S1321.x);
                float _S1324 = float(_S1323);
                uint _S1325 = (atlasHeight_13 - 1U) / uint(_S1321.y);
                float2 cellSize_27 = float2(_S1324, float(_S1325));
                _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_6 = uint(round(_S1249.y));
            if(operation_6 == 1U)
            {
                _S193 = _S193 * _S378;
                break;
            }
            if(operation_6 == 2U)
            {
                _S193 = _S193 + _S378;
                break;
            }
            if(operation_6 == 3U)
            {
                _S193 = _S193 - _S378;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1249.z;
                _S193 = _S193 * float4((1.0f - factor_6))  + _S378 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S193.xyz);
    }
    else
    {
        specularColor_0 = _S169;
    }
    if((textureMask_0 & 512U) != 0U)
    {
        bool useSecondary_7 = (secondaryMask_0 & 512U) != 0U;
        if((alternateMask_0 & 512U) != 0U)
        {
            if(useSecondary_7)
            {
                _S191 = _S190;
            }
            else
            {
                _S191 = _S189;
            }
        }
        else
        {
            if(useSecondary_7)
            {
                _S191 = _S182;
            }
            else
            {
                _S191 = _S181;
            }
        }
        bool _S1326 = (udimMask_0 & 512U) != 0U;
        uint maskU_7 = uint(round(_S175.y));
        uint maskV_7 = uint(round(_S175.z));
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
        uint _S1327 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1326)
            {
                for(;;)
                {
                    if(_S1327 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S1328 = (&kernelContext_0)->clearcoatTexture_0;
                    thread uint width_18;
                    thread uint height_18;
                    (*((&width_18)) = (_S1328).get_width(0)),(*((&height_18)) = (_S1328).get_height(0));
                    float2 size_16 = float2(float(width_18), float(height_18));
                    float2 _S1329 = float2(0.5f) ;
                    float2 remapped_16 = clamp((_S191 * (size_16 - float2(2.0f) ) + float2(1.0f) ) / size_16, _S1329 / size_16, (size_16 - _S1329) / size_16);
                    if((_S1327 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_16.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S191.x;
                    }
                    if((_S1327 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_16.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S191.y;
                    }
                    _S192 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S193 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S192)));
                break;
            }
            texture2d<float, access::sample> _S1330 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1330).get_width(0)),(*((&atlasHeight_14)) = (_S1330).get_height(0));
            int3 _S1331 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1331)).xy), uint(((_S1331)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1332 = atlasWidth_14;
                uint _S1333 = 2U % atlasWidth_14;
                int _S1334 = int(_S1333);
                uint _S1335 = 2U / _S1332;
                int3 _S1336 = int3(_S1334, int(_S1335), int(0));
                int _S1337 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1336)).xy), uint(((_S1336)).z))).x * 255.0f)));
                uint _S1338 = atlasWidth_14;
                uint _S1339 = 3U % atlasWidth_14;
                int _S1340 = int(_S1339);
                uint _S1341 = 3U / _S1338;
                int3 _S1342 = int3(_S1340, int(_S1341), int(0));
                int2 minimumTile_14 = int2(_S1337, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1342)).xy), uint(((_S1342)).z))).x * 255.0f))));
                uint _S1343 = atlasWidth_14;
                uint _S1344 = 4U % atlasWidth_14;
                int _S1345 = int(_S1344);
                uint _S1346 = 4U / _S1343;
                int3 _S1347 = int3(_S1345, int(_S1346), int(0));
                uint _S1348 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1347)).xy), uint(((_S1347)).z))).x * 255.0f));
                uint _S1349 = atlasWidth_14;
                uint _S1350 = 5U % atlasWidth_14;
                int _S1351 = int(_S1350);
                uint _S1352 = 5U / _S1349;
                int3 _S1353 = int3(_S1351, int(_S1352), int(0));
                uint _S1354 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1353)).xy), uint(((_S1353)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1348, _S1354);
                int2 tile_28 = int2(floor(_S191)) - minimumTile_14;
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
                    int3 _S1355 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1355)).xy), uint(((_S1355)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1348 + uint(tile_28.x));
                uint _S1356 = atlasWidth_14;
                uint _S1357 = offset_14 % atlasWidth_14;
                int _S1358 = int(_S1357);
                uint _S1359 = offset_14 / _S1356;
                int3 _S1360 = int3(_S1358, int(_S1359), int(0));
                uint _S1361 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1360)).xy), uint(((_S1360)).z))).x * 255.0f));
                uint _S1362 = atlasWidth_14;
                uint _S1363 = offset_14 + 1U;
                uint _S1364 = _S1363 % atlasWidth_14;
                int _S1365 = int(_S1364);
                uint _S1366 = _S1363 / _S1362;
                int3 _S1367 = int3(_S1365, int(_S1366), int(0));
                uint slot_28 = _S1361 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1367)).xy), uint(((_S1367)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1368 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1368)).xy), uint(((_S1368)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1369 = atlasWidth_14;
                uint _S1370 = 6U % atlasWidth_14;
                int _S1371 = int(_S1370);
                uint _S1372 = 6U / _S1369;
                int3 _S1373 = int3(_S1371, int(_S1372), int(0));
                uint _S1374 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1373)).xy), uint(((_S1373)).z))).x * 255.0f));
                uint _S1375 = atlasWidth_14;
                uint _S1376 = 7U % atlasWidth_14;
                int _S1377 = int(_S1376);
                uint _S1378 = 7U / _S1375;
                int3 _S1379 = int3(_S1377, int(_S1378), int(0));
                uint columns_14 = _S1374 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1379)).xy), uint(((_S1379)).z))).x * 255.0f)) << 8U);
                uint _S1380 = atlasWidth_14;
                uint _S1381 = 8U % atlasWidth_14;
                int _S1382 = int(_S1381);
                uint _S1383 = 8U / _S1380;
                int3 _S1384 = int3(_S1382, int(_S1383), int(0));
                uint _S1385 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1384)).xy), uint(((_S1384)).z))).x * 255.0f));
                uint _S1386 = atlasWidth_14;
                uint _S1387 = 9U % atlasWidth_14;
                int _S1388 = int(_S1387);
                uint _S1389 = 9U / _S1386;
                int3 _S1390 = int3(_S1388, int(_S1389), int(0));
                uint rows_14 = ((_S1385 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1390)).xy), uint(((_S1390)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1348 * _S1354 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1391 = atlasWidth_14 / columns_14;
                float _S1392 = float(_S1391);
                uint _S1393 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1392, float(_S1393));
                uint _S1394 = slot_29 % columns_14;
                float _S1395 = float(_S1394);
                uint _S1396 = slot_29 / columns_14;
                _S193 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1395, float(_S1396)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(_S191) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1397 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(_S191)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1397);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1398 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S193 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1398)).xy), uint(((_S1398)).z)));
                break;
            }
            uint _S1399 = atlasWidth_14 / uint(_S1397.x);
            float _S1400 = float(_S1399);
            uint _S1401 = (atlasHeight_14 - 1U) / uint(_S1397.y);
            float2 cellSize_29 = float2(_S1400, float(_S1401));
            _S193 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1402 = float4(_S6->compositeControls_0) ;
            if((_S1402.x) != 512.0f)
            {
                break;
            }
            bool _S1403 = (_S1402.w) >= 0.5f;
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
            uint _S1404 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1403)
                {
                    for(;;)
                    {
                        if(_S1404 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1405 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_19;
                        thread uint height_19;
                        (*((&width_19)) = (_S1405).get_width(0)),(*((&height_19)) = (_S1405).get_height(0));
                        float2 size_17 = float2(float(width_19), float(height_19));
                        float2 _S1406 = float2(0.5f) ;
                        float2 remapped_17 = clamp((_S191 * (size_17 - float2(2.0f) ) + float2(1.0f) ) / size_17, _S1406 / size_17, (size_17 - _S1406) / size_17);
                        if((_S1404 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_17.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S191.x;
                        }
                        if((_S1404 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_17.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S191.y;
                        }
                        _S191 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S191)));
                    break;
                }
                texture2d<float, access::sample> _S1407 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1407).get_width(0)),(*((&atlasHeight_15)) = (_S1407).get_height(0));
                int3 _S1408 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1408)).xy), uint(((_S1408)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1409 = atlasWidth_15;
                    uint _S1410 = 2U % atlasWidth_15;
                    int _S1411 = int(_S1410);
                    uint _S1412 = 2U / _S1409;
                    int3 _S1413 = int3(_S1411, int(_S1412), int(0));
                    int _S1414 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1413)).xy), uint(((_S1413)).z))).x * 255.0f)));
                    uint _S1415 = atlasWidth_15;
                    uint _S1416 = 3U % atlasWidth_15;
                    int _S1417 = int(_S1416);
                    uint _S1418 = 3U / _S1415;
                    int3 _S1419 = int3(_S1417, int(_S1418), int(0));
                    int2 minimumTile_15 = int2(_S1414, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1419)).xy), uint(((_S1419)).z))).x * 255.0f))));
                    uint _S1420 = atlasWidth_15;
                    uint _S1421 = 4U % atlasWidth_15;
                    int _S1422 = int(_S1421);
                    uint _S1423 = 4U / _S1420;
                    int3 _S1424 = int3(_S1422, int(_S1423), int(0));
                    uint _S1425 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1424)).xy), uint(((_S1424)).z))).x * 255.0f));
                    uint _S1426 = atlasWidth_15;
                    uint _S1427 = 5U % atlasWidth_15;
                    int _S1428 = int(_S1427);
                    uint _S1429 = 5U / _S1426;
                    int3 _S1430 = int3(_S1428, int(_S1429), int(0));
                    uint _S1431 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1430)).xy), uint(((_S1430)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1425, _S1431);
                    int2 tile_30 = int2(floor(_S191)) - minimumTile_15;
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
                        int3 _S1432 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1432)).xy), uint(((_S1432)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1425 + uint(tile_30.x));
                    uint _S1433 = atlasWidth_15;
                    uint _S1434 = offset_15 % atlasWidth_15;
                    int _S1435 = int(_S1434);
                    uint _S1436 = offset_15 / _S1433;
                    int3 _S1437 = int3(_S1435, int(_S1436), int(0));
                    uint _S1438 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1437)).xy), uint(((_S1437)).z))).x * 255.0f));
                    uint _S1439 = atlasWidth_15;
                    uint _S1440 = offset_15 + 1U;
                    uint _S1441 = _S1440 % atlasWidth_15;
                    int _S1442 = int(_S1441);
                    uint _S1443 = _S1440 / _S1439;
                    int3 _S1444 = int3(_S1442, int(_S1443), int(0));
                    uint slot_30 = _S1438 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1444)).xy), uint(((_S1444)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1445 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1445)).xy), uint(((_S1445)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1446 = atlasWidth_15;
                    uint _S1447 = 6U % atlasWidth_15;
                    int _S1448 = int(_S1447);
                    uint _S1449 = 6U / _S1446;
                    int3 _S1450 = int3(_S1448, int(_S1449), int(0));
                    uint _S1451 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1450)).xy), uint(((_S1450)).z))).x * 255.0f));
                    uint _S1452 = atlasWidth_15;
                    uint _S1453 = 7U % atlasWidth_15;
                    int _S1454 = int(_S1453);
                    uint _S1455 = 7U / _S1452;
                    int3 _S1456 = int3(_S1454, int(_S1455), int(0));
                    uint columns_15 = _S1451 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1456)).xy), uint(((_S1456)).z))).x * 255.0f)) << 8U);
                    uint _S1457 = atlasWidth_15;
                    uint _S1458 = 8U % atlasWidth_15;
                    int _S1459 = int(_S1458);
                    uint _S1460 = 8U / _S1457;
                    int3 _S1461 = int3(_S1459, int(_S1460), int(0));
                    uint _S1462 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1461)).xy), uint(((_S1461)).z))).x * 255.0f));
                    uint _S1463 = atlasWidth_15;
                    uint _S1464 = 9U % atlasWidth_15;
                    int _S1465 = int(_S1464);
                    uint _S1466 = 9U / _S1463;
                    int3 _S1467 = int3(_S1465, int(_S1466), int(0));
                    uint rows_15 = ((_S1462 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1467)).xy), uint(((_S1467)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1425 * _S1431 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1468 = atlasWidth_15 / columns_15;
                    float _S1469 = float(_S1468);
                    uint _S1470 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1469, float(_S1470));
                    uint _S1471 = slot_31 % columns_15;
                    float _S1472 = float(_S1471);
                    uint _S1473 = slot_31 / columns_15;
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1472, float(_S1473)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(_S191) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1474 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(_S191)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1474);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1475 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1475)).xy), uint(((_S1475)).z)));
                    break;
                }
                uint _S1476 = atlasWidth_15 / uint(_S1474.x);
                float _S1477 = float(_S1476);
                uint _S1478 = (atlasHeight_15 - 1U) / uint(_S1474.y);
                float2 cellSize_31 = float2(_S1477, float(_S1478));
                _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_7 = uint(round(_S1402.y));
            if(operation_7 == 1U)
            {
                _S193 = _S193 * _S378;
                break;
            }
            if(operation_7 == 2U)
            {
                _S193 = _S193 + _S378;
                break;
            }
            if(operation_7 == 3U)
            {
                _S193 = _S193 - _S378;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1402.z;
                _S193 = _S193 * float4((1.0f - factor_7))  + _S378 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S193.x);
    }
    else
    {
        clearcoatAmount_0 = _S171;
    }
    float ior_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool useSecondary_8 = (secondaryMask_0 & 1024U) != 0U;
        if((alternateMask_0 & 1024U) != 0U)
        {
            if(useSecondary_8)
            {
                _S191 = _S190;
            }
            else
            {
                _S191 = _S189;
            }
        }
        else
        {
            if(useSecondary_8)
            {
                _S191 = _S182;
            }
            else
            {
                _S191 = _S181;
            }
        }
        bool _S1479 = (udimMask_0 & 1024U) != 0U;
        uint maskU_8 = uint(round(_S175.y));
        uint maskV_8 = uint(round(_S175.z));
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
            if(!_S1479)
            {
                for(;;)
                {
                    if(_S1480 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S1481 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
                    thread uint width_20;
                    thread uint height_20;
                    (*((&width_20)) = (_S1481).get_width(0)),(*((&height_20)) = (_S1481).get_height(0));
                    float2 size_18 = float2(float(width_20), float(height_20));
                    float2 _S1482 = float2(0.5f) ;
                    float2 remapped_18 = clamp((_S191 * (size_18 - float2(2.0f) ) + float2(1.0f) ) / size_18, _S1482 / size_18, (size_18 - _S1482) / size_18);
                    if((_S1480 & 1U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_18.x;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S191.x;
                    }
                    if((_S1480 & 2U) != 0U)
                    {
                        ior_0 = remapped_18.y;
                    }
                    else
                    {
                        ior_0 = _S191.y;
                    }
                    _S192 = float2(clearcoatRoughness_0, ior_0);
                    break;
                }
                _S193 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S192)));
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
                int2 tile_32 = int2(floor(_S191)) - minimumTile_16;
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
                    _S193 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1508)).xy), uint(((_S1508)).z)));
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
                    _S193 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1521)).xy), uint(((_S1521)).z)));
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
                _S193 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1548, float(_S1549)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(_S191) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1550 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(_S191)) - int2(metadata_16.xy);
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
                _S193 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1551)).xy), uint(((_S1551)).z)));
                break;
            }
            uint _S1552 = atlasWidth_16 / uint(_S1550.x);
            float _S1553 = float(_S1552);
            uint _S1554 = (atlasHeight_16 - 1U) / uint(_S1550.y);
            float2 cellSize_33 = float2(_S1553, float(_S1554));
            _S193 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
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
                        float2 remapped_19 = clamp((_S191 * (size_19 - float2(2.0f) ) + float2(1.0f) ) / size_19, _S1559 / size_19, (size_19 - _S1559) / size_19);
                        if((_S1557 & 1U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_19.x;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S191.x;
                        }
                        if((_S1557 & 2U) != 0U)
                        {
                            ior_0 = remapped_19.y;
                        }
                        else
                        {
                            ior_0 = _S191.y;
                        }
                        _S191 = float2(clearcoatRoughness_0, ior_0);
                        break;
                    }
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S191)));
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
                    int2 tile_34 = int2(floor(_S191)) - minimumTile_17;
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
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1585)).xy), uint(((_S1585)).z)));
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
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1598)).xy), uint(((_S1598)).z)));
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
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1625, float(_S1626)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(_S191) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1627 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(_S191)) - int2(metadata_17.xy);
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
                    _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1628)).xy), uint(((_S1628)).z)));
                    break;
                }
                uint _S1629 = atlasWidth_17 / uint(_S1627.x);
                float _S1630 = float(_S1629);
                uint _S1631 = (atlasHeight_17 - 1U) / uint(_S1627.y);
                float2 cellSize_35 = float2(_S1630, float(_S1631));
                _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_8 = uint(round(_S1555.y));
            if(operation_8 == 1U)
            {
                _S193 = _S193 * _S378;
                break;
            }
            if(operation_8 == 2U)
            {
                _S193 = _S193 + _S378;
                break;
            }
            if(operation_8 == 3U)
            {
                _S193 = _S193 - _S378;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1555.z;
                _S193 = _S193 * float4((1.0f - factor_8))  + _S378 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S193.x);
    }
    else
    {
        clearcoatRoughness_0 = _S172;
    }
    float lightType_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool useSecondary_9 = (secondaryMask_0 & 2048U) != 0U;
        if((alternateMask_0 & 2048U) != 0U)
        {
            if(useSecondary_9)
            {
                _S191 = _S190;
            }
            else
            {
                _S191 = _S189;
            }
        }
        else
        {
            if(useSecondary_9)
            {
                _S191 = _S182;
            }
            else
            {
                _S191 = _S181;
            }
        }
        bool _S1632 = (udimMask_0 & 2048U) != 0U;
        uint maskU_9 = uint(round(_S175.y));
        uint maskV_9 = uint(round(_S175.z));
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
        uint _S1633 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1632)
            {
                for(;;)
                {
                    if(_S1633 == 0U)
                    {
                        _S192 = _S191;
                        break;
                    }
                    texture2d<float, access::sample> _S1634 = (&kernelContext_0)->iorTexture_0;
                    thread uint width_22;
                    thread uint height_22;
                    (*((&width_22)) = (_S1634).get_width(0)),(*((&height_22)) = (_S1634).get_height(0));
                    float2 size_20 = float2(float(width_22), float(height_22));
                    float2 _S1635 = float2(0.5f) ;
                    float2 remapped_20 = clamp((_S191 * (size_20 - float2(2.0f) ) + float2(1.0f) ) / size_20, _S1635 / size_20, (size_20 - _S1635) / size_20);
                    if((_S1633 & 1U) != 0U)
                    {
                        ior_0 = remapped_20.x;
                    }
                    else
                    {
                        ior_0 = _S191.x;
                    }
                    if((_S1633 & 2U) != 0U)
                    {
                        lightType_0 = remapped_20.y;
                    }
                    else
                    {
                        lightType_0 = _S191.y;
                    }
                    _S192 = float2(ior_0, lightType_0);
                    break;
                }
                _S193 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S192)));
                break;
            }
            texture2d<float, access::sample> _S1636 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1636).get_width(0)),(*((&atlasHeight_18)) = (_S1636).get_height(0));
            int3 _S1637 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1637)).xy), uint(((_S1637)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1638 = atlasWidth_18;
                uint _S1639 = 2U % atlasWidth_18;
                int _S1640 = int(_S1639);
                uint _S1641 = 2U / _S1638;
                int3 _S1642 = int3(_S1640, int(_S1641), int(0));
                int _S1643 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1642)).xy), uint(((_S1642)).z))).x * 255.0f)));
                uint _S1644 = atlasWidth_18;
                uint _S1645 = 3U % atlasWidth_18;
                int _S1646 = int(_S1645);
                uint _S1647 = 3U / _S1644;
                int3 _S1648 = int3(_S1646, int(_S1647), int(0));
                int2 minimumTile_18 = int2(_S1643, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1648)).xy), uint(((_S1648)).z))).x * 255.0f))));
                uint _S1649 = atlasWidth_18;
                uint _S1650 = 4U % atlasWidth_18;
                int _S1651 = int(_S1650);
                uint _S1652 = 4U / _S1649;
                int3 _S1653 = int3(_S1651, int(_S1652), int(0));
                uint _S1654 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1653)).xy), uint(((_S1653)).z))).x * 255.0f));
                uint _S1655 = atlasWidth_18;
                uint _S1656 = 5U % atlasWidth_18;
                int _S1657 = int(_S1656);
                uint _S1658 = 5U / _S1655;
                int3 _S1659 = int3(_S1657, int(_S1658), int(0));
                uint _S1660 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1659)).xy), uint(((_S1659)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1654, _S1660);
                int2 tile_36 = int2(floor(_S191)) - minimumTile_18;
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
                    int3 _S1661 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1661)).xy), uint(((_S1661)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1654 + uint(tile_36.x));
                uint _S1662 = atlasWidth_18;
                uint _S1663 = offset_18 % atlasWidth_18;
                int _S1664 = int(_S1663);
                uint _S1665 = offset_18 / _S1662;
                int3 _S1666 = int3(_S1664, int(_S1665), int(0));
                uint _S1667 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1666)).xy), uint(((_S1666)).z))).x * 255.0f));
                uint _S1668 = atlasWidth_18;
                uint _S1669 = offset_18 + 1U;
                uint _S1670 = _S1669 % atlasWidth_18;
                int _S1671 = int(_S1670);
                uint _S1672 = _S1669 / _S1668;
                int3 _S1673 = int3(_S1671, int(_S1672), int(0));
                uint slot_36 = _S1667 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1673)).xy), uint(((_S1673)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1674 = int3(int(1), int(0), int(0));
                    _S193 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1674)).xy), uint(((_S1674)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1675 = atlasWidth_18;
                uint _S1676 = 6U % atlasWidth_18;
                int _S1677 = int(_S1676);
                uint _S1678 = 6U / _S1675;
                int3 _S1679 = int3(_S1677, int(_S1678), int(0));
                uint _S1680 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1679)).xy), uint(((_S1679)).z))).x * 255.0f));
                uint _S1681 = atlasWidth_18;
                uint _S1682 = 7U % atlasWidth_18;
                int _S1683 = int(_S1682);
                uint _S1684 = 7U / _S1681;
                int3 _S1685 = int3(_S1683, int(_S1684), int(0));
                uint columns_18 = _S1680 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1685)).xy), uint(((_S1685)).z))).x * 255.0f)) << 8U);
                uint _S1686 = atlasWidth_18;
                uint _S1687 = 8U % atlasWidth_18;
                int _S1688 = int(_S1687);
                uint _S1689 = 8U / _S1686;
                int3 _S1690 = int3(_S1688, int(_S1689), int(0));
                uint _S1691 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1690)).xy), uint(((_S1690)).z))).x * 255.0f));
                uint _S1692 = atlasWidth_18;
                uint _S1693 = 9U % atlasWidth_18;
                int _S1694 = int(_S1693);
                uint _S1695 = 9U / _S1692;
                int3 _S1696 = int3(_S1694, int(_S1695), int(0));
                uint rows_18 = ((_S1691 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1696)).xy), uint(((_S1696)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1654 * _S1660 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1697 = atlasWidth_18 / columns_18;
                float _S1698 = float(_S1697);
                uint _S1699 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1698, float(_S1699));
                uint _S1700 = slot_37 % columns_18;
                float _S1701 = float(_S1700);
                uint _S1702 = slot_37 / columns_18;
                _S193 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1701, float(_S1702)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(_S191) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1703 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(_S191)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1703);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1704 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S193 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1704)).xy), uint(((_S1704)).z)));
                break;
            }
            uint _S1705 = atlasWidth_18 / uint(_S1703.x);
            float _S1706 = float(_S1705);
            uint _S1707 = (atlasHeight_18 - 1U) / uint(_S1703.y);
            float2 cellSize_37 = float2(_S1706, float(_S1707));
            _S193 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1708 = float4(_S6->compositeControls_0) ;
            if((_S1708.x) != 2048.0f)
            {
                break;
            }
            bool _S1709 = (_S1708.w) >= 0.5f;
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
            uint _S1710 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1709)
                {
                    for(;;)
                    {
                        if(_S1710 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1711 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_23;
                        thread uint height_23;
                        (*((&width_23)) = (_S1711).get_width(0)),(*((&height_23)) = (_S1711).get_height(0));
                        float2 size_21 = float2(float(width_23), float(height_23));
                        float2 _S1712 = float2(0.5f) ;
                        float2 remapped_21 = clamp((_S191 * (size_21 - float2(2.0f) ) + float2(1.0f) ) / size_21, _S1712 / size_21, (size_21 - _S1712) / size_21);
                        if((_S1710 & 1U) != 0U)
                        {
                            ior_0 = remapped_21.x;
                        }
                        else
                        {
                            ior_0 = _S191.x;
                        }
                        if((_S1710 & 2U) != 0U)
                        {
                            lightType_0 = remapped_21.y;
                        }
                        else
                        {
                            lightType_0 = _S191.y;
                        }
                        _S191 = float2(ior_0, lightType_0);
                        break;
                    }
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S191)));
                    break;
                }
                texture2d<float, access::sample> _S1713 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1713).get_width(0)),(*((&atlasHeight_19)) = (_S1713).get_height(0));
                int3 _S1714 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1714)).xy), uint(((_S1714)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1715 = atlasWidth_19;
                    uint _S1716 = 2U % atlasWidth_19;
                    int _S1717 = int(_S1716);
                    uint _S1718 = 2U / _S1715;
                    int3 _S1719 = int3(_S1717, int(_S1718), int(0));
                    int _S1720 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1719)).xy), uint(((_S1719)).z))).x * 255.0f)));
                    uint _S1721 = atlasWidth_19;
                    uint _S1722 = 3U % atlasWidth_19;
                    int _S1723 = int(_S1722);
                    uint _S1724 = 3U / _S1721;
                    int3 _S1725 = int3(_S1723, int(_S1724), int(0));
                    int2 minimumTile_19 = int2(_S1720, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1725)).xy), uint(((_S1725)).z))).x * 255.0f))));
                    uint _S1726 = atlasWidth_19;
                    uint _S1727 = 4U % atlasWidth_19;
                    int _S1728 = int(_S1727);
                    uint _S1729 = 4U / _S1726;
                    int3 _S1730 = int3(_S1728, int(_S1729), int(0));
                    uint _S1731 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1730)).xy), uint(((_S1730)).z))).x * 255.0f));
                    uint _S1732 = atlasWidth_19;
                    uint _S1733 = 5U % atlasWidth_19;
                    int _S1734 = int(_S1733);
                    uint _S1735 = 5U / _S1732;
                    int3 _S1736 = int3(_S1734, int(_S1735), int(0));
                    uint _S1737 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1736)).xy), uint(((_S1736)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1731, _S1737);
                    int2 tile_38 = int2(floor(_S191)) - minimumTile_19;
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
                        int3 _S1738 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1738)).xy), uint(((_S1738)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1731 + uint(tile_38.x));
                    uint _S1739 = atlasWidth_19;
                    uint _S1740 = offset_19 % atlasWidth_19;
                    int _S1741 = int(_S1740);
                    uint _S1742 = offset_19 / _S1739;
                    int3 _S1743 = int3(_S1741, int(_S1742), int(0));
                    uint _S1744 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1743)).xy), uint(((_S1743)).z))).x * 255.0f));
                    uint _S1745 = atlasWidth_19;
                    uint _S1746 = offset_19 + 1U;
                    uint _S1747 = _S1746 % atlasWidth_19;
                    int _S1748 = int(_S1747);
                    uint _S1749 = _S1746 / _S1745;
                    int3 _S1750 = int3(_S1748, int(_S1749), int(0));
                    uint slot_38 = _S1744 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1750)).xy), uint(((_S1750)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1751 = int3(int(1), int(0), int(0));
                        _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1751)).xy), uint(((_S1751)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1752 = atlasWidth_19;
                    uint _S1753 = 6U % atlasWidth_19;
                    int _S1754 = int(_S1753);
                    uint _S1755 = 6U / _S1752;
                    int3 _S1756 = int3(_S1754, int(_S1755), int(0));
                    uint _S1757 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1756)).xy), uint(((_S1756)).z))).x * 255.0f));
                    uint _S1758 = atlasWidth_19;
                    uint _S1759 = 7U % atlasWidth_19;
                    int _S1760 = int(_S1759);
                    uint _S1761 = 7U / _S1758;
                    int3 _S1762 = int3(_S1760, int(_S1761), int(0));
                    uint columns_19 = _S1757 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1762)).xy), uint(((_S1762)).z))).x * 255.0f)) << 8U);
                    uint _S1763 = atlasWidth_19;
                    uint _S1764 = 8U % atlasWidth_19;
                    int _S1765 = int(_S1764);
                    uint _S1766 = 8U / _S1763;
                    int3 _S1767 = int3(_S1765, int(_S1766), int(0));
                    uint _S1768 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1767)).xy), uint(((_S1767)).z))).x * 255.0f));
                    uint _S1769 = atlasWidth_19;
                    uint _S1770 = 9U % atlasWidth_19;
                    int _S1771 = int(_S1770);
                    uint _S1772 = 9U / _S1769;
                    int3 _S1773 = int3(_S1771, int(_S1772), int(0));
                    uint rows_19 = ((_S1768 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1773)).xy), uint(((_S1773)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1731 * _S1737 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1774 = atlasWidth_19 / columns_19;
                    float _S1775 = float(_S1774);
                    uint _S1776 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1775, float(_S1776));
                    uint _S1777 = slot_39 % columns_19;
                    float _S1778 = float(_S1777);
                    uint _S1779 = slot_39 / columns_19;
                    _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1778, float(_S1779)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(_S191) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1780 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(_S191)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1780);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1781 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S378 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1781)).xy), uint(((_S1781)).z)));
                    break;
                }
                uint _S1782 = atlasWidth_19 / uint(_S1780.x);
                float _S1783 = float(_S1782);
                uint _S1784 = (atlasHeight_19 - 1U) / uint(_S1780.y);
                float2 cellSize_39 = float2(_S1783, float(_S1784));
                _S378 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(_S191) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_9 = uint(round(_S1708.y));
            if(operation_9 == 1U)
            {
                _S193 = _S193 * _S378;
                break;
            }
            if(operation_9 == 2U)
            {
                _S193 = _S193 + _S378;
                break;
            }
            if(operation_9 == 3U)
            {
                _S193 = _S193 - _S378;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1708.z;
                _S193 = _S193 * float4((1.0f - factor_9))  + _S378 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S193.x;
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
    float _S1785 = saturate(abs(dot(normal_2, diffuseFresnel_0)) + 0.00000999999974738f);
    float _S1786 = max(0.00100000004749745f, roughness_0);
    float _S1787 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1788 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1788;
    bool omniPbrSpecular_0 = (_S173.w) >= 0.5f;
    float dielectricLevel_0 = _S159.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1789 = float3(dielectricLevel_0) ;
        float3 _S1790 = float3(metallic_0) ;
        float3 _S1791 = mix(_S1789, diffuseColor_0, _S1790);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1789, diffuseColor_0, _S1790);
        grazingIncidence_0 = _S1791;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S165.w) >= 0.5f)
        {
            float3 _S1792 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1792;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1793 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1793);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1793);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1794 = _S2;
    float _S1795 = _S2.ambientLight_0.w;
    if(_S1795 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1796 = _S1794.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1796, _S1796)) > 0.0f;
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
    uint _S1797 = min(uint(_S1795), 128U);
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
    float4 _S1798 = float4(_S6->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S1798.w) ;
    uint domeLinkMask_0 = uint(max(_S175.x, 0.0f));
    FragmentFrameParameters_0 _S1799 = _S2;
    uint _S1800 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S1800) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S1801 = _S1794.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S1802 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S1802;
        for(;;)
        {
            if(lightCount_0 < _S1800)
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
        domeAmbient_1 = _S1801;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S1803 = !hasSceneLighting_0;
    if(_S1803)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1797;
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
        bool _S1804;
        if(hasSceneLighting_0)
        {
            _S1804 = (((uint4(_S6->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S1804 = false;
        }
        if(_S1804)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S1805 = lightIndex_0 == 0U;
        bool _S1806;
        if(_S1805)
        {
            _S1806 = _S1803;
        }
        else
        {
            _S1806 = false;
        }
        if(_S1806)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S1807 = 224U + lightIndex_0 * 16U;
            float _S1808 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1807)>>2]);
            float _S1809 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1807 + 4U)>>2]);
            float _S1810 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1807 + 8U)>>2]);
            float _S1811 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1807 + 12U)>>2]);
            lightType_0 = _S1811;
        }
        bool _S1812;
        if(_S1805)
        {
            _S1812 = _S1803;
        }
        else
        {
            _S1812 = false;
        }
        if(_S1812)
        {
            lightDirection_0 = normalize((float4(_S6->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S1813 = 2272U + lightIndex_0 * 16U;
            float _S1814 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1813)>>2]);
            float _S1815 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1813 + 4U)>>2]);
            float _S1816 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1813 + 8U)>>2]);
            float _S1817 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1813 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S1814, _S1815, _S1816));
        }
        bool _S1818;
        if(_S1805)
        {
            _S1818 = _S1803;
        }
        else
        {
            _S1818 = false;
        }
        if(_S1818)
        {
            roughness_0 = (float4(_S6->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S1819 = 4320U + lightIndex_0 * 16U;
            float _S1820 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1819)>>2]);
            float _S1821 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1819 + 4U)>>2]);
            float _S1822 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1819 + 8U)>>2]);
            float _S1823 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1819 + 12U)>>2]);
            roughness_0 = _S1823;
        }
        bool _S1824;
        if(_S1805)
        {
            _S1824 = _S1803;
        }
        else
        {
            _S1824 = false;
        }
        if(_S1824)
        {
            diffuseColor_0 = _S1798.xyz;
        }
        else
        {
            uint _S1825 = 4320U + lightIndex_0 * 16U;
            float _S1826 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1825)>>2]);
            float _S1827 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1825 + 4U)>>2]);
            float _S1828 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1825 + 8U)>>2]);
            float _S1829 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1825 + 12U)>>2]);
            diffuseColor_0 = float3(_S1826, _S1827, _S1828);
        }
        bool _S1830;
        if(_S1805)
        {
            _S1830 = _S1803;
        }
        else
        {
            _S1830 = false;
        }
        if(_S1830)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S1831 = 6368U + lightIndex_0 * 16U;
            float _S1832 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1831)>>2]);
            float _S1833 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1831 + 4U)>>2]);
            float _S1834 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1831 + 8U)>>2]);
            float _S1835 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1831 + 12U)>>2]);
            metallic_0 = _S1832;
        }
        bool _S1836;
        if(_S1805)
        {
            _S1836 = _S1803;
        }
        else
        {
            _S1836 = false;
        }
        if(_S1836)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S1837 = 6368U + lightIndex_0 * 16U;
            float _S1838 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1837)>>2]);
            float _S1839 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1837 + 4U)>>2]);
            float _S1840 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1837 + 8U)>>2]);
            float _S1841 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1837 + 12U)>>2]);
            clearcoatRoughness_0 = _S1839;
        }
        bool _S1842;
        if(_S1805)
        {
            _S1842 = _S1803;
        }
        else
        {
            _S1842 = false;
        }
        if(_S1842)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1843 = 8416U + lightIndex_0 * 16U;
            float _S1844 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1843)>>2]);
            float _S1845 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1843 + 4U)>>2]);
            float _S1846 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1843 + 8U)>>2]);
            float _S1847 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1843 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S1844, _S1845, _S1846));
        }
        bool _S1848;
        if(_S1805)
        {
            _S1848 = _S1803;
        }
        else
        {
            _S1848 = false;
        }
        if(_S1848)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S1849 = 10464U + lightIndex_0 * 16U;
            float _S1850 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1849)>>2]);
            float _S1851 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1849 + 4U)>>2]);
            float _S1852 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1849 + 8U)>>2]);
            float _S1853 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1849 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S1850, _S1851, _S1852));
        }
        bool _S1854;
        if(_S1805)
        {
            _S1854 = _S1803;
        }
        else
        {
            _S1854 = false;
        }
        float shapeX_0;
        if(_S1854)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S1855 = 8416U + lightIndex_0 * 16U;
            float _S1856 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1855)>>2]);
            float _S1857 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1855 + 4U)>>2]);
            float _S1858 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1855 + 8U)>>2]);
            float _S1859 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1855 + 12U)>>2]);
            shapeX_0 = _S1859;
        }
        bool _S1860;
        if(_S1805)
        {
            _S1860 = _S1803;
        }
        else
        {
            _S1860 = false;
        }
        float shapeY_0;
        if(_S1860)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S1861 = 10464U + lightIndex_0 * 16U;
            float _S1862 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1861)>>2]);
            float _S1863 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1861 + 4U)>>2]);
            float _S1864 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1861 + 8U)>>2]);
            float _S1865 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1861 + 12U)>>2]);
            shapeY_0 = _S1865;
        }
        bool _S1866;
        if(_S1805)
        {
            _S1866 = _S1803;
        }
        else
        {
            _S1866 = false;
        }
        float lightRadius_0;
        if(_S1866)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S1867 = 2272U + lightIndex_0 * 16U;
            float _S1868 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1867)>>2]);
            float _S1869 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1867 + 4U)>>2]);
            float _S1870 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1867 + 8U)>>2]);
            float _S1871 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1867 + 12U)>>2]);
            lightRadius_0 = _S1871;
        }
        bool _S1872;
        if(_S1805)
        {
            _S1872 = _S1803;
        }
        else
        {
            _S1872 = false;
        }
        if(_S1872)
        {
            domeAmbient_1 = diffuseFresnel_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S1873;
        if(_S1805)
        {
            _S1873 = _S1803;
        }
        else
        {
            _S1873 = false;
        }
        if(_S1873)
        {
            _S193 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1874 = 15296U + lightIndex_0 * 16U;
            float _S1875 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1874)>>2]);
            float _S1876 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1874 + 4U)>>2]);
            float _S1877 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1874 + 8U)>>2]);
            float _S1878 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1874 + 12U)>>2]);
            _S193 = float4(_S1875, _S1876, _S1877, _S1878);
        }
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S1879 = 12960U + lightIndex_0 * 16U;
            float _S1880 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1879)>>2]);
            float _S1881 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1879 + 4U)>>2]);
            float _S1882 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1879 + 8U)>>2]);
            float _S1883 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1879 + 12U)>>2]);
            int shadowSlot_0 = int(_S1880);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S1884 = _S2;
                    FragmentFrameParameters_0 _S1885 = _S2;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - specularColor_0;
                    }
                    else
                    {
                        color_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S1884.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S1886 = lightClip_0.w;
                    if(_S1886 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S1886) ;
                    bool _S1887;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S1887 = true;
                    }
                    else
                    {
                        _S1887 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S1888;
                    if(_S1887)
                    {
                        _S1888 = true;
                    }
                    else
                    {
                        _S1888 = (ndc_0.z) < 0.0f;
                    }
                    bool _S1889;
                    if(_S1888)
                    {
                        _S1889 = true;
                    }
                    else
                    {
                        _S1889 = (ndc_0.z) > 1.0f;
                    }
                    if(_S1889)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S1890 = _S1885.shadowTile_0[shadowSlot_0].xy;
                    float2 _S1891 = _S1885.shadowTile_0[shadowSlot_0].zw;
                    float2 _S1892 = _S1890 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S1891;
                    float texel_0 = _S1884.shadowControls_0[shadowSlot_0].w;
                    float _S1893 = max(_S1884.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S1894 = ndc_0.z - _S1884.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S1895 = float2((texel_0 * 0.5f)) ;
                    float2 _S1896 = _S1890 + _S1895;
                    float2 _S1897 = _S1890 + _S1891 - _S1895;
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
                            if(_S1894 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S1892 + float2(float(x_0), float(y_0)) * float2((_S1893 * texel_0)) , _S1896, _S1897)), level((0.0f))).x))
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
        float3 _S1898 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S1898;
        sampleOffsets_0[int(1)] = _S1898;
        sampleOffsets_0[int(2)] = _S1898;
        sampleOffsets_0[int(3)] = _S1898;
        sampleOffsets_0[int(4)] = _S1898;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S1899 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S1899 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S1899 - halfHeight_0;
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
                uint _S1900 = 224U + lightIndex_0 * 16U;
                float _S1901 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1900)>>2]);
                float _S1902 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1900 + 4U)>>2]);
                float _S1903 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1900 + 8U)>>2]);
                float _S1904 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1900 + 12U)>>2]);
                float3 toLight_0 = float3(_S1901, _S1902, _S1903) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S1905 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S1905)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S1905;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S193.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S193.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S1906 = _S193.y;
                        if(_S1906 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S1906);
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
            float _S1907 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S1907)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S1907) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S1908 = float3(_S1907) ;
            float _S1909 = max(_S1786, 0.00100000004749745f);
            float alpha_0 = _S1909 * _S1909;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S1910 = 1.0f - k_0;
            float3 _S1911 = float3(max(4.0f * normalDotLight_0 * _S1785, 1.00000000317107685e-30f)) ;
            float3 _S1912 = mix(normalIncidence_0, grazingIncidence_0, _S1908) * float3((_S1785 / (_S1785 * _S1910 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S1910 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S1911;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S1913 = max(_S1787, 0.00100000004749745f);
                float alpha_1 = _S1913 * _S1913;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S1914 = 1.0f - k_1;
                directSpecular_0 = _S1912 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S1908) * float3((_S1785 / (_S1785 * _S1914 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S1914 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S1911);
            }
            else
            {
                directSpecular_0 = _S1912;
            }
            float3 _S1915 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S1915 * _S1788) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S1915);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S1916 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S1917 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S1918 = saturate(_S1786);
        float2 _S1919 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S1917, _S1918)), level((0.0f)))).xy;
        float _S1920 = _S1919.x;
        float _S1921 = _S1919.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S1920)  + grazingIncidence_0 * float3(_S1921) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S1920 + _S1921)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S1922 = max(_S1799.domeControls_0.y, 1.0f);
        float3 _S1923 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S1922 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1799.domeControls_0.z;
            float _S1924 = _S1799.domeControls_0.w;
            for(;;)
            {
                bool _S1925 = _S1922 <= 1.0f;
                _S4 = _S1925;
                if(_S1925)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S1924, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S1922)), level((0.0f)))).xyz;
                break;
            }
            float _S1926 = _S1916.environmentControls_0.y;
            float _S1927 = _S1916.environmentControls_0.z;
            for(;;)
            {
                if(_S4)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1928 = max(_S1926, 1.0f);
                    float _S1929 = _S1928 - 1.0f;
                    float slice_0 = _S1918 * max(_S1929, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S1927, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S1928)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S1929) + v_0) / _S1928)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S1930 = max(_S1926, 1.0f);
                float total_0 = _S1930 * _S1922;
                float _S1931 = _S1930 - 1.0f;
                float slice_1 = _S1918 * max(_S1931, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S1927, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S1930;
                specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S1931) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S1932 = max(_S1926, 1.0f);
                        float _S1933 = _S1932 - 1.0f;
                        float slice_2 = saturate(_S1787) * max(_S1933, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S1927, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S1932)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S1933) + v_2) / _S1932)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1934 = max(_S1926, 1.0f);
                    float total_1 = _S1934 * _S1922;
                    float _S1935 = _S1934 - 1.0f;
                    float slice_3 = saturate(_S1787) * max(_S1935, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S1927, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S1934;
                    diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S1935) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightBitangent_0 = diffuseFresnel_0;
            }
            else
            {
                lightBitangent_0 = _S1923;
            }
            lightDirection_0 = diffuseColor_0;
            lightTangent_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightDirection_0 = _S1923;
            lightTangent_0 = _S1923;
            lightBitangent_0 = _S1923;
            for(;;)
            {
                if(sampleIndex_0 < _S1800)
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
                float _S1936 = _S1799.domeControls_0.w;
                for(;;)
                {
                    bool _S1937 = _S1922 <= 1.0f;
                    _S5 = _S1937;
                    if(_S1937)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S1936, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S1922)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightDirection_0 + diffuseColor_0;
                float _S1938 = _S1916.environmentControls_0.y;
                float _S1939 = _S1916.environmentControls_0.z;
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1940 = max(_S1938, 1.0f);
                        float _S1941 = _S1940 - 1.0f;
                        float slice_4 = _S1918 * max(_S1941, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S1939, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S1940)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S1941) + v_4) / _S1940)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1942 = max(_S1938, 1.0f);
                    float total_2 = _S1942 * _S1922;
                    float _S1943 = _S1942 - 1.0f;
                    float slice_5 = _S1918 * max(_S1943, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S1939, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S1942;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S1943) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
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
                            float _S1944 = max(_S1938, 1.0f);
                            float _S1945 = _S1944 - 1.0f;
                            float slice_6 = saturate(_S1787) * max(_S1945, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S1939, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S1944)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S1945) + v_6) / _S1944)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1946 = max(_S1938, 1.0f);
                        float total_3 = _S1946 * _S1922;
                        float _S1947 = _S1946 - 1.0f;
                        float slice_7 = saturate(_S1787) * max(_S1947, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S1939, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S1946;
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S1947) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S1948 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S1917, saturate(_S1787))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightBitangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S1948.x)  + float3(_S1948.y) );
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
    pixelOutput_0 _S1949 = { float4(color_3, opacity_0) };
    return _S1949;
}
