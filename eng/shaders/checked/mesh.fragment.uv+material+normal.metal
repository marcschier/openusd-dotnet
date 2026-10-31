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
    float _S4;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    float3 color_0;
    float3 sampleDirection_0;
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
    float2 detailSource_0;
    float2 tangent_1;
    bool hasSceneLighting_0;
    float4 _S192;
    uint3 _S193;
    if((textureMask_0 & 2U) != 0U)
    {
        bool useSecondary_0 = (secondaryMask_0 & 2U) != 0U;
        if((alternateMask_0 & 2U) != 0U)
        {
            if(useSecondary_0)
            {
                detailSource_0 = _S191;
            }
            else
            {
                detailSource_0 = _S190;
            }
        }
        else
        {
            if(useSecondary_0)
            {
                detailSource_0 = _S183;
            }
            else
            {
                detailSource_0 = _S182;
            }
        }
        thread float4 diffuseSample_0;
        bool _S194 = (udimMask_0 & 2U) != 0U;
        uint maskU_0 = uint(round(_S176.y));
        uint maskV_0 = uint(round(_S176.z));
        bool _S195 = (maskU_0 & 2U) != 0U;
        if(_S195)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S196 = (maskV_0 & 2U) != 0U;
        if(_S196)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S197 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S194)
            {
                for(;;)
                {
                    if(_S197 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S198 = (&kernelContext_0)->baseColorTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S198).get_width(0)),(*((&height_0)) = (_S198).get_height(0));
                    float2 size_0 = float2(float(width_0), float(height_0));
                    float2 _S199 = float2(0.5f) ;
                    float2 remapped_0 = clamp((detailSource_0 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S199 / size_0, (size_0 - _S199) / size_0);
                    if((_S197 & 1U) != 0U)
                    {
                        roughness_0 = remapped_0.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S197 & 2U) != 0U)
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
                _S192 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S200 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S200).get_width(0)),(*((&atlasHeight_0)) = (_S200).get_height(0));
            int3 _S201 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S201)).xy), uint(((_S201)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S202 = atlasWidth_0;
                uint _S203 = 2U % atlasWidth_0;
                int _S204 = int(_S203);
                uint _S205 = 2U / _S202;
                int3 _S206 = int3(_S204, int(_S205), int(0));
                int _S207 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S206)).xy), uint(((_S206)).z))).x * 255.0f)));
                uint _S208 = atlasWidth_0;
                uint _S209 = 3U % atlasWidth_0;
                int _S210 = int(_S209);
                uint _S211 = 3U / _S208;
                int3 _S212 = int3(_S210, int(_S211), int(0));
                int2 minimumTile_0 = int2(_S207, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S212)).xy), uint(((_S212)).z))).x * 255.0f))));
                uint _S213 = atlasWidth_0;
                uint _S214 = 4U % atlasWidth_0;
                int _S215 = int(_S214);
                uint _S216 = 4U / _S213;
                int3 _S217 = int3(_S215, int(_S216), int(0));
                uint _S218 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S217)).xy), uint(((_S217)).z))).x * 255.0f));
                uint _S219 = atlasWidth_0;
                uint _S220 = 5U % atlasWidth_0;
                int _S221 = int(_S220);
                uint _S222 = 5U / _S219;
                int3 _S223 = int3(_S221, int(_S222), int(0));
                uint _S224 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S223)).xy), uint(((_S223)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S218, _S224);
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
                    int3 _S225 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S225)).xy), uint(((_S225)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S218 + uint(tile_0.x));
                uint _S226 = atlasWidth_0;
                uint _S227 = offset_0 % atlasWidth_0;
                int _S228 = int(_S227);
                uint _S229 = offset_0 / _S226;
                int3 _S230 = int3(_S228, int(_S229), int(0));
                uint _S231 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S230)).xy), uint(((_S230)).z))).x * 255.0f));
                uint _S232 = atlasWidth_0;
                uint _S233 = offset_0 + 1U;
                uint _S234 = _S233 % atlasWidth_0;
                int _S235 = int(_S234);
                uint _S236 = _S233 / _S232;
                int3 _S237 = int3(_S235, int(_S236), int(0));
                uint slot_0 = _S231 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S237)).xy), uint(((_S237)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S238 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S238)).xy), uint(((_S238)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S239 = atlasWidth_0;
                uint _S240 = 6U % atlasWidth_0;
                int _S241 = int(_S240);
                uint _S242 = 6U / _S239;
                int3 _S243 = int3(_S241, int(_S242), int(0));
                uint _S244 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S243)).xy), uint(((_S243)).z))).x * 255.0f));
                uint _S245 = atlasWidth_0;
                uint _S246 = 7U % atlasWidth_0;
                int _S247 = int(_S246);
                uint _S248 = 7U / _S245;
                int3 _S249 = int3(_S247, int(_S248), int(0));
                uint columns_0 = _S244 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S249)).xy), uint(((_S249)).z))).x * 255.0f)) << 8U);
                uint _S250 = atlasWidth_0;
                uint _S251 = 8U % atlasWidth_0;
                int _S252 = int(_S251);
                uint _S253 = 8U / _S250;
                int3 _S254 = int3(_S252, int(_S253), int(0));
                uint _S255 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S254)).xy), uint(((_S254)).z))).x * 255.0f));
                uint _S256 = atlasWidth_0;
                uint _S257 = 9U % atlasWidth_0;
                int _S258 = int(_S257);
                uint _S259 = 9U / _S256;
                int3 _S260 = int3(_S258, int(_S259), int(0));
                uint rows_0 = ((_S255 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S260)).xy), uint(((_S260)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S218 * _S224 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S261 = atlasWidth_0 / columns_0;
                float _S262 = float(_S261);
                uint _S263 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S262, float(_S263));
                uint _S264 = slot_1 % columns_0;
                float _S265 = float(_S264);
                uint _S266 = slot_1 / columns_0;
                _S192 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S265, float(_S266)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(detailSource_0) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S267 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(detailSource_0)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S267);
            }
            if(hasSceneLighting_0)
            {
                int3 _S268 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S192 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S268)).xy), uint(((_S268)).z)));
                break;
            }
            uint _S269 = atlasWidth_0 / uint(_S267.x);
            float _S270 = float(_S269);
            uint _S271 = (atlasHeight_0 - 1U) / uint(_S267.y);
            float2 cellSize_1 = float2(_S270, float(_S271));
            _S192 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S192;
        float4 _S272 = float4(_S7->compositeControls_0) ;
        float _S273 = _S272.x;
        if(_S273 == 0.0f)
        {
            hasSceneLighting_0 = (_S272.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S274 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S274).get_width(0)),(*((&sourceHeight_0)) = (_S274).get_height(0));
            texture2d<float, access::sample> _S275 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S275).get_width(0)),(*((&valueHeight_0)) = (_S275).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S276 = diffuseSample_0;
                for(;;)
                {
                    float _S277 = _S276.w;
                    if(_S277 > 1.0f)
                    {
                        diffuseColor_0 = _S276.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S278 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S278).get_width(0)),(*((&height_1)) = (_S278).get_height(0));
                    uint3 _S279 = uint3(round(_S276.xyz * float3(255.0f) ));
                    uint index_1 = ((_S279.x) | ((_S279.y) << 8U)) | ((_S279.z) << 16U);
                    if(_S277 == 0.0f)
                    {
                        _S193 = _S279;
                    }
                    else
                    {
                        _S193 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S193 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S280 = address_0.x;
                    uint _S281 = _S280 % width_1;
                    int _S282 = int(_S281);
                    uint _S283 = _S280 / width_1;
                    int3 _S284 = int3(_S282, int(_S283), int(0));
                    float _S285 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S284)).xy), uint(((_S284)).z))).x;
                    uint _S286 = address_0.y;
                    uint _S287 = _S286 % width_1;
                    int _S288 = int(_S287);
                    uint _S289 = _S286 / width_1;
                    int3 _S290 = int3(_S288, int(_S289), int(0));
                    float _S291 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S290)).xy), uint(((_S290)).z))).x;
                    uint _S292 = address_0.z;
                    uint _S293 = _S292 % width_1;
                    int _S294 = int(_S293);
                    uint _S295 = _S292 / width_1;
                    int3 _S296 = int3(_S294, int(_S295), int(0));
                    diffuseColor_0 = float3(_S285, _S291, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S296)).xy), uint(((_S296)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                if(_S195)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S196)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S297 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S297 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S298 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S298).get_width(0)),(*((&height_2)) = (_S298).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S299 = float2(0.5f) ;
                    float2 remapped_1 = clamp((detailSource_0 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S299 / size_1, (size_1 - _S299) / size_1);
                    if((_S297 & 1U) != 0U)
                    {
                        roughness_0 = remapped_1.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S297 & 2U) != 0U)
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
        float4 _S300 = diffuseSample_0;
        for(;;)
        {
            if(_S273 != 2.0f)
            {
                _S192 = _S300;
                break;
            }
            bool _S301 = (_S272.w) >= 0.5f;
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
            uint _S302 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S301)
                {
                    for(;;)
                    {
                        if(_S302 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S303 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_3;
                        thread uint height_3;
                        (*((&width_3)) = (_S303).get_width(0)),(*((&height_3)) = (_S303).get_height(0));
                        float2 size_2 = float2(float(width_3), float(height_3));
                        float2 _S304 = float2(0.5f) ;
                        float2 remapped_2 = clamp((detailSource_0 * (size_2 - float2(2.0f) ) + float2(1.0f) ) / size_2, _S304 / size_2, (size_2 - _S304) / size_2);
                        if((_S302 & 1U) != 0U)
                        {
                            roughness_0 = remapped_2.x;
                        }
                        else
                        {
                            roughness_0 = detailSource_0.x;
                        }
                        if((_S302 & 2U) != 0U)
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
                    _S192 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S305 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S305).get_width(0)),(*((&atlasHeight_1)) = (_S305).get_height(0));
                int3 _S306 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S306)).xy), uint(((_S306)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S307 = atlasWidth_1;
                    uint _S308 = 2U % atlasWidth_1;
                    int _S309 = int(_S308);
                    uint _S310 = 2U / _S307;
                    int3 _S311 = int3(_S309, int(_S310), int(0));
                    int _S312 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S311)).xy), uint(((_S311)).z))).x * 255.0f)));
                    uint _S313 = atlasWidth_1;
                    uint _S314 = 3U % atlasWidth_1;
                    int _S315 = int(_S314);
                    uint _S316 = 3U / _S313;
                    int3 _S317 = int3(_S315, int(_S316), int(0));
                    int2 minimumTile_1 = int2(_S312, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S317)).xy), uint(((_S317)).z))).x * 255.0f))));
                    uint _S318 = atlasWidth_1;
                    uint _S319 = 4U % atlasWidth_1;
                    int _S320 = int(_S319);
                    uint _S321 = 4U / _S318;
                    int3 _S322 = int3(_S320, int(_S321), int(0));
                    uint _S323 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S322)).xy), uint(((_S322)).z))).x * 255.0f));
                    uint _S324 = atlasWidth_1;
                    uint _S325 = 5U % atlasWidth_1;
                    int _S326 = int(_S325);
                    uint _S327 = 5U / _S324;
                    int3 _S328 = int3(_S326, int(_S327), int(0));
                    uint _S329 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S328)).xy), uint(((_S328)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S323, _S329);
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
                        int3 _S330 = int3(int(1), int(0), int(0));
                        _S192 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S330)).xy), uint(((_S330)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S323 + uint(tile_2.x));
                    uint _S331 = atlasWidth_1;
                    uint _S332 = offset_1 % atlasWidth_1;
                    int _S333 = int(_S332);
                    uint _S334 = offset_1 / _S331;
                    int3 _S335 = int3(_S333, int(_S334), int(0));
                    uint _S336 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S335)).xy), uint(((_S335)).z))).x * 255.0f));
                    uint _S337 = atlasWidth_1;
                    uint _S338 = offset_1 + 1U;
                    uint _S339 = _S338 % atlasWidth_1;
                    int _S340 = int(_S339);
                    uint _S341 = _S338 / _S337;
                    int3 _S342 = int3(_S340, int(_S341), int(0));
                    uint slot_2 = _S336 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S342)).xy), uint(((_S342)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S343 = int3(int(1), int(0), int(0));
                        _S192 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S343)).xy), uint(((_S343)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S344 = atlasWidth_1;
                    uint _S345 = 6U % atlasWidth_1;
                    int _S346 = int(_S345);
                    uint _S347 = 6U / _S344;
                    int3 _S348 = int3(_S346, int(_S347), int(0));
                    uint _S349 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S348)).xy), uint(((_S348)).z))).x * 255.0f));
                    uint _S350 = atlasWidth_1;
                    uint _S351 = 7U % atlasWidth_1;
                    int _S352 = int(_S351);
                    uint _S353 = 7U / _S350;
                    int3 _S354 = int3(_S352, int(_S353), int(0));
                    uint columns_1 = _S349 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S354)).xy), uint(((_S354)).z))).x * 255.0f)) << 8U);
                    uint _S355 = atlasWidth_1;
                    uint _S356 = 8U % atlasWidth_1;
                    int _S357 = int(_S356);
                    uint _S358 = 8U / _S355;
                    int3 _S359 = int3(_S357, int(_S358), int(0));
                    uint _S360 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S359)).xy), uint(((_S359)).z))).x * 255.0f));
                    uint _S361 = atlasWidth_1;
                    uint _S362 = 9U % atlasWidth_1;
                    int _S363 = int(_S362);
                    uint _S364 = 9U / _S361;
                    int3 _S365 = int3(_S363, int(_S364), int(0));
                    uint rows_1 = ((_S360 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S365)).xy), uint(((_S365)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S323 * _S329 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S366 = atlasWidth_1 / columns_1;
                    float _S367 = float(_S366);
                    uint _S368 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S367, float(_S368));
                    uint _S369 = slot_3 % columns_1;
                    float _S370 = float(_S369);
                    uint _S371 = slot_3 / columns_1;
                    _S192 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S370, float(_S371)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailSource_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S372 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(detailSource_0)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S372);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S373 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S192 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S373)).xy), uint(((_S373)).z)));
                    break;
                }
                uint _S374 = atlasWidth_1 / uint(_S372.x);
                float _S375 = float(_S374);
                uint _S376 = (atlasHeight_1 - 1U) / uint(_S372.y);
                float2 cellSize_3 = float2(_S375, float(_S376));
                _S192 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S272.y));
            if(operation_0 == 1U)
            {
                _S192 = _S300 * _S192;
                break;
            }
            if(operation_0 == 2U)
            {
                _S192 = _S300 + _S192;
                break;
            }
            if(operation_0 == 3U)
            {
                _S192 = _S300 - _S192;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S272.z;
                _S192 = _S300 * float4((1.0f - factor_0))  + _S192 * float4(factor_0) ;
                break;
            }
            _S192 = _S300;
            break;
        }
        diffuseColor_0 = _S192.xyz;
    }
    thread float4 baseNormalSample_0;
    bool useSecondary_1 = (secondaryMask_0 & 4U) != 0U;
    bool _S377 = (alternateMask_0 & 4U) != 0U;
    if(_S377)
    {
        if(useSecondary_1)
        {
            detailSource_0 = _S191;
        }
        else
        {
            detailSource_0 = _S190;
        }
    }
    else
    {
        if(useSecondary_1)
        {
            detailSource_0 = _S183;
        }
        else
        {
            detailSource_0 = _S182;
        }
    }
    bool _S378 = (udimMask_0 & 4U) != 0U;
    uint maskU_1 = uint(round(_S176.y));
    uint maskV_1 = uint(round(_S176.z));
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
    uint _S379 = index_0 | lightCount_0;
    for(;;)
    {
        if(!_S378)
        {
            for(;;)
            {
                if(_S379 == 0U)
                {
                    break;
                }
                texture2d<float, access::sample> _S380 = (&kernelContext_0)->normalTexture_0;
                thread uint width_4;
                thread uint height_4;
                (*((&width_4)) = (_S380).get_width(0)),(*((&height_4)) = (_S380).get_height(0));
                float2 size_3 = float2(float(width_4), float(height_4));
                float2 _S381 = float2(0.5f) ;
                float2 remapped_3 = clamp((detailSource_0 * (size_3 - float2(2.0f) ) + float2(1.0f) ) / size_3, _S381 / size_3, (size_3 - _S381) / size_3);
                if((_S379 & 1U) != 0U)
                {
                    roughness_0 = remapped_3.x;
                }
                else
                {
                    roughness_0 = detailSource_0.x;
                }
                if((_S379 & 2U) != 0U)
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
            _S192 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (detailSource_0)));
            break;
        }
        texture2d<float, access::sample> _S382 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S382).get_width(0)),(*((&atlasHeight_2)) = (_S382).get_height(0));
        int3 _S383 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S383)).xy), uint(((_S383)).z))) * float4(255.0f) );
        if((metadata_2.x) == 255.0f)
        {
            uint _S384 = atlasWidth_2;
            uint _S385 = 2U % atlasWidth_2;
            int _S386 = int(_S385);
            uint _S387 = 2U / _S384;
            int3 _S388 = int3(_S386, int(_S387), int(0));
            int _S389 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S388)).xy), uint(((_S388)).z))).x * 255.0f)));
            uint _S390 = atlasWidth_2;
            uint _S391 = 3U % atlasWidth_2;
            int _S392 = int(_S391);
            uint _S393 = 3U / _S390;
            int3 _S394 = int3(_S392, int(_S393), int(0));
            int2 minimumTile_2 = int2(_S389, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S394)).xy), uint(((_S394)).z))).x * 255.0f))));
            uint _S395 = atlasWidth_2;
            uint _S396 = 4U % atlasWidth_2;
            int _S397 = int(_S396);
            uint _S398 = 4U / _S395;
            int3 _S399 = int3(_S397, int(_S398), int(0));
            uint _S400 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S399)).xy), uint(((_S399)).z))).x * 255.0f));
            uint _S401 = atlasWidth_2;
            uint _S402 = 5U % atlasWidth_2;
            int _S403 = int(_S402);
            uint _S404 = 5U / _S401;
            int3 _S405 = int3(_S403, int(_S404), int(0));
            uint _S406 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S405)).xy), uint(((_S405)).z))).x * 255.0f));
            uint2 grid_2 = uint2(_S400, _S406);
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
                int3 _S407 = int3(int(1), int(0), int(0));
                _S192 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S407)).xy), uint(((_S407)).z)));
                break;
            }
            uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S400 + uint(tile_4.x));
            uint _S408 = atlasWidth_2;
            uint _S409 = offset_2 % atlasWidth_2;
            int _S410 = int(_S409);
            uint _S411 = offset_2 / _S408;
            int3 _S412 = int3(_S410, int(_S411), int(0));
            uint _S413 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S412)).xy), uint(((_S412)).z))).x * 255.0f));
            uint _S414 = atlasWidth_2;
            uint _S415 = offset_2 + 1U;
            uint _S416 = _S415 % atlasWidth_2;
            int _S417 = int(_S416);
            uint _S418 = _S415 / _S414;
            int3 _S419 = int3(_S417, int(_S418), int(0));
            uint slot_4 = _S413 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S419)).xy), uint(((_S419)).z))).x * 255.0f)) << 8U);
            if(slot_4 == 0U)
            {
                int3 _S420 = int3(int(1), int(0), int(0));
                _S192 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S420)).xy), uint(((_S420)).z)));
                break;
            }
            uint slot_5 = slot_4 - 1U;
            uint _S421 = atlasWidth_2;
            uint _S422 = 6U % atlasWidth_2;
            int _S423 = int(_S422);
            uint _S424 = 6U / _S421;
            int3 _S425 = int3(_S423, int(_S424), int(0));
            uint _S426 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S425)).xy), uint(((_S425)).z))).x * 255.0f));
            uint _S427 = atlasWidth_2;
            uint _S428 = 7U % atlasWidth_2;
            int _S429 = int(_S428);
            uint _S430 = 7U / _S427;
            int3 _S431 = int3(_S429, int(_S430), int(0));
            uint columns_2 = _S426 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S431)).xy), uint(((_S431)).z))).x * 255.0f)) << 8U);
            uint _S432 = atlasWidth_2;
            uint _S433 = 8U % atlasWidth_2;
            int _S434 = int(_S433);
            uint _S435 = 8U / _S432;
            int3 _S436 = int3(_S434, int(_S435), int(0));
            uint _S437 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S436)).xy), uint(((_S436)).z))).x * 255.0f));
            uint _S438 = atlasWidth_2;
            uint _S439 = 9U % atlasWidth_2;
            int _S440 = int(_S439);
            uint _S441 = 9U / _S438;
            int3 _S442 = int3(_S440, int(_S441), int(0));
            uint rows_2 = ((_S437 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S442)).xy), uint(((_S442)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
            uint metadataRows_2 = (10U + 2U * _S400 * _S406 + atlasWidth_2 - 1U) / atlasWidth_2;
            uint _S443 = atlasWidth_2 / columns_2;
            float _S444 = float(_S443);
            uint _S445 = (atlasHeight_2 - metadataRows_2) / rows_2;
            float2 cellSize_4 = float2(_S444, float(_S445));
            uint _S446 = slot_5 % columns_2;
            float _S447 = float(_S446);
            uint _S448 = slot_5 / columns_2;
            _S192 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S447, float(_S448)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(detailSource_0) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        int2 _S449 = int2(metadata_2.zw);
        int2 tile_5 = int2(floor(detailSource_0)) - int2(metadata_2.xy);
        if(any(tile_5 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_5 >= _S449);
        }
        if(hasSceneLighting_0)
        {
            int3 _S450 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S192 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S450)).xy), uint(((_S450)).z)));
            break;
        }
        uint _S451 = atlasWidth_2 / uint(_S449.x);
        float _S452 = float(_S451);
        uint _S453 = (atlasHeight_2 - 1U) / uint(_S449.y);
        float2 cellSize_5 = float2(_S452, float(_S453));
        _S192 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    baseNormalSample_0 = _S192;
    float4 _S454 = float4(_S7->detailUvTransformRow0_0) ;
    if((_S454.w) != 0.0f)
    {
        float4 _S455 = baseNormalSample_0;
        for(;;)
        {
            float _S456 = _S455.w;
            if(_S456 > 1.0f)
            {
                unlitColor_0 = _S455.xyz;
                break;
            }
            texture2d<float, access::sample> _S457 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_5;
            thread uint height_5;
            (*((&width_5)) = (_S457).get_width(0)),(*((&height_5)) = (_S457).get_height(0));
            uint3 _S458 = uint3(round(_S455.xyz * float3(255.0f) ));
            uint index_2 = ((_S458.x) | ((_S458.y) << 8U)) | ((_S458.z) << 16U);
            if(_S456 == 0.0f)
            {
                _S193 = _S458;
            }
            else
            {
                _S193 = uint3(index_2, index_2, index_2);
            }
            uint3 address_1 = _S193 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S459 = address_1.x;
            uint _S460 = _S459 % width_5;
            int _S461 = int(_S460);
            uint _S462 = _S459 / width_5;
            int3 _S463 = int3(_S461, int(_S462), int(0));
            float _S464 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S463)).xy), uint(((_S463)).z))).x;
            uint _S465 = address_1.y;
            uint _S466 = _S465 % width_5;
            int _S467 = int(_S466);
            uint _S468 = _S465 / width_5;
            int3 _S469 = int3(_S467, int(_S468), int(0));
            float _S470 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S469)).xy), uint(((_S469)).z))).x;
            uint _S471 = address_1.z;
            uint _S472 = _S471 % width_5;
            int _S473 = int(_S472);
            uint _S474 = _S471 / width_5;
            int3 _S475 = int3(_S473, int(_S474), int(0));
            unlitColor_0 = float3(_S464, _S470, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S475)).xy), uint(((_S475)).z))).x);
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
    if(_S377)
    {
        detailSource_0 = _S186;
    }
    else
    {
        detailSource_0 = _S178;
    }
    if(_S377)
    {
        tangent_1 = _S188;
    }
    else
    {
        tangent_1 = _S180;
    }
    thread float3 _S476 = unlitColor_0;
    float2 _S477 = float2(0.0f, 0.0f);
    if(any(tangent_1 != _S477))
    {
        float _S478 = tangent_1.x;
        float _S479 = tangent_1.y;
        float2 tangent_2 = normalize(float2(_S479, - _S478) / float2(max(abs(_S478), abs(_S479))) );
        float2 bitangent_0 = float2(- tangent_2.y, tangent_2.x);
        if((dot(detailSource_0, tangent_2)) < 0.0f)
        {
            detailSource_0 = - tangent_2;
        }
        else
        {
            detailSource_0 = tangent_2;
        }
        _S476.xy = detailSource_0 * float2(_S476.x)  + bitangent_0 * float2(_S476.y) ;
    }
    float3 _S480 = _S476;
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
        float2 _S481 = _S454.xy;
        float _S482 = dot(_S481, detailSource_0) + _S454.z;
        float4 _S483 = float4(_S7->detailUvTransformRow1_0) ;
        float2 _S484 = _S483.xy;
        float _S485 = dot(_S484, detailSource_0) + _S483.z;
        float2 detailUv_0 = float2(_S482, _S485);
        thread float4 detailSample_0;
        bool _S486 = (udimMask_0 & 4096U) != 0U;
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
        uint _S487 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S486)
            {
                for(;;)
                {
                    if(_S487 == 0U)
                    {
                        detailSource_0 = detailUv_0;
                        break;
                    }
                    texture2d<float, access::sample> _S488 = (&kernelContext_0)->detailNormalTexture_0;
                    thread uint width_6;
                    thread uint height_6;
                    (*((&width_6)) = (_S488).get_width(0)),(*((&height_6)) = (_S488).get_height(0));
                    float2 size_4 = float2(float(width_6), float(height_6));
                    float2 _S489 = float2(0.5f) ;
                    float2 remapped_4 = clamp((detailUv_0 * (size_4 - float2(2.0f) ) + float2(1.0f) ) / size_4, _S489 / size_4, (size_4 - _S489) / size_4);
                    if((_S487 & 1U) != 0U)
                    {
                        roughness_0 = remapped_4.x;
                    }
                    else
                    {
                        roughness_0 = _S482;
                    }
                    if((_S487 & 2U) != 0U)
                    {
                        metallic_0 = remapped_4.y;
                    }
                    else
                    {
                        metallic_0 = _S485;
                    }
                    detailSource_0 = float2(roughness_0, metallic_0);
                    break;
                }
                _S192 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S490 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S490).get_width(0)),(*((&atlasHeight_3)) = (_S490).get_height(0));
            int3 _S491 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S491)).xy), uint(((_S491)).z))) * float4(255.0f) );
            if((metadata_3.x) == 255.0f)
            {
                uint _S492 = atlasWidth_3;
                uint _S493 = 2U % atlasWidth_3;
                int _S494 = int(_S493);
                uint _S495 = 2U / _S492;
                int3 _S496 = int3(_S494, int(_S495), int(0));
                int _S497 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S496)).xy), uint(((_S496)).z))).x * 255.0f)));
                uint _S498 = atlasWidth_3;
                uint _S499 = 3U % atlasWidth_3;
                int _S500 = int(_S499);
                uint _S501 = 3U / _S498;
                int3 _S502 = int3(_S500, int(_S501), int(0));
                int2 minimumTile_3 = int2(_S497, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S502)).xy), uint(((_S502)).z))).x * 255.0f))));
                uint _S503 = atlasWidth_3;
                uint _S504 = 4U % atlasWidth_3;
                int _S505 = int(_S504);
                uint _S506 = 4U / _S503;
                int3 _S507 = int3(_S505, int(_S506), int(0));
                uint _S508 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S507)).xy), uint(((_S507)).z))).x * 255.0f));
                uint _S509 = atlasWidth_3;
                uint _S510 = 5U % atlasWidth_3;
                int _S511 = int(_S510);
                uint _S512 = 5U / _S509;
                int3 _S513 = int3(_S511, int(_S512), int(0));
                uint _S514 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S513)).xy), uint(((_S513)).z))).x * 255.0f));
                uint2 grid_3 = uint2(_S508, _S514);
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
                    int3 _S515 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S515)).xy), uint(((_S515)).z)));
                    break;
                }
                uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S508 + uint(tile_6.x));
                uint _S516 = atlasWidth_3;
                uint _S517 = offset_3 % atlasWidth_3;
                int _S518 = int(_S517);
                uint _S519 = offset_3 / _S516;
                int3 _S520 = int3(_S518, int(_S519), int(0));
                uint _S521 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S520)).xy), uint(((_S520)).z))).x * 255.0f));
                uint _S522 = atlasWidth_3;
                uint _S523 = offset_3 + 1U;
                uint _S524 = _S523 % atlasWidth_3;
                int _S525 = int(_S524);
                uint _S526 = _S523 / _S522;
                int3 _S527 = int3(_S525, int(_S526), int(0));
                uint slot_6 = _S521 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S527)).xy), uint(((_S527)).z))).x * 255.0f)) << 8U);
                if(slot_6 == 0U)
                {
                    int3 _S528 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S528)).xy), uint(((_S528)).z)));
                    break;
                }
                uint slot_7 = slot_6 - 1U;
                uint _S529 = atlasWidth_3;
                uint _S530 = 6U % atlasWidth_3;
                int _S531 = int(_S530);
                uint _S532 = 6U / _S529;
                int3 _S533 = int3(_S531, int(_S532), int(0));
                uint _S534 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S533)).xy), uint(((_S533)).z))).x * 255.0f));
                uint _S535 = atlasWidth_3;
                uint _S536 = 7U % atlasWidth_3;
                int _S537 = int(_S536);
                uint _S538 = 7U / _S535;
                int3 _S539 = int3(_S537, int(_S538), int(0));
                uint columns_3 = _S534 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S539)).xy), uint(((_S539)).z))).x * 255.0f)) << 8U);
                uint _S540 = atlasWidth_3;
                uint _S541 = 8U % atlasWidth_3;
                int _S542 = int(_S541);
                uint _S543 = 8U / _S540;
                int3 _S544 = int3(_S542, int(_S543), int(0));
                uint _S545 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S544)).xy), uint(((_S544)).z))).x * 255.0f));
                uint _S546 = atlasWidth_3;
                uint _S547 = 9U % atlasWidth_3;
                int _S548 = int(_S547);
                uint _S549 = 9U / _S546;
                int3 _S550 = int3(_S548, int(_S549), int(0));
                uint rows_3 = ((_S545 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S550)).xy), uint(((_S550)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                uint metadataRows_3 = (10U + 2U * _S508 * _S514 + atlasWidth_3 - 1U) / atlasWidth_3;
                uint _S551 = atlasWidth_3 / columns_3;
                float _S552 = float(_S551);
                uint _S553 = (atlasHeight_3 - metadataRows_3) / rows_3;
                float2 cellSize_6 = float2(_S552, float(_S553));
                uint _S554 = slot_7 % columns_3;
                float _S555 = float(_S554);
                uint _S556 = slot_7 / columns_3;
                _S192 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S555, float(_S556)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(detailUv_0) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            int2 _S557 = int2(metadata_3.zw);
            int2 tile_7 = int2(floor(detailUv_0)) - int2(metadata_3.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S557);
            }
            if(hasSceneLighting_0)
            {
                int3 _S558 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S192 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S558)).xy), uint(((_S558)).z)));
                break;
            }
            uint _S559 = atlasWidth_3 / uint(_S557.x);
            float _S560 = float(_S559);
            uint _S561 = (atlasHeight_3 - 1U) / uint(_S557.y);
            float2 cellSize_7 = float2(_S560, float(_S561));
            _S192 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        detailSample_0 = _S192;
        if((_S483.w) != 0.0f)
        {
            float4 _S562 = detailSample_0;
            for(;;)
            {
                float _S563 = _S562.w;
                if(_S563 > 1.0f)
                {
                    unlitColor_0 = _S562.xyz;
                    break;
                }
                texture2d<float, access::sample> _S564 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_7;
                thread uint height_7;
                (*((&width_7)) = (_S564).get_width(0)),(*((&height_7)) = (_S564).get_height(0));
                uint3 _S565 = uint3(round(_S562.xyz * float3(255.0f) ));
                uint index_3 = ((_S565.x) | ((_S565.y) << 8U)) | ((_S565.z) << 16U);
                if(_S563 == 0.0f)
                {
                    _S193 = _S565;
                }
                else
                {
                    _S193 = uint3(index_3, index_3, index_3);
                }
                uint3 address_2 = _S193 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S566 = address_2.x;
                uint _S567 = _S566 % width_7;
                int _S568 = int(_S567);
                uint _S569 = _S566 / width_7;
                int3 _S570 = int3(_S568, int(_S569), int(0));
                float _S571 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S570)).xy), uint(((_S570)).z))).x;
                uint _S572 = address_2.y;
                uint _S573 = _S572 % width_7;
                int _S574 = int(_S573);
                uint _S575 = _S572 / width_7;
                int3 _S576 = int3(_S574, int(_S575), int(0));
                float _S577 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S576)).xy), uint(((_S576)).z))).x;
                uint _S578 = address_2.z;
                uint _S579 = _S578 % width_7;
                int _S580 = int(_S579);
                uint _S581 = _S578 / width_7;
                int3 _S582 = int3(_S580, int(_S581), int(0));
                unlitColor_0 = float3(_S571, _S577, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S582)).xy), uint(((_S582)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S583 = detail_0;
        if(any(_S484 != _S477))
        {
            float _S584 = _S484.x;
            float _S585 = _S484.y;
            float2 tangent_3 = normalize(float2(_S585, - _S584) / float2(max(abs(_S584), abs(_S585))) );
            float2 bitangent_1 = float2(- tangent_3.y, tangent_3.x);
            if((dot(_S481, tangent_3)) < 0.0f)
            {
                tangent_1 = - tangent_3;
            }
            else
            {
                tangent_1 = tangent_3;
            }
            _S583.xy = tangent_1 * float2(_S583.x)  + bitangent_1 * float2(_S583.y) ;
        }
        float3 _S586 = _S583;
        for(;;)
        {
            float3 t_0 = normalize(_S480) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S586) * float3(-1.0f, -1.0f, 1.0f);
            float _S587 = t_0.z;
            if(_S587 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S587))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S480;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S588 = float3(unlitColor_0.x) ;
    float3 _S589 = float3(unlitColor_0.y) ;
    float3 _S590 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S588 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S589 + _S1.normal_0 * _S590);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S588 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S589 + worldNormalBasis_0 * _S590);
    float4 _S591;
    if((textureMask_0 & 8U) != 0U)
    {
        bool useSecondary_2 = (secondaryMask_0 & 8U) != 0U;
        if((alternateMask_0 & 8U) != 0U)
        {
            if(useSecondary_2)
            {
                detailSource_0 = _S191;
            }
            else
            {
                detailSource_0 = _S190;
            }
        }
        else
        {
            if(useSecondary_2)
            {
                detailSource_0 = _S183;
            }
            else
            {
                detailSource_0 = _S182;
            }
        }
        bool _S592 = (udimMask_0 & 8U) != 0U;
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
        uint _S593 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S592)
            {
                for(;;)
                {
                    if(_S593 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S594 = (&kernelContext_0)->roughnessMetallicTexture_0;
                    thread uint width_8;
                    thread uint height_8;
                    (*((&width_8)) = (_S594).get_width(0)),(*((&height_8)) = (_S594).get_height(0));
                    float2 size_5 = float2(float(width_8), float(height_8));
                    float2 _S595 = float2(0.5f) ;
                    float2 remapped_5 = clamp((detailSource_0 * (size_5 - float2(2.0f) ) + float2(1.0f) ) / size_5, _S595 / size_5, (size_5 - _S595) / size_5);
                    if((_S593 & 1U) != 0U)
                    {
                        roughness_0 = remapped_5.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S593 & 2U) != 0U)
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
                _S192 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S596 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S596).get_width(0)),(*((&atlasHeight_4)) = (_S596).get_height(0));
            int3 _S597 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S597)).xy), uint(((_S597)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S598 = atlasWidth_4;
                uint _S599 = 2U % atlasWidth_4;
                int _S600 = int(_S599);
                uint _S601 = 2U / _S598;
                int3 _S602 = int3(_S600, int(_S601), int(0));
                int _S603 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S602)).xy), uint(((_S602)).z))).x * 255.0f)));
                uint _S604 = atlasWidth_4;
                uint _S605 = 3U % atlasWidth_4;
                int _S606 = int(_S605);
                uint _S607 = 3U / _S604;
                int3 _S608 = int3(_S606, int(_S607), int(0));
                int2 minimumTile_4 = int2(_S603, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S608)).xy), uint(((_S608)).z))).x * 255.0f))));
                uint _S609 = atlasWidth_4;
                uint _S610 = 4U % atlasWidth_4;
                int _S611 = int(_S610);
                uint _S612 = 4U / _S609;
                int3 _S613 = int3(_S611, int(_S612), int(0));
                uint _S614 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S613)).xy), uint(((_S613)).z))).x * 255.0f));
                uint _S615 = atlasWidth_4;
                uint _S616 = 5U % atlasWidth_4;
                int _S617 = int(_S616);
                uint _S618 = 5U / _S615;
                int3 _S619 = int3(_S617, int(_S618), int(0));
                uint _S620 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S619)).xy), uint(((_S619)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S614, _S620);
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
                    int3 _S621 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S621)).xy), uint(((_S621)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S614 + uint(tile_8.x));
                uint _S622 = atlasWidth_4;
                uint _S623 = offset_4 % atlasWidth_4;
                int _S624 = int(_S623);
                uint _S625 = offset_4 / _S622;
                int3 _S626 = int3(_S624, int(_S625), int(0));
                uint _S627 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S626)).xy), uint(((_S626)).z))).x * 255.0f));
                uint _S628 = atlasWidth_4;
                uint _S629 = offset_4 + 1U;
                uint _S630 = _S629 % atlasWidth_4;
                int _S631 = int(_S630);
                uint _S632 = _S629 / _S628;
                int3 _S633 = int3(_S631, int(_S632), int(0));
                uint slot_8 = _S627 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S633)).xy), uint(((_S633)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S634 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S634)).xy), uint(((_S634)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S635 = atlasWidth_4;
                uint _S636 = 6U % atlasWidth_4;
                int _S637 = int(_S636);
                uint _S638 = 6U / _S635;
                int3 _S639 = int3(_S637, int(_S638), int(0));
                uint _S640 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S639)).xy), uint(((_S639)).z))).x * 255.0f));
                uint _S641 = atlasWidth_4;
                uint _S642 = 7U % atlasWidth_4;
                int _S643 = int(_S642);
                uint _S644 = 7U / _S641;
                int3 _S645 = int3(_S643, int(_S644), int(0));
                uint columns_4 = _S640 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S645)).xy), uint(((_S645)).z))).x * 255.0f)) << 8U);
                uint _S646 = atlasWidth_4;
                uint _S647 = 8U % atlasWidth_4;
                int _S648 = int(_S647);
                uint _S649 = 8U / _S646;
                int3 _S650 = int3(_S648, int(_S649), int(0));
                uint _S651 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S650)).xy), uint(((_S650)).z))).x * 255.0f));
                uint _S652 = atlasWidth_4;
                uint _S653 = 9U % atlasWidth_4;
                int _S654 = int(_S653);
                uint _S655 = 9U / _S652;
                int3 _S656 = int3(_S654, int(_S655), int(0));
                uint rows_4 = ((_S651 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S656)).xy), uint(((_S656)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S614 * _S620 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S657 = atlasWidth_4 / columns_4;
                float _S658 = float(_S657);
                uint _S659 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S658, float(_S659));
                uint _S660 = slot_9 % columns_4;
                float _S661 = float(_S660);
                uint _S662 = slot_9 / columns_4;
                _S192 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S661, float(_S662)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(detailSource_0) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S663 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(detailSource_0)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S663);
            }
            if(hasSceneLighting_0)
            {
                int3 _S664 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S192 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S664)).xy), uint(((_S664)).z)));
                break;
            }
            uint _S665 = atlasWidth_4 / uint(_S663.x);
            float _S666 = float(_S665);
            uint _S667 = (atlasHeight_4 - 1U) / uint(_S663.y);
            float2 cellSize_9 = float2(_S666, float(_S667));
            _S192 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S668 = float4(_S7->compositeControls_0) ;
            if((_S668.x) != 8.0f)
            {
                break;
            }
            bool _S669 = (_S668.w) >= 0.5f;
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
            uint _S670 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S669)
                {
                    for(;;)
                    {
                        if(_S670 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S671 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_9;
                        thread uint height_9;
                        (*((&width_9)) = (_S671).get_width(0)),(*((&height_9)) = (_S671).get_height(0));
                        float2 size_6 = float2(float(width_9), float(height_9));
                        float2 _S672 = float2(0.5f) ;
                        float2 remapped_6 = clamp((detailSource_0 * (size_6 - float2(2.0f) ) + float2(1.0f) ) / size_6, _S672 / size_6, (size_6 - _S672) / size_6);
                        if((_S670 & 1U) != 0U)
                        {
                            roughness_0 = remapped_6.x;
                        }
                        else
                        {
                            roughness_0 = detailSource_0.x;
                        }
                        if((_S670 & 2U) != 0U)
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
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S673 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S673).get_width(0)),(*((&atlasHeight_5)) = (_S673).get_height(0));
                int3 _S674 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S674)).xy), uint(((_S674)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S675 = atlasWidth_5;
                    uint _S676 = 2U % atlasWidth_5;
                    int _S677 = int(_S676);
                    uint _S678 = 2U / _S675;
                    int3 _S679 = int3(_S677, int(_S678), int(0));
                    int _S680 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S679)).xy), uint(((_S679)).z))).x * 255.0f)));
                    uint _S681 = atlasWidth_5;
                    uint _S682 = 3U % atlasWidth_5;
                    int _S683 = int(_S682);
                    uint _S684 = 3U / _S681;
                    int3 _S685 = int3(_S683, int(_S684), int(0));
                    int2 minimumTile_5 = int2(_S680, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S685)).xy), uint(((_S685)).z))).x * 255.0f))));
                    uint _S686 = atlasWidth_5;
                    uint _S687 = 4U % atlasWidth_5;
                    int _S688 = int(_S687);
                    uint _S689 = 4U / _S686;
                    int3 _S690 = int3(_S688, int(_S689), int(0));
                    uint _S691 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S690)).xy), uint(((_S690)).z))).x * 255.0f));
                    uint _S692 = atlasWidth_5;
                    uint _S693 = 5U % atlasWidth_5;
                    int _S694 = int(_S693);
                    uint _S695 = 5U / _S692;
                    int3 _S696 = int3(_S694, int(_S695), int(0));
                    uint _S697 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S696)).xy), uint(((_S696)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S691, _S697);
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
                        int3 _S698 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S698)).xy), uint(((_S698)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S691 + uint(tile_10.x));
                    uint _S699 = atlasWidth_5;
                    uint _S700 = offset_5 % atlasWidth_5;
                    int _S701 = int(_S700);
                    uint _S702 = offset_5 / _S699;
                    int3 _S703 = int3(_S701, int(_S702), int(0));
                    uint _S704 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S703)).xy), uint(((_S703)).z))).x * 255.0f));
                    uint _S705 = atlasWidth_5;
                    uint _S706 = offset_5 + 1U;
                    uint _S707 = _S706 % atlasWidth_5;
                    int _S708 = int(_S707);
                    uint _S709 = _S706 / _S705;
                    int3 _S710 = int3(_S708, int(_S709), int(0));
                    uint slot_10 = _S704 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S710)).xy), uint(((_S710)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S711 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S711)).xy), uint(((_S711)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S712 = atlasWidth_5;
                    uint _S713 = 6U % atlasWidth_5;
                    int _S714 = int(_S713);
                    uint _S715 = 6U / _S712;
                    int3 _S716 = int3(_S714, int(_S715), int(0));
                    uint _S717 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S716)).xy), uint(((_S716)).z))).x * 255.0f));
                    uint _S718 = atlasWidth_5;
                    uint _S719 = 7U % atlasWidth_5;
                    int _S720 = int(_S719);
                    uint _S721 = 7U / _S718;
                    int3 _S722 = int3(_S720, int(_S721), int(0));
                    uint columns_5 = _S717 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S722)).xy), uint(((_S722)).z))).x * 255.0f)) << 8U);
                    uint _S723 = atlasWidth_5;
                    uint _S724 = 8U % atlasWidth_5;
                    int _S725 = int(_S724);
                    uint _S726 = 8U / _S723;
                    int3 _S727 = int3(_S725, int(_S726), int(0));
                    uint _S728 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S727)).xy), uint(((_S727)).z))).x * 255.0f));
                    uint _S729 = atlasWidth_5;
                    uint _S730 = 9U % atlasWidth_5;
                    int _S731 = int(_S730);
                    uint _S732 = 9U / _S729;
                    int3 _S733 = int3(_S731, int(_S732), int(0));
                    uint rows_5 = ((_S728 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S733)).xy), uint(((_S733)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S691 * _S697 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S734 = atlasWidth_5 / columns_5;
                    float _S735 = float(_S734);
                    uint _S736 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S735, float(_S736));
                    uint _S737 = slot_11 % columns_5;
                    float _S738 = float(_S737);
                    uint _S739 = slot_11 / columns_5;
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S738, float(_S739)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(detailSource_0) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S740 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(detailSource_0)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S740);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S741 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S741)).xy), uint(((_S741)).z)));
                    break;
                }
                uint _S742 = atlasWidth_5 / uint(_S740.x);
                float _S743 = float(_S742);
                uint _S744 = (atlasHeight_5 - 1U) / uint(_S740.y);
                float2 cellSize_11 = float2(_S743, float(_S744));
                _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_1 = uint(round(_S668.y));
            if(operation_1 == 1U)
            {
                _S192 = _S192 * _S591;
                break;
            }
            if(operation_1 == 2U)
            {
                _S192 = _S192 + _S591;
                break;
            }
            if(operation_1 == 3U)
            {
                _S192 = _S192 - _S591;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S668.z;
                _S192 = _S192 * float4((1.0f - factor_1))  + _S591 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S192.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S168;
    }
    float occlusion_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool useSecondary_3 = (secondaryMask_0 & 32U) != 0U;
        if((alternateMask_0 & 32U) != 0U)
        {
            if(useSecondary_3)
            {
                detailSource_0 = _S191;
            }
            else
            {
                detailSource_0 = _S190;
            }
        }
        else
        {
            if(useSecondary_3)
            {
                detailSource_0 = _S183;
            }
            else
            {
                detailSource_0 = _S182;
            }
        }
        bool _S745 = (udimMask_0 & 32U) != 0U;
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
        uint _S746 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S745)
            {
                for(;;)
                {
                    if(_S746 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S747 = (&kernelContext_0)->metallicTexture_0;
                    thread uint width_10;
                    thread uint height_10;
                    (*((&width_10)) = (_S747).get_width(0)),(*((&height_10)) = (_S747).get_height(0));
                    float2 size_7 = float2(float(width_10), float(height_10));
                    float2 _S748 = float2(0.5f) ;
                    float2 remapped_7 = clamp((detailSource_0 * (size_7 - float2(2.0f) ) + float2(1.0f) ) / size_7, _S748 / size_7, (size_7 - _S748) / size_7);
                    if((_S746 & 1U) != 0U)
                    {
                        metallic_0 = remapped_7.x;
                    }
                    else
                    {
                        metallic_0 = detailSource_0.x;
                    }
                    if((_S746 & 2U) != 0U)
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
                _S192 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S749 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S749).get_width(0)),(*((&atlasHeight_6)) = (_S749).get_height(0));
            int3 _S750 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S750)).xy), uint(((_S750)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S751 = atlasWidth_6;
                uint _S752 = 2U % atlasWidth_6;
                int _S753 = int(_S752);
                uint _S754 = 2U / _S751;
                int3 _S755 = int3(_S753, int(_S754), int(0));
                int _S756 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S755)).xy), uint(((_S755)).z))).x * 255.0f)));
                uint _S757 = atlasWidth_6;
                uint _S758 = 3U % atlasWidth_6;
                int _S759 = int(_S758);
                uint _S760 = 3U / _S757;
                int3 _S761 = int3(_S759, int(_S760), int(0));
                int2 minimumTile_6 = int2(_S756, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S761)).xy), uint(((_S761)).z))).x * 255.0f))));
                uint _S762 = atlasWidth_6;
                uint _S763 = 4U % atlasWidth_6;
                int _S764 = int(_S763);
                uint _S765 = 4U / _S762;
                int3 _S766 = int3(_S764, int(_S765), int(0));
                uint _S767 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S766)).xy), uint(((_S766)).z))).x * 255.0f));
                uint _S768 = atlasWidth_6;
                uint _S769 = 5U % atlasWidth_6;
                int _S770 = int(_S769);
                uint _S771 = 5U / _S768;
                int3 _S772 = int3(_S770, int(_S771), int(0));
                uint _S773 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S772)).xy), uint(((_S772)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S767, _S773);
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
                    int3 _S774 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S774)).xy), uint(((_S774)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S767 + uint(tile_12.x));
                uint _S775 = atlasWidth_6;
                uint _S776 = offset_6 % atlasWidth_6;
                int _S777 = int(_S776);
                uint _S778 = offset_6 / _S775;
                int3 _S779 = int3(_S777, int(_S778), int(0));
                uint _S780 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S779)).xy), uint(((_S779)).z))).x * 255.0f));
                uint _S781 = atlasWidth_6;
                uint _S782 = offset_6 + 1U;
                uint _S783 = _S782 % atlasWidth_6;
                int _S784 = int(_S783);
                uint _S785 = _S782 / _S781;
                int3 _S786 = int3(_S784, int(_S785), int(0));
                uint slot_12 = _S780 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S786)).xy), uint(((_S786)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S787 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S787)).xy), uint(((_S787)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S788 = atlasWidth_6;
                uint _S789 = 6U % atlasWidth_6;
                int _S790 = int(_S789);
                uint _S791 = 6U / _S788;
                int3 _S792 = int3(_S790, int(_S791), int(0));
                uint _S793 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S792)).xy), uint(((_S792)).z))).x * 255.0f));
                uint _S794 = atlasWidth_6;
                uint _S795 = 7U % atlasWidth_6;
                int _S796 = int(_S795);
                uint _S797 = 7U / _S794;
                int3 _S798 = int3(_S796, int(_S797), int(0));
                uint columns_6 = _S793 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S798)).xy), uint(((_S798)).z))).x * 255.0f)) << 8U);
                uint _S799 = atlasWidth_6;
                uint _S800 = 8U % atlasWidth_6;
                int _S801 = int(_S800);
                uint _S802 = 8U / _S799;
                int3 _S803 = int3(_S801, int(_S802), int(0));
                uint _S804 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S803)).xy), uint(((_S803)).z))).x * 255.0f));
                uint _S805 = atlasWidth_6;
                uint _S806 = 9U % atlasWidth_6;
                int _S807 = int(_S806);
                uint _S808 = 9U / _S805;
                int3 _S809 = int3(_S807, int(_S808), int(0));
                uint rows_6 = ((_S804 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S809)).xy), uint(((_S809)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S767 * _S773 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S810 = atlasWidth_6 / columns_6;
                float _S811 = float(_S810);
                uint _S812 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S811, float(_S812));
                uint _S813 = slot_13 % columns_6;
                float _S814 = float(_S813);
                uint _S815 = slot_13 / columns_6;
                _S192 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S814, float(_S815)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(detailSource_0) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S816 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(detailSource_0)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S816);
            }
            if(hasSceneLighting_0)
            {
                int3 _S817 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S192 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S817)).xy), uint(((_S817)).z)));
                break;
            }
            uint _S818 = atlasWidth_6 / uint(_S816.x);
            float _S819 = float(_S818);
            uint _S820 = (atlasHeight_6 - 1U) / uint(_S816.y);
            float2 cellSize_13 = float2(_S819, float(_S820));
            _S192 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        for(;;)
        {
            float4 _S821 = float4(_S7->compositeControls_0) ;
            if((_S821.x) != 32.0f)
            {
                break;
            }
            bool _S822 = (_S821.w) >= 0.5f;
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
            uint _S823 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S822)
                {
                    for(;;)
                    {
                        if(_S823 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S824 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_11;
                        thread uint height_11;
                        (*((&width_11)) = (_S824).get_width(0)),(*((&height_11)) = (_S824).get_height(0));
                        float2 size_8 = float2(float(width_11), float(height_11));
                        float2 _S825 = float2(0.5f) ;
                        float2 remapped_8 = clamp((detailSource_0 * (size_8 - float2(2.0f) ) + float2(1.0f) ) / size_8, _S825 / size_8, (size_8 - _S825) / size_8);
                        if((_S823 & 1U) != 0U)
                        {
                            metallic_0 = remapped_8.x;
                        }
                        else
                        {
                            metallic_0 = detailSource_0.x;
                        }
                        if((_S823 & 2U) != 0U)
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
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S826 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S826).get_width(0)),(*((&atlasHeight_7)) = (_S826).get_height(0));
                int3 _S827 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S827)).xy), uint(((_S827)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S828 = atlasWidth_7;
                    uint _S829 = 2U % atlasWidth_7;
                    int _S830 = int(_S829);
                    uint _S831 = 2U / _S828;
                    int3 _S832 = int3(_S830, int(_S831), int(0));
                    int _S833 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S832)).xy), uint(((_S832)).z))).x * 255.0f)));
                    uint _S834 = atlasWidth_7;
                    uint _S835 = 3U % atlasWidth_7;
                    int _S836 = int(_S835);
                    uint _S837 = 3U / _S834;
                    int3 _S838 = int3(_S836, int(_S837), int(0));
                    int2 minimumTile_7 = int2(_S833, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S838)).xy), uint(((_S838)).z))).x * 255.0f))));
                    uint _S839 = atlasWidth_7;
                    uint _S840 = 4U % atlasWidth_7;
                    int _S841 = int(_S840);
                    uint _S842 = 4U / _S839;
                    int3 _S843 = int3(_S841, int(_S842), int(0));
                    uint _S844 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S843)).xy), uint(((_S843)).z))).x * 255.0f));
                    uint _S845 = atlasWidth_7;
                    uint _S846 = 5U % atlasWidth_7;
                    int _S847 = int(_S846);
                    uint _S848 = 5U / _S845;
                    int3 _S849 = int3(_S847, int(_S848), int(0));
                    uint _S850 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S849)).xy), uint(((_S849)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S844, _S850);
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
                        int3 _S851 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S851)).xy), uint(((_S851)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S844 + uint(tile_14.x));
                    uint _S852 = atlasWidth_7;
                    uint _S853 = offset_7 % atlasWidth_7;
                    int _S854 = int(_S853);
                    uint _S855 = offset_7 / _S852;
                    int3 _S856 = int3(_S854, int(_S855), int(0));
                    uint _S857 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S856)).xy), uint(((_S856)).z))).x * 255.0f));
                    uint _S858 = atlasWidth_7;
                    uint _S859 = offset_7 + 1U;
                    uint _S860 = _S859 % atlasWidth_7;
                    int _S861 = int(_S860);
                    uint _S862 = _S859 / _S858;
                    int3 _S863 = int3(_S861, int(_S862), int(0));
                    uint slot_14 = _S857 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S863)).xy), uint(((_S863)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S864 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S864)).xy), uint(((_S864)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S865 = atlasWidth_7;
                    uint _S866 = 6U % atlasWidth_7;
                    int _S867 = int(_S866);
                    uint _S868 = 6U / _S865;
                    int3 _S869 = int3(_S867, int(_S868), int(0));
                    uint _S870 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S869)).xy), uint(((_S869)).z))).x * 255.0f));
                    uint _S871 = atlasWidth_7;
                    uint _S872 = 7U % atlasWidth_7;
                    int _S873 = int(_S872);
                    uint _S874 = 7U / _S871;
                    int3 _S875 = int3(_S873, int(_S874), int(0));
                    uint columns_7 = _S870 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S875)).xy), uint(((_S875)).z))).x * 255.0f)) << 8U);
                    uint _S876 = atlasWidth_7;
                    uint _S877 = 8U % atlasWidth_7;
                    int _S878 = int(_S877);
                    uint _S879 = 8U / _S876;
                    int3 _S880 = int3(_S878, int(_S879), int(0));
                    uint _S881 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S880)).xy), uint(((_S880)).z))).x * 255.0f));
                    uint _S882 = atlasWidth_7;
                    uint _S883 = 9U % atlasWidth_7;
                    int _S884 = int(_S883);
                    uint _S885 = 9U / _S882;
                    int3 _S886 = int3(_S884, int(_S885), int(0));
                    uint rows_7 = ((_S881 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S886)).xy), uint(((_S886)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S844 * _S850 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S887 = atlasWidth_7 / columns_7;
                    float _S888 = float(_S887);
                    uint _S889 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S888, float(_S889));
                    uint _S890 = slot_15 % columns_7;
                    float _S891 = float(_S890);
                    uint _S892 = slot_15 / columns_7;
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S891, float(_S892)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(detailSource_0) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S893 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(detailSource_0)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S893);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S894 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S894)).xy), uint(((_S894)).z)));
                    break;
                }
                uint _S895 = atlasWidth_7 / uint(_S893.x);
                float _S896 = float(_S895);
                uint _S897 = (atlasHeight_7 - 1U) / uint(_S893.y);
                float2 cellSize_15 = float2(_S896, float(_S897));
                _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_2 = uint(round(_S821.y));
            if(operation_2 == 1U)
            {
                _S192 = _S192 * _S591;
                break;
            }
            if(operation_2 == 2U)
            {
                _S192 = _S192 + _S591;
                break;
            }
            if(operation_2 == 3U)
            {
                _S192 = _S192 - _S591;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S821.z;
                _S192 = _S192 * float4((1.0f - factor_2))  + _S591 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S192.x);
    }
    else
    {
        metallic_0 = _S167;
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
                detailSource_0 = _S191;
            }
            else
            {
                detailSource_0 = _S190;
            }
        }
        else
        {
            if(useSecondary_4)
            {
                detailSource_0 = _S183;
            }
            else
            {
                detailSource_0 = _S182;
            }
        }
        thread float4 emissionSample_0;
        bool _S898 = (udimMask_0 & 16U) != 0U;
        bool _S899 = (maskU_1 & 16U) != 0U;
        if(_S899)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S900 = (maskV_1 & 16U) != 0U;
        if(_S900)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S901 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S898)
            {
                for(;;)
                {
                    if(_S901 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S902 = (&kernelContext_0)->emissiveTexture_0;
                    thread uint width_12;
                    thread uint height_12;
                    (*((&width_12)) = (_S902).get_width(0)),(*((&height_12)) = (_S902).get_height(0));
                    float2 size_9 = float2(float(width_12), float(height_12));
                    float2 _S903 = float2(0.5f) ;
                    float2 remapped_9 = clamp((detailSource_0 * (size_9 - float2(2.0f) ) + float2(1.0f) ) / size_9, _S903 / size_9, (size_9 - _S903) / size_9);
                    if((_S901 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_9.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S901 & 2U) != 0U)
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
                _S192 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S904 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S904).get_width(0)),(*((&atlasHeight_8)) = (_S904).get_height(0));
            int3 _S905 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S905)).xy), uint(((_S905)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S906 = atlasWidth_8;
                uint _S907 = 2U % atlasWidth_8;
                int _S908 = int(_S907);
                uint _S909 = 2U / _S906;
                int3 _S910 = int3(_S908, int(_S909), int(0));
                int _S911 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S910)).xy), uint(((_S910)).z))).x * 255.0f)));
                uint _S912 = atlasWidth_8;
                uint _S913 = 3U % atlasWidth_8;
                int _S914 = int(_S913);
                uint _S915 = 3U / _S912;
                int3 _S916 = int3(_S914, int(_S915), int(0));
                int2 minimumTile_8 = int2(_S911, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S916)).xy), uint(((_S916)).z))).x * 255.0f))));
                uint _S917 = atlasWidth_8;
                uint _S918 = 4U % atlasWidth_8;
                int _S919 = int(_S918);
                uint _S920 = 4U / _S917;
                int3 _S921 = int3(_S919, int(_S920), int(0));
                uint _S922 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S921)).xy), uint(((_S921)).z))).x * 255.0f));
                uint _S923 = atlasWidth_8;
                uint _S924 = 5U % atlasWidth_8;
                int _S925 = int(_S924);
                uint _S926 = 5U / _S923;
                int3 _S927 = int3(_S925, int(_S926), int(0));
                uint _S928 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S927)).xy), uint(((_S927)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S922, _S928);
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
                    int3 _S929 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S929)).xy), uint(((_S929)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S922 + uint(tile_16.x));
                uint _S930 = atlasWidth_8;
                uint _S931 = offset_8 % atlasWidth_8;
                int _S932 = int(_S931);
                uint _S933 = offset_8 / _S930;
                int3 _S934 = int3(_S932, int(_S933), int(0));
                uint _S935 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S934)).xy), uint(((_S934)).z))).x * 255.0f));
                uint _S936 = atlasWidth_8;
                uint _S937 = offset_8 + 1U;
                uint _S938 = _S937 % atlasWidth_8;
                int _S939 = int(_S938);
                uint _S940 = _S937 / _S936;
                int3 _S941 = int3(_S939, int(_S940), int(0));
                uint slot_16 = _S935 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S941)).xy), uint(((_S941)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S942 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S942)).xy), uint(((_S942)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S943 = atlasWidth_8;
                uint _S944 = 6U % atlasWidth_8;
                int _S945 = int(_S944);
                uint _S946 = 6U / _S943;
                int3 _S947 = int3(_S945, int(_S946), int(0));
                uint _S948 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S947)).xy), uint(((_S947)).z))).x * 255.0f));
                uint _S949 = atlasWidth_8;
                uint _S950 = 7U % atlasWidth_8;
                int _S951 = int(_S950);
                uint _S952 = 7U / _S949;
                int3 _S953 = int3(_S951, int(_S952), int(0));
                uint columns_8 = _S948 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S953)).xy), uint(((_S953)).z))).x * 255.0f)) << 8U);
                uint _S954 = atlasWidth_8;
                uint _S955 = 8U % atlasWidth_8;
                int _S956 = int(_S955);
                uint _S957 = 8U / _S954;
                int3 _S958 = int3(_S956, int(_S957), int(0));
                uint _S959 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S958)).xy), uint(((_S958)).z))).x * 255.0f));
                uint _S960 = atlasWidth_8;
                uint _S961 = 9U % atlasWidth_8;
                int _S962 = int(_S961);
                uint _S963 = 9U / _S960;
                int3 _S964 = int3(_S962, int(_S963), int(0));
                uint rows_8 = ((_S959 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S964)).xy), uint(((_S964)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S922 * _S928 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S965 = atlasWidth_8 / columns_8;
                float _S966 = float(_S965);
                uint _S967 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S966, float(_S967));
                uint _S968 = slot_17 % columns_8;
                float _S969 = float(_S968);
                uint _S970 = slot_17 / columns_8;
                _S192 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S969, float(_S970)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(detailSource_0) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S971 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(detailSource_0)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S971);
            }
            if(hasSceneLighting_0)
            {
                int3 _S972 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S192 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S972)).xy), uint(((_S972)).z)));
                break;
            }
            uint _S973 = atlasWidth_8 / uint(_S971.x);
            float _S974 = float(_S973);
            uint _S975 = (atlasHeight_8 - 1U) / uint(_S971.y);
            float2 cellSize_17 = float2(_S974, float(_S975));
            _S192 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        emissionSample_0 = _S192;
        if((_S177.w) != 0.0f)
        {
            texture2d<float, access::sample> _S976 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S976).get_width(0)),(*((&sourceHeight_1)) = (_S976).get_height(0));
            texture2d<float, access::sample> _S977 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S977).get_width(0)),(*((&valueHeight_1)) = (_S977).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S978 = emissionSample_0;
                for(;;)
                {
                    float _S979 = _S978.w;
                    if(_S979 > 1.0f)
                    {
                        unlitColor_0 = _S978.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S980 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_13;
                    thread uint height_13;
                    (*((&width_13)) = (_S980).get_width(0)),(*((&height_13)) = (_S980).get_height(0));
                    uint3 _S981 = uint3(round(_S978.xyz * float3(255.0f) ));
                    uint index_4 = ((_S981.x) | ((_S981.y) << 8U)) | ((_S981.z) << 16U);
                    if(_S979 == 0.0f)
                    {
                        _S193 = _S981;
                    }
                    else
                    {
                        _S193 = uint3(index_4, index_4, index_4);
                    }
                    uint3 address_3 = _S193 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S982 = address_3.x;
                    uint _S983 = _S982 % width_13;
                    int _S984 = int(_S983);
                    uint _S985 = _S982 / width_13;
                    int3 _S986 = int3(_S984, int(_S985), int(0));
                    float _S987 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S986)).xy), uint(((_S986)).z))).x;
                    uint _S988 = address_3.y;
                    uint _S989 = _S988 % width_13;
                    int _S990 = int(_S989);
                    uint _S991 = _S988 / width_13;
                    int3 _S992 = int3(_S990, int(_S991), int(0));
                    float _S993 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S992)).xy), uint(((_S992)).z))).x;
                    uint _S994 = address_3.z;
                    uint _S995 = _S994 % width_13;
                    int _S996 = int(_S995);
                    uint _S997 = _S994 / width_13;
                    int3 _S998 = int3(_S996, int(_S997), int(0));
                    unlitColor_0 = float3(_S987, _S993, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S998)).xy), uint(((_S998)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                if(_S899)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S900)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S999 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S999 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1000 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_14;
                    thread uint height_14;
                    (*((&width_14)) = (_S1000).get_width(0)),(*((&height_14)) = (_S1000).get_height(0));
                    float2 size_10 = float2(float(width_14), float(height_14));
                    float2 _S1001 = float2(0.5f) ;
                    float2 remapped_10 = clamp((detailSource_0 * (size_10 - float2(2.0f) ) + float2(1.0f) ) / size_10, _S1001 / size_10, (size_10 - _S1001) / size_10);
                    if((_S999 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_10.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S999 & 2U) != 0U)
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
        float4 _S1002 = emissionSample_0;
        for(;;)
        {
            float4 _S1003 = float4(_S7->compositeControls_0) ;
            if((_S1003.x) != 16.0f)
            {
                _S192 = _S1002;
                break;
            }
            bool _S1004 = (_S1003.w) >= 0.5f;
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
            uint _S1005 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1004)
                {
                    for(;;)
                    {
                        if(_S1005 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1006 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_15;
                        thread uint height_15;
                        (*((&width_15)) = (_S1006).get_width(0)),(*((&height_15)) = (_S1006).get_height(0));
                        float2 size_11 = float2(float(width_15), float(height_15));
                        float2 _S1007 = float2(0.5f) ;
                        float2 remapped_11 = clamp((detailSource_0 * (size_11 - float2(2.0f) ) + float2(1.0f) ) / size_11, _S1007 / size_11, (size_11 - _S1007) / size_11);
                        if((_S1005 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_11.x;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.x;
                        }
                        if((_S1005 & 2U) != 0U)
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
                    _S192 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1008 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S1008).get_width(0)),(*((&atlasHeight_9)) = (_S1008).get_height(0));
                int3 _S1009 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1009)).xy), uint(((_S1009)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S1010 = atlasWidth_9;
                    uint _S1011 = 2U % atlasWidth_9;
                    int _S1012 = int(_S1011);
                    uint _S1013 = 2U / _S1010;
                    int3 _S1014 = int3(_S1012, int(_S1013), int(0));
                    int _S1015 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1014)).xy), uint(((_S1014)).z))).x * 255.0f)));
                    uint _S1016 = atlasWidth_9;
                    uint _S1017 = 3U % atlasWidth_9;
                    int _S1018 = int(_S1017);
                    uint _S1019 = 3U / _S1016;
                    int3 _S1020 = int3(_S1018, int(_S1019), int(0));
                    int2 minimumTile_9 = int2(_S1015, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1020)).xy), uint(((_S1020)).z))).x * 255.0f))));
                    uint _S1021 = atlasWidth_9;
                    uint _S1022 = 4U % atlasWidth_9;
                    int _S1023 = int(_S1022);
                    uint _S1024 = 4U / _S1021;
                    int3 _S1025 = int3(_S1023, int(_S1024), int(0));
                    uint _S1026 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1025)).xy), uint(((_S1025)).z))).x * 255.0f));
                    uint _S1027 = atlasWidth_9;
                    uint _S1028 = 5U % atlasWidth_9;
                    int _S1029 = int(_S1028);
                    uint _S1030 = 5U / _S1027;
                    int3 _S1031 = int3(_S1029, int(_S1030), int(0));
                    uint _S1032 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1031)).xy), uint(((_S1031)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S1026, _S1032);
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
                        int3 _S1033 = int3(int(1), int(0), int(0));
                        _S192 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1033)).xy), uint(((_S1033)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S1026 + uint(tile_18.x));
                    uint _S1034 = atlasWidth_9;
                    uint _S1035 = offset_9 % atlasWidth_9;
                    int _S1036 = int(_S1035);
                    uint _S1037 = offset_9 / _S1034;
                    int3 _S1038 = int3(_S1036, int(_S1037), int(0));
                    uint _S1039 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1038)).xy), uint(((_S1038)).z))).x * 255.0f));
                    uint _S1040 = atlasWidth_9;
                    uint _S1041 = offset_9 + 1U;
                    uint _S1042 = _S1041 % atlasWidth_9;
                    int _S1043 = int(_S1042);
                    uint _S1044 = _S1041 / _S1040;
                    int3 _S1045 = int3(_S1043, int(_S1044), int(0));
                    uint slot_18 = _S1039 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1045)).xy), uint(((_S1045)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S1046 = int3(int(1), int(0), int(0));
                        _S192 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1046)).xy), uint(((_S1046)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S1047 = atlasWidth_9;
                    uint _S1048 = 6U % atlasWidth_9;
                    int _S1049 = int(_S1048);
                    uint _S1050 = 6U / _S1047;
                    int3 _S1051 = int3(_S1049, int(_S1050), int(0));
                    uint _S1052 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1051)).xy), uint(((_S1051)).z))).x * 255.0f));
                    uint _S1053 = atlasWidth_9;
                    uint _S1054 = 7U % atlasWidth_9;
                    int _S1055 = int(_S1054);
                    uint _S1056 = 7U / _S1053;
                    int3 _S1057 = int3(_S1055, int(_S1056), int(0));
                    uint columns_9 = _S1052 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1057)).xy), uint(((_S1057)).z))).x * 255.0f)) << 8U);
                    uint _S1058 = atlasWidth_9;
                    uint _S1059 = 8U % atlasWidth_9;
                    int _S1060 = int(_S1059);
                    uint _S1061 = 8U / _S1058;
                    int3 _S1062 = int3(_S1060, int(_S1061), int(0));
                    uint _S1063 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1062)).xy), uint(((_S1062)).z))).x * 255.0f));
                    uint _S1064 = atlasWidth_9;
                    uint _S1065 = 9U % atlasWidth_9;
                    int _S1066 = int(_S1065);
                    uint _S1067 = 9U / _S1064;
                    int3 _S1068 = int3(_S1066, int(_S1067), int(0));
                    uint rows_9 = ((_S1063 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1068)).xy), uint(((_S1068)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S1026 * _S1032 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S1069 = atlasWidth_9 / columns_9;
                    float _S1070 = float(_S1069);
                    uint _S1071 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S1070, float(_S1071));
                    uint _S1072 = slot_19 % columns_9;
                    float _S1073 = float(_S1072);
                    uint _S1074 = slot_19 / columns_9;
                    _S192 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1073, float(_S1074)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(detailSource_0) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S1075 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(detailSource_0)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S1075);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1076 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S192 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1076)).xy), uint(((_S1076)).z)));
                    break;
                }
                uint _S1077 = atlasWidth_9 / uint(_S1075.x);
                float _S1078 = float(_S1077);
                uint _S1079 = (atlasHeight_9 - 1U) / uint(_S1075.y);
                float2 cellSize_19 = float2(_S1078, float(_S1079));
                _S192 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_3 = uint(round(_S1003.y));
            if(operation_3 == 1U)
            {
                _S192 = _S1002 * _S192;
                break;
            }
            if(operation_3 == 2U)
            {
                _S192 = _S1002 + _S192;
                break;
            }
            if(operation_3 == 3U)
            {
                _S192 = _S1002 - _S192;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S1003.z;
                _S192 = _S1002 * float4((1.0f - factor_3))  + _S192 * float4(factor_3) ;
                break;
            }
            _S192 = _S1002;
            break;
        }
        emissiveColor_1 = _S192.xyz;
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
                detailSource_0 = _S191;
            }
            else
            {
                detailSource_0 = _S190;
            }
        }
        else
        {
            if(useSecondary_5)
            {
                detailSource_0 = _S183;
            }
            else
            {
                detailSource_0 = _S182;
            }
        }
        for(;;)
        {
            bool _S1080 = (udimMask_0 & 64U) != 0U;
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
            uint _S1081 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1080)
                {
                    for(;;)
                    {
                        if(_S1081 == 0U)
                        {
                            tangent_1 = detailSource_0;
                            break;
                        }
                        texture2d<float, access::sample> _S1082 = (&kernelContext_0)->opacityTexture_0;
                        thread uint width_16;
                        thread uint height_16;
                        (*((&width_16)) = (_S1082).get_width(0)),(*((&height_16)) = (_S1082).get_height(0));
                        float2 size_12 = float2(float(width_16), float(height_16));
                        float2 _S1083 = float2(0.5f) ;
                        float2 remapped_12 = clamp((detailSource_0 * (size_12 - float2(2.0f) ) + float2(1.0f) ) / size_12, _S1083 / size_12, (size_12 - _S1083) / size_12);
                        if((_S1081 & 1U) != 0U)
                        {
                            opacity_0 = remapped_12.x;
                        }
                        else
                        {
                            opacity_0 = detailSource_0.x;
                        }
                        if((_S1081 & 2U) != 0U)
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
                    _S192 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (tangent_1)));
                    break;
                }
                texture2d<float, access::sample> _S1084 = (&kernelContext_0)->opacityTexture_0;
                thread uint atlasWidth_10;
                thread uint atlasHeight_10;
                (*((&atlasWidth_10)) = (_S1084).get_width(0)),(*((&atlasHeight_10)) = (_S1084).get_height(0));
                int3 _S1085 = int3(int(0), int(0), int(0));
                float4 metadata_10 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1085)).xy), uint(((_S1085)).z))) * float4(255.0f) );
                if((metadata_10.x) == 255.0f)
                {
                    uint _S1086 = atlasWidth_10;
                    uint _S1087 = 2U % atlasWidth_10;
                    int _S1088 = int(_S1087);
                    uint _S1089 = 2U / _S1086;
                    int3 _S1090 = int3(_S1088, int(_S1089), int(0));
                    int _S1091 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1090)).xy), uint(((_S1090)).z))).x * 255.0f)));
                    uint _S1092 = atlasWidth_10;
                    uint _S1093 = 3U % atlasWidth_10;
                    int _S1094 = int(_S1093);
                    uint _S1095 = 3U / _S1092;
                    int3 _S1096 = int3(_S1094, int(_S1095), int(0));
                    int2 minimumTile_10 = int2(_S1091, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1096)).xy), uint(((_S1096)).z))).x * 255.0f))));
                    uint _S1097 = atlasWidth_10;
                    uint _S1098 = 4U % atlasWidth_10;
                    int _S1099 = int(_S1098);
                    uint _S1100 = 4U / _S1097;
                    int3 _S1101 = int3(_S1099, int(_S1100), int(0));
                    uint _S1102 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1101)).xy), uint(((_S1101)).z))).x * 255.0f));
                    uint _S1103 = atlasWidth_10;
                    uint _S1104 = 5U % atlasWidth_10;
                    int _S1105 = int(_S1104);
                    uint _S1106 = 5U / _S1103;
                    int3 _S1107 = int3(_S1105, int(_S1106), int(0));
                    uint _S1108 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1107)).xy), uint(((_S1107)).z))).x * 255.0f));
                    uint2 grid_10 = uint2(_S1102, _S1108);
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
                        int3 _S1109 = int3(int(1), int(0), int(0));
                        _S192 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1109)).xy), uint(((_S1109)).z)));
                        break;
                    }
                    uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1102 + uint(tile_20.x));
                    uint _S1110 = atlasWidth_10;
                    uint _S1111 = offset_10 % atlasWidth_10;
                    int _S1112 = int(_S1111);
                    uint _S1113 = offset_10 / _S1110;
                    int3 _S1114 = int3(_S1112, int(_S1113), int(0));
                    uint _S1115 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1114)).xy), uint(((_S1114)).z))).x * 255.0f));
                    uint _S1116 = atlasWidth_10;
                    uint _S1117 = offset_10 + 1U;
                    uint _S1118 = _S1117 % atlasWidth_10;
                    int _S1119 = int(_S1118);
                    uint _S1120 = _S1117 / _S1116;
                    int3 _S1121 = int3(_S1119, int(_S1120), int(0));
                    uint slot_20 = _S1115 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1121)).xy), uint(((_S1121)).z))).x * 255.0f)) << 8U);
                    if(slot_20 == 0U)
                    {
                        int3 _S1122 = int3(int(1), int(0), int(0));
                        _S192 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1122)).xy), uint(((_S1122)).z)));
                        break;
                    }
                    uint slot_21 = slot_20 - 1U;
                    uint _S1123 = atlasWidth_10;
                    uint _S1124 = 6U % atlasWidth_10;
                    int _S1125 = int(_S1124);
                    uint _S1126 = 6U / _S1123;
                    int3 _S1127 = int3(_S1125, int(_S1126), int(0));
                    uint _S1128 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1127)).xy), uint(((_S1127)).z))).x * 255.0f));
                    uint _S1129 = atlasWidth_10;
                    uint _S1130 = 7U % atlasWidth_10;
                    int _S1131 = int(_S1130);
                    uint _S1132 = 7U / _S1129;
                    int3 _S1133 = int3(_S1131, int(_S1132), int(0));
                    uint columns_10 = _S1128 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1133)).xy), uint(((_S1133)).z))).x * 255.0f)) << 8U);
                    uint _S1134 = atlasWidth_10;
                    uint _S1135 = 8U % atlasWidth_10;
                    int _S1136 = int(_S1135);
                    uint _S1137 = 8U / _S1134;
                    int3 _S1138 = int3(_S1136, int(_S1137), int(0));
                    uint _S1139 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1138)).xy), uint(((_S1138)).z))).x * 255.0f));
                    uint _S1140 = atlasWidth_10;
                    uint _S1141 = 9U % atlasWidth_10;
                    int _S1142 = int(_S1141);
                    uint _S1143 = 9U / _S1140;
                    int3 _S1144 = int3(_S1142, int(_S1143), int(0));
                    uint rows_10 = ((_S1139 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1144)).xy), uint(((_S1144)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                    uint metadataRows_10 = (10U + 2U * _S1102 * _S1108 + atlasWidth_10 - 1U) / atlasWidth_10;
                    uint _S1145 = atlasWidth_10 / columns_10;
                    float _S1146 = float(_S1145);
                    uint _S1147 = (atlasHeight_10 - metadataRows_10) / rows_10;
                    float2 cellSize_20 = float2(_S1146, float(_S1147));
                    uint _S1148 = slot_21 % columns_10;
                    float _S1149 = float(_S1148);
                    uint _S1150 = slot_21 / columns_10;
                    _S192 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S1149, float(_S1150)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(detailSource_0) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                    break;
                }
                int2 _S1151 = int2(metadata_10.zw);
                int2 tile_21 = int2(floor(detailSource_0)) - int2(metadata_10.xy);
                if(any(tile_21 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_21 >= _S1151);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1152 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                    _S192 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1152)).xy), uint(((_S1152)).z)));
                    break;
                }
                uint _S1153 = atlasWidth_10 / uint(_S1151.x);
                float _S1154 = float(_S1153);
                uint _S1155 = (atlasHeight_10 - 1U) / uint(_S1151.y);
                float2 cellSize_21 = float2(_S1154, float(_S1155));
                _S192 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            for(;;)
            {
                float4 _S1156 = float4(_S7->compositeControls_0) ;
                if((_S1156.x) != 64.0f)
                {
                    break;
                }
                bool _S1157 = (_S1156.w) >= 0.5f;
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
                uint _S1158 = index_0 | lightCount_0;
                for(;;)
                {
                    if(!_S1157)
                    {
                        for(;;)
                        {
                            if(_S1158 == 0U)
                            {
                                break;
                            }
                            texture2d<float, access::sample> _S1159 = (&kernelContext_0)->compositeTexture_0;
                            thread uint width_17;
                            thread uint height_17;
                            (*((&width_17)) = (_S1159).get_width(0)),(*((&height_17)) = (_S1159).get_height(0));
                            float2 size_13 = float2(float(width_17), float(height_17));
                            float2 _S1160 = float2(0.5f) ;
                            float2 remapped_13 = clamp((detailSource_0 * (size_13 - float2(2.0f) ) + float2(1.0f) ) / size_13, _S1160 / size_13, (size_13 - _S1160) / size_13);
                            if((_S1158 & 1U) != 0U)
                            {
                                opacity_0 = remapped_13.x;
                            }
                            else
                            {
                                opacity_0 = detailSource_0.x;
                            }
                            if((_S1158 & 2U) != 0U)
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
                        _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                        break;
                    }
                    texture2d<float, access::sample> _S1161 = (&kernelContext_0)->compositeTexture_0;
                    thread uint atlasWidth_11;
                    thread uint atlasHeight_11;
                    (*((&atlasWidth_11)) = (_S1161).get_width(0)),(*((&atlasHeight_11)) = (_S1161).get_height(0));
                    int3 _S1162 = int3(int(0), int(0), int(0));
                    float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1162)).xy), uint(((_S1162)).z))) * float4(255.0f) );
                    if((metadata_11.x) == 255.0f)
                    {
                        uint _S1163 = atlasWidth_11;
                        uint _S1164 = 2U % atlasWidth_11;
                        int _S1165 = int(_S1164);
                        uint _S1166 = 2U / _S1163;
                        int3 _S1167 = int3(_S1165, int(_S1166), int(0));
                        int _S1168 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1167)).xy), uint(((_S1167)).z))).x * 255.0f)));
                        uint _S1169 = atlasWidth_11;
                        uint _S1170 = 3U % atlasWidth_11;
                        int _S1171 = int(_S1170);
                        uint _S1172 = 3U / _S1169;
                        int3 _S1173 = int3(_S1171, int(_S1172), int(0));
                        int2 minimumTile_11 = int2(_S1168, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1173)).xy), uint(((_S1173)).z))).x * 255.0f))));
                        uint _S1174 = atlasWidth_11;
                        uint _S1175 = 4U % atlasWidth_11;
                        int _S1176 = int(_S1175);
                        uint _S1177 = 4U / _S1174;
                        int3 _S1178 = int3(_S1176, int(_S1177), int(0));
                        uint _S1179 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1178)).xy), uint(((_S1178)).z))).x * 255.0f));
                        uint _S1180 = atlasWidth_11;
                        uint _S1181 = 5U % atlasWidth_11;
                        int _S1182 = int(_S1181);
                        uint _S1183 = 5U / _S1180;
                        int3 _S1184 = int3(_S1182, int(_S1183), int(0));
                        uint _S1185 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1184)).xy), uint(((_S1184)).z))).x * 255.0f));
                        uint2 grid_11 = uint2(_S1179, _S1185);
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
                            int3 _S1186 = int3(int(1), int(0), int(0));
                            _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1186)).xy), uint(((_S1186)).z)));
                            break;
                        }
                        uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1179 + uint(tile_22.x));
                        uint _S1187 = atlasWidth_11;
                        uint _S1188 = offset_11 % atlasWidth_11;
                        int _S1189 = int(_S1188);
                        uint _S1190 = offset_11 / _S1187;
                        int3 _S1191 = int3(_S1189, int(_S1190), int(0));
                        uint _S1192 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1191)).xy), uint(((_S1191)).z))).x * 255.0f));
                        uint _S1193 = atlasWidth_11;
                        uint _S1194 = offset_11 + 1U;
                        uint _S1195 = _S1194 % atlasWidth_11;
                        int _S1196 = int(_S1195);
                        uint _S1197 = _S1194 / _S1193;
                        int3 _S1198 = int3(_S1196, int(_S1197), int(0));
                        uint slot_22 = _S1192 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1198)).xy), uint(((_S1198)).z))).x * 255.0f)) << 8U);
                        if(slot_22 == 0U)
                        {
                            int3 _S1199 = int3(int(1), int(0), int(0));
                            _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1199)).xy), uint(((_S1199)).z)));
                            break;
                        }
                        uint slot_23 = slot_22 - 1U;
                        uint _S1200 = atlasWidth_11;
                        uint _S1201 = 6U % atlasWidth_11;
                        int _S1202 = int(_S1201);
                        uint _S1203 = 6U / _S1200;
                        int3 _S1204 = int3(_S1202, int(_S1203), int(0));
                        uint _S1205 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1204)).xy), uint(((_S1204)).z))).x * 255.0f));
                        uint _S1206 = atlasWidth_11;
                        uint _S1207 = 7U % atlasWidth_11;
                        int _S1208 = int(_S1207);
                        uint _S1209 = 7U / _S1206;
                        int3 _S1210 = int3(_S1208, int(_S1209), int(0));
                        uint columns_11 = _S1205 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1210)).xy), uint(((_S1210)).z))).x * 255.0f)) << 8U);
                        uint _S1211 = atlasWidth_11;
                        uint _S1212 = 8U % atlasWidth_11;
                        int _S1213 = int(_S1212);
                        uint _S1214 = 8U / _S1211;
                        int3 _S1215 = int3(_S1213, int(_S1214), int(0));
                        uint _S1216 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1215)).xy), uint(((_S1215)).z))).x * 255.0f));
                        uint _S1217 = atlasWidth_11;
                        uint _S1218 = 9U % atlasWidth_11;
                        int _S1219 = int(_S1218);
                        uint _S1220 = 9U / _S1217;
                        int3 _S1221 = int3(_S1219, int(_S1220), int(0));
                        uint rows_11 = ((_S1216 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1221)).xy), uint(((_S1221)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                        uint metadataRows_11 = (10U + 2U * _S1179 * _S1185 + atlasWidth_11 - 1U) / atlasWidth_11;
                        uint _S1222 = atlasWidth_11 / columns_11;
                        float _S1223 = float(_S1222);
                        uint _S1224 = (atlasHeight_11 - metadataRows_11) / rows_11;
                        float2 cellSize_22 = float2(_S1223, float(_S1224));
                        uint _S1225 = slot_23 % columns_11;
                        float _S1226 = float(_S1225);
                        uint _S1227 = slot_23 / columns_11;
                        _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1226, float(_S1227)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(detailSource_0) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                        break;
                    }
                    int2 _S1228 = int2(metadata_11.zw);
                    int2 tile_23 = int2(floor(detailSource_0)) - int2(metadata_11.xy);
                    if(any(tile_23 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_23 >= _S1228);
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1229 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1229)).xy), uint(((_S1229)).z)));
                        break;
                    }
                    uint _S1230 = atlasWidth_11 / uint(_S1228.x);
                    float _S1231 = float(_S1230);
                    uint _S1232 = (atlasHeight_11 - 1U) / uint(_S1228.y);
                    float2 cellSize_23 = float2(_S1231, float(_S1232));
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                uint operation_4 = uint(round(_S1156.y));
                if(operation_4 == 1U)
                {
                    _S192 = _S192 * _S591;
                    break;
                }
                if(operation_4 == 2U)
                {
                    _S192 = _S192 + _S591;
                    break;
                }
                if(operation_4 == 3U)
                {
                    _S192 = _S192 - _S591;
                    break;
                }
                if(operation_4 == 4U)
                {
                    float factor_4 = _S1156.z;
                    _S192 = _S192 * float4((1.0f - factor_4))  + _S591 * float4(factor_4) ;
                    break;
                }
                break;
            }
            _S4 = saturate(_S192.x);
            break;
        }
        opacity_0 = _S4;
    }
    if((textureMask_0 & 128U) != 0U)
    {
        bool useSecondary_6 = (secondaryMask_0 & 128U) != 0U;
        if((alternateMask_0 & 128U) != 0U)
        {
            if(useSecondary_6)
            {
                detailSource_0 = _S191;
            }
            else
            {
                detailSource_0 = _S190;
            }
        }
        else
        {
            if(useSecondary_6)
            {
                detailSource_0 = _S183;
            }
            else
            {
                detailSource_0 = _S182;
            }
        }
        bool _S1233 = (udimMask_0 & 128U) != 0U;
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
        uint _S1234 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1233)
            {
                for(;;)
                {
                    if(_S1234 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1235 = (&kernelContext_0)->occlusionTexture_0;
                    thread uint width_18;
                    thread uint height_18;
                    (*((&width_18)) = (_S1235).get_width(0)),(*((&height_18)) = (_S1235).get_height(0));
                    float2 size_14 = float2(float(width_18), float(height_18));
                    float2 _S1236 = float2(0.5f) ;
                    float2 remapped_14 = clamp((detailSource_0 * (size_14 - float2(2.0f) ) + float2(1.0f) ) / size_14, _S1236 / size_14, (size_14 - _S1236) / size_14);
                    if((_S1234 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_14.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S1234 & 2U) != 0U)
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
                _S192 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1237 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1237).get_width(0)),(*((&atlasHeight_12)) = (_S1237).get_height(0));
            int3 _S1238 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1238)).xy), uint(((_S1238)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1239 = atlasWidth_12;
                uint _S1240 = 2U % atlasWidth_12;
                int _S1241 = int(_S1240);
                uint _S1242 = 2U / _S1239;
                int3 _S1243 = int3(_S1241, int(_S1242), int(0));
                int _S1244 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1243)).xy), uint(((_S1243)).z))).x * 255.0f)));
                uint _S1245 = atlasWidth_12;
                uint _S1246 = 3U % atlasWidth_12;
                int _S1247 = int(_S1246);
                uint _S1248 = 3U / _S1245;
                int3 _S1249 = int3(_S1247, int(_S1248), int(0));
                int2 minimumTile_12 = int2(_S1244, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1249)).xy), uint(((_S1249)).z))).x * 255.0f))));
                uint _S1250 = atlasWidth_12;
                uint _S1251 = 4U % atlasWidth_12;
                int _S1252 = int(_S1251);
                uint _S1253 = 4U / _S1250;
                int3 _S1254 = int3(_S1252, int(_S1253), int(0));
                uint _S1255 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1254)).xy), uint(((_S1254)).z))).x * 255.0f));
                uint _S1256 = atlasWidth_12;
                uint _S1257 = 5U % atlasWidth_12;
                int _S1258 = int(_S1257);
                uint _S1259 = 5U / _S1256;
                int3 _S1260 = int3(_S1258, int(_S1259), int(0));
                uint _S1261 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1260)).xy), uint(((_S1260)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1255, _S1261);
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
                    int3 _S1262 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1262)).xy), uint(((_S1262)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1255 + uint(tile_24.x));
                uint _S1263 = atlasWidth_12;
                uint _S1264 = offset_12 % atlasWidth_12;
                int _S1265 = int(_S1264);
                uint _S1266 = offset_12 / _S1263;
                int3 _S1267 = int3(_S1265, int(_S1266), int(0));
                uint _S1268 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1267)).xy), uint(((_S1267)).z))).x * 255.0f));
                uint _S1269 = atlasWidth_12;
                uint _S1270 = offset_12 + 1U;
                uint _S1271 = _S1270 % atlasWidth_12;
                int _S1272 = int(_S1271);
                uint _S1273 = _S1270 / _S1269;
                int3 _S1274 = int3(_S1272, int(_S1273), int(0));
                uint slot_24 = _S1268 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1274)).xy), uint(((_S1274)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1275 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1275)).xy), uint(((_S1275)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1276 = atlasWidth_12;
                uint _S1277 = 6U % atlasWidth_12;
                int _S1278 = int(_S1277);
                uint _S1279 = 6U / _S1276;
                int3 _S1280 = int3(_S1278, int(_S1279), int(0));
                uint _S1281 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1280)).xy), uint(((_S1280)).z))).x * 255.0f));
                uint _S1282 = atlasWidth_12;
                uint _S1283 = 7U % atlasWidth_12;
                int _S1284 = int(_S1283);
                uint _S1285 = 7U / _S1282;
                int3 _S1286 = int3(_S1284, int(_S1285), int(0));
                uint columns_12 = _S1281 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1286)).xy), uint(((_S1286)).z))).x * 255.0f)) << 8U);
                uint _S1287 = atlasWidth_12;
                uint _S1288 = 8U % atlasWidth_12;
                int _S1289 = int(_S1288);
                uint _S1290 = 8U / _S1287;
                int3 _S1291 = int3(_S1289, int(_S1290), int(0));
                uint _S1292 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1291)).xy), uint(((_S1291)).z))).x * 255.0f));
                uint _S1293 = atlasWidth_12;
                uint _S1294 = 9U % atlasWidth_12;
                int _S1295 = int(_S1294);
                uint _S1296 = 9U / _S1293;
                int3 _S1297 = int3(_S1295, int(_S1296), int(0));
                uint rows_12 = ((_S1292 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1297)).xy), uint(((_S1297)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1255 * _S1261 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1298 = atlasWidth_12 / columns_12;
                float _S1299 = float(_S1298);
                uint _S1300 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1299, float(_S1300));
                uint _S1301 = slot_25 % columns_12;
                float _S1302 = float(_S1301);
                uint _S1303 = slot_25 / columns_12;
                _S192 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1302, float(_S1303)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(detailSource_0) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1304 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(detailSource_0)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1304);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1305 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S192 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1305)).xy), uint(((_S1305)).z)));
                break;
            }
            uint _S1306 = atlasWidth_12 / uint(_S1304.x);
            float _S1307 = float(_S1306);
            uint _S1308 = (atlasHeight_12 - 1U) / uint(_S1304.y);
            float2 cellSize_25 = float2(_S1307, float(_S1308));
            _S192 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1309 = float4(_S7->compositeControls_0) ;
            if((_S1309.x) != 128.0f)
            {
                break;
            }
            bool _S1310 = (_S1309.w) >= 0.5f;
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
            uint _S1311 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1310)
                {
                    for(;;)
                    {
                        if(_S1311 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1312 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_19;
                        thread uint height_19;
                        (*((&width_19)) = (_S1312).get_width(0)),(*((&height_19)) = (_S1312).get_height(0));
                        float2 size_15 = float2(float(width_19), float(height_19));
                        float2 _S1313 = float2(0.5f) ;
                        float2 remapped_15 = clamp((detailSource_0 * (size_15 - float2(2.0f) ) + float2(1.0f) ) / size_15, _S1313 / size_15, (size_15 - _S1313) / size_15);
                        if((_S1311 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_15.x;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.x;
                        }
                        if((_S1311 & 2U) != 0U)
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
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1314 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1314).get_width(0)),(*((&atlasHeight_13)) = (_S1314).get_height(0));
                int3 _S1315 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1315)).xy), uint(((_S1315)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1316 = atlasWidth_13;
                    uint _S1317 = 2U % atlasWidth_13;
                    int _S1318 = int(_S1317);
                    uint _S1319 = 2U / _S1316;
                    int3 _S1320 = int3(_S1318, int(_S1319), int(0));
                    int _S1321 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1320)).xy), uint(((_S1320)).z))).x * 255.0f)));
                    uint _S1322 = atlasWidth_13;
                    uint _S1323 = 3U % atlasWidth_13;
                    int _S1324 = int(_S1323);
                    uint _S1325 = 3U / _S1322;
                    int3 _S1326 = int3(_S1324, int(_S1325), int(0));
                    int2 minimumTile_13 = int2(_S1321, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1326)).xy), uint(((_S1326)).z))).x * 255.0f))));
                    uint _S1327 = atlasWidth_13;
                    uint _S1328 = 4U % atlasWidth_13;
                    int _S1329 = int(_S1328);
                    uint _S1330 = 4U / _S1327;
                    int3 _S1331 = int3(_S1329, int(_S1330), int(0));
                    uint _S1332 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1331)).xy), uint(((_S1331)).z))).x * 255.0f));
                    uint _S1333 = atlasWidth_13;
                    uint _S1334 = 5U % atlasWidth_13;
                    int _S1335 = int(_S1334);
                    uint _S1336 = 5U / _S1333;
                    int3 _S1337 = int3(_S1335, int(_S1336), int(0));
                    uint _S1338 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1337)).xy), uint(((_S1337)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1332, _S1338);
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
                        int3 _S1339 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1339)).xy), uint(((_S1339)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1332 + uint(tile_26.x));
                    uint _S1340 = atlasWidth_13;
                    uint _S1341 = offset_13 % atlasWidth_13;
                    int _S1342 = int(_S1341);
                    uint _S1343 = offset_13 / _S1340;
                    int3 _S1344 = int3(_S1342, int(_S1343), int(0));
                    uint _S1345 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1344)).xy), uint(((_S1344)).z))).x * 255.0f));
                    uint _S1346 = atlasWidth_13;
                    uint _S1347 = offset_13 + 1U;
                    uint _S1348 = _S1347 % atlasWidth_13;
                    int _S1349 = int(_S1348);
                    uint _S1350 = _S1347 / _S1346;
                    int3 _S1351 = int3(_S1349, int(_S1350), int(0));
                    uint slot_26 = _S1345 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1351)).xy), uint(((_S1351)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1352 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1352)).xy), uint(((_S1352)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1353 = atlasWidth_13;
                    uint _S1354 = 6U % atlasWidth_13;
                    int _S1355 = int(_S1354);
                    uint _S1356 = 6U / _S1353;
                    int3 _S1357 = int3(_S1355, int(_S1356), int(0));
                    uint _S1358 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1357)).xy), uint(((_S1357)).z))).x * 255.0f));
                    uint _S1359 = atlasWidth_13;
                    uint _S1360 = 7U % atlasWidth_13;
                    int _S1361 = int(_S1360);
                    uint _S1362 = 7U / _S1359;
                    int3 _S1363 = int3(_S1361, int(_S1362), int(0));
                    uint columns_13 = _S1358 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1363)).xy), uint(((_S1363)).z))).x * 255.0f)) << 8U);
                    uint _S1364 = atlasWidth_13;
                    uint _S1365 = 8U % atlasWidth_13;
                    int _S1366 = int(_S1365);
                    uint _S1367 = 8U / _S1364;
                    int3 _S1368 = int3(_S1366, int(_S1367), int(0));
                    uint _S1369 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1368)).xy), uint(((_S1368)).z))).x * 255.0f));
                    uint _S1370 = atlasWidth_13;
                    uint _S1371 = 9U % atlasWidth_13;
                    int _S1372 = int(_S1371);
                    uint _S1373 = 9U / _S1370;
                    int3 _S1374 = int3(_S1372, int(_S1373), int(0));
                    uint rows_13 = ((_S1369 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1374)).xy), uint(((_S1374)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1332 * _S1338 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1375 = atlasWidth_13 / columns_13;
                    float _S1376 = float(_S1375);
                    uint _S1377 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1376, float(_S1377));
                    uint _S1378 = slot_27 % columns_13;
                    float _S1379 = float(_S1378);
                    uint _S1380 = slot_27 / columns_13;
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1379, float(_S1380)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(detailSource_0) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1381 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(detailSource_0)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1381);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1382 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1382)).xy), uint(((_S1382)).z)));
                    break;
                }
                uint _S1383 = atlasWidth_13 / uint(_S1381.x);
                float _S1384 = float(_S1383);
                uint _S1385 = (atlasHeight_13 - 1U) / uint(_S1381.y);
                float2 cellSize_27 = float2(_S1384, float(_S1385));
                _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_5 = uint(round(_S1309.y));
            if(operation_5 == 1U)
            {
                _S192 = _S192 * _S591;
                break;
            }
            if(operation_5 == 2U)
            {
                _S192 = _S192 + _S591;
                break;
            }
            if(operation_5 == 3U)
            {
                _S192 = _S192 - _S591;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1309.z;
                _S192 = _S192 * float4((1.0f - factor_5))  + _S591 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S192.x);
    }
    else
    {
        occlusion_0 = _S162;
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
                detailSource_0 = _S191;
            }
            else
            {
                detailSource_0 = _S190;
            }
        }
        else
        {
            if(useSecondary_7)
            {
                detailSource_0 = _S183;
            }
            else
            {
                detailSource_0 = _S182;
            }
        }
        bool _S1386 = (udimMask_0 & 256U) != 0U;
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
        uint _S1387 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1386)
            {
                for(;;)
                {
                    if(_S1387 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1388 = (&kernelContext_0)->specularColorTexture_0;
                    thread uint width_20;
                    thread uint height_20;
                    (*((&width_20)) = (_S1388).get_width(0)),(*((&height_20)) = (_S1388).get_height(0));
                    float2 size_16 = float2(float(width_20), float(height_20));
                    float2 _S1389 = float2(0.5f) ;
                    float2 remapped_16 = clamp((detailSource_0 * (size_16 - float2(2.0f) ) + float2(1.0f) ) / size_16, _S1389 / size_16, (size_16 - _S1389) / size_16);
                    if((_S1387 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_16.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.x;
                    }
                    if((_S1387 & 2U) != 0U)
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
                _S192 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1390 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1390).get_width(0)),(*((&atlasHeight_14)) = (_S1390).get_height(0));
            int3 _S1391 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1391)).xy), uint(((_S1391)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1392 = atlasWidth_14;
                uint _S1393 = 2U % atlasWidth_14;
                int _S1394 = int(_S1393);
                uint _S1395 = 2U / _S1392;
                int3 _S1396 = int3(_S1394, int(_S1395), int(0));
                int _S1397 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1396)).xy), uint(((_S1396)).z))).x * 255.0f)));
                uint _S1398 = atlasWidth_14;
                uint _S1399 = 3U % atlasWidth_14;
                int _S1400 = int(_S1399);
                uint _S1401 = 3U / _S1398;
                int3 _S1402 = int3(_S1400, int(_S1401), int(0));
                int2 minimumTile_14 = int2(_S1397, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1402)).xy), uint(((_S1402)).z))).x * 255.0f))));
                uint _S1403 = atlasWidth_14;
                uint _S1404 = 4U % atlasWidth_14;
                int _S1405 = int(_S1404);
                uint _S1406 = 4U / _S1403;
                int3 _S1407 = int3(_S1405, int(_S1406), int(0));
                uint _S1408 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1407)).xy), uint(((_S1407)).z))).x * 255.0f));
                uint _S1409 = atlasWidth_14;
                uint _S1410 = 5U % atlasWidth_14;
                int _S1411 = int(_S1410);
                uint _S1412 = 5U / _S1409;
                int3 _S1413 = int3(_S1411, int(_S1412), int(0));
                uint _S1414 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1413)).xy), uint(((_S1413)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1408, _S1414);
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
                    int3 _S1415 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1415)).xy), uint(((_S1415)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1408 + uint(tile_28.x));
                uint _S1416 = atlasWidth_14;
                uint _S1417 = offset_14 % atlasWidth_14;
                int _S1418 = int(_S1417);
                uint _S1419 = offset_14 / _S1416;
                int3 _S1420 = int3(_S1418, int(_S1419), int(0));
                uint _S1421 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1420)).xy), uint(((_S1420)).z))).x * 255.0f));
                uint _S1422 = atlasWidth_14;
                uint _S1423 = offset_14 + 1U;
                uint _S1424 = _S1423 % atlasWidth_14;
                int _S1425 = int(_S1424);
                uint _S1426 = _S1423 / _S1422;
                int3 _S1427 = int3(_S1425, int(_S1426), int(0));
                uint slot_28 = _S1421 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1427)).xy), uint(((_S1427)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1428 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1428)).xy), uint(((_S1428)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1429 = atlasWidth_14;
                uint _S1430 = 6U % atlasWidth_14;
                int _S1431 = int(_S1430);
                uint _S1432 = 6U / _S1429;
                int3 _S1433 = int3(_S1431, int(_S1432), int(0));
                uint _S1434 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1433)).xy), uint(((_S1433)).z))).x * 255.0f));
                uint _S1435 = atlasWidth_14;
                uint _S1436 = 7U % atlasWidth_14;
                int _S1437 = int(_S1436);
                uint _S1438 = 7U / _S1435;
                int3 _S1439 = int3(_S1437, int(_S1438), int(0));
                uint columns_14 = _S1434 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1439)).xy), uint(((_S1439)).z))).x * 255.0f)) << 8U);
                uint _S1440 = atlasWidth_14;
                uint _S1441 = 8U % atlasWidth_14;
                int _S1442 = int(_S1441);
                uint _S1443 = 8U / _S1440;
                int3 _S1444 = int3(_S1442, int(_S1443), int(0));
                uint _S1445 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1444)).xy), uint(((_S1444)).z))).x * 255.0f));
                uint _S1446 = atlasWidth_14;
                uint _S1447 = 9U % atlasWidth_14;
                int _S1448 = int(_S1447);
                uint _S1449 = 9U / _S1446;
                int3 _S1450 = int3(_S1448, int(_S1449), int(0));
                uint rows_14 = ((_S1445 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1450)).xy), uint(((_S1450)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1408 * _S1414 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1451 = atlasWidth_14 / columns_14;
                float _S1452 = float(_S1451);
                uint _S1453 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1452, float(_S1453));
                uint _S1454 = slot_29 % columns_14;
                float _S1455 = float(_S1454);
                uint _S1456 = slot_29 / columns_14;
                _S192 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1455, float(_S1456)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(detailSource_0) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1457 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(detailSource_0)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1457);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1458 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S192 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1458)).xy), uint(((_S1458)).z)));
                break;
            }
            uint _S1459 = atlasWidth_14 / uint(_S1457.x);
            float _S1460 = float(_S1459);
            uint _S1461 = (atlasHeight_14 - 1U) / uint(_S1457.y);
            float2 cellSize_29 = float2(_S1460, float(_S1461));
            _S192 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1462 = float4(_S7->compositeControls_0) ;
            if((_S1462.x) != 256.0f)
            {
                break;
            }
            bool _S1463 = (_S1462.w) >= 0.5f;
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
            uint _S1464 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1463)
                {
                    for(;;)
                    {
                        if(_S1464 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1465 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_21;
                        thread uint height_21;
                        (*((&width_21)) = (_S1465).get_width(0)),(*((&height_21)) = (_S1465).get_height(0));
                        float2 size_17 = float2(float(width_21), float(height_21));
                        float2 _S1466 = float2(0.5f) ;
                        float2 remapped_17 = clamp((detailSource_0 * (size_17 - float2(2.0f) ) + float2(1.0f) ) / size_17, _S1466 / size_17, (size_17 - _S1466) / size_17);
                        if((_S1464 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_17.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.x;
                        }
                        if((_S1464 & 2U) != 0U)
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
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1467 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1467).get_width(0)),(*((&atlasHeight_15)) = (_S1467).get_height(0));
                int3 _S1468 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1468)).xy), uint(((_S1468)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1469 = atlasWidth_15;
                    uint _S1470 = 2U % atlasWidth_15;
                    int _S1471 = int(_S1470);
                    uint _S1472 = 2U / _S1469;
                    int3 _S1473 = int3(_S1471, int(_S1472), int(0));
                    int _S1474 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1473)).xy), uint(((_S1473)).z))).x * 255.0f)));
                    uint _S1475 = atlasWidth_15;
                    uint _S1476 = 3U % atlasWidth_15;
                    int _S1477 = int(_S1476);
                    uint _S1478 = 3U / _S1475;
                    int3 _S1479 = int3(_S1477, int(_S1478), int(0));
                    int2 minimumTile_15 = int2(_S1474, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1479)).xy), uint(((_S1479)).z))).x * 255.0f))));
                    uint _S1480 = atlasWidth_15;
                    uint _S1481 = 4U % atlasWidth_15;
                    int _S1482 = int(_S1481);
                    uint _S1483 = 4U / _S1480;
                    int3 _S1484 = int3(_S1482, int(_S1483), int(0));
                    uint _S1485 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1484)).xy), uint(((_S1484)).z))).x * 255.0f));
                    uint _S1486 = atlasWidth_15;
                    uint _S1487 = 5U % atlasWidth_15;
                    int _S1488 = int(_S1487);
                    uint _S1489 = 5U / _S1486;
                    int3 _S1490 = int3(_S1488, int(_S1489), int(0));
                    uint _S1491 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1490)).xy), uint(((_S1490)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1485, _S1491);
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
                        int3 _S1492 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1492)).xy), uint(((_S1492)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1485 + uint(tile_30.x));
                    uint _S1493 = atlasWidth_15;
                    uint _S1494 = offset_15 % atlasWidth_15;
                    int _S1495 = int(_S1494);
                    uint _S1496 = offset_15 / _S1493;
                    int3 _S1497 = int3(_S1495, int(_S1496), int(0));
                    uint _S1498 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1497)).xy), uint(((_S1497)).z))).x * 255.0f));
                    uint _S1499 = atlasWidth_15;
                    uint _S1500 = offset_15 + 1U;
                    uint _S1501 = _S1500 % atlasWidth_15;
                    int _S1502 = int(_S1501);
                    uint _S1503 = _S1500 / _S1499;
                    int3 _S1504 = int3(_S1502, int(_S1503), int(0));
                    uint slot_30 = _S1498 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1504)).xy), uint(((_S1504)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1505 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1505)).xy), uint(((_S1505)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1506 = atlasWidth_15;
                    uint _S1507 = 6U % atlasWidth_15;
                    int _S1508 = int(_S1507);
                    uint _S1509 = 6U / _S1506;
                    int3 _S1510 = int3(_S1508, int(_S1509), int(0));
                    uint _S1511 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1510)).xy), uint(((_S1510)).z))).x * 255.0f));
                    uint _S1512 = atlasWidth_15;
                    uint _S1513 = 7U % atlasWidth_15;
                    int _S1514 = int(_S1513);
                    uint _S1515 = 7U / _S1512;
                    int3 _S1516 = int3(_S1514, int(_S1515), int(0));
                    uint columns_15 = _S1511 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1516)).xy), uint(((_S1516)).z))).x * 255.0f)) << 8U);
                    uint _S1517 = atlasWidth_15;
                    uint _S1518 = 8U % atlasWidth_15;
                    int _S1519 = int(_S1518);
                    uint _S1520 = 8U / _S1517;
                    int3 _S1521 = int3(_S1519, int(_S1520), int(0));
                    uint _S1522 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1521)).xy), uint(((_S1521)).z))).x * 255.0f));
                    uint _S1523 = atlasWidth_15;
                    uint _S1524 = 9U % atlasWidth_15;
                    int _S1525 = int(_S1524);
                    uint _S1526 = 9U / _S1523;
                    int3 _S1527 = int3(_S1525, int(_S1526), int(0));
                    uint rows_15 = ((_S1522 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1527)).xy), uint(((_S1527)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1485 * _S1491 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1528 = atlasWidth_15 / columns_15;
                    float _S1529 = float(_S1528);
                    uint _S1530 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1529, float(_S1530));
                    uint _S1531 = slot_31 % columns_15;
                    float _S1532 = float(_S1531);
                    uint _S1533 = slot_31 / columns_15;
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1532, float(_S1533)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(detailSource_0) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1534 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(detailSource_0)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1534);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1535 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1535)).xy), uint(((_S1535)).z)));
                    break;
                }
                uint _S1536 = atlasWidth_15 / uint(_S1534.x);
                float _S1537 = float(_S1536);
                uint _S1538 = (atlasHeight_15 - 1U) / uint(_S1534.y);
                float2 cellSize_31 = float2(_S1537, float(_S1538));
                _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_6 = uint(round(_S1462.y));
            if(operation_6 == 1U)
            {
                _S192 = _S192 * _S591;
                break;
            }
            if(operation_6 == 2U)
            {
                _S192 = _S192 + _S591;
                break;
            }
            if(operation_6 == 3U)
            {
                _S192 = _S192 - _S591;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1462.z;
                _S192 = _S192 * float4((1.0f - factor_6))  + _S591 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S192.xyz);
    }
    else
    {
        specularColor_0 = _S170;
    }
    if((textureMask_0 & 512U) != 0U)
    {
        bool useSecondary_8 = (secondaryMask_0 & 512U) != 0U;
        if((alternateMask_0 & 512U) != 0U)
        {
            if(useSecondary_8)
            {
                detailSource_0 = _S191;
            }
            else
            {
                detailSource_0 = _S190;
            }
        }
        else
        {
            if(useSecondary_8)
            {
                detailSource_0 = _S183;
            }
            else
            {
                detailSource_0 = _S182;
            }
        }
        bool _S1539 = (udimMask_0 & 512U) != 0U;
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
        uint _S1540 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1539)
            {
                for(;;)
                {
                    if(_S1540 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1541 = (&kernelContext_0)->clearcoatTexture_0;
                    thread uint width_22;
                    thread uint height_22;
                    (*((&width_22)) = (_S1541).get_width(0)),(*((&height_22)) = (_S1541).get_height(0));
                    float2 size_18 = float2(float(width_22), float(height_22));
                    float2 _S1542 = float2(0.5f) ;
                    float2 remapped_18 = clamp((detailSource_0 * (size_18 - float2(2.0f) ) + float2(1.0f) ) / size_18, _S1542 / size_18, (size_18 - _S1542) / size_18);
                    if((_S1540 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_18.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.x;
                    }
                    if((_S1540 & 2U) != 0U)
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
                _S192 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1543 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1543).get_width(0)),(*((&atlasHeight_16)) = (_S1543).get_height(0));
            int3 _S1544 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1544)).xy), uint(((_S1544)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1545 = atlasWidth_16;
                uint _S1546 = 2U % atlasWidth_16;
                int _S1547 = int(_S1546);
                uint _S1548 = 2U / _S1545;
                int3 _S1549 = int3(_S1547, int(_S1548), int(0));
                int _S1550 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1549)).xy), uint(((_S1549)).z))).x * 255.0f)));
                uint _S1551 = atlasWidth_16;
                uint _S1552 = 3U % atlasWidth_16;
                int _S1553 = int(_S1552);
                uint _S1554 = 3U / _S1551;
                int3 _S1555 = int3(_S1553, int(_S1554), int(0));
                int2 minimumTile_16 = int2(_S1550, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1555)).xy), uint(((_S1555)).z))).x * 255.0f))));
                uint _S1556 = atlasWidth_16;
                uint _S1557 = 4U % atlasWidth_16;
                int _S1558 = int(_S1557);
                uint _S1559 = 4U / _S1556;
                int3 _S1560 = int3(_S1558, int(_S1559), int(0));
                uint _S1561 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1560)).xy), uint(((_S1560)).z))).x * 255.0f));
                uint _S1562 = atlasWidth_16;
                uint _S1563 = 5U % atlasWidth_16;
                int _S1564 = int(_S1563);
                uint _S1565 = 5U / _S1562;
                int3 _S1566 = int3(_S1564, int(_S1565), int(0));
                uint _S1567 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1566)).xy), uint(((_S1566)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1561, _S1567);
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
                    int3 _S1568 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1568)).xy), uint(((_S1568)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1561 + uint(tile_32.x));
                uint _S1569 = atlasWidth_16;
                uint _S1570 = offset_16 % atlasWidth_16;
                int _S1571 = int(_S1570);
                uint _S1572 = offset_16 / _S1569;
                int3 _S1573 = int3(_S1571, int(_S1572), int(0));
                uint _S1574 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1573)).xy), uint(((_S1573)).z))).x * 255.0f));
                uint _S1575 = atlasWidth_16;
                uint _S1576 = offset_16 + 1U;
                uint _S1577 = _S1576 % atlasWidth_16;
                int _S1578 = int(_S1577);
                uint _S1579 = _S1576 / _S1575;
                int3 _S1580 = int3(_S1578, int(_S1579), int(0));
                uint slot_32 = _S1574 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1580)).xy), uint(((_S1580)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1581 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1581)).xy), uint(((_S1581)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1582 = atlasWidth_16;
                uint _S1583 = 6U % atlasWidth_16;
                int _S1584 = int(_S1583);
                uint _S1585 = 6U / _S1582;
                int3 _S1586 = int3(_S1584, int(_S1585), int(0));
                uint _S1587 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1586)).xy), uint(((_S1586)).z))).x * 255.0f));
                uint _S1588 = atlasWidth_16;
                uint _S1589 = 7U % atlasWidth_16;
                int _S1590 = int(_S1589);
                uint _S1591 = 7U / _S1588;
                int3 _S1592 = int3(_S1590, int(_S1591), int(0));
                uint columns_16 = _S1587 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1592)).xy), uint(((_S1592)).z))).x * 255.0f)) << 8U);
                uint _S1593 = atlasWidth_16;
                uint _S1594 = 8U % atlasWidth_16;
                int _S1595 = int(_S1594);
                uint _S1596 = 8U / _S1593;
                int3 _S1597 = int3(_S1595, int(_S1596), int(0));
                uint _S1598 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1597)).xy), uint(((_S1597)).z))).x * 255.0f));
                uint _S1599 = atlasWidth_16;
                uint _S1600 = 9U % atlasWidth_16;
                int _S1601 = int(_S1600);
                uint _S1602 = 9U / _S1599;
                int3 _S1603 = int3(_S1601, int(_S1602), int(0));
                uint rows_16 = ((_S1598 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1603)).xy), uint(((_S1603)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1561 * _S1567 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1604 = atlasWidth_16 / columns_16;
                float _S1605 = float(_S1604);
                uint _S1606 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1605, float(_S1606));
                uint _S1607 = slot_33 % columns_16;
                float _S1608 = float(_S1607);
                uint _S1609 = slot_33 / columns_16;
                _S192 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1608, float(_S1609)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(detailSource_0) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1610 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(detailSource_0)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1610);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1611 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S192 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1611)).xy), uint(((_S1611)).z)));
                break;
            }
            uint _S1612 = atlasWidth_16 / uint(_S1610.x);
            float _S1613 = float(_S1612);
            uint _S1614 = (atlasHeight_16 - 1U) / uint(_S1610.y);
            float2 cellSize_33 = float2(_S1613, float(_S1614));
            _S192 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1615 = float4(_S7->compositeControls_0) ;
            if((_S1615.x) != 512.0f)
            {
                break;
            }
            bool _S1616 = (_S1615.w) >= 0.5f;
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
            uint _S1617 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1616)
                {
                    for(;;)
                    {
                        if(_S1617 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1618 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_23;
                        thread uint height_23;
                        (*((&width_23)) = (_S1618).get_width(0)),(*((&height_23)) = (_S1618).get_height(0));
                        float2 size_19 = float2(float(width_23), float(height_23));
                        float2 _S1619 = float2(0.5f) ;
                        float2 remapped_19 = clamp((detailSource_0 * (size_19 - float2(2.0f) ) + float2(1.0f) ) / size_19, _S1619 / size_19, (size_19 - _S1619) / size_19);
                        if((_S1617 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_19.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.x;
                        }
                        if((_S1617 & 2U) != 0U)
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
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1620 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1620).get_width(0)),(*((&atlasHeight_17)) = (_S1620).get_height(0));
                int3 _S1621 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1621)).xy), uint(((_S1621)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1622 = atlasWidth_17;
                    uint _S1623 = 2U % atlasWidth_17;
                    int _S1624 = int(_S1623);
                    uint _S1625 = 2U / _S1622;
                    int3 _S1626 = int3(_S1624, int(_S1625), int(0));
                    int _S1627 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1626)).xy), uint(((_S1626)).z))).x * 255.0f)));
                    uint _S1628 = atlasWidth_17;
                    uint _S1629 = 3U % atlasWidth_17;
                    int _S1630 = int(_S1629);
                    uint _S1631 = 3U / _S1628;
                    int3 _S1632 = int3(_S1630, int(_S1631), int(0));
                    int2 minimumTile_17 = int2(_S1627, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1632)).xy), uint(((_S1632)).z))).x * 255.0f))));
                    uint _S1633 = atlasWidth_17;
                    uint _S1634 = 4U % atlasWidth_17;
                    int _S1635 = int(_S1634);
                    uint _S1636 = 4U / _S1633;
                    int3 _S1637 = int3(_S1635, int(_S1636), int(0));
                    uint _S1638 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1637)).xy), uint(((_S1637)).z))).x * 255.0f));
                    uint _S1639 = atlasWidth_17;
                    uint _S1640 = 5U % atlasWidth_17;
                    int _S1641 = int(_S1640);
                    uint _S1642 = 5U / _S1639;
                    int3 _S1643 = int3(_S1641, int(_S1642), int(0));
                    uint _S1644 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1643)).xy), uint(((_S1643)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1638, _S1644);
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
                        int3 _S1645 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1645)).xy), uint(((_S1645)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1638 + uint(tile_34.x));
                    uint _S1646 = atlasWidth_17;
                    uint _S1647 = offset_17 % atlasWidth_17;
                    int _S1648 = int(_S1647);
                    uint _S1649 = offset_17 / _S1646;
                    int3 _S1650 = int3(_S1648, int(_S1649), int(0));
                    uint _S1651 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1650)).xy), uint(((_S1650)).z))).x * 255.0f));
                    uint _S1652 = atlasWidth_17;
                    uint _S1653 = offset_17 + 1U;
                    uint _S1654 = _S1653 % atlasWidth_17;
                    int _S1655 = int(_S1654);
                    uint _S1656 = _S1653 / _S1652;
                    int3 _S1657 = int3(_S1655, int(_S1656), int(0));
                    uint slot_34 = _S1651 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1657)).xy), uint(((_S1657)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1658 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1658)).xy), uint(((_S1658)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1659 = atlasWidth_17;
                    uint _S1660 = 6U % atlasWidth_17;
                    int _S1661 = int(_S1660);
                    uint _S1662 = 6U / _S1659;
                    int3 _S1663 = int3(_S1661, int(_S1662), int(0));
                    uint _S1664 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1663)).xy), uint(((_S1663)).z))).x * 255.0f));
                    uint _S1665 = atlasWidth_17;
                    uint _S1666 = 7U % atlasWidth_17;
                    int _S1667 = int(_S1666);
                    uint _S1668 = 7U / _S1665;
                    int3 _S1669 = int3(_S1667, int(_S1668), int(0));
                    uint columns_17 = _S1664 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1669)).xy), uint(((_S1669)).z))).x * 255.0f)) << 8U);
                    uint _S1670 = atlasWidth_17;
                    uint _S1671 = 8U % atlasWidth_17;
                    int _S1672 = int(_S1671);
                    uint _S1673 = 8U / _S1670;
                    int3 _S1674 = int3(_S1672, int(_S1673), int(0));
                    uint _S1675 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1674)).xy), uint(((_S1674)).z))).x * 255.0f));
                    uint _S1676 = atlasWidth_17;
                    uint _S1677 = 9U % atlasWidth_17;
                    int _S1678 = int(_S1677);
                    uint _S1679 = 9U / _S1676;
                    int3 _S1680 = int3(_S1678, int(_S1679), int(0));
                    uint rows_17 = ((_S1675 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1680)).xy), uint(((_S1680)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1638 * _S1644 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1681 = atlasWidth_17 / columns_17;
                    float _S1682 = float(_S1681);
                    uint _S1683 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1682, float(_S1683));
                    uint _S1684 = slot_35 % columns_17;
                    float _S1685 = float(_S1684);
                    uint _S1686 = slot_35 / columns_17;
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1685, float(_S1686)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(detailSource_0) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1687 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(detailSource_0)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1687);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1688 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1688)).xy), uint(((_S1688)).z)));
                    break;
                }
                uint _S1689 = atlasWidth_17 / uint(_S1687.x);
                float _S1690 = float(_S1689);
                uint _S1691 = (atlasHeight_17 - 1U) / uint(_S1687.y);
                float2 cellSize_35 = float2(_S1690, float(_S1691));
                _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_7 = uint(round(_S1615.y));
            if(operation_7 == 1U)
            {
                _S192 = _S192 * _S591;
                break;
            }
            if(operation_7 == 2U)
            {
                _S192 = _S192 + _S591;
                break;
            }
            if(operation_7 == 3U)
            {
                _S192 = _S192 - _S591;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1615.z;
                _S192 = _S192 * float4((1.0f - factor_7))  + _S591 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S192.x);
    }
    else
    {
        clearcoatAmount_0 = _S172;
    }
    float ior_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool useSecondary_9 = (secondaryMask_0 & 1024U) != 0U;
        if((alternateMask_0 & 1024U) != 0U)
        {
            if(useSecondary_9)
            {
                detailSource_0 = _S191;
            }
            else
            {
                detailSource_0 = _S190;
            }
        }
        else
        {
            if(useSecondary_9)
            {
                detailSource_0 = _S183;
            }
            else
            {
                detailSource_0 = _S182;
            }
        }
        bool _S1692 = (udimMask_0 & 1024U) != 0U;
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
        uint _S1693 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1692)
            {
                for(;;)
                {
                    if(_S1693 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1694 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
                    thread uint width_24;
                    thread uint height_24;
                    (*((&width_24)) = (_S1694).get_width(0)),(*((&height_24)) = (_S1694).get_height(0));
                    float2 size_20 = float2(float(width_24), float(height_24));
                    float2 _S1695 = float2(0.5f) ;
                    float2 remapped_20 = clamp((detailSource_0 * (size_20 - float2(2.0f) ) + float2(1.0f) ) / size_20, _S1695 / size_20, (size_20 - _S1695) / size_20);
                    if((_S1693 & 1U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_20.x;
                    }
                    else
                    {
                        clearcoatRoughness_0 = detailSource_0.x;
                    }
                    if((_S1693 & 2U) != 0U)
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
                _S192 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1696 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1696).get_width(0)),(*((&atlasHeight_18)) = (_S1696).get_height(0));
            int3 _S1697 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1697)).xy), uint(((_S1697)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1698 = atlasWidth_18;
                uint _S1699 = 2U % atlasWidth_18;
                int _S1700 = int(_S1699);
                uint _S1701 = 2U / _S1698;
                int3 _S1702 = int3(_S1700, int(_S1701), int(0));
                int _S1703 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1702)).xy), uint(((_S1702)).z))).x * 255.0f)));
                uint _S1704 = atlasWidth_18;
                uint _S1705 = 3U % atlasWidth_18;
                int _S1706 = int(_S1705);
                uint _S1707 = 3U / _S1704;
                int3 _S1708 = int3(_S1706, int(_S1707), int(0));
                int2 minimumTile_18 = int2(_S1703, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1708)).xy), uint(((_S1708)).z))).x * 255.0f))));
                uint _S1709 = atlasWidth_18;
                uint _S1710 = 4U % atlasWidth_18;
                int _S1711 = int(_S1710);
                uint _S1712 = 4U / _S1709;
                int3 _S1713 = int3(_S1711, int(_S1712), int(0));
                uint _S1714 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1713)).xy), uint(((_S1713)).z))).x * 255.0f));
                uint _S1715 = atlasWidth_18;
                uint _S1716 = 5U % atlasWidth_18;
                int _S1717 = int(_S1716);
                uint _S1718 = 5U / _S1715;
                int3 _S1719 = int3(_S1717, int(_S1718), int(0));
                uint _S1720 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1719)).xy), uint(((_S1719)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1714, _S1720);
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
                    int3 _S1721 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1721)).xy), uint(((_S1721)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1714 + uint(tile_36.x));
                uint _S1722 = atlasWidth_18;
                uint _S1723 = offset_18 % atlasWidth_18;
                int _S1724 = int(_S1723);
                uint _S1725 = offset_18 / _S1722;
                int3 _S1726 = int3(_S1724, int(_S1725), int(0));
                uint _S1727 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1726)).xy), uint(((_S1726)).z))).x * 255.0f));
                uint _S1728 = atlasWidth_18;
                uint _S1729 = offset_18 + 1U;
                uint _S1730 = _S1729 % atlasWidth_18;
                int _S1731 = int(_S1730);
                uint _S1732 = _S1729 / _S1728;
                int3 _S1733 = int3(_S1731, int(_S1732), int(0));
                uint slot_36 = _S1727 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1733)).xy), uint(((_S1733)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1734 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1734)).xy), uint(((_S1734)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1735 = atlasWidth_18;
                uint _S1736 = 6U % atlasWidth_18;
                int _S1737 = int(_S1736);
                uint _S1738 = 6U / _S1735;
                int3 _S1739 = int3(_S1737, int(_S1738), int(0));
                uint _S1740 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1739)).xy), uint(((_S1739)).z))).x * 255.0f));
                uint _S1741 = atlasWidth_18;
                uint _S1742 = 7U % atlasWidth_18;
                int _S1743 = int(_S1742);
                uint _S1744 = 7U / _S1741;
                int3 _S1745 = int3(_S1743, int(_S1744), int(0));
                uint columns_18 = _S1740 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1745)).xy), uint(((_S1745)).z))).x * 255.0f)) << 8U);
                uint _S1746 = atlasWidth_18;
                uint _S1747 = 8U % atlasWidth_18;
                int _S1748 = int(_S1747);
                uint _S1749 = 8U / _S1746;
                int3 _S1750 = int3(_S1748, int(_S1749), int(0));
                uint _S1751 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1750)).xy), uint(((_S1750)).z))).x * 255.0f));
                uint _S1752 = atlasWidth_18;
                uint _S1753 = 9U % atlasWidth_18;
                int _S1754 = int(_S1753);
                uint _S1755 = 9U / _S1752;
                int3 _S1756 = int3(_S1754, int(_S1755), int(0));
                uint rows_18 = ((_S1751 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1756)).xy), uint(((_S1756)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1714 * _S1720 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1757 = atlasWidth_18 / columns_18;
                float _S1758 = float(_S1757);
                uint _S1759 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1758, float(_S1759));
                uint _S1760 = slot_37 % columns_18;
                float _S1761 = float(_S1760);
                uint _S1762 = slot_37 / columns_18;
                _S192 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1761, float(_S1762)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(detailSource_0) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1763 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(detailSource_0)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1763);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1764 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S192 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1764)).xy), uint(((_S1764)).z)));
                break;
            }
            uint _S1765 = atlasWidth_18 / uint(_S1763.x);
            float _S1766 = float(_S1765);
            uint _S1767 = (atlasHeight_18 - 1U) / uint(_S1763.y);
            float2 cellSize_37 = float2(_S1766, float(_S1767));
            _S192 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1768 = float4(_S7->compositeControls_0) ;
            if((_S1768.x) != 1024.0f)
            {
                break;
            }
            bool _S1769 = (_S1768.w) >= 0.5f;
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
            uint _S1770 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1769)
                {
                    for(;;)
                    {
                        if(_S1770 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1771 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_25;
                        thread uint height_25;
                        (*((&width_25)) = (_S1771).get_width(0)),(*((&height_25)) = (_S1771).get_height(0));
                        float2 size_21 = float2(float(width_25), float(height_25));
                        float2 _S1772 = float2(0.5f) ;
                        float2 remapped_21 = clamp((detailSource_0 * (size_21 - float2(2.0f) ) + float2(1.0f) ) / size_21, _S1772 / size_21, (size_21 - _S1772) / size_21);
                        if((_S1770 & 1U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_21.x;
                        }
                        else
                        {
                            clearcoatRoughness_0 = detailSource_0.x;
                        }
                        if((_S1770 & 2U) != 0U)
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
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1773 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1773).get_width(0)),(*((&atlasHeight_19)) = (_S1773).get_height(0));
                int3 _S1774 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1774)).xy), uint(((_S1774)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1775 = atlasWidth_19;
                    uint _S1776 = 2U % atlasWidth_19;
                    int _S1777 = int(_S1776);
                    uint _S1778 = 2U / _S1775;
                    int3 _S1779 = int3(_S1777, int(_S1778), int(0));
                    int _S1780 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1779)).xy), uint(((_S1779)).z))).x * 255.0f)));
                    uint _S1781 = atlasWidth_19;
                    uint _S1782 = 3U % atlasWidth_19;
                    int _S1783 = int(_S1782);
                    uint _S1784 = 3U / _S1781;
                    int3 _S1785 = int3(_S1783, int(_S1784), int(0));
                    int2 minimumTile_19 = int2(_S1780, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1785)).xy), uint(((_S1785)).z))).x * 255.0f))));
                    uint _S1786 = atlasWidth_19;
                    uint _S1787 = 4U % atlasWidth_19;
                    int _S1788 = int(_S1787);
                    uint _S1789 = 4U / _S1786;
                    int3 _S1790 = int3(_S1788, int(_S1789), int(0));
                    uint _S1791 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1790)).xy), uint(((_S1790)).z))).x * 255.0f));
                    uint _S1792 = atlasWidth_19;
                    uint _S1793 = 5U % atlasWidth_19;
                    int _S1794 = int(_S1793);
                    uint _S1795 = 5U / _S1792;
                    int3 _S1796 = int3(_S1794, int(_S1795), int(0));
                    uint _S1797 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1796)).xy), uint(((_S1796)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1791, _S1797);
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
                        int3 _S1798 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1798)).xy), uint(((_S1798)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1791 + uint(tile_38.x));
                    uint _S1799 = atlasWidth_19;
                    uint _S1800 = offset_19 % atlasWidth_19;
                    int _S1801 = int(_S1800);
                    uint _S1802 = offset_19 / _S1799;
                    int3 _S1803 = int3(_S1801, int(_S1802), int(0));
                    uint _S1804 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1803)).xy), uint(((_S1803)).z))).x * 255.0f));
                    uint _S1805 = atlasWidth_19;
                    uint _S1806 = offset_19 + 1U;
                    uint _S1807 = _S1806 % atlasWidth_19;
                    int _S1808 = int(_S1807);
                    uint _S1809 = _S1806 / _S1805;
                    int3 _S1810 = int3(_S1808, int(_S1809), int(0));
                    uint slot_38 = _S1804 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1810)).xy), uint(((_S1810)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1811 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1811)).xy), uint(((_S1811)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1812 = atlasWidth_19;
                    uint _S1813 = 6U % atlasWidth_19;
                    int _S1814 = int(_S1813);
                    uint _S1815 = 6U / _S1812;
                    int3 _S1816 = int3(_S1814, int(_S1815), int(0));
                    uint _S1817 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1816)).xy), uint(((_S1816)).z))).x * 255.0f));
                    uint _S1818 = atlasWidth_19;
                    uint _S1819 = 7U % atlasWidth_19;
                    int _S1820 = int(_S1819);
                    uint _S1821 = 7U / _S1818;
                    int3 _S1822 = int3(_S1820, int(_S1821), int(0));
                    uint columns_19 = _S1817 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1822)).xy), uint(((_S1822)).z))).x * 255.0f)) << 8U);
                    uint _S1823 = atlasWidth_19;
                    uint _S1824 = 8U % atlasWidth_19;
                    int _S1825 = int(_S1824);
                    uint _S1826 = 8U / _S1823;
                    int3 _S1827 = int3(_S1825, int(_S1826), int(0));
                    uint _S1828 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1827)).xy), uint(((_S1827)).z))).x * 255.0f));
                    uint _S1829 = atlasWidth_19;
                    uint _S1830 = 9U % atlasWidth_19;
                    int _S1831 = int(_S1830);
                    uint _S1832 = 9U / _S1829;
                    int3 _S1833 = int3(_S1831, int(_S1832), int(0));
                    uint rows_19 = ((_S1828 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1833)).xy), uint(((_S1833)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1791 * _S1797 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1834 = atlasWidth_19 / columns_19;
                    float _S1835 = float(_S1834);
                    uint _S1836 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1835, float(_S1836));
                    uint _S1837 = slot_39 % columns_19;
                    float _S1838 = float(_S1837);
                    uint _S1839 = slot_39 / columns_19;
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1838, float(_S1839)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(detailSource_0) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1840 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(detailSource_0)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1840);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1841 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1841)).xy), uint(((_S1841)).z)));
                    break;
                }
                uint _S1842 = atlasWidth_19 / uint(_S1840.x);
                float _S1843 = float(_S1842);
                uint _S1844 = (atlasHeight_19 - 1U) / uint(_S1840.y);
                float2 cellSize_39 = float2(_S1843, float(_S1844));
                _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_8 = uint(round(_S1768.y));
            if(operation_8 == 1U)
            {
                _S192 = _S192 * _S591;
                break;
            }
            if(operation_8 == 2U)
            {
                _S192 = _S192 + _S591;
                break;
            }
            if(operation_8 == 3U)
            {
                _S192 = _S192 - _S591;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1768.z;
                _S192 = _S192 * float4((1.0f - factor_8))  + _S591 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S192.x);
    }
    else
    {
        clearcoatRoughness_0 = _S173;
    }
    float lightType_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool useSecondary_10 = (secondaryMask_0 & 2048U) != 0U;
        if((alternateMask_0 & 2048U) != 0U)
        {
            if(useSecondary_10)
            {
                detailSource_0 = _S191;
            }
            else
            {
                detailSource_0 = _S190;
            }
        }
        else
        {
            if(useSecondary_10)
            {
                detailSource_0 = _S183;
            }
            else
            {
                detailSource_0 = _S182;
            }
        }
        bool _S1845 = (udimMask_0 & 2048U) != 0U;
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
        uint _S1846 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1845)
            {
                for(;;)
                {
                    if(_S1846 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1847 = (&kernelContext_0)->iorTexture_0;
                    thread uint width_26;
                    thread uint height_26;
                    (*((&width_26)) = (_S1847).get_width(0)),(*((&height_26)) = (_S1847).get_height(0));
                    float2 size_22 = float2(float(width_26), float(height_26));
                    float2 _S1848 = float2(0.5f) ;
                    float2 remapped_22 = clamp((detailSource_0 * (size_22 - float2(2.0f) ) + float2(1.0f) ) / size_22, _S1848 / size_22, (size_22 - _S1848) / size_22);
                    if((_S1846 & 1U) != 0U)
                    {
                        ior_0 = remapped_22.x;
                    }
                    else
                    {
                        ior_0 = detailSource_0.x;
                    }
                    if((_S1846 & 2U) != 0U)
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
                _S192 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1849 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_20;
            thread uint atlasHeight_20;
            (*((&atlasWidth_20)) = (_S1849).get_width(0)),(*((&atlasHeight_20)) = (_S1849).get_height(0));
            int3 _S1850 = int3(int(0), int(0), int(0));
            float4 metadata_20 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1850)).xy), uint(((_S1850)).z))) * float4(255.0f) );
            if((metadata_20.x) == 255.0f)
            {
                uint _S1851 = atlasWidth_20;
                uint _S1852 = 2U % atlasWidth_20;
                int _S1853 = int(_S1852);
                uint _S1854 = 2U / _S1851;
                int3 _S1855 = int3(_S1853, int(_S1854), int(0));
                int _S1856 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1855)).xy), uint(((_S1855)).z))).x * 255.0f)));
                uint _S1857 = atlasWidth_20;
                uint _S1858 = 3U % atlasWidth_20;
                int _S1859 = int(_S1858);
                uint _S1860 = 3U / _S1857;
                int3 _S1861 = int3(_S1859, int(_S1860), int(0));
                int2 minimumTile_20 = int2(_S1856, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1861)).xy), uint(((_S1861)).z))).x * 255.0f))));
                uint _S1862 = atlasWidth_20;
                uint _S1863 = 4U % atlasWidth_20;
                int _S1864 = int(_S1863);
                uint _S1865 = 4U / _S1862;
                int3 _S1866 = int3(_S1864, int(_S1865), int(0));
                uint _S1867 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1866)).xy), uint(((_S1866)).z))).x * 255.0f));
                uint _S1868 = atlasWidth_20;
                uint _S1869 = 5U % atlasWidth_20;
                int _S1870 = int(_S1869);
                uint _S1871 = 5U / _S1868;
                int3 _S1872 = int3(_S1870, int(_S1871), int(0));
                uint _S1873 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1872)).xy), uint(((_S1872)).z))).x * 255.0f));
                uint2 grid_20 = uint2(_S1867, _S1873);
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
                    int3 _S1874 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1874)).xy), uint(((_S1874)).z)));
                    break;
                }
                uint offset_20 = 10U + 2U * (uint(tile_40.y) * _S1867 + uint(tile_40.x));
                uint _S1875 = atlasWidth_20;
                uint _S1876 = offset_20 % atlasWidth_20;
                int _S1877 = int(_S1876);
                uint _S1878 = offset_20 / _S1875;
                int3 _S1879 = int3(_S1877, int(_S1878), int(0));
                uint _S1880 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1879)).xy), uint(((_S1879)).z))).x * 255.0f));
                uint _S1881 = atlasWidth_20;
                uint _S1882 = offset_20 + 1U;
                uint _S1883 = _S1882 % atlasWidth_20;
                int _S1884 = int(_S1883);
                uint _S1885 = _S1882 / _S1881;
                int3 _S1886 = int3(_S1884, int(_S1885), int(0));
                uint slot_40 = _S1880 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1886)).xy), uint(((_S1886)).z))).x * 255.0f)) << 8U);
                if(slot_40 == 0U)
                {
                    int3 _S1887 = int3(int(1), int(0), int(0));
                    _S192 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1887)).xy), uint(((_S1887)).z)));
                    break;
                }
                uint slot_41 = slot_40 - 1U;
                uint _S1888 = atlasWidth_20;
                uint _S1889 = 6U % atlasWidth_20;
                int _S1890 = int(_S1889);
                uint _S1891 = 6U / _S1888;
                int3 _S1892 = int3(_S1890, int(_S1891), int(0));
                uint _S1893 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1892)).xy), uint(((_S1892)).z))).x * 255.0f));
                uint _S1894 = atlasWidth_20;
                uint _S1895 = 7U % atlasWidth_20;
                int _S1896 = int(_S1895);
                uint _S1897 = 7U / _S1894;
                int3 _S1898 = int3(_S1896, int(_S1897), int(0));
                uint columns_20 = _S1893 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1898)).xy), uint(((_S1898)).z))).x * 255.0f)) << 8U);
                uint _S1899 = atlasWidth_20;
                uint _S1900 = 8U % atlasWidth_20;
                int _S1901 = int(_S1900);
                uint _S1902 = 8U / _S1899;
                int3 _S1903 = int3(_S1901, int(_S1902), int(0));
                uint _S1904 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1903)).xy), uint(((_S1903)).z))).x * 255.0f));
                uint _S1905 = atlasWidth_20;
                uint _S1906 = 9U % atlasWidth_20;
                int _S1907 = int(_S1906);
                uint _S1908 = 9U / _S1905;
                int3 _S1909 = int3(_S1907, int(_S1908), int(0));
                uint rows_20 = ((_S1904 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1909)).xy), uint(((_S1909)).z))).x * 255.0f)) << 8U)) + columns_20 - 1U) / columns_20;
                uint metadataRows_20 = (10U + 2U * _S1867 * _S1873 + atlasWidth_20 - 1U) / atlasWidth_20;
                uint _S1910 = atlasWidth_20 / columns_20;
                float _S1911 = float(_S1910);
                uint _S1912 = (atlasHeight_20 - metadataRows_20) / rows_20;
                float2 cellSize_40 = float2(_S1911, float(_S1912));
                uint _S1913 = slot_41 % columns_20;
                float _S1914 = float(_S1913);
                uint _S1915 = slot_41 / columns_20;
                _S192 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1914, float(_S1915)) * cellSize_40 + float2(1.5f, float(metadataRows_20) + 1.5f) + fract(detailSource_0) * max(cellSize_40 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
                break;
            }
            int2 _S1916 = int2(metadata_20.zw);
            int2 tile_41 = int2(floor(detailSource_0)) - int2(metadata_20.xy);
            if(any(tile_41 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_41 >= _S1916);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1917 = int3(int(min(1U, atlasWidth_20 - 1U)), int(0), int(0));
                _S192 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1917)).xy), uint(((_S1917)).z)));
                break;
            }
            uint _S1918 = atlasWidth_20 / uint(_S1916.x);
            float _S1919 = float(_S1918);
            uint _S1920 = (atlasHeight_20 - 1U) / uint(_S1916.y);
            float2 cellSize_41 = float2(_S1919, float(_S1920));
            _S192 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_41) * cellSize_41 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_41 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
            break;
        }
        for(;;)
        {
            float4 _S1921 = float4(_S7->compositeControls_0) ;
            if((_S1921.x) != 2048.0f)
            {
                break;
            }
            bool _S1922 = (_S1921.w) >= 0.5f;
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
            uint _S1923 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1922)
                {
                    for(;;)
                    {
                        if(_S1923 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1924 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_27;
                        thread uint height_27;
                        (*((&width_27)) = (_S1924).get_width(0)),(*((&height_27)) = (_S1924).get_height(0));
                        float2 size_23 = float2(float(width_27), float(height_27));
                        float2 _S1925 = float2(0.5f) ;
                        float2 remapped_23 = clamp((detailSource_0 * (size_23 - float2(2.0f) ) + float2(1.0f) ) / size_23, _S1925 / size_23, (size_23 - _S1925) / size_23);
                        if((_S1923 & 1U) != 0U)
                        {
                            ior_0 = remapped_23.x;
                        }
                        else
                        {
                            ior_0 = detailSource_0.x;
                        }
                        if((_S1923 & 2U) != 0U)
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
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1926 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_21;
                thread uint atlasHeight_21;
                (*((&atlasWidth_21)) = (_S1926).get_width(0)),(*((&atlasHeight_21)) = (_S1926).get_height(0));
                int3 _S1927 = int3(int(0), int(0), int(0));
                float4 metadata_21 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1927)).xy), uint(((_S1927)).z))) * float4(255.0f) );
                if((metadata_21.x) == 255.0f)
                {
                    uint _S1928 = atlasWidth_21;
                    uint _S1929 = 2U % atlasWidth_21;
                    int _S1930 = int(_S1929);
                    uint _S1931 = 2U / _S1928;
                    int3 _S1932 = int3(_S1930, int(_S1931), int(0));
                    int _S1933 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1932)).xy), uint(((_S1932)).z))).x * 255.0f)));
                    uint _S1934 = atlasWidth_21;
                    uint _S1935 = 3U % atlasWidth_21;
                    int _S1936 = int(_S1935);
                    uint _S1937 = 3U / _S1934;
                    int3 _S1938 = int3(_S1936, int(_S1937), int(0));
                    int2 minimumTile_21 = int2(_S1933, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1938)).xy), uint(((_S1938)).z))).x * 255.0f))));
                    uint _S1939 = atlasWidth_21;
                    uint _S1940 = 4U % atlasWidth_21;
                    int _S1941 = int(_S1940);
                    uint _S1942 = 4U / _S1939;
                    int3 _S1943 = int3(_S1941, int(_S1942), int(0));
                    uint _S1944 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1943)).xy), uint(((_S1943)).z))).x * 255.0f));
                    uint _S1945 = atlasWidth_21;
                    uint _S1946 = 5U % atlasWidth_21;
                    int _S1947 = int(_S1946);
                    uint _S1948 = 5U / _S1945;
                    int3 _S1949 = int3(_S1947, int(_S1948), int(0));
                    uint _S1950 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1949)).xy), uint(((_S1949)).z))).x * 255.0f));
                    uint2 grid_21 = uint2(_S1944, _S1950);
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
                        int3 _S1951 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1951)).xy), uint(((_S1951)).z)));
                        break;
                    }
                    uint offset_21 = 10U + 2U * (uint(tile_42.y) * _S1944 + uint(tile_42.x));
                    uint _S1952 = atlasWidth_21;
                    uint _S1953 = offset_21 % atlasWidth_21;
                    int _S1954 = int(_S1953);
                    uint _S1955 = offset_21 / _S1952;
                    int3 _S1956 = int3(_S1954, int(_S1955), int(0));
                    uint _S1957 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1956)).xy), uint(((_S1956)).z))).x * 255.0f));
                    uint _S1958 = atlasWidth_21;
                    uint _S1959 = offset_21 + 1U;
                    uint _S1960 = _S1959 % atlasWidth_21;
                    int _S1961 = int(_S1960);
                    uint _S1962 = _S1959 / _S1958;
                    int3 _S1963 = int3(_S1961, int(_S1962), int(0));
                    uint slot_42 = _S1957 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1963)).xy), uint(((_S1963)).z))).x * 255.0f)) << 8U);
                    if(slot_42 == 0U)
                    {
                        int3 _S1964 = int3(int(1), int(0), int(0));
                        _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1964)).xy), uint(((_S1964)).z)));
                        break;
                    }
                    uint slot_43 = slot_42 - 1U;
                    uint _S1965 = atlasWidth_21;
                    uint _S1966 = 6U % atlasWidth_21;
                    int _S1967 = int(_S1966);
                    uint _S1968 = 6U / _S1965;
                    int3 _S1969 = int3(_S1967, int(_S1968), int(0));
                    uint _S1970 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1969)).xy), uint(((_S1969)).z))).x * 255.0f));
                    uint _S1971 = atlasWidth_21;
                    uint _S1972 = 7U % atlasWidth_21;
                    int _S1973 = int(_S1972);
                    uint _S1974 = 7U / _S1971;
                    int3 _S1975 = int3(_S1973, int(_S1974), int(0));
                    uint columns_21 = _S1970 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1975)).xy), uint(((_S1975)).z))).x * 255.0f)) << 8U);
                    uint _S1976 = atlasWidth_21;
                    uint _S1977 = 8U % atlasWidth_21;
                    int _S1978 = int(_S1977);
                    uint _S1979 = 8U / _S1976;
                    int3 _S1980 = int3(_S1978, int(_S1979), int(0));
                    uint _S1981 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1980)).xy), uint(((_S1980)).z))).x * 255.0f));
                    uint _S1982 = atlasWidth_21;
                    uint _S1983 = 9U % atlasWidth_21;
                    int _S1984 = int(_S1983);
                    uint _S1985 = 9U / _S1982;
                    int3 _S1986 = int3(_S1984, int(_S1985), int(0));
                    uint rows_21 = ((_S1981 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1986)).xy), uint(((_S1986)).z))).x * 255.0f)) << 8U)) + columns_21 - 1U) / columns_21;
                    uint metadataRows_21 = (10U + 2U * _S1944 * _S1950 + atlasWidth_21 - 1U) / atlasWidth_21;
                    uint _S1987 = atlasWidth_21 / columns_21;
                    float _S1988 = float(_S1987);
                    uint _S1989 = (atlasHeight_21 - metadataRows_21) / rows_21;
                    float2 cellSize_42 = float2(_S1988, float(_S1989));
                    uint _S1990 = slot_43 % columns_21;
                    float _S1991 = float(_S1990);
                    uint _S1992 = slot_43 / columns_21;
                    _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1991, float(_S1992)) * cellSize_42 + float2(1.5f, float(metadataRows_21) + 1.5f) + fract(detailSource_0) * max(cellSize_42 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                    break;
                }
                int2 _S1993 = int2(metadata_21.zw);
                int2 tile_43 = int2(floor(detailSource_0)) - int2(metadata_21.xy);
                if(any(tile_43 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_43 >= _S1993);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1994 = int3(int(min(1U, atlasWidth_21 - 1U)), int(0), int(0));
                    _S591 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1994)).xy), uint(((_S1994)).z)));
                    break;
                }
                uint _S1995 = atlasWidth_21 / uint(_S1993.x);
                float _S1996 = float(_S1995);
                uint _S1997 = (atlasHeight_21 - 1U) / uint(_S1993.y);
                float2 cellSize_43 = float2(_S1996, float(_S1997));
                _S591 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_43) * cellSize_43 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_43 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                break;
            }
            uint operation_9 = uint(round(_S1921.y));
            if(operation_9 == 1U)
            {
                _S192 = _S192 * _S591;
                break;
            }
            if(operation_9 == 2U)
            {
                _S192 = _S192 + _S591;
                break;
            }
            if(operation_9 == 3U)
            {
                _S192 = _S192 - _S591;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1921.z;
                _S192 = _S192 * float4((1.0f - factor_9))  + _S591 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S192.x;
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
    float _S1998 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S1999 = max(0.00100000004749745f, roughness_0);
    float _S2000 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S2001 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S2001;
    bool omniPbrSpecular_0 = (_S174.w) >= 0.5f;
    float dielectricLevel_0 = _S160.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S2002 = float3(dielectricLevel_0) ;
        float3 _S2003 = float3(metallic_0) ;
        float3 _S2004 = mix(_S2002, diffuseColor_0, _S2003);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S2002, diffuseColor_0, _S2003);
        grazingIncidence_0 = _S2004;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S166.w) >= 0.5f)
        {
            float3 _S2005 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S2005;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S2006 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S2006);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S2006);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S2007 = _S2;
    float _S2008 = _S2.ambientLight_0.w;
    if(_S2008 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S2009 = _S2007.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S2009, _S2009)) > 0.0f;
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
    uint _S2010 = min(uint(_S2008), 128U);
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
    float4 _S2011 = float4(_S7->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S2011.w) ;
    uint domeLinkMask_0 = uint(max(_S176.x, 0.0f));
    FragmentFrameParameters_0 _S2012 = _S2;
    uint _S2013 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S2013) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S2014 = _S2007.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S2015 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S2015;
        for(;;)
        {
            if(lightCount_0 < _S2013)
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
        domeAmbient_1 = _S2014;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S2016 = !hasSceneLighting_0;
    if(_S2016)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S2010;
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
        bool _S2017;
        if(hasSceneLighting_0)
        {
            _S2017 = (((uint4(_S7->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S2017 = false;
        }
        if(_S2017)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S2018 = lightIndex_0 == 0U;
        bool _S2019;
        if(_S2018)
        {
            _S2019 = _S2016;
        }
        else
        {
            _S2019 = false;
        }
        if(_S2019)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S2020 = 224U + lightIndex_0 * 16U;
            float _S2021 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2020)>>2]);
            float _S2022 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2020 + 4U)>>2]);
            float _S2023 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2020 + 8U)>>2]);
            float _S2024 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2020 + 12U)>>2]);
            lightType_0 = _S2024;
        }
        bool _S2025;
        if(_S2018)
        {
            _S2025 = _S2016;
        }
        else
        {
            _S2025 = false;
        }
        if(_S2025)
        {
            lightDirection_0 = normalize((float4(_S7->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S2026 = 2272U + lightIndex_0 * 16U;
            float _S2027 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2026)>>2]);
            float _S2028 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2026 + 4U)>>2]);
            float _S2029 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2026 + 8U)>>2]);
            float _S2030 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2026 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S2027, _S2028, _S2029));
        }
        bool _S2031;
        if(_S2018)
        {
            _S2031 = _S2016;
        }
        else
        {
            _S2031 = false;
        }
        if(_S2031)
        {
            roughness_0 = (float4(_S7->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S2032 = 4320U + lightIndex_0 * 16U;
            float _S2033 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2032)>>2]);
            float _S2034 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2032 + 4U)>>2]);
            float _S2035 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2032 + 8U)>>2]);
            float _S2036 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2032 + 12U)>>2]);
            roughness_0 = _S2036;
        }
        bool _S2037;
        if(_S2018)
        {
            _S2037 = _S2016;
        }
        else
        {
            _S2037 = false;
        }
        if(_S2037)
        {
            diffuseColor_0 = _S2011.xyz;
        }
        else
        {
            uint _S2038 = 4320U + lightIndex_0 * 16U;
            float _S2039 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2038)>>2]);
            float _S2040 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2038 + 4U)>>2]);
            float _S2041 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2038 + 8U)>>2]);
            float _S2042 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2038 + 12U)>>2]);
            diffuseColor_0 = float3(_S2039, _S2040, _S2041);
        }
        bool _S2043;
        if(_S2018)
        {
            _S2043 = _S2016;
        }
        else
        {
            _S2043 = false;
        }
        if(_S2043)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S2044 = 6368U + lightIndex_0 * 16U;
            float _S2045 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2044)>>2]);
            float _S2046 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2044 + 4U)>>2]);
            float _S2047 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2044 + 8U)>>2]);
            float _S2048 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2044 + 12U)>>2]);
            metallic_0 = _S2045;
        }
        bool _S2049;
        if(_S2018)
        {
            _S2049 = _S2016;
        }
        else
        {
            _S2049 = false;
        }
        if(_S2049)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S2050 = 6368U + lightIndex_0 * 16U;
            float _S2051 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2050)>>2]);
            float _S2052 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2050 + 4U)>>2]);
            float _S2053 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2050 + 8U)>>2]);
            float _S2054 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2050 + 12U)>>2]);
            clearcoatRoughness_0 = _S2052;
        }
        bool _S2055;
        if(_S2018)
        {
            _S2055 = _S2016;
        }
        else
        {
            _S2055 = false;
        }
        if(_S2055)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S2056 = 8416U + lightIndex_0 * 16U;
            float _S2057 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2056)>>2]);
            float _S2058 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2056 + 4U)>>2]);
            float _S2059 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2056 + 8U)>>2]);
            float _S2060 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2056 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S2057, _S2058, _S2059));
        }
        bool _S2061;
        if(_S2018)
        {
            _S2061 = _S2016;
        }
        else
        {
            _S2061 = false;
        }
        if(_S2061)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S2062 = 10464U + lightIndex_0 * 16U;
            float _S2063 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2062)>>2]);
            float _S2064 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2062 + 4U)>>2]);
            float _S2065 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2062 + 8U)>>2]);
            float _S2066 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2062 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S2063, _S2064, _S2065));
        }
        bool _S2067;
        if(_S2018)
        {
            _S2067 = _S2016;
        }
        else
        {
            _S2067 = false;
        }
        float shapeX_0;
        if(_S2067)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S2068 = 8416U + lightIndex_0 * 16U;
            float _S2069 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068)>>2]);
            float _S2070 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 4U)>>2]);
            float _S2071 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 8U)>>2]);
            float _S2072 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 12U)>>2]);
            shapeX_0 = _S2072;
        }
        bool _S2073;
        if(_S2018)
        {
            _S2073 = _S2016;
        }
        else
        {
            _S2073 = false;
        }
        float shapeY_0;
        if(_S2073)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S2074 = 10464U + lightIndex_0 * 16U;
            float _S2075 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2074)>>2]);
            float _S2076 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2074 + 4U)>>2]);
            float _S2077 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2074 + 8U)>>2]);
            float _S2078 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2074 + 12U)>>2]);
            shapeY_0 = _S2078;
        }
        bool _S2079;
        if(_S2018)
        {
            _S2079 = _S2016;
        }
        else
        {
            _S2079 = false;
        }
        float lightRadius_0;
        if(_S2079)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S2080 = 2272U + lightIndex_0 * 16U;
            float _S2081 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2080)>>2]);
            float _S2082 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2080 + 4U)>>2]);
            float _S2083 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2080 + 8U)>>2]);
            float _S2084 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2080 + 12U)>>2]);
            lightRadius_0 = _S2084;
        }
        bool _S2085;
        if(_S2018)
        {
            _S2085 = _S2016;
        }
        else
        {
            _S2085 = false;
        }
        if(_S2085)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S2086;
        if(_S2018)
        {
            _S2086 = _S2016;
        }
        else
        {
            _S2086 = false;
        }
        if(_S2086)
        {
            _S192 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S2087 = 15296U + lightIndex_0 * 16U;
            float _S2088 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2087)>>2]);
            float _S2089 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2087 + 4U)>>2]);
            float _S2090 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2087 + 8U)>>2]);
            float _S2091 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2087 + 12U)>>2]);
            _S192 = float4(_S2088, _S2089, _S2090, _S2091);
        }
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S2092 = 12960U + lightIndex_0 * 16U;
            float _S2093 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2092)>>2]);
            float _S2094 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2092 + 4U)>>2]);
            float _S2095 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2092 + 8U)>>2]);
            float _S2096 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2092 + 12U)>>2]);
            int shadowSlot_0 = int(_S2093);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S2097 = _S2;
                    FragmentFrameParameters_0 _S2098 = _S2;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - specularColor_0;
                    }
                    else
                    {
                        color_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S2097.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S2099 = lightClip_0.w;
                    if(_S2099 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S2099) ;
                    bool _S2100;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S2100 = true;
                    }
                    else
                    {
                        _S2100 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S2101;
                    if(_S2100)
                    {
                        _S2101 = true;
                    }
                    else
                    {
                        _S2101 = (ndc_0.z) < 0.0f;
                    }
                    bool _S2102;
                    if(_S2101)
                    {
                        _S2102 = true;
                    }
                    else
                    {
                        _S2102 = (ndc_0.z) > 1.0f;
                    }
                    if(_S2102)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S2103 = _S2098.shadowTile_0[shadowSlot_0].xy;
                    float2 _S2104 = _S2098.shadowTile_0[shadowSlot_0].zw;
                    float2 _S2105 = _S2103 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S2104;
                    float texel_0 = _S2097.shadowControls_0[shadowSlot_0].w;
                    float _S2106 = max(_S2097.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S2107 = ndc_0.z - _S2097.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S2108 = float2((texel_0 * 0.5f)) ;
                    float2 _S2109 = _S2103 + _S2108;
                    float2 _S2110 = _S2103 + _S2104 - _S2108;
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
                            if(_S2107 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S2105 + float2(float(x_0), float(y_0)) * float2((_S2106 * texel_0)) , _S2109, _S2110)), level((0.0f))).x))
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
        float3 _S2111 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S2111;
        sampleOffsets_0[int(1)] = _S2111;
        sampleOffsets_0[int(2)] = _S2111;
        sampleOffsets_0[int(3)] = _S2111;
        sampleOffsets_0[int(4)] = _S2111;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S2112 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S2112 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S2112 - halfHeight_0;
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
                uint _S2113 = 224U + lightIndex_0 * 16U;
                float _S2114 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2113)>>2]);
                float _S2115 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2113 + 4U)>>2]);
                float _S2116 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2113 + 8U)>>2]);
                float _S2117 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2113 + 12U)>>2]);
                float3 toLight_0 = float3(_S2114, _S2115, _S2116) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S2118 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S2118)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S2118;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S192.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S192.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S2119 = _S192.y;
                        if(_S2119 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S2119);
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
            float _S2120 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_0;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_0 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S2120)) ;
            }
            else
            {
                diffuseFresnel_0 = mix(normalIncidence_0, grazingIncidence_0, float3(_S2120) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_0);
            float3 _S2121 = float3(_S2120) ;
            float _S2122 = max(_S1999, 0.00100000004749745f);
            float alpha_0 = _S2122 * _S2122;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S2123 = 1.0f - k_0;
            float3 _S2124 = float3(max(4.0f * normalDotLight_0 * _S1998, 1.00000000317107685e-30f)) ;
            float3 _S2125 = mix(normalIncidence_0, grazingIncidence_0, _S2121) * float3((_S1998 / (_S1998 * _S2123 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S2123 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S2124;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S2126 = max(_S2000, 0.00100000004749745f);
                float alpha_1 = _S2126 * _S2126;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S2127 = 1.0f - k_1;
                directSpecular_0 = _S2125 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S2121) * float3((_S1998 / (_S1998 * _S2127 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S2127 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S2124);
            }
            else
            {
                directSpecular_0 = _S2125;
            }
            float3 _S2128 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S2128 * _S2001) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S2128);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S2129 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S2130 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S2131 = saturate(_S1999);
        float2 _S2132 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2130, _S2131)), level((0.0f)))).xy;
        float _S2133 = _S2132.x;
        float _S2134 = _S2132.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S2133)  + grazingIncidence_0 * float3(_S2134) ;
        if(omniPbrSpecular_0)
        {
            lightDirection_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S2133 + _S2134)) ;
        }
        else
        {
            lightDirection_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - lightDirection_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S2135 = max(_S2012.domeControls_0.y, 1.0f);
        float3 _S2136 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S2135 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S2012.domeControls_0.z;
            float _S2137 = _S2012.domeControls_0.w;
            for(;;)
            {
                bool _S2138 = _S2135 <= 1.0f;
                _S5 = _S2138;
                if(_S2138)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S2137, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S2135)), level((0.0f)))).xyz;
                break;
            }
            float _S2139 = _S2129.environmentControls_0.y;
            float _S2140 = _S2129.environmentControls_0.z;
            for(;;)
            {
                if(_S5)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2141 = max(_S2139, 1.0f);
                    float _S2142 = _S2141 - 1.0f;
                    float slice_0 = _S2131 * max(_S2142, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S2140, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S2141)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S2142) + v_0) / _S2141)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S2143 = max(_S2139, 1.0f);
                float total_0 = _S2143 * _S2135;
                float _S2144 = _S2143 - 1.0f;
                float slice_1 = _S2131 * max(_S2144, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S2140, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S2143;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S2144) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_3 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2145 = max(_S2139, 1.0f);
                        float _S2146 = _S2145 - 1.0f;
                        float slice_2 = saturate(_S2000) * max(_S2146, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S2140, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S2145)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S2146) + v_2) / _S2145)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2147 = max(_S2139, 1.0f);
                    float total_1 = _S2147 * _S2135;
                    float _S2148 = _S2147 - 1.0f;
                    float slice_3 = saturate(_S2000) * max(_S2148, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S2140, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S2147;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S2148) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                color_0 = specularColor_0;
            }
            else
            {
                color_0 = _S2136;
            }
            lightTangent_0 = diffuseColor_0;
            lightBitangent_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightTangent_0 = _S2136;
            lightBitangent_0 = _S2136;
            color_0 = _S2136;
            for(;;)
            {
                if(sampleIndex_0 < _S2013)
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
                float _S2149 = _S2012.domeControls_0.w;
                for(;;)
                {
                    bool _S2150 = _S2135 <= 1.0f;
                    _S6 = _S2150;
                    if(_S2150)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S2149, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S2135)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightTangent_0 + diffuseColor_0;
                float _S2151 = _S2129.environmentControls_0.y;
                float _S2152 = _S2129.environmentControls_0.z;
                for(;;)
                {
                    if(_S6)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2153 = max(_S2151, 1.0f);
                        float _S2154 = _S2153 - 1.0f;
                        float slice_4 = _S2131 * max(_S2154, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S2152, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S2153)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S2154) + v_4) / _S2153)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2155 = max(_S2151, 1.0f);
                    float total_2 = _S2155 * _S2135;
                    float _S2156 = _S2155 - 1.0f;
                    float slice_5 = _S2131 * max(_S2156, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S2152, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S2155;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S2156) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightBitangent_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S6)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_7 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S2157 = max(_S2151, 1.0f);
                            float _S2158 = _S2157 - 1.0f;
                            float slice_6 = saturate(_S2000) * max(_S2158, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S2152, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S2157)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S2158) + v_6) / _S2157)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2159 = max(_S2151, 1.0f);
                        float total_3 = _S2159 * _S2135;
                        float _S2160 = _S2159 - 1.0f;
                        float slice_7 = saturate(_S2000) * max(_S2160, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S2152, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S2159;
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S2160) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S2161 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2130, saturate(_S2000))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * color_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S2161.x)  + float3(_S2161.y) );
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
    pixelOutput_0 _S2162 = { float4(color_3, opacity_0) };
    return _S2162;
}
