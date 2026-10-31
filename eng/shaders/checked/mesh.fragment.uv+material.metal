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
    float _S4;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    float3 color_0;
    bool _S5;
    bool _S6;
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
    SurfaceParameters_natural_0 device* _S7 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        uint _S8 = as_type<uint>((&kernelContext_0)->frameParameters_0[(64U)>>2]);
        uint _S9 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
        uint _S10 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
        uint _S11 = as_type<uint>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S8, _S9, _S10, _S11);
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
            uint _S12 = 80U + index_0 * 16U;
            float _S13 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S12)>>2]);
            float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S12 + 4U)>>2]);
            float _S15 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S12 + 8U)>>2]);
            float _S16 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S12 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_0] = float4(_S13, _S14, _S15, _S16);
            index_0 = index_0 + 1U;
        }
        float _S17 = as_type<float>((&kernelContext_0)->frameParameters_0[(208U)>>2]);
        float _S18 = as_type<float>((&kernelContext_0)->frameParameters_0[(212U)>>2]);
        float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(216U)>>2]);
        float _S20 = as_type<float>((&kernelContext_0)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S17, _S18, _S19, _S20);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 12960U;
        float _S21 = as_type<float>((&kernelContext_0)->frameParameters_0[(12512U)>>2]);
        float _S22 = as_type<float>((&kernelContext_0)->frameParameters_0[(12516U)>>2]);
        float _S23 = as_type<float>((&kernelContext_0)->frameParameters_0[(12520U)>>2]);
        float _S24 = as_type<float>((&kernelContext_0)->frameParameters_0[(12524U)>>2]);
        float4 _S25 = float4(_S21, _S22, _S23, _S24);
        float _S26 = as_type<float>((&kernelContext_0)->frameParameters_0[(12528U)>>2]);
        float _S27 = as_type<float>((&kernelContext_0)->frameParameters_0[(12532U)>>2]);
        float _S28 = as_type<float>((&kernelContext_0)->frameParameters_0[(12536U)>>2]);
        float _S29 = as_type<float>((&kernelContext_0)->frameParameters_0[(12540U)>>2]);
        float4 _S30 = float4(_S26, _S27, _S28, _S29);
        float _S31 = as_type<float>((&kernelContext_0)->frameParameters_0[(12544U)>>2]);
        float _S32 = as_type<float>((&kernelContext_0)->frameParameters_0[(12548U)>>2]);
        float _S33 = as_type<float>((&kernelContext_0)->frameParameters_0[(12552U)>>2]);
        float _S34 = as_type<float>((&kernelContext_0)->frameParameters_0[(12556U)>>2]);
        float4 _S35 = float4(_S31, _S32, _S33, _S34);
        float _S36 = as_type<float>((&kernelContext_0)->frameParameters_0[(12560U)>>2]);
        float _S37 = as_type<float>((&kernelContext_0)->frameParameters_0[(12564U)>>2]);
        float _S38 = as_type<float>((&kernelContext_0)->frameParameters_0[(12568U)>>2]);
        float _S39 = as_type<float>((&kernelContext_0)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S25, _S30, _S35, float4(_S36, _S37, _S38, _S39));
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
            uint _S40 = 12576U + index_0 * 64U;
            float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40)>>2]);
            float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 4U)>>2]);
            float _S43 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 8U)>>2]);
            float _S44 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 12U)>>2]);
            float4 _S45 = float4(_S41, _S42, _S43, _S44);
            float _S46 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 16U)>>2]);
            float _S47 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 20U)>>2]);
            float _S48 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 24U)>>2]);
            float _S49 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 28U)>>2]);
            float4 _S50 = float4(_S46, _S47, _S48, _S49);
            float _S51 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 32U)>>2]);
            float _S52 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 36U)>>2]);
            float _S53 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 40U)>>2]);
            float _S54 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 44U)>>2]);
            float4 _S55 = float4(_S51, _S52, _S53, _S54);
            float _S56 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 48U)>>2]);
            float _S57 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 52U)>>2]);
            float _S58 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 56U)>>2]);
            float _S59 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 60U)>>2]);
            (&frame_0)->shadowWorldToLightClip_0[index_0] = matrix<float,int(4),int(4)> (_S45, _S50, _S55, float4(_S56, _S57, _S58, _S59));
            uint _S60 = index_0 * 16U;
            uint _S61 = 12832U + _S60;
            float _S62 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S61)>>2]);
            float _S63 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S61 + 4U)>>2]);
            float _S64 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S61 + 8U)>>2]);
            float _S65 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S61 + 12U)>>2]);
            (&frame_0)->shadowTile_0[index_0] = float4(_S62, _S63, _S64, _S65);
            uint _S66 = 12896U + _S60;
            float _S67 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S66)>>2]);
            float _S68 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S66 + 4U)>>2]);
            float _S69 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S66 + 8U)>>2]);
            float _S70 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S66 + 12U)>>2]);
            (&frame_0)->shadowControls_0[index_0] = float4(_S67, _S68, _S69, _S70);
            index_0 = index_0 + 1U;
        }
        float _S71 = as_type<float>((&kernelContext_0)->frameParameters_0[(15008U)>>2]);
        float _S72 = as_type<float>((&kernelContext_0)->frameParameters_0[(15012U)>>2]);
        float _S73 = as_type<float>((&kernelContext_0)->frameParameters_0[(15016U)>>2]);
        float _S74 = as_type<float>((&kernelContext_0)->frameParameters_0[(15020U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S71, _S72, _S73, _S74);
        float _S75 = as_type<float>((&kernelContext_0)->frameParameters_0[(15024U)>>2]);
        float _S76 = as_type<float>((&kernelContext_0)->frameParameters_0[(15028U)>>2]);
        float _S77 = as_type<float>((&kernelContext_0)->frameParameters_0[(15032U)>>2]);
        float _S78 = as_type<float>((&kernelContext_0)->frameParameters_0[(15036U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S75, _S76, _S77, _S78);
        float _S79 = as_type<float>((&kernelContext_0)->frameParameters_0[(15040U)>>2]);
        float _S80 = as_type<float>((&kernelContext_0)->frameParameters_0[(15044U)>>2]);
        float _S81 = as_type<float>((&kernelContext_0)->frameParameters_0[(15048U)>>2]);
        float _S82 = as_type<float>((&kernelContext_0)->frameParameters_0[(15052U)>>2]);
        float4 _S83 = float4(_S79, _S80, _S81, _S82);
        float _S84 = as_type<float>((&kernelContext_0)->frameParameters_0[(15056U)>>2]);
        float _S85 = as_type<float>((&kernelContext_0)->frameParameters_0[(15060U)>>2]);
        float _S86 = as_type<float>((&kernelContext_0)->frameParameters_0[(15064U)>>2]);
        float _S87 = as_type<float>((&kernelContext_0)->frameParameters_0[(15068U)>>2]);
        float4 _S88 = float4(_S84, _S85, _S86, _S87);
        float _S89 = as_type<float>((&kernelContext_0)->frameParameters_0[(15072U)>>2]);
        float _S90 = as_type<float>((&kernelContext_0)->frameParameters_0[(15076U)>>2]);
        float _S91 = as_type<float>((&kernelContext_0)->frameParameters_0[(15080U)>>2]);
        float _S92 = as_type<float>((&kernelContext_0)->frameParameters_0[(15084U)>>2]);
        float4 _S93 = float4(_S89, _S90, _S91, _S92);
        float _S94 = as_type<float>((&kernelContext_0)->frameParameters_0[(15088U)>>2]);
        float _S95 = as_type<float>((&kernelContext_0)->frameParameters_0[(15092U)>>2]);
        float _S96 = as_type<float>((&kernelContext_0)->frameParameters_0[(15096U)>>2]);
        float _S97 = as_type<float>((&kernelContext_0)->frameParameters_0[(15100U)>>2]);
        float4 _S98 = float4(_S94, _S95, _S96, _S97);
        float _S99 = as_type<float>((&kernelContext_0)->frameParameters_0[(15104U)>>2]);
        float _S100 = as_type<float>((&kernelContext_0)->frameParameters_0[(15108U)>>2]);
        float _S101 = as_type<float>((&kernelContext_0)->frameParameters_0[(15112U)>>2]);
        float _S102 = as_type<float>((&kernelContext_0)->frameParameters_0[(15116U)>>2]);
        float4 _S103 = float4(_S99, _S100, _S101, _S102);
        float _S104 = as_type<float>((&kernelContext_0)->frameParameters_0[(15120U)>>2]);
        float _S105 = as_type<float>((&kernelContext_0)->frameParameters_0[(15124U)>>2]);
        float _S106 = as_type<float>((&kernelContext_0)->frameParameters_0[(15128U)>>2]);
        float _S107 = as_type<float>((&kernelContext_0)->frameParameters_0[(15132U)>>2]);
        float4 _S108 = float4(_S104, _S105, _S106, _S107);
        float _S109 = as_type<float>((&kernelContext_0)->frameParameters_0[(15136U)>>2]);
        float _S110 = as_type<float>((&kernelContext_0)->frameParameters_0[(15140U)>>2]);
        float _S111 = as_type<float>((&kernelContext_0)->frameParameters_0[(15144U)>>2]);
        float _S112 = as_type<float>((&kernelContext_0)->frameParameters_0[(15148U)>>2]);
        float4 _S113 = float4(_S109, _S110, _S111, _S112);
        float _S114 = as_type<float>((&kernelContext_0)->frameParameters_0[(15152U)>>2]);
        float _S115 = as_type<float>((&kernelContext_0)->frameParameters_0[(15156U)>>2]);
        float _S116 = as_type<float>((&kernelContext_0)->frameParameters_0[(15160U)>>2]);
        float _S117 = as_type<float>((&kernelContext_0)->frameParameters_0[(15164U)>>2]);
        float4 _S118 = float4(_S114, _S115, _S116, _S117);
        float _S119 = as_type<float>((&kernelContext_0)->frameParameters_0[(15168U)>>2]);
        float _S120 = as_type<float>((&kernelContext_0)->frameParameters_0[(15172U)>>2]);
        float _S121 = as_type<float>((&kernelContext_0)->frameParameters_0[(15176U)>>2]);
        float _S122 = as_type<float>((&kernelContext_0)->frameParameters_0[(15180U)>>2]);
        float4 _S123 = float4(_S119, _S120, _S121, _S122);
        float _S124 = as_type<float>((&kernelContext_0)->frameParameters_0[(15184U)>>2]);
        float _S125 = as_type<float>((&kernelContext_0)->frameParameters_0[(15188U)>>2]);
        float _S126 = as_type<float>((&kernelContext_0)->frameParameters_0[(15192U)>>2]);
        float _S127 = as_type<float>((&kernelContext_0)->frameParameters_0[(15196U)>>2]);
        float4 _S128 = float4(_S124, _S125, _S126, _S127);
        float _S129 = as_type<float>((&kernelContext_0)->frameParameters_0[(15200U)>>2]);
        float _S130 = as_type<float>((&kernelContext_0)->frameParameters_0[(15204U)>>2]);
        float _S131 = as_type<float>((&kernelContext_0)->frameParameters_0[(15208U)>>2]);
        float _S132 = as_type<float>((&kernelContext_0)->frameParameters_0[(15212U)>>2]);
        float4 _S133 = float4(_S129, _S130, _S131, _S132);
        float _S134 = as_type<float>((&kernelContext_0)->frameParameters_0[(15216U)>>2]);
        float _S135 = as_type<float>((&kernelContext_0)->frameParameters_0[(15220U)>>2]);
        float _S136 = as_type<float>((&kernelContext_0)->frameParameters_0[(15224U)>>2]);
        float _S137 = as_type<float>((&kernelContext_0)->frameParameters_0[(15228U)>>2]);
        float4 _S138 = float4(_S134, _S135, _S136, _S137);
        float _S139 = as_type<float>((&kernelContext_0)->frameParameters_0[(15232U)>>2]);
        float _S140 = as_type<float>((&kernelContext_0)->frameParameters_0[(15236U)>>2]);
        float _S141 = as_type<float>((&kernelContext_0)->frameParameters_0[(15240U)>>2]);
        float _S142 = as_type<float>((&kernelContext_0)->frameParameters_0[(15244U)>>2]);
        float4 _S143 = float4(_S139, _S140, _S141, _S142);
        float _S144 = as_type<float>((&kernelContext_0)->frameParameters_0[(15248U)>>2]);
        float _S145 = as_type<float>((&kernelContext_0)->frameParameters_0[(15252U)>>2]);
        float _S146 = as_type<float>((&kernelContext_0)->frameParameters_0[(15256U)>>2]);
        float _S147 = as_type<float>((&kernelContext_0)->frameParameters_0[(15260U)>>2]);
        float4 _S148 = float4(_S144, _S145, _S146, _S147);
        float _S149 = as_type<float>((&kernelContext_0)->frameParameters_0[(15264U)>>2]);
        float _S150 = as_type<float>((&kernelContext_0)->frameParameters_0[(15268U)>>2]);
        float _S151 = as_type<float>((&kernelContext_0)->frameParameters_0[(15272U)>>2]);
        float _S152 = as_type<float>((&kernelContext_0)->frameParameters_0[(15276U)>>2]);
        float4 _S153 = float4(_S149, _S150, _S151, _S152);
        float _S154 = as_type<float>((&kernelContext_0)->frameParameters_0[(15280U)>>2]);
        float _S155 = as_type<float>((&kernelContext_0)->frameParameters_0[(15284U)>>2]);
        float _S156 = as_type<float>((&kernelContext_0)->frameParameters_0[(15288U)>>2]);
        float _S157 = as_type<float>((&kernelContext_0)->frameParameters_0[(15292U)>>2]);
        float4 _S158 = float4(_S154, _S155, _S156, _S157);
        (&frame_0)->domeAmbient_0[int(0)] = _S83;
        (&frame_0)->domeAmbient_0[int(1)] = _S88;
        (&frame_0)->domeAmbient_0[int(2)] = _S93;
        (&frame_0)->domeAmbient_0[int(3)] = _S98;
        (&frame_0)->domeAmbient_0[int(4)] = _S103;
        (&frame_0)->domeAmbient_0[int(5)] = _S108;
        (&frame_0)->domeAmbient_0[int(6)] = _S113;
        (&frame_0)->domeAmbient_0[int(7)] = _S118;
        (&frame_0)->domeEnvironment_0[int(0)] = _S123;
        (&frame_0)->domeEnvironment_0[int(1)] = _S128;
        (&frame_0)->domeEnvironment_0[int(2)] = _S133;
        (&frame_0)->domeEnvironment_0[int(3)] = _S138;
        (&frame_0)->domeEnvironment_0[int(4)] = _S143;
        (&frame_0)->domeEnvironment_0[int(5)] = _S148;
        (&frame_0)->domeEnvironment_0[int(6)] = _S153;
        (&frame_0)->domeEnvironment_0[int(7)] = _S158;
        _S2 = frame_0;
        break;
    }
    for(;;)
    {
        _S3 = _S2.clipPlaneCount_0;
        uint _S159 = min(_S2.clipPlaneCount_0.x, 8U);
        index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S159)
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
    float4 _S160 = float4(_S7->clearcoatShaded_0) ;
    float shadedMode_0 = _S160.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S7->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S1.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S7->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S1.tint_0.w;
    }
    float4 _S161 = float4(_S7->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S161.xyz;
    float _S162 = _S161.w;
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
        pixelOutput_0 _S163 = { float4(unlitColor_0, opacity_0) };
        return _S163;
    }
    float4 _S164 = float4(_S7->reserved_0) ;
    if((_S164.x) >= 0.5f)
    {
        pixelOutput_0 _S165 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S164.y) * max(0.0f, _S164.z)))) , 1.0f) };
        return _S165;
    }
    float4 _S166 = float4(_S7->metallicRoughnessThresholdWorkflow_0) ;
    float _S167 = saturate(_S166.x);
    float _S168 = clamp(_S166.y, 0.00999999977648258f, 1.0f);
    float4 _S169 = float4(_S7->specularIor_0) ;
    float3 _S170 = _S169.xyz;
    float _S171 = _S169.w;
    float _S172 = _S160.x;
    float _S173 = _S160.y;
    float4 _S174 = float4(_S7->textureControls_0) ;
    uint textureMask_0 = uint(round(_S174.x));
    uint udimMask_0 = uint(round(_S174.y));
    float4 _S175 = float4(_S7->uvTransformRow1_0) ;
    uint secondaryMask_0 = uint(round(_S175.w));
    float4 _S176 = float4(_S7->domeLinkControls_0) ;
    uint alternateMask_0 = uint(round(_S176.w));
    float4 _S177 = float4(_S7->uvTransformRow0_0) ;
    float2 _S178 = _S177.xy;
    float _S179 = _S177.z;
    float2 _S180 = _S175.xy;
    float _S181 = _S175.z;
    float2 _S182 = float2(dot(_S178, _S1.texCoord_0) + _S179, dot(_S180, _S1.texCoord_0) + _S181);
    float2 _S183 = float2(dot(_S178, _S1.texCoord1_0) + _S179, dot(_S180, _S1.texCoord1_0) + _S181);
    float4 _S184 = float4(_S7->alternateUvTransformRow0_0) ;
    float4 _S185 = float4(_S7->alternateUvTransformRow1_0) ;
    float2 _S186 = _S184.xy;
    float _S187 = _S184.z;
    float2 _S188 = _S185.xy;
    float _S189 = _S185.z;
    float2 _S190 = float2(dot(_S186, _S1.texCoord_0) + _S187, dot(_S188, _S1.texCoord_0) + _S189);
    float2 _S191 = float2(dot(_S186, _S1.texCoord1_0) + _S187, dot(_S188, _S1.texCoord1_0) + _S189);
    uint lightCount_0;
    float roughness_0;
    float metallic_0;
    float2 _S192;
    float2 _S193;
    bool hasSceneLighting_0;
    float4 _S194;
    uint3 _S195;
    if((textureMask_0 & 2U) != 0U)
    {
        bool useSecondary_0 = (secondaryMask_0 & 2U) != 0U;
        if((alternateMask_0 & 2U) != 0U)
        {
            if(useSecondary_0)
            {
                _S192 = _S191;
            }
            else
            {
                _S192 = _S190;
            }
        }
        else
        {
            if(useSecondary_0)
            {
                _S192 = _S183;
            }
            else
            {
                _S192 = _S182;
            }
        }
        thread float4 diffuseSample_0;
        bool _S196 = (udimMask_0 & 2U) != 0U;
        uint maskU_0 = uint(round(_S176.y));
        uint maskV_0 = uint(round(_S176.z));
        bool _S197 = (maskU_0 & 2U) != 0U;
        if(_S197)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S198 = (maskV_0 & 2U) != 0U;
        if(_S198)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S199 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S196)
            {
                for(;;)
                {
                    if(_S199 == 0U)
                    {
                        _S193 = _S192;
                        break;
                    }
                    texture2d<float, access::sample> _S200 = (&kernelContext_0)->baseColorTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S200).get_width(0)),(*((&height_0)) = (_S200).get_height(0));
                    float2 size_0 = float2(float(width_0), float(height_0));
                    float2 _S201 = float2(0.5f) ;
                    float2 remapped_0 = clamp((_S192 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S201 / size_0, (size_0 - _S201) / size_0);
                    if((_S199 & 1U) != 0U)
                    {
                        roughness_0 = remapped_0.x;
                    }
                    else
                    {
                        roughness_0 = _S192.x;
                    }
                    if((_S199 & 2U) != 0U)
                    {
                        metallic_0 = remapped_0.y;
                    }
                    else
                    {
                        metallic_0 = _S192.y;
                    }
                    _S193 = float2(roughness_0, metallic_0);
                    break;
                }
                _S194 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S193)));
                break;
            }
            texture2d<float, access::sample> _S202 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S202).get_width(0)),(*((&atlasHeight_0)) = (_S202).get_height(0));
            int3 _S203 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S203)).xy), uint(((_S203)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S204 = atlasWidth_0;
                uint _S205 = 2U % atlasWidth_0;
                int _S206 = int(_S205);
                uint _S207 = 2U / _S204;
                int3 _S208 = int3(_S206, int(_S207), int(0));
                int _S209 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S208)).xy), uint(((_S208)).z))).x * 255.0f)));
                uint _S210 = atlasWidth_0;
                uint _S211 = 3U % atlasWidth_0;
                int _S212 = int(_S211);
                uint _S213 = 3U / _S210;
                int3 _S214 = int3(_S212, int(_S213), int(0));
                int2 minimumTile_0 = int2(_S209, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S214)).xy), uint(((_S214)).z))).x * 255.0f))));
                uint _S215 = atlasWidth_0;
                uint _S216 = 4U % atlasWidth_0;
                int _S217 = int(_S216);
                uint _S218 = 4U / _S215;
                int3 _S219 = int3(_S217, int(_S218), int(0));
                uint _S220 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S219)).xy), uint(((_S219)).z))).x * 255.0f));
                uint _S221 = atlasWidth_0;
                uint _S222 = 5U % atlasWidth_0;
                int _S223 = int(_S222);
                uint _S224 = 5U / _S221;
                int3 _S225 = int3(_S223, int(_S224), int(0));
                uint _S226 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S225)).xy), uint(((_S225)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S220, _S226);
                int2 tile_0 = int2(floor(_S192)) - minimumTile_0;
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
                    int3 _S227 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S227)).xy), uint(((_S227)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S220 + uint(tile_0.x));
                uint _S228 = atlasWidth_0;
                uint _S229 = offset_0 % atlasWidth_0;
                int _S230 = int(_S229);
                uint _S231 = offset_0 / _S228;
                int3 _S232 = int3(_S230, int(_S231), int(0));
                uint _S233 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S232)).xy), uint(((_S232)).z))).x * 255.0f));
                uint _S234 = atlasWidth_0;
                uint _S235 = offset_0 + 1U;
                uint _S236 = _S235 % atlasWidth_0;
                int _S237 = int(_S236);
                uint _S238 = _S235 / _S234;
                int3 _S239 = int3(_S237, int(_S238), int(0));
                uint slot_0 = _S233 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S239)).xy), uint(((_S239)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S240 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S240)).xy), uint(((_S240)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S241 = atlasWidth_0;
                uint _S242 = 6U % atlasWidth_0;
                int _S243 = int(_S242);
                uint _S244 = 6U / _S241;
                int3 _S245 = int3(_S243, int(_S244), int(0));
                uint _S246 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S245)).xy), uint(((_S245)).z))).x * 255.0f));
                uint _S247 = atlasWidth_0;
                uint _S248 = 7U % atlasWidth_0;
                int _S249 = int(_S248);
                uint _S250 = 7U / _S247;
                int3 _S251 = int3(_S249, int(_S250), int(0));
                uint columns_0 = _S246 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S251)).xy), uint(((_S251)).z))).x * 255.0f)) << 8U);
                uint _S252 = atlasWidth_0;
                uint _S253 = 8U % atlasWidth_0;
                int _S254 = int(_S253);
                uint _S255 = 8U / _S252;
                int3 _S256 = int3(_S254, int(_S255), int(0));
                uint _S257 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S256)).xy), uint(((_S256)).z))).x * 255.0f));
                uint _S258 = atlasWidth_0;
                uint _S259 = 9U % atlasWidth_0;
                int _S260 = int(_S259);
                uint _S261 = 9U / _S258;
                int3 _S262 = int3(_S260, int(_S261), int(0));
                uint rows_0 = ((_S257 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S262)).xy), uint(((_S262)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S220 * _S226 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S263 = atlasWidth_0 / columns_0;
                float _S264 = float(_S263);
                uint _S265 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S264, float(_S265));
                uint _S266 = slot_1 % columns_0;
                float _S267 = float(_S266);
                uint _S268 = slot_1 / columns_0;
                _S194 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S267, float(_S268)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S192) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S269 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(_S192)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S269);
            }
            if(hasSceneLighting_0)
            {
                int3 _S270 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S194 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S270)).xy), uint(((_S270)).z)));
                break;
            }
            uint _S271 = atlasWidth_0 / uint(_S269.x);
            float _S272 = float(_S271);
            uint _S273 = (atlasHeight_0 - 1U) / uint(_S269.y);
            float2 cellSize_1 = float2(_S272, float(_S273));
            _S194 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S194;
        float4 _S274 = float4(_S7->compositeControls_0) ;
        float _S275 = _S274.x;
        if(_S275 == 0.0f)
        {
            hasSceneLighting_0 = (_S274.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S276 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S276).get_width(0)),(*((&sourceHeight_0)) = (_S276).get_height(0));
            texture2d<float, access::sample> _S277 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S277).get_width(0)),(*((&valueHeight_0)) = (_S277).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S278 = diffuseSample_0;
                for(;;)
                {
                    float _S279 = _S278.w;
                    if(_S279 > 1.0f)
                    {
                        diffuseColor_0 = _S278.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S280 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S280).get_width(0)),(*((&height_1)) = (_S280).get_height(0));
                    uint3 _S281 = uint3(round(_S278.xyz * float3(255.0f) ));
                    uint index_1 = ((_S281.x) | ((_S281.y) << 8U)) | ((_S281.z) << 16U);
                    if(_S279 == 0.0f)
                    {
                        _S195 = _S281;
                    }
                    else
                    {
                        _S195 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S195 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S282 = address_0.x;
                    uint _S283 = _S282 % width_1;
                    int _S284 = int(_S283);
                    uint _S285 = _S282 / width_1;
                    int3 _S286 = int3(_S284, int(_S285), int(0));
                    float _S287 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S286)).xy), uint(((_S286)).z))).x;
                    uint _S288 = address_0.y;
                    uint _S289 = _S288 % width_1;
                    int _S290 = int(_S289);
                    uint _S291 = _S288 / width_1;
                    int3 _S292 = int3(_S290, int(_S291), int(0));
                    float _S293 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S292)).xy), uint(((_S292)).z))).x;
                    uint _S294 = address_0.z;
                    uint _S295 = _S294 % width_1;
                    int _S296 = int(_S295);
                    uint _S297 = _S294 / width_1;
                    int3 _S298 = int3(_S296, int(_S297), int(0));
                    diffuseColor_0 = float3(_S287, _S293, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S298)).xy), uint(((_S298)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                if(_S197)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S198)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S299 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S299 == 0U)
                    {
                        _S193 = _S192;
                        break;
                    }
                    texture2d<float, access::sample> _S300 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S300).get_width(0)),(*((&height_2)) = (_S300).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S301 = float2(0.5f) ;
                    float2 remapped_1 = clamp((_S192 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S301 / size_1, (size_1 - _S301) / size_1);
                    if((_S299 & 1U) != 0U)
                    {
                        roughness_0 = remapped_1.x;
                    }
                    else
                    {
                        roughness_0 = _S192.x;
                    }
                    if((_S299 & 2U) != 0U)
                    {
                        metallic_0 = remapped_1.y;
                    }
                    else
                    {
                        metallic_0 = _S192.y;
                    }
                    _S193 = float2(roughness_0, metallic_0);
                    break;
                }
                diffuseSample_0.z = (((&kernelContext_0)->diffuseValuesTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S193))).x;
            }
        }
        float4 _S302 = diffuseSample_0;
        for(;;)
        {
            if(_S275 != 2.0f)
            {
                _S194 = _S302;
                break;
            }
            bool _S303 = (_S274.w) >= 0.5f;
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
            uint _S304 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S303)
                {
                    for(;;)
                    {
                        if(_S304 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S305 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_3;
                        thread uint height_3;
                        (*((&width_3)) = (_S305).get_width(0)),(*((&height_3)) = (_S305).get_height(0));
                        float2 size_2 = float2(float(width_3), float(height_3));
                        float2 _S306 = float2(0.5f) ;
                        float2 remapped_2 = clamp((_S192 * (size_2 - float2(2.0f) ) + float2(1.0f) ) / size_2, _S306 / size_2, (size_2 - _S306) / size_2);
                        if((_S304 & 1U) != 0U)
                        {
                            roughness_0 = remapped_2.x;
                        }
                        else
                        {
                            roughness_0 = _S192.x;
                        }
                        if((_S304 & 2U) != 0U)
                        {
                            metallic_0 = remapped_2.y;
                        }
                        else
                        {
                            metallic_0 = _S192.y;
                        }
                        _S192 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S194 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S192)));
                    break;
                }
                texture2d<float, access::sample> _S307 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S307).get_width(0)),(*((&atlasHeight_1)) = (_S307).get_height(0));
                int3 _S308 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S308)).xy), uint(((_S308)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S309 = atlasWidth_1;
                    uint _S310 = 2U % atlasWidth_1;
                    int _S311 = int(_S310);
                    uint _S312 = 2U / _S309;
                    int3 _S313 = int3(_S311, int(_S312), int(0));
                    int _S314 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S313)).xy), uint(((_S313)).z))).x * 255.0f)));
                    uint _S315 = atlasWidth_1;
                    uint _S316 = 3U % atlasWidth_1;
                    int _S317 = int(_S316);
                    uint _S318 = 3U / _S315;
                    int3 _S319 = int3(_S317, int(_S318), int(0));
                    int2 minimumTile_1 = int2(_S314, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S319)).xy), uint(((_S319)).z))).x * 255.0f))));
                    uint _S320 = atlasWidth_1;
                    uint _S321 = 4U % atlasWidth_1;
                    int _S322 = int(_S321);
                    uint _S323 = 4U / _S320;
                    int3 _S324 = int3(_S322, int(_S323), int(0));
                    uint _S325 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S324)).xy), uint(((_S324)).z))).x * 255.0f));
                    uint _S326 = atlasWidth_1;
                    uint _S327 = 5U % atlasWidth_1;
                    int _S328 = int(_S327);
                    uint _S329 = 5U / _S326;
                    int3 _S330 = int3(_S328, int(_S329), int(0));
                    uint _S331 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S330)).xy), uint(((_S330)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S325, _S331);
                    int2 tile_2 = int2(floor(_S192)) - minimumTile_1;
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
                        int3 _S332 = int3(int(1), int(0), int(0));
                        _S194 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S332)).xy), uint(((_S332)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S325 + uint(tile_2.x));
                    uint _S333 = atlasWidth_1;
                    uint _S334 = offset_1 % atlasWidth_1;
                    int _S335 = int(_S334);
                    uint _S336 = offset_1 / _S333;
                    int3 _S337 = int3(_S335, int(_S336), int(0));
                    uint _S338 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S337)).xy), uint(((_S337)).z))).x * 255.0f));
                    uint _S339 = atlasWidth_1;
                    uint _S340 = offset_1 + 1U;
                    uint _S341 = _S340 % atlasWidth_1;
                    int _S342 = int(_S341);
                    uint _S343 = _S340 / _S339;
                    int3 _S344 = int3(_S342, int(_S343), int(0));
                    uint slot_2 = _S338 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S344)).xy), uint(((_S344)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S345 = int3(int(1), int(0), int(0));
                        _S194 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S345)).xy), uint(((_S345)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S346 = atlasWidth_1;
                    uint _S347 = 6U % atlasWidth_1;
                    int _S348 = int(_S347);
                    uint _S349 = 6U / _S346;
                    int3 _S350 = int3(_S348, int(_S349), int(0));
                    uint _S351 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S350)).xy), uint(((_S350)).z))).x * 255.0f));
                    uint _S352 = atlasWidth_1;
                    uint _S353 = 7U % atlasWidth_1;
                    int _S354 = int(_S353);
                    uint _S355 = 7U / _S352;
                    int3 _S356 = int3(_S354, int(_S355), int(0));
                    uint columns_1 = _S351 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S356)).xy), uint(((_S356)).z))).x * 255.0f)) << 8U);
                    uint _S357 = atlasWidth_1;
                    uint _S358 = 8U % atlasWidth_1;
                    int _S359 = int(_S358);
                    uint _S360 = 8U / _S357;
                    int3 _S361 = int3(_S359, int(_S360), int(0));
                    uint _S362 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S361)).xy), uint(((_S361)).z))).x * 255.0f));
                    uint _S363 = atlasWidth_1;
                    uint _S364 = 9U % atlasWidth_1;
                    int _S365 = int(_S364);
                    uint _S366 = 9U / _S363;
                    int3 _S367 = int3(_S365, int(_S366), int(0));
                    uint rows_1 = ((_S362 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S367)).xy), uint(((_S367)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S325 * _S331 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S368 = atlasWidth_1 / columns_1;
                    float _S369 = float(_S368);
                    uint _S370 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S369, float(_S370));
                    uint _S371 = slot_3 % columns_1;
                    float _S372 = float(_S371);
                    uint _S373 = slot_3 / columns_1;
                    _S194 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S372, float(_S373)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S192) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S374 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(_S192)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S374);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S375 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S194 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S375)).xy), uint(((_S375)).z)));
                    break;
                }
                uint _S376 = atlasWidth_1 / uint(_S374.x);
                float _S377 = float(_S376);
                uint _S378 = (atlasHeight_1 - 1U) / uint(_S374.y);
                float2 cellSize_3 = float2(_S377, float(_S378));
                _S194 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S274.y));
            if(operation_0 == 1U)
            {
                _S194 = _S302 * _S194;
                break;
            }
            if(operation_0 == 2U)
            {
                _S194 = _S302 + _S194;
                break;
            }
            if(operation_0 == 3U)
            {
                _S194 = _S302 - _S194;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S274.z;
                _S194 = _S302 * float4((1.0f - factor_0))  + _S194 * float4(factor_0) ;
                break;
            }
            _S194 = _S302;
            break;
        }
        diffuseColor_0 = _S194.xyz;
    }
    float4 _S379;
    if((textureMask_0 & 8U) != 0U)
    {
        bool useSecondary_1 = (secondaryMask_0 & 8U) != 0U;
        if((alternateMask_0 & 8U) != 0U)
        {
            if(useSecondary_1)
            {
                _S192 = _S191;
            }
            else
            {
                _S192 = _S190;
            }
        }
        else
        {
            if(useSecondary_1)
            {
                _S192 = _S183;
            }
            else
            {
                _S192 = _S182;
            }
        }
        bool _S380 = (udimMask_0 & 8U) != 0U;
        uint maskU_1 = uint(round(_S176.y));
        uint maskV_1 = uint(round(_S176.z));
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
        uint _S381 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S380)
            {
                for(;;)
                {
                    if(_S381 == 0U)
                    {
                        _S193 = _S192;
                        break;
                    }
                    texture2d<float, access::sample> _S382 = (&kernelContext_0)->roughnessMetallicTexture_0;
                    thread uint width_4;
                    thread uint height_4;
                    (*((&width_4)) = (_S382).get_width(0)),(*((&height_4)) = (_S382).get_height(0));
                    float2 size_3 = float2(float(width_4), float(height_4));
                    float2 _S383 = float2(0.5f) ;
                    float2 remapped_3 = clamp((_S192 * (size_3 - float2(2.0f) ) + float2(1.0f) ) / size_3, _S383 / size_3, (size_3 - _S383) / size_3);
                    if((_S381 & 1U) != 0U)
                    {
                        roughness_0 = remapped_3.x;
                    }
                    else
                    {
                        roughness_0 = _S192.x;
                    }
                    if((_S381 & 2U) != 0U)
                    {
                        metallic_0 = remapped_3.y;
                    }
                    else
                    {
                        metallic_0 = _S192.y;
                    }
                    _S193 = float2(roughness_0, metallic_0);
                    break;
                }
                _S194 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S193)));
                break;
            }
            texture2d<float, access::sample> _S384 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S384).get_width(0)),(*((&atlasHeight_2)) = (_S384).get_height(0));
            int3 _S385 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S385)).xy), uint(((_S385)).z))) * float4(255.0f) );
            if((metadata_2.x) == 255.0f)
            {
                uint _S386 = atlasWidth_2;
                uint _S387 = 2U % atlasWidth_2;
                int _S388 = int(_S387);
                uint _S389 = 2U / _S386;
                int3 _S390 = int3(_S388, int(_S389), int(0));
                int _S391 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S390)).xy), uint(((_S390)).z))).x * 255.0f)));
                uint _S392 = atlasWidth_2;
                uint _S393 = 3U % atlasWidth_2;
                int _S394 = int(_S393);
                uint _S395 = 3U / _S392;
                int3 _S396 = int3(_S394, int(_S395), int(0));
                int2 minimumTile_2 = int2(_S391, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S396)).xy), uint(((_S396)).z))).x * 255.0f))));
                uint _S397 = atlasWidth_2;
                uint _S398 = 4U % atlasWidth_2;
                int _S399 = int(_S398);
                uint _S400 = 4U / _S397;
                int3 _S401 = int3(_S399, int(_S400), int(0));
                uint _S402 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S401)).xy), uint(((_S401)).z))).x * 255.0f));
                uint _S403 = atlasWidth_2;
                uint _S404 = 5U % atlasWidth_2;
                int _S405 = int(_S404);
                uint _S406 = 5U / _S403;
                int3 _S407 = int3(_S405, int(_S406), int(0));
                uint _S408 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S407)).xy), uint(((_S407)).z))).x * 255.0f));
                uint2 grid_2 = uint2(_S402, _S408);
                int2 tile_4 = int2(floor(_S192)) - minimumTile_2;
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
                    int3 _S409 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S409)).xy), uint(((_S409)).z)));
                    break;
                }
                uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S402 + uint(tile_4.x));
                uint _S410 = atlasWidth_2;
                uint _S411 = offset_2 % atlasWidth_2;
                int _S412 = int(_S411);
                uint _S413 = offset_2 / _S410;
                int3 _S414 = int3(_S412, int(_S413), int(0));
                uint _S415 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S414)).xy), uint(((_S414)).z))).x * 255.0f));
                uint _S416 = atlasWidth_2;
                uint _S417 = offset_2 + 1U;
                uint _S418 = _S417 % atlasWidth_2;
                int _S419 = int(_S418);
                uint _S420 = _S417 / _S416;
                int3 _S421 = int3(_S419, int(_S420), int(0));
                uint slot_4 = _S415 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S421)).xy), uint(((_S421)).z))).x * 255.0f)) << 8U);
                if(slot_4 == 0U)
                {
                    int3 _S422 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S422)).xy), uint(((_S422)).z)));
                    break;
                }
                uint slot_5 = slot_4 - 1U;
                uint _S423 = atlasWidth_2;
                uint _S424 = 6U % atlasWidth_2;
                int _S425 = int(_S424);
                uint _S426 = 6U / _S423;
                int3 _S427 = int3(_S425, int(_S426), int(0));
                uint _S428 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S427)).xy), uint(((_S427)).z))).x * 255.0f));
                uint _S429 = atlasWidth_2;
                uint _S430 = 7U % atlasWidth_2;
                int _S431 = int(_S430);
                uint _S432 = 7U / _S429;
                int3 _S433 = int3(_S431, int(_S432), int(0));
                uint columns_2 = _S428 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S433)).xy), uint(((_S433)).z))).x * 255.0f)) << 8U);
                uint _S434 = atlasWidth_2;
                uint _S435 = 8U % atlasWidth_2;
                int _S436 = int(_S435);
                uint _S437 = 8U / _S434;
                int3 _S438 = int3(_S436, int(_S437), int(0));
                uint _S439 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S438)).xy), uint(((_S438)).z))).x * 255.0f));
                uint _S440 = atlasWidth_2;
                uint _S441 = 9U % atlasWidth_2;
                int _S442 = int(_S441);
                uint _S443 = 9U / _S440;
                int3 _S444 = int3(_S442, int(_S443), int(0));
                uint rows_2 = ((_S439 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S444)).xy), uint(((_S444)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
                uint metadataRows_2 = (10U + 2U * _S402 * _S408 + atlasWidth_2 - 1U) / atlasWidth_2;
                uint _S445 = atlasWidth_2 / columns_2;
                float _S446 = float(_S445);
                uint _S447 = (atlasHeight_2 - metadataRows_2) / rows_2;
                float2 cellSize_4 = float2(_S446, float(_S447));
                uint _S448 = slot_5 % columns_2;
                float _S449 = float(_S448);
                uint _S450 = slot_5 / columns_2;
                _S194 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S449, float(_S450)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(_S192) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
                break;
            }
            int2 _S451 = int2(metadata_2.zw);
            int2 tile_5 = int2(floor(_S192)) - int2(metadata_2.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S451);
            }
            if(hasSceneLighting_0)
            {
                int3 _S452 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S194 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S452)).xy), uint(((_S452)).z)));
                break;
            }
            uint _S453 = atlasWidth_2 / uint(_S451.x);
            float _S454 = float(_S453);
            uint _S455 = (atlasHeight_2 - 1U) / uint(_S451.y);
            float2 cellSize_5 = float2(_S454, float(_S455));
            _S194 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        for(;;)
        {
            float4 _S456 = float4(_S7->compositeControls_0) ;
            if((_S456.x) != 8.0f)
            {
                break;
            }
            bool _S457 = (_S456.w) >= 0.5f;
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
            uint _S458 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S457)
                {
                    for(;;)
                    {
                        if(_S458 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S459 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_5;
                        thread uint height_5;
                        (*((&width_5)) = (_S459).get_width(0)),(*((&height_5)) = (_S459).get_height(0));
                        float2 size_4 = float2(float(width_5), float(height_5));
                        float2 _S460 = float2(0.5f) ;
                        float2 remapped_4 = clamp((_S192 * (size_4 - float2(2.0f) ) + float2(1.0f) ) / size_4, _S460 / size_4, (size_4 - _S460) / size_4);
                        if((_S458 & 1U) != 0U)
                        {
                            roughness_0 = remapped_4.x;
                        }
                        else
                        {
                            roughness_0 = _S192.x;
                        }
                        if((_S458 & 2U) != 0U)
                        {
                            metallic_0 = remapped_4.y;
                        }
                        else
                        {
                            metallic_0 = _S192.y;
                        }
                        _S192 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S192)));
                    break;
                }
                texture2d<float, access::sample> _S461 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_3;
                thread uint atlasHeight_3;
                (*((&atlasWidth_3)) = (_S461).get_width(0)),(*((&atlasHeight_3)) = (_S461).get_height(0));
                int3 _S462 = int3(int(0), int(0), int(0));
                float4 metadata_3 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S462)).xy), uint(((_S462)).z))) * float4(255.0f) );
                if((metadata_3.x) == 255.0f)
                {
                    uint _S463 = atlasWidth_3;
                    uint _S464 = 2U % atlasWidth_3;
                    int _S465 = int(_S464);
                    uint _S466 = 2U / _S463;
                    int3 _S467 = int3(_S465, int(_S466), int(0));
                    int _S468 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S467)).xy), uint(((_S467)).z))).x * 255.0f)));
                    uint _S469 = atlasWidth_3;
                    uint _S470 = 3U % atlasWidth_3;
                    int _S471 = int(_S470);
                    uint _S472 = 3U / _S469;
                    int3 _S473 = int3(_S471, int(_S472), int(0));
                    int2 minimumTile_3 = int2(_S468, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S473)).xy), uint(((_S473)).z))).x * 255.0f))));
                    uint _S474 = atlasWidth_3;
                    uint _S475 = 4U % atlasWidth_3;
                    int _S476 = int(_S475);
                    uint _S477 = 4U / _S474;
                    int3 _S478 = int3(_S476, int(_S477), int(0));
                    uint _S479 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S478)).xy), uint(((_S478)).z))).x * 255.0f));
                    uint _S480 = atlasWidth_3;
                    uint _S481 = 5U % atlasWidth_3;
                    int _S482 = int(_S481);
                    uint _S483 = 5U / _S480;
                    int3 _S484 = int3(_S482, int(_S483), int(0));
                    uint _S485 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S484)).xy), uint(((_S484)).z))).x * 255.0f));
                    uint2 grid_3 = uint2(_S479, _S485);
                    int2 tile_6 = int2(floor(_S192)) - minimumTile_3;
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
                        int3 _S486 = int3(int(1), int(0), int(0));
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S486)).xy), uint(((_S486)).z)));
                        break;
                    }
                    uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S479 + uint(tile_6.x));
                    uint _S487 = atlasWidth_3;
                    uint _S488 = offset_3 % atlasWidth_3;
                    int _S489 = int(_S488);
                    uint _S490 = offset_3 / _S487;
                    int3 _S491 = int3(_S489, int(_S490), int(0));
                    uint _S492 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S491)).xy), uint(((_S491)).z))).x * 255.0f));
                    uint _S493 = atlasWidth_3;
                    uint _S494 = offset_3 + 1U;
                    uint _S495 = _S494 % atlasWidth_3;
                    int _S496 = int(_S495);
                    uint _S497 = _S494 / _S493;
                    int3 _S498 = int3(_S496, int(_S497), int(0));
                    uint slot_6 = _S492 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S498)).xy), uint(((_S498)).z))).x * 255.0f)) << 8U);
                    if(slot_6 == 0U)
                    {
                        int3 _S499 = int3(int(1), int(0), int(0));
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S499)).xy), uint(((_S499)).z)));
                        break;
                    }
                    uint slot_7 = slot_6 - 1U;
                    uint _S500 = atlasWidth_3;
                    uint _S501 = 6U % atlasWidth_3;
                    int _S502 = int(_S501);
                    uint _S503 = 6U / _S500;
                    int3 _S504 = int3(_S502, int(_S503), int(0));
                    uint _S505 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S504)).xy), uint(((_S504)).z))).x * 255.0f));
                    uint _S506 = atlasWidth_3;
                    uint _S507 = 7U % atlasWidth_3;
                    int _S508 = int(_S507);
                    uint _S509 = 7U / _S506;
                    int3 _S510 = int3(_S508, int(_S509), int(0));
                    uint columns_3 = _S505 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S510)).xy), uint(((_S510)).z))).x * 255.0f)) << 8U);
                    uint _S511 = atlasWidth_3;
                    uint _S512 = 8U % atlasWidth_3;
                    int _S513 = int(_S512);
                    uint _S514 = 8U / _S511;
                    int3 _S515 = int3(_S513, int(_S514), int(0));
                    uint _S516 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S515)).xy), uint(((_S515)).z))).x * 255.0f));
                    uint _S517 = atlasWidth_3;
                    uint _S518 = 9U % atlasWidth_3;
                    int _S519 = int(_S518);
                    uint _S520 = 9U / _S517;
                    int3 _S521 = int3(_S519, int(_S520), int(0));
                    uint rows_3 = ((_S516 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S521)).xy), uint(((_S521)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                    uint metadataRows_3 = (10U + 2U * _S479 * _S485 + atlasWidth_3 - 1U) / atlasWidth_3;
                    uint _S522 = atlasWidth_3 / columns_3;
                    float _S523 = float(_S522);
                    uint _S524 = (atlasHeight_3 - metadataRows_3) / rows_3;
                    float2 cellSize_6 = float2(_S523, float(_S524));
                    uint _S525 = slot_7 % columns_3;
                    float _S526 = float(_S525);
                    uint _S527 = slot_7 / columns_3;
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S526, float(_S527)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(_S192) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                    break;
                }
                int2 _S528 = int2(metadata_3.zw);
                int2 tile_7 = int2(floor(_S192)) - int2(metadata_3.xy);
                if(any(tile_7 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_7 >= _S528);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S529 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                    _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S529)).xy), uint(((_S529)).z)));
                    break;
                }
                uint _S530 = atlasWidth_3 / uint(_S528.x);
                float _S531 = float(_S530);
                uint _S532 = (atlasHeight_3 - 1U) / uint(_S528.y);
                float2 cellSize_7 = float2(_S531, float(_S532));
                _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            uint operation_1 = uint(round(_S456.y));
            if(operation_1 == 1U)
            {
                _S194 = _S194 * _S379;
                break;
            }
            if(operation_1 == 2U)
            {
                _S194 = _S194 + _S379;
                break;
            }
            if(operation_1 == 3U)
            {
                _S194 = _S194 - _S379;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S456.z;
                _S194 = _S194 * float4((1.0f - factor_1))  + _S379 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S194.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S168;
    }
    float occlusion_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool useSecondary_2 = (secondaryMask_0 & 32U) != 0U;
        if((alternateMask_0 & 32U) != 0U)
        {
            if(useSecondary_2)
            {
                _S192 = _S191;
            }
            else
            {
                _S192 = _S190;
            }
        }
        else
        {
            if(useSecondary_2)
            {
                _S192 = _S183;
            }
            else
            {
                _S192 = _S182;
            }
        }
        bool _S533 = (udimMask_0 & 32U) != 0U;
        uint maskU_2 = uint(round(_S176.y));
        uint maskV_2 = uint(round(_S176.z));
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
        uint _S534 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S533)
            {
                for(;;)
                {
                    if(_S534 == 0U)
                    {
                        _S193 = _S192;
                        break;
                    }
                    texture2d<float, access::sample> _S535 = (&kernelContext_0)->metallicTexture_0;
                    thread uint width_6;
                    thread uint height_6;
                    (*((&width_6)) = (_S535).get_width(0)),(*((&height_6)) = (_S535).get_height(0));
                    float2 size_5 = float2(float(width_6), float(height_6));
                    float2 _S536 = float2(0.5f) ;
                    float2 remapped_5 = clamp((_S192 * (size_5 - float2(2.0f) ) + float2(1.0f) ) / size_5, _S536 / size_5, (size_5 - _S536) / size_5);
                    if((_S534 & 1U) != 0U)
                    {
                        metallic_0 = remapped_5.x;
                    }
                    else
                    {
                        metallic_0 = _S192.x;
                    }
                    if((_S534 & 2U) != 0U)
                    {
                        occlusion_0 = remapped_5.y;
                    }
                    else
                    {
                        occlusion_0 = _S192.y;
                    }
                    _S193 = float2(metallic_0, occlusion_0);
                    break;
                }
                _S194 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S193)));
                break;
            }
            texture2d<float, access::sample> _S537 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S537).get_width(0)),(*((&atlasHeight_4)) = (_S537).get_height(0));
            int3 _S538 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S538)).xy), uint(((_S538)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S539 = atlasWidth_4;
                uint _S540 = 2U % atlasWidth_4;
                int _S541 = int(_S540);
                uint _S542 = 2U / _S539;
                int3 _S543 = int3(_S541, int(_S542), int(0));
                int _S544 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S543)).xy), uint(((_S543)).z))).x * 255.0f)));
                uint _S545 = atlasWidth_4;
                uint _S546 = 3U % atlasWidth_4;
                int _S547 = int(_S546);
                uint _S548 = 3U / _S545;
                int3 _S549 = int3(_S547, int(_S548), int(0));
                int2 minimumTile_4 = int2(_S544, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S549)).xy), uint(((_S549)).z))).x * 255.0f))));
                uint _S550 = atlasWidth_4;
                uint _S551 = 4U % atlasWidth_4;
                int _S552 = int(_S551);
                uint _S553 = 4U / _S550;
                int3 _S554 = int3(_S552, int(_S553), int(0));
                uint _S555 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S554)).xy), uint(((_S554)).z))).x * 255.0f));
                uint _S556 = atlasWidth_4;
                uint _S557 = 5U % atlasWidth_4;
                int _S558 = int(_S557);
                uint _S559 = 5U / _S556;
                int3 _S560 = int3(_S558, int(_S559), int(0));
                uint _S561 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S560)).xy), uint(((_S560)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S555, _S561);
                int2 tile_8 = int2(floor(_S192)) - minimumTile_4;
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
                    int3 _S562 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S562)).xy), uint(((_S562)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S555 + uint(tile_8.x));
                uint _S563 = atlasWidth_4;
                uint _S564 = offset_4 % atlasWidth_4;
                int _S565 = int(_S564);
                uint _S566 = offset_4 / _S563;
                int3 _S567 = int3(_S565, int(_S566), int(0));
                uint _S568 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S567)).xy), uint(((_S567)).z))).x * 255.0f));
                uint _S569 = atlasWidth_4;
                uint _S570 = offset_4 + 1U;
                uint _S571 = _S570 % atlasWidth_4;
                int _S572 = int(_S571);
                uint _S573 = _S570 / _S569;
                int3 _S574 = int3(_S572, int(_S573), int(0));
                uint slot_8 = _S568 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S574)).xy), uint(((_S574)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S575 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S575)).xy), uint(((_S575)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S576 = atlasWidth_4;
                uint _S577 = 6U % atlasWidth_4;
                int _S578 = int(_S577);
                uint _S579 = 6U / _S576;
                int3 _S580 = int3(_S578, int(_S579), int(0));
                uint _S581 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S580)).xy), uint(((_S580)).z))).x * 255.0f));
                uint _S582 = atlasWidth_4;
                uint _S583 = 7U % atlasWidth_4;
                int _S584 = int(_S583);
                uint _S585 = 7U / _S582;
                int3 _S586 = int3(_S584, int(_S585), int(0));
                uint columns_4 = _S581 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S586)).xy), uint(((_S586)).z))).x * 255.0f)) << 8U);
                uint _S587 = atlasWidth_4;
                uint _S588 = 8U % atlasWidth_4;
                int _S589 = int(_S588);
                uint _S590 = 8U / _S587;
                int3 _S591 = int3(_S589, int(_S590), int(0));
                uint _S592 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S591)).xy), uint(((_S591)).z))).x * 255.0f));
                uint _S593 = atlasWidth_4;
                uint _S594 = 9U % atlasWidth_4;
                int _S595 = int(_S594);
                uint _S596 = 9U / _S593;
                int3 _S597 = int3(_S595, int(_S596), int(0));
                uint rows_4 = ((_S592 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S597)).xy), uint(((_S597)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S555 * _S561 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S598 = atlasWidth_4 / columns_4;
                float _S599 = float(_S598);
                uint _S600 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S599, float(_S600));
                uint _S601 = slot_9 % columns_4;
                float _S602 = float(_S601);
                uint _S603 = slot_9 / columns_4;
                _S194 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S602, float(_S603)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(_S192) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S604 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(_S192)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S604);
            }
            if(hasSceneLighting_0)
            {
                int3 _S605 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S194 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S605)).xy), uint(((_S605)).z)));
                break;
            }
            uint _S606 = atlasWidth_4 / uint(_S604.x);
            float _S607 = float(_S606);
            uint _S608 = (atlasHeight_4 - 1U) / uint(_S604.y);
            float2 cellSize_9 = float2(_S607, float(_S608));
            _S194 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S609 = float4(_S7->compositeControls_0) ;
            if((_S609.x) != 32.0f)
            {
                break;
            }
            bool _S610 = (_S609.w) >= 0.5f;
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
            uint _S611 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S610)
                {
                    for(;;)
                    {
                        if(_S611 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S612 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_7;
                        thread uint height_7;
                        (*((&width_7)) = (_S612).get_width(0)),(*((&height_7)) = (_S612).get_height(0));
                        float2 size_6 = float2(float(width_7), float(height_7));
                        float2 _S613 = float2(0.5f) ;
                        float2 remapped_6 = clamp((_S192 * (size_6 - float2(2.0f) ) + float2(1.0f) ) / size_6, _S613 / size_6, (size_6 - _S613) / size_6);
                        if((_S611 & 1U) != 0U)
                        {
                            metallic_0 = remapped_6.x;
                        }
                        else
                        {
                            metallic_0 = _S192.x;
                        }
                        if((_S611 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_6.y;
                        }
                        else
                        {
                            occlusion_0 = _S192.y;
                        }
                        _S192 = float2(metallic_0, occlusion_0);
                        break;
                    }
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S192)));
                    break;
                }
                texture2d<float, access::sample> _S614 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S614).get_width(0)),(*((&atlasHeight_5)) = (_S614).get_height(0));
                int3 _S615 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S615)).xy), uint(((_S615)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S616 = atlasWidth_5;
                    uint _S617 = 2U % atlasWidth_5;
                    int _S618 = int(_S617);
                    uint _S619 = 2U / _S616;
                    int3 _S620 = int3(_S618, int(_S619), int(0));
                    int _S621 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S620)).xy), uint(((_S620)).z))).x * 255.0f)));
                    uint _S622 = atlasWidth_5;
                    uint _S623 = 3U % atlasWidth_5;
                    int _S624 = int(_S623);
                    uint _S625 = 3U / _S622;
                    int3 _S626 = int3(_S624, int(_S625), int(0));
                    int2 minimumTile_5 = int2(_S621, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S626)).xy), uint(((_S626)).z))).x * 255.0f))));
                    uint _S627 = atlasWidth_5;
                    uint _S628 = 4U % atlasWidth_5;
                    int _S629 = int(_S628);
                    uint _S630 = 4U / _S627;
                    int3 _S631 = int3(_S629, int(_S630), int(0));
                    uint _S632 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S631)).xy), uint(((_S631)).z))).x * 255.0f));
                    uint _S633 = atlasWidth_5;
                    uint _S634 = 5U % atlasWidth_5;
                    int _S635 = int(_S634);
                    uint _S636 = 5U / _S633;
                    int3 _S637 = int3(_S635, int(_S636), int(0));
                    uint _S638 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S637)).xy), uint(((_S637)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S632, _S638);
                    int2 tile_10 = int2(floor(_S192)) - minimumTile_5;
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
                        int3 _S639 = int3(int(1), int(0), int(0));
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S639)).xy), uint(((_S639)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S632 + uint(tile_10.x));
                    uint _S640 = atlasWidth_5;
                    uint _S641 = offset_5 % atlasWidth_5;
                    int _S642 = int(_S641);
                    uint _S643 = offset_5 / _S640;
                    int3 _S644 = int3(_S642, int(_S643), int(0));
                    uint _S645 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S644)).xy), uint(((_S644)).z))).x * 255.0f));
                    uint _S646 = atlasWidth_5;
                    uint _S647 = offset_5 + 1U;
                    uint _S648 = _S647 % atlasWidth_5;
                    int _S649 = int(_S648);
                    uint _S650 = _S647 / _S646;
                    int3 _S651 = int3(_S649, int(_S650), int(0));
                    uint slot_10 = _S645 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S651)).xy), uint(((_S651)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S652 = int3(int(1), int(0), int(0));
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S652)).xy), uint(((_S652)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S653 = atlasWidth_5;
                    uint _S654 = 6U % atlasWidth_5;
                    int _S655 = int(_S654);
                    uint _S656 = 6U / _S653;
                    int3 _S657 = int3(_S655, int(_S656), int(0));
                    uint _S658 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S657)).xy), uint(((_S657)).z))).x * 255.0f));
                    uint _S659 = atlasWidth_5;
                    uint _S660 = 7U % atlasWidth_5;
                    int _S661 = int(_S660);
                    uint _S662 = 7U / _S659;
                    int3 _S663 = int3(_S661, int(_S662), int(0));
                    uint columns_5 = _S658 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S663)).xy), uint(((_S663)).z))).x * 255.0f)) << 8U);
                    uint _S664 = atlasWidth_5;
                    uint _S665 = 8U % atlasWidth_5;
                    int _S666 = int(_S665);
                    uint _S667 = 8U / _S664;
                    int3 _S668 = int3(_S666, int(_S667), int(0));
                    uint _S669 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S668)).xy), uint(((_S668)).z))).x * 255.0f));
                    uint _S670 = atlasWidth_5;
                    uint _S671 = 9U % atlasWidth_5;
                    int _S672 = int(_S671);
                    uint _S673 = 9U / _S670;
                    int3 _S674 = int3(_S672, int(_S673), int(0));
                    uint rows_5 = ((_S669 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S674)).xy), uint(((_S674)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S632 * _S638 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S675 = atlasWidth_5 / columns_5;
                    float _S676 = float(_S675);
                    uint _S677 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S676, float(_S677));
                    uint _S678 = slot_11 % columns_5;
                    float _S679 = float(_S678);
                    uint _S680 = slot_11 / columns_5;
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S679, float(_S680)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(_S192) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S681 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(_S192)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S681);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S682 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S682)).xy), uint(((_S682)).z)));
                    break;
                }
                uint _S683 = atlasWidth_5 / uint(_S681.x);
                float _S684 = float(_S683);
                uint _S685 = (atlasHeight_5 - 1U) / uint(_S681.y);
                float2 cellSize_11 = float2(_S684, float(_S685));
                _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_2 = uint(round(_S609.y));
            if(operation_2 == 1U)
            {
                _S194 = _S194 * _S379;
                break;
            }
            if(operation_2 == 2U)
            {
                _S194 = _S194 + _S379;
                break;
            }
            if(operation_2 == 3U)
            {
                _S194 = _S194 - _S379;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S609.z;
                _S194 = _S194 * float4((1.0f - factor_2))  + _S379 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S194.x);
    }
    else
    {
        metallic_0 = _S167;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 16U) != 0U)
    {
        bool useSecondary_3 = (secondaryMask_0 & 16U) != 0U;
        if((alternateMask_0 & 16U) != 0U)
        {
            if(useSecondary_3)
            {
                _S192 = _S191;
            }
            else
            {
                _S192 = _S190;
            }
        }
        else
        {
            if(useSecondary_3)
            {
                _S192 = _S183;
            }
            else
            {
                _S192 = _S182;
            }
        }
        thread float4 emissionSample_0;
        bool _S686 = (udimMask_0 & 16U) != 0U;
        uint maskU_3 = uint(round(_S176.y));
        uint maskV_3 = uint(round(_S176.z));
        bool _S687 = (maskU_3 & 16U) != 0U;
        if(_S687)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S688 = (maskV_3 & 16U) != 0U;
        if(_S688)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S689 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S686)
            {
                for(;;)
                {
                    if(_S689 == 0U)
                    {
                        _S193 = _S192;
                        break;
                    }
                    texture2d<float, access::sample> _S690 = (&kernelContext_0)->emissiveTexture_0;
                    thread uint width_8;
                    thread uint height_8;
                    (*((&width_8)) = (_S690).get_width(0)),(*((&height_8)) = (_S690).get_height(0));
                    float2 size_7 = float2(float(width_8), float(height_8));
                    float2 _S691 = float2(0.5f) ;
                    float2 remapped_7 = clamp((_S192 * (size_7 - float2(2.0f) ) + float2(1.0f) ) / size_7, _S691 / size_7, (size_7 - _S691) / size_7);
                    if((_S689 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_7.x;
                    }
                    else
                    {
                        occlusion_0 = _S192.x;
                    }
                    if((_S689 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_7.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S192.y;
                    }
                    _S193 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S194 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S193)));
                break;
            }
            texture2d<float, access::sample> _S692 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S692).get_width(0)),(*((&atlasHeight_6)) = (_S692).get_height(0));
            int3 _S693 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S693)).xy), uint(((_S693)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S694 = atlasWidth_6;
                uint _S695 = 2U % atlasWidth_6;
                int _S696 = int(_S695);
                uint _S697 = 2U / _S694;
                int3 _S698 = int3(_S696, int(_S697), int(0));
                int _S699 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S698)).xy), uint(((_S698)).z))).x * 255.0f)));
                uint _S700 = atlasWidth_6;
                uint _S701 = 3U % atlasWidth_6;
                int _S702 = int(_S701);
                uint _S703 = 3U / _S700;
                int3 _S704 = int3(_S702, int(_S703), int(0));
                int2 minimumTile_6 = int2(_S699, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S704)).xy), uint(((_S704)).z))).x * 255.0f))));
                uint _S705 = atlasWidth_6;
                uint _S706 = 4U % atlasWidth_6;
                int _S707 = int(_S706);
                uint _S708 = 4U / _S705;
                int3 _S709 = int3(_S707, int(_S708), int(0));
                uint _S710 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S709)).xy), uint(((_S709)).z))).x * 255.0f));
                uint _S711 = atlasWidth_6;
                uint _S712 = 5U % atlasWidth_6;
                int _S713 = int(_S712);
                uint _S714 = 5U / _S711;
                int3 _S715 = int3(_S713, int(_S714), int(0));
                uint _S716 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S715)).xy), uint(((_S715)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S710, _S716);
                int2 tile_12 = int2(floor(_S192)) - minimumTile_6;
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
                    int3 _S717 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S717)).xy), uint(((_S717)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S710 + uint(tile_12.x));
                uint _S718 = atlasWidth_6;
                uint _S719 = offset_6 % atlasWidth_6;
                int _S720 = int(_S719);
                uint _S721 = offset_6 / _S718;
                int3 _S722 = int3(_S720, int(_S721), int(0));
                uint _S723 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S722)).xy), uint(((_S722)).z))).x * 255.0f));
                uint _S724 = atlasWidth_6;
                uint _S725 = offset_6 + 1U;
                uint _S726 = _S725 % atlasWidth_6;
                int _S727 = int(_S726);
                uint _S728 = _S725 / _S724;
                int3 _S729 = int3(_S727, int(_S728), int(0));
                uint slot_12 = _S723 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S729)).xy), uint(((_S729)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S730 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S730)).xy), uint(((_S730)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S731 = atlasWidth_6;
                uint _S732 = 6U % atlasWidth_6;
                int _S733 = int(_S732);
                uint _S734 = 6U / _S731;
                int3 _S735 = int3(_S733, int(_S734), int(0));
                uint _S736 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S735)).xy), uint(((_S735)).z))).x * 255.0f));
                uint _S737 = atlasWidth_6;
                uint _S738 = 7U % atlasWidth_6;
                int _S739 = int(_S738);
                uint _S740 = 7U / _S737;
                int3 _S741 = int3(_S739, int(_S740), int(0));
                uint columns_6 = _S736 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S741)).xy), uint(((_S741)).z))).x * 255.0f)) << 8U);
                uint _S742 = atlasWidth_6;
                uint _S743 = 8U % atlasWidth_6;
                int _S744 = int(_S743);
                uint _S745 = 8U / _S742;
                int3 _S746 = int3(_S744, int(_S745), int(0));
                uint _S747 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S746)).xy), uint(((_S746)).z))).x * 255.0f));
                uint _S748 = atlasWidth_6;
                uint _S749 = 9U % atlasWidth_6;
                int _S750 = int(_S749);
                uint _S751 = 9U / _S748;
                int3 _S752 = int3(_S750, int(_S751), int(0));
                uint rows_6 = ((_S747 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S752)).xy), uint(((_S752)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S710 * _S716 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S753 = atlasWidth_6 / columns_6;
                float _S754 = float(_S753);
                uint _S755 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S754, float(_S755));
                uint _S756 = slot_13 % columns_6;
                float _S757 = float(_S756);
                uint _S758 = slot_13 / columns_6;
                _S194 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S757, float(_S758)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(_S192) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S759 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(_S192)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S759);
            }
            if(hasSceneLighting_0)
            {
                int3 _S760 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S194 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S760)).xy), uint(((_S760)).z)));
                break;
            }
            uint _S761 = atlasWidth_6 / uint(_S759.x);
            float _S762 = float(_S761);
            uint _S763 = (atlasHeight_6 - 1U) / uint(_S759.y);
            float2 cellSize_13 = float2(_S762, float(_S763));
            _S194 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        emissionSample_0 = _S194;
        if((_S177.w) != 0.0f)
        {
            texture2d<float, access::sample> _S764 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S764).get_width(0)),(*((&sourceHeight_1)) = (_S764).get_height(0));
            texture2d<float, access::sample> _S765 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S765).get_width(0)),(*((&valueHeight_1)) = (_S765).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S766 = emissionSample_0;
                for(;;)
                {
                    float _S767 = _S766.w;
                    if(_S767 > 1.0f)
                    {
                        unlitColor_0 = _S766.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S768 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_9;
                    thread uint height_9;
                    (*((&width_9)) = (_S768).get_width(0)),(*((&height_9)) = (_S768).get_height(0));
                    uint3 _S769 = uint3(round(_S766.xyz * float3(255.0f) ));
                    uint index_2 = ((_S769.x) | ((_S769.y) << 8U)) | ((_S769.z) << 16U);
                    if(_S767 == 0.0f)
                    {
                        _S195 = _S769;
                    }
                    else
                    {
                        _S195 = uint3(index_2, index_2, index_2);
                    }
                    uint3 address_1 = _S195 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S770 = address_1.x;
                    uint _S771 = _S770 % width_9;
                    int _S772 = int(_S771);
                    uint _S773 = _S770 / width_9;
                    int3 _S774 = int3(_S772, int(_S773), int(0));
                    float _S775 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S774)).xy), uint(((_S774)).z))).x;
                    uint _S776 = address_1.y;
                    uint _S777 = _S776 % width_9;
                    int _S778 = int(_S777);
                    uint _S779 = _S776 / width_9;
                    int3 _S780 = int3(_S778, int(_S779), int(0));
                    float _S781 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S780)).xy), uint(((_S780)).z))).x;
                    uint _S782 = address_1.z;
                    uint _S783 = _S782 % width_9;
                    int _S784 = int(_S783);
                    uint _S785 = _S782 / width_9;
                    int3 _S786 = int3(_S784, int(_S785), int(0));
                    unlitColor_0 = float3(_S775, _S781, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S786)).xy), uint(((_S786)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                if(_S687)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S688)
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
                    if(_S787 == 0U)
                    {
                        _S193 = _S192;
                        break;
                    }
                    texture2d<float, access::sample> _S788 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_10;
                    thread uint height_10;
                    (*((&width_10)) = (_S788).get_width(0)),(*((&height_10)) = (_S788).get_height(0));
                    float2 size_8 = float2(float(width_10), float(height_10));
                    float2 _S789 = float2(0.5f) ;
                    float2 remapped_8 = clamp((_S192 * (size_8 - float2(2.0f) ) + float2(1.0f) ) / size_8, _S789 / size_8, (size_8 - _S789) / size_8);
                    if((_S787 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_8.x;
                    }
                    else
                    {
                        occlusion_0 = _S192.x;
                    }
                    if((_S787 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_8.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S192.y;
                    }
                    _S193 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                emissionSample_0.z = (((&kernelContext_0)->emissiveValuesTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S193))).x;
            }
        }
        float4 _S790 = emissionSample_0;
        for(;;)
        {
            float4 _S791 = float4(_S7->compositeControls_0) ;
            if((_S791.x) != 16.0f)
            {
                _S194 = _S790;
                break;
            }
            bool _S792 = (_S791.w) >= 0.5f;
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
            uint _S793 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S792)
                {
                    for(;;)
                    {
                        if(_S793 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S794 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_11;
                        thread uint height_11;
                        (*((&width_11)) = (_S794).get_width(0)),(*((&height_11)) = (_S794).get_height(0));
                        float2 size_9 = float2(float(width_11), float(height_11));
                        float2 _S795 = float2(0.5f) ;
                        float2 remapped_9 = clamp((_S192 * (size_9 - float2(2.0f) ) + float2(1.0f) ) / size_9, _S795 / size_9, (size_9 - _S795) / size_9);
                        if((_S793 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_9.x;
                        }
                        else
                        {
                            occlusion_0 = _S192.x;
                        }
                        if((_S793 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_9.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S192.y;
                        }
                        _S192 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S194 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S192)));
                    break;
                }
                texture2d<float, access::sample> _S796 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S796).get_width(0)),(*((&atlasHeight_7)) = (_S796).get_height(0));
                int3 _S797 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S797)).xy), uint(((_S797)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S798 = atlasWidth_7;
                    uint _S799 = 2U % atlasWidth_7;
                    int _S800 = int(_S799);
                    uint _S801 = 2U / _S798;
                    int3 _S802 = int3(_S800, int(_S801), int(0));
                    int _S803 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S802)).xy), uint(((_S802)).z))).x * 255.0f)));
                    uint _S804 = atlasWidth_7;
                    uint _S805 = 3U % atlasWidth_7;
                    int _S806 = int(_S805);
                    uint _S807 = 3U / _S804;
                    int3 _S808 = int3(_S806, int(_S807), int(0));
                    int2 minimumTile_7 = int2(_S803, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S808)).xy), uint(((_S808)).z))).x * 255.0f))));
                    uint _S809 = atlasWidth_7;
                    uint _S810 = 4U % atlasWidth_7;
                    int _S811 = int(_S810);
                    uint _S812 = 4U / _S809;
                    int3 _S813 = int3(_S811, int(_S812), int(0));
                    uint _S814 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S813)).xy), uint(((_S813)).z))).x * 255.0f));
                    uint _S815 = atlasWidth_7;
                    uint _S816 = 5U % atlasWidth_7;
                    int _S817 = int(_S816);
                    uint _S818 = 5U / _S815;
                    int3 _S819 = int3(_S817, int(_S818), int(0));
                    uint _S820 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S819)).xy), uint(((_S819)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S814, _S820);
                    int2 tile_14 = int2(floor(_S192)) - minimumTile_7;
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
                        int3 _S821 = int3(int(1), int(0), int(0));
                        _S194 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S821)).xy), uint(((_S821)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S814 + uint(tile_14.x));
                    uint _S822 = atlasWidth_7;
                    uint _S823 = offset_7 % atlasWidth_7;
                    int _S824 = int(_S823);
                    uint _S825 = offset_7 / _S822;
                    int3 _S826 = int3(_S824, int(_S825), int(0));
                    uint _S827 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S826)).xy), uint(((_S826)).z))).x * 255.0f));
                    uint _S828 = atlasWidth_7;
                    uint _S829 = offset_7 + 1U;
                    uint _S830 = _S829 % atlasWidth_7;
                    int _S831 = int(_S830);
                    uint _S832 = _S829 / _S828;
                    int3 _S833 = int3(_S831, int(_S832), int(0));
                    uint slot_14 = _S827 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S833)).xy), uint(((_S833)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S834 = int3(int(1), int(0), int(0));
                        _S194 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S834)).xy), uint(((_S834)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S835 = atlasWidth_7;
                    uint _S836 = 6U % atlasWidth_7;
                    int _S837 = int(_S836);
                    uint _S838 = 6U / _S835;
                    int3 _S839 = int3(_S837, int(_S838), int(0));
                    uint _S840 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S839)).xy), uint(((_S839)).z))).x * 255.0f));
                    uint _S841 = atlasWidth_7;
                    uint _S842 = 7U % atlasWidth_7;
                    int _S843 = int(_S842);
                    uint _S844 = 7U / _S841;
                    int3 _S845 = int3(_S843, int(_S844), int(0));
                    uint columns_7 = _S840 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S845)).xy), uint(((_S845)).z))).x * 255.0f)) << 8U);
                    uint _S846 = atlasWidth_7;
                    uint _S847 = 8U % atlasWidth_7;
                    int _S848 = int(_S847);
                    uint _S849 = 8U / _S846;
                    int3 _S850 = int3(_S848, int(_S849), int(0));
                    uint _S851 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S850)).xy), uint(((_S850)).z))).x * 255.0f));
                    uint _S852 = atlasWidth_7;
                    uint _S853 = 9U % atlasWidth_7;
                    int _S854 = int(_S853);
                    uint _S855 = 9U / _S852;
                    int3 _S856 = int3(_S854, int(_S855), int(0));
                    uint rows_7 = ((_S851 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S856)).xy), uint(((_S856)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S814 * _S820 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S857 = atlasWidth_7 / columns_7;
                    float _S858 = float(_S857);
                    uint _S859 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S858, float(_S859));
                    uint _S860 = slot_15 % columns_7;
                    float _S861 = float(_S860);
                    uint _S862 = slot_15 / columns_7;
                    _S194 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S861, float(_S862)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(_S192) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S863 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(_S192)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S863);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S864 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S194 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S864)).xy), uint(((_S864)).z)));
                    break;
                }
                uint _S865 = atlasWidth_7 / uint(_S863.x);
                float _S866 = float(_S865);
                uint _S867 = (atlasHeight_7 - 1U) / uint(_S863.y);
                float2 cellSize_15 = float2(_S866, float(_S867));
                _S194 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_3 = uint(round(_S791.y));
            if(operation_3 == 1U)
            {
                _S194 = _S790 * _S194;
                break;
            }
            if(operation_3 == 2U)
            {
                _S194 = _S790 + _S194;
                break;
            }
            if(operation_3 == 3U)
            {
                _S194 = _S790 - _S194;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S791.z;
                _S194 = _S790 * float4((1.0f - factor_3))  + _S194 * float4(factor_3) ;
                break;
            }
            _S194 = _S790;
            break;
        }
        unlitColor_0 = _S194.xyz;
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
                _S192 = _S191;
            }
            else
            {
                _S192 = _S190;
            }
        }
        else
        {
            if(useSecondary_4)
            {
                _S192 = _S183;
            }
            else
            {
                _S192 = _S182;
            }
        }
        for(;;)
        {
            bool _S868 = (udimMask_0 & 64U) != 0U;
            uint maskU_4 = uint(round(_S176.y));
            uint maskV_4 = uint(round(_S176.z));
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
            uint _S869 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S868)
                {
                    for(;;)
                    {
                        if(_S869 == 0U)
                        {
                            _S193 = _S192;
                            break;
                        }
                        texture2d<float, access::sample> _S870 = (&kernelContext_0)->opacityTexture_0;
                        thread uint width_12;
                        thread uint height_12;
                        (*((&width_12)) = (_S870).get_width(0)),(*((&height_12)) = (_S870).get_height(0));
                        float2 size_10 = float2(float(width_12), float(height_12));
                        float2 _S871 = float2(0.5f) ;
                        float2 remapped_10 = clamp((_S192 * (size_10 - float2(2.0f) ) + float2(1.0f) ) / size_10, _S871 / size_10, (size_10 - _S871) / size_10);
                        if((_S869 & 1U) != 0U)
                        {
                            opacity_0 = remapped_10.x;
                        }
                        else
                        {
                            opacity_0 = _S192.x;
                        }
                        if((_S869 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_10.y;
                        }
                        else
                        {
                            occlusion_0 = _S192.y;
                        }
                        _S193 = float2(opacity_0, occlusion_0);
                        break;
                    }
                    _S194 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S193)));
                    break;
                }
                texture2d<float, access::sample> _S872 = (&kernelContext_0)->opacityTexture_0;
                thread uint atlasWidth_8;
                thread uint atlasHeight_8;
                (*((&atlasWidth_8)) = (_S872).get_width(0)),(*((&atlasHeight_8)) = (_S872).get_height(0));
                int3 _S873 = int3(int(0), int(0), int(0));
                float4 metadata_8 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S873)).xy), uint(((_S873)).z))) * float4(255.0f) );
                if((metadata_8.x) == 255.0f)
                {
                    uint _S874 = atlasWidth_8;
                    uint _S875 = 2U % atlasWidth_8;
                    int _S876 = int(_S875);
                    uint _S877 = 2U / _S874;
                    int3 _S878 = int3(_S876, int(_S877), int(0));
                    int _S879 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S878)).xy), uint(((_S878)).z))).x * 255.0f)));
                    uint _S880 = atlasWidth_8;
                    uint _S881 = 3U % atlasWidth_8;
                    int _S882 = int(_S881);
                    uint _S883 = 3U / _S880;
                    int3 _S884 = int3(_S882, int(_S883), int(0));
                    int2 minimumTile_8 = int2(_S879, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S884)).xy), uint(((_S884)).z))).x * 255.0f))));
                    uint _S885 = atlasWidth_8;
                    uint _S886 = 4U % atlasWidth_8;
                    int _S887 = int(_S886);
                    uint _S888 = 4U / _S885;
                    int3 _S889 = int3(_S887, int(_S888), int(0));
                    uint _S890 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S889)).xy), uint(((_S889)).z))).x * 255.0f));
                    uint _S891 = atlasWidth_8;
                    uint _S892 = 5U % atlasWidth_8;
                    int _S893 = int(_S892);
                    uint _S894 = 5U / _S891;
                    int3 _S895 = int3(_S893, int(_S894), int(0));
                    uint _S896 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S895)).xy), uint(((_S895)).z))).x * 255.0f));
                    uint2 grid_8 = uint2(_S890, _S896);
                    int2 tile_16 = int2(floor(_S192)) - minimumTile_8;
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
                        int3 _S897 = int3(int(1), int(0), int(0));
                        _S194 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S897)).xy), uint(((_S897)).z)));
                        break;
                    }
                    uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S890 + uint(tile_16.x));
                    uint _S898 = atlasWidth_8;
                    uint _S899 = offset_8 % atlasWidth_8;
                    int _S900 = int(_S899);
                    uint _S901 = offset_8 / _S898;
                    int3 _S902 = int3(_S900, int(_S901), int(0));
                    uint _S903 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S902)).xy), uint(((_S902)).z))).x * 255.0f));
                    uint _S904 = atlasWidth_8;
                    uint _S905 = offset_8 + 1U;
                    uint _S906 = _S905 % atlasWidth_8;
                    int _S907 = int(_S906);
                    uint _S908 = _S905 / _S904;
                    int3 _S909 = int3(_S907, int(_S908), int(0));
                    uint slot_16 = _S903 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S909)).xy), uint(((_S909)).z))).x * 255.0f)) << 8U);
                    if(slot_16 == 0U)
                    {
                        int3 _S910 = int3(int(1), int(0), int(0));
                        _S194 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S910)).xy), uint(((_S910)).z)));
                        break;
                    }
                    uint slot_17 = slot_16 - 1U;
                    uint _S911 = atlasWidth_8;
                    uint _S912 = 6U % atlasWidth_8;
                    int _S913 = int(_S912);
                    uint _S914 = 6U / _S911;
                    int3 _S915 = int3(_S913, int(_S914), int(0));
                    uint _S916 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S915)).xy), uint(((_S915)).z))).x * 255.0f));
                    uint _S917 = atlasWidth_8;
                    uint _S918 = 7U % atlasWidth_8;
                    int _S919 = int(_S918);
                    uint _S920 = 7U / _S917;
                    int3 _S921 = int3(_S919, int(_S920), int(0));
                    uint columns_8 = _S916 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S921)).xy), uint(((_S921)).z))).x * 255.0f)) << 8U);
                    uint _S922 = atlasWidth_8;
                    uint _S923 = 8U % atlasWidth_8;
                    int _S924 = int(_S923);
                    uint _S925 = 8U / _S922;
                    int3 _S926 = int3(_S924, int(_S925), int(0));
                    uint _S927 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S926)).xy), uint(((_S926)).z))).x * 255.0f));
                    uint _S928 = atlasWidth_8;
                    uint _S929 = 9U % atlasWidth_8;
                    int _S930 = int(_S929);
                    uint _S931 = 9U / _S928;
                    int3 _S932 = int3(_S930, int(_S931), int(0));
                    uint rows_8 = ((_S927 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S932)).xy), uint(((_S932)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                    uint metadataRows_8 = (10U + 2U * _S890 * _S896 + atlasWidth_8 - 1U) / atlasWidth_8;
                    uint _S933 = atlasWidth_8 / columns_8;
                    float _S934 = float(_S933);
                    uint _S935 = (atlasHeight_8 - metadataRows_8) / rows_8;
                    float2 cellSize_16 = float2(_S934, float(_S935));
                    uint _S936 = slot_17 % columns_8;
                    float _S937 = float(_S936);
                    uint _S938 = slot_17 / columns_8;
                    _S194 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S937, float(_S938)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(_S192) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                    break;
                }
                int2 _S939 = int2(metadata_8.zw);
                int2 tile_17 = int2(floor(_S192)) - int2(metadata_8.xy);
                if(any(tile_17 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_17 >= _S939);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S940 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                    _S194 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S940)).xy), uint(((_S940)).z)));
                    break;
                }
                uint _S941 = atlasWidth_8 / uint(_S939.x);
                float _S942 = float(_S941);
                uint _S943 = (atlasHeight_8 - 1U) / uint(_S939.y);
                float2 cellSize_17 = float2(_S942, float(_S943));
                _S194 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            for(;;)
            {
                float4 _S944 = float4(_S7->compositeControls_0) ;
                if((_S944.x) != 64.0f)
                {
                    break;
                }
                bool _S945 = (_S944.w) >= 0.5f;
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
                uint _S946 = index_0 | lightCount_0;
                for(;;)
                {
                    if(!_S945)
                    {
                        for(;;)
                        {
                            if(_S946 == 0U)
                            {
                                break;
                            }
                            texture2d<float, access::sample> _S947 = (&kernelContext_0)->compositeTexture_0;
                            thread uint width_13;
                            thread uint height_13;
                            (*((&width_13)) = (_S947).get_width(0)),(*((&height_13)) = (_S947).get_height(0));
                            float2 size_11 = float2(float(width_13), float(height_13));
                            float2 _S948 = float2(0.5f) ;
                            float2 remapped_11 = clamp((_S192 * (size_11 - float2(2.0f) ) + float2(1.0f) ) / size_11, _S948 / size_11, (size_11 - _S948) / size_11);
                            if((_S946 & 1U) != 0U)
                            {
                                opacity_0 = remapped_11.x;
                            }
                            else
                            {
                                opacity_0 = _S192.x;
                            }
                            if((_S946 & 2U) != 0U)
                            {
                                occlusion_0 = remapped_11.y;
                            }
                            else
                            {
                                occlusion_0 = _S192.y;
                            }
                            _S192 = float2(opacity_0, occlusion_0);
                            break;
                        }
                        _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S192)));
                        break;
                    }
                    texture2d<float, access::sample> _S949 = (&kernelContext_0)->compositeTexture_0;
                    thread uint atlasWidth_9;
                    thread uint atlasHeight_9;
                    (*((&atlasWidth_9)) = (_S949).get_width(0)),(*((&atlasHeight_9)) = (_S949).get_height(0));
                    int3 _S950 = int3(int(0), int(0), int(0));
                    float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S950)).xy), uint(((_S950)).z))) * float4(255.0f) );
                    if((metadata_9.x) == 255.0f)
                    {
                        uint _S951 = atlasWidth_9;
                        uint _S952 = 2U % atlasWidth_9;
                        int _S953 = int(_S952);
                        uint _S954 = 2U / _S951;
                        int3 _S955 = int3(_S953, int(_S954), int(0));
                        int _S956 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S955)).xy), uint(((_S955)).z))).x * 255.0f)));
                        uint _S957 = atlasWidth_9;
                        uint _S958 = 3U % atlasWidth_9;
                        int _S959 = int(_S958);
                        uint _S960 = 3U / _S957;
                        int3 _S961 = int3(_S959, int(_S960), int(0));
                        int2 minimumTile_9 = int2(_S956, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S961)).xy), uint(((_S961)).z))).x * 255.0f))));
                        uint _S962 = atlasWidth_9;
                        uint _S963 = 4U % atlasWidth_9;
                        int _S964 = int(_S963);
                        uint _S965 = 4U / _S962;
                        int3 _S966 = int3(_S964, int(_S965), int(0));
                        uint _S967 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S966)).xy), uint(((_S966)).z))).x * 255.0f));
                        uint _S968 = atlasWidth_9;
                        uint _S969 = 5U % atlasWidth_9;
                        int _S970 = int(_S969);
                        uint _S971 = 5U / _S968;
                        int3 _S972 = int3(_S970, int(_S971), int(0));
                        uint _S973 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S972)).xy), uint(((_S972)).z))).x * 255.0f));
                        uint2 grid_9 = uint2(_S967, _S973);
                        int2 tile_18 = int2(floor(_S192)) - minimumTile_9;
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
                            int3 _S974 = int3(int(1), int(0), int(0));
                            _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S974)).xy), uint(((_S974)).z)));
                            break;
                        }
                        uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S967 + uint(tile_18.x));
                        uint _S975 = atlasWidth_9;
                        uint _S976 = offset_9 % atlasWidth_9;
                        int _S977 = int(_S976);
                        uint _S978 = offset_9 / _S975;
                        int3 _S979 = int3(_S977, int(_S978), int(0));
                        uint _S980 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S979)).xy), uint(((_S979)).z))).x * 255.0f));
                        uint _S981 = atlasWidth_9;
                        uint _S982 = offset_9 + 1U;
                        uint _S983 = _S982 % atlasWidth_9;
                        int _S984 = int(_S983);
                        uint _S985 = _S982 / _S981;
                        int3 _S986 = int3(_S984, int(_S985), int(0));
                        uint slot_18 = _S980 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S986)).xy), uint(((_S986)).z))).x * 255.0f)) << 8U);
                        if(slot_18 == 0U)
                        {
                            int3 _S987 = int3(int(1), int(0), int(0));
                            _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S987)).xy), uint(((_S987)).z)));
                            break;
                        }
                        uint slot_19 = slot_18 - 1U;
                        uint _S988 = atlasWidth_9;
                        uint _S989 = 6U % atlasWidth_9;
                        int _S990 = int(_S989);
                        uint _S991 = 6U / _S988;
                        int3 _S992 = int3(_S990, int(_S991), int(0));
                        uint _S993 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S992)).xy), uint(((_S992)).z))).x * 255.0f));
                        uint _S994 = atlasWidth_9;
                        uint _S995 = 7U % atlasWidth_9;
                        int _S996 = int(_S995);
                        uint _S997 = 7U / _S994;
                        int3 _S998 = int3(_S996, int(_S997), int(0));
                        uint columns_9 = _S993 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S998)).xy), uint(((_S998)).z))).x * 255.0f)) << 8U);
                        uint _S999 = atlasWidth_9;
                        uint _S1000 = 8U % atlasWidth_9;
                        int _S1001 = int(_S1000);
                        uint _S1002 = 8U / _S999;
                        int3 _S1003 = int3(_S1001, int(_S1002), int(0));
                        uint _S1004 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1003)).xy), uint(((_S1003)).z))).x * 255.0f));
                        uint _S1005 = atlasWidth_9;
                        uint _S1006 = 9U % atlasWidth_9;
                        int _S1007 = int(_S1006);
                        uint _S1008 = 9U / _S1005;
                        int3 _S1009 = int3(_S1007, int(_S1008), int(0));
                        uint rows_9 = ((_S1004 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1009)).xy), uint(((_S1009)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                        uint metadataRows_9 = (10U + 2U * _S967 * _S973 + atlasWidth_9 - 1U) / atlasWidth_9;
                        uint _S1010 = atlasWidth_9 / columns_9;
                        float _S1011 = float(_S1010);
                        uint _S1012 = (atlasHeight_9 - metadataRows_9) / rows_9;
                        float2 cellSize_18 = float2(_S1011, float(_S1012));
                        uint _S1013 = slot_19 % columns_9;
                        float _S1014 = float(_S1013);
                        uint _S1015 = slot_19 / columns_9;
                        _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1014, float(_S1015)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(_S192) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                        break;
                    }
                    int2 _S1016 = int2(metadata_9.zw);
                    int2 tile_19 = int2(floor(_S192)) - int2(metadata_9.xy);
                    if(any(tile_19 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_19 >= _S1016);
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1017 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1017)).xy), uint(((_S1017)).z)));
                        break;
                    }
                    uint _S1018 = atlasWidth_9 / uint(_S1016.x);
                    float _S1019 = float(_S1018);
                    uint _S1020 = (atlasHeight_9 - 1U) / uint(_S1016.y);
                    float2 cellSize_19 = float2(_S1019, float(_S1020));
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                uint operation_4 = uint(round(_S944.y));
                if(operation_4 == 1U)
                {
                    _S194 = _S194 * _S379;
                    break;
                }
                if(operation_4 == 2U)
                {
                    _S194 = _S194 + _S379;
                    break;
                }
                if(operation_4 == 3U)
                {
                    _S194 = _S194 - _S379;
                    break;
                }
                if(operation_4 == 4U)
                {
                    float factor_4 = _S944.z;
                    _S194 = _S194 * float4((1.0f - factor_4))  + _S379 * float4(factor_4) ;
                    break;
                }
                break;
            }
            _S4 = saturate(_S194.x);
            break;
        }
        opacity_0 = _S4;
    }
    if((textureMask_0 & 128U) != 0U)
    {
        bool useSecondary_5 = (secondaryMask_0 & 128U) != 0U;
        if((alternateMask_0 & 128U) != 0U)
        {
            if(useSecondary_5)
            {
                _S192 = _S191;
            }
            else
            {
                _S192 = _S190;
            }
        }
        else
        {
            if(useSecondary_5)
            {
                _S192 = _S183;
            }
            else
            {
                _S192 = _S182;
            }
        }
        bool _S1021 = (udimMask_0 & 128U) != 0U;
        uint maskU_5 = uint(round(_S176.y));
        uint maskV_5 = uint(round(_S176.z));
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
        uint _S1022 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1021)
            {
                for(;;)
                {
                    if(_S1022 == 0U)
                    {
                        _S193 = _S192;
                        break;
                    }
                    texture2d<float, access::sample> _S1023 = (&kernelContext_0)->occlusionTexture_0;
                    thread uint width_14;
                    thread uint height_14;
                    (*((&width_14)) = (_S1023).get_width(0)),(*((&height_14)) = (_S1023).get_height(0));
                    float2 size_12 = float2(float(width_14), float(height_14));
                    float2 _S1024 = float2(0.5f) ;
                    float2 remapped_12 = clamp((_S192 * (size_12 - float2(2.0f) ) + float2(1.0f) ) / size_12, _S1024 / size_12, (size_12 - _S1024) / size_12);
                    if((_S1022 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_12.x;
                    }
                    else
                    {
                        occlusion_0 = _S192.x;
                    }
                    if((_S1022 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_12.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S192.y;
                    }
                    _S193 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S194 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S193)));
                break;
            }
            texture2d<float, access::sample> _S1025 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S1025).get_width(0)),(*((&atlasHeight_10)) = (_S1025).get_height(0));
            int3 _S1026 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1026)).xy), uint(((_S1026)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S1027 = atlasWidth_10;
                uint _S1028 = 2U % atlasWidth_10;
                int _S1029 = int(_S1028);
                uint _S1030 = 2U / _S1027;
                int3 _S1031 = int3(_S1029, int(_S1030), int(0));
                int _S1032 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1031)).xy), uint(((_S1031)).z))).x * 255.0f)));
                uint _S1033 = atlasWidth_10;
                uint _S1034 = 3U % atlasWidth_10;
                int _S1035 = int(_S1034);
                uint _S1036 = 3U / _S1033;
                int3 _S1037 = int3(_S1035, int(_S1036), int(0));
                int2 minimumTile_10 = int2(_S1032, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1037)).xy), uint(((_S1037)).z))).x * 255.0f))));
                uint _S1038 = atlasWidth_10;
                uint _S1039 = 4U % atlasWidth_10;
                int _S1040 = int(_S1039);
                uint _S1041 = 4U / _S1038;
                int3 _S1042 = int3(_S1040, int(_S1041), int(0));
                uint _S1043 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1042)).xy), uint(((_S1042)).z))).x * 255.0f));
                uint _S1044 = atlasWidth_10;
                uint _S1045 = 5U % atlasWidth_10;
                int _S1046 = int(_S1045);
                uint _S1047 = 5U / _S1044;
                int3 _S1048 = int3(_S1046, int(_S1047), int(0));
                uint _S1049 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1048)).xy), uint(((_S1048)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S1043, _S1049);
                int2 tile_20 = int2(floor(_S192)) - minimumTile_10;
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
                    _S194 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1050)).xy), uint(((_S1050)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1043 + uint(tile_20.x));
                uint _S1051 = atlasWidth_10;
                uint _S1052 = offset_10 % atlasWidth_10;
                int _S1053 = int(_S1052);
                uint _S1054 = offset_10 / _S1051;
                int3 _S1055 = int3(_S1053, int(_S1054), int(0));
                uint _S1056 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1055)).xy), uint(((_S1055)).z))).x * 255.0f));
                uint _S1057 = atlasWidth_10;
                uint _S1058 = offset_10 + 1U;
                uint _S1059 = _S1058 % atlasWidth_10;
                int _S1060 = int(_S1059);
                uint _S1061 = _S1058 / _S1057;
                int3 _S1062 = int3(_S1060, int(_S1061), int(0));
                uint slot_20 = _S1056 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1062)).xy), uint(((_S1062)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1063 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1063)).xy), uint(((_S1063)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1064 = atlasWidth_10;
                uint _S1065 = 6U % atlasWidth_10;
                int _S1066 = int(_S1065);
                uint _S1067 = 6U / _S1064;
                int3 _S1068 = int3(_S1066, int(_S1067), int(0));
                uint _S1069 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1068)).xy), uint(((_S1068)).z))).x * 255.0f));
                uint _S1070 = atlasWidth_10;
                uint _S1071 = 7U % atlasWidth_10;
                int _S1072 = int(_S1071);
                uint _S1073 = 7U / _S1070;
                int3 _S1074 = int3(_S1072, int(_S1073), int(0));
                uint columns_10 = _S1069 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1074)).xy), uint(((_S1074)).z))).x * 255.0f)) << 8U);
                uint _S1075 = atlasWidth_10;
                uint _S1076 = 8U % atlasWidth_10;
                int _S1077 = int(_S1076);
                uint _S1078 = 8U / _S1075;
                int3 _S1079 = int3(_S1077, int(_S1078), int(0));
                uint _S1080 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1079)).xy), uint(((_S1079)).z))).x * 255.0f));
                uint _S1081 = atlasWidth_10;
                uint _S1082 = 9U % atlasWidth_10;
                int _S1083 = int(_S1082);
                uint _S1084 = 9U / _S1081;
                int3 _S1085 = int3(_S1083, int(_S1084), int(0));
                uint rows_10 = ((_S1080 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1085)).xy), uint(((_S1085)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S1043 * _S1049 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1086 = atlasWidth_10 / columns_10;
                float _S1087 = float(_S1086);
                uint _S1088 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1087, float(_S1088));
                uint _S1089 = slot_21 % columns_10;
                float _S1090 = float(_S1089);
                uint _S1091 = slot_21 / columns_10;
                _S194 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1090, float(_S1091)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(_S192) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1092 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(_S192)) - int2(metadata_10.xy);
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
                _S194 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1093)).xy), uint(((_S1093)).z)));
                break;
            }
            uint _S1094 = atlasWidth_10 / uint(_S1092.x);
            float _S1095 = float(_S1094);
            uint _S1096 = (atlasHeight_10 - 1U) / uint(_S1092.y);
            float2 cellSize_21 = float2(_S1095, float(_S1096));
            _S194 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1097 = float4(_S7->compositeControls_0) ;
            if((_S1097.x) != 128.0f)
            {
                break;
            }
            bool _S1098 = (_S1097.w) >= 0.5f;
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
            uint _S1099 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1098)
                {
                    for(;;)
                    {
                        if(_S1099 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1100 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_15;
                        thread uint height_15;
                        (*((&width_15)) = (_S1100).get_width(0)),(*((&height_15)) = (_S1100).get_height(0));
                        float2 size_13 = float2(float(width_15), float(height_15));
                        float2 _S1101 = float2(0.5f) ;
                        float2 remapped_13 = clamp((_S192 * (size_13 - float2(2.0f) ) + float2(1.0f) ) / size_13, _S1101 / size_13, (size_13 - _S1101) / size_13);
                        if((_S1099 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_13.x;
                        }
                        else
                        {
                            occlusion_0 = _S192.x;
                        }
                        if((_S1099 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_13.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S192.y;
                        }
                        _S192 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S192)));
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
                    int2 tile_22 = int2(floor(_S192)) - minimumTile_11;
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
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1127)).xy), uint(((_S1127)).z)));
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
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1140)).xy), uint(((_S1140)).z)));
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
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1167, float(_S1168)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(_S192) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1169 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(_S192)) - int2(metadata_11.xy);
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
                    _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1170)).xy), uint(((_S1170)).z)));
                    break;
                }
                uint _S1171 = atlasWidth_11 / uint(_S1169.x);
                float _S1172 = float(_S1171);
                uint _S1173 = (atlasHeight_11 - 1U) / uint(_S1169.y);
                float2 cellSize_23 = float2(_S1172, float(_S1173));
                _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_5 = uint(round(_S1097.y));
            if(operation_5 == 1U)
            {
                _S194 = _S194 * _S379;
                break;
            }
            if(operation_5 == 2U)
            {
                _S194 = _S194 + _S379;
                break;
            }
            if(operation_5 == 3U)
            {
                _S194 = _S194 - _S379;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1097.z;
                _S194 = _S194 * float4((1.0f - factor_5))  + _S379 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S194.x);
    }
    else
    {
        occlusion_0 = _S162;
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
                _S192 = _S191;
            }
            else
            {
                _S192 = _S190;
            }
        }
        else
        {
            if(useSecondary_6)
            {
                _S192 = _S183;
            }
            else
            {
                _S192 = _S182;
            }
        }
        bool _S1174 = (udimMask_0 & 256U) != 0U;
        uint maskU_6 = uint(round(_S176.y));
        uint maskV_6 = uint(round(_S176.z));
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
        uint _S1175 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1174)
            {
                for(;;)
                {
                    if(_S1175 == 0U)
                    {
                        _S193 = _S192;
                        break;
                    }
                    texture2d<float, access::sample> _S1176 = (&kernelContext_0)->specularColorTexture_0;
                    thread uint width_16;
                    thread uint height_16;
                    (*((&width_16)) = (_S1176).get_width(0)),(*((&height_16)) = (_S1176).get_height(0));
                    float2 size_14 = float2(float(width_16), float(height_16));
                    float2 _S1177 = float2(0.5f) ;
                    float2 remapped_14 = clamp((_S192 * (size_14 - float2(2.0f) ) + float2(1.0f) ) / size_14, _S1177 / size_14, (size_14 - _S1177) / size_14);
                    if((_S1175 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_14.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S192.x;
                    }
                    if((_S1175 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_14.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S192.y;
                    }
                    _S193 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S194 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S193)));
                break;
            }
            texture2d<float, access::sample> _S1178 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1178).get_width(0)),(*((&atlasHeight_12)) = (_S1178).get_height(0));
            int3 _S1179 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1179)).xy), uint(((_S1179)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1180 = atlasWidth_12;
                uint _S1181 = 2U % atlasWidth_12;
                int _S1182 = int(_S1181);
                uint _S1183 = 2U / _S1180;
                int3 _S1184 = int3(_S1182, int(_S1183), int(0));
                int _S1185 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1184)).xy), uint(((_S1184)).z))).x * 255.0f)));
                uint _S1186 = atlasWidth_12;
                uint _S1187 = 3U % atlasWidth_12;
                int _S1188 = int(_S1187);
                uint _S1189 = 3U / _S1186;
                int3 _S1190 = int3(_S1188, int(_S1189), int(0));
                int2 minimumTile_12 = int2(_S1185, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1190)).xy), uint(((_S1190)).z))).x * 255.0f))));
                uint _S1191 = atlasWidth_12;
                uint _S1192 = 4U % atlasWidth_12;
                int _S1193 = int(_S1192);
                uint _S1194 = 4U / _S1191;
                int3 _S1195 = int3(_S1193, int(_S1194), int(0));
                uint _S1196 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1195)).xy), uint(((_S1195)).z))).x * 255.0f));
                uint _S1197 = atlasWidth_12;
                uint _S1198 = 5U % atlasWidth_12;
                int _S1199 = int(_S1198);
                uint _S1200 = 5U / _S1197;
                int3 _S1201 = int3(_S1199, int(_S1200), int(0));
                uint _S1202 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1201)).xy), uint(((_S1201)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1196, _S1202);
                int2 tile_24 = int2(floor(_S192)) - minimumTile_12;
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
                    int3 _S1203 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1203)).xy), uint(((_S1203)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1196 + uint(tile_24.x));
                uint _S1204 = atlasWidth_12;
                uint _S1205 = offset_12 % atlasWidth_12;
                int _S1206 = int(_S1205);
                uint _S1207 = offset_12 / _S1204;
                int3 _S1208 = int3(_S1206, int(_S1207), int(0));
                uint _S1209 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1208)).xy), uint(((_S1208)).z))).x * 255.0f));
                uint _S1210 = atlasWidth_12;
                uint _S1211 = offset_12 + 1U;
                uint _S1212 = _S1211 % atlasWidth_12;
                int _S1213 = int(_S1212);
                uint _S1214 = _S1211 / _S1210;
                int3 _S1215 = int3(_S1213, int(_S1214), int(0));
                uint slot_24 = _S1209 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1215)).xy), uint(((_S1215)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1216 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1216)).xy), uint(((_S1216)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1217 = atlasWidth_12;
                uint _S1218 = 6U % atlasWidth_12;
                int _S1219 = int(_S1218);
                uint _S1220 = 6U / _S1217;
                int3 _S1221 = int3(_S1219, int(_S1220), int(0));
                uint _S1222 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1221)).xy), uint(((_S1221)).z))).x * 255.0f));
                uint _S1223 = atlasWidth_12;
                uint _S1224 = 7U % atlasWidth_12;
                int _S1225 = int(_S1224);
                uint _S1226 = 7U / _S1223;
                int3 _S1227 = int3(_S1225, int(_S1226), int(0));
                uint columns_12 = _S1222 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1227)).xy), uint(((_S1227)).z))).x * 255.0f)) << 8U);
                uint _S1228 = atlasWidth_12;
                uint _S1229 = 8U % atlasWidth_12;
                int _S1230 = int(_S1229);
                uint _S1231 = 8U / _S1228;
                int3 _S1232 = int3(_S1230, int(_S1231), int(0));
                uint _S1233 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1232)).xy), uint(((_S1232)).z))).x * 255.0f));
                uint _S1234 = atlasWidth_12;
                uint _S1235 = 9U % atlasWidth_12;
                int _S1236 = int(_S1235);
                uint _S1237 = 9U / _S1234;
                int3 _S1238 = int3(_S1236, int(_S1237), int(0));
                uint rows_12 = ((_S1233 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1238)).xy), uint(((_S1238)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1196 * _S1202 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1239 = atlasWidth_12 / columns_12;
                float _S1240 = float(_S1239);
                uint _S1241 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1240, float(_S1241));
                uint _S1242 = slot_25 % columns_12;
                float _S1243 = float(_S1242);
                uint _S1244 = slot_25 / columns_12;
                _S194 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1243, float(_S1244)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(_S192) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1245 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(_S192)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1245);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1246 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S194 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1246)).xy), uint(((_S1246)).z)));
                break;
            }
            uint _S1247 = atlasWidth_12 / uint(_S1245.x);
            float _S1248 = float(_S1247);
            uint _S1249 = (atlasHeight_12 - 1U) / uint(_S1245.y);
            float2 cellSize_25 = float2(_S1248, float(_S1249));
            _S194 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1250 = float4(_S7->compositeControls_0) ;
            if((_S1250.x) != 256.0f)
            {
                break;
            }
            bool _S1251 = (_S1250.w) >= 0.5f;
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
            uint _S1252 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1251)
                {
                    for(;;)
                    {
                        if(_S1252 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1253 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_17;
                        thread uint height_17;
                        (*((&width_17)) = (_S1253).get_width(0)),(*((&height_17)) = (_S1253).get_height(0));
                        float2 size_15 = float2(float(width_17), float(height_17));
                        float2 _S1254 = float2(0.5f) ;
                        float2 remapped_15 = clamp((_S192 * (size_15 - float2(2.0f) ) + float2(1.0f) ) / size_15, _S1254 / size_15, (size_15 - _S1254) / size_15);
                        if((_S1252 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_15.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S192.x;
                        }
                        if((_S1252 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_15.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S192.y;
                        }
                        _S192 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S192)));
                    break;
                }
                texture2d<float, access::sample> _S1255 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1255).get_width(0)),(*((&atlasHeight_13)) = (_S1255).get_height(0));
                int3 _S1256 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1256)).xy), uint(((_S1256)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1257 = atlasWidth_13;
                    uint _S1258 = 2U % atlasWidth_13;
                    int _S1259 = int(_S1258);
                    uint _S1260 = 2U / _S1257;
                    int3 _S1261 = int3(_S1259, int(_S1260), int(0));
                    int _S1262 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1261)).xy), uint(((_S1261)).z))).x * 255.0f)));
                    uint _S1263 = atlasWidth_13;
                    uint _S1264 = 3U % atlasWidth_13;
                    int _S1265 = int(_S1264);
                    uint _S1266 = 3U / _S1263;
                    int3 _S1267 = int3(_S1265, int(_S1266), int(0));
                    int2 minimumTile_13 = int2(_S1262, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1267)).xy), uint(((_S1267)).z))).x * 255.0f))));
                    uint _S1268 = atlasWidth_13;
                    uint _S1269 = 4U % atlasWidth_13;
                    int _S1270 = int(_S1269);
                    uint _S1271 = 4U / _S1268;
                    int3 _S1272 = int3(_S1270, int(_S1271), int(0));
                    uint _S1273 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1272)).xy), uint(((_S1272)).z))).x * 255.0f));
                    uint _S1274 = atlasWidth_13;
                    uint _S1275 = 5U % atlasWidth_13;
                    int _S1276 = int(_S1275);
                    uint _S1277 = 5U / _S1274;
                    int3 _S1278 = int3(_S1276, int(_S1277), int(0));
                    uint _S1279 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1278)).xy), uint(((_S1278)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1273, _S1279);
                    int2 tile_26 = int2(floor(_S192)) - minimumTile_13;
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
                        int3 _S1280 = int3(int(1), int(0), int(0));
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1280)).xy), uint(((_S1280)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1273 + uint(tile_26.x));
                    uint _S1281 = atlasWidth_13;
                    uint _S1282 = offset_13 % atlasWidth_13;
                    int _S1283 = int(_S1282);
                    uint _S1284 = offset_13 / _S1281;
                    int3 _S1285 = int3(_S1283, int(_S1284), int(0));
                    uint _S1286 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1285)).xy), uint(((_S1285)).z))).x * 255.0f));
                    uint _S1287 = atlasWidth_13;
                    uint _S1288 = offset_13 + 1U;
                    uint _S1289 = _S1288 % atlasWidth_13;
                    int _S1290 = int(_S1289);
                    uint _S1291 = _S1288 / _S1287;
                    int3 _S1292 = int3(_S1290, int(_S1291), int(0));
                    uint slot_26 = _S1286 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1292)).xy), uint(((_S1292)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1293 = int3(int(1), int(0), int(0));
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1293)).xy), uint(((_S1293)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1294 = atlasWidth_13;
                    uint _S1295 = 6U % atlasWidth_13;
                    int _S1296 = int(_S1295);
                    uint _S1297 = 6U / _S1294;
                    int3 _S1298 = int3(_S1296, int(_S1297), int(0));
                    uint _S1299 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1298)).xy), uint(((_S1298)).z))).x * 255.0f));
                    uint _S1300 = atlasWidth_13;
                    uint _S1301 = 7U % atlasWidth_13;
                    int _S1302 = int(_S1301);
                    uint _S1303 = 7U / _S1300;
                    int3 _S1304 = int3(_S1302, int(_S1303), int(0));
                    uint columns_13 = _S1299 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1304)).xy), uint(((_S1304)).z))).x * 255.0f)) << 8U);
                    uint _S1305 = atlasWidth_13;
                    uint _S1306 = 8U % atlasWidth_13;
                    int _S1307 = int(_S1306);
                    uint _S1308 = 8U / _S1305;
                    int3 _S1309 = int3(_S1307, int(_S1308), int(0));
                    uint _S1310 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1309)).xy), uint(((_S1309)).z))).x * 255.0f));
                    uint _S1311 = atlasWidth_13;
                    uint _S1312 = 9U % atlasWidth_13;
                    int _S1313 = int(_S1312);
                    uint _S1314 = 9U / _S1311;
                    int3 _S1315 = int3(_S1313, int(_S1314), int(0));
                    uint rows_13 = ((_S1310 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1315)).xy), uint(((_S1315)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1273 * _S1279 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1316 = atlasWidth_13 / columns_13;
                    float _S1317 = float(_S1316);
                    uint _S1318 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1317, float(_S1318));
                    uint _S1319 = slot_27 % columns_13;
                    float _S1320 = float(_S1319);
                    uint _S1321 = slot_27 / columns_13;
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1320, float(_S1321)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(_S192) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1322 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(_S192)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1322);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1323 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1323)).xy), uint(((_S1323)).z)));
                    break;
                }
                uint _S1324 = atlasWidth_13 / uint(_S1322.x);
                float _S1325 = float(_S1324);
                uint _S1326 = (atlasHeight_13 - 1U) / uint(_S1322.y);
                float2 cellSize_27 = float2(_S1325, float(_S1326));
                _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_6 = uint(round(_S1250.y));
            if(operation_6 == 1U)
            {
                _S194 = _S194 * _S379;
                break;
            }
            if(operation_6 == 2U)
            {
                _S194 = _S194 + _S379;
                break;
            }
            if(operation_6 == 3U)
            {
                _S194 = _S194 - _S379;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1250.z;
                _S194 = _S194 * float4((1.0f - factor_6))  + _S379 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S194.xyz);
    }
    else
    {
        specularColor_0 = _S170;
    }
    if((textureMask_0 & 512U) != 0U)
    {
        bool useSecondary_7 = (secondaryMask_0 & 512U) != 0U;
        if((alternateMask_0 & 512U) != 0U)
        {
            if(useSecondary_7)
            {
                _S192 = _S191;
            }
            else
            {
                _S192 = _S190;
            }
        }
        else
        {
            if(useSecondary_7)
            {
                _S192 = _S183;
            }
            else
            {
                _S192 = _S182;
            }
        }
        bool _S1327 = (udimMask_0 & 512U) != 0U;
        uint maskU_7 = uint(round(_S176.y));
        uint maskV_7 = uint(round(_S176.z));
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
        uint _S1328 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1327)
            {
                for(;;)
                {
                    if(_S1328 == 0U)
                    {
                        _S193 = _S192;
                        break;
                    }
                    texture2d<float, access::sample> _S1329 = (&kernelContext_0)->clearcoatTexture_0;
                    thread uint width_18;
                    thread uint height_18;
                    (*((&width_18)) = (_S1329).get_width(0)),(*((&height_18)) = (_S1329).get_height(0));
                    float2 size_16 = float2(float(width_18), float(height_18));
                    float2 _S1330 = float2(0.5f) ;
                    float2 remapped_16 = clamp((_S192 * (size_16 - float2(2.0f) ) + float2(1.0f) ) / size_16, _S1330 / size_16, (size_16 - _S1330) / size_16);
                    if((_S1328 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_16.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S192.x;
                    }
                    if((_S1328 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_16.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S192.y;
                    }
                    _S193 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S194 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S193)));
                break;
            }
            texture2d<float, access::sample> _S1331 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1331).get_width(0)),(*((&atlasHeight_14)) = (_S1331).get_height(0));
            int3 _S1332 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1332)).xy), uint(((_S1332)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1333 = atlasWidth_14;
                uint _S1334 = 2U % atlasWidth_14;
                int _S1335 = int(_S1334);
                uint _S1336 = 2U / _S1333;
                int3 _S1337 = int3(_S1335, int(_S1336), int(0));
                int _S1338 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1337)).xy), uint(((_S1337)).z))).x * 255.0f)));
                uint _S1339 = atlasWidth_14;
                uint _S1340 = 3U % atlasWidth_14;
                int _S1341 = int(_S1340);
                uint _S1342 = 3U / _S1339;
                int3 _S1343 = int3(_S1341, int(_S1342), int(0));
                int2 minimumTile_14 = int2(_S1338, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1343)).xy), uint(((_S1343)).z))).x * 255.0f))));
                uint _S1344 = atlasWidth_14;
                uint _S1345 = 4U % atlasWidth_14;
                int _S1346 = int(_S1345);
                uint _S1347 = 4U / _S1344;
                int3 _S1348 = int3(_S1346, int(_S1347), int(0));
                uint _S1349 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1348)).xy), uint(((_S1348)).z))).x * 255.0f));
                uint _S1350 = atlasWidth_14;
                uint _S1351 = 5U % atlasWidth_14;
                int _S1352 = int(_S1351);
                uint _S1353 = 5U / _S1350;
                int3 _S1354 = int3(_S1352, int(_S1353), int(0));
                uint _S1355 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1354)).xy), uint(((_S1354)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1349, _S1355);
                int2 tile_28 = int2(floor(_S192)) - minimumTile_14;
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
                    int3 _S1356 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1356)).xy), uint(((_S1356)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1349 + uint(tile_28.x));
                uint _S1357 = atlasWidth_14;
                uint _S1358 = offset_14 % atlasWidth_14;
                int _S1359 = int(_S1358);
                uint _S1360 = offset_14 / _S1357;
                int3 _S1361 = int3(_S1359, int(_S1360), int(0));
                uint _S1362 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1361)).xy), uint(((_S1361)).z))).x * 255.0f));
                uint _S1363 = atlasWidth_14;
                uint _S1364 = offset_14 + 1U;
                uint _S1365 = _S1364 % atlasWidth_14;
                int _S1366 = int(_S1365);
                uint _S1367 = _S1364 / _S1363;
                int3 _S1368 = int3(_S1366, int(_S1367), int(0));
                uint slot_28 = _S1362 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1368)).xy), uint(((_S1368)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1369 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1369)).xy), uint(((_S1369)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1370 = atlasWidth_14;
                uint _S1371 = 6U % atlasWidth_14;
                int _S1372 = int(_S1371);
                uint _S1373 = 6U / _S1370;
                int3 _S1374 = int3(_S1372, int(_S1373), int(0));
                uint _S1375 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1374)).xy), uint(((_S1374)).z))).x * 255.0f));
                uint _S1376 = atlasWidth_14;
                uint _S1377 = 7U % atlasWidth_14;
                int _S1378 = int(_S1377);
                uint _S1379 = 7U / _S1376;
                int3 _S1380 = int3(_S1378, int(_S1379), int(0));
                uint columns_14 = _S1375 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1380)).xy), uint(((_S1380)).z))).x * 255.0f)) << 8U);
                uint _S1381 = atlasWidth_14;
                uint _S1382 = 8U % atlasWidth_14;
                int _S1383 = int(_S1382);
                uint _S1384 = 8U / _S1381;
                int3 _S1385 = int3(_S1383, int(_S1384), int(0));
                uint _S1386 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1385)).xy), uint(((_S1385)).z))).x * 255.0f));
                uint _S1387 = atlasWidth_14;
                uint _S1388 = 9U % atlasWidth_14;
                int _S1389 = int(_S1388);
                uint _S1390 = 9U / _S1387;
                int3 _S1391 = int3(_S1389, int(_S1390), int(0));
                uint rows_14 = ((_S1386 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1391)).xy), uint(((_S1391)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1349 * _S1355 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1392 = atlasWidth_14 / columns_14;
                float _S1393 = float(_S1392);
                uint _S1394 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1393, float(_S1394));
                uint _S1395 = slot_29 % columns_14;
                float _S1396 = float(_S1395);
                uint _S1397 = slot_29 / columns_14;
                _S194 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1396, float(_S1397)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(_S192) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1398 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(_S192)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1398);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1399 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S194 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1399)).xy), uint(((_S1399)).z)));
                break;
            }
            uint _S1400 = atlasWidth_14 / uint(_S1398.x);
            float _S1401 = float(_S1400);
            uint _S1402 = (atlasHeight_14 - 1U) / uint(_S1398.y);
            float2 cellSize_29 = float2(_S1401, float(_S1402));
            _S194 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1403 = float4(_S7->compositeControls_0) ;
            if((_S1403.x) != 512.0f)
            {
                break;
            }
            bool _S1404 = (_S1403.w) >= 0.5f;
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
            uint _S1405 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1404)
                {
                    for(;;)
                    {
                        if(_S1405 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1406 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_19;
                        thread uint height_19;
                        (*((&width_19)) = (_S1406).get_width(0)),(*((&height_19)) = (_S1406).get_height(0));
                        float2 size_17 = float2(float(width_19), float(height_19));
                        float2 _S1407 = float2(0.5f) ;
                        float2 remapped_17 = clamp((_S192 * (size_17 - float2(2.0f) ) + float2(1.0f) ) / size_17, _S1407 / size_17, (size_17 - _S1407) / size_17);
                        if((_S1405 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_17.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S192.x;
                        }
                        if((_S1405 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_17.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S192.y;
                        }
                        _S192 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S192)));
                    break;
                }
                texture2d<float, access::sample> _S1408 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1408).get_width(0)),(*((&atlasHeight_15)) = (_S1408).get_height(0));
                int3 _S1409 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1409)).xy), uint(((_S1409)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1410 = atlasWidth_15;
                    uint _S1411 = 2U % atlasWidth_15;
                    int _S1412 = int(_S1411);
                    uint _S1413 = 2U / _S1410;
                    int3 _S1414 = int3(_S1412, int(_S1413), int(0));
                    int _S1415 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1414)).xy), uint(((_S1414)).z))).x * 255.0f)));
                    uint _S1416 = atlasWidth_15;
                    uint _S1417 = 3U % atlasWidth_15;
                    int _S1418 = int(_S1417);
                    uint _S1419 = 3U / _S1416;
                    int3 _S1420 = int3(_S1418, int(_S1419), int(0));
                    int2 minimumTile_15 = int2(_S1415, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1420)).xy), uint(((_S1420)).z))).x * 255.0f))));
                    uint _S1421 = atlasWidth_15;
                    uint _S1422 = 4U % atlasWidth_15;
                    int _S1423 = int(_S1422);
                    uint _S1424 = 4U / _S1421;
                    int3 _S1425 = int3(_S1423, int(_S1424), int(0));
                    uint _S1426 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1425)).xy), uint(((_S1425)).z))).x * 255.0f));
                    uint _S1427 = atlasWidth_15;
                    uint _S1428 = 5U % atlasWidth_15;
                    int _S1429 = int(_S1428);
                    uint _S1430 = 5U / _S1427;
                    int3 _S1431 = int3(_S1429, int(_S1430), int(0));
                    uint _S1432 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1431)).xy), uint(((_S1431)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1426, _S1432);
                    int2 tile_30 = int2(floor(_S192)) - minimumTile_15;
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
                        int3 _S1433 = int3(int(1), int(0), int(0));
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1433)).xy), uint(((_S1433)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1426 + uint(tile_30.x));
                    uint _S1434 = atlasWidth_15;
                    uint _S1435 = offset_15 % atlasWidth_15;
                    int _S1436 = int(_S1435);
                    uint _S1437 = offset_15 / _S1434;
                    int3 _S1438 = int3(_S1436, int(_S1437), int(0));
                    uint _S1439 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1438)).xy), uint(((_S1438)).z))).x * 255.0f));
                    uint _S1440 = atlasWidth_15;
                    uint _S1441 = offset_15 + 1U;
                    uint _S1442 = _S1441 % atlasWidth_15;
                    int _S1443 = int(_S1442);
                    uint _S1444 = _S1441 / _S1440;
                    int3 _S1445 = int3(_S1443, int(_S1444), int(0));
                    uint slot_30 = _S1439 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1445)).xy), uint(((_S1445)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1446 = int3(int(1), int(0), int(0));
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1446)).xy), uint(((_S1446)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1447 = atlasWidth_15;
                    uint _S1448 = 6U % atlasWidth_15;
                    int _S1449 = int(_S1448);
                    uint _S1450 = 6U / _S1447;
                    int3 _S1451 = int3(_S1449, int(_S1450), int(0));
                    uint _S1452 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1451)).xy), uint(((_S1451)).z))).x * 255.0f));
                    uint _S1453 = atlasWidth_15;
                    uint _S1454 = 7U % atlasWidth_15;
                    int _S1455 = int(_S1454);
                    uint _S1456 = 7U / _S1453;
                    int3 _S1457 = int3(_S1455, int(_S1456), int(0));
                    uint columns_15 = _S1452 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1457)).xy), uint(((_S1457)).z))).x * 255.0f)) << 8U);
                    uint _S1458 = atlasWidth_15;
                    uint _S1459 = 8U % atlasWidth_15;
                    int _S1460 = int(_S1459);
                    uint _S1461 = 8U / _S1458;
                    int3 _S1462 = int3(_S1460, int(_S1461), int(0));
                    uint _S1463 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1462)).xy), uint(((_S1462)).z))).x * 255.0f));
                    uint _S1464 = atlasWidth_15;
                    uint _S1465 = 9U % atlasWidth_15;
                    int _S1466 = int(_S1465);
                    uint _S1467 = 9U / _S1464;
                    int3 _S1468 = int3(_S1466, int(_S1467), int(0));
                    uint rows_15 = ((_S1463 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1468)).xy), uint(((_S1468)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1426 * _S1432 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1469 = atlasWidth_15 / columns_15;
                    float _S1470 = float(_S1469);
                    uint _S1471 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1470, float(_S1471));
                    uint _S1472 = slot_31 % columns_15;
                    float _S1473 = float(_S1472);
                    uint _S1474 = slot_31 / columns_15;
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1473, float(_S1474)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(_S192) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1475 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(_S192)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1475);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1476 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1476)).xy), uint(((_S1476)).z)));
                    break;
                }
                uint _S1477 = atlasWidth_15 / uint(_S1475.x);
                float _S1478 = float(_S1477);
                uint _S1479 = (atlasHeight_15 - 1U) / uint(_S1475.y);
                float2 cellSize_31 = float2(_S1478, float(_S1479));
                _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_7 = uint(round(_S1403.y));
            if(operation_7 == 1U)
            {
                _S194 = _S194 * _S379;
                break;
            }
            if(operation_7 == 2U)
            {
                _S194 = _S194 + _S379;
                break;
            }
            if(operation_7 == 3U)
            {
                _S194 = _S194 - _S379;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1403.z;
                _S194 = _S194 * float4((1.0f - factor_7))  + _S379 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S194.x);
    }
    else
    {
        clearcoatAmount_0 = _S172;
    }
    float ior_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool useSecondary_8 = (secondaryMask_0 & 1024U) != 0U;
        if((alternateMask_0 & 1024U) != 0U)
        {
            if(useSecondary_8)
            {
                _S192 = _S191;
            }
            else
            {
                _S192 = _S190;
            }
        }
        else
        {
            if(useSecondary_8)
            {
                _S192 = _S183;
            }
            else
            {
                _S192 = _S182;
            }
        }
        bool _S1480 = (udimMask_0 & 1024U) != 0U;
        uint maskU_8 = uint(round(_S176.y));
        uint maskV_8 = uint(round(_S176.z));
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
        uint _S1481 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1480)
            {
                for(;;)
                {
                    if(_S1481 == 0U)
                    {
                        _S193 = _S192;
                        break;
                    }
                    texture2d<float, access::sample> _S1482 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
                    thread uint width_20;
                    thread uint height_20;
                    (*((&width_20)) = (_S1482).get_width(0)),(*((&height_20)) = (_S1482).get_height(0));
                    float2 size_18 = float2(float(width_20), float(height_20));
                    float2 _S1483 = float2(0.5f) ;
                    float2 remapped_18 = clamp((_S192 * (size_18 - float2(2.0f) ) + float2(1.0f) ) / size_18, _S1483 / size_18, (size_18 - _S1483) / size_18);
                    if((_S1481 & 1U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_18.x;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S192.x;
                    }
                    if((_S1481 & 2U) != 0U)
                    {
                        ior_0 = remapped_18.y;
                    }
                    else
                    {
                        ior_0 = _S192.y;
                    }
                    _S193 = float2(clearcoatRoughness_0, ior_0);
                    break;
                }
                _S194 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S193)));
                break;
            }
            texture2d<float, access::sample> _S1484 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1484).get_width(0)),(*((&atlasHeight_16)) = (_S1484).get_height(0));
            int3 _S1485 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1485)).xy), uint(((_S1485)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1486 = atlasWidth_16;
                uint _S1487 = 2U % atlasWidth_16;
                int _S1488 = int(_S1487);
                uint _S1489 = 2U / _S1486;
                int3 _S1490 = int3(_S1488, int(_S1489), int(0));
                int _S1491 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1490)).xy), uint(((_S1490)).z))).x * 255.0f)));
                uint _S1492 = atlasWidth_16;
                uint _S1493 = 3U % atlasWidth_16;
                int _S1494 = int(_S1493);
                uint _S1495 = 3U / _S1492;
                int3 _S1496 = int3(_S1494, int(_S1495), int(0));
                int2 minimumTile_16 = int2(_S1491, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1496)).xy), uint(((_S1496)).z))).x * 255.0f))));
                uint _S1497 = atlasWidth_16;
                uint _S1498 = 4U % atlasWidth_16;
                int _S1499 = int(_S1498);
                uint _S1500 = 4U / _S1497;
                int3 _S1501 = int3(_S1499, int(_S1500), int(0));
                uint _S1502 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1501)).xy), uint(((_S1501)).z))).x * 255.0f));
                uint _S1503 = atlasWidth_16;
                uint _S1504 = 5U % atlasWidth_16;
                int _S1505 = int(_S1504);
                uint _S1506 = 5U / _S1503;
                int3 _S1507 = int3(_S1505, int(_S1506), int(0));
                uint _S1508 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1507)).xy), uint(((_S1507)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1502, _S1508);
                int2 tile_32 = int2(floor(_S192)) - minimumTile_16;
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
                    int3 _S1509 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1509)).xy), uint(((_S1509)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1502 + uint(tile_32.x));
                uint _S1510 = atlasWidth_16;
                uint _S1511 = offset_16 % atlasWidth_16;
                int _S1512 = int(_S1511);
                uint _S1513 = offset_16 / _S1510;
                int3 _S1514 = int3(_S1512, int(_S1513), int(0));
                uint _S1515 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1514)).xy), uint(((_S1514)).z))).x * 255.0f));
                uint _S1516 = atlasWidth_16;
                uint _S1517 = offset_16 + 1U;
                uint _S1518 = _S1517 % atlasWidth_16;
                int _S1519 = int(_S1518);
                uint _S1520 = _S1517 / _S1516;
                int3 _S1521 = int3(_S1519, int(_S1520), int(0));
                uint slot_32 = _S1515 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1521)).xy), uint(((_S1521)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1522 = int3(int(1), int(0), int(0));
                    _S194 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1522)).xy), uint(((_S1522)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1523 = atlasWidth_16;
                uint _S1524 = 6U % atlasWidth_16;
                int _S1525 = int(_S1524);
                uint _S1526 = 6U / _S1523;
                int3 _S1527 = int3(_S1525, int(_S1526), int(0));
                uint _S1528 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1527)).xy), uint(((_S1527)).z))).x * 255.0f));
                uint _S1529 = atlasWidth_16;
                uint _S1530 = 7U % atlasWidth_16;
                int _S1531 = int(_S1530);
                uint _S1532 = 7U / _S1529;
                int3 _S1533 = int3(_S1531, int(_S1532), int(0));
                uint columns_16 = _S1528 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1533)).xy), uint(((_S1533)).z))).x * 255.0f)) << 8U);
                uint _S1534 = atlasWidth_16;
                uint _S1535 = 8U % atlasWidth_16;
                int _S1536 = int(_S1535);
                uint _S1537 = 8U / _S1534;
                int3 _S1538 = int3(_S1536, int(_S1537), int(0));
                uint _S1539 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1538)).xy), uint(((_S1538)).z))).x * 255.0f));
                uint _S1540 = atlasWidth_16;
                uint _S1541 = 9U % atlasWidth_16;
                int _S1542 = int(_S1541);
                uint _S1543 = 9U / _S1540;
                int3 _S1544 = int3(_S1542, int(_S1543), int(0));
                uint rows_16 = ((_S1539 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1544)).xy), uint(((_S1544)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1502 * _S1508 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1545 = atlasWidth_16 / columns_16;
                float _S1546 = float(_S1545);
                uint _S1547 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1546, float(_S1547));
                uint _S1548 = slot_33 % columns_16;
                float _S1549 = float(_S1548);
                uint _S1550 = slot_33 / columns_16;
                _S194 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1549, float(_S1550)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(_S192) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1551 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(_S192)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1551);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1552 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S194 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1552)).xy), uint(((_S1552)).z)));
                break;
            }
            uint _S1553 = atlasWidth_16 / uint(_S1551.x);
            float _S1554 = float(_S1553);
            uint _S1555 = (atlasHeight_16 - 1U) / uint(_S1551.y);
            float2 cellSize_33 = float2(_S1554, float(_S1555));
            _S194 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1556 = float4(_S7->compositeControls_0) ;
            if((_S1556.x) != 1024.0f)
            {
                break;
            }
            bool _S1557 = (_S1556.w) >= 0.5f;
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
            uint _S1558 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1557)
                {
                    for(;;)
                    {
                        if(_S1558 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1559 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_21;
                        thread uint height_21;
                        (*((&width_21)) = (_S1559).get_width(0)),(*((&height_21)) = (_S1559).get_height(0));
                        float2 size_19 = float2(float(width_21), float(height_21));
                        float2 _S1560 = float2(0.5f) ;
                        float2 remapped_19 = clamp((_S192 * (size_19 - float2(2.0f) ) + float2(1.0f) ) / size_19, _S1560 / size_19, (size_19 - _S1560) / size_19);
                        if((_S1558 & 1U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_19.x;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S192.x;
                        }
                        if((_S1558 & 2U) != 0U)
                        {
                            ior_0 = remapped_19.y;
                        }
                        else
                        {
                            ior_0 = _S192.y;
                        }
                        _S192 = float2(clearcoatRoughness_0, ior_0);
                        break;
                    }
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S192)));
                    break;
                }
                texture2d<float, access::sample> _S1561 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1561).get_width(0)),(*((&atlasHeight_17)) = (_S1561).get_height(0));
                int3 _S1562 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1562)).xy), uint(((_S1562)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1563 = atlasWidth_17;
                    uint _S1564 = 2U % atlasWidth_17;
                    int _S1565 = int(_S1564);
                    uint _S1566 = 2U / _S1563;
                    int3 _S1567 = int3(_S1565, int(_S1566), int(0));
                    int _S1568 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1567)).xy), uint(((_S1567)).z))).x * 255.0f)));
                    uint _S1569 = atlasWidth_17;
                    uint _S1570 = 3U % atlasWidth_17;
                    int _S1571 = int(_S1570);
                    uint _S1572 = 3U / _S1569;
                    int3 _S1573 = int3(_S1571, int(_S1572), int(0));
                    int2 minimumTile_17 = int2(_S1568, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1573)).xy), uint(((_S1573)).z))).x * 255.0f))));
                    uint _S1574 = atlasWidth_17;
                    uint _S1575 = 4U % atlasWidth_17;
                    int _S1576 = int(_S1575);
                    uint _S1577 = 4U / _S1574;
                    int3 _S1578 = int3(_S1576, int(_S1577), int(0));
                    uint _S1579 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1578)).xy), uint(((_S1578)).z))).x * 255.0f));
                    uint _S1580 = atlasWidth_17;
                    uint _S1581 = 5U % atlasWidth_17;
                    int _S1582 = int(_S1581);
                    uint _S1583 = 5U / _S1580;
                    int3 _S1584 = int3(_S1582, int(_S1583), int(0));
                    uint _S1585 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1584)).xy), uint(((_S1584)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1579, _S1585);
                    int2 tile_34 = int2(floor(_S192)) - minimumTile_17;
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
                        int3 _S1586 = int3(int(1), int(0), int(0));
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1586)).xy), uint(((_S1586)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1579 + uint(tile_34.x));
                    uint _S1587 = atlasWidth_17;
                    uint _S1588 = offset_17 % atlasWidth_17;
                    int _S1589 = int(_S1588);
                    uint _S1590 = offset_17 / _S1587;
                    int3 _S1591 = int3(_S1589, int(_S1590), int(0));
                    uint _S1592 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1591)).xy), uint(((_S1591)).z))).x * 255.0f));
                    uint _S1593 = atlasWidth_17;
                    uint _S1594 = offset_17 + 1U;
                    uint _S1595 = _S1594 % atlasWidth_17;
                    int _S1596 = int(_S1595);
                    uint _S1597 = _S1594 / _S1593;
                    int3 _S1598 = int3(_S1596, int(_S1597), int(0));
                    uint slot_34 = _S1592 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1598)).xy), uint(((_S1598)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1599 = int3(int(1), int(0), int(0));
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1599)).xy), uint(((_S1599)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1600 = atlasWidth_17;
                    uint _S1601 = 6U % atlasWidth_17;
                    int _S1602 = int(_S1601);
                    uint _S1603 = 6U / _S1600;
                    int3 _S1604 = int3(_S1602, int(_S1603), int(0));
                    uint _S1605 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1604)).xy), uint(((_S1604)).z))).x * 255.0f));
                    uint _S1606 = atlasWidth_17;
                    uint _S1607 = 7U % atlasWidth_17;
                    int _S1608 = int(_S1607);
                    uint _S1609 = 7U / _S1606;
                    int3 _S1610 = int3(_S1608, int(_S1609), int(0));
                    uint columns_17 = _S1605 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1610)).xy), uint(((_S1610)).z))).x * 255.0f)) << 8U);
                    uint _S1611 = atlasWidth_17;
                    uint _S1612 = 8U % atlasWidth_17;
                    int _S1613 = int(_S1612);
                    uint _S1614 = 8U / _S1611;
                    int3 _S1615 = int3(_S1613, int(_S1614), int(0));
                    uint _S1616 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1615)).xy), uint(((_S1615)).z))).x * 255.0f));
                    uint _S1617 = atlasWidth_17;
                    uint _S1618 = 9U % atlasWidth_17;
                    int _S1619 = int(_S1618);
                    uint _S1620 = 9U / _S1617;
                    int3 _S1621 = int3(_S1619, int(_S1620), int(0));
                    uint rows_17 = ((_S1616 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1621)).xy), uint(((_S1621)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1579 * _S1585 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1622 = atlasWidth_17 / columns_17;
                    float _S1623 = float(_S1622);
                    uint _S1624 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1623, float(_S1624));
                    uint _S1625 = slot_35 % columns_17;
                    float _S1626 = float(_S1625);
                    uint _S1627 = slot_35 / columns_17;
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1626, float(_S1627)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(_S192) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1628 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(_S192)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1628);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1629 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1629)).xy), uint(((_S1629)).z)));
                    break;
                }
                uint _S1630 = atlasWidth_17 / uint(_S1628.x);
                float _S1631 = float(_S1630);
                uint _S1632 = (atlasHeight_17 - 1U) / uint(_S1628.y);
                float2 cellSize_35 = float2(_S1631, float(_S1632));
                _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_8 = uint(round(_S1556.y));
            if(operation_8 == 1U)
            {
                _S194 = _S194 * _S379;
                break;
            }
            if(operation_8 == 2U)
            {
                _S194 = _S194 + _S379;
                break;
            }
            if(operation_8 == 3U)
            {
                _S194 = _S194 - _S379;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1556.z;
                _S194 = _S194 * float4((1.0f - factor_8))  + _S379 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S194.x);
    }
    else
    {
        clearcoatRoughness_0 = _S173;
    }
    float lightType_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool useSecondary_9 = (secondaryMask_0 & 2048U) != 0U;
        if((alternateMask_0 & 2048U) != 0U)
        {
            if(useSecondary_9)
            {
                _S192 = _S191;
            }
            else
            {
                _S192 = _S190;
            }
        }
        else
        {
            if(useSecondary_9)
            {
                _S192 = _S183;
            }
            else
            {
                _S192 = _S182;
            }
        }
        bool _S1633 = (udimMask_0 & 2048U) != 0U;
        uint maskU_9 = uint(round(_S176.y));
        uint maskV_9 = uint(round(_S176.z));
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
            if(!_S1633)
            {
                for(;;)
                {
                    if(_S1634 == 0U)
                    {
                        _S193 = _S192;
                        break;
                    }
                    texture2d<float, access::sample> _S1635 = (&kernelContext_0)->iorTexture_0;
                    thread uint width_22;
                    thread uint height_22;
                    (*((&width_22)) = (_S1635).get_width(0)),(*((&height_22)) = (_S1635).get_height(0));
                    float2 size_20 = float2(float(width_22), float(height_22));
                    float2 _S1636 = float2(0.5f) ;
                    float2 remapped_20 = clamp((_S192 * (size_20 - float2(2.0f) ) + float2(1.0f) ) / size_20, _S1636 / size_20, (size_20 - _S1636) / size_20);
                    if((_S1634 & 1U) != 0U)
                    {
                        ior_0 = remapped_20.x;
                    }
                    else
                    {
                        ior_0 = _S192.x;
                    }
                    if((_S1634 & 2U) != 0U)
                    {
                        lightType_0 = remapped_20.y;
                    }
                    else
                    {
                        lightType_0 = _S192.y;
                    }
                    _S193 = float2(ior_0, lightType_0);
                    break;
                }
                _S194 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S193)));
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
                int2 tile_36 = int2(floor(_S192)) - minimumTile_18;
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
                    _S194 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1662)).xy), uint(((_S1662)).z)));
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
                    _S194 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1675)).xy), uint(((_S1675)).z)));
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
                _S194 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1702, float(_S1703)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(_S192) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1704 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(_S192)) - int2(metadata_18.xy);
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
                _S194 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1705)).xy), uint(((_S1705)).z)));
                break;
            }
            uint _S1706 = atlasWidth_18 / uint(_S1704.x);
            float _S1707 = float(_S1706);
            uint _S1708 = (atlasHeight_18 - 1U) / uint(_S1704.y);
            float2 cellSize_37 = float2(_S1707, float(_S1708));
            _S194 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1709 = float4(_S7->compositeControls_0) ;
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
                        float2 remapped_21 = clamp((_S192 * (size_21 - float2(2.0f) ) + float2(1.0f) ) / size_21, _S1713 / size_21, (size_21 - _S1713) / size_21);
                        if((_S1711 & 1U) != 0U)
                        {
                            ior_0 = remapped_21.x;
                        }
                        else
                        {
                            ior_0 = _S192.x;
                        }
                        if((_S1711 & 2U) != 0U)
                        {
                            lightType_0 = remapped_21.y;
                        }
                        else
                        {
                            lightType_0 = _S192.y;
                        }
                        _S192 = float2(ior_0, lightType_0);
                        break;
                    }
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S192)));
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
                    int2 tile_38 = int2(floor(_S192)) - minimumTile_19;
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
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1739)).xy), uint(((_S1739)).z)));
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
                        _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1752)).xy), uint(((_S1752)).z)));
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
                    _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1779, float(_S1780)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(_S192) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1781 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(_S192)) - int2(metadata_19.xy);
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
                    _S379 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1782)).xy), uint(((_S1782)).z)));
                    break;
                }
                uint _S1783 = atlasWidth_19 / uint(_S1781.x);
                float _S1784 = float(_S1783);
                uint _S1785 = (atlasHeight_19 - 1U) / uint(_S1781.y);
                float2 cellSize_39 = float2(_S1784, float(_S1785));
                _S379 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(_S192) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_9 = uint(round(_S1709.y));
            if(operation_9 == 1U)
            {
                _S194 = _S194 * _S379;
                break;
            }
            if(operation_9 == 2U)
            {
                _S194 = _S194 + _S379;
                break;
            }
            if(operation_9 == 3U)
            {
                _S194 = _S194 - _S379;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1709.z;
                _S194 = _S194 * float4((1.0f - factor_9))  + _S379 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S194.x;
    }
    else
    {
        ior_0 = _S171;
    }
    float opacityThreshold_0 = _S166.z;
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
    bool omniPbrSpecular_0 = (_S174.w) >= 0.5f;
    float dielectricLevel_0 = _S160.w;
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
        if((_S166.w) >= 0.5f)
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
    float4 _S1799 = float4(_S7->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S1799.w) ;
    uint domeLinkMask_0 = uint(max(_S176.x, 0.0f));
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
            _S1805 = (((uint4(_S7->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
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
            lightDirection_0 = normalize((float4(_S7->lightDirectionIntensity_0) ).xyz);
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
            roughness_0 = (float4(_S7->lightDirectionIntensity_0) ).w;
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
            _S194 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1875 = 15296U + lightIndex_0 * 16U;
            float _S1876 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1875)>>2]);
            float _S1877 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1875 + 4U)>>2]);
            float _S1878 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1875 + 8U)>>2]);
            float _S1879 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1875 + 12U)>>2]);
            _S194 = float4(_S1876, _S1877, _S1878, _S1879);
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
                if((_S194.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S194.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S1907 = _S194.y;
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
                _S5 = _S1926;
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
                if(_S5)
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
                    if(_S5)
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
                    _S6 = _S1938;
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
                    if(_S6)
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
                        if(_S6)
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
