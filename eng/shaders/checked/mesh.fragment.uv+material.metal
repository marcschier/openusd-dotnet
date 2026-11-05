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

[[fragment]] pixelOutput_0 fragmentMain_uv_material(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> diffuseValuesTexture_1 [[texture(22)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> emissiveValuesTexture_1 [[texture(23)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> shadowTransmission0_1 [[texture(24)]], texture2d<float, access::sample> shadowTransmission1_1 [[texture(25)]], texture2d<float, access::sample> shadowTransmission2_1 [[texture(26)]], texture2d<float, access::sample> shadowTransmission3_1 [[texture(27)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_0;
    FragmentFrameParameters_0 _S2;
    uint4 _S3;
    float _S4;
    uint sampleIndex_0;
    float3 lightDirectionAxis_0;
    float3 lightTangentAxis_0;
    float3 lightBitangentAxis_0;
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
    float2 _S180;
    float2 _S181;
    bool hasSceneLighting_0;
    float4 _S182;
    uint3 _S183;
    if((textureMask_0 & 2U) != 0U)
    {
        bool useSecondary_0 = (secondaryMask_0 & 2U) != 0U;
        if((alternateMask_0 & 2U) != 0U)
        {
            if(useSecondary_0)
            {
                _S180 = _S179;
            }
            else
            {
                _S180 = _S178;
            }
        }
        else
        {
            if(useSecondary_0)
            {
                _S180 = _S171;
            }
            else
            {
                _S180 = _S170;
            }
        }
        thread float4 diffuseSample_0;
        bool _S184 = (udimMask_0 & 2U) != 0U;
        uint maskU_0 = uint(round(_S164.y));
        uint maskV_0 = uint(round(_S164.z));
        bool _S185 = (maskU_0 & 2U) != 0U;
        if(_S185)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S186 = (maskV_0 & 2U) != 0U;
        if(_S186)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S187 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S184)
            {
                for(;;)
                {
                    if(_S187 == 0U)
                    {
                        _S181 = _S180;
                        break;
                    }
                    texture2d<float, access::sample> _S188 = (&kernelContext_0)->baseColorTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S188).get_width(0)),(*((&height_0)) = (_S188).get_height(0));
                    float2 size_0 = float2(float(width_0), float(height_0));
                    float2 _S189 = float2(0.5f) ;
                    float2 remapped_0 = clamp((_S180 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S189 / size_0, (size_0 - _S189) / size_0);
                    if((_S187 & 1U) != 0U)
                    {
                        roughness_0 = remapped_0.x;
                    }
                    else
                    {
                        roughness_0 = _S180.x;
                    }
                    if((_S187 & 2U) != 0U)
                    {
                        metallic_0 = remapped_0.y;
                    }
                    else
                    {
                        metallic_0 = _S180.y;
                    }
                    _S181 = float2(roughness_0, metallic_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S190 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S190).get_width(0)),(*((&atlasHeight_0)) = (_S190).get_height(0));
            int3 _S191 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S191)).xy), uint(((_S191)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S192 = atlasWidth_0;
                uint _S193 = 2U % atlasWidth_0;
                int _S194 = int(_S193);
                uint _S195 = 2U / _S192;
                int3 _S196 = int3(_S194, int(_S195), int(0));
                int _S197 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S196)).xy), uint(((_S196)).z))).x * 255.0f)));
                uint _S198 = atlasWidth_0;
                uint _S199 = 3U % atlasWidth_0;
                int _S200 = int(_S199);
                uint _S201 = 3U / _S198;
                int3 _S202 = int3(_S200, int(_S201), int(0));
                int2 minimumTile_0 = int2(_S197, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S202)).xy), uint(((_S202)).z))).x * 255.0f))));
                uint _S203 = atlasWidth_0;
                uint _S204 = 4U % atlasWidth_0;
                int _S205 = int(_S204);
                uint _S206 = 4U / _S203;
                int3 _S207 = int3(_S205, int(_S206), int(0));
                uint _S208 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S207)).xy), uint(((_S207)).z))).x * 255.0f));
                uint _S209 = atlasWidth_0;
                uint _S210 = 5U % atlasWidth_0;
                int _S211 = int(_S210);
                uint _S212 = 5U / _S209;
                int3 _S213 = int3(_S211, int(_S212), int(0));
                uint _S214 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S213)).xy), uint(((_S213)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S208, _S214);
                int2 tile_0 = int2(floor(_S180)) - minimumTile_0;
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
                    int3 _S215 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S215)).xy), uint(((_S215)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S208 + uint(tile_0.x));
                uint _S216 = atlasWidth_0;
                uint _S217 = offset_0 % atlasWidth_0;
                int _S218 = int(_S217);
                uint _S219 = offset_0 / _S216;
                int3 _S220 = int3(_S218, int(_S219), int(0));
                uint _S221 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S220)).xy), uint(((_S220)).z))).x * 255.0f));
                uint _S222 = atlasWidth_0;
                uint _S223 = offset_0 + 1U;
                uint _S224 = _S223 % atlasWidth_0;
                int _S225 = int(_S224);
                uint _S226 = _S223 / _S222;
                int3 _S227 = int3(_S225, int(_S226), int(0));
                uint slot_0 = _S221 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S227)).xy), uint(((_S227)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S228 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S228)).xy), uint(((_S228)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S229 = atlasWidth_0;
                uint _S230 = 6U % atlasWidth_0;
                int _S231 = int(_S230);
                uint _S232 = 6U / _S229;
                int3 _S233 = int3(_S231, int(_S232), int(0));
                uint _S234 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S233)).xy), uint(((_S233)).z))).x * 255.0f));
                uint _S235 = atlasWidth_0;
                uint _S236 = 7U % atlasWidth_0;
                int _S237 = int(_S236);
                uint _S238 = 7U / _S235;
                int3 _S239 = int3(_S237, int(_S238), int(0));
                uint columns_0 = _S234 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S239)).xy), uint(((_S239)).z))).x * 255.0f)) << 8U);
                uint _S240 = atlasWidth_0;
                uint _S241 = 8U % atlasWidth_0;
                int _S242 = int(_S241);
                uint _S243 = 8U / _S240;
                int3 _S244 = int3(_S242, int(_S243), int(0));
                uint _S245 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S244)).xy), uint(((_S244)).z))).x * 255.0f));
                uint _S246 = atlasWidth_0;
                uint _S247 = 9U % atlasWidth_0;
                int _S248 = int(_S247);
                uint _S249 = 9U / _S246;
                int3 _S250 = int3(_S248, int(_S249), int(0));
                uint rows_0 = ((_S245 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S250)).xy), uint(((_S250)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S208 * _S214 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S251 = atlasWidth_0 / columns_0;
                float _S252 = float(_S251);
                uint _S253 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S252, float(_S253));
                uint _S254 = slot_1 % columns_0;
                float _S255 = float(_S254);
                uint _S256 = slot_1 / columns_0;
                _S182 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S255, float(_S256)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S180) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S257 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(_S180)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S257);
            }
            if(hasSceneLighting_0)
            {
                int3 _S258 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S258)).xy), uint(((_S258)).z)));
                break;
            }
            uint _S259 = atlasWidth_0 / uint(_S257.x);
            float _S260 = float(_S259);
            uint _S261 = (atlasHeight_0 - 1U) / uint(_S257.y);
            float2 cellSize_1 = float2(_S260, float(_S261));
            _S182 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S182;
        float4 _S262 = float4(_S7->compositeControls_0) ;
        float _S263 = _S262.x;
        if(_S263 == 0.0f)
        {
            hasSceneLighting_0 = (_S262.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S264 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S264).get_width(0)),(*((&sourceHeight_0)) = (_S264).get_height(0));
            texture2d<float, access::sample> _S265 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S265).get_width(0)),(*((&valueHeight_0)) = (_S265).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S266 = diffuseSample_0;
                for(;;)
                {
                    float _S267 = _S266.w;
                    if(_S267 > 1.0f)
                    {
                        diffuseColor_0 = _S266.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S268 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S268).get_width(0)),(*((&height_1)) = (_S268).get_height(0));
                    uint3 _S269 = uint3(round(_S266.xyz * float3(255.0f) ));
                    uint index_1 = ((_S269.x) | ((_S269.y) << 8U)) | ((_S269.z) << 16U);
                    if(_S267 == 0.0f)
                    {
                        _S183 = _S269;
                    }
                    else
                    {
                        _S183 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S183 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S270 = address_0.x;
                    uint _S271 = _S270 % width_1;
                    int _S272 = int(_S271);
                    uint _S273 = _S270 / width_1;
                    int3 _S274 = int3(_S272, int(_S273), int(0));
                    float _S275 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S274)).xy), uint(((_S274)).z))).x;
                    uint _S276 = address_0.y;
                    uint _S277 = _S276 % width_1;
                    int _S278 = int(_S277);
                    uint _S279 = _S276 / width_1;
                    int3 _S280 = int3(_S278, int(_S279), int(0));
                    float _S281 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S280)).xy), uint(((_S280)).z))).x;
                    uint _S282 = address_0.z;
                    uint _S283 = _S282 % width_1;
                    int _S284 = int(_S283);
                    uint _S285 = _S282 / width_1;
                    int3 _S286 = int3(_S284, int(_S285), int(0));
                    diffuseColor_0 = float3(_S275, _S281, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S286)).xy), uint(((_S286)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                if(_S185)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S186)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S287 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S287 == 0U)
                    {
                        _S181 = _S180;
                        break;
                    }
                    texture2d<float, access::sample> _S288 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S288).get_width(0)),(*((&height_2)) = (_S288).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S289 = float2(0.5f) ;
                    float2 remapped_1 = clamp((_S180 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S289 / size_1, (size_1 - _S289) / size_1);
                    if((_S287 & 1U) != 0U)
                    {
                        roughness_0 = remapped_1.x;
                    }
                    else
                    {
                        roughness_0 = _S180.x;
                    }
                    if((_S287 & 2U) != 0U)
                    {
                        metallic_0 = remapped_1.y;
                    }
                    else
                    {
                        metallic_0 = _S180.y;
                    }
                    _S181 = float2(roughness_0, metallic_0);
                    break;
                }
                diffuseSample_0.z = (((&kernelContext_0)->diffuseValuesTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S181))).x;
            }
        }
        float4 _S290 = diffuseSample_0;
        for(;;)
        {
            if(_S263 != 2.0f)
            {
                _S182 = _S290;
                break;
            }
            bool _S291 = (_S262.w) >= 0.5f;
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
            uint _S292 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S291)
                {
                    for(;;)
                    {
                        if(_S292 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S293 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_3;
                        thread uint height_3;
                        (*((&width_3)) = (_S293).get_width(0)),(*((&height_3)) = (_S293).get_height(0));
                        float2 size_2 = float2(float(width_3), float(height_3));
                        float2 _S294 = float2(0.5f) ;
                        float2 remapped_2 = clamp((_S180 * (size_2 - float2(2.0f) ) + float2(1.0f) ) / size_2, _S294 / size_2, (size_2 - _S294) / size_2);
                        if((_S292 & 1U) != 0U)
                        {
                            roughness_0 = remapped_2.x;
                        }
                        else
                        {
                            roughness_0 = _S180.x;
                        }
                        if((_S292 & 2U) != 0U)
                        {
                            metallic_0 = remapped_2.y;
                        }
                        else
                        {
                            metallic_0 = _S180.y;
                        }
                        _S180 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S180)));
                    break;
                }
                texture2d<float, access::sample> _S295 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S295).get_width(0)),(*((&atlasHeight_1)) = (_S295).get_height(0));
                int3 _S296 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S296)).xy), uint(((_S296)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S297 = atlasWidth_1;
                    uint _S298 = 2U % atlasWidth_1;
                    int _S299 = int(_S298);
                    uint _S300 = 2U / _S297;
                    int3 _S301 = int3(_S299, int(_S300), int(0));
                    int _S302 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S301)).xy), uint(((_S301)).z))).x * 255.0f)));
                    uint _S303 = atlasWidth_1;
                    uint _S304 = 3U % atlasWidth_1;
                    int _S305 = int(_S304);
                    uint _S306 = 3U / _S303;
                    int3 _S307 = int3(_S305, int(_S306), int(0));
                    int2 minimumTile_1 = int2(_S302, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S307)).xy), uint(((_S307)).z))).x * 255.0f))));
                    uint _S308 = atlasWidth_1;
                    uint _S309 = 4U % atlasWidth_1;
                    int _S310 = int(_S309);
                    uint _S311 = 4U / _S308;
                    int3 _S312 = int3(_S310, int(_S311), int(0));
                    uint _S313 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S312)).xy), uint(((_S312)).z))).x * 255.0f));
                    uint _S314 = atlasWidth_1;
                    uint _S315 = 5U % atlasWidth_1;
                    int _S316 = int(_S315);
                    uint _S317 = 5U / _S314;
                    int3 _S318 = int3(_S316, int(_S317), int(0));
                    uint _S319 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S318)).xy), uint(((_S318)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S313, _S319);
                    int2 tile_2 = int2(floor(_S180)) - minimumTile_1;
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
                        int3 _S320 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S320)).xy), uint(((_S320)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S313 + uint(tile_2.x));
                    uint _S321 = atlasWidth_1;
                    uint _S322 = offset_1 % atlasWidth_1;
                    int _S323 = int(_S322);
                    uint _S324 = offset_1 / _S321;
                    int3 _S325 = int3(_S323, int(_S324), int(0));
                    uint _S326 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S325)).xy), uint(((_S325)).z))).x * 255.0f));
                    uint _S327 = atlasWidth_1;
                    uint _S328 = offset_1 + 1U;
                    uint _S329 = _S328 % atlasWidth_1;
                    int _S330 = int(_S329);
                    uint _S331 = _S328 / _S327;
                    int3 _S332 = int3(_S330, int(_S331), int(0));
                    uint slot_2 = _S326 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S332)).xy), uint(((_S332)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S333 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S333)).xy), uint(((_S333)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S334 = atlasWidth_1;
                    uint _S335 = 6U % atlasWidth_1;
                    int _S336 = int(_S335);
                    uint _S337 = 6U / _S334;
                    int3 _S338 = int3(_S336, int(_S337), int(0));
                    uint _S339 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S338)).xy), uint(((_S338)).z))).x * 255.0f));
                    uint _S340 = atlasWidth_1;
                    uint _S341 = 7U % atlasWidth_1;
                    int _S342 = int(_S341);
                    uint _S343 = 7U / _S340;
                    int3 _S344 = int3(_S342, int(_S343), int(0));
                    uint columns_1 = _S339 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S344)).xy), uint(((_S344)).z))).x * 255.0f)) << 8U);
                    uint _S345 = atlasWidth_1;
                    uint _S346 = 8U % atlasWidth_1;
                    int _S347 = int(_S346);
                    uint _S348 = 8U / _S345;
                    int3 _S349 = int3(_S347, int(_S348), int(0));
                    uint _S350 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S349)).xy), uint(((_S349)).z))).x * 255.0f));
                    uint _S351 = atlasWidth_1;
                    uint _S352 = 9U % atlasWidth_1;
                    int _S353 = int(_S352);
                    uint _S354 = 9U / _S351;
                    int3 _S355 = int3(_S353, int(_S354), int(0));
                    uint rows_1 = ((_S350 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S355)).xy), uint(((_S355)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S313 * _S319 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S356 = atlasWidth_1 / columns_1;
                    float _S357 = float(_S356);
                    uint _S358 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S357, float(_S358));
                    uint _S359 = slot_3 % columns_1;
                    float _S360 = float(_S359);
                    uint _S361 = slot_3 / columns_1;
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S360, float(_S361)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S180) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S362 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(_S180)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S362);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S363 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S363)).xy), uint(((_S363)).z)));
                    break;
                }
                uint _S364 = atlasWidth_1 / uint(_S362.x);
                float _S365 = float(_S364);
                uint _S366 = (atlasHeight_1 - 1U) / uint(_S362.y);
                float2 cellSize_3 = float2(_S365, float(_S366));
                _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S262.y));
            if(operation_0 == 1U)
            {
                _S182 = _S290 * _S182;
                break;
            }
            if(operation_0 == 2U)
            {
                _S182 = _S290 + _S182;
                break;
            }
            if(operation_0 == 3U)
            {
                _S182 = _S290 - _S182;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S262.z;
                _S182 = _S290 * float4((1.0f - factor_0))  + _S182 * float4(factor_0) ;
                break;
            }
            _S182 = _S290;
            break;
        }
        diffuseColor_0 = _S182.xyz;
    }
    float4 _S367;
    if((textureMask_0 & 8U) != 0U)
    {
        bool useSecondary_1 = (secondaryMask_0 & 8U) != 0U;
        if((alternateMask_0 & 8U) != 0U)
        {
            if(useSecondary_1)
            {
                _S180 = _S179;
            }
            else
            {
                _S180 = _S178;
            }
        }
        else
        {
            if(useSecondary_1)
            {
                _S180 = _S171;
            }
            else
            {
                _S180 = _S170;
            }
        }
        bool _S368 = (udimMask_0 & 8U) != 0U;
        uint maskU_1 = uint(round(_S164.y));
        uint maskV_1 = uint(round(_S164.z));
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
        uint _S369 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S368)
            {
                for(;;)
                {
                    if(_S369 == 0U)
                    {
                        _S181 = _S180;
                        break;
                    }
                    texture2d<float, access::sample> _S370 = (&kernelContext_0)->roughnessMetallicTexture_0;
                    thread uint width_4;
                    thread uint height_4;
                    (*((&width_4)) = (_S370).get_width(0)),(*((&height_4)) = (_S370).get_height(0));
                    float2 size_3 = float2(float(width_4), float(height_4));
                    float2 _S371 = float2(0.5f) ;
                    float2 remapped_3 = clamp((_S180 * (size_3 - float2(2.0f) ) + float2(1.0f) ) / size_3, _S371 / size_3, (size_3 - _S371) / size_3);
                    if((_S369 & 1U) != 0U)
                    {
                        roughness_0 = remapped_3.x;
                    }
                    else
                    {
                        roughness_0 = _S180.x;
                    }
                    if((_S369 & 2U) != 0U)
                    {
                        metallic_0 = remapped_3.y;
                    }
                    else
                    {
                        metallic_0 = _S180.y;
                    }
                    _S181 = float2(roughness_0, metallic_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S372 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S372).get_width(0)),(*((&atlasHeight_2)) = (_S372).get_height(0));
            int3 _S373 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S373)).xy), uint(((_S373)).z))) * float4(255.0f) );
            if((metadata_2.x) == 255.0f)
            {
                uint _S374 = atlasWidth_2;
                uint _S375 = 2U % atlasWidth_2;
                int _S376 = int(_S375);
                uint _S377 = 2U / _S374;
                int3 _S378 = int3(_S376, int(_S377), int(0));
                int _S379 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S378)).xy), uint(((_S378)).z))).x * 255.0f)));
                uint _S380 = atlasWidth_2;
                uint _S381 = 3U % atlasWidth_2;
                int _S382 = int(_S381);
                uint _S383 = 3U / _S380;
                int3 _S384 = int3(_S382, int(_S383), int(0));
                int2 minimumTile_2 = int2(_S379, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S384)).xy), uint(((_S384)).z))).x * 255.0f))));
                uint _S385 = atlasWidth_2;
                uint _S386 = 4U % atlasWidth_2;
                int _S387 = int(_S386);
                uint _S388 = 4U / _S385;
                int3 _S389 = int3(_S387, int(_S388), int(0));
                uint _S390 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S389)).xy), uint(((_S389)).z))).x * 255.0f));
                uint _S391 = atlasWidth_2;
                uint _S392 = 5U % atlasWidth_2;
                int _S393 = int(_S392);
                uint _S394 = 5U / _S391;
                int3 _S395 = int3(_S393, int(_S394), int(0));
                uint _S396 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S395)).xy), uint(((_S395)).z))).x * 255.0f));
                uint2 grid_2 = uint2(_S390, _S396);
                int2 tile_4 = int2(floor(_S180)) - minimumTile_2;
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
                    int3 _S397 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S397)).xy), uint(((_S397)).z)));
                    break;
                }
                uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S390 + uint(tile_4.x));
                uint _S398 = atlasWidth_2;
                uint _S399 = offset_2 % atlasWidth_2;
                int _S400 = int(_S399);
                uint _S401 = offset_2 / _S398;
                int3 _S402 = int3(_S400, int(_S401), int(0));
                uint _S403 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S402)).xy), uint(((_S402)).z))).x * 255.0f));
                uint _S404 = atlasWidth_2;
                uint _S405 = offset_2 + 1U;
                uint _S406 = _S405 % atlasWidth_2;
                int _S407 = int(_S406);
                uint _S408 = _S405 / _S404;
                int3 _S409 = int3(_S407, int(_S408), int(0));
                uint slot_4 = _S403 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S409)).xy), uint(((_S409)).z))).x * 255.0f)) << 8U);
                if(slot_4 == 0U)
                {
                    int3 _S410 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S410)).xy), uint(((_S410)).z)));
                    break;
                }
                uint slot_5 = slot_4 - 1U;
                uint _S411 = atlasWidth_2;
                uint _S412 = 6U % atlasWidth_2;
                int _S413 = int(_S412);
                uint _S414 = 6U / _S411;
                int3 _S415 = int3(_S413, int(_S414), int(0));
                uint _S416 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S415)).xy), uint(((_S415)).z))).x * 255.0f));
                uint _S417 = atlasWidth_2;
                uint _S418 = 7U % atlasWidth_2;
                int _S419 = int(_S418);
                uint _S420 = 7U / _S417;
                int3 _S421 = int3(_S419, int(_S420), int(0));
                uint columns_2 = _S416 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S421)).xy), uint(((_S421)).z))).x * 255.0f)) << 8U);
                uint _S422 = atlasWidth_2;
                uint _S423 = 8U % atlasWidth_2;
                int _S424 = int(_S423);
                uint _S425 = 8U / _S422;
                int3 _S426 = int3(_S424, int(_S425), int(0));
                uint _S427 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S426)).xy), uint(((_S426)).z))).x * 255.0f));
                uint _S428 = atlasWidth_2;
                uint _S429 = 9U % atlasWidth_2;
                int _S430 = int(_S429);
                uint _S431 = 9U / _S428;
                int3 _S432 = int3(_S430, int(_S431), int(0));
                uint rows_2 = ((_S427 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S432)).xy), uint(((_S432)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
                uint metadataRows_2 = (10U + 2U * _S390 * _S396 + atlasWidth_2 - 1U) / atlasWidth_2;
                uint _S433 = atlasWidth_2 / columns_2;
                float _S434 = float(_S433);
                uint _S435 = (atlasHeight_2 - metadataRows_2) / rows_2;
                float2 cellSize_4 = float2(_S434, float(_S435));
                uint _S436 = slot_5 % columns_2;
                float _S437 = float(_S436);
                uint _S438 = slot_5 / columns_2;
                _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S437, float(_S438)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(_S180) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
                break;
            }
            int2 _S439 = int2(metadata_2.zw);
            int2 tile_5 = int2(floor(_S180)) - int2(metadata_2.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S439);
            }
            if(hasSceneLighting_0)
            {
                int3 _S440 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S440)).xy), uint(((_S440)).z)));
                break;
            }
            uint _S441 = atlasWidth_2 / uint(_S439.x);
            float _S442 = float(_S441);
            uint _S443 = (atlasHeight_2 - 1U) / uint(_S439.y);
            float2 cellSize_5 = float2(_S442, float(_S443));
            _S182 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        for(;;)
        {
            float4 _S444 = float4(_S7->compositeControls_0) ;
            if((_S444.x) != 8.0f)
            {
                break;
            }
            bool _S445 = (_S444.w) >= 0.5f;
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
            uint _S446 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S445)
                {
                    for(;;)
                    {
                        if(_S446 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S447 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_5;
                        thread uint height_5;
                        (*((&width_5)) = (_S447).get_width(0)),(*((&height_5)) = (_S447).get_height(0));
                        float2 size_4 = float2(float(width_5), float(height_5));
                        float2 _S448 = float2(0.5f) ;
                        float2 remapped_4 = clamp((_S180 * (size_4 - float2(2.0f) ) + float2(1.0f) ) / size_4, _S448 / size_4, (size_4 - _S448) / size_4);
                        if((_S446 & 1U) != 0U)
                        {
                            roughness_0 = remapped_4.x;
                        }
                        else
                        {
                            roughness_0 = _S180.x;
                        }
                        if((_S446 & 2U) != 0U)
                        {
                            metallic_0 = remapped_4.y;
                        }
                        else
                        {
                            metallic_0 = _S180.y;
                        }
                        _S180 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S180)));
                    break;
                }
                texture2d<float, access::sample> _S449 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_3;
                thread uint atlasHeight_3;
                (*((&atlasWidth_3)) = (_S449).get_width(0)),(*((&atlasHeight_3)) = (_S449).get_height(0));
                int3 _S450 = int3(int(0), int(0), int(0));
                float4 metadata_3 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S450)).xy), uint(((_S450)).z))) * float4(255.0f) );
                if((metadata_3.x) == 255.0f)
                {
                    uint _S451 = atlasWidth_3;
                    uint _S452 = 2U % atlasWidth_3;
                    int _S453 = int(_S452);
                    uint _S454 = 2U / _S451;
                    int3 _S455 = int3(_S453, int(_S454), int(0));
                    int _S456 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S455)).xy), uint(((_S455)).z))).x * 255.0f)));
                    uint _S457 = atlasWidth_3;
                    uint _S458 = 3U % atlasWidth_3;
                    int _S459 = int(_S458);
                    uint _S460 = 3U / _S457;
                    int3 _S461 = int3(_S459, int(_S460), int(0));
                    int2 minimumTile_3 = int2(_S456, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S461)).xy), uint(((_S461)).z))).x * 255.0f))));
                    uint _S462 = atlasWidth_3;
                    uint _S463 = 4U % atlasWidth_3;
                    int _S464 = int(_S463);
                    uint _S465 = 4U / _S462;
                    int3 _S466 = int3(_S464, int(_S465), int(0));
                    uint _S467 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S466)).xy), uint(((_S466)).z))).x * 255.0f));
                    uint _S468 = atlasWidth_3;
                    uint _S469 = 5U % atlasWidth_3;
                    int _S470 = int(_S469);
                    uint _S471 = 5U / _S468;
                    int3 _S472 = int3(_S470, int(_S471), int(0));
                    uint _S473 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S472)).xy), uint(((_S472)).z))).x * 255.0f));
                    uint2 grid_3 = uint2(_S467, _S473);
                    int2 tile_6 = int2(floor(_S180)) - minimumTile_3;
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
                        int3 _S474 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S474)).xy), uint(((_S474)).z)));
                        break;
                    }
                    uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S467 + uint(tile_6.x));
                    uint _S475 = atlasWidth_3;
                    uint _S476 = offset_3 % atlasWidth_3;
                    int _S477 = int(_S476);
                    uint _S478 = offset_3 / _S475;
                    int3 _S479 = int3(_S477, int(_S478), int(0));
                    uint _S480 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S479)).xy), uint(((_S479)).z))).x * 255.0f));
                    uint _S481 = atlasWidth_3;
                    uint _S482 = offset_3 + 1U;
                    uint _S483 = _S482 % atlasWidth_3;
                    int _S484 = int(_S483);
                    uint _S485 = _S482 / _S481;
                    int3 _S486 = int3(_S484, int(_S485), int(0));
                    uint slot_6 = _S480 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S486)).xy), uint(((_S486)).z))).x * 255.0f)) << 8U);
                    if(slot_6 == 0U)
                    {
                        int3 _S487 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S487)).xy), uint(((_S487)).z)));
                        break;
                    }
                    uint slot_7 = slot_6 - 1U;
                    uint _S488 = atlasWidth_3;
                    uint _S489 = 6U % atlasWidth_3;
                    int _S490 = int(_S489);
                    uint _S491 = 6U / _S488;
                    int3 _S492 = int3(_S490, int(_S491), int(0));
                    uint _S493 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S492)).xy), uint(((_S492)).z))).x * 255.0f));
                    uint _S494 = atlasWidth_3;
                    uint _S495 = 7U % atlasWidth_3;
                    int _S496 = int(_S495);
                    uint _S497 = 7U / _S494;
                    int3 _S498 = int3(_S496, int(_S497), int(0));
                    uint columns_3 = _S493 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S498)).xy), uint(((_S498)).z))).x * 255.0f)) << 8U);
                    uint _S499 = atlasWidth_3;
                    uint _S500 = 8U % atlasWidth_3;
                    int _S501 = int(_S500);
                    uint _S502 = 8U / _S499;
                    int3 _S503 = int3(_S501, int(_S502), int(0));
                    uint _S504 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S503)).xy), uint(((_S503)).z))).x * 255.0f));
                    uint _S505 = atlasWidth_3;
                    uint _S506 = 9U % atlasWidth_3;
                    int _S507 = int(_S506);
                    uint _S508 = 9U / _S505;
                    int3 _S509 = int3(_S507, int(_S508), int(0));
                    uint rows_3 = ((_S504 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S509)).xy), uint(((_S509)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                    uint metadataRows_3 = (10U + 2U * _S467 * _S473 + atlasWidth_3 - 1U) / atlasWidth_3;
                    uint _S510 = atlasWidth_3 / columns_3;
                    float _S511 = float(_S510);
                    uint _S512 = (atlasHeight_3 - metadataRows_3) / rows_3;
                    float2 cellSize_6 = float2(_S511, float(_S512));
                    uint _S513 = slot_7 % columns_3;
                    float _S514 = float(_S513);
                    uint _S515 = slot_7 / columns_3;
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S514, float(_S515)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(_S180) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                    break;
                }
                int2 _S516 = int2(metadata_3.zw);
                int2 tile_7 = int2(floor(_S180)) - int2(metadata_3.xy);
                if(any(tile_7 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_7 >= _S516);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S517 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                    _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S517)).xy), uint(((_S517)).z)));
                    break;
                }
                uint _S518 = atlasWidth_3 / uint(_S516.x);
                float _S519 = float(_S518);
                uint _S520 = (atlasHeight_3 - 1U) / uint(_S516.y);
                float2 cellSize_7 = float2(_S519, float(_S520));
                _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            uint operation_1 = uint(round(_S444.y));
            if(operation_1 == 1U)
            {
                _S182 = _S182 * _S367;
                break;
            }
            if(operation_1 == 2U)
            {
                _S182 = _S182 + _S367;
                break;
            }
            if(operation_1 == 3U)
            {
                _S182 = _S182 - _S367;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S444.z;
                _S182 = _S182 * float4((1.0f - factor_1))  + _S367 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S182.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S156;
    }
    float occlusion_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool useSecondary_2 = (secondaryMask_0 & 32U) != 0U;
        if((alternateMask_0 & 32U) != 0U)
        {
            if(useSecondary_2)
            {
                _S180 = _S179;
            }
            else
            {
                _S180 = _S178;
            }
        }
        else
        {
            if(useSecondary_2)
            {
                _S180 = _S171;
            }
            else
            {
                _S180 = _S170;
            }
        }
        bool _S521 = (udimMask_0 & 32U) != 0U;
        uint maskU_2 = uint(round(_S164.y));
        uint maskV_2 = uint(round(_S164.z));
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
        uint _S522 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S521)
            {
                for(;;)
                {
                    if(_S522 == 0U)
                    {
                        _S181 = _S180;
                        break;
                    }
                    texture2d<float, access::sample> _S523 = (&kernelContext_0)->metallicTexture_0;
                    thread uint width_6;
                    thread uint height_6;
                    (*((&width_6)) = (_S523).get_width(0)),(*((&height_6)) = (_S523).get_height(0));
                    float2 size_5 = float2(float(width_6), float(height_6));
                    float2 _S524 = float2(0.5f) ;
                    float2 remapped_5 = clamp((_S180 * (size_5 - float2(2.0f) ) + float2(1.0f) ) / size_5, _S524 / size_5, (size_5 - _S524) / size_5);
                    if((_S522 & 1U) != 0U)
                    {
                        metallic_0 = remapped_5.x;
                    }
                    else
                    {
                        metallic_0 = _S180.x;
                    }
                    if((_S522 & 2U) != 0U)
                    {
                        occlusion_0 = remapped_5.y;
                    }
                    else
                    {
                        occlusion_0 = _S180.y;
                    }
                    _S181 = float2(metallic_0, occlusion_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S525 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S525).get_width(0)),(*((&atlasHeight_4)) = (_S525).get_height(0));
            int3 _S526 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S526)).xy), uint(((_S526)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S527 = atlasWidth_4;
                uint _S528 = 2U % atlasWidth_4;
                int _S529 = int(_S528);
                uint _S530 = 2U / _S527;
                int3 _S531 = int3(_S529, int(_S530), int(0));
                int _S532 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S531)).xy), uint(((_S531)).z))).x * 255.0f)));
                uint _S533 = atlasWidth_4;
                uint _S534 = 3U % atlasWidth_4;
                int _S535 = int(_S534);
                uint _S536 = 3U / _S533;
                int3 _S537 = int3(_S535, int(_S536), int(0));
                int2 minimumTile_4 = int2(_S532, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S537)).xy), uint(((_S537)).z))).x * 255.0f))));
                uint _S538 = atlasWidth_4;
                uint _S539 = 4U % atlasWidth_4;
                int _S540 = int(_S539);
                uint _S541 = 4U / _S538;
                int3 _S542 = int3(_S540, int(_S541), int(0));
                uint _S543 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S542)).xy), uint(((_S542)).z))).x * 255.0f));
                uint _S544 = atlasWidth_4;
                uint _S545 = 5U % atlasWidth_4;
                int _S546 = int(_S545);
                uint _S547 = 5U / _S544;
                int3 _S548 = int3(_S546, int(_S547), int(0));
                uint _S549 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S548)).xy), uint(((_S548)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S543, _S549);
                int2 tile_8 = int2(floor(_S180)) - minimumTile_4;
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
                    int3 _S550 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S550)).xy), uint(((_S550)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S543 + uint(tile_8.x));
                uint _S551 = atlasWidth_4;
                uint _S552 = offset_4 % atlasWidth_4;
                int _S553 = int(_S552);
                uint _S554 = offset_4 / _S551;
                int3 _S555 = int3(_S553, int(_S554), int(0));
                uint _S556 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S555)).xy), uint(((_S555)).z))).x * 255.0f));
                uint _S557 = atlasWidth_4;
                uint _S558 = offset_4 + 1U;
                uint _S559 = _S558 % atlasWidth_4;
                int _S560 = int(_S559);
                uint _S561 = _S558 / _S557;
                int3 _S562 = int3(_S560, int(_S561), int(0));
                uint slot_8 = _S556 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S562)).xy), uint(((_S562)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S563 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S563)).xy), uint(((_S563)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S564 = atlasWidth_4;
                uint _S565 = 6U % atlasWidth_4;
                int _S566 = int(_S565);
                uint _S567 = 6U / _S564;
                int3 _S568 = int3(_S566, int(_S567), int(0));
                uint _S569 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S568)).xy), uint(((_S568)).z))).x * 255.0f));
                uint _S570 = atlasWidth_4;
                uint _S571 = 7U % atlasWidth_4;
                int _S572 = int(_S571);
                uint _S573 = 7U / _S570;
                int3 _S574 = int3(_S572, int(_S573), int(0));
                uint columns_4 = _S569 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S574)).xy), uint(((_S574)).z))).x * 255.0f)) << 8U);
                uint _S575 = atlasWidth_4;
                uint _S576 = 8U % atlasWidth_4;
                int _S577 = int(_S576);
                uint _S578 = 8U / _S575;
                int3 _S579 = int3(_S577, int(_S578), int(0));
                uint _S580 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S579)).xy), uint(((_S579)).z))).x * 255.0f));
                uint _S581 = atlasWidth_4;
                uint _S582 = 9U % atlasWidth_4;
                int _S583 = int(_S582);
                uint _S584 = 9U / _S581;
                int3 _S585 = int3(_S583, int(_S584), int(0));
                uint rows_4 = ((_S580 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S585)).xy), uint(((_S585)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S543 * _S549 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S586 = atlasWidth_4 / columns_4;
                float _S587 = float(_S586);
                uint _S588 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S587, float(_S588));
                uint _S589 = slot_9 % columns_4;
                float _S590 = float(_S589);
                uint _S591 = slot_9 / columns_4;
                _S182 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S590, float(_S591)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(_S180) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S592 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(_S180)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S592);
            }
            if(hasSceneLighting_0)
            {
                int3 _S593 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S593)).xy), uint(((_S593)).z)));
                break;
            }
            uint _S594 = atlasWidth_4 / uint(_S592.x);
            float _S595 = float(_S594);
            uint _S596 = (atlasHeight_4 - 1U) / uint(_S592.y);
            float2 cellSize_9 = float2(_S595, float(_S596));
            _S182 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S597 = float4(_S7->compositeControls_0) ;
            if((_S597.x) != 32.0f)
            {
                break;
            }
            bool _S598 = (_S597.w) >= 0.5f;
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
            uint _S599 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S598)
                {
                    for(;;)
                    {
                        if(_S599 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S600 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_7;
                        thread uint height_7;
                        (*((&width_7)) = (_S600).get_width(0)),(*((&height_7)) = (_S600).get_height(0));
                        float2 size_6 = float2(float(width_7), float(height_7));
                        float2 _S601 = float2(0.5f) ;
                        float2 remapped_6 = clamp((_S180 * (size_6 - float2(2.0f) ) + float2(1.0f) ) / size_6, _S601 / size_6, (size_6 - _S601) / size_6);
                        if((_S599 & 1U) != 0U)
                        {
                            metallic_0 = remapped_6.x;
                        }
                        else
                        {
                            metallic_0 = _S180.x;
                        }
                        if((_S599 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_6.y;
                        }
                        else
                        {
                            occlusion_0 = _S180.y;
                        }
                        _S180 = float2(metallic_0, occlusion_0);
                        break;
                    }
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S180)));
                    break;
                }
                texture2d<float, access::sample> _S602 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S602).get_width(0)),(*((&atlasHeight_5)) = (_S602).get_height(0));
                int3 _S603 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S603)).xy), uint(((_S603)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S604 = atlasWidth_5;
                    uint _S605 = 2U % atlasWidth_5;
                    int _S606 = int(_S605);
                    uint _S607 = 2U / _S604;
                    int3 _S608 = int3(_S606, int(_S607), int(0));
                    int _S609 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S608)).xy), uint(((_S608)).z))).x * 255.0f)));
                    uint _S610 = atlasWidth_5;
                    uint _S611 = 3U % atlasWidth_5;
                    int _S612 = int(_S611);
                    uint _S613 = 3U / _S610;
                    int3 _S614 = int3(_S612, int(_S613), int(0));
                    int2 minimumTile_5 = int2(_S609, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S614)).xy), uint(((_S614)).z))).x * 255.0f))));
                    uint _S615 = atlasWidth_5;
                    uint _S616 = 4U % atlasWidth_5;
                    int _S617 = int(_S616);
                    uint _S618 = 4U / _S615;
                    int3 _S619 = int3(_S617, int(_S618), int(0));
                    uint _S620 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S619)).xy), uint(((_S619)).z))).x * 255.0f));
                    uint _S621 = atlasWidth_5;
                    uint _S622 = 5U % atlasWidth_5;
                    int _S623 = int(_S622);
                    uint _S624 = 5U / _S621;
                    int3 _S625 = int3(_S623, int(_S624), int(0));
                    uint _S626 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S625)).xy), uint(((_S625)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S620, _S626);
                    int2 tile_10 = int2(floor(_S180)) - minimumTile_5;
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
                        int3 _S627 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S627)).xy), uint(((_S627)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S620 + uint(tile_10.x));
                    uint _S628 = atlasWidth_5;
                    uint _S629 = offset_5 % atlasWidth_5;
                    int _S630 = int(_S629);
                    uint _S631 = offset_5 / _S628;
                    int3 _S632 = int3(_S630, int(_S631), int(0));
                    uint _S633 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S632)).xy), uint(((_S632)).z))).x * 255.0f));
                    uint _S634 = atlasWidth_5;
                    uint _S635 = offset_5 + 1U;
                    uint _S636 = _S635 % atlasWidth_5;
                    int _S637 = int(_S636);
                    uint _S638 = _S635 / _S634;
                    int3 _S639 = int3(_S637, int(_S638), int(0));
                    uint slot_10 = _S633 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S639)).xy), uint(((_S639)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S640 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S640)).xy), uint(((_S640)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S641 = atlasWidth_5;
                    uint _S642 = 6U % atlasWidth_5;
                    int _S643 = int(_S642);
                    uint _S644 = 6U / _S641;
                    int3 _S645 = int3(_S643, int(_S644), int(0));
                    uint _S646 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S645)).xy), uint(((_S645)).z))).x * 255.0f));
                    uint _S647 = atlasWidth_5;
                    uint _S648 = 7U % atlasWidth_5;
                    int _S649 = int(_S648);
                    uint _S650 = 7U / _S647;
                    int3 _S651 = int3(_S649, int(_S650), int(0));
                    uint columns_5 = _S646 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S651)).xy), uint(((_S651)).z))).x * 255.0f)) << 8U);
                    uint _S652 = atlasWidth_5;
                    uint _S653 = 8U % atlasWidth_5;
                    int _S654 = int(_S653);
                    uint _S655 = 8U / _S652;
                    int3 _S656 = int3(_S654, int(_S655), int(0));
                    uint _S657 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S656)).xy), uint(((_S656)).z))).x * 255.0f));
                    uint _S658 = atlasWidth_5;
                    uint _S659 = 9U % atlasWidth_5;
                    int _S660 = int(_S659);
                    uint _S661 = 9U / _S658;
                    int3 _S662 = int3(_S660, int(_S661), int(0));
                    uint rows_5 = ((_S657 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S662)).xy), uint(((_S662)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S620 * _S626 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S663 = atlasWidth_5 / columns_5;
                    float _S664 = float(_S663);
                    uint _S665 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S664, float(_S665));
                    uint _S666 = slot_11 % columns_5;
                    float _S667 = float(_S666);
                    uint _S668 = slot_11 / columns_5;
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S667, float(_S668)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(_S180) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S669 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(_S180)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S669);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S670 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S670)).xy), uint(((_S670)).z)));
                    break;
                }
                uint _S671 = atlasWidth_5 / uint(_S669.x);
                float _S672 = float(_S671);
                uint _S673 = (atlasHeight_5 - 1U) / uint(_S669.y);
                float2 cellSize_11 = float2(_S672, float(_S673));
                _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_2 = uint(round(_S597.y));
            if(operation_2 == 1U)
            {
                _S182 = _S182 * _S367;
                break;
            }
            if(operation_2 == 2U)
            {
                _S182 = _S182 + _S367;
                break;
            }
            if(operation_2 == 3U)
            {
                _S182 = _S182 - _S367;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S597.z;
                _S182 = _S182 * float4((1.0f - factor_2))  + _S367 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S182.x);
    }
    else
    {
        metallic_0 = _S155;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 16U) != 0U)
    {
        bool useSecondary_3 = (secondaryMask_0 & 16U) != 0U;
        if((alternateMask_0 & 16U) != 0U)
        {
            if(useSecondary_3)
            {
                _S180 = _S179;
            }
            else
            {
                _S180 = _S178;
            }
        }
        else
        {
            if(useSecondary_3)
            {
                _S180 = _S171;
            }
            else
            {
                _S180 = _S170;
            }
        }
        thread float4 emissionSample_0;
        bool _S674 = (udimMask_0 & 16U) != 0U;
        uint maskU_3 = uint(round(_S164.y));
        uint maskV_3 = uint(round(_S164.z));
        bool _S675 = (maskU_3 & 16U) != 0U;
        if(_S675)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S676 = (maskV_3 & 16U) != 0U;
        if(_S676)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S677 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S674)
            {
                for(;;)
                {
                    if(_S677 == 0U)
                    {
                        _S181 = _S180;
                        break;
                    }
                    texture2d<float, access::sample> _S678 = (&kernelContext_0)->emissiveTexture_0;
                    thread uint width_8;
                    thread uint height_8;
                    (*((&width_8)) = (_S678).get_width(0)),(*((&height_8)) = (_S678).get_height(0));
                    float2 size_7 = float2(float(width_8), float(height_8));
                    float2 _S679 = float2(0.5f) ;
                    float2 remapped_7 = clamp((_S180 * (size_7 - float2(2.0f) ) + float2(1.0f) ) / size_7, _S679 / size_7, (size_7 - _S679) / size_7);
                    if((_S677 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_7.x;
                    }
                    else
                    {
                        occlusion_0 = _S180.x;
                    }
                    if((_S677 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_7.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S180.y;
                    }
                    _S181 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S680 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S680).get_width(0)),(*((&atlasHeight_6)) = (_S680).get_height(0));
            int3 _S681 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S681)).xy), uint(((_S681)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S682 = atlasWidth_6;
                uint _S683 = 2U % atlasWidth_6;
                int _S684 = int(_S683);
                uint _S685 = 2U / _S682;
                int3 _S686 = int3(_S684, int(_S685), int(0));
                int _S687 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S686)).xy), uint(((_S686)).z))).x * 255.0f)));
                uint _S688 = atlasWidth_6;
                uint _S689 = 3U % atlasWidth_6;
                int _S690 = int(_S689);
                uint _S691 = 3U / _S688;
                int3 _S692 = int3(_S690, int(_S691), int(0));
                int2 minimumTile_6 = int2(_S687, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S692)).xy), uint(((_S692)).z))).x * 255.0f))));
                uint _S693 = atlasWidth_6;
                uint _S694 = 4U % atlasWidth_6;
                int _S695 = int(_S694);
                uint _S696 = 4U / _S693;
                int3 _S697 = int3(_S695, int(_S696), int(0));
                uint _S698 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S697)).xy), uint(((_S697)).z))).x * 255.0f));
                uint _S699 = atlasWidth_6;
                uint _S700 = 5U % atlasWidth_6;
                int _S701 = int(_S700);
                uint _S702 = 5U / _S699;
                int3 _S703 = int3(_S701, int(_S702), int(0));
                uint _S704 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S703)).xy), uint(((_S703)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S698, _S704);
                int2 tile_12 = int2(floor(_S180)) - minimumTile_6;
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
                    int3 _S705 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S705)).xy), uint(((_S705)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S698 + uint(tile_12.x));
                uint _S706 = atlasWidth_6;
                uint _S707 = offset_6 % atlasWidth_6;
                int _S708 = int(_S707);
                uint _S709 = offset_6 / _S706;
                int3 _S710 = int3(_S708, int(_S709), int(0));
                uint _S711 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S710)).xy), uint(((_S710)).z))).x * 255.0f));
                uint _S712 = atlasWidth_6;
                uint _S713 = offset_6 + 1U;
                uint _S714 = _S713 % atlasWidth_6;
                int _S715 = int(_S714);
                uint _S716 = _S713 / _S712;
                int3 _S717 = int3(_S715, int(_S716), int(0));
                uint slot_12 = _S711 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S717)).xy), uint(((_S717)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S718 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S718)).xy), uint(((_S718)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S719 = atlasWidth_6;
                uint _S720 = 6U % atlasWidth_6;
                int _S721 = int(_S720);
                uint _S722 = 6U / _S719;
                int3 _S723 = int3(_S721, int(_S722), int(0));
                uint _S724 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S723)).xy), uint(((_S723)).z))).x * 255.0f));
                uint _S725 = atlasWidth_6;
                uint _S726 = 7U % atlasWidth_6;
                int _S727 = int(_S726);
                uint _S728 = 7U / _S725;
                int3 _S729 = int3(_S727, int(_S728), int(0));
                uint columns_6 = _S724 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S729)).xy), uint(((_S729)).z))).x * 255.0f)) << 8U);
                uint _S730 = atlasWidth_6;
                uint _S731 = 8U % atlasWidth_6;
                int _S732 = int(_S731);
                uint _S733 = 8U / _S730;
                int3 _S734 = int3(_S732, int(_S733), int(0));
                uint _S735 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S734)).xy), uint(((_S734)).z))).x * 255.0f));
                uint _S736 = atlasWidth_6;
                uint _S737 = 9U % atlasWidth_6;
                int _S738 = int(_S737);
                uint _S739 = 9U / _S736;
                int3 _S740 = int3(_S738, int(_S739), int(0));
                uint rows_6 = ((_S735 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S740)).xy), uint(((_S740)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S698 * _S704 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S741 = atlasWidth_6 / columns_6;
                float _S742 = float(_S741);
                uint _S743 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S742, float(_S743));
                uint _S744 = slot_13 % columns_6;
                float _S745 = float(_S744);
                uint _S746 = slot_13 / columns_6;
                _S182 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S745, float(_S746)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(_S180) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S747 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(_S180)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S747);
            }
            if(hasSceneLighting_0)
            {
                int3 _S748 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S748)).xy), uint(((_S748)).z)));
                break;
            }
            uint _S749 = atlasWidth_6 / uint(_S747.x);
            float _S750 = float(_S749);
            uint _S751 = (atlasHeight_6 - 1U) / uint(_S747.y);
            float2 cellSize_13 = float2(_S750, float(_S751));
            _S182 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        emissionSample_0 = _S182;
        if((_S165.w) != 0.0f)
        {
            texture2d<float, access::sample> _S752 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S752).get_width(0)),(*((&sourceHeight_1)) = (_S752).get_height(0));
            texture2d<float, access::sample> _S753 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S753).get_width(0)),(*((&valueHeight_1)) = (_S753).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S754 = emissionSample_0;
                for(;;)
                {
                    float _S755 = _S754.w;
                    if(_S755 > 1.0f)
                    {
                        unlitColor_0 = _S754.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S756 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_9;
                    thread uint height_9;
                    (*((&width_9)) = (_S756).get_width(0)),(*((&height_9)) = (_S756).get_height(0));
                    uint3 _S757 = uint3(round(_S754.xyz * float3(255.0f) ));
                    uint index_2 = ((_S757.x) | ((_S757.y) << 8U)) | ((_S757.z) << 16U);
                    if(_S755 == 0.0f)
                    {
                        _S183 = _S757;
                    }
                    else
                    {
                        _S183 = uint3(index_2, index_2, index_2);
                    }
                    uint3 address_1 = _S183 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S758 = address_1.x;
                    uint _S759 = _S758 % width_9;
                    int _S760 = int(_S759);
                    uint _S761 = _S758 / width_9;
                    int3 _S762 = int3(_S760, int(_S761), int(0));
                    float _S763 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S762)).xy), uint(((_S762)).z))).x;
                    uint _S764 = address_1.y;
                    uint _S765 = _S764 % width_9;
                    int _S766 = int(_S765);
                    uint _S767 = _S764 / width_9;
                    int3 _S768 = int3(_S766, int(_S767), int(0));
                    float _S769 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S768)).xy), uint(((_S768)).z))).x;
                    uint _S770 = address_1.z;
                    uint _S771 = _S770 % width_9;
                    int _S772 = int(_S771);
                    uint _S773 = _S770 / width_9;
                    int3 _S774 = int3(_S772, int(_S773), int(0));
                    unlitColor_0 = float3(_S763, _S769, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S774)).xy), uint(((_S774)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                if(_S675)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S676)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S775 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S775 == 0U)
                    {
                        _S181 = _S180;
                        break;
                    }
                    texture2d<float, access::sample> _S776 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_10;
                    thread uint height_10;
                    (*((&width_10)) = (_S776).get_width(0)),(*((&height_10)) = (_S776).get_height(0));
                    float2 size_8 = float2(float(width_10), float(height_10));
                    float2 _S777 = float2(0.5f) ;
                    float2 remapped_8 = clamp((_S180 * (size_8 - float2(2.0f) ) + float2(1.0f) ) / size_8, _S777 / size_8, (size_8 - _S777) / size_8);
                    if((_S775 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_8.x;
                    }
                    else
                    {
                        occlusion_0 = _S180.x;
                    }
                    if((_S775 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_8.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S180.y;
                    }
                    _S181 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                emissionSample_0.z = (((&kernelContext_0)->emissiveValuesTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S181))).x;
            }
        }
        float4 _S778 = emissionSample_0;
        for(;;)
        {
            float4 _S779 = float4(_S7->compositeControls_0) ;
            if((_S779.x) != 16.0f)
            {
                _S182 = _S778;
                break;
            }
            bool _S780 = (_S779.w) >= 0.5f;
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
            uint _S781 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S780)
                {
                    for(;;)
                    {
                        if(_S781 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S782 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_11;
                        thread uint height_11;
                        (*((&width_11)) = (_S782).get_width(0)),(*((&height_11)) = (_S782).get_height(0));
                        float2 size_9 = float2(float(width_11), float(height_11));
                        float2 _S783 = float2(0.5f) ;
                        float2 remapped_9 = clamp((_S180 * (size_9 - float2(2.0f) ) + float2(1.0f) ) / size_9, _S783 / size_9, (size_9 - _S783) / size_9);
                        if((_S781 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_9.x;
                        }
                        else
                        {
                            occlusion_0 = _S180.x;
                        }
                        if((_S781 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_9.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S180.y;
                        }
                        _S180 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S180)));
                    break;
                }
                texture2d<float, access::sample> _S784 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S784).get_width(0)),(*((&atlasHeight_7)) = (_S784).get_height(0));
                int3 _S785 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S785)).xy), uint(((_S785)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S786 = atlasWidth_7;
                    uint _S787 = 2U % atlasWidth_7;
                    int _S788 = int(_S787);
                    uint _S789 = 2U / _S786;
                    int3 _S790 = int3(_S788, int(_S789), int(0));
                    int _S791 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S790)).xy), uint(((_S790)).z))).x * 255.0f)));
                    uint _S792 = atlasWidth_7;
                    uint _S793 = 3U % atlasWidth_7;
                    int _S794 = int(_S793);
                    uint _S795 = 3U / _S792;
                    int3 _S796 = int3(_S794, int(_S795), int(0));
                    int2 minimumTile_7 = int2(_S791, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S796)).xy), uint(((_S796)).z))).x * 255.0f))));
                    uint _S797 = atlasWidth_7;
                    uint _S798 = 4U % atlasWidth_7;
                    int _S799 = int(_S798);
                    uint _S800 = 4U / _S797;
                    int3 _S801 = int3(_S799, int(_S800), int(0));
                    uint _S802 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S801)).xy), uint(((_S801)).z))).x * 255.0f));
                    uint _S803 = atlasWidth_7;
                    uint _S804 = 5U % atlasWidth_7;
                    int _S805 = int(_S804);
                    uint _S806 = 5U / _S803;
                    int3 _S807 = int3(_S805, int(_S806), int(0));
                    uint _S808 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S807)).xy), uint(((_S807)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S802, _S808);
                    int2 tile_14 = int2(floor(_S180)) - minimumTile_7;
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
                        int3 _S809 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S809)).xy), uint(((_S809)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S802 + uint(tile_14.x));
                    uint _S810 = atlasWidth_7;
                    uint _S811 = offset_7 % atlasWidth_7;
                    int _S812 = int(_S811);
                    uint _S813 = offset_7 / _S810;
                    int3 _S814 = int3(_S812, int(_S813), int(0));
                    uint _S815 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S814)).xy), uint(((_S814)).z))).x * 255.0f));
                    uint _S816 = atlasWidth_7;
                    uint _S817 = offset_7 + 1U;
                    uint _S818 = _S817 % atlasWidth_7;
                    int _S819 = int(_S818);
                    uint _S820 = _S817 / _S816;
                    int3 _S821 = int3(_S819, int(_S820), int(0));
                    uint slot_14 = _S815 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S821)).xy), uint(((_S821)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S822 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S822)).xy), uint(((_S822)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S823 = atlasWidth_7;
                    uint _S824 = 6U % atlasWidth_7;
                    int _S825 = int(_S824);
                    uint _S826 = 6U / _S823;
                    int3 _S827 = int3(_S825, int(_S826), int(0));
                    uint _S828 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S827)).xy), uint(((_S827)).z))).x * 255.0f));
                    uint _S829 = atlasWidth_7;
                    uint _S830 = 7U % atlasWidth_7;
                    int _S831 = int(_S830);
                    uint _S832 = 7U / _S829;
                    int3 _S833 = int3(_S831, int(_S832), int(0));
                    uint columns_7 = _S828 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S833)).xy), uint(((_S833)).z))).x * 255.0f)) << 8U);
                    uint _S834 = atlasWidth_7;
                    uint _S835 = 8U % atlasWidth_7;
                    int _S836 = int(_S835);
                    uint _S837 = 8U / _S834;
                    int3 _S838 = int3(_S836, int(_S837), int(0));
                    uint _S839 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S838)).xy), uint(((_S838)).z))).x * 255.0f));
                    uint _S840 = atlasWidth_7;
                    uint _S841 = 9U % atlasWidth_7;
                    int _S842 = int(_S841);
                    uint _S843 = 9U / _S840;
                    int3 _S844 = int3(_S842, int(_S843), int(0));
                    uint rows_7 = ((_S839 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S844)).xy), uint(((_S844)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S802 * _S808 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S845 = atlasWidth_7 / columns_7;
                    float _S846 = float(_S845);
                    uint _S847 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S846, float(_S847));
                    uint _S848 = slot_15 % columns_7;
                    float _S849 = float(_S848);
                    uint _S850 = slot_15 / columns_7;
                    _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S849, float(_S850)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(_S180) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S851 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(_S180)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S851);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S852 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S182 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S852)).xy), uint(((_S852)).z)));
                    break;
                }
                uint _S853 = atlasWidth_7 / uint(_S851.x);
                float _S854 = float(_S853);
                uint _S855 = (atlasHeight_7 - 1U) / uint(_S851.y);
                float2 cellSize_15 = float2(_S854, float(_S855));
                _S182 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_3 = uint(round(_S779.y));
            if(operation_3 == 1U)
            {
                _S182 = _S778 * _S182;
                break;
            }
            if(operation_3 == 2U)
            {
                _S182 = _S778 + _S182;
                break;
            }
            if(operation_3 == 3U)
            {
                _S182 = _S778 - _S182;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S779.z;
                _S182 = _S778 * float4((1.0f - factor_3))  + _S182 * float4(factor_3) ;
                break;
            }
            _S182 = _S778;
            break;
        }
        unlitColor_0 = _S182.xyz;
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
                _S180 = _S179;
            }
            else
            {
                _S180 = _S178;
            }
        }
        else
        {
            if(useSecondary_4)
            {
                _S180 = _S171;
            }
            else
            {
                _S180 = _S170;
            }
        }
        for(;;)
        {
            bool _S856 = (udimMask_0 & 64U) != 0U;
            uint maskU_4 = uint(round(_S164.y));
            uint maskV_4 = uint(round(_S164.z));
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
            uint _S857 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S856)
                {
                    for(;;)
                    {
                        if(_S857 == 0U)
                        {
                            _S181 = _S180;
                            break;
                        }
                        texture2d<float, access::sample> _S858 = (&kernelContext_0)->opacityTexture_0;
                        thread uint width_12;
                        thread uint height_12;
                        (*((&width_12)) = (_S858).get_width(0)),(*((&height_12)) = (_S858).get_height(0));
                        float2 size_10 = float2(float(width_12), float(height_12));
                        float2 _S859 = float2(0.5f) ;
                        float2 remapped_10 = clamp((_S180 * (size_10 - float2(2.0f) ) + float2(1.0f) ) / size_10, _S859 / size_10, (size_10 - _S859) / size_10);
                        if((_S857 & 1U) != 0U)
                        {
                            opacity_0 = remapped_10.x;
                        }
                        else
                        {
                            opacity_0 = _S180.x;
                        }
                        if((_S857 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_10.y;
                        }
                        else
                        {
                            occlusion_0 = _S180.y;
                        }
                        _S181 = float2(opacity_0, occlusion_0);
                        break;
                    }
                    _S182 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S181)));
                    break;
                }
                texture2d<float, access::sample> _S860 = (&kernelContext_0)->opacityTexture_0;
                thread uint atlasWidth_8;
                thread uint atlasHeight_8;
                (*((&atlasWidth_8)) = (_S860).get_width(0)),(*((&atlasHeight_8)) = (_S860).get_height(0));
                int3 _S861 = int3(int(0), int(0), int(0));
                float4 metadata_8 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S861)).xy), uint(((_S861)).z))) * float4(255.0f) );
                if((metadata_8.x) == 255.0f)
                {
                    uint _S862 = atlasWidth_8;
                    uint _S863 = 2U % atlasWidth_8;
                    int _S864 = int(_S863);
                    uint _S865 = 2U / _S862;
                    int3 _S866 = int3(_S864, int(_S865), int(0));
                    int _S867 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S866)).xy), uint(((_S866)).z))).x * 255.0f)));
                    uint _S868 = atlasWidth_8;
                    uint _S869 = 3U % atlasWidth_8;
                    int _S870 = int(_S869);
                    uint _S871 = 3U / _S868;
                    int3 _S872 = int3(_S870, int(_S871), int(0));
                    int2 minimumTile_8 = int2(_S867, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S872)).xy), uint(((_S872)).z))).x * 255.0f))));
                    uint _S873 = atlasWidth_8;
                    uint _S874 = 4U % atlasWidth_8;
                    int _S875 = int(_S874);
                    uint _S876 = 4U / _S873;
                    int3 _S877 = int3(_S875, int(_S876), int(0));
                    uint _S878 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S877)).xy), uint(((_S877)).z))).x * 255.0f));
                    uint _S879 = atlasWidth_8;
                    uint _S880 = 5U % atlasWidth_8;
                    int _S881 = int(_S880);
                    uint _S882 = 5U / _S879;
                    int3 _S883 = int3(_S881, int(_S882), int(0));
                    uint _S884 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S883)).xy), uint(((_S883)).z))).x * 255.0f));
                    uint2 grid_8 = uint2(_S878, _S884);
                    int2 tile_16 = int2(floor(_S180)) - minimumTile_8;
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
                        int3 _S885 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S885)).xy), uint(((_S885)).z)));
                        break;
                    }
                    uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S878 + uint(tile_16.x));
                    uint _S886 = atlasWidth_8;
                    uint _S887 = offset_8 % atlasWidth_8;
                    int _S888 = int(_S887);
                    uint _S889 = offset_8 / _S886;
                    int3 _S890 = int3(_S888, int(_S889), int(0));
                    uint _S891 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S890)).xy), uint(((_S890)).z))).x * 255.0f));
                    uint _S892 = atlasWidth_8;
                    uint _S893 = offset_8 + 1U;
                    uint _S894 = _S893 % atlasWidth_8;
                    int _S895 = int(_S894);
                    uint _S896 = _S893 / _S892;
                    int3 _S897 = int3(_S895, int(_S896), int(0));
                    uint slot_16 = _S891 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S897)).xy), uint(((_S897)).z))).x * 255.0f)) << 8U);
                    if(slot_16 == 0U)
                    {
                        int3 _S898 = int3(int(1), int(0), int(0));
                        _S182 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S898)).xy), uint(((_S898)).z)));
                        break;
                    }
                    uint slot_17 = slot_16 - 1U;
                    uint _S899 = atlasWidth_8;
                    uint _S900 = 6U % atlasWidth_8;
                    int _S901 = int(_S900);
                    uint _S902 = 6U / _S899;
                    int3 _S903 = int3(_S901, int(_S902), int(0));
                    uint _S904 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S903)).xy), uint(((_S903)).z))).x * 255.0f));
                    uint _S905 = atlasWidth_8;
                    uint _S906 = 7U % atlasWidth_8;
                    int _S907 = int(_S906);
                    uint _S908 = 7U / _S905;
                    int3 _S909 = int3(_S907, int(_S908), int(0));
                    uint columns_8 = _S904 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S909)).xy), uint(((_S909)).z))).x * 255.0f)) << 8U);
                    uint _S910 = atlasWidth_8;
                    uint _S911 = 8U % atlasWidth_8;
                    int _S912 = int(_S911);
                    uint _S913 = 8U / _S910;
                    int3 _S914 = int3(_S912, int(_S913), int(0));
                    uint _S915 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S914)).xy), uint(((_S914)).z))).x * 255.0f));
                    uint _S916 = atlasWidth_8;
                    uint _S917 = 9U % atlasWidth_8;
                    int _S918 = int(_S917);
                    uint _S919 = 9U / _S916;
                    int3 _S920 = int3(_S918, int(_S919), int(0));
                    uint rows_8 = ((_S915 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S920)).xy), uint(((_S920)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                    uint metadataRows_8 = (10U + 2U * _S878 * _S884 + atlasWidth_8 - 1U) / atlasWidth_8;
                    uint _S921 = atlasWidth_8 / columns_8;
                    float _S922 = float(_S921);
                    uint _S923 = (atlasHeight_8 - metadataRows_8) / rows_8;
                    float2 cellSize_16 = float2(_S922, float(_S923));
                    uint _S924 = slot_17 % columns_8;
                    float _S925 = float(_S924);
                    uint _S926 = slot_17 / columns_8;
                    _S182 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S925, float(_S926)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(_S180) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                    break;
                }
                int2 _S927 = int2(metadata_8.zw);
                int2 tile_17 = int2(floor(_S180)) - int2(metadata_8.xy);
                if(any(tile_17 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_17 >= _S927);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S928 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                    _S182 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S928)).xy), uint(((_S928)).z)));
                    break;
                }
                uint _S929 = atlasWidth_8 / uint(_S927.x);
                float _S930 = float(_S929);
                uint _S931 = (atlasHeight_8 - 1U) / uint(_S927.y);
                float2 cellSize_17 = float2(_S930, float(_S931));
                _S182 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            for(;;)
            {
                float4 _S932 = float4(_S7->compositeControls_0) ;
                if((_S932.x) != 64.0f)
                {
                    break;
                }
                bool _S933 = (_S932.w) >= 0.5f;
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
                uint _S934 = index_0 | lightCount_0;
                for(;;)
                {
                    if(!_S933)
                    {
                        for(;;)
                        {
                            if(_S934 == 0U)
                            {
                                break;
                            }
                            texture2d<float, access::sample> _S935 = (&kernelContext_0)->compositeTexture_0;
                            thread uint width_13;
                            thread uint height_13;
                            (*((&width_13)) = (_S935).get_width(0)),(*((&height_13)) = (_S935).get_height(0));
                            float2 size_11 = float2(float(width_13), float(height_13));
                            float2 _S936 = float2(0.5f) ;
                            float2 remapped_11 = clamp((_S180 * (size_11 - float2(2.0f) ) + float2(1.0f) ) / size_11, _S936 / size_11, (size_11 - _S936) / size_11);
                            if((_S934 & 1U) != 0U)
                            {
                                opacity_0 = remapped_11.x;
                            }
                            else
                            {
                                opacity_0 = _S180.x;
                            }
                            if((_S934 & 2U) != 0U)
                            {
                                occlusion_0 = remapped_11.y;
                            }
                            else
                            {
                                occlusion_0 = _S180.y;
                            }
                            _S180 = float2(opacity_0, occlusion_0);
                            break;
                        }
                        _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S180)));
                        break;
                    }
                    texture2d<float, access::sample> _S937 = (&kernelContext_0)->compositeTexture_0;
                    thread uint atlasWidth_9;
                    thread uint atlasHeight_9;
                    (*((&atlasWidth_9)) = (_S937).get_width(0)),(*((&atlasHeight_9)) = (_S937).get_height(0));
                    int3 _S938 = int3(int(0), int(0), int(0));
                    float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S938)).xy), uint(((_S938)).z))) * float4(255.0f) );
                    if((metadata_9.x) == 255.0f)
                    {
                        uint _S939 = atlasWidth_9;
                        uint _S940 = 2U % atlasWidth_9;
                        int _S941 = int(_S940);
                        uint _S942 = 2U / _S939;
                        int3 _S943 = int3(_S941, int(_S942), int(0));
                        int _S944 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S943)).xy), uint(((_S943)).z))).x * 255.0f)));
                        uint _S945 = atlasWidth_9;
                        uint _S946 = 3U % atlasWidth_9;
                        int _S947 = int(_S946);
                        uint _S948 = 3U / _S945;
                        int3 _S949 = int3(_S947, int(_S948), int(0));
                        int2 minimumTile_9 = int2(_S944, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S949)).xy), uint(((_S949)).z))).x * 255.0f))));
                        uint _S950 = atlasWidth_9;
                        uint _S951 = 4U % atlasWidth_9;
                        int _S952 = int(_S951);
                        uint _S953 = 4U / _S950;
                        int3 _S954 = int3(_S952, int(_S953), int(0));
                        uint _S955 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S954)).xy), uint(((_S954)).z))).x * 255.0f));
                        uint _S956 = atlasWidth_9;
                        uint _S957 = 5U % atlasWidth_9;
                        int _S958 = int(_S957);
                        uint _S959 = 5U / _S956;
                        int3 _S960 = int3(_S958, int(_S959), int(0));
                        uint _S961 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S960)).xy), uint(((_S960)).z))).x * 255.0f));
                        uint2 grid_9 = uint2(_S955, _S961);
                        int2 tile_18 = int2(floor(_S180)) - minimumTile_9;
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
                            int3 _S962 = int3(int(1), int(0), int(0));
                            _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S962)).xy), uint(((_S962)).z)));
                            break;
                        }
                        uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S955 + uint(tile_18.x));
                        uint _S963 = atlasWidth_9;
                        uint _S964 = offset_9 % atlasWidth_9;
                        int _S965 = int(_S964);
                        uint _S966 = offset_9 / _S963;
                        int3 _S967 = int3(_S965, int(_S966), int(0));
                        uint _S968 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S967)).xy), uint(((_S967)).z))).x * 255.0f));
                        uint _S969 = atlasWidth_9;
                        uint _S970 = offset_9 + 1U;
                        uint _S971 = _S970 % atlasWidth_9;
                        int _S972 = int(_S971);
                        uint _S973 = _S970 / _S969;
                        int3 _S974 = int3(_S972, int(_S973), int(0));
                        uint slot_18 = _S968 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S974)).xy), uint(((_S974)).z))).x * 255.0f)) << 8U);
                        if(slot_18 == 0U)
                        {
                            int3 _S975 = int3(int(1), int(0), int(0));
                            _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S975)).xy), uint(((_S975)).z)));
                            break;
                        }
                        uint slot_19 = slot_18 - 1U;
                        uint _S976 = atlasWidth_9;
                        uint _S977 = 6U % atlasWidth_9;
                        int _S978 = int(_S977);
                        uint _S979 = 6U / _S976;
                        int3 _S980 = int3(_S978, int(_S979), int(0));
                        uint _S981 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S980)).xy), uint(((_S980)).z))).x * 255.0f));
                        uint _S982 = atlasWidth_9;
                        uint _S983 = 7U % atlasWidth_9;
                        int _S984 = int(_S983);
                        uint _S985 = 7U / _S982;
                        int3 _S986 = int3(_S984, int(_S985), int(0));
                        uint columns_9 = _S981 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S986)).xy), uint(((_S986)).z))).x * 255.0f)) << 8U);
                        uint _S987 = atlasWidth_9;
                        uint _S988 = 8U % atlasWidth_9;
                        int _S989 = int(_S988);
                        uint _S990 = 8U / _S987;
                        int3 _S991 = int3(_S989, int(_S990), int(0));
                        uint _S992 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S991)).xy), uint(((_S991)).z))).x * 255.0f));
                        uint _S993 = atlasWidth_9;
                        uint _S994 = 9U % atlasWidth_9;
                        int _S995 = int(_S994);
                        uint _S996 = 9U / _S993;
                        int3 _S997 = int3(_S995, int(_S996), int(0));
                        uint rows_9 = ((_S992 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S997)).xy), uint(((_S997)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                        uint metadataRows_9 = (10U + 2U * _S955 * _S961 + atlasWidth_9 - 1U) / atlasWidth_9;
                        uint _S998 = atlasWidth_9 / columns_9;
                        float _S999 = float(_S998);
                        uint _S1000 = (atlasHeight_9 - metadataRows_9) / rows_9;
                        float2 cellSize_18 = float2(_S999, float(_S1000));
                        uint _S1001 = slot_19 % columns_9;
                        float _S1002 = float(_S1001);
                        uint _S1003 = slot_19 / columns_9;
                        _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1002, float(_S1003)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(_S180) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                        break;
                    }
                    int2 _S1004 = int2(metadata_9.zw);
                    int2 tile_19 = int2(floor(_S180)) - int2(metadata_9.xy);
                    if(any(tile_19 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_19 >= _S1004);
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1005 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1005)).xy), uint(((_S1005)).z)));
                        break;
                    }
                    uint _S1006 = atlasWidth_9 / uint(_S1004.x);
                    float _S1007 = float(_S1006);
                    uint _S1008 = (atlasHeight_9 - 1U) / uint(_S1004.y);
                    float2 cellSize_19 = float2(_S1007, float(_S1008));
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                uint operation_4 = uint(round(_S932.y));
                if(operation_4 == 1U)
                {
                    _S182 = _S182 * _S367;
                    break;
                }
                if(operation_4 == 2U)
                {
                    _S182 = _S182 + _S367;
                    break;
                }
                if(operation_4 == 3U)
                {
                    _S182 = _S182 - _S367;
                    break;
                }
                if(operation_4 == 4U)
                {
                    float factor_4 = _S932.z;
                    _S182 = _S182 * float4((1.0f - factor_4))  + _S367 * float4(factor_4) ;
                    break;
                }
                break;
            }
            _S4 = saturate(_S182.x);
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
                _S180 = _S179;
            }
            else
            {
                _S180 = _S178;
            }
        }
        else
        {
            if(useSecondary_5)
            {
                _S180 = _S171;
            }
            else
            {
                _S180 = _S170;
            }
        }
        bool _S1009 = (udimMask_0 & 128U) != 0U;
        uint maskU_5 = uint(round(_S164.y));
        uint maskV_5 = uint(round(_S164.z));
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
        uint _S1010 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1009)
            {
                for(;;)
                {
                    if(_S1010 == 0U)
                    {
                        _S181 = _S180;
                        break;
                    }
                    texture2d<float, access::sample> _S1011 = (&kernelContext_0)->occlusionTexture_0;
                    thread uint width_14;
                    thread uint height_14;
                    (*((&width_14)) = (_S1011).get_width(0)),(*((&height_14)) = (_S1011).get_height(0));
                    float2 size_12 = float2(float(width_14), float(height_14));
                    float2 _S1012 = float2(0.5f) ;
                    float2 remapped_12 = clamp((_S180 * (size_12 - float2(2.0f) ) + float2(1.0f) ) / size_12, _S1012 / size_12, (size_12 - _S1012) / size_12);
                    if((_S1010 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_12.x;
                    }
                    else
                    {
                        occlusion_0 = _S180.x;
                    }
                    if((_S1010 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_12.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S180.y;
                    }
                    _S181 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S1013 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S1013).get_width(0)),(*((&atlasHeight_10)) = (_S1013).get_height(0));
            int3 _S1014 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1014)).xy), uint(((_S1014)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S1015 = atlasWidth_10;
                uint _S1016 = 2U % atlasWidth_10;
                int _S1017 = int(_S1016);
                uint _S1018 = 2U / _S1015;
                int3 _S1019 = int3(_S1017, int(_S1018), int(0));
                int _S1020 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1019)).xy), uint(((_S1019)).z))).x * 255.0f)));
                uint _S1021 = atlasWidth_10;
                uint _S1022 = 3U % atlasWidth_10;
                int _S1023 = int(_S1022);
                uint _S1024 = 3U / _S1021;
                int3 _S1025 = int3(_S1023, int(_S1024), int(0));
                int2 minimumTile_10 = int2(_S1020, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1025)).xy), uint(((_S1025)).z))).x * 255.0f))));
                uint _S1026 = atlasWidth_10;
                uint _S1027 = 4U % atlasWidth_10;
                int _S1028 = int(_S1027);
                uint _S1029 = 4U / _S1026;
                int3 _S1030 = int3(_S1028, int(_S1029), int(0));
                uint _S1031 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1030)).xy), uint(((_S1030)).z))).x * 255.0f));
                uint _S1032 = atlasWidth_10;
                uint _S1033 = 5U % atlasWidth_10;
                int _S1034 = int(_S1033);
                uint _S1035 = 5U / _S1032;
                int3 _S1036 = int3(_S1034, int(_S1035), int(0));
                uint _S1037 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1036)).xy), uint(((_S1036)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S1031, _S1037);
                int2 tile_20 = int2(floor(_S180)) - minimumTile_10;
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
                    int3 _S1038 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1038)).xy), uint(((_S1038)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1031 + uint(tile_20.x));
                uint _S1039 = atlasWidth_10;
                uint _S1040 = offset_10 % atlasWidth_10;
                int _S1041 = int(_S1040);
                uint _S1042 = offset_10 / _S1039;
                int3 _S1043 = int3(_S1041, int(_S1042), int(0));
                uint _S1044 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1043)).xy), uint(((_S1043)).z))).x * 255.0f));
                uint _S1045 = atlasWidth_10;
                uint _S1046 = offset_10 + 1U;
                uint _S1047 = _S1046 % atlasWidth_10;
                int _S1048 = int(_S1047);
                uint _S1049 = _S1046 / _S1045;
                int3 _S1050 = int3(_S1048, int(_S1049), int(0));
                uint slot_20 = _S1044 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1050)).xy), uint(((_S1050)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1051 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1051)).xy), uint(((_S1051)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1052 = atlasWidth_10;
                uint _S1053 = 6U % atlasWidth_10;
                int _S1054 = int(_S1053);
                uint _S1055 = 6U / _S1052;
                int3 _S1056 = int3(_S1054, int(_S1055), int(0));
                uint _S1057 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1056)).xy), uint(((_S1056)).z))).x * 255.0f));
                uint _S1058 = atlasWidth_10;
                uint _S1059 = 7U % atlasWidth_10;
                int _S1060 = int(_S1059);
                uint _S1061 = 7U / _S1058;
                int3 _S1062 = int3(_S1060, int(_S1061), int(0));
                uint columns_10 = _S1057 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1062)).xy), uint(((_S1062)).z))).x * 255.0f)) << 8U);
                uint _S1063 = atlasWidth_10;
                uint _S1064 = 8U % atlasWidth_10;
                int _S1065 = int(_S1064);
                uint _S1066 = 8U / _S1063;
                int3 _S1067 = int3(_S1065, int(_S1066), int(0));
                uint _S1068 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1067)).xy), uint(((_S1067)).z))).x * 255.0f));
                uint _S1069 = atlasWidth_10;
                uint _S1070 = 9U % atlasWidth_10;
                int _S1071 = int(_S1070);
                uint _S1072 = 9U / _S1069;
                int3 _S1073 = int3(_S1071, int(_S1072), int(0));
                uint rows_10 = ((_S1068 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1073)).xy), uint(((_S1073)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S1031 * _S1037 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1074 = atlasWidth_10 / columns_10;
                float _S1075 = float(_S1074);
                uint _S1076 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1075, float(_S1076));
                uint _S1077 = slot_21 % columns_10;
                float _S1078 = float(_S1077);
                uint _S1079 = slot_21 / columns_10;
                _S182 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1078, float(_S1079)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(_S180) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1080 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(_S180)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1080);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1081 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1081)).xy), uint(((_S1081)).z)));
                break;
            }
            uint _S1082 = atlasWidth_10 / uint(_S1080.x);
            float _S1083 = float(_S1082);
            uint _S1084 = (atlasHeight_10 - 1U) / uint(_S1080.y);
            float2 cellSize_21 = float2(_S1083, float(_S1084));
            _S182 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1085 = float4(_S7->compositeControls_0) ;
            if((_S1085.x) != 128.0f)
            {
                break;
            }
            bool _S1086 = (_S1085.w) >= 0.5f;
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
            uint _S1087 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1086)
                {
                    for(;;)
                    {
                        if(_S1087 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1088 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_15;
                        thread uint height_15;
                        (*((&width_15)) = (_S1088).get_width(0)),(*((&height_15)) = (_S1088).get_height(0));
                        float2 size_13 = float2(float(width_15), float(height_15));
                        float2 _S1089 = float2(0.5f) ;
                        float2 remapped_13 = clamp((_S180 * (size_13 - float2(2.0f) ) + float2(1.0f) ) / size_13, _S1089 / size_13, (size_13 - _S1089) / size_13);
                        if((_S1087 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_13.x;
                        }
                        else
                        {
                            occlusion_0 = _S180.x;
                        }
                        if((_S1087 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_13.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S180.y;
                        }
                        _S180 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S180)));
                    break;
                }
                texture2d<float, access::sample> _S1090 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1090).get_width(0)),(*((&atlasHeight_11)) = (_S1090).get_height(0));
                int3 _S1091 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1091)).xy), uint(((_S1091)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1092 = atlasWidth_11;
                    uint _S1093 = 2U % atlasWidth_11;
                    int _S1094 = int(_S1093);
                    uint _S1095 = 2U / _S1092;
                    int3 _S1096 = int3(_S1094, int(_S1095), int(0));
                    int _S1097 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1096)).xy), uint(((_S1096)).z))).x * 255.0f)));
                    uint _S1098 = atlasWidth_11;
                    uint _S1099 = 3U % atlasWidth_11;
                    int _S1100 = int(_S1099);
                    uint _S1101 = 3U / _S1098;
                    int3 _S1102 = int3(_S1100, int(_S1101), int(0));
                    int2 minimumTile_11 = int2(_S1097, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1102)).xy), uint(((_S1102)).z))).x * 255.0f))));
                    uint _S1103 = atlasWidth_11;
                    uint _S1104 = 4U % atlasWidth_11;
                    int _S1105 = int(_S1104);
                    uint _S1106 = 4U / _S1103;
                    int3 _S1107 = int3(_S1105, int(_S1106), int(0));
                    uint _S1108 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1107)).xy), uint(((_S1107)).z))).x * 255.0f));
                    uint _S1109 = atlasWidth_11;
                    uint _S1110 = 5U % atlasWidth_11;
                    int _S1111 = int(_S1110);
                    uint _S1112 = 5U / _S1109;
                    int3 _S1113 = int3(_S1111, int(_S1112), int(0));
                    uint _S1114 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1113)).xy), uint(((_S1113)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1108, _S1114);
                    int2 tile_22 = int2(floor(_S180)) - minimumTile_11;
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
                        int3 _S1115 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1115)).xy), uint(((_S1115)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1108 + uint(tile_22.x));
                    uint _S1116 = atlasWidth_11;
                    uint _S1117 = offset_11 % atlasWidth_11;
                    int _S1118 = int(_S1117);
                    uint _S1119 = offset_11 / _S1116;
                    int3 _S1120 = int3(_S1118, int(_S1119), int(0));
                    uint _S1121 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1120)).xy), uint(((_S1120)).z))).x * 255.0f));
                    uint _S1122 = atlasWidth_11;
                    uint _S1123 = offset_11 + 1U;
                    uint _S1124 = _S1123 % atlasWidth_11;
                    int _S1125 = int(_S1124);
                    uint _S1126 = _S1123 / _S1122;
                    int3 _S1127 = int3(_S1125, int(_S1126), int(0));
                    uint slot_22 = _S1121 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1127)).xy), uint(((_S1127)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1128 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1128)).xy), uint(((_S1128)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1129 = atlasWidth_11;
                    uint _S1130 = 6U % atlasWidth_11;
                    int _S1131 = int(_S1130);
                    uint _S1132 = 6U / _S1129;
                    int3 _S1133 = int3(_S1131, int(_S1132), int(0));
                    uint _S1134 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1133)).xy), uint(((_S1133)).z))).x * 255.0f));
                    uint _S1135 = atlasWidth_11;
                    uint _S1136 = 7U % atlasWidth_11;
                    int _S1137 = int(_S1136);
                    uint _S1138 = 7U / _S1135;
                    int3 _S1139 = int3(_S1137, int(_S1138), int(0));
                    uint columns_11 = _S1134 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1139)).xy), uint(((_S1139)).z))).x * 255.0f)) << 8U);
                    uint _S1140 = atlasWidth_11;
                    uint _S1141 = 8U % atlasWidth_11;
                    int _S1142 = int(_S1141);
                    uint _S1143 = 8U / _S1140;
                    int3 _S1144 = int3(_S1142, int(_S1143), int(0));
                    uint _S1145 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1144)).xy), uint(((_S1144)).z))).x * 255.0f));
                    uint _S1146 = atlasWidth_11;
                    uint _S1147 = 9U % atlasWidth_11;
                    int _S1148 = int(_S1147);
                    uint _S1149 = 9U / _S1146;
                    int3 _S1150 = int3(_S1148, int(_S1149), int(0));
                    uint rows_11 = ((_S1145 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1150)).xy), uint(((_S1150)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1108 * _S1114 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1151 = atlasWidth_11 / columns_11;
                    float _S1152 = float(_S1151);
                    uint _S1153 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1152, float(_S1153));
                    uint _S1154 = slot_23 % columns_11;
                    float _S1155 = float(_S1154);
                    uint _S1156 = slot_23 / columns_11;
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1155, float(_S1156)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(_S180) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1157 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(_S180)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1157);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1158 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1158)).xy), uint(((_S1158)).z)));
                    break;
                }
                uint _S1159 = atlasWidth_11 / uint(_S1157.x);
                float _S1160 = float(_S1159);
                uint _S1161 = (atlasHeight_11 - 1U) / uint(_S1157.y);
                float2 cellSize_23 = float2(_S1160, float(_S1161));
                _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_5 = uint(round(_S1085.y));
            if(operation_5 == 1U)
            {
                _S182 = _S182 * _S367;
                break;
            }
            if(operation_5 == 2U)
            {
                _S182 = _S182 + _S367;
                break;
            }
            if(operation_5 == 3U)
            {
                _S182 = _S182 - _S367;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1085.z;
                _S182 = _S182 * float4((1.0f - factor_5))  + _S367 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S182.x);
    }
    else
    {
        occlusion_0 = _S150;
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
                _S180 = _S179;
            }
            else
            {
                _S180 = _S178;
            }
        }
        else
        {
            if(useSecondary_6)
            {
                _S180 = _S171;
            }
            else
            {
                _S180 = _S170;
            }
        }
        bool _S1162 = (udimMask_0 & 256U) != 0U;
        uint maskU_6 = uint(round(_S164.y));
        uint maskV_6 = uint(round(_S164.z));
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
        uint _S1163 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1162)
            {
                for(;;)
                {
                    if(_S1163 == 0U)
                    {
                        _S181 = _S180;
                        break;
                    }
                    texture2d<float, access::sample> _S1164 = (&kernelContext_0)->specularColorTexture_0;
                    thread uint width_16;
                    thread uint height_16;
                    (*((&width_16)) = (_S1164).get_width(0)),(*((&height_16)) = (_S1164).get_height(0));
                    float2 size_14 = float2(float(width_16), float(height_16));
                    float2 _S1165 = float2(0.5f) ;
                    float2 remapped_14 = clamp((_S180 * (size_14 - float2(2.0f) ) + float2(1.0f) ) / size_14, _S1165 / size_14, (size_14 - _S1165) / size_14);
                    if((_S1163 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_14.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S180.x;
                    }
                    if((_S1163 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_14.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S180.y;
                    }
                    _S181 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S1166 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1166).get_width(0)),(*((&atlasHeight_12)) = (_S1166).get_height(0));
            int3 _S1167 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1167)).xy), uint(((_S1167)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1168 = atlasWidth_12;
                uint _S1169 = 2U % atlasWidth_12;
                int _S1170 = int(_S1169);
                uint _S1171 = 2U / _S1168;
                int3 _S1172 = int3(_S1170, int(_S1171), int(0));
                int _S1173 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1172)).xy), uint(((_S1172)).z))).x * 255.0f)));
                uint _S1174 = atlasWidth_12;
                uint _S1175 = 3U % atlasWidth_12;
                int _S1176 = int(_S1175);
                uint _S1177 = 3U / _S1174;
                int3 _S1178 = int3(_S1176, int(_S1177), int(0));
                int2 minimumTile_12 = int2(_S1173, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1178)).xy), uint(((_S1178)).z))).x * 255.0f))));
                uint _S1179 = atlasWidth_12;
                uint _S1180 = 4U % atlasWidth_12;
                int _S1181 = int(_S1180);
                uint _S1182 = 4U / _S1179;
                int3 _S1183 = int3(_S1181, int(_S1182), int(0));
                uint _S1184 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1183)).xy), uint(((_S1183)).z))).x * 255.0f));
                uint _S1185 = atlasWidth_12;
                uint _S1186 = 5U % atlasWidth_12;
                int _S1187 = int(_S1186);
                uint _S1188 = 5U / _S1185;
                int3 _S1189 = int3(_S1187, int(_S1188), int(0));
                uint _S1190 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1189)).xy), uint(((_S1189)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1184, _S1190);
                int2 tile_24 = int2(floor(_S180)) - minimumTile_12;
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
                    int3 _S1191 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1191)).xy), uint(((_S1191)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1184 + uint(tile_24.x));
                uint _S1192 = atlasWidth_12;
                uint _S1193 = offset_12 % atlasWidth_12;
                int _S1194 = int(_S1193);
                uint _S1195 = offset_12 / _S1192;
                int3 _S1196 = int3(_S1194, int(_S1195), int(0));
                uint _S1197 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1196)).xy), uint(((_S1196)).z))).x * 255.0f));
                uint _S1198 = atlasWidth_12;
                uint _S1199 = offset_12 + 1U;
                uint _S1200 = _S1199 % atlasWidth_12;
                int _S1201 = int(_S1200);
                uint _S1202 = _S1199 / _S1198;
                int3 _S1203 = int3(_S1201, int(_S1202), int(0));
                uint slot_24 = _S1197 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1203)).xy), uint(((_S1203)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1204 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1204)).xy), uint(((_S1204)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1205 = atlasWidth_12;
                uint _S1206 = 6U % atlasWidth_12;
                int _S1207 = int(_S1206);
                uint _S1208 = 6U / _S1205;
                int3 _S1209 = int3(_S1207, int(_S1208), int(0));
                uint _S1210 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1209)).xy), uint(((_S1209)).z))).x * 255.0f));
                uint _S1211 = atlasWidth_12;
                uint _S1212 = 7U % atlasWidth_12;
                int _S1213 = int(_S1212);
                uint _S1214 = 7U / _S1211;
                int3 _S1215 = int3(_S1213, int(_S1214), int(0));
                uint columns_12 = _S1210 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1215)).xy), uint(((_S1215)).z))).x * 255.0f)) << 8U);
                uint _S1216 = atlasWidth_12;
                uint _S1217 = 8U % atlasWidth_12;
                int _S1218 = int(_S1217);
                uint _S1219 = 8U / _S1216;
                int3 _S1220 = int3(_S1218, int(_S1219), int(0));
                uint _S1221 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1220)).xy), uint(((_S1220)).z))).x * 255.0f));
                uint _S1222 = atlasWidth_12;
                uint _S1223 = 9U % atlasWidth_12;
                int _S1224 = int(_S1223);
                uint _S1225 = 9U / _S1222;
                int3 _S1226 = int3(_S1224, int(_S1225), int(0));
                uint rows_12 = ((_S1221 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1226)).xy), uint(((_S1226)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1184 * _S1190 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1227 = atlasWidth_12 / columns_12;
                float _S1228 = float(_S1227);
                uint _S1229 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1228, float(_S1229));
                uint _S1230 = slot_25 % columns_12;
                float _S1231 = float(_S1230);
                uint _S1232 = slot_25 / columns_12;
                _S182 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1231, float(_S1232)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(_S180) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1233 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(_S180)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1233);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1234 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1234)).xy), uint(((_S1234)).z)));
                break;
            }
            uint _S1235 = atlasWidth_12 / uint(_S1233.x);
            float _S1236 = float(_S1235);
            uint _S1237 = (atlasHeight_12 - 1U) / uint(_S1233.y);
            float2 cellSize_25 = float2(_S1236, float(_S1237));
            _S182 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1238 = float4(_S7->compositeControls_0) ;
            if((_S1238.x) != 256.0f)
            {
                break;
            }
            bool _S1239 = (_S1238.w) >= 0.5f;
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
            uint _S1240 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1239)
                {
                    for(;;)
                    {
                        if(_S1240 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1241 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_17;
                        thread uint height_17;
                        (*((&width_17)) = (_S1241).get_width(0)),(*((&height_17)) = (_S1241).get_height(0));
                        float2 size_15 = float2(float(width_17), float(height_17));
                        float2 _S1242 = float2(0.5f) ;
                        float2 remapped_15 = clamp((_S180 * (size_15 - float2(2.0f) ) + float2(1.0f) ) / size_15, _S1242 / size_15, (size_15 - _S1242) / size_15);
                        if((_S1240 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_15.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S180.x;
                        }
                        if((_S1240 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_15.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S180.y;
                        }
                        _S180 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S180)));
                    break;
                }
                texture2d<float, access::sample> _S1243 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1243).get_width(0)),(*((&atlasHeight_13)) = (_S1243).get_height(0));
                int3 _S1244 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1244)).xy), uint(((_S1244)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1245 = atlasWidth_13;
                    uint _S1246 = 2U % atlasWidth_13;
                    int _S1247 = int(_S1246);
                    uint _S1248 = 2U / _S1245;
                    int3 _S1249 = int3(_S1247, int(_S1248), int(0));
                    int _S1250 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1249)).xy), uint(((_S1249)).z))).x * 255.0f)));
                    uint _S1251 = atlasWidth_13;
                    uint _S1252 = 3U % atlasWidth_13;
                    int _S1253 = int(_S1252);
                    uint _S1254 = 3U / _S1251;
                    int3 _S1255 = int3(_S1253, int(_S1254), int(0));
                    int2 minimumTile_13 = int2(_S1250, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1255)).xy), uint(((_S1255)).z))).x * 255.0f))));
                    uint _S1256 = atlasWidth_13;
                    uint _S1257 = 4U % atlasWidth_13;
                    int _S1258 = int(_S1257);
                    uint _S1259 = 4U / _S1256;
                    int3 _S1260 = int3(_S1258, int(_S1259), int(0));
                    uint _S1261 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1260)).xy), uint(((_S1260)).z))).x * 255.0f));
                    uint _S1262 = atlasWidth_13;
                    uint _S1263 = 5U % atlasWidth_13;
                    int _S1264 = int(_S1263);
                    uint _S1265 = 5U / _S1262;
                    int3 _S1266 = int3(_S1264, int(_S1265), int(0));
                    uint _S1267 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1266)).xy), uint(((_S1266)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1261, _S1267);
                    int2 tile_26 = int2(floor(_S180)) - minimumTile_13;
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
                        int3 _S1268 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1268)).xy), uint(((_S1268)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1261 + uint(tile_26.x));
                    uint _S1269 = atlasWidth_13;
                    uint _S1270 = offset_13 % atlasWidth_13;
                    int _S1271 = int(_S1270);
                    uint _S1272 = offset_13 / _S1269;
                    int3 _S1273 = int3(_S1271, int(_S1272), int(0));
                    uint _S1274 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1273)).xy), uint(((_S1273)).z))).x * 255.0f));
                    uint _S1275 = atlasWidth_13;
                    uint _S1276 = offset_13 + 1U;
                    uint _S1277 = _S1276 % atlasWidth_13;
                    int _S1278 = int(_S1277);
                    uint _S1279 = _S1276 / _S1275;
                    int3 _S1280 = int3(_S1278, int(_S1279), int(0));
                    uint slot_26 = _S1274 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1280)).xy), uint(((_S1280)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1281 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1281)).xy), uint(((_S1281)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1282 = atlasWidth_13;
                    uint _S1283 = 6U % atlasWidth_13;
                    int _S1284 = int(_S1283);
                    uint _S1285 = 6U / _S1282;
                    int3 _S1286 = int3(_S1284, int(_S1285), int(0));
                    uint _S1287 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1286)).xy), uint(((_S1286)).z))).x * 255.0f));
                    uint _S1288 = atlasWidth_13;
                    uint _S1289 = 7U % atlasWidth_13;
                    int _S1290 = int(_S1289);
                    uint _S1291 = 7U / _S1288;
                    int3 _S1292 = int3(_S1290, int(_S1291), int(0));
                    uint columns_13 = _S1287 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1292)).xy), uint(((_S1292)).z))).x * 255.0f)) << 8U);
                    uint _S1293 = atlasWidth_13;
                    uint _S1294 = 8U % atlasWidth_13;
                    int _S1295 = int(_S1294);
                    uint _S1296 = 8U / _S1293;
                    int3 _S1297 = int3(_S1295, int(_S1296), int(0));
                    uint _S1298 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1297)).xy), uint(((_S1297)).z))).x * 255.0f));
                    uint _S1299 = atlasWidth_13;
                    uint _S1300 = 9U % atlasWidth_13;
                    int _S1301 = int(_S1300);
                    uint _S1302 = 9U / _S1299;
                    int3 _S1303 = int3(_S1301, int(_S1302), int(0));
                    uint rows_13 = ((_S1298 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1303)).xy), uint(((_S1303)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1261 * _S1267 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1304 = atlasWidth_13 / columns_13;
                    float _S1305 = float(_S1304);
                    uint _S1306 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1305, float(_S1306));
                    uint _S1307 = slot_27 % columns_13;
                    float _S1308 = float(_S1307);
                    uint _S1309 = slot_27 / columns_13;
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1308, float(_S1309)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(_S180) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1310 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(_S180)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1310);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1311 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1311)).xy), uint(((_S1311)).z)));
                    break;
                }
                uint _S1312 = atlasWidth_13 / uint(_S1310.x);
                float _S1313 = float(_S1312);
                uint _S1314 = (atlasHeight_13 - 1U) / uint(_S1310.y);
                float2 cellSize_27 = float2(_S1313, float(_S1314));
                _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_6 = uint(round(_S1238.y));
            if(operation_6 == 1U)
            {
                _S182 = _S182 * _S367;
                break;
            }
            if(operation_6 == 2U)
            {
                _S182 = _S182 + _S367;
                break;
            }
            if(operation_6 == 3U)
            {
                _S182 = _S182 - _S367;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1238.z;
                _S182 = _S182 * float4((1.0f - factor_6))  + _S367 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S182.xyz);
    }
    else
    {
        specularColor_0 = _S158;
    }
    if((textureMask_0 & 512U) != 0U)
    {
        bool useSecondary_7 = (secondaryMask_0 & 512U) != 0U;
        if((alternateMask_0 & 512U) != 0U)
        {
            if(useSecondary_7)
            {
                _S180 = _S179;
            }
            else
            {
                _S180 = _S178;
            }
        }
        else
        {
            if(useSecondary_7)
            {
                _S180 = _S171;
            }
            else
            {
                _S180 = _S170;
            }
        }
        bool _S1315 = (udimMask_0 & 512U) != 0U;
        uint maskU_7 = uint(round(_S164.y));
        uint maskV_7 = uint(round(_S164.z));
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
        uint _S1316 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1315)
            {
                for(;;)
                {
                    if(_S1316 == 0U)
                    {
                        _S181 = _S180;
                        break;
                    }
                    texture2d<float, access::sample> _S1317 = (&kernelContext_0)->clearcoatTexture_0;
                    thread uint width_18;
                    thread uint height_18;
                    (*((&width_18)) = (_S1317).get_width(0)),(*((&height_18)) = (_S1317).get_height(0));
                    float2 size_16 = float2(float(width_18), float(height_18));
                    float2 _S1318 = float2(0.5f) ;
                    float2 remapped_16 = clamp((_S180 * (size_16 - float2(2.0f) ) + float2(1.0f) ) / size_16, _S1318 / size_16, (size_16 - _S1318) / size_16);
                    if((_S1316 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_16.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S180.x;
                    }
                    if((_S1316 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_16.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S180.y;
                    }
                    _S181 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S1319 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1319).get_width(0)),(*((&atlasHeight_14)) = (_S1319).get_height(0));
            int3 _S1320 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1320)).xy), uint(((_S1320)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1321 = atlasWidth_14;
                uint _S1322 = 2U % atlasWidth_14;
                int _S1323 = int(_S1322);
                uint _S1324 = 2U / _S1321;
                int3 _S1325 = int3(_S1323, int(_S1324), int(0));
                int _S1326 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1325)).xy), uint(((_S1325)).z))).x * 255.0f)));
                uint _S1327 = atlasWidth_14;
                uint _S1328 = 3U % atlasWidth_14;
                int _S1329 = int(_S1328);
                uint _S1330 = 3U / _S1327;
                int3 _S1331 = int3(_S1329, int(_S1330), int(0));
                int2 minimumTile_14 = int2(_S1326, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1331)).xy), uint(((_S1331)).z))).x * 255.0f))));
                uint _S1332 = atlasWidth_14;
                uint _S1333 = 4U % atlasWidth_14;
                int _S1334 = int(_S1333);
                uint _S1335 = 4U / _S1332;
                int3 _S1336 = int3(_S1334, int(_S1335), int(0));
                uint _S1337 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1336)).xy), uint(((_S1336)).z))).x * 255.0f));
                uint _S1338 = atlasWidth_14;
                uint _S1339 = 5U % atlasWidth_14;
                int _S1340 = int(_S1339);
                uint _S1341 = 5U / _S1338;
                int3 _S1342 = int3(_S1340, int(_S1341), int(0));
                uint _S1343 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1342)).xy), uint(((_S1342)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1337, _S1343);
                int2 tile_28 = int2(floor(_S180)) - minimumTile_14;
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
                    _S182 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1344)).xy), uint(((_S1344)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1337 + uint(tile_28.x));
                uint _S1345 = atlasWidth_14;
                uint _S1346 = offset_14 % atlasWidth_14;
                int _S1347 = int(_S1346);
                uint _S1348 = offset_14 / _S1345;
                int3 _S1349 = int3(_S1347, int(_S1348), int(0));
                uint _S1350 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1349)).xy), uint(((_S1349)).z))).x * 255.0f));
                uint _S1351 = atlasWidth_14;
                uint _S1352 = offset_14 + 1U;
                uint _S1353 = _S1352 % atlasWidth_14;
                int _S1354 = int(_S1353);
                uint _S1355 = _S1352 / _S1351;
                int3 _S1356 = int3(_S1354, int(_S1355), int(0));
                uint slot_28 = _S1350 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1356)).xy), uint(((_S1356)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1357 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1357)).xy), uint(((_S1357)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1358 = atlasWidth_14;
                uint _S1359 = 6U % atlasWidth_14;
                int _S1360 = int(_S1359);
                uint _S1361 = 6U / _S1358;
                int3 _S1362 = int3(_S1360, int(_S1361), int(0));
                uint _S1363 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1362)).xy), uint(((_S1362)).z))).x * 255.0f));
                uint _S1364 = atlasWidth_14;
                uint _S1365 = 7U % atlasWidth_14;
                int _S1366 = int(_S1365);
                uint _S1367 = 7U / _S1364;
                int3 _S1368 = int3(_S1366, int(_S1367), int(0));
                uint columns_14 = _S1363 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1368)).xy), uint(((_S1368)).z))).x * 255.0f)) << 8U);
                uint _S1369 = atlasWidth_14;
                uint _S1370 = 8U % atlasWidth_14;
                int _S1371 = int(_S1370);
                uint _S1372 = 8U / _S1369;
                int3 _S1373 = int3(_S1371, int(_S1372), int(0));
                uint _S1374 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1373)).xy), uint(((_S1373)).z))).x * 255.0f));
                uint _S1375 = atlasWidth_14;
                uint _S1376 = 9U % atlasWidth_14;
                int _S1377 = int(_S1376);
                uint _S1378 = 9U / _S1375;
                int3 _S1379 = int3(_S1377, int(_S1378), int(0));
                uint rows_14 = ((_S1374 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1379)).xy), uint(((_S1379)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1337 * _S1343 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1380 = atlasWidth_14 / columns_14;
                float _S1381 = float(_S1380);
                uint _S1382 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1381, float(_S1382));
                uint _S1383 = slot_29 % columns_14;
                float _S1384 = float(_S1383);
                uint _S1385 = slot_29 / columns_14;
                _S182 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1384, float(_S1385)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(_S180) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1386 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(_S180)) - int2(metadata_14.xy);
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
                _S182 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1387)).xy), uint(((_S1387)).z)));
                break;
            }
            uint _S1388 = atlasWidth_14 / uint(_S1386.x);
            float _S1389 = float(_S1388);
            uint _S1390 = (atlasHeight_14 - 1U) / uint(_S1386.y);
            float2 cellSize_29 = float2(_S1389, float(_S1390));
            _S182 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1391 = float4(_S7->compositeControls_0) ;
            if((_S1391.x) != 512.0f)
            {
                break;
            }
            bool _S1392 = (_S1391.w) >= 0.5f;
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
            uint _S1393 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1392)
                {
                    for(;;)
                    {
                        if(_S1393 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1394 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_19;
                        thread uint height_19;
                        (*((&width_19)) = (_S1394).get_width(0)),(*((&height_19)) = (_S1394).get_height(0));
                        float2 size_17 = float2(float(width_19), float(height_19));
                        float2 _S1395 = float2(0.5f) ;
                        float2 remapped_17 = clamp((_S180 * (size_17 - float2(2.0f) ) + float2(1.0f) ) / size_17, _S1395 / size_17, (size_17 - _S1395) / size_17);
                        if((_S1393 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_17.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S180.x;
                        }
                        if((_S1393 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_17.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S180.y;
                        }
                        _S180 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S180)));
                    break;
                }
                texture2d<float, access::sample> _S1396 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1396).get_width(0)),(*((&atlasHeight_15)) = (_S1396).get_height(0));
                int3 _S1397 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1397)).xy), uint(((_S1397)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1398 = atlasWidth_15;
                    uint _S1399 = 2U % atlasWidth_15;
                    int _S1400 = int(_S1399);
                    uint _S1401 = 2U / _S1398;
                    int3 _S1402 = int3(_S1400, int(_S1401), int(0));
                    int _S1403 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1402)).xy), uint(((_S1402)).z))).x * 255.0f)));
                    uint _S1404 = atlasWidth_15;
                    uint _S1405 = 3U % atlasWidth_15;
                    int _S1406 = int(_S1405);
                    uint _S1407 = 3U / _S1404;
                    int3 _S1408 = int3(_S1406, int(_S1407), int(0));
                    int2 minimumTile_15 = int2(_S1403, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1408)).xy), uint(((_S1408)).z))).x * 255.0f))));
                    uint _S1409 = atlasWidth_15;
                    uint _S1410 = 4U % atlasWidth_15;
                    int _S1411 = int(_S1410);
                    uint _S1412 = 4U / _S1409;
                    int3 _S1413 = int3(_S1411, int(_S1412), int(0));
                    uint _S1414 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1413)).xy), uint(((_S1413)).z))).x * 255.0f));
                    uint _S1415 = atlasWidth_15;
                    uint _S1416 = 5U % atlasWidth_15;
                    int _S1417 = int(_S1416);
                    uint _S1418 = 5U / _S1415;
                    int3 _S1419 = int3(_S1417, int(_S1418), int(0));
                    uint _S1420 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1419)).xy), uint(((_S1419)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1414, _S1420);
                    int2 tile_30 = int2(floor(_S180)) - minimumTile_15;
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
                        int3 _S1421 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1421)).xy), uint(((_S1421)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1414 + uint(tile_30.x));
                    uint _S1422 = atlasWidth_15;
                    uint _S1423 = offset_15 % atlasWidth_15;
                    int _S1424 = int(_S1423);
                    uint _S1425 = offset_15 / _S1422;
                    int3 _S1426 = int3(_S1424, int(_S1425), int(0));
                    uint _S1427 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1426)).xy), uint(((_S1426)).z))).x * 255.0f));
                    uint _S1428 = atlasWidth_15;
                    uint _S1429 = offset_15 + 1U;
                    uint _S1430 = _S1429 % atlasWidth_15;
                    int _S1431 = int(_S1430);
                    uint _S1432 = _S1429 / _S1428;
                    int3 _S1433 = int3(_S1431, int(_S1432), int(0));
                    uint slot_30 = _S1427 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1433)).xy), uint(((_S1433)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1434 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1434)).xy), uint(((_S1434)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1435 = atlasWidth_15;
                    uint _S1436 = 6U % atlasWidth_15;
                    int _S1437 = int(_S1436);
                    uint _S1438 = 6U / _S1435;
                    int3 _S1439 = int3(_S1437, int(_S1438), int(0));
                    uint _S1440 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1439)).xy), uint(((_S1439)).z))).x * 255.0f));
                    uint _S1441 = atlasWidth_15;
                    uint _S1442 = 7U % atlasWidth_15;
                    int _S1443 = int(_S1442);
                    uint _S1444 = 7U / _S1441;
                    int3 _S1445 = int3(_S1443, int(_S1444), int(0));
                    uint columns_15 = _S1440 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1445)).xy), uint(((_S1445)).z))).x * 255.0f)) << 8U);
                    uint _S1446 = atlasWidth_15;
                    uint _S1447 = 8U % atlasWidth_15;
                    int _S1448 = int(_S1447);
                    uint _S1449 = 8U / _S1446;
                    int3 _S1450 = int3(_S1448, int(_S1449), int(0));
                    uint _S1451 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1450)).xy), uint(((_S1450)).z))).x * 255.0f));
                    uint _S1452 = atlasWidth_15;
                    uint _S1453 = 9U % atlasWidth_15;
                    int _S1454 = int(_S1453);
                    uint _S1455 = 9U / _S1452;
                    int3 _S1456 = int3(_S1454, int(_S1455), int(0));
                    uint rows_15 = ((_S1451 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1456)).xy), uint(((_S1456)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1414 * _S1420 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1457 = atlasWidth_15 / columns_15;
                    float _S1458 = float(_S1457);
                    uint _S1459 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1458, float(_S1459));
                    uint _S1460 = slot_31 % columns_15;
                    float _S1461 = float(_S1460);
                    uint _S1462 = slot_31 / columns_15;
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1461, float(_S1462)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(_S180) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1463 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(_S180)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1463);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1464 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1464)).xy), uint(((_S1464)).z)));
                    break;
                }
                uint _S1465 = atlasWidth_15 / uint(_S1463.x);
                float _S1466 = float(_S1465);
                uint _S1467 = (atlasHeight_15 - 1U) / uint(_S1463.y);
                float2 cellSize_31 = float2(_S1466, float(_S1467));
                _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_7 = uint(round(_S1391.y));
            if(operation_7 == 1U)
            {
                _S182 = _S182 * _S367;
                break;
            }
            if(operation_7 == 2U)
            {
                _S182 = _S182 + _S367;
                break;
            }
            if(operation_7 == 3U)
            {
                _S182 = _S182 - _S367;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1391.z;
                _S182 = _S182 * float4((1.0f - factor_7))  + _S367 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S182.x);
    }
    else
    {
        clearcoatAmount_0 = _S160;
    }
    float ior_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool useSecondary_8 = (secondaryMask_0 & 1024U) != 0U;
        if((alternateMask_0 & 1024U) != 0U)
        {
            if(useSecondary_8)
            {
                _S180 = _S179;
            }
            else
            {
                _S180 = _S178;
            }
        }
        else
        {
            if(useSecondary_8)
            {
                _S180 = _S171;
            }
            else
            {
                _S180 = _S170;
            }
        }
        bool _S1468 = (udimMask_0 & 1024U) != 0U;
        uint maskU_8 = uint(round(_S164.y));
        uint maskV_8 = uint(round(_S164.z));
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
        uint _S1469 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1468)
            {
                for(;;)
                {
                    if(_S1469 == 0U)
                    {
                        _S181 = _S180;
                        break;
                    }
                    texture2d<float, access::sample> _S1470 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
                    thread uint width_20;
                    thread uint height_20;
                    (*((&width_20)) = (_S1470).get_width(0)),(*((&height_20)) = (_S1470).get_height(0));
                    float2 size_18 = float2(float(width_20), float(height_20));
                    float2 _S1471 = float2(0.5f) ;
                    float2 remapped_18 = clamp((_S180 * (size_18 - float2(2.0f) ) + float2(1.0f) ) / size_18, _S1471 / size_18, (size_18 - _S1471) / size_18);
                    if((_S1469 & 1U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_18.x;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S180.x;
                    }
                    if((_S1469 & 2U) != 0U)
                    {
                        ior_0 = remapped_18.y;
                    }
                    else
                    {
                        ior_0 = _S180.y;
                    }
                    _S181 = float2(clearcoatRoughness_0, ior_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S1472 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1472).get_width(0)),(*((&atlasHeight_16)) = (_S1472).get_height(0));
            int3 _S1473 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1473)).xy), uint(((_S1473)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1474 = atlasWidth_16;
                uint _S1475 = 2U % atlasWidth_16;
                int _S1476 = int(_S1475);
                uint _S1477 = 2U / _S1474;
                int3 _S1478 = int3(_S1476, int(_S1477), int(0));
                int _S1479 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1478)).xy), uint(((_S1478)).z))).x * 255.0f)));
                uint _S1480 = atlasWidth_16;
                uint _S1481 = 3U % atlasWidth_16;
                int _S1482 = int(_S1481);
                uint _S1483 = 3U / _S1480;
                int3 _S1484 = int3(_S1482, int(_S1483), int(0));
                int2 minimumTile_16 = int2(_S1479, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1484)).xy), uint(((_S1484)).z))).x * 255.0f))));
                uint _S1485 = atlasWidth_16;
                uint _S1486 = 4U % atlasWidth_16;
                int _S1487 = int(_S1486);
                uint _S1488 = 4U / _S1485;
                int3 _S1489 = int3(_S1487, int(_S1488), int(0));
                uint _S1490 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1489)).xy), uint(((_S1489)).z))).x * 255.0f));
                uint _S1491 = atlasWidth_16;
                uint _S1492 = 5U % atlasWidth_16;
                int _S1493 = int(_S1492);
                uint _S1494 = 5U / _S1491;
                int3 _S1495 = int3(_S1493, int(_S1494), int(0));
                uint _S1496 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1495)).xy), uint(((_S1495)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1490, _S1496);
                int2 tile_32 = int2(floor(_S180)) - minimumTile_16;
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
                    int3 _S1497 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1497)).xy), uint(((_S1497)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1490 + uint(tile_32.x));
                uint _S1498 = atlasWidth_16;
                uint _S1499 = offset_16 % atlasWidth_16;
                int _S1500 = int(_S1499);
                uint _S1501 = offset_16 / _S1498;
                int3 _S1502 = int3(_S1500, int(_S1501), int(0));
                uint _S1503 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1502)).xy), uint(((_S1502)).z))).x * 255.0f));
                uint _S1504 = atlasWidth_16;
                uint _S1505 = offset_16 + 1U;
                uint _S1506 = _S1505 % atlasWidth_16;
                int _S1507 = int(_S1506);
                uint _S1508 = _S1505 / _S1504;
                int3 _S1509 = int3(_S1507, int(_S1508), int(0));
                uint slot_32 = _S1503 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1509)).xy), uint(((_S1509)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1510 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1510)).xy), uint(((_S1510)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1511 = atlasWidth_16;
                uint _S1512 = 6U % atlasWidth_16;
                int _S1513 = int(_S1512);
                uint _S1514 = 6U / _S1511;
                int3 _S1515 = int3(_S1513, int(_S1514), int(0));
                uint _S1516 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1515)).xy), uint(((_S1515)).z))).x * 255.0f));
                uint _S1517 = atlasWidth_16;
                uint _S1518 = 7U % atlasWidth_16;
                int _S1519 = int(_S1518);
                uint _S1520 = 7U / _S1517;
                int3 _S1521 = int3(_S1519, int(_S1520), int(0));
                uint columns_16 = _S1516 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1521)).xy), uint(((_S1521)).z))).x * 255.0f)) << 8U);
                uint _S1522 = atlasWidth_16;
                uint _S1523 = 8U % atlasWidth_16;
                int _S1524 = int(_S1523);
                uint _S1525 = 8U / _S1522;
                int3 _S1526 = int3(_S1524, int(_S1525), int(0));
                uint _S1527 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1526)).xy), uint(((_S1526)).z))).x * 255.0f));
                uint _S1528 = atlasWidth_16;
                uint _S1529 = 9U % atlasWidth_16;
                int _S1530 = int(_S1529);
                uint _S1531 = 9U / _S1528;
                int3 _S1532 = int3(_S1530, int(_S1531), int(0));
                uint rows_16 = ((_S1527 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1532)).xy), uint(((_S1532)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1490 * _S1496 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1533 = atlasWidth_16 / columns_16;
                float _S1534 = float(_S1533);
                uint _S1535 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1534, float(_S1535));
                uint _S1536 = slot_33 % columns_16;
                float _S1537 = float(_S1536);
                uint _S1538 = slot_33 / columns_16;
                _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1537, float(_S1538)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(_S180) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1539 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(_S180)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1539);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1540 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1540)).xy), uint(((_S1540)).z)));
                break;
            }
            uint _S1541 = atlasWidth_16 / uint(_S1539.x);
            float _S1542 = float(_S1541);
            uint _S1543 = (atlasHeight_16 - 1U) / uint(_S1539.y);
            float2 cellSize_33 = float2(_S1542, float(_S1543));
            _S182 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1544 = float4(_S7->compositeControls_0) ;
            if((_S1544.x) != 1024.0f)
            {
                break;
            }
            bool _S1545 = (_S1544.w) >= 0.5f;
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
            uint _S1546 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1545)
                {
                    for(;;)
                    {
                        if(_S1546 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1547 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_21;
                        thread uint height_21;
                        (*((&width_21)) = (_S1547).get_width(0)),(*((&height_21)) = (_S1547).get_height(0));
                        float2 size_19 = float2(float(width_21), float(height_21));
                        float2 _S1548 = float2(0.5f) ;
                        float2 remapped_19 = clamp((_S180 * (size_19 - float2(2.0f) ) + float2(1.0f) ) / size_19, _S1548 / size_19, (size_19 - _S1548) / size_19);
                        if((_S1546 & 1U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_19.x;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S180.x;
                        }
                        if((_S1546 & 2U) != 0U)
                        {
                            ior_0 = remapped_19.y;
                        }
                        else
                        {
                            ior_0 = _S180.y;
                        }
                        _S180 = float2(clearcoatRoughness_0, ior_0);
                        break;
                    }
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S180)));
                    break;
                }
                texture2d<float, access::sample> _S1549 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1549).get_width(0)),(*((&atlasHeight_17)) = (_S1549).get_height(0));
                int3 _S1550 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1550)).xy), uint(((_S1550)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1551 = atlasWidth_17;
                    uint _S1552 = 2U % atlasWidth_17;
                    int _S1553 = int(_S1552);
                    uint _S1554 = 2U / _S1551;
                    int3 _S1555 = int3(_S1553, int(_S1554), int(0));
                    int _S1556 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1555)).xy), uint(((_S1555)).z))).x * 255.0f)));
                    uint _S1557 = atlasWidth_17;
                    uint _S1558 = 3U % atlasWidth_17;
                    int _S1559 = int(_S1558);
                    uint _S1560 = 3U / _S1557;
                    int3 _S1561 = int3(_S1559, int(_S1560), int(0));
                    int2 minimumTile_17 = int2(_S1556, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1561)).xy), uint(((_S1561)).z))).x * 255.0f))));
                    uint _S1562 = atlasWidth_17;
                    uint _S1563 = 4U % atlasWidth_17;
                    int _S1564 = int(_S1563);
                    uint _S1565 = 4U / _S1562;
                    int3 _S1566 = int3(_S1564, int(_S1565), int(0));
                    uint _S1567 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1566)).xy), uint(((_S1566)).z))).x * 255.0f));
                    uint _S1568 = atlasWidth_17;
                    uint _S1569 = 5U % atlasWidth_17;
                    int _S1570 = int(_S1569);
                    uint _S1571 = 5U / _S1568;
                    int3 _S1572 = int3(_S1570, int(_S1571), int(0));
                    uint _S1573 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1572)).xy), uint(((_S1572)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1567, _S1573);
                    int2 tile_34 = int2(floor(_S180)) - minimumTile_17;
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
                        int3 _S1574 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1574)).xy), uint(((_S1574)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1567 + uint(tile_34.x));
                    uint _S1575 = atlasWidth_17;
                    uint _S1576 = offset_17 % atlasWidth_17;
                    int _S1577 = int(_S1576);
                    uint _S1578 = offset_17 / _S1575;
                    int3 _S1579 = int3(_S1577, int(_S1578), int(0));
                    uint _S1580 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1579)).xy), uint(((_S1579)).z))).x * 255.0f));
                    uint _S1581 = atlasWidth_17;
                    uint _S1582 = offset_17 + 1U;
                    uint _S1583 = _S1582 % atlasWidth_17;
                    int _S1584 = int(_S1583);
                    uint _S1585 = _S1582 / _S1581;
                    int3 _S1586 = int3(_S1584, int(_S1585), int(0));
                    uint slot_34 = _S1580 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1586)).xy), uint(((_S1586)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1587 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1587)).xy), uint(((_S1587)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1588 = atlasWidth_17;
                    uint _S1589 = 6U % atlasWidth_17;
                    int _S1590 = int(_S1589);
                    uint _S1591 = 6U / _S1588;
                    int3 _S1592 = int3(_S1590, int(_S1591), int(0));
                    uint _S1593 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1592)).xy), uint(((_S1592)).z))).x * 255.0f));
                    uint _S1594 = atlasWidth_17;
                    uint _S1595 = 7U % atlasWidth_17;
                    int _S1596 = int(_S1595);
                    uint _S1597 = 7U / _S1594;
                    int3 _S1598 = int3(_S1596, int(_S1597), int(0));
                    uint columns_17 = _S1593 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1598)).xy), uint(((_S1598)).z))).x * 255.0f)) << 8U);
                    uint _S1599 = atlasWidth_17;
                    uint _S1600 = 8U % atlasWidth_17;
                    int _S1601 = int(_S1600);
                    uint _S1602 = 8U / _S1599;
                    int3 _S1603 = int3(_S1601, int(_S1602), int(0));
                    uint _S1604 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1603)).xy), uint(((_S1603)).z))).x * 255.0f));
                    uint _S1605 = atlasWidth_17;
                    uint _S1606 = 9U % atlasWidth_17;
                    int _S1607 = int(_S1606);
                    uint _S1608 = 9U / _S1605;
                    int3 _S1609 = int3(_S1607, int(_S1608), int(0));
                    uint rows_17 = ((_S1604 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1609)).xy), uint(((_S1609)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1567 * _S1573 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1610 = atlasWidth_17 / columns_17;
                    float _S1611 = float(_S1610);
                    uint _S1612 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1611, float(_S1612));
                    uint _S1613 = slot_35 % columns_17;
                    float _S1614 = float(_S1613);
                    uint _S1615 = slot_35 / columns_17;
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1614, float(_S1615)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(_S180) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1616 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(_S180)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1616);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1617 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1617)).xy), uint(((_S1617)).z)));
                    break;
                }
                uint _S1618 = atlasWidth_17 / uint(_S1616.x);
                float _S1619 = float(_S1618);
                uint _S1620 = (atlasHeight_17 - 1U) / uint(_S1616.y);
                float2 cellSize_35 = float2(_S1619, float(_S1620));
                _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_8 = uint(round(_S1544.y));
            if(operation_8 == 1U)
            {
                _S182 = _S182 * _S367;
                break;
            }
            if(operation_8 == 2U)
            {
                _S182 = _S182 + _S367;
                break;
            }
            if(operation_8 == 3U)
            {
                _S182 = _S182 - _S367;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1544.z;
                _S182 = _S182 * float4((1.0f - factor_8))  + _S367 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S182.x);
    }
    else
    {
        clearcoatRoughness_0 = _S161;
    }
    float lightType_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool useSecondary_9 = (secondaryMask_0 & 2048U) != 0U;
        if((alternateMask_0 & 2048U) != 0U)
        {
            if(useSecondary_9)
            {
                _S180 = _S179;
            }
            else
            {
                _S180 = _S178;
            }
        }
        else
        {
            if(useSecondary_9)
            {
                _S180 = _S171;
            }
            else
            {
                _S180 = _S170;
            }
        }
        bool _S1621 = (udimMask_0 & 2048U) != 0U;
        uint maskU_9 = uint(round(_S164.y));
        uint maskV_9 = uint(round(_S164.z));
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
        uint _S1622 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1621)
            {
                for(;;)
                {
                    if(_S1622 == 0U)
                    {
                        _S181 = _S180;
                        break;
                    }
                    texture2d<float, access::sample> _S1623 = (&kernelContext_0)->iorTexture_0;
                    thread uint width_22;
                    thread uint height_22;
                    (*((&width_22)) = (_S1623).get_width(0)),(*((&height_22)) = (_S1623).get_height(0));
                    float2 size_20 = float2(float(width_22), float(height_22));
                    float2 _S1624 = float2(0.5f) ;
                    float2 remapped_20 = clamp((_S180 * (size_20 - float2(2.0f) ) + float2(1.0f) ) / size_20, _S1624 / size_20, (size_20 - _S1624) / size_20);
                    if((_S1622 & 1U) != 0U)
                    {
                        ior_0 = remapped_20.x;
                    }
                    else
                    {
                        ior_0 = _S180.x;
                    }
                    if((_S1622 & 2U) != 0U)
                    {
                        lightType_0 = remapped_20.y;
                    }
                    else
                    {
                        lightType_0 = _S180.y;
                    }
                    _S181 = float2(ior_0, lightType_0);
                    break;
                }
                _S182 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S1625 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1625).get_width(0)),(*((&atlasHeight_18)) = (_S1625).get_height(0));
            int3 _S1626 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1626)).xy), uint(((_S1626)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1627 = atlasWidth_18;
                uint _S1628 = 2U % atlasWidth_18;
                int _S1629 = int(_S1628);
                uint _S1630 = 2U / _S1627;
                int3 _S1631 = int3(_S1629, int(_S1630), int(0));
                int _S1632 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1631)).xy), uint(((_S1631)).z))).x * 255.0f)));
                uint _S1633 = atlasWidth_18;
                uint _S1634 = 3U % atlasWidth_18;
                int _S1635 = int(_S1634);
                uint _S1636 = 3U / _S1633;
                int3 _S1637 = int3(_S1635, int(_S1636), int(0));
                int2 minimumTile_18 = int2(_S1632, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1637)).xy), uint(((_S1637)).z))).x * 255.0f))));
                uint _S1638 = atlasWidth_18;
                uint _S1639 = 4U % atlasWidth_18;
                int _S1640 = int(_S1639);
                uint _S1641 = 4U / _S1638;
                int3 _S1642 = int3(_S1640, int(_S1641), int(0));
                uint _S1643 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1642)).xy), uint(((_S1642)).z))).x * 255.0f));
                uint _S1644 = atlasWidth_18;
                uint _S1645 = 5U % atlasWidth_18;
                int _S1646 = int(_S1645);
                uint _S1647 = 5U / _S1644;
                int3 _S1648 = int3(_S1646, int(_S1647), int(0));
                uint _S1649 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1648)).xy), uint(((_S1648)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1643, _S1649);
                int2 tile_36 = int2(floor(_S180)) - minimumTile_18;
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
                    int3 _S1650 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1650)).xy), uint(((_S1650)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1643 + uint(tile_36.x));
                uint _S1651 = atlasWidth_18;
                uint _S1652 = offset_18 % atlasWidth_18;
                int _S1653 = int(_S1652);
                uint _S1654 = offset_18 / _S1651;
                int3 _S1655 = int3(_S1653, int(_S1654), int(0));
                uint _S1656 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1655)).xy), uint(((_S1655)).z))).x * 255.0f));
                uint _S1657 = atlasWidth_18;
                uint _S1658 = offset_18 + 1U;
                uint _S1659 = _S1658 % atlasWidth_18;
                int _S1660 = int(_S1659);
                uint _S1661 = _S1658 / _S1657;
                int3 _S1662 = int3(_S1660, int(_S1661), int(0));
                uint slot_36 = _S1656 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1662)).xy), uint(((_S1662)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1663 = int3(int(1), int(0), int(0));
                    _S182 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1663)).xy), uint(((_S1663)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1664 = atlasWidth_18;
                uint _S1665 = 6U % atlasWidth_18;
                int _S1666 = int(_S1665);
                uint _S1667 = 6U / _S1664;
                int3 _S1668 = int3(_S1666, int(_S1667), int(0));
                uint _S1669 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1668)).xy), uint(((_S1668)).z))).x * 255.0f));
                uint _S1670 = atlasWidth_18;
                uint _S1671 = 7U % atlasWidth_18;
                int _S1672 = int(_S1671);
                uint _S1673 = 7U / _S1670;
                int3 _S1674 = int3(_S1672, int(_S1673), int(0));
                uint columns_18 = _S1669 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1674)).xy), uint(((_S1674)).z))).x * 255.0f)) << 8U);
                uint _S1675 = atlasWidth_18;
                uint _S1676 = 8U % atlasWidth_18;
                int _S1677 = int(_S1676);
                uint _S1678 = 8U / _S1675;
                int3 _S1679 = int3(_S1677, int(_S1678), int(0));
                uint _S1680 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1679)).xy), uint(((_S1679)).z))).x * 255.0f));
                uint _S1681 = atlasWidth_18;
                uint _S1682 = 9U % atlasWidth_18;
                int _S1683 = int(_S1682);
                uint _S1684 = 9U / _S1681;
                int3 _S1685 = int3(_S1683, int(_S1684), int(0));
                uint rows_18 = ((_S1680 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1685)).xy), uint(((_S1685)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1643 * _S1649 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1686 = atlasWidth_18 / columns_18;
                float _S1687 = float(_S1686);
                uint _S1688 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1687, float(_S1688));
                uint _S1689 = slot_37 % columns_18;
                float _S1690 = float(_S1689);
                uint _S1691 = slot_37 / columns_18;
                _S182 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1690, float(_S1691)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(_S180) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1692 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(_S180)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1692);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1693 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1693)).xy), uint(((_S1693)).z)));
                break;
            }
            uint _S1694 = atlasWidth_18 / uint(_S1692.x);
            float _S1695 = float(_S1694);
            uint _S1696 = (atlasHeight_18 - 1U) / uint(_S1692.y);
            float2 cellSize_37 = float2(_S1695, float(_S1696));
            _S182 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1697 = float4(_S7->compositeControls_0) ;
            if((_S1697.x) != 2048.0f)
            {
                break;
            }
            bool _S1698 = (_S1697.w) >= 0.5f;
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
            uint _S1699 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1698)
                {
                    for(;;)
                    {
                        if(_S1699 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1700 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_23;
                        thread uint height_23;
                        (*((&width_23)) = (_S1700).get_width(0)),(*((&height_23)) = (_S1700).get_height(0));
                        float2 size_21 = float2(float(width_23), float(height_23));
                        float2 _S1701 = float2(0.5f) ;
                        float2 remapped_21 = clamp((_S180 * (size_21 - float2(2.0f) ) + float2(1.0f) ) / size_21, _S1701 / size_21, (size_21 - _S1701) / size_21);
                        if((_S1699 & 1U) != 0U)
                        {
                            ior_0 = remapped_21.x;
                        }
                        else
                        {
                            ior_0 = _S180.x;
                        }
                        if((_S1699 & 2U) != 0U)
                        {
                            lightType_0 = remapped_21.y;
                        }
                        else
                        {
                            lightType_0 = _S180.y;
                        }
                        _S180 = float2(ior_0, lightType_0);
                        break;
                    }
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S180)));
                    break;
                }
                texture2d<float, access::sample> _S1702 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1702).get_width(0)),(*((&atlasHeight_19)) = (_S1702).get_height(0));
                int3 _S1703 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1703)).xy), uint(((_S1703)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1704 = atlasWidth_19;
                    uint _S1705 = 2U % atlasWidth_19;
                    int _S1706 = int(_S1705);
                    uint _S1707 = 2U / _S1704;
                    int3 _S1708 = int3(_S1706, int(_S1707), int(0));
                    int _S1709 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1708)).xy), uint(((_S1708)).z))).x * 255.0f)));
                    uint _S1710 = atlasWidth_19;
                    uint _S1711 = 3U % atlasWidth_19;
                    int _S1712 = int(_S1711);
                    uint _S1713 = 3U / _S1710;
                    int3 _S1714 = int3(_S1712, int(_S1713), int(0));
                    int2 minimumTile_19 = int2(_S1709, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1714)).xy), uint(((_S1714)).z))).x * 255.0f))));
                    uint _S1715 = atlasWidth_19;
                    uint _S1716 = 4U % atlasWidth_19;
                    int _S1717 = int(_S1716);
                    uint _S1718 = 4U / _S1715;
                    int3 _S1719 = int3(_S1717, int(_S1718), int(0));
                    uint _S1720 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1719)).xy), uint(((_S1719)).z))).x * 255.0f));
                    uint _S1721 = atlasWidth_19;
                    uint _S1722 = 5U % atlasWidth_19;
                    int _S1723 = int(_S1722);
                    uint _S1724 = 5U / _S1721;
                    int3 _S1725 = int3(_S1723, int(_S1724), int(0));
                    uint _S1726 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1725)).xy), uint(((_S1725)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1720, _S1726);
                    int2 tile_38 = int2(floor(_S180)) - minimumTile_19;
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
                        int3 _S1727 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1727)).xy), uint(((_S1727)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1720 + uint(tile_38.x));
                    uint _S1728 = atlasWidth_19;
                    uint _S1729 = offset_19 % atlasWidth_19;
                    int _S1730 = int(_S1729);
                    uint _S1731 = offset_19 / _S1728;
                    int3 _S1732 = int3(_S1730, int(_S1731), int(0));
                    uint _S1733 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1732)).xy), uint(((_S1732)).z))).x * 255.0f));
                    uint _S1734 = atlasWidth_19;
                    uint _S1735 = offset_19 + 1U;
                    uint _S1736 = _S1735 % atlasWidth_19;
                    int _S1737 = int(_S1736);
                    uint _S1738 = _S1735 / _S1734;
                    int3 _S1739 = int3(_S1737, int(_S1738), int(0));
                    uint slot_38 = _S1733 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1739)).xy), uint(((_S1739)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1740 = int3(int(1), int(0), int(0));
                        _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1740)).xy), uint(((_S1740)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1741 = atlasWidth_19;
                    uint _S1742 = 6U % atlasWidth_19;
                    int _S1743 = int(_S1742);
                    uint _S1744 = 6U / _S1741;
                    int3 _S1745 = int3(_S1743, int(_S1744), int(0));
                    uint _S1746 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1745)).xy), uint(((_S1745)).z))).x * 255.0f));
                    uint _S1747 = atlasWidth_19;
                    uint _S1748 = 7U % atlasWidth_19;
                    int _S1749 = int(_S1748);
                    uint _S1750 = 7U / _S1747;
                    int3 _S1751 = int3(_S1749, int(_S1750), int(0));
                    uint columns_19 = _S1746 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1751)).xy), uint(((_S1751)).z))).x * 255.0f)) << 8U);
                    uint _S1752 = atlasWidth_19;
                    uint _S1753 = 8U % atlasWidth_19;
                    int _S1754 = int(_S1753);
                    uint _S1755 = 8U / _S1752;
                    int3 _S1756 = int3(_S1754, int(_S1755), int(0));
                    uint _S1757 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1756)).xy), uint(((_S1756)).z))).x * 255.0f));
                    uint _S1758 = atlasWidth_19;
                    uint _S1759 = 9U % atlasWidth_19;
                    int _S1760 = int(_S1759);
                    uint _S1761 = 9U / _S1758;
                    int3 _S1762 = int3(_S1760, int(_S1761), int(0));
                    uint rows_19 = ((_S1757 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1762)).xy), uint(((_S1762)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1720 * _S1726 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1763 = atlasWidth_19 / columns_19;
                    float _S1764 = float(_S1763);
                    uint _S1765 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1764, float(_S1765));
                    uint _S1766 = slot_39 % columns_19;
                    float _S1767 = float(_S1766);
                    uint _S1768 = slot_39 / columns_19;
                    _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1767, float(_S1768)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(_S180) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1769 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(_S180)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1769);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1770 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S367 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1770)).xy), uint(((_S1770)).z)));
                    break;
                }
                uint _S1771 = atlasWidth_19 / uint(_S1769.x);
                float _S1772 = float(_S1771);
                uint _S1773 = (atlasHeight_19 - 1U) / uint(_S1769.y);
                float2 cellSize_39 = float2(_S1772, float(_S1773));
                _S367 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(_S180) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_9 = uint(round(_S1697.y));
            if(operation_9 == 1U)
            {
                _S182 = _S182 * _S367;
                break;
            }
            if(operation_9 == 2U)
            {
                _S182 = _S182 + _S367;
                break;
            }
            if(operation_9 == 3U)
            {
                _S182 = _S182 - _S367;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1697.z;
                _S182 = _S182 * float4((1.0f - factor_9))  + _S367 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S182.x;
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
    float3 irradiance_0;
    if(isFrontFace_0)
    {
        irradiance_0 = normal_1;
    }
    else
    {
        irradiance_0 = - normal_1;
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
    float _S1774 = max(0.00100000004749745f, roughness_0);
    float _S1775 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1776 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1776;
    bool omniPbrSpecular_0 = (_S162.w) >= 0.5f;
    float dielectricLevel_0 = _S148.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1777 = float3(dielectricLevel_0) ;
        float3 _S1778 = float3(metallic_0) ;
        float3 _S1779 = mix(_S1777, diffuseColor_0, _S1778);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1777, diffuseColor_0, _S1778);
        grazingIncidence_0 = _S1779;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S154.w) >= 0.5f)
        {
            float3 _S1780 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1780;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1781 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1781);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1781);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1782 = _S2;
    float _S1783 = _S2.ambientLight_0.w;
    if(_S1783 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1784 = _S1782.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1784, _S1784)) > 0.0f;
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
    uint _S1785 = min(uint(_S1783), 128U);
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
    float4 _S1786 = float4(_S7->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S1786.w) ;
    uint domeLinkMask_0 = uint(max(_S164.x, 0.0f));
    FragmentFrameParameters_0 _S1787 = _S2;
    uint _S1788 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S1788) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S1789 = _S1782.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S1790 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S1790;
        for(;;)
        {
            if(lightCount_0 < _S1788)
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
        domeAmbient_1 = _S1789;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S1791 = !hasSceneLighting_0;
    if(_S1791)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1785;
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
        bool _S1792;
        if(hasSceneLighting_0)
        {
            _S1792 = (((uint4(_S7->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S1792 = false;
        }
        if(_S1792)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S1793 = lightIndex_0 == 0U;
        bool _S1794;
        if(_S1793)
        {
            _S1794 = _S1791;
        }
        else
        {
            _S1794 = false;
        }
        if(_S1794)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S1795 = 224U + lightIndex_0 * 16U;
            float _S1796 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1795)>>2]);
            float _S1797 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1795 + 4U)>>2]);
            float _S1798 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1795 + 8U)>>2]);
            float _S1799 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1795 + 12U)>>2]);
            lightType_0 = _S1799;
        }
        bool _S1800;
        if(_S1793)
        {
            _S1800 = _S1791;
        }
        else
        {
            _S1800 = false;
        }
        if(_S1800)
        {
            lightDirectionAxis_0 = normalize((float4(_S7->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S1801 = 2272U + lightIndex_0 * 16U;
            float _S1802 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1801)>>2]);
            float _S1803 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1801 + 4U)>>2]);
            float _S1804 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1801 + 8U)>>2]);
            float _S1805 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1801 + 12U)>>2]);
            lightDirectionAxis_0 = float3(_S1802, _S1803, _S1804);
        }
        float3 lightDirection_0 = normalize(lightDirectionAxis_0);
        bool _S1806;
        if(_S1793)
        {
            _S1806 = _S1791;
        }
        else
        {
            _S1806 = false;
        }
        if(_S1806)
        {
            roughness_0 = (float4(_S7->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S1807 = 4320U + lightIndex_0 * 16U;
            float _S1808 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1807)>>2]);
            float _S1809 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1807 + 4U)>>2]);
            float _S1810 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1807 + 8U)>>2]);
            float _S1811 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1807 + 12U)>>2]);
            roughness_0 = _S1811;
        }
        bool _S1812;
        if(_S1793)
        {
            _S1812 = _S1791;
        }
        else
        {
            _S1812 = false;
        }
        if(_S1812)
        {
            diffuseColor_0 = _S1786.xyz;
        }
        else
        {
            uint _S1813 = 4320U + lightIndex_0 * 16U;
            float _S1814 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1813)>>2]);
            float _S1815 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1813 + 4U)>>2]);
            float _S1816 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1813 + 8U)>>2]);
            float _S1817 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1813 + 12U)>>2]);
            diffuseColor_0 = float3(_S1814, _S1815, _S1816);
        }
        bool _S1818;
        if(_S1793)
        {
            _S1818 = _S1791;
        }
        else
        {
            _S1818 = false;
        }
        if(_S1818)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S1819 = 6368U + lightIndex_0 * 16U;
            float _S1820 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1819)>>2]);
            float _S1821 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1819 + 4U)>>2]);
            float _S1822 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1819 + 8U)>>2]);
            float _S1823 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1819 + 12U)>>2]);
            metallic_0 = _S1820;
        }
        bool _S1824;
        if(_S1793)
        {
            _S1824 = _S1791;
        }
        else
        {
            _S1824 = false;
        }
        if(_S1824)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S1825 = 6368U + lightIndex_0 * 16U;
            float _S1826 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1825)>>2]);
            float _S1827 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1825 + 4U)>>2]);
            float _S1828 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1825 + 8U)>>2]);
            float _S1829 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1825 + 12U)>>2]);
            clearcoatRoughness_0 = _S1827;
        }
        bool _S1830;
        if(_S1793)
        {
            _S1830 = _S1791;
        }
        else
        {
            _S1830 = false;
        }
        if(_S1830)
        {
            lightTangentAxis_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1831 = 8416U + lightIndex_0 * 16U;
            float _S1832 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1831)>>2]);
            float _S1833 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1831 + 4U)>>2]);
            float _S1834 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1831 + 8U)>>2]);
            float _S1835 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1831 + 12U)>>2]);
            lightTangentAxis_0 = float3(_S1832, _S1833, _S1834);
        }
        bool _S1836;
        if(_S1793)
        {
            _S1836 = _S1791;
        }
        else
        {
            _S1836 = false;
        }
        if(_S1836)
        {
            lightBitangentAxis_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S1837 = 10464U + lightIndex_0 * 16U;
            float _S1838 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1837)>>2]);
            float _S1839 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1837 + 4U)>>2]);
            float _S1840 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1837 + 8U)>>2]);
            float _S1841 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1837 + 12U)>>2]);
            lightBitangentAxis_0 = float3(_S1838, _S1839, _S1840);
        }
        bool _S1842;
        if(_S1793)
        {
            _S1842 = _S1791;
        }
        else
        {
            _S1842 = false;
        }
        float shapeX_0;
        if(_S1842)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S1843 = 8416U + lightIndex_0 * 16U;
            float _S1844 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1843)>>2]);
            float _S1845 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1843 + 4U)>>2]);
            float _S1846 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1843 + 8U)>>2]);
            float _S1847 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1843 + 12U)>>2]);
            shapeX_0 = _S1847;
        }
        bool _S1848;
        if(_S1793)
        {
            _S1848 = _S1791;
        }
        else
        {
            _S1848 = false;
        }
        float shapeY_0;
        if(_S1848)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S1849 = 10464U + lightIndex_0 * 16U;
            float _S1850 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1849)>>2]);
            float _S1851 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1849 + 4U)>>2]);
            float _S1852 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1849 + 8U)>>2]);
            float _S1853 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1849 + 12U)>>2]);
            shapeY_0 = _S1853;
        }
        bool _S1854;
        if(_S1793)
        {
            _S1854 = _S1791;
        }
        else
        {
            _S1854 = false;
        }
        float lightRadius_0;
        if(_S1854)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S1855 = 2272U + lightIndex_0 * 16U;
            float _S1856 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1855)>>2]);
            float _S1857 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1855 + 4U)>>2]);
            float _S1858 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1855 + 8U)>>2]);
            float _S1859 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1855 + 12U)>>2]);
            lightRadius_0 = _S1859;
        }
        bool _S1860;
        if(_S1793)
        {
            _S1860 = _S1791;
        }
        else
        {
            _S1860 = false;
        }
        float3 lightEyeBase_0;
        if(_S1860)
        {
            lightEyeBase_0 = diffuseFresnel_0;
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
            directNormal_0 = irradiance_0;
        }
        float _S1861 = saturate(abs(dot(directNormal_0, lightEyeBase_0)) + 0.00000999999974738f);
        bool _S1862;
        if(_S1793)
        {
            _S1862 = _S1791;
        }
        else
        {
            _S1862 = false;
        }
        if(_S1862)
        {
            _S182 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1863 = 17216U + lightIndex_0 * 16U;
            float _S1864 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1863)>>2]);
            float _S1865 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1863 + 4U)>>2]);
            float _S1866 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1863 + 8U)>>2]);
            float _S1867 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1863 + 12U)>>2]);
            _S182 = float4(_S1864, _S1865, _S1866, _S1867);
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S1868 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S1868;
        sampleOffsets_0[int(1)] = _S1868;
        sampleOffsets_0[int(2)] = _S1868;
        sampleOffsets_0[int(3)] = _S1868;
        sampleOffsets_0[int(4)] = _S1868;
        thread array<float3, int(5)> sampleEmitterNormals_0;
        float3 _S1869 = - lightDirection_0;
        sampleEmitterNormals_0[int(0)] = _S1869;
        sampleEmitterNormals_0[int(1)] = _S1869;
        sampleEmitterNormals_0[int(2)] = _S1869;
        sampleEmitterNormals_0[int(3)] = _S1869;
        sampleEmitterNormals_0[int(4)] = _S1869;
        bool _S1870 = lightType_0 == 2.0f;
        bool _S1871;
        if(_S1870)
        {
            _S1871 = lightRadius_0 > 0.0f;
        }
        else
        {
            _S1871 = false;
        }
        float sampleCount_0;
        float shadowVisibility_0;
        float sampleIntensity_0;
        bool useEmitterShadowSamples_0;
        if(_S1871)
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
                float3 _S1872 = - halfWidth_0;
                sampleOffsets_0[int(3)] = _S1872 + halfHeight_0;
                sampleOffsets_0[int(4)] = _S1872 - halfHeight_0;
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
                    float3 _S1873 = cross(y_0, lightDirection_0);
                    if((dot(x_0, _S1873)) < 0.0f)
                    {
                        sampleCount_0 = -1.0f;
                    }
                    else
                    {
                        sampleCount_0 = 1.0f;
                    }
                    bool _S1874 = (abs(0.0f)) > 0.5f;
                    float3 _S1875 = float3(0.0f) ;
                    float3 _S1876 = normalize((select(_S1868, _S1875 * _S1873, _S1874) + select(_S1868, _S1875 * cross(lightDirection_0, x_0), _S1874) + select(_S1868, float3(-1.0f)  * cross(x_0, y_0), (abs(-1.0f)) > 0.5f)) * float3(sampleCount_0) );
                    sampleEmitterNormals_0[int(0)] = _S1876;
                    sampleEmitterNormals_0[int(1)] = _S1876;
                    sampleEmitterNormals_0[int(2)] = _S1876;
                    sampleEmitterNormals_0[int(3)] = _S1876;
                    sampleEmitterNormals_0[int(4)] = _S1876;
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
                            float3 _S1877 = lightTangentAxis_0 * float3(lightRadius_0) ;
                            sampleOffsets_0[int(0)] = _S1877 - halfLength_0;
                            sampleOffsets_0[int(1)] = _S1877 + halfLength_0;
                            float3 _S1878 = - lightTangentAxis_0 * float3(lightRadius_0) ;
                            sampleOffsets_0[int(2)] = _S1878 - halfLength_0;
                            sampleOffsets_0[int(3)] = _S1878 + halfLength_0;
                            sampleOffsets_0[int(4)] = lightBitangentAxis_0 * float3(lightRadius_0) ;
                            float3 x_1 = normalize(lightTangentAxis_0);
                            float3 y_1 = normalize(lightBitangentAxis_0);
                            float3 _S1879 = cross(y_1, lightDirection_0);
                            bool _S1880 = (dot(x_1, _S1879)) < 0.0f;
                            if(_S1880)
                            {
                                sampleCount_0 = -1.0f;
                            }
                            else
                            {
                                sampleCount_0 = 1.0f;
                            }
                            bool _S1881 = (abs(1.0f)) > 0.5f;
                            bool _S1882 = (abs(0.0f)) > 0.5f;
                            float3 _S1883 = cross(lightDirection_0, x_1);
                            float3 _S1884 = float3(0.0f) ;
                            float3 _S1885 = _S1884 * _S1883;
                            float3 _S1886 = _S1884 * cross(x_1, y_1);
                            float3 _S1887 = normalize((select(_S1868, _S1879, _S1881) + select(_S1868, _S1885, _S1882) + select(_S1868, _S1886, _S1882)) * float3(sampleCount_0) );
                            sampleEmitterNormals_0[int(0)] = _S1887;
                            sampleEmitterNormals_0[int(1)] = _S1887;
                            if(_S1880)
                            {
                                shadowVisibility_0 = -1.0f;
                            }
                            else
                            {
                                shadowVisibility_0 = 1.0f;
                            }
                            float3 _S1888 = normalize((select(_S1868, float3(-1.0f)  * _S1879, (abs(-1.0f)) > 0.5f) + select(_S1868, _S1885, _S1882) + select(_S1868, _S1886, _S1882)) * float3(shadowVisibility_0) );
                            sampleEmitterNormals_0[int(2)] = _S1888;
                            sampleEmitterNormals_0[int(3)] = _S1888;
                            if(_S1880)
                            {
                                sampleIntensity_0 = -1.0f;
                            }
                            else
                            {
                                sampleIntensity_0 = 1.0f;
                            }
                            float3 _S1889 = normalize((select(_S1868, _S1884 * _S1879, _S1882) + select(_S1868, _S1883, _S1881) + select(_S1868, _S1886, _S1882)) * float3(sampleIntensity_0) );
                            sampleEmitterNormals_0[int(4)] = _S1889;
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
        float sampleShadowVisibility_0;
        bool _S1890;
        bool _S1891;
        if(hasSceneLighting_0)
        {
            uint _S1892 = lightIndex_0 * 16U;
            uint _S1893 = 14880U + _S1892;
            float _S1894 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1893)>>2]);
            float _S1895 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1893 + 4U)>>2]);
            float _S1896 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1893 + 8U)>>2]);
            float _S1897 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1893 + 12U)>>2]);
            int shadowSlot_0 = int(_S1894);
            if(shadowSlot_0 >= int(0))
            {
                int shadowSlot_1;
                int y_2;
                if(_S1895 == 6.0f)
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
                        int _S1898 = shadowSlot_0 + y_2;
                        uint _S1899 = 12576U + uint(_S1898) * 64U;
                        float _S1900 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899)>>2]);
                        float _S1901 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 4U)>>2]);
                        float _S1902 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 8U)>>2]);
                        float _S1903 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 12U)>>2]);
                        float4 _S1904 = float4(_S1900, _S1901, _S1902, _S1903);
                        float _S1905 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 16U)>>2]);
                        float _S1906 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 20U)>>2]);
                        float _S1907 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 24U)>>2]);
                        float _S1908 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 28U)>>2]);
                        float4 _S1909 = float4(_S1905, _S1906, _S1907, _S1908);
                        float _S1910 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 32U)>>2]);
                        float _S1911 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 36U)>>2]);
                        float _S1912 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 40U)>>2]);
                        float _S1913 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 44U)>>2]);
                        float4 _S1914 = float4(_S1910, _S1911, _S1912, _S1913);
                        float _S1915 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 48U)>>2]);
                        float _S1916 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 52U)>>2]);
                        float _S1917 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 56U)>>2]);
                        float _S1918 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1899 + 60U)>>2]);
                        float _S1919 = (((float4(worldPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S1904, _S1909, _S1914, float4(_S1915, _S1916, _S1917, _S1918))))).w;
                        if(_S1919 > shadowVisibility_0)
                        {
                            shadowVisibility_0 = _S1919;
                            shadowSlot_1 = _S1898;
                        }
                        y_2 = y_2 + int(1);
                    }
                }
                else
                {
                    shadowSlot_1 = shadowSlot_0;
                }
                bool _S1920;
                int x_2;
                if(_S1895 == 5.0f)
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
                        uint _S1921 = 224U + _S1892;
                        float _S1922 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1921)>>2]);
                        float _S1923 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1921 + 4U)>>2]);
                        float _S1924 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1921 + 8U)>>2]);
                        float _S1925 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1921 + 12U)>>2]);
                        float3 toShadowSample_0 = float3(_S1922, _S1923, _S1924) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                        float3 shadowLightDirection_0 = toShadowSample_0 * float3(rsqrt(max(dot(toShadowSample_0, toShadowSample_0), 0.00100000004749745f))) ;
                        int _S1926 = shadowSlot_1 + int(sampleIndex_0);
                        for(;;)
                        {
                            uint _S1927 = uint(_S1926);
                            uint _S1928 = _S1927 * 16U;
                            uint _S1929 = 14496U + _S1928;
                            float _S1930 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1929)>>2]);
                            float _S1931 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1929 + 4U)>>2]);
                            float _S1932 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1929 + 8U)>>2]);
                            float _S1933 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1929 + 12U)>>2]);
                            uint _S1934 = 14112U + _S1928;
                            float _S1935 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1934)>>2]);
                            float _S1936 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1934 + 4U)>>2]);
                            float _S1937 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1934 + 8U)>>2]);
                            float _S1938 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1934 + 12U)>>2]);
                            if((dot(specularColor_0, shadowLightDirection_0)) < 0.0f)
                            {
                                sampleDirection_0 = - specularColor_0;
                            }
                            else
                            {
                                sampleDirection_0 = specularColor_0;
                            }
                            float slope_0 = clamp(1.0f - saturate(dot(sampleDirection_0, shadowLightDirection_0)), 0.0f, 1.0f);
                            float3 offsetPosition_0 = worldPosition_0 + sampleDirection_0 * float3((_S1931 * slope_0)) ;
                            uint _S1939 = 12576U + _S1927 * 64U;
                            float _S1940 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939)>>2]);
                            float _S1941 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 4U)>>2]);
                            float _S1942 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 8U)>>2]);
                            float _S1943 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 12U)>>2]);
                            float4 _S1944 = float4(_S1940, _S1941, _S1942, _S1943);
                            float _S1945 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 16U)>>2]);
                            float _S1946 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 20U)>>2]);
                            float _S1947 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 24U)>>2]);
                            float _S1948 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 28U)>>2]);
                            float4 _S1949 = float4(_S1945, _S1946, _S1947, _S1948);
                            float _S1950 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 32U)>>2]);
                            float _S1951 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 36U)>>2]);
                            float _S1952 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 40U)>>2]);
                            float _S1953 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 44U)>>2]);
                            float4 _S1954 = float4(_S1950, _S1951, _S1952, _S1953);
                            float _S1955 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 48U)>>2]);
                            float _S1956 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 52U)>>2]);
                            float _S1957 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 56U)>>2]);
                            float _S1958 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1939 + 60U)>>2]);
                            float4 lightClip_0 = (((float4(offsetPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S1944, _S1949, _S1954, float4(_S1955, _S1956, _S1957, _S1958)))));
                            float _S1959 = lightClip_0.w;
                            if(_S1959 <= 0.0f)
                            {
                                ior_0 = 1.0f;
                                break;
                            }
                            float3 ndc_0 = lightClip_0.xyz / float3(_S1959) ;
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
                                _S1890 = true;
                            }
                            else
                            {
                                _S1890 = (ndc_0.z) < 0.0f;
                            }
                            if(_S1890)
                            {
                                _S1891 = true;
                            }
                            else
                            {
                                _S1891 = (ndc_0.z) > 1.0f;
                            }
                            if(_S1891)
                            {
                                ior_0 = 1.0f;
                                break;
                            }
                            float2 _S1960 = float2(_S1935, _S1936);
                            float2 _S1961 = float2(_S1937, _S1938);
                            float2 _S1962 = _S1960 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S1961;
                            bool transmission_0 = _S1932 < 0.0f;
                            if(transmission_0)
                            {
                                ior_0 = - _S1932 - 1.0f;
                            }
                            else
                            {
                                ior_0 = max(_S1932, 0.0f);
                            }
                            float _S1963 = _S1930 * (1.0f + 2.0f * slope_0);
                            float _S1964 = ndc_0.z - _S1963;
                            if(_S1896 > 0.0f)
                            {
                                float linearDepth_0 = _S1959 * _S1896;
                                if(linearDepth_0 > 1.0f)
                                {
                                    ior_0 = 1.0f;
                                    break;
                                }
                                shadowVisibility_0 = linearDepth_0 / (1.0f + _S1963 * linearDepth_0);
                            }
                            else
                            {
                                shadowVisibility_0 = _S1964;
                            }
                            float2 _S1965 = float2((_S1933 * 0.5f)) ;
                            float2 _S1966 = _S1960 + _S1965;
                            float2 _S1967 = _S1960 + _S1961 - _S1965;
                            y_2 = int(-1);
                            sampleIntensity_0 = 0.0f;
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
                                    float2 coordinate_0 = clamp(_S1962 + float2(float(x_2), float(y_2)) * float2((ior_0 * _S1933)) , _S1966, _S1967);
                                    if(shadowVisibility_0 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f))).x))
                                    {
                                        emissionScale_0 = 1.0f;
                                    }
                                    else
                                    {
                                        emissionScale_0 = 0.0f;
                                    }
                                    if(transmission_0)
                                    {
                                        _S1920 = emissionScale_0 > 0.0f;
                                    }
                                    else
                                    {
                                        _S1920 = false;
                                    }
                                    if(_S1920)
                                    {
                                        float2 layer0_0 = (((&kernelContext_0)->shadowTransmission0_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float2 layer1_0 = (((&kernelContext_0)->shadowTransmission1_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float2 layer2_0 = (((&kernelContext_0)->shadowTransmission2_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float2 layer3_0 = (((&kernelContext_0)->shadowTransmission3_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float4 depths_0 = float4(layer0_0.x, layer1_0.x, layer2_0.x, layer3_0.x);
                                        float4 weights_0 = select(float4(1.0f, 1.0f, 1.0f, 1.0f), float4(layer0_0.y, layer1_0.y, layer2_0.y, layer3_0.y), (depths_0 >= (float4(0.0f) )) & ((float4(shadowVisibility_0) ) > depths_0));
                                        sampleShadowVisibility_0 = emissionScale_0 * weights_0.x * weights_0.y * weights_0.z * weights_0.w;
                                    }
                                    else
                                    {
                                        sampleShadowVisibility_0 = emissionScale_0;
                                    }
                                    float lit_0 = sampleIntensity_0 + sampleShadowVisibility_0;
                                    x_2 = x_2 + int(1);
                                    sampleIntensity_0 = lit_0;
                                }
                                y_2 = y_2 + int(1);
                            }
                            ior_0 = sampleIntensity_0 * 0.1111111119389534f;
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
                    if(_S1870)
                    {
                        uint _S1968 = 224U + _S1892;
                        float _S1969 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1968)>>2]);
                        float _S1970 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1968 + 4U)>>2]);
                        float _S1971 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1968 + 8U)>>2]);
                        float _S1972 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1968 + 12U)>>2]);
                        float3 toShadowLight_0 = float3(_S1969, _S1970, _S1971) - worldPosition_0;
                        color_3 = toShadowLight_0 * float3(rsqrt(max(dot(toShadowLight_0, toShadowLight_0), 0.00100000004749745f))) ;
                    }
                    else
                    {
                        color_3 = lightDirection_0;
                    }
                    for(;;)
                    {
                        uint _S1973 = uint(shadowSlot_1);
                        uint _S1974 = _S1973 * 16U;
                        uint _S1975 = 14496U + _S1974;
                        float _S1976 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1975)>>2]);
                        float _S1977 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1975 + 4U)>>2]);
                        float _S1978 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1975 + 8U)>>2]);
                        float _S1979 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1975 + 12U)>>2]);
                        uint _S1980 = 14112U + _S1974;
                        float _S1981 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1980)>>2]);
                        float _S1982 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1980 + 4U)>>2]);
                        float _S1983 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1980 + 8U)>>2]);
                        float _S1984 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1980 + 12U)>>2]);
                        if((dot(specularColor_0, color_3)) < 0.0f)
                        {
                            sampleDirection_0 = - specularColor_0;
                        }
                        else
                        {
                            sampleDirection_0 = specularColor_0;
                        }
                        float slope_1 = clamp(1.0f - saturate(dot(sampleDirection_0, color_3)), 0.0f, 1.0f);
                        float3 offsetPosition_1 = worldPosition_0 + sampleDirection_0 * float3((_S1977 * slope_1)) ;
                        uint _S1985 = 12576U + _S1973 * 64U;
                        float _S1986 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985)>>2]);
                        float _S1987 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 4U)>>2]);
                        float _S1988 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 8U)>>2]);
                        float _S1989 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 12U)>>2]);
                        float4 _S1990 = float4(_S1986, _S1987, _S1988, _S1989);
                        float _S1991 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 16U)>>2]);
                        float _S1992 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 20U)>>2]);
                        float _S1993 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 24U)>>2]);
                        float _S1994 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 28U)>>2]);
                        float4 _S1995 = float4(_S1991, _S1992, _S1993, _S1994);
                        float _S1996 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 32U)>>2]);
                        float _S1997 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 36U)>>2]);
                        float _S1998 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 40U)>>2]);
                        float _S1999 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 44U)>>2]);
                        float4 _S2000 = float4(_S1996, _S1997, _S1998, _S1999);
                        float _S2001 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 48U)>>2]);
                        float _S2002 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 52U)>>2]);
                        float _S2003 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 56U)>>2]);
                        float _S2004 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1985 + 60U)>>2]);
                        float4 lightClip_1 = (((float4(offsetPosition_1, 1.0f)) * (matrix<float,int(4),int(4)> (_S1990, _S1995, _S2000, float4(_S2001, _S2002, _S2003, _S2004)))));
                        float _S2005 = lightClip_1.w;
                        if(_S2005 <= 0.0f)
                        {
                            ior_0 = 1.0f;
                            break;
                        }
                        float3 ndc_1 = lightClip_1.xyz / float3(_S2005) ;
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
                            _S1890 = true;
                        }
                        else
                        {
                            _S1890 = (ndc_1.z) < 0.0f;
                        }
                        if(_S1890)
                        {
                            _S1891 = true;
                        }
                        else
                        {
                            _S1891 = (ndc_1.z) > 1.0f;
                        }
                        if(_S1891)
                        {
                            ior_0 = 1.0f;
                            break;
                        }
                        float2 _S2006 = float2(_S1981, _S1982);
                        float2 _S2007 = float2(_S1983, _S1984);
                        float2 _S2008 = _S2006 + (ndc_1.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S2007;
                        bool transmission_1 = _S1978 < 0.0f;
                        if(transmission_1)
                        {
                            ior_0 = - _S1978 - 1.0f;
                        }
                        else
                        {
                            ior_0 = max(_S1978, 0.0f);
                        }
                        float _S2009 = _S1976 * (1.0f + 2.0f * slope_1);
                        float _S2010 = ndc_1.z - _S2009;
                        if(_S1896 > 0.0f)
                        {
                            float linearDepth_1 = _S2005 * _S1896;
                            if(linearDepth_1 > 1.0f)
                            {
                                ior_0 = 1.0f;
                                break;
                            }
                            shadowVisibility_0 = linearDepth_1 / (1.0f + _S2009 * linearDepth_1);
                        }
                        else
                        {
                            shadowVisibility_0 = _S2010;
                        }
                        float2 _S2011 = float2((_S1979 * 0.5f)) ;
                        float2 _S2012 = _S2006 + _S2011;
                        float2 _S2013 = _S2006 + _S2007 - _S2011;
                        y_2 = int(-1);
                        sampleIntensity_0 = 0.0f;
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
                                float2 coordinate_1 = clamp(_S2008 + float2(float(x_2), float(y_2)) * float2((ior_0 * _S1979)) , _S2012, _S2013);
                                if(shadowVisibility_0 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f))).x))
                                {
                                    emissionScale_0 = 1.0f;
                                }
                                else
                                {
                                    emissionScale_0 = 0.0f;
                                }
                                if(transmission_1)
                                {
                                    _S1920 = emissionScale_0 > 0.0f;
                                }
                                else
                                {
                                    _S1920 = false;
                                }
                                if(_S1920)
                                {
                                    float2 layer0_1 = (((&kernelContext_0)->shadowTransmission0_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float2 layer1_1 = (((&kernelContext_0)->shadowTransmission1_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float2 layer2_1 = (((&kernelContext_0)->shadowTransmission2_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float2 layer3_1 = (((&kernelContext_0)->shadowTransmission3_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float4 depths_1 = float4(layer0_1.x, layer1_1.x, layer2_1.x, layer3_1.x);
                                    float4 weights_1 = select(float4(1.0f, 1.0f, 1.0f, 1.0f), float4(layer0_1.y, layer1_1.y, layer2_1.y, layer3_1.y), (depths_1 >= (float4(0.0f) )) & ((float4(shadowVisibility_0) ) > depths_1));
                                    sampleShadowVisibility_0 = emissionScale_0 * weights_1.x * weights_1.y * weights_1.z * weights_1.w;
                                }
                                else
                                {
                                    sampleShadowVisibility_0 = emissionScale_0;
                                }
                                float lit_1 = sampleIntensity_0 + sampleShadowVisibility_0;
                                x_2 = x_2 + int(1);
                                sampleIntensity_0 = lit_1;
                            }
                            y_2 = y_2 + int(1);
                        }
                        ior_0 = sampleIntensity_0 * 0.1111111119389534f;
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
                uint _S2014 = 224U + lightIndex_0 * 16U;
                float _S2015 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2014)>>2]);
                float _S2016 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2014 + 4U)>>2]);
                float _S2017 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2014 + 8U)>>2]);
                float _S2018 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2014 + 12U)>>2]);
                float3 toLight_0 = float3(_S2015, _S2016, _S2017) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S2019 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S2019)) ;
                float sampleIntensity_2 = sampleIntensity_1 / _S2019;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    _S1890 = true;
                }
                else
                {
                    if(lightType_0 == 5.0f)
                    {
                        _S1890 = lightRadius_0 > 0.0f;
                    }
                    else
                    {
                        _S1890 = false;
                    }
                    if(_S1890)
                    {
                        _S1891 = shapeX_0 > 0.0f;
                    }
                    else
                    {
                        _S1891 = false;
                    }
                    _S1890 = _S1891;
                }
                if(_S1890)
                {
                    sampleIntensity_0 = saturate(dot(sampleEmitterNormals_0[sampleIndex_0], - sampleDirection_1));
                }
                else
                {
                    sampleIntensity_0 = 1.0f;
                }
                if((_S182.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S182.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S2020 = _S182.y;
                        if(_S2020 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S2020);
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
            float _S2021 = pow(max(0.0f, 1.0f - saturate(dot(lightEyeBase_0, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S2021)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S2021) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S2022 = float3(_S2021) ;
            float _S2023 = max(_S1774, 0.00100000004749745f);
            float alpha_0 = _S2023 * _S2023;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S2024 = 1.0f - k_0;
            float3 _S2025 = float3(max(4.0f * normalDotLight_0 * _S1861, 1.00000000317107685e-30f)) ;
            float3 _S2026 = mix(normalIncidence_0, grazingIncidence_0, _S2022) * float3((_S1861 / (_S1861 * _S2024 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S2024 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S2025;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S2027 = max(_S1775, 0.00100000004749745f);
                float alpha_1 = _S2027 * _S2027;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S2028 = 1.0f - k_1;
                directSpecular_0 = _S2026 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S2022) * float3((_S1861 / (_S1861 * _S2028 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S2028 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S2025);
            }
            else
            {
                directSpecular_0 = _S2026;
            }
            float3 _S2029 = diffuseColor_0 * float3(sampleIntensity_0) ;
            float3 diffuseIrradiance_0 = _S2029 * _S1776;
            if(useEmitterShadowSamples_0)
            {
                sampleShadowVisibility_0 = shadowVisibilityBySample_0[sampleIndex_0];
            }
            else
            {
                sampleShadowVisibility_0 = shadowVisibility_0;
            }
            color_3 = color_3 + float3((sampleShadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * diffuseIrradiance_0 + directSpecular_0 * float3(clearcoatRoughness_0)  * _S2029);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S2030 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S2031 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S2032 = saturate(_S1774);
        float2 _S2033 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2031, _S2032)), level((0.0f)))).xy;
        float _S2034 = _S2033.x;
        float _S2035 = _S2033.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S2034)  + grazingIncidence_0 * float3(_S2035) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S2034 + _S2035)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S2036 = max(_S1787.domeControls_0.y, 1.0f);
        float3 _S2037 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S2036 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1787.domeControls_0.z;
            float _S2038 = _S1787.domeControls_0.w;
            for(;;)
            {
                bool _S2039 = _S2036 <= 1.0f;
                _S5 = _S2039;
                if(_S2039)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S2038, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S2036)), level((0.0f)))).xyz;
                break;
            }
            float _S2040 = _S2030.environmentControls_0.y;
            float _S2041 = _S2030.environmentControls_0.z;
            for(;;)
            {
                if(_S5)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2042 = max(_S2040, 1.0f);
                    float _S2043 = _S2042 - 1.0f;
                    float slice_0 = _S2032 * max(_S2043, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S2041, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S2042)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S2043) + v_0) / _S2042)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S2044 = max(_S2040, 1.0f);
                float total_0 = _S2044 * _S2036;
                float _S2045 = _S2044 - 1.0f;
                float slice_1 = _S2032 * max(_S2045, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S2041, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S2044;
                specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S2045) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S2046 = max(_S2040, 1.0f);
                        float _S2047 = _S2046 - 1.0f;
                        float slice_2 = saturate(_S1775) * max(_S2047, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S2041, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S2046)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S2047) + v_2) / _S2046)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2048 = max(_S2040, 1.0f);
                    float total_1 = _S2048 * _S2036;
                    float _S2049 = _S2048 - 1.0f;
                    float slice_3 = saturate(_S1775) * max(_S2049, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S2041, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S2048;
                    diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S2049) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightTangentAxis_0 = diffuseFresnel_0;
            }
            else
            {
                lightTangentAxis_0 = _S2037;
            }
            irradiance_0 = diffuseColor_0;
            lightDirectionAxis_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S2037;
            lightDirectionAxis_0 = _S2037;
            lightTangentAxis_0 = _S2037;
            for(;;)
            {
                if(sampleIndex_0 < _S1788)
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
                float _S2050 = _S1787.domeControls_0.w;
                for(;;)
                {
                    bool _S2051 = _S2036 <= 1.0f;
                    _S6 = _S2051;
                    if(_S2051)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S2050, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S2036)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                float _S2052 = _S2030.environmentControls_0.y;
                float _S2053 = _S2030.environmentControls_0.z;
                for(;;)
                {
                    if(_S6)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2054 = max(_S2052, 1.0f);
                        float _S2055 = _S2054 - 1.0f;
                        float slice_4 = _S2032 * max(_S2055, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S2053, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S2054)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S2055) + v_4) / _S2054)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2056 = max(_S2052, 1.0f);
                    float total_2 = _S2056 * _S2036;
                    float _S2057 = _S2056 - 1.0f;
                    float slice_5 = _S2032 * max(_S2057, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S2053, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S2056;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S2057) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightDirectionAxis_0 + specularColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S6)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S2058 = max(_S2052, 1.0f);
                            float _S2059 = _S2058 - 1.0f;
                            float slice_6 = saturate(_S1775) * max(_S2059, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S2053, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S2058)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S2059) + v_6) / _S2058)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2060 = max(_S2052, 1.0f);
                        float total_3 = _S2060 * _S2036;
                        float _S2061 = _S2060 - 1.0f;
                        float slice_7 = saturate(_S1775) * max(_S2061, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S2053, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S2060;
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S2061) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    lightBitangentAxis_0 = lightTangentAxis_0 + diffuseFresnel_0;
                }
                else
                {
                    lightBitangentAxis_0 = lightTangentAxis_0;
                }
                irradiance_0 = irradiance_1;
                lightDirectionAxis_0 = prefiltered_0;
                lightTangentAxis_0 = lightBitangentAxis_0;
                sampleIndex_0 = sampleIndex_0 + 1U;
            }
        }
        float3 color_4 = color_2 + float3(occlusion_0)  * diffuse_1 * irradiance_0 * diffuseWeight_0 + float3(occlusion_0)  * lightDirectionAxis_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S2062 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2031, saturate(_S1775))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightTangentAxis_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S2062.x)  + float3(_S2062.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + unlitColor_0) * float3(exp2((as_type<float>((_S3.z))))) ;
    if((_S3.y) == 1U)
    {
        color_2 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S2063 = { float4(color_2, opacity_0) };
    return _S2063;
}
