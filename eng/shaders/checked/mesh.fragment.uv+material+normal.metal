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
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 14880U;
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
        float _S40 = as_type<float>((&kernelContext_0)->frameParameters_0[(16928U)>>2]);
        float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(16932U)>>2]);
        float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(16936U)>>2]);
        float _S43 = as_type<float>((&kernelContext_0)->frameParameters_0[(16940U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S40, _S41, _S42, _S43);
        float _S44 = as_type<float>((&kernelContext_0)->frameParameters_0[(16944U)>>2]);
        float _S45 = as_type<float>((&kernelContext_0)->frameParameters_0[(16948U)>>2]);
        float _S46 = as_type<float>((&kernelContext_0)->frameParameters_0[(16952U)>>2]);
        float _S47 = as_type<float>((&kernelContext_0)->frameParameters_0[(16956U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S44, _S45, _S46, _S47);
        float _S48 = as_type<float>((&kernelContext_0)->frameParameters_0[(16960U)>>2]);
        float _S49 = as_type<float>((&kernelContext_0)->frameParameters_0[(16964U)>>2]);
        float _S50 = as_type<float>((&kernelContext_0)->frameParameters_0[(16968U)>>2]);
        float _S51 = as_type<float>((&kernelContext_0)->frameParameters_0[(16972U)>>2]);
        float4 _S52 = float4(_S48, _S49, _S50, _S51);
        float _S53 = as_type<float>((&kernelContext_0)->frameParameters_0[(16976U)>>2]);
        float _S54 = as_type<float>((&kernelContext_0)->frameParameters_0[(16980U)>>2]);
        float _S55 = as_type<float>((&kernelContext_0)->frameParameters_0[(16984U)>>2]);
        float _S56 = as_type<float>((&kernelContext_0)->frameParameters_0[(16988U)>>2]);
        float4 _S57 = float4(_S53, _S54, _S55, _S56);
        float _S58 = as_type<float>((&kernelContext_0)->frameParameters_0[(16992U)>>2]);
        float _S59 = as_type<float>((&kernelContext_0)->frameParameters_0[(16996U)>>2]);
        float _S60 = as_type<float>((&kernelContext_0)->frameParameters_0[(17000U)>>2]);
        float _S61 = as_type<float>((&kernelContext_0)->frameParameters_0[(17004U)>>2]);
        float4 _S62 = float4(_S58, _S59, _S60, _S61);
        float _S63 = as_type<float>((&kernelContext_0)->frameParameters_0[(17008U)>>2]);
        float _S64 = as_type<float>((&kernelContext_0)->frameParameters_0[(17012U)>>2]);
        float _S65 = as_type<float>((&kernelContext_0)->frameParameters_0[(17016U)>>2]);
        float _S66 = as_type<float>((&kernelContext_0)->frameParameters_0[(17020U)>>2]);
        float4 _S67 = float4(_S63, _S64, _S65, _S66);
        float _S68 = as_type<float>((&kernelContext_0)->frameParameters_0[(17024U)>>2]);
        float _S69 = as_type<float>((&kernelContext_0)->frameParameters_0[(17028U)>>2]);
        float _S70 = as_type<float>((&kernelContext_0)->frameParameters_0[(17032U)>>2]);
        float _S71 = as_type<float>((&kernelContext_0)->frameParameters_0[(17036U)>>2]);
        float4 _S72 = float4(_S68, _S69, _S70, _S71);
        float _S73 = as_type<float>((&kernelContext_0)->frameParameters_0[(17040U)>>2]);
        float _S74 = as_type<float>((&kernelContext_0)->frameParameters_0[(17044U)>>2]);
        float _S75 = as_type<float>((&kernelContext_0)->frameParameters_0[(17048U)>>2]);
        float _S76 = as_type<float>((&kernelContext_0)->frameParameters_0[(17052U)>>2]);
        float4 _S77 = float4(_S73, _S74, _S75, _S76);
        float _S78 = as_type<float>((&kernelContext_0)->frameParameters_0[(17056U)>>2]);
        float _S79 = as_type<float>((&kernelContext_0)->frameParameters_0[(17060U)>>2]);
        float _S80 = as_type<float>((&kernelContext_0)->frameParameters_0[(17064U)>>2]);
        float _S81 = as_type<float>((&kernelContext_0)->frameParameters_0[(17068U)>>2]);
        float4 _S82 = float4(_S78, _S79, _S80, _S81);
        float _S83 = as_type<float>((&kernelContext_0)->frameParameters_0[(17072U)>>2]);
        float _S84 = as_type<float>((&kernelContext_0)->frameParameters_0[(17076U)>>2]);
        float _S85 = as_type<float>((&kernelContext_0)->frameParameters_0[(17080U)>>2]);
        float _S86 = as_type<float>((&kernelContext_0)->frameParameters_0[(17084U)>>2]);
        float4 _S87 = float4(_S83, _S84, _S85, _S86);
        float _S88 = as_type<float>((&kernelContext_0)->frameParameters_0[(17088U)>>2]);
        float _S89 = as_type<float>((&kernelContext_0)->frameParameters_0[(17092U)>>2]);
        float _S90 = as_type<float>((&kernelContext_0)->frameParameters_0[(17096U)>>2]);
        float _S91 = as_type<float>((&kernelContext_0)->frameParameters_0[(17100U)>>2]);
        float4 _S92 = float4(_S88, _S89, _S90, _S91);
        float _S93 = as_type<float>((&kernelContext_0)->frameParameters_0[(17104U)>>2]);
        float _S94 = as_type<float>((&kernelContext_0)->frameParameters_0[(17108U)>>2]);
        float _S95 = as_type<float>((&kernelContext_0)->frameParameters_0[(17112U)>>2]);
        float _S96 = as_type<float>((&kernelContext_0)->frameParameters_0[(17116U)>>2]);
        float4 _S97 = float4(_S93, _S94, _S95, _S96);
        float _S98 = as_type<float>((&kernelContext_0)->frameParameters_0[(17120U)>>2]);
        float _S99 = as_type<float>((&kernelContext_0)->frameParameters_0[(17124U)>>2]);
        float _S100 = as_type<float>((&kernelContext_0)->frameParameters_0[(17128U)>>2]);
        float _S101 = as_type<float>((&kernelContext_0)->frameParameters_0[(17132U)>>2]);
        float4 _S102 = float4(_S98, _S99, _S100, _S101);
        float _S103 = as_type<float>((&kernelContext_0)->frameParameters_0[(17136U)>>2]);
        float _S104 = as_type<float>((&kernelContext_0)->frameParameters_0[(17140U)>>2]);
        float _S105 = as_type<float>((&kernelContext_0)->frameParameters_0[(17144U)>>2]);
        float _S106 = as_type<float>((&kernelContext_0)->frameParameters_0[(17148U)>>2]);
        float4 _S107 = float4(_S103, _S104, _S105, _S106);
        float _S108 = as_type<float>((&kernelContext_0)->frameParameters_0[(17152U)>>2]);
        float _S109 = as_type<float>((&kernelContext_0)->frameParameters_0[(17156U)>>2]);
        float _S110 = as_type<float>((&kernelContext_0)->frameParameters_0[(17160U)>>2]);
        float _S111 = as_type<float>((&kernelContext_0)->frameParameters_0[(17164U)>>2]);
        float4 _S112 = float4(_S108, _S109, _S110, _S111);
        float _S113 = as_type<float>((&kernelContext_0)->frameParameters_0[(17168U)>>2]);
        float _S114 = as_type<float>((&kernelContext_0)->frameParameters_0[(17172U)>>2]);
        float _S115 = as_type<float>((&kernelContext_0)->frameParameters_0[(17176U)>>2]);
        float _S116 = as_type<float>((&kernelContext_0)->frameParameters_0[(17180U)>>2]);
        float4 _S117 = float4(_S113, _S114, _S115, _S116);
        float _S118 = as_type<float>((&kernelContext_0)->frameParameters_0[(17184U)>>2]);
        float _S119 = as_type<float>((&kernelContext_0)->frameParameters_0[(17188U)>>2]);
        float _S120 = as_type<float>((&kernelContext_0)->frameParameters_0[(17192U)>>2]);
        float _S121 = as_type<float>((&kernelContext_0)->frameParameters_0[(17196U)>>2]);
        float4 _S122 = float4(_S118, _S119, _S120, _S121);
        float _S123 = as_type<float>((&kernelContext_0)->frameParameters_0[(17200U)>>2]);
        float _S124 = as_type<float>((&kernelContext_0)->frameParameters_0[(17204U)>>2]);
        float _S125 = as_type<float>((&kernelContext_0)->frameParameters_0[(17208U)>>2]);
        float _S126 = as_type<float>((&kernelContext_0)->frameParameters_0[(17212U)>>2]);
        float4 _S127 = float4(_S123, _S124, _S125, _S126);
        (&frame_0)->domeAmbient_0[int(0)] = _S52;
        (&frame_0)->domeAmbient_0[int(1)] = _S57;
        (&frame_0)->domeAmbient_0[int(2)] = _S62;
        (&frame_0)->domeAmbient_0[int(3)] = _S67;
        (&frame_0)->domeAmbient_0[int(4)] = _S72;
        (&frame_0)->domeAmbient_0[int(5)] = _S77;
        (&frame_0)->domeAmbient_0[int(6)] = _S82;
        (&frame_0)->domeAmbient_0[int(7)] = _S87;
        (&frame_0)->domeEnvironment_0[int(0)] = _S92;
        (&frame_0)->domeEnvironment_0[int(1)] = _S97;
        (&frame_0)->domeEnvironment_0[int(2)] = _S102;
        (&frame_0)->domeEnvironment_0[int(3)] = _S107;
        (&frame_0)->domeEnvironment_0[int(4)] = _S112;
        (&frame_0)->domeEnvironment_0[int(5)] = _S117;
        (&frame_0)->domeEnvironment_0[int(6)] = _S122;
        (&frame_0)->domeEnvironment_0[int(7)] = _S127;
        _S2 = frame_0;
        break;
    }
    for(;;)
    {
        _S3 = _S2.clipPlaneCount_0;
        uint _S128 = min(_S2.clipPlaneCount_0.x, 8U);
        index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S128)
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
    float4 _S129 = float4(_S7->clearcoatShaded_0) ;
    float shadedMode_0 = _S129.z;
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
    float4 _S130 = float4(_S7->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S130.xyz;
    float _S131 = _S130.w;
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
        pixelOutput_0 _S132 = { float4(unlitColor_0, opacity_0) };
        return _S132;
    }
    float4 _S133 = float4(_S7->reserved_0) ;
    if((_S133.x) >= 0.5f)
    {
        pixelOutput_0 _S134 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S133.y) * max(0.0f, _S133.z)))) , 1.0f) };
        return _S134;
    }
    float4 _S135 = float4(_S7->metallicRoughnessThresholdWorkflow_0) ;
    float _S136 = saturate(_S135.x);
    float _S137 = clamp(_S135.y, 0.00999999977648258f, 1.0f);
    float4 _S138 = float4(_S7->specularIor_0) ;
    float3 _S139 = _S138.xyz;
    float _S140 = _S138.w;
    float _S141 = _S129.x;
    float _S142 = _S129.y;
    float4 _S143 = float4(_S7->textureControls_0) ;
    uint textureMask_0 = uint(round(_S143.x));
    uint udimMask_0 = uint(round(_S143.y));
    float4 _S144 = float4(_S7->uvTransformRow1_0) ;
    uint secondaryMask_0 = uint(round(_S144.w));
    float4 _S145 = float4(_S7->domeLinkControls_0) ;
    uint alternateMask_0 = uint(round(_S145.w));
    float4 _S146 = float4(_S7->uvTransformRow0_0) ;
    float2 _S147 = _S146.xy;
    float _S148 = _S146.z;
    float2 _S149 = _S144.xy;
    float _S150 = _S144.z;
    float2 _S151 = float2(dot(_S147, _S1.texCoord_0) + _S148, dot(_S149, _S1.texCoord_0) + _S150);
    float2 _S152 = float2(dot(_S147, _S1.texCoord1_0) + _S148, dot(_S149, _S1.texCoord1_0) + _S150);
    float4 _S153 = float4(_S7->alternateUvTransformRow0_0) ;
    float4 _S154 = float4(_S7->alternateUvTransformRow1_0) ;
    float2 _S155 = _S153.xy;
    float _S156 = _S153.z;
    float2 _S157 = _S154.xy;
    float _S158 = _S154.z;
    float2 _S159 = float2(dot(_S155, _S1.texCoord_0) + _S156, dot(_S157, _S1.texCoord_0) + _S158);
    float2 _S160 = float2(dot(_S155, _S1.texCoord1_0) + _S156, dot(_S157, _S1.texCoord1_0) + _S158);
    uint lightCount_0;
    float roughness_0;
    float metallic_0;
    float2 detailSource_0;
    float2 tangent_1;
    bool hasSceneLighting_0;
    float4 _S161;
    uint3 _S162;
    if((textureMask_0 & 2U) != 0U)
    {
        bool useSecondary_0 = (secondaryMask_0 & 2U) != 0U;
        if((alternateMask_0 & 2U) != 0U)
        {
            if(useSecondary_0)
            {
                detailSource_0 = _S160;
            }
            else
            {
                detailSource_0 = _S159;
            }
        }
        else
        {
            if(useSecondary_0)
            {
                detailSource_0 = _S152;
            }
            else
            {
                detailSource_0 = _S151;
            }
        }
        thread float4 diffuseSample_0;
        bool _S163 = (udimMask_0 & 2U) != 0U;
        uint maskU_0 = uint(round(_S145.y));
        uint maskV_0 = uint(round(_S145.z));
        bool _S164 = (maskU_0 & 2U) != 0U;
        if(_S164)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S165 = (maskV_0 & 2U) != 0U;
        if(_S165)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S166 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S163)
            {
                for(;;)
                {
                    if(_S166 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S167 = (&kernelContext_0)->baseColorTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S167).get_width(0)),(*((&height_0)) = (_S167).get_height(0));
                    float2 size_0 = float2(float(width_0), float(height_0));
                    float2 _S168 = float2(0.5f) ;
                    float2 remapped_0 = clamp((detailSource_0 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S168 / size_0, (size_0 - _S168) / size_0);
                    if((_S166 & 1U) != 0U)
                    {
                        roughness_0 = remapped_0.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S166 & 2U) != 0U)
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
                _S161 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S169 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S169).get_width(0)),(*((&atlasHeight_0)) = (_S169).get_height(0));
            int3 _S170 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S170)).xy), uint(((_S170)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S171 = atlasWidth_0;
                uint _S172 = 2U % atlasWidth_0;
                int _S173 = int(_S172);
                uint _S174 = 2U / _S171;
                int3 _S175 = int3(_S173, int(_S174), int(0));
                int _S176 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S175)).xy), uint(((_S175)).z))).x * 255.0f)));
                uint _S177 = atlasWidth_0;
                uint _S178 = 3U % atlasWidth_0;
                int _S179 = int(_S178);
                uint _S180 = 3U / _S177;
                int3 _S181 = int3(_S179, int(_S180), int(0));
                int2 minimumTile_0 = int2(_S176, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S181)).xy), uint(((_S181)).z))).x * 255.0f))));
                uint _S182 = atlasWidth_0;
                uint _S183 = 4U % atlasWidth_0;
                int _S184 = int(_S183);
                uint _S185 = 4U / _S182;
                int3 _S186 = int3(_S184, int(_S185), int(0));
                uint _S187 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S186)).xy), uint(((_S186)).z))).x * 255.0f));
                uint _S188 = atlasWidth_0;
                uint _S189 = 5U % atlasWidth_0;
                int _S190 = int(_S189);
                uint _S191 = 5U / _S188;
                int3 _S192 = int3(_S190, int(_S191), int(0));
                uint _S193 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S192)).xy), uint(((_S192)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S187, _S193);
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
                    int3 _S194 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S194)).xy), uint(((_S194)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S187 + uint(tile_0.x));
                uint _S195 = atlasWidth_0;
                uint _S196 = offset_0 % atlasWidth_0;
                int _S197 = int(_S196);
                uint _S198 = offset_0 / _S195;
                int3 _S199 = int3(_S197, int(_S198), int(0));
                uint _S200 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S199)).xy), uint(((_S199)).z))).x * 255.0f));
                uint _S201 = atlasWidth_0;
                uint _S202 = offset_0 + 1U;
                uint _S203 = _S202 % atlasWidth_0;
                int _S204 = int(_S203);
                uint _S205 = _S202 / _S201;
                int3 _S206 = int3(_S204, int(_S205), int(0));
                uint slot_0 = _S200 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S206)).xy), uint(((_S206)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S207 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S207)).xy), uint(((_S207)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S208 = atlasWidth_0;
                uint _S209 = 6U % atlasWidth_0;
                int _S210 = int(_S209);
                uint _S211 = 6U / _S208;
                int3 _S212 = int3(_S210, int(_S211), int(0));
                uint _S213 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S212)).xy), uint(((_S212)).z))).x * 255.0f));
                uint _S214 = atlasWidth_0;
                uint _S215 = 7U % atlasWidth_0;
                int _S216 = int(_S215);
                uint _S217 = 7U / _S214;
                int3 _S218 = int3(_S216, int(_S217), int(0));
                uint columns_0 = _S213 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S218)).xy), uint(((_S218)).z))).x * 255.0f)) << 8U);
                uint _S219 = atlasWidth_0;
                uint _S220 = 8U % atlasWidth_0;
                int _S221 = int(_S220);
                uint _S222 = 8U / _S219;
                int3 _S223 = int3(_S221, int(_S222), int(0));
                uint _S224 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S223)).xy), uint(((_S223)).z))).x * 255.0f));
                uint _S225 = atlasWidth_0;
                uint _S226 = 9U % atlasWidth_0;
                int _S227 = int(_S226);
                uint _S228 = 9U / _S225;
                int3 _S229 = int3(_S227, int(_S228), int(0));
                uint rows_0 = ((_S224 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S229)).xy), uint(((_S229)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S187 * _S193 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S230 = atlasWidth_0 / columns_0;
                float _S231 = float(_S230);
                uint _S232 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S231, float(_S232));
                uint _S233 = slot_1 % columns_0;
                float _S234 = float(_S233);
                uint _S235 = slot_1 / columns_0;
                _S161 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S234, float(_S235)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(detailSource_0) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S236 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(detailSource_0)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S236);
            }
            if(hasSceneLighting_0)
            {
                int3 _S237 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S161 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S237)).xy), uint(((_S237)).z)));
                break;
            }
            uint _S238 = atlasWidth_0 / uint(_S236.x);
            float _S239 = float(_S238);
            uint _S240 = (atlasHeight_0 - 1U) / uint(_S236.y);
            float2 cellSize_1 = float2(_S239, float(_S240));
            _S161 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S161;
        float4 _S241 = float4(_S7->compositeControls_0) ;
        float _S242 = _S241.x;
        if(_S242 == 0.0f)
        {
            hasSceneLighting_0 = (_S241.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S243 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S243).get_width(0)),(*((&sourceHeight_0)) = (_S243).get_height(0));
            texture2d<float, access::sample> _S244 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S244).get_width(0)),(*((&valueHeight_0)) = (_S244).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S245 = diffuseSample_0;
                for(;;)
                {
                    float _S246 = _S245.w;
                    if(_S246 > 1.0f)
                    {
                        diffuseColor_0 = _S245.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S247 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S247).get_width(0)),(*((&height_1)) = (_S247).get_height(0));
                    uint3 _S248 = uint3(round(_S245.xyz * float3(255.0f) ));
                    uint index_1 = ((_S248.x) | ((_S248.y) << 8U)) | ((_S248.z) << 16U);
                    if(_S246 == 0.0f)
                    {
                        _S162 = _S248;
                    }
                    else
                    {
                        _S162 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S162 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S249 = address_0.x;
                    uint _S250 = _S249 % width_1;
                    int _S251 = int(_S250);
                    uint _S252 = _S249 / width_1;
                    int3 _S253 = int3(_S251, int(_S252), int(0));
                    float _S254 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S253)).xy), uint(((_S253)).z))).x;
                    uint _S255 = address_0.y;
                    uint _S256 = _S255 % width_1;
                    int _S257 = int(_S256);
                    uint _S258 = _S255 / width_1;
                    int3 _S259 = int3(_S257, int(_S258), int(0));
                    float _S260 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S259)).xy), uint(((_S259)).z))).x;
                    uint _S261 = address_0.z;
                    uint _S262 = _S261 % width_1;
                    int _S263 = int(_S262);
                    uint _S264 = _S261 / width_1;
                    int3 _S265 = int3(_S263, int(_S264), int(0));
                    diffuseColor_0 = float3(_S254, _S260, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S265)).xy), uint(((_S265)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                if(_S164)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S165)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S266 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S266 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S267 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S267).get_width(0)),(*((&height_2)) = (_S267).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S268 = float2(0.5f) ;
                    float2 remapped_1 = clamp((detailSource_0 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S268 / size_1, (size_1 - _S268) / size_1);
                    if((_S266 & 1U) != 0U)
                    {
                        roughness_0 = remapped_1.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S266 & 2U) != 0U)
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
        float4 _S269 = diffuseSample_0;
        for(;;)
        {
            if(_S242 != 2.0f)
            {
                _S161 = _S269;
                break;
            }
            bool _S270 = (_S241.w) >= 0.5f;
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
            uint _S271 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S270)
                {
                    for(;;)
                    {
                        if(_S271 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S272 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_3;
                        thread uint height_3;
                        (*((&width_3)) = (_S272).get_width(0)),(*((&height_3)) = (_S272).get_height(0));
                        float2 size_2 = float2(float(width_3), float(height_3));
                        float2 _S273 = float2(0.5f) ;
                        float2 remapped_2 = clamp((detailSource_0 * (size_2 - float2(2.0f) ) + float2(1.0f) ) / size_2, _S273 / size_2, (size_2 - _S273) / size_2);
                        if((_S271 & 1U) != 0U)
                        {
                            roughness_0 = remapped_2.x;
                        }
                        else
                        {
                            roughness_0 = detailSource_0.x;
                        }
                        if((_S271 & 2U) != 0U)
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
                    _S161 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S274 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S274).get_width(0)),(*((&atlasHeight_1)) = (_S274).get_height(0));
                int3 _S275 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S275)).xy), uint(((_S275)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S276 = atlasWidth_1;
                    uint _S277 = 2U % atlasWidth_1;
                    int _S278 = int(_S277);
                    uint _S279 = 2U / _S276;
                    int3 _S280 = int3(_S278, int(_S279), int(0));
                    int _S281 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S280)).xy), uint(((_S280)).z))).x * 255.0f)));
                    uint _S282 = atlasWidth_1;
                    uint _S283 = 3U % atlasWidth_1;
                    int _S284 = int(_S283);
                    uint _S285 = 3U / _S282;
                    int3 _S286 = int3(_S284, int(_S285), int(0));
                    int2 minimumTile_1 = int2(_S281, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S286)).xy), uint(((_S286)).z))).x * 255.0f))));
                    uint _S287 = atlasWidth_1;
                    uint _S288 = 4U % atlasWidth_1;
                    int _S289 = int(_S288);
                    uint _S290 = 4U / _S287;
                    int3 _S291 = int3(_S289, int(_S290), int(0));
                    uint _S292 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S291)).xy), uint(((_S291)).z))).x * 255.0f));
                    uint _S293 = atlasWidth_1;
                    uint _S294 = 5U % atlasWidth_1;
                    int _S295 = int(_S294);
                    uint _S296 = 5U / _S293;
                    int3 _S297 = int3(_S295, int(_S296), int(0));
                    uint _S298 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S297)).xy), uint(((_S297)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S292, _S298);
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
                        int3 _S299 = int3(int(1), int(0), int(0));
                        _S161 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S299)).xy), uint(((_S299)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S292 + uint(tile_2.x));
                    uint _S300 = atlasWidth_1;
                    uint _S301 = offset_1 % atlasWidth_1;
                    int _S302 = int(_S301);
                    uint _S303 = offset_1 / _S300;
                    int3 _S304 = int3(_S302, int(_S303), int(0));
                    uint _S305 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S304)).xy), uint(((_S304)).z))).x * 255.0f));
                    uint _S306 = atlasWidth_1;
                    uint _S307 = offset_1 + 1U;
                    uint _S308 = _S307 % atlasWidth_1;
                    int _S309 = int(_S308);
                    uint _S310 = _S307 / _S306;
                    int3 _S311 = int3(_S309, int(_S310), int(0));
                    uint slot_2 = _S305 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S311)).xy), uint(((_S311)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S312 = int3(int(1), int(0), int(0));
                        _S161 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S312)).xy), uint(((_S312)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S313 = atlasWidth_1;
                    uint _S314 = 6U % atlasWidth_1;
                    int _S315 = int(_S314);
                    uint _S316 = 6U / _S313;
                    int3 _S317 = int3(_S315, int(_S316), int(0));
                    uint _S318 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S317)).xy), uint(((_S317)).z))).x * 255.0f));
                    uint _S319 = atlasWidth_1;
                    uint _S320 = 7U % atlasWidth_1;
                    int _S321 = int(_S320);
                    uint _S322 = 7U / _S319;
                    int3 _S323 = int3(_S321, int(_S322), int(0));
                    uint columns_1 = _S318 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S323)).xy), uint(((_S323)).z))).x * 255.0f)) << 8U);
                    uint _S324 = atlasWidth_1;
                    uint _S325 = 8U % atlasWidth_1;
                    int _S326 = int(_S325);
                    uint _S327 = 8U / _S324;
                    int3 _S328 = int3(_S326, int(_S327), int(0));
                    uint _S329 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S328)).xy), uint(((_S328)).z))).x * 255.0f));
                    uint _S330 = atlasWidth_1;
                    uint _S331 = 9U % atlasWidth_1;
                    int _S332 = int(_S331);
                    uint _S333 = 9U / _S330;
                    int3 _S334 = int3(_S332, int(_S333), int(0));
                    uint rows_1 = ((_S329 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S334)).xy), uint(((_S334)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S292 * _S298 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S335 = atlasWidth_1 / columns_1;
                    float _S336 = float(_S335);
                    uint _S337 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S336, float(_S337));
                    uint _S338 = slot_3 % columns_1;
                    float _S339 = float(_S338);
                    uint _S340 = slot_3 / columns_1;
                    _S161 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S339, float(_S340)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailSource_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S341 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(detailSource_0)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S341);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S342 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S161 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S342)).xy), uint(((_S342)).z)));
                    break;
                }
                uint _S343 = atlasWidth_1 / uint(_S341.x);
                float _S344 = float(_S343);
                uint _S345 = (atlasHeight_1 - 1U) / uint(_S341.y);
                float2 cellSize_3 = float2(_S344, float(_S345));
                _S161 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S241.y));
            if(operation_0 == 1U)
            {
                _S161 = _S269 * _S161;
                break;
            }
            if(operation_0 == 2U)
            {
                _S161 = _S269 + _S161;
                break;
            }
            if(operation_0 == 3U)
            {
                _S161 = _S269 - _S161;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S241.z;
                _S161 = _S269 * float4((1.0f - factor_0))  + _S161 * float4(factor_0) ;
                break;
            }
            _S161 = _S269;
            break;
        }
        diffuseColor_0 = _S161.xyz;
    }
    thread float4 baseNormalSample_0;
    bool useSecondary_1 = (secondaryMask_0 & 4U) != 0U;
    bool _S346 = (alternateMask_0 & 4U) != 0U;
    if(_S346)
    {
        if(useSecondary_1)
        {
            detailSource_0 = _S160;
        }
        else
        {
            detailSource_0 = _S159;
        }
    }
    else
    {
        if(useSecondary_1)
        {
            detailSource_0 = _S152;
        }
        else
        {
            detailSource_0 = _S151;
        }
    }
    bool _S347 = (udimMask_0 & 4U) != 0U;
    uint maskU_1 = uint(round(_S145.y));
    uint maskV_1 = uint(round(_S145.z));
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
    uint _S348 = index_0 | lightCount_0;
    for(;;)
    {
        if(!_S347)
        {
            for(;;)
            {
                if(_S348 == 0U)
                {
                    break;
                }
                texture2d<float, access::sample> _S349 = (&kernelContext_0)->normalTexture_0;
                thread uint width_4;
                thread uint height_4;
                (*((&width_4)) = (_S349).get_width(0)),(*((&height_4)) = (_S349).get_height(0));
                float2 size_3 = float2(float(width_4), float(height_4));
                float2 _S350 = float2(0.5f) ;
                float2 remapped_3 = clamp((detailSource_0 * (size_3 - float2(2.0f) ) + float2(1.0f) ) / size_3, _S350 / size_3, (size_3 - _S350) / size_3);
                if((_S348 & 1U) != 0U)
                {
                    roughness_0 = remapped_3.x;
                }
                else
                {
                    roughness_0 = detailSource_0.x;
                }
                if((_S348 & 2U) != 0U)
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
            _S161 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (detailSource_0)));
            break;
        }
        texture2d<float, access::sample> _S351 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S351).get_width(0)),(*((&atlasHeight_2)) = (_S351).get_height(0));
        int3 _S352 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S352)).xy), uint(((_S352)).z))) * float4(255.0f) );
        if((metadata_2.x) == 255.0f)
        {
            uint _S353 = atlasWidth_2;
            uint _S354 = 2U % atlasWidth_2;
            int _S355 = int(_S354);
            uint _S356 = 2U / _S353;
            int3 _S357 = int3(_S355, int(_S356), int(0));
            int _S358 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S357)).xy), uint(((_S357)).z))).x * 255.0f)));
            uint _S359 = atlasWidth_2;
            uint _S360 = 3U % atlasWidth_2;
            int _S361 = int(_S360);
            uint _S362 = 3U / _S359;
            int3 _S363 = int3(_S361, int(_S362), int(0));
            int2 minimumTile_2 = int2(_S358, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S363)).xy), uint(((_S363)).z))).x * 255.0f))));
            uint _S364 = atlasWidth_2;
            uint _S365 = 4U % atlasWidth_2;
            int _S366 = int(_S365);
            uint _S367 = 4U / _S364;
            int3 _S368 = int3(_S366, int(_S367), int(0));
            uint _S369 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S368)).xy), uint(((_S368)).z))).x * 255.0f));
            uint _S370 = atlasWidth_2;
            uint _S371 = 5U % atlasWidth_2;
            int _S372 = int(_S371);
            uint _S373 = 5U / _S370;
            int3 _S374 = int3(_S372, int(_S373), int(0));
            uint _S375 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S374)).xy), uint(((_S374)).z))).x * 255.0f));
            uint2 grid_2 = uint2(_S369, _S375);
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
                int3 _S376 = int3(int(1), int(0), int(0));
                _S161 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S376)).xy), uint(((_S376)).z)));
                break;
            }
            uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S369 + uint(tile_4.x));
            uint _S377 = atlasWidth_2;
            uint _S378 = offset_2 % atlasWidth_2;
            int _S379 = int(_S378);
            uint _S380 = offset_2 / _S377;
            int3 _S381 = int3(_S379, int(_S380), int(0));
            uint _S382 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S381)).xy), uint(((_S381)).z))).x * 255.0f));
            uint _S383 = atlasWidth_2;
            uint _S384 = offset_2 + 1U;
            uint _S385 = _S384 % atlasWidth_2;
            int _S386 = int(_S385);
            uint _S387 = _S384 / _S383;
            int3 _S388 = int3(_S386, int(_S387), int(0));
            uint slot_4 = _S382 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S388)).xy), uint(((_S388)).z))).x * 255.0f)) << 8U);
            if(slot_4 == 0U)
            {
                int3 _S389 = int3(int(1), int(0), int(0));
                _S161 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S389)).xy), uint(((_S389)).z)));
                break;
            }
            uint slot_5 = slot_4 - 1U;
            uint _S390 = atlasWidth_2;
            uint _S391 = 6U % atlasWidth_2;
            int _S392 = int(_S391);
            uint _S393 = 6U / _S390;
            int3 _S394 = int3(_S392, int(_S393), int(0));
            uint _S395 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S394)).xy), uint(((_S394)).z))).x * 255.0f));
            uint _S396 = atlasWidth_2;
            uint _S397 = 7U % atlasWidth_2;
            int _S398 = int(_S397);
            uint _S399 = 7U / _S396;
            int3 _S400 = int3(_S398, int(_S399), int(0));
            uint columns_2 = _S395 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S400)).xy), uint(((_S400)).z))).x * 255.0f)) << 8U);
            uint _S401 = atlasWidth_2;
            uint _S402 = 8U % atlasWidth_2;
            int _S403 = int(_S402);
            uint _S404 = 8U / _S401;
            int3 _S405 = int3(_S403, int(_S404), int(0));
            uint _S406 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S405)).xy), uint(((_S405)).z))).x * 255.0f));
            uint _S407 = atlasWidth_2;
            uint _S408 = 9U % atlasWidth_2;
            int _S409 = int(_S408);
            uint _S410 = 9U / _S407;
            int3 _S411 = int3(_S409, int(_S410), int(0));
            uint rows_2 = ((_S406 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S411)).xy), uint(((_S411)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
            uint metadataRows_2 = (10U + 2U * _S369 * _S375 + atlasWidth_2 - 1U) / atlasWidth_2;
            uint _S412 = atlasWidth_2 / columns_2;
            float _S413 = float(_S412);
            uint _S414 = (atlasHeight_2 - metadataRows_2) / rows_2;
            float2 cellSize_4 = float2(_S413, float(_S414));
            uint _S415 = slot_5 % columns_2;
            float _S416 = float(_S415);
            uint _S417 = slot_5 / columns_2;
            _S161 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S416, float(_S417)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(detailSource_0) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        int2 _S418 = int2(metadata_2.zw);
        int2 tile_5 = int2(floor(detailSource_0)) - int2(metadata_2.xy);
        if(any(tile_5 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_5 >= _S418);
        }
        if(hasSceneLighting_0)
        {
            int3 _S419 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S161 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S419)).xy), uint(((_S419)).z)));
            break;
        }
        uint _S420 = atlasWidth_2 / uint(_S418.x);
        float _S421 = float(_S420);
        uint _S422 = (atlasHeight_2 - 1U) / uint(_S418.y);
        float2 cellSize_5 = float2(_S421, float(_S422));
        _S161 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    baseNormalSample_0 = _S161;
    float4 _S423 = float4(_S7->detailUvTransformRow0_0) ;
    if((_S423.w) != 0.0f)
    {
        float4 _S424 = baseNormalSample_0;
        for(;;)
        {
            float _S425 = _S424.w;
            if(_S425 > 1.0f)
            {
                unlitColor_0 = _S424.xyz;
                break;
            }
            texture2d<float, access::sample> _S426 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_5;
            thread uint height_5;
            (*((&width_5)) = (_S426).get_width(0)),(*((&height_5)) = (_S426).get_height(0));
            uint3 _S427 = uint3(round(_S424.xyz * float3(255.0f) ));
            uint index_2 = ((_S427.x) | ((_S427.y) << 8U)) | ((_S427.z) << 16U);
            if(_S425 == 0.0f)
            {
                _S162 = _S427;
            }
            else
            {
                _S162 = uint3(index_2, index_2, index_2);
            }
            uint3 address_1 = _S162 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S428 = address_1.x;
            uint _S429 = _S428 % width_5;
            int _S430 = int(_S429);
            uint _S431 = _S428 / width_5;
            int3 _S432 = int3(_S430, int(_S431), int(0));
            float _S433 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S432)).xy), uint(((_S432)).z))).x;
            uint _S434 = address_1.y;
            uint _S435 = _S434 % width_5;
            int _S436 = int(_S435);
            uint _S437 = _S434 / width_5;
            int3 _S438 = int3(_S436, int(_S437), int(0));
            float _S439 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S438)).xy), uint(((_S438)).z))).x;
            uint _S440 = address_1.z;
            uint _S441 = _S440 % width_5;
            int _S442 = int(_S441);
            uint _S443 = _S440 / width_5;
            int3 _S444 = int3(_S442, int(_S443), int(0));
            unlitColor_0 = float3(_S433, _S439, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S444)).xy), uint(((_S444)).z))).x);
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
    if(_S346)
    {
        detailSource_0 = _S155;
    }
    else
    {
        detailSource_0 = _S147;
    }
    if(_S346)
    {
        tangent_1 = _S157;
    }
    else
    {
        tangent_1 = _S149;
    }
    thread float3 _S445 = unlitColor_0;
    float2 _S446 = float2(0.0f, 0.0f);
    if(any(tangent_1 != _S446))
    {
        float _S447 = tangent_1.x;
        float _S448 = tangent_1.y;
        float2 tangent_2 = normalize(float2(_S448, - _S447) / float2(max(abs(_S447), abs(_S448))) );
        float2 bitangent_0 = float2(- tangent_2.y, tangent_2.x);
        if((dot(detailSource_0, tangent_2)) < 0.0f)
        {
            detailSource_0 = - tangent_2;
        }
        else
        {
            detailSource_0 = tangent_2;
        }
        _S445.xy = detailSource_0 * float2(_S445.x)  + bitangent_0 * float2(_S445.y) ;
    }
    float3 _S449 = _S445;
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
        float2 _S450 = _S423.xy;
        float _S451 = dot(_S450, detailSource_0) + _S423.z;
        float4 _S452 = float4(_S7->detailUvTransformRow1_0) ;
        float2 _S453 = _S452.xy;
        float _S454 = dot(_S453, detailSource_0) + _S452.z;
        float2 detailUv_0 = float2(_S451, _S454);
        thread float4 detailSample_0;
        bool _S455 = (udimMask_0 & 4096U) != 0U;
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
        uint _S456 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S455)
            {
                for(;;)
                {
                    if(_S456 == 0U)
                    {
                        detailSource_0 = detailUv_0;
                        break;
                    }
                    texture2d<float, access::sample> _S457 = (&kernelContext_0)->detailNormalTexture_0;
                    thread uint width_6;
                    thread uint height_6;
                    (*((&width_6)) = (_S457).get_width(0)),(*((&height_6)) = (_S457).get_height(0));
                    float2 size_4 = float2(float(width_6), float(height_6));
                    float2 _S458 = float2(0.5f) ;
                    float2 remapped_4 = clamp((detailUv_0 * (size_4 - float2(2.0f) ) + float2(1.0f) ) / size_4, _S458 / size_4, (size_4 - _S458) / size_4);
                    if((_S456 & 1U) != 0U)
                    {
                        roughness_0 = remapped_4.x;
                    }
                    else
                    {
                        roughness_0 = _S451;
                    }
                    if((_S456 & 2U) != 0U)
                    {
                        metallic_0 = remapped_4.y;
                    }
                    else
                    {
                        metallic_0 = _S454;
                    }
                    detailSource_0 = float2(roughness_0, metallic_0);
                    break;
                }
                _S161 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S459 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S459).get_width(0)),(*((&atlasHeight_3)) = (_S459).get_height(0));
            int3 _S460 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S460)).xy), uint(((_S460)).z))) * float4(255.0f) );
            if((metadata_3.x) == 255.0f)
            {
                uint _S461 = atlasWidth_3;
                uint _S462 = 2U % atlasWidth_3;
                int _S463 = int(_S462);
                uint _S464 = 2U / _S461;
                int3 _S465 = int3(_S463, int(_S464), int(0));
                int _S466 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S465)).xy), uint(((_S465)).z))).x * 255.0f)));
                uint _S467 = atlasWidth_3;
                uint _S468 = 3U % atlasWidth_3;
                int _S469 = int(_S468);
                uint _S470 = 3U / _S467;
                int3 _S471 = int3(_S469, int(_S470), int(0));
                int2 minimumTile_3 = int2(_S466, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S471)).xy), uint(((_S471)).z))).x * 255.0f))));
                uint _S472 = atlasWidth_3;
                uint _S473 = 4U % atlasWidth_3;
                int _S474 = int(_S473);
                uint _S475 = 4U / _S472;
                int3 _S476 = int3(_S474, int(_S475), int(0));
                uint _S477 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S476)).xy), uint(((_S476)).z))).x * 255.0f));
                uint _S478 = atlasWidth_3;
                uint _S479 = 5U % atlasWidth_3;
                int _S480 = int(_S479);
                uint _S481 = 5U / _S478;
                int3 _S482 = int3(_S480, int(_S481), int(0));
                uint _S483 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S482)).xy), uint(((_S482)).z))).x * 255.0f));
                uint2 grid_3 = uint2(_S477, _S483);
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
                    int3 _S484 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S484)).xy), uint(((_S484)).z)));
                    break;
                }
                uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S477 + uint(tile_6.x));
                uint _S485 = atlasWidth_3;
                uint _S486 = offset_3 % atlasWidth_3;
                int _S487 = int(_S486);
                uint _S488 = offset_3 / _S485;
                int3 _S489 = int3(_S487, int(_S488), int(0));
                uint _S490 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S489)).xy), uint(((_S489)).z))).x * 255.0f));
                uint _S491 = atlasWidth_3;
                uint _S492 = offset_3 + 1U;
                uint _S493 = _S492 % atlasWidth_3;
                int _S494 = int(_S493);
                uint _S495 = _S492 / _S491;
                int3 _S496 = int3(_S494, int(_S495), int(0));
                uint slot_6 = _S490 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S496)).xy), uint(((_S496)).z))).x * 255.0f)) << 8U);
                if(slot_6 == 0U)
                {
                    int3 _S497 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S497)).xy), uint(((_S497)).z)));
                    break;
                }
                uint slot_7 = slot_6 - 1U;
                uint _S498 = atlasWidth_3;
                uint _S499 = 6U % atlasWidth_3;
                int _S500 = int(_S499);
                uint _S501 = 6U / _S498;
                int3 _S502 = int3(_S500, int(_S501), int(0));
                uint _S503 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S502)).xy), uint(((_S502)).z))).x * 255.0f));
                uint _S504 = atlasWidth_3;
                uint _S505 = 7U % atlasWidth_3;
                int _S506 = int(_S505);
                uint _S507 = 7U / _S504;
                int3 _S508 = int3(_S506, int(_S507), int(0));
                uint columns_3 = _S503 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S508)).xy), uint(((_S508)).z))).x * 255.0f)) << 8U);
                uint _S509 = atlasWidth_3;
                uint _S510 = 8U % atlasWidth_3;
                int _S511 = int(_S510);
                uint _S512 = 8U / _S509;
                int3 _S513 = int3(_S511, int(_S512), int(0));
                uint _S514 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S513)).xy), uint(((_S513)).z))).x * 255.0f));
                uint _S515 = atlasWidth_3;
                uint _S516 = 9U % atlasWidth_3;
                int _S517 = int(_S516);
                uint _S518 = 9U / _S515;
                int3 _S519 = int3(_S517, int(_S518), int(0));
                uint rows_3 = ((_S514 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S519)).xy), uint(((_S519)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                uint metadataRows_3 = (10U + 2U * _S477 * _S483 + atlasWidth_3 - 1U) / atlasWidth_3;
                uint _S520 = atlasWidth_3 / columns_3;
                float _S521 = float(_S520);
                uint _S522 = (atlasHeight_3 - metadataRows_3) / rows_3;
                float2 cellSize_6 = float2(_S521, float(_S522));
                uint _S523 = slot_7 % columns_3;
                float _S524 = float(_S523);
                uint _S525 = slot_7 / columns_3;
                _S161 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S524, float(_S525)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(detailUv_0) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            int2 _S526 = int2(metadata_3.zw);
            int2 tile_7 = int2(floor(detailUv_0)) - int2(metadata_3.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S526);
            }
            if(hasSceneLighting_0)
            {
                int3 _S527 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S161 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S527)).xy), uint(((_S527)).z)));
                break;
            }
            uint _S528 = atlasWidth_3 / uint(_S526.x);
            float _S529 = float(_S528);
            uint _S530 = (atlasHeight_3 - 1U) / uint(_S526.y);
            float2 cellSize_7 = float2(_S529, float(_S530));
            _S161 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        detailSample_0 = _S161;
        if((_S452.w) != 0.0f)
        {
            float4 _S531 = detailSample_0;
            for(;;)
            {
                float _S532 = _S531.w;
                if(_S532 > 1.0f)
                {
                    unlitColor_0 = _S531.xyz;
                    break;
                }
                texture2d<float, access::sample> _S533 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_7;
                thread uint height_7;
                (*((&width_7)) = (_S533).get_width(0)),(*((&height_7)) = (_S533).get_height(0));
                uint3 _S534 = uint3(round(_S531.xyz * float3(255.0f) ));
                uint index_3 = ((_S534.x) | ((_S534.y) << 8U)) | ((_S534.z) << 16U);
                if(_S532 == 0.0f)
                {
                    _S162 = _S534;
                }
                else
                {
                    _S162 = uint3(index_3, index_3, index_3);
                }
                uint3 address_2 = _S162 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S535 = address_2.x;
                uint _S536 = _S535 % width_7;
                int _S537 = int(_S536);
                uint _S538 = _S535 / width_7;
                int3 _S539 = int3(_S537, int(_S538), int(0));
                float _S540 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S539)).xy), uint(((_S539)).z))).x;
                uint _S541 = address_2.y;
                uint _S542 = _S541 % width_7;
                int _S543 = int(_S542);
                uint _S544 = _S541 / width_7;
                int3 _S545 = int3(_S543, int(_S544), int(0));
                float _S546 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S545)).xy), uint(((_S545)).z))).x;
                uint _S547 = address_2.z;
                uint _S548 = _S547 % width_7;
                int _S549 = int(_S548);
                uint _S550 = _S547 / width_7;
                int3 _S551 = int3(_S549, int(_S550), int(0));
                unlitColor_0 = float3(_S540, _S546, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S551)).xy), uint(((_S551)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S552 = detail_0;
        if(any(_S453 != _S446))
        {
            float _S553 = _S453.x;
            float _S554 = _S453.y;
            float2 tangent_3 = normalize(float2(_S554, - _S553) / float2(max(abs(_S553), abs(_S554))) );
            float2 bitangent_1 = float2(- tangent_3.y, tangent_3.x);
            if((dot(_S450, tangent_3)) < 0.0f)
            {
                tangent_1 = - tangent_3;
            }
            else
            {
                tangent_1 = tangent_3;
            }
            _S552.xy = tangent_1 * float2(_S552.x)  + bitangent_1 * float2(_S552.y) ;
        }
        float3 _S555 = _S552;
        for(;;)
        {
            float3 t_0 = normalize(_S449) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S555) * float3(-1.0f, -1.0f, 1.0f);
            float _S556 = t_0.z;
            if(_S556 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S556))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S449;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S557 = float3(unlitColor_0.x) ;
    float3 _S558 = float3(unlitColor_0.y) ;
    float3 _S559 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S557 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S558 + _S1.normal_0 * _S559);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S557 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S558 + worldNormalBasis_0 * _S559);
    float4 _S560;
    if((textureMask_0 & 8U) != 0U)
    {
        bool useSecondary_2 = (secondaryMask_0 & 8U) != 0U;
        if((alternateMask_0 & 8U) != 0U)
        {
            if(useSecondary_2)
            {
                detailSource_0 = _S160;
            }
            else
            {
                detailSource_0 = _S159;
            }
        }
        else
        {
            if(useSecondary_2)
            {
                detailSource_0 = _S152;
            }
            else
            {
                detailSource_0 = _S151;
            }
        }
        bool _S561 = (udimMask_0 & 8U) != 0U;
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
        uint _S562 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S561)
            {
                for(;;)
                {
                    if(_S562 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S563 = (&kernelContext_0)->roughnessMetallicTexture_0;
                    thread uint width_8;
                    thread uint height_8;
                    (*((&width_8)) = (_S563).get_width(0)),(*((&height_8)) = (_S563).get_height(0));
                    float2 size_5 = float2(float(width_8), float(height_8));
                    float2 _S564 = float2(0.5f) ;
                    float2 remapped_5 = clamp((detailSource_0 * (size_5 - float2(2.0f) ) + float2(1.0f) ) / size_5, _S564 / size_5, (size_5 - _S564) / size_5);
                    if((_S562 & 1U) != 0U)
                    {
                        roughness_0 = remapped_5.x;
                    }
                    else
                    {
                        roughness_0 = detailSource_0.x;
                    }
                    if((_S562 & 2U) != 0U)
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
                _S161 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S565 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S565).get_width(0)),(*((&atlasHeight_4)) = (_S565).get_height(0));
            int3 _S566 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S566)).xy), uint(((_S566)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S567 = atlasWidth_4;
                uint _S568 = 2U % atlasWidth_4;
                int _S569 = int(_S568);
                uint _S570 = 2U / _S567;
                int3 _S571 = int3(_S569, int(_S570), int(0));
                int _S572 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S571)).xy), uint(((_S571)).z))).x * 255.0f)));
                uint _S573 = atlasWidth_4;
                uint _S574 = 3U % atlasWidth_4;
                int _S575 = int(_S574);
                uint _S576 = 3U / _S573;
                int3 _S577 = int3(_S575, int(_S576), int(0));
                int2 minimumTile_4 = int2(_S572, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S577)).xy), uint(((_S577)).z))).x * 255.0f))));
                uint _S578 = atlasWidth_4;
                uint _S579 = 4U % atlasWidth_4;
                int _S580 = int(_S579);
                uint _S581 = 4U / _S578;
                int3 _S582 = int3(_S580, int(_S581), int(0));
                uint _S583 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S582)).xy), uint(((_S582)).z))).x * 255.0f));
                uint _S584 = atlasWidth_4;
                uint _S585 = 5U % atlasWidth_4;
                int _S586 = int(_S585);
                uint _S587 = 5U / _S584;
                int3 _S588 = int3(_S586, int(_S587), int(0));
                uint _S589 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S588)).xy), uint(((_S588)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S583, _S589);
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
                    int3 _S590 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S590)).xy), uint(((_S590)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S583 + uint(tile_8.x));
                uint _S591 = atlasWidth_4;
                uint _S592 = offset_4 % atlasWidth_4;
                int _S593 = int(_S592);
                uint _S594 = offset_4 / _S591;
                int3 _S595 = int3(_S593, int(_S594), int(0));
                uint _S596 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S595)).xy), uint(((_S595)).z))).x * 255.0f));
                uint _S597 = atlasWidth_4;
                uint _S598 = offset_4 + 1U;
                uint _S599 = _S598 % atlasWidth_4;
                int _S600 = int(_S599);
                uint _S601 = _S598 / _S597;
                int3 _S602 = int3(_S600, int(_S601), int(0));
                uint slot_8 = _S596 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S602)).xy), uint(((_S602)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S603 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S603)).xy), uint(((_S603)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S604 = atlasWidth_4;
                uint _S605 = 6U % atlasWidth_4;
                int _S606 = int(_S605);
                uint _S607 = 6U / _S604;
                int3 _S608 = int3(_S606, int(_S607), int(0));
                uint _S609 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S608)).xy), uint(((_S608)).z))).x * 255.0f));
                uint _S610 = atlasWidth_4;
                uint _S611 = 7U % atlasWidth_4;
                int _S612 = int(_S611);
                uint _S613 = 7U / _S610;
                int3 _S614 = int3(_S612, int(_S613), int(0));
                uint columns_4 = _S609 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S614)).xy), uint(((_S614)).z))).x * 255.0f)) << 8U);
                uint _S615 = atlasWidth_4;
                uint _S616 = 8U % atlasWidth_4;
                int _S617 = int(_S616);
                uint _S618 = 8U / _S615;
                int3 _S619 = int3(_S617, int(_S618), int(0));
                uint _S620 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S619)).xy), uint(((_S619)).z))).x * 255.0f));
                uint _S621 = atlasWidth_4;
                uint _S622 = 9U % atlasWidth_4;
                int _S623 = int(_S622);
                uint _S624 = 9U / _S621;
                int3 _S625 = int3(_S623, int(_S624), int(0));
                uint rows_4 = ((_S620 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S625)).xy), uint(((_S625)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S583 * _S589 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S626 = atlasWidth_4 / columns_4;
                float _S627 = float(_S626);
                uint _S628 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S627, float(_S628));
                uint _S629 = slot_9 % columns_4;
                float _S630 = float(_S629);
                uint _S631 = slot_9 / columns_4;
                _S161 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S630, float(_S631)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(detailSource_0) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S632 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(detailSource_0)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S632);
            }
            if(hasSceneLighting_0)
            {
                int3 _S633 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S161 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S633)).xy), uint(((_S633)).z)));
                break;
            }
            uint _S634 = atlasWidth_4 / uint(_S632.x);
            float _S635 = float(_S634);
            uint _S636 = (atlasHeight_4 - 1U) / uint(_S632.y);
            float2 cellSize_9 = float2(_S635, float(_S636));
            _S161 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S637 = float4(_S7->compositeControls_0) ;
            if((_S637.x) != 8.0f)
            {
                break;
            }
            bool _S638 = (_S637.w) >= 0.5f;
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
            uint _S639 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S638)
                {
                    for(;;)
                    {
                        if(_S639 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S640 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_9;
                        thread uint height_9;
                        (*((&width_9)) = (_S640).get_width(0)),(*((&height_9)) = (_S640).get_height(0));
                        float2 size_6 = float2(float(width_9), float(height_9));
                        float2 _S641 = float2(0.5f) ;
                        float2 remapped_6 = clamp((detailSource_0 * (size_6 - float2(2.0f) ) + float2(1.0f) ) / size_6, _S641 / size_6, (size_6 - _S641) / size_6);
                        if((_S639 & 1U) != 0U)
                        {
                            roughness_0 = remapped_6.x;
                        }
                        else
                        {
                            roughness_0 = detailSource_0.x;
                        }
                        if((_S639 & 2U) != 0U)
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
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S642 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S642).get_width(0)),(*((&atlasHeight_5)) = (_S642).get_height(0));
                int3 _S643 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S643)).xy), uint(((_S643)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S644 = atlasWidth_5;
                    uint _S645 = 2U % atlasWidth_5;
                    int _S646 = int(_S645);
                    uint _S647 = 2U / _S644;
                    int3 _S648 = int3(_S646, int(_S647), int(0));
                    int _S649 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S648)).xy), uint(((_S648)).z))).x * 255.0f)));
                    uint _S650 = atlasWidth_5;
                    uint _S651 = 3U % atlasWidth_5;
                    int _S652 = int(_S651);
                    uint _S653 = 3U / _S650;
                    int3 _S654 = int3(_S652, int(_S653), int(0));
                    int2 minimumTile_5 = int2(_S649, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S654)).xy), uint(((_S654)).z))).x * 255.0f))));
                    uint _S655 = atlasWidth_5;
                    uint _S656 = 4U % atlasWidth_5;
                    int _S657 = int(_S656);
                    uint _S658 = 4U / _S655;
                    int3 _S659 = int3(_S657, int(_S658), int(0));
                    uint _S660 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S659)).xy), uint(((_S659)).z))).x * 255.0f));
                    uint _S661 = atlasWidth_5;
                    uint _S662 = 5U % atlasWidth_5;
                    int _S663 = int(_S662);
                    uint _S664 = 5U / _S661;
                    int3 _S665 = int3(_S663, int(_S664), int(0));
                    uint _S666 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S665)).xy), uint(((_S665)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S660, _S666);
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
                        int3 _S667 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S667)).xy), uint(((_S667)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S660 + uint(tile_10.x));
                    uint _S668 = atlasWidth_5;
                    uint _S669 = offset_5 % atlasWidth_5;
                    int _S670 = int(_S669);
                    uint _S671 = offset_5 / _S668;
                    int3 _S672 = int3(_S670, int(_S671), int(0));
                    uint _S673 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S672)).xy), uint(((_S672)).z))).x * 255.0f));
                    uint _S674 = atlasWidth_5;
                    uint _S675 = offset_5 + 1U;
                    uint _S676 = _S675 % atlasWidth_5;
                    int _S677 = int(_S676);
                    uint _S678 = _S675 / _S674;
                    int3 _S679 = int3(_S677, int(_S678), int(0));
                    uint slot_10 = _S673 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S679)).xy), uint(((_S679)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S680 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S680)).xy), uint(((_S680)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S681 = atlasWidth_5;
                    uint _S682 = 6U % atlasWidth_5;
                    int _S683 = int(_S682);
                    uint _S684 = 6U / _S681;
                    int3 _S685 = int3(_S683, int(_S684), int(0));
                    uint _S686 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S685)).xy), uint(((_S685)).z))).x * 255.0f));
                    uint _S687 = atlasWidth_5;
                    uint _S688 = 7U % atlasWidth_5;
                    int _S689 = int(_S688);
                    uint _S690 = 7U / _S687;
                    int3 _S691 = int3(_S689, int(_S690), int(0));
                    uint columns_5 = _S686 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S691)).xy), uint(((_S691)).z))).x * 255.0f)) << 8U);
                    uint _S692 = atlasWidth_5;
                    uint _S693 = 8U % atlasWidth_5;
                    int _S694 = int(_S693);
                    uint _S695 = 8U / _S692;
                    int3 _S696 = int3(_S694, int(_S695), int(0));
                    uint _S697 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S696)).xy), uint(((_S696)).z))).x * 255.0f));
                    uint _S698 = atlasWidth_5;
                    uint _S699 = 9U % atlasWidth_5;
                    int _S700 = int(_S699);
                    uint _S701 = 9U / _S698;
                    int3 _S702 = int3(_S700, int(_S701), int(0));
                    uint rows_5 = ((_S697 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S702)).xy), uint(((_S702)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S660 * _S666 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S703 = atlasWidth_5 / columns_5;
                    float _S704 = float(_S703);
                    uint _S705 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S704, float(_S705));
                    uint _S706 = slot_11 % columns_5;
                    float _S707 = float(_S706);
                    uint _S708 = slot_11 / columns_5;
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S707, float(_S708)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(detailSource_0) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S709 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(detailSource_0)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S709);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S710 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S710)).xy), uint(((_S710)).z)));
                    break;
                }
                uint _S711 = atlasWidth_5 / uint(_S709.x);
                float _S712 = float(_S711);
                uint _S713 = (atlasHeight_5 - 1U) / uint(_S709.y);
                float2 cellSize_11 = float2(_S712, float(_S713));
                _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_1 = uint(round(_S637.y));
            if(operation_1 == 1U)
            {
                _S161 = _S161 * _S560;
                break;
            }
            if(operation_1 == 2U)
            {
                _S161 = _S161 + _S560;
                break;
            }
            if(operation_1 == 3U)
            {
                _S161 = _S161 - _S560;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S637.z;
                _S161 = _S161 * float4((1.0f - factor_1))  + _S560 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S161.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S137;
    }
    float occlusion_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool useSecondary_3 = (secondaryMask_0 & 32U) != 0U;
        if((alternateMask_0 & 32U) != 0U)
        {
            if(useSecondary_3)
            {
                detailSource_0 = _S160;
            }
            else
            {
                detailSource_0 = _S159;
            }
        }
        else
        {
            if(useSecondary_3)
            {
                detailSource_0 = _S152;
            }
            else
            {
                detailSource_0 = _S151;
            }
        }
        bool _S714 = (udimMask_0 & 32U) != 0U;
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
        uint _S715 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S714)
            {
                for(;;)
                {
                    if(_S715 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S716 = (&kernelContext_0)->metallicTexture_0;
                    thread uint width_10;
                    thread uint height_10;
                    (*((&width_10)) = (_S716).get_width(0)),(*((&height_10)) = (_S716).get_height(0));
                    float2 size_7 = float2(float(width_10), float(height_10));
                    float2 _S717 = float2(0.5f) ;
                    float2 remapped_7 = clamp((detailSource_0 * (size_7 - float2(2.0f) ) + float2(1.0f) ) / size_7, _S717 / size_7, (size_7 - _S717) / size_7);
                    if((_S715 & 1U) != 0U)
                    {
                        metallic_0 = remapped_7.x;
                    }
                    else
                    {
                        metallic_0 = detailSource_0.x;
                    }
                    if((_S715 & 2U) != 0U)
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
                _S161 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S718 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S718).get_width(0)),(*((&atlasHeight_6)) = (_S718).get_height(0));
            int3 _S719 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S719)).xy), uint(((_S719)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S720 = atlasWidth_6;
                uint _S721 = 2U % atlasWidth_6;
                int _S722 = int(_S721);
                uint _S723 = 2U / _S720;
                int3 _S724 = int3(_S722, int(_S723), int(0));
                int _S725 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S724)).xy), uint(((_S724)).z))).x * 255.0f)));
                uint _S726 = atlasWidth_6;
                uint _S727 = 3U % atlasWidth_6;
                int _S728 = int(_S727);
                uint _S729 = 3U / _S726;
                int3 _S730 = int3(_S728, int(_S729), int(0));
                int2 minimumTile_6 = int2(_S725, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S730)).xy), uint(((_S730)).z))).x * 255.0f))));
                uint _S731 = atlasWidth_6;
                uint _S732 = 4U % atlasWidth_6;
                int _S733 = int(_S732);
                uint _S734 = 4U / _S731;
                int3 _S735 = int3(_S733, int(_S734), int(0));
                uint _S736 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S735)).xy), uint(((_S735)).z))).x * 255.0f));
                uint _S737 = atlasWidth_6;
                uint _S738 = 5U % atlasWidth_6;
                int _S739 = int(_S738);
                uint _S740 = 5U / _S737;
                int3 _S741 = int3(_S739, int(_S740), int(0));
                uint _S742 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S741)).xy), uint(((_S741)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S736, _S742);
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
                    int3 _S743 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S743)).xy), uint(((_S743)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S736 + uint(tile_12.x));
                uint _S744 = atlasWidth_6;
                uint _S745 = offset_6 % atlasWidth_6;
                int _S746 = int(_S745);
                uint _S747 = offset_6 / _S744;
                int3 _S748 = int3(_S746, int(_S747), int(0));
                uint _S749 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S748)).xy), uint(((_S748)).z))).x * 255.0f));
                uint _S750 = atlasWidth_6;
                uint _S751 = offset_6 + 1U;
                uint _S752 = _S751 % atlasWidth_6;
                int _S753 = int(_S752);
                uint _S754 = _S751 / _S750;
                int3 _S755 = int3(_S753, int(_S754), int(0));
                uint slot_12 = _S749 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S755)).xy), uint(((_S755)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S756 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S756)).xy), uint(((_S756)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S757 = atlasWidth_6;
                uint _S758 = 6U % atlasWidth_6;
                int _S759 = int(_S758);
                uint _S760 = 6U / _S757;
                int3 _S761 = int3(_S759, int(_S760), int(0));
                uint _S762 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S761)).xy), uint(((_S761)).z))).x * 255.0f));
                uint _S763 = atlasWidth_6;
                uint _S764 = 7U % atlasWidth_6;
                int _S765 = int(_S764);
                uint _S766 = 7U / _S763;
                int3 _S767 = int3(_S765, int(_S766), int(0));
                uint columns_6 = _S762 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S767)).xy), uint(((_S767)).z))).x * 255.0f)) << 8U);
                uint _S768 = atlasWidth_6;
                uint _S769 = 8U % atlasWidth_6;
                int _S770 = int(_S769);
                uint _S771 = 8U / _S768;
                int3 _S772 = int3(_S770, int(_S771), int(0));
                uint _S773 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S772)).xy), uint(((_S772)).z))).x * 255.0f));
                uint _S774 = atlasWidth_6;
                uint _S775 = 9U % atlasWidth_6;
                int _S776 = int(_S775);
                uint _S777 = 9U / _S774;
                int3 _S778 = int3(_S776, int(_S777), int(0));
                uint rows_6 = ((_S773 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S778)).xy), uint(((_S778)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S736 * _S742 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S779 = atlasWidth_6 / columns_6;
                float _S780 = float(_S779);
                uint _S781 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S780, float(_S781));
                uint _S782 = slot_13 % columns_6;
                float _S783 = float(_S782);
                uint _S784 = slot_13 / columns_6;
                _S161 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S783, float(_S784)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(detailSource_0) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S785 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(detailSource_0)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S785);
            }
            if(hasSceneLighting_0)
            {
                int3 _S786 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S161 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S786)).xy), uint(((_S786)).z)));
                break;
            }
            uint _S787 = atlasWidth_6 / uint(_S785.x);
            float _S788 = float(_S787);
            uint _S789 = (atlasHeight_6 - 1U) / uint(_S785.y);
            float2 cellSize_13 = float2(_S788, float(_S789));
            _S161 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        for(;;)
        {
            float4 _S790 = float4(_S7->compositeControls_0) ;
            if((_S790.x) != 32.0f)
            {
                break;
            }
            bool _S791 = (_S790.w) >= 0.5f;
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
                        float2 size_8 = float2(float(width_11), float(height_11));
                        float2 _S794 = float2(0.5f) ;
                        float2 remapped_8 = clamp((detailSource_0 * (size_8 - float2(2.0f) ) + float2(1.0f) ) / size_8, _S794 / size_8, (size_8 - _S794) / size_8);
                        if((_S792 & 1U) != 0U)
                        {
                            metallic_0 = remapped_8.x;
                        }
                        else
                        {
                            metallic_0 = detailSource_0.x;
                        }
                        if((_S792 & 2U) != 0U)
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
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
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
                        int3 _S820 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S820)).xy), uint(((_S820)).z)));
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
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S833)).xy), uint(((_S833)).z)));
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
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S860, float(_S861)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(detailSource_0) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S862 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(detailSource_0)) - int2(metadata_7.xy);
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
                    _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S863)).xy), uint(((_S863)).z)));
                    break;
                }
                uint _S864 = atlasWidth_7 / uint(_S862.x);
                float _S865 = float(_S864);
                uint _S866 = (atlasHeight_7 - 1U) / uint(_S862.y);
                float2 cellSize_15 = float2(_S865, float(_S866));
                _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_2 = uint(round(_S790.y));
            if(operation_2 == 1U)
            {
                _S161 = _S161 * _S560;
                break;
            }
            if(operation_2 == 2U)
            {
                _S161 = _S161 + _S560;
                break;
            }
            if(operation_2 == 3U)
            {
                _S161 = _S161 - _S560;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S790.z;
                _S161 = _S161 * float4((1.0f - factor_2))  + _S560 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S161.x);
    }
    else
    {
        metallic_0 = _S136;
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
                detailSource_0 = _S160;
            }
            else
            {
                detailSource_0 = _S159;
            }
        }
        else
        {
            if(useSecondary_4)
            {
                detailSource_0 = _S152;
            }
            else
            {
                detailSource_0 = _S151;
            }
        }
        thread float4 emissionSample_0;
        bool _S867 = (udimMask_0 & 16U) != 0U;
        bool _S868 = (maskU_1 & 16U) != 0U;
        if(_S868)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S869 = (maskV_1 & 16U) != 0U;
        if(_S869)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S870 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S867)
            {
                for(;;)
                {
                    if(_S870 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S871 = (&kernelContext_0)->emissiveTexture_0;
                    thread uint width_12;
                    thread uint height_12;
                    (*((&width_12)) = (_S871).get_width(0)),(*((&height_12)) = (_S871).get_height(0));
                    float2 size_9 = float2(float(width_12), float(height_12));
                    float2 _S872 = float2(0.5f) ;
                    float2 remapped_9 = clamp((detailSource_0 * (size_9 - float2(2.0f) ) + float2(1.0f) ) / size_9, _S872 / size_9, (size_9 - _S872) / size_9);
                    if((_S870 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_9.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S870 & 2U) != 0U)
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
                _S161 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S873 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S873).get_width(0)),(*((&atlasHeight_8)) = (_S873).get_height(0));
            int3 _S874 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S874)).xy), uint(((_S874)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S875 = atlasWidth_8;
                uint _S876 = 2U % atlasWidth_8;
                int _S877 = int(_S876);
                uint _S878 = 2U / _S875;
                int3 _S879 = int3(_S877, int(_S878), int(0));
                int _S880 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S879)).xy), uint(((_S879)).z))).x * 255.0f)));
                uint _S881 = atlasWidth_8;
                uint _S882 = 3U % atlasWidth_8;
                int _S883 = int(_S882);
                uint _S884 = 3U / _S881;
                int3 _S885 = int3(_S883, int(_S884), int(0));
                int2 minimumTile_8 = int2(_S880, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S885)).xy), uint(((_S885)).z))).x * 255.0f))));
                uint _S886 = atlasWidth_8;
                uint _S887 = 4U % atlasWidth_8;
                int _S888 = int(_S887);
                uint _S889 = 4U / _S886;
                int3 _S890 = int3(_S888, int(_S889), int(0));
                uint _S891 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S890)).xy), uint(((_S890)).z))).x * 255.0f));
                uint _S892 = atlasWidth_8;
                uint _S893 = 5U % atlasWidth_8;
                int _S894 = int(_S893);
                uint _S895 = 5U / _S892;
                int3 _S896 = int3(_S894, int(_S895), int(0));
                uint _S897 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S896)).xy), uint(((_S896)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S891, _S897);
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
                    int3 _S898 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S898)).xy), uint(((_S898)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S891 + uint(tile_16.x));
                uint _S899 = atlasWidth_8;
                uint _S900 = offset_8 % atlasWidth_8;
                int _S901 = int(_S900);
                uint _S902 = offset_8 / _S899;
                int3 _S903 = int3(_S901, int(_S902), int(0));
                uint _S904 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S903)).xy), uint(((_S903)).z))).x * 255.0f));
                uint _S905 = atlasWidth_8;
                uint _S906 = offset_8 + 1U;
                uint _S907 = _S906 % atlasWidth_8;
                int _S908 = int(_S907);
                uint _S909 = _S906 / _S905;
                int3 _S910 = int3(_S908, int(_S909), int(0));
                uint slot_16 = _S904 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S910)).xy), uint(((_S910)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S911 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S911)).xy), uint(((_S911)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S912 = atlasWidth_8;
                uint _S913 = 6U % atlasWidth_8;
                int _S914 = int(_S913);
                uint _S915 = 6U / _S912;
                int3 _S916 = int3(_S914, int(_S915), int(0));
                uint _S917 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S916)).xy), uint(((_S916)).z))).x * 255.0f));
                uint _S918 = atlasWidth_8;
                uint _S919 = 7U % atlasWidth_8;
                int _S920 = int(_S919);
                uint _S921 = 7U / _S918;
                int3 _S922 = int3(_S920, int(_S921), int(0));
                uint columns_8 = _S917 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S922)).xy), uint(((_S922)).z))).x * 255.0f)) << 8U);
                uint _S923 = atlasWidth_8;
                uint _S924 = 8U % atlasWidth_8;
                int _S925 = int(_S924);
                uint _S926 = 8U / _S923;
                int3 _S927 = int3(_S925, int(_S926), int(0));
                uint _S928 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S927)).xy), uint(((_S927)).z))).x * 255.0f));
                uint _S929 = atlasWidth_8;
                uint _S930 = 9U % atlasWidth_8;
                int _S931 = int(_S930);
                uint _S932 = 9U / _S929;
                int3 _S933 = int3(_S931, int(_S932), int(0));
                uint rows_8 = ((_S928 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S933)).xy), uint(((_S933)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S891 * _S897 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S934 = atlasWidth_8 / columns_8;
                float _S935 = float(_S934);
                uint _S936 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S935, float(_S936));
                uint _S937 = slot_17 % columns_8;
                float _S938 = float(_S937);
                uint _S939 = slot_17 / columns_8;
                _S161 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S938, float(_S939)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(detailSource_0) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S940 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(detailSource_0)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S940);
            }
            if(hasSceneLighting_0)
            {
                int3 _S941 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S161 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S941)).xy), uint(((_S941)).z)));
                break;
            }
            uint _S942 = atlasWidth_8 / uint(_S940.x);
            float _S943 = float(_S942);
            uint _S944 = (atlasHeight_8 - 1U) / uint(_S940.y);
            float2 cellSize_17 = float2(_S943, float(_S944));
            _S161 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        emissionSample_0 = _S161;
        if((_S146.w) != 0.0f)
        {
            texture2d<float, access::sample> _S945 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S945).get_width(0)),(*((&sourceHeight_1)) = (_S945).get_height(0));
            texture2d<float, access::sample> _S946 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S946).get_width(0)),(*((&valueHeight_1)) = (_S946).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S947 = emissionSample_0;
                for(;;)
                {
                    float _S948 = _S947.w;
                    if(_S948 > 1.0f)
                    {
                        unlitColor_0 = _S947.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S949 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_13;
                    thread uint height_13;
                    (*((&width_13)) = (_S949).get_width(0)),(*((&height_13)) = (_S949).get_height(0));
                    uint3 _S950 = uint3(round(_S947.xyz * float3(255.0f) ));
                    uint index_4 = ((_S950.x) | ((_S950.y) << 8U)) | ((_S950.z) << 16U);
                    if(_S948 == 0.0f)
                    {
                        _S162 = _S950;
                    }
                    else
                    {
                        _S162 = uint3(index_4, index_4, index_4);
                    }
                    uint3 address_3 = _S162 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S951 = address_3.x;
                    uint _S952 = _S951 % width_13;
                    int _S953 = int(_S952);
                    uint _S954 = _S951 / width_13;
                    int3 _S955 = int3(_S953, int(_S954), int(0));
                    float _S956 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S955)).xy), uint(((_S955)).z))).x;
                    uint _S957 = address_3.y;
                    uint _S958 = _S957 % width_13;
                    int _S959 = int(_S958);
                    uint _S960 = _S957 / width_13;
                    int3 _S961 = int3(_S959, int(_S960), int(0));
                    float _S962 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S961)).xy), uint(((_S961)).z))).x;
                    uint _S963 = address_3.z;
                    uint _S964 = _S963 % width_13;
                    int _S965 = int(_S964);
                    uint _S966 = _S963 / width_13;
                    int3 _S967 = int3(_S965, int(_S966), int(0));
                    unlitColor_0 = float3(_S956, _S962, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S967)).xy), uint(((_S967)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                if(_S868)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S869)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S968 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S968 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S969 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_14;
                    thread uint height_14;
                    (*((&width_14)) = (_S969).get_width(0)),(*((&height_14)) = (_S969).get_height(0));
                    float2 size_10 = float2(float(width_14), float(height_14));
                    float2 _S970 = float2(0.5f) ;
                    float2 remapped_10 = clamp((detailSource_0 * (size_10 - float2(2.0f) ) + float2(1.0f) ) / size_10, _S970 / size_10, (size_10 - _S970) / size_10);
                    if((_S968 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_10.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S968 & 2U) != 0U)
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
        float4 _S971 = emissionSample_0;
        for(;;)
        {
            float4 _S972 = float4(_S7->compositeControls_0) ;
            if((_S972.x) != 16.0f)
            {
                _S161 = _S971;
                break;
            }
            bool _S973 = (_S972.w) >= 0.5f;
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
            uint _S974 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S973)
                {
                    for(;;)
                    {
                        if(_S974 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S975 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_15;
                        thread uint height_15;
                        (*((&width_15)) = (_S975).get_width(0)),(*((&height_15)) = (_S975).get_height(0));
                        float2 size_11 = float2(float(width_15), float(height_15));
                        float2 _S976 = float2(0.5f) ;
                        float2 remapped_11 = clamp((detailSource_0 * (size_11 - float2(2.0f) ) + float2(1.0f) ) / size_11, _S976 / size_11, (size_11 - _S976) / size_11);
                        if((_S974 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_11.x;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.x;
                        }
                        if((_S974 & 2U) != 0U)
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
                    _S161 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S977 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S977).get_width(0)),(*((&atlasHeight_9)) = (_S977).get_height(0));
                int3 _S978 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S978)).xy), uint(((_S978)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S979 = atlasWidth_9;
                    uint _S980 = 2U % atlasWidth_9;
                    int _S981 = int(_S980);
                    uint _S982 = 2U / _S979;
                    int3 _S983 = int3(_S981, int(_S982), int(0));
                    int _S984 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S983)).xy), uint(((_S983)).z))).x * 255.0f)));
                    uint _S985 = atlasWidth_9;
                    uint _S986 = 3U % atlasWidth_9;
                    int _S987 = int(_S986);
                    uint _S988 = 3U / _S985;
                    int3 _S989 = int3(_S987, int(_S988), int(0));
                    int2 minimumTile_9 = int2(_S984, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S989)).xy), uint(((_S989)).z))).x * 255.0f))));
                    uint _S990 = atlasWidth_9;
                    uint _S991 = 4U % atlasWidth_9;
                    int _S992 = int(_S991);
                    uint _S993 = 4U / _S990;
                    int3 _S994 = int3(_S992, int(_S993), int(0));
                    uint _S995 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S994)).xy), uint(((_S994)).z))).x * 255.0f));
                    uint _S996 = atlasWidth_9;
                    uint _S997 = 5U % atlasWidth_9;
                    int _S998 = int(_S997);
                    uint _S999 = 5U / _S996;
                    int3 _S1000 = int3(_S998, int(_S999), int(0));
                    uint _S1001 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1000)).xy), uint(((_S1000)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S995, _S1001);
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
                        int3 _S1002 = int3(int(1), int(0), int(0));
                        _S161 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1002)).xy), uint(((_S1002)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S995 + uint(tile_18.x));
                    uint _S1003 = atlasWidth_9;
                    uint _S1004 = offset_9 % atlasWidth_9;
                    int _S1005 = int(_S1004);
                    uint _S1006 = offset_9 / _S1003;
                    int3 _S1007 = int3(_S1005, int(_S1006), int(0));
                    uint _S1008 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1007)).xy), uint(((_S1007)).z))).x * 255.0f));
                    uint _S1009 = atlasWidth_9;
                    uint _S1010 = offset_9 + 1U;
                    uint _S1011 = _S1010 % atlasWidth_9;
                    int _S1012 = int(_S1011);
                    uint _S1013 = _S1010 / _S1009;
                    int3 _S1014 = int3(_S1012, int(_S1013), int(0));
                    uint slot_18 = _S1008 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1014)).xy), uint(((_S1014)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S1015 = int3(int(1), int(0), int(0));
                        _S161 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1015)).xy), uint(((_S1015)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S1016 = atlasWidth_9;
                    uint _S1017 = 6U % atlasWidth_9;
                    int _S1018 = int(_S1017);
                    uint _S1019 = 6U / _S1016;
                    int3 _S1020 = int3(_S1018, int(_S1019), int(0));
                    uint _S1021 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1020)).xy), uint(((_S1020)).z))).x * 255.0f));
                    uint _S1022 = atlasWidth_9;
                    uint _S1023 = 7U % atlasWidth_9;
                    int _S1024 = int(_S1023);
                    uint _S1025 = 7U / _S1022;
                    int3 _S1026 = int3(_S1024, int(_S1025), int(0));
                    uint columns_9 = _S1021 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1026)).xy), uint(((_S1026)).z))).x * 255.0f)) << 8U);
                    uint _S1027 = atlasWidth_9;
                    uint _S1028 = 8U % atlasWidth_9;
                    int _S1029 = int(_S1028);
                    uint _S1030 = 8U / _S1027;
                    int3 _S1031 = int3(_S1029, int(_S1030), int(0));
                    uint _S1032 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1031)).xy), uint(((_S1031)).z))).x * 255.0f));
                    uint _S1033 = atlasWidth_9;
                    uint _S1034 = 9U % atlasWidth_9;
                    int _S1035 = int(_S1034);
                    uint _S1036 = 9U / _S1033;
                    int3 _S1037 = int3(_S1035, int(_S1036), int(0));
                    uint rows_9 = ((_S1032 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1037)).xy), uint(((_S1037)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S995 * _S1001 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S1038 = atlasWidth_9 / columns_9;
                    float _S1039 = float(_S1038);
                    uint _S1040 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S1039, float(_S1040));
                    uint _S1041 = slot_19 % columns_9;
                    float _S1042 = float(_S1041);
                    uint _S1043 = slot_19 / columns_9;
                    _S161 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1042, float(_S1043)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(detailSource_0) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S1044 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(detailSource_0)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S1044);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1045 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S161 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1045)).xy), uint(((_S1045)).z)));
                    break;
                }
                uint _S1046 = atlasWidth_9 / uint(_S1044.x);
                float _S1047 = float(_S1046);
                uint _S1048 = (atlasHeight_9 - 1U) / uint(_S1044.y);
                float2 cellSize_19 = float2(_S1047, float(_S1048));
                _S161 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_3 = uint(round(_S972.y));
            if(operation_3 == 1U)
            {
                _S161 = _S971 * _S161;
                break;
            }
            if(operation_3 == 2U)
            {
                _S161 = _S971 + _S161;
                break;
            }
            if(operation_3 == 3U)
            {
                _S161 = _S971 - _S161;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S972.z;
                _S161 = _S971 * float4((1.0f - factor_3))  + _S161 * float4(factor_3) ;
                break;
            }
            _S161 = _S971;
            break;
        }
        emissiveColor_1 = _S161.xyz;
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
                detailSource_0 = _S160;
            }
            else
            {
                detailSource_0 = _S159;
            }
        }
        else
        {
            if(useSecondary_5)
            {
                detailSource_0 = _S152;
            }
            else
            {
                detailSource_0 = _S151;
            }
        }
        for(;;)
        {
            bool _S1049 = (udimMask_0 & 64U) != 0U;
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
            uint _S1050 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1049)
                {
                    for(;;)
                    {
                        if(_S1050 == 0U)
                        {
                            tangent_1 = detailSource_0;
                            break;
                        }
                        texture2d<float, access::sample> _S1051 = (&kernelContext_0)->opacityTexture_0;
                        thread uint width_16;
                        thread uint height_16;
                        (*((&width_16)) = (_S1051).get_width(0)),(*((&height_16)) = (_S1051).get_height(0));
                        float2 size_12 = float2(float(width_16), float(height_16));
                        float2 _S1052 = float2(0.5f) ;
                        float2 remapped_12 = clamp((detailSource_0 * (size_12 - float2(2.0f) ) + float2(1.0f) ) / size_12, _S1052 / size_12, (size_12 - _S1052) / size_12);
                        if((_S1050 & 1U) != 0U)
                        {
                            opacity_0 = remapped_12.x;
                        }
                        else
                        {
                            opacity_0 = detailSource_0.x;
                        }
                        if((_S1050 & 2U) != 0U)
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
                    _S161 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (tangent_1)));
                    break;
                }
                texture2d<float, access::sample> _S1053 = (&kernelContext_0)->opacityTexture_0;
                thread uint atlasWidth_10;
                thread uint atlasHeight_10;
                (*((&atlasWidth_10)) = (_S1053).get_width(0)),(*((&atlasHeight_10)) = (_S1053).get_height(0));
                int3 _S1054 = int3(int(0), int(0), int(0));
                float4 metadata_10 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1054)).xy), uint(((_S1054)).z))) * float4(255.0f) );
                if((metadata_10.x) == 255.0f)
                {
                    uint _S1055 = atlasWidth_10;
                    uint _S1056 = 2U % atlasWidth_10;
                    int _S1057 = int(_S1056);
                    uint _S1058 = 2U / _S1055;
                    int3 _S1059 = int3(_S1057, int(_S1058), int(0));
                    int _S1060 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1059)).xy), uint(((_S1059)).z))).x * 255.0f)));
                    uint _S1061 = atlasWidth_10;
                    uint _S1062 = 3U % atlasWidth_10;
                    int _S1063 = int(_S1062);
                    uint _S1064 = 3U / _S1061;
                    int3 _S1065 = int3(_S1063, int(_S1064), int(0));
                    int2 minimumTile_10 = int2(_S1060, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1065)).xy), uint(((_S1065)).z))).x * 255.0f))));
                    uint _S1066 = atlasWidth_10;
                    uint _S1067 = 4U % atlasWidth_10;
                    int _S1068 = int(_S1067);
                    uint _S1069 = 4U / _S1066;
                    int3 _S1070 = int3(_S1068, int(_S1069), int(0));
                    uint _S1071 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1070)).xy), uint(((_S1070)).z))).x * 255.0f));
                    uint _S1072 = atlasWidth_10;
                    uint _S1073 = 5U % atlasWidth_10;
                    int _S1074 = int(_S1073);
                    uint _S1075 = 5U / _S1072;
                    int3 _S1076 = int3(_S1074, int(_S1075), int(0));
                    uint _S1077 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1076)).xy), uint(((_S1076)).z))).x * 255.0f));
                    uint2 grid_10 = uint2(_S1071, _S1077);
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
                        int3 _S1078 = int3(int(1), int(0), int(0));
                        _S161 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1078)).xy), uint(((_S1078)).z)));
                        break;
                    }
                    uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1071 + uint(tile_20.x));
                    uint _S1079 = atlasWidth_10;
                    uint _S1080 = offset_10 % atlasWidth_10;
                    int _S1081 = int(_S1080);
                    uint _S1082 = offset_10 / _S1079;
                    int3 _S1083 = int3(_S1081, int(_S1082), int(0));
                    uint _S1084 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1083)).xy), uint(((_S1083)).z))).x * 255.0f));
                    uint _S1085 = atlasWidth_10;
                    uint _S1086 = offset_10 + 1U;
                    uint _S1087 = _S1086 % atlasWidth_10;
                    int _S1088 = int(_S1087);
                    uint _S1089 = _S1086 / _S1085;
                    int3 _S1090 = int3(_S1088, int(_S1089), int(0));
                    uint slot_20 = _S1084 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1090)).xy), uint(((_S1090)).z))).x * 255.0f)) << 8U);
                    if(slot_20 == 0U)
                    {
                        int3 _S1091 = int3(int(1), int(0), int(0));
                        _S161 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1091)).xy), uint(((_S1091)).z)));
                        break;
                    }
                    uint slot_21 = slot_20 - 1U;
                    uint _S1092 = atlasWidth_10;
                    uint _S1093 = 6U % atlasWidth_10;
                    int _S1094 = int(_S1093);
                    uint _S1095 = 6U / _S1092;
                    int3 _S1096 = int3(_S1094, int(_S1095), int(0));
                    uint _S1097 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1096)).xy), uint(((_S1096)).z))).x * 255.0f));
                    uint _S1098 = atlasWidth_10;
                    uint _S1099 = 7U % atlasWidth_10;
                    int _S1100 = int(_S1099);
                    uint _S1101 = 7U / _S1098;
                    int3 _S1102 = int3(_S1100, int(_S1101), int(0));
                    uint columns_10 = _S1097 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1102)).xy), uint(((_S1102)).z))).x * 255.0f)) << 8U);
                    uint _S1103 = atlasWidth_10;
                    uint _S1104 = 8U % atlasWidth_10;
                    int _S1105 = int(_S1104);
                    uint _S1106 = 8U / _S1103;
                    int3 _S1107 = int3(_S1105, int(_S1106), int(0));
                    uint _S1108 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1107)).xy), uint(((_S1107)).z))).x * 255.0f));
                    uint _S1109 = atlasWidth_10;
                    uint _S1110 = 9U % atlasWidth_10;
                    int _S1111 = int(_S1110);
                    uint _S1112 = 9U / _S1109;
                    int3 _S1113 = int3(_S1111, int(_S1112), int(0));
                    uint rows_10 = ((_S1108 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1113)).xy), uint(((_S1113)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                    uint metadataRows_10 = (10U + 2U * _S1071 * _S1077 + atlasWidth_10 - 1U) / atlasWidth_10;
                    uint _S1114 = atlasWidth_10 / columns_10;
                    float _S1115 = float(_S1114);
                    uint _S1116 = (atlasHeight_10 - metadataRows_10) / rows_10;
                    float2 cellSize_20 = float2(_S1115, float(_S1116));
                    uint _S1117 = slot_21 % columns_10;
                    float _S1118 = float(_S1117);
                    uint _S1119 = slot_21 / columns_10;
                    _S161 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S1118, float(_S1119)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(detailSource_0) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                    break;
                }
                int2 _S1120 = int2(metadata_10.zw);
                int2 tile_21 = int2(floor(detailSource_0)) - int2(metadata_10.xy);
                if(any(tile_21 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_21 >= _S1120);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1121 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                    _S161 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S1121)).xy), uint(((_S1121)).z)));
                    break;
                }
                uint _S1122 = atlasWidth_10 / uint(_S1120.x);
                float _S1123 = float(_S1122);
                uint _S1124 = (atlasHeight_10 - 1U) / uint(_S1120.y);
                float2 cellSize_21 = float2(_S1123, float(_S1124));
                _S161 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            for(;;)
            {
                float4 _S1125 = float4(_S7->compositeControls_0) ;
                if((_S1125.x) != 64.0f)
                {
                    break;
                }
                bool _S1126 = (_S1125.w) >= 0.5f;
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
                uint _S1127 = index_0 | lightCount_0;
                for(;;)
                {
                    if(!_S1126)
                    {
                        for(;;)
                        {
                            if(_S1127 == 0U)
                            {
                                break;
                            }
                            texture2d<float, access::sample> _S1128 = (&kernelContext_0)->compositeTexture_0;
                            thread uint width_17;
                            thread uint height_17;
                            (*((&width_17)) = (_S1128).get_width(0)),(*((&height_17)) = (_S1128).get_height(0));
                            float2 size_13 = float2(float(width_17), float(height_17));
                            float2 _S1129 = float2(0.5f) ;
                            float2 remapped_13 = clamp((detailSource_0 * (size_13 - float2(2.0f) ) + float2(1.0f) ) / size_13, _S1129 / size_13, (size_13 - _S1129) / size_13);
                            if((_S1127 & 1U) != 0U)
                            {
                                opacity_0 = remapped_13.x;
                            }
                            else
                            {
                                opacity_0 = detailSource_0.x;
                            }
                            if((_S1127 & 2U) != 0U)
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
                        _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                        break;
                    }
                    texture2d<float, access::sample> _S1130 = (&kernelContext_0)->compositeTexture_0;
                    thread uint atlasWidth_11;
                    thread uint atlasHeight_11;
                    (*((&atlasWidth_11)) = (_S1130).get_width(0)),(*((&atlasHeight_11)) = (_S1130).get_height(0));
                    int3 _S1131 = int3(int(0), int(0), int(0));
                    float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1131)).xy), uint(((_S1131)).z))) * float4(255.0f) );
                    if((metadata_11.x) == 255.0f)
                    {
                        uint _S1132 = atlasWidth_11;
                        uint _S1133 = 2U % atlasWidth_11;
                        int _S1134 = int(_S1133);
                        uint _S1135 = 2U / _S1132;
                        int3 _S1136 = int3(_S1134, int(_S1135), int(0));
                        int _S1137 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1136)).xy), uint(((_S1136)).z))).x * 255.0f)));
                        uint _S1138 = atlasWidth_11;
                        uint _S1139 = 3U % atlasWidth_11;
                        int _S1140 = int(_S1139);
                        uint _S1141 = 3U / _S1138;
                        int3 _S1142 = int3(_S1140, int(_S1141), int(0));
                        int2 minimumTile_11 = int2(_S1137, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1142)).xy), uint(((_S1142)).z))).x * 255.0f))));
                        uint _S1143 = atlasWidth_11;
                        uint _S1144 = 4U % atlasWidth_11;
                        int _S1145 = int(_S1144);
                        uint _S1146 = 4U / _S1143;
                        int3 _S1147 = int3(_S1145, int(_S1146), int(0));
                        uint _S1148 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1147)).xy), uint(((_S1147)).z))).x * 255.0f));
                        uint _S1149 = atlasWidth_11;
                        uint _S1150 = 5U % atlasWidth_11;
                        int _S1151 = int(_S1150);
                        uint _S1152 = 5U / _S1149;
                        int3 _S1153 = int3(_S1151, int(_S1152), int(0));
                        uint _S1154 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1153)).xy), uint(((_S1153)).z))).x * 255.0f));
                        uint2 grid_11 = uint2(_S1148, _S1154);
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
                            int3 _S1155 = int3(int(1), int(0), int(0));
                            _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1155)).xy), uint(((_S1155)).z)));
                            break;
                        }
                        uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1148 + uint(tile_22.x));
                        uint _S1156 = atlasWidth_11;
                        uint _S1157 = offset_11 % atlasWidth_11;
                        int _S1158 = int(_S1157);
                        uint _S1159 = offset_11 / _S1156;
                        int3 _S1160 = int3(_S1158, int(_S1159), int(0));
                        uint _S1161 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1160)).xy), uint(((_S1160)).z))).x * 255.0f));
                        uint _S1162 = atlasWidth_11;
                        uint _S1163 = offset_11 + 1U;
                        uint _S1164 = _S1163 % atlasWidth_11;
                        int _S1165 = int(_S1164);
                        uint _S1166 = _S1163 / _S1162;
                        int3 _S1167 = int3(_S1165, int(_S1166), int(0));
                        uint slot_22 = _S1161 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1167)).xy), uint(((_S1167)).z))).x * 255.0f)) << 8U);
                        if(slot_22 == 0U)
                        {
                            int3 _S1168 = int3(int(1), int(0), int(0));
                            _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1168)).xy), uint(((_S1168)).z)));
                            break;
                        }
                        uint slot_23 = slot_22 - 1U;
                        uint _S1169 = atlasWidth_11;
                        uint _S1170 = 6U % atlasWidth_11;
                        int _S1171 = int(_S1170);
                        uint _S1172 = 6U / _S1169;
                        int3 _S1173 = int3(_S1171, int(_S1172), int(0));
                        uint _S1174 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1173)).xy), uint(((_S1173)).z))).x * 255.0f));
                        uint _S1175 = atlasWidth_11;
                        uint _S1176 = 7U % atlasWidth_11;
                        int _S1177 = int(_S1176);
                        uint _S1178 = 7U / _S1175;
                        int3 _S1179 = int3(_S1177, int(_S1178), int(0));
                        uint columns_11 = _S1174 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1179)).xy), uint(((_S1179)).z))).x * 255.0f)) << 8U);
                        uint _S1180 = atlasWidth_11;
                        uint _S1181 = 8U % atlasWidth_11;
                        int _S1182 = int(_S1181);
                        uint _S1183 = 8U / _S1180;
                        int3 _S1184 = int3(_S1182, int(_S1183), int(0));
                        uint _S1185 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1184)).xy), uint(((_S1184)).z))).x * 255.0f));
                        uint _S1186 = atlasWidth_11;
                        uint _S1187 = 9U % atlasWidth_11;
                        int _S1188 = int(_S1187);
                        uint _S1189 = 9U / _S1186;
                        int3 _S1190 = int3(_S1188, int(_S1189), int(0));
                        uint rows_11 = ((_S1185 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1190)).xy), uint(((_S1190)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                        uint metadataRows_11 = (10U + 2U * _S1148 * _S1154 + atlasWidth_11 - 1U) / atlasWidth_11;
                        uint _S1191 = atlasWidth_11 / columns_11;
                        float _S1192 = float(_S1191);
                        uint _S1193 = (atlasHeight_11 - metadataRows_11) / rows_11;
                        float2 cellSize_22 = float2(_S1192, float(_S1193));
                        uint _S1194 = slot_23 % columns_11;
                        float _S1195 = float(_S1194);
                        uint _S1196 = slot_23 / columns_11;
                        _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1195, float(_S1196)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(detailSource_0) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                        break;
                    }
                    int2 _S1197 = int2(metadata_11.zw);
                    int2 tile_23 = int2(floor(detailSource_0)) - int2(metadata_11.xy);
                    if(any(tile_23 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_23 >= _S1197);
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1198 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1198)).xy), uint(((_S1198)).z)));
                        break;
                    }
                    uint _S1199 = atlasWidth_11 / uint(_S1197.x);
                    float _S1200 = float(_S1199);
                    uint _S1201 = (atlasHeight_11 - 1U) / uint(_S1197.y);
                    float2 cellSize_23 = float2(_S1200, float(_S1201));
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                uint operation_4 = uint(round(_S1125.y));
                if(operation_4 == 1U)
                {
                    _S161 = _S161 * _S560;
                    break;
                }
                if(operation_4 == 2U)
                {
                    _S161 = _S161 + _S560;
                    break;
                }
                if(operation_4 == 3U)
                {
                    _S161 = _S161 - _S560;
                    break;
                }
                if(operation_4 == 4U)
                {
                    float factor_4 = _S1125.z;
                    _S161 = _S161 * float4((1.0f - factor_4))  + _S560 * float4(factor_4) ;
                    break;
                }
                break;
            }
            _S4 = saturate(_S161.x);
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
                detailSource_0 = _S160;
            }
            else
            {
                detailSource_0 = _S159;
            }
        }
        else
        {
            if(useSecondary_6)
            {
                detailSource_0 = _S152;
            }
            else
            {
                detailSource_0 = _S151;
            }
        }
        bool _S1202 = (udimMask_0 & 128U) != 0U;
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
        uint _S1203 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1202)
            {
                for(;;)
                {
                    if(_S1203 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1204 = (&kernelContext_0)->occlusionTexture_0;
                    thread uint width_18;
                    thread uint height_18;
                    (*((&width_18)) = (_S1204).get_width(0)),(*((&height_18)) = (_S1204).get_height(0));
                    float2 size_14 = float2(float(width_18), float(height_18));
                    float2 _S1205 = float2(0.5f) ;
                    float2 remapped_14 = clamp((detailSource_0 * (size_14 - float2(2.0f) ) + float2(1.0f) ) / size_14, _S1205 / size_14, (size_14 - _S1205) / size_14);
                    if((_S1203 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_14.x;
                    }
                    else
                    {
                        occlusion_0 = detailSource_0.x;
                    }
                    if((_S1203 & 2U) != 0U)
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
                _S161 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1206 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1206).get_width(0)),(*((&atlasHeight_12)) = (_S1206).get_height(0));
            int3 _S1207 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1207)).xy), uint(((_S1207)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1208 = atlasWidth_12;
                uint _S1209 = 2U % atlasWidth_12;
                int _S1210 = int(_S1209);
                uint _S1211 = 2U / _S1208;
                int3 _S1212 = int3(_S1210, int(_S1211), int(0));
                int _S1213 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1212)).xy), uint(((_S1212)).z))).x * 255.0f)));
                uint _S1214 = atlasWidth_12;
                uint _S1215 = 3U % atlasWidth_12;
                int _S1216 = int(_S1215);
                uint _S1217 = 3U / _S1214;
                int3 _S1218 = int3(_S1216, int(_S1217), int(0));
                int2 minimumTile_12 = int2(_S1213, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1218)).xy), uint(((_S1218)).z))).x * 255.0f))));
                uint _S1219 = atlasWidth_12;
                uint _S1220 = 4U % atlasWidth_12;
                int _S1221 = int(_S1220);
                uint _S1222 = 4U / _S1219;
                int3 _S1223 = int3(_S1221, int(_S1222), int(0));
                uint _S1224 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1223)).xy), uint(((_S1223)).z))).x * 255.0f));
                uint _S1225 = atlasWidth_12;
                uint _S1226 = 5U % atlasWidth_12;
                int _S1227 = int(_S1226);
                uint _S1228 = 5U / _S1225;
                int3 _S1229 = int3(_S1227, int(_S1228), int(0));
                uint _S1230 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1229)).xy), uint(((_S1229)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1224, _S1230);
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
                    int3 _S1231 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1231)).xy), uint(((_S1231)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1224 + uint(tile_24.x));
                uint _S1232 = atlasWidth_12;
                uint _S1233 = offset_12 % atlasWidth_12;
                int _S1234 = int(_S1233);
                uint _S1235 = offset_12 / _S1232;
                int3 _S1236 = int3(_S1234, int(_S1235), int(0));
                uint _S1237 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1236)).xy), uint(((_S1236)).z))).x * 255.0f));
                uint _S1238 = atlasWidth_12;
                uint _S1239 = offset_12 + 1U;
                uint _S1240 = _S1239 % atlasWidth_12;
                int _S1241 = int(_S1240);
                uint _S1242 = _S1239 / _S1238;
                int3 _S1243 = int3(_S1241, int(_S1242), int(0));
                uint slot_24 = _S1237 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1243)).xy), uint(((_S1243)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1244 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1244)).xy), uint(((_S1244)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1245 = atlasWidth_12;
                uint _S1246 = 6U % atlasWidth_12;
                int _S1247 = int(_S1246);
                uint _S1248 = 6U / _S1245;
                int3 _S1249 = int3(_S1247, int(_S1248), int(0));
                uint _S1250 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1249)).xy), uint(((_S1249)).z))).x * 255.0f));
                uint _S1251 = atlasWidth_12;
                uint _S1252 = 7U % atlasWidth_12;
                int _S1253 = int(_S1252);
                uint _S1254 = 7U / _S1251;
                int3 _S1255 = int3(_S1253, int(_S1254), int(0));
                uint columns_12 = _S1250 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1255)).xy), uint(((_S1255)).z))).x * 255.0f)) << 8U);
                uint _S1256 = atlasWidth_12;
                uint _S1257 = 8U % atlasWidth_12;
                int _S1258 = int(_S1257);
                uint _S1259 = 8U / _S1256;
                int3 _S1260 = int3(_S1258, int(_S1259), int(0));
                uint _S1261 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1260)).xy), uint(((_S1260)).z))).x * 255.0f));
                uint _S1262 = atlasWidth_12;
                uint _S1263 = 9U % atlasWidth_12;
                int _S1264 = int(_S1263);
                uint _S1265 = 9U / _S1262;
                int3 _S1266 = int3(_S1264, int(_S1265), int(0));
                uint rows_12 = ((_S1261 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1266)).xy), uint(((_S1266)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1224 * _S1230 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1267 = atlasWidth_12 / columns_12;
                float _S1268 = float(_S1267);
                uint _S1269 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1268, float(_S1269));
                uint _S1270 = slot_25 % columns_12;
                float _S1271 = float(_S1270);
                uint _S1272 = slot_25 / columns_12;
                _S161 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1271, float(_S1272)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(detailSource_0) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1273 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(detailSource_0)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1273);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1274 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S161 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1274)).xy), uint(((_S1274)).z)));
                break;
            }
            uint _S1275 = atlasWidth_12 / uint(_S1273.x);
            float _S1276 = float(_S1275);
            uint _S1277 = (atlasHeight_12 - 1U) / uint(_S1273.y);
            float2 cellSize_25 = float2(_S1276, float(_S1277));
            _S161 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1278 = float4(_S7->compositeControls_0) ;
            if((_S1278.x) != 128.0f)
            {
                break;
            }
            bool _S1279 = (_S1278.w) >= 0.5f;
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
            uint _S1280 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1279)
                {
                    for(;;)
                    {
                        if(_S1280 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1281 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_19;
                        thread uint height_19;
                        (*((&width_19)) = (_S1281).get_width(0)),(*((&height_19)) = (_S1281).get_height(0));
                        float2 size_15 = float2(float(width_19), float(height_19));
                        float2 _S1282 = float2(0.5f) ;
                        float2 remapped_15 = clamp((detailSource_0 * (size_15 - float2(2.0f) ) + float2(1.0f) ) / size_15, _S1282 / size_15, (size_15 - _S1282) / size_15);
                        if((_S1280 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_15.x;
                        }
                        else
                        {
                            occlusion_0 = detailSource_0.x;
                        }
                        if((_S1280 & 2U) != 0U)
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
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1283 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1283).get_width(0)),(*((&atlasHeight_13)) = (_S1283).get_height(0));
                int3 _S1284 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1284)).xy), uint(((_S1284)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1285 = atlasWidth_13;
                    uint _S1286 = 2U % atlasWidth_13;
                    int _S1287 = int(_S1286);
                    uint _S1288 = 2U / _S1285;
                    int3 _S1289 = int3(_S1287, int(_S1288), int(0));
                    int _S1290 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1289)).xy), uint(((_S1289)).z))).x * 255.0f)));
                    uint _S1291 = atlasWidth_13;
                    uint _S1292 = 3U % atlasWidth_13;
                    int _S1293 = int(_S1292);
                    uint _S1294 = 3U / _S1291;
                    int3 _S1295 = int3(_S1293, int(_S1294), int(0));
                    int2 minimumTile_13 = int2(_S1290, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1295)).xy), uint(((_S1295)).z))).x * 255.0f))));
                    uint _S1296 = atlasWidth_13;
                    uint _S1297 = 4U % atlasWidth_13;
                    int _S1298 = int(_S1297);
                    uint _S1299 = 4U / _S1296;
                    int3 _S1300 = int3(_S1298, int(_S1299), int(0));
                    uint _S1301 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1300)).xy), uint(((_S1300)).z))).x * 255.0f));
                    uint _S1302 = atlasWidth_13;
                    uint _S1303 = 5U % atlasWidth_13;
                    int _S1304 = int(_S1303);
                    uint _S1305 = 5U / _S1302;
                    int3 _S1306 = int3(_S1304, int(_S1305), int(0));
                    uint _S1307 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1306)).xy), uint(((_S1306)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1301, _S1307);
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
                        int3 _S1308 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1308)).xy), uint(((_S1308)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1301 + uint(tile_26.x));
                    uint _S1309 = atlasWidth_13;
                    uint _S1310 = offset_13 % atlasWidth_13;
                    int _S1311 = int(_S1310);
                    uint _S1312 = offset_13 / _S1309;
                    int3 _S1313 = int3(_S1311, int(_S1312), int(0));
                    uint _S1314 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1313)).xy), uint(((_S1313)).z))).x * 255.0f));
                    uint _S1315 = atlasWidth_13;
                    uint _S1316 = offset_13 + 1U;
                    uint _S1317 = _S1316 % atlasWidth_13;
                    int _S1318 = int(_S1317);
                    uint _S1319 = _S1316 / _S1315;
                    int3 _S1320 = int3(_S1318, int(_S1319), int(0));
                    uint slot_26 = _S1314 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1320)).xy), uint(((_S1320)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1321 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1321)).xy), uint(((_S1321)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1322 = atlasWidth_13;
                    uint _S1323 = 6U % atlasWidth_13;
                    int _S1324 = int(_S1323);
                    uint _S1325 = 6U / _S1322;
                    int3 _S1326 = int3(_S1324, int(_S1325), int(0));
                    uint _S1327 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1326)).xy), uint(((_S1326)).z))).x * 255.0f));
                    uint _S1328 = atlasWidth_13;
                    uint _S1329 = 7U % atlasWidth_13;
                    int _S1330 = int(_S1329);
                    uint _S1331 = 7U / _S1328;
                    int3 _S1332 = int3(_S1330, int(_S1331), int(0));
                    uint columns_13 = _S1327 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1332)).xy), uint(((_S1332)).z))).x * 255.0f)) << 8U);
                    uint _S1333 = atlasWidth_13;
                    uint _S1334 = 8U % atlasWidth_13;
                    int _S1335 = int(_S1334);
                    uint _S1336 = 8U / _S1333;
                    int3 _S1337 = int3(_S1335, int(_S1336), int(0));
                    uint _S1338 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1337)).xy), uint(((_S1337)).z))).x * 255.0f));
                    uint _S1339 = atlasWidth_13;
                    uint _S1340 = 9U % atlasWidth_13;
                    int _S1341 = int(_S1340);
                    uint _S1342 = 9U / _S1339;
                    int3 _S1343 = int3(_S1341, int(_S1342), int(0));
                    uint rows_13 = ((_S1338 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1343)).xy), uint(((_S1343)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1301 * _S1307 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1344 = atlasWidth_13 / columns_13;
                    float _S1345 = float(_S1344);
                    uint _S1346 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1345, float(_S1346));
                    uint _S1347 = slot_27 % columns_13;
                    float _S1348 = float(_S1347);
                    uint _S1349 = slot_27 / columns_13;
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1348, float(_S1349)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(detailSource_0) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1350 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(detailSource_0)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1350);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1351 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1351)).xy), uint(((_S1351)).z)));
                    break;
                }
                uint _S1352 = atlasWidth_13 / uint(_S1350.x);
                float _S1353 = float(_S1352);
                uint _S1354 = (atlasHeight_13 - 1U) / uint(_S1350.y);
                float2 cellSize_27 = float2(_S1353, float(_S1354));
                _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_5 = uint(round(_S1278.y));
            if(operation_5 == 1U)
            {
                _S161 = _S161 * _S560;
                break;
            }
            if(operation_5 == 2U)
            {
                _S161 = _S161 + _S560;
                break;
            }
            if(operation_5 == 3U)
            {
                _S161 = _S161 - _S560;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1278.z;
                _S161 = _S161 * float4((1.0f - factor_5))  + _S560 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S161.x);
    }
    else
    {
        occlusion_0 = _S131;
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
                detailSource_0 = _S160;
            }
            else
            {
                detailSource_0 = _S159;
            }
        }
        else
        {
            if(useSecondary_7)
            {
                detailSource_0 = _S152;
            }
            else
            {
                detailSource_0 = _S151;
            }
        }
        bool _S1355 = (udimMask_0 & 256U) != 0U;
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
        uint _S1356 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1355)
            {
                for(;;)
                {
                    if(_S1356 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1357 = (&kernelContext_0)->specularColorTexture_0;
                    thread uint width_20;
                    thread uint height_20;
                    (*((&width_20)) = (_S1357).get_width(0)),(*((&height_20)) = (_S1357).get_height(0));
                    float2 size_16 = float2(float(width_20), float(height_20));
                    float2 _S1358 = float2(0.5f) ;
                    float2 remapped_16 = clamp((detailSource_0 * (size_16 - float2(2.0f) ) + float2(1.0f) ) / size_16, _S1358 / size_16, (size_16 - _S1358) / size_16);
                    if((_S1356 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_16.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.x;
                    }
                    if((_S1356 & 2U) != 0U)
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
                _S161 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1359 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1359).get_width(0)),(*((&atlasHeight_14)) = (_S1359).get_height(0));
            int3 _S1360 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1360)).xy), uint(((_S1360)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1361 = atlasWidth_14;
                uint _S1362 = 2U % atlasWidth_14;
                int _S1363 = int(_S1362);
                uint _S1364 = 2U / _S1361;
                int3 _S1365 = int3(_S1363, int(_S1364), int(0));
                int _S1366 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1365)).xy), uint(((_S1365)).z))).x * 255.0f)));
                uint _S1367 = atlasWidth_14;
                uint _S1368 = 3U % atlasWidth_14;
                int _S1369 = int(_S1368);
                uint _S1370 = 3U / _S1367;
                int3 _S1371 = int3(_S1369, int(_S1370), int(0));
                int2 minimumTile_14 = int2(_S1366, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1371)).xy), uint(((_S1371)).z))).x * 255.0f))));
                uint _S1372 = atlasWidth_14;
                uint _S1373 = 4U % atlasWidth_14;
                int _S1374 = int(_S1373);
                uint _S1375 = 4U / _S1372;
                int3 _S1376 = int3(_S1374, int(_S1375), int(0));
                uint _S1377 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1376)).xy), uint(((_S1376)).z))).x * 255.0f));
                uint _S1378 = atlasWidth_14;
                uint _S1379 = 5U % atlasWidth_14;
                int _S1380 = int(_S1379);
                uint _S1381 = 5U / _S1378;
                int3 _S1382 = int3(_S1380, int(_S1381), int(0));
                uint _S1383 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1382)).xy), uint(((_S1382)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1377, _S1383);
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
                    int3 _S1384 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1384)).xy), uint(((_S1384)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1377 + uint(tile_28.x));
                uint _S1385 = atlasWidth_14;
                uint _S1386 = offset_14 % atlasWidth_14;
                int _S1387 = int(_S1386);
                uint _S1388 = offset_14 / _S1385;
                int3 _S1389 = int3(_S1387, int(_S1388), int(0));
                uint _S1390 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1389)).xy), uint(((_S1389)).z))).x * 255.0f));
                uint _S1391 = atlasWidth_14;
                uint _S1392 = offset_14 + 1U;
                uint _S1393 = _S1392 % atlasWidth_14;
                int _S1394 = int(_S1393);
                uint _S1395 = _S1392 / _S1391;
                int3 _S1396 = int3(_S1394, int(_S1395), int(0));
                uint slot_28 = _S1390 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1396)).xy), uint(((_S1396)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1397 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1397)).xy), uint(((_S1397)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1398 = atlasWidth_14;
                uint _S1399 = 6U % atlasWidth_14;
                int _S1400 = int(_S1399);
                uint _S1401 = 6U / _S1398;
                int3 _S1402 = int3(_S1400, int(_S1401), int(0));
                uint _S1403 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1402)).xy), uint(((_S1402)).z))).x * 255.0f));
                uint _S1404 = atlasWidth_14;
                uint _S1405 = 7U % atlasWidth_14;
                int _S1406 = int(_S1405);
                uint _S1407 = 7U / _S1404;
                int3 _S1408 = int3(_S1406, int(_S1407), int(0));
                uint columns_14 = _S1403 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1408)).xy), uint(((_S1408)).z))).x * 255.0f)) << 8U);
                uint _S1409 = atlasWidth_14;
                uint _S1410 = 8U % atlasWidth_14;
                int _S1411 = int(_S1410);
                uint _S1412 = 8U / _S1409;
                int3 _S1413 = int3(_S1411, int(_S1412), int(0));
                uint _S1414 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1413)).xy), uint(((_S1413)).z))).x * 255.0f));
                uint _S1415 = atlasWidth_14;
                uint _S1416 = 9U % atlasWidth_14;
                int _S1417 = int(_S1416);
                uint _S1418 = 9U / _S1415;
                int3 _S1419 = int3(_S1417, int(_S1418), int(0));
                uint rows_14 = ((_S1414 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1419)).xy), uint(((_S1419)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1377 * _S1383 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1420 = atlasWidth_14 / columns_14;
                float _S1421 = float(_S1420);
                uint _S1422 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1421, float(_S1422));
                uint _S1423 = slot_29 % columns_14;
                float _S1424 = float(_S1423);
                uint _S1425 = slot_29 / columns_14;
                _S161 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1424, float(_S1425)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(detailSource_0) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1426 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(detailSource_0)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1426);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1427 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S161 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1427)).xy), uint(((_S1427)).z)));
                break;
            }
            uint _S1428 = atlasWidth_14 / uint(_S1426.x);
            float _S1429 = float(_S1428);
            uint _S1430 = (atlasHeight_14 - 1U) / uint(_S1426.y);
            float2 cellSize_29 = float2(_S1429, float(_S1430));
            _S161 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1431 = float4(_S7->compositeControls_0) ;
            if((_S1431.x) != 256.0f)
            {
                break;
            }
            bool _S1432 = (_S1431.w) >= 0.5f;
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
            uint _S1433 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1432)
                {
                    for(;;)
                    {
                        if(_S1433 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1434 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_21;
                        thread uint height_21;
                        (*((&width_21)) = (_S1434).get_width(0)),(*((&height_21)) = (_S1434).get_height(0));
                        float2 size_17 = float2(float(width_21), float(height_21));
                        float2 _S1435 = float2(0.5f) ;
                        float2 remapped_17 = clamp((detailSource_0 * (size_17 - float2(2.0f) ) + float2(1.0f) ) / size_17, _S1435 / size_17, (size_17 - _S1435) / size_17);
                        if((_S1433 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_17.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.x;
                        }
                        if((_S1433 & 2U) != 0U)
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
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1436 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1436).get_width(0)),(*((&atlasHeight_15)) = (_S1436).get_height(0));
                int3 _S1437 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1437)).xy), uint(((_S1437)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1438 = atlasWidth_15;
                    uint _S1439 = 2U % atlasWidth_15;
                    int _S1440 = int(_S1439);
                    uint _S1441 = 2U / _S1438;
                    int3 _S1442 = int3(_S1440, int(_S1441), int(0));
                    int _S1443 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1442)).xy), uint(((_S1442)).z))).x * 255.0f)));
                    uint _S1444 = atlasWidth_15;
                    uint _S1445 = 3U % atlasWidth_15;
                    int _S1446 = int(_S1445);
                    uint _S1447 = 3U / _S1444;
                    int3 _S1448 = int3(_S1446, int(_S1447), int(0));
                    int2 minimumTile_15 = int2(_S1443, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1448)).xy), uint(((_S1448)).z))).x * 255.0f))));
                    uint _S1449 = atlasWidth_15;
                    uint _S1450 = 4U % atlasWidth_15;
                    int _S1451 = int(_S1450);
                    uint _S1452 = 4U / _S1449;
                    int3 _S1453 = int3(_S1451, int(_S1452), int(0));
                    uint _S1454 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1453)).xy), uint(((_S1453)).z))).x * 255.0f));
                    uint _S1455 = atlasWidth_15;
                    uint _S1456 = 5U % atlasWidth_15;
                    int _S1457 = int(_S1456);
                    uint _S1458 = 5U / _S1455;
                    int3 _S1459 = int3(_S1457, int(_S1458), int(0));
                    uint _S1460 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1459)).xy), uint(((_S1459)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1454, _S1460);
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
                        int3 _S1461 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1461)).xy), uint(((_S1461)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1454 + uint(tile_30.x));
                    uint _S1462 = atlasWidth_15;
                    uint _S1463 = offset_15 % atlasWidth_15;
                    int _S1464 = int(_S1463);
                    uint _S1465 = offset_15 / _S1462;
                    int3 _S1466 = int3(_S1464, int(_S1465), int(0));
                    uint _S1467 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1466)).xy), uint(((_S1466)).z))).x * 255.0f));
                    uint _S1468 = atlasWidth_15;
                    uint _S1469 = offset_15 + 1U;
                    uint _S1470 = _S1469 % atlasWidth_15;
                    int _S1471 = int(_S1470);
                    uint _S1472 = _S1469 / _S1468;
                    int3 _S1473 = int3(_S1471, int(_S1472), int(0));
                    uint slot_30 = _S1467 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1473)).xy), uint(((_S1473)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1474 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1474)).xy), uint(((_S1474)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1475 = atlasWidth_15;
                    uint _S1476 = 6U % atlasWidth_15;
                    int _S1477 = int(_S1476);
                    uint _S1478 = 6U / _S1475;
                    int3 _S1479 = int3(_S1477, int(_S1478), int(0));
                    uint _S1480 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1479)).xy), uint(((_S1479)).z))).x * 255.0f));
                    uint _S1481 = atlasWidth_15;
                    uint _S1482 = 7U % atlasWidth_15;
                    int _S1483 = int(_S1482);
                    uint _S1484 = 7U / _S1481;
                    int3 _S1485 = int3(_S1483, int(_S1484), int(0));
                    uint columns_15 = _S1480 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1485)).xy), uint(((_S1485)).z))).x * 255.0f)) << 8U);
                    uint _S1486 = atlasWidth_15;
                    uint _S1487 = 8U % atlasWidth_15;
                    int _S1488 = int(_S1487);
                    uint _S1489 = 8U / _S1486;
                    int3 _S1490 = int3(_S1488, int(_S1489), int(0));
                    uint _S1491 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1490)).xy), uint(((_S1490)).z))).x * 255.0f));
                    uint _S1492 = atlasWidth_15;
                    uint _S1493 = 9U % atlasWidth_15;
                    int _S1494 = int(_S1493);
                    uint _S1495 = 9U / _S1492;
                    int3 _S1496 = int3(_S1494, int(_S1495), int(0));
                    uint rows_15 = ((_S1491 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1496)).xy), uint(((_S1496)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1454 * _S1460 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1497 = atlasWidth_15 / columns_15;
                    float _S1498 = float(_S1497);
                    uint _S1499 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1498, float(_S1499));
                    uint _S1500 = slot_31 % columns_15;
                    float _S1501 = float(_S1500);
                    uint _S1502 = slot_31 / columns_15;
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1501, float(_S1502)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(detailSource_0) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1503 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(detailSource_0)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1503);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1504 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1504)).xy), uint(((_S1504)).z)));
                    break;
                }
                uint _S1505 = atlasWidth_15 / uint(_S1503.x);
                float _S1506 = float(_S1505);
                uint _S1507 = (atlasHeight_15 - 1U) / uint(_S1503.y);
                float2 cellSize_31 = float2(_S1506, float(_S1507));
                _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_6 = uint(round(_S1431.y));
            if(operation_6 == 1U)
            {
                _S161 = _S161 * _S560;
                break;
            }
            if(operation_6 == 2U)
            {
                _S161 = _S161 + _S560;
                break;
            }
            if(operation_6 == 3U)
            {
                _S161 = _S161 - _S560;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1431.z;
                _S161 = _S161 * float4((1.0f - factor_6))  + _S560 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S161.xyz);
    }
    else
    {
        specularColor_0 = _S139;
    }
    if((textureMask_0 & 512U) != 0U)
    {
        bool useSecondary_8 = (secondaryMask_0 & 512U) != 0U;
        if((alternateMask_0 & 512U) != 0U)
        {
            if(useSecondary_8)
            {
                detailSource_0 = _S160;
            }
            else
            {
                detailSource_0 = _S159;
            }
        }
        else
        {
            if(useSecondary_8)
            {
                detailSource_0 = _S152;
            }
            else
            {
                detailSource_0 = _S151;
            }
        }
        bool _S1508 = (udimMask_0 & 512U) != 0U;
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
        uint _S1509 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1508)
            {
                for(;;)
                {
                    if(_S1509 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1510 = (&kernelContext_0)->clearcoatTexture_0;
                    thread uint width_22;
                    thread uint height_22;
                    (*((&width_22)) = (_S1510).get_width(0)),(*((&height_22)) = (_S1510).get_height(0));
                    float2 size_18 = float2(float(width_22), float(height_22));
                    float2 _S1511 = float2(0.5f) ;
                    float2 remapped_18 = clamp((detailSource_0 * (size_18 - float2(2.0f) ) + float2(1.0f) ) / size_18, _S1511 / size_18, (size_18 - _S1511) / size_18);
                    if((_S1509 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_18.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = detailSource_0.x;
                    }
                    if((_S1509 & 2U) != 0U)
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
                _S161 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1512 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1512).get_width(0)),(*((&atlasHeight_16)) = (_S1512).get_height(0));
            int3 _S1513 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1513)).xy), uint(((_S1513)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1514 = atlasWidth_16;
                uint _S1515 = 2U % atlasWidth_16;
                int _S1516 = int(_S1515);
                uint _S1517 = 2U / _S1514;
                int3 _S1518 = int3(_S1516, int(_S1517), int(0));
                int _S1519 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1518)).xy), uint(((_S1518)).z))).x * 255.0f)));
                uint _S1520 = atlasWidth_16;
                uint _S1521 = 3U % atlasWidth_16;
                int _S1522 = int(_S1521);
                uint _S1523 = 3U / _S1520;
                int3 _S1524 = int3(_S1522, int(_S1523), int(0));
                int2 minimumTile_16 = int2(_S1519, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1524)).xy), uint(((_S1524)).z))).x * 255.0f))));
                uint _S1525 = atlasWidth_16;
                uint _S1526 = 4U % atlasWidth_16;
                int _S1527 = int(_S1526);
                uint _S1528 = 4U / _S1525;
                int3 _S1529 = int3(_S1527, int(_S1528), int(0));
                uint _S1530 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1529)).xy), uint(((_S1529)).z))).x * 255.0f));
                uint _S1531 = atlasWidth_16;
                uint _S1532 = 5U % atlasWidth_16;
                int _S1533 = int(_S1532);
                uint _S1534 = 5U / _S1531;
                int3 _S1535 = int3(_S1533, int(_S1534), int(0));
                uint _S1536 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1535)).xy), uint(((_S1535)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1530, _S1536);
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
                    int3 _S1537 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1537)).xy), uint(((_S1537)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1530 + uint(tile_32.x));
                uint _S1538 = atlasWidth_16;
                uint _S1539 = offset_16 % atlasWidth_16;
                int _S1540 = int(_S1539);
                uint _S1541 = offset_16 / _S1538;
                int3 _S1542 = int3(_S1540, int(_S1541), int(0));
                uint _S1543 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1542)).xy), uint(((_S1542)).z))).x * 255.0f));
                uint _S1544 = atlasWidth_16;
                uint _S1545 = offset_16 + 1U;
                uint _S1546 = _S1545 % atlasWidth_16;
                int _S1547 = int(_S1546);
                uint _S1548 = _S1545 / _S1544;
                int3 _S1549 = int3(_S1547, int(_S1548), int(0));
                uint slot_32 = _S1543 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1549)).xy), uint(((_S1549)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1550 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1550)).xy), uint(((_S1550)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1551 = atlasWidth_16;
                uint _S1552 = 6U % atlasWidth_16;
                int _S1553 = int(_S1552);
                uint _S1554 = 6U / _S1551;
                int3 _S1555 = int3(_S1553, int(_S1554), int(0));
                uint _S1556 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1555)).xy), uint(((_S1555)).z))).x * 255.0f));
                uint _S1557 = atlasWidth_16;
                uint _S1558 = 7U % atlasWidth_16;
                int _S1559 = int(_S1558);
                uint _S1560 = 7U / _S1557;
                int3 _S1561 = int3(_S1559, int(_S1560), int(0));
                uint columns_16 = _S1556 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1561)).xy), uint(((_S1561)).z))).x * 255.0f)) << 8U);
                uint _S1562 = atlasWidth_16;
                uint _S1563 = 8U % atlasWidth_16;
                int _S1564 = int(_S1563);
                uint _S1565 = 8U / _S1562;
                int3 _S1566 = int3(_S1564, int(_S1565), int(0));
                uint _S1567 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1566)).xy), uint(((_S1566)).z))).x * 255.0f));
                uint _S1568 = atlasWidth_16;
                uint _S1569 = 9U % atlasWidth_16;
                int _S1570 = int(_S1569);
                uint _S1571 = 9U / _S1568;
                int3 _S1572 = int3(_S1570, int(_S1571), int(0));
                uint rows_16 = ((_S1567 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1572)).xy), uint(((_S1572)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1530 * _S1536 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1573 = atlasWidth_16 / columns_16;
                float _S1574 = float(_S1573);
                uint _S1575 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1574, float(_S1575));
                uint _S1576 = slot_33 % columns_16;
                float _S1577 = float(_S1576);
                uint _S1578 = slot_33 / columns_16;
                _S161 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1577, float(_S1578)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(detailSource_0) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1579 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(detailSource_0)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1579);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1580 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S161 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1580)).xy), uint(((_S1580)).z)));
                break;
            }
            uint _S1581 = atlasWidth_16 / uint(_S1579.x);
            float _S1582 = float(_S1581);
            uint _S1583 = (atlasHeight_16 - 1U) / uint(_S1579.y);
            float2 cellSize_33 = float2(_S1582, float(_S1583));
            _S161 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1584 = float4(_S7->compositeControls_0) ;
            if((_S1584.x) != 512.0f)
            {
                break;
            }
            bool _S1585 = (_S1584.w) >= 0.5f;
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
            uint _S1586 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1585)
                {
                    for(;;)
                    {
                        if(_S1586 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1587 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_23;
                        thread uint height_23;
                        (*((&width_23)) = (_S1587).get_width(0)),(*((&height_23)) = (_S1587).get_height(0));
                        float2 size_19 = float2(float(width_23), float(height_23));
                        float2 _S1588 = float2(0.5f) ;
                        float2 remapped_19 = clamp((detailSource_0 * (size_19 - float2(2.0f) ) + float2(1.0f) ) / size_19, _S1588 / size_19, (size_19 - _S1588) / size_19);
                        if((_S1586 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_19.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = detailSource_0.x;
                        }
                        if((_S1586 & 2U) != 0U)
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
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1589 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1589).get_width(0)),(*((&atlasHeight_17)) = (_S1589).get_height(0));
                int3 _S1590 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1590)).xy), uint(((_S1590)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1591 = atlasWidth_17;
                    uint _S1592 = 2U % atlasWidth_17;
                    int _S1593 = int(_S1592);
                    uint _S1594 = 2U / _S1591;
                    int3 _S1595 = int3(_S1593, int(_S1594), int(0));
                    int _S1596 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1595)).xy), uint(((_S1595)).z))).x * 255.0f)));
                    uint _S1597 = atlasWidth_17;
                    uint _S1598 = 3U % atlasWidth_17;
                    int _S1599 = int(_S1598);
                    uint _S1600 = 3U / _S1597;
                    int3 _S1601 = int3(_S1599, int(_S1600), int(0));
                    int2 minimumTile_17 = int2(_S1596, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1601)).xy), uint(((_S1601)).z))).x * 255.0f))));
                    uint _S1602 = atlasWidth_17;
                    uint _S1603 = 4U % atlasWidth_17;
                    int _S1604 = int(_S1603);
                    uint _S1605 = 4U / _S1602;
                    int3 _S1606 = int3(_S1604, int(_S1605), int(0));
                    uint _S1607 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1606)).xy), uint(((_S1606)).z))).x * 255.0f));
                    uint _S1608 = atlasWidth_17;
                    uint _S1609 = 5U % atlasWidth_17;
                    int _S1610 = int(_S1609);
                    uint _S1611 = 5U / _S1608;
                    int3 _S1612 = int3(_S1610, int(_S1611), int(0));
                    uint _S1613 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1612)).xy), uint(((_S1612)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1607, _S1613);
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
                        int3 _S1614 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1614)).xy), uint(((_S1614)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1607 + uint(tile_34.x));
                    uint _S1615 = atlasWidth_17;
                    uint _S1616 = offset_17 % atlasWidth_17;
                    int _S1617 = int(_S1616);
                    uint _S1618 = offset_17 / _S1615;
                    int3 _S1619 = int3(_S1617, int(_S1618), int(0));
                    uint _S1620 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1619)).xy), uint(((_S1619)).z))).x * 255.0f));
                    uint _S1621 = atlasWidth_17;
                    uint _S1622 = offset_17 + 1U;
                    uint _S1623 = _S1622 % atlasWidth_17;
                    int _S1624 = int(_S1623);
                    uint _S1625 = _S1622 / _S1621;
                    int3 _S1626 = int3(_S1624, int(_S1625), int(0));
                    uint slot_34 = _S1620 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1626)).xy), uint(((_S1626)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1627 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1627)).xy), uint(((_S1627)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1628 = atlasWidth_17;
                    uint _S1629 = 6U % atlasWidth_17;
                    int _S1630 = int(_S1629);
                    uint _S1631 = 6U / _S1628;
                    int3 _S1632 = int3(_S1630, int(_S1631), int(0));
                    uint _S1633 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1632)).xy), uint(((_S1632)).z))).x * 255.0f));
                    uint _S1634 = atlasWidth_17;
                    uint _S1635 = 7U % atlasWidth_17;
                    int _S1636 = int(_S1635);
                    uint _S1637 = 7U / _S1634;
                    int3 _S1638 = int3(_S1636, int(_S1637), int(0));
                    uint columns_17 = _S1633 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1638)).xy), uint(((_S1638)).z))).x * 255.0f)) << 8U);
                    uint _S1639 = atlasWidth_17;
                    uint _S1640 = 8U % atlasWidth_17;
                    int _S1641 = int(_S1640);
                    uint _S1642 = 8U / _S1639;
                    int3 _S1643 = int3(_S1641, int(_S1642), int(0));
                    uint _S1644 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1643)).xy), uint(((_S1643)).z))).x * 255.0f));
                    uint _S1645 = atlasWidth_17;
                    uint _S1646 = 9U % atlasWidth_17;
                    int _S1647 = int(_S1646);
                    uint _S1648 = 9U / _S1645;
                    int3 _S1649 = int3(_S1647, int(_S1648), int(0));
                    uint rows_17 = ((_S1644 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1649)).xy), uint(((_S1649)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1607 * _S1613 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1650 = atlasWidth_17 / columns_17;
                    float _S1651 = float(_S1650);
                    uint _S1652 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1651, float(_S1652));
                    uint _S1653 = slot_35 % columns_17;
                    float _S1654 = float(_S1653);
                    uint _S1655 = slot_35 / columns_17;
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1654, float(_S1655)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(detailSource_0) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1656 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(detailSource_0)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1656);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1657 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1657)).xy), uint(((_S1657)).z)));
                    break;
                }
                uint _S1658 = atlasWidth_17 / uint(_S1656.x);
                float _S1659 = float(_S1658);
                uint _S1660 = (atlasHeight_17 - 1U) / uint(_S1656.y);
                float2 cellSize_35 = float2(_S1659, float(_S1660));
                _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_7 = uint(round(_S1584.y));
            if(operation_7 == 1U)
            {
                _S161 = _S161 * _S560;
                break;
            }
            if(operation_7 == 2U)
            {
                _S161 = _S161 + _S560;
                break;
            }
            if(operation_7 == 3U)
            {
                _S161 = _S161 - _S560;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1584.z;
                _S161 = _S161 * float4((1.0f - factor_7))  + _S560 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S161.x);
    }
    else
    {
        clearcoatAmount_0 = _S141;
    }
    float ior_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool useSecondary_9 = (secondaryMask_0 & 1024U) != 0U;
        if((alternateMask_0 & 1024U) != 0U)
        {
            if(useSecondary_9)
            {
                detailSource_0 = _S160;
            }
            else
            {
                detailSource_0 = _S159;
            }
        }
        else
        {
            if(useSecondary_9)
            {
                detailSource_0 = _S152;
            }
            else
            {
                detailSource_0 = _S151;
            }
        }
        bool _S1661 = (udimMask_0 & 1024U) != 0U;
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
        uint _S1662 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1661)
            {
                for(;;)
                {
                    if(_S1662 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1663 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
                    thread uint width_24;
                    thread uint height_24;
                    (*((&width_24)) = (_S1663).get_width(0)),(*((&height_24)) = (_S1663).get_height(0));
                    float2 size_20 = float2(float(width_24), float(height_24));
                    float2 _S1664 = float2(0.5f) ;
                    float2 remapped_20 = clamp((detailSource_0 * (size_20 - float2(2.0f) ) + float2(1.0f) ) / size_20, _S1664 / size_20, (size_20 - _S1664) / size_20);
                    if((_S1662 & 1U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_20.x;
                    }
                    else
                    {
                        clearcoatRoughness_0 = detailSource_0.x;
                    }
                    if((_S1662 & 2U) != 0U)
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
                _S161 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1665 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1665).get_width(0)),(*((&atlasHeight_18)) = (_S1665).get_height(0));
            int3 _S1666 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1666)).xy), uint(((_S1666)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1667 = atlasWidth_18;
                uint _S1668 = 2U % atlasWidth_18;
                int _S1669 = int(_S1668);
                uint _S1670 = 2U / _S1667;
                int3 _S1671 = int3(_S1669, int(_S1670), int(0));
                int _S1672 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1671)).xy), uint(((_S1671)).z))).x * 255.0f)));
                uint _S1673 = atlasWidth_18;
                uint _S1674 = 3U % atlasWidth_18;
                int _S1675 = int(_S1674);
                uint _S1676 = 3U / _S1673;
                int3 _S1677 = int3(_S1675, int(_S1676), int(0));
                int2 minimumTile_18 = int2(_S1672, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1677)).xy), uint(((_S1677)).z))).x * 255.0f))));
                uint _S1678 = atlasWidth_18;
                uint _S1679 = 4U % atlasWidth_18;
                int _S1680 = int(_S1679);
                uint _S1681 = 4U / _S1678;
                int3 _S1682 = int3(_S1680, int(_S1681), int(0));
                uint _S1683 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1682)).xy), uint(((_S1682)).z))).x * 255.0f));
                uint _S1684 = atlasWidth_18;
                uint _S1685 = 5U % atlasWidth_18;
                int _S1686 = int(_S1685);
                uint _S1687 = 5U / _S1684;
                int3 _S1688 = int3(_S1686, int(_S1687), int(0));
                uint _S1689 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1688)).xy), uint(((_S1688)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1683, _S1689);
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
                    int3 _S1690 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1690)).xy), uint(((_S1690)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1683 + uint(tile_36.x));
                uint _S1691 = atlasWidth_18;
                uint _S1692 = offset_18 % atlasWidth_18;
                int _S1693 = int(_S1692);
                uint _S1694 = offset_18 / _S1691;
                int3 _S1695 = int3(_S1693, int(_S1694), int(0));
                uint _S1696 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1695)).xy), uint(((_S1695)).z))).x * 255.0f));
                uint _S1697 = atlasWidth_18;
                uint _S1698 = offset_18 + 1U;
                uint _S1699 = _S1698 % atlasWidth_18;
                int _S1700 = int(_S1699);
                uint _S1701 = _S1698 / _S1697;
                int3 _S1702 = int3(_S1700, int(_S1701), int(0));
                uint slot_36 = _S1696 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1702)).xy), uint(((_S1702)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1703 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1703)).xy), uint(((_S1703)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1704 = atlasWidth_18;
                uint _S1705 = 6U % atlasWidth_18;
                int _S1706 = int(_S1705);
                uint _S1707 = 6U / _S1704;
                int3 _S1708 = int3(_S1706, int(_S1707), int(0));
                uint _S1709 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1708)).xy), uint(((_S1708)).z))).x * 255.0f));
                uint _S1710 = atlasWidth_18;
                uint _S1711 = 7U % atlasWidth_18;
                int _S1712 = int(_S1711);
                uint _S1713 = 7U / _S1710;
                int3 _S1714 = int3(_S1712, int(_S1713), int(0));
                uint columns_18 = _S1709 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1714)).xy), uint(((_S1714)).z))).x * 255.0f)) << 8U);
                uint _S1715 = atlasWidth_18;
                uint _S1716 = 8U % atlasWidth_18;
                int _S1717 = int(_S1716);
                uint _S1718 = 8U / _S1715;
                int3 _S1719 = int3(_S1717, int(_S1718), int(0));
                uint _S1720 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1719)).xy), uint(((_S1719)).z))).x * 255.0f));
                uint _S1721 = atlasWidth_18;
                uint _S1722 = 9U % atlasWidth_18;
                int _S1723 = int(_S1722);
                uint _S1724 = 9U / _S1721;
                int3 _S1725 = int3(_S1723, int(_S1724), int(0));
                uint rows_18 = ((_S1720 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1725)).xy), uint(((_S1725)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1683 * _S1689 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1726 = atlasWidth_18 / columns_18;
                float _S1727 = float(_S1726);
                uint _S1728 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1727, float(_S1728));
                uint _S1729 = slot_37 % columns_18;
                float _S1730 = float(_S1729);
                uint _S1731 = slot_37 / columns_18;
                _S161 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1730, float(_S1731)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(detailSource_0) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1732 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(detailSource_0)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1732);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1733 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S161 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1733)).xy), uint(((_S1733)).z)));
                break;
            }
            uint _S1734 = atlasWidth_18 / uint(_S1732.x);
            float _S1735 = float(_S1734);
            uint _S1736 = (atlasHeight_18 - 1U) / uint(_S1732.y);
            float2 cellSize_37 = float2(_S1735, float(_S1736));
            _S161 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1737 = float4(_S7->compositeControls_0) ;
            if((_S1737.x) != 1024.0f)
            {
                break;
            }
            bool _S1738 = (_S1737.w) >= 0.5f;
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
            uint _S1739 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1738)
                {
                    for(;;)
                    {
                        if(_S1739 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1740 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_25;
                        thread uint height_25;
                        (*((&width_25)) = (_S1740).get_width(0)),(*((&height_25)) = (_S1740).get_height(0));
                        float2 size_21 = float2(float(width_25), float(height_25));
                        float2 _S1741 = float2(0.5f) ;
                        float2 remapped_21 = clamp((detailSource_0 * (size_21 - float2(2.0f) ) + float2(1.0f) ) / size_21, _S1741 / size_21, (size_21 - _S1741) / size_21);
                        if((_S1739 & 1U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_21.x;
                        }
                        else
                        {
                            clearcoatRoughness_0 = detailSource_0.x;
                        }
                        if((_S1739 & 2U) != 0U)
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
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1742 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1742).get_width(0)),(*((&atlasHeight_19)) = (_S1742).get_height(0));
                int3 _S1743 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1743)).xy), uint(((_S1743)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1744 = atlasWidth_19;
                    uint _S1745 = 2U % atlasWidth_19;
                    int _S1746 = int(_S1745);
                    uint _S1747 = 2U / _S1744;
                    int3 _S1748 = int3(_S1746, int(_S1747), int(0));
                    int _S1749 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1748)).xy), uint(((_S1748)).z))).x * 255.0f)));
                    uint _S1750 = atlasWidth_19;
                    uint _S1751 = 3U % atlasWidth_19;
                    int _S1752 = int(_S1751);
                    uint _S1753 = 3U / _S1750;
                    int3 _S1754 = int3(_S1752, int(_S1753), int(0));
                    int2 minimumTile_19 = int2(_S1749, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1754)).xy), uint(((_S1754)).z))).x * 255.0f))));
                    uint _S1755 = atlasWidth_19;
                    uint _S1756 = 4U % atlasWidth_19;
                    int _S1757 = int(_S1756);
                    uint _S1758 = 4U / _S1755;
                    int3 _S1759 = int3(_S1757, int(_S1758), int(0));
                    uint _S1760 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1759)).xy), uint(((_S1759)).z))).x * 255.0f));
                    uint _S1761 = atlasWidth_19;
                    uint _S1762 = 5U % atlasWidth_19;
                    int _S1763 = int(_S1762);
                    uint _S1764 = 5U / _S1761;
                    int3 _S1765 = int3(_S1763, int(_S1764), int(0));
                    uint _S1766 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1765)).xy), uint(((_S1765)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1760, _S1766);
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
                        int3 _S1767 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1767)).xy), uint(((_S1767)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1760 + uint(tile_38.x));
                    uint _S1768 = atlasWidth_19;
                    uint _S1769 = offset_19 % atlasWidth_19;
                    int _S1770 = int(_S1769);
                    uint _S1771 = offset_19 / _S1768;
                    int3 _S1772 = int3(_S1770, int(_S1771), int(0));
                    uint _S1773 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1772)).xy), uint(((_S1772)).z))).x * 255.0f));
                    uint _S1774 = atlasWidth_19;
                    uint _S1775 = offset_19 + 1U;
                    uint _S1776 = _S1775 % atlasWidth_19;
                    int _S1777 = int(_S1776);
                    uint _S1778 = _S1775 / _S1774;
                    int3 _S1779 = int3(_S1777, int(_S1778), int(0));
                    uint slot_38 = _S1773 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1779)).xy), uint(((_S1779)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1780 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1780)).xy), uint(((_S1780)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1781 = atlasWidth_19;
                    uint _S1782 = 6U % atlasWidth_19;
                    int _S1783 = int(_S1782);
                    uint _S1784 = 6U / _S1781;
                    int3 _S1785 = int3(_S1783, int(_S1784), int(0));
                    uint _S1786 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1785)).xy), uint(((_S1785)).z))).x * 255.0f));
                    uint _S1787 = atlasWidth_19;
                    uint _S1788 = 7U % atlasWidth_19;
                    int _S1789 = int(_S1788);
                    uint _S1790 = 7U / _S1787;
                    int3 _S1791 = int3(_S1789, int(_S1790), int(0));
                    uint columns_19 = _S1786 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1791)).xy), uint(((_S1791)).z))).x * 255.0f)) << 8U);
                    uint _S1792 = atlasWidth_19;
                    uint _S1793 = 8U % atlasWidth_19;
                    int _S1794 = int(_S1793);
                    uint _S1795 = 8U / _S1792;
                    int3 _S1796 = int3(_S1794, int(_S1795), int(0));
                    uint _S1797 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1796)).xy), uint(((_S1796)).z))).x * 255.0f));
                    uint _S1798 = atlasWidth_19;
                    uint _S1799 = 9U % atlasWidth_19;
                    int _S1800 = int(_S1799);
                    uint _S1801 = 9U / _S1798;
                    int3 _S1802 = int3(_S1800, int(_S1801), int(0));
                    uint rows_19 = ((_S1797 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1802)).xy), uint(((_S1802)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1760 * _S1766 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1803 = atlasWidth_19 / columns_19;
                    float _S1804 = float(_S1803);
                    uint _S1805 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1804, float(_S1805));
                    uint _S1806 = slot_39 % columns_19;
                    float _S1807 = float(_S1806);
                    uint _S1808 = slot_39 / columns_19;
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1807, float(_S1808)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(detailSource_0) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1809 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(detailSource_0)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1809);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1810 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1810)).xy), uint(((_S1810)).z)));
                    break;
                }
                uint _S1811 = atlasWidth_19 / uint(_S1809.x);
                float _S1812 = float(_S1811);
                uint _S1813 = (atlasHeight_19 - 1U) / uint(_S1809.y);
                float2 cellSize_39 = float2(_S1812, float(_S1813));
                _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_8 = uint(round(_S1737.y));
            if(operation_8 == 1U)
            {
                _S161 = _S161 * _S560;
                break;
            }
            if(operation_8 == 2U)
            {
                _S161 = _S161 + _S560;
                break;
            }
            if(operation_8 == 3U)
            {
                _S161 = _S161 - _S560;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1737.z;
                _S161 = _S161 * float4((1.0f - factor_8))  + _S560 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S161.x);
    }
    else
    {
        clearcoatRoughness_0 = _S142;
    }
    float lightType_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool useSecondary_10 = (secondaryMask_0 & 2048U) != 0U;
        if((alternateMask_0 & 2048U) != 0U)
        {
            if(useSecondary_10)
            {
                detailSource_0 = _S160;
            }
            else
            {
                detailSource_0 = _S159;
            }
        }
        else
        {
            if(useSecondary_10)
            {
                detailSource_0 = _S152;
            }
            else
            {
                detailSource_0 = _S151;
            }
        }
        bool _S1814 = (udimMask_0 & 2048U) != 0U;
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
        uint _S1815 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1814)
            {
                for(;;)
                {
                    if(_S1815 == 0U)
                    {
                        tangent_1 = detailSource_0;
                        break;
                    }
                    texture2d<float, access::sample> _S1816 = (&kernelContext_0)->iorTexture_0;
                    thread uint width_26;
                    thread uint height_26;
                    (*((&width_26)) = (_S1816).get_width(0)),(*((&height_26)) = (_S1816).get_height(0));
                    float2 size_22 = float2(float(width_26), float(height_26));
                    float2 _S1817 = float2(0.5f) ;
                    float2 remapped_22 = clamp((detailSource_0 * (size_22 - float2(2.0f) ) + float2(1.0f) ) / size_22, _S1817 / size_22, (size_22 - _S1817) / size_22);
                    if((_S1815 & 1U) != 0U)
                    {
                        ior_0 = remapped_22.x;
                    }
                    else
                    {
                        ior_0 = detailSource_0.x;
                    }
                    if((_S1815 & 2U) != 0U)
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
                _S161 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (tangent_1)));
                break;
            }
            texture2d<float, access::sample> _S1818 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_20;
            thread uint atlasHeight_20;
            (*((&atlasWidth_20)) = (_S1818).get_width(0)),(*((&atlasHeight_20)) = (_S1818).get_height(0));
            int3 _S1819 = int3(int(0), int(0), int(0));
            float4 metadata_20 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1819)).xy), uint(((_S1819)).z))) * float4(255.0f) );
            if((metadata_20.x) == 255.0f)
            {
                uint _S1820 = atlasWidth_20;
                uint _S1821 = 2U % atlasWidth_20;
                int _S1822 = int(_S1821);
                uint _S1823 = 2U / _S1820;
                int3 _S1824 = int3(_S1822, int(_S1823), int(0));
                int _S1825 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1824)).xy), uint(((_S1824)).z))).x * 255.0f)));
                uint _S1826 = atlasWidth_20;
                uint _S1827 = 3U % atlasWidth_20;
                int _S1828 = int(_S1827);
                uint _S1829 = 3U / _S1826;
                int3 _S1830 = int3(_S1828, int(_S1829), int(0));
                int2 minimumTile_20 = int2(_S1825, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1830)).xy), uint(((_S1830)).z))).x * 255.0f))));
                uint _S1831 = atlasWidth_20;
                uint _S1832 = 4U % atlasWidth_20;
                int _S1833 = int(_S1832);
                uint _S1834 = 4U / _S1831;
                int3 _S1835 = int3(_S1833, int(_S1834), int(0));
                uint _S1836 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1835)).xy), uint(((_S1835)).z))).x * 255.0f));
                uint _S1837 = atlasWidth_20;
                uint _S1838 = 5U % atlasWidth_20;
                int _S1839 = int(_S1838);
                uint _S1840 = 5U / _S1837;
                int3 _S1841 = int3(_S1839, int(_S1840), int(0));
                uint _S1842 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1841)).xy), uint(((_S1841)).z))).x * 255.0f));
                uint2 grid_20 = uint2(_S1836, _S1842);
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
                    int3 _S1843 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1843)).xy), uint(((_S1843)).z)));
                    break;
                }
                uint offset_20 = 10U + 2U * (uint(tile_40.y) * _S1836 + uint(tile_40.x));
                uint _S1844 = atlasWidth_20;
                uint _S1845 = offset_20 % atlasWidth_20;
                int _S1846 = int(_S1845);
                uint _S1847 = offset_20 / _S1844;
                int3 _S1848 = int3(_S1846, int(_S1847), int(0));
                uint _S1849 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1848)).xy), uint(((_S1848)).z))).x * 255.0f));
                uint _S1850 = atlasWidth_20;
                uint _S1851 = offset_20 + 1U;
                uint _S1852 = _S1851 % atlasWidth_20;
                int _S1853 = int(_S1852);
                uint _S1854 = _S1851 / _S1850;
                int3 _S1855 = int3(_S1853, int(_S1854), int(0));
                uint slot_40 = _S1849 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1855)).xy), uint(((_S1855)).z))).x * 255.0f)) << 8U);
                if(slot_40 == 0U)
                {
                    int3 _S1856 = int3(int(1), int(0), int(0));
                    _S161 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1856)).xy), uint(((_S1856)).z)));
                    break;
                }
                uint slot_41 = slot_40 - 1U;
                uint _S1857 = atlasWidth_20;
                uint _S1858 = 6U % atlasWidth_20;
                int _S1859 = int(_S1858);
                uint _S1860 = 6U / _S1857;
                int3 _S1861 = int3(_S1859, int(_S1860), int(0));
                uint _S1862 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1861)).xy), uint(((_S1861)).z))).x * 255.0f));
                uint _S1863 = atlasWidth_20;
                uint _S1864 = 7U % atlasWidth_20;
                int _S1865 = int(_S1864);
                uint _S1866 = 7U / _S1863;
                int3 _S1867 = int3(_S1865, int(_S1866), int(0));
                uint columns_20 = _S1862 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1867)).xy), uint(((_S1867)).z))).x * 255.0f)) << 8U);
                uint _S1868 = atlasWidth_20;
                uint _S1869 = 8U % atlasWidth_20;
                int _S1870 = int(_S1869);
                uint _S1871 = 8U / _S1868;
                int3 _S1872 = int3(_S1870, int(_S1871), int(0));
                uint _S1873 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1872)).xy), uint(((_S1872)).z))).x * 255.0f));
                uint _S1874 = atlasWidth_20;
                uint _S1875 = 9U % atlasWidth_20;
                int _S1876 = int(_S1875);
                uint _S1877 = 9U / _S1874;
                int3 _S1878 = int3(_S1876, int(_S1877), int(0));
                uint rows_20 = ((_S1873 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1878)).xy), uint(((_S1878)).z))).x * 255.0f)) << 8U)) + columns_20 - 1U) / columns_20;
                uint metadataRows_20 = (10U + 2U * _S1836 * _S1842 + atlasWidth_20 - 1U) / atlasWidth_20;
                uint _S1879 = atlasWidth_20 / columns_20;
                float _S1880 = float(_S1879);
                uint _S1881 = (atlasHeight_20 - metadataRows_20) / rows_20;
                float2 cellSize_40 = float2(_S1880, float(_S1881));
                uint _S1882 = slot_41 % columns_20;
                float _S1883 = float(_S1882);
                uint _S1884 = slot_41 / columns_20;
                _S161 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1883, float(_S1884)) * cellSize_40 + float2(1.5f, float(metadataRows_20) + 1.5f) + fract(detailSource_0) * max(cellSize_40 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
                break;
            }
            int2 _S1885 = int2(metadata_20.zw);
            int2 tile_41 = int2(floor(detailSource_0)) - int2(metadata_20.xy);
            if(any(tile_41 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_41 >= _S1885);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1886 = int3(int(min(1U, atlasWidth_20 - 1U)), int(0), int(0));
                _S161 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1886)).xy), uint(((_S1886)).z)));
                break;
            }
            uint _S1887 = atlasWidth_20 / uint(_S1885.x);
            float _S1888 = float(_S1887);
            uint _S1889 = (atlasHeight_20 - 1U) / uint(_S1885.y);
            float2 cellSize_41 = float2(_S1888, float(_S1889));
            _S161 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_41) * cellSize_41 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_41 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
            break;
        }
        for(;;)
        {
            float4 _S1890 = float4(_S7->compositeControls_0) ;
            if((_S1890.x) != 2048.0f)
            {
                break;
            }
            bool _S1891 = (_S1890.w) >= 0.5f;
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
            uint _S1892 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1891)
                {
                    for(;;)
                    {
                        if(_S1892 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1893 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_27;
                        thread uint height_27;
                        (*((&width_27)) = (_S1893).get_width(0)),(*((&height_27)) = (_S1893).get_height(0));
                        float2 size_23 = float2(float(width_27), float(height_27));
                        float2 _S1894 = float2(0.5f) ;
                        float2 remapped_23 = clamp((detailSource_0 * (size_23 - float2(2.0f) ) + float2(1.0f) ) / size_23, _S1894 / size_23, (size_23 - _S1894) / size_23);
                        if((_S1892 & 1U) != 0U)
                        {
                            ior_0 = remapped_23.x;
                        }
                        else
                        {
                            ior_0 = detailSource_0.x;
                        }
                        if((_S1892 & 2U) != 0U)
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
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (detailSource_0)));
                    break;
                }
                texture2d<float, access::sample> _S1895 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_21;
                thread uint atlasHeight_21;
                (*((&atlasWidth_21)) = (_S1895).get_width(0)),(*((&atlasHeight_21)) = (_S1895).get_height(0));
                int3 _S1896 = int3(int(0), int(0), int(0));
                float4 metadata_21 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1896)).xy), uint(((_S1896)).z))) * float4(255.0f) );
                if((metadata_21.x) == 255.0f)
                {
                    uint _S1897 = atlasWidth_21;
                    uint _S1898 = 2U % atlasWidth_21;
                    int _S1899 = int(_S1898);
                    uint _S1900 = 2U / _S1897;
                    int3 _S1901 = int3(_S1899, int(_S1900), int(0));
                    int _S1902 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1901)).xy), uint(((_S1901)).z))).x * 255.0f)));
                    uint _S1903 = atlasWidth_21;
                    uint _S1904 = 3U % atlasWidth_21;
                    int _S1905 = int(_S1904);
                    uint _S1906 = 3U / _S1903;
                    int3 _S1907 = int3(_S1905, int(_S1906), int(0));
                    int2 minimumTile_21 = int2(_S1902, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1907)).xy), uint(((_S1907)).z))).x * 255.0f))));
                    uint _S1908 = atlasWidth_21;
                    uint _S1909 = 4U % atlasWidth_21;
                    int _S1910 = int(_S1909);
                    uint _S1911 = 4U / _S1908;
                    int3 _S1912 = int3(_S1910, int(_S1911), int(0));
                    uint _S1913 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1912)).xy), uint(((_S1912)).z))).x * 255.0f));
                    uint _S1914 = atlasWidth_21;
                    uint _S1915 = 5U % atlasWidth_21;
                    int _S1916 = int(_S1915);
                    uint _S1917 = 5U / _S1914;
                    int3 _S1918 = int3(_S1916, int(_S1917), int(0));
                    uint _S1919 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1918)).xy), uint(((_S1918)).z))).x * 255.0f));
                    uint2 grid_21 = uint2(_S1913, _S1919);
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
                        int3 _S1920 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1920)).xy), uint(((_S1920)).z)));
                        break;
                    }
                    uint offset_21 = 10U + 2U * (uint(tile_42.y) * _S1913 + uint(tile_42.x));
                    uint _S1921 = atlasWidth_21;
                    uint _S1922 = offset_21 % atlasWidth_21;
                    int _S1923 = int(_S1922);
                    uint _S1924 = offset_21 / _S1921;
                    int3 _S1925 = int3(_S1923, int(_S1924), int(0));
                    uint _S1926 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1925)).xy), uint(((_S1925)).z))).x * 255.0f));
                    uint _S1927 = atlasWidth_21;
                    uint _S1928 = offset_21 + 1U;
                    uint _S1929 = _S1928 % atlasWidth_21;
                    int _S1930 = int(_S1929);
                    uint _S1931 = _S1928 / _S1927;
                    int3 _S1932 = int3(_S1930, int(_S1931), int(0));
                    uint slot_42 = _S1926 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1932)).xy), uint(((_S1932)).z))).x * 255.0f)) << 8U);
                    if(slot_42 == 0U)
                    {
                        int3 _S1933 = int3(int(1), int(0), int(0));
                        _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1933)).xy), uint(((_S1933)).z)));
                        break;
                    }
                    uint slot_43 = slot_42 - 1U;
                    uint _S1934 = atlasWidth_21;
                    uint _S1935 = 6U % atlasWidth_21;
                    int _S1936 = int(_S1935);
                    uint _S1937 = 6U / _S1934;
                    int3 _S1938 = int3(_S1936, int(_S1937), int(0));
                    uint _S1939 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1938)).xy), uint(((_S1938)).z))).x * 255.0f));
                    uint _S1940 = atlasWidth_21;
                    uint _S1941 = 7U % atlasWidth_21;
                    int _S1942 = int(_S1941);
                    uint _S1943 = 7U / _S1940;
                    int3 _S1944 = int3(_S1942, int(_S1943), int(0));
                    uint columns_21 = _S1939 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1944)).xy), uint(((_S1944)).z))).x * 255.0f)) << 8U);
                    uint _S1945 = atlasWidth_21;
                    uint _S1946 = 8U % atlasWidth_21;
                    int _S1947 = int(_S1946);
                    uint _S1948 = 8U / _S1945;
                    int3 _S1949 = int3(_S1947, int(_S1948), int(0));
                    uint _S1950 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1949)).xy), uint(((_S1949)).z))).x * 255.0f));
                    uint _S1951 = atlasWidth_21;
                    uint _S1952 = 9U % atlasWidth_21;
                    int _S1953 = int(_S1952);
                    uint _S1954 = 9U / _S1951;
                    int3 _S1955 = int3(_S1953, int(_S1954), int(0));
                    uint rows_21 = ((_S1950 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1955)).xy), uint(((_S1955)).z))).x * 255.0f)) << 8U)) + columns_21 - 1U) / columns_21;
                    uint metadataRows_21 = (10U + 2U * _S1913 * _S1919 + atlasWidth_21 - 1U) / atlasWidth_21;
                    uint _S1956 = atlasWidth_21 / columns_21;
                    float _S1957 = float(_S1956);
                    uint _S1958 = (atlasHeight_21 - metadataRows_21) / rows_21;
                    float2 cellSize_42 = float2(_S1957, float(_S1958));
                    uint _S1959 = slot_43 % columns_21;
                    float _S1960 = float(_S1959);
                    uint _S1961 = slot_43 / columns_21;
                    _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1960, float(_S1961)) * cellSize_42 + float2(1.5f, float(metadataRows_21) + 1.5f) + fract(detailSource_0) * max(cellSize_42 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                    break;
                }
                int2 _S1962 = int2(metadata_21.zw);
                int2 tile_43 = int2(floor(detailSource_0)) - int2(metadata_21.xy);
                if(any(tile_43 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_43 >= _S1962);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1963 = int3(int(min(1U, atlasWidth_21 - 1U)), int(0), int(0));
                    _S560 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1963)).xy), uint(((_S1963)).z)));
                    break;
                }
                uint _S1964 = atlasWidth_21 / uint(_S1962.x);
                float _S1965 = float(_S1964);
                uint _S1966 = (atlasHeight_21 - 1U) / uint(_S1962.y);
                float2 cellSize_43 = float2(_S1965, float(_S1966));
                _S560 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_43) * cellSize_43 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_43 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                break;
            }
            uint operation_9 = uint(round(_S1890.y));
            if(operation_9 == 1U)
            {
                _S161 = _S161 * _S560;
                break;
            }
            if(operation_9 == 2U)
            {
                _S161 = _S161 + _S560;
                break;
            }
            if(operation_9 == 3U)
            {
                _S161 = _S161 - _S560;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1890.z;
                _S161 = _S161 * float4((1.0f - factor_9))  + _S560 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S161.x;
    }
    else
    {
        ior_0 = _S140;
    }
    float opacityThreshold_0 = _S135.z;
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
    float _S1967 = max(0.00100000004749745f, roughness_0);
    float _S1968 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1969 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1969;
    bool omniPbrSpecular_0 = (_S143.w) >= 0.5f;
    float dielectricLevel_0 = _S129.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1970 = float3(dielectricLevel_0) ;
        float3 _S1971 = float3(metallic_0) ;
        float3 _S1972 = mix(_S1970, diffuseColor_0, _S1971);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1970, diffuseColor_0, _S1971);
        grazingIncidence_0 = _S1972;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S135.w) >= 0.5f)
        {
            float3 _S1973 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1973;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1974 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1974);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1974);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1975 = _S2;
    float _S1976 = _S2.ambientLight_0.w;
    if(_S1976 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1977 = _S1975.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1977, _S1977)) > 0.0f;
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
    uint _S1978 = min(uint(_S1976), 128U);
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
    float4 _S1979 = float4(_S7->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S1979.w) ;
    uint domeLinkMask_0 = uint(max(_S145.x, 0.0f));
    FragmentFrameParameters_0 _S1980 = _S2;
    uint _S1981 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S1981) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S1982 = _S1975.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S1983 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S1983;
        for(;;)
        {
            if(lightCount_0 < _S1981)
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
        domeAmbient_1 = _S1982;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S1984 = !hasSceneLighting_0;
    if(_S1984)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1978;
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
        bool _S1985;
        if(hasSceneLighting_0)
        {
            _S1985 = (((uint4(_S7->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S1985 = false;
        }
        if(_S1985)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S1986 = lightIndex_0 == 0U;
        bool _S1987;
        if(_S1986)
        {
            _S1987 = _S1984;
        }
        else
        {
            _S1987 = false;
        }
        if(_S1987)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S1988 = 224U + lightIndex_0 * 16U;
            float _S1989 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1988)>>2]);
            float _S1990 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1988 + 4U)>>2]);
            float _S1991 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1988 + 8U)>>2]);
            float _S1992 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1988 + 12U)>>2]);
            lightType_0 = _S1992;
        }
        bool _S1993;
        if(_S1986)
        {
            _S1993 = _S1984;
        }
        else
        {
            _S1993 = false;
        }
        if(_S1993)
        {
            lightDirection_0 = normalize((float4(_S7->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S1994 = 2272U + lightIndex_0 * 16U;
            float _S1995 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1994)>>2]);
            float _S1996 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1994 + 4U)>>2]);
            float _S1997 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1994 + 8U)>>2]);
            float _S1998 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1994 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S1995, _S1996, _S1997));
        }
        bool _S1999;
        if(_S1986)
        {
            _S1999 = _S1984;
        }
        else
        {
            _S1999 = false;
        }
        if(_S1999)
        {
            roughness_0 = (float4(_S7->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S2000 = 4320U + lightIndex_0 * 16U;
            float _S2001 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2000)>>2]);
            float _S2002 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2000 + 4U)>>2]);
            float _S2003 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2000 + 8U)>>2]);
            float _S2004 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2000 + 12U)>>2]);
            roughness_0 = _S2004;
        }
        bool _S2005;
        if(_S1986)
        {
            _S2005 = _S1984;
        }
        else
        {
            _S2005 = false;
        }
        if(_S2005)
        {
            diffuseColor_0 = _S1979.xyz;
        }
        else
        {
            uint _S2006 = 4320U + lightIndex_0 * 16U;
            float _S2007 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2006)>>2]);
            float _S2008 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2006 + 4U)>>2]);
            float _S2009 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2006 + 8U)>>2]);
            float _S2010 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2006 + 12U)>>2]);
            diffuseColor_0 = float3(_S2007, _S2008, _S2009);
        }
        bool _S2011;
        if(_S1986)
        {
            _S2011 = _S1984;
        }
        else
        {
            _S2011 = false;
        }
        if(_S2011)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S2012 = 6368U + lightIndex_0 * 16U;
            float _S2013 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2012)>>2]);
            float _S2014 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2012 + 4U)>>2]);
            float _S2015 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2012 + 8U)>>2]);
            float _S2016 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2012 + 12U)>>2]);
            metallic_0 = _S2013;
        }
        bool _S2017;
        if(_S1986)
        {
            _S2017 = _S1984;
        }
        else
        {
            _S2017 = false;
        }
        if(_S2017)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S2018 = 6368U + lightIndex_0 * 16U;
            float _S2019 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2018)>>2]);
            float _S2020 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2018 + 4U)>>2]);
            float _S2021 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2018 + 8U)>>2]);
            float _S2022 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2018 + 12U)>>2]);
            clearcoatRoughness_0 = _S2020;
        }
        bool _S2023;
        if(_S1986)
        {
            _S2023 = _S1984;
        }
        else
        {
            _S2023 = false;
        }
        if(_S2023)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S2024 = 8416U + lightIndex_0 * 16U;
            float _S2025 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2024)>>2]);
            float _S2026 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2024 + 4U)>>2]);
            float _S2027 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2024 + 8U)>>2]);
            float _S2028 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2024 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S2025, _S2026, _S2027));
        }
        bool _S2029;
        if(_S1986)
        {
            _S2029 = _S1984;
        }
        else
        {
            _S2029 = false;
        }
        if(_S2029)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S2030 = 10464U + lightIndex_0 * 16U;
            float _S2031 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2030)>>2]);
            float _S2032 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2030 + 4U)>>2]);
            float _S2033 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2030 + 8U)>>2]);
            float _S2034 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2030 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S2031, _S2032, _S2033));
        }
        bool _S2035;
        if(_S1986)
        {
            _S2035 = _S1984;
        }
        else
        {
            _S2035 = false;
        }
        float shapeX_0;
        if(_S2035)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S2036 = 8416U + lightIndex_0 * 16U;
            float _S2037 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2036)>>2]);
            float _S2038 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2036 + 4U)>>2]);
            float _S2039 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2036 + 8U)>>2]);
            float _S2040 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2036 + 12U)>>2]);
            shapeX_0 = _S2040;
        }
        bool _S2041;
        if(_S1986)
        {
            _S2041 = _S1984;
        }
        else
        {
            _S2041 = false;
        }
        float shapeY_0;
        if(_S2041)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S2042 = 10464U + lightIndex_0 * 16U;
            float _S2043 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2042)>>2]);
            float _S2044 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2042 + 4U)>>2]);
            float _S2045 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2042 + 8U)>>2]);
            float _S2046 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2042 + 12U)>>2]);
            shapeY_0 = _S2046;
        }
        bool _S2047;
        if(_S1986)
        {
            _S2047 = _S1984;
        }
        else
        {
            _S2047 = false;
        }
        float lightRadius_0;
        if(_S2047)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S2048 = 2272U + lightIndex_0 * 16U;
            float _S2049 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2048)>>2]);
            float _S2050 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2048 + 4U)>>2]);
            float _S2051 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2048 + 8U)>>2]);
            float _S2052 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2048 + 12U)>>2]);
            lightRadius_0 = _S2052;
        }
        bool _S2053;
        if(_S1986)
        {
            _S2053 = _S1984;
        }
        else
        {
            _S2053 = false;
        }
        if(_S2053)
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
        float _S2054 = saturate(abs(dot(directNormal_0, lightEyeBase_0)) + 0.00000999999974738f);
        bool _S2055;
        if(_S1986)
        {
            _S2055 = _S1984;
        }
        else
        {
            _S2055 = false;
        }
        if(_S2055)
        {
            _S161 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S2056 = 17216U + lightIndex_0 * 16U;
            float _S2057 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2056)>>2]);
            float _S2058 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2056 + 4U)>>2]);
            float _S2059 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2056 + 8U)>>2]);
            float _S2060 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2056 + 12U)>>2]);
            _S161 = float4(_S2057, _S2058, _S2059, _S2060);
        }
        float3 color_3;
        float3 sampleDirection_0;
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S2061 = lightIndex_0 * 16U;
            uint _S2062 = 14880U + _S2061;
            float _S2063 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2062)>>2]);
            float _S2064 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2062 + 4U)>>2]);
            float _S2065 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2062 + 8U)>>2]);
            float _S2066 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2062 + 12U)>>2]);
            int shadowSlot_0 = int(_S2063);
            if(shadowSlot_0 >= int(0))
            {
                int shadowSlot_1;
                int y_0;
                if(_S2064 == 6.0f)
                {
                    shadowVisibility_0 = -1.0f;
                    shadowSlot_1 = shadowSlot_0;
                    y_0 = int(0);
                    for(;;)
                    {
                        if(y_0 < int(6))
                        {
                        }
                        else
                        {
                            break;
                        }
                        int _S2067 = shadowSlot_0 + y_0;
                        uint _S2068 = 12576U + uint(_S2067) * 64U;
                        float _S2069 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068)>>2]);
                        float _S2070 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 4U)>>2]);
                        float _S2071 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 8U)>>2]);
                        float _S2072 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 12U)>>2]);
                        float4 _S2073 = float4(_S2069, _S2070, _S2071, _S2072);
                        float _S2074 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 16U)>>2]);
                        float _S2075 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 20U)>>2]);
                        float _S2076 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 24U)>>2]);
                        float _S2077 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 28U)>>2]);
                        float4 _S2078 = float4(_S2074, _S2075, _S2076, _S2077);
                        float _S2079 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 32U)>>2]);
                        float _S2080 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 36U)>>2]);
                        float _S2081 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 40U)>>2]);
                        float _S2082 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 44U)>>2]);
                        float4 _S2083 = float4(_S2079, _S2080, _S2081, _S2082);
                        float _S2084 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 48U)>>2]);
                        float _S2085 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 52U)>>2]);
                        float _S2086 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 56U)>>2]);
                        float _S2087 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2068 + 60U)>>2]);
                        float _S2088 = (((float4(worldPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S2073, _S2078, _S2083, float4(_S2084, _S2085, _S2086, _S2087))))).w;
                        if(_S2088 > shadowVisibility_0)
                        {
                            shadowVisibility_0 = _S2088;
                            shadowSlot_1 = _S2067;
                        }
                        y_0 = y_0 + int(1);
                    }
                }
                else
                {
                    shadowSlot_1 = shadowSlot_0;
                }
                if(lightType_0 == 2.0f)
                {
                    uint _S2089 = 224U + _S2061;
                    float _S2090 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2089)>>2]);
                    float _S2091 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2089 + 4U)>>2]);
                    float _S2092 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2089 + 8U)>>2]);
                    float _S2093 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2089 + 12U)>>2]);
                    float3 toShadowLight_0 = float3(_S2090, _S2091, _S2092) - worldPosition_0;
                    color_3 = toShadowLight_0 * float3(rsqrt(max(dot(toShadowLight_0, toShadowLight_0), 0.00100000004749745f))) ;
                }
                else
                {
                    color_3 = lightDirection_0;
                }
                for(;;)
                {
                    uint _S2094 = uint(shadowSlot_1);
                    uint _S2095 = _S2094 * 16U;
                    uint _S2096 = 14496U + _S2095;
                    float _S2097 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2096)>>2]);
                    float _S2098 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2096 + 4U)>>2]);
                    float _S2099 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2096 + 8U)>>2]);
                    float _S2100 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2096 + 12U)>>2]);
                    uint _S2101 = 14112U + _S2095;
                    float _S2102 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2101)>>2]);
                    float _S2103 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2101 + 4U)>>2]);
                    float _S2104 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2101 + 8U)>>2]);
                    float _S2105 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2101 + 12U)>>2]);
                    if((dot(specularColor_0, color_3)) < 0.0f)
                    {
                        sampleDirection_0 = - specularColor_0;
                    }
                    else
                    {
                        sampleDirection_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(sampleDirection_0, color_3)), 0.0f, 1.0f);
                    float3 offsetPosition_0 = worldPosition_0 + sampleDirection_0 * float3((_S2098 * slope_0)) ;
                    uint _S2106 = 12576U + _S2094 * 64U;
                    float _S2107 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106)>>2]);
                    float _S2108 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 4U)>>2]);
                    float _S2109 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 8U)>>2]);
                    float _S2110 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 12U)>>2]);
                    float4 _S2111 = float4(_S2107, _S2108, _S2109, _S2110);
                    float _S2112 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 16U)>>2]);
                    float _S2113 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 20U)>>2]);
                    float _S2114 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 24U)>>2]);
                    float _S2115 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 28U)>>2]);
                    float4 _S2116 = float4(_S2112, _S2113, _S2114, _S2115);
                    float _S2117 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 32U)>>2]);
                    float _S2118 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 36U)>>2]);
                    float _S2119 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 40U)>>2]);
                    float _S2120 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 44U)>>2]);
                    float4 _S2121 = float4(_S2117, _S2118, _S2119, _S2120);
                    float _S2122 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 48U)>>2]);
                    float _S2123 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 52U)>>2]);
                    float _S2124 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 56U)>>2]);
                    float _S2125 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2106 + 60U)>>2]);
                    float4 lightClip_0 = (((float4(offsetPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S2111, _S2116, _S2121, float4(_S2122, _S2123, _S2124, _S2125)))));
                    float _S2126 = lightClip_0.w;
                    if(_S2126 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S2126) ;
                    bool _S2127;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S2127 = true;
                    }
                    else
                    {
                        _S2127 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S2128;
                    if(_S2127)
                    {
                        _S2128 = true;
                    }
                    else
                    {
                        _S2128 = (ndc_0.z) < 0.0f;
                    }
                    bool _S2129;
                    if(_S2128)
                    {
                        _S2129 = true;
                    }
                    else
                    {
                        _S2129 = (ndc_0.z) > 1.0f;
                    }
                    if(_S2129)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S2130 = float2(_S2102, _S2103);
                    float2 _S2131 = float2(_S2104, _S2105);
                    float2 _S2132 = _S2130 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S2131;
                    float _S2133 = max(_S2099, 0.0f);
                    float _S2134 = ndc_0.z - _S2097 * (1.0f + 2.0f * slope_0);
                    float2 _S2135 = float2((_S2100 * 0.5f)) ;
                    float2 _S2136 = _S2130 + _S2135;
                    float2 _S2137 = _S2130 + _S2131 - _S2135;
                    y_0 = int(-1);
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
                            if(_S2134 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S2132 + float2(float(x_0), float(y_0)) * float2((_S2133 * _S2100)) , _S2136, _S2137)), level((0.0f))).x))
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
        float3 _S2138 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S2138;
        sampleOffsets_0[int(1)] = _S2138;
        sampleOffsets_0[int(2)] = _S2138;
        sampleOffsets_0[int(3)] = _S2138;
        sampleOffsets_0[int(4)] = _S2138;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S2139 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S2139 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S2139 - halfHeight_0;
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
            float sampleIntensity_1;
            float emissionScale_0;
            if(lightType_0 >= 2.0f)
            {
                uint _S2140 = 224U + lightIndex_0 * 16U;
                float _S2141 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2140)>>2]);
                float _S2142 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2140 + 4U)>>2]);
                float _S2143 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2140 + 8U)>>2]);
                float _S2144 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S2140 + 12U)>>2]);
                float3 toLight_0 = float3(_S2141, _S2142, _S2143) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S2145 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S2145)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S2145;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S161.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S161.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S2146 = _S161.y;
                        if(_S2146 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S2146);
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
            float3 half_0 = normalize(sampleDirection_0 + lightEyeBase_0);
            float normalDotLight_0 = saturate(dot(directNormal_0, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(directNormal_0, half_0));
            float _S2147 = pow(max(0.0f, 1.0f - saturate(dot(lightEyeBase_0, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S2147)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S2147) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S2148 = float3(_S2147) ;
            float _S2149 = max(_S1967, 0.00100000004749745f);
            float alpha_0 = _S2149 * _S2149;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S2150 = 1.0f - k_0;
            float3 _S2151 = float3(max(4.0f * normalDotLight_0 * _S2054, 1.00000000317107685e-30f)) ;
            float3 _S2152 = mix(normalIncidence_0, grazingIncidence_0, _S2148) * float3((_S2054 / (_S2054 * _S2150 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S2150 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S2151;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S2153 = max(_S1968, 0.00100000004749745f);
                float alpha_1 = _S2153 * _S2153;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S2154 = 1.0f - k_1;
                directSpecular_0 = _S2152 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S2148) * float3((_S2054 / (_S2054 * _S2154 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S2154 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S2151);
            }
            else
            {
                directSpecular_0 = _S2152;
            }
            float3 _S2155 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S2155 * _S1969) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S2155);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S2156 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S2157 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S2158 = saturate(_S1967);
        float2 _S2159 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2157, _S2158)), level((0.0f)))).xy;
        float _S2160 = _S2159.x;
        float _S2161 = _S2159.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S2160)  + grazingIncidence_0 * float3(_S2161) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S2160 + _S2161)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S2162 = max(_S1980.domeControls_0.y, 1.0f);
        float3 _S2163 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S2162 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1980.domeControls_0.z;
            float _S2164 = _S1980.domeControls_0.w;
            for(;;)
            {
                bool _S2165 = _S2162 <= 1.0f;
                _S5 = _S2165;
                if(_S2165)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S2164, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S2162)), level((0.0f)))).xyz;
                break;
            }
            float _S2166 = _S2156.environmentControls_0.y;
            float _S2167 = _S2156.environmentControls_0.z;
            for(;;)
            {
                if(_S5)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2168 = max(_S2166, 1.0f);
                    float _S2169 = _S2168 - 1.0f;
                    float slice_0 = _S2158 * max(_S2169, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S2167, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S2168)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S2169) + v_0) / _S2168)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S2170 = max(_S2166, 1.0f);
                float total_0 = _S2170 * _S2162;
                float _S2171 = _S2170 - 1.0f;
                float slice_1 = _S2158 * max(_S2171, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S2167, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S2170;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S2171) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S2172 = max(_S2166, 1.0f);
                        float _S2173 = _S2172 - 1.0f;
                        float slice_2 = saturate(_S1968) * max(_S2173, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S2167, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S2172)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S2173) + v_2) / _S2172)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2174 = max(_S2166, 1.0f);
                    float total_1 = _S2174 * _S2162;
                    float _S2175 = _S2174 - 1.0f;
                    float slice_3 = saturate(_S1968) * max(_S2175, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S2167, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S2174;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S2175) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightBitangent_0 = specularColor_0;
            }
            else
            {
                lightBitangent_0 = _S2163;
            }
            lightDirection_0 = diffuseColor_0;
            lightTangent_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightDirection_0 = _S2163;
            lightTangent_0 = _S2163;
            lightBitangent_0 = _S2163;
            for(;;)
            {
                if(sampleIndex_0 < _S1981)
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
                float _S2176 = _S1980.domeControls_0.w;
                for(;;)
                {
                    bool _S2177 = _S2162 <= 1.0f;
                    _S6 = _S2177;
                    if(_S2177)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S2176, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S2162)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightDirection_0 + diffuseColor_0;
                float _S2178 = _S2156.environmentControls_0.y;
                float _S2179 = _S2156.environmentControls_0.z;
                for(;;)
                {
                    if(_S6)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2180 = max(_S2178, 1.0f);
                        float _S2181 = _S2180 - 1.0f;
                        float slice_4 = _S2158 * max(_S2181, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S2179, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S2180)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S2181) + v_4) / _S2180)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S2182 = max(_S2178, 1.0f);
                    float total_2 = _S2182 * _S2162;
                    float _S2183 = _S2182 - 1.0f;
                    float slice_5 = _S2158 * max(_S2183, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S2179, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S2182;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S2183) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightTangent_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S6)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_7 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S2184 = max(_S2178, 1.0f);
                            float _S2185 = _S2184 - 1.0f;
                            float slice_6 = saturate(_S1968) * max(_S2185, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S2179, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S2184)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S2185) + v_6) / _S2184)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S2186 = max(_S2178, 1.0f);
                        float total_3 = _S2186 * _S2162;
                        float _S2187 = _S2186 - 1.0f;
                        float slice_7 = saturate(_S1968) * max(_S2187, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S2179, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S2186;
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S2187) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    lightEyeBase_0 = lightBitangent_0 + specularColor_0;
                }
                else
                {
                    lightEyeBase_0 = lightBitangent_0;
                }
                lightDirection_0 = irradiance_0;
                lightTangent_0 = prefiltered_0;
                lightBitangent_0 = lightEyeBase_0;
                sampleIndex_0 = sampleIndex_0 + 1U;
            }
        }
        float3 color_4 = color_2 + float3(occlusion_0)  * diffuse_1 * lightDirection_0 * diffuseWeight_0 + float3(occlusion_0)  * lightTangent_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S2188 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S2157, saturate(_S1968))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightBitangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S2188.x)  + float3(_S2188.y) );
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
    pixelOutput_0 _S2189 = { float4(color_2, opacity_0) };
    return _S2189;
}
