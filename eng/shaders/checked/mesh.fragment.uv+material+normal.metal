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
    texture2d<float, access::sample> compositeTexture_0;
    sampler compositeSampler_0;
    texture2d<float, access::sample> normalTexture_0;
    sampler normalSampler_0;
    texture2d<float, access::sample> roughnessMetallicTexture_0;
    sampler roughnessMetallicSampler_0;
    texture2d<float, access::sample> metallicTexture_0;
    sampler metallicSampler_0;
    texture2d<float, access::sample> emissiveTexture_0;
    sampler emissiveSampler_0;
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

[[fragment]] pixelOutput_0 fragmentMain_uv_material_normal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_0;
    FragmentFrameParameters_0 _S2;
    uint4 _S3;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    bool _S4;
    bool _S5;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_0)->baseColorSampler_0 = baseColorSampler_1;
    (&kernelContext_0)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_0)->compositeSampler_0 = compositeSampler_1;
    (&kernelContext_0)->normalTexture_0 = normalTexture_1;
    (&kernelContext_0)->normalSampler_0 = normalSampler_1;
    (&kernelContext_0)->roughnessMetallicTexture_0 = roughnessMetallicTexture_1;
    (&kernelContext_0)->roughnessMetallicSampler_0 = roughnessMetallicSampler_1;
    (&kernelContext_0)->metallicTexture_0 = metallicTexture_1;
    (&kernelContext_0)->metallicSampler_0 = metallicSampler_1;
    (&kernelContext_0)->emissiveTexture_0 = emissiveTexture_1;
    (&kernelContext_0)->emissiveSampler_0 = emissiveSampler_1;
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
    float4 _S178;
    if((textureMask_0 & 2U) != 0U)
    {
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
            int2 _S182 = int2(metadata_0.zw);
            int2 tile_0 = int2(floor(_S176)) - int2(metadata_0.xy);
            if(any(tile_0 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_0 >= _S182);
            }
            if(hasSceneLighting_0)
            {
                int3 _S183 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S183)).xy), uint(((_S183)).z)));
                break;
            }
            uint _S184 = atlasWidth_0 / uint(_S182.x);
            float _S185 = float(_S184);
            uint _S186 = (atlasHeight_0 - 1U) / uint(_S182.y);
            float2 cellSize_0 = float2(_S185, float(_S186));
            _S177 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        for(;;)
        {
            float4 _S187 = float4(_S6->compositeControls_0) ;
            if((_S187.x) != 2.0f)
            {
                break;
            }
            bool _S188 = (_S187.w) >= 0.5f;
            for(;;)
            {
                if(!_S188)
                {
                    _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S189 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S189).get_width(0)),(*((&atlasHeight_1)) = (_S189).get_height(0));
                int3 _S190 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S190)).xy), uint(((_S190)).z))) * float4(255.0f) );
                int2 _S191 = int2(metadata_1.zw);
                int2 tile_1 = int2(floor(_S176)) - int2(metadata_1.xy);
                if(any(tile_1 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_1 >= _S191);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S192 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S178 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S192)).xy), uint(((_S192)).z)));
                    break;
                }
                uint _S193 = atlasWidth_1 / uint(_S191.x);
                float _S194 = float(_S193);
                uint _S195 = (atlasHeight_1 - 1U) / uint(_S191.y);
                float2 cellSize_1 = float2(_S194, float(_S195));
                _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S187.y));
            if(operation_0 == 1U)
            {
                _S177 = _S177 * _S178;
                break;
            }
            if(operation_0 == 2U)
            {
                _S177 = _S177 + _S178;
                break;
            }
            if(operation_0 == 3U)
            {
                _S177 = _S177 - _S178;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S187.z;
                _S177 = _S177 * float4((1.0f - factor_0))  + _S178 * float4(factor_0) ;
                break;
            }
            break;
        }
        diffuseColor_0 = _S177.xyz;
    }
    bool _S196 = (udimMask_0 & 4U) != 0U;
    for(;;)
    {
        if(!_S196)
        {
            _S177 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (_S176)));
            break;
        }
        texture2d<float, access::sample> _S197 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S197).get_width(0)),(*((&atlasHeight_2)) = (_S197).get_height(0));
        int3 _S198 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S198)).xy), uint(((_S198)).z))) * float4(255.0f) );
        int2 _S199 = int2(metadata_2.zw);
        int2 tile_2 = int2(floor(_S176)) - int2(metadata_2.xy);
        if(any(tile_2 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_2 >= _S199);
        }
        if(hasSceneLighting_0)
        {
            int3 _S200 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S177 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S200)).xy), uint(((_S200)).z)));
            break;
        }
        uint _S201 = atlasWidth_2 / uint(_S199.x);
        float _S202 = float(_S201);
        uint _S203 = (atlasHeight_2 - 1U) / uint(_S199.y);
        float2 cellSize_2 = float2(_S202, float(_S203));
        _S177 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_2) * cellSize_2 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_2 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    float3 _S204 = float3(1.0f) ;
    float3 sampledNormal_0 = _S177.xyz * float3(2.0f)  - _S204;
    float3 tangent_1 = normalize(_S1.tangent_0.xyz);
    float3 _S205 = float3(sampledNormal_0.x) ;
    float3 _S206 = float3(sampledNormal_0.y) ;
    float3 _S207 = float3(sampledNormal_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_1 * _S205 + cross(normalize(_S1.normal_0), tangent_1) * float3(_S1.tangent_0.w)  * _S206 + _S1.normal_0 * _S207);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S205 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S206 + worldNormalBasis_0 * _S207);
    float roughness_0;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S208 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S208)
            {
                _S177 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S209 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S209).get_width(0)),(*((&atlasHeight_3)) = (_S209).get_height(0));
            int3 _S210 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S210)).xy), uint(((_S210)).z))) * float4(255.0f) );
            int2 _S211 = int2(metadata_3.zw);
            int2 tile_3 = int2(floor(_S176)) - int2(metadata_3.xy);
            if(any(tile_3 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_3 >= _S211);
            }
            if(hasSceneLighting_0)
            {
                int3 _S212 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S212)).xy), uint(((_S212)).z)));
                break;
            }
            uint _S213 = atlasWidth_3 / uint(_S211.x);
            float _S214 = float(_S213);
            uint _S215 = (atlasHeight_3 - 1U) / uint(_S211.y);
            float2 cellSize_3 = float2(_S214, float(_S215));
            _S177 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        for(;;)
        {
            float4 _S216 = float4(_S6->compositeControls_0) ;
            if((_S216.x) != 8.0f)
            {
                break;
            }
            bool _S217 = (_S216.w) >= 0.5f;
            for(;;)
            {
                if(!_S217)
                {
                    _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S218 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_4;
                thread uint atlasHeight_4;
                (*((&atlasWidth_4)) = (_S218).get_width(0)),(*((&atlasHeight_4)) = (_S218).get_height(0));
                int3 _S219 = int3(int(0), int(0), int(0));
                float4 metadata_4 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S219)).xy), uint(((_S219)).z))) * float4(255.0f) );
                int2 _S220 = int2(metadata_4.zw);
                int2 tile_4 = int2(floor(_S176)) - int2(metadata_4.xy);
                if(any(tile_4 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_4 >= _S220);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S221 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                    _S178 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S221)).xy), uint(((_S221)).z)));
                    break;
                }
                uint _S222 = atlasWidth_4 / uint(_S220.x);
                float _S223 = float(_S222);
                uint _S224 = (atlasHeight_4 - 1U) / uint(_S220.y);
                float2 cellSize_4 = float2(_S223, float(_S224));
                _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_4) * cellSize_4 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_4 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            uint operation_1 = uint(round(_S216.y));
            if(operation_1 == 1U)
            {
                _S177 = _S177 * _S178;
                break;
            }
            if(operation_1 == 2U)
            {
                _S177 = _S177 + _S178;
                break;
            }
            if(operation_1 == 3U)
            {
                _S177 = _S177 - _S178;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S216.z;
                _S177 = _S177 * float4((1.0f - factor_1))  + _S178 * float4(factor_1) ;
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
        bool _S225 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S225)
            {
                _S177 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S226 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_5;
            thread uint atlasHeight_5;
            (*((&atlasWidth_5)) = (_S226).get_width(0)),(*((&atlasHeight_5)) = (_S226).get_height(0));
            int3 _S227 = int3(int(0), int(0), int(0));
            float4 metadata_5 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S227)).xy), uint(((_S227)).z))) * float4(255.0f) );
            int2 _S228 = int2(metadata_5.zw);
            int2 tile_5 = int2(floor(_S176)) - int2(metadata_5.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S228);
            }
            if(hasSceneLighting_0)
            {
                int3 _S229 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S229)).xy), uint(((_S229)).z)));
                break;
            }
            uint _S230 = atlasWidth_5 / uint(_S228.x);
            float _S231 = float(_S230);
            uint _S232 = (atlasHeight_5 - 1U) / uint(_S228.y);
            float2 cellSize_5 = float2(_S231, float(_S232));
            _S177 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
            break;
        }
        for(;;)
        {
            float4 _S233 = float4(_S6->compositeControls_0) ;
            if((_S233.x) != 32.0f)
            {
                break;
            }
            bool _S234 = (_S233.w) >= 0.5f;
            for(;;)
            {
                if(!_S234)
                {
                    _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S235 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_6;
                thread uint atlasHeight_6;
                (*((&atlasWidth_6)) = (_S235).get_width(0)),(*((&atlasHeight_6)) = (_S235).get_height(0));
                int3 _S236 = int3(int(0), int(0), int(0));
                float4 metadata_6 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S236)).xy), uint(((_S236)).z))) * float4(255.0f) );
                int2 _S237 = int2(metadata_6.zw);
                int2 tile_6 = int2(floor(_S176)) - int2(metadata_6.xy);
                if(any(tile_6 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_6 >= _S237);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S238 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                    _S178 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S238)).xy), uint(((_S238)).z)));
                    break;
                }
                uint _S239 = atlasWidth_6 / uint(_S237.x);
                float _S240 = float(_S239);
                uint _S241 = (atlasHeight_6 - 1U) / uint(_S237.y);
                float2 cellSize_6 = float2(_S240, float(_S241));
                _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_6) * cellSize_6 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_6 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            uint operation_2 = uint(round(_S233.y));
            if(operation_2 == 1U)
            {
                _S177 = _S177 * _S178;
                break;
            }
            if(operation_2 == 2U)
            {
                _S177 = _S177 + _S178;
                break;
            }
            if(operation_2 == 3U)
            {
                _S177 = _S177 - _S178;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S233.z;
                _S177 = _S177 * float4((1.0f - factor_2))  + _S178 * float4(factor_2) ;
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
        bool _S242 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S242)
            {
                _S177 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S243 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_7;
            thread uint atlasHeight_7;
            (*((&atlasWidth_7)) = (_S243).get_width(0)),(*((&atlasHeight_7)) = (_S243).get_height(0));
            int3 _S244 = int3(int(0), int(0), int(0));
            float4 metadata_7 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S244)).xy), uint(((_S244)).z))) * float4(255.0f) );
            int2 _S245 = int2(metadata_7.zw);
            int2 tile_7 = int2(floor(_S176)) - int2(metadata_7.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S245);
            }
            if(hasSceneLighting_0)
            {
                int3 _S246 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S246)).xy), uint(((_S246)).z)));
                break;
            }
            uint _S247 = atlasWidth_7 / uint(_S245.x);
            float _S248 = float(_S247);
            uint _S249 = (atlasHeight_7 - 1U) / uint(_S245.y);
            float2 cellSize_7 = float2(_S248, float(_S249));
            _S177 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
            break;
        }
        for(;;)
        {
            float4 _S250 = float4(_S6->compositeControls_0) ;
            if((_S250.x) != 16.0f)
            {
                break;
            }
            bool _S251 = (_S250.w) >= 0.5f;
            for(;;)
            {
                if(!_S251)
                {
                    _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S252 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_8;
                thread uint atlasHeight_8;
                (*((&atlasWidth_8)) = (_S252).get_width(0)),(*((&atlasHeight_8)) = (_S252).get_height(0));
                int3 _S253 = int3(int(0), int(0), int(0));
                float4 metadata_8 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S253)).xy), uint(((_S253)).z))) * float4(255.0f) );
                int2 _S254 = int2(metadata_8.zw);
                int2 tile_8 = int2(floor(_S176)) - int2(metadata_8.xy);
                if(any(tile_8 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_8 >= _S254);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S255 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                    _S178 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S255)).xy), uint(((_S255)).z)));
                    break;
                }
                uint _S256 = atlasWidth_8 / uint(_S254.x);
                float _S257 = float(_S256);
                uint _S258 = (atlasHeight_8 - 1U) / uint(_S254.y);
                float2 cellSize_8 = float2(_S257, float(_S258));
                _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_8) * cellSize_8 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_8 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            uint operation_3 = uint(round(_S250.y));
            if(operation_3 == 1U)
            {
                _S177 = _S177 * _S178;
                break;
            }
            if(operation_3 == 2U)
            {
                _S177 = _S177 + _S178;
                break;
            }
            if(operation_3 == 3U)
            {
                _S177 = _S177 - _S178;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S250.z;
                _S177 = _S177 * float4((1.0f - factor_3))  + _S178 * float4(factor_3) ;
                break;
            }
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
        bool _S259 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S259)
            {
                _S177 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S260 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_9;
            thread uint atlasHeight_9;
            (*((&atlasWidth_9)) = (_S260).get_width(0)),(*((&atlasHeight_9)) = (_S260).get_height(0));
            int3 _S261 = int3(int(0), int(0), int(0));
            float4 metadata_9 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S261)).xy), uint(((_S261)).z))) * float4(255.0f) );
            int2 _S262 = int2(metadata_9.zw);
            int2 tile_9 = int2(floor(_S176)) - int2(metadata_9.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S262);
            }
            if(hasSceneLighting_0)
            {
                int3 _S263 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S263)).xy), uint(((_S263)).z)));
                break;
            }
            uint _S264 = atlasWidth_9 / uint(_S262.x);
            float _S265 = float(_S264);
            uint _S266 = (atlasHeight_9 - 1U) / uint(_S262.y);
            float2 cellSize_9 = float2(_S265, float(_S266));
            _S177 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
            break;
        }
        for(;;)
        {
            float4 _S267 = float4(_S6->compositeControls_0) ;
            if((_S267.x) != 64.0f)
            {
                break;
            }
            bool _S268 = (_S267.w) >= 0.5f;
            for(;;)
            {
                if(!_S268)
                {
                    _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S269 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_10;
                thread uint atlasHeight_10;
                (*((&atlasWidth_10)) = (_S269).get_width(0)),(*((&atlasHeight_10)) = (_S269).get_height(0));
                int3 _S270 = int3(int(0), int(0), int(0));
                float4 metadata_10 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S270)).xy), uint(((_S270)).z))) * float4(255.0f) );
                int2 _S271 = int2(metadata_10.zw);
                int2 tile_10 = int2(floor(_S176)) - int2(metadata_10.xy);
                if(any(tile_10 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_10 >= _S271);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S272 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                    _S178 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S272)).xy), uint(((_S272)).z)));
                    break;
                }
                uint _S273 = atlasWidth_10 / uint(_S271.x);
                float _S274 = float(_S273);
                uint _S275 = (atlasHeight_10 - 1U) / uint(_S271.y);
                float2 cellSize_10 = float2(_S274, float(_S275));
                _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_10) * cellSize_10 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_10 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            uint operation_4 = uint(round(_S267.y));
            if(operation_4 == 1U)
            {
                _S177 = _S177 * _S178;
                break;
            }
            if(operation_4 == 2U)
            {
                _S177 = _S177 + _S178;
                break;
            }
            if(operation_4 == 3U)
            {
                _S177 = _S177 - _S178;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S267.z;
                _S177 = _S177 * float4((1.0f - factor_4))  + _S178 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S177.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        bool _S276 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S276)
            {
                _S177 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S277 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_11;
            thread uint atlasHeight_11;
            (*((&atlasWidth_11)) = (_S277).get_width(0)),(*((&atlasHeight_11)) = (_S277).get_height(0));
            int3 _S278 = int3(int(0), int(0), int(0));
            float4 metadata_11 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S278)).xy), uint(((_S278)).z))) * float4(255.0f) );
            int2 _S279 = int2(metadata_11.zw);
            int2 tile_11 = int2(floor(_S176)) - int2(metadata_11.xy);
            if(any(tile_11 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_11 >= _S279);
            }
            if(hasSceneLighting_0)
            {
                int3 _S280 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S280)).xy), uint(((_S280)).z)));
                break;
            }
            uint _S281 = atlasWidth_11 / uint(_S279.x);
            float _S282 = float(_S281);
            uint _S283 = (atlasHeight_11 - 1U) / uint(_S279.y);
            float2 cellSize_11 = float2(_S282, float(_S283));
            _S177 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
            break;
        }
        for(;;)
        {
            float4 _S284 = float4(_S6->compositeControls_0) ;
            if((_S284.x) != 128.0f)
            {
                break;
            }
            bool _S285 = (_S284.w) >= 0.5f;
            for(;;)
            {
                if(!_S285)
                {
                    _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S286 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_12;
                thread uint atlasHeight_12;
                (*((&atlasWidth_12)) = (_S286).get_width(0)),(*((&atlasHeight_12)) = (_S286).get_height(0));
                int3 _S287 = int3(int(0), int(0), int(0));
                float4 metadata_12 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S287)).xy), uint(((_S287)).z))) * float4(255.0f) );
                int2 _S288 = int2(metadata_12.zw);
                int2 tile_12 = int2(floor(_S176)) - int2(metadata_12.xy);
                if(any(tile_12 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_12 >= _S288);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S289 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                    _S178 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S289)).xy), uint(((_S289)).z)));
                    break;
                }
                uint _S290 = atlasWidth_12 / uint(_S288.x);
                float _S291 = float(_S290);
                uint _S292 = (atlasHeight_12 - 1U) / uint(_S288.y);
                float2 cellSize_12 = float2(_S291, float(_S292));
                _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_12) * cellSize_12 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_12 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            uint operation_5 = uint(round(_S284.y));
            if(operation_5 == 1U)
            {
                _S177 = _S177 * _S178;
                break;
            }
            if(operation_5 == 2U)
            {
                _S177 = _S177 + _S178;
                break;
            }
            if(operation_5 == 3U)
            {
                _S177 = _S177 - _S178;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S284.z;
                _S177 = _S177 * float4((1.0f - factor_5))  + _S178 * float4(factor_5) ;
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
        bool _S293 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S293)
            {
                _S177 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S294 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_13;
            thread uint atlasHeight_13;
            (*((&atlasWidth_13)) = (_S294).get_width(0)),(*((&atlasHeight_13)) = (_S294).get_height(0));
            int3 _S295 = int3(int(0), int(0), int(0));
            float4 metadata_13 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S295)).xy), uint(((_S295)).z))) * float4(255.0f) );
            int2 _S296 = int2(metadata_13.zw);
            int2 tile_13 = int2(floor(_S176)) - int2(metadata_13.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S296);
            }
            if(hasSceneLighting_0)
            {
                int3 _S297 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S297)).xy), uint(((_S297)).z)));
                break;
            }
            uint _S298 = atlasWidth_13 / uint(_S296.x);
            float _S299 = float(_S298);
            uint _S300 = (atlasHeight_13 - 1U) / uint(_S296.y);
            float2 cellSize_13 = float2(_S299, float(_S300));
            _S177 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
            break;
        }
        for(;;)
        {
            float4 _S301 = float4(_S6->compositeControls_0) ;
            if((_S301.x) != 256.0f)
            {
                break;
            }
            bool _S302 = (_S301.w) >= 0.5f;
            for(;;)
            {
                if(!_S302)
                {
                    _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S303 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_14;
                thread uint atlasHeight_14;
                (*((&atlasWidth_14)) = (_S303).get_width(0)),(*((&atlasHeight_14)) = (_S303).get_height(0));
                int3 _S304 = int3(int(0), int(0), int(0));
                float4 metadata_14 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S304)).xy), uint(((_S304)).z))) * float4(255.0f) );
                int2 _S305 = int2(metadata_14.zw);
                int2 tile_14 = int2(floor(_S176)) - int2(metadata_14.xy);
                if(any(tile_14 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_14 >= _S305);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S306 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                    _S178 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S306)).xy), uint(((_S306)).z)));
                    break;
                }
                uint _S307 = atlasWidth_14 / uint(_S305.x);
                float _S308 = float(_S307);
                uint _S309 = (atlasHeight_14 - 1U) / uint(_S305.y);
                float2 cellSize_14 = float2(_S308, float(_S309));
                _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_14) * cellSize_14 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_14 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            uint operation_6 = uint(round(_S301.y));
            if(operation_6 == 1U)
            {
                _S177 = _S177 * _S178;
                break;
            }
            if(operation_6 == 2U)
            {
                _S177 = _S177 + _S178;
                break;
            }
            if(operation_6 == 3U)
            {
                _S177 = _S177 - _S178;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S301.z;
                _S177 = _S177 * float4((1.0f - factor_6))  + _S178 * float4(factor_6) ;
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
        bool _S310 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S310)
            {
                _S177 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S311 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_15;
            thread uint atlasHeight_15;
            (*((&atlasWidth_15)) = (_S311).get_width(0)),(*((&atlasHeight_15)) = (_S311).get_height(0));
            int3 _S312 = int3(int(0), int(0), int(0));
            float4 metadata_15 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S312)).xy), uint(((_S312)).z))) * float4(255.0f) );
            int2 _S313 = int2(metadata_15.zw);
            int2 tile_15 = int2(floor(_S176)) - int2(metadata_15.xy);
            if(any(tile_15 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_15 >= _S313);
            }
            if(hasSceneLighting_0)
            {
                int3 _S314 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S314)).xy), uint(((_S314)).z)));
                break;
            }
            uint _S315 = atlasWidth_15 / uint(_S313.x);
            float _S316 = float(_S315);
            uint _S317 = (atlasHeight_15 - 1U) / uint(_S313.y);
            float2 cellSize_15 = float2(_S316, float(_S317));
            _S177 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
            break;
        }
        for(;;)
        {
            float4 _S318 = float4(_S6->compositeControls_0) ;
            if((_S318.x) != 512.0f)
            {
                break;
            }
            bool _S319 = (_S318.w) >= 0.5f;
            for(;;)
            {
                if(!_S319)
                {
                    _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S320 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_16;
                thread uint atlasHeight_16;
                (*((&atlasWidth_16)) = (_S320).get_width(0)),(*((&atlasHeight_16)) = (_S320).get_height(0));
                int3 _S321 = int3(int(0), int(0), int(0));
                float4 metadata_16 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S321)).xy), uint(((_S321)).z))) * float4(255.0f) );
                int2 _S322 = int2(metadata_16.zw);
                int2 tile_16 = int2(floor(_S176)) - int2(metadata_16.xy);
                if(any(tile_16 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_16 >= _S322);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S323 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                    _S178 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S323)).xy), uint(((_S323)).z)));
                    break;
                }
                uint _S324 = atlasWidth_16 / uint(_S322.x);
                float _S325 = float(_S324);
                uint _S326 = (atlasHeight_16 - 1U) / uint(_S322.y);
                float2 cellSize_16 = float2(_S325, float(_S326));
                _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_16) * cellSize_16 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_16 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            uint operation_7 = uint(round(_S318.y));
            if(operation_7 == 1U)
            {
                _S177 = _S177 * _S178;
                break;
            }
            if(operation_7 == 2U)
            {
                _S177 = _S177 + _S178;
                break;
            }
            if(operation_7 == 3U)
            {
                _S177 = _S177 - _S178;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S318.z;
                _S177 = _S177 * float4((1.0f - factor_7))  + _S178 * float4(factor_7) ;
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
        bool _S327 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S327)
            {
                _S177 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S328 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_17;
            thread uint atlasHeight_17;
            (*((&atlasWidth_17)) = (_S328).get_width(0)),(*((&atlasHeight_17)) = (_S328).get_height(0));
            int3 _S329 = int3(int(0), int(0), int(0));
            float4 metadata_17 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S329)).xy), uint(((_S329)).z))) * float4(255.0f) );
            int2 _S330 = int2(metadata_17.zw);
            int2 tile_17 = int2(floor(_S176)) - int2(metadata_17.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S330);
            }
            if(hasSceneLighting_0)
            {
                int3 _S331 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S331)).xy), uint(((_S331)).z)));
                break;
            }
            uint _S332 = atlasWidth_17 / uint(_S330.x);
            float _S333 = float(_S332);
            uint _S334 = (atlasHeight_17 - 1U) / uint(_S330.y);
            float2 cellSize_17 = float2(_S333, float(_S334));
            _S177 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
            break;
        }
        for(;;)
        {
            float4 _S335 = float4(_S6->compositeControls_0) ;
            if((_S335.x) != 1024.0f)
            {
                break;
            }
            bool _S336 = (_S335.w) >= 0.5f;
            for(;;)
            {
                if(!_S336)
                {
                    _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S337 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_18;
                thread uint atlasHeight_18;
                (*((&atlasWidth_18)) = (_S337).get_width(0)),(*((&atlasHeight_18)) = (_S337).get_height(0));
                int3 _S338 = int3(int(0), int(0), int(0));
                float4 metadata_18 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S338)).xy), uint(((_S338)).z))) * float4(255.0f) );
                int2 _S339 = int2(metadata_18.zw);
                int2 tile_18 = int2(floor(_S176)) - int2(metadata_18.xy);
                if(any(tile_18 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_18 >= _S339);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S340 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                    _S178 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S340)).xy), uint(((_S340)).z)));
                    break;
                }
                uint _S341 = atlasWidth_18 / uint(_S339.x);
                float _S342 = float(_S341);
                uint _S343 = (atlasHeight_18 - 1U) / uint(_S339.y);
                float2 cellSize_18 = float2(_S342, float(_S343));
                _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_18) * cellSize_18 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_18 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            uint operation_8 = uint(round(_S335.y));
            if(operation_8 == 1U)
            {
                _S177 = _S177 * _S178;
                break;
            }
            if(operation_8 == 2U)
            {
                _S177 = _S177 + _S178;
                break;
            }
            if(operation_8 == 3U)
            {
                _S177 = _S177 - _S178;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S335.z;
                _S177 = _S177 * float4((1.0f - factor_8))  + _S178 * float4(factor_8) ;
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
        bool _S344 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S344)
            {
                _S177 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S176)));
                break;
            }
            texture2d<float, access::sample> _S345 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_19;
            thread uint atlasHeight_19;
            (*((&atlasWidth_19)) = (_S345).get_width(0)),(*((&atlasHeight_19)) = (_S345).get_height(0));
            int3 _S346 = int3(int(0), int(0), int(0));
            float4 metadata_19 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S346)).xy), uint(((_S346)).z))) * float4(255.0f) );
            int2 _S347 = int2(metadata_19.zw);
            int2 tile_19 = int2(floor(_S176)) - int2(metadata_19.xy);
            if(any(tile_19 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_19 >= _S347);
            }
            if(hasSceneLighting_0)
            {
                int3 _S348 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                _S177 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S348)).xy), uint(((_S348)).z)));
                break;
            }
            uint _S349 = atlasWidth_19 / uint(_S347.x);
            float _S350 = float(_S349);
            uint _S351 = (atlasHeight_19 - 1U) / uint(_S347.y);
            float2 cellSize_19 = float2(_S350, float(_S351));
            _S177 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
            break;
        }
        for(;;)
        {
            float4 _S352 = float4(_S6->compositeControls_0) ;
            if((_S352.x) != 2048.0f)
            {
                break;
            }
            bool _S353 = (_S352.w) >= 0.5f;
            for(;;)
            {
                if(!_S353)
                {
                    _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S176)));
                    break;
                }
                texture2d<float, access::sample> _S354 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_20;
                thread uint atlasHeight_20;
                (*((&atlasWidth_20)) = (_S354).get_width(0)),(*((&atlasHeight_20)) = (_S354).get_height(0));
                int3 _S355 = int3(int(0), int(0), int(0));
                float4 metadata_20 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S355)).xy), uint(((_S355)).z))) * float4(255.0f) );
                int2 _S356 = int2(metadata_20.zw);
                int2 tile_20 = int2(floor(_S176)) - int2(metadata_20.xy);
                if(any(tile_20 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_20 >= _S356);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S357 = int3(int(min(1U, atlasWidth_20 - 1U)), int(0), int(0));
                    _S178 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S357)).xy), uint(((_S357)).z)));
                    break;
                }
                uint _S358 = atlasWidth_20 / uint(_S356.x);
                float _S359 = float(_S358);
                uint _S360 = (atlasHeight_20 - 1U) / uint(_S356.y);
                float2 cellSize_20 = float2(_S359, float(_S360));
                _S178 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_20) * cellSize_20 + float2(1.5f, 2.5f) + fract(_S176) * max(cellSize_20 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
                break;
            }
            uint operation_9 = uint(round(_S352.y));
            if(operation_9 == 1U)
            {
                _S177 = _S177 * _S178;
                break;
            }
            if(operation_9 == 2U)
            {
                _S177 = _S177 + _S178;
                break;
            }
            if(operation_9 == 3U)
            {
                _S177 = _S177 - _S178;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S352.z;
                _S177 = _S177 * float4((1.0f - factor_9))  + _S178 * float4(factor_9) ;
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
        hasSceneLighting_0 = opacity_0 < opacityThreshold_0;
    }
    else
    {
        hasSceneLighting_0 = false;
    }
    if(hasSceneLighting_0)
    {
        discard_fragment();
    }
    float3 normal_1 = normalize(shadingNormal_0);
    float3 worldNormal_1 = normalize(worldShadingNormal_0);
    float lengthSquared_0 = dot(_S1.eyePosition_0, _S1.eyePosition_0);
    float3 irradiance_0;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        irradiance_0 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        irradiance_0 = float3(0.0f, 0.0f, 1.0f);
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
    float _S361 = saturate(abs(dot(normal_2, irradiance_0)) + 0.00000999999974738f);
    float _S362 = max(0.00100000004749745f, roughness_0);
    float _S363 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S364 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S364;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S165.w) >= 0.5f)
    {
        float3 _S365 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S365;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S366 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S366);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S366);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    FragmentFrameParameters_0 _S367 = _S2;
    float _S368 = _S2.ambientLight_0.w;
    if(_S368 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S369 = _S367.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S369, _S369)) > 0.0f;
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
    uint _S370 = min(uint(_S368), 128U);
    float3 sceneEye_0 = normalize((((float4(irradiance_0, 0.0f)) * (_S2.eyeToWorld_0))).xyz);
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
    float4 _S371 = float4(_S6->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S371.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S6->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S372 = _S2;
    uint _S373 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S373) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S374 = _S367.ambientLight_0.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S375 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S375;
        for(;;)
        {
            if(lightCount_0 < _S373)
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
        domeAmbient_1 = _S374;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S376 = !hasSceneLighting_0;
    if(_S376)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S370;
    }
    uint lightIndex_0 = 0U;
    float3 color_2 = color_1;
    for(;;)
    {
        if(lightIndex_0 < lightCount_0)
        {
        }
        else
        {
            break;
        }
        bool _S377;
        if(hasSceneLighting_0)
        {
            _S377 = (((uint4(_S6->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S377 = false;
        }
        if(_S377)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S378 = lightIndex_0 == 0U;
        bool _S379;
        if(_S378)
        {
            _S379 = _S376;
        }
        else
        {
            _S379 = false;
        }
        float lightType_0;
        if(_S379)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S380 = _S2.lightPositionType_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S381 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S380)>>2]);
            float _S382 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S380 + 4U)>>2]);
            float _S383 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S380 + 8U)>>2]);
            float _S384 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S380 + 12U)>>2]);
            lightType_0 = _S384;
        }
        bool _S385;
        if(_S378)
        {
            _S385 = _S376;
        }
        else
        {
            _S385 = false;
        }
        if(_S385)
        {
            lightDirection_0 = normalize((float4(_S6->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S386 = _S2.lightDirectionRadius_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S387 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S386)>>2]);
            float _S388 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S386 + 4U)>>2]);
            float _S389 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S386 + 8U)>>2]);
            float _S390 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S386 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S387, _S388, _S389));
        }
        bool _S391;
        if(_S378)
        {
            _S391 = _S376;
        }
        else
        {
            _S391 = false;
        }
        if(_S391)
        {
            roughness_0 = (float4(_S6->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S392 = _S2.lightColorIntensity_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S393 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S392)>>2]);
            float _S394 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S392 + 4U)>>2]);
            float _S395 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S392 + 8U)>>2]);
            float _S396 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S392 + 12U)>>2]);
            roughness_0 = _S396;
        }
        bool _S397;
        if(_S378)
        {
            _S397 = _S376;
        }
        else
        {
            _S397 = false;
        }
        if(_S397)
        {
            diffuseColor_0 = _S371.xyz;
        }
        else
        {
            uint _S398 = _S2.lightColorIntensity_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S399 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S398)>>2]);
            float _S400 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S398 + 4U)>>2]);
            float _S401 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S398 + 8U)>>2]);
            float _S402 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S398 + 12U)>>2]);
            diffuseColor_0 = float3(_S399, _S400, _S401);
        }
        bool _S403;
        if(_S378)
        {
            _S403 = _S376;
        }
        else
        {
            _S403 = false;
        }
        if(_S403)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S404 = _S2.lightControls_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S405 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S404)>>2]);
            float _S406 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S404 + 4U)>>2]);
            float _S407 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S404 + 8U)>>2]);
            float _S408 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S404 + 12U)>>2]);
            metallic_0 = _S405;
        }
        bool _S409;
        if(_S378)
        {
            _S409 = _S376;
        }
        else
        {
            _S409 = false;
        }
        if(_S409)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S410 = _S2.lightControls_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S411 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410)>>2]);
            float _S412 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410 + 4U)>>2]);
            float _S413 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410 + 8U)>>2]);
            float _S414 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410 + 12U)>>2]);
            clearcoatRoughness_0 = _S412;
        }
        bool _S415;
        if(_S378)
        {
            _S415 = _S376;
        }
        else
        {
            _S415 = false;
        }
        if(_S415)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S416 = _S2.lightTangentShapeX_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S417 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416)>>2]);
            float _S418 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416 + 4U)>>2]);
            float _S419 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416 + 8U)>>2]);
            float _S420 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S417, _S418, _S419));
        }
        bool _S421;
        if(_S378)
        {
            _S421 = _S376;
        }
        else
        {
            _S421 = false;
        }
        if(_S421)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S422 = _S2.lightBitangentShapeY_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S423 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S422)>>2]);
            float _S424 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S422 + 4U)>>2]);
            float _S425 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S422 + 8U)>>2]);
            float _S426 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S422 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S423, _S424, _S425));
        }
        bool _S427;
        if(_S378)
        {
            _S427 = _S376;
        }
        else
        {
            _S427 = false;
        }
        float shapeX_0;
        if(_S427)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S428 = _S2.lightTangentShapeX_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S429 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S428)>>2]);
            float _S430 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S428 + 4U)>>2]);
            float _S431 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S428 + 8U)>>2]);
            float _S432 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S428 + 12U)>>2]);
            shapeX_0 = _S432;
        }
        bool _S433;
        if(_S378)
        {
            _S433 = _S376;
        }
        else
        {
            _S433 = false;
        }
        float shapeY_0;
        if(_S433)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S434 = _S2.lightBitangentShapeY_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S435 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S434)>>2]);
            float _S436 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S434 + 4U)>>2]);
            float _S437 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S434 + 8U)>>2]);
            float _S438 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S434 + 12U)>>2]);
            shapeY_0 = _S438;
        }
        bool _S439;
        if(_S378)
        {
            _S439 = _S376;
        }
        else
        {
            _S439 = false;
        }
        float lightRadius_0;
        if(_S439)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S440 = _S2.lightDirectionRadius_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S441 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S440)>>2]);
            float _S442 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S440 + 4U)>>2]);
            float _S443 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S440 + 8U)>>2]);
            float _S444 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S440 + 12U)>>2]);
            lightRadius_0 = _S444;
        }
        bool _S445;
        if(_S378)
        {
            _S445 = _S376;
        }
        else
        {
            _S445 = false;
        }
        if(_S445)
        {
            domeAmbient_1 = irradiance_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        float3 color_3;
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S446 = _S2.shadowSlots_0.byteOffset_0 + lightIndex_0 * 16U;
            float _S447 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S446)>>2]);
            float _S448 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S446 + 4U)>>2]);
            float _S449 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S446 + 8U)>>2]);
            float _S450 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S446 + 12U)>>2]);
            int shadowSlot_0 = int(_S447);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S451 = _S2;
                    FragmentFrameParameters_0 _S452 = _S2;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_3 = - specularColor_0;
                    }
                    else
                    {
                        color_3 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_3, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_3 * float3((_S451.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S453 = lightClip_0.w;
                    if(_S453 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S453) ;
                    bool _S454;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S454 = true;
                    }
                    else
                    {
                        _S454 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S455;
                    if(_S454)
                    {
                        _S455 = true;
                    }
                    else
                    {
                        _S455 = (ndc_0.z) < 0.0f;
                    }
                    bool _S456;
                    if(_S455)
                    {
                        _S456 = true;
                    }
                    else
                    {
                        _S456 = (ndc_0.z) > 1.0f;
                    }
                    if(_S456)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S457 = _S452.shadowTile_0[shadowSlot_0].xy;
                    float2 _S458 = _S452.shadowTile_0[shadowSlot_0].zw;
                    float2 _S459 = _S457 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S458;
                    float texel_0 = _S451.shadowControls_0[shadowSlot_0].w;
                    float _S460 = max(_S451.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S461 = ndc_0.z - _S451.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S462 = float2((texel_0 * 0.5f)) ;
                    float2 _S463 = _S457 + _S462;
                    float2 _S464 = _S457 + _S458 - _S462;
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
                            if(_S461 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S459 + float2(float(x_0), float(y_0)) * float2((_S460 * texel_0)) , _S463, _S464)), level((0.0f))).x))
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
        float3 _S465 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S465;
        sampleOffsets_0[int(1)] = _S465;
        sampleOffsets_0[int(2)] = _S465;
        sampleOffsets_0[int(3)] = _S465;
        sampleOffsets_0[int(4)] = _S465;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S466 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S466 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S466 - halfHeight_0;
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
        color_3 = color_2;
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
                uint _S467 = _S2.lightPositionType_0.byteOffset_0 + lightIndex_0 * 16U;
                float _S468 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467)>>2]);
                float _S469 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 4U)>>2]);
                float _S470 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 8U)>>2]);
                float _S471 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 12U)>>2]);
                float3 toLight_0 = float3(_S468, _S469, _S470) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S472 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S472)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S472;
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
            float3 _S473 = float3(pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f)) ;
            float3 _S474 = mix(normalIncidence_0, grazingIncidence_0, _S473);
            float3 directDiffuse_0 = diffuse_1 * (_S204 - _S474);
            float _S475 = max(_S362, 0.00100000004749745f);
            float alpha_0 = _S475 * _S475;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S476 = 1.0f - k_0;
            float3 _S477 = float3(max(4.0f * normalDotLight_0 * _S361, 1.00000000317107685e-30f)) ;
            float3 _S478 = _S474 * float3((_S361 / (_S361 * _S476 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S476 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S477;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S479 = max(_S363, 0.00100000004749745f);
                float alpha_1 = _S479 * _S479;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S480 = 1.0f - k_1;
                directSpecular_0 = _S478 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S473) * float3((_S361 / (_S361 * _S480 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S480 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S477);
            }
            else
            {
                directSpecular_0 = _S478;
            }
            float3 _S481 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S481 * _S364) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S481);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S482 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S483 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S484 = saturate(_S362);
        float2 _S485 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S483, _S484)), level((0.0f)))).xy;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S485.x)  + grazingIncidence_0 * float3(_S485.y) ;
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - specularWeight_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S486 = max(_S372.domeControls_0.y, 1.0f);
        float3 _S487 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S486 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S372.domeControls_0.z;
            float _S488 = _S372.domeControls_0.w;
            for(;;)
            {
                bool _S489 = _S486 <= 1.0f;
                _S4 = _S489;
                if(_S489)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S488, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S486)), level((0.0f)))).xyz;
                break;
            }
            float _S490 = _S482.environmentControls_0.y;
            float _S491 = _S482.environmentControls_0.z;
            for(;;)
            {
                if(_S4)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S492 = max(_S490, 1.0f);
                    float _S493 = _S492 - 1.0f;
                    float slice_0 = _S484 * max(_S493, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S491, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S492)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S493) + v_0) / _S492)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S494 = max(_S490, 1.0f);
                float total_0 = _S494 * _S486;
                float _S495 = _S494 - 1.0f;
                float slice_1 = _S484 * max(_S495, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S491, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S494;
                specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S495) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S496 = max(_S490, 1.0f);
                        float _S497 = _S496 - 1.0f;
                        float slice_2 = saturate(_S363) * max(_S497, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S491, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S496)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S497) + v_2) / _S496)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S498 = max(_S490, 1.0f);
                    float total_1 = _S498 * _S486;
                    float _S499 = _S498 - 1.0f;
                    float slice_3 = saturate(_S363) * max(_S499, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S491, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S498;
                    irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S499) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightTangent_0 = irradiance_0;
            }
            else
            {
                lightTangent_0 = _S487;
            }
            irradiance_0 = diffuseColor_0;
            lightDirection_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S487;
            lightDirection_0 = _S487;
            lightTangent_0 = _S487;
            for(;;)
            {
                if(sampleIndex_0 < _S373)
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
                float _S500 = _S372.domeControls_0.w;
                for(;;)
                {
                    bool _S501 = _S486 <= 1.0f;
                    _S5 = _S501;
                    if(_S501)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S500, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S486)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                float _S502 = _S482.environmentControls_0.y;
                float _S503 = _S482.environmentControls_0.z;
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S504 = max(_S502, 1.0f);
                        float _S505 = _S504 - 1.0f;
                        float slice_4 = _S484 * max(_S505, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S503, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S504)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S505) + v_4) / _S504)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S506 = max(_S502, 1.0f);
                    float total_2 = _S506 * _S486;
                    float _S507 = _S506 - 1.0f;
                    float slice_5 = _S484 * max(_S507, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S503, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S506;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S507) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightDirection_0 + specularColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S5)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S508 = max(_S502, 1.0f);
                            float _S509 = _S508 - 1.0f;
                            float slice_6 = saturate(_S363) * max(_S509, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S503, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S508)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S509) + v_6) / _S508)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S510 = max(_S502, 1.0f);
                        float total_3 = _S510 * _S486;
                        float _S511 = _S510 - 1.0f;
                        float slice_7 = saturate(_S363) * max(_S511, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S503, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S510;
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S511) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    lightBitangent_0 = lightTangent_0 + normal_2;
                }
                else
                {
                    lightBitangent_0 = lightTangent_0;
                }
                irradiance_0 = irradiance_1;
                lightDirection_0 = prefiltered_0;
                lightTangent_0 = lightBitangent_0;
                sampleIndex_0 = sampleIndex_0 + 1U;
            }
        }
        float3 color_4 = color_2 + float3(occlusion_0)  * diffuse_1 * irradiance_0 * diffuseWeight_0 + float3(occlusion_0)  * lightDirection_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S512 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S483, saturate(_S363))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightTangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S512.x)  + float3(_S512.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + unlitColor_0) * float3(exp2((as_type<float>((_S3.z))))) ;
    if((_S3.y) == 1U)
    {
        color_2 = color_5 / (_S204 + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S513 = { float4(color_2, opacity_0) };
    return _S513;
}
