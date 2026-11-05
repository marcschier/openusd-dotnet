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
    matrix<float,int(4),int(4)>  clipToEye_0;
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
    texture2d<float, access::sample> shadowTransmission0_0;
    texture2d<float, access::sample> shadowTransmission1_0;
    texture2d<float, access::sample> shadowTransmission2_0;
    texture2d<float, access::sample> shadowTransmission3_0;
    texture2d<float, access::sample> environmentBrdf_0;
    sampler environmentBrdfSampler_0;
    texture2d<float, access::sample> environmentIrradiance_0;
    sampler environmentSampler_0;
    texture2d<float, access::sample> environmentSpecular_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_material_normal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> diffuseValuesTexture_1 [[texture(22)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> normalValuesTexture_1 [[texture(20)]], texture2d<float, access::sample> detailNormalTexture_1 [[texture(9)]], sampler detailNormalSampler_1 [[sampler(4)]], texture2d<float, access::sample> detailNormalValuesTexture_1 [[texture(21)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> emissiveValuesTexture_1 [[texture(23)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> shadowTransmission0_1 [[texture(24)]], texture2d<float, access::sample> shadowTransmission1_1 [[texture(25)]], texture2d<float, access::sample> shadowTransmission2_1 [[texture(26)]], texture2d<float, access::sample> shadowTransmission3_1 [[texture(27)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_0;
    FragmentFrameParameters_0 _S2;
    uint4 _S3;
    float _S4;
    uint sampleIndex_0;
    float3 lightDirectionAxis_0;
    float3 lightTangentAxis_0;
    float3 lightBitangentAxis_0;
    float3 lightEyeBase_0;
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
    (&kernelContext_0)->shadowTransmission0_0 = shadowTransmission0_1;
    (&kernelContext_0)->shadowTransmission1_0 = shadowTransmission1_1;
    (&kernelContext_0)->shadowTransmission2_0 = shadowTransmission2_1;
    (&kernelContext_0)->shadowTransmission3_0 = shadowTransmission3_1;
    (&kernelContext_0)->environmentBrdf_0 = environmentBrdf_1;
    (&kernelContext_0)->environmentBrdfSampler_0 = environmentBrdfSampler_1;
    (&kernelContext_0)->environmentIrradiance_0 = environmentIrradiance_1;
    (&kernelContext_0)->environmentSampler_0 = environmentSampler_1;
    (&kernelContext_0)->environmentSpecular_0 = environmentSpecular_1;
    SurfaceParameters_natural_0 device* _S7 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        float _S8 = as_type<float>((&kernelContext_0)->frameParameters_0[(0U)>>2]);
        float _S9 = as_type<float>((&kernelContext_0)->frameParameters_0[(4U)>>2]);
        float _S10 = as_type<float>((&kernelContext_0)->frameParameters_0[(8U)>>2]);
        float _S11 = as_type<float>((&kernelContext_0)->frameParameters_0[(12U)>>2]);
        float4 _S12 = float4(_S8, _S9, _S10, _S11);
        float _S13 = as_type<float>((&kernelContext_0)->frameParameters_0[(16U)>>2]);
        float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(20U)>>2]);
        float _S15 = as_type<float>((&kernelContext_0)->frameParameters_0[(24U)>>2]);
        float _S16 = as_type<float>((&kernelContext_0)->frameParameters_0[(28U)>>2]);
        float4 _S17 = float4(_S13, _S14, _S15, _S16);
        float _S18 = as_type<float>((&kernelContext_0)->frameParameters_0[(32U)>>2]);
        float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(36U)>>2]);
        float _S20 = as_type<float>((&kernelContext_0)->frameParameters_0[(40U)>>2]);
        float _S21 = as_type<float>((&kernelContext_0)->frameParameters_0[(44U)>>2]);
        float4 _S22 = float4(_S18, _S19, _S20, _S21);
        float _S23 = as_type<float>((&kernelContext_0)->frameParameters_0[(48U)>>2]);
        float _S24 = as_type<float>((&kernelContext_0)->frameParameters_0[(52U)>>2]);
        float _S25 = as_type<float>((&kernelContext_0)->frameParameters_0[(56U)>>2]);
        float _S26 = as_type<float>((&kernelContext_0)->frameParameters_0[(60U)>>2]);
        (&frame_0)->clipToEye_0 = matrix<float,int(4),int(4)> (_S12, _S17, _S22, float4(_S23, _S24, _S25, _S26));
        uint _S27 = as_type<uint>((&kernelContext_0)->frameParameters_0[(64U)>>2]);
        uint _S28 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
        uint _S29 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
        uint _S30 = as_type<uint>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S27, _S28, _S29, _S30);
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
            uint _S31 = 80U + index_0 * 16U;
            float _S32 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S31)>>2]);
            float _S33 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S31 + 4U)>>2]);
            float _S34 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S31 + 8U)>>2]);
            float _S35 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S31 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_0] = float4(_S32, _S33, _S34, _S35);
            index_0 = index_0 + 1U;
        }
        float _S36 = as_type<float>((&kernelContext_0)->frameParameters_0[(208U)>>2]);
        float _S37 = as_type<float>((&kernelContext_0)->frameParameters_0[(212U)>>2]);
        float _S38 = as_type<float>((&kernelContext_0)->frameParameters_0[(216U)>>2]);
        float _S39 = as_type<float>((&kernelContext_0)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S36, _S37, _S38, _S39);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 14880U;
        float _S40 = as_type<float>((&kernelContext_0)->frameParameters_0[(12512U)>>2]);
        float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(12516U)>>2]);
        float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(12520U)>>2]);
        float _S43 = as_type<float>((&kernelContext_0)->frameParameters_0[(12524U)>>2]);
        float4 _S44 = float4(_S40, _S41, _S42, _S43);
        float _S45 = as_type<float>((&kernelContext_0)->frameParameters_0[(12528U)>>2]);
        float _S46 = as_type<float>((&kernelContext_0)->frameParameters_0[(12532U)>>2]);
        float _S47 = as_type<float>((&kernelContext_0)->frameParameters_0[(12536U)>>2]);
        float _S48 = as_type<float>((&kernelContext_0)->frameParameters_0[(12540U)>>2]);
        float4 _S49 = float4(_S45, _S46, _S47, _S48);
        float _S50 = as_type<float>((&kernelContext_0)->frameParameters_0[(12544U)>>2]);
        float _S51 = as_type<float>((&kernelContext_0)->frameParameters_0[(12548U)>>2]);
        float _S52 = as_type<float>((&kernelContext_0)->frameParameters_0[(12552U)>>2]);
        float _S53 = as_type<float>((&kernelContext_0)->frameParameters_0[(12556U)>>2]);
        float4 _S54 = float4(_S50, _S51, _S52, _S53);
        float _S55 = as_type<float>((&kernelContext_0)->frameParameters_0[(12560U)>>2]);
        float _S56 = as_type<float>((&kernelContext_0)->frameParameters_0[(12564U)>>2]);
        float _S57 = as_type<float>((&kernelContext_0)->frameParameters_0[(12568U)>>2]);
        float _S58 = as_type<float>((&kernelContext_0)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S44, _S49, _S54, float4(_S55, _S56, _S57, _S58));
        float _S59 = as_type<float>((&kernelContext_0)->frameParameters_0[(16928U)>>2]);
        float _S60 = as_type<float>((&kernelContext_0)->frameParameters_0[(16932U)>>2]);
        float _S61 = as_type<float>((&kernelContext_0)->frameParameters_0[(16936U)>>2]);
        float _S62 = as_type<float>((&kernelContext_0)->frameParameters_0[(16940U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S59, _S60, _S61, _S62);
        float _S63 = as_type<float>((&kernelContext_0)->frameParameters_0[(16944U)>>2]);
        float _S64 = as_type<float>((&kernelContext_0)->frameParameters_0[(16948U)>>2]);
        float _S65 = as_type<float>((&kernelContext_0)->frameParameters_0[(16952U)>>2]);
        float _S66 = as_type<float>((&kernelContext_0)->frameParameters_0[(16956U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S63, _S64, _S65, _S66);
        float _S67 = as_type<float>((&kernelContext_0)->frameParameters_0[(16960U)>>2]);
        float _S68 = as_type<float>((&kernelContext_0)->frameParameters_0[(16964U)>>2]);
        float _S69 = as_type<float>((&kernelContext_0)->frameParameters_0[(16968U)>>2]);
        float _S70 = as_type<float>((&kernelContext_0)->frameParameters_0[(16972U)>>2]);
        float4 _S71 = float4(_S67, _S68, _S69, _S70);
        float _S72 = as_type<float>((&kernelContext_0)->frameParameters_0[(16976U)>>2]);
        float _S73 = as_type<float>((&kernelContext_0)->frameParameters_0[(16980U)>>2]);
        float _S74 = as_type<float>((&kernelContext_0)->frameParameters_0[(16984U)>>2]);
        float _S75 = as_type<float>((&kernelContext_0)->frameParameters_0[(16988U)>>2]);
        float4 _S76 = float4(_S72, _S73, _S74, _S75);
        float _S77 = as_type<float>((&kernelContext_0)->frameParameters_0[(16992U)>>2]);
        float _S78 = as_type<float>((&kernelContext_0)->frameParameters_0[(16996U)>>2]);
        float _S79 = as_type<float>((&kernelContext_0)->frameParameters_0[(17000U)>>2]);
        float _S80 = as_type<float>((&kernelContext_0)->frameParameters_0[(17004U)>>2]);
        float4 _S81 = float4(_S77, _S78, _S79, _S80);
        float _S82 = as_type<float>((&kernelContext_0)->frameParameters_0[(17008U)>>2]);
        float _S83 = as_type<float>((&kernelContext_0)->frameParameters_0[(17012U)>>2]);
        float _S84 = as_type<float>((&kernelContext_0)->frameParameters_0[(17016U)>>2]);
        float _S85 = as_type<float>((&kernelContext_0)->frameParameters_0[(17020U)>>2]);
        float4 _S86 = float4(_S82, _S83, _S84, _S85);
        float _S87 = as_type<float>((&kernelContext_0)->frameParameters_0[(17024U)>>2]);
        float _S88 = as_type<float>((&kernelContext_0)->frameParameters_0[(17028U)>>2]);
        float _S89 = as_type<float>((&kernelContext_0)->frameParameters_0[(17032U)>>2]);
        float _S90 = as_type<float>((&kernelContext_0)->frameParameters_0[(17036U)>>2]);
        float4 _S91 = float4(_S87, _S88, _S89, _S90);
        float _S92 = as_type<float>((&kernelContext_0)->frameParameters_0[(17040U)>>2]);
        float _S93 = as_type<float>((&kernelContext_0)->frameParameters_0[(17044U)>>2]);
        float _S94 = as_type<float>((&kernelContext_0)->frameParameters_0[(17048U)>>2]);
        float _S95 = as_type<float>((&kernelContext_0)->frameParameters_0[(17052U)>>2]);
        float4 _S96 = float4(_S92, _S93, _S94, _S95);
        float _S97 = as_type<float>((&kernelContext_0)->frameParameters_0[(17056U)>>2]);
        float _S98 = as_type<float>((&kernelContext_0)->frameParameters_0[(17060U)>>2]);
        float _S99 = as_type<float>((&kernelContext_0)->frameParameters_0[(17064U)>>2]);
        float _S100 = as_type<float>((&kernelContext_0)->frameParameters_0[(17068U)>>2]);
        float4 _S101 = float4(_S97, _S98, _S99, _S100);
        float _S102 = as_type<float>((&kernelContext_0)->frameParameters_0[(17072U)>>2]);
        float _S103 = as_type<float>((&kernelContext_0)->frameParameters_0[(17076U)>>2]);
        float _S104 = as_type<float>((&kernelContext_0)->frameParameters_0[(17080U)>>2]);
        float _S105 = as_type<float>((&kernelContext_0)->frameParameters_0[(17084U)>>2]);
        float4 _S106 = float4(_S102, _S103, _S104, _S105);
        float _S107 = as_type<float>((&kernelContext_0)->frameParameters_0[(17088U)>>2]);
        float _S108 = as_type<float>((&kernelContext_0)->frameParameters_0[(17092U)>>2]);
        float _S109 = as_type<float>((&kernelContext_0)->frameParameters_0[(17096U)>>2]);
        float _S110 = as_type<float>((&kernelContext_0)->frameParameters_0[(17100U)>>2]);
        float4 _S111 = float4(_S107, _S108, _S109, _S110);
        float _S112 = as_type<float>((&kernelContext_0)->frameParameters_0[(17104U)>>2]);
        float _S113 = as_type<float>((&kernelContext_0)->frameParameters_0[(17108U)>>2]);
        float _S114 = as_type<float>((&kernelContext_0)->frameParameters_0[(17112U)>>2]);
        float _S115 = as_type<float>((&kernelContext_0)->frameParameters_0[(17116U)>>2]);
        float4 _S116 = float4(_S112, _S113, _S114, _S115);
        float _S117 = as_type<float>((&kernelContext_0)->frameParameters_0[(17120U)>>2]);
        float _S118 = as_type<float>((&kernelContext_0)->frameParameters_0[(17124U)>>2]);
        float _S119 = as_type<float>((&kernelContext_0)->frameParameters_0[(17128U)>>2]);
        float _S120 = as_type<float>((&kernelContext_0)->frameParameters_0[(17132U)>>2]);
        float4 _S121 = float4(_S117, _S118, _S119, _S120);
        float _S122 = as_type<float>((&kernelContext_0)->frameParameters_0[(17136U)>>2]);
        float _S123 = as_type<float>((&kernelContext_0)->frameParameters_0[(17140U)>>2]);
        float _S124 = as_type<float>((&kernelContext_0)->frameParameters_0[(17144U)>>2]);
        float _S125 = as_type<float>((&kernelContext_0)->frameParameters_0[(17148U)>>2]);
        float4 _S126 = float4(_S122, _S123, _S124, _S125);
        float _S127 = as_type<float>((&kernelContext_0)->frameParameters_0[(17152U)>>2]);
        float _S128 = as_type<float>((&kernelContext_0)->frameParameters_0[(17156U)>>2]);
        float _S129 = as_type<float>((&kernelContext_0)->frameParameters_0[(17160U)>>2]);
        float _S130 = as_type<float>((&kernelContext_0)->frameParameters_0[(17164U)>>2]);
        float4 _S131 = float4(_S127, _S128, _S129, _S130);
        float _S132 = as_type<float>((&kernelContext_0)->frameParameters_0[(17168U)>>2]);
        float _S133 = as_type<float>((&kernelContext_0)->frameParameters_0[(17172U)>>2]);
        float _S134 = as_type<float>((&kernelContext_0)->frameParameters_0[(17176U)>>2]);
        float _S135 = as_type<float>((&kernelContext_0)->frameParameters_0[(17180U)>>2]);
        float4 _S136 = float4(_S132, _S133, _S134, _S135);
        float _S137 = as_type<float>((&kernelContext_0)->frameParameters_0[(17184U)>>2]);
        float _S138 = as_type<float>((&kernelContext_0)->frameParameters_0[(17188U)>>2]);
        float _S139 = as_type<float>((&kernelContext_0)->frameParameters_0[(17192U)>>2]);
        float _S140 = as_type<float>((&kernelContext_0)->frameParameters_0[(17196U)>>2]);
        float4 _S141 = float4(_S137, _S138, _S139, _S140);
        float _S142 = as_type<float>((&kernelContext_0)->frameParameters_0[(17200U)>>2]);
        float _S143 = as_type<float>((&kernelContext_0)->frameParameters_0[(17204U)>>2]);
        float _S144 = as_type<float>((&kernelContext_0)->frameParameters_0[(17208U)>>2]);
        float _S145 = as_type<float>((&kernelContext_0)->frameParameters_0[(17212U)>>2]);
        float4 _S146 = float4(_S142, _S143, _S144, _S145);
        (&frame_0)->domeAmbient_0[int(0)] = _S71;
        (&frame_0)->domeAmbient_0[int(1)] = _S76;
        (&frame_0)->domeAmbient_0[int(2)] = _S81;
        (&frame_0)->domeAmbient_0[int(3)] = _S86;
        (&frame_0)->domeAmbient_0[int(4)] = _S91;
        (&frame_0)->domeAmbient_0[int(5)] = _S96;
        (&frame_0)->domeAmbient_0[int(6)] = _S101;
        (&frame_0)->domeAmbient_0[int(7)] = _S106;
        (&frame_0)->domeEnvironment_0[int(0)] = _S111;
        (&frame_0)->domeEnvironment_0[int(1)] = _S116;
        (&frame_0)->domeEnvironment_0[int(2)] = _S121;
        (&frame_0)->domeEnvironment_0[int(3)] = _S126;
        (&frame_0)->domeEnvironment_0[int(4)] = _S131;
        (&frame_0)->domeEnvironment_0[int(5)] = _S136;
        (&frame_0)->domeEnvironment_0[int(6)] = _S141;
        (&frame_0)->domeEnvironment_0[int(7)] = _S146;
        _S2 = frame_0;
        break;
    }
    for(;;)
    {
        _S3 = _S2.clipPlaneCount_0;
        uint _S147 = min(_S2.clipPlaneCount_0.x, 8U);
        index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S147)
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
    float4 _S148 = float4(_S7->clearcoatShaded_0) ;
    float shadedMode_0 = _S148.z;
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
    float4 _S149 = float4(_S7->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S149.xyz;
    float _S150 = _S149.w;
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
        pixelOutput_0 _S151 = { float4(unlitColor_0, opacity_0) };
        return _S151;
    }
    float4 _S152 = float4(_S7->reserved_0) ;
    if((_S152.x) >= 0.5f)
    {
        pixelOutput_0 _S153 = { float4(diffuseColor_0, saturate(opacity_0) * (1.0f - exp(- max(0.0f, max(0.0f, _S152.y) * max(0.0f, _S152.z))))) };
        return _S153;
    }
    float4 _S154 = float4(_S7->metallicRoughnessThresholdWorkflow_0) ;
    float _S155 = saturate(_S154.x);
    float _S156 = clamp(_S154.y, 0.00999999977648258f, 1.0f);
    float4 _S157 = float4(_S7->specularIor_0) ;
    float3 _S158 = _S157.xyz;
    float _S159 = _S157.w;
    float _S160 = _S148.x;
    float _S161 = _S148.y;
    float4 _S162 = float4(_S7->textureControls_0) ;
    uint textureMask_0 = uint(round(_S162.x));
    uint udimMask_0 = uint(round(_S162.y));
    float4 _S163 = float4(_S7->uvTransformRow1_0) ;
    uint secondaryMask_0 = uint(round(_S163.w));
    float4 _S164 = float4(_S7->domeLinkControls_0) ;
    uint alternateMask_0 = uint(round(_S164.w));
    float4 _S165 = float4(_S7->uvTransformRow0_0) ;
    float2 _S166 = _S165.xy;
    float _S167 = _S165.z;
    float2 _S168 = _S163.xy;
    float _S169 = _S163.z;
    float2 _S170 = float2(dot(_S166, _S1.texCoord_0) + _S167, dot(_S168, _S1.texCoord_0) + _S169);
    float2 _S171 = float2(dot(_S166, _S1.texCoord1_0) + _S167, dot(_S168, _S1.texCoord1_0) + _S169);
    float4 _S172 = float4(_S7->alternateUvTransformRow0_0) ;
    float4 _S173 = float4(_S7->alternateUvTransformRow1_0) ;
    float2 _S174 = _S172.xy;
    float _S175 = _S172.z;
    float2 _S176 = _S173.xy;
    float _S177 = _S173.z;
    float2 _S178 = float2(dot(_S174, _S1.texCoord_0) + _S175, dot(_S176, _S1.texCoord_0) + _S177);
    float2 _S179 = float2(dot(_S174, _S1.texCoord1_0) + _S175, dot(_S176, _S1.texCoord1_0) + _S177);
    uint lightCount_0;
    float roughness_0;
    float metallic_0;
    float2 detailSource_0;
    float2 tangent_1;
    bool hasSceneLighting_0;
    float4 _S180;
    uint3 _S181;
    if((textureMask_0 & 2U) != 0U)
    {
        bool useSecondary_0 = (secondaryMask_0 & 2U) != 0U;
        if((alternateMask_0 & 2U) != 0U)
        {
            if(useSecondary_0)
            {
                detailSource_0 = _S179;
            }
            else
            {
                detailSource_0 = _S178;
            }
        }
        else
        {
            if(useSecondary_0)
            {
                detailSource_0 = _S171;
            }
            else
            {
                detailSource_0 = _S170;
            }
        }
        thread float4 diffuseSample_0;
        bool _S182 = (udimMask_0 & 2U) != 0U;
        uint maskU_0 = uint(round(_S164.y));
        uint maskV_0 = uint(round(_S164.z));
        bool _S183 = (maskU_0 & 2U) != 0U;
        if(_S183)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S184 = (maskV_0 & 2U) != 0U;
        if(_S184)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S185 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S182)
            {
                for(;;)
                {
                    if(_S185 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S186 = (&kernelContext_0)->baseColorTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S186).get_width(0)),(*((&height_0)) = (_S186).get_height(0));
                    float2 size_0 = float2(float(width_0), float(height_0));
                    float2 _S187 = float2(0.5f) ;
                    float2 remapped_0 = clamp((detailSource_0 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S187 / size_0, (size_0 - _S187) / size_0);
                    if((_S185 & 1U) != 0U)
                    {
                        roughness_0 = remapped_0.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S185 & 2U) != 0U)
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
                _S180 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S188 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S188).get_width(0)),(*((&atlasHeight_0)) = (_S188).get_height(0));
            int3 _S189 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S189)).xy), uint(((_S189)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S190 = atlasWidth_0;
                uint _S191 = 2U % atlasWidth_0;
                int _S192 = int(_S191);
                uint _S193 = 2U / _S190;
                int3 _S194 = int3(_S192, int(_S193), int(0));
                int _S195 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S194)).xy), uint(((_S194)).z))).x * 255.0f)));
                uint _S196 = atlasWidth_0;
                uint _S197 = 3U % atlasWidth_0;
                int _S198 = int(_S197);
                uint _S199 = 3U / _S196;
                int3 _S200 = int3(_S198, int(_S199), int(0));
                int2 minimumTile_0 = int2(_S195, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S200)).xy), uint(((_S200)).z))).x * 255.0f))));
                uint _S201 = atlasWidth_0;
                uint _S202 = 4U % atlasWidth_0;
                int _S203 = int(_S202);
                uint _S204 = 4U / _S201;
                int3 _S205 = int3(_S203, int(_S204), int(0));
                uint _S206 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S205)).xy), uint(((_S205)).z))).x * 255.0f));
                uint _S207 = atlasWidth_0;
                uint _S208 = 5U % atlasWidth_0;
                int _S209 = int(_S208);
                uint _S210 = 5U / _S207;
                int3 _S211 = int3(_S209, int(_S210), int(0));
                uint _S212 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S211)).xy), uint(((_S211)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S206, _S212);
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
                    int3 _S213 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S213)).xy), uint(((_S213)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S206 + uint(tile_0.x));
                uint _S214 = atlasWidth_0;
                uint _S215 = offset_0 % atlasWidth_0;
                int _S216 = int(_S215);
                uint _S217 = offset_0 / _S214;
                int3 _S218 = int3(_S216, int(_S217), int(0));
                uint _S219 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S218)).xy), uint(((_S218)).z))).x * 255.0f));
                uint _S220 = atlasWidth_0;
                uint _S221 = offset_0 + 1U;
                uint _S222 = _S221 % atlasWidth_0;
                int _S223 = int(_S222);
                uint _S224 = _S221 / _S220;
                int3 _S225 = int3(_S223, int(_S224), int(0));
                uint slot_0 = _S219 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S225)).xy), uint(((_S225)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S226 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S226)).xy), uint(((_S226)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S227 = atlasWidth_0;
                uint _S228 = 6U % atlasWidth_0;
                int _S229 = int(_S228);
                uint _S230 = 6U / _S227;
                int3 _S231 = int3(_S229, int(_S230), int(0));
                uint _S232 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S231)).xy), uint(((_S231)).z))).x * 255.0f));
                uint _S233 = atlasWidth_0;
                uint _S234 = 7U % atlasWidth_0;
                int _S235 = int(_S234);
                uint _S236 = 7U / _S233;
                int3 _S237 = int3(_S235, int(_S236), int(0));
                uint columns_0 = _S232 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S237)).xy), uint(((_S237)).z))).x * 255.0f)) << 8U);
                uint _S238 = atlasWidth_0;
                uint _S239 = 8U % atlasWidth_0;
                int _S240 = int(_S239);
                uint _S241 = 8U / _S238;
                int3 _S242 = int3(_S240, int(_S241), int(0));
                uint _S243 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S242)).xy), uint(((_S242)).z))).x * 255.0f));
                uint _S244 = atlasWidth_0;
                uint _S245 = 9U % atlasWidth_0;
                int _S246 = int(_S245);
                uint _S247 = 9U / _S244;
                int3 _S248 = int3(_S246, int(_S247), int(0));
                uint rows_0 = ((_S243 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S248)).xy), uint(((_S248)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S206 * _S212 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S249 = atlasWidth_0 / columns_0;
                float _S250 = float(_S249);
                uint _S251 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S250, float(_S251));
                uint _S252 = slot_1 % columns_0;
                float _S253 = float(_S252);
                uint _S254 = slot_1 / columns_0;
                _S180 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S253, float(_S254)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(detailSource_0) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S255 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(detailSource_0)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S255);
            }
            if(hasSceneLighting_0)
            {
                int3 _S256 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S180 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S256)).xy), uint(((_S256)).z)));
                break;
            }
            uint _S257 = atlasWidth_0 / uint(_S255.x);
            float _S258 = float(_S257);
            uint _S259 = (atlasHeight_0 - 1U) / uint(_S255.y);
            float2 cellSize_1 = float2(_S258, float(_S259));
            _S180 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S180;
        float4 _S260 = float4(_S7->compositeControls_0) ;
        float _S261 = _S260.x;
        if(_S261 == 0.0f)
        {
            hasSceneLighting_0 = (_S260.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S262 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S262).get_width(0)),(*((&sourceHeight_0)) = (_S262).get_height(0));
            texture2d<float, access::sample> _S263 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S263).get_width(0)),(*((&valueHeight_0)) = (_S263).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S264 = diffuseSample_0;
                for(;;)
                {
                    float _S265 = _S264.w;
                    if(_S265 > 1.0f)
                    {
                        diffuseColor_0 = _S264.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S266 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S266).get_width(0)),(*((&height_1)) = (_S266).get_height(0));
                    uint3 _S267 = uint3(round(_S264.xyz * float3(255.0f) ));
                    uint index_1 = ((_S267.x) | ((_S267.y) << 8U)) | ((_S267.z) << 16U);
                    if(_S265 == 0.0f)
                    {
                        _S181 = _S267;
                    }
                    else
                    {
                        _S181 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S181 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S268 = address_0.x;
                    uint _S269 = _S268 % width_1;
                    int _S270 = int(_S269);
                    uint _S271 = _S268 / width_1;
                    int3 _S272 = int3(_S270, int(_S271), int(0));
                    float _S273 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S272)).xy), uint(((_S272)).z))).x;
                    uint _S274 = address_0.y;
                    uint _S275 = _S274 % width_1;
                    int _S276 = int(_S275);
                    uint _S277 = _S274 / width_1;
                    int3 _S278 = int3(_S276, int(_S277), int(0));
                    float _S279 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S278)).xy), uint(((_S278)).z))).x;
                    uint _S280 = address_0.z;
                    uint _S281 = _S280 % width_1;
                    int _S282 = int(_S281);
                    uint _S283 = _S280 / width_1;
                    int3 _S284 = int3(_S282, int(_S283), int(0));
                    diffuseColor_0 = float3(_S273, _S279, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S284)).xy), uint(((_S284)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                if(_S183)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S184)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S285 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S285 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S286 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S286).get_width(0)),(*((&height_2)) = (_S286).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S287 = float2(0.5f) ;
                    float2 remapped_1 = clamp((detailSource_0 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S287 / size_1, (size_1 - _S287) / size_1);
                    if((_S285 & 1U) != 0U)
                    {
                        roughness_0 = remapped_1.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S285 & 2U) != 0U)
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
        float4 _S288 = diffuseSample_0;
        for(;;)
        {
            if(_S261 != 2.0f)
            {
                _S180 = _S288;
                break;
            }
            bool _S289 = (_S260.w) >= 0.5f;
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
            uint _S290 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S289)
                {
                    for(;;)
                    {
                        if(_S290 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S291 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_3;
                        thread uint height_3;
                        (*((&width_3)) = (_S291).get_width(0)),(*((&height_3)) = (_S291).get_height(0));
                        float2 size_2 = float2(float(width_3), float(height_3));
                        float2 _S292 = float2(0.5f) ;
                        float2 remapped_2 = clamp((detailSource_0 * (size_2 - float2(2.0f) ) + float2(1.0f) ) / size_2, _S292 / size_2, (size_2 - _S292) / size_2);
                        if((_S290 & 1U) != 0U)
                        {
                            roughness_0 = remapped_2.x;
                        }
                        else
                        {
                            roughness_0 = detailSource_0.x;
                        }
                        if((_S290 & 2U) != 0U)
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
                    _S180 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S293 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S293).get_width(0)),(*((&atlasHeight_1)) = (_S293).get_height(0));
                int3 _S294 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S294)).xy), uint(((_S294)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S295 = atlasWidth_1;
                    uint _S296 = 2U % atlasWidth_1;
                    int _S297 = int(_S296);
                    uint _S298 = 2U / _S295;
                    int3 _S299 = int3(_S297, int(_S298), int(0));
                    int _S300 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S299)).xy), uint(((_S299)).z))).x * 255.0f)));
                    uint _S301 = atlasWidth_1;
                    uint _S302 = 3U % atlasWidth_1;
                    int _S303 = int(_S302);
                    uint _S304 = 3U / _S301;
                    int3 _S305 = int3(_S303, int(_S304), int(0));
                    int2 minimumTile_1 = int2(_S300, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S305)).xy), uint(((_S305)).z))).x * 255.0f))));
                    uint _S306 = atlasWidth_1;
                    uint _S307 = 4U % atlasWidth_1;
                    int _S308 = int(_S307);
                    uint _S309 = 4U / _S306;
                    int3 _S310 = int3(_S308, int(_S309), int(0));
                    uint _S311 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S310)).xy), uint(((_S310)).z))).x * 255.0f));
                    uint _S312 = atlasWidth_1;
                    uint _S313 = 5U % atlasWidth_1;
                    int _S314 = int(_S313);
                    uint _S315 = 5U / _S312;
                    int3 _S316 = int3(_S314, int(_S315), int(0));
                    uint _S317 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S316)).xy), uint(((_S316)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S311, _S317);
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
                        int3 _S318 = int3(int(1), int(0), int(0));
                        _S180 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S318)).xy), uint(((_S318)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S311 + uint(tile_2.x));
                    uint _S319 = atlasWidth_1;
                    uint _S320 = offset_1 % atlasWidth_1;
                    int _S321 = int(_S320);
                    uint _S322 = offset_1 / _S319;
                    int3 _S323 = int3(_S321, int(_S322), int(0));
                    uint _S324 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S323)).xy), uint(((_S323)).z))).x * 255.0f));
                    uint _S325 = atlasWidth_1;
                    uint _S326 = offset_1 + 1U;
                    uint _S327 = _S326 % atlasWidth_1;
                    int _S328 = int(_S327);
                    uint _S329 = _S326 / _S325;
                    int3 _S330 = int3(_S328, int(_S329), int(0));
                    uint slot_2 = _S324 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S330)).xy), uint(((_S330)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S331 = int3(int(1), int(0), int(0));
                        _S180 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S331)).xy), uint(((_S331)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S332 = atlasWidth_1;
                    uint _S333 = 6U % atlasWidth_1;
                    int _S334 = int(_S333);
                    uint _S335 = 6U / _S332;
                    int3 _S336 = int3(_S334, int(_S335), int(0));
                    uint _S337 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S336)).xy), uint(((_S336)).z))).x * 255.0f));
                    uint _S338 = atlasWidth_1;
                    uint _S339 = 7U % atlasWidth_1;
                    int _S340 = int(_S339);
                    uint _S341 = 7U / _S338;
                    int3 _S342 = int3(_S340, int(_S341), int(0));
                    uint columns_1 = _S337 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S342)).xy), uint(((_S342)).z))).x * 255.0f)) << 8U);
                    uint _S343 = atlasWidth_1;
                    uint _S344 = 8U % atlasWidth_1;
                    int _S345 = int(_S344);
                    uint _S346 = 8U / _S343;
                    int3 _S347 = int3(_S345, int(_S346), int(0));
                    uint _S348 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S347)).xy), uint(((_S347)).z))).x * 255.0f));
                    uint _S349 = atlasWidth_1;
                    uint _S350 = 9U % atlasWidth_1;
                    int _S351 = int(_S350);
                    uint _S352 = 9U / _S349;
                    int3 _S353 = int3(_S351, int(_S352), int(0));
                    uint rows_1 = ((_S348 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S353)).xy), uint(((_S353)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S311 * _S317 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S354 = atlasWidth_1 / columns_1;
                    float _S355 = float(_S354);
                    uint _S356 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S355, float(_S356));
                    uint _S357 = slot_3 % columns_1;
                    float _S358 = float(_S357);
                    uint _S359 = slot_3 / columns_1;
                    _S180 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S358, float(_S359)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailSource_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S360 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(detailSource_0)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S360);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S361 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S180 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S361)).xy), uint(((_S361)).z)));
                    break;
                }
                uint _S362 = atlasWidth_1 / uint(_S360.x);
                float _S363 = float(_S362);
                uint _S364 = (atlasHeight_1 - 1U) / uint(_S360.y);
                float2 cellSize_3 = float2(_S363, float(_S364));
                _S180 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S260.y));
            if(operation_0 == 1U)
            {
                _S180 = _S288 * _S180;
                break;
            }
            if(operation_0 == 2U)
            {
                _S180 = _S288 + _S180;
                break;
            }
            if(operation_0 == 3U)
            {
                _S180 = _S288 - _S180;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S260.z;
                _S180 = _S288 * float4((1.0f - factor_0))  + _S180 * float4(factor_0) ;
                break;
            }
            _S180 = _S288;
            break;
        }
        diffuseColor_0 = _S180.xyz;
    }
    thread float4 baseNormalSample_0;
    bool useSecondary_1 = (secondaryMask_0 & 4U) != 0U;
    bool _S365 = (alternateMask_0 & 4U) != 0U;
    if(_S365)
    {
        if(useSecondary_1)
        {
            detailSource_0 = _S179;
        }
        else
        {
            detailSource_0 = _S178;
        }
    }
    else
    {
        if(useSecondary_1)
        {
            detailSource_0 = _S171;
        }
        else
        {
            detailSource_0 = _S170;
        }
    }
    bool _S366 = (udimMask_0 & 4U) != 0U;
    uint maskU_1 = uint(round(_S164.y));
    uint maskV_1 = uint(round(_S164.z));
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
    uint _S367 = index_0 | lightCount_0;
    for(;;)
    {
        if(!_S366)
        {
            for(;;)
            {
                if(_S367 == 0U)
                {
                    break;
                }
                texture2d<float, access::sample> _S368 = (&kernelContext_0)->normalTexture_0;
                thread uint width_4;
                thread uint height_4;
                (*((&width_4)) = (_S368).get_width(0)),(*((&height_4)) = (_S368).get_height(0));
                float2 size_3 = float2(float(width_4), float(height_4));
                float2 _S369 = float2(0.5f) ;
                float2 remapped_3 = clamp((detailSource_0 * (size_3 - float2(2.0f) ) + float2(1.0f) ) / size_3, _S369 / size_3, (size_3 - _S369) / size_3);
                if((_S367 & 1U) != 0U)
                {
                    roughness_0 = remapped_3.x;
                }
                else
                {
                    roughness_0 = detailSource_0.x;
                }
                if((_S367 & 2U) != 0U)
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
            _S180 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (detailSource_0)));
            break;
        }
        texture2d<float, access::sample> _S370 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S370).get_width(0)),(*((&atlasHeight_2)) = (_S370).get_height(0));
        int3 _S371 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S371)).xy), uint(((_S371)).z))) * float4(255.0f) );
        if((metadata_2.x) == 255.0f)
        {
            uint _S372 = atlasWidth_2;
            uint _S373 = 2U % atlasWidth_2;
            int _S374 = int(_S373);
            uint _S375 = 2U / _S372;
            int3 _S376 = int3(_S374, int(_S375), int(0));
            int _S377 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S376)).xy), uint(((_S376)).z))).x * 255.0f)));
            uint _S378 = atlasWidth_2;
            uint _S379 = 3U % atlasWidth_2;
            int _S380 = int(_S379);
            uint _S381 = 3U / _S378;
            int3 _S382 = int3(_S380, int(_S381), int(0));
            int2 minimumTile_2 = int2(_S377, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S382)).xy), uint(((_S382)).z))).x * 255.0f))));
            uint _S383 = atlasWidth_2;
            uint _S384 = 4U % atlasWidth_2;
            int _S385 = int(_S384);
            uint _S386 = 4U / _S383;
            int3 _S387 = int3(_S385, int(_S386), int(0));
            uint _S388 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S387)).xy), uint(((_S387)).z))).x * 255.0f));
            uint _S389 = atlasWidth_2;
            uint _S390 = 5U % atlasWidth_2;
            int _S391 = int(_S390);
            uint _S392 = 5U / _S389;
            int3 _S393 = int3(_S391, int(_S392), int(0));
            uint _S394 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S393)).xy), uint(((_S393)).z))).x * 255.0f));
            uint2 grid_2 = uint2(_S388, _S394);
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
                int3 _S395 = int3(int(1), int(0), int(0));
                _S180 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S395)).xy), uint(((_S395)).z)));
                break;
            }
            uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S388 + uint(tile_4.x));
            uint _S396 = atlasWidth_2;
            uint _S397 = offset_2 % atlasWidth_2;
            int _S398 = int(_S397);
            uint _S399 = offset_2 / _S396;
            int3 _S400 = int3(_S398, int(_S399), int(0));
            uint _S401 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S400)).xy), uint(((_S400)).z))).x * 255.0f));
            uint _S402 = atlasWidth_2;
            uint _S403 = offset_2 + 1U;
            uint _S404 = _S403 % atlasWidth_2;
            int _S405 = int(_S404);
            uint _S406 = _S403 / _S402;
            int3 _S407 = int3(_S405, int(_S406), int(0));
            uint slot_4 = _S401 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S407)).xy), uint(((_S407)).z))).x * 255.0f)) << 8U);
            if(slot_4 == 0U)
            {
                int3 _S408 = int3(int(1), int(0), int(0));
                _S180 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S408)).xy), uint(((_S408)).z)));
                break;
            }
            uint slot_5 = slot_4 - 1U;
            uint _S409 = atlasWidth_2;
            uint _S410 = 6U % atlasWidth_2;
            int _S411 = int(_S410);
            uint _S412 = 6U / _S409;
            int3 _S413 = int3(_S411, int(_S412), int(0));
            uint _S414 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S413)).xy), uint(((_S413)).z))).x * 255.0f));
            uint _S415 = atlasWidth_2;
            uint _S416 = 7U % atlasWidth_2;
            int _S417 = int(_S416);
            uint _S418 = 7U / _S415;
            int3 _S419 = int3(_S417, int(_S418), int(0));
            uint columns_2 = _S414 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S419)).xy), uint(((_S419)).z))).x * 255.0f)) << 8U);
            uint _S420 = atlasWidth_2;
            uint _S421 = 8U % atlasWidth_2;
            int _S422 = int(_S421);
            uint _S423 = 8U / _S420;
            int3 _S424 = int3(_S422, int(_S423), int(0));
            uint _S425 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S424)).xy), uint(((_S424)).z))).x * 255.0f));
            uint _S426 = atlasWidth_2;
            uint _S427 = 9U % atlasWidth_2;
            int _S428 = int(_S427);
            uint _S429 = 9U / _S426;
            int3 _S430 = int3(_S428, int(_S429), int(0));
            uint rows_2 = ((_S425 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S430)).xy), uint(((_S430)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
            uint metadataRows_2 = (10U + 2U * _S388 * _S394 + atlasWidth_2 - 1U) / atlasWidth_2;
            uint _S431 = atlasWidth_2 / columns_2;
            float _S432 = float(_S431);
            uint _S433 = (atlasHeight_2 - metadataRows_2) / rows_2;
            float2 cellSize_4 = float2(_S432, float(_S433));
            uint _S434 = slot_5 % columns_2;
            float _S435 = float(_S434);
            uint _S436 = slot_5 / columns_2;
            _S180 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S435, float(_S436)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(detailSource_0) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        int2 _S437 = int2(metadata_2.zw);
        int2 tile_5 = int2(floor(detailSource_0)) - int2(metadata_2.xy);
        if(any(tile_5 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_5 >= _S437);
        }
        if(hasSceneLighting_0)
        {
            int3 _S438 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S180 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S438)).xy), uint(((_S438)).z)));
            break;
        }
        uint _S439 = atlasWidth_2 / uint(_S437.x);
        float _S440 = float(_S439);
        uint _S441 = (atlasHeight_2 - 1U) / uint(_S437.y);
        float2 cellSize_5 = float2(_S440, float(_S441));
        _S180 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    baseNormalSample_0 = _S180;
    float4 _S442 = float4(_S7->detailUvTransformRow0_0) ;
    if((_S442.w) != 0.0f)
    {
        float4 _S443 = baseNormalSample_0;
        for(;;)
        {
            float _S444 = _S443.w;
            if(_S444 > 1.0f)
            {
                unlitColor_0 = _S443.xyz;
                break;
            }
            texture2d<float, access::sample> _S445 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_5;
            thread uint height_5;
            (*((&width_5)) = (_S445).get_width(0)),(*((&height_5)) = (_S445).get_height(0));
            uint3 _S446 = uint3(round(_S443.xyz * float3(255.0f) ));
            uint index_2 = ((_S446.x) | ((_S446.y) << 8U)) | ((_S446.z) << 16U);
            if(_S444 == 0.0f)
            {
                _S181 = _S446;
            }
            else
            {
                _S181 = uint3(index_2, index_2, index_2);
            }
            uint3 address_1 = _S181 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S447 = address_1.x;
            uint _S448 = _S447 % width_5;
            int _S449 = int(_S448);
            uint _S450 = _S447 / width_5;
            int3 _S451 = int3(_S449, int(_S450), int(0));
            float _S452 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S451)).xy), uint(((_S451)).z))).x;
            uint _S453 = address_1.y;
            uint _S454 = _S453 % width_5;
            int _S455 = int(_S454);
            uint _S456 = _S453 / width_5;
            int3 _S457 = int3(_S455, int(_S456), int(0));
            float _S458 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S457)).xy), uint(((_S457)).z))).x;
            uint _S459 = address_1.z;
            uint _S460 = _S459 % width_5;
            int _S461 = int(_S460);
            uint _S462 = _S459 / width_5;
            int3 _S463 = int3(_S461, int(_S462), int(0));
            unlitColor_0 = float3(_S452, _S458, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S463)).xy), uint(((_S463)).z))).x);
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
    if(_S365)
    {
        detailSource_0 = _S174;
    }
    else
    {
        detailSource_0 = _S166;
    }
    if(_S365)
    {
        tangent_1 = _S176;
    }
    else
    {
        tangent_1 = _S168;
    }
    thread float3 _S464 = unlitColor_0;
    float2 _S465 = float2(0.0f, 0.0f);
    if(any(tangent_1 != _S465))
    {
        float _S466 = tangent_1.x;
        float _S467 = tangent_1.y;
        float2 tangent_2 = normalize(float2(_S467, - _S466) / float2(max(abs(_S466), abs(_S467))) );
        float2 bitangent_0 = float2(- tangent_2.y, tangent_2.x);
        if((dot(detailSource_0, tangent_2)) < 0.0f)
        {
            detailSource_0 = - tangent_2;
        }
        else
        {
            detailSource_0 = tangent_2;
        }
        _S464.xy = detailSource_0 * float2(_S464.x)  + bitangent_0 * float2(_S464.y) ;
    }
    float3 _S468 = _S464;
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
        float2 _S469 = _S442.xy;
        float _S470 = dot(_S469, detailSource_0) + _S442.z;
        float4 _S471 = float4(_S7->detailUvTransformRow1_0) ;
        float2 _S472 = _S471.xy;
        float _S473 = dot(_S472, detailSource_0) + _S471.z;
        float2 detailUv_0 = float2(_S470, _S473);
        thread float4 detailSample_0;
        bool _S474 = (udimMask_0 & 4096U) != 0U;
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
        uint _S475 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S474)
            {
                for(;;)
                {
                    if(_S475 == 0U)
                    {
                        detailSource_0 = detailUv_0;
                        break;
                    }
                    texture2d<float, access::sample> _S476 = (&kernelContext_0)->detailNormalTexture_0;
                    thread uint width_6;
                    thread uint height_6;
                    (*((&width_6)) = (_S476).get_width(0)),(*((&height_6)) = (_S476).get_height(0));
                    float2 size_4 = float2(float(width_6), float(height_6));
                    float2 _S477 = float2(0.5f) ;
                    float2 remapped_4 = clamp((detailUv_0 * (size_4 - float2(2.0f) ) + float2(1.0f) ) / size_4, _S477 / size_4, (size_4 - _S477) / size_4);
                    if((_S475 & 1U) != 0U)
                    {
                        roughness_0 = remapped_4.x;
                    }
                    else
                    {
                        roughness_0 = _S470;
                    }
                    if((_S475 & 2U) != 0U)
                    {
                        metallic_0 = remapped_4.y;
                    }
                    else
                    {
                        metallic_0 = _S473;
                    }
                    detailSource_0 = float2(roughness_0, metallic_0);
                    break;
                }
                _S180 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S478 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S478).get_width(0)),(*((&atlasHeight_3)) = (_S478).get_height(0));
            int3 _S479 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S479)).xy), uint(((_S479)).z))) * float4(255.0f) );
            if((metadata_3.x) == 255.0f)
            {
                uint _S480 = atlasWidth_3;
                uint _S481 = 2U % atlasWidth_3;
                int _S482 = int(_S481);
                uint _S483 = 2U / _S480;
                int3 _S484 = int3(_S482, int(_S483), int(0));
                int _S485 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S484)).xy), uint(((_S484)).z))).x * 255.0f)));
                uint _S486 = atlasWidth_3;
                uint _S487 = 3U % atlasWidth_3;
                int _S488 = int(_S487);
                uint _S489 = 3U / _S486;
                int3 _S490 = int3(_S488, int(_S489), int(0));
                int2 minimumTile_3 = int2(_S485, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S490)).xy), uint(((_S490)).z))).x * 255.0f))));
                uint _S491 = atlasWidth_3;
                uint _S492 = 4U % atlasWidth_3;
                int _S493 = int(_S492);
                uint _S494 = 4U / _S491;
                int3 _S495 = int3(_S493, int(_S494), int(0));
                uint _S496 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S495)).xy), uint(((_S495)).z))).x * 255.0f));
                uint _S497 = atlasWidth_3;
                uint _S498 = 5U % atlasWidth_3;
                int _S499 = int(_S498);
                uint _S500 = 5U / _S497;
                int3 _S501 = int3(_S499, int(_S500), int(0));
                uint _S502 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S501)).xy), uint(((_S501)).z))).x * 255.0f));
                uint2 grid_3 = uint2(_S496, _S502);
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
                    int3 _S503 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S503)).xy), uint(((_S503)).z)));
                    break;
                }
                uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S496 + uint(tile_6.x));
                uint _S504 = atlasWidth_3;
                uint _S505 = offset_3 % atlasWidth_3;
                int _S506 = int(_S505);
                uint _S507 = offset_3 / _S504;
                int3 _S508 = int3(_S506, int(_S507), int(0));
                uint _S509 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S508)).xy), uint(((_S508)).z))).x * 255.0f));
                uint _S510 = atlasWidth_3;
                uint _S511 = offset_3 + 1U;
                uint _S512 = _S511 % atlasWidth_3;
                int _S513 = int(_S512);
                uint _S514 = _S511 / _S510;
                int3 _S515 = int3(_S513, int(_S514), int(0));
                uint slot_6 = _S509 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S515)).xy), uint(((_S515)).z))).x * 255.0f)) << 8U);
                if(slot_6 == 0U)
                {
                    int3 _S516 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S516)).xy), uint(((_S516)).z)));
                    break;
                }
                uint slot_7 = slot_6 - 1U;
                uint _S517 = atlasWidth_3;
                uint _S518 = 6U % atlasWidth_3;
                int _S519 = int(_S518);
                uint _S520 = 6U / _S517;
                int3 _S521 = int3(_S519, int(_S520), int(0));
                uint _S522 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S521)).xy), uint(((_S521)).z))).x * 255.0f));
                uint _S523 = atlasWidth_3;
                uint _S524 = 7U % atlasWidth_3;
                int _S525 = int(_S524);
                uint _S526 = 7U / _S523;
                int3 _S527 = int3(_S525, int(_S526), int(0));
                uint columns_3 = _S522 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S527)).xy), uint(((_S527)).z))).x * 255.0f)) << 8U);
                uint _S528 = atlasWidth_3;
                uint _S529 = 8U % atlasWidth_3;
                int _S530 = int(_S529);
                uint _S531 = 8U / _S528;
                int3 _S532 = int3(_S530, int(_S531), int(0));
                uint _S533 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S532)).xy), uint(((_S532)).z))).x * 255.0f));
                uint _S534 = atlasWidth_3;
                uint _S535 = 9U % atlasWidth_3;
                int _S536 = int(_S535);
                uint _S537 = 9U / _S534;
                int3 _S538 = int3(_S536, int(_S537), int(0));
                uint rows_3 = ((_S533 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S538)).xy), uint(((_S538)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                uint metadataRows_3 = (10U + 2U * _S496 * _S502 + atlasWidth_3 - 1U) / atlasWidth_3;
                uint _S539 = atlasWidth_3 / columns_3;
                float _S540 = float(_S539);
                uint _S541 = (atlasHeight_3 - metadataRows_3) / rows_3;
                float2 cellSize_6 = float2(_S540, float(_S541));
                uint _S542 = slot_7 % columns_3;
                float _S543 = float(_S542);
                uint _S544 = slot_7 / columns_3;
                _S180 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S543, float(_S544)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(detailUv_0) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            int2 _S545 = int2(metadata_3.zw);
            int2 tile_7 = int2(floor(detailUv_0)) - int2(metadata_3.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S545);
            }
            if(hasSceneLighting_0)
            {
                int3 _S546 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S180 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S546)).xy), uint(((_S546)).z)));
                break;
            }
            uint _S547 = atlasWidth_3 / uint(_S545.x);
            float _S548 = float(_S547);
            uint _S549 = (atlasHeight_3 - 1U) / uint(_S545.y);
            float2 cellSize_7 = float2(_S548, float(_S549));
            _S180 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        detailSample_0 = _S180;
        if((_S471.w) != 0.0f)
        {
            float4 _S550 = detailSample_0;
            for(;;)
            {
                float _S551 = _S550.w;
                if(_S551 > 1.0f)
                {
                    unlitColor_0 = _S550.xyz;
                    break;
                }
                texture2d<float, access::sample> _S552 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_7;
                thread uint height_7;
                (*((&width_7)) = (_S552).get_width(0)),(*((&height_7)) = (_S552).get_height(0));
                uint3 _S553 = uint3(round(_S550.xyz * float3(255.0f) ));
                uint index_3 = ((_S553.x) | ((_S553.y) << 8U)) | ((_S553.z) << 16U);
                if(_S551 == 0.0f)
                {
                    _S181 = _S553;
                }
                else
                {
                    _S181 = uint3(index_3, index_3, index_3);
                }
                uint3 address_2 = _S181 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S554 = address_2.x;
                uint _S555 = _S554 % width_7;
                int _S556 = int(_S555);
                uint _S557 = _S554 / width_7;
                int3 _S558 = int3(_S556, int(_S557), int(0));
                float _S559 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S558)).xy), uint(((_S558)).z))).x;
                uint _S560 = address_2.y;
                uint _S561 = _S560 % width_7;
                int _S562 = int(_S561);
                uint _S563 = _S560 / width_7;
                int3 _S564 = int3(_S562, int(_S563), int(0));
                float _S565 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S564)).xy), uint(((_S564)).z))).x;
                uint _S566 = address_2.z;
                uint _S567 = _S566 % width_7;
                int _S568 = int(_S567);
                uint _S569 = _S566 / width_7;
                int3 _S570 = int3(_S568, int(_S569), int(0));
                unlitColor_0 = float3(_S559, _S565, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S570)).xy), uint(((_S570)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S571 = detail_0;
        if(any(_S472 != _S465))
        {
            float _S572 = _S472.x;
            float _S573 = _S472.y;
            float2 tangent_3 = normalize(float2(_S573, - _S572) / float2(max(abs(_S572), abs(_S573))) );
            float2 bitangent_1 = float2(- tangent_3.y, tangent_3.x);
            if((dot(_S469, tangent_3)) < 0.0f)
            {
                tangent_1 = - tangent_3;
            }
            else
            {
                tangent_1 = tangent_3;
            }
            _S571.xy = tangent_1 * float2(_S571.x)  + bitangent_1 * float2(_S571.y) ;
        }
        float3 _S574 = _S571;
        for(;;)
        {
            float3 t_0 = normalize(_S468) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S574) * float3(-1.0f, -1.0f, 1.0f);
            float _S575 = t_0.z;
            if(_S575 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S575))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S468;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S576 = float3(unlitColor_0.x) ;
    float3 _S577 = float3(unlitColor_0.y) ;
    float3 _S578 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S576 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S577 + _S1.normal_0 * _S578);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S576 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S577 + worldNormalBasis_0 * _S578);
    float4 _S579;
    if((textureMask_0 & 8U) != 0U)
    {
        bool useSecondary_2 = (secondaryMask_0 & 8U) != 0U;
        if((alternateMask_0 & 8U) != 0U)
        {
            if(useSecondary_2)
            {
                detailSource_0 = _S179;
            }
            else
            {
                detailSource_0 = _S178;
            }
        }
        else
        {
            if(useSecondary_2)
            {
                detailSource_0 = _S171;
            }
            else
            {
                detailSource_0 = _S170;
            }
        }
        bool _S580 = (udimMask_0 & 8U) != 0U;
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
        uint _S581 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S580)
            {
                for(;;)
                {
                    if(_S581 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S582 = (&kernelContext_0)->roughnessMetallicTexture_0;
                    thread uint width_8;
                    thread uint height_8;
                    (*((&width_8)) = (_S582).get_width(0)),(*((&height_8)) = (_S582).get_height(0));
                    float2 size_5 = float2(float(width_8), float(height_8));
                    float2 _S583 = float2(0.5f) ;
                    float2 remapped_5 = clamp((detailSource_0 * (size_5 - float2(2.0f) ) + float2(1.0f) ) / size_5, _S583 / size_5, (size_5 - _S583) / size_5);
                    if((_S581 & 1U) != 0U)
                    {
                        roughness_0 = remapped_5.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S581 & 2U) != 0U)
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
                _S180 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S584 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S584).get_width(0)),(*((&atlasHeight_4)) = (_S584).get_height(0));
            int3 _S585 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S585)).xy), uint(((_S585)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S586 = atlasWidth_4;
                uint _S587 = 2U % atlasWidth_4;
                int _S588 = int(_S587);
                uint _S589 = 2U / _S586;
                int3 _S590 = int3(_S588, int(_S589), int(0));
                int _S591 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S590)).xy), uint(((_S590)).z))).x * 255.0f)));
                uint _S592 = atlasWidth_4;
                uint _S593 = 3U % atlasWidth_4;
                int _S594 = int(_S593);
                uint _S595 = 3U / _S592;
                int3 _S596 = int3(_S594, int(_S595), int(0));
                int2 minimumTile_4 = int2(_S591, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S596)).xy), uint(((_S596)).z))).x * 255.0f))));
                uint _S597 = atlasWidth_4;
                uint _S598 = 4U % atlasWidth_4;
                int _S599 = int(_S598);
                uint _S600 = 4U / _S597;
                int3 _S601 = int3(_S599, int(_S600), int(0));
                uint _S602 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S601)).xy), uint(((_S601)).z))).x * 255.0f));
                uint _S603 = atlasWidth_4;
                uint _S604 = 5U % atlasWidth_4;
                int _S605 = int(_S604);
                uint _S606 = 5U / _S603;
                int3 _S607 = int3(_S605, int(_S606), int(0));
                uint _S608 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S607)).xy), uint(((_S607)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S602, _S608);
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
                    int3 _S609 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S609)).xy), uint(((_S609)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S602 + uint(tile_8.x));
                uint _S610 = atlasWidth_4;
                uint _S611 = offset_4 % atlasWidth_4;
                int _S612 = int(_S611);
                uint _S613 = offset_4 / _S610;
                int3 _S614 = int3(_S612, int(_S613), int(0));
                uint _S615 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S614)).xy), uint(((_S614)).z))).x * 255.0f));
                uint _S616 = atlasWidth_4;
                uint _S617 = offset_4 + 1U;
                uint _S618 = _S617 % atlasWidth_4;
                int _S619 = int(_S618);
                uint _S620 = _S617 / _S616;
                int3 _S621 = int3(_S619, int(_S620), int(0));
                uint slot_8 = _S615 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S621)).xy), uint(((_S621)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S622 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S622)).xy), uint(((_S622)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S623 = atlasWidth_4;
                uint _S624 = 6U % atlasWidth_4;
                int _S625 = int(_S624);
                uint _S626 = 6U / _S623;
                int3 _S627 = int3(_S625, int(_S626), int(0));
                uint _S628 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S627)).xy), uint(((_S627)).z))).x * 255.0f));
                uint _S629 = atlasWidth_4;
                uint _S630 = 7U % atlasWidth_4;
                int _S631 = int(_S630);
                uint _S632 = 7U / _S629;
                int3 _S633 = int3(_S631, int(_S632), int(0));
                uint columns_4 = _S628 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S633)).xy), uint(((_S633)).z))).x * 255.0f)) << 8U);
                uint _S634 = atlasWidth_4;
                uint _S635 = 8U % atlasWidth_4;
                int _S636 = int(_S635);
                uint _S637 = 8U / _S634;
                int3 _S638 = int3(_S636, int(_S637), int(0));
                uint _S639 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S638)).xy), uint(((_S638)).z))).x * 255.0f));
                uint _S640 = atlasWidth_4;
                uint _S641 = 9U % atlasWidth_4;
                int _S642 = int(_S641);
                uint _S643 = 9U / _S640;
                int3 _S644 = int3(_S642, int(_S643), int(0));
                uint rows_4 = ((_S639 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S644)).xy), uint(((_S644)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S602 * _S608 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S645 = atlasWidth_4 / columns_4;
                float _S646 = float(_S645);
                uint _S647 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S646, float(_S647));
                uint _S648 = slot_9 % columns_4;
                float _S649 = float(_S648);
                uint _S650 = slot_9 / columns_4;
                _S180 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S649, float(_S650)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(detailSource_0) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S651 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(detailSource_0)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S651);
            }
            if(hasSceneLighting_0)
            {
                int3 _S652 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S180 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S652)).xy), uint(((_S652)).z)));
                break;
            }
            uint _S653 = atlasWidth_4 / uint(_S651.x);
            float _S654 = float(_S653);
            uint _S655 = (atlasHeight_4 - 1U) / uint(_S651.y);
            float2 cellSize_9 = float2(_S654, float(_S655));
            _S180 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S656 = float4(_S7->compositeControls_0) ;
            if((_S656.x) != 8.0f)
            {
                break;
            }
            bool _S657 = (_S656.w) >= 0.5f;
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
            uint _S658 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S657)
                {
                    for(;;)
                    {
                        if(_S658 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S659 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_9;
                        thread uint height_9;
                        (*((&width_9)) = (_S659).get_width(0)),(*((&height_9)) = (_S659).get_height(0));
                        float2 size_6 = float2(float(width_9), float(height_9));
                        float2 _S660 = float2(0.5f) ;
                        float2 remapped_6 = clamp((detailSource_0 * (size_6 - float2(2.0f) ) + float2(1.0f) ) / size_6, _S660 / size_6, (size_6 - _S660) / size_6);
                        if((_S658 & 1U) != 0U)
                        {
                            roughness_0 = remapped_6.x;
                        }
                        else
                        {
                            roughness_0 = detailSource_0.x;
                        }
                        if((_S658 & 2U) != 0U)
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
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S661 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S661).get_width(0)),(*((&atlasHeight_5)) = (_S661).get_height(0));
                int3 _S662 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S662)).xy), uint(((_S662)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S663 = atlasWidth_5;
                    uint _S664 = 2U % atlasWidth_5;
                    int _S665 = int(_S664);
                    uint _S666 = 2U / _S663;
                    int3 _S667 = int3(_S665, int(_S666), int(0));
                    int _S668 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S667)).xy), uint(((_S667)).z))).x * 255.0f)));
                    uint _S669 = atlasWidth_5;
                    uint _S670 = 3U % atlasWidth_5;
                    int _S671 = int(_S670);
                    uint _S672 = 3U / _S669;
                    int3 _S673 = int3(_S671, int(_S672), int(0));
                    int2 minimumTile_5 = int2(_S668, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S673)).xy), uint(((_S673)).z))).x * 255.0f))));
                    uint _S674 = atlasWidth_5;
                    uint _S675 = 4U % atlasWidth_5;
                    int _S676 = int(_S675);
                    uint _S677 = 4U / _S674;
                    int3 _S678 = int3(_S676, int(_S677), int(0));
                    uint _S679 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S678)).xy), uint(((_S678)).z))).x * 255.0f));
                    uint _S680 = atlasWidth_5;
                    uint _S681 = 5U % atlasWidth_5;
                    int _S682 = int(_S681);
                    uint _S683 = 5U / _S680;
                    int3 _S684 = int3(_S682, int(_S683), int(0));
                    uint _S685 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S684)).xy), uint(((_S684)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S679, _S685);
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
                        int3 _S686 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S686)).xy), uint(((_S686)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S679 + uint(tile_10.x));
                    uint _S687 = atlasWidth_5;
                    uint _S688 = offset_5 % atlasWidth_5;
                    int _S689 = int(_S688);
                    uint _S690 = offset_5 / _S687;
                    int3 _S691 = int3(_S689, int(_S690), int(0));
                    uint _S692 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S691)).xy), uint(((_S691)).z))).x * 255.0f));
                    uint _S693 = atlasWidth_5;
                    uint _S694 = offset_5 + 1U;
                    uint _S695 = _S694 % atlasWidth_5;
                    int _S696 = int(_S695);
                    uint _S697 = _S694 / _S693;
                    int3 _S698 = int3(_S696, int(_S697), int(0));
                    uint slot_10 = _S692 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S698)).xy), uint(((_S698)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S699 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S699)).xy), uint(((_S699)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S700 = atlasWidth_5;
                    uint _S701 = 6U % atlasWidth_5;
                    int _S702 = int(_S701);
                    uint _S703 = 6U / _S700;
                    int3 _S704 = int3(_S702, int(_S703), int(0));
                    uint _S705 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S704)).xy), uint(((_S704)).z))).x * 255.0f));
                    uint _S706 = atlasWidth_5;
                    uint _S707 = 7U % atlasWidth_5;
                    int _S708 = int(_S707);
                    uint _S709 = 7U / _S706;
                    int3 _S710 = int3(_S708, int(_S709), int(0));
                    uint columns_5 = _S705 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S710)).xy), uint(((_S710)).z))).x * 255.0f)) << 8U);
                    uint _S711 = atlasWidth_5;
                    uint _S712 = 8U % atlasWidth_5;
                    int _S713 = int(_S712);
                    uint _S714 = 8U / _S711;
                    int3 _S715 = int3(_S713, int(_S714), int(0));
                    uint _S716 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S715)).xy), uint(((_S715)).z))).x * 255.0f));
                    uint _S717 = atlasWidth_5;
                    uint _S718 = 9U % atlasWidth_5;
                    int _S719 = int(_S718);
                    uint _S720 = 9U / _S717;
                    int3 _S721 = int3(_S719, int(_S720), int(0));
                    uint rows_5 = ((_S716 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S721)).xy), uint(((_S721)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S679 * _S685 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S722 = atlasWidth_5 / columns_5;
                    float _S723 = float(_S722);
                    uint _S724 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S723, float(_S724));
                    uint _S725 = slot_11 % columns_5;
                    float _S726 = float(_S725);
                    uint _S727 = slot_11 / columns_5;
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S726, float(_S727)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(detailSource_0) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S728 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(detailSource_0)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S728);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S729 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S729)).xy), uint(((_S729)).z)));
                    break;
                }
                uint _S730 = atlasWidth_5 / uint(_S728.x);
                float _S731 = float(_S730);
                uint _S732 = (atlasHeight_5 - 1U) / uint(_S728.y);
                float2 cellSize_11 = float2(_S731, float(_S732));
                _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_1 = uint(round(_S656.y));
            if(operation_1 == 1U)
            {
                _S180 = _S180 * _S579;
                break;
            }
            if(operation_1 == 2U)
            {
                _S180 = _S180 + _S579;
                break;
            }
            if(operation_1 == 3U)
            {
                _S180 = _S180 - _S579;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S656.z;
                _S180 = _S180 * float4((1.0f - factor_1))  + _S579 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S180.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S156;
    }
    float occlusion_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool useSecondary_3 = (secondaryMask_0 & 32U) != 0U;
        if((alternateMask_0 & 32U) != 0U)
        {
            if(useSecondary_3)
            {
                detailSource_0 = _S179;
            }
            else
            {
                detailSource_0 = _S178;
            }
        }
        else
        {
            if(useSecondary_3)
            {
                detailSource_0 = _S171;
            }
            else
            {
                detailSource_0 = _S170;
            }
        }
        bool _S733 = (udimMask_0 & 32U) != 0U;
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
        uint _S734 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S733)
            {
                for(;;)
                {
                    if(_S734 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S735 = (&kernelContext_0)->metallicTexture_0;
                    thread uint width_10;
                    thread uint height_10;
                    (*((&width_10)) = (_S735).get_width(0)),(*((&height_10)) = (_S735).get_height(0));
                    float2 size_7 = float2(float(width_10), float(height_10));
                    float2 _S736 = float2(0.5f) ;
                    float2 remapped_7 = clamp((detailSource_0 * (size_7 - float2(2.0f) ) + float2(1.0f) ) / size_7, _S736 / size_7, (size_7 - _S736) / size_7);
                    if((_S734 & 1U) != 0U)
                    {
                        metallic_0 = remapped_7.x;
                    }
                    else
                    {
                        metallic_0 = detailSource_0.x;
                    }
                    if((_S734 & 2U) != 0U)
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
                _S180 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S737 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S737).get_width(0)),(*((&atlasHeight_6)) = (_S737).get_height(0));
            int3 _S738 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S738)).xy), uint(((_S738)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S739 = atlasWidth_6;
                uint _S740 = 2U % atlasWidth_6;
                int _S741 = int(_S740);
                uint _S742 = 2U / _S739;
                int3 _S743 = int3(_S741, int(_S742), int(0));
                int _S744 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S743)).xy), uint(((_S743)).z))).x * 255.0f)));
                uint _S745 = atlasWidth_6;
                uint _S746 = 3U % atlasWidth_6;
                int _S747 = int(_S746);
                uint _S748 = 3U / _S745;
                int3 _S749 = int3(_S747, int(_S748), int(0));
                int2 minimumTile_6 = int2(_S744, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S749)).xy), uint(((_S749)).z))).x * 255.0f))));
                uint _S750 = atlasWidth_6;
                uint _S751 = 4U % atlasWidth_6;
                int _S752 = int(_S751);
                uint _S753 = 4U / _S750;
                int3 _S754 = int3(_S752, int(_S753), int(0));
                uint _S755 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S754)).xy), uint(((_S754)).z))).x * 255.0f));
                uint _S756 = atlasWidth_6;
                uint _S757 = 5U % atlasWidth_6;
                int _S758 = int(_S757);
                uint _S759 = 5U / _S756;
                int3 _S760 = int3(_S758, int(_S759), int(0));
                uint _S761 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S760)).xy), uint(((_S760)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S755, _S761);
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
                    int3 _S762 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S762)).xy), uint(((_S762)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S755 + uint(tile_12.x));
                uint _S763 = atlasWidth_6;
                uint _S764 = offset_6 % atlasWidth_6;
                int _S765 = int(_S764);
                uint _S766 = offset_6 / _S763;
                int3 _S767 = int3(_S765, int(_S766), int(0));
                uint _S768 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S767)).xy), uint(((_S767)).z))).x * 255.0f));
                uint _S769 = atlasWidth_6;
                uint _S770 = offset_6 + 1U;
                uint _S771 = _S770 % atlasWidth_6;
                int _S772 = int(_S771);
                uint _S773 = _S770 / _S769;
                int3 _S774 = int3(_S772, int(_S773), int(0));
                uint slot_12 = _S768 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S774)).xy), uint(((_S774)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S775 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S775)).xy), uint(((_S775)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S776 = atlasWidth_6;
                uint _S777 = 6U % atlasWidth_6;
                int _S778 = int(_S777);
                uint _S779 = 6U / _S776;
                int3 _S780 = int3(_S778, int(_S779), int(0));
                uint _S781 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S780)).xy), uint(((_S780)).z))).x * 255.0f));
                uint _S782 = atlasWidth_6;
                uint _S783 = 7U % atlasWidth_6;
                int _S784 = int(_S783);
                uint _S785 = 7U / _S782;
                int3 _S786 = int3(_S784, int(_S785), int(0));
                uint columns_6 = _S781 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S786)).xy), uint(((_S786)).z))).x * 255.0f)) << 8U);
                uint _S787 = atlasWidth_6;
                uint _S788 = 8U % atlasWidth_6;
                int _S789 = int(_S788);
                uint _S790 = 8U / _S787;
                int3 _S791 = int3(_S789, int(_S790), int(0));
                uint _S792 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S791)).xy), uint(((_S791)).z))).x * 255.0f));
                uint _S793 = atlasWidth_6;
                uint _S794 = 9U % atlasWidth_6;
                int _S795 = int(_S794);
                uint _S796 = 9U / _S793;
                int3 _S797 = int3(_S795, int(_S796), int(0));
                uint rows_6 = ((_S792 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S797)).xy), uint(((_S797)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S755 * _S761 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S798 = atlasWidth_6 / columns_6;
                float _S799 = float(_S798);
                uint _S800 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S799, float(_S800));
                uint _S801 = slot_13 % columns_6;
                float _S802 = float(_S801);
                uint _S803 = slot_13 / columns_6;
                _S180 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S802, float(_S803)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(detailSource_0) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S804 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(detailSource_0)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S804);
            }
            if(hasSceneLighting_0)
            {
                int3 _S805 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S180 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S805)).xy), uint(((_S805)).z)));
                break;
            }
            uint _S806 = atlasWidth_6 / uint(_S804.x);
            float _S807 = float(_S806);
            uint _S808 = (atlasHeight_6 - 1U) / uint(_S804.y);
            float2 cellSize_13 = float2(_S807, float(_S808));
            _S180 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        for(;;)
        {
            float4 _S809 = float4(_S7->compositeControls_0) ;
            if((_S809.x) != 32.0f)
            {
                break;
            }
            bool _S810 = (_S809.w) >= 0.5f;
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
            uint _S811 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S810)
                {
                    for(;;)
                    {
                        if(_S811 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S812 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_11;
                        thread uint height_11;
                        (*((&width_11)) = (_S812).get_width(0)),(*((&height_11)) = (_S812).get_height(0));
                        float2 size_8 = float2(float(width_11), float(height_11));
                        float2 _S813 = float2(0.5f) ;
                        float2 remapped_8 = clamp((detailSource_0 * (size_8 - float2(2.0f) ) + float2(1.0f) ) / size_8, _S813 / size_8, (size_8 - _S813) / size_8);
                        if((_S811 & 1U) != 0U)
                        {
                            metallic_0 = remapped_8.x;
                        }
                        else
                        {
                            metallic_0 = detailSource_0.x;
                        }
                        if((_S811 & 2U) != 0U)
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
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S814 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S814).get_width(0)),(*((&atlasHeight_7)) = (_S814).get_height(0));
                int3 _S815 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S815)).xy), uint(((_S815)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S816 = atlasWidth_7;
                    uint _S817 = 2U % atlasWidth_7;
                    int _S818 = int(_S817);
                    uint _S819 = 2U / _S816;
                    int3 _S820 = int3(_S818, int(_S819), int(0));
                    int _S821 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S820)).xy), uint(((_S820)).z))).x * 255.0f)));
                    uint _S822 = atlasWidth_7;
                    uint _S823 = 3U % atlasWidth_7;
                    int _S824 = int(_S823);
                    uint _S825 = 3U / _S822;
                    int3 _S826 = int3(_S824, int(_S825), int(0));
                    int2 minimumTile_7 = int2(_S821, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S826)).xy), uint(((_S826)).z))).x * 255.0f))));
                    uint _S827 = atlasWidth_7;
                    uint _S828 = 4U % atlasWidth_7;
                    int _S829 = int(_S828);
                    uint _S830 = 4U / _S827;
                    int3 _S831 = int3(_S829, int(_S830), int(0));
                    uint _S832 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S831)).xy), uint(((_S831)).z))).x * 255.0f));
                    uint _S833 = atlasWidth_7;
                    uint _S834 = 5U % atlasWidth_7;
                    int _S835 = int(_S834);
                    uint _S836 = 5U / _S833;
                    int3 _S837 = int3(_S835, int(_S836), int(0));
                    uint _S838 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S837)).xy), uint(((_S837)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S832, _S838);
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
                        int3 _S839 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S839)).xy), uint(((_S839)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S832 + uint(tile_14.x));
                    uint _S840 = atlasWidth_7;
                    uint _S841 = offset_7 % atlasWidth_7;
                    int _S842 = int(_S841);
                    uint _S843 = offset_7 / _S840;
                    int3 _S844 = int3(_S842, int(_S843), int(0));
                    uint _S845 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S844)).xy), uint(((_S844)).z))).x * 255.0f));
                    uint _S846 = atlasWidth_7;
                    uint _S847 = offset_7 + 1U;
                    uint _S848 = _S847 % atlasWidth_7;
                    int _S849 = int(_S848);
                    uint _S850 = _S847 / _S846;
                    int3 _S851 = int3(_S849, int(_S850), int(0));
                    uint slot_14 = _S845 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S851)).xy), uint(((_S851)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S852 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S852)).xy), uint(((_S852)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S853 = atlasWidth_7;
                    uint _S854 = 6U % atlasWidth_7;
                    int _S855 = int(_S854);
                    uint _S856 = 6U / _S853;
                    int3 _S857 = int3(_S855, int(_S856), int(0));
                    uint _S858 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S857)).xy), uint(((_S857)).z))).x * 255.0f));
                    uint _S859 = atlasWidth_7;
                    uint _S860 = 7U % atlasWidth_7;
                    int _S861 = int(_S860);
                    uint _S862 = 7U / _S859;
                    int3 _S863 = int3(_S861, int(_S862), int(0));
                    uint columns_7 = _S858 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S863)).xy), uint(((_S863)).z))).x * 255.0f)) << 8U);
                    uint _S864 = atlasWidth_7;
                    uint _S865 = 8U % atlasWidth_7;
                    int _S866 = int(_S865);
                    uint _S867 = 8U / _S864;
                    int3 _S868 = int3(_S866, int(_S867), int(0));
                    uint _S869 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S868)).xy), uint(((_S868)).z))).x * 255.0f));
                    uint _S870 = atlasWidth_7;
                    uint _S871 = 9U % atlasWidth_7;
                    int _S872 = int(_S871);
                    uint _S873 = 9U / _S870;
                    int3 _S874 = int3(_S872, int(_S873), int(0));
                    uint rows_7 = ((_S869 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S874)).xy), uint(((_S874)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S832 * _S838 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S875 = atlasWidth_7 / columns_7;
                    float _S876 = float(_S875);
                    uint _S877 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S876, float(_S877));
                    uint _S878 = slot_15 % columns_7;
                    float _S879 = float(_S878);
                    uint _S880 = slot_15 / columns_7;
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S879, float(_S880)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(detailSource_0) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S881 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(detailSource_0)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S881);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S882 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S882)).xy), uint(((_S882)).z)));
                    break;
                }
                uint _S883 = atlasWidth_7 / uint(_S881.x);
                float _S884 = float(_S883);
                uint _S885 = (atlasHeight_7 - 1U) / uint(_S881.y);
                float2 cellSize_15 = float2(_S884, float(_S885));
                _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_2 = uint(round(_S809.y));
            if(operation_2 == 1U)
            {
                _S180 = _S180 * _S579;
                break;
            }
            if(operation_2 == 2U)
            {
                _S180 = _S180 + _S579;
                break;
            }
            if(operation_2 == 3U)
            {
                _S180 = _S180 - _S579;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S809.z;
                _S180 = _S180 * float4((1.0f - factor_2))  + _S579 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S180.x);
    }
    else
    {
        metallic_0 = _S155;
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
                detailSource_0 = _S179;
            }
            else
            {
                detailSource_0 = _S178;
            }
        }
        else
        {
            if(useSecondary_4)
            {
                detailSource_0 = _S171;
            }
            else
            {
                detailSource_0 = _S170;
            }
        }
        thread float4 emissionSample_0;
        bool _S886 = (udimMask_0 & 16U) != 0U;
        bool _S887 = (maskU_1 & 16U) != 0U;
        if(_S887)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S888 = (maskV_1 & 16U) != 0U;
        if(_S888)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S889 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S886)
            {
                for(;;)
                {
                    if(_S889 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S890 = (&kernelContext_0)->emissiveTexture_0;
                    thread uint width_12;
                    thread uint height_12;
                    (*((&width_12)) = (_S890).get_width(0)),(*((&height_12)) = (_S890).get_height(0));
                    float2 size_9 = float2(float(width_12), float(height_12));
                    float2 _S891 = float2(0.5f) ;
                    float2 remapped_9 = clamp((detailSource_0 * (size_9 - float2(2.0f) ) + float2(1.0f) ) / size_9, _S891 / size_9, (size_9 - _S891) / size_9);
                    if((_S889 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_9.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S889 & 2U) != 0U)
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
                _S180 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S892 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S892).get_width(0)),(*((&atlasHeight_8)) = (_S892).get_height(0));
            int3 _S893 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S893)).xy), uint(((_S893)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S894 = atlasWidth_8;
                uint _S895 = 2U % atlasWidth_8;
                int _S896 = int(_S895);
                uint _S897 = 2U / _S894;
                int3 _S898 = int3(_S896, int(_S897), int(0));
                int _S899 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S898)).xy), uint(((_S898)).z))).x * 255.0f)));
                uint _S900 = atlasWidth_8;
                uint _S901 = 3U % atlasWidth_8;
                int _S902 = int(_S901);
                uint _S903 = 3U / _S900;
                int3 _S904 = int3(_S902, int(_S903), int(0));
                int2 minimumTile_8 = int2(_S899, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S904)).xy), uint(((_S904)).z))).x * 255.0f))));
                uint _S905 = atlasWidth_8;
                uint _S906 = 4U % atlasWidth_8;
                int _S907 = int(_S906);
                uint _S908 = 4U / _S905;
                int3 _S909 = int3(_S907, int(_S908), int(0));
                uint _S910 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S909)).xy), uint(((_S909)).z))).x * 255.0f));
                uint _S911 = atlasWidth_8;
                uint _S912 = 5U % atlasWidth_8;
                int _S913 = int(_S912);
                uint _S914 = 5U / _S911;
                int3 _S915 = int3(_S913, int(_S914), int(0));
                uint _S916 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S915)).xy), uint(((_S915)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S910, _S916);
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
                    int3 _S917 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S917)).xy), uint(((_S917)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S910 + uint(tile_16.x));
                uint _S918 = atlasWidth_8;
                uint _S919 = offset_8 % atlasWidth_8;
                int _S920 = int(_S919);
                uint _S921 = offset_8 / _S918;
                int3 _S922 = int3(_S920, int(_S921), int(0));
                uint _S923 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S922)).xy), uint(((_S922)).z))).x * 255.0f));
                uint _S924 = atlasWidth_8;
                uint _S925 = offset_8 + 1U;
                uint _S926 = _S925 % atlasWidth_8;
                int _S927 = int(_S926);
                uint _S928 = _S925 / _S924;
                int3 _S929 = int3(_S927, int(_S928), int(0));
                uint slot_16 = _S923 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S929)).xy), uint(((_S929)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S930 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S930)).xy), uint(((_S930)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S931 = atlasWidth_8;
                uint _S932 = 6U % atlasWidth_8;
                int _S933 = int(_S932);
                uint _S934 = 6U / _S931;
                int3 _S935 = int3(_S933, int(_S934), int(0));
                uint _S936 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S935)).xy), uint(((_S935)).z))).x * 255.0f));
                uint _S937 = atlasWidth_8;
                uint _S938 = 7U % atlasWidth_8;
                int _S939 = int(_S938);
                uint _S940 = 7U / _S937;
                int3 _S941 = int3(_S939, int(_S940), int(0));
                uint columns_8 = _S936 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S941)).xy), uint(((_S941)).z))).x * 255.0f)) << 8U);
                uint _S942 = atlasWidth_8;
                uint _S943 = 8U % atlasWidth_8;
                int _S944 = int(_S943);
                uint _S945 = 8U / _S942;
                int3 _S946 = int3(_S944, int(_S945), int(0));
                uint _S947 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S946)).xy), uint(((_S946)).z))).x * 255.0f));
                uint _S948 = atlasWidth_8;
                uint _S949 = 9U % atlasWidth_8;
                int _S950 = int(_S949);
                uint _S951 = 9U / _S948;
                int3 _S952 = int3(_S950, int(_S951), int(0));
                uint rows_8 = ((_S947 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S952)).xy), uint(((_S952)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S910 * _S916 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S953 = atlasWidth_8 / columns_8;
                float _S954 = float(_S953);
                uint _S955 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S954, float(_S955));
                uint _S956 = slot_17 % columns_8;
                float _S957 = float(_S956);
                uint _S958 = slot_17 / columns_8;
                _S180 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S957, float(_S958)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(detailSource_0) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S959 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(detailSource_0)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S959);
            }
            if(hasSceneLighting_0)
            {
                int3 _S960 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S180 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S960)).xy), uint(((_S960)).z)));
                break;
            }
            uint _S961 = atlasWidth_8 / uint(_S959.x);
            float _S962 = float(_S961);
            uint _S963 = (atlasHeight_8 - 1U) / uint(_S959.y);
            float2 cellSize_17 = float2(_S962, float(_S963));
            _S180 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        emissionSample_0 = _S180;
        if((_S165.w) != 0.0f)
        {
            texture2d<float, access::sample> _S964 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S964).get_width(0)),(*((&sourceHeight_1)) = (_S964).get_height(0));
            texture2d<float, access::sample> _S965 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S965).get_width(0)),(*((&valueHeight_1)) = (_S965).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S966 = emissionSample_0;
                for(;;)
                {
                    float _S967 = _S966.w;
                    if(_S967 > 1.0f)
                    {
                        unlitColor_0 = _S966.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S968 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_13;
                    thread uint height_13;
                    (*((&width_13)) = (_S968).get_width(0)),(*((&height_13)) = (_S968).get_height(0));
                    uint3 _S969 = uint3(round(_S966.xyz * float3(255.0f) ));
                    uint index_4 = ((_S969.x) | ((_S969.y) << 8U)) | ((_S969.z) << 16U);
                    if(_S967 == 0.0f)
                    {
                        _S181 = _S969;
                    }
                    else
                    {
                        _S181 = uint3(index_4, index_4, index_4);
                    }
                    uint3 address_3 = _S181 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S970 = address_3.x;
                    uint _S971 = _S970 % width_13;
                    int _S972 = int(_S971);
                    uint _S973 = _S970 / width_13;
                    int3 _S974 = int3(_S972, int(_S973), int(0));
                    float _S975 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S974)).xy), uint(((_S974)).z))).x;
                    uint _S976 = address_3.y;
                    uint _S977 = _S976 % width_13;
                    int _S978 = int(_S977);
                    uint _S979 = _S976 / width_13;
                    int3 _S980 = int3(_S978, int(_S979), int(0));
                    float _S981 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S980)).xy), uint(((_S980)).z))).x;
                    uint _S982 = address_3.z;
                    uint _S983 = _S982 % width_13;
                    int _S984 = int(_S983);
                    uint _S985 = _S982 / width_13;
                    int3 _S986 = int3(_S984, int(_S985), int(0));
                    unlitColor_0 = float3(_S975, _S981, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S986)).xy), uint(((_S986)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                if(_S887)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S888)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S987 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S987 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S988 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_14;
                    thread uint height_14;
                    (*((&width_14)) = (_S988).get_width(0)),(*((&height_14)) = (_S988).get_height(0));
                    float2 size_10 = float2(float(width_14), float(height_14));
                    float2 _S989 = float2(0.5f) ;
                    float2 remapped_10 = clamp((detailSource_0 * (size_10 - float2(2.0f) ) + float2(1.0f) ) / size_10, _S989 / size_10, (size_10 - _S989) / size_10);
                    if((_S987 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_10.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S987 & 2U) != 0U)
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
        float4 _S990 = emissionSample_0;
        for(;;)
        {
            float4 _S991 = float4(_S7->compositeControls_0) ;
            if((_S991.x) != 16.0f)
            {
                _S180 = _S990;
                break;
            }
            bool _S992 = (_S991.w) >= 0.5f;
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
            uint _S993 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S992)
                {
                    for(;;)
                    {
                        if(_S993 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S994 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_15;
                        thread uint height_15;
                        (*((&width_15)) = (_S994).get_width(0)),(*((&height_15)) = (_S994).get_height(0));
                        float2 size_11 = float2(float(width_15), float(height_15));
                        float2 _S995 = float2(0.5f) ;
                        float2 remapped_11 = clamp((detailSource_0 * (size_11 - float2(2.0f) ) + float2(1.0f) ) / size_11, _S995 / size_11, (size_11 - _S995) / size_11);
                        if((_S993 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_11.x;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.x;
                        }
                        if((_S993 & 2U) != 0U)
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
                    _S180 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S996 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S996).get_width(0)),(*((&atlasHeight_9)) = (_S996).get_height(0));
                int3 _S997 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S997)).xy), uint(((_S997)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S998 = atlasWidth_9;
                    uint _S999 = 2U % atlasWidth_9;
                    int _S1000 = int(_S999);
                    uint _S1001 = 2U / _S998;
                    int3 _S1002 = int3(_S1000, int(_S1001), int(0));
                    int _S1003 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1002)).xy), uint(((_S1002)).z))).x * 255.0f)));
                    uint _S1004 = atlasWidth_9;
                    uint _S1005 = 3U % atlasWidth_9;
                    int _S1006 = int(_S1005);
                    uint _S1007 = 3U / _S1004;
                    int3 _S1008 = int3(_S1006, int(_S1007), int(0));
                    int2 minimumTile_9 = int2(_S1003, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1008)).xy), uint(((_S1008)).z))).x * 255.0f))));
                    uint _S1009 = atlasWidth_9;
                    uint _S1010 = 4U % atlasWidth_9;
                    int _S1011 = int(_S1010);
                    uint _S1012 = 4U / _S1009;
                    int3 _S1013 = int3(_S1011, int(_S1012), int(0));
                    uint _S1014 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1013)).xy), uint(((_S1013)).z))).x * 255.0f));
                    uint _S1015 = atlasWidth_9;
                    uint _S1016 = 5U % atlasWidth_9;
                    int _S1017 = int(_S1016);
                    uint _S1018 = 5U / _S1015;
                    int3 _S1019 = int3(_S1017, int(_S1018), int(0));
                    uint _S1020 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1019)).xy), uint(((_S1019)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S1014, _S1020);
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
                        int3 _S1021 = int3(int(1), int(0), int(0));
                        _S180 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1021)).xy), uint(((_S1021)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S1014 + uint(tile_18.x));
                    uint _S1022 = atlasWidth_9;
                    uint _S1023 = offset_9 % atlasWidth_9;
                    int _S1024 = int(_S1023);
                    uint _S1025 = offset_9 / _S1022;
                    int3 _S1026 = int3(_S1024, int(_S1025), int(0));
                    uint _S1027 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1026)).xy), uint(((_S1026)).z))).x * 255.0f));
                    uint _S1028 = atlasWidth_9;
                    uint _S1029 = offset_9 + 1U;
                    uint _S1030 = _S1029 % atlasWidth_9;
                    int _S1031 = int(_S1030);
                    uint _S1032 = _S1029 / _S1028;
                    int3 _S1033 = int3(_S1031, int(_S1032), int(0));
                    uint slot_18 = _S1027 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1033)).xy), uint(((_S1033)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S1034 = int3(int(1), int(0), int(0));
                        _S180 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1034)).xy), uint(((_S1034)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S1035 = atlasWidth_9;
                    uint _S1036 = 6U % atlasWidth_9;
                    int _S1037 = int(_S1036);
                    uint _S1038 = 6U / _S1035;
                    int3 _S1039 = int3(_S1037, int(_S1038), int(0));
                    uint _S1040 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1039)).xy), uint(((_S1039)).z))).x * 255.0f));
                    uint _S1041 = atlasWidth_9;
                    uint _S1042 = 7U % atlasWidth_9;
                    int _S1043 = int(_S1042);
                    uint _S1044 = 7U / _S1041;
                    int3 _S1045 = int3(_S1043, int(_S1044), int(0));
                    uint columns_9 = _S1040 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1045)).xy), uint(((_S1045)).z))).x * 255.0f)) << 8U);
                    uint _S1046 = atlasWidth_9;
                    uint _S1047 = 8U % atlasWidth_9;
                    int _S1048 = int(_S1047);
                    uint _S1049 = 8U / _S1046;
                    int3 _S1050 = int3(_S1048, int(_S1049), int(0));
                    uint _S1051 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1050)).xy), uint(((_S1050)).z))).x * 255.0f));
                    uint _S1052 = atlasWidth_9;
                    uint _S1053 = 9U % atlasWidth_9;
                    int _S1054 = int(_S1053);
                    uint _S1055 = 9U / _S1052;
                    int3 _S1056 = int3(_S1054, int(_S1055), int(0));
                    uint rows_9 = ((_S1051 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1056)).xy), uint(((_S1056)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S1014 * _S1020 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S1057 = atlasWidth_9 / columns_9;
                    float _S1058 = float(_S1057);
                    uint _S1059 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S1058, float(_S1059));
                    uint _S1060 = slot_19 % columns_9;
                    float _S1061 = float(_S1060);
                    uint _S1062 = slot_19 / columns_9;
                    _S180 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1061, float(_S1062)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(detailSource_0) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S1063 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(detailSource_0)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S1063);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1064 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S180 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1064)).xy), uint(((_S1064)).z)));
                    break;
                }
                uint _S1065 = atlasWidth_9 / uint(_S1063.x);
                float _S1066 = float(_S1065);
                uint _S1067 = (atlasHeight_9 - 1U) / uint(_S1063.y);
                float2 cellSize_19 = float2(_S1066, float(_S1067));
                _S180 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_3 = uint(round(_S991.y));
            if(operation_3 == 1U)
            {
                _S180 = _S990 * _S180;
                break;
            }
            if(operation_3 == 2U)
            {
                _S180 = _S990 + _S180;
                break;
            }
            if(operation_3 == 3U)
            {
                _S180 = _S990 - _S180;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S991.z;
                _S180 = _S990 * float4((1.0f - factor_3))  + _S180 * float4(factor_3) ;
                break;
            }
            _S180 = _S990;
            break;
        }
        emissiveColor_1 = _S180.xyz;
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
                detailSource_0 = _S179;
            }
            else
            {
                detailSource_0 = _S178;
            }
        }
        else
        {
            if(useSecondary_5)
            {
                detailSource_0 = _S171;
            }
            else
            {
                detailSource_0 = _S170;
            }
        }
        for(;;)
        {
            bool _S1068 = (udimMask_0 & 64U) != 0U;
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
            uint _S1069 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1068)
                {
                    for(;;)
                    {
                        if(_S1069 == 0U)
                        {
                            tangent_1 = detailSource_0;
                            break;
                        }
                        texture2d<float, access::sample> _S1070 = (&kernelContext_0)->opacityTexture_0;
                        thread uint width_16;
                        thread uint height_16;
                        (*((&width_16)) = (_S1070).get_width(0)),(*((&height_16)) = (_S1070).get_height(0));
                        float2 size_12 = float2(float(width_16), float(height_16));
                        float2 _S1071 = float2(0.5f) ;
                        float2 remapped_12 = clamp((detailSource_0 * (size_12 - float2(2.0f) ) + float2(1.0f) ) / size_12, _S1071 / size_12, (size_12 - _S1071) / size_12);
                        if((_S1069 & 1U) != 0U)
                        {
                            opacity_0 = remapped_12.x;
                        }
                        else
                        {
                            opacity_0 = detailSource_0.x;
                        }
                        if((_S1069 & 2U) != 0U)
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
                    _S180 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (tangent_1)));
                    break;
                }
                texture2d<float, access::sample> _S1072 = (&kernelContext_0)->opacityTexture_0;
                thread uint atlasWidth_10;
                thread uint atlasHeight_10;
                (*((&atlasWidth_10)) = (_S1072).get_width(0)),(*((&atlasHeight_10)) = (_S1072).get_height(0));
                int3 _S1073 = int3(int(0), int(0), int(0));
                float4 metadata_10 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1073)).xy), uint(((_S1073)).z))) * float4(255.0f) );
                if((metadata_10.x) == 255.0f)
                {
                    uint _S1074 = atlasWidth_10;
                    uint _S1075 = 2U % atlasWidth_10;
                    int _S1076 = int(_S1075);
                    uint _S1077 = 2U / _S1074;
                    int3 _S1078 = int3(_S1076, int(_S1077), int(0));
                    int _S1079 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1078)).xy), uint(((_S1078)).z))).x * 255.0f)));
                    uint _S1080 = atlasWidth_10;
                    uint _S1081 = 3U % atlasWidth_10;
                    int _S1082 = int(_S1081);
                    uint _S1083 = 3U / _S1080;
                    int3 _S1084 = int3(_S1082, int(_S1083), int(0));
                    int2 minimumTile_10 = int2(_S1079, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1084)).xy), uint(((_S1084)).z))).x * 255.0f))));
                    uint _S1085 = atlasWidth_10;
                    uint _S1086 = 4U % atlasWidth_10;
                    int _S1087 = int(_S1086);
                    uint _S1088 = 4U / _S1085;
                    int3 _S1089 = int3(_S1087, int(_S1088), int(0));
                    uint _S1090 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1089)).xy), uint(((_S1089)).z))).x * 255.0f));
                    uint _S1091 = atlasWidth_10;
                    uint _S1092 = 5U % atlasWidth_10;
                    int _S1093 = int(_S1092);
                    uint _S1094 = 5U / _S1091;
                    int3 _S1095 = int3(_S1093, int(_S1094), int(0));
                    uint _S1096 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1095)).xy), uint(((_S1095)).z))).x * 255.0f));
                    uint2 grid_10 = uint2(_S1090, _S1096);
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
                        int3 _S1097 = int3(int(1), int(0), int(0));
                        _S180 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1097)).xy), uint(((_S1097)).z)));
                        break;
                    }
                    uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1090 + uint(tile_20.x));
                    uint _S1098 = atlasWidth_10;
                    uint _S1099 = offset_10 % atlasWidth_10;
                    int _S1100 = int(_S1099);
                    uint _S1101 = offset_10 / _S1098;
                    int3 _S1102 = int3(_S1100, int(_S1101), int(0));
                    uint _S1103 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1102)).xy), uint(((_S1102)).z))).x * 255.0f));
                    uint _S1104 = atlasWidth_10;
                    uint _S1105 = offset_10 + 1U;
                    uint _S1106 = _S1105 % atlasWidth_10;
                    int _S1107 = int(_S1106);
                    uint _S1108 = _S1105 / _S1104;
                    int3 _S1109 = int3(_S1107, int(_S1108), int(0));
                    uint slot_20 = _S1103 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1109)).xy), uint(((_S1109)).z))).x * 255.0f)) << 8U);
                    if(slot_20 == 0U)
                    {
                        int3 _S1110 = int3(int(1), int(0), int(0));
                        _S180 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1110)).xy), uint(((_S1110)).z)));
                        break;
                    }
                    uint slot_21 = slot_20 - 1U;
                    uint _S1111 = atlasWidth_10;
                    uint _S1112 = 6U % atlasWidth_10;
                    int _S1113 = int(_S1112);
                    uint _S1114 = 6U / _S1111;
                    int3 _S1115 = int3(_S1113, int(_S1114), int(0));
                    uint _S1116 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1115)).xy), uint(((_S1115)).z))).x * 255.0f));
                    uint _S1117 = atlasWidth_10;
                    uint _S1118 = 7U % atlasWidth_10;
                    int _S1119 = int(_S1118);
                    uint _S1120 = 7U / _S1117;
                    int3 _S1121 = int3(_S1119, int(_S1120), int(0));
                    uint columns_10 = _S1116 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1121)).xy), uint(((_S1121)).z))).x * 255.0f)) << 8U);
                    uint _S1122 = atlasWidth_10;
                    uint _S1123 = 8U % atlasWidth_10;
                    int _S1124 = int(_S1123);
                    uint _S1125 = 8U / _S1122;
                    int3 _S1126 = int3(_S1124, int(_S1125), int(0));
                    uint _S1127 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1126)).xy), uint(((_S1126)).z))).x * 255.0f));
                    uint _S1128 = atlasWidth_10;
                    uint _S1129 = 9U % atlasWidth_10;
                    int _S1130 = int(_S1129);
                    uint _S1131 = 9U / _S1128;
                    int3 _S1132 = int3(_S1130, int(_S1131), int(0));
                    uint rows_10 = ((_S1127 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1132)).xy), uint(((_S1132)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                    uint metadataRows_10 = (10U + 2U * _S1090 * _S1096 + atlasWidth_10 - 1U) / atlasWidth_10;
                    uint _S1133 = atlasWidth_10 / columns_10;
                    float _S1134 = float(_S1133);
                    uint _S1135 = (atlasHeight_10 - metadataRows_10) / rows_10;
                    float2 cellSize_20 = float2(_S1134, float(_S1135));
                    uint _S1136 = slot_21 % columns_10;
                    float _S1137 = float(_S1136);
                    uint _S1138 = slot_21 / columns_10;
                    _S180 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S1137, float(_S1138)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(detailSource_0) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                    break;
                }
                int2 _S1139 = int2(metadata_10.zw);
                int2 tile_21 = int2(floor(detailSource_0)) - int2(metadata_10.xy);
                if(any(tile_21 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_21 >= _S1139);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1140 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                    _S180 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1140)).xy), uint(((_S1140)).z)));
                    break;
                }
                uint _S1141 = atlasWidth_10 / uint(_S1139.x);
                float _S1142 = float(_S1141);
                uint _S1143 = (atlasHeight_10 - 1U) / uint(_S1139.y);
                float2 cellSize_21 = float2(_S1142, float(_S1143));
                _S180 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            for(;;)
            {
                float4 _S1144 = float4(_S7->compositeControls_0) ;
                if((_S1144.x) != 64.0f)
                {
                    break;
                }
                bool _S1145 = (_S1144.w) >= 0.5f;
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
                uint _S1146 = index_0 | lightCount_0;
                for(;;)
                {
                    if(!_S1145)
                    {
                        for(;;)
                        {
                            if(_S1146 == 0U)
                            {
                                break;
                            }
                            texture2d<float, access::sample> _S1147 = (&kernelContext_0)->compositeTexture_0;
                            thread uint width_17;
                            thread uint height_17;
                            (*((&width_17)) = (_S1147).get_width(0)),(*((&height_17)) = (_S1147).get_height(0));
                            float2 size_13 = float2(float(width_17), float(height_17));
                            float2 _S1148 = float2(0.5f) ;
                            float2 remapped_13 = clamp((detailSource_0 * (size_13 - float2(2.0f) ) + float2(1.0f) ) / size_13, _S1148 / size_13, (size_13 - _S1148) / size_13);
                            if((_S1146 & 1U) != 0U)
                            {
                                opacity_0 = remapped_13.x;
                            }
                            else
                            {
                                opacity_0 = detailSource_0.x;
                            }
                            if((_S1146 & 2U) != 0U)
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
                        _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                        break;
                    }
                    texture2d<float, access::sample> _S1149 = (&kernelContext_0)->compositeTexture_0;
                    thread uint atlasWidth_11;
                    thread uint atlasHeight_11;
                    (*((&atlasWidth_11)) = (_S1149).get_width(0)),(*((&atlasHeight_11)) = (_S1149).get_height(0));
                    int3 _S1150 = int3(int(0), int(0), int(0));
                    float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1150)).xy), uint(((_S1150)).z))) * float4(255.0f) );
                    if((metadata_11.x) == 255.0f)
                    {
                        uint _S1151 = atlasWidth_11;
                        uint _S1152 = 2U % atlasWidth_11;
                        int _S1153 = int(_S1152);
                        uint _S1154 = 2U / _S1151;
                        int3 _S1155 = int3(_S1153, int(_S1154), int(0));
                        int _S1156 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1155)).xy), uint(((_S1155)).z))).x * 255.0f)));
                        uint _S1157 = atlasWidth_11;
                        uint _S1158 = 3U % atlasWidth_11;
                        int _S1159 = int(_S1158);
                        uint _S1160 = 3U / _S1157;
                        int3 _S1161 = int3(_S1159, int(_S1160), int(0));
                        int2 minimumTile_11 = int2(_S1156, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1161)).xy), uint(((_S1161)).z))).x * 255.0f))));
                        uint _S1162 = atlasWidth_11;
                        uint _S1163 = 4U % atlasWidth_11;
                        int _S1164 = int(_S1163);
                        uint _S1165 = 4U / _S1162;
                        int3 _S1166 = int3(_S1164, int(_S1165), int(0));
                        uint _S1167 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1166)).xy), uint(((_S1166)).z))).x * 255.0f));
                        uint _S1168 = atlasWidth_11;
                        uint _S1169 = 5U % atlasWidth_11;
                        int _S1170 = int(_S1169);
                        uint _S1171 = 5U / _S1168;
                        int3 _S1172 = int3(_S1170, int(_S1171), int(0));
                        uint _S1173 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1172)).xy), uint(((_S1172)).z))).x * 255.0f));
                        uint2 grid_11 = uint2(_S1167, _S1173);
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
                            int3 _S1174 = int3(int(1), int(0), int(0));
                            _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1174)).xy), uint(((_S1174)).z)));
                            break;
                        }
                        uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1167 + uint(tile_22.x));
                        uint _S1175 = atlasWidth_11;
                        uint _S1176 = offset_11 % atlasWidth_11;
                        int _S1177 = int(_S1176);
                        uint _S1178 = offset_11 / _S1175;
                        int3 _S1179 = int3(_S1177, int(_S1178), int(0));
                        uint _S1180 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1179)).xy), uint(((_S1179)).z))).x * 255.0f));
                        uint _S1181 = atlasWidth_11;
                        uint _S1182 = offset_11 + 1U;
                        uint _S1183 = _S1182 % atlasWidth_11;
                        int _S1184 = int(_S1183);
                        uint _S1185 = _S1182 / _S1181;
                        int3 _S1186 = int3(_S1184, int(_S1185), int(0));
                        uint slot_22 = _S1180 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1186)).xy), uint(((_S1186)).z))).x * 255.0f)) << 8U);
                        if(slot_22 == 0U)
                        {
                            int3 _S1187 = int3(int(1), int(0), int(0));
                            _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1187)).xy), uint(((_S1187)).z)));
                            break;
                        }
                        uint slot_23 = slot_22 - 1U;
                        uint _S1188 = atlasWidth_11;
                        uint _S1189 = 6U % atlasWidth_11;
                        int _S1190 = int(_S1189);
                        uint _S1191 = 6U / _S1188;
                        int3 _S1192 = int3(_S1190, int(_S1191), int(0));
                        uint _S1193 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1192)).xy), uint(((_S1192)).z))).x * 255.0f));
                        uint _S1194 = atlasWidth_11;
                        uint _S1195 = 7U % atlasWidth_11;
                        int _S1196 = int(_S1195);
                        uint _S1197 = 7U / _S1194;
                        int3 _S1198 = int3(_S1196, int(_S1197), int(0));
                        uint columns_11 = _S1193 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1198)).xy), uint(((_S1198)).z))).x * 255.0f)) << 8U);
                        uint _S1199 = atlasWidth_11;
                        uint _S1200 = 8U % atlasWidth_11;
                        int _S1201 = int(_S1200);
                        uint _S1202 = 8U / _S1199;
                        int3 _S1203 = int3(_S1201, int(_S1202), int(0));
                        uint _S1204 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1203)).xy), uint(((_S1203)).z))).x * 255.0f));
                        uint _S1205 = atlasWidth_11;
                        uint _S1206 = 9U % atlasWidth_11;
                        int _S1207 = int(_S1206);
                        uint _S1208 = 9U / _S1205;
                        int3 _S1209 = int3(_S1207, int(_S1208), int(0));
                        uint rows_11 = ((_S1204 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1209)).xy), uint(((_S1209)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                        uint metadataRows_11 = (10U + 2U * _S1167 * _S1173 + atlasWidth_11 - 1U) / atlasWidth_11;
                        uint _S1210 = atlasWidth_11 / columns_11;
                        float _S1211 = float(_S1210);
                        uint _S1212 = (atlasHeight_11 - metadataRows_11) / rows_11;
                        float2 cellSize_22 = float2(_S1211, float(_S1212));
                        uint _S1213 = slot_23 % columns_11;
                        float _S1214 = float(_S1213);
                        uint _S1215 = slot_23 / columns_11;
                        _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1214, float(_S1215)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(detailSource_0) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                        break;
                    }
                    int2 _S1216 = int2(metadata_11.zw);
                    int2 tile_23 = int2(floor(detailSource_0)) - int2(metadata_11.xy);
                    if(any(tile_23 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_23 >= _S1216);
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1217 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1217)).xy), uint(((_S1217)).z)));
                        break;
                    }
                    uint _S1218 = atlasWidth_11 / uint(_S1216.x);
                    float _S1219 = float(_S1218);
                    uint _S1220 = (atlasHeight_11 - 1U) / uint(_S1216.y);
                    float2 cellSize_23 = float2(_S1219, float(_S1220));
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                uint operation_4 = uint(round(_S1144.y));
                if(operation_4 == 1U)
                {
                    _S180 = _S180 * _S579;
                    break;
                }
                if(operation_4 == 2U)
                {
                    _S180 = _S180 + _S579;
                    break;
                }
                if(operation_4 == 3U)
                {
                    _S180 = _S180 - _S579;
                    break;
                }
                if(operation_4 == 4U)
                {
                    float factor_4 = _S1144.z;
                    _S180 = _S180 * float4((1.0f - factor_4))  + _S579 * float4(factor_4) ;
                    break;
                }
                break;
            }
            _S4 = saturate(_S180.x);
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
                detailSource_0 = _S179;
            }
            else
            {
                detailSource_0 = _S178;
            }
        }
        else
        {
            if(useSecondary_6)
            {
                detailSource_0 = _S171;
            }
            else
            {
                detailSource_0 = _S170;
            }
        }
        bool _S1221 = (udimMask_0 & 128U) != 0U;
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
        uint _S1222 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1221)
            {
                for(;;)
                {
                    if(_S1222 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1223 = (&kernelContext_0)->occlusionTexture_0;
                    thread uint width_18;
                    thread uint height_18;
                    (*((&width_18)) = (_S1223).get_width(0)),(*((&height_18)) = (_S1223).get_height(0));
                    float2 size_14 = float2(float(width_18), float(height_18));
                    float2 _S1224 = float2(0.5f) ;
                    float2 remapped_14 = clamp((detailSource_0 * (size_14 - float2(2.0f) ) + float2(1.0f) ) / size_14, _S1224 / size_14, (size_14 - _S1224) / size_14);
                    if((_S1222 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_14.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S1222 & 2U) != 0U)
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
                _S180 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1225 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1225).get_width(0)),(*((&atlasHeight_12)) = (_S1225).get_height(0));
            int3 _S1226 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1226)).xy), uint(((_S1226)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1227 = atlasWidth_12;
                uint _S1228 = 2U % atlasWidth_12;
                int _S1229 = int(_S1228);
                uint _S1230 = 2U / _S1227;
                int3 _S1231 = int3(_S1229, int(_S1230), int(0));
                int _S1232 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1231)).xy), uint(((_S1231)).z))).x * 255.0f)));
                uint _S1233 = atlasWidth_12;
                uint _S1234 = 3U % atlasWidth_12;
                int _S1235 = int(_S1234);
                uint _S1236 = 3U / _S1233;
                int3 _S1237 = int3(_S1235, int(_S1236), int(0));
                int2 minimumTile_12 = int2(_S1232, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1237)).xy), uint(((_S1237)).z))).x * 255.0f))));
                uint _S1238 = atlasWidth_12;
                uint _S1239 = 4U % atlasWidth_12;
                int _S1240 = int(_S1239);
                uint _S1241 = 4U / _S1238;
                int3 _S1242 = int3(_S1240, int(_S1241), int(0));
                uint _S1243 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1242)).xy), uint(((_S1242)).z))).x * 255.0f));
                uint _S1244 = atlasWidth_12;
                uint _S1245 = 5U % atlasWidth_12;
                int _S1246 = int(_S1245);
                uint _S1247 = 5U / _S1244;
                int3 _S1248 = int3(_S1246, int(_S1247), int(0));
                uint _S1249 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1248)).xy), uint(((_S1248)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1243, _S1249);
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
                    int3 _S1250 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1250)).xy), uint(((_S1250)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1243 + uint(tile_24.x));
                uint _S1251 = atlasWidth_12;
                uint _S1252 = offset_12 % atlasWidth_12;
                int _S1253 = int(_S1252);
                uint _S1254 = offset_12 / _S1251;
                int3 _S1255 = int3(_S1253, int(_S1254), int(0));
                uint _S1256 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1255)).xy), uint(((_S1255)).z))).x * 255.0f));
                uint _S1257 = atlasWidth_12;
                uint _S1258 = offset_12 + 1U;
                uint _S1259 = _S1258 % atlasWidth_12;
                int _S1260 = int(_S1259);
                uint _S1261 = _S1258 / _S1257;
                int3 _S1262 = int3(_S1260, int(_S1261), int(0));
                uint slot_24 = _S1256 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1262)).xy), uint(((_S1262)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1263 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1263)).xy), uint(((_S1263)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1264 = atlasWidth_12;
                uint _S1265 = 6U % atlasWidth_12;
                int _S1266 = int(_S1265);
                uint _S1267 = 6U / _S1264;
                int3 _S1268 = int3(_S1266, int(_S1267), int(0));
                uint _S1269 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1268)).xy), uint(((_S1268)).z))).x * 255.0f));
                uint _S1270 = atlasWidth_12;
                uint _S1271 = 7U % atlasWidth_12;
                int _S1272 = int(_S1271);
                uint _S1273 = 7U / _S1270;
                int3 _S1274 = int3(_S1272, int(_S1273), int(0));
                uint columns_12 = _S1269 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1274)).xy), uint(((_S1274)).z))).x * 255.0f)) << 8U);
                uint _S1275 = atlasWidth_12;
                uint _S1276 = 8U % atlasWidth_12;
                int _S1277 = int(_S1276);
                uint _S1278 = 8U / _S1275;
                int3 _S1279 = int3(_S1277, int(_S1278), int(0));
                uint _S1280 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1279)).xy), uint(((_S1279)).z))).x * 255.0f));
                uint _S1281 = atlasWidth_12;
                uint _S1282 = 9U % atlasWidth_12;
                int _S1283 = int(_S1282);
                uint _S1284 = 9U / _S1281;
                int3 _S1285 = int3(_S1283, int(_S1284), int(0));
                uint rows_12 = ((_S1280 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1285)).xy), uint(((_S1285)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1243 * _S1249 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1286 = atlasWidth_12 / columns_12;
                float _S1287 = float(_S1286);
                uint _S1288 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1287, float(_S1288));
                uint _S1289 = slot_25 % columns_12;
                float _S1290 = float(_S1289);
                uint _S1291 = slot_25 / columns_12;
                _S180 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1290, float(_S1291)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(detailSource_0) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1292 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(detailSource_0)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1292);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1293 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S180 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1293)).xy), uint(((_S1293)).z)));
                break;
            }
            uint _S1294 = atlasWidth_12 / uint(_S1292.x);
            float _S1295 = float(_S1294);
            uint _S1296 = (atlasHeight_12 - 1U) / uint(_S1292.y);
            float2 cellSize_25 = float2(_S1295, float(_S1296));
            _S180 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1297 = float4(_S7->compositeControls_0) ;
            if((_S1297.x) != 128.0f)
            {
                break;
            }
            bool _S1298 = (_S1297.w) >= 0.5f;
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
            uint _S1299 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1298)
                {
                    for(;;)
                    {
                        if(_S1299 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1300 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_19;
                        thread uint height_19;
                        (*((&width_19)) = (_S1300).get_width(0)),(*((&height_19)) = (_S1300).get_height(0));
                        float2 size_15 = float2(float(width_19), float(height_19));
                        float2 _S1301 = float2(0.5f) ;
                        float2 remapped_15 = clamp((detailSource_0 * (size_15 - float2(2.0f) ) + float2(1.0f) ) / size_15, _S1301 / size_15, (size_15 - _S1301) / size_15);
                        if((_S1299 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_15.x;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.x;
                        }
                        if((_S1299 & 2U) != 0U)
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
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1302 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1302).get_width(0)),(*((&atlasHeight_13)) = (_S1302).get_height(0));
                int3 _S1303 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1303)).xy), uint(((_S1303)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1304 = atlasWidth_13;
                    uint _S1305 = 2U % atlasWidth_13;
                    int _S1306 = int(_S1305);
                    uint _S1307 = 2U / _S1304;
                    int3 _S1308 = int3(_S1306, int(_S1307), int(0));
                    int _S1309 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1308)).xy), uint(((_S1308)).z))).x * 255.0f)));
                    uint _S1310 = atlasWidth_13;
                    uint _S1311 = 3U % atlasWidth_13;
                    int _S1312 = int(_S1311);
                    uint _S1313 = 3U / _S1310;
                    int3 _S1314 = int3(_S1312, int(_S1313), int(0));
                    int2 minimumTile_13 = int2(_S1309, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1314)).xy), uint(((_S1314)).z))).x * 255.0f))));
                    uint _S1315 = atlasWidth_13;
                    uint _S1316 = 4U % atlasWidth_13;
                    int _S1317 = int(_S1316);
                    uint _S1318 = 4U / _S1315;
                    int3 _S1319 = int3(_S1317, int(_S1318), int(0));
                    uint _S1320 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1319)).xy), uint(((_S1319)).z))).x * 255.0f));
                    uint _S1321 = atlasWidth_13;
                    uint _S1322 = 5U % atlasWidth_13;
                    int _S1323 = int(_S1322);
                    uint _S1324 = 5U / _S1321;
                    int3 _S1325 = int3(_S1323, int(_S1324), int(0));
                    uint _S1326 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1325)).xy), uint(((_S1325)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1320, _S1326);
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
                        int3 _S1327 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1327)).xy), uint(((_S1327)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1320 + uint(tile_26.x));
                    uint _S1328 = atlasWidth_13;
                    uint _S1329 = offset_13 % atlasWidth_13;
                    int _S1330 = int(_S1329);
                    uint _S1331 = offset_13 / _S1328;
                    int3 _S1332 = int3(_S1330, int(_S1331), int(0));
                    uint _S1333 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1332)).xy), uint(((_S1332)).z))).x * 255.0f));
                    uint _S1334 = atlasWidth_13;
                    uint _S1335 = offset_13 + 1U;
                    uint _S1336 = _S1335 % atlasWidth_13;
                    int _S1337 = int(_S1336);
                    uint _S1338 = _S1335 / _S1334;
                    int3 _S1339 = int3(_S1337, int(_S1338), int(0));
                    uint slot_26 = _S1333 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1339)).xy), uint(((_S1339)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1340 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1340)).xy), uint(((_S1340)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1341 = atlasWidth_13;
                    uint _S1342 = 6U % atlasWidth_13;
                    int _S1343 = int(_S1342);
                    uint _S1344 = 6U / _S1341;
                    int3 _S1345 = int3(_S1343, int(_S1344), int(0));
                    uint _S1346 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1345)).xy), uint(((_S1345)).z))).x * 255.0f));
                    uint _S1347 = atlasWidth_13;
                    uint _S1348 = 7U % atlasWidth_13;
                    int _S1349 = int(_S1348);
                    uint _S1350 = 7U / _S1347;
                    int3 _S1351 = int3(_S1349, int(_S1350), int(0));
                    uint columns_13 = _S1346 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1351)).xy), uint(((_S1351)).z))).x * 255.0f)) << 8U);
                    uint _S1352 = atlasWidth_13;
                    uint _S1353 = 8U % atlasWidth_13;
                    int _S1354 = int(_S1353);
                    uint _S1355 = 8U / _S1352;
                    int3 _S1356 = int3(_S1354, int(_S1355), int(0));
                    uint _S1357 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1356)).xy), uint(((_S1356)).z))).x * 255.0f));
                    uint _S1358 = atlasWidth_13;
                    uint _S1359 = 9U % atlasWidth_13;
                    int _S1360 = int(_S1359);
                    uint _S1361 = 9U / _S1358;
                    int3 _S1362 = int3(_S1360, int(_S1361), int(0));
                    uint rows_13 = ((_S1357 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1362)).xy), uint(((_S1362)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1320 * _S1326 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1363 = atlasWidth_13 / columns_13;
                    float _S1364 = float(_S1363);
                    uint _S1365 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1364, float(_S1365));
                    uint _S1366 = slot_27 % columns_13;
                    float _S1367 = float(_S1366);
                    uint _S1368 = slot_27 / columns_13;
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1367, float(_S1368)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(detailSource_0) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1369 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(detailSource_0)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1369);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1370 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1370)).xy), uint(((_S1370)).z)));
                    break;
                }
                uint _S1371 = atlasWidth_13 / uint(_S1369.x);
                float _S1372 = float(_S1371);
                uint _S1373 = (atlasHeight_13 - 1U) / uint(_S1369.y);
                float2 cellSize_27 = float2(_S1372, float(_S1373));
                _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_5 = uint(round(_S1297.y));
            if(operation_5 == 1U)
            {
                _S180 = _S180 * _S579;
                break;
            }
            if(operation_5 == 2U)
            {
                _S180 = _S180 + _S579;
                break;
            }
            if(operation_5 == 3U)
            {
                _S180 = _S180 - _S579;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1297.z;
                _S180 = _S180 * float4((1.0f - factor_5))  + _S579 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S180.x);
    }
    else
    {
        occlusion_0 = _S150;
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
                detailSource_0 = _S179;
            }
            else
            {
                detailSource_0 = _S178;
            }
        }
        else
        {
            if(useSecondary_7)
            {
                detailSource_0 = _S171;
            }
            else
            {
                detailSource_0 = _S170;
            }
        }
        bool _S1374 = (udimMask_0 & 256U) != 0U;
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
        uint _S1375 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1374)
            {
                for(;;)
                {
                    if(_S1375 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1376 = (&kernelContext_0)->specularColorTexture_0;
                    thread uint width_20;
                    thread uint height_20;
                    (*((&width_20)) = (_S1376).get_width(0)),(*((&height_20)) = (_S1376).get_height(0));
                    float2 size_16 = float2(float(width_20), float(height_20));
                    float2 _S1377 = float2(0.5f) ;
                    float2 remapped_16 = clamp((detailSource_0 * (size_16 - float2(2.0f) ) + float2(1.0f) ) / size_16, _S1377 / size_16, (size_16 - _S1377) / size_16);
                    if((_S1375 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_16.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.x;
                    }
                    if((_S1375 & 2U) != 0U)
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
                _S180 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1378 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1378).get_width(0)),(*((&atlasHeight_14)) = (_S1378).get_height(0));
            int3 _S1379 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1379)).xy), uint(((_S1379)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1380 = atlasWidth_14;
                uint _S1381 = 2U % atlasWidth_14;
                int _S1382 = int(_S1381);
                uint _S1383 = 2U / _S1380;
                int3 _S1384 = int3(_S1382, int(_S1383), int(0));
                int _S1385 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1384)).xy), uint(((_S1384)).z))).x * 255.0f)));
                uint _S1386 = atlasWidth_14;
                uint _S1387 = 3U % atlasWidth_14;
                int _S1388 = int(_S1387);
                uint _S1389 = 3U / _S1386;
                int3 _S1390 = int3(_S1388, int(_S1389), int(0));
                int2 minimumTile_14 = int2(_S1385, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1390)).xy), uint(((_S1390)).z))).x * 255.0f))));
                uint _S1391 = atlasWidth_14;
                uint _S1392 = 4U % atlasWidth_14;
                int _S1393 = int(_S1392);
                uint _S1394 = 4U / _S1391;
                int3 _S1395 = int3(_S1393, int(_S1394), int(0));
                uint _S1396 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1395)).xy), uint(((_S1395)).z))).x * 255.0f));
                uint _S1397 = atlasWidth_14;
                uint _S1398 = 5U % atlasWidth_14;
                int _S1399 = int(_S1398);
                uint _S1400 = 5U / _S1397;
                int3 _S1401 = int3(_S1399, int(_S1400), int(0));
                uint _S1402 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1401)).xy), uint(((_S1401)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1396, _S1402);
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
                    int3 _S1403 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1403)).xy), uint(((_S1403)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1396 + uint(tile_28.x));
                uint _S1404 = atlasWidth_14;
                uint _S1405 = offset_14 % atlasWidth_14;
                int _S1406 = int(_S1405);
                uint _S1407 = offset_14 / _S1404;
                int3 _S1408 = int3(_S1406, int(_S1407), int(0));
                uint _S1409 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1408)).xy), uint(((_S1408)).z))).x * 255.0f));
                uint _S1410 = atlasWidth_14;
                uint _S1411 = offset_14 + 1U;
                uint _S1412 = _S1411 % atlasWidth_14;
                int _S1413 = int(_S1412);
                uint _S1414 = _S1411 / _S1410;
                int3 _S1415 = int3(_S1413, int(_S1414), int(0));
                uint slot_28 = _S1409 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1415)).xy), uint(((_S1415)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1416 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1416)).xy), uint(((_S1416)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1417 = atlasWidth_14;
                uint _S1418 = 6U % atlasWidth_14;
                int _S1419 = int(_S1418);
                uint _S1420 = 6U / _S1417;
                int3 _S1421 = int3(_S1419, int(_S1420), int(0));
                uint _S1422 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1421)).xy), uint(((_S1421)).z))).x * 255.0f));
                uint _S1423 = atlasWidth_14;
                uint _S1424 = 7U % atlasWidth_14;
                int _S1425 = int(_S1424);
                uint _S1426 = 7U / _S1423;
                int3 _S1427 = int3(_S1425, int(_S1426), int(0));
                uint columns_14 = _S1422 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1427)).xy), uint(((_S1427)).z))).x * 255.0f)) << 8U);
                uint _S1428 = atlasWidth_14;
                uint _S1429 = 8U % atlasWidth_14;
                int _S1430 = int(_S1429);
                uint _S1431 = 8U / _S1428;
                int3 _S1432 = int3(_S1430, int(_S1431), int(0));
                uint _S1433 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1432)).xy), uint(((_S1432)).z))).x * 255.0f));
                uint _S1434 = atlasWidth_14;
                uint _S1435 = 9U % atlasWidth_14;
                int _S1436 = int(_S1435);
                uint _S1437 = 9U / _S1434;
                int3 _S1438 = int3(_S1436, int(_S1437), int(0));
                uint rows_14 = ((_S1433 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1438)).xy), uint(((_S1438)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1396 * _S1402 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1439 = atlasWidth_14 / columns_14;
                float _S1440 = float(_S1439);
                uint _S1441 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1440, float(_S1441));
                uint _S1442 = slot_29 % columns_14;
                float _S1443 = float(_S1442);
                uint _S1444 = slot_29 / columns_14;
                _S180 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1443, float(_S1444)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(detailSource_0) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1445 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(detailSource_0)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1445);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1446 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S180 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1446)).xy), uint(((_S1446)).z)));
                break;
            }
            uint _S1447 = atlasWidth_14 / uint(_S1445.x);
            float _S1448 = float(_S1447);
            uint _S1449 = (atlasHeight_14 - 1U) / uint(_S1445.y);
            float2 cellSize_29 = float2(_S1448, float(_S1449));
            _S180 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1450 = float4(_S7->compositeControls_0) ;
            if((_S1450.x) != 256.0f)
            {
                break;
            }
            bool _S1451 = (_S1450.w) >= 0.5f;
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
            uint _S1452 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1451)
                {
                    for(;;)
                    {
                        if(_S1452 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1453 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_21;
                        thread uint height_21;
                        (*((&width_21)) = (_S1453).get_width(0)),(*((&height_21)) = (_S1453).get_height(0));
                        float2 size_17 = float2(float(width_21), float(height_21));
                        float2 _S1454 = float2(0.5f) ;
                        float2 remapped_17 = clamp((detailSource_0 * (size_17 - float2(2.0f) ) + float2(1.0f) ) / size_17, _S1454 / size_17, (size_17 - _S1454) / size_17);
                        if((_S1452 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_17.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.x;
                        }
                        if((_S1452 & 2U) != 0U)
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
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1455 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1455).get_width(0)),(*((&atlasHeight_15)) = (_S1455).get_height(0));
                int3 _S1456 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1456)).xy), uint(((_S1456)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1457 = atlasWidth_15;
                    uint _S1458 = 2U % atlasWidth_15;
                    int _S1459 = int(_S1458);
                    uint _S1460 = 2U / _S1457;
                    int3 _S1461 = int3(_S1459, int(_S1460), int(0));
                    int _S1462 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1461)).xy), uint(((_S1461)).z))).x * 255.0f)));
                    uint _S1463 = atlasWidth_15;
                    uint _S1464 = 3U % atlasWidth_15;
                    int _S1465 = int(_S1464);
                    uint _S1466 = 3U / _S1463;
                    int3 _S1467 = int3(_S1465, int(_S1466), int(0));
                    int2 minimumTile_15 = int2(_S1462, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1467)).xy), uint(((_S1467)).z))).x * 255.0f))));
                    uint _S1468 = atlasWidth_15;
                    uint _S1469 = 4U % atlasWidth_15;
                    int _S1470 = int(_S1469);
                    uint _S1471 = 4U / _S1468;
                    int3 _S1472 = int3(_S1470, int(_S1471), int(0));
                    uint _S1473 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1472)).xy), uint(((_S1472)).z))).x * 255.0f));
                    uint _S1474 = atlasWidth_15;
                    uint _S1475 = 5U % atlasWidth_15;
                    int _S1476 = int(_S1475);
                    uint _S1477 = 5U / _S1474;
                    int3 _S1478 = int3(_S1476, int(_S1477), int(0));
                    uint _S1479 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1478)).xy), uint(((_S1478)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1473, _S1479);
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
                        int3 _S1480 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1480)).xy), uint(((_S1480)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1473 + uint(tile_30.x));
                    uint _S1481 = atlasWidth_15;
                    uint _S1482 = offset_15 % atlasWidth_15;
                    int _S1483 = int(_S1482);
                    uint _S1484 = offset_15 / _S1481;
                    int3 _S1485 = int3(_S1483, int(_S1484), int(0));
                    uint _S1486 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1485)).xy), uint(((_S1485)).z))).x * 255.0f));
                    uint _S1487 = atlasWidth_15;
                    uint _S1488 = offset_15 + 1U;
                    uint _S1489 = _S1488 % atlasWidth_15;
                    int _S1490 = int(_S1489);
                    uint _S1491 = _S1488 / _S1487;
                    int3 _S1492 = int3(_S1490, int(_S1491), int(0));
                    uint slot_30 = _S1486 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1492)).xy), uint(((_S1492)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1493 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1493)).xy), uint(((_S1493)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1494 = atlasWidth_15;
                    uint _S1495 = 6U % atlasWidth_15;
                    int _S1496 = int(_S1495);
                    uint _S1497 = 6U / _S1494;
                    int3 _S1498 = int3(_S1496, int(_S1497), int(0));
                    uint _S1499 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1498)).xy), uint(((_S1498)).z))).x * 255.0f));
                    uint _S1500 = atlasWidth_15;
                    uint _S1501 = 7U % atlasWidth_15;
                    int _S1502 = int(_S1501);
                    uint _S1503 = 7U / _S1500;
                    int3 _S1504 = int3(_S1502, int(_S1503), int(0));
                    uint columns_15 = _S1499 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1504)).xy), uint(((_S1504)).z))).x * 255.0f)) << 8U);
                    uint _S1505 = atlasWidth_15;
                    uint _S1506 = 8U % atlasWidth_15;
                    int _S1507 = int(_S1506);
                    uint _S1508 = 8U / _S1505;
                    int3 _S1509 = int3(_S1507, int(_S1508), int(0));
                    uint _S1510 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1509)).xy), uint(((_S1509)).z))).x * 255.0f));
                    uint _S1511 = atlasWidth_15;
                    uint _S1512 = 9U % atlasWidth_15;
                    int _S1513 = int(_S1512);
                    uint _S1514 = 9U / _S1511;
                    int3 _S1515 = int3(_S1513, int(_S1514), int(0));
                    uint rows_15 = ((_S1510 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1515)).xy), uint(((_S1515)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1473 * _S1479 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1516 = atlasWidth_15 / columns_15;
                    float _S1517 = float(_S1516);
                    uint _S1518 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1517, float(_S1518));
                    uint _S1519 = slot_31 % columns_15;
                    float _S1520 = float(_S1519);
                    uint _S1521 = slot_31 / columns_15;
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1520, float(_S1521)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(detailSource_0) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1522 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(detailSource_0)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1522);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1523 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1523)).xy), uint(((_S1523)).z)));
                    break;
                }
                uint _S1524 = atlasWidth_15 / uint(_S1522.x);
                float _S1525 = float(_S1524);
                uint _S1526 = (atlasHeight_15 - 1U) / uint(_S1522.y);
                float2 cellSize_31 = float2(_S1525, float(_S1526));
                _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_6 = uint(round(_S1450.y));
            if(operation_6 == 1U)
            {
                _S180 = _S180 * _S579;
                break;
            }
            if(operation_6 == 2U)
            {
                _S180 = _S180 + _S579;
                break;
            }
            if(operation_6 == 3U)
            {
                _S180 = _S180 - _S579;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1450.z;
                _S180 = _S180 * float4((1.0f - factor_6))  + _S579 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S180.xyz);
    }
    else
    {
        specularColor_0 = _S158;
    }
    if((textureMask_0 & 512U) != 0U)
    {
        bool useSecondary_8 = (secondaryMask_0 & 512U) != 0U;
        if((alternateMask_0 & 512U) != 0U)
        {
            if(useSecondary_8)
            {
                detailSource_0 = _S179;
            }
            else
            {
                detailSource_0 = _S178;
            }
        }
        else
        {
            if(useSecondary_8)
            {
                detailSource_0 = _S171;
            }
            else
            {
                detailSource_0 = _S170;
            }
        }
        bool _S1527 = (udimMask_0 & 512U) != 0U;
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
        uint _S1528 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1527)
            {
                for(;;)
                {
                    if(_S1528 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1529 = (&kernelContext_0)->clearcoatTexture_0;
                    thread uint width_22;
                    thread uint height_22;
                    (*((&width_22)) = (_S1529).get_width(0)),(*((&height_22)) = (_S1529).get_height(0));
                    float2 size_18 = float2(float(width_22), float(height_22));
                    float2 _S1530 = float2(0.5f) ;
                    float2 remapped_18 = clamp((detailSource_0 * (size_18 - float2(2.0f) ) + float2(1.0f) ) / size_18, _S1530 / size_18, (size_18 - _S1530) / size_18);
                    if((_S1528 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_18.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.x;
                    }
                    if((_S1528 & 2U) != 0U)
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
                _S180 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1531 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1531).get_width(0)),(*((&atlasHeight_16)) = (_S1531).get_height(0));
            int3 _S1532 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1532)).xy), uint(((_S1532)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1533 = atlasWidth_16;
                uint _S1534 = 2U % atlasWidth_16;
                int _S1535 = int(_S1534);
                uint _S1536 = 2U / _S1533;
                int3 _S1537 = int3(_S1535, int(_S1536), int(0));
                int _S1538 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1537)).xy), uint(((_S1537)).z))).x * 255.0f)));
                uint _S1539 = atlasWidth_16;
                uint _S1540 = 3U % atlasWidth_16;
                int _S1541 = int(_S1540);
                uint _S1542 = 3U / _S1539;
                int3 _S1543 = int3(_S1541, int(_S1542), int(0));
                int2 minimumTile_16 = int2(_S1538, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1543)).xy), uint(((_S1543)).z))).x * 255.0f))));
                uint _S1544 = atlasWidth_16;
                uint _S1545 = 4U % atlasWidth_16;
                int _S1546 = int(_S1545);
                uint _S1547 = 4U / _S1544;
                int3 _S1548 = int3(_S1546, int(_S1547), int(0));
                uint _S1549 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1548)).xy), uint(((_S1548)).z))).x * 255.0f));
                uint _S1550 = atlasWidth_16;
                uint _S1551 = 5U % atlasWidth_16;
                int _S1552 = int(_S1551);
                uint _S1553 = 5U / _S1550;
                int3 _S1554 = int3(_S1552, int(_S1553), int(0));
                uint _S1555 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1554)).xy), uint(((_S1554)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1549, _S1555);
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
                    int3 _S1556 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1556)).xy), uint(((_S1556)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1549 + uint(tile_32.x));
                uint _S1557 = atlasWidth_16;
                uint _S1558 = offset_16 % atlasWidth_16;
                int _S1559 = int(_S1558);
                uint _S1560 = offset_16 / _S1557;
                int3 _S1561 = int3(_S1559, int(_S1560), int(0));
                uint _S1562 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1561)).xy), uint(((_S1561)).z))).x * 255.0f));
                uint _S1563 = atlasWidth_16;
                uint _S1564 = offset_16 + 1U;
                uint _S1565 = _S1564 % atlasWidth_16;
                int _S1566 = int(_S1565);
                uint _S1567 = _S1564 / _S1563;
                int3 _S1568 = int3(_S1566, int(_S1567), int(0));
                uint slot_32 = _S1562 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1568)).xy), uint(((_S1568)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1569 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1569)).xy), uint(((_S1569)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1570 = atlasWidth_16;
                uint _S1571 = 6U % atlasWidth_16;
                int _S1572 = int(_S1571);
                uint _S1573 = 6U / _S1570;
                int3 _S1574 = int3(_S1572, int(_S1573), int(0));
                uint _S1575 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1574)).xy), uint(((_S1574)).z))).x * 255.0f));
                uint _S1576 = atlasWidth_16;
                uint _S1577 = 7U % atlasWidth_16;
                int _S1578 = int(_S1577);
                uint _S1579 = 7U / _S1576;
                int3 _S1580 = int3(_S1578, int(_S1579), int(0));
                uint columns_16 = _S1575 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1580)).xy), uint(((_S1580)).z))).x * 255.0f)) << 8U);
                uint _S1581 = atlasWidth_16;
                uint _S1582 = 8U % atlasWidth_16;
                int _S1583 = int(_S1582);
                uint _S1584 = 8U / _S1581;
                int3 _S1585 = int3(_S1583, int(_S1584), int(0));
                uint _S1586 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1585)).xy), uint(((_S1585)).z))).x * 255.0f));
                uint _S1587 = atlasWidth_16;
                uint _S1588 = 9U % atlasWidth_16;
                int _S1589 = int(_S1588);
                uint _S1590 = 9U / _S1587;
                int3 _S1591 = int3(_S1589, int(_S1590), int(0));
                uint rows_16 = ((_S1586 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1591)).xy), uint(((_S1591)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1549 * _S1555 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1592 = atlasWidth_16 / columns_16;
                float _S1593 = float(_S1592);
                uint _S1594 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1593, float(_S1594));
                uint _S1595 = slot_33 % columns_16;
                float _S1596 = float(_S1595);
                uint _S1597 = slot_33 / columns_16;
                _S180 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1596, float(_S1597)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(detailSource_0) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1598 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(detailSource_0)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1598);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1599 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S180 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1599)).xy), uint(((_S1599)).z)));
                break;
            }
            uint _S1600 = atlasWidth_16 / uint(_S1598.x);
            float _S1601 = float(_S1600);
            uint _S1602 = (atlasHeight_16 - 1U) / uint(_S1598.y);
            float2 cellSize_33 = float2(_S1601, float(_S1602));
            _S180 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1603 = float4(_S7->compositeControls_0) ;
            if((_S1603.x) != 512.0f)
            {
                break;
            }
            bool _S1604 = (_S1603.w) >= 0.5f;
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
            uint _S1605 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1604)
                {
                    for(;;)
                    {
                        if(_S1605 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1606 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_23;
                        thread uint height_23;
                        (*((&width_23)) = (_S1606).get_width(0)),(*((&height_23)) = (_S1606).get_height(0));
                        float2 size_19 = float2(float(width_23), float(height_23));
                        float2 _S1607 = float2(0.5f) ;
                        float2 remapped_19 = clamp((detailSource_0 * (size_19 - float2(2.0f) ) + float2(1.0f) ) / size_19, _S1607 / size_19, (size_19 - _S1607) / size_19);
                        if((_S1605 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_19.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.x;
                        }
                        if((_S1605 & 2U) != 0U)
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
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1608 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1608).get_width(0)),(*((&atlasHeight_17)) = (_S1608).get_height(0));
                int3 _S1609 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1609)).xy), uint(((_S1609)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1610 = atlasWidth_17;
                    uint _S1611 = 2U % atlasWidth_17;
                    int _S1612 = int(_S1611);
                    uint _S1613 = 2U / _S1610;
                    int3 _S1614 = int3(_S1612, int(_S1613), int(0));
                    int _S1615 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1614)).xy), uint(((_S1614)).z))).x * 255.0f)));
                    uint _S1616 = atlasWidth_17;
                    uint _S1617 = 3U % atlasWidth_17;
                    int _S1618 = int(_S1617);
                    uint _S1619 = 3U / _S1616;
                    int3 _S1620 = int3(_S1618, int(_S1619), int(0));
                    int2 minimumTile_17 = int2(_S1615, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1620)).xy), uint(((_S1620)).z))).x * 255.0f))));
                    uint _S1621 = atlasWidth_17;
                    uint _S1622 = 4U % atlasWidth_17;
                    int _S1623 = int(_S1622);
                    uint _S1624 = 4U / _S1621;
                    int3 _S1625 = int3(_S1623, int(_S1624), int(0));
                    uint _S1626 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1625)).xy), uint(((_S1625)).z))).x * 255.0f));
                    uint _S1627 = atlasWidth_17;
                    uint _S1628 = 5U % atlasWidth_17;
                    int _S1629 = int(_S1628);
                    uint _S1630 = 5U / _S1627;
                    int3 _S1631 = int3(_S1629, int(_S1630), int(0));
                    uint _S1632 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1631)).xy), uint(((_S1631)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1626, _S1632);
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
                        int3 _S1633 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1633)).xy), uint(((_S1633)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1626 + uint(tile_34.x));
                    uint _S1634 = atlasWidth_17;
                    uint _S1635 = offset_17 % atlasWidth_17;
                    int _S1636 = int(_S1635);
                    uint _S1637 = offset_17 / _S1634;
                    int3 _S1638 = int3(_S1636, int(_S1637), int(0));
                    uint _S1639 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1638)).xy), uint(((_S1638)).z))).x * 255.0f));
                    uint _S1640 = atlasWidth_17;
                    uint _S1641 = offset_17 + 1U;
                    uint _S1642 = _S1641 % atlasWidth_17;
                    int _S1643 = int(_S1642);
                    uint _S1644 = _S1641 / _S1640;
                    int3 _S1645 = int3(_S1643, int(_S1644), int(0));
                    uint slot_34 = _S1639 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1645)).xy), uint(((_S1645)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1646 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1646)).xy), uint(((_S1646)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1647 = atlasWidth_17;
                    uint _S1648 = 6U % atlasWidth_17;
                    int _S1649 = int(_S1648);
                    uint _S1650 = 6U / _S1647;
                    int3 _S1651 = int3(_S1649, int(_S1650), int(0));
                    uint _S1652 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1651)).xy), uint(((_S1651)).z))).x * 255.0f));
                    uint _S1653 = atlasWidth_17;
                    uint _S1654 = 7U % atlasWidth_17;
                    int _S1655 = int(_S1654);
                    uint _S1656 = 7U / _S1653;
                    int3 _S1657 = int3(_S1655, int(_S1656), int(0));
                    uint columns_17 = _S1652 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1657)).xy), uint(((_S1657)).z))).x * 255.0f)) << 8U);
                    uint _S1658 = atlasWidth_17;
                    uint _S1659 = 8U % atlasWidth_17;
                    int _S1660 = int(_S1659);
                    uint _S1661 = 8U / _S1658;
                    int3 _S1662 = int3(_S1660, int(_S1661), int(0));
                    uint _S1663 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1662)).xy), uint(((_S1662)).z))).x * 255.0f));
                    uint _S1664 = atlasWidth_17;
                    uint _S1665 = 9U % atlasWidth_17;
                    int _S1666 = int(_S1665);
                    uint _S1667 = 9U / _S1664;
                    int3 _S1668 = int3(_S1666, int(_S1667), int(0));
                    uint rows_17 = ((_S1663 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1668)).xy), uint(((_S1668)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1626 * _S1632 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1669 = atlasWidth_17 / columns_17;
                    float _S1670 = float(_S1669);
                    uint _S1671 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1670, float(_S1671));
                    uint _S1672 = slot_35 % columns_17;
                    float _S1673 = float(_S1672);
                    uint _S1674 = slot_35 / columns_17;
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1673, float(_S1674)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(detailSource_0) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1675 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(detailSource_0)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1675);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1676 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1676)).xy), uint(((_S1676)).z)));
                    break;
                }
                uint _S1677 = atlasWidth_17 / uint(_S1675.x);
                float _S1678 = float(_S1677);
                uint _S1679 = (atlasHeight_17 - 1U) / uint(_S1675.y);
                float2 cellSize_35 = float2(_S1678, float(_S1679));
                _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_7 = uint(round(_S1603.y));
            if(operation_7 == 1U)
            {
                _S180 = _S180 * _S579;
                break;
            }
            if(operation_7 == 2U)
            {
                _S180 = _S180 + _S579;
                break;
            }
            if(operation_7 == 3U)
            {
                _S180 = _S180 - _S579;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1603.z;
                _S180 = _S180 * float4((1.0f - factor_7))  + _S579 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S180.x);
    }
    else
    {
        clearcoatAmount_0 = _S160;
    }
    float ior_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool useSecondary_9 = (secondaryMask_0 & 1024U) != 0U;
        if((alternateMask_0 & 1024U) != 0U)
        {
            if(useSecondary_9)
            {
                detailSource_0 = _S179;
            }
            else
            {
                detailSource_0 = _S178;
            }
        }
        else
        {
            if(useSecondary_9)
            {
                detailSource_0 = _S171;
            }
            else
            {
                detailSource_0 = _S170;
            }
        }
        bool _S1680 = (udimMask_0 & 1024U) != 0U;
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
        uint _S1681 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1680)
            {
                for(;;)
                {
                    if(_S1681 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1682 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
                    thread uint width_24;
                    thread uint height_24;
                    (*((&width_24)) = (_S1682).get_width(0)),(*((&height_24)) = (_S1682).get_height(0));
                    float2 size_20 = float2(float(width_24), float(height_24));
                    float2 _S1683 = float2(0.5f) ;
                    float2 remapped_20 = clamp((detailSource_0 * (size_20 - float2(2.0f) ) + float2(1.0f) ) / size_20, _S1683 / size_20, (size_20 - _S1683) / size_20);
                    if((_S1681 & 1U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_20.x;
                    }
                    else
                    {
                        clearcoatRoughness_0 = detailSource_0.x;
                    }
                    if((_S1681 & 2U) != 0U)
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
                _S180 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1684 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1684).get_width(0)),(*((&atlasHeight_18)) = (_S1684).get_height(0));
            int3 _S1685 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1685)).xy), uint(((_S1685)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1686 = atlasWidth_18;
                uint _S1687 = 2U % atlasWidth_18;
                int _S1688 = int(_S1687);
                uint _S1689 = 2U / _S1686;
                int3 _S1690 = int3(_S1688, int(_S1689), int(0));
                int _S1691 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1690)).xy), uint(((_S1690)).z))).x * 255.0f)));
                uint _S1692 = atlasWidth_18;
                uint _S1693 = 3U % atlasWidth_18;
                int _S1694 = int(_S1693);
                uint _S1695 = 3U / _S1692;
                int3 _S1696 = int3(_S1694, int(_S1695), int(0));
                int2 minimumTile_18 = int2(_S1691, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1696)).xy), uint(((_S1696)).z))).x * 255.0f))));
                uint _S1697 = atlasWidth_18;
                uint _S1698 = 4U % atlasWidth_18;
                int _S1699 = int(_S1698);
                uint _S1700 = 4U / _S1697;
                int3 _S1701 = int3(_S1699, int(_S1700), int(0));
                uint _S1702 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1701)).xy), uint(((_S1701)).z))).x * 255.0f));
                uint _S1703 = atlasWidth_18;
                uint _S1704 = 5U % atlasWidth_18;
                int _S1705 = int(_S1704);
                uint _S1706 = 5U / _S1703;
                int3 _S1707 = int3(_S1705, int(_S1706), int(0));
                uint _S1708 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1707)).xy), uint(((_S1707)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1702, _S1708);
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
                    int3 _S1709 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1709)).xy), uint(((_S1709)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1702 + uint(tile_36.x));
                uint _S1710 = atlasWidth_18;
                uint _S1711 = offset_18 % atlasWidth_18;
                int _S1712 = int(_S1711);
                uint _S1713 = offset_18 / _S1710;
                int3 _S1714 = int3(_S1712, int(_S1713), int(0));
                uint _S1715 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1714)).xy), uint(((_S1714)).z))).x * 255.0f));
                uint _S1716 = atlasWidth_18;
                uint _S1717 = offset_18 + 1U;
                uint _S1718 = _S1717 % atlasWidth_18;
                int _S1719 = int(_S1718);
                uint _S1720 = _S1717 / _S1716;
                int3 _S1721 = int3(_S1719, int(_S1720), int(0));
                uint slot_36 = _S1715 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1721)).xy), uint(((_S1721)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1722 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1722)).xy), uint(((_S1722)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1723 = atlasWidth_18;
                uint _S1724 = 6U % atlasWidth_18;
                int _S1725 = int(_S1724);
                uint _S1726 = 6U / _S1723;
                int3 _S1727 = int3(_S1725, int(_S1726), int(0));
                uint _S1728 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1727)).xy), uint(((_S1727)).z))).x * 255.0f));
                uint _S1729 = atlasWidth_18;
                uint _S1730 = 7U % atlasWidth_18;
                int _S1731 = int(_S1730);
                uint _S1732 = 7U / _S1729;
                int3 _S1733 = int3(_S1731, int(_S1732), int(0));
                uint columns_18 = _S1728 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1733)).xy), uint(((_S1733)).z))).x * 255.0f)) << 8U);
                uint _S1734 = atlasWidth_18;
                uint _S1735 = 8U % atlasWidth_18;
                int _S1736 = int(_S1735);
                uint _S1737 = 8U / _S1734;
                int3 _S1738 = int3(_S1736, int(_S1737), int(0));
                uint _S1739 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1738)).xy), uint(((_S1738)).z))).x * 255.0f));
                uint _S1740 = atlasWidth_18;
                uint _S1741 = 9U % atlasWidth_18;
                int _S1742 = int(_S1741);
                uint _S1743 = 9U / _S1740;
                int3 _S1744 = int3(_S1742, int(_S1743), int(0));
                uint rows_18 = ((_S1739 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1744)).xy), uint(((_S1744)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1702 * _S1708 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1745 = atlasWidth_18 / columns_18;
                float _S1746 = float(_S1745);
                uint _S1747 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1746, float(_S1747));
                uint _S1748 = slot_37 % columns_18;
                float _S1749 = float(_S1748);
                uint _S1750 = slot_37 / columns_18;
                _S180 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1749, float(_S1750)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(detailSource_0) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1751 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(detailSource_0)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1751);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1752 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S180 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1752)).xy), uint(((_S1752)).z)));
                break;
            }
            uint _S1753 = atlasWidth_18 / uint(_S1751.x);
            float _S1754 = float(_S1753);
            uint _S1755 = (atlasHeight_18 - 1U) / uint(_S1751.y);
            float2 cellSize_37 = float2(_S1754, float(_S1755));
            _S180 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1756 = float4(_S7->compositeControls_0) ;
            if((_S1756.x) != 1024.0f)
            {
                break;
            }
            bool _S1757 = (_S1756.w) >= 0.5f;
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
            uint _S1758 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1757)
                {
                    for(;;)
                    {
                        if(_S1758 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1759 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_25;
                        thread uint height_25;
                        (*((&width_25)) = (_S1759).get_width(0)),(*((&height_25)) = (_S1759).get_height(0));
                        float2 size_21 = float2(float(width_25), float(height_25));
                        float2 _S1760 = float2(0.5f) ;
                        float2 remapped_21 = clamp((detailSource_0 * (size_21 - float2(2.0f) ) + float2(1.0f) ) / size_21, _S1760 / size_21, (size_21 - _S1760) / size_21);
                        if((_S1758 & 1U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_21.x;
                        }
                        else
                        {
                            clearcoatRoughness_0 = detailSource_0.x;
                        }
                        if((_S1758 & 2U) != 0U)
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
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1761 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1761).get_width(0)),(*((&atlasHeight_19)) = (_S1761).get_height(0));
                int3 _S1762 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1762)).xy), uint(((_S1762)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1763 = atlasWidth_19;
                    uint _S1764 = 2U % atlasWidth_19;
                    int _S1765 = int(_S1764);
                    uint _S1766 = 2U / _S1763;
                    int3 _S1767 = int3(_S1765, int(_S1766), int(0));
                    int _S1768 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1767)).xy), uint(((_S1767)).z))).x * 255.0f)));
                    uint _S1769 = atlasWidth_19;
                    uint _S1770 = 3U % atlasWidth_19;
                    int _S1771 = int(_S1770);
                    uint _S1772 = 3U / _S1769;
                    int3 _S1773 = int3(_S1771, int(_S1772), int(0));
                    int2 minimumTile_19 = int2(_S1768, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1773)).xy), uint(((_S1773)).z))).x * 255.0f))));
                    uint _S1774 = atlasWidth_19;
                    uint _S1775 = 4U % atlasWidth_19;
                    int _S1776 = int(_S1775);
                    uint _S1777 = 4U / _S1774;
                    int3 _S1778 = int3(_S1776, int(_S1777), int(0));
                    uint _S1779 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1778)).xy), uint(((_S1778)).z))).x * 255.0f));
                    uint _S1780 = atlasWidth_19;
                    uint _S1781 = 5U % atlasWidth_19;
                    int _S1782 = int(_S1781);
                    uint _S1783 = 5U / _S1780;
                    int3 _S1784 = int3(_S1782, int(_S1783), int(0));
                    uint _S1785 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1784)).xy), uint(((_S1784)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1779, _S1785);
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
                        int3 _S1786 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1786)).xy), uint(((_S1786)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1779 + uint(tile_38.x));
                    uint _S1787 = atlasWidth_19;
                    uint _S1788 = offset_19 % atlasWidth_19;
                    int _S1789 = int(_S1788);
                    uint _S1790 = offset_19 / _S1787;
                    int3 _S1791 = int3(_S1789, int(_S1790), int(0));
                    uint _S1792 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1791)).xy), uint(((_S1791)).z))).x * 255.0f));
                    uint _S1793 = atlasWidth_19;
                    uint _S1794 = offset_19 + 1U;
                    uint _S1795 = _S1794 % atlasWidth_19;
                    int _S1796 = int(_S1795);
                    uint _S1797 = _S1794 / _S1793;
                    int3 _S1798 = int3(_S1796, int(_S1797), int(0));
                    uint slot_38 = _S1792 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1798)).xy), uint(((_S1798)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1799 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1799)).xy), uint(((_S1799)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1800 = atlasWidth_19;
                    uint _S1801 = 6U % atlasWidth_19;
                    int _S1802 = int(_S1801);
                    uint _S1803 = 6U / _S1800;
                    int3 _S1804 = int3(_S1802, int(_S1803), int(0));
                    uint _S1805 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1804)).xy), uint(((_S1804)).z))).x * 255.0f));
                    uint _S1806 = atlasWidth_19;
                    uint _S1807 = 7U % atlasWidth_19;
                    int _S1808 = int(_S1807);
                    uint _S1809 = 7U / _S1806;
                    int3 _S1810 = int3(_S1808, int(_S1809), int(0));
                    uint columns_19 = _S1805 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1810)).xy), uint(((_S1810)).z))).x * 255.0f)) << 8U);
                    uint _S1811 = atlasWidth_19;
                    uint _S1812 = 8U % atlasWidth_19;
                    int _S1813 = int(_S1812);
                    uint _S1814 = 8U / _S1811;
                    int3 _S1815 = int3(_S1813, int(_S1814), int(0));
                    uint _S1816 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1815)).xy), uint(((_S1815)).z))).x * 255.0f));
                    uint _S1817 = atlasWidth_19;
                    uint _S1818 = 9U % atlasWidth_19;
                    int _S1819 = int(_S1818);
                    uint _S1820 = 9U / _S1817;
                    int3 _S1821 = int3(_S1819, int(_S1820), int(0));
                    uint rows_19 = ((_S1816 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1821)).xy), uint(((_S1821)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1779 * _S1785 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1822 = atlasWidth_19 / columns_19;
                    float _S1823 = float(_S1822);
                    uint _S1824 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1823, float(_S1824));
                    uint _S1825 = slot_39 % columns_19;
                    float _S1826 = float(_S1825);
                    uint _S1827 = slot_39 / columns_19;
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1826, float(_S1827)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(detailSource_0) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1828 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(detailSource_0)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1828);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1829 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1829)).xy), uint(((_S1829)).z)));
                    break;
                }
                uint _S1830 = atlasWidth_19 / uint(_S1828.x);
                float _S1831 = float(_S1830);
                uint _S1832 = (atlasHeight_19 - 1U) / uint(_S1828.y);
                float2 cellSize_39 = float2(_S1831, float(_S1832));
                _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_8 = uint(round(_S1756.y));
            if(operation_8 == 1U)
            {
                _S180 = _S180 * _S579;
                break;
            }
            if(operation_8 == 2U)
            {
                _S180 = _S180 + _S579;
                break;
            }
            if(operation_8 == 3U)
            {
                _S180 = _S180 - _S579;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1756.z;
                _S180 = _S180 * float4((1.0f - factor_8))  + _S579 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S180.x);
    }
    else
    {
        clearcoatRoughness_0 = _S161;
    }
    float lightType_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool useSecondary_10 = (secondaryMask_0 & 2048U) != 0U;
        if((alternateMask_0 & 2048U) != 0U)
        {
            if(useSecondary_10)
            {
                detailSource_0 = _S179;
            }
            else
            {
                detailSource_0 = _S178;
            }
        }
        else
        {
            if(useSecondary_10)
            {
                detailSource_0 = _S171;
            }
            else
            {
                detailSource_0 = _S170;
            }
        }
        bool _S1833 = (udimMask_0 & 2048U) != 0U;
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
        uint _S1834 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1833)
            {
                for(;;)
                {
                    if(_S1834 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1835 = (&kernelContext_0)->iorTexture_0;
                    thread uint width_26;
                    thread uint height_26;
                    (*((&width_26)) = (_S1835).get_width(0)),(*((&height_26)) = (_S1835).get_height(0));
                    float2 size_22 = float2(float(width_26), float(height_26));
                    float2 _S1836 = float2(0.5f) ;
                    float2 remapped_22 = clamp((detailSource_0 * (size_22 - float2(2.0f) ) + float2(1.0f) ) / size_22, _S1836 / size_22, (size_22 - _S1836) / size_22);
                    if((_S1834 & 1U) != 0U)
                    {
                        ior_0 = remapped_22.x;
                    }
                    else
                    {
                        ior_0 = detailSource_0.x;
                    }
                    if((_S1834 & 2U) != 0U)
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
                _S180 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1837 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_20;
            thread uint atlasHeight_20;
            (*((&atlasWidth_20)) = (_S1837).get_width(0)),(*((&atlasHeight_20)) = (_S1837).get_height(0));
            int3 _S1838 = int3(int(0), int(0), int(0));
            float4 metadata_20 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1838)).xy), uint(((_S1838)).z))) * float4(255.0f) );
            if((metadata_20.x) == 255.0f)
            {
                uint _S1839 = atlasWidth_20;
                uint _S1840 = 2U % atlasWidth_20;
                int _S1841 = int(_S1840);
                uint _S1842 = 2U / _S1839;
                int3 _S1843 = int3(_S1841, int(_S1842), int(0));
                int _S1844 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1843)).xy), uint(((_S1843)).z))).x * 255.0f)));
                uint _S1845 = atlasWidth_20;
                uint _S1846 = 3U % atlasWidth_20;
                int _S1847 = int(_S1846);
                uint _S1848 = 3U / _S1845;
                int3 _S1849 = int3(_S1847, int(_S1848), int(0));
                int2 minimumTile_20 = int2(_S1844, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1849)).xy), uint(((_S1849)).z))).x * 255.0f))));
                uint _S1850 = atlasWidth_20;
                uint _S1851 = 4U % atlasWidth_20;
                int _S1852 = int(_S1851);
                uint _S1853 = 4U / _S1850;
                int3 _S1854 = int3(_S1852, int(_S1853), int(0));
                uint _S1855 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1854)).xy), uint(((_S1854)).z))).x * 255.0f));
                uint _S1856 = atlasWidth_20;
                uint _S1857 = 5U % atlasWidth_20;
                int _S1858 = int(_S1857);
                uint _S1859 = 5U / _S1856;
                int3 _S1860 = int3(_S1858, int(_S1859), int(0));
                uint _S1861 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1860)).xy), uint(((_S1860)).z))).x * 255.0f));
                uint2 grid_20 = uint2(_S1855, _S1861);
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
                    int3 _S1862 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1862)).xy), uint(((_S1862)).z)));
                    break;
                }
                uint offset_20 = 10U + 2U * (uint(tile_40.y) * _S1855 + uint(tile_40.x));
                uint _S1863 = atlasWidth_20;
                uint _S1864 = offset_20 % atlasWidth_20;
                int _S1865 = int(_S1864);
                uint _S1866 = offset_20 / _S1863;
                int3 _S1867 = int3(_S1865, int(_S1866), int(0));
                uint _S1868 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1867)).xy), uint(((_S1867)).z))).x * 255.0f));
                uint _S1869 = atlasWidth_20;
                uint _S1870 = offset_20 + 1U;
                uint _S1871 = _S1870 % atlasWidth_20;
                int _S1872 = int(_S1871);
                uint _S1873 = _S1870 / _S1869;
                int3 _S1874 = int3(_S1872, int(_S1873), int(0));
                uint slot_40 = _S1868 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1874)).xy), uint(((_S1874)).z))).x * 255.0f)) << 8U);
                if(slot_40 == 0U)
                {
                    int3 _S1875 = int3(int(1), int(0), int(0));
                    _S180 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1875)).xy), uint(((_S1875)).z)));
                    break;
                }
                uint slot_41 = slot_40 - 1U;
                uint _S1876 = atlasWidth_20;
                uint _S1877 = 6U % atlasWidth_20;
                int _S1878 = int(_S1877);
                uint _S1879 = 6U / _S1876;
                int3 _S1880 = int3(_S1878, int(_S1879), int(0));
                uint _S1881 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1880)).xy), uint(((_S1880)).z))).x * 255.0f));
                uint _S1882 = atlasWidth_20;
                uint _S1883 = 7U % atlasWidth_20;
                int _S1884 = int(_S1883);
                uint _S1885 = 7U / _S1882;
                int3 _S1886 = int3(_S1884, int(_S1885), int(0));
                uint columns_20 = _S1881 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1886)).xy), uint(((_S1886)).z))).x * 255.0f)) << 8U);
                uint _S1887 = atlasWidth_20;
                uint _S1888 = 8U % atlasWidth_20;
                int _S1889 = int(_S1888);
                uint _S1890 = 8U / _S1887;
                int3 _S1891 = int3(_S1889, int(_S1890), int(0));
                uint _S1892 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1891)).xy), uint(((_S1891)).z))).x * 255.0f));
                uint _S1893 = atlasWidth_20;
                uint _S1894 = 9U % atlasWidth_20;
                int _S1895 = int(_S1894);
                uint _S1896 = 9U / _S1893;
                int3 _S1897 = int3(_S1895, int(_S1896), int(0));
                uint rows_20 = ((_S1892 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1897)).xy), uint(((_S1897)).z))).x * 255.0f)) << 8U)) + columns_20 - 1U) / columns_20;
                uint metadataRows_20 = (10U + 2U * _S1855 * _S1861 + atlasWidth_20 - 1U) / atlasWidth_20;
                uint _S1898 = atlasWidth_20 / columns_20;
                float _S1899 = float(_S1898);
                uint _S1900 = (atlasHeight_20 - metadataRows_20) / rows_20;
                float2 cellSize_40 = float2(_S1899, float(_S1900));
                uint _S1901 = slot_41 % columns_20;
                float _S1902 = float(_S1901);
                uint _S1903 = slot_41 / columns_20;
                _S180 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1902, float(_S1903)) * cellSize_40 + float2(1.5f, float(metadataRows_20) + 1.5f) + fract(detailSource_0) * max(cellSize_40 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
                break;
            }
            int2 _S1904 = int2(metadata_20.zw);
            int2 tile_41 = int2(floor(detailSource_0)) - int2(metadata_20.xy);
            if(any(tile_41 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_41 >= _S1904);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1905 = int3(int(min(1U, atlasWidth_20 - 1U)), int(0), int(0));
                _S180 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1905)).xy), uint(((_S1905)).z)));
                break;
            }
            uint _S1906 = atlasWidth_20 / uint(_S1904.x);
            float _S1907 = float(_S1906);
            uint _S1908 = (atlasHeight_20 - 1U) / uint(_S1904.y);
            float2 cellSize_41 = float2(_S1907, float(_S1908));
            _S180 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_41) * cellSize_41 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_41 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
            break;
        }
        for(;;)
        {
            float4 _S1909 = float4(_S7->compositeControls_0) ;
            if((_S1909.x) != 2048.0f)
            {
                break;
            }
            bool _S1910 = (_S1909.w) >= 0.5f;
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
            uint _S1911 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1910)
                {
                    for(;;)
                    {
                        if(_S1911 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1912 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_27;
                        thread uint height_27;
                        (*((&width_27)) = (_S1912).get_width(0)),(*((&height_27)) = (_S1912).get_height(0));
                        float2 size_23 = float2(float(width_27), float(height_27));
                        float2 _S1913 = float2(0.5f) ;
                        float2 remapped_23 = clamp((detailSource_0 * (size_23 - float2(2.0f) ) + float2(1.0f) ) / size_23, _S1913 / size_23, (size_23 - _S1913) / size_23);
                        if((_S1911 & 1U) != 0U)
                        {
                            ior_0 = remapped_23.x;
                        }
                        else
                        {
                            ior_0 = detailSource_0.x;
                        }
                        if((_S1911 & 2U) != 0U)
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
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1914 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_21;
                thread uint atlasHeight_21;
                (*((&atlasWidth_21)) = (_S1914).get_width(0)),(*((&atlasHeight_21)) = (_S1914).get_height(0));
                int3 _S1915 = int3(int(0), int(0), int(0));
                float4 metadata_21 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1915)).xy), uint(((_S1915)).z))) * float4(255.0f) );
                if((metadata_21.x) == 255.0f)
                {
                    uint _S1916 = atlasWidth_21;
                    uint _S1917 = 2U % atlasWidth_21;
                    int _S1918 = int(_S1917);
                    uint _S1919 = 2U / _S1916;
                    int3 _S1920 = int3(_S1918, int(_S1919), int(0));
                    int _S1921 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1920)).xy), uint(((_S1920)).z))).x * 255.0f)));
                    uint _S1922 = atlasWidth_21;
                    uint _S1923 = 3U % atlasWidth_21;
                    int _S1924 = int(_S1923);
                    uint _S1925 = 3U / _S1922;
                    int3 _S1926 = int3(_S1924, int(_S1925), int(0));
                    int2 minimumTile_21 = int2(_S1921, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1926)).xy), uint(((_S1926)).z))).x * 255.0f))));
                    uint _S1927 = atlasWidth_21;
                    uint _S1928 = 4U % atlasWidth_21;
                    int _S1929 = int(_S1928);
                    uint _S1930 = 4U / _S1927;
                    int3 _S1931 = int3(_S1929, int(_S1930), int(0));
                    uint _S1932 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1931)).xy), uint(((_S1931)).z))).x * 255.0f));
                    uint _S1933 = atlasWidth_21;
                    uint _S1934 = 5U % atlasWidth_21;
                    int _S1935 = int(_S1934);
                    uint _S1936 = 5U / _S1933;
                    int3 _S1937 = int3(_S1935, int(_S1936), int(0));
                    uint _S1938 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1937)).xy), uint(((_S1937)).z))).x * 255.0f));
                    uint2 grid_21 = uint2(_S1932, _S1938);
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
                        int3 _S1939 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1939)).xy), uint(((_S1939)).z)));
                        break;
                    }
                    uint offset_21 = 10U + 2U * (uint(tile_42.y) * _S1932 + uint(tile_42.x));
                    uint _S1940 = atlasWidth_21;
                    uint _S1941 = offset_21 % atlasWidth_21;
                    int _S1942 = int(_S1941);
                    uint _S1943 = offset_21 / _S1940;
                    int3 _S1944 = int3(_S1942, int(_S1943), int(0));
                    uint _S1945 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1944)).xy), uint(((_S1944)).z))).x * 255.0f));
                    uint _S1946 = atlasWidth_21;
                    uint _S1947 = offset_21 + 1U;
                    uint _S1948 = _S1947 % atlasWidth_21;
                    int _S1949 = int(_S1948);
                    uint _S1950 = _S1947 / _S1946;
                    int3 _S1951 = int3(_S1949, int(_S1950), int(0));
                    uint slot_42 = _S1945 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1951)).xy), uint(((_S1951)).z))).x * 255.0f)) << 8U);
                    if(slot_42 == 0U)
                    {
                        int3 _S1952 = int3(int(1), int(0), int(0));
                        _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1952)).xy), uint(((_S1952)).z)));
                        break;
                    }
                    uint slot_43 = slot_42 - 1U;
                    uint _S1953 = atlasWidth_21;
                    uint _S1954 = 6U % atlasWidth_21;
                    int _S1955 = int(_S1954);
                    uint _S1956 = 6U / _S1953;
                    int3 _S1957 = int3(_S1955, int(_S1956), int(0));
                    uint _S1958 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1957)).xy), uint(((_S1957)).z))).x * 255.0f));
                    uint _S1959 = atlasWidth_21;
                    uint _S1960 = 7U % atlasWidth_21;
                    int _S1961 = int(_S1960);
                    uint _S1962 = 7U / _S1959;
                    int3 _S1963 = int3(_S1961, int(_S1962), int(0));
                    uint columns_21 = _S1958 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1963)).xy), uint(((_S1963)).z))).x * 255.0f)) << 8U);
                    uint _S1964 = atlasWidth_21;
                    uint _S1965 = 8U % atlasWidth_21;
                    int _S1966 = int(_S1965);
                    uint _S1967 = 8U / _S1964;
                    int3 _S1968 = int3(_S1966, int(_S1967), int(0));
                    uint _S1969 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1968)).xy), uint(((_S1968)).z))).x * 255.0f));
                    uint _S1970 = atlasWidth_21;
                    uint _S1971 = 9U % atlasWidth_21;
                    int _S1972 = int(_S1971);
                    uint _S1973 = 9U / _S1970;
                    int3 _S1974 = int3(_S1972, int(_S1973), int(0));
                    uint rows_21 = ((_S1969 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1974)).xy), uint(((_S1974)).z))).x * 255.0f)) << 8U)) + columns_21 - 1U) / columns_21;
                    uint metadataRows_21 = (10U + 2U * _S1932 * _S1938 + atlasWidth_21 - 1U) / atlasWidth_21;
                    uint _S1975 = atlasWidth_21 / columns_21;
                    float _S1976 = float(_S1975);
                    uint _S1977 = (atlasHeight_21 - metadataRows_21) / rows_21;
                    float2 cellSize_42 = float2(_S1976, float(_S1977));
                    uint _S1978 = slot_43 % columns_21;
                    float _S1979 = float(_S1978);
                    uint _S1980 = slot_43 / columns_21;
                    _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1979, float(_S1980)) * cellSize_42 + float2(1.5f, float(metadataRows_21) + 1.5f) + fract(detailSource_0) * max(cellSize_42 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                    break;
                }
                int2 _S1981 = int2(metadata_21.zw);
                int2 tile_43 = int2(floor(detailSource_0)) - int2(metadata_21.xy);
                if(any(tile_43 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_43 >= _S1981);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1982 = int3(int(min(1U, atlasWidth_21 - 1U)), int(0), int(0));
                    _S579 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1982)).xy), uint(((_S1982)).z)));
                    break;
                }
                uint _S1983 = atlasWidth_21 / uint(_S1981.x);
                float _S1984 = float(_S1983);
                uint _S1985 = (atlasHeight_21 - 1U) / uint(_S1981.y);
                float2 cellSize_43 = float2(_S1984, float(_S1985));
                _S579 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_43) * cellSize_43 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_43 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                break;
            }
            uint operation_9 = uint(round(_S1909.y));
            if(operation_9 == 1U)
            {
                _S180 = _S180 * _S579;
                break;
            }
            if(operation_9 == 2U)
            {
                _S180 = _S180 + _S579;
                break;
            }
            if(operation_9 == 3U)
            {
                _S180 = _S180 - _S579;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1909.z;
                _S180 = _S180 * float4((1.0f - factor_9))  + _S579 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S180.x;
    }
    else
    {
        ior_0 = _S159;
    }
    float opacityThreshold_0 = _S154.z;
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
    float3 diffuseFresnel_0;
    if(isFrontFace_0)
    {
        diffuseFresnel_0 = normal_1;
    }
    else
    {
        diffuseFresnel_0 = - normal_1;
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
    float _S1986 = max(0.00100000004749745f, roughness_0);
    float _S1987 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1988 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1988;
    bool omniPbrSpecular_0 = (_S162.w) >= 0.5f;
    float dielectricLevel_0 = _S148.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1989 = float3(dielectricLevel_0) ;
        float3 _S1990 = float3(metallic_0) ;
        float3 _S1991 = mix(_S1989, diffuseColor_0, _S1990);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1989, diffuseColor_0, _S1990);
        grazingIncidence_0 = _S1991;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S154.w) >= 0.5f)
        {
            float3 _S1992 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1992;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1993 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1993);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1993);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1994 = _S2;
    float _S1995 = _S2.ambientLight_0.w;
    if(_S1995 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1996 = _S1994.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1996, _S1996)) > 0.0f;
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
    uint _S1997 = min(uint(_S1995), 128U);
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
    float4 _S1998 = float4(_S7->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S1998.w) ;
    uint domeLinkMask_0 = uint(max(_S164.x, 0.0f));
    FragmentFrameParameters_0 _S1999 = _S2;
    uint _S2000 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S2000) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S2001 = _S1994.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S2002 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S2002;
        for(;;)
        {
            if(lightCount_0 < _S2000)
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
        domeAmbient_1 = _S2001;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S2003 = !hasSceneLighting_0;
    if(_S2003)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1997;
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
        bool _S2004;
        if(hasSceneLighting_0)
        {
            _S2004 = (((uint4(_S7->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S2004 = false;
        }
        if(_S2004)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S2005 = lightIndex_0 == 0U;
        bool _S2006;
        if(_S2005)
        {
            _S2006 = _S2003;
        }
        else
        {
            _S2006 = false;
        }
        if(_S2006)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S2007 = 224U + lightIndex_0 * 16U;
            float _S2008 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2007)>>2]);
            float _S2009 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2007 + 4U)>>2]);
            float _S2010 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2007 + 8U)>>2]);
            float _S2011 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2007 + 12U)>>2]);
            lightType_0 = _S2011;
        }
        bool _S2012;
        if(_S2005)
        {
            _S2012 = _S2003;
        }
        else
        {
            _S2012 = false;
        }
        if(_S2012)
        {
            lightDirectionAxis_0 = normalize((float4(_S7->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S2013 = 2272U + lightIndex_0 * 16U;
            float _S2014 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2013)>>2]);
            float _S2015 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2013 + 4U)>>2]);
            float _S2016 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2013 + 8U)>>2]);
            float _S2017 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2013 + 12U)>>2]);
            lightDirectionAxis_0 = float3(_S2014, _S2015, _S2016);
        }
        float3 lightDirection_0 = normalize(lightDirectionAxis_0);
        bool _S2018;
        if(_S2005)
        {
            _S2018 = _S2003;
        }
        else
        {
            _S2018 = false;
        }
        if(_S2018)
        {
            roughness_0 = (float4(_S7->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S2019 = 4320U + lightIndex_0 * 16U;
            float _S2020 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2019)>>2]);
            float _S2021 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2019 + 4U)>>2]);
            float _S2022 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2019 + 8U)>>2]);
            float _S2023 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2019 + 12U)>>2]);
            roughness_0 = _S2023;
        }
        bool _S2024;
        if(_S2005)
        {
            _S2024 = _S2003;
        }
        else
        {
            _S2024 = false;
        }
        if(_S2024)
        {
            diffuseColor_0 = _S1998.xyz;
        }
        else
        {
            uint _S2025 = 4320U + lightIndex_0 * 16U;
            float _S2026 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2025)>>2]);
            float _S2027 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2025 + 4U)>>2]);
            float _S2028 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2025 + 8U)>>2]);
            float _S2029 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2025 + 12U)>>2]);
            diffuseColor_0 = float3(_S2026, _S2027, _S2028);
        }
        bool _S2030;
        if(_S2005)
        {
            _S2030 = _S2003;
        }
        else
        {
            _S2030 = false;
        }
        if(_S2030)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S2031 = 6368U + lightIndex_0 * 16U;
            float _S2032 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2031)>>2]);
            float _S2033 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2031 + 4U)>>2]);
            float _S2034 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2031 + 8U)>>2]);
            float _S2035 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2031 + 12U)>>2]);
            metallic_0 = _S2032;
        }
        bool _S2036;
        if(_S2005)
        {
            _S2036 = _S2003;
        }
        else
        {
            _S2036 = false;
        }
        if(_S2036)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S2037 = 6368U + lightIndex_0 * 16U;
            float _S2038 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2037)>>2]);
            float _S2039 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2037 + 4U)>>2]);
            float _S2040 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2037 + 8U)>>2]);
            float _S2041 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2037 + 12U)>>2]);
            clearcoatRoughness_0 = _S2039;
        }
        bool _S2042;
        if(_S2005)
        {
            _S2042 = _S2003;
        }
        else
        {
            _S2042 = false;
        }
        if(_S2042)
        {
            lightTangentAxis_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S2043 = 8416U + lightIndex_0 * 16U;
            float _S2044 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2043)>>2]);
            float _S2045 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2043 + 4U)>>2]);
            float _S2046 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2043 + 8U)>>2]);
            float _S2047 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2043 + 12U)>>2]);
            lightTangentAxis_0 = float3(_S2044, _S2045, _S2046);
        }
        bool _S2048;
        if(_S2005)
        {
            _S2048 = _S2003;
        }
        else
        {
            _S2048 = false;
        }
        if(_S2048)
        {
            lightBitangentAxis_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S2049 = 10464U + lightIndex_0 * 16U;
            float _S2050 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2049)>>2]);
            float _S2051 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2049 + 4U)>>2]);
            float _S2052 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2049 + 8U)>>2]);
            float _S2053 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2049 + 12U)>>2]);
            lightBitangentAxis_0 = float3(_S2050, _S2051, _S2052);
        }
        bool _S2054;
        if(_S2005)
        {
            _S2054 = _S2003;
        }
        else
        {
            _S2054 = false;
        }
        float shapeX_0;
        if(_S2054)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S2055 = 8416U + lightIndex_0 * 16U;
            float _S2056 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2055)>>2]);
            float _S2057 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2055 + 4U)>>2]);
            float _S2058 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2055 + 8U)>>2]);
            float _S2059 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2055 + 12U)>>2]);
            shapeX_0 = _S2059;
        }
        bool _S2060;
        if(_S2005)
        {
            _S2060 = _S2003;
        }
        else
        {
            _S2060 = false;
        }
        float shapeY_0;
        if(_S2060)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S2061 = 10464U + lightIndex_0 * 16U;
            float _S2062 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2061)>>2]);
            float _S2063 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2061 + 4U)>>2]);
            float _S2064 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2061 + 8U)>>2]);
            float _S2065 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2061 + 12U)>>2]);
            shapeY_0 = _S2065;
        }
        bool _S2066;
        if(_S2005)
        {
            _S2066 = _S2003;
        }
        else
        {
            _S2066 = false;
        }
        float lightRadius_0;
        if(_S2066)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S2067 = 2272U + lightIndex_0 * 16U;
            float _S2068 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2067)>>2]);
            float _S2069 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2067 + 4U)>>2]);
            float _S2070 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2067 + 8U)>>2]);
            float _S2071 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2067 + 12U)>>2]);
            lightRadius_0 = _S2071;
        }
        bool _S2072;
        if(_S2005)
        {
            _S2072 = _S2003;
        }
        else
        {
            _S2072 = false;
        }
        if(_S2072)
        {
            lightEyeBase_0 = unlitColor_0;
        }
        else
        {
            lightEyeBase_0 = sceneEye_0;
        }
        float3 directNormal_0;
        if(hasSceneLighting_0)
        {
            directNormal_0 = worldNormal_2;
        }
        else
        {
            directNormal_0 = diffuseFresnel_0;
        }
        float _S2073 = saturate(abs(dot(directNormal_0, lightEyeBase_0)) + 0.00000999999974738f);
        bool _S2074;
        if(_S2005)
        {
            _S2074 = _S2003;
        }
        else
        {
            _S2074 = false;
        }
        if(_S2074)
        {
            _S180 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S2075 = 17216U + lightIndex_0 * 16U;
            float _S2076 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2075)>>2]);
            float _S2077 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2075 + 4U)>>2]);
            float _S2078 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2075 + 8U)>>2]);
            float _S2079 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2075 + 12U)>>2]);
            _S180 = float4(_S2076, _S2077, _S2078, _S2079);
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S2080 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S2080;
        sampleOffsets_0[int(1)] = _S2080;
        sampleOffsets_0[int(2)] = _S2080;
        sampleOffsets_0[int(3)] = _S2080;
        sampleOffsets_0[int(4)] = _S2080;
        thread array<float3, int(5)> sampleEmitterNormals_0;
        float3 _S2081 = - lightDirection_0;
        sampleEmitterNormals_0[int(0)] = _S2081;
        sampleEmitterNormals_0[int(1)] = _S2081;
        sampleEmitterNormals_0[int(2)] = _S2081;
        sampleEmitterNormals_0[int(3)] = _S2081;
        sampleEmitterNormals_0[int(4)] = _S2081;
        bool _S2082 = lightType_0 == 2.0f;
        bool _S2083;
        if(_S2082)
        {
            _S2083 = lightRadius_0 > 0.0f;
        }
        else
        {
            _S2083 = false;
        }
        float sampleCount_0;
        float shadowVisibility_0;
        float sampleIntensity_0;
        bool useEmitterShadowSamples_0;
        if(_S2083)
        {
            sampleOffsets_0[int(0)] = - lightDirectionAxis_0 * float3(lightRadius_0) ;
            sampleOffsets_0[int(1)] = lightTangentAxis_0 * float3(lightRadius_0) ;
            sampleOffsets_0[int(2)] = - lightTangentAxis_0 * float3(lightRadius_0) ;
            sampleOffsets_0[int(3)] = lightBitangentAxis_0 * float3(lightRadius_0) ;
            sampleOffsets_0[int(4)] = - lightBitangentAxis_0 * float3(lightRadius_0) ;
            sampleCount_0 = 5.0f;
        }
        else
        {
            if(lightType_0 == 3.0f)
            {
                float3 halfWidth_0 = lightTangentAxis_0 * float3((shapeX_0 * 0.5f)) ;
                float3 halfHeight_0 = lightBitangentAxis_0 * float3((shapeY_0 * 0.5f)) ;
                sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
                sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
                float3 _S2084 = - halfWidth_0;
                sampleOffsets_0[int(3)] = _S2084 + halfHeight_0;
                sampleOffsets_0[int(4)] = _S2084 - halfHeight_0;
                sampleCount_0 = 5.0f;
            }
            else
            {
                if(lightType_0 == 4.0f)
                {
                    sampleOffsets_0[int(1)] = lightTangentAxis_0 * float3(lightRadius_0) ;
                    sampleOffsets_0[int(2)] = - lightTangentAxis_0 * float3(lightRadius_0) ;
                    sampleOffsets_0[int(3)] = lightBitangentAxis_0 * float3(lightRadius_0) ;
                    sampleOffsets_0[int(4)] = - lightBitangentAxis_0 * float3(lightRadius_0) ;
                    float3 x_0 = normalize(lightTangentAxis_0);
                    float3 y_0 = normalize(lightBitangentAxis_0);
                    float3 _S2085 = cross(y_0, lightDirection_0);
                    if((dot(x_0, _S2085)) < 0.0f)
                    {
                        sampleCount_0 = -1.0f;
                    }
                    else
                    {
                        sampleCount_0 = 1.0f;
                    }
                    bool _S2086 = (abs(0.0f)) > 0.5f;
                    float3 _S2087 = float3(0.0f) ;
                    float3 _S2088 = normalize((select(_S2080, _S2087 * _S2085, _S2086) + select(_S2080, _S2087 * cross(lightDirection_0, x_0), _S2086) + select(_S2080, float3(-1.0f)  * cross(x_0, y_0), (abs(-1.0f)) > 0.5f)) * float3(sampleCount_0) );
                    sampleEmitterNormals_0[int(0)] = _S2088;
                    sampleEmitterNormals_0[int(1)] = _S2088;
                    sampleEmitterNormals_0[int(2)] = _S2088;
                    sampleEmitterNormals_0[int(3)] = _S2088;
                    sampleEmitterNormals_0[int(4)] = _S2088;
                    sampleCount_0 = 5.0f;
                }
                else
                {
                    if(lightType_0 == 5.0f)
                    {
                        float3 halfLength_0 = lightDirectionAxis_0 * float3((shapeX_0 * 0.5f)) ;
                        if(lightRadius_0 > 0.0f)
                        {
                            useEmitterShadowSamples_0 = shapeX_0 > 0.0f;
                        }
                        else
                        {
                            useEmitterShadowSamples_0 = false;
                        }
                        if(useEmitterShadowSamples_0)
                        {
                            float3 _S2089 = lightTangentAxis_0 * float3(lightRadius_0) ;
                            sampleOffsets_0[int(0)] = _S2089 - halfLength_0;
                            sampleOffsets_0[int(1)] = _S2089 + halfLength_0;
                            float3 _S2090 = - lightTangentAxis_0 * float3(lightRadius_0) ;
                            sampleOffsets_0[int(2)] = _S2090 - halfLength_0;
                            sampleOffsets_0[int(3)] = _S2090 + halfLength_0;
                            sampleOffsets_0[int(4)] = lightBitangentAxis_0 * float3(lightRadius_0) ;
                            float3 x_1 = normalize(lightTangentAxis_0);
                            float3 y_1 = normalize(lightBitangentAxis_0);
                            float3 _S2091 = cross(y_1, lightDirection_0);
                            bool _S2092 = (dot(x_1, _S2091)) < 0.0f;
                            if(_S2092)
                            {
                                sampleCount_0 = -1.0f;
                            }
                            else
                            {
                                sampleCount_0 = 1.0f;
                            }
                            bool _S2093 = (abs(1.0f)) > 0.5f;
                            bool _S2094 = (abs(0.0f)) > 0.5f;
                            float3 _S2095 = cross(lightDirection_0, x_1);
                            float3 _S2096 = float3(0.0f) ;
                            float3 _S2097 = _S2096 * _S2095;
                            float3 _S2098 = _S2096 * cross(x_1, y_1);
                            float3 _S2099 = normalize((select(_S2080, _S2091, _S2093) + select(_S2080, _S2097, _S2094) + select(_S2080, _S2098, _S2094)) * float3(sampleCount_0) );
                            sampleEmitterNormals_0[int(0)] = _S2099;
                            sampleEmitterNormals_0[int(1)] = _S2099;
                            if(_S2092)
                            {
                                shadowVisibility_0 = -1.0f;
                            }
                            else
                            {
                                shadowVisibility_0 = 1.0f;
                            }
                            float3 _S2100 = normalize((select(_S2080, float3(-1.0f)  * _S2091, (abs(-1.0f)) > 0.5f) + select(_S2080, _S2097, _S2094) + select(_S2080, _S2098, _S2094)) * float3(shadowVisibility_0) );
                            sampleEmitterNormals_0[int(2)] = _S2100;
                            sampleEmitterNormals_0[int(3)] = _S2100;
                            if(_S2092)
                            {
                                sampleIntensity_0 = -1.0f;
                            }
                            else
                            {
                                sampleIntensity_0 = 1.0f;
                            }
                            float3 _S2101 = normalize((select(_S2080, _S2096 * _S2091, _S2094) + select(_S2080, _S2095, _S2093) + select(_S2080, _S2098, _S2094)) * float3(sampleIntensity_0) );
                            sampleEmitterNormals_0[int(4)] = _S2101;
                            sampleCount_0 = 5.0f;
                        }
                        else
                        {
                            sampleOffsets_0[int(1)] = halfLength_0;
                            sampleOffsets_0[int(2)] = - halfLength_0;
                            sampleCount_0 = 3.0f;
                        }
                    }
                    else
                    {
                        sampleCount_0 = 1.0f;
                    }
                }
            }
        }
        thread array<float, int(5)> shadowVisibilityBySample_0;
        shadowVisibilityBySample_0[int(0)] = 1.0f;
        shadowVisibilityBySample_0[int(1)] = 1.0f;
        shadowVisibilityBySample_0[int(2)] = 1.0f;
        shadowVisibilityBySample_0[int(3)] = 1.0f;
        shadowVisibilityBySample_0[int(4)] = 1.0f;
        float3 color_3;
        float3 sampleDirection_0;
        float emissionScale_0;
        bool _S2102;
        bool _S2103;
        if(hasSceneLighting_0)
        {
            uint _S2104 = lightIndex_0 * 16U;
            uint _S2105 = 14880U + _S2104;
            float _S2106 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2105)>>2]);
            float _S2107 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2105 + 4U)>>2]);
            float _S2108 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2105 + 8U)>>2]);
            float _S2109 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2105 + 12U)>>2]);
            int shadowSlot_0 = int(_S2106);
            if(shadowSlot_0 >= int(0))
            {
                int shadowSlot_1;
                int y_2;
                if(_S2107 == 6.0f)
                {
                    shadowVisibility_0 = -1.0f;
                    shadowSlot_1 = shadowSlot_0;
                    y_2 = int(0);
                    for(;;)
                    {
                        if(y_2 < int(6))
                        {
                        }
                        else
                        {
                            break;
                        }
                        int _S2110 = shadowSlot_0 + y_2;
                        uint _S2111 = 12576U + uint(_S2110) * 64U;
                        float _S2112 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111)>>2]);
                        float _S2113 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 4U)>>2]);
                        float _S2114 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 8U)>>2]);
                        float _S2115 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 12U)>>2]);
                        float4 _S2116 = float4(_S2112, _S2113, _S2114, _S2115);
                        float _S2117 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 16U)>>2]);
                        float _S2118 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 20U)>>2]);
                        float _S2119 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 24U)>>2]);
                        float _S2120 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 28U)>>2]);
                        float4 _S2121 = float4(_S2117, _S2118, _S2119, _S2120);
                        float _S2122 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 32U)>>2]);
                        float _S2123 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 36U)>>2]);
                        float _S2124 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 40U)>>2]);
                        float _S2125 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 44U)>>2]);
                        float4 _S2126 = float4(_S2122, _S2123, _S2124, _S2125);
                        float _S2127 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 48U)>>2]);
                        float _S2128 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 52U)>>2]);
                        float _S2129 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 56U)>>2]);
                        float _S2130 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2111 + 60U)>>2]);
                        float _S2131 = (((float4(worldPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S2116, _S2121, _S2126, float4(_S2127, _S2128, _S2129, _S2130))))).w;
                        if(_S2131 > shadowVisibility_0)
                        {
                            shadowVisibility_0 = _S2131;
                            shadowSlot_1 = _S2110;
                        }
                        y_2 = y_2 + int(1);
                    }
                }
                else
                {
                    shadowSlot_1 = shadowSlot_0;
                }
                bool _S2132;
                int x_2;
                if(_S2107 == 5.0f)
                {
                    sampleIndex_0 = 0U;
                    for(;;)
                    {
                        if(sampleIndex_0 < 5U)
                        {
                        }
                        else
                        {
                            break;
                        }
                        uint _S2133 = 224U + _S2104;
                        float _S2134 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2133)>>2]);
                        float _S2135 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2133 + 4U)>>2]);
                        float _S2136 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2133 + 8U)>>2]);
                        float _S2137 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2133 + 12U)>>2]);
                        float3 toShadowSample_0 = float3(_S2134, _S2135, _S2136) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                        float3 shadowLightDirection_0 = toShadowSample_0 * float3(rsqrt(max(dot(toShadowSample_0, toShadowSample_0), 0.00100000004749745f))) ;
                        int _S2138 = shadowSlot_1 + int(sampleIndex_0);
                        for(;;)
                        {
                            uint _S2139 = uint(_S2138);
                            uint _S2140 = _S2139 * 16U;
                            uint _S2141 = 14496U + _S2140;
                            float _S2142 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2141)>>2]);
                            float _S2143 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2141 + 4U)>>2]);
                            float _S2144 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2141 + 8U)>>2]);
                            float _S2145 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2141 + 12U)>>2]);
                            uint _S2146 = 14112U + _S2140;
                            float _S2147 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2146)>>2]);
                            float _S2148 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2146 + 4U)>>2]);
                            float _S2149 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2146 + 8U)>>2]);
                            float _S2150 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2146 + 12U)>>2]);
                            if((dot(specularColor_0, shadowLightDirection_0)) < 0.0f)
                            {
                                sampleDirection_0 = - specularColor_0;
                            }
                            else
                            {
                                sampleDirection_0 = specularColor_0;
                            }
                            float slope_0 = clamp(1.0f - saturate(dot(sampleDirection_0, shadowLightDirection_0)), 0.0f, 1.0f);
                            float3 offsetPosition_0 = worldPosition_0 + sampleDirection_0 * float3((_S2143 * slope_0)) ;
                            uint _S2151 = 12576U + _S2139 * 64U;
                            float _S2152 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151)>>2]);
                            float _S2153 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 4U)>>2]);
                            float _S2154 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 8U)>>2]);
                            float _S2155 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 12U)>>2]);
                            float4 _S2156 = float4(_S2152, _S2153, _S2154, _S2155);
                            float _S2157 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 16U)>>2]);
                            float _S2158 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 20U)>>2]);
                            float _S2159 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 24U)>>2]);
                            float _S2160 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 28U)>>2]);
                            float4 _S2161 = float4(_S2157, _S2158, _S2159, _S2160);
                            float _S2162 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 32U)>>2]);
                            float _S2163 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 36U)>>2]);
                            float _S2164 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 40U)>>2]);
                            float _S2165 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 44U)>>2]);
                            float4 _S2166 = float4(_S2162, _S2163, _S2164, _S2165);
                            float _S2167 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 48U)>>2]);
                            float _S2168 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 52U)>>2]);
                            float _S2169 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 56U)>>2]);
                            float _S2170 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2151 + 60U)>>2]);
                            float4 lightClip_0 = (((float4(offsetPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S2156, _S2161, _S2166, float4(_S2167, _S2168, _S2169, _S2170)))));
                            float _S2171 = lightClip_0.w;
                            if(_S2171 <= 0.0f)
                            {
                                ior_0 = 1.0f;
                                break;
                            }
                            float3 ndc_0 = lightClip_0.xyz / float3(_S2171) ;
                            if((abs(ndc_0.x)) > 1.0f)
                            {
                                useEmitterShadowSamples_0 = true;
                            }
                            else
                            {
                                useEmitterShadowSamples_0 = (abs(ndc_0.y)) > 1.0f;
                            }
                            if(useEmitterShadowSamples_0)
                            {
                                _S2102 = true;
                            }
                            else
                            {
                                _S2102 = (ndc_0.z) < 0.0f;
                            }
                            if(_S2102)
                            {
                                _S2103 = true;
                            }
                            else
                            {
                                _S2103 = (ndc_0.z) > 1.0f;
                            }
                            if(_S2103)
                            {
                                ior_0 = 1.0f;
                                break;
                            }
                            float2 _S2172 = float2(_S2147, _S2148);
                            float2 _S2173 = float2(_S2149, _S2150);
                            float2 _S2174 = _S2172 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S2173;
                            bool transmission_0 = _S2144 < 0.0f;
                            if(transmission_0)
                            {
                                ior_0 = - _S2144 - 1.0f;
                            }
                            else
                            {
                                ior_0 = max(_S2144, 0.0f);
                            }
                            float _S2175 = ndc_0.z - _S2142 * (1.0f + 2.0f * slope_0);
                            float2 _S2176 = float2((_S2145 * 0.5f)) ;
                            float2 _S2177 = _S2172 + _S2176;
                            float2 _S2178 = _S2172 + _S2173 - _S2176;
                            y_2 = int(-1);
                            shadowVisibility_0 = 0.0f;
                            for(;;)
                            {
                                if(y_2 <= int(1))
                                {
                                }
                                else
                                {
                                    break;
                                }
                                x_2 = int(-1);
                                for(;;)
                                {
                                    if(x_2 <= int(1))
                                    {
                                    }
                                    else
                                    {
                                        break;
                                    }
                                    float2 coordinate_0 = clamp(_S2174 + float2(float(x_2), float(y_2)) * float2((ior_0 * _S2145)) , _S2177, _S2178);
                                    if(_S2175 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f))).x))
                                    {
                                        sampleIntensity_0 = 1.0f;
                                    }
                                    else
                                    {
                                        sampleIntensity_0 = 0.0f;
                                    }
                                    if(transmission_0)
                                    {
                                        _S2132 = sampleIntensity_0 > 0.0f;
                                    }
                                    else
                                    {
                                        _S2132 = false;
                                    }
                                    if(_S2132)
                                    {
                                        float2 layer0_0 = (((&kernelContext_0)->shadowTransmission0_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float2 layer1_0 = (((&kernelContext_0)->shadowTransmission1_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float2 layer2_0 = (((&kernelContext_0)->shadowTransmission2_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float2 layer3_0 = (((&kernelContext_0)->shadowTransmission3_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float4 depths_0 = float4(layer0_0.x, layer1_0.x, layer2_0.x, layer3_0.x);
                                        float4 weights_0 = select(float4(1.0f, 1.0f, 1.0f, 1.0f), float4(layer0_0.y, layer1_0.y, layer2_0.y, layer3_0.y), (depths_0 >= (float4(0.0f) )) & ((float4(_S2175) ) > depths_0));
                                        emissionScale_0 = sampleIntensity_0 * weights_0.x * weights_0.y * weights_0.z * weights_0.w;
                                    }
                                    else
                                    {
                                        emissionScale_0 = sampleIntensity_0;
                                    }
                                    float lit_0 = shadowVisibility_0 + emissionScale_0;
                                    x_2 = x_2 + int(1);
                                    shadowVisibility_0 = lit_0;
                                }
                                y_2 = y_2 + int(1);
                            }
                            ior_0 = shadowVisibility_0 * 0.1111111119389534f;
                            break;
                        }
                        shadowVisibilityBySample_0[sampleIndex_0] = ior_0;
                        sampleIndex_0 = sampleIndex_0 + 1U;
                    }
                    useEmitterShadowSamples_0 = true;
                    shadowVisibility_0 = 1.0f;
                }
                else
                {
                    if(_S2082)
                    {
                        uint _S2179 = 224U + _S2104;
                        float _S2180 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2179)>>2]);
                        float _S2181 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2179 + 4U)>>2]);
                        float _S2182 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2179 + 8U)>>2]);
                        float _S2183 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2179 + 12U)>>2]);
                        float3 toShadowLight_0 = float3(_S2180, _S2181, _S2182) - worldPosition_0;
                        color_3 = toShadowLight_0 * float3(rsqrt(max(dot(toShadowLight_0, toShadowLight_0), 0.00100000004749745f))) ;
                    }
                    else
                    {
                        color_3 = lightDirection_0;
                    }
                    for(;;)
                    {
                        uint _S2184 = uint(shadowSlot_1);
                        uint _S2185 = _S2184 * 16U;
                        uint _S2186 = 14496U + _S2185;
                        float _S2187 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2186)>>2]);
                        float _S2188 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2186 + 4U)>>2]);
                        float _S2189 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2186 + 8U)>>2]);
                        float _S2190 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2186 + 12U)>>2]);
                        uint _S2191 = 14112U + _S2185;
                        float _S2192 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2191)>>2]);
                        float _S2193 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2191 + 4U)>>2]);
                        float _S2194 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2191 + 8U)>>2]);
                        float _S2195 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2191 + 12U)>>2]);
                        if((dot(specularColor_0, color_3)) < 0.0f)
                        {
                            sampleDirection_0 = - specularColor_0;
                        }
                        else
                        {
                            sampleDirection_0 = specularColor_0;
                        }
                        float slope_1 = clamp(1.0f - saturate(dot(sampleDirection_0, color_3)), 0.0f, 1.0f);
                        float3 offsetPosition_1 = worldPosition_0 + sampleDirection_0 * float3((_S2188 * slope_1)) ;
                        uint _S2196 = 12576U + _S2184 * 64U;
                        float _S2197 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196)>>2]);
                        float _S2198 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 4U)>>2]);
                        float _S2199 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 8U)>>2]);
                        float _S2200 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 12U)>>2]);
                        float4 _S2201 = float4(_S2197, _S2198, _S2199, _S2200);
                        float _S2202 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 16U)>>2]);
                        float _S2203 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 20U)>>2]);
                        float _S2204 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 24U)>>2]);
                        float _S2205 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 28U)>>2]);
                        float4 _S2206 = float4(_S2202, _S2203, _S2204, _S2205);
                        float _S2207 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 32U)>>2]);
                        float _S2208 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 36U)>>2]);
                        float _S2209 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 40U)>>2]);
                        float _S2210 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 44U)>>2]);
                        float4 _S2211 = float4(_S2207, _S2208, _S2209, _S2210);
                        float _S2212 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 48U)>>2]);
                        float _S2213 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 52U)>>2]);
                        float _S2214 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 56U)>>2]);
                        float _S2215 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2196 + 60U)>>2]);
                        float4 lightClip_1 = (((float4(offsetPosition_1, 1.0f)) * (matrix<float,int(4),int(4)> (_S2201, _S2206, _S2211, float4(_S2212, _S2213, _S2214, _S2215)))));
                        float _S2216 = lightClip_1.w;
                        if(_S2216 <= 0.0f)
                        {
                            ior_0 = 1.0f;
                            break;
                        }
                        float3 ndc_1 = lightClip_1.xyz / float3(_S2216) ;
                        if((abs(ndc_1.x)) > 1.0f)
                        {
                            useEmitterShadowSamples_0 = true;
                        }
                        else
                        {
                            useEmitterShadowSamples_0 = (abs(ndc_1.y)) > 1.0f;
                        }
                        if(useEmitterShadowSamples_0)
                        {
                            _S2102 = true;
                        }
                        else
                        {
                            _S2102 = (ndc_1.z) < 0.0f;
                        }
                        if(_S2102)
                        {
                            _S2103 = true;
                        }
                        else
                        {
                            _S2103 = (ndc_1.z) > 1.0f;
                        }
                        if(_S2103)
                        {
                            ior_0 = 1.0f;
                            break;
                        }
                        float2 _S2217 = float2(_S2192, _S2193);
                        float2 _S2218 = float2(_S2194, _S2195);
                        float2 _S2219 = _S2217 + (ndc_1.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S2218;
                        bool transmission_1 = _S2189 < 0.0f;
                        if(transmission_1)
                        {
                            ior_0 = - _S2189 - 1.0f;
                        }
                        else
                        {
                            ior_0 = max(_S2189, 0.0f);
                        }
                        float _S2220 = ndc_1.z - _S2187 * (1.0f + 2.0f * slope_1);
                        float2 _S2221 = float2((_S2190 * 0.5f)) ;
                        float2 _S2222 = _S2217 + _S2221;
                        float2 _S2223 = _S2217 + _S2218 - _S2221;
                        y_2 = int(-1);
                        shadowVisibility_0 = 0.0f;
                        for(;;)
                        {
                            if(y_2 <= int(1))
                            {
                            }
                            else
                            {
                                break;
                            }
                            x_2 = int(-1);
                            for(;;)
                            {
                                if(x_2 <= int(1))
                                {
                                }
                                else
                                {
                                    break;
                                }
                                float2 coordinate_1 = clamp(_S2219 + float2(float(x_2), float(y_2)) * float2((ior_0 * _S2190)) , _S2222, _S2223);
                                if(_S2220 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f))).x))
                                {
                                    sampleIntensity_0 = 1.0f;
                                }
                                else
                                {
                                    sampleIntensity_0 = 0.0f;
                                }
                                if(transmission_1)
                                {
                                    _S2132 = sampleIntensity_0 > 0.0f;
                                }
                                else
                                {
                                    _S2132 = false;
                                }
                                if(_S2132)
                                {
                                    float2 layer0_1 = (((&kernelContext_0)->shadowTransmission0_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float2 layer1_1 = (((&kernelContext_0)->shadowTransmission1_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float2 layer2_1 = (((&kernelContext_0)->shadowTransmission2_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float2 layer3_1 = (((&kernelContext_0)->shadowTransmission3_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float4 depths_1 = float4(layer0_1.x, layer1_1.x, layer2_1.x, layer3_1.x);
                                    float4 weights_1 = select(float4(1.0f, 1.0f, 1.0f, 1.0f), float4(layer0_1.y, layer1_1.y, layer2_1.y, layer3_1.y), (depths_1 >= (float4(0.0f) )) & ((float4(_S2220) ) > depths_1));
                                    emissionScale_0 = sampleIntensity_0 * weights_1.x * weights_1.y * weights_1.z * weights_1.w;
                                }
                                else
                                {
                                    emissionScale_0 = sampleIntensity_0;
                                }
                                float lit_1 = shadowVisibility_0 + emissionScale_0;
                                x_2 = x_2 + int(1);
                                shadowVisibility_0 = lit_1;
                            }
                            y_2 = y_2 + int(1);
                        }
                        ior_0 = shadowVisibility_0 * 0.1111111119389534f;
                        break;
                    }
                    useEmitterShadowSamples_0 = false;
                    shadowVisibility_0 = ior_0;
                }
            }
            else
            {
                useEmitterShadowSamples_0 = false;
                shadowVisibility_0 = 1.0f;
            }
        }
        else
        {
            useEmitterShadowSamples_0 = false;
            shadowVisibility_0 = 1.0f;
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
            float sampleIntensity_1 = roughness_0 / sampleCount_0;
            if(lightType_0 >= 2.0f)
            {
                uint _S2224 = 224U + lightIndex_0 * 16U;
                float _S2225 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2224)>>2]);
                float _S2226 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2224 + 4U)>>2]);
                float _S2227 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2224 + 8U)>>2]);
                float _S2228 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2224 + 12U)>>2]);
                float3 toLight_0 = float3(_S2225, _S2226, _S2227) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S2229 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S2229)) ;
                float sampleIntensity_2 = sampleIntensity_1 / _S2229;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    _S2102 = true;
                }
                else
                {
                    if(lightType_0 == 5.0f)
                    {
                        _S2102 = lightRadius_0 > 0.0f;
                    }
                    else
                    {
                        _S2102 = false;
                    }
                    if(_S2102)
                    {
                        _S2103 = shapeX_0 > 0.0f;
                    }
                    else
                    {
                        _S2103 = false;
                    }
                    _S2102 = _S2103;
                }
                if(_S2102)
                {
                    sampleIntensity_0 = saturate(dot(sampleEmitterNormals_0[sampleIndex_0], - sampleDirection_1));
                }
                else
                {
                    sampleIntensity_0 = 1.0f;
                }
                if((_S180.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S180.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S2230 = _S180.y;
                        if(_S2230 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S2230);
                        }
                        else
                        {
                            ior_0 = 1.0f;
                        }
                        emissionScale_0 = ior_0;
                    }
                    emissionScale_0 = sampleIntensity_0 * emissionScale_0;
                }
                else
                {
                    emissionScale_0 = sampleIntensity_0;
                }
                sampleDirection_0 = sampleDirection_1;
                sampleIntensity_0 = sampleIntensity_2;
            }
            else
            {
                sampleDirection_0 = lightDirection_0;
                sampleIntensity_0 = sampleIntensity_1;
                emissionScale_0 = 1.0f;
            }
            float3 half_0 = normalize(sampleDirection_0 + lightEyeBase_0);
            float normalDotLight_0 = saturate(dot(directNormal_0, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(directNormal_0, half_0));
            float _S2231 = pow(max(0.0f, 1.0f - saturate(dot(lightEyeBase_0, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S2231)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S2231) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S2232 = float3(_S2231) ;
            float _S2233 = max(_S1986, 0.00100000004749745f);
            float alpha_0 = _S2233 * _S2233;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S2234 = 1.0f - k_0;
            float3 _S2235 = float3(max(4.0f * normalDotLight_0 * _S2073, 1.00000000317107685e-30f)) ;
            float3 _S2236 = mix(normalIncidence_0, grazingIncidence_0, _S2232) * float3((_S2073 / (_S2073 * _S2234 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S2234 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S2235;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S2237 = max(_S1987, 0.00100000004749745f);
                float alpha_1 = _S2237 * _S2237;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S2238 = 1.0f - k_1;
                directSpecular_0 = _S2236 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S2232) * float3((_S2073 / (_S2073 * _S2238 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S2238 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S2235);
            }
            else
            {
                directSpecular_0 = _S2236;
            }
            float3 _S2239 = diffuseColor_0 * float3(sampleIntensity_0) ;
            float3 diffuseIrradiance_0 = _S2239 * _S1988;
            float sampleShadowVisibility_0;
            if(useEmitterShadowSamples_0)
            {
                sampleShadowVisibility_0 = shadowVisibilityBySample_0[sampleIndex_0];
            }
            else
            {
                sampleShadowVisibility_0 = shadowVisibility_0;
            }
            color_3 = color_3 + float3((sampleShadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * diffuseIrradiance_0 + directSpecular_0 * float3(clearcoatRoughness_0)  * _S2239);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S2240 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S2241 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S2242 = saturate(_S1986);
        float2 _S2243 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2241, _S2242)), level((0.0f)))).xy;
        float _S2244 = _S2243.x;
        float _S2245 = _S2243.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S2244)  + grazingIncidence_0 * float3(_S2245) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S2244 + _S2245)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S2246 = max(_S1999.domeControls_0.y, 1.0f);
        float3 _S2247 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S2246 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1999.domeControls_0.z;
            float _S2248 = _S1999.domeControls_0.w;
            for(;;)
            {
                bool _S2249 = _S2246 <= 1.0f;
                _S5 = _S2249;
                if(_S2249)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S2248, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S2246)), level((0.0f)))).xyz;
                break;
            }
            float _S2250 = _S2240.environmentControls_0.y;
            float _S2251 = _S2240.environmentControls_0.z;
            for(;;)
            {
                if(_S5)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2252 = max(_S2250, 1.0f);
                    float _S2253 = _S2252 - 1.0f;
                    float slice_0 = _S2242 * max(_S2253, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S2251, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S2252)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S2253) + v_0) / _S2252)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S2254 = max(_S2250, 1.0f);
                float total_0 = _S2254 * _S2246;
                float _S2255 = _S2254 - 1.0f;
                float slice_1 = _S2242 * max(_S2255, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S2251, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S2254;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S2255) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S2256 = max(_S2250, 1.0f);
                        float _S2257 = _S2256 - 1.0f;
                        float slice_2 = saturate(_S1987) * max(_S2257, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S2251, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S2256)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S2257) + v_2) / _S2256)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2258 = max(_S2250, 1.0f);
                    float total_1 = _S2258 * _S2246;
                    float _S2259 = _S2258 - 1.0f;
                    float slice_3 = saturate(_S1987) * max(_S2259, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S2251, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S2258;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S2259) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightBitangentAxis_0 = specularColor_0;
            }
            else
            {
                lightBitangentAxis_0 = _S2247;
            }
            lightDirectionAxis_0 = diffuseColor_0;
            lightTangentAxis_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightDirectionAxis_0 = _S2247;
            lightTangentAxis_0 = _S2247;
            lightBitangentAxis_0 = _S2247;
            for(;;)
            {
                if(sampleIndex_0 < _S2000)
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
                float _S2260 = _S1999.domeControls_0.w;
                for(;;)
                {
                    bool _S2261 = _S2246 <= 1.0f;
                    _S6 = _S2261;
                    if(_S2261)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S2260, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S2246)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightDirectionAxis_0 + diffuseColor_0;
                float _S2262 = _S2240.environmentControls_0.y;
                float _S2263 = _S2240.environmentControls_0.z;
                for(;;)
                {
                    if(_S6)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2264 = max(_S2262, 1.0f);
                        float _S2265 = _S2264 - 1.0f;
                        float slice_4 = _S2242 * max(_S2265, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S2263, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S2264)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S2265) + v_4) / _S2264)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2266 = max(_S2262, 1.0f);
                    float total_2 = _S2266 * _S2246;
                    float _S2267 = _S2266 - 1.0f;
                    float slice_5 = _S2242 * max(_S2267, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S2263, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S2266;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S2267) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightTangentAxis_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S6)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_7 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S2268 = max(_S2262, 1.0f);
                            float _S2269 = _S2268 - 1.0f;
                            float slice_6 = saturate(_S1987) * max(_S2269, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S2263, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S2268)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S2269) + v_6) / _S2268)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2270 = max(_S2262, 1.0f);
                        float total_3 = _S2270 * _S2246;
                        float _S2271 = _S2270 - 1.0f;
                        float slice_7 = saturate(_S1987) * max(_S2271, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S2263, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S2270;
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S2271) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    lightEyeBase_0 = lightBitangentAxis_0 + specularColor_0;
                }
                else
                {
                    lightEyeBase_0 = lightBitangentAxis_0;
                }
                lightDirectionAxis_0 = irradiance_0;
                lightTangentAxis_0 = prefiltered_0;
                lightBitangentAxis_0 = lightEyeBase_0;
                sampleIndex_0 = sampleIndex_0 + 1U;
            }
        }
        float3 color_4 = color_2 + float3(occlusion_0)  * diffuse_1 * lightDirectionAxis_0 * diffuseWeight_0 + float3(occlusion_0)  * lightTangentAxis_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S2272 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2241, saturate(_S1987))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightBitangentAxis_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S2272.x)  + float3(_S2272.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + emissiveColor_1) * float3(exp2((as_type<float>((_S3.z))))) ;
    if((_S3.y) == 1U)
    {
        color_2 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S2273 = { float4(color_2, opacity_0) };
    return _S2273;
}
