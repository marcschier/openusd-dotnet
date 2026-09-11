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
};

struct KernelContext_0
{
    SurfaceParameters_natural_0 device* surfaceParameters_0;
    uint32_t device* frameParameters_0;
    texture2d<float, access::sample> baseColorTexture_0;
    sampler baseColorSampler_0;
    texture2d<float, access::sample> compositeTexture_0;
    sampler compositeSampler_0;
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

[[fragment]] pixelOutput_0 fragmentMain_uv_material(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    bool _S2;
    bool _S3;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_0)->baseColorSampler_0 = baseColorSampler_1;
    (&kernelContext_0)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_0)->compositeSampler_0 = compositeSampler_1;
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
    SurfaceParameters_natural_0 device* _S4 = surfaceParameters_1+int(0);
    float _S5 = as_type<float>(frameParameters_1[(0U)>>2]);
    float _S6 = as_type<float>((&kernelContext_0)->frameParameters_0[(4U)>>2]);
    float _S7 = as_type<float>((&kernelContext_0)->frameParameters_0[(8U)>>2]);
    float _S8 = as_type<float>((&kernelContext_0)->frameParameters_0[(12U)>>2]);
    float _S9 = as_type<float>((&kernelContext_0)->frameParameters_0[(16U)>>2]);
    float _S10 = as_type<float>((&kernelContext_0)->frameParameters_0[(20U)>>2]);
    float _S11 = as_type<float>((&kernelContext_0)->frameParameters_0[(24U)>>2]);
    float _S12 = as_type<float>((&kernelContext_0)->frameParameters_0[(28U)>>2]);
    float _S13 = as_type<float>((&kernelContext_0)->frameParameters_0[(32U)>>2]);
    float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(36U)>>2]);
    float _S15 = as_type<float>((&kernelContext_0)->frameParameters_0[(40U)>>2]);
    float _S16 = as_type<float>((&kernelContext_0)->frameParameters_0[(44U)>>2]);
    float _S17 = as_type<float>((&kernelContext_0)->frameParameters_0[(48U)>>2]);
    float _S18 = as_type<float>((&kernelContext_0)->frameParameters_0[(52U)>>2]);
    float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(56U)>>2]);
    float _S20 = as_type<float>((&kernelContext_0)->frameParameters_0[(60U)>>2]);
    uint _S21 = as_type<uint>((&kernelContext_0)->frameParameters_0[(64U)>>2]);
    uint _S22 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
    uint _S23 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
    uint _S24 = as_type<uint>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
    float _S25 = as_type<float>((&kernelContext_0)->frameParameters_0[(80U)>>2]);
    float _S26 = as_type<float>((&kernelContext_0)->frameParameters_0[(84U)>>2]);
    float _S27 = as_type<float>((&kernelContext_0)->frameParameters_0[(88U)>>2]);
    float _S28 = as_type<float>((&kernelContext_0)->frameParameters_0[(92U)>>2]);
    float4 _S29 = float4(_S25, _S26, _S27, _S28);
    float _S30 = as_type<float>((&kernelContext_0)->frameParameters_0[(96U)>>2]);
    float _S31 = as_type<float>((&kernelContext_0)->frameParameters_0[(100U)>>2]);
    float _S32 = as_type<float>((&kernelContext_0)->frameParameters_0[(104U)>>2]);
    float _S33 = as_type<float>((&kernelContext_0)->frameParameters_0[(108U)>>2]);
    float4 _S34 = float4(_S30, _S31, _S32, _S33);
    float _S35 = as_type<float>((&kernelContext_0)->frameParameters_0[(112U)>>2]);
    float _S36 = as_type<float>((&kernelContext_0)->frameParameters_0[(116U)>>2]);
    float _S37 = as_type<float>((&kernelContext_0)->frameParameters_0[(120U)>>2]);
    float _S38 = as_type<float>((&kernelContext_0)->frameParameters_0[(124U)>>2]);
    float4 _S39 = float4(_S35, _S36, _S37, _S38);
    float _S40 = as_type<float>((&kernelContext_0)->frameParameters_0[(128U)>>2]);
    float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(132U)>>2]);
    float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(136U)>>2]);
    float _S43 = as_type<float>((&kernelContext_0)->frameParameters_0[(140U)>>2]);
    float4 _S44 = float4(_S40, _S41, _S42, _S43);
    float _S45 = as_type<float>((&kernelContext_0)->frameParameters_0[(144U)>>2]);
    float _S46 = as_type<float>((&kernelContext_0)->frameParameters_0[(148U)>>2]);
    float _S47 = as_type<float>((&kernelContext_0)->frameParameters_0[(152U)>>2]);
    float _S48 = as_type<float>((&kernelContext_0)->frameParameters_0[(156U)>>2]);
    float4 _S49 = float4(_S45, _S46, _S47, _S48);
    float _S50 = as_type<float>((&kernelContext_0)->frameParameters_0[(160U)>>2]);
    float _S51 = as_type<float>((&kernelContext_0)->frameParameters_0[(164U)>>2]);
    float _S52 = as_type<float>((&kernelContext_0)->frameParameters_0[(168U)>>2]);
    float _S53 = as_type<float>((&kernelContext_0)->frameParameters_0[(172U)>>2]);
    float4 _S54 = float4(_S50, _S51, _S52, _S53);
    float _S55 = as_type<float>((&kernelContext_0)->frameParameters_0[(176U)>>2]);
    float _S56 = as_type<float>((&kernelContext_0)->frameParameters_0[(180U)>>2]);
    float _S57 = as_type<float>((&kernelContext_0)->frameParameters_0[(184U)>>2]);
    float _S58 = as_type<float>((&kernelContext_0)->frameParameters_0[(188U)>>2]);
    float4 _S59 = float4(_S55, _S56, _S57, _S58);
    float _S60 = as_type<float>((&kernelContext_0)->frameParameters_0[(192U)>>2]);
    float _S61 = as_type<float>((&kernelContext_0)->frameParameters_0[(196U)>>2]);
    float _S62 = as_type<float>((&kernelContext_0)->frameParameters_0[(200U)>>2]);
    float _S63 = as_type<float>((&kernelContext_0)->frameParameters_0[(204U)>>2]);
    array<float4, int(8)> _S64 = { _S29, _S34, _S39, _S44, _S49, _S54, _S59, float4(_S60, _S61, _S62, _S63) };
    float _S65 = as_type<float>((&kernelContext_0)->frameParameters_0[(208U)>>2]);
    float _S66 = as_type<float>((&kernelContext_0)->frameParameters_0[(212U)>>2]);
    float _S67 = as_type<float>((&kernelContext_0)->frameParameters_0[(216U)>>2]);
    float _S68 = as_type<float>((&kernelContext_0)->frameParameters_0[(220U)>>2]);
    float _S69 = as_type<float>((&kernelContext_0)->frameParameters_0[(224U)>>2]);
    float _S70 = as_type<float>((&kernelContext_0)->frameParameters_0[(228U)>>2]);
    float _S71 = as_type<float>((&kernelContext_0)->frameParameters_0[(232U)>>2]);
    float _S72 = as_type<float>((&kernelContext_0)->frameParameters_0[(236U)>>2]);
    float4 _S73 = float4(_S69, _S70, _S71, _S72);
    float _S74 = as_type<float>((&kernelContext_0)->frameParameters_0[(240U)>>2]);
    float _S75 = as_type<float>((&kernelContext_0)->frameParameters_0[(244U)>>2]);
    float _S76 = as_type<float>((&kernelContext_0)->frameParameters_0[(248U)>>2]);
    float _S77 = as_type<float>((&kernelContext_0)->frameParameters_0[(252U)>>2]);
    float4 _S78 = float4(_S74, _S75, _S76, _S77);
    float _S79 = as_type<float>((&kernelContext_0)->frameParameters_0[(256U)>>2]);
    float _S80 = as_type<float>((&kernelContext_0)->frameParameters_0[(260U)>>2]);
    float _S81 = as_type<float>((&kernelContext_0)->frameParameters_0[(264U)>>2]);
    float _S82 = as_type<float>((&kernelContext_0)->frameParameters_0[(268U)>>2]);
    float4 _S83 = float4(_S79, _S80, _S81, _S82);
    float _S84 = as_type<float>((&kernelContext_0)->frameParameters_0[(272U)>>2]);
    float _S85 = as_type<float>((&kernelContext_0)->frameParameters_0[(276U)>>2]);
    float _S86 = as_type<float>((&kernelContext_0)->frameParameters_0[(280U)>>2]);
    float _S87 = as_type<float>((&kernelContext_0)->frameParameters_0[(284U)>>2]);
    float4 _S88 = float4(_S84, _S85, _S86, _S87);
    float _S89 = as_type<float>((&kernelContext_0)->frameParameters_0[(288U)>>2]);
    float _S90 = as_type<float>((&kernelContext_0)->frameParameters_0[(292U)>>2]);
    float _S91 = as_type<float>((&kernelContext_0)->frameParameters_0[(296U)>>2]);
    float _S92 = as_type<float>((&kernelContext_0)->frameParameters_0[(300U)>>2]);
    float4 _S93 = float4(_S89, _S90, _S91, _S92);
    float _S94 = as_type<float>((&kernelContext_0)->frameParameters_0[(304U)>>2]);
    float _S95 = as_type<float>((&kernelContext_0)->frameParameters_0[(308U)>>2]);
    float _S96 = as_type<float>((&kernelContext_0)->frameParameters_0[(312U)>>2]);
    float _S97 = as_type<float>((&kernelContext_0)->frameParameters_0[(316U)>>2]);
    float4 _S98 = float4(_S94, _S95, _S96, _S97);
    float _S99 = as_type<float>((&kernelContext_0)->frameParameters_0[(320U)>>2]);
    float _S100 = as_type<float>((&kernelContext_0)->frameParameters_0[(324U)>>2]);
    float _S101 = as_type<float>((&kernelContext_0)->frameParameters_0[(328U)>>2]);
    float _S102 = as_type<float>((&kernelContext_0)->frameParameters_0[(332U)>>2]);
    float4 _S103 = float4(_S99, _S100, _S101, _S102);
    float _S104 = as_type<float>((&kernelContext_0)->frameParameters_0[(336U)>>2]);
    float _S105 = as_type<float>((&kernelContext_0)->frameParameters_0[(340U)>>2]);
    float _S106 = as_type<float>((&kernelContext_0)->frameParameters_0[(344U)>>2]);
    float _S107 = as_type<float>((&kernelContext_0)->frameParameters_0[(348U)>>2]);
    float4 _S108 = float4(_S104, _S105, _S106, _S107);
    float _S109 = as_type<float>((&kernelContext_0)->frameParameters_0[(352U)>>2]);
    float _S110 = as_type<float>((&kernelContext_0)->frameParameters_0[(356U)>>2]);
    float _S111 = as_type<float>((&kernelContext_0)->frameParameters_0[(360U)>>2]);
    float _S112 = as_type<float>((&kernelContext_0)->frameParameters_0[(364U)>>2]);
    float4 _S113 = float4(_S109, _S110, _S111, _S112);
    float _S114 = as_type<float>((&kernelContext_0)->frameParameters_0[(368U)>>2]);
    float _S115 = as_type<float>((&kernelContext_0)->frameParameters_0[(372U)>>2]);
    float _S116 = as_type<float>((&kernelContext_0)->frameParameters_0[(376U)>>2]);
    float _S117 = as_type<float>((&kernelContext_0)->frameParameters_0[(380U)>>2]);
    float4 _S118 = float4(_S114, _S115, _S116, _S117);
    float _S119 = as_type<float>((&kernelContext_0)->frameParameters_0[(384U)>>2]);
    float _S120 = as_type<float>((&kernelContext_0)->frameParameters_0[(388U)>>2]);
    float _S121 = as_type<float>((&kernelContext_0)->frameParameters_0[(392U)>>2]);
    float _S122 = as_type<float>((&kernelContext_0)->frameParameters_0[(396U)>>2]);
    float4 _S123 = float4(_S119, _S120, _S121, _S122);
    float _S124 = as_type<float>((&kernelContext_0)->frameParameters_0[(400U)>>2]);
    float _S125 = as_type<float>((&kernelContext_0)->frameParameters_0[(404U)>>2]);
    float _S126 = as_type<float>((&kernelContext_0)->frameParameters_0[(408U)>>2]);
    float _S127 = as_type<float>((&kernelContext_0)->frameParameters_0[(412U)>>2]);
    float4 _S128 = float4(_S124, _S125, _S126, _S127);
    float _S129 = as_type<float>((&kernelContext_0)->frameParameters_0[(416U)>>2]);
    float _S130 = as_type<float>((&kernelContext_0)->frameParameters_0[(420U)>>2]);
    float _S131 = as_type<float>((&kernelContext_0)->frameParameters_0[(424U)>>2]);
    float _S132 = as_type<float>((&kernelContext_0)->frameParameters_0[(428U)>>2]);
    float4 _S133 = float4(_S129, _S130, _S131, _S132);
    float _S134 = as_type<float>((&kernelContext_0)->frameParameters_0[(432U)>>2]);
    float _S135 = as_type<float>((&kernelContext_0)->frameParameters_0[(436U)>>2]);
    float _S136 = as_type<float>((&kernelContext_0)->frameParameters_0[(440U)>>2]);
    float _S137 = as_type<float>((&kernelContext_0)->frameParameters_0[(444U)>>2]);
    float4 _S138 = float4(_S134, _S135, _S136, _S137);
    float _S139 = as_type<float>((&kernelContext_0)->frameParameters_0[(448U)>>2]);
    float _S140 = as_type<float>((&kernelContext_0)->frameParameters_0[(452U)>>2]);
    float _S141 = as_type<float>((&kernelContext_0)->frameParameters_0[(456U)>>2]);
    float _S142 = as_type<float>((&kernelContext_0)->frameParameters_0[(460U)>>2]);
    float4 _S143 = float4(_S139, _S140, _S141, _S142);
    float _S144 = as_type<float>((&kernelContext_0)->frameParameters_0[(464U)>>2]);
    float _S145 = as_type<float>((&kernelContext_0)->frameParameters_0[(468U)>>2]);
    float _S146 = as_type<float>((&kernelContext_0)->frameParameters_0[(472U)>>2]);
    float _S147 = as_type<float>((&kernelContext_0)->frameParameters_0[(476U)>>2]);
    float4 _S148 = float4(_S144, _S145, _S146, _S147);
    float _S149 = as_type<float>((&kernelContext_0)->frameParameters_0[(480U)>>2]);
    float _S150 = as_type<float>((&kernelContext_0)->frameParameters_0[(484U)>>2]);
    float _S151 = as_type<float>((&kernelContext_0)->frameParameters_0[(488U)>>2]);
    float _S152 = as_type<float>((&kernelContext_0)->frameParameters_0[(492U)>>2]);
    float4 _S153 = float4(_S149, _S150, _S151, _S152);
    float _S154 = as_type<float>((&kernelContext_0)->frameParameters_0[(496U)>>2]);
    float _S155 = as_type<float>((&kernelContext_0)->frameParameters_0[(500U)>>2]);
    float _S156 = as_type<float>((&kernelContext_0)->frameParameters_0[(504U)>>2]);
    float _S157 = as_type<float>((&kernelContext_0)->frameParameters_0[(508U)>>2]);
    float4 _S158 = float4(_S154, _S155, _S156, _S157);
    float _S159 = as_type<float>((&kernelContext_0)->frameParameters_0[(512U)>>2]);
    float _S160 = as_type<float>((&kernelContext_0)->frameParameters_0[(516U)>>2]);
    float _S161 = as_type<float>((&kernelContext_0)->frameParameters_0[(520U)>>2]);
    float _S162 = as_type<float>((&kernelContext_0)->frameParameters_0[(524U)>>2]);
    float4 _S163 = float4(_S159, _S160, _S161, _S162);
    float _S164 = as_type<float>((&kernelContext_0)->frameParameters_0[(528U)>>2]);
    float _S165 = as_type<float>((&kernelContext_0)->frameParameters_0[(532U)>>2]);
    float _S166 = as_type<float>((&kernelContext_0)->frameParameters_0[(536U)>>2]);
    float _S167 = as_type<float>((&kernelContext_0)->frameParameters_0[(540U)>>2]);
    float4 _S168 = float4(_S164, _S165, _S166, _S167);
    float _S169 = as_type<float>((&kernelContext_0)->frameParameters_0[(544U)>>2]);
    float _S170 = as_type<float>((&kernelContext_0)->frameParameters_0[(548U)>>2]);
    float _S171 = as_type<float>((&kernelContext_0)->frameParameters_0[(552U)>>2]);
    float _S172 = as_type<float>((&kernelContext_0)->frameParameters_0[(556U)>>2]);
    float4 _S173 = float4(_S169, _S170, _S171, _S172);
    float _S174 = as_type<float>((&kernelContext_0)->frameParameters_0[(560U)>>2]);
    float _S175 = as_type<float>((&kernelContext_0)->frameParameters_0[(564U)>>2]);
    float _S176 = as_type<float>((&kernelContext_0)->frameParameters_0[(568U)>>2]);
    float _S177 = as_type<float>((&kernelContext_0)->frameParameters_0[(572U)>>2]);
    float4 _S178 = float4(_S174, _S175, _S176, _S177);
    float _S179 = as_type<float>((&kernelContext_0)->frameParameters_0[(576U)>>2]);
    float _S180 = as_type<float>((&kernelContext_0)->frameParameters_0[(580U)>>2]);
    float _S181 = as_type<float>((&kernelContext_0)->frameParameters_0[(584U)>>2]);
    float _S182 = as_type<float>((&kernelContext_0)->frameParameters_0[(588U)>>2]);
    float4 _S183 = float4(_S179, _S180, _S181, _S182);
    float _S184 = as_type<float>((&kernelContext_0)->frameParameters_0[(592U)>>2]);
    float _S185 = as_type<float>((&kernelContext_0)->frameParameters_0[(596U)>>2]);
    float _S186 = as_type<float>((&kernelContext_0)->frameParameters_0[(600U)>>2]);
    float _S187 = as_type<float>((&kernelContext_0)->frameParameters_0[(604U)>>2]);
    float4 _S188 = float4(_S184, _S185, _S186, _S187);
    float _S189 = as_type<float>((&kernelContext_0)->frameParameters_0[(608U)>>2]);
    float _S190 = as_type<float>((&kernelContext_0)->frameParameters_0[(612U)>>2]);
    float _S191 = as_type<float>((&kernelContext_0)->frameParameters_0[(616U)>>2]);
    float _S192 = as_type<float>((&kernelContext_0)->frameParameters_0[(620U)>>2]);
    float4 _S193 = float4(_S189, _S190, _S191, _S192);
    float _S194 = as_type<float>((&kernelContext_0)->frameParameters_0[(624U)>>2]);
    float _S195 = as_type<float>((&kernelContext_0)->frameParameters_0[(628U)>>2]);
    float _S196 = as_type<float>((&kernelContext_0)->frameParameters_0[(632U)>>2]);
    float _S197 = as_type<float>((&kernelContext_0)->frameParameters_0[(636U)>>2]);
    float4 _S198 = float4(_S194, _S195, _S196, _S197);
    float _S199 = as_type<float>((&kernelContext_0)->frameParameters_0[(640U)>>2]);
    float _S200 = as_type<float>((&kernelContext_0)->frameParameters_0[(644U)>>2]);
    float _S201 = as_type<float>((&kernelContext_0)->frameParameters_0[(648U)>>2]);
    float _S202 = as_type<float>((&kernelContext_0)->frameParameters_0[(652U)>>2]);
    float4 _S203 = float4(_S199, _S200, _S201, _S202);
    float _S204 = as_type<float>((&kernelContext_0)->frameParameters_0[(656U)>>2]);
    float _S205 = as_type<float>((&kernelContext_0)->frameParameters_0[(660U)>>2]);
    float _S206 = as_type<float>((&kernelContext_0)->frameParameters_0[(664U)>>2]);
    float _S207 = as_type<float>((&kernelContext_0)->frameParameters_0[(668U)>>2]);
    float4 _S208 = float4(_S204, _S205, _S206, _S207);
    float _S209 = as_type<float>((&kernelContext_0)->frameParameters_0[(672U)>>2]);
    float _S210 = as_type<float>((&kernelContext_0)->frameParameters_0[(676U)>>2]);
    float _S211 = as_type<float>((&kernelContext_0)->frameParameters_0[(680U)>>2]);
    float _S212 = as_type<float>((&kernelContext_0)->frameParameters_0[(684U)>>2]);
    float4 _S213 = float4(_S209, _S210, _S211, _S212);
    float _S214 = as_type<float>((&kernelContext_0)->frameParameters_0[(688U)>>2]);
    float _S215 = as_type<float>((&kernelContext_0)->frameParameters_0[(692U)>>2]);
    float _S216 = as_type<float>((&kernelContext_0)->frameParameters_0[(696U)>>2]);
    float _S217 = as_type<float>((&kernelContext_0)->frameParameters_0[(700U)>>2]);
    float4 _S218 = float4(_S214, _S215, _S216, _S217);
    float _S219 = as_type<float>((&kernelContext_0)->frameParameters_0[(704U)>>2]);
    float _S220 = as_type<float>((&kernelContext_0)->frameParameters_0[(708U)>>2]);
    float _S221 = as_type<float>((&kernelContext_0)->frameParameters_0[(712U)>>2]);
    float _S222 = as_type<float>((&kernelContext_0)->frameParameters_0[(716U)>>2]);
    float4 _S223 = float4(_S219, _S220, _S221, _S222);
    float _S224 = as_type<float>((&kernelContext_0)->frameParameters_0[(720U)>>2]);
    float _S225 = as_type<float>((&kernelContext_0)->frameParameters_0[(724U)>>2]);
    float _S226 = as_type<float>((&kernelContext_0)->frameParameters_0[(728U)>>2]);
    float _S227 = as_type<float>((&kernelContext_0)->frameParameters_0[(732U)>>2]);
    float4 _S228 = float4(_S224, _S225, _S226, _S227);
    float _S229 = as_type<float>((&kernelContext_0)->frameParameters_0[(736U)>>2]);
    float _S230 = as_type<float>((&kernelContext_0)->frameParameters_0[(740U)>>2]);
    float _S231 = as_type<float>((&kernelContext_0)->frameParameters_0[(744U)>>2]);
    float _S232 = as_type<float>((&kernelContext_0)->frameParameters_0[(748U)>>2]);
    float4 _S233 = float4(_S229, _S230, _S231, _S232);
    float _S234 = as_type<float>((&kernelContext_0)->frameParameters_0[(752U)>>2]);
    float _S235 = as_type<float>((&kernelContext_0)->frameParameters_0[(756U)>>2]);
    float _S236 = as_type<float>((&kernelContext_0)->frameParameters_0[(760U)>>2]);
    float _S237 = as_type<float>((&kernelContext_0)->frameParameters_0[(764U)>>2]);
    float4 _S238 = float4(_S234, _S235, _S236, _S237);
    float _S239 = as_type<float>((&kernelContext_0)->frameParameters_0[(768U)>>2]);
    float _S240 = as_type<float>((&kernelContext_0)->frameParameters_0[(772U)>>2]);
    float _S241 = as_type<float>((&kernelContext_0)->frameParameters_0[(776U)>>2]);
    float _S242 = as_type<float>((&kernelContext_0)->frameParameters_0[(780U)>>2]);
    float4 _S243 = float4(_S239, _S240, _S241, _S242);
    float _S244 = as_type<float>((&kernelContext_0)->frameParameters_0[(784U)>>2]);
    float _S245 = as_type<float>((&kernelContext_0)->frameParameters_0[(788U)>>2]);
    float _S246 = as_type<float>((&kernelContext_0)->frameParameters_0[(792U)>>2]);
    float _S247 = as_type<float>((&kernelContext_0)->frameParameters_0[(796U)>>2]);
    float4 _S248 = float4(_S244, _S245, _S246, _S247);
    float _S249 = as_type<float>((&kernelContext_0)->frameParameters_0[(800U)>>2]);
    float _S250 = as_type<float>((&kernelContext_0)->frameParameters_0[(804U)>>2]);
    float _S251 = as_type<float>((&kernelContext_0)->frameParameters_0[(808U)>>2]);
    float _S252 = as_type<float>((&kernelContext_0)->frameParameters_0[(812U)>>2]);
    float4 _S253 = float4(_S249, _S250, _S251, _S252);
    float _S254 = as_type<float>((&kernelContext_0)->frameParameters_0[(816U)>>2]);
    float _S255 = as_type<float>((&kernelContext_0)->frameParameters_0[(820U)>>2]);
    float _S256 = as_type<float>((&kernelContext_0)->frameParameters_0[(824U)>>2]);
    float _S257 = as_type<float>((&kernelContext_0)->frameParameters_0[(828U)>>2]);
    float4 _S258 = float4(_S254, _S255, _S256, _S257);
    float _S259 = as_type<float>((&kernelContext_0)->frameParameters_0[(832U)>>2]);
    float _S260 = as_type<float>((&kernelContext_0)->frameParameters_0[(836U)>>2]);
    float _S261 = as_type<float>((&kernelContext_0)->frameParameters_0[(840U)>>2]);
    float _S262 = as_type<float>((&kernelContext_0)->frameParameters_0[(844U)>>2]);
    float4 _S263 = float4(_S259, _S260, _S261, _S262);
    float _S264 = as_type<float>((&kernelContext_0)->frameParameters_0[(848U)>>2]);
    float _S265 = as_type<float>((&kernelContext_0)->frameParameters_0[(852U)>>2]);
    float _S266 = as_type<float>((&kernelContext_0)->frameParameters_0[(856U)>>2]);
    float _S267 = as_type<float>((&kernelContext_0)->frameParameters_0[(860U)>>2]);
    float4 _S268 = float4(_S264, _S265, _S266, _S267);
    float _S269 = as_type<float>((&kernelContext_0)->frameParameters_0[(864U)>>2]);
    float _S270 = as_type<float>((&kernelContext_0)->frameParameters_0[(868U)>>2]);
    float _S271 = as_type<float>((&kernelContext_0)->frameParameters_0[(872U)>>2]);
    float _S272 = as_type<float>((&kernelContext_0)->frameParameters_0[(876U)>>2]);
    float4 _S273 = float4(_S269, _S270, _S271, _S272);
    float _S274 = as_type<float>((&kernelContext_0)->frameParameters_0[(880U)>>2]);
    float _S275 = as_type<float>((&kernelContext_0)->frameParameters_0[(884U)>>2]);
    float _S276 = as_type<float>((&kernelContext_0)->frameParameters_0[(888U)>>2]);
    float _S277 = as_type<float>((&kernelContext_0)->frameParameters_0[(892U)>>2]);
    float4 _S278 = float4(_S274, _S275, _S276, _S277);
    float _S279 = as_type<float>((&kernelContext_0)->frameParameters_0[(896U)>>2]);
    float _S280 = as_type<float>((&kernelContext_0)->frameParameters_0[(900U)>>2]);
    float _S281 = as_type<float>((&kernelContext_0)->frameParameters_0[(904U)>>2]);
    float _S282 = as_type<float>((&kernelContext_0)->frameParameters_0[(908U)>>2]);
    float4 _S283 = float4(_S279, _S280, _S281, _S282);
    float _S284 = as_type<float>((&kernelContext_0)->frameParameters_0[(912U)>>2]);
    float _S285 = as_type<float>((&kernelContext_0)->frameParameters_0[(916U)>>2]);
    float _S286 = as_type<float>((&kernelContext_0)->frameParameters_0[(920U)>>2]);
    float _S287 = as_type<float>((&kernelContext_0)->frameParameters_0[(924U)>>2]);
    float4 _S288 = float4(_S284, _S285, _S286, _S287);
    float _S289 = as_type<float>((&kernelContext_0)->frameParameters_0[(928U)>>2]);
    float _S290 = as_type<float>((&kernelContext_0)->frameParameters_0[(932U)>>2]);
    float _S291 = as_type<float>((&kernelContext_0)->frameParameters_0[(936U)>>2]);
    float _S292 = as_type<float>((&kernelContext_0)->frameParameters_0[(940U)>>2]);
    float4 _S293 = float4(_S289, _S290, _S291, _S292);
    float _S294 = as_type<float>((&kernelContext_0)->frameParameters_0[(944U)>>2]);
    float _S295 = as_type<float>((&kernelContext_0)->frameParameters_0[(948U)>>2]);
    float _S296 = as_type<float>((&kernelContext_0)->frameParameters_0[(952U)>>2]);
    float _S297 = as_type<float>((&kernelContext_0)->frameParameters_0[(956U)>>2]);
    float4 _S298 = float4(_S294, _S295, _S296, _S297);
    float _S299 = as_type<float>((&kernelContext_0)->frameParameters_0[(960U)>>2]);
    float _S300 = as_type<float>((&kernelContext_0)->frameParameters_0[(964U)>>2]);
    float _S301 = as_type<float>((&kernelContext_0)->frameParameters_0[(968U)>>2]);
    float _S302 = as_type<float>((&kernelContext_0)->frameParameters_0[(972U)>>2]);
    float4 _S303 = float4(_S299, _S300, _S301, _S302);
    float _S304 = as_type<float>((&kernelContext_0)->frameParameters_0[(976U)>>2]);
    float _S305 = as_type<float>((&kernelContext_0)->frameParameters_0[(980U)>>2]);
    float _S306 = as_type<float>((&kernelContext_0)->frameParameters_0[(984U)>>2]);
    float _S307 = as_type<float>((&kernelContext_0)->frameParameters_0[(988U)>>2]);
    float4 _S308 = float4(_S304, _S305, _S306, _S307);
    float _S309 = as_type<float>((&kernelContext_0)->frameParameters_0[(992U)>>2]);
    float _S310 = as_type<float>((&kernelContext_0)->frameParameters_0[(996U)>>2]);
    float _S311 = as_type<float>((&kernelContext_0)->frameParameters_0[(1000U)>>2]);
    float _S312 = as_type<float>((&kernelContext_0)->frameParameters_0[(1004U)>>2]);
    float4 _S313 = float4(_S309, _S310, _S311, _S312);
    float _S314 = as_type<float>((&kernelContext_0)->frameParameters_0[(1008U)>>2]);
    float _S315 = as_type<float>((&kernelContext_0)->frameParameters_0[(1012U)>>2]);
    float _S316 = as_type<float>((&kernelContext_0)->frameParameters_0[(1016U)>>2]);
    float _S317 = as_type<float>((&kernelContext_0)->frameParameters_0[(1020U)>>2]);
    float4 _S318 = float4(_S314, _S315, _S316, _S317);
    float _S319 = as_type<float>((&kernelContext_0)->frameParameters_0[(1024U)>>2]);
    float _S320 = as_type<float>((&kernelContext_0)->frameParameters_0[(1028U)>>2]);
    float _S321 = as_type<float>((&kernelContext_0)->frameParameters_0[(1032U)>>2]);
    float _S322 = as_type<float>((&kernelContext_0)->frameParameters_0[(1036U)>>2]);
    float4 _S323 = float4(_S319, _S320, _S321, _S322);
    float _S324 = as_type<float>((&kernelContext_0)->frameParameters_0[(1040U)>>2]);
    float _S325 = as_type<float>((&kernelContext_0)->frameParameters_0[(1044U)>>2]);
    float _S326 = as_type<float>((&kernelContext_0)->frameParameters_0[(1048U)>>2]);
    float _S327 = as_type<float>((&kernelContext_0)->frameParameters_0[(1052U)>>2]);
    float4 _S328 = float4(_S324, _S325, _S326, _S327);
    float _S329 = as_type<float>((&kernelContext_0)->frameParameters_0[(1056U)>>2]);
    float _S330 = as_type<float>((&kernelContext_0)->frameParameters_0[(1060U)>>2]);
    float _S331 = as_type<float>((&kernelContext_0)->frameParameters_0[(1064U)>>2]);
    float _S332 = as_type<float>((&kernelContext_0)->frameParameters_0[(1068U)>>2]);
    float4 _S333 = float4(_S329, _S330, _S331, _S332);
    float _S334 = as_type<float>((&kernelContext_0)->frameParameters_0[(1072U)>>2]);
    float _S335 = as_type<float>((&kernelContext_0)->frameParameters_0[(1076U)>>2]);
    float _S336 = as_type<float>((&kernelContext_0)->frameParameters_0[(1080U)>>2]);
    float _S337 = as_type<float>((&kernelContext_0)->frameParameters_0[(1084U)>>2]);
    float4 _S338 = float4(_S334, _S335, _S336, _S337);
    float _S339 = as_type<float>((&kernelContext_0)->frameParameters_0[(1088U)>>2]);
    float _S340 = as_type<float>((&kernelContext_0)->frameParameters_0[(1092U)>>2]);
    float _S341 = as_type<float>((&kernelContext_0)->frameParameters_0[(1096U)>>2]);
    float _S342 = as_type<float>((&kernelContext_0)->frameParameters_0[(1100U)>>2]);
    float4 _S343 = float4(_S339, _S340, _S341, _S342);
    float _S344 = as_type<float>((&kernelContext_0)->frameParameters_0[(1104U)>>2]);
    float _S345 = as_type<float>((&kernelContext_0)->frameParameters_0[(1108U)>>2]);
    float _S346 = as_type<float>((&kernelContext_0)->frameParameters_0[(1112U)>>2]);
    float _S347 = as_type<float>((&kernelContext_0)->frameParameters_0[(1116U)>>2]);
    float4 _S348 = float4(_S344, _S345, _S346, _S347);
    float _S349 = as_type<float>((&kernelContext_0)->frameParameters_0[(1120U)>>2]);
    float _S350 = as_type<float>((&kernelContext_0)->frameParameters_0[(1124U)>>2]);
    float _S351 = as_type<float>((&kernelContext_0)->frameParameters_0[(1128U)>>2]);
    float _S352 = as_type<float>((&kernelContext_0)->frameParameters_0[(1132U)>>2]);
    float4 _S353 = float4(_S349, _S350, _S351, _S352);
    float _S354 = as_type<float>((&kernelContext_0)->frameParameters_0[(1136U)>>2]);
    float _S355 = as_type<float>((&kernelContext_0)->frameParameters_0[(1140U)>>2]);
    float _S356 = as_type<float>((&kernelContext_0)->frameParameters_0[(1144U)>>2]);
    float _S357 = as_type<float>((&kernelContext_0)->frameParameters_0[(1148U)>>2]);
    float4 _S358 = float4(_S354, _S355, _S356, _S357);
    float _S359 = as_type<float>((&kernelContext_0)->frameParameters_0[(1152U)>>2]);
    float _S360 = as_type<float>((&kernelContext_0)->frameParameters_0[(1156U)>>2]);
    float _S361 = as_type<float>((&kernelContext_0)->frameParameters_0[(1160U)>>2]);
    float _S362 = as_type<float>((&kernelContext_0)->frameParameters_0[(1164U)>>2]);
    float4 _S363 = float4(_S359, _S360, _S361, _S362);
    float _S364 = as_type<float>((&kernelContext_0)->frameParameters_0[(1168U)>>2]);
    float _S365 = as_type<float>((&kernelContext_0)->frameParameters_0[(1172U)>>2]);
    float _S366 = as_type<float>((&kernelContext_0)->frameParameters_0[(1176U)>>2]);
    float _S367 = as_type<float>((&kernelContext_0)->frameParameters_0[(1180U)>>2]);
    float4 _S368 = float4(_S364, _S365, _S366, _S367);
    float _S369 = as_type<float>((&kernelContext_0)->frameParameters_0[(1184U)>>2]);
    float _S370 = as_type<float>((&kernelContext_0)->frameParameters_0[(1188U)>>2]);
    float _S371 = as_type<float>((&kernelContext_0)->frameParameters_0[(1192U)>>2]);
    float _S372 = as_type<float>((&kernelContext_0)->frameParameters_0[(1196U)>>2]);
    float4 _S373 = float4(_S369, _S370, _S371, _S372);
    float _S374 = as_type<float>((&kernelContext_0)->frameParameters_0[(1200U)>>2]);
    float _S375 = as_type<float>((&kernelContext_0)->frameParameters_0[(1204U)>>2]);
    float _S376 = as_type<float>((&kernelContext_0)->frameParameters_0[(1208U)>>2]);
    float _S377 = as_type<float>((&kernelContext_0)->frameParameters_0[(1212U)>>2]);
    float4 _S378 = float4(_S374, _S375, _S376, _S377);
    float _S379 = as_type<float>((&kernelContext_0)->frameParameters_0[(1216U)>>2]);
    float _S380 = as_type<float>((&kernelContext_0)->frameParameters_0[(1220U)>>2]);
    float _S381 = as_type<float>((&kernelContext_0)->frameParameters_0[(1224U)>>2]);
    float _S382 = as_type<float>((&kernelContext_0)->frameParameters_0[(1228U)>>2]);
    float4 _S383 = float4(_S379, _S380, _S381, _S382);
    float _S384 = as_type<float>((&kernelContext_0)->frameParameters_0[(1232U)>>2]);
    float _S385 = as_type<float>((&kernelContext_0)->frameParameters_0[(1236U)>>2]);
    float _S386 = as_type<float>((&kernelContext_0)->frameParameters_0[(1240U)>>2]);
    float _S387 = as_type<float>((&kernelContext_0)->frameParameters_0[(1244U)>>2]);
    float4 _S388 = float4(_S384, _S385, _S386, _S387);
    float _S389 = as_type<float>((&kernelContext_0)->frameParameters_0[(1248U)>>2]);
    float _S390 = as_type<float>((&kernelContext_0)->frameParameters_0[(1252U)>>2]);
    float _S391 = as_type<float>((&kernelContext_0)->frameParameters_0[(1256U)>>2]);
    float _S392 = as_type<float>((&kernelContext_0)->frameParameters_0[(1260U)>>2]);
    float4 _S393 = float4(_S389, _S390, _S391, _S392);
    float _S394 = as_type<float>((&kernelContext_0)->frameParameters_0[(1264U)>>2]);
    float _S395 = as_type<float>((&kernelContext_0)->frameParameters_0[(1268U)>>2]);
    float _S396 = as_type<float>((&kernelContext_0)->frameParameters_0[(1272U)>>2]);
    float _S397 = as_type<float>((&kernelContext_0)->frameParameters_0[(1276U)>>2]);
    float4 _S398 = float4(_S394, _S395, _S396, _S397);
    float _S399 = as_type<float>((&kernelContext_0)->frameParameters_0[(1280U)>>2]);
    float _S400 = as_type<float>((&kernelContext_0)->frameParameters_0[(1284U)>>2]);
    float _S401 = as_type<float>((&kernelContext_0)->frameParameters_0[(1288U)>>2]);
    float _S402 = as_type<float>((&kernelContext_0)->frameParameters_0[(1292U)>>2]);
    float4 _S403 = float4(_S399, _S400, _S401, _S402);
    float _S404 = as_type<float>((&kernelContext_0)->frameParameters_0[(1296U)>>2]);
    float _S405 = as_type<float>((&kernelContext_0)->frameParameters_0[(1300U)>>2]);
    float _S406 = as_type<float>((&kernelContext_0)->frameParameters_0[(1304U)>>2]);
    float _S407 = as_type<float>((&kernelContext_0)->frameParameters_0[(1308U)>>2]);
    float4 _S408 = float4(_S404, _S405, _S406, _S407);
    float _S409 = as_type<float>((&kernelContext_0)->frameParameters_0[(1312U)>>2]);
    float _S410 = as_type<float>((&kernelContext_0)->frameParameters_0[(1316U)>>2]);
    float _S411 = as_type<float>((&kernelContext_0)->frameParameters_0[(1320U)>>2]);
    float _S412 = as_type<float>((&kernelContext_0)->frameParameters_0[(1324U)>>2]);
    float4 _S413 = float4(_S409, _S410, _S411, _S412);
    float _S414 = as_type<float>((&kernelContext_0)->frameParameters_0[(1328U)>>2]);
    float _S415 = as_type<float>((&kernelContext_0)->frameParameters_0[(1332U)>>2]);
    float _S416 = as_type<float>((&kernelContext_0)->frameParameters_0[(1336U)>>2]);
    float _S417 = as_type<float>((&kernelContext_0)->frameParameters_0[(1340U)>>2]);
    float4 _S418 = float4(_S414, _S415, _S416, _S417);
    float _S419 = as_type<float>((&kernelContext_0)->frameParameters_0[(1344U)>>2]);
    float _S420 = as_type<float>((&kernelContext_0)->frameParameters_0[(1348U)>>2]);
    float _S421 = as_type<float>((&kernelContext_0)->frameParameters_0[(1352U)>>2]);
    float _S422 = as_type<float>((&kernelContext_0)->frameParameters_0[(1356U)>>2]);
    float4 _S423 = float4(_S419, _S420, _S421, _S422);
    float _S424 = as_type<float>((&kernelContext_0)->frameParameters_0[(1360U)>>2]);
    float _S425 = as_type<float>((&kernelContext_0)->frameParameters_0[(1364U)>>2]);
    float _S426 = as_type<float>((&kernelContext_0)->frameParameters_0[(1368U)>>2]);
    float _S427 = as_type<float>((&kernelContext_0)->frameParameters_0[(1372U)>>2]);
    float4 _S428 = float4(_S424, _S425, _S426, _S427);
    float _S429 = as_type<float>((&kernelContext_0)->frameParameters_0[(1376U)>>2]);
    float _S430 = as_type<float>((&kernelContext_0)->frameParameters_0[(1380U)>>2]);
    float _S431 = as_type<float>((&kernelContext_0)->frameParameters_0[(1384U)>>2]);
    float _S432 = as_type<float>((&kernelContext_0)->frameParameters_0[(1388U)>>2]);
    float4 _S433 = float4(_S429, _S430, _S431, _S432);
    float _S434 = as_type<float>((&kernelContext_0)->frameParameters_0[(1392U)>>2]);
    float _S435 = as_type<float>((&kernelContext_0)->frameParameters_0[(1396U)>>2]);
    float _S436 = as_type<float>((&kernelContext_0)->frameParameters_0[(1400U)>>2]);
    float _S437 = as_type<float>((&kernelContext_0)->frameParameters_0[(1404U)>>2]);
    float4 _S438 = float4(_S434, _S435, _S436, _S437);
    float _S439 = as_type<float>((&kernelContext_0)->frameParameters_0[(1408U)>>2]);
    float _S440 = as_type<float>((&kernelContext_0)->frameParameters_0[(1412U)>>2]);
    float _S441 = as_type<float>((&kernelContext_0)->frameParameters_0[(1416U)>>2]);
    float _S442 = as_type<float>((&kernelContext_0)->frameParameters_0[(1420U)>>2]);
    float4 _S443 = float4(_S439, _S440, _S441, _S442);
    float _S444 = as_type<float>((&kernelContext_0)->frameParameters_0[(1424U)>>2]);
    float _S445 = as_type<float>((&kernelContext_0)->frameParameters_0[(1428U)>>2]);
    float _S446 = as_type<float>((&kernelContext_0)->frameParameters_0[(1432U)>>2]);
    float _S447 = as_type<float>((&kernelContext_0)->frameParameters_0[(1436U)>>2]);
    float4 _S448 = float4(_S444, _S445, _S446, _S447);
    float _S449 = as_type<float>((&kernelContext_0)->frameParameters_0[(1440U)>>2]);
    float _S450 = as_type<float>((&kernelContext_0)->frameParameters_0[(1444U)>>2]);
    float _S451 = as_type<float>((&kernelContext_0)->frameParameters_0[(1448U)>>2]);
    float _S452 = as_type<float>((&kernelContext_0)->frameParameters_0[(1452U)>>2]);
    float4 _S453 = float4(_S449, _S450, _S451, _S452);
    float _S454 = as_type<float>((&kernelContext_0)->frameParameters_0[(1456U)>>2]);
    float _S455 = as_type<float>((&kernelContext_0)->frameParameters_0[(1460U)>>2]);
    float _S456 = as_type<float>((&kernelContext_0)->frameParameters_0[(1464U)>>2]);
    float _S457 = as_type<float>((&kernelContext_0)->frameParameters_0[(1468U)>>2]);
    float4 _S458 = float4(_S454, _S455, _S456, _S457);
    float _S459 = as_type<float>((&kernelContext_0)->frameParameters_0[(1472U)>>2]);
    float _S460 = as_type<float>((&kernelContext_0)->frameParameters_0[(1476U)>>2]);
    float _S461 = as_type<float>((&kernelContext_0)->frameParameters_0[(1480U)>>2]);
    float _S462 = as_type<float>((&kernelContext_0)->frameParameters_0[(1484U)>>2]);
    float4 _S463 = float4(_S459, _S460, _S461, _S462);
    float _S464 = as_type<float>((&kernelContext_0)->frameParameters_0[(1488U)>>2]);
    float _S465 = as_type<float>((&kernelContext_0)->frameParameters_0[(1492U)>>2]);
    float _S466 = as_type<float>((&kernelContext_0)->frameParameters_0[(1496U)>>2]);
    float _S467 = as_type<float>((&kernelContext_0)->frameParameters_0[(1500U)>>2]);
    float4 _S468 = float4(_S464, _S465, _S466, _S467);
    float _S469 = as_type<float>((&kernelContext_0)->frameParameters_0[(1504U)>>2]);
    float _S470 = as_type<float>((&kernelContext_0)->frameParameters_0[(1508U)>>2]);
    float _S471 = as_type<float>((&kernelContext_0)->frameParameters_0[(1512U)>>2]);
    float _S472 = as_type<float>((&kernelContext_0)->frameParameters_0[(1516U)>>2]);
    float4 _S473 = float4(_S469, _S470, _S471, _S472);
    float _S474 = as_type<float>((&kernelContext_0)->frameParameters_0[(1520U)>>2]);
    float _S475 = as_type<float>((&kernelContext_0)->frameParameters_0[(1524U)>>2]);
    float _S476 = as_type<float>((&kernelContext_0)->frameParameters_0[(1528U)>>2]);
    float _S477 = as_type<float>((&kernelContext_0)->frameParameters_0[(1532U)>>2]);
    float4 _S478 = float4(_S474, _S475, _S476, _S477);
    float _S479 = as_type<float>((&kernelContext_0)->frameParameters_0[(1536U)>>2]);
    float _S480 = as_type<float>((&kernelContext_0)->frameParameters_0[(1540U)>>2]);
    float _S481 = as_type<float>((&kernelContext_0)->frameParameters_0[(1544U)>>2]);
    float _S482 = as_type<float>((&kernelContext_0)->frameParameters_0[(1548U)>>2]);
    float4 _S483 = float4(_S479, _S480, _S481, _S482);
    float _S484 = as_type<float>((&kernelContext_0)->frameParameters_0[(1552U)>>2]);
    float _S485 = as_type<float>((&kernelContext_0)->frameParameters_0[(1556U)>>2]);
    float _S486 = as_type<float>((&kernelContext_0)->frameParameters_0[(1560U)>>2]);
    float _S487 = as_type<float>((&kernelContext_0)->frameParameters_0[(1564U)>>2]);
    float4 _S488 = float4(_S484, _S485, _S486, _S487);
    float _S489 = as_type<float>((&kernelContext_0)->frameParameters_0[(1568U)>>2]);
    float _S490 = as_type<float>((&kernelContext_0)->frameParameters_0[(1572U)>>2]);
    float _S491 = as_type<float>((&kernelContext_0)->frameParameters_0[(1576U)>>2]);
    float _S492 = as_type<float>((&kernelContext_0)->frameParameters_0[(1580U)>>2]);
    float4 _S493 = float4(_S489, _S490, _S491, _S492);
    float _S494 = as_type<float>((&kernelContext_0)->frameParameters_0[(1584U)>>2]);
    float _S495 = as_type<float>((&kernelContext_0)->frameParameters_0[(1588U)>>2]);
    float _S496 = as_type<float>((&kernelContext_0)->frameParameters_0[(1592U)>>2]);
    float _S497 = as_type<float>((&kernelContext_0)->frameParameters_0[(1596U)>>2]);
    float4 _S498 = float4(_S494, _S495, _S496, _S497);
    float _S499 = as_type<float>((&kernelContext_0)->frameParameters_0[(1600U)>>2]);
    float _S500 = as_type<float>((&kernelContext_0)->frameParameters_0[(1604U)>>2]);
    float _S501 = as_type<float>((&kernelContext_0)->frameParameters_0[(1608U)>>2]);
    float _S502 = as_type<float>((&kernelContext_0)->frameParameters_0[(1612U)>>2]);
    float4 _S503 = float4(_S499, _S500, _S501, _S502);
    float _S504 = as_type<float>((&kernelContext_0)->frameParameters_0[(1616U)>>2]);
    float _S505 = as_type<float>((&kernelContext_0)->frameParameters_0[(1620U)>>2]);
    float _S506 = as_type<float>((&kernelContext_0)->frameParameters_0[(1624U)>>2]);
    float _S507 = as_type<float>((&kernelContext_0)->frameParameters_0[(1628U)>>2]);
    float4 _S508 = float4(_S504, _S505, _S506, _S507);
    float _S509 = as_type<float>((&kernelContext_0)->frameParameters_0[(1632U)>>2]);
    float _S510 = as_type<float>((&kernelContext_0)->frameParameters_0[(1636U)>>2]);
    float _S511 = as_type<float>((&kernelContext_0)->frameParameters_0[(1640U)>>2]);
    float _S512 = as_type<float>((&kernelContext_0)->frameParameters_0[(1644U)>>2]);
    float4 _S513 = float4(_S509, _S510, _S511, _S512);
    float _S514 = as_type<float>((&kernelContext_0)->frameParameters_0[(1648U)>>2]);
    float _S515 = as_type<float>((&kernelContext_0)->frameParameters_0[(1652U)>>2]);
    float _S516 = as_type<float>((&kernelContext_0)->frameParameters_0[(1656U)>>2]);
    float _S517 = as_type<float>((&kernelContext_0)->frameParameters_0[(1660U)>>2]);
    float4 _S518 = float4(_S514, _S515, _S516, _S517);
    float _S519 = as_type<float>((&kernelContext_0)->frameParameters_0[(1664U)>>2]);
    float _S520 = as_type<float>((&kernelContext_0)->frameParameters_0[(1668U)>>2]);
    float _S521 = as_type<float>((&kernelContext_0)->frameParameters_0[(1672U)>>2]);
    float _S522 = as_type<float>((&kernelContext_0)->frameParameters_0[(1676U)>>2]);
    float4 _S523 = float4(_S519, _S520, _S521, _S522);
    float _S524 = as_type<float>((&kernelContext_0)->frameParameters_0[(1680U)>>2]);
    float _S525 = as_type<float>((&kernelContext_0)->frameParameters_0[(1684U)>>2]);
    float _S526 = as_type<float>((&kernelContext_0)->frameParameters_0[(1688U)>>2]);
    float _S527 = as_type<float>((&kernelContext_0)->frameParameters_0[(1692U)>>2]);
    float4 _S528 = float4(_S524, _S525, _S526, _S527);
    float _S529 = as_type<float>((&kernelContext_0)->frameParameters_0[(1696U)>>2]);
    float _S530 = as_type<float>((&kernelContext_0)->frameParameters_0[(1700U)>>2]);
    float _S531 = as_type<float>((&kernelContext_0)->frameParameters_0[(1704U)>>2]);
    float _S532 = as_type<float>((&kernelContext_0)->frameParameters_0[(1708U)>>2]);
    float4 _S533 = float4(_S529, _S530, _S531, _S532);
    float _S534 = as_type<float>((&kernelContext_0)->frameParameters_0[(1712U)>>2]);
    float _S535 = as_type<float>((&kernelContext_0)->frameParameters_0[(1716U)>>2]);
    float _S536 = as_type<float>((&kernelContext_0)->frameParameters_0[(1720U)>>2]);
    float _S537 = as_type<float>((&kernelContext_0)->frameParameters_0[(1724U)>>2]);
    float4 _S538 = float4(_S534, _S535, _S536, _S537);
    float _S539 = as_type<float>((&kernelContext_0)->frameParameters_0[(1728U)>>2]);
    float _S540 = as_type<float>((&kernelContext_0)->frameParameters_0[(1732U)>>2]);
    float _S541 = as_type<float>((&kernelContext_0)->frameParameters_0[(1736U)>>2]);
    float _S542 = as_type<float>((&kernelContext_0)->frameParameters_0[(1740U)>>2]);
    float4 _S543 = float4(_S539, _S540, _S541, _S542);
    float _S544 = as_type<float>((&kernelContext_0)->frameParameters_0[(1744U)>>2]);
    float _S545 = as_type<float>((&kernelContext_0)->frameParameters_0[(1748U)>>2]);
    float _S546 = as_type<float>((&kernelContext_0)->frameParameters_0[(1752U)>>2]);
    float _S547 = as_type<float>((&kernelContext_0)->frameParameters_0[(1756U)>>2]);
    float4 _S548 = float4(_S544, _S545, _S546, _S547);
    float _S549 = as_type<float>((&kernelContext_0)->frameParameters_0[(1760U)>>2]);
    float _S550 = as_type<float>((&kernelContext_0)->frameParameters_0[(1764U)>>2]);
    float _S551 = as_type<float>((&kernelContext_0)->frameParameters_0[(1768U)>>2]);
    float _S552 = as_type<float>((&kernelContext_0)->frameParameters_0[(1772U)>>2]);
    float4 _S553 = float4(_S549, _S550, _S551, _S552);
    float _S554 = as_type<float>((&kernelContext_0)->frameParameters_0[(1776U)>>2]);
    float _S555 = as_type<float>((&kernelContext_0)->frameParameters_0[(1780U)>>2]);
    float _S556 = as_type<float>((&kernelContext_0)->frameParameters_0[(1784U)>>2]);
    float _S557 = as_type<float>((&kernelContext_0)->frameParameters_0[(1788U)>>2]);
    float4 _S558 = float4(_S554, _S555, _S556, _S557);
    float _S559 = as_type<float>((&kernelContext_0)->frameParameters_0[(1792U)>>2]);
    float _S560 = as_type<float>((&kernelContext_0)->frameParameters_0[(1796U)>>2]);
    float _S561 = as_type<float>((&kernelContext_0)->frameParameters_0[(1800U)>>2]);
    float _S562 = as_type<float>((&kernelContext_0)->frameParameters_0[(1804U)>>2]);
    float4 _S563 = float4(_S559, _S560, _S561, _S562);
    float _S564 = as_type<float>((&kernelContext_0)->frameParameters_0[(1808U)>>2]);
    float _S565 = as_type<float>((&kernelContext_0)->frameParameters_0[(1812U)>>2]);
    float _S566 = as_type<float>((&kernelContext_0)->frameParameters_0[(1816U)>>2]);
    float _S567 = as_type<float>((&kernelContext_0)->frameParameters_0[(1820U)>>2]);
    float4 _S568 = float4(_S564, _S565, _S566, _S567);
    float _S569 = as_type<float>((&kernelContext_0)->frameParameters_0[(1824U)>>2]);
    float _S570 = as_type<float>((&kernelContext_0)->frameParameters_0[(1828U)>>2]);
    float _S571 = as_type<float>((&kernelContext_0)->frameParameters_0[(1832U)>>2]);
    float _S572 = as_type<float>((&kernelContext_0)->frameParameters_0[(1836U)>>2]);
    float4 _S573 = float4(_S569, _S570, _S571, _S572);
    float _S574 = as_type<float>((&kernelContext_0)->frameParameters_0[(1840U)>>2]);
    float _S575 = as_type<float>((&kernelContext_0)->frameParameters_0[(1844U)>>2]);
    float _S576 = as_type<float>((&kernelContext_0)->frameParameters_0[(1848U)>>2]);
    float _S577 = as_type<float>((&kernelContext_0)->frameParameters_0[(1852U)>>2]);
    float4 _S578 = float4(_S574, _S575, _S576, _S577);
    float _S579 = as_type<float>((&kernelContext_0)->frameParameters_0[(1856U)>>2]);
    float _S580 = as_type<float>((&kernelContext_0)->frameParameters_0[(1860U)>>2]);
    float _S581 = as_type<float>((&kernelContext_0)->frameParameters_0[(1864U)>>2]);
    float _S582 = as_type<float>((&kernelContext_0)->frameParameters_0[(1868U)>>2]);
    float4 _S583 = float4(_S579, _S580, _S581, _S582);
    float _S584 = as_type<float>((&kernelContext_0)->frameParameters_0[(1872U)>>2]);
    float _S585 = as_type<float>((&kernelContext_0)->frameParameters_0[(1876U)>>2]);
    float _S586 = as_type<float>((&kernelContext_0)->frameParameters_0[(1880U)>>2]);
    float _S587 = as_type<float>((&kernelContext_0)->frameParameters_0[(1884U)>>2]);
    float4 _S588 = float4(_S584, _S585, _S586, _S587);
    float _S589 = as_type<float>((&kernelContext_0)->frameParameters_0[(1888U)>>2]);
    float _S590 = as_type<float>((&kernelContext_0)->frameParameters_0[(1892U)>>2]);
    float _S591 = as_type<float>((&kernelContext_0)->frameParameters_0[(1896U)>>2]);
    float _S592 = as_type<float>((&kernelContext_0)->frameParameters_0[(1900U)>>2]);
    float4 _S593 = float4(_S589, _S590, _S591, _S592);
    float _S594 = as_type<float>((&kernelContext_0)->frameParameters_0[(1904U)>>2]);
    float _S595 = as_type<float>((&kernelContext_0)->frameParameters_0[(1908U)>>2]);
    float _S596 = as_type<float>((&kernelContext_0)->frameParameters_0[(1912U)>>2]);
    float _S597 = as_type<float>((&kernelContext_0)->frameParameters_0[(1916U)>>2]);
    float4 _S598 = float4(_S594, _S595, _S596, _S597);
    float _S599 = as_type<float>((&kernelContext_0)->frameParameters_0[(1920U)>>2]);
    float _S600 = as_type<float>((&kernelContext_0)->frameParameters_0[(1924U)>>2]);
    float _S601 = as_type<float>((&kernelContext_0)->frameParameters_0[(1928U)>>2]);
    float _S602 = as_type<float>((&kernelContext_0)->frameParameters_0[(1932U)>>2]);
    float4 _S603 = float4(_S599, _S600, _S601, _S602);
    float _S604 = as_type<float>((&kernelContext_0)->frameParameters_0[(1936U)>>2]);
    float _S605 = as_type<float>((&kernelContext_0)->frameParameters_0[(1940U)>>2]);
    float _S606 = as_type<float>((&kernelContext_0)->frameParameters_0[(1944U)>>2]);
    float _S607 = as_type<float>((&kernelContext_0)->frameParameters_0[(1948U)>>2]);
    float4 _S608 = float4(_S604, _S605, _S606, _S607);
    float _S609 = as_type<float>((&kernelContext_0)->frameParameters_0[(1952U)>>2]);
    float _S610 = as_type<float>((&kernelContext_0)->frameParameters_0[(1956U)>>2]);
    float _S611 = as_type<float>((&kernelContext_0)->frameParameters_0[(1960U)>>2]);
    float _S612 = as_type<float>((&kernelContext_0)->frameParameters_0[(1964U)>>2]);
    float4 _S613 = float4(_S609, _S610, _S611, _S612);
    float _S614 = as_type<float>((&kernelContext_0)->frameParameters_0[(1968U)>>2]);
    float _S615 = as_type<float>((&kernelContext_0)->frameParameters_0[(1972U)>>2]);
    float _S616 = as_type<float>((&kernelContext_0)->frameParameters_0[(1976U)>>2]);
    float _S617 = as_type<float>((&kernelContext_0)->frameParameters_0[(1980U)>>2]);
    float4 _S618 = float4(_S614, _S615, _S616, _S617);
    float _S619 = as_type<float>((&kernelContext_0)->frameParameters_0[(1984U)>>2]);
    float _S620 = as_type<float>((&kernelContext_0)->frameParameters_0[(1988U)>>2]);
    float _S621 = as_type<float>((&kernelContext_0)->frameParameters_0[(1992U)>>2]);
    float _S622 = as_type<float>((&kernelContext_0)->frameParameters_0[(1996U)>>2]);
    float4 _S623 = float4(_S619, _S620, _S621, _S622);
    float _S624 = as_type<float>((&kernelContext_0)->frameParameters_0[(2000U)>>2]);
    float _S625 = as_type<float>((&kernelContext_0)->frameParameters_0[(2004U)>>2]);
    float _S626 = as_type<float>((&kernelContext_0)->frameParameters_0[(2008U)>>2]);
    float _S627 = as_type<float>((&kernelContext_0)->frameParameters_0[(2012U)>>2]);
    float4 _S628 = float4(_S624, _S625, _S626, _S627);
    float _S629 = as_type<float>((&kernelContext_0)->frameParameters_0[(2016U)>>2]);
    float _S630 = as_type<float>((&kernelContext_0)->frameParameters_0[(2020U)>>2]);
    float _S631 = as_type<float>((&kernelContext_0)->frameParameters_0[(2024U)>>2]);
    float _S632 = as_type<float>((&kernelContext_0)->frameParameters_0[(2028U)>>2]);
    float4 _S633 = float4(_S629, _S630, _S631, _S632);
    float _S634 = as_type<float>((&kernelContext_0)->frameParameters_0[(2032U)>>2]);
    float _S635 = as_type<float>((&kernelContext_0)->frameParameters_0[(2036U)>>2]);
    float _S636 = as_type<float>((&kernelContext_0)->frameParameters_0[(2040U)>>2]);
    float _S637 = as_type<float>((&kernelContext_0)->frameParameters_0[(2044U)>>2]);
    float4 _S638 = float4(_S634, _S635, _S636, _S637);
    float _S639 = as_type<float>((&kernelContext_0)->frameParameters_0[(2048U)>>2]);
    float _S640 = as_type<float>((&kernelContext_0)->frameParameters_0[(2052U)>>2]);
    float _S641 = as_type<float>((&kernelContext_0)->frameParameters_0[(2056U)>>2]);
    float _S642 = as_type<float>((&kernelContext_0)->frameParameters_0[(2060U)>>2]);
    float4 _S643 = float4(_S639, _S640, _S641, _S642);
    float _S644 = as_type<float>((&kernelContext_0)->frameParameters_0[(2064U)>>2]);
    float _S645 = as_type<float>((&kernelContext_0)->frameParameters_0[(2068U)>>2]);
    float _S646 = as_type<float>((&kernelContext_0)->frameParameters_0[(2072U)>>2]);
    float _S647 = as_type<float>((&kernelContext_0)->frameParameters_0[(2076U)>>2]);
    float4 _S648 = float4(_S644, _S645, _S646, _S647);
    float _S649 = as_type<float>((&kernelContext_0)->frameParameters_0[(2080U)>>2]);
    float _S650 = as_type<float>((&kernelContext_0)->frameParameters_0[(2084U)>>2]);
    float _S651 = as_type<float>((&kernelContext_0)->frameParameters_0[(2088U)>>2]);
    float _S652 = as_type<float>((&kernelContext_0)->frameParameters_0[(2092U)>>2]);
    float4 _S653 = float4(_S649, _S650, _S651, _S652);
    float _S654 = as_type<float>((&kernelContext_0)->frameParameters_0[(2096U)>>2]);
    float _S655 = as_type<float>((&kernelContext_0)->frameParameters_0[(2100U)>>2]);
    float _S656 = as_type<float>((&kernelContext_0)->frameParameters_0[(2104U)>>2]);
    float _S657 = as_type<float>((&kernelContext_0)->frameParameters_0[(2108U)>>2]);
    float4 _S658 = float4(_S654, _S655, _S656, _S657);
    float _S659 = as_type<float>((&kernelContext_0)->frameParameters_0[(2112U)>>2]);
    float _S660 = as_type<float>((&kernelContext_0)->frameParameters_0[(2116U)>>2]);
    float _S661 = as_type<float>((&kernelContext_0)->frameParameters_0[(2120U)>>2]);
    float _S662 = as_type<float>((&kernelContext_0)->frameParameters_0[(2124U)>>2]);
    float4 _S663 = float4(_S659, _S660, _S661, _S662);
    float _S664 = as_type<float>((&kernelContext_0)->frameParameters_0[(2128U)>>2]);
    float _S665 = as_type<float>((&kernelContext_0)->frameParameters_0[(2132U)>>2]);
    float _S666 = as_type<float>((&kernelContext_0)->frameParameters_0[(2136U)>>2]);
    float _S667 = as_type<float>((&kernelContext_0)->frameParameters_0[(2140U)>>2]);
    float4 _S668 = float4(_S664, _S665, _S666, _S667);
    float _S669 = as_type<float>((&kernelContext_0)->frameParameters_0[(2144U)>>2]);
    float _S670 = as_type<float>((&kernelContext_0)->frameParameters_0[(2148U)>>2]);
    float _S671 = as_type<float>((&kernelContext_0)->frameParameters_0[(2152U)>>2]);
    float _S672 = as_type<float>((&kernelContext_0)->frameParameters_0[(2156U)>>2]);
    float4 _S673 = float4(_S669, _S670, _S671, _S672);
    float _S674 = as_type<float>((&kernelContext_0)->frameParameters_0[(2160U)>>2]);
    float _S675 = as_type<float>((&kernelContext_0)->frameParameters_0[(2164U)>>2]);
    float _S676 = as_type<float>((&kernelContext_0)->frameParameters_0[(2168U)>>2]);
    float _S677 = as_type<float>((&kernelContext_0)->frameParameters_0[(2172U)>>2]);
    float4 _S678 = float4(_S674, _S675, _S676, _S677);
    float _S679 = as_type<float>((&kernelContext_0)->frameParameters_0[(2176U)>>2]);
    float _S680 = as_type<float>((&kernelContext_0)->frameParameters_0[(2180U)>>2]);
    float _S681 = as_type<float>((&kernelContext_0)->frameParameters_0[(2184U)>>2]);
    float _S682 = as_type<float>((&kernelContext_0)->frameParameters_0[(2188U)>>2]);
    float4 _S683 = float4(_S679, _S680, _S681, _S682);
    float _S684 = as_type<float>((&kernelContext_0)->frameParameters_0[(2192U)>>2]);
    float _S685 = as_type<float>((&kernelContext_0)->frameParameters_0[(2196U)>>2]);
    float _S686 = as_type<float>((&kernelContext_0)->frameParameters_0[(2200U)>>2]);
    float _S687 = as_type<float>((&kernelContext_0)->frameParameters_0[(2204U)>>2]);
    float4 _S688 = float4(_S684, _S685, _S686, _S687);
    float _S689 = as_type<float>((&kernelContext_0)->frameParameters_0[(2208U)>>2]);
    float _S690 = as_type<float>((&kernelContext_0)->frameParameters_0[(2212U)>>2]);
    float _S691 = as_type<float>((&kernelContext_0)->frameParameters_0[(2216U)>>2]);
    float _S692 = as_type<float>((&kernelContext_0)->frameParameters_0[(2220U)>>2]);
    float4 _S693 = float4(_S689, _S690, _S691, _S692);
    float _S694 = as_type<float>((&kernelContext_0)->frameParameters_0[(2224U)>>2]);
    float _S695 = as_type<float>((&kernelContext_0)->frameParameters_0[(2228U)>>2]);
    float _S696 = as_type<float>((&kernelContext_0)->frameParameters_0[(2232U)>>2]);
    float _S697 = as_type<float>((&kernelContext_0)->frameParameters_0[(2236U)>>2]);
    float4 _S698 = float4(_S694, _S695, _S696, _S697);
    float _S699 = as_type<float>((&kernelContext_0)->frameParameters_0[(2240U)>>2]);
    float _S700 = as_type<float>((&kernelContext_0)->frameParameters_0[(2244U)>>2]);
    float _S701 = as_type<float>((&kernelContext_0)->frameParameters_0[(2248U)>>2]);
    float _S702 = as_type<float>((&kernelContext_0)->frameParameters_0[(2252U)>>2]);
    float4 _S703 = float4(_S699, _S700, _S701, _S702);
    float _S704 = as_type<float>((&kernelContext_0)->frameParameters_0[(2256U)>>2]);
    float _S705 = as_type<float>((&kernelContext_0)->frameParameters_0[(2260U)>>2]);
    float _S706 = as_type<float>((&kernelContext_0)->frameParameters_0[(2264U)>>2]);
    float _S707 = as_type<float>((&kernelContext_0)->frameParameters_0[(2268U)>>2]);
    array<float4, int(128)> _S708 = { _S73, _S78, _S83, _S88, _S93, _S98, _S103, _S108, _S113, _S118, _S123, _S128, _S133, _S138, _S143, _S148, _S153, _S158, _S163, _S168, _S173, _S178, _S183, _S188, _S193, _S198, _S203, _S208, _S213, _S218, _S223, _S228, _S233, _S238, _S243, _S248, _S253, _S258, _S263, _S268, _S273, _S278, _S283, _S288, _S293, _S298, _S303, _S308, _S313, _S318, _S323, _S328, _S333, _S338, _S343, _S348, _S353, _S358, _S363, _S368, _S373, _S378, _S383, _S388, _S393, _S398, _S403, _S408, _S413, _S418, _S423, _S428, _S433, _S438, _S443, _S448, _S453, _S458, _S463, _S468, _S473, _S478, _S483, _S488, _S493, _S498, _S503, _S508, _S513, _S518, _S523, _S528, _S533, _S538, _S543, _S548, _S553, _S558, _S563, _S568, _S573, _S578, _S583, _S588, _S593, _S598, _S603, _S608, _S613, _S618, _S623, _S628, _S633, _S638, _S643, _S648, _S653, _S658, _S663, _S668, _S673, _S678, _S683, _S688, _S693, _S698, _S703, float4(_S704, _S705, _S706, _S707) };
    float _S709 = as_type<float>((&kernelContext_0)->frameParameters_0[(2272U)>>2]);
    float _S710 = as_type<float>((&kernelContext_0)->frameParameters_0[(2276U)>>2]);
    float _S711 = as_type<float>((&kernelContext_0)->frameParameters_0[(2280U)>>2]);
    float _S712 = as_type<float>((&kernelContext_0)->frameParameters_0[(2284U)>>2]);
    float4 _S713 = float4(_S709, _S710, _S711, _S712);
    float _S714 = as_type<float>((&kernelContext_0)->frameParameters_0[(2288U)>>2]);
    float _S715 = as_type<float>((&kernelContext_0)->frameParameters_0[(2292U)>>2]);
    float _S716 = as_type<float>((&kernelContext_0)->frameParameters_0[(2296U)>>2]);
    float _S717 = as_type<float>((&kernelContext_0)->frameParameters_0[(2300U)>>2]);
    float4 _S718 = float4(_S714, _S715, _S716, _S717);
    float _S719 = as_type<float>((&kernelContext_0)->frameParameters_0[(2304U)>>2]);
    float _S720 = as_type<float>((&kernelContext_0)->frameParameters_0[(2308U)>>2]);
    float _S721 = as_type<float>((&kernelContext_0)->frameParameters_0[(2312U)>>2]);
    float _S722 = as_type<float>((&kernelContext_0)->frameParameters_0[(2316U)>>2]);
    float4 _S723 = float4(_S719, _S720, _S721, _S722);
    float _S724 = as_type<float>((&kernelContext_0)->frameParameters_0[(2320U)>>2]);
    float _S725 = as_type<float>((&kernelContext_0)->frameParameters_0[(2324U)>>2]);
    float _S726 = as_type<float>((&kernelContext_0)->frameParameters_0[(2328U)>>2]);
    float _S727 = as_type<float>((&kernelContext_0)->frameParameters_0[(2332U)>>2]);
    float4 _S728 = float4(_S724, _S725, _S726, _S727);
    float _S729 = as_type<float>((&kernelContext_0)->frameParameters_0[(2336U)>>2]);
    float _S730 = as_type<float>((&kernelContext_0)->frameParameters_0[(2340U)>>2]);
    float _S731 = as_type<float>((&kernelContext_0)->frameParameters_0[(2344U)>>2]);
    float _S732 = as_type<float>((&kernelContext_0)->frameParameters_0[(2348U)>>2]);
    float4 _S733 = float4(_S729, _S730, _S731, _S732);
    float _S734 = as_type<float>((&kernelContext_0)->frameParameters_0[(2352U)>>2]);
    float _S735 = as_type<float>((&kernelContext_0)->frameParameters_0[(2356U)>>2]);
    float _S736 = as_type<float>((&kernelContext_0)->frameParameters_0[(2360U)>>2]);
    float _S737 = as_type<float>((&kernelContext_0)->frameParameters_0[(2364U)>>2]);
    float4 _S738 = float4(_S734, _S735, _S736, _S737);
    float _S739 = as_type<float>((&kernelContext_0)->frameParameters_0[(2368U)>>2]);
    float _S740 = as_type<float>((&kernelContext_0)->frameParameters_0[(2372U)>>2]);
    float _S741 = as_type<float>((&kernelContext_0)->frameParameters_0[(2376U)>>2]);
    float _S742 = as_type<float>((&kernelContext_0)->frameParameters_0[(2380U)>>2]);
    float4 _S743 = float4(_S739, _S740, _S741, _S742);
    float _S744 = as_type<float>((&kernelContext_0)->frameParameters_0[(2384U)>>2]);
    float _S745 = as_type<float>((&kernelContext_0)->frameParameters_0[(2388U)>>2]);
    float _S746 = as_type<float>((&kernelContext_0)->frameParameters_0[(2392U)>>2]);
    float _S747 = as_type<float>((&kernelContext_0)->frameParameters_0[(2396U)>>2]);
    float4 _S748 = float4(_S744, _S745, _S746, _S747);
    float _S749 = as_type<float>((&kernelContext_0)->frameParameters_0[(2400U)>>2]);
    float _S750 = as_type<float>((&kernelContext_0)->frameParameters_0[(2404U)>>2]);
    float _S751 = as_type<float>((&kernelContext_0)->frameParameters_0[(2408U)>>2]);
    float _S752 = as_type<float>((&kernelContext_0)->frameParameters_0[(2412U)>>2]);
    float4 _S753 = float4(_S749, _S750, _S751, _S752);
    float _S754 = as_type<float>((&kernelContext_0)->frameParameters_0[(2416U)>>2]);
    float _S755 = as_type<float>((&kernelContext_0)->frameParameters_0[(2420U)>>2]);
    float _S756 = as_type<float>((&kernelContext_0)->frameParameters_0[(2424U)>>2]);
    float _S757 = as_type<float>((&kernelContext_0)->frameParameters_0[(2428U)>>2]);
    float4 _S758 = float4(_S754, _S755, _S756, _S757);
    float _S759 = as_type<float>((&kernelContext_0)->frameParameters_0[(2432U)>>2]);
    float _S760 = as_type<float>((&kernelContext_0)->frameParameters_0[(2436U)>>2]);
    float _S761 = as_type<float>((&kernelContext_0)->frameParameters_0[(2440U)>>2]);
    float _S762 = as_type<float>((&kernelContext_0)->frameParameters_0[(2444U)>>2]);
    float4 _S763 = float4(_S759, _S760, _S761, _S762);
    float _S764 = as_type<float>((&kernelContext_0)->frameParameters_0[(2448U)>>2]);
    float _S765 = as_type<float>((&kernelContext_0)->frameParameters_0[(2452U)>>2]);
    float _S766 = as_type<float>((&kernelContext_0)->frameParameters_0[(2456U)>>2]);
    float _S767 = as_type<float>((&kernelContext_0)->frameParameters_0[(2460U)>>2]);
    float4 _S768 = float4(_S764, _S765, _S766, _S767);
    float _S769 = as_type<float>((&kernelContext_0)->frameParameters_0[(2464U)>>2]);
    float _S770 = as_type<float>((&kernelContext_0)->frameParameters_0[(2468U)>>2]);
    float _S771 = as_type<float>((&kernelContext_0)->frameParameters_0[(2472U)>>2]);
    float _S772 = as_type<float>((&kernelContext_0)->frameParameters_0[(2476U)>>2]);
    float4 _S773 = float4(_S769, _S770, _S771, _S772);
    float _S774 = as_type<float>((&kernelContext_0)->frameParameters_0[(2480U)>>2]);
    float _S775 = as_type<float>((&kernelContext_0)->frameParameters_0[(2484U)>>2]);
    float _S776 = as_type<float>((&kernelContext_0)->frameParameters_0[(2488U)>>2]);
    float _S777 = as_type<float>((&kernelContext_0)->frameParameters_0[(2492U)>>2]);
    float4 _S778 = float4(_S774, _S775, _S776, _S777);
    float _S779 = as_type<float>((&kernelContext_0)->frameParameters_0[(2496U)>>2]);
    float _S780 = as_type<float>((&kernelContext_0)->frameParameters_0[(2500U)>>2]);
    float _S781 = as_type<float>((&kernelContext_0)->frameParameters_0[(2504U)>>2]);
    float _S782 = as_type<float>((&kernelContext_0)->frameParameters_0[(2508U)>>2]);
    float4 _S783 = float4(_S779, _S780, _S781, _S782);
    float _S784 = as_type<float>((&kernelContext_0)->frameParameters_0[(2512U)>>2]);
    float _S785 = as_type<float>((&kernelContext_0)->frameParameters_0[(2516U)>>2]);
    float _S786 = as_type<float>((&kernelContext_0)->frameParameters_0[(2520U)>>2]);
    float _S787 = as_type<float>((&kernelContext_0)->frameParameters_0[(2524U)>>2]);
    float4 _S788 = float4(_S784, _S785, _S786, _S787);
    float _S789 = as_type<float>((&kernelContext_0)->frameParameters_0[(2528U)>>2]);
    float _S790 = as_type<float>((&kernelContext_0)->frameParameters_0[(2532U)>>2]);
    float _S791 = as_type<float>((&kernelContext_0)->frameParameters_0[(2536U)>>2]);
    float _S792 = as_type<float>((&kernelContext_0)->frameParameters_0[(2540U)>>2]);
    float4 _S793 = float4(_S789, _S790, _S791, _S792);
    float _S794 = as_type<float>((&kernelContext_0)->frameParameters_0[(2544U)>>2]);
    float _S795 = as_type<float>((&kernelContext_0)->frameParameters_0[(2548U)>>2]);
    float _S796 = as_type<float>((&kernelContext_0)->frameParameters_0[(2552U)>>2]);
    float _S797 = as_type<float>((&kernelContext_0)->frameParameters_0[(2556U)>>2]);
    float4 _S798 = float4(_S794, _S795, _S796, _S797);
    float _S799 = as_type<float>((&kernelContext_0)->frameParameters_0[(2560U)>>2]);
    float _S800 = as_type<float>((&kernelContext_0)->frameParameters_0[(2564U)>>2]);
    float _S801 = as_type<float>((&kernelContext_0)->frameParameters_0[(2568U)>>2]);
    float _S802 = as_type<float>((&kernelContext_0)->frameParameters_0[(2572U)>>2]);
    float4 _S803 = float4(_S799, _S800, _S801, _S802);
    float _S804 = as_type<float>((&kernelContext_0)->frameParameters_0[(2576U)>>2]);
    float _S805 = as_type<float>((&kernelContext_0)->frameParameters_0[(2580U)>>2]);
    float _S806 = as_type<float>((&kernelContext_0)->frameParameters_0[(2584U)>>2]);
    float _S807 = as_type<float>((&kernelContext_0)->frameParameters_0[(2588U)>>2]);
    float4 _S808 = float4(_S804, _S805, _S806, _S807);
    float _S809 = as_type<float>((&kernelContext_0)->frameParameters_0[(2592U)>>2]);
    float _S810 = as_type<float>((&kernelContext_0)->frameParameters_0[(2596U)>>2]);
    float _S811 = as_type<float>((&kernelContext_0)->frameParameters_0[(2600U)>>2]);
    float _S812 = as_type<float>((&kernelContext_0)->frameParameters_0[(2604U)>>2]);
    float4 _S813 = float4(_S809, _S810, _S811, _S812);
    float _S814 = as_type<float>((&kernelContext_0)->frameParameters_0[(2608U)>>2]);
    float _S815 = as_type<float>((&kernelContext_0)->frameParameters_0[(2612U)>>2]);
    float _S816 = as_type<float>((&kernelContext_0)->frameParameters_0[(2616U)>>2]);
    float _S817 = as_type<float>((&kernelContext_0)->frameParameters_0[(2620U)>>2]);
    float4 _S818 = float4(_S814, _S815, _S816, _S817);
    float _S819 = as_type<float>((&kernelContext_0)->frameParameters_0[(2624U)>>2]);
    float _S820 = as_type<float>((&kernelContext_0)->frameParameters_0[(2628U)>>2]);
    float _S821 = as_type<float>((&kernelContext_0)->frameParameters_0[(2632U)>>2]);
    float _S822 = as_type<float>((&kernelContext_0)->frameParameters_0[(2636U)>>2]);
    float4 _S823 = float4(_S819, _S820, _S821, _S822);
    float _S824 = as_type<float>((&kernelContext_0)->frameParameters_0[(2640U)>>2]);
    float _S825 = as_type<float>((&kernelContext_0)->frameParameters_0[(2644U)>>2]);
    float _S826 = as_type<float>((&kernelContext_0)->frameParameters_0[(2648U)>>2]);
    float _S827 = as_type<float>((&kernelContext_0)->frameParameters_0[(2652U)>>2]);
    float4 _S828 = float4(_S824, _S825, _S826, _S827);
    float _S829 = as_type<float>((&kernelContext_0)->frameParameters_0[(2656U)>>2]);
    float _S830 = as_type<float>((&kernelContext_0)->frameParameters_0[(2660U)>>2]);
    float _S831 = as_type<float>((&kernelContext_0)->frameParameters_0[(2664U)>>2]);
    float _S832 = as_type<float>((&kernelContext_0)->frameParameters_0[(2668U)>>2]);
    float4 _S833 = float4(_S829, _S830, _S831, _S832);
    float _S834 = as_type<float>((&kernelContext_0)->frameParameters_0[(2672U)>>2]);
    float _S835 = as_type<float>((&kernelContext_0)->frameParameters_0[(2676U)>>2]);
    float _S836 = as_type<float>((&kernelContext_0)->frameParameters_0[(2680U)>>2]);
    float _S837 = as_type<float>((&kernelContext_0)->frameParameters_0[(2684U)>>2]);
    float4 _S838 = float4(_S834, _S835, _S836, _S837);
    float _S839 = as_type<float>((&kernelContext_0)->frameParameters_0[(2688U)>>2]);
    float _S840 = as_type<float>((&kernelContext_0)->frameParameters_0[(2692U)>>2]);
    float _S841 = as_type<float>((&kernelContext_0)->frameParameters_0[(2696U)>>2]);
    float _S842 = as_type<float>((&kernelContext_0)->frameParameters_0[(2700U)>>2]);
    float4 _S843 = float4(_S839, _S840, _S841, _S842);
    float _S844 = as_type<float>((&kernelContext_0)->frameParameters_0[(2704U)>>2]);
    float _S845 = as_type<float>((&kernelContext_0)->frameParameters_0[(2708U)>>2]);
    float _S846 = as_type<float>((&kernelContext_0)->frameParameters_0[(2712U)>>2]);
    float _S847 = as_type<float>((&kernelContext_0)->frameParameters_0[(2716U)>>2]);
    float4 _S848 = float4(_S844, _S845, _S846, _S847);
    float _S849 = as_type<float>((&kernelContext_0)->frameParameters_0[(2720U)>>2]);
    float _S850 = as_type<float>((&kernelContext_0)->frameParameters_0[(2724U)>>2]);
    float _S851 = as_type<float>((&kernelContext_0)->frameParameters_0[(2728U)>>2]);
    float _S852 = as_type<float>((&kernelContext_0)->frameParameters_0[(2732U)>>2]);
    float4 _S853 = float4(_S849, _S850, _S851, _S852);
    float _S854 = as_type<float>((&kernelContext_0)->frameParameters_0[(2736U)>>2]);
    float _S855 = as_type<float>((&kernelContext_0)->frameParameters_0[(2740U)>>2]);
    float _S856 = as_type<float>((&kernelContext_0)->frameParameters_0[(2744U)>>2]);
    float _S857 = as_type<float>((&kernelContext_0)->frameParameters_0[(2748U)>>2]);
    float4 _S858 = float4(_S854, _S855, _S856, _S857);
    float _S859 = as_type<float>((&kernelContext_0)->frameParameters_0[(2752U)>>2]);
    float _S860 = as_type<float>((&kernelContext_0)->frameParameters_0[(2756U)>>2]);
    float _S861 = as_type<float>((&kernelContext_0)->frameParameters_0[(2760U)>>2]);
    float _S862 = as_type<float>((&kernelContext_0)->frameParameters_0[(2764U)>>2]);
    float4 _S863 = float4(_S859, _S860, _S861, _S862);
    float _S864 = as_type<float>((&kernelContext_0)->frameParameters_0[(2768U)>>2]);
    float _S865 = as_type<float>((&kernelContext_0)->frameParameters_0[(2772U)>>2]);
    float _S866 = as_type<float>((&kernelContext_0)->frameParameters_0[(2776U)>>2]);
    float _S867 = as_type<float>((&kernelContext_0)->frameParameters_0[(2780U)>>2]);
    float4 _S868 = float4(_S864, _S865, _S866, _S867);
    float _S869 = as_type<float>((&kernelContext_0)->frameParameters_0[(2784U)>>2]);
    float _S870 = as_type<float>((&kernelContext_0)->frameParameters_0[(2788U)>>2]);
    float _S871 = as_type<float>((&kernelContext_0)->frameParameters_0[(2792U)>>2]);
    float _S872 = as_type<float>((&kernelContext_0)->frameParameters_0[(2796U)>>2]);
    float4 _S873 = float4(_S869, _S870, _S871, _S872);
    float _S874 = as_type<float>((&kernelContext_0)->frameParameters_0[(2800U)>>2]);
    float _S875 = as_type<float>((&kernelContext_0)->frameParameters_0[(2804U)>>2]);
    float _S876 = as_type<float>((&kernelContext_0)->frameParameters_0[(2808U)>>2]);
    float _S877 = as_type<float>((&kernelContext_0)->frameParameters_0[(2812U)>>2]);
    float4 _S878 = float4(_S874, _S875, _S876, _S877);
    float _S879 = as_type<float>((&kernelContext_0)->frameParameters_0[(2816U)>>2]);
    float _S880 = as_type<float>((&kernelContext_0)->frameParameters_0[(2820U)>>2]);
    float _S881 = as_type<float>((&kernelContext_0)->frameParameters_0[(2824U)>>2]);
    float _S882 = as_type<float>((&kernelContext_0)->frameParameters_0[(2828U)>>2]);
    float4 _S883 = float4(_S879, _S880, _S881, _S882);
    float _S884 = as_type<float>((&kernelContext_0)->frameParameters_0[(2832U)>>2]);
    float _S885 = as_type<float>((&kernelContext_0)->frameParameters_0[(2836U)>>2]);
    float _S886 = as_type<float>((&kernelContext_0)->frameParameters_0[(2840U)>>2]);
    float _S887 = as_type<float>((&kernelContext_0)->frameParameters_0[(2844U)>>2]);
    float4 _S888 = float4(_S884, _S885, _S886, _S887);
    float _S889 = as_type<float>((&kernelContext_0)->frameParameters_0[(2848U)>>2]);
    float _S890 = as_type<float>((&kernelContext_0)->frameParameters_0[(2852U)>>2]);
    float _S891 = as_type<float>((&kernelContext_0)->frameParameters_0[(2856U)>>2]);
    float _S892 = as_type<float>((&kernelContext_0)->frameParameters_0[(2860U)>>2]);
    float4 _S893 = float4(_S889, _S890, _S891, _S892);
    float _S894 = as_type<float>((&kernelContext_0)->frameParameters_0[(2864U)>>2]);
    float _S895 = as_type<float>((&kernelContext_0)->frameParameters_0[(2868U)>>2]);
    float _S896 = as_type<float>((&kernelContext_0)->frameParameters_0[(2872U)>>2]);
    float _S897 = as_type<float>((&kernelContext_0)->frameParameters_0[(2876U)>>2]);
    float4 _S898 = float4(_S894, _S895, _S896, _S897);
    float _S899 = as_type<float>((&kernelContext_0)->frameParameters_0[(2880U)>>2]);
    float _S900 = as_type<float>((&kernelContext_0)->frameParameters_0[(2884U)>>2]);
    float _S901 = as_type<float>((&kernelContext_0)->frameParameters_0[(2888U)>>2]);
    float _S902 = as_type<float>((&kernelContext_0)->frameParameters_0[(2892U)>>2]);
    float4 _S903 = float4(_S899, _S900, _S901, _S902);
    float _S904 = as_type<float>((&kernelContext_0)->frameParameters_0[(2896U)>>2]);
    float _S905 = as_type<float>((&kernelContext_0)->frameParameters_0[(2900U)>>2]);
    float _S906 = as_type<float>((&kernelContext_0)->frameParameters_0[(2904U)>>2]);
    float _S907 = as_type<float>((&kernelContext_0)->frameParameters_0[(2908U)>>2]);
    float4 _S908 = float4(_S904, _S905, _S906, _S907);
    float _S909 = as_type<float>((&kernelContext_0)->frameParameters_0[(2912U)>>2]);
    float _S910 = as_type<float>((&kernelContext_0)->frameParameters_0[(2916U)>>2]);
    float _S911 = as_type<float>((&kernelContext_0)->frameParameters_0[(2920U)>>2]);
    float _S912 = as_type<float>((&kernelContext_0)->frameParameters_0[(2924U)>>2]);
    float4 _S913 = float4(_S909, _S910, _S911, _S912);
    float _S914 = as_type<float>((&kernelContext_0)->frameParameters_0[(2928U)>>2]);
    float _S915 = as_type<float>((&kernelContext_0)->frameParameters_0[(2932U)>>2]);
    float _S916 = as_type<float>((&kernelContext_0)->frameParameters_0[(2936U)>>2]);
    float _S917 = as_type<float>((&kernelContext_0)->frameParameters_0[(2940U)>>2]);
    float4 _S918 = float4(_S914, _S915, _S916, _S917);
    float _S919 = as_type<float>((&kernelContext_0)->frameParameters_0[(2944U)>>2]);
    float _S920 = as_type<float>((&kernelContext_0)->frameParameters_0[(2948U)>>2]);
    float _S921 = as_type<float>((&kernelContext_0)->frameParameters_0[(2952U)>>2]);
    float _S922 = as_type<float>((&kernelContext_0)->frameParameters_0[(2956U)>>2]);
    float4 _S923 = float4(_S919, _S920, _S921, _S922);
    float _S924 = as_type<float>((&kernelContext_0)->frameParameters_0[(2960U)>>2]);
    float _S925 = as_type<float>((&kernelContext_0)->frameParameters_0[(2964U)>>2]);
    float _S926 = as_type<float>((&kernelContext_0)->frameParameters_0[(2968U)>>2]);
    float _S927 = as_type<float>((&kernelContext_0)->frameParameters_0[(2972U)>>2]);
    float4 _S928 = float4(_S924, _S925, _S926, _S927);
    float _S929 = as_type<float>((&kernelContext_0)->frameParameters_0[(2976U)>>2]);
    float _S930 = as_type<float>((&kernelContext_0)->frameParameters_0[(2980U)>>2]);
    float _S931 = as_type<float>((&kernelContext_0)->frameParameters_0[(2984U)>>2]);
    float _S932 = as_type<float>((&kernelContext_0)->frameParameters_0[(2988U)>>2]);
    float4 _S933 = float4(_S929, _S930, _S931, _S932);
    float _S934 = as_type<float>((&kernelContext_0)->frameParameters_0[(2992U)>>2]);
    float _S935 = as_type<float>((&kernelContext_0)->frameParameters_0[(2996U)>>2]);
    float _S936 = as_type<float>((&kernelContext_0)->frameParameters_0[(3000U)>>2]);
    float _S937 = as_type<float>((&kernelContext_0)->frameParameters_0[(3004U)>>2]);
    float4 _S938 = float4(_S934, _S935, _S936, _S937);
    float _S939 = as_type<float>((&kernelContext_0)->frameParameters_0[(3008U)>>2]);
    float _S940 = as_type<float>((&kernelContext_0)->frameParameters_0[(3012U)>>2]);
    float _S941 = as_type<float>((&kernelContext_0)->frameParameters_0[(3016U)>>2]);
    float _S942 = as_type<float>((&kernelContext_0)->frameParameters_0[(3020U)>>2]);
    float4 _S943 = float4(_S939, _S940, _S941, _S942);
    float _S944 = as_type<float>((&kernelContext_0)->frameParameters_0[(3024U)>>2]);
    float _S945 = as_type<float>((&kernelContext_0)->frameParameters_0[(3028U)>>2]);
    float _S946 = as_type<float>((&kernelContext_0)->frameParameters_0[(3032U)>>2]);
    float _S947 = as_type<float>((&kernelContext_0)->frameParameters_0[(3036U)>>2]);
    float4 _S948 = float4(_S944, _S945, _S946, _S947);
    float _S949 = as_type<float>((&kernelContext_0)->frameParameters_0[(3040U)>>2]);
    float _S950 = as_type<float>((&kernelContext_0)->frameParameters_0[(3044U)>>2]);
    float _S951 = as_type<float>((&kernelContext_0)->frameParameters_0[(3048U)>>2]);
    float _S952 = as_type<float>((&kernelContext_0)->frameParameters_0[(3052U)>>2]);
    float4 _S953 = float4(_S949, _S950, _S951, _S952);
    float _S954 = as_type<float>((&kernelContext_0)->frameParameters_0[(3056U)>>2]);
    float _S955 = as_type<float>((&kernelContext_0)->frameParameters_0[(3060U)>>2]);
    float _S956 = as_type<float>((&kernelContext_0)->frameParameters_0[(3064U)>>2]);
    float _S957 = as_type<float>((&kernelContext_0)->frameParameters_0[(3068U)>>2]);
    float4 _S958 = float4(_S954, _S955, _S956, _S957);
    float _S959 = as_type<float>((&kernelContext_0)->frameParameters_0[(3072U)>>2]);
    float _S960 = as_type<float>((&kernelContext_0)->frameParameters_0[(3076U)>>2]);
    float _S961 = as_type<float>((&kernelContext_0)->frameParameters_0[(3080U)>>2]);
    float _S962 = as_type<float>((&kernelContext_0)->frameParameters_0[(3084U)>>2]);
    float4 _S963 = float4(_S959, _S960, _S961, _S962);
    float _S964 = as_type<float>((&kernelContext_0)->frameParameters_0[(3088U)>>2]);
    float _S965 = as_type<float>((&kernelContext_0)->frameParameters_0[(3092U)>>2]);
    float _S966 = as_type<float>((&kernelContext_0)->frameParameters_0[(3096U)>>2]);
    float _S967 = as_type<float>((&kernelContext_0)->frameParameters_0[(3100U)>>2]);
    float4 _S968 = float4(_S964, _S965, _S966, _S967);
    float _S969 = as_type<float>((&kernelContext_0)->frameParameters_0[(3104U)>>2]);
    float _S970 = as_type<float>((&kernelContext_0)->frameParameters_0[(3108U)>>2]);
    float _S971 = as_type<float>((&kernelContext_0)->frameParameters_0[(3112U)>>2]);
    float _S972 = as_type<float>((&kernelContext_0)->frameParameters_0[(3116U)>>2]);
    float4 _S973 = float4(_S969, _S970, _S971, _S972);
    float _S974 = as_type<float>((&kernelContext_0)->frameParameters_0[(3120U)>>2]);
    float _S975 = as_type<float>((&kernelContext_0)->frameParameters_0[(3124U)>>2]);
    float _S976 = as_type<float>((&kernelContext_0)->frameParameters_0[(3128U)>>2]);
    float _S977 = as_type<float>((&kernelContext_0)->frameParameters_0[(3132U)>>2]);
    float4 _S978 = float4(_S974, _S975, _S976, _S977);
    float _S979 = as_type<float>((&kernelContext_0)->frameParameters_0[(3136U)>>2]);
    float _S980 = as_type<float>((&kernelContext_0)->frameParameters_0[(3140U)>>2]);
    float _S981 = as_type<float>((&kernelContext_0)->frameParameters_0[(3144U)>>2]);
    float _S982 = as_type<float>((&kernelContext_0)->frameParameters_0[(3148U)>>2]);
    float4 _S983 = float4(_S979, _S980, _S981, _S982);
    float _S984 = as_type<float>((&kernelContext_0)->frameParameters_0[(3152U)>>2]);
    float _S985 = as_type<float>((&kernelContext_0)->frameParameters_0[(3156U)>>2]);
    float _S986 = as_type<float>((&kernelContext_0)->frameParameters_0[(3160U)>>2]);
    float _S987 = as_type<float>((&kernelContext_0)->frameParameters_0[(3164U)>>2]);
    float4 _S988 = float4(_S984, _S985, _S986, _S987);
    float _S989 = as_type<float>((&kernelContext_0)->frameParameters_0[(3168U)>>2]);
    float _S990 = as_type<float>((&kernelContext_0)->frameParameters_0[(3172U)>>2]);
    float _S991 = as_type<float>((&kernelContext_0)->frameParameters_0[(3176U)>>2]);
    float _S992 = as_type<float>((&kernelContext_0)->frameParameters_0[(3180U)>>2]);
    float4 _S993 = float4(_S989, _S990, _S991, _S992);
    float _S994 = as_type<float>((&kernelContext_0)->frameParameters_0[(3184U)>>2]);
    float _S995 = as_type<float>((&kernelContext_0)->frameParameters_0[(3188U)>>2]);
    float _S996 = as_type<float>((&kernelContext_0)->frameParameters_0[(3192U)>>2]);
    float _S997 = as_type<float>((&kernelContext_0)->frameParameters_0[(3196U)>>2]);
    float4 _S998 = float4(_S994, _S995, _S996, _S997);
    float _S999 = as_type<float>((&kernelContext_0)->frameParameters_0[(3200U)>>2]);
    float _S1000 = as_type<float>((&kernelContext_0)->frameParameters_0[(3204U)>>2]);
    float _S1001 = as_type<float>((&kernelContext_0)->frameParameters_0[(3208U)>>2]);
    float _S1002 = as_type<float>((&kernelContext_0)->frameParameters_0[(3212U)>>2]);
    float4 _S1003 = float4(_S999, _S1000, _S1001, _S1002);
    float _S1004 = as_type<float>((&kernelContext_0)->frameParameters_0[(3216U)>>2]);
    float _S1005 = as_type<float>((&kernelContext_0)->frameParameters_0[(3220U)>>2]);
    float _S1006 = as_type<float>((&kernelContext_0)->frameParameters_0[(3224U)>>2]);
    float _S1007 = as_type<float>((&kernelContext_0)->frameParameters_0[(3228U)>>2]);
    float4 _S1008 = float4(_S1004, _S1005, _S1006, _S1007);
    float _S1009 = as_type<float>((&kernelContext_0)->frameParameters_0[(3232U)>>2]);
    float _S1010 = as_type<float>((&kernelContext_0)->frameParameters_0[(3236U)>>2]);
    float _S1011 = as_type<float>((&kernelContext_0)->frameParameters_0[(3240U)>>2]);
    float _S1012 = as_type<float>((&kernelContext_0)->frameParameters_0[(3244U)>>2]);
    float4 _S1013 = float4(_S1009, _S1010, _S1011, _S1012);
    float _S1014 = as_type<float>((&kernelContext_0)->frameParameters_0[(3248U)>>2]);
    float _S1015 = as_type<float>((&kernelContext_0)->frameParameters_0[(3252U)>>2]);
    float _S1016 = as_type<float>((&kernelContext_0)->frameParameters_0[(3256U)>>2]);
    float _S1017 = as_type<float>((&kernelContext_0)->frameParameters_0[(3260U)>>2]);
    float4 _S1018 = float4(_S1014, _S1015, _S1016, _S1017);
    float _S1019 = as_type<float>((&kernelContext_0)->frameParameters_0[(3264U)>>2]);
    float _S1020 = as_type<float>((&kernelContext_0)->frameParameters_0[(3268U)>>2]);
    float _S1021 = as_type<float>((&kernelContext_0)->frameParameters_0[(3272U)>>2]);
    float _S1022 = as_type<float>((&kernelContext_0)->frameParameters_0[(3276U)>>2]);
    float4 _S1023 = float4(_S1019, _S1020, _S1021, _S1022);
    float _S1024 = as_type<float>((&kernelContext_0)->frameParameters_0[(3280U)>>2]);
    float _S1025 = as_type<float>((&kernelContext_0)->frameParameters_0[(3284U)>>2]);
    float _S1026 = as_type<float>((&kernelContext_0)->frameParameters_0[(3288U)>>2]);
    float _S1027 = as_type<float>((&kernelContext_0)->frameParameters_0[(3292U)>>2]);
    float4 _S1028 = float4(_S1024, _S1025, _S1026, _S1027);
    float _S1029 = as_type<float>((&kernelContext_0)->frameParameters_0[(3296U)>>2]);
    float _S1030 = as_type<float>((&kernelContext_0)->frameParameters_0[(3300U)>>2]);
    float _S1031 = as_type<float>((&kernelContext_0)->frameParameters_0[(3304U)>>2]);
    float _S1032 = as_type<float>((&kernelContext_0)->frameParameters_0[(3308U)>>2]);
    float4 _S1033 = float4(_S1029, _S1030, _S1031, _S1032);
    float _S1034 = as_type<float>((&kernelContext_0)->frameParameters_0[(3312U)>>2]);
    float _S1035 = as_type<float>((&kernelContext_0)->frameParameters_0[(3316U)>>2]);
    float _S1036 = as_type<float>((&kernelContext_0)->frameParameters_0[(3320U)>>2]);
    float _S1037 = as_type<float>((&kernelContext_0)->frameParameters_0[(3324U)>>2]);
    float4 _S1038 = float4(_S1034, _S1035, _S1036, _S1037);
    float _S1039 = as_type<float>((&kernelContext_0)->frameParameters_0[(3328U)>>2]);
    float _S1040 = as_type<float>((&kernelContext_0)->frameParameters_0[(3332U)>>2]);
    float _S1041 = as_type<float>((&kernelContext_0)->frameParameters_0[(3336U)>>2]);
    float _S1042 = as_type<float>((&kernelContext_0)->frameParameters_0[(3340U)>>2]);
    float4 _S1043 = float4(_S1039, _S1040, _S1041, _S1042);
    float _S1044 = as_type<float>((&kernelContext_0)->frameParameters_0[(3344U)>>2]);
    float _S1045 = as_type<float>((&kernelContext_0)->frameParameters_0[(3348U)>>2]);
    float _S1046 = as_type<float>((&kernelContext_0)->frameParameters_0[(3352U)>>2]);
    float _S1047 = as_type<float>((&kernelContext_0)->frameParameters_0[(3356U)>>2]);
    float4 _S1048 = float4(_S1044, _S1045, _S1046, _S1047);
    float _S1049 = as_type<float>((&kernelContext_0)->frameParameters_0[(3360U)>>2]);
    float _S1050 = as_type<float>((&kernelContext_0)->frameParameters_0[(3364U)>>2]);
    float _S1051 = as_type<float>((&kernelContext_0)->frameParameters_0[(3368U)>>2]);
    float _S1052 = as_type<float>((&kernelContext_0)->frameParameters_0[(3372U)>>2]);
    float4 _S1053 = float4(_S1049, _S1050, _S1051, _S1052);
    float _S1054 = as_type<float>((&kernelContext_0)->frameParameters_0[(3376U)>>2]);
    float _S1055 = as_type<float>((&kernelContext_0)->frameParameters_0[(3380U)>>2]);
    float _S1056 = as_type<float>((&kernelContext_0)->frameParameters_0[(3384U)>>2]);
    float _S1057 = as_type<float>((&kernelContext_0)->frameParameters_0[(3388U)>>2]);
    float4 _S1058 = float4(_S1054, _S1055, _S1056, _S1057);
    float _S1059 = as_type<float>((&kernelContext_0)->frameParameters_0[(3392U)>>2]);
    float _S1060 = as_type<float>((&kernelContext_0)->frameParameters_0[(3396U)>>2]);
    float _S1061 = as_type<float>((&kernelContext_0)->frameParameters_0[(3400U)>>2]);
    float _S1062 = as_type<float>((&kernelContext_0)->frameParameters_0[(3404U)>>2]);
    float4 _S1063 = float4(_S1059, _S1060, _S1061, _S1062);
    float _S1064 = as_type<float>((&kernelContext_0)->frameParameters_0[(3408U)>>2]);
    float _S1065 = as_type<float>((&kernelContext_0)->frameParameters_0[(3412U)>>2]);
    float _S1066 = as_type<float>((&kernelContext_0)->frameParameters_0[(3416U)>>2]);
    float _S1067 = as_type<float>((&kernelContext_0)->frameParameters_0[(3420U)>>2]);
    float4 _S1068 = float4(_S1064, _S1065, _S1066, _S1067);
    float _S1069 = as_type<float>((&kernelContext_0)->frameParameters_0[(3424U)>>2]);
    float _S1070 = as_type<float>((&kernelContext_0)->frameParameters_0[(3428U)>>2]);
    float _S1071 = as_type<float>((&kernelContext_0)->frameParameters_0[(3432U)>>2]);
    float _S1072 = as_type<float>((&kernelContext_0)->frameParameters_0[(3436U)>>2]);
    float4 _S1073 = float4(_S1069, _S1070, _S1071, _S1072);
    float _S1074 = as_type<float>((&kernelContext_0)->frameParameters_0[(3440U)>>2]);
    float _S1075 = as_type<float>((&kernelContext_0)->frameParameters_0[(3444U)>>2]);
    float _S1076 = as_type<float>((&kernelContext_0)->frameParameters_0[(3448U)>>2]);
    float _S1077 = as_type<float>((&kernelContext_0)->frameParameters_0[(3452U)>>2]);
    float4 _S1078 = float4(_S1074, _S1075, _S1076, _S1077);
    float _S1079 = as_type<float>((&kernelContext_0)->frameParameters_0[(3456U)>>2]);
    float _S1080 = as_type<float>((&kernelContext_0)->frameParameters_0[(3460U)>>2]);
    float _S1081 = as_type<float>((&kernelContext_0)->frameParameters_0[(3464U)>>2]);
    float _S1082 = as_type<float>((&kernelContext_0)->frameParameters_0[(3468U)>>2]);
    float4 _S1083 = float4(_S1079, _S1080, _S1081, _S1082);
    float _S1084 = as_type<float>((&kernelContext_0)->frameParameters_0[(3472U)>>2]);
    float _S1085 = as_type<float>((&kernelContext_0)->frameParameters_0[(3476U)>>2]);
    float _S1086 = as_type<float>((&kernelContext_0)->frameParameters_0[(3480U)>>2]);
    float _S1087 = as_type<float>((&kernelContext_0)->frameParameters_0[(3484U)>>2]);
    float4 _S1088 = float4(_S1084, _S1085, _S1086, _S1087);
    float _S1089 = as_type<float>((&kernelContext_0)->frameParameters_0[(3488U)>>2]);
    float _S1090 = as_type<float>((&kernelContext_0)->frameParameters_0[(3492U)>>2]);
    float _S1091 = as_type<float>((&kernelContext_0)->frameParameters_0[(3496U)>>2]);
    float _S1092 = as_type<float>((&kernelContext_0)->frameParameters_0[(3500U)>>2]);
    float4 _S1093 = float4(_S1089, _S1090, _S1091, _S1092);
    float _S1094 = as_type<float>((&kernelContext_0)->frameParameters_0[(3504U)>>2]);
    float _S1095 = as_type<float>((&kernelContext_0)->frameParameters_0[(3508U)>>2]);
    float _S1096 = as_type<float>((&kernelContext_0)->frameParameters_0[(3512U)>>2]);
    float _S1097 = as_type<float>((&kernelContext_0)->frameParameters_0[(3516U)>>2]);
    float4 _S1098 = float4(_S1094, _S1095, _S1096, _S1097);
    float _S1099 = as_type<float>((&kernelContext_0)->frameParameters_0[(3520U)>>2]);
    float _S1100 = as_type<float>((&kernelContext_0)->frameParameters_0[(3524U)>>2]);
    float _S1101 = as_type<float>((&kernelContext_0)->frameParameters_0[(3528U)>>2]);
    float _S1102 = as_type<float>((&kernelContext_0)->frameParameters_0[(3532U)>>2]);
    float4 _S1103 = float4(_S1099, _S1100, _S1101, _S1102);
    float _S1104 = as_type<float>((&kernelContext_0)->frameParameters_0[(3536U)>>2]);
    float _S1105 = as_type<float>((&kernelContext_0)->frameParameters_0[(3540U)>>2]);
    float _S1106 = as_type<float>((&kernelContext_0)->frameParameters_0[(3544U)>>2]);
    float _S1107 = as_type<float>((&kernelContext_0)->frameParameters_0[(3548U)>>2]);
    float4 _S1108 = float4(_S1104, _S1105, _S1106, _S1107);
    float _S1109 = as_type<float>((&kernelContext_0)->frameParameters_0[(3552U)>>2]);
    float _S1110 = as_type<float>((&kernelContext_0)->frameParameters_0[(3556U)>>2]);
    float _S1111 = as_type<float>((&kernelContext_0)->frameParameters_0[(3560U)>>2]);
    float _S1112 = as_type<float>((&kernelContext_0)->frameParameters_0[(3564U)>>2]);
    float4 _S1113 = float4(_S1109, _S1110, _S1111, _S1112);
    float _S1114 = as_type<float>((&kernelContext_0)->frameParameters_0[(3568U)>>2]);
    float _S1115 = as_type<float>((&kernelContext_0)->frameParameters_0[(3572U)>>2]);
    float _S1116 = as_type<float>((&kernelContext_0)->frameParameters_0[(3576U)>>2]);
    float _S1117 = as_type<float>((&kernelContext_0)->frameParameters_0[(3580U)>>2]);
    float4 _S1118 = float4(_S1114, _S1115, _S1116, _S1117);
    float _S1119 = as_type<float>((&kernelContext_0)->frameParameters_0[(3584U)>>2]);
    float _S1120 = as_type<float>((&kernelContext_0)->frameParameters_0[(3588U)>>2]);
    float _S1121 = as_type<float>((&kernelContext_0)->frameParameters_0[(3592U)>>2]);
    float _S1122 = as_type<float>((&kernelContext_0)->frameParameters_0[(3596U)>>2]);
    float4 _S1123 = float4(_S1119, _S1120, _S1121, _S1122);
    float _S1124 = as_type<float>((&kernelContext_0)->frameParameters_0[(3600U)>>2]);
    float _S1125 = as_type<float>((&kernelContext_0)->frameParameters_0[(3604U)>>2]);
    float _S1126 = as_type<float>((&kernelContext_0)->frameParameters_0[(3608U)>>2]);
    float _S1127 = as_type<float>((&kernelContext_0)->frameParameters_0[(3612U)>>2]);
    float4 _S1128 = float4(_S1124, _S1125, _S1126, _S1127);
    float _S1129 = as_type<float>((&kernelContext_0)->frameParameters_0[(3616U)>>2]);
    float _S1130 = as_type<float>((&kernelContext_0)->frameParameters_0[(3620U)>>2]);
    float _S1131 = as_type<float>((&kernelContext_0)->frameParameters_0[(3624U)>>2]);
    float _S1132 = as_type<float>((&kernelContext_0)->frameParameters_0[(3628U)>>2]);
    float4 _S1133 = float4(_S1129, _S1130, _S1131, _S1132);
    float _S1134 = as_type<float>((&kernelContext_0)->frameParameters_0[(3632U)>>2]);
    float _S1135 = as_type<float>((&kernelContext_0)->frameParameters_0[(3636U)>>2]);
    float _S1136 = as_type<float>((&kernelContext_0)->frameParameters_0[(3640U)>>2]);
    float _S1137 = as_type<float>((&kernelContext_0)->frameParameters_0[(3644U)>>2]);
    float4 _S1138 = float4(_S1134, _S1135, _S1136, _S1137);
    float _S1139 = as_type<float>((&kernelContext_0)->frameParameters_0[(3648U)>>2]);
    float _S1140 = as_type<float>((&kernelContext_0)->frameParameters_0[(3652U)>>2]);
    float _S1141 = as_type<float>((&kernelContext_0)->frameParameters_0[(3656U)>>2]);
    float _S1142 = as_type<float>((&kernelContext_0)->frameParameters_0[(3660U)>>2]);
    float4 _S1143 = float4(_S1139, _S1140, _S1141, _S1142);
    float _S1144 = as_type<float>((&kernelContext_0)->frameParameters_0[(3664U)>>2]);
    float _S1145 = as_type<float>((&kernelContext_0)->frameParameters_0[(3668U)>>2]);
    float _S1146 = as_type<float>((&kernelContext_0)->frameParameters_0[(3672U)>>2]);
    float _S1147 = as_type<float>((&kernelContext_0)->frameParameters_0[(3676U)>>2]);
    float4 _S1148 = float4(_S1144, _S1145, _S1146, _S1147);
    float _S1149 = as_type<float>((&kernelContext_0)->frameParameters_0[(3680U)>>2]);
    float _S1150 = as_type<float>((&kernelContext_0)->frameParameters_0[(3684U)>>2]);
    float _S1151 = as_type<float>((&kernelContext_0)->frameParameters_0[(3688U)>>2]);
    float _S1152 = as_type<float>((&kernelContext_0)->frameParameters_0[(3692U)>>2]);
    float4 _S1153 = float4(_S1149, _S1150, _S1151, _S1152);
    float _S1154 = as_type<float>((&kernelContext_0)->frameParameters_0[(3696U)>>2]);
    float _S1155 = as_type<float>((&kernelContext_0)->frameParameters_0[(3700U)>>2]);
    float _S1156 = as_type<float>((&kernelContext_0)->frameParameters_0[(3704U)>>2]);
    float _S1157 = as_type<float>((&kernelContext_0)->frameParameters_0[(3708U)>>2]);
    float4 _S1158 = float4(_S1154, _S1155, _S1156, _S1157);
    float _S1159 = as_type<float>((&kernelContext_0)->frameParameters_0[(3712U)>>2]);
    float _S1160 = as_type<float>((&kernelContext_0)->frameParameters_0[(3716U)>>2]);
    float _S1161 = as_type<float>((&kernelContext_0)->frameParameters_0[(3720U)>>2]);
    float _S1162 = as_type<float>((&kernelContext_0)->frameParameters_0[(3724U)>>2]);
    float4 _S1163 = float4(_S1159, _S1160, _S1161, _S1162);
    float _S1164 = as_type<float>((&kernelContext_0)->frameParameters_0[(3728U)>>2]);
    float _S1165 = as_type<float>((&kernelContext_0)->frameParameters_0[(3732U)>>2]);
    float _S1166 = as_type<float>((&kernelContext_0)->frameParameters_0[(3736U)>>2]);
    float _S1167 = as_type<float>((&kernelContext_0)->frameParameters_0[(3740U)>>2]);
    float4 _S1168 = float4(_S1164, _S1165, _S1166, _S1167);
    float _S1169 = as_type<float>((&kernelContext_0)->frameParameters_0[(3744U)>>2]);
    float _S1170 = as_type<float>((&kernelContext_0)->frameParameters_0[(3748U)>>2]);
    float _S1171 = as_type<float>((&kernelContext_0)->frameParameters_0[(3752U)>>2]);
    float _S1172 = as_type<float>((&kernelContext_0)->frameParameters_0[(3756U)>>2]);
    float4 _S1173 = float4(_S1169, _S1170, _S1171, _S1172);
    float _S1174 = as_type<float>((&kernelContext_0)->frameParameters_0[(3760U)>>2]);
    float _S1175 = as_type<float>((&kernelContext_0)->frameParameters_0[(3764U)>>2]);
    float _S1176 = as_type<float>((&kernelContext_0)->frameParameters_0[(3768U)>>2]);
    float _S1177 = as_type<float>((&kernelContext_0)->frameParameters_0[(3772U)>>2]);
    float4 _S1178 = float4(_S1174, _S1175, _S1176, _S1177);
    float _S1179 = as_type<float>((&kernelContext_0)->frameParameters_0[(3776U)>>2]);
    float _S1180 = as_type<float>((&kernelContext_0)->frameParameters_0[(3780U)>>2]);
    float _S1181 = as_type<float>((&kernelContext_0)->frameParameters_0[(3784U)>>2]);
    float _S1182 = as_type<float>((&kernelContext_0)->frameParameters_0[(3788U)>>2]);
    float4 _S1183 = float4(_S1179, _S1180, _S1181, _S1182);
    float _S1184 = as_type<float>((&kernelContext_0)->frameParameters_0[(3792U)>>2]);
    float _S1185 = as_type<float>((&kernelContext_0)->frameParameters_0[(3796U)>>2]);
    float _S1186 = as_type<float>((&kernelContext_0)->frameParameters_0[(3800U)>>2]);
    float _S1187 = as_type<float>((&kernelContext_0)->frameParameters_0[(3804U)>>2]);
    float4 _S1188 = float4(_S1184, _S1185, _S1186, _S1187);
    float _S1189 = as_type<float>((&kernelContext_0)->frameParameters_0[(3808U)>>2]);
    float _S1190 = as_type<float>((&kernelContext_0)->frameParameters_0[(3812U)>>2]);
    float _S1191 = as_type<float>((&kernelContext_0)->frameParameters_0[(3816U)>>2]);
    float _S1192 = as_type<float>((&kernelContext_0)->frameParameters_0[(3820U)>>2]);
    float4 _S1193 = float4(_S1189, _S1190, _S1191, _S1192);
    float _S1194 = as_type<float>((&kernelContext_0)->frameParameters_0[(3824U)>>2]);
    float _S1195 = as_type<float>((&kernelContext_0)->frameParameters_0[(3828U)>>2]);
    float _S1196 = as_type<float>((&kernelContext_0)->frameParameters_0[(3832U)>>2]);
    float _S1197 = as_type<float>((&kernelContext_0)->frameParameters_0[(3836U)>>2]);
    float4 _S1198 = float4(_S1194, _S1195, _S1196, _S1197);
    float _S1199 = as_type<float>((&kernelContext_0)->frameParameters_0[(3840U)>>2]);
    float _S1200 = as_type<float>((&kernelContext_0)->frameParameters_0[(3844U)>>2]);
    float _S1201 = as_type<float>((&kernelContext_0)->frameParameters_0[(3848U)>>2]);
    float _S1202 = as_type<float>((&kernelContext_0)->frameParameters_0[(3852U)>>2]);
    float4 _S1203 = float4(_S1199, _S1200, _S1201, _S1202);
    float _S1204 = as_type<float>((&kernelContext_0)->frameParameters_0[(3856U)>>2]);
    float _S1205 = as_type<float>((&kernelContext_0)->frameParameters_0[(3860U)>>2]);
    float _S1206 = as_type<float>((&kernelContext_0)->frameParameters_0[(3864U)>>2]);
    float _S1207 = as_type<float>((&kernelContext_0)->frameParameters_0[(3868U)>>2]);
    float4 _S1208 = float4(_S1204, _S1205, _S1206, _S1207);
    float _S1209 = as_type<float>((&kernelContext_0)->frameParameters_0[(3872U)>>2]);
    float _S1210 = as_type<float>((&kernelContext_0)->frameParameters_0[(3876U)>>2]);
    float _S1211 = as_type<float>((&kernelContext_0)->frameParameters_0[(3880U)>>2]);
    float _S1212 = as_type<float>((&kernelContext_0)->frameParameters_0[(3884U)>>2]);
    float4 _S1213 = float4(_S1209, _S1210, _S1211, _S1212);
    float _S1214 = as_type<float>((&kernelContext_0)->frameParameters_0[(3888U)>>2]);
    float _S1215 = as_type<float>((&kernelContext_0)->frameParameters_0[(3892U)>>2]);
    float _S1216 = as_type<float>((&kernelContext_0)->frameParameters_0[(3896U)>>2]);
    float _S1217 = as_type<float>((&kernelContext_0)->frameParameters_0[(3900U)>>2]);
    float4 _S1218 = float4(_S1214, _S1215, _S1216, _S1217);
    float _S1219 = as_type<float>((&kernelContext_0)->frameParameters_0[(3904U)>>2]);
    float _S1220 = as_type<float>((&kernelContext_0)->frameParameters_0[(3908U)>>2]);
    float _S1221 = as_type<float>((&kernelContext_0)->frameParameters_0[(3912U)>>2]);
    float _S1222 = as_type<float>((&kernelContext_0)->frameParameters_0[(3916U)>>2]);
    float4 _S1223 = float4(_S1219, _S1220, _S1221, _S1222);
    float _S1224 = as_type<float>((&kernelContext_0)->frameParameters_0[(3920U)>>2]);
    float _S1225 = as_type<float>((&kernelContext_0)->frameParameters_0[(3924U)>>2]);
    float _S1226 = as_type<float>((&kernelContext_0)->frameParameters_0[(3928U)>>2]);
    float _S1227 = as_type<float>((&kernelContext_0)->frameParameters_0[(3932U)>>2]);
    float4 _S1228 = float4(_S1224, _S1225, _S1226, _S1227);
    float _S1229 = as_type<float>((&kernelContext_0)->frameParameters_0[(3936U)>>2]);
    float _S1230 = as_type<float>((&kernelContext_0)->frameParameters_0[(3940U)>>2]);
    float _S1231 = as_type<float>((&kernelContext_0)->frameParameters_0[(3944U)>>2]);
    float _S1232 = as_type<float>((&kernelContext_0)->frameParameters_0[(3948U)>>2]);
    float4 _S1233 = float4(_S1229, _S1230, _S1231, _S1232);
    float _S1234 = as_type<float>((&kernelContext_0)->frameParameters_0[(3952U)>>2]);
    float _S1235 = as_type<float>((&kernelContext_0)->frameParameters_0[(3956U)>>2]);
    float _S1236 = as_type<float>((&kernelContext_0)->frameParameters_0[(3960U)>>2]);
    float _S1237 = as_type<float>((&kernelContext_0)->frameParameters_0[(3964U)>>2]);
    float4 _S1238 = float4(_S1234, _S1235, _S1236, _S1237);
    float _S1239 = as_type<float>((&kernelContext_0)->frameParameters_0[(3968U)>>2]);
    float _S1240 = as_type<float>((&kernelContext_0)->frameParameters_0[(3972U)>>2]);
    float _S1241 = as_type<float>((&kernelContext_0)->frameParameters_0[(3976U)>>2]);
    float _S1242 = as_type<float>((&kernelContext_0)->frameParameters_0[(3980U)>>2]);
    float4 _S1243 = float4(_S1239, _S1240, _S1241, _S1242);
    float _S1244 = as_type<float>((&kernelContext_0)->frameParameters_0[(3984U)>>2]);
    float _S1245 = as_type<float>((&kernelContext_0)->frameParameters_0[(3988U)>>2]);
    float _S1246 = as_type<float>((&kernelContext_0)->frameParameters_0[(3992U)>>2]);
    float _S1247 = as_type<float>((&kernelContext_0)->frameParameters_0[(3996U)>>2]);
    float4 _S1248 = float4(_S1244, _S1245, _S1246, _S1247);
    float _S1249 = as_type<float>((&kernelContext_0)->frameParameters_0[(4000U)>>2]);
    float _S1250 = as_type<float>((&kernelContext_0)->frameParameters_0[(4004U)>>2]);
    float _S1251 = as_type<float>((&kernelContext_0)->frameParameters_0[(4008U)>>2]);
    float _S1252 = as_type<float>((&kernelContext_0)->frameParameters_0[(4012U)>>2]);
    float4 _S1253 = float4(_S1249, _S1250, _S1251, _S1252);
    float _S1254 = as_type<float>((&kernelContext_0)->frameParameters_0[(4016U)>>2]);
    float _S1255 = as_type<float>((&kernelContext_0)->frameParameters_0[(4020U)>>2]);
    float _S1256 = as_type<float>((&kernelContext_0)->frameParameters_0[(4024U)>>2]);
    float _S1257 = as_type<float>((&kernelContext_0)->frameParameters_0[(4028U)>>2]);
    float4 _S1258 = float4(_S1254, _S1255, _S1256, _S1257);
    float _S1259 = as_type<float>((&kernelContext_0)->frameParameters_0[(4032U)>>2]);
    float _S1260 = as_type<float>((&kernelContext_0)->frameParameters_0[(4036U)>>2]);
    float _S1261 = as_type<float>((&kernelContext_0)->frameParameters_0[(4040U)>>2]);
    float _S1262 = as_type<float>((&kernelContext_0)->frameParameters_0[(4044U)>>2]);
    float4 _S1263 = float4(_S1259, _S1260, _S1261, _S1262);
    float _S1264 = as_type<float>((&kernelContext_0)->frameParameters_0[(4048U)>>2]);
    float _S1265 = as_type<float>((&kernelContext_0)->frameParameters_0[(4052U)>>2]);
    float _S1266 = as_type<float>((&kernelContext_0)->frameParameters_0[(4056U)>>2]);
    float _S1267 = as_type<float>((&kernelContext_0)->frameParameters_0[(4060U)>>2]);
    float4 _S1268 = float4(_S1264, _S1265, _S1266, _S1267);
    float _S1269 = as_type<float>((&kernelContext_0)->frameParameters_0[(4064U)>>2]);
    float _S1270 = as_type<float>((&kernelContext_0)->frameParameters_0[(4068U)>>2]);
    float _S1271 = as_type<float>((&kernelContext_0)->frameParameters_0[(4072U)>>2]);
    float _S1272 = as_type<float>((&kernelContext_0)->frameParameters_0[(4076U)>>2]);
    float4 _S1273 = float4(_S1269, _S1270, _S1271, _S1272);
    float _S1274 = as_type<float>((&kernelContext_0)->frameParameters_0[(4080U)>>2]);
    float _S1275 = as_type<float>((&kernelContext_0)->frameParameters_0[(4084U)>>2]);
    float _S1276 = as_type<float>((&kernelContext_0)->frameParameters_0[(4088U)>>2]);
    float _S1277 = as_type<float>((&kernelContext_0)->frameParameters_0[(4092U)>>2]);
    float4 _S1278 = float4(_S1274, _S1275, _S1276, _S1277);
    float _S1279 = as_type<float>((&kernelContext_0)->frameParameters_0[(4096U)>>2]);
    float _S1280 = as_type<float>((&kernelContext_0)->frameParameters_0[(4100U)>>2]);
    float _S1281 = as_type<float>((&kernelContext_0)->frameParameters_0[(4104U)>>2]);
    float _S1282 = as_type<float>((&kernelContext_0)->frameParameters_0[(4108U)>>2]);
    float4 _S1283 = float4(_S1279, _S1280, _S1281, _S1282);
    float _S1284 = as_type<float>((&kernelContext_0)->frameParameters_0[(4112U)>>2]);
    float _S1285 = as_type<float>((&kernelContext_0)->frameParameters_0[(4116U)>>2]);
    float _S1286 = as_type<float>((&kernelContext_0)->frameParameters_0[(4120U)>>2]);
    float _S1287 = as_type<float>((&kernelContext_0)->frameParameters_0[(4124U)>>2]);
    float4 _S1288 = float4(_S1284, _S1285, _S1286, _S1287);
    float _S1289 = as_type<float>((&kernelContext_0)->frameParameters_0[(4128U)>>2]);
    float _S1290 = as_type<float>((&kernelContext_0)->frameParameters_0[(4132U)>>2]);
    float _S1291 = as_type<float>((&kernelContext_0)->frameParameters_0[(4136U)>>2]);
    float _S1292 = as_type<float>((&kernelContext_0)->frameParameters_0[(4140U)>>2]);
    float4 _S1293 = float4(_S1289, _S1290, _S1291, _S1292);
    float _S1294 = as_type<float>((&kernelContext_0)->frameParameters_0[(4144U)>>2]);
    float _S1295 = as_type<float>((&kernelContext_0)->frameParameters_0[(4148U)>>2]);
    float _S1296 = as_type<float>((&kernelContext_0)->frameParameters_0[(4152U)>>2]);
    float _S1297 = as_type<float>((&kernelContext_0)->frameParameters_0[(4156U)>>2]);
    float4 _S1298 = float4(_S1294, _S1295, _S1296, _S1297);
    float _S1299 = as_type<float>((&kernelContext_0)->frameParameters_0[(4160U)>>2]);
    float _S1300 = as_type<float>((&kernelContext_0)->frameParameters_0[(4164U)>>2]);
    float _S1301 = as_type<float>((&kernelContext_0)->frameParameters_0[(4168U)>>2]);
    float _S1302 = as_type<float>((&kernelContext_0)->frameParameters_0[(4172U)>>2]);
    float4 _S1303 = float4(_S1299, _S1300, _S1301, _S1302);
    float _S1304 = as_type<float>((&kernelContext_0)->frameParameters_0[(4176U)>>2]);
    float _S1305 = as_type<float>((&kernelContext_0)->frameParameters_0[(4180U)>>2]);
    float _S1306 = as_type<float>((&kernelContext_0)->frameParameters_0[(4184U)>>2]);
    float _S1307 = as_type<float>((&kernelContext_0)->frameParameters_0[(4188U)>>2]);
    float4 _S1308 = float4(_S1304, _S1305, _S1306, _S1307);
    float _S1309 = as_type<float>((&kernelContext_0)->frameParameters_0[(4192U)>>2]);
    float _S1310 = as_type<float>((&kernelContext_0)->frameParameters_0[(4196U)>>2]);
    float _S1311 = as_type<float>((&kernelContext_0)->frameParameters_0[(4200U)>>2]);
    float _S1312 = as_type<float>((&kernelContext_0)->frameParameters_0[(4204U)>>2]);
    float4 _S1313 = float4(_S1309, _S1310, _S1311, _S1312);
    float _S1314 = as_type<float>((&kernelContext_0)->frameParameters_0[(4208U)>>2]);
    float _S1315 = as_type<float>((&kernelContext_0)->frameParameters_0[(4212U)>>2]);
    float _S1316 = as_type<float>((&kernelContext_0)->frameParameters_0[(4216U)>>2]);
    float _S1317 = as_type<float>((&kernelContext_0)->frameParameters_0[(4220U)>>2]);
    float4 _S1318 = float4(_S1314, _S1315, _S1316, _S1317);
    float _S1319 = as_type<float>((&kernelContext_0)->frameParameters_0[(4224U)>>2]);
    float _S1320 = as_type<float>((&kernelContext_0)->frameParameters_0[(4228U)>>2]);
    float _S1321 = as_type<float>((&kernelContext_0)->frameParameters_0[(4232U)>>2]);
    float _S1322 = as_type<float>((&kernelContext_0)->frameParameters_0[(4236U)>>2]);
    float4 _S1323 = float4(_S1319, _S1320, _S1321, _S1322);
    float _S1324 = as_type<float>((&kernelContext_0)->frameParameters_0[(4240U)>>2]);
    float _S1325 = as_type<float>((&kernelContext_0)->frameParameters_0[(4244U)>>2]);
    float _S1326 = as_type<float>((&kernelContext_0)->frameParameters_0[(4248U)>>2]);
    float _S1327 = as_type<float>((&kernelContext_0)->frameParameters_0[(4252U)>>2]);
    float4 _S1328 = float4(_S1324, _S1325, _S1326, _S1327);
    float _S1329 = as_type<float>((&kernelContext_0)->frameParameters_0[(4256U)>>2]);
    float _S1330 = as_type<float>((&kernelContext_0)->frameParameters_0[(4260U)>>2]);
    float _S1331 = as_type<float>((&kernelContext_0)->frameParameters_0[(4264U)>>2]);
    float _S1332 = as_type<float>((&kernelContext_0)->frameParameters_0[(4268U)>>2]);
    float4 _S1333 = float4(_S1329, _S1330, _S1331, _S1332);
    float _S1334 = as_type<float>((&kernelContext_0)->frameParameters_0[(4272U)>>2]);
    float _S1335 = as_type<float>((&kernelContext_0)->frameParameters_0[(4276U)>>2]);
    float _S1336 = as_type<float>((&kernelContext_0)->frameParameters_0[(4280U)>>2]);
    float _S1337 = as_type<float>((&kernelContext_0)->frameParameters_0[(4284U)>>2]);
    float4 _S1338 = float4(_S1334, _S1335, _S1336, _S1337);
    float _S1339 = as_type<float>((&kernelContext_0)->frameParameters_0[(4288U)>>2]);
    float _S1340 = as_type<float>((&kernelContext_0)->frameParameters_0[(4292U)>>2]);
    float _S1341 = as_type<float>((&kernelContext_0)->frameParameters_0[(4296U)>>2]);
    float _S1342 = as_type<float>((&kernelContext_0)->frameParameters_0[(4300U)>>2]);
    float4 _S1343 = float4(_S1339, _S1340, _S1341, _S1342);
    float _S1344 = as_type<float>((&kernelContext_0)->frameParameters_0[(4304U)>>2]);
    float _S1345 = as_type<float>((&kernelContext_0)->frameParameters_0[(4308U)>>2]);
    float _S1346 = as_type<float>((&kernelContext_0)->frameParameters_0[(4312U)>>2]);
    float _S1347 = as_type<float>((&kernelContext_0)->frameParameters_0[(4316U)>>2]);
    array<float4, int(128)> _S1348 = { _S713, _S718, _S723, _S728, _S733, _S738, _S743, _S748, _S753, _S758, _S763, _S768, _S773, _S778, _S783, _S788, _S793, _S798, _S803, _S808, _S813, _S818, _S823, _S828, _S833, _S838, _S843, _S848, _S853, _S858, _S863, _S868, _S873, _S878, _S883, _S888, _S893, _S898, _S903, _S908, _S913, _S918, _S923, _S928, _S933, _S938, _S943, _S948, _S953, _S958, _S963, _S968, _S973, _S978, _S983, _S988, _S993, _S998, _S1003, _S1008, _S1013, _S1018, _S1023, _S1028, _S1033, _S1038, _S1043, _S1048, _S1053, _S1058, _S1063, _S1068, _S1073, _S1078, _S1083, _S1088, _S1093, _S1098, _S1103, _S1108, _S1113, _S1118, _S1123, _S1128, _S1133, _S1138, _S1143, _S1148, _S1153, _S1158, _S1163, _S1168, _S1173, _S1178, _S1183, _S1188, _S1193, _S1198, _S1203, _S1208, _S1213, _S1218, _S1223, _S1228, _S1233, _S1238, _S1243, _S1248, _S1253, _S1258, _S1263, _S1268, _S1273, _S1278, _S1283, _S1288, _S1293, _S1298, _S1303, _S1308, _S1313, _S1318, _S1323, _S1328, _S1333, _S1338, _S1343, float4(_S1344, _S1345, _S1346, _S1347) };
    float _S1349 = as_type<float>((&kernelContext_0)->frameParameters_0[(4320U)>>2]);
    float _S1350 = as_type<float>((&kernelContext_0)->frameParameters_0[(4324U)>>2]);
    float _S1351 = as_type<float>((&kernelContext_0)->frameParameters_0[(4328U)>>2]);
    float _S1352 = as_type<float>((&kernelContext_0)->frameParameters_0[(4332U)>>2]);
    float4 _S1353 = float4(_S1349, _S1350, _S1351, _S1352);
    float _S1354 = as_type<float>((&kernelContext_0)->frameParameters_0[(4336U)>>2]);
    float _S1355 = as_type<float>((&kernelContext_0)->frameParameters_0[(4340U)>>2]);
    float _S1356 = as_type<float>((&kernelContext_0)->frameParameters_0[(4344U)>>2]);
    float _S1357 = as_type<float>((&kernelContext_0)->frameParameters_0[(4348U)>>2]);
    float4 _S1358 = float4(_S1354, _S1355, _S1356, _S1357);
    float _S1359 = as_type<float>((&kernelContext_0)->frameParameters_0[(4352U)>>2]);
    float _S1360 = as_type<float>((&kernelContext_0)->frameParameters_0[(4356U)>>2]);
    float _S1361 = as_type<float>((&kernelContext_0)->frameParameters_0[(4360U)>>2]);
    float _S1362 = as_type<float>((&kernelContext_0)->frameParameters_0[(4364U)>>2]);
    float4 _S1363 = float4(_S1359, _S1360, _S1361, _S1362);
    float _S1364 = as_type<float>((&kernelContext_0)->frameParameters_0[(4368U)>>2]);
    float _S1365 = as_type<float>((&kernelContext_0)->frameParameters_0[(4372U)>>2]);
    float _S1366 = as_type<float>((&kernelContext_0)->frameParameters_0[(4376U)>>2]);
    float _S1367 = as_type<float>((&kernelContext_0)->frameParameters_0[(4380U)>>2]);
    float4 _S1368 = float4(_S1364, _S1365, _S1366, _S1367);
    float _S1369 = as_type<float>((&kernelContext_0)->frameParameters_0[(4384U)>>2]);
    float _S1370 = as_type<float>((&kernelContext_0)->frameParameters_0[(4388U)>>2]);
    float _S1371 = as_type<float>((&kernelContext_0)->frameParameters_0[(4392U)>>2]);
    float _S1372 = as_type<float>((&kernelContext_0)->frameParameters_0[(4396U)>>2]);
    float4 _S1373 = float4(_S1369, _S1370, _S1371, _S1372);
    float _S1374 = as_type<float>((&kernelContext_0)->frameParameters_0[(4400U)>>2]);
    float _S1375 = as_type<float>((&kernelContext_0)->frameParameters_0[(4404U)>>2]);
    float _S1376 = as_type<float>((&kernelContext_0)->frameParameters_0[(4408U)>>2]);
    float _S1377 = as_type<float>((&kernelContext_0)->frameParameters_0[(4412U)>>2]);
    float4 _S1378 = float4(_S1374, _S1375, _S1376, _S1377);
    float _S1379 = as_type<float>((&kernelContext_0)->frameParameters_0[(4416U)>>2]);
    float _S1380 = as_type<float>((&kernelContext_0)->frameParameters_0[(4420U)>>2]);
    float _S1381 = as_type<float>((&kernelContext_0)->frameParameters_0[(4424U)>>2]);
    float _S1382 = as_type<float>((&kernelContext_0)->frameParameters_0[(4428U)>>2]);
    float4 _S1383 = float4(_S1379, _S1380, _S1381, _S1382);
    float _S1384 = as_type<float>((&kernelContext_0)->frameParameters_0[(4432U)>>2]);
    float _S1385 = as_type<float>((&kernelContext_0)->frameParameters_0[(4436U)>>2]);
    float _S1386 = as_type<float>((&kernelContext_0)->frameParameters_0[(4440U)>>2]);
    float _S1387 = as_type<float>((&kernelContext_0)->frameParameters_0[(4444U)>>2]);
    float4 _S1388 = float4(_S1384, _S1385, _S1386, _S1387);
    float _S1389 = as_type<float>((&kernelContext_0)->frameParameters_0[(4448U)>>2]);
    float _S1390 = as_type<float>((&kernelContext_0)->frameParameters_0[(4452U)>>2]);
    float _S1391 = as_type<float>((&kernelContext_0)->frameParameters_0[(4456U)>>2]);
    float _S1392 = as_type<float>((&kernelContext_0)->frameParameters_0[(4460U)>>2]);
    float4 _S1393 = float4(_S1389, _S1390, _S1391, _S1392);
    float _S1394 = as_type<float>((&kernelContext_0)->frameParameters_0[(4464U)>>2]);
    float _S1395 = as_type<float>((&kernelContext_0)->frameParameters_0[(4468U)>>2]);
    float _S1396 = as_type<float>((&kernelContext_0)->frameParameters_0[(4472U)>>2]);
    float _S1397 = as_type<float>((&kernelContext_0)->frameParameters_0[(4476U)>>2]);
    float4 _S1398 = float4(_S1394, _S1395, _S1396, _S1397);
    float _S1399 = as_type<float>((&kernelContext_0)->frameParameters_0[(4480U)>>2]);
    float _S1400 = as_type<float>((&kernelContext_0)->frameParameters_0[(4484U)>>2]);
    float _S1401 = as_type<float>((&kernelContext_0)->frameParameters_0[(4488U)>>2]);
    float _S1402 = as_type<float>((&kernelContext_0)->frameParameters_0[(4492U)>>2]);
    float4 _S1403 = float4(_S1399, _S1400, _S1401, _S1402);
    float _S1404 = as_type<float>((&kernelContext_0)->frameParameters_0[(4496U)>>2]);
    float _S1405 = as_type<float>((&kernelContext_0)->frameParameters_0[(4500U)>>2]);
    float _S1406 = as_type<float>((&kernelContext_0)->frameParameters_0[(4504U)>>2]);
    float _S1407 = as_type<float>((&kernelContext_0)->frameParameters_0[(4508U)>>2]);
    float4 _S1408 = float4(_S1404, _S1405, _S1406, _S1407);
    float _S1409 = as_type<float>((&kernelContext_0)->frameParameters_0[(4512U)>>2]);
    float _S1410 = as_type<float>((&kernelContext_0)->frameParameters_0[(4516U)>>2]);
    float _S1411 = as_type<float>((&kernelContext_0)->frameParameters_0[(4520U)>>2]);
    float _S1412 = as_type<float>((&kernelContext_0)->frameParameters_0[(4524U)>>2]);
    float4 _S1413 = float4(_S1409, _S1410, _S1411, _S1412);
    float _S1414 = as_type<float>((&kernelContext_0)->frameParameters_0[(4528U)>>2]);
    float _S1415 = as_type<float>((&kernelContext_0)->frameParameters_0[(4532U)>>2]);
    float _S1416 = as_type<float>((&kernelContext_0)->frameParameters_0[(4536U)>>2]);
    float _S1417 = as_type<float>((&kernelContext_0)->frameParameters_0[(4540U)>>2]);
    float4 _S1418 = float4(_S1414, _S1415, _S1416, _S1417);
    float _S1419 = as_type<float>((&kernelContext_0)->frameParameters_0[(4544U)>>2]);
    float _S1420 = as_type<float>((&kernelContext_0)->frameParameters_0[(4548U)>>2]);
    float _S1421 = as_type<float>((&kernelContext_0)->frameParameters_0[(4552U)>>2]);
    float _S1422 = as_type<float>((&kernelContext_0)->frameParameters_0[(4556U)>>2]);
    float4 _S1423 = float4(_S1419, _S1420, _S1421, _S1422);
    float _S1424 = as_type<float>((&kernelContext_0)->frameParameters_0[(4560U)>>2]);
    float _S1425 = as_type<float>((&kernelContext_0)->frameParameters_0[(4564U)>>2]);
    float _S1426 = as_type<float>((&kernelContext_0)->frameParameters_0[(4568U)>>2]);
    float _S1427 = as_type<float>((&kernelContext_0)->frameParameters_0[(4572U)>>2]);
    float4 _S1428 = float4(_S1424, _S1425, _S1426, _S1427);
    float _S1429 = as_type<float>((&kernelContext_0)->frameParameters_0[(4576U)>>2]);
    float _S1430 = as_type<float>((&kernelContext_0)->frameParameters_0[(4580U)>>2]);
    float _S1431 = as_type<float>((&kernelContext_0)->frameParameters_0[(4584U)>>2]);
    float _S1432 = as_type<float>((&kernelContext_0)->frameParameters_0[(4588U)>>2]);
    float4 _S1433 = float4(_S1429, _S1430, _S1431, _S1432);
    float _S1434 = as_type<float>((&kernelContext_0)->frameParameters_0[(4592U)>>2]);
    float _S1435 = as_type<float>((&kernelContext_0)->frameParameters_0[(4596U)>>2]);
    float _S1436 = as_type<float>((&kernelContext_0)->frameParameters_0[(4600U)>>2]);
    float _S1437 = as_type<float>((&kernelContext_0)->frameParameters_0[(4604U)>>2]);
    float4 _S1438 = float4(_S1434, _S1435, _S1436, _S1437);
    float _S1439 = as_type<float>((&kernelContext_0)->frameParameters_0[(4608U)>>2]);
    float _S1440 = as_type<float>((&kernelContext_0)->frameParameters_0[(4612U)>>2]);
    float _S1441 = as_type<float>((&kernelContext_0)->frameParameters_0[(4616U)>>2]);
    float _S1442 = as_type<float>((&kernelContext_0)->frameParameters_0[(4620U)>>2]);
    float4 _S1443 = float4(_S1439, _S1440, _S1441, _S1442);
    float _S1444 = as_type<float>((&kernelContext_0)->frameParameters_0[(4624U)>>2]);
    float _S1445 = as_type<float>((&kernelContext_0)->frameParameters_0[(4628U)>>2]);
    float _S1446 = as_type<float>((&kernelContext_0)->frameParameters_0[(4632U)>>2]);
    float _S1447 = as_type<float>((&kernelContext_0)->frameParameters_0[(4636U)>>2]);
    float4 _S1448 = float4(_S1444, _S1445, _S1446, _S1447);
    float _S1449 = as_type<float>((&kernelContext_0)->frameParameters_0[(4640U)>>2]);
    float _S1450 = as_type<float>((&kernelContext_0)->frameParameters_0[(4644U)>>2]);
    float _S1451 = as_type<float>((&kernelContext_0)->frameParameters_0[(4648U)>>2]);
    float _S1452 = as_type<float>((&kernelContext_0)->frameParameters_0[(4652U)>>2]);
    float4 _S1453 = float4(_S1449, _S1450, _S1451, _S1452);
    float _S1454 = as_type<float>((&kernelContext_0)->frameParameters_0[(4656U)>>2]);
    float _S1455 = as_type<float>((&kernelContext_0)->frameParameters_0[(4660U)>>2]);
    float _S1456 = as_type<float>((&kernelContext_0)->frameParameters_0[(4664U)>>2]);
    float _S1457 = as_type<float>((&kernelContext_0)->frameParameters_0[(4668U)>>2]);
    float4 _S1458 = float4(_S1454, _S1455, _S1456, _S1457);
    float _S1459 = as_type<float>((&kernelContext_0)->frameParameters_0[(4672U)>>2]);
    float _S1460 = as_type<float>((&kernelContext_0)->frameParameters_0[(4676U)>>2]);
    float _S1461 = as_type<float>((&kernelContext_0)->frameParameters_0[(4680U)>>2]);
    float _S1462 = as_type<float>((&kernelContext_0)->frameParameters_0[(4684U)>>2]);
    float4 _S1463 = float4(_S1459, _S1460, _S1461, _S1462);
    float _S1464 = as_type<float>((&kernelContext_0)->frameParameters_0[(4688U)>>2]);
    float _S1465 = as_type<float>((&kernelContext_0)->frameParameters_0[(4692U)>>2]);
    float _S1466 = as_type<float>((&kernelContext_0)->frameParameters_0[(4696U)>>2]);
    float _S1467 = as_type<float>((&kernelContext_0)->frameParameters_0[(4700U)>>2]);
    float4 _S1468 = float4(_S1464, _S1465, _S1466, _S1467);
    float _S1469 = as_type<float>((&kernelContext_0)->frameParameters_0[(4704U)>>2]);
    float _S1470 = as_type<float>((&kernelContext_0)->frameParameters_0[(4708U)>>2]);
    float _S1471 = as_type<float>((&kernelContext_0)->frameParameters_0[(4712U)>>2]);
    float _S1472 = as_type<float>((&kernelContext_0)->frameParameters_0[(4716U)>>2]);
    float4 _S1473 = float4(_S1469, _S1470, _S1471, _S1472);
    float _S1474 = as_type<float>((&kernelContext_0)->frameParameters_0[(4720U)>>2]);
    float _S1475 = as_type<float>((&kernelContext_0)->frameParameters_0[(4724U)>>2]);
    float _S1476 = as_type<float>((&kernelContext_0)->frameParameters_0[(4728U)>>2]);
    float _S1477 = as_type<float>((&kernelContext_0)->frameParameters_0[(4732U)>>2]);
    float4 _S1478 = float4(_S1474, _S1475, _S1476, _S1477);
    float _S1479 = as_type<float>((&kernelContext_0)->frameParameters_0[(4736U)>>2]);
    float _S1480 = as_type<float>((&kernelContext_0)->frameParameters_0[(4740U)>>2]);
    float _S1481 = as_type<float>((&kernelContext_0)->frameParameters_0[(4744U)>>2]);
    float _S1482 = as_type<float>((&kernelContext_0)->frameParameters_0[(4748U)>>2]);
    float4 _S1483 = float4(_S1479, _S1480, _S1481, _S1482);
    float _S1484 = as_type<float>((&kernelContext_0)->frameParameters_0[(4752U)>>2]);
    float _S1485 = as_type<float>((&kernelContext_0)->frameParameters_0[(4756U)>>2]);
    float _S1486 = as_type<float>((&kernelContext_0)->frameParameters_0[(4760U)>>2]);
    float _S1487 = as_type<float>((&kernelContext_0)->frameParameters_0[(4764U)>>2]);
    float4 _S1488 = float4(_S1484, _S1485, _S1486, _S1487);
    float _S1489 = as_type<float>((&kernelContext_0)->frameParameters_0[(4768U)>>2]);
    float _S1490 = as_type<float>((&kernelContext_0)->frameParameters_0[(4772U)>>2]);
    float _S1491 = as_type<float>((&kernelContext_0)->frameParameters_0[(4776U)>>2]);
    float _S1492 = as_type<float>((&kernelContext_0)->frameParameters_0[(4780U)>>2]);
    float4 _S1493 = float4(_S1489, _S1490, _S1491, _S1492);
    float _S1494 = as_type<float>((&kernelContext_0)->frameParameters_0[(4784U)>>2]);
    float _S1495 = as_type<float>((&kernelContext_0)->frameParameters_0[(4788U)>>2]);
    float _S1496 = as_type<float>((&kernelContext_0)->frameParameters_0[(4792U)>>2]);
    float _S1497 = as_type<float>((&kernelContext_0)->frameParameters_0[(4796U)>>2]);
    float4 _S1498 = float4(_S1494, _S1495, _S1496, _S1497);
    float _S1499 = as_type<float>((&kernelContext_0)->frameParameters_0[(4800U)>>2]);
    float _S1500 = as_type<float>((&kernelContext_0)->frameParameters_0[(4804U)>>2]);
    float _S1501 = as_type<float>((&kernelContext_0)->frameParameters_0[(4808U)>>2]);
    float _S1502 = as_type<float>((&kernelContext_0)->frameParameters_0[(4812U)>>2]);
    float4 _S1503 = float4(_S1499, _S1500, _S1501, _S1502);
    float _S1504 = as_type<float>((&kernelContext_0)->frameParameters_0[(4816U)>>2]);
    float _S1505 = as_type<float>((&kernelContext_0)->frameParameters_0[(4820U)>>2]);
    float _S1506 = as_type<float>((&kernelContext_0)->frameParameters_0[(4824U)>>2]);
    float _S1507 = as_type<float>((&kernelContext_0)->frameParameters_0[(4828U)>>2]);
    float4 _S1508 = float4(_S1504, _S1505, _S1506, _S1507);
    float _S1509 = as_type<float>((&kernelContext_0)->frameParameters_0[(4832U)>>2]);
    float _S1510 = as_type<float>((&kernelContext_0)->frameParameters_0[(4836U)>>2]);
    float _S1511 = as_type<float>((&kernelContext_0)->frameParameters_0[(4840U)>>2]);
    float _S1512 = as_type<float>((&kernelContext_0)->frameParameters_0[(4844U)>>2]);
    float4 _S1513 = float4(_S1509, _S1510, _S1511, _S1512);
    float _S1514 = as_type<float>((&kernelContext_0)->frameParameters_0[(4848U)>>2]);
    float _S1515 = as_type<float>((&kernelContext_0)->frameParameters_0[(4852U)>>2]);
    float _S1516 = as_type<float>((&kernelContext_0)->frameParameters_0[(4856U)>>2]);
    float _S1517 = as_type<float>((&kernelContext_0)->frameParameters_0[(4860U)>>2]);
    float4 _S1518 = float4(_S1514, _S1515, _S1516, _S1517);
    float _S1519 = as_type<float>((&kernelContext_0)->frameParameters_0[(4864U)>>2]);
    float _S1520 = as_type<float>((&kernelContext_0)->frameParameters_0[(4868U)>>2]);
    float _S1521 = as_type<float>((&kernelContext_0)->frameParameters_0[(4872U)>>2]);
    float _S1522 = as_type<float>((&kernelContext_0)->frameParameters_0[(4876U)>>2]);
    float4 _S1523 = float4(_S1519, _S1520, _S1521, _S1522);
    float _S1524 = as_type<float>((&kernelContext_0)->frameParameters_0[(4880U)>>2]);
    float _S1525 = as_type<float>((&kernelContext_0)->frameParameters_0[(4884U)>>2]);
    float _S1526 = as_type<float>((&kernelContext_0)->frameParameters_0[(4888U)>>2]);
    float _S1527 = as_type<float>((&kernelContext_0)->frameParameters_0[(4892U)>>2]);
    float4 _S1528 = float4(_S1524, _S1525, _S1526, _S1527);
    float _S1529 = as_type<float>((&kernelContext_0)->frameParameters_0[(4896U)>>2]);
    float _S1530 = as_type<float>((&kernelContext_0)->frameParameters_0[(4900U)>>2]);
    float _S1531 = as_type<float>((&kernelContext_0)->frameParameters_0[(4904U)>>2]);
    float _S1532 = as_type<float>((&kernelContext_0)->frameParameters_0[(4908U)>>2]);
    float4 _S1533 = float4(_S1529, _S1530, _S1531, _S1532);
    float _S1534 = as_type<float>((&kernelContext_0)->frameParameters_0[(4912U)>>2]);
    float _S1535 = as_type<float>((&kernelContext_0)->frameParameters_0[(4916U)>>2]);
    float _S1536 = as_type<float>((&kernelContext_0)->frameParameters_0[(4920U)>>2]);
    float _S1537 = as_type<float>((&kernelContext_0)->frameParameters_0[(4924U)>>2]);
    float4 _S1538 = float4(_S1534, _S1535, _S1536, _S1537);
    float _S1539 = as_type<float>((&kernelContext_0)->frameParameters_0[(4928U)>>2]);
    float _S1540 = as_type<float>((&kernelContext_0)->frameParameters_0[(4932U)>>2]);
    float _S1541 = as_type<float>((&kernelContext_0)->frameParameters_0[(4936U)>>2]);
    float _S1542 = as_type<float>((&kernelContext_0)->frameParameters_0[(4940U)>>2]);
    float4 _S1543 = float4(_S1539, _S1540, _S1541, _S1542);
    float _S1544 = as_type<float>((&kernelContext_0)->frameParameters_0[(4944U)>>2]);
    float _S1545 = as_type<float>((&kernelContext_0)->frameParameters_0[(4948U)>>2]);
    float _S1546 = as_type<float>((&kernelContext_0)->frameParameters_0[(4952U)>>2]);
    float _S1547 = as_type<float>((&kernelContext_0)->frameParameters_0[(4956U)>>2]);
    float4 _S1548 = float4(_S1544, _S1545, _S1546, _S1547);
    float _S1549 = as_type<float>((&kernelContext_0)->frameParameters_0[(4960U)>>2]);
    float _S1550 = as_type<float>((&kernelContext_0)->frameParameters_0[(4964U)>>2]);
    float _S1551 = as_type<float>((&kernelContext_0)->frameParameters_0[(4968U)>>2]);
    float _S1552 = as_type<float>((&kernelContext_0)->frameParameters_0[(4972U)>>2]);
    float4 _S1553 = float4(_S1549, _S1550, _S1551, _S1552);
    float _S1554 = as_type<float>((&kernelContext_0)->frameParameters_0[(4976U)>>2]);
    float _S1555 = as_type<float>((&kernelContext_0)->frameParameters_0[(4980U)>>2]);
    float _S1556 = as_type<float>((&kernelContext_0)->frameParameters_0[(4984U)>>2]);
    float _S1557 = as_type<float>((&kernelContext_0)->frameParameters_0[(4988U)>>2]);
    float4 _S1558 = float4(_S1554, _S1555, _S1556, _S1557);
    float _S1559 = as_type<float>((&kernelContext_0)->frameParameters_0[(4992U)>>2]);
    float _S1560 = as_type<float>((&kernelContext_0)->frameParameters_0[(4996U)>>2]);
    float _S1561 = as_type<float>((&kernelContext_0)->frameParameters_0[(5000U)>>2]);
    float _S1562 = as_type<float>((&kernelContext_0)->frameParameters_0[(5004U)>>2]);
    float4 _S1563 = float4(_S1559, _S1560, _S1561, _S1562);
    float _S1564 = as_type<float>((&kernelContext_0)->frameParameters_0[(5008U)>>2]);
    float _S1565 = as_type<float>((&kernelContext_0)->frameParameters_0[(5012U)>>2]);
    float _S1566 = as_type<float>((&kernelContext_0)->frameParameters_0[(5016U)>>2]);
    float _S1567 = as_type<float>((&kernelContext_0)->frameParameters_0[(5020U)>>2]);
    float4 _S1568 = float4(_S1564, _S1565, _S1566, _S1567);
    float _S1569 = as_type<float>((&kernelContext_0)->frameParameters_0[(5024U)>>2]);
    float _S1570 = as_type<float>((&kernelContext_0)->frameParameters_0[(5028U)>>2]);
    float _S1571 = as_type<float>((&kernelContext_0)->frameParameters_0[(5032U)>>2]);
    float _S1572 = as_type<float>((&kernelContext_0)->frameParameters_0[(5036U)>>2]);
    float4 _S1573 = float4(_S1569, _S1570, _S1571, _S1572);
    float _S1574 = as_type<float>((&kernelContext_0)->frameParameters_0[(5040U)>>2]);
    float _S1575 = as_type<float>((&kernelContext_0)->frameParameters_0[(5044U)>>2]);
    float _S1576 = as_type<float>((&kernelContext_0)->frameParameters_0[(5048U)>>2]);
    float _S1577 = as_type<float>((&kernelContext_0)->frameParameters_0[(5052U)>>2]);
    float4 _S1578 = float4(_S1574, _S1575, _S1576, _S1577);
    float _S1579 = as_type<float>((&kernelContext_0)->frameParameters_0[(5056U)>>2]);
    float _S1580 = as_type<float>((&kernelContext_0)->frameParameters_0[(5060U)>>2]);
    float _S1581 = as_type<float>((&kernelContext_0)->frameParameters_0[(5064U)>>2]);
    float _S1582 = as_type<float>((&kernelContext_0)->frameParameters_0[(5068U)>>2]);
    float4 _S1583 = float4(_S1579, _S1580, _S1581, _S1582);
    float _S1584 = as_type<float>((&kernelContext_0)->frameParameters_0[(5072U)>>2]);
    float _S1585 = as_type<float>((&kernelContext_0)->frameParameters_0[(5076U)>>2]);
    float _S1586 = as_type<float>((&kernelContext_0)->frameParameters_0[(5080U)>>2]);
    float _S1587 = as_type<float>((&kernelContext_0)->frameParameters_0[(5084U)>>2]);
    float4 _S1588 = float4(_S1584, _S1585, _S1586, _S1587);
    float _S1589 = as_type<float>((&kernelContext_0)->frameParameters_0[(5088U)>>2]);
    float _S1590 = as_type<float>((&kernelContext_0)->frameParameters_0[(5092U)>>2]);
    float _S1591 = as_type<float>((&kernelContext_0)->frameParameters_0[(5096U)>>2]);
    float _S1592 = as_type<float>((&kernelContext_0)->frameParameters_0[(5100U)>>2]);
    float4 _S1593 = float4(_S1589, _S1590, _S1591, _S1592);
    float _S1594 = as_type<float>((&kernelContext_0)->frameParameters_0[(5104U)>>2]);
    float _S1595 = as_type<float>((&kernelContext_0)->frameParameters_0[(5108U)>>2]);
    float _S1596 = as_type<float>((&kernelContext_0)->frameParameters_0[(5112U)>>2]);
    float _S1597 = as_type<float>((&kernelContext_0)->frameParameters_0[(5116U)>>2]);
    float4 _S1598 = float4(_S1594, _S1595, _S1596, _S1597);
    float _S1599 = as_type<float>((&kernelContext_0)->frameParameters_0[(5120U)>>2]);
    float _S1600 = as_type<float>((&kernelContext_0)->frameParameters_0[(5124U)>>2]);
    float _S1601 = as_type<float>((&kernelContext_0)->frameParameters_0[(5128U)>>2]);
    float _S1602 = as_type<float>((&kernelContext_0)->frameParameters_0[(5132U)>>2]);
    float4 _S1603 = float4(_S1599, _S1600, _S1601, _S1602);
    float _S1604 = as_type<float>((&kernelContext_0)->frameParameters_0[(5136U)>>2]);
    float _S1605 = as_type<float>((&kernelContext_0)->frameParameters_0[(5140U)>>2]);
    float _S1606 = as_type<float>((&kernelContext_0)->frameParameters_0[(5144U)>>2]);
    float _S1607 = as_type<float>((&kernelContext_0)->frameParameters_0[(5148U)>>2]);
    float4 _S1608 = float4(_S1604, _S1605, _S1606, _S1607);
    float _S1609 = as_type<float>((&kernelContext_0)->frameParameters_0[(5152U)>>2]);
    float _S1610 = as_type<float>((&kernelContext_0)->frameParameters_0[(5156U)>>2]);
    float _S1611 = as_type<float>((&kernelContext_0)->frameParameters_0[(5160U)>>2]);
    float _S1612 = as_type<float>((&kernelContext_0)->frameParameters_0[(5164U)>>2]);
    float4 _S1613 = float4(_S1609, _S1610, _S1611, _S1612);
    float _S1614 = as_type<float>((&kernelContext_0)->frameParameters_0[(5168U)>>2]);
    float _S1615 = as_type<float>((&kernelContext_0)->frameParameters_0[(5172U)>>2]);
    float _S1616 = as_type<float>((&kernelContext_0)->frameParameters_0[(5176U)>>2]);
    float _S1617 = as_type<float>((&kernelContext_0)->frameParameters_0[(5180U)>>2]);
    float4 _S1618 = float4(_S1614, _S1615, _S1616, _S1617);
    float _S1619 = as_type<float>((&kernelContext_0)->frameParameters_0[(5184U)>>2]);
    float _S1620 = as_type<float>((&kernelContext_0)->frameParameters_0[(5188U)>>2]);
    float _S1621 = as_type<float>((&kernelContext_0)->frameParameters_0[(5192U)>>2]);
    float _S1622 = as_type<float>((&kernelContext_0)->frameParameters_0[(5196U)>>2]);
    float4 _S1623 = float4(_S1619, _S1620, _S1621, _S1622);
    float _S1624 = as_type<float>((&kernelContext_0)->frameParameters_0[(5200U)>>2]);
    float _S1625 = as_type<float>((&kernelContext_0)->frameParameters_0[(5204U)>>2]);
    float _S1626 = as_type<float>((&kernelContext_0)->frameParameters_0[(5208U)>>2]);
    float _S1627 = as_type<float>((&kernelContext_0)->frameParameters_0[(5212U)>>2]);
    float4 _S1628 = float4(_S1624, _S1625, _S1626, _S1627);
    float _S1629 = as_type<float>((&kernelContext_0)->frameParameters_0[(5216U)>>2]);
    float _S1630 = as_type<float>((&kernelContext_0)->frameParameters_0[(5220U)>>2]);
    float _S1631 = as_type<float>((&kernelContext_0)->frameParameters_0[(5224U)>>2]);
    float _S1632 = as_type<float>((&kernelContext_0)->frameParameters_0[(5228U)>>2]);
    float4 _S1633 = float4(_S1629, _S1630, _S1631, _S1632);
    float _S1634 = as_type<float>((&kernelContext_0)->frameParameters_0[(5232U)>>2]);
    float _S1635 = as_type<float>((&kernelContext_0)->frameParameters_0[(5236U)>>2]);
    float _S1636 = as_type<float>((&kernelContext_0)->frameParameters_0[(5240U)>>2]);
    float _S1637 = as_type<float>((&kernelContext_0)->frameParameters_0[(5244U)>>2]);
    float4 _S1638 = float4(_S1634, _S1635, _S1636, _S1637);
    float _S1639 = as_type<float>((&kernelContext_0)->frameParameters_0[(5248U)>>2]);
    float _S1640 = as_type<float>((&kernelContext_0)->frameParameters_0[(5252U)>>2]);
    float _S1641 = as_type<float>((&kernelContext_0)->frameParameters_0[(5256U)>>2]);
    float _S1642 = as_type<float>((&kernelContext_0)->frameParameters_0[(5260U)>>2]);
    float4 _S1643 = float4(_S1639, _S1640, _S1641, _S1642);
    float _S1644 = as_type<float>((&kernelContext_0)->frameParameters_0[(5264U)>>2]);
    float _S1645 = as_type<float>((&kernelContext_0)->frameParameters_0[(5268U)>>2]);
    float _S1646 = as_type<float>((&kernelContext_0)->frameParameters_0[(5272U)>>2]);
    float _S1647 = as_type<float>((&kernelContext_0)->frameParameters_0[(5276U)>>2]);
    float4 _S1648 = float4(_S1644, _S1645, _S1646, _S1647);
    float _S1649 = as_type<float>((&kernelContext_0)->frameParameters_0[(5280U)>>2]);
    float _S1650 = as_type<float>((&kernelContext_0)->frameParameters_0[(5284U)>>2]);
    float _S1651 = as_type<float>((&kernelContext_0)->frameParameters_0[(5288U)>>2]);
    float _S1652 = as_type<float>((&kernelContext_0)->frameParameters_0[(5292U)>>2]);
    float4 _S1653 = float4(_S1649, _S1650, _S1651, _S1652);
    float _S1654 = as_type<float>((&kernelContext_0)->frameParameters_0[(5296U)>>2]);
    float _S1655 = as_type<float>((&kernelContext_0)->frameParameters_0[(5300U)>>2]);
    float _S1656 = as_type<float>((&kernelContext_0)->frameParameters_0[(5304U)>>2]);
    float _S1657 = as_type<float>((&kernelContext_0)->frameParameters_0[(5308U)>>2]);
    float4 _S1658 = float4(_S1654, _S1655, _S1656, _S1657);
    float _S1659 = as_type<float>((&kernelContext_0)->frameParameters_0[(5312U)>>2]);
    float _S1660 = as_type<float>((&kernelContext_0)->frameParameters_0[(5316U)>>2]);
    float _S1661 = as_type<float>((&kernelContext_0)->frameParameters_0[(5320U)>>2]);
    float _S1662 = as_type<float>((&kernelContext_0)->frameParameters_0[(5324U)>>2]);
    float4 _S1663 = float4(_S1659, _S1660, _S1661, _S1662);
    float _S1664 = as_type<float>((&kernelContext_0)->frameParameters_0[(5328U)>>2]);
    float _S1665 = as_type<float>((&kernelContext_0)->frameParameters_0[(5332U)>>2]);
    float _S1666 = as_type<float>((&kernelContext_0)->frameParameters_0[(5336U)>>2]);
    float _S1667 = as_type<float>((&kernelContext_0)->frameParameters_0[(5340U)>>2]);
    float4 _S1668 = float4(_S1664, _S1665, _S1666, _S1667);
    float _S1669 = as_type<float>((&kernelContext_0)->frameParameters_0[(5344U)>>2]);
    float _S1670 = as_type<float>((&kernelContext_0)->frameParameters_0[(5348U)>>2]);
    float _S1671 = as_type<float>((&kernelContext_0)->frameParameters_0[(5352U)>>2]);
    float _S1672 = as_type<float>((&kernelContext_0)->frameParameters_0[(5356U)>>2]);
    float4 _S1673 = float4(_S1669, _S1670, _S1671, _S1672);
    float _S1674 = as_type<float>((&kernelContext_0)->frameParameters_0[(5360U)>>2]);
    float _S1675 = as_type<float>((&kernelContext_0)->frameParameters_0[(5364U)>>2]);
    float _S1676 = as_type<float>((&kernelContext_0)->frameParameters_0[(5368U)>>2]);
    float _S1677 = as_type<float>((&kernelContext_0)->frameParameters_0[(5372U)>>2]);
    float4 _S1678 = float4(_S1674, _S1675, _S1676, _S1677);
    float _S1679 = as_type<float>((&kernelContext_0)->frameParameters_0[(5376U)>>2]);
    float _S1680 = as_type<float>((&kernelContext_0)->frameParameters_0[(5380U)>>2]);
    float _S1681 = as_type<float>((&kernelContext_0)->frameParameters_0[(5384U)>>2]);
    float _S1682 = as_type<float>((&kernelContext_0)->frameParameters_0[(5388U)>>2]);
    float4 _S1683 = float4(_S1679, _S1680, _S1681, _S1682);
    float _S1684 = as_type<float>((&kernelContext_0)->frameParameters_0[(5392U)>>2]);
    float _S1685 = as_type<float>((&kernelContext_0)->frameParameters_0[(5396U)>>2]);
    float _S1686 = as_type<float>((&kernelContext_0)->frameParameters_0[(5400U)>>2]);
    float _S1687 = as_type<float>((&kernelContext_0)->frameParameters_0[(5404U)>>2]);
    float4 _S1688 = float4(_S1684, _S1685, _S1686, _S1687);
    float _S1689 = as_type<float>((&kernelContext_0)->frameParameters_0[(5408U)>>2]);
    float _S1690 = as_type<float>((&kernelContext_0)->frameParameters_0[(5412U)>>2]);
    float _S1691 = as_type<float>((&kernelContext_0)->frameParameters_0[(5416U)>>2]);
    float _S1692 = as_type<float>((&kernelContext_0)->frameParameters_0[(5420U)>>2]);
    float4 _S1693 = float4(_S1689, _S1690, _S1691, _S1692);
    float _S1694 = as_type<float>((&kernelContext_0)->frameParameters_0[(5424U)>>2]);
    float _S1695 = as_type<float>((&kernelContext_0)->frameParameters_0[(5428U)>>2]);
    float _S1696 = as_type<float>((&kernelContext_0)->frameParameters_0[(5432U)>>2]);
    float _S1697 = as_type<float>((&kernelContext_0)->frameParameters_0[(5436U)>>2]);
    float4 _S1698 = float4(_S1694, _S1695, _S1696, _S1697);
    float _S1699 = as_type<float>((&kernelContext_0)->frameParameters_0[(5440U)>>2]);
    float _S1700 = as_type<float>((&kernelContext_0)->frameParameters_0[(5444U)>>2]);
    float _S1701 = as_type<float>((&kernelContext_0)->frameParameters_0[(5448U)>>2]);
    float _S1702 = as_type<float>((&kernelContext_0)->frameParameters_0[(5452U)>>2]);
    float4 _S1703 = float4(_S1699, _S1700, _S1701, _S1702);
    float _S1704 = as_type<float>((&kernelContext_0)->frameParameters_0[(5456U)>>2]);
    float _S1705 = as_type<float>((&kernelContext_0)->frameParameters_0[(5460U)>>2]);
    float _S1706 = as_type<float>((&kernelContext_0)->frameParameters_0[(5464U)>>2]);
    float _S1707 = as_type<float>((&kernelContext_0)->frameParameters_0[(5468U)>>2]);
    float4 _S1708 = float4(_S1704, _S1705, _S1706, _S1707);
    float _S1709 = as_type<float>((&kernelContext_0)->frameParameters_0[(5472U)>>2]);
    float _S1710 = as_type<float>((&kernelContext_0)->frameParameters_0[(5476U)>>2]);
    float _S1711 = as_type<float>((&kernelContext_0)->frameParameters_0[(5480U)>>2]);
    float _S1712 = as_type<float>((&kernelContext_0)->frameParameters_0[(5484U)>>2]);
    float4 _S1713 = float4(_S1709, _S1710, _S1711, _S1712);
    float _S1714 = as_type<float>((&kernelContext_0)->frameParameters_0[(5488U)>>2]);
    float _S1715 = as_type<float>((&kernelContext_0)->frameParameters_0[(5492U)>>2]);
    float _S1716 = as_type<float>((&kernelContext_0)->frameParameters_0[(5496U)>>2]);
    float _S1717 = as_type<float>((&kernelContext_0)->frameParameters_0[(5500U)>>2]);
    float4 _S1718 = float4(_S1714, _S1715, _S1716, _S1717);
    float _S1719 = as_type<float>((&kernelContext_0)->frameParameters_0[(5504U)>>2]);
    float _S1720 = as_type<float>((&kernelContext_0)->frameParameters_0[(5508U)>>2]);
    float _S1721 = as_type<float>((&kernelContext_0)->frameParameters_0[(5512U)>>2]);
    float _S1722 = as_type<float>((&kernelContext_0)->frameParameters_0[(5516U)>>2]);
    float4 _S1723 = float4(_S1719, _S1720, _S1721, _S1722);
    float _S1724 = as_type<float>((&kernelContext_0)->frameParameters_0[(5520U)>>2]);
    float _S1725 = as_type<float>((&kernelContext_0)->frameParameters_0[(5524U)>>2]);
    float _S1726 = as_type<float>((&kernelContext_0)->frameParameters_0[(5528U)>>2]);
    float _S1727 = as_type<float>((&kernelContext_0)->frameParameters_0[(5532U)>>2]);
    float4 _S1728 = float4(_S1724, _S1725, _S1726, _S1727);
    float _S1729 = as_type<float>((&kernelContext_0)->frameParameters_0[(5536U)>>2]);
    float _S1730 = as_type<float>((&kernelContext_0)->frameParameters_0[(5540U)>>2]);
    float _S1731 = as_type<float>((&kernelContext_0)->frameParameters_0[(5544U)>>2]);
    float _S1732 = as_type<float>((&kernelContext_0)->frameParameters_0[(5548U)>>2]);
    float4 _S1733 = float4(_S1729, _S1730, _S1731, _S1732);
    float _S1734 = as_type<float>((&kernelContext_0)->frameParameters_0[(5552U)>>2]);
    float _S1735 = as_type<float>((&kernelContext_0)->frameParameters_0[(5556U)>>2]);
    float _S1736 = as_type<float>((&kernelContext_0)->frameParameters_0[(5560U)>>2]);
    float _S1737 = as_type<float>((&kernelContext_0)->frameParameters_0[(5564U)>>2]);
    float4 _S1738 = float4(_S1734, _S1735, _S1736, _S1737);
    float _S1739 = as_type<float>((&kernelContext_0)->frameParameters_0[(5568U)>>2]);
    float _S1740 = as_type<float>((&kernelContext_0)->frameParameters_0[(5572U)>>2]);
    float _S1741 = as_type<float>((&kernelContext_0)->frameParameters_0[(5576U)>>2]);
    float _S1742 = as_type<float>((&kernelContext_0)->frameParameters_0[(5580U)>>2]);
    float4 _S1743 = float4(_S1739, _S1740, _S1741, _S1742);
    float _S1744 = as_type<float>((&kernelContext_0)->frameParameters_0[(5584U)>>2]);
    float _S1745 = as_type<float>((&kernelContext_0)->frameParameters_0[(5588U)>>2]);
    float _S1746 = as_type<float>((&kernelContext_0)->frameParameters_0[(5592U)>>2]);
    float _S1747 = as_type<float>((&kernelContext_0)->frameParameters_0[(5596U)>>2]);
    float4 _S1748 = float4(_S1744, _S1745, _S1746, _S1747);
    float _S1749 = as_type<float>((&kernelContext_0)->frameParameters_0[(5600U)>>2]);
    float _S1750 = as_type<float>((&kernelContext_0)->frameParameters_0[(5604U)>>2]);
    float _S1751 = as_type<float>((&kernelContext_0)->frameParameters_0[(5608U)>>2]);
    float _S1752 = as_type<float>((&kernelContext_0)->frameParameters_0[(5612U)>>2]);
    float4 _S1753 = float4(_S1749, _S1750, _S1751, _S1752);
    float _S1754 = as_type<float>((&kernelContext_0)->frameParameters_0[(5616U)>>2]);
    float _S1755 = as_type<float>((&kernelContext_0)->frameParameters_0[(5620U)>>2]);
    float _S1756 = as_type<float>((&kernelContext_0)->frameParameters_0[(5624U)>>2]);
    float _S1757 = as_type<float>((&kernelContext_0)->frameParameters_0[(5628U)>>2]);
    float4 _S1758 = float4(_S1754, _S1755, _S1756, _S1757);
    float _S1759 = as_type<float>((&kernelContext_0)->frameParameters_0[(5632U)>>2]);
    float _S1760 = as_type<float>((&kernelContext_0)->frameParameters_0[(5636U)>>2]);
    float _S1761 = as_type<float>((&kernelContext_0)->frameParameters_0[(5640U)>>2]);
    float _S1762 = as_type<float>((&kernelContext_0)->frameParameters_0[(5644U)>>2]);
    float4 _S1763 = float4(_S1759, _S1760, _S1761, _S1762);
    float _S1764 = as_type<float>((&kernelContext_0)->frameParameters_0[(5648U)>>2]);
    float _S1765 = as_type<float>((&kernelContext_0)->frameParameters_0[(5652U)>>2]);
    float _S1766 = as_type<float>((&kernelContext_0)->frameParameters_0[(5656U)>>2]);
    float _S1767 = as_type<float>((&kernelContext_0)->frameParameters_0[(5660U)>>2]);
    float4 _S1768 = float4(_S1764, _S1765, _S1766, _S1767);
    float _S1769 = as_type<float>((&kernelContext_0)->frameParameters_0[(5664U)>>2]);
    float _S1770 = as_type<float>((&kernelContext_0)->frameParameters_0[(5668U)>>2]);
    float _S1771 = as_type<float>((&kernelContext_0)->frameParameters_0[(5672U)>>2]);
    float _S1772 = as_type<float>((&kernelContext_0)->frameParameters_0[(5676U)>>2]);
    float4 _S1773 = float4(_S1769, _S1770, _S1771, _S1772);
    float _S1774 = as_type<float>((&kernelContext_0)->frameParameters_0[(5680U)>>2]);
    float _S1775 = as_type<float>((&kernelContext_0)->frameParameters_0[(5684U)>>2]);
    float _S1776 = as_type<float>((&kernelContext_0)->frameParameters_0[(5688U)>>2]);
    float _S1777 = as_type<float>((&kernelContext_0)->frameParameters_0[(5692U)>>2]);
    float4 _S1778 = float4(_S1774, _S1775, _S1776, _S1777);
    float _S1779 = as_type<float>((&kernelContext_0)->frameParameters_0[(5696U)>>2]);
    float _S1780 = as_type<float>((&kernelContext_0)->frameParameters_0[(5700U)>>2]);
    float _S1781 = as_type<float>((&kernelContext_0)->frameParameters_0[(5704U)>>2]);
    float _S1782 = as_type<float>((&kernelContext_0)->frameParameters_0[(5708U)>>2]);
    float4 _S1783 = float4(_S1779, _S1780, _S1781, _S1782);
    float _S1784 = as_type<float>((&kernelContext_0)->frameParameters_0[(5712U)>>2]);
    float _S1785 = as_type<float>((&kernelContext_0)->frameParameters_0[(5716U)>>2]);
    float _S1786 = as_type<float>((&kernelContext_0)->frameParameters_0[(5720U)>>2]);
    float _S1787 = as_type<float>((&kernelContext_0)->frameParameters_0[(5724U)>>2]);
    float4 _S1788 = float4(_S1784, _S1785, _S1786, _S1787);
    float _S1789 = as_type<float>((&kernelContext_0)->frameParameters_0[(5728U)>>2]);
    float _S1790 = as_type<float>((&kernelContext_0)->frameParameters_0[(5732U)>>2]);
    float _S1791 = as_type<float>((&kernelContext_0)->frameParameters_0[(5736U)>>2]);
    float _S1792 = as_type<float>((&kernelContext_0)->frameParameters_0[(5740U)>>2]);
    float4 _S1793 = float4(_S1789, _S1790, _S1791, _S1792);
    float _S1794 = as_type<float>((&kernelContext_0)->frameParameters_0[(5744U)>>2]);
    float _S1795 = as_type<float>((&kernelContext_0)->frameParameters_0[(5748U)>>2]);
    float _S1796 = as_type<float>((&kernelContext_0)->frameParameters_0[(5752U)>>2]);
    float _S1797 = as_type<float>((&kernelContext_0)->frameParameters_0[(5756U)>>2]);
    float4 _S1798 = float4(_S1794, _S1795, _S1796, _S1797);
    float _S1799 = as_type<float>((&kernelContext_0)->frameParameters_0[(5760U)>>2]);
    float _S1800 = as_type<float>((&kernelContext_0)->frameParameters_0[(5764U)>>2]);
    float _S1801 = as_type<float>((&kernelContext_0)->frameParameters_0[(5768U)>>2]);
    float _S1802 = as_type<float>((&kernelContext_0)->frameParameters_0[(5772U)>>2]);
    float4 _S1803 = float4(_S1799, _S1800, _S1801, _S1802);
    float _S1804 = as_type<float>((&kernelContext_0)->frameParameters_0[(5776U)>>2]);
    float _S1805 = as_type<float>((&kernelContext_0)->frameParameters_0[(5780U)>>2]);
    float _S1806 = as_type<float>((&kernelContext_0)->frameParameters_0[(5784U)>>2]);
    float _S1807 = as_type<float>((&kernelContext_0)->frameParameters_0[(5788U)>>2]);
    float4 _S1808 = float4(_S1804, _S1805, _S1806, _S1807);
    float _S1809 = as_type<float>((&kernelContext_0)->frameParameters_0[(5792U)>>2]);
    float _S1810 = as_type<float>((&kernelContext_0)->frameParameters_0[(5796U)>>2]);
    float _S1811 = as_type<float>((&kernelContext_0)->frameParameters_0[(5800U)>>2]);
    float _S1812 = as_type<float>((&kernelContext_0)->frameParameters_0[(5804U)>>2]);
    float4 _S1813 = float4(_S1809, _S1810, _S1811, _S1812);
    float _S1814 = as_type<float>((&kernelContext_0)->frameParameters_0[(5808U)>>2]);
    float _S1815 = as_type<float>((&kernelContext_0)->frameParameters_0[(5812U)>>2]);
    float _S1816 = as_type<float>((&kernelContext_0)->frameParameters_0[(5816U)>>2]);
    float _S1817 = as_type<float>((&kernelContext_0)->frameParameters_0[(5820U)>>2]);
    float4 _S1818 = float4(_S1814, _S1815, _S1816, _S1817);
    float _S1819 = as_type<float>((&kernelContext_0)->frameParameters_0[(5824U)>>2]);
    float _S1820 = as_type<float>((&kernelContext_0)->frameParameters_0[(5828U)>>2]);
    float _S1821 = as_type<float>((&kernelContext_0)->frameParameters_0[(5832U)>>2]);
    float _S1822 = as_type<float>((&kernelContext_0)->frameParameters_0[(5836U)>>2]);
    float4 _S1823 = float4(_S1819, _S1820, _S1821, _S1822);
    float _S1824 = as_type<float>((&kernelContext_0)->frameParameters_0[(5840U)>>2]);
    float _S1825 = as_type<float>((&kernelContext_0)->frameParameters_0[(5844U)>>2]);
    float _S1826 = as_type<float>((&kernelContext_0)->frameParameters_0[(5848U)>>2]);
    float _S1827 = as_type<float>((&kernelContext_0)->frameParameters_0[(5852U)>>2]);
    float4 _S1828 = float4(_S1824, _S1825, _S1826, _S1827);
    float _S1829 = as_type<float>((&kernelContext_0)->frameParameters_0[(5856U)>>2]);
    float _S1830 = as_type<float>((&kernelContext_0)->frameParameters_0[(5860U)>>2]);
    float _S1831 = as_type<float>((&kernelContext_0)->frameParameters_0[(5864U)>>2]);
    float _S1832 = as_type<float>((&kernelContext_0)->frameParameters_0[(5868U)>>2]);
    float4 _S1833 = float4(_S1829, _S1830, _S1831, _S1832);
    float _S1834 = as_type<float>((&kernelContext_0)->frameParameters_0[(5872U)>>2]);
    float _S1835 = as_type<float>((&kernelContext_0)->frameParameters_0[(5876U)>>2]);
    float _S1836 = as_type<float>((&kernelContext_0)->frameParameters_0[(5880U)>>2]);
    float _S1837 = as_type<float>((&kernelContext_0)->frameParameters_0[(5884U)>>2]);
    float4 _S1838 = float4(_S1834, _S1835, _S1836, _S1837);
    float _S1839 = as_type<float>((&kernelContext_0)->frameParameters_0[(5888U)>>2]);
    float _S1840 = as_type<float>((&kernelContext_0)->frameParameters_0[(5892U)>>2]);
    float _S1841 = as_type<float>((&kernelContext_0)->frameParameters_0[(5896U)>>2]);
    float _S1842 = as_type<float>((&kernelContext_0)->frameParameters_0[(5900U)>>2]);
    float4 _S1843 = float4(_S1839, _S1840, _S1841, _S1842);
    float _S1844 = as_type<float>((&kernelContext_0)->frameParameters_0[(5904U)>>2]);
    float _S1845 = as_type<float>((&kernelContext_0)->frameParameters_0[(5908U)>>2]);
    float _S1846 = as_type<float>((&kernelContext_0)->frameParameters_0[(5912U)>>2]);
    float _S1847 = as_type<float>((&kernelContext_0)->frameParameters_0[(5916U)>>2]);
    float4 _S1848 = float4(_S1844, _S1845, _S1846, _S1847);
    float _S1849 = as_type<float>((&kernelContext_0)->frameParameters_0[(5920U)>>2]);
    float _S1850 = as_type<float>((&kernelContext_0)->frameParameters_0[(5924U)>>2]);
    float _S1851 = as_type<float>((&kernelContext_0)->frameParameters_0[(5928U)>>2]);
    float _S1852 = as_type<float>((&kernelContext_0)->frameParameters_0[(5932U)>>2]);
    float4 _S1853 = float4(_S1849, _S1850, _S1851, _S1852);
    float _S1854 = as_type<float>((&kernelContext_0)->frameParameters_0[(5936U)>>2]);
    float _S1855 = as_type<float>((&kernelContext_0)->frameParameters_0[(5940U)>>2]);
    float _S1856 = as_type<float>((&kernelContext_0)->frameParameters_0[(5944U)>>2]);
    float _S1857 = as_type<float>((&kernelContext_0)->frameParameters_0[(5948U)>>2]);
    float4 _S1858 = float4(_S1854, _S1855, _S1856, _S1857);
    float _S1859 = as_type<float>((&kernelContext_0)->frameParameters_0[(5952U)>>2]);
    float _S1860 = as_type<float>((&kernelContext_0)->frameParameters_0[(5956U)>>2]);
    float _S1861 = as_type<float>((&kernelContext_0)->frameParameters_0[(5960U)>>2]);
    float _S1862 = as_type<float>((&kernelContext_0)->frameParameters_0[(5964U)>>2]);
    float4 _S1863 = float4(_S1859, _S1860, _S1861, _S1862);
    float _S1864 = as_type<float>((&kernelContext_0)->frameParameters_0[(5968U)>>2]);
    float _S1865 = as_type<float>((&kernelContext_0)->frameParameters_0[(5972U)>>2]);
    float _S1866 = as_type<float>((&kernelContext_0)->frameParameters_0[(5976U)>>2]);
    float _S1867 = as_type<float>((&kernelContext_0)->frameParameters_0[(5980U)>>2]);
    float4 _S1868 = float4(_S1864, _S1865, _S1866, _S1867);
    float _S1869 = as_type<float>((&kernelContext_0)->frameParameters_0[(5984U)>>2]);
    float _S1870 = as_type<float>((&kernelContext_0)->frameParameters_0[(5988U)>>2]);
    float _S1871 = as_type<float>((&kernelContext_0)->frameParameters_0[(5992U)>>2]);
    float _S1872 = as_type<float>((&kernelContext_0)->frameParameters_0[(5996U)>>2]);
    float4 _S1873 = float4(_S1869, _S1870, _S1871, _S1872);
    float _S1874 = as_type<float>((&kernelContext_0)->frameParameters_0[(6000U)>>2]);
    float _S1875 = as_type<float>((&kernelContext_0)->frameParameters_0[(6004U)>>2]);
    float _S1876 = as_type<float>((&kernelContext_0)->frameParameters_0[(6008U)>>2]);
    float _S1877 = as_type<float>((&kernelContext_0)->frameParameters_0[(6012U)>>2]);
    float4 _S1878 = float4(_S1874, _S1875, _S1876, _S1877);
    float _S1879 = as_type<float>((&kernelContext_0)->frameParameters_0[(6016U)>>2]);
    float _S1880 = as_type<float>((&kernelContext_0)->frameParameters_0[(6020U)>>2]);
    float _S1881 = as_type<float>((&kernelContext_0)->frameParameters_0[(6024U)>>2]);
    float _S1882 = as_type<float>((&kernelContext_0)->frameParameters_0[(6028U)>>2]);
    float4 _S1883 = float4(_S1879, _S1880, _S1881, _S1882);
    float _S1884 = as_type<float>((&kernelContext_0)->frameParameters_0[(6032U)>>2]);
    float _S1885 = as_type<float>((&kernelContext_0)->frameParameters_0[(6036U)>>2]);
    float _S1886 = as_type<float>((&kernelContext_0)->frameParameters_0[(6040U)>>2]);
    float _S1887 = as_type<float>((&kernelContext_0)->frameParameters_0[(6044U)>>2]);
    float4 _S1888 = float4(_S1884, _S1885, _S1886, _S1887);
    float _S1889 = as_type<float>((&kernelContext_0)->frameParameters_0[(6048U)>>2]);
    float _S1890 = as_type<float>((&kernelContext_0)->frameParameters_0[(6052U)>>2]);
    float _S1891 = as_type<float>((&kernelContext_0)->frameParameters_0[(6056U)>>2]);
    float _S1892 = as_type<float>((&kernelContext_0)->frameParameters_0[(6060U)>>2]);
    float4 _S1893 = float4(_S1889, _S1890, _S1891, _S1892);
    float _S1894 = as_type<float>((&kernelContext_0)->frameParameters_0[(6064U)>>2]);
    float _S1895 = as_type<float>((&kernelContext_0)->frameParameters_0[(6068U)>>2]);
    float _S1896 = as_type<float>((&kernelContext_0)->frameParameters_0[(6072U)>>2]);
    float _S1897 = as_type<float>((&kernelContext_0)->frameParameters_0[(6076U)>>2]);
    float4 _S1898 = float4(_S1894, _S1895, _S1896, _S1897);
    float _S1899 = as_type<float>((&kernelContext_0)->frameParameters_0[(6080U)>>2]);
    float _S1900 = as_type<float>((&kernelContext_0)->frameParameters_0[(6084U)>>2]);
    float _S1901 = as_type<float>((&kernelContext_0)->frameParameters_0[(6088U)>>2]);
    float _S1902 = as_type<float>((&kernelContext_0)->frameParameters_0[(6092U)>>2]);
    float4 _S1903 = float4(_S1899, _S1900, _S1901, _S1902);
    float _S1904 = as_type<float>((&kernelContext_0)->frameParameters_0[(6096U)>>2]);
    float _S1905 = as_type<float>((&kernelContext_0)->frameParameters_0[(6100U)>>2]);
    float _S1906 = as_type<float>((&kernelContext_0)->frameParameters_0[(6104U)>>2]);
    float _S1907 = as_type<float>((&kernelContext_0)->frameParameters_0[(6108U)>>2]);
    float4 _S1908 = float4(_S1904, _S1905, _S1906, _S1907);
    float _S1909 = as_type<float>((&kernelContext_0)->frameParameters_0[(6112U)>>2]);
    float _S1910 = as_type<float>((&kernelContext_0)->frameParameters_0[(6116U)>>2]);
    float _S1911 = as_type<float>((&kernelContext_0)->frameParameters_0[(6120U)>>2]);
    float _S1912 = as_type<float>((&kernelContext_0)->frameParameters_0[(6124U)>>2]);
    float4 _S1913 = float4(_S1909, _S1910, _S1911, _S1912);
    float _S1914 = as_type<float>((&kernelContext_0)->frameParameters_0[(6128U)>>2]);
    float _S1915 = as_type<float>((&kernelContext_0)->frameParameters_0[(6132U)>>2]);
    float _S1916 = as_type<float>((&kernelContext_0)->frameParameters_0[(6136U)>>2]);
    float _S1917 = as_type<float>((&kernelContext_0)->frameParameters_0[(6140U)>>2]);
    float4 _S1918 = float4(_S1914, _S1915, _S1916, _S1917);
    float _S1919 = as_type<float>((&kernelContext_0)->frameParameters_0[(6144U)>>2]);
    float _S1920 = as_type<float>((&kernelContext_0)->frameParameters_0[(6148U)>>2]);
    float _S1921 = as_type<float>((&kernelContext_0)->frameParameters_0[(6152U)>>2]);
    float _S1922 = as_type<float>((&kernelContext_0)->frameParameters_0[(6156U)>>2]);
    float4 _S1923 = float4(_S1919, _S1920, _S1921, _S1922);
    float _S1924 = as_type<float>((&kernelContext_0)->frameParameters_0[(6160U)>>2]);
    float _S1925 = as_type<float>((&kernelContext_0)->frameParameters_0[(6164U)>>2]);
    float _S1926 = as_type<float>((&kernelContext_0)->frameParameters_0[(6168U)>>2]);
    float _S1927 = as_type<float>((&kernelContext_0)->frameParameters_0[(6172U)>>2]);
    float4 _S1928 = float4(_S1924, _S1925, _S1926, _S1927);
    float _S1929 = as_type<float>((&kernelContext_0)->frameParameters_0[(6176U)>>2]);
    float _S1930 = as_type<float>((&kernelContext_0)->frameParameters_0[(6180U)>>2]);
    float _S1931 = as_type<float>((&kernelContext_0)->frameParameters_0[(6184U)>>2]);
    float _S1932 = as_type<float>((&kernelContext_0)->frameParameters_0[(6188U)>>2]);
    float4 _S1933 = float4(_S1929, _S1930, _S1931, _S1932);
    float _S1934 = as_type<float>((&kernelContext_0)->frameParameters_0[(6192U)>>2]);
    float _S1935 = as_type<float>((&kernelContext_0)->frameParameters_0[(6196U)>>2]);
    float _S1936 = as_type<float>((&kernelContext_0)->frameParameters_0[(6200U)>>2]);
    float _S1937 = as_type<float>((&kernelContext_0)->frameParameters_0[(6204U)>>2]);
    float4 _S1938 = float4(_S1934, _S1935, _S1936, _S1937);
    float _S1939 = as_type<float>((&kernelContext_0)->frameParameters_0[(6208U)>>2]);
    float _S1940 = as_type<float>((&kernelContext_0)->frameParameters_0[(6212U)>>2]);
    float _S1941 = as_type<float>((&kernelContext_0)->frameParameters_0[(6216U)>>2]);
    float _S1942 = as_type<float>((&kernelContext_0)->frameParameters_0[(6220U)>>2]);
    float4 _S1943 = float4(_S1939, _S1940, _S1941, _S1942);
    float _S1944 = as_type<float>((&kernelContext_0)->frameParameters_0[(6224U)>>2]);
    float _S1945 = as_type<float>((&kernelContext_0)->frameParameters_0[(6228U)>>2]);
    float _S1946 = as_type<float>((&kernelContext_0)->frameParameters_0[(6232U)>>2]);
    float _S1947 = as_type<float>((&kernelContext_0)->frameParameters_0[(6236U)>>2]);
    float4 _S1948 = float4(_S1944, _S1945, _S1946, _S1947);
    float _S1949 = as_type<float>((&kernelContext_0)->frameParameters_0[(6240U)>>2]);
    float _S1950 = as_type<float>((&kernelContext_0)->frameParameters_0[(6244U)>>2]);
    float _S1951 = as_type<float>((&kernelContext_0)->frameParameters_0[(6248U)>>2]);
    float _S1952 = as_type<float>((&kernelContext_0)->frameParameters_0[(6252U)>>2]);
    float4 _S1953 = float4(_S1949, _S1950, _S1951, _S1952);
    float _S1954 = as_type<float>((&kernelContext_0)->frameParameters_0[(6256U)>>2]);
    float _S1955 = as_type<float>((&kernelContext_0)->frameParameters_0[(6260U)>>2]);
    float _S1956 = as_type<float>((&kernelContext_0)->frameParameters_0[(6264U)>>2]);
    float _S1957 = as_type<float>((&kernelContext_0)->frameParameters_0[(6268U)>>2]);
    float4 _S1958 = float4(_S1954, _S1955, _S1956, _S1957);
    float _S1959 = as_type<float>((&kernelContext_0)->frameParameters_0[(6272U)>>2]);
    float _S1960 = as_type<float>((&kernelContext_0)->frameParameters_0[(6276U)>>2]);
    float _S1961 = as_type<float>((&kernelContext_0)->frameParameters_0[(6280U)>>2]);
    float _S1962 = as_type<float>((&kernelContext_0)->frameParameters_0[(6284U)>>2]);
    float4 _S1963 = float4(_S1959, _S1960, _S1961, _S1962);
    float _S1964 = as_type<float>((&kernelContext_0)->frameParameters_0[(6288U)>>2]);
    float _S1965 = as_type<float>((&kernelContext_0)->frameParameters_0[(6292U)>>2]);
    float _S1966 = as_type<float>((&kernelContext_0)->frameParameters_0[(6296U)>>2]);
    float _S1967 = as_type<float>((&kernelContext_0)->frameParameters_0[(6300U)>>2]);
    float4 _S1968 = float4(_S1964, _S1965, _S1966, _S1967);
    float _S1969 = as_type<float>((&kernelContext_0)->frameParameters_0[(6304U)>>2]);
    float _S1970 = as_type<float>((&kernelContext_0)->frameParameters_0[(6308U)>>2]);
    float _S1971 = as_type<float>((&kernelContext_0)->frameParameters_0[(6312U)>>2]);
    float _S1972 = as_type<float>((&kernelContext_0)->frameParameters_0[(6316U)>>2]);
    float4 _S1973 = float4(_S1969, _S1970, _S1971, _S1972);
    float _S1974 = as_type<float>((&kernelContext_0)->frameParameters_0[(6320U)>>2]);
    float _S1975 = as_type<float>((&kernelContext_0)->frameParameters_0[(6324U)>>2]);
    float _S1976 = as_type<float>((&kernelContext_0)->frameParameters_0[(6328U)>>2]);
    float _S1977 = as_type<float>((&kernelContext_0)->frameParameters_0[(6332U)>>2]);
    float4 _S1978 = float4(_S1974, _S1975, _S1976, _S1977);
    float _S1979 = as_type<float>((&kernelContext_0)->frameParameters_0[(6336U)>>2]);
    float _S1980 = as_type<float>((&kernelContext_0)->frameParameters_0[(6340U)>>2]);
    float _S1981 = as_type<float>((&kernelContext_0)->frameParameters_0[(6344U)>>2]);
    float _S1982 = as_type<float>((&kernelContext_0)->frameParameters_0[(6348U)>>2]);
    float4 _S1983 = float4(_S1979, _S1980, _S1981, _S1982);
    float _S1984 = as_type<float>((&kernelContext_0)->frameParameters_0[(6352U)>>2]);
    float _S1985 = as_type<float>((&kernelContext_0)->frameParameters_0[(6356U)>>2]);
    float _S1986 = as_type<float>((&kernelContext_0)->frameParameters_0[(6360U)>>2]);
    float _S1987 = as_type<float>((&kernelContext_0)->frameParameters_0[(6364U)>>2]);
    array<float4, int(128)> _S1988 = { _S1353, _S1358, _S1363, _S1368, _S1373, _S1378, _S1383, _S1388, _S1393, _S1398, _S1403, _S1408, _S1413, _S1418, _S1423, _S1428, _S1433, _S1438, _S1443, _S1448, _S1453, _S1458, _S1463, _S1468, _S1473, _S1478, _S1483, _S1488, _S1493, _S1498, _S1503, _S1508, _S1513, _S1518, _S1523, _S1528, _S1533, _S1538, _S1543, _S1548, _S1553, _S1558, _S1563, _S1568, _S1573, _S1578, _S1583, _S1588, _S1593, _S1598, _S1603, _S1608, _S1613, _S1618, _S1623, _S1628, _S1633, _S1638, _S1643, _S1648, _S1653, _S1658, _S1663, _S1668, _S1673, _S1678, _S1683, _S1688, _S1693, _S1698, _S1703, _S1708, _S1713, _S1718, _S1723, _S1728, _S1733, _S1738, _S1743, _S1748, _S1753, _S1758, _S1763, _S1768, _S1773, _S1778, _S1783, _S1788, _S1793, _S1798, _S1803, _S1808, _S1813, _S1818, _S1823, _S1828, _S1833, _S1838, _S1843, _S1848, _S1853, _S1858, _S1863, _S1868, _S1873, _S1878, _S1883, _S1888, _S1893, _S1898, _S1903, _S1908, _S1913, _S1918, _S1923, _S1928, _S1933, _S1938, _S1943, _S1948, _S1953, _S1958, _S1963, _S1968, _S1973, _S1978, _S1983, float4(_S1984, _S1985, _S1986, _S1987) };
    float _S1989 = as_type<float>((&kernelContext_0)->frameParameters_0[(6368U)>>2]);
    float _S1990 = as_type<float>((&kernelContext_0)->frameParameters_0[(6372U)>>2]);
    float _S1991 = as_type<float>((&kernelContext_0)->frameParameters_0[(6376U)>>2]);
    float _S1992 = as_type<float>((&kernelContext_0)->frameParameters_0[(6380U)>>2]);
    float4 _S1993 = float4(_S1989, _S1990, _S1991, _S1992);
    float _S1994 = as_type<float>((&kernelContext_0)->frameParameters_0[(6384U)>>2]);
    float _S1995 = as_type<float>((&kernelContext_0)->frameParameters_0[(6388U)>>2]);
    float _S1996 = as_type<float>((&kernelContext_0)->frameParameters_0[(6392U)>>2]);
    float _S1997 = as_type<float>((&kernelContext_0)->frameParameters_0[(6396U)>>2]);
    float4 _S1998 = float4(_S1994, _S1995, _S1996, _S1997);
    float _S1999 = as_type<float>((&kernelContext_0)->frameParameters_0[(6400U)>>2]);
    float _S2000 = as_type<float>((&kernelContext_0)->frameParameters_0[(6404U)>>2]);
    float _S2001 = as_type<float>((&kernelContext_0)->frameParameters_0[(6408U)>>2]);
    float _S2002 = as_type<float>((&kernelContext_0)->frameParameters_0[(6412U)>>2]);
    float4 _S2003 = float4(_S1999, _S2000, _S2001, _S2002);
    float _S2004 = as_type<float>((&kernelContext_0)->frameParameters_0[(6416U)>>2]);
    float _S2005 = as_type<float>((&kernelContext_0)->frameParameters_0[(6420U)>>2]);
    float _S2006 = as_type<float>((&kernelContext_0)->frameParameters_0[(6424U)>>2]);
    float _S2007 = as_type<float>((&kernelContext_0)->frameParameters_0[(6428U)>>2]);
    float4 _S2008 = float4(_S2004, _S2005, _S2006, _S2007);
    float _S2009 = as_type<float>((&kernelContext_0)->frameParameters_0[(6432U)>>2]);
    float _S2010 = as_type<float>((&kernelContext_0)->frameParameters_0[(6436U)>>2]);
    float _S2011 = as_type<float>((&kernelContext_0)->frameParameters_0[(6440U)>>2]);
    float _S2012 = as_type<float>((&kernelContext_0)->frameParameters_0[(6444U)>>2]);
    float4 _S2013 = float4(_S2009, _S2010, _S2011, _S2012);
    float _S2014 = as_type<float>((&kernelContext_0)->frameParameters_0[(6448U)>>2]);
    float _S2015 = as_type<float>((&kernelContext_0)->frameParameters_0[(6452U)>>2]);
    float _S2016 = as_type<float>((&kernelContext_0)->frameParameters_0[(6456U)>>2]);
    float _S2017 = as_type<float>((&kernelContext_0)->frameParameters_0[(6460U)>>2]);
    float4 _S2018 = float4(_S2014, _S2015, _S2016, _S2017);
    float _S2019 = as_type<float>((&kernelContext_0)->frameParameters_0[(6464U)>>2]);
    float _S2020 = as_type<float>((&kernelContext_0)->frameParameters_0[(6468U)>>2]);
    float _S2021 = as_type<float>((&kernelContext_0)->frameParameters_0[(6472U)>>2]);
    float _S2022 = as_type<float>((&kernelContext_0)->frameParameters_0[(6476U)>>2]);
    float4 _S2023 = float4(_S2019, _S2020, _S2021, _S2022);
    float _S2024 = as_type<float>((&kernelContext_0)->frameParameters_0[(6480U)>>2]);
    float _S2025 = as_type<float>((&kernelContext_0)->frameParameters_0[(6484U)>>2]);
    float _S2026 = as_type<float>((&kernelContext_0)->frameParameters_0[(6488U)>>2]);
    float _S2027 = as_type<float>((&kernelContext_0)->frameParameters_0[(6492U)>>2]);
    float4 _S2028 = float4(_S2024, _S2025, _S2026, _S2027);
    float _S2029 = as_type<float>((&kernelContext_0)->frameParameters_0[(6496U)>>2]);
    float _S2030 = as_type<float>((&kernelContext_0)->frameParameters_0[(6500U)>>2]);
    float _S2031 = as_type<float>((&kernelContext_0)->frameParameters_0[(6504U)>>2]);
    float _S2032 = as_type<float>((&kernelContext_0)->frameParameters_0[(6508U)>>2]);
    float4 _S2033 = float4(_S2029, _S2030, _S2031, _S2032);
    float _S2034 = as_type<float>((&kernelContext_0)->frameParameters_0[(6512U)>>2]);
    float _S2035 = as_type<float>((&kernelContext_0)->frameParameters_0[(6516U)>>2]);
    float _S2036 = as_type<float>((&kernelContext_0)->frameParameters_0[(6520U)>>2]);
    float _S2037 = as_type<float>((&kernelContext_0)->frameParameters_0[(6524U)>>2]);
    float4 _S2038 = float4(_S2034, _S2035, _S2036, _S2037);
    float _S2039 = as_type<float>((&kernelContext_0)->frameParameters_0[(6528U)>>2]);
    float _S2040 = as_type<float>((&kernelContext_0)->frameParameters_0[(6532U)>>2]);
    float _S2041 = as_type<float>((&kernelContext_0)->frameParameters_0[(6536U)>>2]);
    float _S2042 = as_type<float>((&kernelContext_0)->frameParameters_0[(6540U)>>2]);
    float4 _S2043 = float4(_S2039, _S2040, _S2041, _S2042);
    float _S2044 = as_type<float>((&kernelContext_0)->frameParameters_0[(6544U)>>2]);
    float _S2045 = as_type<float>((&kernelContext_0)->frameParameters_0[(6548U)>>2]);
    float _S2046 = as_type<float>((&kernelContext_0)->frameParameters_0[(6552U)>>2]);
    float _S2047 = as_type<float>((&kernelContext_0)->frameParameters_0[(6556U)>>2]);
    float4 _S2048 = float4(_S2044, _S2045, _S2046, _S2047);
    float _S2049 = as_type<float>((&kernelContext_0)->frameParameters_0[(6560U)>>2]);
    float _S2050 = as_type<float>((&kernelContext_0)->frameParameters_0[(6564U)>>2]);
    float _S2051 = as_type<float>((&kernelContext_0)->frameParameters_0[(6568U)>>2]);
    float _S2052 = as_type<float>((&kernelContext_0)->frameParameters_0[(6572U)>>2]);
    float4 _S2053 = float4(_S2049, _S2050, _S2051, _S2052);
    float _S2054 = as_type<float>((&kernelContext_0)->frameParameters_0[(6576U)>>2]);
    float _S2055 = as_type<float>((&kernelContext_0)->frameParameters_0[(6580U)>>2]);
    float _S2056 = as_type<float>((&kernelContext_0)->frameParameters_0[(6584U)>>2]);
    float _S2057 = as_type<float>((&kernelContext_0)->frameParameters_0[(6588U)>>2]);
    float4 _S2058 = float4(_S2054, _S2055, _S2056, _S2057);
    float _S2059 = as_type<float>((&kernelContext_0)->frameParameters_0[(6592U)>>2]);
    float _S2060 = as_type<float>((&kernelContext_0)->frameParameters_0[(6596U)>>2]);
    float _S2061 = as_type<float>((&kernelContext_0)->frameParameters_0[(6600U)>>2]);
    float _S2062 = as_type<float>((&kernelContext_0)->frameParameters_0[(6604U)>>2]);
    float4 _S2063 = float4(_S2059, _S2060, _S2061, _S2062);
    float _S2064 = as_type<float>((&kernelContext_0)->frameParameters_0[(6608U)>>2]);
    float _S2065 = as_type<float>((&kernelContext_0)->frameParameters_0[(6612U)>>2]);
    float _S2066 = as_type<float>((&kernelContext_0)->frameParameters_0[(6616U)>>2]);
    float _S2067 = as_type<float>((&kernelContext_0)->frameParameters_0[(6620U)>>2]);
    float4 _S2068 = float4(_S2064, _S2065, _S2066, _S2067);
    float _S2069 = as_type<float>((&kernelContext_0)->frameParameters_0[(6624U)>>2]);
    float _S2070 = as_type<float>((&kernelContext_0)->frameParameters_0[(6628U)>>2]);
    float _S2071 = as_type<float>((&kernelContext_0)->frameParameters_0[(6632U)>>2]);
    float _S2072 = as_type<float>((&kernelContext_0)->frameParameters_0[(6636U)>>2]);
    float4 _S2073 = float4(_S2069, _S2070, _S2071, _S2072);
    float _S2074 = as_type<float>((&kernelContext_0)->frameParameters_0[(6640U)>>2]);
    float _S2075 = as_type<float>((&kernelContext_0)->frameParameters_0[(6644U)>>2]);
    float _S2076 = as_type<float>((&kernelContext_0)->frameParameters_0[(6648U)>>2]);
    float _S2077 = as_type<float>((&kernelContext_0)->frameParameters_0[(6652U)>>2]);
    float4 _S2078 = float4(_S2074, _S2075, _S2076, _S2077);
    float _S2079 = as_type<float>((&kernelContext_0)->frameParameters_0[(6656U)>>2]);
    float _S2080 = as_type<float>((&kernelContext_0)->frameParameters_0[(6660U)>>2]);
    float _S2081 = as_type<float>((&kernelContext_0)->frameParameters_0[(6664U)>>2]);
    float _S2082 = as_type<float>((&kernelContext_0)->frameParameters_0[(6668U)>>2]);
    float4 _S2083 = float4(_S2079, _S2080, _S2081, _S2082);
    float _S2084 = as_type<float>((&kernelContext_0)->frameParameters_0[(6672U)>>2]);
    float _S2085 = as_type<float>((&kernelContext_0)->frameParameters_0[(6676U)>>2]);
    float _S2086 = as_type<float>((&kernelContext_0)->frameParameters_0[(6680U)>>2]);
    float _S2087 = as_type<float>((&kernelContext_0)->frameParameters_0[(6684U)>>2]);
    float4 _S2088 = float4(_S2084, _S2085, _S2086, _S2087);
    float _S2089 = as_type<float>((&kernelContext_0)->frameParameters_0[(6688U)>>2]);
    float _S2090 = as_type<float>((&kernelContext_0)->frameParameters_0[(6692U)>>2]);
    float _S2091 = as_type<float>((&kernelContext_0)->frameParameters_0[(6696U)>>2]);
    float _S2092 = as_type<float>((&kernelContext_0)->frameParameters_0[(6700U)>>2]);
    float4 _S2093 = float4(_S2089, _S2090, _S2091, _S2092);
    float _S2094 = as_type<float>((&kernelContext_0)->frameParameters_0[(6704U)>>2]);
    float _S2095 = as_type<float>((&kernelContext_0)->frameParameters_0[(6708U)>>2]);
    float _S2096 = as_type<float>((&kernelContext_0)->frameParameters_0[(6712U)>>2]);
    float _S2097 = as_type<float>((&kernelContext_0)->frameParameters_0[(6716U)>>2]);
    float4 _S2098 = float4(_S2094, _S2095, _S2096, _S2097);
    float _S2099 = as_type<float>((&kernelContext_0)->frameParameters_0[(6720U)>>2]);
    float _S2100 = as_type<float>((&kernelContext_0)->frameParameters_0[(6724U)>>2]);
    float _S2101 = as_type<float>((&kernelContext_0)->frameParameters_0[(6728U)>>2]);
    float _S2102 = as_type<float>((&kernelContext_0)->frameParameters_0[(6732U)>>2]);
    float4 _S2103 = float4(_S2099, _S2100, _S2101, _S2102);
    float _S2104 = as_type<float>((&kernelContext_0)->frameParameters_0[(6736U)>>2]);
    float _S2105 = as_type<float>((&kernelContext_0)->frameParameters_0[(6740U)>>2]);
    float _S2106 = as_type<float>((&kernelContext_0)->frameParameters_0[(6744U)>>2]);
    float _S2107 = as_type<float>((&kernelContext_0)->frameParameters_0[(6748U)>>2]);
    float4 _S2108 = float4(_S2104, _S2105, _S2106, _S2107);
    float _S2109 = as_type<float>((&kernelContext_0)->frameParameters_0[(6752U)>>2]);
    float _S2110 = as_type<float>((&kernelContext_0)->frameParameters_0[(6756U)>>2]);
    float _S2111 = as_type<float>((&kernelContext_0)->frameParameters_0[(6760U)>>2]);
    float _S2112 = as_type<float>((&kernelContext_0)->frameParameters_0[(6764U)>>2]);
    float4 _S2113 = float4(_S2109, _S2110, _S2111, _S2112);
    float _S2114 = as_type<float>((&kernelContext_0)->frameParameters_0[(6768U)>>2]);
    float _S2115 = as_type<float>((&kernelContext_0)->frameParameters_0[(6772U)>>2]);
    float _S2116 = as_type<float>((&kernelContext_0)->frameParameters_0[(6776U)>>2]);
    float _S2117 = as_type<float>((&kernelContext_0)->frameParameters_0[(6780U)>>2]);
    float4 _S2118 = float4(_S2114, _S2115, _S2116, _S2117);
    float _S2119 = as_type<float>((&kernelContext_0)->frameParameters_0[(6784U)>>2]);
    float _S2120 = as_type<float>((&kernelContext_0)->frameParameters_0[(6788U)>>2]);
    float _S2121 = as_type<float>((&kernelContext_0)->frameParameters_0[(6792U)>>2]);
    float _S2122 = as_type<float>((&kernelContext_0)->frameParameters_0[(6796U)>>2]);
    float4 _S2123 = float4(_S2119, _S2120, _S2121, _S2122);
    float _S2124 = as_type<float>((&kernelContext_0)->frameParameters_0[(6800U)>>2]);
    float _S2125 = as_type<float>((&kernelContext_0)->frameParameters_0[(6804U)>>2]);
    float _S2126 = as_type<float>((&kernelContext_0)->frameParameters_0[(6808U)>>2]);
    float _S2127 = as_type<float>((&kernelContext_0)->frameParameters_0[(6812U)>>2]);
    float4 _S2128 = float4(_S2124, _S2125, _S2126, _S2127);
    float _S2129 = as_type<float>((&kernelContext_0)->frameParameters_0[(6816U)>>2]);
    float _S2130 = as_type<float>((&kernelContext_0)->frameParameters_0[(6820U)>>2]);
    float _S2131 = as_type<float>((&kernelContext_0)->frameParameters_0[(6824U)>>2]);
    float _S2132 = as_type<float>((&kernelContext_0)->frameParameters_0[(6828U)>>2]);
    float4 _S2133 = float4(_S2129, _S2130, _S2131, _S2132);
    float _S2134 = as_type<float>((&kernelContext_0)->frameParameters_0[(6832U)>>2]);
    float _S2135 = as_type<float>((&kernelContext_0)->frameParameters_0[(6836U)>>2]);
    float _S2136 = as_type<float>((&kernelContext_0)->frameParameters_0[(6840U)>>2]);
    float _S2137 = as_type<float>((&kernelContext_0)->frameParameters_0[(6844U)>>2]);
    float4 _S2138 = float4(_S2134, _S2135, _S2136, _S2137);
    float _S2139 = as_type<float>((&kernelContext_0)->frameParameters_0[(6848U)>>2]);
    float _S2140 = as_type<float>((&kernelContext_0)->frameParameters_0[(6852U)>>2]);
    float _S2141 = as_type<float>((&kernelContext_0)->frameParameters_0[(6856U)>>2]);
    float _S2142 = as_type<float>((&kernelContext_0)->frameParameters_0[(6860U)>>2]);
    float4 _S2143 = float4(_S2139, _S2140, _S2141, _S2142);
    float _S2144 = as_type<float>((&kernelContext_0)->frameParameters_0[(6864U)>>2]);
    float _S2145 = as_type<float>((&kernelContext_0)->frameParameters_0[(6868U)>>2]);
    float _S2146 = as_type<float>((&kernelContext_0)->frameParameters_0[(6872U)>>2]);
    float _S2147 = as_type<float>((&kernelContext_0)->frameParameters_0[(6876U)>>2]);
    float4 _S2148 = float4(_S2144, _S2145, _S2146, _S2147);
    float _S2149 = as_type<float>((&kernelContext_0)->frameParameters_0[(6880U)>>2]);
    float _S2150 = as_type<float>((&kernelContext_0)->frameParameters_0[(6884U)>>2]);
    float _S2151 = as_type<float>((&kernelContext_0)->frameParameters_0[(6888U)>>2]);
    float _S2152 = as_type<float>((&kernelContext_0)->frameParameters_0[(6892U)>>2]);
    float4 _S2153 = float4(_S2149, _S2150, _S2151, _S2152);
    float _S2154 = as_type<float>((&kernelContext_0)->frameParameters_0[(6896U)>>2]);
    float _S2155 = as_type<float>((&kernelContext_0)->frameParameters_0[(6900U)>>2]);
    float _S2156 = as_type<float>((&kernelContext_0)->frameParameters_0[(6904U)>>2]);
    float _S2157 = as_type<float>((&kernelContext_0)->frameParameters_0[(6908U)>>2]);
    float4 _S2158 = float4(_S2154, _S2155, _S2156, _S2157);
    float _S2159 = as_type<float>((&kernelContext_0)->frameParameters_0[(6912U)>>2]);
    float _S2160 = as_type<float>((&kernelContext_0)->frameParameters_0[(6916U)>>2]);
    float _S2161 = as_type<float>((&kernelContext_0)->frameParameters_0[(6920U)>>2]);
    float _S2162 = as_type<float>((&kernelContext_0)->frameParameters_0[(6924U)>>2]);
    float4 _S2163 = float4(_S2159, _S2160, _S2161, _S2162);
    float _S2164 = as_type<float>((&kernelContext_0)->frameParameters_0[(6928U)>>2]);
    float _S2165 = as_type<float>((&kernelContext_0)->frameParameters_0[(6932U)>>2]);
    float _S2166 = as_type<float>((&kernelContext_0)->frameParameters_0[(6936U)>>2]);
    float _S2167 = as_type<float>((&kernelContext_0)->frameParameters_0[(6940U)>>2]);
    float4 _S2168 = float4(_S2164, _S2165, _S2166, _S2167);
    float _S2169 = as_type<float>((&kernelContext_0)->frameParameters_0[(6944U)>>2]);
    float _S2170 = as_type<float>((&kernelContext_0)->frameParameters_0[(6948U)>>2]);
    float _S2171 = as_type<float>((&kernelContext_0)->frameParameters_0[(6952U)>>2]);
    float _S2172 = as_type<float>((&kernelContext_0)->frameParameters_0[(6956U)>>2]);
    float4 _S2173 = float4(_S2169, _S2170, _S2171, _S2172);
    float _S2174 = as_type<float>((&kernelContext_0)->frameParameters_0[(6960U)>>2]);
    float _S2175 = as_type<float>((&kernelContext_0)->frameParameters_0[(6964U)>>2]);
    float _S2176 = as_type<float>((&kernelContext_0)->frameParameters_0[(6968U)>>2]);
    float _S2177 = as_type<float>((&kernelContext_0)->frameParameters_0[(6972U)>>2]);
    float4 _S2178 = float4(_S2174, _S2175, _S2176, _S2177);
    float _S2179 = as_type<float>((&kernelContext_0)->frameParameters_0[(6976U)>>2]);
    float _S2180 = as_type<float>((&kernelContext_0)->frameParameters_0[(6980U)>>2]);
    float _S2181 = as_type<float>((&kernelContext_0)->frameParameters_0[(6984U)>>2]);
    float _S2182 = as_type<float>((&kernelContext_0)->frameParameters_0[(6988U)>>2]);
    float4 _S2183 = float4(_S2179, _S2180, _S2181, _S2182);
    float _S2184 = as_type<float>((&kernelContext_0)->frameParameters_0[(6992U)>>2]);
    float _S2185 = as_type<float>((&kernelContext_0)->frameParameters_0[(6996U)>>2]);
    float _S2186 = as_type<float>((&kernelContext_0)->frameParameters_0[(7000U)>>2]);
    float _S2187 = as_type<float>((&kernelContext_0)->frameParameters_0[(7004U)>>2]);
    float4 _S2188 = float4(_S2184, _S2185, _S2186, _S2187);
    float _S2189 = as_type<float>((&kernelContext_0)->frameParameters_0[(7008U)>>2]);
    float _S2190 = as_type<float>((&kernelContext_0)->frameParameters_0[(7012U)>>2]);
    float _S2191 = as_type<float>((&kernelContext_0)->frameParameters_0[(7016U)>>2]);
    float _S2192 = as_type<float>((&kernelContext_0)->frameParameters_0[(7020U)>>2]);
    float4 _S2193 = float4(_S2189, _S2190, _S2191, _S2192);
    float _S2194 = as_type<float>((&kernelContext_0)->frameParameters_0[(7024U)>>2]);
    float _S2195 = as_type<float>((&kernelContext_0)->frameParameters_0[(7028U)>>2]);
    float _S2196 = as_type<float>((&kernelContext_0)->frameParameters_0[(7032U)>>2]);
    float _S2197 = as_type<float>((&kernelContext_0)->frameParameters_0[(7036U)>>2]);
    float4 _S2198 = float4(_S2194, _S2195, _S2196, _S2197);
    float _S2199 = as_type<float>((&kernelContext_0)->frameParameters_0[(7040U)>>2]);
    float _S2200 = as_type<float>((&kernelContext_0)->frameParameters_0[(7044U)>>2]);
    float _S2201 = as_type<float>((&kernelContext_0)->frameParameters_0[(7048U)>>2]);
    float _S2202 = as_type<float>((&kernelContext_0)->frameParameters_0[(7052U)>>2]);
    float4 _S2203 = float4(_S2199, _S2200, _S2201, _S2202);
    float _S2204 = as_type<float>((&kernelContext_0)->frameParameters_0[(7056U)>>2]);
    float _S2205 = as_type<float>((&kernelContext_0)->frameParameters_0[(7060U)>>2]);
    float _S2206 = as_type<float>((&kernelContext_0)->frameParameters_0[(7064U)>>2]);
    float _S2207 = as_type<float>((&kernelContext_0)->frameParameters_0[(7068U)>>2]);
    float4 _S2208 = float4(_S2204, _S2205, _S2206, _S2207);
    float _S2209 = as_type<float>((&kernelContext_0)->frameParameters_0[(7072U)>>2]);
    float _S2210 = as_type<float>((&kernelContext_0)->frameParameters_0[(7076U)>>2]);
    float _S2211 = as_type<float>((&kernelContext_0)->frameParameters_0[(7080U)>>2]);
    float _S2212 = as_type<float>((&kernelContext_0)->frameParameters_0[(7084U)>>2]);
    float4 _S2213 = float4(_S2209, _S2210, _S2211, _S2212);
    float _S2214 = as_type<float>((&kernelContext_0)->frameParameters_0[(7088U)>>2]);
    float _S2215 = as_type<float>((&kernelContext_0)->frameParameters_0[(7092U)>>2]);
    float _S2216 = as_type<float>((&kernelContext_0)->frameParameters_0[(7096U)>>2]);
    float _S2217 = as_type<float>((&kernelContext_0)->frameParameters_0[(7100U)>>2]);
    float4 _S2218 = float4(_S2214, _S2215, _S2216, _S2217);
    float _S2219 = as_type<float>((&kernelContext_0)->frameParameters_0[(7104U)>>2]);
    float _S2220 = as_type<float>((&kernelContext_0)->frameParameters_0[(7108U)>>2]);
    float _S2221 = as_type<float>((&kernelContext_0)->frameParameters_0[(7112U)>>2]);
    float _S2222 = as_type<float>((&kernelContext_0)->frameParameters_0[(7116U)>>2]);
    float4 _S2223 = float4(_S2219, _S2220, _S2221, _S2222);
    float _S2224 = as_type<float>((&kernelContext_0)->frameParameters_0[(7120U)>>2]);
    float _S2225 = as_type<float>((&kernelContext_0)->frameParameters_0[(7124U)>>2]);
    float _S2226 = as_type<float>((&kernelContext_0)->frameParameters_0[(7128U)>>2]);
    float _S2227 = as_type<float>((&kernelContext_0)->frameParameters_0[(7132U)>>2]);
    float4 _S2228 = float4(_S2224, _S2225, _S2226, _S2227);
    float _S2229 = as_type<float>((&kernelContext_0)->frameParameters_0[(7136U)>>2]);
    float _S2230 = as_type<float>((&kernelContext_0)->frameParameters_0[(7140U)>>2]);
    float _S2231 = as_type<float>((&kernelContext_0)->frameParameters_0[(7144U)>>2]);
    float _S2232 = as_type<float>((&kernelContext_0)->frameParameters_0[(7148U)>>2]);
    float4 _S2233 = float4(_S2229, _S2230, _S2231, _S2232);
    float _S2234 = as_type<float>((&kernelContext_0)->frameParameters_0[(7152U)>>2]);
    float _S2235 = as_type<float>((&kernelContext_0)->frameParameters_0[(7156U)>>2]);
    float _S2236 = as_type<float>((&kernelContext_0)->frameParameters_0[(7160U)>>2]);
    float _S2237 = as_type<float>((&kernelContext_0)->frameParameters_0[(7164U)>>2]);
    float4 _S2238 = float4(_S2234, _S2235, _S2236, _S2237);
    float _S2239 = as_type<float>((&kernelContext_0)->frameParameters_0[(7168U)>>2]);
    float _S2240 = as_type<float>((&kernelContext_0)->frameParameters_0[(7172U)>>2]);
    float _S2241 = as_type<float>((&kernelContext_0)->frameParameters_0[(7176U)>>2]);
    float _S2242 = as_type<float>((&kernelContext_0)->frameParameters_0[(7180U)>>2]);
    float4 _S2243 = float4(_S2239, _S2240, _S2241, _S2242);
    float _S2244 = as_type<float>((&kernelContext_0)->frameParameters_0[(7184U)>>2]);
    float _S2245 = as_type<float>((&kernelContext_0)->frameParameters_0[(7188U)>>2]);
    float _S2246 = as_type<float>((&kernelContext_0)->frameParameters_0[(7192U)>>2]);
    float _S2247 = as_type<float>((&kernelContext_0)->frameParameters_0[(7196U)>>2]);
    float4 _S2248 = float4(_S2244, _S2245, _S2246, _S2247);
    float _S2249 = as_type<float>((&kernelContext_0)->frameParameters_0[(7200U)>>2]);
    float _S2250 = as_type<float>((&kernelContext_0)->frameParameters_0[(7204U)>>2]);
    float _S2251 = as_type<float>((&kernelContext_0)->frameParameters_0[(7208U)>>2]);
    float _S2252 = as_type<float>((&kernelContext_0)->frameParameters_0[(7212U)>>2]);
    float4 _S2253 = float4(_S2249, _S2250, _S2251, _S2252);
    float _S2254 = as_type<float>((&kernelContext_0)->frameParameters_0[(7216U)>>2]);
    float _S2255 = as_type<float>((&kernelContext_0)->frameParameters_0[(7220U)>>2]);
    float _S2256 = as_type<float>((&kernelContext_0)->frameParameters_0[(7224U)>>2]);
    float _S2257 = as_type<float>((&kernelContext_0)->frameParameters_0[(7228U)>>2]);
    float4 _S2258 = float4(_S2254, _S2255, _S2256, _S2257);
    float _S2259 = as_type<float>((&kernelContext_0)->frameParameters_0[(7232U)>>2]);
    float _S2260 = as_type<float>((&kernelContext_0)->frameParameters_0[(7236U)>>2]);
    float _S2261 = as_type<float>((&kernelContext_0)->frameParameters_0[(7240U)>>2]);
    float _S2262 = as_type<float>((&kernelContext_0)->frameParameters_0[(7244U)>>2]);
    float4 _S2263 = float4(_S2259, _S2260, _S2261, _S2262);
    float _S2264 = as_type<float>((&kernelContext_0)->frameParameters_0[(7248U)>>2]);
    float _S2265 = as_type<float>((&kernelContext_0)->frameParameters_0[(7252U)>>2]);
    float _S2266 = as_type<float>((&kernelContext_0)->frameParameters_0[(7256U)>>2]);
    float _S2267 = as_type<float>((&kernelContext_0)->frameParameters_0[(7260U)>>2]);
    float4 _S2268 = float4(_S2264, _S2265, _S2266, _S2267);
    float _S2269 = as_type<float>((&kernelContext_0)->frameParameters_0[(7264U)>>2]);
    float _S2270 = as_type<float>((&kernelContext_0)->frameParameters_0[(7268U)>>2]);
    float _S2271 = as_type<float>((&kernelContext_0)->frameParameters_0[(7272U)>>2]);
    float _S2272 = as_type<float>((&kernelContext_0)->frameParameters_0[(7276U)>>2]);
    float4 _S2273 = float4(_S2269, _S2270, _S2271, _S2272);
    float _S2274 = as_type<float>((&kernelContext_0)->frameParameters_0[(7280U)>>2]);
    float _S2275 = as_type<float>((&kernelContext_0)->frameParameters_0[(7284U)>>2]);
    float _S2276 = as_type<float>((&kernelContext_0)->frameParameters_0[(7288U)>>2]);
    float _S2277 = as_type<float>((&kernelContext_0)->frameParameters_0[(7292U)>>2]);
    float4 _S2278 = float4(_S2274, _S2275, _S2276, _S2277);
    float _S2279 = as_type<float>((&kernelContext_0)->frameParameters_0[(7296U)>>2]);
    float _S2280 = as_type<float>((&kernelContext_0)->frameParameters_0[(7300U)>>2]);
    float _S2281 = as_type<float>((&kernelContext_0)->frameParameters_0[(7304U)>>2]);
    float _S2282 = as_type<float>((&kernelContext_0)->frameParameters_0[(7308U)>>2]);
    float4 _S2283 = float4(_S2279, _S2280, _S2281, _S2282);
    float _S2284 = as_type<float>((&kernelContext_0)->frameParameters_0[(7312U)>>2]);
    float _S2285 = as_type<float>((&kernelContext_0)->frameParameters_0[(7316U)>>2]);
    float _S2286 = as_type<float>((&kernelContext_0)->frameParameters_0[(7320U)>>2]);
    float _S2287 = as_type<float>((&kernelContext_0)->frameParameters_0[(7324U)>>2]);
    float4 _S2288 = float4(_S2284, _S2285, _S2286, _S2287);
    float _S2289 = as_type<float>((&kernelContext_0)->frameParameters_0[(7328U)>>2]);
    float _S2290 = as_type<float>((&kernelContext_0)->frameParameters_0[(7332U)>>2]);
    float _S2291 = as_type<float>((&kernelContext_0)->frameParameters_0[(7336U)>>2]);
    float _S2292 = as_type<float>((&kernelContext_0)->frameParameters_0[(7340U)>>2]);
    float4 _S2293 = float4(_S2289, _S2290, _S2291, _S2292);
    float _S2294 = as_type<float>((&kernelContext_0)->frameParameters_0[(7344U)>>2]);
    float _S2295 = as_type<float>((&kernelContext_0)->frameParameters_0[(7348U)>>2]);
    float _S2296 = as_type<float>((&kernelContext_0)->frameParameters_0[(7352U)>>2]);
    float _S2297 = as_type<float>((&kernelContext_0)->frameParameters_0[(7356U)>>2]);
    float4 _S2298 = float4(_S2294, _S2295, _S2296, _S2297);
    float _S2299 = as_type<float>((&kernelContext_0)->frameParameters_0[(7360U)>>2]);
    float _S2300 = as_type<float>((&kernelContext_0)->frameParameters_0[(7364U)>>2]);
    float _S2301 = as_type<float>((&kernelContext_0)->frameParameters_0[(7368U)>>2]);
    float _S2302 = as_type<float>((&kernelContext_0)->frameParameters_0[(7372U)>>2]);
    float4 _S2303 = float4(_S2299, _S2300, _S2301, _S2302);
    float _S2304 = as_type<float>((&kernelContext_0)->frameParameters_0[(7376U)>>2]);
    float _S2305 = as_type<float>((&kernelContext_0)->frameParameters_0[(7380U)>>2]);
    float _S2306 = as_type<float>((&kernelContext_0)->frameParameters_0[(7384U)>>2]);
    float _S2307 = as_type<float>((&kernelContext_0)->frameParameters_0[(7388U)>>2]);
    float4 _S2308 = float4(_S2304, _S2305, _S2306, _S2307);
    float _S2309 = as_type<float>((&kernelContext_0)->frameParameters_0[(7392U)>>2]);
    float _S2310 = as_type<float>((&kernelContext_0)->frameParameters_0[(7396U)>>2]);
    float _S2311 = as_type<float>((&kernelContext_0)->frameParameters_0[(7400U)>>2]);
    float _S2312 = as_type<float>((&kernelContext_0)->frameParameters_0[(7404U)>>2]);
    float4 _S2313 = float4(_S2309, _S2310, _S2311, _S2312);
    float _S2314 = as_type<float>((&kernelContext_0)->frameParameters_0[(7408U)>>2]);
    float _S2315 = as_type<float>((&kernelContext_0)->frameParameters_0[(7412U)>>2]);
    float _S2316 = as_type<float>((&kernelContext_0)->frameParameters_0[(7416U)>>2]);
    float _S2317 = as_type<float>((&kernelContext_0)->frameParameters_0[(7420U)>>2]);
    float4 _S2318 = float4(_S2314, _S2315, _S2316, _S2317);
    float _S2319 = as_type<float>((&kernelContext_0)->frameParameters_0[(7424U)>>2]);
    float _S2320 = as_type<float>((&kernelContext_0)->frameParameters_0[(7428U)>>2]);
    float _S2321 = as_type<float>((&kernelContext_0)->frameParameters_0[(7432U)>>2]);
    float _S2322 = as_type<float>((&kernelContext_0)->frameParameters_0[(7436U)>>2]);
    float4 _S2323 = float4(_S2319, _S2320, _S2321, _S2322);
    float _S2324 = as_type<float>((&kernelContext_0)->frameParameters_0[(7440U)>>2]);
    float _S2325 = as_type<float>((&kernelContext_0)->frameParameters_0[(7444U)>>2]);
    float _S2326 = as_type<float>((&kernelContext_0)->frameParameters_0[(7448U)>>2]);
    float _S2327 = as_type<float>((&kernelContext_0)->frameParameters_0[(7452U)>>2]);
    float4 _S2328 = float4(_S2324, _S2325, _S2326, _S2327);
    float _S2329 = as_type<float>((&kernelContext_0)->frameParameters_0[(7456U)>>2]);
    float _S2330 = as_type<float>((&kernelContext_0)->frameParameters_0[(7460U)>>2]);
    float _S2331 = as_type<float>((&kernelContext_0)->frameParameters_0[(7464U)>>2]);
    float _S2332 = as_type<float>((&kernelContext_0)->frameParameters_0[(7468U)>>2]);
    float4 _S2333 = float4(_S2329, _S2330, _S2331, _S2332);
    float _S2334 = as_type<float>((&kernelContext_0)->frameParameters_0[(7472U)>>2]);
    float _S2335 = as_type<float>((&kernelContext_0)->frameParameters_0[(7476U)>>2]);
    float _S2336 = as_type<float>((&kernelContext_0)->frameParameters_0[(7480U)>>2]);
    float _S2337 = as_type<float>((&kernelContext_0)->frameParameters_0[(7484U)>>2]);
    float4 _S2338 = float4(_S2334, _S2335, _S2336, _S2337);
    float _S2339 = as_type<float>((&kernelContext_0)->frameParameters_0[(7488U)>>2]);
    float _S2340 = as_type<float>((&kernelContext_0)->frameParameters_0[(7492U)>>2]);
    float _S2341 = as_type<float>((&kernelContext_0)->frameParameters_0[(7496U)>>2]);
    float _S2342 = as_type<float>((&kernelContext_0)->frameParameters_0[(7500U)>>2]);
    float4 _S2343 = float4(_S2339, _S2340, _S2341, _S2342);
    float _S2344 = as_type<float>((&kernelContext_0)->frameParameters_0[(7504U)>>2]);
    float _S2345 = as_type<float>((&kernelContext_0)->frameParameters_0[(7508U)>>2]);
    float _S2346 = as_type<float>((&kernelContext_0)->frameParameters_0[(7512U)>>2]);
    float _S2347 = as_type<float>((&kernelContext_0)->frameParameters_0[(7516U)>>2]);
    float4 _S2348 = float4(_S2344, _S2345, _S2346, _S2347);
    float _S2349 = as_type<float>((&kernelContext_0)->frameParameters_0[(7520U)>>2]);
    float _S2350 = as_type<float>((&kernelContext_0)->frameParameters_0[(7524U)>>2]);
    float _S2351 = as_type<float>((&kernelContext_0)->frameParameters_0[(7528U)>>2]);
    float _S2352 = as_type<float>((&kernelContext_0)->frameParameters_0[(7532U)>>2]);
    float4 _S2353 = float4(_S2349, _S2350, _S2351, _S2352);
    float _S2354 = as_type<float>((&kernelContext_0)->frameParameters_0[(7536U)>>2]);
    float _S2355 = as_type<float>((&kernelContext_0)->frameParameters_0[(7540U)>>2]);
    float _S2356 = as_type<float>((&kernelContext_0)->frameParameters_0[(7544U)>>2]);
    float _S2357 = as_type<float>((&kernelContext_0)->frameParameters_0[(7548U)>>2]);
    float4 _S2358 = float4(_S2354, _S2355, _S2356, _S2357);
    float _S2359 = as_type<float>((&kernelContext_0)->frameParameters_0[(7552U)>>2]);
    float _S2360 = as_type<float>((&kernelContext_0)->frameParameters_0[(7556U)>>2]);
    float _S2361 = as_type<float>((&kernelContext_0)->frameParameters_0[(7560U)>>2]);
    float _S2362 = as_type<float>((&kernelContext_0)->frameParameters_0[(7564U)>>2]);
    float4 _S2363 = float4(_S2359, _S2360, _S2361, _S2362);
    float _S2364 = as_type<float>((&kernelContext_0)->frameParameters_0[(7568U)>>2]);
    float _S2365 = as_type<float>((&kernelContext_0)->frameParameters_0[(7572U)>>2]);
    float _S2366 = as_type<float>((&kernelContext_0)->frameParameters_0[(7576U)>>2]);
    float _S2367 = as_type<float>((&kernelContext_0)->frameParameters_0[(7580U)>>2]);
    float4 _S2368 = float4(_S2364, _S2365, _S2366, _S2367);
    float _S2369 = as_type<float>((&kernelContext_0)->frameParameters_0[(7584U)>>2]);
    float _S2370 = as_type<float>((&kernelContext_0)->frameParameters_0[(7588U)>>2]);
    float _S2371 = as_type<float>((&kernelContext_0)->frameParameters_0[(7592U)>>2]);
    float _S2372 = as_type<float>((&kernelContext_0)->frameParameters_0[(7596U)>>2]);
    float4 _S2373 = float4(_S2369, _S2370, _S2371, _S2372);
    float _S2374 = as_type<float>((&kernelContext_0)->frameParameters_0[(7600U)>>2]);
    float _S2375 = as_type<float>((&kernelContext_0)->frameParameters_0[(7604U)>>2]);
    float _S2376 = as_type<float>((&kernelContext_0)->frameParameters_0[(7608U)>>2]);
    float _S2377 = as_type<float>((&kernelContext_0)->frameParameters_0[(7612U)>>2]);
    float4 _S2378 = float4(_S2374, _S2375, _S2376, _S2377);
    float _S2379 = as_type<float>((&kernelContext_0)->frameParameters_0[(7616U)>>2]);
    float _S2380 = as_type<float>((&kernelContext_0)->frameParameters_0[(7620U)>>2]);
    float _S2381 = as_type<float>((&kernelContext_0)->frameParameters_0[(7624U)>>2]);
    float _S2382 = as_type<float>((&kernelContext_0)->frameParameters_0[(7628U)>>2]);
    float4 _S2383 = float4(_S2379, _S2380, _S2381, _S2382);
    float _S2384 = as_type<float>((&kernelContext_0)->frameParameters_0[(7632U)>>2]);
    float _S2385 = as_type<float>((&kernelContext_0)->frameParameters_0[(7636U)>>2]);
    float _S2386 = as_type<float>((&kernelContext_0)->frameParameters_0[(7640U)>>2]);
    float _S2387 = as_type<float>((&kernelContext_0)->frameParameters_0[(7644U)>>2]);
    float4 _S2388 = float4(_S2384, _S2385, _S2386, _S2387);
    float _S2389 = as_type<float>((&kernelContext_0)->frameParameters_0[(7648U)>>2]);
    float _S2390 = as_type<float>((&kernelContext_0)->frameParameters_0[(7652U)>>2]);
    float _S2391 = as_type<float>((&kernelContext_0)->frameParameters_0[(7656U)>>2]);
    float _S2392 = as_type<float>((&kernelContext_0)->frameParameters_0[(7660U)>>2]);
    float4 _S2393 = float4(_S2389, _S2390, _S2391, _S2392);
    float _S2394 = as_type<float>((&kernelContext_0)->frameParameters_0[(7664U)>>2]);
    float _S2395 = as_type<float>((&kernelContext_0)->frameParameters_0[(7668U)>>2]);
    float _S2396 = as_type<float>((&kernelContext_0)->frameParameters_0[(7672U)>>2]);
    float _S2397 = as_type<float>((&kernelContext_0)->frameParameters_0[(7676U)>>2]);
    float4 _S2398 = float4(_S2394, _S2395, _S2396, _S2397);
    float _S2399 = as_type<float>((&kernelContext_0)->frameParameters_0[(7680U)>>2]);
    float _S2400 = as_type<float>((&kernelContext_0)->frameParameters_0[(7684U)>>2]);
    float _S2401 = as_type<float>((&kernelContext_0)->frameParameters_0[(7688U)>>2]);
    float _S2402 = as_type<float>((&kernelContext_0)->frameParameters_0[(7692U)>>2]);
    float4 _S2403 = float4(_S2399, _S2400, _S2401, _S2402);
    float _S2404 = as_type<float>((&kernelContext_0)->frameParameters_0[(7696U)>>2]);
    float _S2405 = as_type<float>((&kernelContext_0)->frameParameters_0[(7700U)>>2]);
    float _S2406 = as_type<float>((&kernelContext_0)->frameParameters_0[(7704U)>>2]);
    float _S2407 = as_type<float>((&kernelContext_0)->frameParameters_0[(7708U)>>2]);
    float4 _S2408 = float4(_S2404, _S2405, _S2406, _S2407);
    float _S2409 = as_type<float>((&kernelContext_0)->frameParameters_0[(7712U)>>2]);
    float _S2410 = as_type<float>((&kernelContext_0)->frameParameters_0[(7716U)>>2]);
    float _S2411 = as_type<float>((&kernelContext_0)->frameParameters_0[(7720U)>>2]);
    float _S2412 = as_type<float>((&kernelContext_0)->frameParameters_0[(7724U)>>2]);
    float4 _S2413 = float4(_S2409, _S2410, _S2411, _S2412);
    float _S2414 = as_type<float>((&kernelContext_0)->frameParameters_0[(7728U)>>2]);
    float _S2415 = as_type<float>((&kernelContext_0)->frameParameters_0[(7732U)>>2]);
    float _S2416 = as_type<float>((&kernelContext_0)->frameParameters_0[(7736U)>>2]);
    float _S2417 = as_type<float>((&kernelContext_0)->frameParameters_0[(7740U)>>2]);
    float4 _S2418 = float4(_S2414, _S2415, _S2416, _S2417);
    float _S2419 = as_type<float>((&kernelContext_0)->frameParameters_0[(7744U)>>2]);
    float _S2420 = as_type<float>((&kernelContext_0)->frameParameters_0[(7748U)>>2]);
    float _S2421 = as_type<float>((&kernelContext_0)->frameParameters_0[(7752U)>>2]);
    float _S2422 = as_type<float>((&kernelContext_0)->frameParameters_0[(7756U)>>2]);
    float4 _S2423 = float4(_S2419, _S2420, _S2421, _S2422);
    float _S2424 = as_type<float>((&kernelContext_0)->frameParameters_0[(7760U)>>2]);
    float _S2425 = as_type<float>((&kernelContext_0)->frameParameters_0[(7764U)>>2]);
    float _S2426 = as_type<float>((&kernelContext_0)->frameParameters_0[(7768U)>>2]);
    float _S2427 = as_type<float>((&kernelContext_0)->frameParameters_0[(7772U)>>2]);
    float4 _S2428 = float4(_S2424, _S2425, _S2426, _S2427);
    float _S2429 = as_type<float>((&kernelContext_0)->frameParameters_0[(7776U)>>2]);
    float _S2430 = as_type<float>((&kernelContext_0)->frameParameters_0[(7780U)>>2]);
    float _S2431 = as_type<float>((&kernelContext_0)->frameParameters_0[(7784U)>>2]);
    float _S2432 = as_type<float>((&kernelContext_0)->frameParameters_0[(7788U)>>2]);
    float4 _S2433 = float4(_S2429, _S2430, _S2431, _S2432);
    float _S2434 = as_type<float>((&kernelContext_0)->frameParameters_0[(7792U)>>2]);
    float _S2435 = as_type<float>((&kernelContext_0)->frameParameters_0[(7796U)>>2]);
    float _S2436 = as_type<float>((&kernelContext_0)->frameParameters_0[(7800U)>>2]);
    float _S2437 = as_type<float>((&kernelContext_0)->frameParameters_0[(7804U)>>2]);
    float4 _S2438 = float4(_S2434, _S2435, _S2436, _S2437);
    float _S2439 = as_type<float>((&kernelContext_0)->frameParameters_0[(7808U)>>2]);
    float _S2440 = as_type<float>((&kernelContext_0)->frameParameters_0[(7812U)>>2]);
    float _S2441 = as_type<float>((&kernelContext_0)->frameParameters_0[(7816U)>>2]);
    float _S2442 = as_type<float>((&kernelContext_0)->frameParameters_0[(7820U)>>2]);
    float4 _S2443 = float4(_S2439, _S2440, _S2441, _S2442);
    float _S2444 = as_type<float>((&kernelContext_0)->frameParameters_0[(7824U)>>2]);
    float _S2445 = as_type<float>((&kernelContext_0)->frameParameters_0[(7828U)>>2]);
    float _S2446 = as_type<float>((&kernelContext_0)->frameParameters_0[(7832U)>>2]);
    float _S2447 = as_type<float>((&kernelContext_0)->frameParameters_0[(7836U)>>2]);
    float4 _S2448 = float4(_S2444, _S2445, _S2446, _S2447);
    float _S2449 = as_type<float>((&kernelContext_0)->frameParameters_0[(7840U)>>2]);
    float _S2450 = as_type<float>((&kernelContext_0)->frameParameters_0[(7844U)>>2]);
    float _S2451 = as_type<float>((&kernelContext_0)->frameParameters_0[(7848U)>>2]);
    float _S2452 = as_type<float>((&kernelContext_0)->frameParameters_0[(7852U)>>2]);
    float4 _S2453 = float4(_S2449, _S2450, _S2451, _S2452);
    float _S2454 = as_type<float>((&kernelContext_0)->frameParameters_0[(7856U)>>2]);
    float _S2455 = as_type<float>((&kernelContext_0)->frameParameters_0[(7860U)>>2]);
    float _S2456 = as_type<float>((&kernelContext_0)->frameParameters_0[(7864U)>>2]);
    float _S2457 = as_type<float>((&kernelContext_0)->frameParameters_0[(7868U)>>2]);
    float4 _S2458 = float4(_S2454, _S2455, _S2456, _S2457);
    float _S2459 = as_type<float>((&kernelContext_0)->frameParameters_0[(7872U)>>2]);
    float _S2460 = as_type<float>((&kernelContext_0)->frameParameters_0[(7876U)>>2]);
    float _S2461 = as_type<float>((&kernelContext_0)->frameParameters_0[(7880U)>>2]);
    float _S2462 = as_type<float>((&kernelContext_0)->frameParameters_0[(7884U)>>2]);
    float4 _S2463 = float4(_S2459, _S2460, _S2461, _S2462);
    float _S2464 = as_type<float>((&kernelContext_0)->frameParameters_0[(7888U)>>2]);
    float _S2465 = as_type<float>((&kernelContext_0)->frameParameters_0[(7892U)>>2]);
    float _S2466 = as_type<float>((&kernelContext_0)->frameParameters_0[(7896U)>>2]);
    float _S2467 = as_type<float>((&kernelContext_0)->frameParameters_0[(7900U)>>2]);
    float4 _S2468 = float4(_S2464, _S2465, _S2466, _S2467);
    float _S2469 = as_type<float>((&kernelContext_0)->frameParameters_0[(7904U)>>2]);
    float _S2470 = as_type<float>((&kernelContext_0)->frameParameters_0[(7908U)>>2]);
    float _S2471 = as_type<float>((&kernelContext_0)->frameParameters_0[(7912U)>>2]);
    float _S2472 = as_type<float>((&kernelContext_0)->frameParameters_0[(7916U)>>2]);
    float4 _S2473 = float4(_S2469, _S2470, _S2471, _S2472);
    float _S2474 = as_type<float>((&kernelContext_0)->frameParameters_0[(7920U)>>2]);
    float _S2475 = as_type<float>((&kernelContext_0)->frameParameters_0[(7924U)>>2]);
    float _S2476 = as_type<float>((&kernelContext_0)->frameParameters_0[(7928U)>>2]);
    float _S2477 = as_type<float>((&kernelContext_0)->frameParameters_0[(7932U)>>2]);
    float4 _S2478 = float4(_S2474, _S2475, _S2476, _S2477);
    float _S2479 = as_type<float>((&kernelContext_0)->frameParameters_0[(7936U)>>2]);
    float _S2480 = as_type<float>((&kernelContext_0)->frameParameters_0[(7940U)>>2]);
    float _S2481 = as_type<float>((&kernelContext_0)->frameParameters_0[(7944U)>>2]);
    float _S2482 = as_type<float>((&kernelContext_0)->frameParameters_0[(7948U)>>2]);
    float4 _S2483 = float4(_S2479, _S2480, _S2481, _S2482);
    float _S2484 = as_type<float>((&kernelContext_0)->frameParameters_0[(7952U)>>2]);
    float _S2485 = as_type<float>((&kernelContext_0)->frameParameters_0[(7956U)>>2]);
    float _S2486 = as_type<float>((&kernelContext_0)->frameParameters_0[(7960U)>>2]);
    float _S2487 = as_type<float>((&kernelContext_0)->frameParameters_0[(7964U)>>2]);
    float4 _S2488 = float4(_S2484, _S2485, _S2486, _S2487);
    float _S2489 = as_type<float>((&kernelContext_0)->frameParameters_0[(7968U)>>2]);
    float _S2490 = as_type<float>((&kernelContext_0)->frameParameters_0[(7972U)>>2]);
    float _S2491 = as_type<float>((&kernelContext_0)->frameParameters_0[(7976U)>>2]);
    float _S2492 = as_type<float>((&kernelContext_0)->frameParameters_0[(7980U)>>2]);
    float4 _S2493 = float4(_S2489, _S2490, _S2491, _S2492);
    float _S2494 = as_type<float>((&kernelContext_0)->frameParameters_0[(7984U)>>2]);
    float _S2495 = as_type<float>((&kernelContext_0)->frameParameters_0[(7988U)>>2]);
    float _S2496 = as_type<float>((&kernelContext_0)->frameParameters_0[(7992U)>>2]);
    float _S2497 = as_type<float>((&kernelContext_0)->frameParameters_0[(7996U)>>2]);
    float4 _S2498 = float4(_S2494, _S2495, _S2496, _S2497);
    float _S2499 = as_type<float>((&kernelContext_0)->frameParameters_0[(8000U)>>2]);
    float _S2500 = as_type<float>((&kernelContext_0)->frameParameters_0[(8004U)>>2]);
    float _S2501 = as_type<float>((&kernelContext_0)->frameParameters_0[(8008U)>>2]);
    float _S2502 = as_type<float>((&kernelContext_0)->frameParameters_0[(8012U)>>2]);
    float4 _S2503 = float4(_S2499, _S2500, _S2501, _S2502);
    float _S2504 = as_type<float>((&kernelContext_0)->frameParameters_0[(8016U)>>2]);
    float _S2505 = as_type<float>((&kernelContext_0)->frameParameters_0[(8020U)>>2]);
    float _S2506 = as_type<float>((&kernelContext_0)->frameParameters_0[(8024U)>>2]);
    float _S2507 = as_type<float>((&kernelContext_0)->frameParameters_0[(8028U)>>2]);
    float4 _S2508 = float4(_S2504, _S2505, _S2506, _S2507);
    float _S2509 = as_type<float>((&kernelContext_0)->frameParameters_0[(8032U)>>2]);
    float _S2510 = as_type<float>((&kernelContext_0)->frameParameters_0[(8036U)>>2]);
    float _S2511 = as_type<float>((&kernelContext_0)->frameParameters_0[(8040U)>>2]);
    float _S2512 = as_type<float>((&kernelContext_0)->frameParameters_0[(8044U)>>2]);
    float4 _S2513 = float4(_S2509, _S2510, _S2511, _S2512);
    float _S2514 = as_type<float>((&kernelContext_0)->frameParameters_0[(8048U)>>2]);
    float _S2515 = as_type<float>((&kernelContext_0)->frameParameters_0[(8052U)>>2]);
    float _S2516 = as_type<float>((&kernelContext_0)->frameParameters_0[(8056U)>>2]);
    float _S2517 = as_type<float>((&kernelContext_0)->frameParameters_0[(8060U)>>2]);
    float4 _S2518 = float4(_S2514, _S2515, _S2516, _S2517);
    float _S2519 = as_type<float>((&kernelContext_0)->frameParameters_0[(8064U)>>2]);
    float _S2520 = as_type<float>((&kernelContext_0)->frameParameters_0[(8068U)>>2]);
    float _S2521 = as_type<float>((&kernelContext_0)->frameParameters_0[(8072U)>>2]);
    float _S2522 = as_type<float>((&kernelContext_0)->frameParameters_0[(8076U)>>2]);
    float4 _S2523 = float4(_S2519, _S2520, _S2521, _S2522);
    float _S2524 = as_type<float>((&kernelContext_0)->frameParameters_0[(8080U)>>2]);
    float _S2525 = as_type<float>((&kernelContext_0)->frameParameters_0[(8084U)>>2]);
    float _S2526 = as_type<float>((&kernelContext_0)->frameParameters_0[(8088U)>>2]);
    float _S2527 = as_type<float>((&kernelContext_0)->frameParameters_0[(8092U)>>2]);
    float4 _S2528 = float4(_S2524, _S2525, _S2526, _S2527);
    float _S2529 = as_type<float>((&kernelContext_0)->frameParameters_0[(8096U)>>2]);
    float _S2530 = as_type<float>((&kernelContext_0)->frameParameters_0[(8100U)>>2]);
    float _S2531 = as_type<float>((&kernelContext_0)->frameParameters_0[(8104U)>>2]);
    float _S2532 = as_type<float>((&kernelContext_0)->frameParameters_0[(8108U)>>2]);
    float4 _S2533 = float4(_S2529, _S2530, _S2531, _S2532);
    float _S2534 = as_type<float>((&kernelContext_0)->frameParameters_0[(8112U)>>2]);
    float _S2535 = as_type<float>((&kernelContext_0)->frameParameters_0[(8116U)>>2]);
    float _S2536 = as_type<float>((&kernelContext_0)->frameParameters_0[(8120U)>>2]);
    float _S2537 = as_type<float>((&kernelContext_0)->frameParameters_0[(8124U)>>2]);
    float4 _S2538 = float4(_S2534, _S2535, _S2536, _S2537);
    float _S2539 = as_type<float>((&kernelContext_0)->frameParameters_0[(8128U)>>2]);
    float _S2540 = as_type<float>((&kernelContext_0)->frameParameters_0[(8132U)>>2]);
    float _S2541 = as_type<float>((&kernelContext_0)->frameParameters_0[(8136U)>>2]);
    float _S2542 = as_type<float>((&kernelContext_0)->frameParameters_0[(8140U)>>2]);
    float4 _S2543 = float4(_S2539, _S2540, _S2541, _S2542);
    float _S2544 = as_type<float>((&kernelContext_0)->frameParameters_0[(8144U)>>2]);
    float _S2545 = as_type<float>((&kernelContext_0)->frameParameters_0[(8148U)>>2]);
    float _S2546 = as_type<float>((&kernelContext_0)->frameParameters_0[(8152U)>>2]);
    float _S2547 = as_type<float>((&kernelContext_0)->frameParameters_0[(8156U)>>2]);
    float4 _S2548 = float4(_S2544, _S2545, _S2546, _S2547);
    float _S2549 = as_type<float>((&kernelContext_0)->frameParameters_0[(8160U)>>2]);
    float _S2550 = as_type<float>((&kernelContext_0)->frameParameters_0[(8164U)>>2]);
    float _S2551 = as_type<float>((&kernelContext_0)->frameParameters_0[(8168U)>>2]);
    float _S2552 = as_type<float>((&kernelContext_0)->frameParameters_0[(8172U)>>2]);
    float4 _S2553 = float4(_S2549, _S2550, _S2551, _S2552);
    float _S2554 = as_type<float>((&kernelContext_0)->frameParameters_0[(8176U)>>2]);
    float _S2555 = as_type<float>((&kernelContext_0)->frameParameters_0[(8180U)>>2]);
    float _S2556 = as_type<float>((&kernelContext_0)->frameParameters_0[(8184U)>>2]);
    float _S2557 = as_type<float>((&kernelContext_0)->frameParameters_0[(8188U)>>2]);
    float4 _S2558 = float4(_S2554, _S2555, _S2556, _S2557);
    float _S2559 = as_type<float>((&kernelContext_0)->frameParameters_0[(8192U)>>2]);
    float _S2560 = as_type<float>((&kernelContext_0)->frameParameters_0[(8196U)>>2]);
    float _S2561 = as_type<float>((&kernelContext_0)->frameParameters_0[(8200U)>>2]);
    float _S2562 = as_type<float>((&kernelContext_0)->frameParameters_0[(8204U)>>2]);
    float4 _S2563 = float4(_S2559, _S2560, _S2561, _S2562);
    float _S2564 = as_type<float>((&kernelContext_0)->frameParameters_0[(8208U)>>2]);
    float _S2565 = as_type<float>((&kernelContext_0)->frameParameters_0[(8212U)>>2]);
    float _S2566 = as_type<float>((&kernelContext_0)->frameParameters_0[(8216U)>>2]);
    float _S2567 = as_type<float>((&kernelContext_0)->frameParameters_0[(8220U)>>2]);
    float4 _S2568 = float4(_S2564, _S2565, _S2566, _S2567);
    float _S2569 = as_type<float>((&kernelContext_0)->frameParameters_0[(8224U)>>2]);
    float _S2570 = as_type<float>((&kernelContext_0)->frameParameters_0[(8228U)>>2]);
    float _S2571 = as_type<float>((&kernelContext_0)->frameParameters_0[(8232U)>>2]);
    float _S2572 = as_type<float>((&kernelContext_0)->frameParameters_0[(8236U)>>2]);
    float4 _S2573 = float4(_S2569, _S2570, _S2571, _S2572);
    float _S2574 = as_type<float>((&kernelContext_0)->frameParameters_0[(8240U)>>2]);
    float _S2575 = as_type<float>((&kernelContext_0)->frameParameters_0[(8244U)>>2]);
    float _S2576 = as_type<float>((&kernelContext_0)->frameParameters_0[(8248U)>>2]);
    float _S2577 = as_type<float>((&kernelContext_0)->frameParameters_0[(8252U)>>2]);
    float4 _S2578 = float4(_S2574, _S2575, _S2576, _S2577);
    float _S2579 = as_type<float>((&kernelContext_0)->frameParameters_0[(8256U)>>2]);
    float _S2580 = as_type<float>((&kernelContext_0)->frameParameters_0[(8260U)>>2]);
    float _S2581 = as_type<float>((&kernelContext_0)->frameParameters_0[(8264U)>>2]);
    float _S2582 = as_type<float>((&kernelContext_0)->frameParameters_0[(8268U)>>2]);
    float4 _S2583 = float4(_S2579, _S2580, _S2581, _S2582);
    float _S2584 = as_type<float>((&kernelContext_0)->frameParameters_0[(8272U)>>2]);
    float _S2585 = as_type<float>((&kernelContext_0)->frameParameters_0[(8276U)>>2]);
    float _S2586 = as_type<float>((&kernelContext_0)->frameParameters_0[(8280U)>>2]);
    float _S2587 = as_type<float>((&kernelContext_0)->frameParameters_0[(8284U)>>2]);
    float4 _S2588 = float4(_S2584, _S2585, _S2586, _S2587);
    float _S2589 = as_type<float>((&kernelContext_0)->frameParameters_0[(8288U)>>2]);
    float _S2590 = as_type<float>((&kernelContext_0)->frameParameters_0[(8292U)>>2]);
    float _S2591 = as_type<float>((&kernelContext_0)->frameParameters_0[(8296U)>>2]);
    float _S2592 = as_type<float>((&kernelContext_0)->frameParameters_0[(8300U)>>2]);
    float4 _S2593 = float4(_S2589, _S2590, _S2591, _S2592);
    float _S2594 = as_type<float>((&kernelContext_0)->frameParameters_0[(8304U)>>2]);
    float _S2595 = as_type<float>((&kernelContext_0)->frameParameters_0[(8308U)>>2]);
    float _S2596 = as_type<float>((&kernelContext_0)->frameParameters_0[(8312U)>>2]);
    float _S2597 = as_type<float>((&kernelContext_0)->frameParameters_0[(8316U)>>2]);
    float4 _S2598 = float4(_S2594, _S2595, _S2596, _S2597);
    float _S2599 = as_type<float>((&kernelContext_0)->frameParameters_0[(8320U)>>2]);
    float _S2600 = as_type<float>((&kernelContext_0)->frameParameters_0[(8324U)>>2]);
    float _S2601 = as_type<float>((&kernelContext_0)->frameParameters_0[(8328U)>>2]);
    float _S2602 = as_type<float>((&kernelContext_0)->frameParameters_0[(8332U)>>2]);
    float4 _S2603 = float4(_S2599, _S2600, _S2601, _S2602);
    float _S2604 = as_type<float>((&kernelContext_0)->frameParameters_0[(8336U)>>2]);
    float _S2605 = as_type<float>((&kernelContext_0)->frameParameters_0[(8340U)>>2]);
    float _S2606 = as_type<float>((&kernelContext_0)->frameParameters_0[(8344U)>>2]);
    float _S2607 = as_type<float>((&kernelContext_0)->frameParameters_0[(8348U)>>2]);
    float4 _S2608 = float4(_S2604, _S2605, _S2606, _S2607);
    float _S2609 = as_type<float>((&kernelContext_0)->frameParameters_0[(8352U)>>2]);
    float _S2610 = as_type<float>((&kernelContext_0)->frameParameters_0[(8356U)>>2]);
    float _S2611 = as_type<float>((&kernelContext_0)->frameParameters_0[(8360U)>>2]);
    float _S2612 = as_type<float>((&kernelContext_0)->frameParameters_0[(8364U)>>2]);
    float4 _S2613 = float4(_S2609, _S2610, _S2611, _S2612);
    float _S2614 = as_type<float>((&kernelContext_0)->frameParameters_0[(8368U)>>2]);
    float _S2615 = as_type<float>((&kernelContext_0)->frameParameters_0[(8372U)>>2]);
    float _S2616 = as_type<float>((&kernelContext_0)->frameParameters_0[(8376U)>>2]);
    float _S2617 = as_type<float>((&kernelContext_0)->frameParameters_0[(8380U)>>2]);
    float4 _S2618 = float4(_S2614, _S2615, _S2616, _S2617);
    float _S2619 = as_type<float>((&kernelContext_0)->frameParameters_0[(8384U)>>2]);
    float _S2620 = as_type<float>((&kernelContext_0)->frameParameters_0[(8388U)>>2]);
    float _S2621 = as_type<float>((&kernelContext_0)->frameParameters_0[(8392U)>>2]);
    float _S2622 = as_type<float>((&kernelContext_0)->frameParameters_0[(8396U)>>2]);
    float4 _S2623 = float4(_S2619, _S2620, _S2621, _S2622);
    float _S2624 = as_type<float>((&kernelContext_0)->frameParameters_0[(8400U)>>2]);
    float _S2625 = as_type<float>((&kernelContext_0)->frameParameters_0[(8404U)>>2]);
    float _S2626 = as_type<float>((&kernelContext_0)->frameParameters_0[(8408U)>>2]);
    float _S2627 = as_type<float>((&kernelContext_0)->frameParameters_0[(8412U)>>2]);
    array<float4, int(128)> _S2628 = { _S1993, _S1998, _S2003, _S2008, _S2013, _S2018, _S2023, _S2028, _S2033, _S2038, _S2043, _S2048, _S2053, _S2058, _S2063, _S2068, _S2073, _S2078, _S2083, _S2088, _S2093, _S2098, _S2103, _S2108, _S2113, _S2118, _S2123, _S2128, _S2133, _S2138, _S2143, _S2148, _S2153, _S2158, _S2163, _S2168, _S2173, _S2178, _S2183, _S2188, _S2193, _S2198, _S2203, _S2208, _S2213, _S2218, _S2223, _S2228, _S2233, _S2238, _S2243, _S2248, _S2253, _S2258, _S2263, _S2268, _S2273, _S2278, _S2283, _S2288, _S2293, _S2298, _S2303, _S2308, _S2313, _S2318, _S2323, _S2328, _S2333, _S2338, _S2343, _S2348, _S2353, _S2358, _S2363, _S2368, _S2373, _S2378, _S2383, _S2388, _S2393, _S2398, _S2403, _S2408, _S2413, _S2418, _S2423, _S2428, _S2433, _S2438, _S2443, _S2448, _S2453, _S2458, _S2463, _S2468, _S2473, _S2478, _S2483, _S2488, _S2493, _S2498, _S2503, _S2508, _S2513, _S2518, _S2523, _S2528, _S2533, _S2538, _S2543, _S2548, _S2553, _S2558, _S2563, _S2568, _S2573, _S2578, _S2583, _S2588, _S2593, _S2598, _S2603, _S2608, _S2613, _S2618, _S2623, float4(_S2624, _S2625, _S2626, _S2627) };
    float _S2629 = as_type<float>((&kernelContext_0)->frameParameters_0[(8416U)>>2]);
    float _S2630 = as_type<float>((&kernelContext_0)->frameParameters_0[(8420U)>>2]);
    float _S2631 = as_type<float>((&kernelContext_0)->frameParameters_0[(8424U)>>2]);
    float _S2632 = as_type<float>((&kernelContext_0)->frameParameters_0[(8428U)>>2]);
    float4 _S2633 = float4(_S2629, _S2630, _S2631, _S2632);
    float _S2634 = as_type<float>((&kernelContext_0)->frameParameters_0[(8432U)>>2]);
    float _S2635 = as_type<float>((&kernelContext_0)->frameParameters_0[(8436U)>>2]);
    float _S2636 = as_type<float>((&kernelContext_0)->frameParameters_0[(8440U)>>2]);
    float _S2637 = as_type<float>((&kernelContext_0)->frameParameters_0[(8444U)>>2]);
    float4 _S2638 = float4(_S2634, _S2635, _S2636, _S2637);
    float _S2639 = as_type<float>((&kernelContext_0)->frameParameters_0[(8448U)>>2]);
    float _S2640 = as_type<float>((&kernelContext_0)->frameParameters_0[(8452U)>>2]);
    float _S2641 = as_type<float>((&kernelContext_0)->frameParameters_0[(8456U)>>2]);
    float _S2642 = as_type<float>((&kernelContext_0)->frameParameters_0[(8460U)>>2]);
    float4 _S2643 = float4(_S2639, _S2640, _S2641, _S2642);
    float _S2644 = as_type<float>((&kernelContext_0)->frameParameters_0[(8464U)>>2]);
    float _S2645 = as_type<float>((&kernelContext_0)->frameParameters_0[(8468U)>>2]);
    float _S2646 = as_type<float>((&kernelContext_0)->frameParameters_0[(8472U)>>2]);
    float _S2647 = as_type<float>((&kernelContext_0)->frameParameters_0[(8476U)>>2]);
    float4 _S2648 = float4(_S2644, _S2645, _S2646, _S2647);
    float _S2649 = as_type<float>((&kernelContext_0)->frameParameters_0[(8480U)>>2]);
    float _S2650 = as_type<float>((&kernelContext_0)->frameParameters_0[(8484U)>>2]);
    float _S2651 = as_type<float>((&kernelContext_0)->frameParameters_0[(8488U)>>2]);
    float _S2652 = as_type<float>((&kernelContext_0)->frameParameters_0[(8492U)>>2]);
    float4 _S2653 = float4(_S2649, _S2650, _S2651, _S2652);
    float _S2654 = as_type<float>((&kernelContext_0)->frameParameters_0[(8496U)>>2]);
    float _S2655 = as_type<float>((&kernelContext_0)->frameParameters_0[(8500U)>>2]);
    float _S2656 = as_type<float>((&kernelContext_0)->frameParameters_0[(8504U)>>2]);
    float _S2657 = as_type<float>((&kernelContext_0)->frameParameters_0[(8508U)>>2]);
    float4 _S2658 = float4(_S2654, _S2655, _S2656, _S2657);
    float _S2659 = as_type<float>((&kernelContext_0)->frameParameters_0[(8512U)>>2]);
    float _S2660 = as_type<float>((&kernelContext_0)->frameParameters_0[(8516U)>>2]);
    float _S2661 = as_type<float>((&kernelContext_0)->frameParameters_0[(8520U)>>2]);
    float _S2662 = as_type<float>((&kernelContext_0)->frameParameters_0[(8524U)>>2]);
    float4 _S2663 = float4(_S2659, _S2660, _S2661, _S2662);
    float _S2664 = as_type<float>((&kernelContext_0)->frameParameters_0[(8528U)>>2]);
    float _S2665 = as_type<float>((&kernelContext_0)->frameParameters_0[(8532U)>>2]);
    float _S2666 = as_type<float>((&kernelContext_0)->frameParameters_0[(8536U)>>2]);
    float _S2667 = as_type<float>((&kernelContext_0)->frameParameters_0[(8540U)>>2]);
    float4 _S2668 = float4(_S2664, _S2665, _S2666, _S2667);
    float _S2669 = as_type<float>((&kernelContext_0)->frameParameters_0[(8544U)>>2]);
    float _S2670 = as_type<float>((&kernelContext_0)->frameParameters_0[(8548U)>>2]);
    float _S2671 = as_type<float>((&kernelContext_0)->frameParameters_0[(8552U)>>2]);
    float _S2672 = as_type<float>((&kernelContext_0)->frameParameters_0[(8556U)>>2]);
    float4 _S2673 = float4(_S2669, _S2670, _S2671, _S2672);
    float _S2674 = as_type<float>((&kernelContext_0)->frameParameters_0[(8560U)>>2]);
    float _S2675 = as_type<float>((&kernelContext_0)->frameParameters_0[(8564U)>>2]);
    float _S2676 = as_type<float>((&kernelContext_0)->frameParameters_0[(8568U)>>2]);
    float _S2677 = as_type<float>((&kernelContext_0)->frameParameters_0[(8572U)>>2]);
    float4 _S2678 = float4(_S2674, _S2675, _S2676, _S2677);
    float _S2679 = as_type<float>((&kernelContext_0)->frameParameters_0[(8576U)>>2]);
    float _S2680 = as_type<float>((&kernelContext_0)->frameParameters_0[(8580U)>>2]);
    float _S2681 = as_type<float>((&kernelContext_0)->frameParameters_0[(8584U)>>2]);
    float _S2682 = as_type<float>((&kernelContext_0)->frameParameters_0[(8588U)>>2]);
    float4 _S2683 = float4(_S2679, _S2680, _S2681, _S2682);
    float _S2684 = as_type<float>((&kernelContext_0)->frameParameters_0[(8592U)>>2]);
    float _S2685 = as_type<float>((&kernelContext_0)->frameParameters_0[(8596U)>>2]);
    float _S2686 = as_type<float>((&kernelContext_0)->frameParameters_0[(8600U)>>2]);
    float _S2687 = as_type<float>((&kernelContext_0)->frameParameters_0[(8604U)>>2]);
    float4 _S2688 = float4(_S2684, _S2685, _S2686, _S2687);
    float _S2689 = as_type<float>((&kernelContext_0)->frameParameters_0[(8608U)>>2]);
    float _S2690 = as_type<float>((&kernelContext_0)->frameParameters_0[(8612U)>>2]);
    float _S2691 = as_type<float>((&kernelContext_0)->frameParameters_0[(8616U)>>2]);
    float _S2692 = as_type<float>((&kernelContext_0)->frameParameters_0[(8620U)>>2]);
    float4 _S2693 = float4(_S2689, _S2690, _S2691, _S2692);
    float _S2694 = as_type<float>((&kernelContext_0)->frameParameters_0[(8624U)>>2]);
    float _S2695 = as_type<float>((&kernelContext_0)->frameParameters_0[(8628U)>>2]);
    float _S2696 = as_type<float>((&kernelContext_0)->frameParameters_0[(8632U)>>2]);
    float _S2697 = as_type<float>((&kernelContext_0)->frameParameters_0[(8636U)>>2]);
    float4 _S2698 = float4(_S2694, _S2695, _S2696, _S2697);
    float _S2699 = as_type<float>((&kernelContext_0)->frameParameters_0[(8640U)>>2]);
    float _S2700 = as_type<float>((&kernelContext_0)->frameParameters_0[(8644U)>>2]);
    float _S2701 = as_type<float>((&kernelContext_0)->frameParameters_0[(8648U)>>2]);
    float _S2702 = as_type<float>((&kernelContext_0)->frameParameters_0[(8652U)>>2]);
    float4 _S2703 = float4(_S2699, _S2700, _S2701, _S2702);
    float _S2704 = as_type<float>((&kernelContext_0)->frameParameters_0[(8656U)>>2]);
    float _S2705 = as_type<float>((&kernelContext_0)->frameParameters_0[(8660U)>>2]);
    float _S2706 = as_type<float>((&kernelContext_0)->frameParameters_0[(8664U)>>2]);
    float _S2707 = as_type<float>((&kernelContext_0)->frameParameters_0[(8668U)>>2]);
    float4 _S2708 = float4(_S2704, _S2705, _S2706, _S2707);
    float _S2709 = as_type<float>((&kernelContext_0)->frameParameters_0[(8672U)>>2]);
    float _S2710 = as_type<float>((&kernelContext_0)->frameParameters_0[(8676U)>>2]);
    float _S2711 = as_type<float>((&kernelContext_0)->frameParameters_0[(8680U)>>2]);
    float _S2712 = as_type<float>((&kernelContext_0)->frameParameters_0[(8684U)>>2]);
    float4 _S2713 = float4(_S2709, _S2710, _S2711, _S2712);
    float _S2714 = as_type<float>((&kernelContext_0)->frameParameters_0[(8688U)>>2]);
    float _S2715 = as_type<float>((&kernelContext_0)->frameParameters_0[(8692U)>>2]);
    float _S2716 = as_type<float>((&kernelContext_0)->frameParameters_0[(8696U)>>2]);
    float _S2717 = as_type<float>((&kernelContext_0)->frameParameters_0[(8700U)>>2]);
    float4 _S2718 = float4(_S2714, _S2715, _S2716, _S2717);
    float _S2719 = as_type<float>((&kernelContext_0)->frameParameters_0[(8704U)>>2]);
    float _S2720 = as_type<float>((&kernelContext_0)->frameParameters_0[(8708U)>>2]);
    float _S2721 = as_type<float>((&kernelContext_0)->frameParameters_0[(8712U)>>2]);
    float _S2722 = as_type<float>((&kernelContext_0)->frameParameters_0[(8716U)>>2]);
    float4 _S2723 = float4(_S2719, _S2720, _S2721, _S2722);
    float _S2724 = as_type<float>((&kernelContext_0)->frameParameters_0[(8720U)>>2]);
    float _S2725 = as_type<float>((&kernelContext_0)->frameParameters_0[(8724U)>>2]);
    float _S2726 = as_type<float>((&kernelContext_0)->frameParameters_0[(8728U)>>2]);
    float _S2727 = as_type<float>((&kernelContext_0)->frameParameters_0[(8732U)>>2]);
    float4 _S2728 = float4(_S2724, _S2725, _S2726, _S2727);
    float _S2729 = as_type<float>((&kernelContext_0)->frameParameters_0[(8736U)>>2]);
    float _S2730 = as_type<float>((&kernelContext_0)->frameParameters_0[(8740U)>>2]);
    float _S2731 = as_type<float>((&kernelContext_0)->frameParameters_0[(8744U)>>2]);
    float _S2732 = as_type<float>((&kernelContext_0)->frameParameters_0[(8748U)>>2]);
    float4 _S2733 = float4(_S2729, _S2730, _S2731, _S2732);
    float _S2734 = as_type<float>((&kernelContext_0)->frameParameters_0[(8752U)>>2]);
    float _S2735 = as_type<float>((&kernelContext_0)->frameParameters_0[(8756U)>>2]);
    float _S2736 = as_type<float>((&kernelContext_0)->frameParameters_0[(8760U)>>2]);
    float _S2737 = as_type<float>((&kernelContext_0)->frameParameters_0[(8764U)>>2]);
    float4 _S2738 = float4(_S2734, _S2735, _S2736, _S2737);
    float _S2739 = as_type<float>((&kernelContext_0)->frameParameters_0[(8768U)>>2]);
    float _S2740 = as_type<float>((&kernelContext_0)->frameParameters_0[(8772U)>>2]);
    float _S2741 = as_type<float>((&kernelContext_0)->frameParameters_0[(8776U)>>2]);
    float _S2742 = as_type<float>((&kernelContext_0)->frameParameters_0[(8780U)>>2]);
    float4 _S2743 = float4(_S2739, _S2740, _S2741, _S2742);
    float _S2744 = as_type<float>((&kernelContext_0)->frameParameters_0[(8784U)>>2]);
    float _S2745 = as_type<float>((&kernelContext_0)->frameParameters_0[(8788U)>>2]);
    float _S2746 = as_type<float>((&kernelContext_0)->frameParameters_0[(8792U)>>2]);
    float _S2747 = as_type<float>((&kernelContext_0)->frameParameters_0[(8796U)>>2]);
    float4 _S2748 = float4(_S2744, _S2745, _S2746, _S2747);
    float _S2749 = as_type<float>((&kernelContext_0)->frameParameters_0[(8800U)>>2]);
    float _S2750 = as_type<float>((&kernelContext_0)->frameParameters_0[(8804U)>>2]);
    float _S2751 = as_type<float>((&kernelContext_0)->frameParameters_0[(8808U)>>2]);
    float _S2752 = as_type<float>((&kernelContext_0)->frameParameters_0[(8812U)>>2]);
    float4 _S2753 = float4(_S2749, _S2750, _S2751, _S2752);
    float _S2754 = as_type<float>((&kernelContext_0)->frameParameters_0[(8816U)>>2]);
    float _S2755 = as_type<float>((&kernelContext_0)->frameParameters_0[(8820U)>>2]);
    float _S2756 = as_type<float>((&kernelContext_0)->frameParameters_0[(8824U)>>2]);
    float _S2757 = as_type<float>((&kernelContext_0)->frameParameters_0[(8828U)>>2]);
    float4 _S2758 = float4(_S2754, _S2755, _S2756, _S2757);
    float _S2759 = as_type<float>((&kernelContext_0)->frameParameters_0[(8832U)>>2]);
    float _S2760 = as_type<float>((&kernelContext_0)->frameParameters_0[(8836U)>>2]);
    float _S2761 = as_type<float>((&kernelContext_0)->frameParameters_0[(8840U)>>2]);
    float _S2762 = as_type<float>((&kernelContext_0)->frameParameters_0[(8844U)>>2]);
    float4 _S2763 = float4(_S2759, _S2760, _S2761, _S2762);
    float _S2764 = as_type<float>((&kernelContext_0)->frameParameters_0[(8848U)>>2]);
    float _S2765 = as_type<float>((&kernelContext_0)->frameParameters_0[(8852U)>>2]);
    float _S2766 = as_type<float>((&kernelContext_0)->frameParameters_0[(8856U)>>2]);
    float _S2767 = as_type<float>((&kernelContext_0)->frameParameters_0[(8860U)>>2]);
    float4 _S2768 = float4(_S2764, _S2765, _S2766, _S2767);
    float _S2769 = as_type<float>((&kernelContext_0)->frameParameters_0[(8864U)>>2]);
    float _S2770 = as_type<float>((&kernelContext_0)->frameParameters_0[(8868U)>>2]);
    float _S2771 = as_type<float>((&kernelContext_0)->frameParameters_0[(8872U)>>2]);
    float _S2772 = as_type<float>((&kernelContext_0)->frameParameters_0[(8876U)>>2]);
    float4 _S2773 = float4(_S2769, _S2770, _S2771, _S2772);
    float _S2774 = as_type<float>((&kernelContext_0)->frameParameters_0[(8880U)>>2]);
    float _S2775 = as_type<float>((&kernelContext_0)->frameParameters_0[(8884U)>>2]);
    float _S2776 = as_type<float>((&kernelContext_0)->frameParameters_0[(8888U)>>2]);
    float _S2777 = as_type<float>((&kernelContext_0)->frameParameters_0[(8892U)>>2]);
    float4 _S2778 = float4(_S2774, _S2775, _S2776, _S2777);
    float _S2779 = as_type<float>((&kernelContext_0)->frameParameters_0[(8896U)>>2]);
    float _S2780 = as_type<float>((&kernelContext_0)->frameParameters_0[(8900U)>>2]);
    float _S2781 = as_type<float>((&kernelContext_0)->frameParameters_0[(8904U)>>2]);
    float _S2782 = as_type<float>((&kernelContext_0)->frameParameters_0[(8908U)>>2]);
    float4 _S2783 = float4(_S2779, _S2780, _S2781, _S2782);
    float _S2784 = as_type<float>((&kernelContext_0)->frameParameters_0[(8912U)>>2]);
    float _S2785 = as_type<float>((&kernelContext_0)->frameParameters_0[(8916U)>>2]);
    float _S2786 = as_type<float>((&kernelContext_0)->frameParameters_0[(8920U)>>2]);
    float _S2787 = as_type<float>((&kernelContext_0)->frameParameters_0[(8924U)>>2]);
    float4 _S2788 = float4(_S2784, _S2785, _S2786, _S2787);
    float _S2789 = as_type<float>((&kernelContext_0)->frameParameters_0[(8928U)>>2]);
    float _S2790 = as_type<float>((&kernelContext_0)->frameParameters_0[(8932U)>>2]);
    float _S2791 = as_type<float>((&kernelContext_0)->frameParameters_0[(8936U)>>2]);
    float _S2792 = as_type<float>((&kernelContext_0)->frameParameters_0[(8940U)>>2]);
    float4 _S2793 = float4(_S2789, _S2790, _S2791, _S2792);
    float _S2794 = as_type<float>((&kernelContext_0)->frameParameters_0[(8944U)>>2]);
    float _S2795 = as_type<float>((&kernelContext_0)->frameParameters_0[(8948U)>>2]);
    float _S2796 = as_type<float>((&kernelContext_0)->frameParameters_0[(8952U)>>2]);
    float _S2797 = as_type<float>((&kernelContext_0)->frameParameters_0[(8956U)>>2]);
    float4 _S2798 = float4(_S2794, _S2795, _S2796, _S2797);
    float _S2799 = as_type<float>((&kernelContext_0)->frameParameters_0[(8960U)>>2]);
    float _S2800 = as_type<float>((&kernelContext_0)->frameParameters_0[(8964U)>>2]);
    float _S2801 = as_type<float>((&kernelContext_0)->frameParameters_0[(8968U)>>2]);
    float _S2802 = as_type<float>((&kernelContext_0)->frameParameters_0[(8972U)>>2]);
    float4 _S2803 = float4(_S2799, _S2800, _S2801, _S2802);
    float _S2804 = as_type<float>((&kernelContext_0)->frameParameters_0[(8976U)>>2]);
    float _S2805 = as_type<float>((&kernelContext_0)->frameParameters_0[(8980U)>>2]);
    float _S2806 = as_type<float>((&kernelContext_0)->frameParameters_0[(8984U)>>2]);
    float _S2807 = as_type<float>((&kernelContext_0)->frameParameters_0[(8988U)>>2]);
    float4 _S2808 = float4(_S2804, _S2805, _S2806, _S2807);
    float _S2809 = as_type<float>((&kernelContext_0)->frameParameters_0[(8992U)>>2]);
    float _S2810 = as_type<float>((&kernelContext_0)->frameParameters_0[(8996U)>>2]);
    float _S2811 = as_type<float>((&kernelContext_0)->frameParameters_0[(9000U)>>2]);
    float _S2812 = as_type<float>((&kernelContext_0)->frameParameters_0[(9004U)>>2]);
    float4 _S2813 = float4(_S2809, _S2810, _S2811, _S2812);
    float _S2814 = as_type<float>((&kernelContext_0)->frameParameters_0[(9008U)>>2]);
    float _S2815 = as_type<float>((&kernelContext_0)->frameParameters_0[(9012U)>>2]);
    float _S2816 = as_type<float>((&kernelContext_0)->frameParameters_0[(9016U)>>2]);
    float _S2817 = as_type<float>((&kernelContext_0)->frameParameters_0[(9020U)>>2]);
    float4 _S2818 = float4(_S2814, _S2815, _S2816, _S2817);
    float _S2819 = as_type<float>((&kernelContext_0)->frameParameters_0[(9024U)>>2]);
    float _S2820 = as_type<float>((&kernelContext_0)->frameParameters_0[(9028U)>>2]);
    float _S2821 = as_type<float>((&kernelContext_0)->frameParameters_0[(9032U)>>2]);
    float _S2822 = as_type<float>((&kernelContext_0)->frameParameters_0[(9036U)>>2]);
    float4 _S2823 = float4(_S2819, _S2820, _S2821, _S2822);
    float _S2824 = as_type<float>((&kernelContext_0)->frameParameters_0[(9040U)>>2]);
    float _S2825 = as_type<float>((&kernelContext_0)->frameParameters_0[(9044U)>>2]);
    float _S2826 = as_type<float>((&kernelContext_0)->frameParameters_0[(9048U)>>2]);
    float _S2827 = as_type<float>((&kernelContext_0)->frameParameters_0[(9052U)>>2]);
    float4 _S2828 = float4(_S2824, _S2825, _S2826, _S2827);
    float _S2829 = as_type<float>((&kernelContext_0)->frameParameters_0[(9056U)>>2]);
    float _S2830 = as_type<float>((&kernelContext_0)->frameParameters_0[(9060U)>>2]);
    float _S2831 = as_type<float>((&kernelContext_0)->frameParameters_0[(9064U)>>2]);
    float _S2832 = as_type<float>((&kernelContext_0)->frameParameters_0[(9068U)>>2]);
    float4 _S2833 = float4(_S2829, _S2830, _S2831, _S2832);
    float _S2834 = as_type<float>((&kernelContext_0)->frameParameters_0[(9072U)>>2]);
    float _S2835 = as_type<float>((&kernelContext_0)->frameParameters_0[(9076U)>>2]);
    float _S2836 = as_type<float>((&kernelContext_0)->frameParameters_0[(9080U)>>2]);
    float _S2837 = as_type<float>((&kernelContext_0)->frameParameters_0[(9084U)>>2]);
    float4 _S2838 = float4(_S2834, _S2835, _S2836, _S2837);
    float _S2839 = as_type<float>((&kernelContext_0)->frameParameters_0[(9088U)>>2]);
    float _S2840 = as_type<float>((&kernelContext_0)->frameParameters_0[(9092U)>>2]);
    float _S2841 = as_type<float>((&kernelContext_0)->frameParameters_0[(9096U)>>2]);
    float _S2842 = as_type<float>((&kernelContext_0)->frameParameters_0[(9100U)>>2]);
    float4 _S2843 = float4(_S2839, _S2840, _S2841, _S2842);
    float _S2844 = as_type<float>((&kernelContext_0)->frameParameters_0[(9104U)>>2]);
    float _S2845 = as_type<float>((&kernelContext_0)->frameParameters_0[(9108U)>>2]);
    float _S2846 = as_type<float>((&kernelContext_0)->frameParameters_0[(9112U)>>2]);
    float _S2847 = as_type<float>((&kernelContext_0)->frameParameters_0[(9116U)>>2]);
    float4 _S2848 = float4(_S2844, _S2845, _S2846, _S2847);
    float _S2849 = as_type<float>((&kernelContext_0)->frameParameters_0[(9120U)>>2]);
    float _S2850 = as_type<float>((&kernelContext_0)->frameParameters_0[(9124U)>>2]);
    float _S2851 = as_type<float>((&kernelContext_0)->frameParameters_0[(9128U)>>2]);
    float _S2852 = as_type<float>((&kernelContext_0)->frameParameters_0[(9132U)>>2]);
    float4 _S2853 = float4(_S2849, _S2850, _S2851, _S2852);
    float _S2854 = as_type<float>((&kernelContext_0)->frameParameters_0[(9136U)>>2]);
    float _S2855 = as_type<float>((&kernelContext_0)->frameParameters_0[(9140U)>>2]);
    float _S2856 = as_type<float>((&kernelContext_0)->frameParameters_0[(9144U)>>2]);
    float _S2857 = as_type<float>((&kernelContext_0)->frameParameters_0[(9148U)>>2]);
    float4 _S2858 = float4(_S2854, _S2855, _S2856, _S2857);
    float _S2859 = as_type<float>((&kernelContext_0)->frameParameters_0[(9152U)>>2]);
    float _S2860 = as_type<float>((&kernelContext_0)->frameParameters_0[(9156U)>>2]);
    float _S2861 = as_type<float>((&kernelContext_0)->frameParameters_0[(9160U)>>2]);
    float _S2862 = as_type<float>((&kernelContext_0)->frameParameters_0[(9164U)>>2]);
    float4 _S2863 = float4(_S2859, _S2860, _S2861, _S2862);
    float _S2864 = as_type<float>((&kernelContext_0)->frameParameters_0[(9168U)>>2]);
    float _S2865 = as_type<float>((&kernelContext_0)->frameParameters_0[(9172U)>>2]);
    float _S2866 = as_type<float>((&kernelContext_0)->frameParameters_0[(9176U)>>2]);
    float _S2867 = as_type<float>((&kernelContext_0)->frameParameters_0[(9180U)>>2]);
    float4 _S2868 = float4(_S2864, _S2865, _S2866, _S2867);
    float _S2869 = as_type<float>((&kernelContext_0)->frameParameters_0[(9184U)>>2]);
    float _S2870 = as_type<float>((&kernelContext_0)->frameParameters_0[(9188U)>>2]);
    float _S2871 = as_type<float>((&kernelContext_0)->frameParameters_0[(9192U)>>2]);
    float _S2872 = as_type<float>((&kernelContext_0)->frameParameters_0[(9196U)>>2]);
    float4 _S2873 = float4(_S2869, _S2870, _S2871, _S2872);
    float _S2874 = as_type<float>((&kernelContext_0)->frameParameters_0[(9200U)>>2]);
    float _S2875 = as_type<float>((&kernelContext_0)->frameParameters_0[(9204U)>>2]);
    float _S2876 = as_type<float>((&kernelContext_0)->frameParameters_0[(9208U)>>2]);
    float _S2877 = as_type<float>((&kernelContext_0)->frameParameters_0[(9212U)>>2]);
    float4 _S2878 = float4(_S2874, _S2875, _S2876, _S2877);
    float _S2879 = as_type<float>((&kernelContext_0)->frameParameters_0[(9216U)>>2]);
    float _S2880 = as_type<float>((&kernelContext_0)->frameParameters_0[(9220U)>>2]);
    float _S2881 = as_type<float>((&kernelContext_0)->frameParameters_0[(9224U)>>2]);
    float _S2882 = as_type<float>((&kernelContext_0)->frameParameters_0[(9228U)>>2]);
    float4 _S2883 = float4(_S2879, _S2880, _S2881, _S2882);
    float _S2884 = as_type<float>((&kernelContext_0)->frameParameters_0[(9232U)>>2]);
    float _S2885 = as_type<float>((&kernelContext_0)->frameParameters_0[(9236U)>>2]);
    float _S2886 = as_type<float>((&kernelContext_0)->frameParameters_0[(9240U)>>2]);
    float _S2887 = as_type<float>((&kernelContext_0)->frameParameters_0[(9244U)>>2]);
    float4 _S2888 = float4(_S2884, _S2885, _S2886, _S2887);
    float _S2889 = as_type<float>((&kernelContext_0)->frameParameters_0[(9248U)>>2]);
    float _S2890 = as_type<float>((&kernelContext_0)->frameParameters_0[(9252U)>>2]);
    float _S2891 = as_type<float>((&kernelContext_0)->frameParameters_0[(9256U)>>2]);
    float _S2892 = as_type<float>((&kernelContext_0)->frameParameters_0[(9260U)>>2]);
    float4 _S2893 = float4(_S2889, _S2890, _S2891, _S2892);
    float _S2894 = as_type<float>((&kernelContext_0)->frameParameters_0[(9264U)>>2]);
    float _S2895 = as_type<float>((&kernelContext_0)->frameParameters_0[(9268U)>>2]);
    float _S2896 = as_type<float>((&kernelContext_0)->frameParameters_0[(9272U)>>2]);
    float _S2897 = as_type<float>((&kernelContext_0)->frameParameters_0[(9276U)>>2]);
    float4 _S2898 = float4(_S2894, _S2895, _S2896, _S2897);
    float _S2899 = as_type<float>((&kernelContext_0)->frameParameters_0[(9280U)>>2]);
    float _S2900 = as_type<float>((&kernelContext_0)->frameParameters_0[(9284U)>>2]);
    float _S2901 = as_type<float>((&kernelContext_0)->frameParameters_0[(9288U)>>2]);
    float _S2902 = as_type<float>((&kernelContext_0)->frameParameters_0[(9292U)>>2]);
    float4 _S2903 = float4(_S2899, _S2900, _S2901, _S2902);
    float _S2904 = as_type<float>((&kernelContext_0)->frameParameters_0[(9296U)>>2]);
    float _S2905 = as_type<float>((&kernelContext_0)->frameParameters_0[(9300U)>>2]);
    float _S2906 = as_type<float>((&kernelContext_0)->frameParameters_0[(9304U)>>2]);
    float _S2907 = as_type<float>((&kernelContext_0)->frameParameters_0[(9308U)>>2]);
    float4 _S2908 = float4(_S2904, _S2905, _S2906, _S2907);
    float _S2909 = as_type<float>((&kernelContext_0)->frameParameters_0[(9312U)>>2]);
    float _S2910 = as_type<float>((&kernelContext_0)->frameParameters_0[(9316U)>>2]);
    float _S2911 = as_type<float>((&kernelContext_0)->frameParameters_0[(9320U)>>2]);
    float _S2912 = as_type<float>((&kernelContext_0)->frameParameters_0[(9324U)>>2]);
    float4 _S2913 = float4(_S2909, _S2910, _S2911, _S2912);
    float _S2914 = as_type<float>((&kernelContext_0)->frameParameters_0[(9328U)>>2]);
    float _S2915 = as_type<float>((&kernelContext_0)->frameParameters_0[(9332U)>>2]);
    float _S2916 = as_type<float>((&kernelContext_0)->frameParameters_0[(9336U)>>2]);
    float _S2917 = as_type<float>((&kernelContext_0)->frameParameters_0[(9340U)>>2]);
    float4 _S2918 = float4(_S2914, _S2915, _S2916, _S2917);
    float _S2919 = as_type<float>((&kernelContext_0)->frameParameters_0[(9344U)>>2]);
    float _S2920 = as_type<float>((&kernelContext_0)->frameParameters_0[(9348U)>>2]);
    float _S2921 = as_type<float>((&kernelContext_0)->frameParameters_0[(9352U)>>2]);
    float _S2922 = as_type<float>((&kernelContext_0)->frameParameters_0[(9356U)>>2]);
    float4 _S2923 = float4(_S2919, _S2920, _S2921, _S2922);
    float _S2924 = as_type<float>((&kernelContext_0)->frameParameters_0[(9360U)>>2]);
    float _S2925 = as_type<float>((&kernelContext_0)->frameParameters_0[(9364U)>>2]);
    float _S2926 = as_type<float>((&kernelContext_0)->frameParameters_0[(9368U)>>2]);
    float _S2927 = as_type<float>((&kernelContext_0)->frameParameters_0[(9372U)>>2]);
    float4 _S2928 = float4(_S2924, _S2925, _S2926, _S2927);
    float _S2929 = as_type<float>((&kernelContext_0)->frameParameters_0[(9376U)>>2]);
    float _S2930 = as_type<float>((&kernelContext_0)->frameParameters_0[(9380U)>>2]);
    float _S2931 = as_type<float>((&kernelContext_0)->frameParameters_0[(9384U)>>2]);
    float _S2932 = as_type<float>((&kernelContext_0)->frameParameters_0[(9388U)>>2]);
    float4 _S2933 = float4(_S2929, _S2930, _S2931, _S2932);
    float _S2934 = as_type<float>((&kernelContext_0)->frameParameters_0[(9392U)>>2]);
    float _S2935 = as_type<float>((&kernelContext_0)->frameParameters_0[(9396U)>>2]);
    float _S2936 = as_type<float>((&kernelContext_0)->frameParameters_0[(9400U)>>2]);
    float _S2937 = as_type<float>((&kernelContext_0)->frameParameters_0[(9404U)>>2]);
    float4 _S2938 = float4(_S2934, _S2935, _S2936, _S2937);
    float _S2939 = as_type<float>((&kernelContext_0)->frameParameters_0[(9408U)>>2]);
    float _S2940 = as_type<float>((&kernelContext_0)->frameParameters_0[(9412U)>>2]);
    float _S2941 = as_type<float>((&kernelContext_0)->frameParameters_0[(9416U)>>2]);
    float _S2942 = as_type<float>((&kernelContext_0)->frameParameters_0[(9420U)>>2]);
    float4 _S2943 = float4(_S2939, _S2940, _S2941, _S2942);
    float _S2944 = as_type<float>((&kernelContext_0)->frameParameters_0[(9424U)>>2]);
    float _S2945 = as_type<float>((&kernelContext_0)->frameParameters_0[(9428U)>>2]);
    float _S2946 = as_type<float>((&kernelContext_0)->frameParameters_0[(9432U)>>2]);
    float _S2947 = as_type<float>((&kernelContext_0)->frameParameters_0[(9436U)>>2]);
    float4 _S2948 = float4(_S2944, _S2945, _S2946, _S2947);
    float _S2949 = as_type<float>((&kernelContext_0)->frameParameters_0[(9440U)>>2]);
    float _S2950 = as_type<float>((&kernelContext_0)->frameParameters_0[(9444U)>>2]);
    float _S2951 = as_type<float>((&kernelContext_0)->frameParameters_0[(9448U)>>2]);
    float _S2952 = as_type<float>((&kernelContext_0)->frameParameters_0[(9452U)>>2]);
    float4 _S2953 = float4(_S2949, _S2950, _S2951, _S2952);
    float _S2954 = as_type<float>((&kernelContext_0)->frameParameters_0[(9456U)>>2]);
    float _S2955 = as_type<float>((&kernelContext_0)->frameParameters_0[(9460U)>>2]);
    float _S2956 = as_type<float>((&kernelContext_0)->frameParameters_0[(9464U)>>2]);
    float _S2957 = as_type<float>((&kernelContext_0)->frameParameters_0[(9468U)>>2]);
    float4 _S2958 = float4(_S2954, _S2955, _S2956, _S2957);
    float _S2959 = as_type<float>((&kernelContext_0)->frameParameters_0[(9472U)>>2]);
    float _S2960 = as_type<float>((&kernelContext_0)->frameParameters_0[(9476U)>>2]);
    float _S2961 = as_type<float>((&kernelContext_0)->frameParameters_0[(9480U)>>2]);
    float _S2962 = as_type<float>((&kernelContext_0)->frameParameters_0[(9484U)>>2]);
    float4 _S2963 = float4(_S2959, _S2960, _S2961, _S2962);
    float _S2964 = as_type<float>((&kernelContext_0)->frameParameters_0[(9488U)>>2]);
    float _S2965 = as_type<float>((&kernelContext_0)->frameParameters_0[(9492U)>>2]);
    float _S2966 = as_type<float>((&kernelContext_0)->frameParameters_0[(9496U)>>2]);
    float _S2967 = as_type<float>((&kernelContext_0)->frameParameters_0[(9500U)>>2]);
    float4 _S2968 = float4(_S2964, _S2965, _S2966, _S2967);
    float _S2969 = as_type<float>((&kernelContext_0)->frameParameters_0[(9504U)>>2]);
    float _S2970 = as_type<float>((&kernelContext_0)->frameParameters_0[(9508U)>>2]);
    float _S2971 = as_type<float>((&kernelContext_0)->frameParameters_0[(9512U)>>2]);
    float _S2972 = as_type<float>((&kernelContext_0)->frameParameters_0[(9516U)>>2]);
    float4 _S2973 = float4(_S2969, _S2970, _S2971, _S2972);
    float _S2974 = as_type<float>((&kernelContext_0)->frameParameters_0[(9520U)>>2]);
    float _S2975 = as_type<float>((&kernelContext_0)->frameParameters_0[(9524U)>>2]);
    float _S2976 = as_type<float>((&kernelContext_0)->frameParameters_0[(9528U)>>2]);
    float _S2977 = as_type<float>((&kernelContext_0)->frameParameters_0[(9532U)>>2]);
    float4 _S2978 = float4(_S2974, _S2975, _S2976, _S2977);
    float _S2979 = as_type<float>((&kernelContext_0)->frameParameters_0[(9536U)>>2]);
    float _S2980 = as_type<float>((&kernelContext_0)->frameParameters_0[(9540U)>>2]);
    float _S2981 = as_type<float>((&kernelContext_0)->frameParameters_0[(9544U)>>2]);
    float _S2982 = as_type<float>((&kernelContext_0)->frameParameters_0[(9548U)>>2]);
    float4 _S2983 = float4(_S2979, _S2980, _S2981, _S2982);
    float _S2984 = as_type<float>((&kernelContext_0)->frameParameters_0[(9552U)>>2]);
    float _S2985 = as_type<float>((&kernelContext_0)->frameParameters_0[(9556U)>>2]);
    float _S2986 = as_type<float>((&kernelContext_0)->frameParameters_0[(9560U)>>2]);
    float _S2987 = as_type<float>((&kernelContext_0)->frameParameters_0[(9564U)>>2]);
    float4 _S2988 = float4(_S2984, _S2985, _S2986, _S2987);
    float _S2989 = as_type<float>((&kernelContext_0)->frameParameters_0[(9568U)>>2]);
    float _S2990 = as_type<float>((&kernelContext_0)->frameParameters_0[(9572U)>>2]);
    float _S2991 = as_type<float>((&kernelContext_0)->frameParameters_0[(9576U)>>2]);
    float _S2992 = as_type<float>((&kernelContext_0)->frameParameters_0[(9580U)>>2]);
    float4 _S2993 = float4(_S2989, _S2990, _S2991, _S2992);
    float _S2994 = as_type<float>((&kernelContext_0)->frameParameters_0[(9584U)>>2]);
    float _S2995 = as_type<float>((&kernelContext_0)->frameParameters_0[(9588U)>>2]);
    float _S2996 = as_type<float>((&kernelContext_0)->frameParameters_0[(9592U)>>2]);
    float _S2997 = as_type<float>((&kernelContext_0)->frameParameters_0[(9596U)>>2]);
    float4 _S2998 = float4(_S2994, _S2995, _S2996, _S2997);
    float _S2999 = as_type<float>((&kernelContext_0)->frameParameters_0[(9600U)>>2]);
    float _S3000 = as_type<float>((&kernelContext_0)->frameParameters_0[(9604U)>>2]);
    float _S3001 = as_type<float>((&kernelContext_0)->frameParameters_0[(9608U)>>2]);
    float _S3002 = as_type<float>((&kernelContext_0)->frameParameters_0[(9612U)>>2]);
    float4 _S3003 = float4(_S2999, _S3000, _S3001, _S3002);
    float _S3004 = as_type<float>((&kernelContext_0)->frameParameters_0[(9616U)>>2]);
    float _S3005 = as_type<float>((&kernelContext_0)->frameParameters_0[(9620U)>>2]);
    float _S3006 = as_type<float>((&kernelContext_0)->frameParameters_0[(9624U)>>2]);
    float _S3007 = as_type<float>((&kernelContext_0)->frameParameters_0[(9628U)>>2]);
    float4 _S3008 = float4(_S3004, _S3005, _S3006, _S3007);
    float _S3009 = as_type<float>((&kernelContext_0)->frameParameters_0[(9632U)>>2]);
    float _S3010 = as_type<float>((&kernelContext_0)->frameParameters_0[(9636U)>>2]);
    float _S3011 = as_type<float>((&kernelContext_0)->frameParameters_0[(9640U)>>2]);
    float _S3012 = as_type<float>((&kernelContext_0)->frameParameters_0[(9644U)>>2]);
    float4 _S3013 = float4(_S3009, _S3010, _S3011, _S3012);
    float _S3014 = as_type<float>((&kernelContext_0)->frameParameters_0[(9648U)>>2]);
    float _S3015 = as_type<float>((&kernelContext_0)->frameParameters_0[(9652U)>>2]);
    float _S3016 = as_type<float>((&kernelContext_0)->frameParameters_0[(9656U)>>2]);
    float _S3017 = as_type<float>((&kernelContext_0)->frameParameters_0[(9660U)>>2]);
    float4 _S3018 = float4(_S3014, _S3015, _S3016, _S3017);
    float _S3019 = as_type<float>((&kernelContext_0)->frameParameters_0[(9664U)>>2]);
    float _S3020 = as_type<float>((&kernelContext_0)->frameParameters_0[(9668U)>>2]);
    float _S3021 = as_type<float>((&kernelContext_0)->frameParameters_0[(9672U)>>2]);
    float _S3022 = as_type<float>((&kernelContext_0)->frameParameters_0[(9676U)>>2]);
    float4 _S3023 = float4(_S3019, _S3020, _S3021, _S3022);
    float _S3024 = as_type<float>((&kernelContext_0)->frameParameters_0[(9680U)>>2]);
    float _S3025 = as_type<float>((&kernelContext_0)->frameParameters_0[(9684U)>>2]);
    float _S3026 = as_type<float>((&kernelContext_0)->frameParameters_0[(9688U)>>2]);
    float _S3027 = as_type<float>((&kernelContext_0)->frameParameters_0[(9692U)>>2]);
    float4 _S3028 = float4(_S3024, _S3025, _S3026, _S3027);
    float _S3029 = as_type<float>((&kernelContext_0)->frameParameters_0[(9696U)>>2]);
    float _S3030 = as_type<float>((&kernelContext_0)->frameParameters_0[(9700U)>>2]);
    float _S3031 = as_type<float>((&kernelContext_0)->frameParameters_0[(9704U)>>2]);
    float _S3032 = as_type<float>((&kernelContext_0)->frameParameters_0[(9708U)>>2]);
    float4 _S3033 = float4(_S3029, _S3030, _S3031, _S3032);
    float _S3034 = as_type<float>((&kernelContext_0)->frameParameters_0[(9712U)>>2]);
    float _S3035 = as_type<float>((&kernelContext_0)->frameParameters_0[(9716U)>>2]);
    float _S3036 = as_type<float>((&kernelContext_0)->frameParameters_0[(9720U)>>2]);
    float _S3037 = as_type<float>((&kernelContext_0)->frameParameters_0[(9724U)>>2]);
    float4 _S3038 = float4(_S3034, _S3035, _S3036, _S3037);
    float _S3039 = as_type<float>((&kernelContext_0)->frameParameters_0[(9728U)>>2]);
    float _S3040 = as_type<float>((&kernelContext_0)->frameParameters_0[(9732U)>>2]);
    float _S3041 = as_type<float>((&kernelContext_0)->frameParameters_0[(9736U)>>2]);
    float _S3042 = as_type<float>((&kernelContext_0)->frameParameters_0[(9740U)>>2]);
    float4 _S3043 = float4(_S3039, _S3040, _S3041, _S3042);
    float _S3044 = as_type<float>((&kernelContext_0)->frameParameters_0[(9744U)>>2]);
    float _S3045 = as_type<float>((&kernelContext_0)->frameParameters_0[(9748U)>>2]);
    float _S3046 = as_type<float>((&kernelContext_0)->frameParameters_0[(9752U)>>2]);
    float _S3047 = as_type<float>((&kernelContext_0)->frameParameters_0[(9756U)>>2]);
    float4 _S3048 = float4(_S3044, _S3045, _S3046, _S3047);
    float _S3049 = as_type<float>((&kernelContext_0)->frameParameters_0[(9760U)>>2]);
    float _S3050 = as_type<float>((&kernelContext_0)->frameParameters_0[(9764U)>>2]);
    float _S3051 = as_type<float>((&kernelContext_0)->frameParameters_0[(9768U)>>2]);
    float _S3052 = as_type<float>((&kernelContext_0)->frameParameters_0[(9772U)>>2]);
    float4 _S3053 = float4(_S3049, _S3050, _S3051, _S3052);
    float _S3054 = as_type<float>((&kernelContext_0)->frameParameters_0[(9776U)>>2]);
    float _S3055 = as_type<float>((&kernelContext_0)->frameParameters_0[(9780U)>>2]);
    float _S3056 = as_type<float>((&kernelContext_0)->frameParameters_0[(9784U)>>2]);
    float _S3057 = as_type<float>((&kernelContext_0)->frameParameters_0[(9788U)>>2]);
    float4 _S3058 = float4(_S3054, _S3055, _S3056, _S3057);
    float _S3059 = as_type<float>((&kernelContext_0)->frameParameters_0[(9792U)>>2]);
    float _S3060 = as_type<float>((&kernelContext_0)->frameParameters_0[(9796U)>>2]);
    float _S3061 = as_type<float>((&kernelContext_0)->frameParameters_0[(9800U)>>2]);
    float _S3062 = as_type<float>((&kernelContext_0)->frameParameters_0[(9804U)>>2]);
    float4 _S3063 = float4(_S3059, _S3060, _S3061, _S3062);
    float _S3064 = as_type<float>((&kernelContext_0)->frameParameters_0[(9808U)>>2]);
    float _S3065 = as_type<float>((&kernelContext_0)->frameParameters_0[(9812U)>>2]);
    float _S3066 = as_type<float>((&kernelContext_0)->frameParameters_0[(9816U)>>2]);
    float _S3067 = as_type<float>((&kernelContext_0)->frameParameters_0[(9820U)>>2]);
    float4 _S3068 = float4(_S3064, _S3065, _S3066, _S3067);
    float _S3069 = as_type<float>((&kernelContext_0)->frameParameters_0[(9824U)>>2]);
    float _S3070 = as_type<float>((&kernelContext_0)->frameParameters_0[(9828U)>>2]);
    float _S3071 = as_type<float>((&kernelContext_0)->frameParameters_0[(9832U)>>2]);
    float _S3072 = as_type<float>((&kernelContext_0)->frameParameters_0[(9836U)>>2]);
    float4 _S3073 = float4(_S3069, _S3070, _S3071, _S3072);
    float _S3074 = as_type<float>((&kernelContext_0)->frameParameters_0[(9840U)>>2]);
    float _S3075 = as_type<float>((&kernelContext_0)->frameParameters_0[(9844U)>>2]);
    float _S3076 = as_type<float>((&kernelContext_0)->frameParameters_0[(9848U)>>2]);
    float _S3077 = as_type<float>((&kernelContext_0)->frameParameters_0[(9852U)>>2]);
    float4 _S3078 = float4(_S3074, _S3075, _S3076, _S3077);
    float _S3079 = as_type<float>((&kernelContext_0)->frameParameters_0[(9856U)>>2]);
    float _S3080 = as_type<float>((&kernelContext_0)->frameParameters_0[(9860U)>>2]);
    float _S3081 = as_type<float>((&kernelContext_0)->frameParameters_0[(9864U)>>2]);
    float _S3082 = as_type<float>((&kernelContext_0)->frameParameters_0[(9868U)>>2]);
    float4 _S3083 = float4(_S3079, _S3080, _S3081, _S3082);
    float _S3084 = as_type<float>((&kernelContext_0)->frameParameters_0[(9872U)>>2]);
    float _S3085 = as_type<float>((&kernelContext_0)->frameParameters_0[(9876U)>>2]);
    float _S3086 = as_type<float>((&kernelContext_0)->frameParameters_0[(9880U)>>2]);
    float _S3087 = as_type<float>((&kernelContext_0)->frameParameters_0[(9884U)>>2]);
    float4 _S3088 = float4(_S3084, _S3085, _S3086, _S3087);
    float _S3089 = as_type<float>((&kernelContext_0)->frameParameters_0[(9888U)>>2]);
    float _S3090 = as_type<float>((&kernelContext_0)->frameParameters_0[(9892U)>>2]);
    float _S3091 = as_type<float>((&kernelContext_0)->frameParameters_0[(9896U)>>2]);
    float _S3092 = as_type<float>((&kernelContext_0)->frameParameters_0[(9900U)>>2]);
    float4 _S3093 = float4(_S3089, _S3090, _S3091, _S3092);
    float _S3094 = as_type<float>((&kernelContext_0)->frameParameters_0[(9904U)>>2]);
    float _S3095 = as_type<float>((&kernelContext_0)->frameParameters_0[(9908U)>>2]);
    float _S3096 = as_type<float>((&kernelContext_0)->frameParameters_0[(9912U)>>2]);
    float _S3097 = as_type<float>((&kernelContext_0)->frameParameters_0[(9916U)>>2]);
    float4 _S3098 = float4(_S3094, _S3095, _S3096, _S3097);
    float _S3099 = as_type<float>((&kernelContext_0)->frameParameters_0[(9920U)>>2]);
    float _S3100 = as_type<float>((&kernelContext_0)->frameParameters_0[(9924U)>>2]);
    float _S3101 = as_type<float>((&kernelContext_0)->frameParameters_0[(9928U)>>2]);
    float _S3102 = as_type<float>((&kernelContext_0)->frameParameters_0[(9932U)>>2]);
    float4 _S3103 = float4(_S3099, _S3100, _S3101, _S3102);
    float _S3104 = as_type<float>((&kernelContext_0)->frameParameters_0[(9936U)>>2]);
    float _S3105 = as_type<float>((&kernelContext_0)->frameParameters_0[(9940U)>>2]);
    float _S3106 = as_type<float>((&kernelContext_0)->frameParameters_0[(9944U)>>2]);
    float _S3107 = as_type<float>((&kernelContext_0)->frameParameters_0[(9948U)>>2]);
    float4 _S3108 = float4(_S3104, _S3105, _S3106, _S3107);
    float _S3109 = as_type<float>((&kernelContext_0)->frameParameters_0[(9952U)>>2]);
    float _S3110 = as_type<float>((&kernelContext_0)->frameParameters_0[(9956U)>>2]);
    float _S3111 = as_type<float>((&kernelContext_0)->frameParameters_0[(9960U)>>2]);
    float _S3112 = as_type<float>((&kernelContext_0)->frameParameters_0[(9964U)>>2]);
    float4 _S3113 = float4(_S3109, _S3110, _S3111, _S3112);
    float _S3114 = as_type<float>((&kernelContext_0)->frameParameters_0[(9968U)>>2]);
    float _S3115 = as_type<float>((&kernelContext_0)->frameParameters_0[(9972U)>>2]);
    float _S3116 = as_type<float>((&kernelContext_0)->frameParameters_0[(9976U)>>2]);
    float _S3117 = as_type<float>((&kernelContext_0)->frameParameters_0[(9980U)>>2]);
    float4 _S3118 = float4(_S3114, _S3115, _S3116, _S3117);
    float _S3119 = as_type<float>((&kernelContext_0)->frameParameters_0[(9984U)>>2]);
    float _S3120 = as_type<float>((&kernelContext_0)->frameParameters_0[(9988U)>>2]);
    float _S3121 = as_type<float>((&kernelContext_0)->frameParameters_0[(9992U)>>2]);
    float _S3122 = as_type<float>((&kernelContext_0)->frameParameters_0[(9996U)>>2]);
    float4 _S3123 = float4(_S3119, _S3120, _S3121, _S3122);
    float _S3124 = as_type<float>((&kernelContext_0)->frameParameters_0[(10000U)>>2]);
    float _S3125 = as_type<float>((&kernelContext_0)->frameParameters_0[(10004U)>>2]);
    float _S3126 = as_type<float>((&kernelContext_0)->frameParameters_0[(10008U)>>2]);
    float _S3127 = as_type<float>((&kernelContext_0)->frameParameters_0[(10012U)>>2]);
    float4 _S3128 = float4(_S3124, _S3125, _S3126, _S3127);
    float _S3129 = as_type<float>((&kernelContext_0)->frameParameters_0[(10016U)>>2]);
    float _S3130 = as_type<float>((&kernelContext_0)->frameParameters_0[(10020U)>>2]);
    float _S3131 = as_type<float>((&kernelContext_0)->frameParameters_0[(10024U)>>2]);
    float _S3132 = as_type<float>((&kernelContext_0)->frameParameters_0[(10028U)>>2]);
    float4 _S3133 = float4(_S3129, _S3130, _S3131, _S3132);
    float _S3134 = as_type<float>((&kernelContext_0)->frameParameters_0[(10032U)>>2]);
    float _S3135 = as_type<float>((&kernelContext_0)->frameParameters_0[(10036U)>>2]);
    float _S3136 = as_type<float>((&kernelContext_0)->frameParameters_0[(10040U)>>2]);
    float _S3137 = as_type<float>((&kernelContext_0)->frameParameters_0[(10044U)>>2]);
    float4 _S3138 = float4(_S3134, _S3135, _S3136, _S3137);
    float _S3139 = as_type<float>((&kernelContext_0)->frameParameters_0[(10048U)>>2]);
    float _S3140 = as_type<float>((&kernelContext_0)->frameParameters_0[(10052U)>>2]);
    float _S3141 = as_type<float>((&kernelContext_0)->frameParameters_0[(10056U)>>2]);
    float _S3142 = as_type<float>((&kernelContext_0)->frameParameters_0[(10060U)>>2]);
    float4 _S3143 = float4(_S3139, _S3140, _S3141, _S3142);
    float _S3144 = as_type<float>((&kernelContext_0)->frameParameters_0[(10064U)>>2]);
    float _S3145 = as_type<float>((&kernelContext_0)->frameParameters_0[(10068U)>>2]);
    float _S3146 = as_type<float>((&kernelContext_0)->frameParameters_0[(10072U)>>2]);
    float _S3147 = as_type<float>((&kernelContext_0)->frameParameters_0[(10076U)>>2]);
    float4 _S3148 = float4(_S3144, _S3145, _S3146, _S3147);
    float _S3149 = as_type<float>((&kernelContext_0)->frameParameters_0[(10080U)>>2]);
    float _S3150 = as_type<float>((&kernelContext_0)->frameParameters_0[(10084U)>>2]);
    float _S3151 = as_type<float>((&kernelContext_0)->frameParameters_0[(10088U)>>2]);
    float _S3152 = as_type<float>((&kernelContext_0)->frameParameters_0[(10092U)>>2]);
    float4 _S3153 = float4(_S3149, _S3150, _S3151, _S3152);
    float _S3154 = as_type<float>((&kernelContext_0)->frameParameters_0[(10096U)>>2]);
    float _S3155 = as_type<float>((&kernelContext_0)->frameParameters_0[(10100U)>>2]);
    float _S3156 = as_type<float>((&kernelContext_0)->frameParameters_0[(10104U)>>2]);
    float _S3157 = as_type<float>((&kernelContext_0)->frameParameters_0[(10108U)>>2]);
    float4 _S3158 = float4(_S3154, _S3155, _S3156, _S3157);
    float _S3159 = as_type<float>((&kernelContext_0)->frameParameters_0[(10112U)>>2]);
    float _S3160 = as_type<float>((&kernelContext_0)->frameParameters_0[(10116U)>>2]);
    float _S3161 = as_type<float>((&kernelContext_0)->frameParameters_0[(10120U)>>2]);
    float _S3162 = as_type<float>((&kernelContext_0)->frameParameters_0[(10124U)>>2]);
    float4 _S3163 = float4(_S3159, _S3160, _S3161, _S3162);
    float _S3164 = as_type<float>((&kernelContext_0)->frameParameters_0[(10128U)>>2]);
    float _S3165 = as_type<float>((&kernelContext_0)->frameParameters_0[(10132U)>>2]);
    float _S3166 = as_type<float>((&kernelContext_0)->frameParameters_0[(10136U)>>2]);
    float _S3167 = as_type<float>((&kernelContext_0)->frameParameters_0[(10140U)>>2]);
    float4 _S3168 = float4(_S3164, _S3165, _S3166, _S3167);
    float _S3169 = as_type<float>((&kernelContext_0)->frameParameters_0[(10144U)>>2]);
    float _S3170 = as_type<float>((&kernelContext_0)->frameParameters_0[(10148U)>>2]);
    float _S3171 = as_type<float>((&kernelContext_0)->frameParameters_0[(10152U)>>2]);
    float _S3172 = as_type<float>((&kernelContext_0)->frameParameters_0[(10156U)>>2]);
    float4 _S3173 = float4(_S3169, _S3170, _S3171, _S3172);
    float _S3174 = as_type<float>((&kernelContext_0)->frameParameters_0[(10160U)>>2]);
    float _S3175 = as_type<float>((&kernelContext_0)->frameParameters_0[(10164U)>>2]);
    float _S3176 = as_type<float>((&kernelContext_0)->frameParameters_0[(10168U)>>2]);
    float _S3177 = as_type<float>((&kernelContext_0)->frameParameters_0[(10172U)>>2]);
    float4 _S3178 = float4(_S3174, _S3175, _S3176, _S3177);
    float _S3179 = as_type<float>((&kernelContext_0)->frameParameters_0[(10176U)>>2]);
    float _S3180 = as_type<float>((&kernelContext_0)->frameParameters_0[(10180U)>>2]);
    float _S3181 = as_type<float>((&kernelContext_0)->frameParameters_0[(10184U)>>2]);
    float _S3182 = as_type<float>((&kernelContext_0)->frameParameters_0[(10188U)>>2]);
    float4 _S3183 = float4(_S3179, _S3180, _S3181, _S3182);
    float _S3184 = as_type<float>((&kernelContext_0)->frameParameters_0[(10192U)>>2]);
    float _S3185 = as_type<float>((&kernelContext_0)->frameParameters_0[(10196U)>>2]);
    float _S3186 = as_type<float>((&kernelContext_0)->frameParameters_0[(10200U)>>2]);
    float _S3187 = as_type<float>((&kernelContext_0)->frameParameters_0[(10204U)>>2]);
    float4 _S3188 = float4(_S3184, _S3185, _S3186, _S3187);
    float _S3189 = as_type<float>((&kernelContext_0)->frameParameters_0[(10208U)>>2]);
    float _S3190 = as_type<float>((&kernelContext_0)->frameParameters_0[(10212U)>>2]);
    float _S3191 = as_type<float>((&kernelContext_0)->frameParameters_0[(10216U)>>2]);
    float _S3192 = as_type<float>((&kernelContext_0)->frameParameters_0[(10220U)>>2]);
    float4 _S3193 = float4(_S3189, _S3190, _S3191, _S3192);
    float _S3194 = as_type<float>((&kernelContext_0)->frameParameters_0[(10224U)>>2]);
    float _S3195 = as_type<float>((&kernelContext_0)->frameParameters_0[(10228U)>>2]);
    float _S3196 = as_type<float>((&kernelContext_0)->frameParameters_0[(10232U)>>2]);
    float _S3197 = as_type<float>((&kernelContext_0)->frameParameters_0[(10236U)>>2]);
    float4 _S3198 = float4(_S3194, _S3195, _S3196, _S3197);
    float _S3199 = as_type<float>((&kernelContext_0)->frameParameters_0[(10240U)>>2]);
    float _S3200 = as_type<float>((&kernelContext_0)->frameParameters_0[(10244U)>>2]);
    float _S3201 = as_type<float>((&kernelContext_0)->frameParameters_0[(10248U)>>2]);
    float _S3202 = as_type<float>((&kernelContext_0)->frameParameters_0[(10252U)>>2]);
    float4 _S3203 = float4(_S3199, _S3200, _S3201, _S3202);
    float _S3204 = as_type<float>((&kernelContext_0)->frameParameters_0[(10256U)>>2]);
    float _S3205 = as_type<float>((&kernelContext_0)->frameParameters_0[(10260U)>>2]);
    float _S3206 = as_type<float>((&kernelContext_0)->frameParameters_0[(10264U)>>2]);
    float _S3207 = as_type<float>((&kernelContext_0)->frameParameters_0[(10268U)>>2]);
    float4 _S3208 = float4(_S3204, _S3205, _S3206, _S3207);
    float _S3209 = as_type<float>((&kernelContext_0)->frameParameters_0[(10272U)>>2]);
    float _S3210 = as_type<float>((&kernelContext_0)->frameParameters_0[(10276U)>>2]);
    float _S3211 = as_type<float>((&kernelContext_0)->frameParameters_0[(10280U)>>2]);
    float _S3212 = as_type<float>((&kernelContext_0)->frameParameters_0[(10284U)>>2]);
    float4 _S3213 = float4(_S3209, _S3210, _S3211, _S3212);
    float _S3214 = as_type<float>((&kernelContext_0)->frameParameters_0[(10288U)>>2]);
    float _S3215 = as_type<float>((&kernelContext_0)->frameParameters_0[(10292U)>>2]);
    float _S3216 = as_type<float>((&kernelContext_0)->frameParameters_0[(10296U)>>2]);
    float _S3217 = as_type<float>((&kernelContext_0)->frameParameters_0[(10300U)>>2]);
    float4 _S3218 = float4(_S3214, _S3215, _S3216, _S3217);
    float _S3219 = as_type<float>((&kernelContext_0)->frameParameters_0[(10304U)>>2]);
    float _S3220 = as_type<float>((&kernelContext_0)->frameParameters_0[(10308U)>>2]);
    float _S3221 = as_type<float>((&kernelContext_0)->frameParameters_0[(10312U)>>2]);
    float _S3222 = as_type<float>((&kernelContext_0)->frameParameters_0[(10316U)>>2]);
    float4 _S3223 = float4(_S3219, _S3220, _S3221, _S3222);
    float _S3224 = as_type<float>((&kernelContext_0)->frameParameters_0[(10320U)>>2]);
    float _S3225 = as_type<float>((&kernelContext_0)->frameParameters_0[(10324U)>>2]);
    float _S3226 = as_type<float>((&kernelContext_0)->frameParameters_0[(10328U)>>2]);
    float _S3227 = as_type<float>((&kernelContext_0)->frameParameters_0[(10332U)>>2]);
    float4 _S3228 = float4(_S3224, _S3225, _S3226, _S3227);
    float _S3229 = as_type<float>((&kernelContext_0)->frameParameters_0[(10336U)>>2]);
    float _S3230 = as_type<float>((&kernelContext_0)->frameParameters_0[(10340U)>>2]);
    float _S3231 = as_type<float>((&kernelContext_0)->frameParameters_0[(10344U)>>2]);
    float _S3232 = as_type<float>((&kernelContext_0)->frameParameters_0[(10348U)>>2]);
    float4 _S3233 = float4(_S3229, _S3230, _S3231, _S3232);
    float _S3234 = as_type<float>((&kernelContext_0)->frameParameters_0[(10352U)>>2]);
    float _S3235 = as_type<float>((&kernelContext_0)->frameParameters_0[(10356U)>>2]);
    float _S3236 = as_type<float>((&kernelContext_0)->frameParameters_0[(10360U)>>2]);
    float _S3237 = as_type<float>((&kernelContext_0)->frameParameters_0[(10364U)>>2]);
    float4 _S3238 = float4(_S3234, _S3235, _S3236, _S3237);
    float _S3239 = as_type<float>((&kernelContext_0)->frameParameters_0[(10368U)>>2]);
    float _S3240 = as_type<float>((&kernelContext_0)->frameParameters_0[(10372U)>>2]);
    float _S3241 = as_type<float>((&kernelContext_0)->frameParameters_0[(10376U)>>2]);
    float _S3242 = as_type<float>((&kernelContext_0)->frameParameters_0[(10380U)>>2]);
    float4 _S3243 = float4(_S3239, _S3240, _S3241, _S3242);
    float _S3244 = as_type<float>((&kernelContext_0)->frameParameters_0[(10384U)>>2]);
    float _S3245 = as_type<float>((&kernelContext_0)->frameParameters_0[(10388U)>>2]);
    float _S3246 = as_type<float>((&kernelContext_0)->frameParameters_0[(10392U)>>2]);
    float _S3247 = as_type<float>((&kernelContext_0)->frameParameters_0[(10396U)>>2]);
    float4 _S3248 = float4(_S3244, _S3245, _S3246, _S3247);
    float _S3249 = as_type<float>((&kernelContext_0)->frameParameters_0[(10400U)>>2]);
    float _S3250 = as_type<float>((&kernelContext_0)->frameParameters_0[(10404U)>>2]);
    float _S3251 = as_type<float>((&kernelContext_0)->frameParameters_0[(10408U)>>2]);
    float _S3252 = as_type<float>((&kernelContext_0)->frameParameters_0[(10412U)>>2]);
    float4 _S3253 = float4(_S3249, _S3250, _S3251, _S3252);
    float _S3254 = as_type<float>((&kernelContext_0)->frameParameters_0[(10416U)>>2]);
    float _S3255 = as_type<float>((&kernelContext_0)->frameParameters_0[(10420U)>>2]);
    float _S3256 = as_type<float>((&kernelContext_0)->frameParameters_0[(10424U)>>2]);
    float _S3257 = as_type<float>((&kernelContext_0)->frameParameters_0[(10428U)>>2]);
    float4 _S3258 = float4(_S3254, _S3255, _S3256, _S3257);
    float _S3259 = as_type<float>((&kernelContext_0)->frameParameters_0[(10432U)>>2]);
    float _S3260 = as_type<float>((&kernelContext_0)->frameParameters_0[(10436U)>>2]);
    float _S3261 = as_type<float>((&kernelContext_0)->frameParameters_0[(10440U)>>2]);
    float _S3262 = as_type<float>((&kernelContext_0)->frameParameters_0[(10444U)>>2]);
    float4 _S3263 = float4(_S3259, _S3260, _S3261, _S3262);
    float _S3264 = as_type<float>((&kernelContext_0)->frameParameters_0[(10448U)>>2]);
    float _S3265 = as_type<float>((&kernelContext_0)->frameParameters_0[(10452U)>>2]);
    float _S3266 = as_type<float>((&kernelContext_0)->frameParameters_0[(10456U)>>2]);
    float _S3267 = as_type<float>((&kernelContext_0)->frameParameters_0[(10460U)>>2]);
    array<float4, int(128)> _S3268 = { _S2633, _S2638, _S2643, _S2648, _S2653, _S2658, _S2663, _S2668, _S2673, _S2678, _S2683, _S2688, _S2693, _S2698, _S2703, _S2708, _S2713, _S2718, _S2723, _S2728, _S2733, _S2738, _S2743, _S2748, _S2753, _S2758, _S2763, _S2768, _S2773, _S2778, _S2783, _S2788, _S2793, _S2798, _S2803, _S2808, _S2813, _S2818, _S2823, _S2828, _S2833, _S2838, _S2843, _S2848, _S2853, _S2858, _S2863, _S2868, _S2873, _S2878, _S2883, _S2888, _S2893, _S2898, _S2903, _S2908, _S2913, _S2918, _S2923, _S2928, _S2933, _S2938, _S2943, _S2948, _S2953, _S2958, _S2963, _S2968, _S2973, _S2978, _S2983, _S2988, _S2993, _S2998, _S3003, _S3008, _S3013, _S3018, _S3023, _S3028, _S3033, _S3038, _S3043, _S3048, _S3053, _S3058, _S3063, _S3068, _S3073, _S3078, _S3083, _S3088, _S3093, _S3098, _S3103, _S3108, _S3113, _S3118, _S3123, _S3128, _S3133, _S3138, _S3143, _S3148, _S3153, _S3158, _S3163, _S3168, _S3173, _S3178, _S3183, _S3188, _S3193, _S3198, _S3203, _S3208, _S3213, _S3218, _S3223, _S3228, _S3233, _S3238, _S3243, _S3248, _S3253, _S3258, _S3263, float4(_S3264, _S3265, _S3266, _S3267) };
    float _S3269 = as_type<float>((&kernelContext_0)->frameParameters_0[(10464U)>>2]);
    float _S3270 = as_type<float>((&kernelContext_0)->frameParameters_0[(10468U)>>2]);
    float _S3271 = as_type<float>((&kernelContext_0)->frameParameters_0[(10472U)>>2]);
    float _S3272 = as_type<float>((&kernelContext_0)->frameParameters_0[(10476U)>>2]);
    float4 _S3273 = float4(_S3269, _S3270, _S3271, _S3272);
    float _S3274 = as_type<float>((&kernelContext_0)->frameParameters_0[(10480U)>>2]);
    float _S3275 = as_type<float>((&kernelContext_0)->frameParameters_0[(10484U)>>2]);
    float _S3276 = as_type<float>((&kernelContext_0)->frameParameters_0[(10488U)>>2]);
    float _S3277 = as_type<float>((&kernelContext_0)->frameParameters_0[(10492U)>>2]);
    float4 _S3278 = float4(_S3274, _S3275, _S3276, _S3277);
    float _S3279 = as_type<float>((&kernelContext_0)->frameParameters_0[(10496U)>>2]);
    float _S3280 = as_type<float>((&kernelContext_0)->frameParameters_0[(10500U)>>2]);
    float _S3281 = as_type<float>((&kernelContext_0)->frameParameters_0[(10504U)>>2]);
    float _S3282 = as_type<float>((&kernelContext_0)->frameParameters_0[(10508U)>>2]);
    float4 _S3283 = float4(_S3279, _S3280, _S3281, _S3282);
    float _S3284 = as_type<float>((&kernelContext_0)->frameParameters_0[(10512U)>>2]);
    float _S3285 = as_type<float>((&kernelContext_0)->frameParameters_0[(10516U)>>2]);
    float _S3286 = as_type<float>((&kernelContext_0)->frameParameters_0[(10520U)>>2]);
    float _S3287 = as_type<float>((&kernelContext_0)->frameParameters_0[(10524U)>>2]);
    float4 _S3288 = float4(_S3284, _S3285, _S3286, _S3287);
    float _S3289 = as_type<float>((&kernelContext_0)->frameParameters_0[(10528U)>>2]);
    float _S3290 = as_type<float>((&kernelContext_0)->frameParameters_0[(10532U)>>2]);
    float _S3291 = as_type<float>((&kernelContext_0)->frameParameters_0[(10536U)>>2]);
    float _S3292 = as_type<float>((&kernelContext_0)->frameParameters_0[(10540U)>>2]);
    float4 _S3293 = float4(_S3289, _S3290, _S3291, _S3292);
    float _S3294 = as_type<float>((&kernelContext_0)->frameParameters_0[(10544U)>>2]);
    float _S3295 = as_type<float>((&kernelContext_0)->frameParameters_0[(10548U)>>2]);
    float _S3296 = as_type<float>((&kernelContext_0)->frameParameters_0[(10552U)>>2]);
    float _S3297 = as_type<float>((&kernelContext_0)->frameParameters_0[(10556U)>>2]);
    float4 _S3298 = float4(_S3294, _S3295, _S3296, _S3297);
    float _S3299 = as_type<float>((&kernelContext_0)->frameParameters_0[(10560U)>>2]);
    float _S3300 = as_type<float>((&kernelContext_0)->frameParameters_0[(10564U)>>2]);
    float _S3301 = as_type<float>((&kernelContext_0)->frameParameters_0[(10568U)>>2]);
    float _S3302 = as_type<float>((&kernelContext_0)->frameParameters_0[(10572U)>>2]);
    float4 _S3303 = float4(_S3299, _S3300, _S3301, _S3302);
    float _S3304 = as_type<float>((&kernelContext_0)->frameParameters_0[(10576U)>>2]);
    float _S3305 = as_type<float>((&kernelContext_0)->frameParameters_0[(10580U)>>2]);
    float _S3306 = as_type<float>((&kernelContext_0)->frameParameters_0[(10584U)>>2]);
    float _S3307 = as_type<float>((&kernelContext_0)->frameParameters_0[(10588U)>>2]);
    float4 _S3308 = float4(_S3304, _S3305, _S3306, _S3307);
    float _S3309 = as_type<float>((&kernelContext_0)->frameParameters_0[(10592U)>>2]);
    float _S3310 = as_type<float>((&kernelContext_0)->frameParameters_0[(10596U)>>2]);
    float _S3311 = as_type<float>((&kernelContext_0)->frameParameters_0[(10600U)>>2]);
    float _S3312 = as_type<float>((&kernelContext_0)->frameParameters_0[(10604U)>>2]);
    float4 _S3313 = float4(_S3309, _S3310, _S3311, _S3312);
    float _S3314 = as_type<float>((&kernelContext_0)->frameParameters_0[(10608U)>>2]);
    float _S3315 = as_type<float>((&kernelContext_0)->frameParameters_0[(10612U)>>2]);
    float _S3316 = as_type<float>((&kernelContext_0)->frameParameters_0[(10616U)>>2]);
    float _S3317 = as_type<float>((&kernelContext_0)->frameParameters_0[(10620U)>>2]);
    float4 _S3318 = float4(_S3314, _S3315, _S3316, _S3317);
    float _S3319 = as_type<float>((&kernelContext_0)->frameParameters_0[(10624U)>>2]);
    float _S3320 = as_type<float>((&kernelContext_0)->frameParameters_0[(10628U)>>2]);
    float _S3321 = as_type<float>((&kernelContext_0)->frameParameters_0[(10632U)>>2]);
    float _S3322 = as_type<float>((&kernelContext_0)->frameParameters_0[(10636U)>>2]);
    float4 _S3323 = float4(_S3319, _S3320, _S3321, _S3322);
    float _S3324 = as_type<float>((&kernelContext_0)->frameParameters_0[(10640U)>>2]);
    float _S3325 = as_type<float>((&kernelContext_0)->frameParameters_0[(10644U)>>2]);
    float _S3326 = as_type<float>((&kernelContext_0)->frameParameters_0[(10648U)>>2]);
    float _S3327 = as_type<float>((&kernelContext_0)->frameParameters_0[(10652U)>>2]);
    float4 _S3328 = float4(_S3324, _S3325, _S3326, _S3327);
    float _S3329 = as_type<float>((&kernelContext_0)->frameParameters_0[(10656U)>>2]);
    float _S3330 = as_type<float>((&kernelContext_0)->frameParameters_0[(10660U)>>2]);
    float _S3331 = as_type<float>((&kernelContext_0)->frameParameters_0[(10664U)>>2]);
    float _S3332 = as_type<float>((&kernelContext_0)->frameParameters_0[(10668U)>>2]);
    float4 _S3333 = float4(_S3329, _S3330, _S3331, _S3332);
    float _S3334 = as_type<float>((&kernelContext_0)->frameParameters_0[(10672U)>>2]);
    float _S3335 = as_type<float>((&kernelContext_0)->frameParameters_0[(10676U)>>2]);
    float _S3336 = as_type<float>((&kernelContext_0)->frameParameters_0[(10680U)>>2]);
    float _S3337 = as_type<float>((&kernelContext_0)->frameParameters_0[(10684U)>>2]);
    float4 _S3338 = float4(_S3334, _S3335, _S3336, _S3337);
    float _S3339 = as_type<float>((&kernelContext_0)->frameParameters_0[(10688U)>>2]);
    float _S3340 = as_type<float>((&kernelContext_0)->frameParameters_0[(10692U)>>2]);
    float _S3341 = as_type<float>((&kernelContext_0)->frameParameters_0[(10696U)>>2]);
    float _S3342 = as_type<float>((&kernelContext_0)->frameParameters_0[(10700U)>>2]);
    float4 _S3343 = float4(_S3339, _S3340, _S3341, _S3342);
    float _S3344 = as_type<float>((&kernelContext_0)->frameParameters_0[(10704U)>>2]);
    float _S3345 = as_type<float>((&kernelContext_0)->frameParameters_0[(10708U)>>2]);
    float _S3346 = as_type<float>((&kernelContext_0)->frameParameters_0[(10712U)>>2]);
    float _S3347 = as_type<float>((&kernelContext_0)->frameParameters_0[(10716U)>>2]);
    float4 _S3348 = float4(_S3344, _S3345, _S3346, _S3347);
    float _S3349 = as_type<float>((&kernelContext_0)->frameParameters_0[(10720U)>>2]);
    float _S3350 = as_type<float>((&kernelContext_0)->frameParameters_0[(10724U)>>2]);
    float _S3351 = as_type<float>((&kernelContext_0)->frameParameters_0[(10728U)>>2]);
    float _S3352 = as_type<float>((&kernelContext_0)->frameParameters_0[(10732U)>>2]);
    float4 _S3353 = float4(_S3349, _S3350, _S3351, _S3352);
    float _S3354 = as_type<float>((&kernelContext_0)->frameParameters_0[(10736U)>>2]);
    float _S3355 = as_type<float>((&kernelContext_0)->frameParameters_0[(10740U)>>2]);
    float _S3356 = as_type<float>((&kernelContext_0)->frameParameters_0[(10744U)>>2]);
    float _S3357 = as_type<float>((&kernelContext_0)->frameParameters_0[(10748U)>>2]);
    float4 _S3358 = float4(_S3354, _S3355, _S3356, _S3357);
    float _S3359 = as_type<float>((&kernelContext_0)->frameParameters_0[(10752U)>>2]);
    float _S3360 = as_type<float>((&kernelContext_0)->frameParameters_0[(10756U)>>2]);
    float _S3361 = as_type<float>((&kernelContext_0)->frameParameters_0[(10760U)>>2]);
    float _S3362 = as_type<float>((&kernelContext_0)->frameParameters_0[(10764U)>>2]);
    float4 _S3363 = float4(_S3359, _S3360, _S3361, _S3362);
    float _S3364 = as_type<float>((&kernelContext_0)->frameParameters_0[(10768U)>>2]);
    float _S3365 = as_type<float>((&kernelContext_0)->frameParameters_0[(10772U)>>2]);
    float _S3366 = as_type<float>((&kernelContext_0)->frameParameters_0[(10776U)>>2]);
    float _S3367 = as_type<float>((&kernelContext_0)->frameParameters_0[(10780U)>>2]);
    float4 _S3368 = float4(_S3364, _S3365, _S3366, _S3367);
    float _S3369 = as_type<float>((&kernelContext_0)->frameParameters_0[(10784U)>>2]);
    float _S3370 = as_type<float>((&kernelContext_0)->frameParameters_0[(10788U)>>2]);
    float _S3371 = as_type<float>((&kernelContext_0)->frameParameters_0[(10792U)>>2]);
    float _S3372 = as_type<float>((&kernelContext_0)->frameParameters_0[(10796U)>>2]);
    float4 _S3373 = float4(_S3369, _S3370, _S3371, _S3372);
    float _S3374 = as_type<float>((&kernelContext_0)->frameParameters_0[(10800U)>>2]);
    float _S3375 = as_type<float>((&kernelContext_0)->frameParameters_0[(10804U)>>2]);
    float _S3376 = as_type<float>((&kernelContext_0)->frameParameters_0[(10808U)>>2]);
    float _S3377 = as_type<float>((&kernelContext_0)->frameParameters_0[(10812U)>>2]);
    float4 _S3378 = float4(_S3374, _S3375, _S3376, _S3377);
    float _S3379 = as_type<float>((&kernelContext_0)->frameParameters_0[(10816U)>>2]);
    float _S3380 = as_type<float>((&kernelContext_0)->frameParameters_0[(10820U)>>2]);
    float _S3381 = as_type<float>((&kernelContext_0)->frameParameters_0[(10824U)>>2]);
    float _S3382 = as_type<float>((&kernelContext_0)->frameParameters_0[(10828U)>>2]);
    float4 _S3383 = float4(_S3379, _S3380, _S3381, _S3382);
    float _S3384 = as_type<float>((&kernelContext_0)->frameParameters_0[(10832U)>>2]);
    float _S3385 = as_type<float>((&kernelContext_0)->frameParameters_0[(10836U)>>2]);
    float _S3386 = as_type<float>((&kernelContext_0)->frameParameters_0[(10840U)>>2]);
    float _S3387 = as_type<float>((&kernelContext_0)->frameParameters_0[(10844U)>>2]);
    float4 _S3388 = float4(_S3384, _S3385, _S3386, _S3387);
    float _S3389 = as_type<float>((&kernelContext_0)->frameParameters_0[(10848U)>>2]);
    float _S3390 = as_type<float>((&kernelContext_0)->frameParameters_0[(10852U)>>2]);
    float _S3391 = as_type<float>((&kernelContext_0)->frameParameters_0[(10856U)>>2]);
    float _S3392 = as_type<float>((&kernelContext_0)->frameParameters_0[(10860U)>>2]);
    float4 _S3393 = float4(_S3389, _S3390, _S3391, _S3392);
    float _S3394 = as_type<float>((&kernelContext_0)->frameParameters_0[(10864U)>>2]);
    float _S3395 = as_type<float>((&kernelContext_0)->frameParameters_0[(10868U)>>2]);
    float _S3396 = as_type<float>((&kernelContext_0)->frameParameters_0[(10872U)>>2]);
    float _S3397 = as_type<float>((&kernelContext_0)->frameParameters_0[(10876U)>>2]);
    float4 _S3398 = float4(_S3394, _S3395, _S3396, _S3397);
    float _S3399 = as_type<float>((&kernelContext_0)->frameParameters_0[(10880U)>>2]);
    float _S3400 = as_type<float>((&kernelContext_0)->frameParameters_0[(10884U)>>2]);
    float _S3401 = as_type<float>((&kernelContext_0)->frameParameters_0[(10888U)>>2]);
    float _S3402 = as_type<float>((&kernelContext_0)->frameParameters_0[(10892U)>>2]);
    float4 _S3403 = float4(_S3399, _S3400, _S3401, _S3402);
    float _S3404 = as_type<float>((&kernelContext_0)->frameParameters_0[(10896U)>>2]);
    float _S3405 = as_type<float>((&kernelContext_0)->frameParameters_0[(10900U)>>2]);
    float _S3406 = as_type<float>((&kernelContext_0)->frameParameters_0[(10904U)>>2]);
    float _S3407 = as_type<float>((&kernelContext_0)->frameParameters_0[(10908U)>>2]);
    float4 _S3408 = float4(_S3404, _S3405, _S3406, _S3407);
    float _S3409 = as_type<float>((&kernelContext_0)->frameParameters_0[(10912U)>>2]);
    float _S3410 = as_type<float>((&kernelContext_0)->frameParameters_0[(10916U)>>2]);
    float _S3411 = as_type<float>((&kernelContext_0)->frameParameters_0[(10920U)>>2]);
    float _S3412 = as_type<float>((&kernelContext_0)->frameParameters_0[(10924U)>>2]);
    float4 _S3413 = float4(_S3409, _S3410, _S3411, _S3412);
    float _S3414 = as_type<float>((&kernelContext_0)->frameParameters_0[(10928U)>>2]);
    float _S3415 = as_type<float>((&kernelContext_0)->frameParameters_0[(10932U)>>2]);
    float _S3416 = as_type<float>((&kernelContext_0)->frameParameters_0[(10936U)>>2]);
    float _S3417 = as_type<float>((&kernelContext_0)->frameParameters_0[(10940U)>>2]);
    float4 _S3418 = float4(_S3414, _S3415, _S3416, _S3417);
    float _S3419 = as_type<float>((&kernelContext_0)->frameParameters_0[(10944U)>>2]);
    float _S3420 = as_type<float>((&kernelContext_0)->frameParameters_0[(10948U)>>2]);
    float _S3421 = as_type<float>((&kernelContext_0)->frameParameters_0[(10952U)>>2]);
    float _S3422 = as_type<float>((&kernelContext_0)->frameParameters_0[(10956U)>>2]);
    float4 _S3423 = float4(_S3419, _S3420, _S3421, _S3422);
    float _S3424 = as_type<float>((&kernelContext_0)->frameParameters_0[(10960U)>>2]);
    float _S3425 = as_type<float>((&kernelContext_0)->frameParameters_0[(10964U)>>2]);
    float _S3426 = as_type<float>((&kernelContext_0)->frameParameters_0[(10968U)>>2]);
    float _S3427 = as_type<float>((&kernelContext_0)->frameParameters_0[(10972U)>>2]);
    float4 _S3428 = float4(_S3424, _S3425, _S3426, _S3427);
    float _S3429 = as_type<float>((&kernelContext_0)->frameParameters_0[(10976U)>>2]);
    float _S3430 = as_type<float>((&kernelContext_0)->frameParameters_0[(10980U)>>2]);
    float _S3431 = as_type<float>((&kernelContext_0)->frameParameters_0[(10984U)>>2]);
    float _S3432 = as_type<float>((&kernelContext_0)->frameParameters_0[(10988U)>>2]);
    float4 _S3433 = float4(_S3429, _S3430, _S3431, _S3432);
    float _S3434 = as_type<float>((&kernelContext_0)->frameParameters_0[(10992U)>>2]);
    float _S3435 = as_type<float>((&kernelContext_0)->frameParameters_0[(10996U)>>2]);
    float _S3436 = as_type<float>((&kernelContext_0)->frameParameters_0[(11000U)>>2]);
    float _S3437 = as_type<float>((&kernelContext_0)->frameParameters_0[(11004U)>>2]);
    float4 _S3438 = float4(_S3434, _S3435, _S3436, _S3437);
    float _S3439 = as_type<float>((&kernelContext_0)->frameParameters_0[(11008U)>>2]);
    float _S3440 = as_type<float>((&kernelContext_0)->frameParameters_0[(11012U)>>2]);
    float _S3441 = as_type<float>((&kernelContext_0)->frameParameters_0[(11016U)>>2]);
    float _S3442 = as_type<float>((&kernelContext_0)->frameParameters_0[(11020U)>>2]);
    float4 _S3443 = float4(_S3439, _S3440, _S3441, _S3442);
    float _S3444 = as_type<float>((&kernelContext_0)->frameParameters_0[(11024U)>>2]);
    float _S3445 = as_type<float>((&kernelContext_0)->frameParameters_0[(11028U)>>2]);
    float _S3446 = as_type<float>((&kernelContext_0)->frameParameters_0[(11032U)>>2]);
    float _S3447 = as_type<float>((&kernelContext_0)->frameParameters_0[(11036U)>>2]);
    float4 _S3448 = float4(_S3444, _S3445, _S3446, _S3447);
    float _S3449 = as_type<float>((&kernelContext_0)->frameParameters_0[(11040U)>>2]);
    float _S3450 = as_type<float>((&kernelContext_0)->frameParameters_0[(11044U)>>2]);
    float _S3451 = as_type<float>((&kernelContext_0)->frameParameters_0[(11048U)>>2]);
    float _S3452 = as_type<float>((&kernelContext_0)->frameParameters_0[(11052U)>>2]);
    float4 _S3453 = float4(_S3449, _S3450, _S3451, _S3452);
    float _S3454 = as_type<float>((&kernelContext_0)->frameParameters_0[(11056U)>>2]);
    float _S3455 = as_type<float>((&kernelContext_0)->frameParameters_0[(11060U)>>2]);
    float _S3456 = as_type<float>((&kernelContext_0)->frameParameters_0[(11064U)>>2]);
    float _S3457 = as_type<float>((&kernelContext_0)->frameParameters_0[(11068U)>>2]);
    float4 _S3458 = float4(_S3454, _S3455, _S3456, _S3457);
    float _S3459 = as_type<float>((&kernelContext_0)->frameParameters_0[(11072U)>>2]);
    float _S3460 = as_type<float>((&kernelContext_0)->frameParameters_0[(11076U)>>2]);
    float _S3461 = as_type<float>((&kernelContext_0)->frameParameters_0[(11080U)>>2]);
    float _S3462 = as_type<float>((&kernelContext_0)->frameParameters_0[(11084U)>>2]);
    float4 _S3463 = float4(_S3459, _S3460, _S3461, _S3462);
    float _S3464 = as_type<float>((&kernelContext_0)->frameParameters_0[(11088U)>>2]);
    float _S3465 = as_type<float>((&kernelContext_0)->frameParameters_0[(11092U)>>2]);
    float _S3466 = as_type<float>((&kernelContext_0)->frameParameters_0[(11096U)>>2]);
    float _S3467 = as_type<float>((&kernelContext_0)->frameParameters_0[(11100U)>>2]);
    float4 _S3468 = float4(_S3464, _S3465, _S3466, _S3467);
    float _S3469 = as_type<float>((&kernelContext_0)->frameParameters_0[(11104U)>>2]);
    float _S3470 = as_type<float>((&kernelContext_0)->frameParameters_0[(11108U)>>2]);
    float _S3471 = as_type<float>((&kernelContext_0)->frameParameters_0[(11112U)>>2]);
    float _S3472 = as_type<float>((&kernelContext_0)->frameParameters_0[(11116U)>>2]);
    float4 _S3473 = float4(_S3469, _S3470, _S3471, _S3472);
    float _S3474 = as_type<float>((&kernelContext_0)->frameParameters_0[(11120U)>>2]);
    float _S3475 = as_type<float>((&kernelContext_0)->frameParameters_0[(11124U)>>2]);
    float _S3476 = as_type<float>((&kernelContext_0)->frameParameters_0[(11128U)>>2]);
    float _S3477 = as_type<float>((&kernelContext_0)->frameParameters_0[(11132U)>>2]);
    float4 _S3478 = float4(_S3474, _S3475, _S3476, _S3477);
    float _S3479 = as_type<float>((&kernelContext_0)->frameParameters_0[(11136U)>>2]);
    float _S3480 = as_type<float>((&kernelContext_0)->frameParameters_0[(11140U)>>2]);
    float _S3481 = as_type<float>((&kernelContext_0)->frameParameters_0[(11144U)>>2]);
    float _S3482 = as_type<float>((&kernelContext_0)->frameParameters_0[(11148U)>>2]);
    float4 _S3483 = float4(_S3479, _S3480, _S3481, _S3482);
    float _S3484 = as_type<float>((&kernelContext_0)->frameParameters_0[(11152U)>>2]);
    float _S3485 = as_type<float>((&kernelContext_0)->frameParameters_0[(11156U)>>2]);
    float _S3486 = as_type<float>((&kernelContext_0)->frameParameters_0[(11160U)>>2]);
    float _S3487 = as_type<float>((&kernelContext_0)->frameParameters_0[(11164U)>>2]);
    float4 _S3488 = float4(_S3484, _S3485, _S3486, _S3487);
    float _S3489 = as_type<float>((&kernelContext_0)->frameParameters_0[(11168U)>>2]);
    float _S3490 = as_type<float>((&kernelContext_0)->frameParameters_0[(11172U)>>2]);
    float _S3491 = as_type<float>((&kernelContext_0)->frameParameters_0[(11176U)>>2]);
    float _S3492 = as_type<float>((&kernelContext_0)->frameParameters_0[(11180U)>>2]);
    float4 _S3493 = float4(_S3489, _S3490, _S3491, _S3492);
    float _S3494 = as_type<float>((&kernelContext_0)->frameParameters_0[(11184U)>>2]);
    float _S3495 = as_type<float>((&kernelContext_0)->frameParameters_0[(11188U)>>2]);
    float _S3496 = as_type<float>((&kernelContext_0)->frameParameters_0[(11192U)>>2]);
    float _S3497 = as_type<float>((&kernelContext_0)->frameParameters_0[(11196U)>>2]);
    float4 _S3498 = float4(_S3494, _S3495, _S3496, _S3497);
    float _S3499 = as_type<float>((&kernelContext_0)->frameParameters_0[(11200U)>>2]);
    float _S3500 = as_type<float>((&kernelContext_0)->frameParameters_0[(11204U)>>2]);
    float _S3501 = as_type<float>((&kernelContext_0)->frameParameters_0[(11208U)>>2]);
    float _S3502 = as_type<float>((&kernelContext_0)->frameParameters_0[(11212U)>>2]);
    float4 _S3503 = float4(_S3499, _S3500, _S3501, _S3502);
    float _S3504 = as_type<float>((&kernelContext_0)->frameParameters_0[(11216U)>>2]);
    float _S3505 = as_type<float>((&kernelContext_0)->frameParameters_0[(11220U)>>2]);
    float _S3506 = as_type<float>((&kernelContext_0)->frameParameters_0[(11224U)>>2]);
    float _S3507 = as_type<float>((&kernelContext_0)->frameParameters_0[(11228U)>>2]);
    float4 _S3508 = float4(_S3504, _S3505, _S3506, _S3507);
    float _S3509 = as_type<float>((&kernelContext_0)->frameParameters_0[(11232U)>>2]);
    float _S3510 = as_type<float>((&kernelContext_0)->frameParameters_0[(11236U)>>2]);
    float _S3511 = as_type<float>((&kernelContext_0)->frameParameters_0[(11240U)>>2]);
    float _S3512 = as_type<float>((&kernelContext_0)->frameParameters_0[(11244U)>>2]);
    float4 _S3513 = float4(_S3509, _S3510, _S3511, _S3512);
    float _S3514 = as_type<float>((&kernelContext_0)->frameParameters_0[(11248U)>>2]);
    float _S3515 = as_type<float>((&kernelContext_0)->frameParameters_0[(11252U)>>2]);
    float _S3516 = as_type<float>((&kernelContext_0)->frameParameters_0[(11256U)>>2]);
    float _S3517 = as_type<float>((&kernelContext_0)->frameParameters_0[(11260U)>>2]);
    float4 _S3518 = float4(_S3514, _S3515, _S3516, _S3517);
    float _S3519 = as_type<float>((&kernelContext_0)->frameParameters_0[(11264U)>>2]);
    float _S3520 = as_type<float>((&kernelContext_0)->frameParameters_0[(11268U)>>2]);
    float _S3521 = as_type<float>((&kernelContext_0)->frameParameters_0[(11272U)>>2]);
    float _S3522 = as_type<float>((&kernelContext_0)->frameParameters_0[(11276U)>>2]);
    float4 _S3523 = float4(_S3519, _S3520, _S3521, _S3522);
    float _S3524 = as_type<float>((&kernelContext_0)->frameParameters_0[(11280U)>>2]);
    float _S3525 = as_type<float>((&kernelContext_0)->frameParameters_0[(11284U)>>2]);
    float _S3526 = as_type<float>((&kernelContext_0)->frameParameters_0[(11288U)>>2]);
    float _S3527 = as_type<float>((&kernelContext_0)->frameParameters_0[(11292U)>>2]);
    float4 _S3528 = float4(_S3524, _S3525, _S3526, _S3527);
    float _S3529 = as_type<float>((&kernelContext_0)->frameParameters_0[(11296U)>>2]);
    float _S3530 = as_type<float>((&kernelContext_0)->frameParameters_0[(11300U)>>2]);
    float _S3531 = as_type<float>((&kernelContext_0)->frameParameters_0[(11304U)>>2]);
    float _S3532 = as_type<float>((&kernelContext_0)->frameParameters_0[(11308U)>>2]);
    float4 _S3533 = float4(_S3529, _S3530, _S3531, _S3532);
    float _S3534 = as_type<float>((&kernelContext_0)->frameParameters_0[(11312U)>>2]);
    float _S3535 = as_type<float>((&kernelContext_0)->frameParameters_0[(11316U)>>2]);
    float _S3536 = as_type<float>((&kernelContext_0)->frameParameters_0[(11320U)>>2]);
    float _S3537 = as_type<float>((&kernelContext_0)->frameParameters_0[(11324U)>>2]);
    float4 _S3538 = float4(_S3534, _S3535, _S3536, _S3537);
    float _S3539 = as_type<float>((&kernelContext_0)->frameParameters_0[(11328U)>>2]);
    float _S3540 = as_type<float>((&kernelContext_0)->frameParameters_0[(11332U)>>2]);
    float _S3541 = as_type<float>((&kernelContext_0)->frameParameters_0[(11336U)>>2]);
    float _S3542 = as_type<float>((&kernelContext_0)->frameParameters_0[(11340U)>>2]);
    float4 _S3543 = float4(_S3539, _S3540, _S3541, _S3542);
    float _S3544 = as_type<float>((&kernelContext_0)->frameParameters_0[(11344U)>>2]);
    float _S3545 = as_type<float>((&kernelContext_0)->frameParameters_0[(11348U)>>2]);
    float _S3546 = as_type<float>((&kernelContext_0)->frameParameters_0[(11352U)>>2]);
    float _S3547 = as_type<float>((&kernelContext_0)->frameParameters_0[(11356U)>>2]);
    float4 _S3548 = float4(_S3544, _S3545, _S3546, _S3547);
    float _S3549 = as_type<float>((&kernelContext_0)->frameParameters_0[(11360U)>>2]);
    float _S3550 = as_type<float>((&kernelContext_0)->frameParameters_0[(11364U)>>2]);
    float _S3551 = as_type<float>((&kernelContext_0)->frameParameters_0[(11368U)>>2]);
    float _S3552 = as_type<float>((&kernelContext_0)->frameParameters_0[(11372U)>>2]);
    float4 _S3553 = float4(_S3549, _S3550, _S3551, _S3552);
    float _S3554 = as_type<float>((&kernelContext_0)->frameParameters_0[(11376U)>>2]);
    float _S3555 = as_type<float>((&kernelContext_0)->frameParameters_0[(11380U)>>2]);
    float _S3556 = as_type<float>((&kernelContext_0)->frameParameters_0[(11384U)>>2]);
    float _S3557 = as_type<float>((&kernelContext_0)->frameParameters_0[(11388U)>>2]);
    float4 _S3558 = float4(_S3554, _S3555, _S3556, _S3557);
    float _S3559 = as_type<float>((&kernelContext_0)->frameParameters_0[(11392U)>>2]);
    float _S3560 = as_type<float>((&kernelContext_0)->frameParameters_0[(11396U)>>2]);
    float _S3561 = as_type<float>((&kernelContext_0)->frameParameters_0[(11400U)>>2]);
    float _S3562 = as_type<float>((&kernelContext_0)->frameParameters_0[(11404U)>>2]);
    float4 _S3563 = float4(_S3559, _S3560, _S3561, _S3562);
    float _S3564 = as_type<float>((&kernelContext_0)->frameParameters_0[(11408U)>>2]);
    float _S3565 = as_type<float>((&kernelContext_0)->frameParameters_0[(11412U)>>2]);
    float _S3566 = as_type<float>((&kernelContext_0)->frameParameters_0[(11416U)>>2]);
    float _S3567 = as_type<float>((&kernelContext_0)->frameParameters_0[(11420U)>>2]);
    float4 _S3568 = float4(_S3564, _S3565, _S3566, _S3567);
    float _S3569 = as_type<float>((&kernelContext_0)->frameParameters_0[(11424U)>>2]);
    float _S3570 = as_type<float>((&kernelContext_0)->frameParameters_0[(11428U)>>2]);
    float _S3571 = as_type<float>((&kernelContext_0)->frameParameters_0[(11432U)>>2]);
    float _S3572 = as_type<float>((&kernelContext_0)->frameParameters_0[(11436U)>>2]);
    float4 _S3573 = float4(_S3569, _S3570, _S3571, _S3572);
    float _S3574 = as_type<float>((&kernelContext_0)->frameParameters_0[(11440U)>>2]);
    float _S3575 = as_type<float>((&kernelContext_0)->frameParameters_0[(11444U)>>2]);
    float _S3576 = as_type<float>((&kernelContext_0)->frameParameters_0[(11448U)>>2]);
    float _S3577 = as_type<float>((&kernelContext_0)->frameParameters_0[(11452U)>>2]);
    float4 _S3578 = float4(_S3574, _S3575, _S3576, _S3577);
    float _S3579 = as_type<float>((&kernelContext_0)->frameParameters_0[(11456U)>>2]);
    float _S3580 = as_type<float>((&kernelContext_0)->frameParameters_0[(11460U)>>2]);
    float _S3581 = as_type<float>((&kernelContext_0)->frameParameters_0[(11464U)>>2]);
    float _S3582 = as_type<float>((&kernelContext_0)->frameParameters_0[(11468U)>>2]);
    float4 _S3583 = float4(_S3579, _S3580, _S3581, _S3582);
    float _S3584 = as_type<float>((&kernelContext_0)->frameParameters_0[(11472U)>>2]);
    float _S3585 = as_type<float>((&kernelContext_0)->frameParameters_0[(11476U)>>2]);
    float _S3586 = as_type<float>((&kernelContext_0)->frameParameters_0[(11480U)>>2]);
    float _S3587 = as_type<float>((&kernelContext_0)->frameParameters_0[(11484U)>>2]);
    float4 _S3588 = float4(_S3584, _S3585, _S3586, _S3587);
    float _S3589 = as_type<float>((&kernelContext_0)->frameParameters_0[(11488U)>>2]);
    float _S3590 = as_type<float>((&kernelContext_0)->frameParameters_0[(11492U)>>2]);
    float _S3591 = as_type<float>((&kernelContext_0)->frameParameters_0[(11496U)>>2]);
    float _S3592 = as_type<float>((&kernelContext_0)->frameParameters_0[(11500U)>>2]);
    float4 _S3593 = float4(_S3589, _S3590, _S3591, _S3592);
    float _S3594 = as_type<float>((&kernelContext_0)->frameParameters_0[(11504U)>>2]);
    float _S3595 = as_type<float>((&kernelContext_0)->frameParameters_0[(11508U)>>2]);
    float _S3596 = as_type<float>((&kernelContext_0)->frameParameters_0[(11512U)>>2]);
    float _S3597 = as_type<float>((&kernelContext_0)->frameParameters_0[(11516U)>>2]);
    float4 _S3598 = float4(_S3594, _S3595, _S3596, _S3597);
    float _S3599 = as_type<float>((&kernelContext_0)->frameParameters_0[(11520U)>>2]);
    float _S3600 = as_type<float>((&kernelContext_0)->frameParameters_0[(11524U)>>2]);
    float _S3601 = as_type<float>((&kernelContext_0)->frameParameters_0[(11528U)>>2]);
    float _S3602 = as_type<float>((&kernelContext_0)->frameParameters_0[(11532U)>>2]);
    float4 _S3603 = float4(_S3599, _S3600, _S3601, _S3602);
    float _S3604 = as_type<float>((&kernelContext_0)->frameParameters_0[(11536U)>>2]);
    float _S3605 = as_type<float>((&kernelContext_0)->frameParameters_0[(11540U)>>2]);
    float _S3606 = as_type<float>((&kernelContext_0)->frameParameters_0[(11544U)>>2]);
    float _S3607 = as_type<float>((&kernelContext_0)->frameParameters_0[(11548U)>>2]);
    float4 _S3608 = float4(_S3604, _S3605, _S3606, _S3607);
    float _S3609 = as_type<float>((&kernelContext_0)->frameParameters_0[(11552U)>>2]);
    float _S3610 = as_type<float>((&kernelContext_0)->frameParameters_0[(11556U)>>2]);
    float _S3611 = as_type<float>((&kernelContext_0)->frameParameters_0[(11560U)>>2]);
    float _S3612 = as_type<float>((&kernelContext_0)->frameParameters_0[(11564U)>>2]);
    float4 _S3613 = float4(_S3609, _S3610, _S3611, _S3612);
    float _S3614 = as_type<float>((&kernelContext_0)->frameParameters_0[(11568U)>>2]);
    float _S3615 = as_type<float>((&kernelContext_0)->frameParameters_0[(11572U)>>2]);
    float _S3616 = as_type<float>((&kernelContext_0)->frameParameters_0[(11576U)>>2]);
    float _S3617 = as_type<float>((&kernelContext_0)->frameParameters_0[(11580U)>>2]);
    float4 _S3618 = float4(_S3614, _S3615, _S3616, _S3617);
    float _S3619 = as_type<float>((&kernelContext_0)->frameParameters_0[(11584U)>>2]);
    float _S3620 = as_type<float>((&kernelContext_0)->frameParameters_0[(11588U)>>2]);
    float _S3621 = as_type<float>((&kernelContext_0)->frameParameters_0[(11592U)>>2]);
    float _S3622 = as_type<float>((&kernelContext_0)->frameParameters_0[(11596U)>>2]);
    float4 _S3623 = float4(_S3619, _S3620, _S3621, _S3622);
    float _S3624 = as_type<float>((&kernelContext_0)->frameParameters_0[(11600U)>>2]);
    float _S3625 = as_type<float>((&kernelContext_0)->frameParameters_0[(11604U)>>2]);
    float _S3626 = as_type<float>((&kernelContext_0)->frameParameters_0[(11608U)>>2]);
    float _S3627 = as_type<float>((&kernelContext_0)->frameParameters_0[(11612U)>>2]);
    float4 _S3628 = float4(_S3624, _S3625, _S3626, _S3627);
    float _S3629 = as_type<float>((&kernelContext_0)->frameParameters_0[(11616U)>>2]);
    float _S3630 = as_type<float>((&kernelContext_0)->frameParameters_0[(11620U)>>2]);
    float _S3631 = as_type<float>((&kernelContext_0)->frameParameters_0[(11624U)>>2]);
    float _S3632 = as_type<float>((&kernelContext_0)->frameParameters_0[(11628U)>>2]);
    float4 _S3633 = float4(_S3629, _S3630, _S3631, _S3632);
    float _S3634 = as_type<float>((&kernelContext_0)->frameParameters_0[(11632U)>>2]);
    float _S3635 = as_type<float>((&kernelContext_0)->frameParameters_0[(11636U)>>2]);
    float _S3636 = as_type<float>((&kernelContext_0)->frameParameters_0[(11640U)>>2]);
    float _S3637 = as_type<float>((&kernelContext_0)->frameParameters_0[(11644U)>>2]);
    float4 _S3638 = float4(_S3634, _S3635, _S3636, _S3637);
    float _S3639 = as_type<float>((&kernelContext_0)->frameParameters_0[(11648U)>>2]);
    float _S3640 = as_type<float>((&kernelContext_0)->frameParameters_0[(11652U)>>2]);
    float _S3641 = as_type<float>((&kernelContext_0)->frameParameters_0[(11656U)>>2]);
    float _S3642 = as_type<float>((&kernelContext_0)->frameParameters_0[(11660U)>>2]);
    float4 _S3643 = float4(_S3639, _S3640, _S3641, _S3642);
    float _S3644 = as_type<float>((&kernelContext_0)->frameParameters_0[(11664U)>>2]);
    float _S3645 = as_type<float>((&kernelContext_0)->frameParameters_0[(11668U)>>2]);
    float _S3646 = as_type<float>((&kernelContext_0)->frameParameters_0[(11672U)>>2]);
    float _S3647 = as_type<float>((&kernelContext_0)->frameParameters_0[(11676U)>>2]);
    float4 _S3648 = float4(_S3644, _S3645, _S3646, _S3647);
    float _S3649 = as_type<float>((&kernelContext_0)->frameParameters_0[(11680U)>>2]);
    float _S3650 = as_type<float>((&kernelContext_0)->frameParameters_0[(11684U)>>2]);
    float _S3651 = as_type<float>((&kernelContext_0)->frameParameters_0[(11688U)>>2]);
    float _S3652 = as_type<float>((&kernelContext_0)->frameParameters_0[(11692U)>>2]);
    float4 _S3653 = float4(_S3649, _S3650, _S3651, _S3652);
    float _S3654 = as_type<float>((&kernelContext_0)->frameParameters_0[(11696U)>>2]);
    float _S3655 = as_type<float>((&kernelContext_0)->frameParameters_0[(11700U)>>2]);
    float _S3656 = as_type<float>((&kernelContext_0)->frameParameters_0[(11704U)>>2]);
    float _S3657 = as_type<float>((&kernelContext_0)->frameParameters_0[(11708U)>>2]);
    float4 _S3658 = float4(_S3654, _S3655, _S3656, _S3657);
    float _S3659 = as_type<float>((&kernelContext_0)->frameParameters_0[(11712U)>>2]);
    float _S3660 = as_type<float>((&kernelContext_0)->frameParameters_0[(11716U)>>2]);
    float _S3661 = as_type<float>((&kernelContext_0)->frameParameters_0[(11720U)>>2]);
    float _S3662 = as_type<float>((&kernelContext_0)->frameParameters_0[(11724U)>>2]);
    float4 _S3663 = float4(_S3659, _S3660, _S3661, _S3662);
    float _S3664 = as_type<float>((&kernelContext_0)->frameParameters_0[(11728U)>>2]);
    float _S3665 = as_type<float>((&kernelContext_0)->frameParameters_0[(11732U)>>2]);
    float _S3666 = as_type<float>((&kernelContext_0)->frameParameters_0[(11736U)>>2]);
    float _S3667 = as_type<float>((&kernelContext_0)->frameParameters_0[(11740U)>>2]);
    float4 _S3668 = float4(_S3664, _S3665, _S3666, _S3667);
    float _S3669 = as_type<float>((&kernelContext_0)->frameParameters_0[(11744U)>>2]);
    float _S3670 = as_type<float>((&kernelContext_0)->frameParameters_0[(11748U)>>2]);
    float _S3671 = as_type<float>((&kernelContext_0)->frameParameters_0[(11752U)>>2]);
    float _S3672 = as_type<float>((&kernelContext_0)->frameParameters_0[(11756U)>>2]);
    float4 _S3673 = float4(_S3669, _S3670, _S3671, _S3672);
    float _S3674 = as_type<float>((&kernelContext_0)->frameParameters_0[(11760U)>>2]);
    float _S3675 = as_type<float>((&kernelContext_0)->frameParameters_0[(11764U)>>2]);
    float _S3676 = as_type<float>((&kernelContext_0)->frameParameters_0[(11768U)>>2]);
    float _S3677 = as_type<float>((&kernelContext_0)->frameParameters_0[(11772U)>>2]);
    float4 _S3678 = float4(_S3674, _S3675, _S3676, _S3677);
    float _S3679 = as_type<float>((&kernelContext_0)->frameParameters_0[(11776U)>>2]);
    float _S3680 = as_type<float>((&kernelContext_0)->frameParameters_0[(11780U)>>2]);
    float _S3681 = as_type<float>((&kernelContext_0)->frameParameters_0[(11784U)>>2]);
    float _S3682 = as_type<float>((&kernelContext_0)->frameParameters_0[(11788U)>>2]);
    float4 _S3683 = float4(_S3679, _S3680, _S3681, _S3682);
    float _S3684 = as_type<float>((&kernelContext_0)->frameParameters_0[(11792U)>>2]);
    float _S3685 = as_type<float>((&kernelContext_0)->frameParameters_0[(11796U)>>2]);
    float _S3686 = as_type<float>((&kernelContext_0)->frameParameters_0[(11800U)>>2]);
    float _S3687 = as_type<float>((&kernelContext_0)->frameParameters_0[(11804U)>>2]);
    float4 _S3688 = float4(_S3684, _S3685, _S3686, _S3687);
    float _S3689 = as_type<float>((&kernelContext_0)->frameParameters_0[(11808U)>>2]);
    float _S3690 = as_type<float>((&kernelContext_0)->frameParameters_0[(11812U)>>2]);
    float _S3691 = as_type<float>((&kernelContext_0)->frameParameters_0[(11816U)>>2]);
    float _S3692 = as_type<float>((&kernelContext_0)->frameParameters_0[(11820U)>>2]);
    float4 _S3693 = float4(_S3689, _S3690, _S3691, _S3692);
    float _S3694 = as_type<float>((&kernelContext_0)->frameParameters_0[(11824U)>>2]);
    float _S3695 = as_type<float>((&kernelContext_0)->frameParameters_0[(11828U)>>2]);
    float _S3696 = as_type<float>((&kernelContext_0)->frameParameters_0[(11832U)>>2]);
    float _S3697 = as_type<float>((&kernelContext_0)->frameParameters_0[(11836U)>>2]);
    float4 _S3698 = float4(_S3694, _S3695, _S3696, _S3697);
    float _S3699 = as_type<float>((&kernelContext_0)->frameParameters_0[(11840U)>>2]);
    float _S3700 = as_type<float>((&kernelContext_0)->frameParameters_0[(11844U)>>2]);
    float _S3701 = as_type<float>((&kernelContext_0)->frameParameters_0[(11848U)>>2]);
    float _S3702 = as_type<float>((&kernelContext_0)->frameParameters_0[(11852U)>>2]);
    float4 _S3703 = float4(_S3699, _S3700, _S3701, _S3702);
    float _S3704 = as_type<float>((&kernelContext_0)->frameParameters_0[(11856U)>>2]);
    float _S3705 = as_type<float>((&kernelContext_0)->frameParameters_0[(11860U)>>2]);
    float _S3706 = as_type<float>((&kernelContext_0)->frameParameters_0[(11864U)>>2]);
    float _S3707 = as_type<float>((&kernelContext_0)->frameParameters_0[(11868U)>>2]);
    float4 _S3708 = float4(_S3704, _S3705, _S3706, _S3707);
    float _S3709 = as_type<float>((&kernelContext_0)->frameParameters_0[(11872U)>>2]);
    float _S3710 = as_type<float>((&kernelContext_0)->frameParameters_0[(11876U)>>2]);
    float _S3711 = as_type<float>((&kernelContext_0)->frameParameters_0[(11880U)>>2]);
    float _S3712 = as_type<float>((&kernelContext_0)->frameParameters_0[(11884U)>>2]);
    float4 _S3713 = float4(_S3709, _S3710, _S3711, _S3712);
    float _S3714 = as_type<float>((&kernelContext_0)->frameParameters_0[(11888U)>>2]);
    float _S3715 = as_type<float>((&kernelContext_0)->frameParameters_0[(11892U)>>2]);
    float _S3716 = as_type<float>((&kernelContext_0)->frameParameters_0[(11896U)>>2]);
    float _S3717 = as_type<float>((&kernelContext_0)->frameParameters_0[(11900U)>>2]);
    float4 _S3718 = float4(_S3714, _S3715, _S3716, _S3717);
    float _S3719 = as_type<float>((&kernelContext_0)->frameParameters_0[(11904U)>>2]);
    float _S3720 = as_type<float>((&kernelContext_0)->frameParameters_0[(11908U)>>2]);
    float _S3721 = as_type<float>((&kernelContext_0)->frameParameters_0[(11912U)>>2]);
    float _S3722 = as_type<float>((&kernelContext_0)->frameParameters_0[(11916U)>>2]);
    float4 _S3723 = float4(_S3719, _S3720, _S3721, _S3722);
    float _S3724 = as_type<float>((&kernelContext_0)->frameParameters_0[(11920U)>>2]);
    float _S3725 = as_type<float>((&kernelContext_0)->frameParameters_0[(11924U)>>2]);
    float _S3726 = as_type<float>((&kernelContext_0)->frameParameters_0[(11928U)>>2]);
    float _S3727 = as_type<float>((&kernelContext_0)->frameParameters_0[(11932U)>>2]);
    float4 _S3728 = float4(_S3724, _S3725, _S3726, _S3727);
    float _S3729 = as_type<float>((&kernelContext_0)->frameParameters_0[(11936U)>>2]);
    float _S3730 = as_type<float>((&kernelContext_0)->frameParameters_0[(11940U)>>2]);
    float _S3731 = as_type<float>((&kernelContext_0)->frameParameters_0[(11944U)>>2]);
    float _S3732 = as_type<float>((&kernelContext_0)->frameParameters_0[(11948U)>>2]);
    float4 _S3733 = float4(_S3729, _S3730, _S3731, _S3732);
    float _S3734 = as_type<float>((&kernelContext_0)->frameParameters_0[(11952U)>>2]);
    float _S3735 = as_type<float>((&kernelContext_0)->frameParameters_0[(11956U)>>2]);
    float _S3736 = as_type<float>((&kernelContext_0)->frameParameters_0[(11960U)>>2]);
    float _S3737 = as_type<float>((&kernelContext_0)->frameParameters_0[(11964U)>>2]);
    float4 _S3738 = float4(_S3734, _S3735, _S3736, _S3737);
    float _S3739 = as_type<float>((&kernelContext_0)->frameParameters_0[(11968U)>>2]);
    float _S3740 = as_type<float>((&kernelContext_0)->frameParameters_0[(11972U)>>2]);
    float _S3741 = as_type<float>((&kernelContext_0)->frameParameters_0[(11976U)>>2]);
    float _S3742 = as_type<float>((&kernelContext_0)->frameParameters_0[(11980U)>>2]);
    float4 _S3743 = float4(_S3739, _S3740, _S3741, _S3742);
    float _S3744 = as_type<float>((&kernelContext_0)->frameParameters_0[(11984U)>>2]);
    float _S3745 = as_type<float>((&kernelContext_0)->frameParameters_0[(11988U)>>2]);
    float _S3746 = as_type<float>((&kernelContext_0)->frameParameters_0[(11992U)>>2]);
    float _S3747 = as_type<float>((&kernelContext_0)->frameParameters_0[(11996U)>>2]);
    float4 _S3748 = float4(_S3744, _S3745, _S3746, _S3747);
    float _S3749 = as_type<float>((&kernelContext_0)->frameParameters_0[(12000U)>>2]);
    float _S3750 = as_type<float>((&kernelContext_0)->frameParameters_0[(12004U)>>2]);
    float _S3751 = as_type<float>((&kernelContext_0)->frameParameters_0[(12008U)>>2]);
    float _S3752 = as_type<float>((&kernelContext_0)->frameParameters_0[(12012U)>>2]);
    float4 _S3753 = float4(_S3749, _S3750, _S3751, _S3752);
    float _S3754 = as_type<float>((&kernelContext_0)->frameParameters_0[(12016U)>>2]);
    float _S3755 = as_type<float>((&kernelContext_0)->frameParameters_0[(12020U)>>2]);
    float _S3756 = as_type<float>((&kernelContext_0)->frameParameters_0[(12024U)>>2]);
    float _S3757 = as_type<float>((&kernelContext_0)->frameParameters_0[(12028U)>>2]);
    float4 _S3758 = float4(_S3754, _S3755, _S3756, _S3757);
    float _S3759 = as_type<float>((&kernelContext_0)->frameParameters_0[(12032U)>>2]);
    float _S3760 = as_type<float>((&kernelContext_0)->frameParameters_0[(12036U)>>2]);
    float _S3761 = as_type<float>((&kernelContext_0)->frameParameters_0[(12040U)>>2]);
    float _S3762 = as_type<float>((&kernelContext_0)->frameParameters_0[(12044U)>>2]);
    float4 _S3763 = float4(_S3759, _S3760, _S3761, _S3762);
    float _S3764 = as_type<float>((&kernelContext_0)->frameParameters_0[(12048U)>>2]);
    float _S3765 = as_type<float>((&kernelContext_0)->frameParameters_0[(12052U)>>2]);
    float _S3766 = as_type<float>((&kernelContext_0)->frameParameters_0[(12056U)>>2]);
    float _S3767 = as_type<float>((&kernelContext_0)->frameParameters_0[(12060U)>>2]);
    float4 _S3768 = float4(_S3764, _S3765, _S3766, _S3767);
    float _S3769 = as_type<float>((&kernelContext_0)->frameParameters_0[(12064U)>>2]);
    float _S3770 = as_type<float>((&kernelContext_0)->frameParameters_0[(12068U)>>2]);
    float _S3771 = as_type<float>((&kernelContext_0)->frameParameters_0[(12072U)>>2]);
    float _S3772 = as_type<float>((&kernelContext_0)->frameParameters_0[(12076U)>>2]);
    float4 _S3773 = float4(_S3769, _S3770, _S3771, _S3772);
    float _S3774 = as_type<float>((&kernelContext_0)->frameParameters_0[(12080U)>>2]);
    float _S3775 = as_type<float>((&kernelContext_0)->frameParameters_0[(12084U)>>2]);
    float _S3776 = as_type<float>((&kernelContext_0)->frameParameters_0[(12088U)>>2]);
    float _S3777 = as_type<float>((&kernelContext_0)->frameParameters_0[(12092U)>>2]);
    float4 _S3778 = float4(_S3774, _S3775, _S3776, _S3777);
    float _S3779 = as_type<float>((&kernelContext_0)->frameParameters_0[(12096U)>>2]);
    float _S3780 = as_type<float>((&kernelContext_0)->frameParameters_0[(12100U)>>2]);
    float _S3781 = as_type<float>((&kernelContext_0)->frameParameters_0[(12104U)>>2]);
    float _S3782 = as_type<float>((&kernelContext_0)->frameParameters_0[(12108U)>>2]);
    float4 _S3783 = float4(_S3779, _S3780, _S3781, _S3782);
    float _S3784 = as_type<float>((&kernelContext_0)->frameParameters_0[(12112U)>>2]);
    float _S3785 = as_type<float>((&kernelContext_0)->frameParameters_0[(12116U)>>2]);
    float _S3786 = as_type<float>((&kernelContext_0)->frameParameters_0[(12120U)>>2]);
    float _S3787 = as_type<float>((&kernelContext_0)->frameParameters_0[(12124U)>>2]);
    float4 _S3788 = float4(_S3784, _S3785, _S3786, _S3787);
    float _S3789 = as_type<float>((&kernelContext_0)->frameParameters_0[(12128U)>>2]);
    float _S3790 = as_type<float>((&kernelContext_0)->frameParameters_0[(12132U)>>2]);
    float _S3791 = as_type<float>((&kernelContext_0)->frameParameters_0[(12136U)>>2]);
    float _S3792 = as_type<float>((&kernelContext_0)->frameParameters_0[(12140U)>>2]);
    float4 _S3793 = float4(_S3789, _S3790, _S3791, _S3792);
    float _S3794 = as_type<float>((&kernelContext_0)->frameParameters_0[(12144U)>>2]);
    float _S3795 = as_type<float>((&kernelContext_0)->frameParameters_0[(12148U)>>2]);
    float _S3796 = as_type<float>((&kernelContext_0)->frameParameters_0[(12152U)>>2]);
    float _S3797 = as_type<float>((&kernelContext_0)->frameParameters_0[(12156U)>>2]);
    float4 _S3798 = float4(_S3794, _S3795, _S3796, _S3797);
    float _S3799 = as_type<float>((&kernelContext_0)->frameParameters_0[(12160U)>>2]);
    float _S3800 = as_type<float>((&kernelContext_0)->frameParameters_0[(12164U)>>2]);
    float _S3801 = as_type<float>((&kernelContext_0)->frameParameters_0[(12168U)>>2]);
    float _S3802 = as_type<float>((&kernelContext_0)->frameParameters_0[(12172U)>>2]);
    float4 _S3803 = float4(_S3799, _S3800, _S3801, _S3802);
    float _S3804 = as_type<float>((&kernelContext_0)->frameParameters_0[(12176U)>>2]);
    float _S3805 = as_type<float>((&kernelContext_0)->frameParameters_0[(12180U)>>2]);
    float _S3806 = as_type<float>((&kernelContext_0)->frameParameters_0[(12184U)>>2]);
    float _S3807 = as_type<float>((&kernelContext_0)->frameParameters_0[(12188U)>>2]);
    float4 _S3808 = float4(_S3804, _S3805, _S3806, _S3807);
    float _S3809 = as_type<float>((&kernelContext_0)->frameParameters_0[(12192U)>>2]);
    float _S3810 = as_type<float>((&kernelContext_0)->frameParameters_0[(12196U)>>2]);
    float _S3811 = as_type<float>((&kernelContext_0)->frameParameters_0[(12200U)>>2]);
    float _S3812 = as_type<float>((&kernelContext_0)->frameParameters_0[(12204U)>>2]);
    float4 _S3813 = float4(_S3809, _S3810, _S3811, _S3812);
    float _S3814 = as_type<float>((&kernelContext_0)->frameParameters_0[(12208U)>>2]);
    float _S3815 = as_type<float>((&kernelContext_0)->frameParameters_0[(12212U)>>2]);
    float _S3816 = as_type<float>((&kernelContext_0)->frameParameters_0[(12216U)>>2]);
    float _S3817 = as_type<float>((&kernelContext_0)->frameParameters_0[(12220U)>>2]);
    float4 _S3818 = float4(_S3814, _S3815, _S3816, _S3817);
    float _S3819 = as_type<float>((&kernelContext_0)->frameParameters_0[(12224U)>>2]);
    float _S3820 = as_type<float>((&kernelContext_0)->frameParameters_0[(12228U)>>2]);
    float _S3821 = as_type<float>((&kernelContext_0)->frameParameters_0[(12232U)>>2]);
    float _S3822 = as_type<float>((&kernelContext_0)->frameParameters_0[(12236U)>>2]);
    float4 _S3823 = float4(_S3819, _S3820, _S3821, _S3822);
    float _S3824 = as_type<float>((&kernelContext_0)->frameParameters_0[(12240U)>>2]);
    float _S3825 = as_type<float>((&kernelContext_0)->frameParameters_0[(12244U)>>2]);
    float _S3826 = as_type<float>((&kernelContext_0)->frameParameters_0[(12248U)>>2]);
    float _S3827 = as_type<float>((&kernelContext_0)->frameParameters_0[(12252U)>>2]);
    float4 _S3828 = float4(_S3824, _S3825, _S3826, _S3827);
    float _S3829 = as_type<float>((&kernelContext_0)->frameParameters_0[(12256U)>>2]);
    float _S3830 = as_type<float>((&kernelContext_0)->frameParameters_0[(12260U)>>2]);
    float _S3831 = as_type<float>((&kernelContext_0)->frameParameters_0[(12264U)>>2]);
    float _S3832 = as_type<float>((&kernelContext_0)->frameParameters_0[(12268U)>>2]);
    float4 _S3833 = float4(_S3829, _S3830, _S3831, _S3832);
    float _S3834 = as_type<float>((&kernelContext_0)->frameParameters_0[(12272U)>>2]);
    float _S3835 = as_type<float>((&kernelContext_0)->frameParameters_0[(12276U)>>2]);
    float _S3836 = as_type<float>((&kernelContext_0)->frameParameters_0[(12280U)>>2]);
    float _S3837 = as_type<float>((&kernelContext_0)->frameParameters_0[(12284U)>>2]);
    float4 _S3838 = float4(_S3834, _S3835, _S3836, _S3837);
    float _S3839 = as_type<float>((&kernelContext_0)->frameParameters_0[(12288U)>>2]);
    float _S3840 = as_type<float>((&kernelContext_0)->frameParameters_0[(12292U)>>2]);
    float _S3841 = as_type<float>((&kernelContext_0)->frameParameters_0[(12296U)>>2]);
    float _S3842 = as_type<float>((&kernelContext_0)->frameParameters_0[(12300U)>>2]);
    float4 _S3843 = float4(_S3839, _S3840, _S3841, _S3842);
    float _S3844 = as_type<float>((&kernelContext_0)->frameParameters_0[(12304U)>>2]);
    float _S3845 = as_type<float>((&kernelContext_0)->frameParameters_0[(12308U)>>2]);
    float _S3846 = as_type<float>((&kernelContext_0)->frameParameters_0[(12312U)>>2]);
    float _S3847 = as_type<float>((&kernelContext_0)->frameParameters_0[(12316U)>>2]);
    float4 _S3848 = float4(_S3844, _S3845, _S3846, _S3847);
    float _S3849 = as_type<float>((&kernelContext_0)->frameParameters_0[(12320U)>>2]);
    float _S3850 = as_type<float>((&kernelContext_0)->frameParameters_0[(12324U)>>2]);
    float _S3851 = as_type<float>((&kernelContext_0)->frameParameters_0[(12328U)>>2]);
    float _S3852 = as_type<float>((&kernelContext_0)->frameParameters_0[(12332U)>>2]);
    float4 _S3853 = float4(_S3849, _S3850, _S3851, _S3852);
    float _S3854 = as_type<float>((&kernelContext_0)->frameParameters_0[(12336U)>>2]);
    float _S3855 = as_type<float>((&kernelContext_0)->frameParameters_0[(12340U)>>2]);
    float _S3856 = as_type<float>((&kernelContext_0)->frameParameters_0[(12344U)>>2]);
    float _S3857 = as_type<float>((&kernelContext_0)->frameParameters_0[(12348U)>>2]);
    float4 _S3858 = float4(_S3854, _S3855, _S3856, _S3857);
    float _S3859 = as_type<float>((&kernelContext_0)->frameParameters_0[(12352U)>>2]);
    float _S3860 = as_type<float>((&kernelContext_0)->frameParameters_0[(12356U)>>2]);
    float _S3861 = as_type<float>((&kernelContext_0)->frameParameters_0[(12360U)>>2]);
    float _S3862 = as_type<float>((&kernelContext_0)->frameParameters_0[(12364U)>>2]);
    float4 _S3863 = float4(_S3859, _S3860, _S3861, _S3862);
    float _S3864 = as_type<float>((&kernelContext_0)->frameParameters_0[(12368U)>>2]);
    float _S3865 = as_type<float>((&kernelContext_0)->frameParameters_0[(12372U)>>2]);
    float _S3866 = as_type<float>((&kernelContext_0)->frameParameters_0[(12376U)>>2]);
    float _S3867 = as_type<float>((&kernelContext_0)->frameParameters_0[(12380U)>>2]);
    float4 _S3868 = float4(_S3864, _S3865, _S3866, _S3867);
    float _S3869 = as_type<float>((&kernelContext_0)->frameParameters_0[(12384U)>>2]);
    float _S3870 = as_type<float>((&kernelContext_0)->frameParameters_0[(12388U)>>2]);
    float _S3871 = as_type<float>((&kernelContext_0)->frameParameters_0[(12392U)>>2]);
    float _S3872 = as_type<float>((&kernelContext_0)->frameParameters_0[(12396U)>>2]);
    float4 _S3873 = float4(_S3869, _S3870, _S3871, _S3872);
    float _S3874 = as_type<float>((&kernelContext_0)->frameParameters_0[(12400U)>>2]);
    float _S3875 = as_type<float>((&kernelContext_0)->frameParameters_0[(12404U)>>2]);
    float _S3876 = as_type<float>((&kernelContext_0)->frameParameters_0[(12408U)>>2]);
    float _S3877 = as_type<float>((&kernelContext_0)->frameParameters_0[(12412U)>>2]);
    float4 _S3878 = float4(_S3874, _S3875, _S3876, _S3877);
    float _S3879 = as_type<float>((&kernelContext_0)->frameParameters_0[(12416U)>>2]);
    float _S3880 = as_type<float>((&kernelContext_0)->frameParameters_0[(12420U)>>2]);
    float _S3881 = as_type<float>((&kernelContext_0)->frameParameters_0[(12424U)>>2]);
    float _S3882 = as_type<float>((&kernelContext_0)->frameParameters_0[(12428U)>>2]);
    float4 _S3883 = float4(_S3879, _S3880, _S3881, _S3882);
    float _S3884 = as_type<float>((&kernelContext_0)->frameParameters_0[(12432U)>>2]);
    float _S3885 = as_type<float>((&kernelContext_0)->frameParameters_0[(12436U)>>2]);
    float _S3886 = as_type<float>((&kernelContext_0)->frameParameters_0[(12440U)>>2]);
    float _S3887 = as_type<float>((&kernelContext_0)->frameParameters_0[(12444U)>>2]);
    float4 _S3888 = float4(_S3884, _S3885, _S3886, _S3887);
    float _S3889 = as_type<float>((&kernelContext_0)->frameParameters_0[(12448U)>>2]);
    float _S3890 = as_type<float>((&kernelContext_0)->frameParameters_0[(12452U)>>2]);
    float _S3891 = as_type<float>((&kernelContext_0)->frameParameters_0[(12456U)>>2]);
    float _S3892 = as_type<float>((&kernelContext_0)->frameParameters_0[(12460U)>>2]);
    float4 _S3893 = float4(_S3889, _S3890, _S3891, _S3892);
    float _S3894 = as_type<float>((&kernelContext_0)->frameParameters_0[(12464U)>>2]);
    float _S3895 = as_type<float>((&kernelContext_0)->frameParameters_0[(12468U)>>2]);
    float _S3896 = as_type<float>((&kernelContext_0)->frameParameters_0[(12472U)>>2]);
    float _S3897 = as_type<float>((&kernelContext_0)->frameParameters_0[(12476U)>>2]);
    float4 _S3898 = float4(_S3894, _S3895, _S3896, _S3897);
    float _S3899 = as_type<float>((&kernelContext_0)->frameParameters_0[(12480U)>>2]);
    float _S3900 = as_type<float>((&kernelContext_0)->frameParameters_0[(12484U)>>2]);
    float _S3901 = as_type<float>((&kernelContext_0)->frameParameters_0[(12488U)>>2]);
    float _S3902 = as_type<float>((&kernelContext_0)->frameParameters_0[(12492U)>>2]);
    float4 _S3903 = float4(_S3899, _S3900, _S3901, _S3902);
    float _S3904 = as_type<float>((&kernelContext_0)->frameParameters_0[(12496U)>>2]);
    float _S3905 = as_type<float>((&kernelContext_0)->frameParameters_0[(12500U)>>2]);
    float _S3906 = as_type<float>((&kernelContext_0)->frameParameters_0[(12504U)>>2]);
    float _S3907 = as_type<float>((&kernelContext_0)->frameParameters_0[(12508U)>>2]);
    array<float4, int(128)> _S3908 = { _S3273, _S3278, _S3283, _S3288, _S3293, _S3298, _S3303, _S3308, _S3313, _S3318, _S3323, _S3328, _S3333, _S3338, _S3343, _S3348, _S3353, _S3358, _S3363, _S3368, _S3373, _S3378, _S3383, _S3388, _S3393, _S3398, _S3403, _S3408, _S3413, _S3418, _S3423, _S3428, _S3433, _S3438, _S3443, _S3448, _S3453, _S3458, _S3463, _S3468, _S3473, _S3478, _S3483, _S3488, _S3493, _S3498, _S3503, _S3508, _S3513, _S3518, _S3523, _S3528, _S3533, _S3538, _S3543, _S3548, _S3553, _S3558, _S3563, _S3568, _S3573, _S3578, _S3583, _S3588, _S3593, _S3598, _S3603, _S3608, _S3613, _S3618, _S3623, _S3628, _S3633, _S3638, _S3643, _S3648, _S3653, _S3658, _S3663, _S3668, _S3673, _S3678, _S3683, _S3688, _S3693, _S3698, _S3703, _S3708, _S3713, _S3718, _S3723, _S3728, _S3733, _S3738, _S3743, _S3748, _S3753, _S3758, _S3763, _S3768, _S3773, _S3778, _S3783, _S3788, _S3793, _S3798, _S3803, _S3808, _S3813, _S3818, _S3823, _S3828, _S3833, _S3838, _S3843, _S3848, _S3853, _S3858, _S3863, _S3868, _S3873, _S3878, _S3883, _S3888, _S3893, _S3898, _S3903, float4(_S3904, _S3905, _S3906, _S3907) };
    float _S3909 = as_type<float>((&kernelContext_0)->frameParameters_0[(12512U)>>2]);
    float _S3910 = as_type<float>((&kernelContext_0)->frameParameters_0[(12516U)>>2]);
    float _S3911 = as_type<float>((&kernelContext_0)->frameParameters_0[(12520U)>>2]);
    float _S3912 = as_type<float>((&kernelContext_0)->frameParameters_0[(12524U)>>2]);
    float4 _S3913 = float4(_S3909, _S3910, _S3911, _S3912);
    float _S3914 = as_type<float>((&kernelContext_0)->frameParameters_0[(12528U)>>2]);
    float _S3915 = as_type<float>((&kernelContext_0)->frameParameters_0[(12532U)>>2]);
    float _S3916 = as_type<float>((&kernelContext_0)->frameParameters_0[(12536U)>>2]);
    float _S3917 = as_type<float>((&kernelContext_0)->frameParameters_0[(12540U)>>2]);
    float4 _S3918 = float4(_S3914, _S3915, _S3916, _S3917);
    float _S3919 = as_type<float>((&kernelContext_0)->frameParameters_0[(12544U)>>2]);
    float _S3920 = as_type<float>((&kernelContext_0)->frameParameters_0[(12548U)>>2]);
    float _S3921 = as_type<float>((&kernelContext_0)->frameParameters_0[(12552U)>>2]);
    float _S3922 = as_type<float>((&kernelContext_0)->frameParameters_0[(12556U)>>2]);
    float4 _S3923 = float4(_S3919, _S3920, _S3921, _S3922);
    float _S3924 = as_type<float>((&kernelContext_0)->frameParameters_0[(12560U)>>2]);
    float _S3925 = as_type<float>((&kernelContext_0)->frameParameters_0[(12564U)>>2]);
    float _S3926 = as_type<float>((&kernelContext_0)->frameParameters_0[(12568U)>>2]);
    float _S3927 = as_type<float>((&kernelContext_0)->frameParameters_0[(12572U)>>2]);
    matrix<float,int(4),int(4)>  _S3928 = matrix<float,int(4),int(4)> (_S3913, _S3918, _S3923, float4(_S3924, _S3925, _S3926, _S3927));
    float _S3929 = as_type<float>((&kernelContext_0)->frameParameters_0[(12576U)>>2]);
    float _S3930 = as_type<float>((&kernelContext_0)->frameParameters_0[(12580U)>>2]);
    float _S3931 = as_type<float>((&kernelContext_0)->frameParameters_0[(12584U)>>2]);
    float _S3932 = as_type<float>((&kernelContext_0)->frameParameters_0[(12588U)>>2]);
    float4 _S3933 = float4(_S3929, _S3930, _S3931, _S3932);
    float _S3934 = as_type<float>((&kernelContext_0)->frameParameters_0[(12592U)>>2]);
    float _S3935 = as_type<float>((&kernelContext_0)->frameParameters_0[(12596U)>>2]);
    float _S3936 = as_type<float>((&kernelContext_0)->frameParameters_0[(12600U)>>2]);
    float _S3937 = as_type<float>((&kernelContext_0)->frameParameters_0[(12604U)>>2]);
    float4 _S3938 = float4(_S3934, _S3935, _S3936, _S3937);
    float _S3939 = as_type<float>((&kernelContext_0)->frameParameters_0[(12608U)>>2]);
    float _S3940 = as_type<float>((&kernelContext_0)->frameParameters_0[(12612U)>>2]);
    float _S3941 = as_type<float>((&kernelContext_0)->frameParameters_0[(12616U)>>2]);
    float _S3942 = as_type<float>((&kernelContext_0)->frameParameters_0[(12620U)>>2]);
    float4 _S3943 = float4(_S3939, _S3940, _S3941, _S3942);
    float _S3944 = as_type<float>((&kernelContext_0)->frameParameters_0[(12624U)>>2]);
    float _S3945 = as_type<float>((&kernelContext_0)->frameParameters_0[(12628U)>>2]);
    float _S3946 = as_type<float>((&kernelContext_0)->frameParameters_0[(12632U)>>2]);
    float _S3947 = as_type<float>((&kernelContext_0)->frameParameters_0[(12636U)>>2]);
    matrix<float,int(4),int(4)>  _S3948 = matrix<float,int(4),int(4)> (_S3933, _S3938, _S3943, float4(_S3944, _S3945, _S3946, _S3947));
    float _S3949 = as_type<float>((&kernelContext_0)->frameParameters_0[(12640U)>>2]);
    float _S3950 = as_type<float>((&kernelContext_0)->frameParameters_0[(12644U)>>2]);
    float _S3951 = as_type<float>((&kernelContext_0)->frameParameters_0[(12648U)>>2]);
    float _S3952 = as_type<float>((&kernelContext_0)->frameParameters_0[(12652U)>>2]);
    float4 _S3953 = float4(_S3949, _S3950, _S3951, _S3952);
    float _S3954 = as_type<float>((&kernelContext_0)->frameParameters_0[(12656U)>>2]);
    float _S3955 = as_type<float>((&kernelContext_0)->frameParameters_0[(12660U)>>2]);
    float _S3956 = as_type<float>((&kernelContext_0)->frameParameters_0[(12664U)>>2]);
    float _S3957 = as_type<float>((&kernelContext_0)->frameParameters_0[(12668U)>>2]);
    float4 _S3958 = float4(_S3954, _S3955, _S3956, _S3957);
    float _S3959 = as_type<float>((&kernelContext_0)->frameParameters_0[(12672U)>>2]);
    float _S3960 = as_type<float>((&kernelContext_0)->frameParameters_0[(12676U)>>2]);
    float _S3961 = as_type<float>((&kernelContext_0)->frameParameters_0[(12680U)>>2]);
    float _S3962 = as_type<float>((&kernelContext_0)->frameParameters_0[(12684U)>>2]);
    float4 _S3963 = float4(_S3959, _S3960, _S3961, _S3962);
    float _S3964 = as_type<float>((&kernelContext_0)->frameParameters_0[(12688U)>>2]);
    float _S3965 = as_type<float>((&kernelContext_0)->frameParameters_0[(12692U)>>2]);
    float _S3966 = as_type<float>((&kernelContext_0)->frameParameters_0[(12696U)>>2]);
    float _S3967 = as_type<float>((&kernelContext_0)->frameParameters_0[(12700U)>>2]);
    matrix<float,int(4),int(4)>  _S3968 = matrix<float,int(4),int(4)> (_S3953, _S3958, _S3963, float4(_S3964, _S3965, _S3966, _S3967));
    float _S3969 = as_type<float>((&kernelContext_0)->frameParameters_0[(12704U)>>2]);
    float _S3970 = as_type<float>((&kernelContext_0)->frameParameters_0[(12708U)>>2]);
    float _S3971 = as_type<float>((&kernelContext_0)->frameParameters_0[(12712U)>>2]);
    float _S3972 = as_type<float>((&kernelContext_0)->frameParameters_0[(12716U)>>2]);
    float4 _S3973 = float4(_S3969, _S3970, _S3971, _S3972);
    float _S3974 = as_type<float>((&kernelContext_0)->frameParameters_0[(12720U)>>2]);
    float _S3975 = as_type<float>((&kernelContext_0)->frameParameters_0[(12724U)>>2]);
    float _S3976 = as_type<float>((&kernelContext_0)->frameParameters_0[(12728U)>>2]);
    float _S3977 = as_type<float>((&kernelContext_0)->frameParameters_0[(12732U)>>2]);
    float4 _S3978 = float4(_S3974, _S3975, _S3976, _S3977);
    float _S3979 = as_type<float>((&kernelContext_0)->frameParameters_0[(12736U)>>2]);
    float _S3980 = as_type<float>((&kernelContext_0)->frameParameters_0[(12740U)>>2]);
    float _S3981 = as_type<float>((&kernelContext_0)->frameParameters_0[(12744U)>>2]);
    float _S3982 = as_type<float>((&kernelContext_0)->frameParameters_0[(12748U)>>2]);
    float4 _S3983 = float4(_S3979, _S3980, _S3981, _S3982);
    float _S3984 = as_type<float>((&kernelContext_0)->frameParameters_0[(12752U)>>2]);
    float _S3985 = as_type<float>((&kernelContext_0)->frameParameters_0[(12756U)>>2]);
    float _S3986 = as_type<float>((&kernelContext_0)->frameParameters_0[(12760U)>>2]);
    float _S3987 = as_type<float>((&kernelContext_0)->frameParameters_0[(12764U)>>2]);
    matrix<float,int(4),int(4)>  _S3988 = matrix<float,int(4),int(4)> (_S3973, _S3978, _S3983, float4(_S3984, _S3985, _S3986, _S3987));
    float _S3989 = as_type<float>((&kernelContext_0)->frameParameters_0[(12768U)>>2]);
    float _S3990 = as_type<float>((&kernelContext_0)->frameParameters_0[(12772U)>>2]);
    float _S3991 = as_type<float>((&kernelContext_0)->frameParameters_0[(12776U)>>2]);
    float _S3992 = as_type<float>((&kernelContext_0)->frameParameters_0[(12780U)>>2]);
    float4 _S3993 = float4(_S3989, _S3990, _S3991, _S3992);
    float _S3994 = as_type<float>((&kernelContext_0)->frameParameters_0[(12784U)>>2]);
    float _S3995 = as_type<float>((&kernelContext_0)->frameParameters_0[(12788U)>>2]);
    float _S3996 = as_type<float>((&kernelContext_0)->frameParameters_0[(12792U)>>2]);
    float _S3997 = as_type<float>((&kernelContext_0)->frameParameters_0[(12796U)>>2]);
    float4 _S3998 = float4(_S3994, _S3995, _S3996, _S3997);
    float _S3999 = as_type<float>((&kernelContext_0)->frameParameters_0[(12800U)>>2]);
    float _S4000 = as_type<float>((&kernelContext_0)->frameParameters_0[(12804U)>>2]);
    float _S4001 = as_type<float>((&kernelContext_0)->frameParameters_0[(12808U)>>2]);
    float _S4002 = as_type<float>((&kernelContext_0)->frameParameters_0[(12812U)>>2]);
    float4 _S4003 = float4(_S3999, _S4000, _S4001, _S4002);
    float _S4004 = as_type<float>((&kernelContext_0)->frameParameters_0[(12816U)>>2]);
    float _S4005 = as_type<float>((&kernelContext_0)->frameParameters_0[(12820U)>>2]);
    float _S4006 = as_type<float>((&kernelContext_0)->frameParameters_0[(12824U)>>2]);
    float _S4007 = as_type<float>((&kernelContext_0)->frameParameters_0[(12828U)>>2]);
    array<matrix<float,int(4),int(4)> , int(4)> _S4008 = { _S3948, _S3968, _S3988, matrix<float,int(4),int(4)> (_S3993, _S3998, _S4003, float4(_S4004, _S4005, _S4006, _S4007)) };
    float _S4009 = as_type<float>((&kernelContext_0)->frameParameters_0[(12832U)>>2]);
    float _S4010 = as_type<float>((&kernelContext_0)->frameParameters_0[(12836U)>>2]);
    float _S4011 = as_type<float>((&kernelContext_0)->frameParameters_0[(12840U)>>2]);
    float _S4012 = as_type<float>((&kernelContext_0)->frameParameters_0[(12844U)>>2]);
    float4 _S4013 = float4(_S4009, _S4010, _S4011, _S4012);
    float _S4014 = as_type<float>((&kernelContext_0)->frameParameters_0[(12848U)>>2]);
    float _S4015 = as_type<float>((&kernelContext_0)->frameParameters_0[(12852U)>>2]);
    float _S4016 = as_type<float>((&kernelContext_0)->frameParameters_0[(12856U)>>2]);
    float _S4017 = as_type<float>((&kernelContext_0)->frameParameters_0[(12860U)>>2]);
    float4 _S4018 = float4(_S4014, _S4015, _S4016, _S4017);
    float _S4019 = as_type<float>((&kernelContext_0)->frameParameters_0[(12864U)>>2]);
    float _S4020 = as_type<float>((&kernelContext_0)->frameParameters_0[(12868U)>>2]);
    float _S4021 = as_type<float>((&kernelContext_0)->frameParameters_0[(12872U)>>2]);
    float _S4022 = as_type<float>((&kernelContext_0)->frameParameters_0[(12876U)>>2]);
    float4 _S4023 = float4(_S4019, _S4020, _S4021, _S4022);
    float _S4024 = as_type<float>((&kernelContext_0)->frameParameters_0[(12880U)>>2]);
    float _S4025 = as_type<float>((&kernelContext_0)->frameParameters_0[(12884U)>>2]);
    float _S4026 = as_type<float>((&kernelContext_0)->frameParameters_0[(12888U)>>2]);
    float _S4027 = as_type<float>((&kernelContext_0)->frameParameters_0[(12892U)>>2]);
    array<float4, int(4)> _S4028 = { _S4013, _S4018, _S4023, float4(_S4024, _S4025, _S4026, _S4027) };
    float _S4029 = as_type<float>((&kernelContext_0)->frameParameters_0[(12896U)>>2]);
    float _S4030 = as_type<float>((&kernelContext_0)->frameParameters_0[(12900U)>>2]);
    float _S4031 = as_type<float>((&kernelContext_0)->frameParameters_0[(12904U)>>2]);
    float _S4032 = as_type<float>((&kernelContext_0)->frameParameters_0[(12908U)>>2]);
    float4 _S4033 = float4(_S4029, _S4030, _S4031, _S4032);
    float _S4034 = as_type<float>((&kernelContext_0)->frameParameters_0[(12912U)>>2]);
    float _S4035 = as_type<float>((&kernelContext_0)->frameParameters_0[(12916U)>>2]);
    float _S4036 = as_type<float>((&kernelContext_0)->frameParameters_0[(12920U)>>2]);
    float _S4037 = as_type<float>((&kernelContext_0)->frameParameters_0[(12924U)>>2]);
    float4 _S4038 = float4(_S4034, _S4035, _S4036, _S4037);
    float _S4039 = as_type<float>((&kernelContext_0)->frameParameters_0[(12928U)>>2]);
    float _S4040 = as_type<float>((&kernelContext_0)->frameParameters_0[(12932U)>>2]);
    float _S4041 = as_type<float>((&kernelContext_0)->frameParameters_0[(12936U)>>2]);
    float _S4042 = as_type<float>((&kernelContext_0)->frameParameters_0[(12940U)>>2]);
    float4 _S4043 = float4(_S4039, _S4040, _S4041, _S4042);
    float _S4044 = as_type<float>((&kernelContext_0)->frameParameters_0[(12944U)>>2]);
    float _S4045 = as_type<float>((&kernelContext_0)->frameParameters_0[(12948U)>>2]);
    float _S4046 = as_type<float>((&kernelContext_0)->frameParameters_0[(12952U)>>2]);
    float _S4047 = as_type<float>((&kernelContext_0)->frameParameters_0[(12956U)>>2]);
    array<float4, int(4)> _S4048 = { _S4033, _S4038, _S4043, float4(_S4044, _S4045, _S4046, _S4047) };
    float _S4049 = as_type<float>((&kernelContext_0)->frameParameters_0[(12960U)>>2]);
    float _S4050 = as_type<float>((&kernelContext_0)->frameParameters_0[(12964U)>>2]);
    float _S4051 = as_type<float>((&kernelContext_0)->frameParameters_0[(12968U)>>2]);
    float _S4052 = as_type<float>((&kernelContext_0)->frameParameters_0[(12972U)>>2]);
    float4 _S4053 = float4(_S4049, _S4050, _S4051, _S4052);
    float _S4054 = as_type<float>((&kernelContext_0)->frameParameters_0[(12976U)>>2]);
    float _S4055 = as_type<float>((&kernelContext_0)->frameParameters_0[(12980U)>>2]);
    float _S4056 = as_type<float>((&kernelContext_0)->frameParameters_0[(12984U)>>2]);
    float _S4057 = as_type<float>((&kernelContext_0)->frameParameters_0[(12988U)>>2]);
    float4 _S4058 = float4(_S4054, _S4055, _S4056, _S4057);
    float _S4059 = as_type<float>((&kernelContext_0)->frameParameters_0[(12992U)>>2]);
    float _S4060 = as_type<float>((&kernelContext_0)->frameParameters_0[(12996U)>>2]);
    float _S4061 = as_type<float>((&kernelContext_0)->frameParameters_0[(13000U)>>2]);
    float _S4062 = as_type<float>((&kernelContext_0)->frameParameters_0[(13004U)>>2]);
    float4 _S4063 = float4(_S4059, _S4060, _S4061, _S4062);
    float _S4064 = as_type<float>((&kernelContext_0)->frameParameters_0[(13008U)>>2]);
    float _S4065 = as_type<float>((&kernelContext_0)->frameParameters_0[(13012U)>>2]);
    float _S4066 = as_type<float>((&kernelContext_0)->frameParameters_0[(13016U)>>2]);
    float _S4067 = as_type<float>((&kernelContext_0)->frameParameters_0[(13020U)>>2]);
    float4 _S4068 = float4(_S4064, _S4065, _S4066, _S4067);
    float _S4069 = as_type<float>((&kernelContext_0)->frameParameters_0[(13024U)>>2]);
    float _S4070 = as_type<float>((&kernelContext_0)->frameParameters_0[(13028U)>>2]);
    float _S4071 = as_type<float>((&kernelContext_0)->frameParameters_0[(13032U)>>2]);
    float _S4072 = as_type<float>((&kernelContext_0)->frameParameters_0[(13036U)>>2]);
    float4 _S4073 = float4(_S4069, _S4070, _S4071, _S4072);
    float _S4074 = as_type<float>((&kernelContext_0)->frameParameters_0[(13040U)>>2]);
    float _S4075 = as_type<float>((&kernelContext_0)->frameParameters_0[(13044U)>>2]);
    float _S4076 = as_type<float>((&kernelContext_0)->frameParameters_0[(13048U)>>2]);
    float _S4077 = as_type<float>((&kernelContext_0)->frameParameters_0[(13052U)>>2]);
    float4 _S4078 = float4(_S4074, _S4075, _S4076, _S4077);
    float _S4079 = as_type<float>((&kernelContext_0)->frameParameters_0[(13056U)>>2]);
    float _S4080 = as_type<float>((&kernelContext_0)->frameParameters_0[(13060U)>>2]);
    float _S4081 = as_type<float>((&kernelContext_0)->frameParameters_0[(13064U)>>2]);
    float _S4082 = as_type<float>((&kernelContext_0)->frameParameters_0[(13068U)>>2]);
    float4 _S4083 = float4(_S4079, _S4080, _S4081, _S4082);
    float _S4084 = as_type<float>((&kernelContext_0)->frameParameters_0[(13072U)>>2]);
    float _S4085 = as_type<float>((&kernelContext_0)->frameParameters_0[(13076U)>>2]);
    float _S4086 = as_type<float>((&kernelContext_0)->frameParameters_0[(13080U)>>2]);
    float _S4087 = as_type<float>((&kernelContext_0)->frameParameters_0[(13084U)>>2]);
    float4 _S4088 = float4(_S4084, _S4085, _S4086, _S4087);
    float _S4089 = as_type<float>((&kernelContext_0)->frameParameters_0[(13088U)>>2]);
    float _S4090 = as_type<float>((&kernelContext_0)->frameParameters_0[(13092U)>>2]);
    float _S4091 = as_type<float>((&kernelContext_0)->frameParameters_0[(13096U)>>2]);
    float _S4092 = as_type<float>((&kernelContext_0)->frameParameters_0[(13100U)>>2]);
    float4 _S4093 = float4(_S4089, _S4090, _S4091, _S4092);
    float _S4094 = as_type<float>((&kernelContext_0)->frameParameters_0[(13104U)>>2]);
    float _S4095 = as_type<float>((&kernelContext_0)->frameParameters_0[(13108U)>>2]);
    float _S4096 = as_type<float>((&kernelContext_0)->frameParameters_0[(13112U)>>2]);
    float _S4097 = as_type<float>((&kernelContext_0)->frameParameters_0[(13116U)>>2]);
    float4 _S4098 = float4(_S4094, _S4095, _S4096, _S4097);
    float _S4099 = as_type<float>((&kernelContext_0)->frameParameters_0[(13120U)>>2]);
    float _S4100 = as_type<float>((&kernelContext_0)->frameParameters_0[(13124U)>>2]);
    float _S4101 = as_type<float>((&kernelContext_0)->frameParameters_0[(13128U)>>2]);
    float _S4102 = as_type<float>((&kernelContext_0)->frameParameters_0[(13132U)>>2]);
    float4 _S4103 = float4(_S4099, _S4100, _S4101, _S4102);
    float _S4104 = as_type<float>((&kernelContext_0)->frameParameters_0[(13136U)>>2]);
    float _S4105 = as_type<float>((&kernelContext_0)->frameParameters_0[(13140U)>>2]);
    float _S4106 = as_type<float>((&kernelContext_0)->frameParameters_0[(13144U)>>2]);
    float _S4107 = as_type<float>((&kernelContext_0)->frameParameters_0[(13148U)>>2]);
    float4 _S4108 = float4(_S4104, _S4105, _S4106, _S4107);
    float _S4109 = as_type<float>((&kernelContext_0)->frameParameters_0[(13152U)>>2]);
    float _S4110 = as_type<float>((&kernelContext_0)->frameParameters_0[(13156U)>>2]);
    float _S4111 = as_type<float>((&kernelContext_0)->frameParameters_0[(13160U)>>2]);
    float _S4112 = as_type<float>((&kernelContext_0)->frameParameters_0[(13164U)>>2]);
    float4 _S4113 = float4(_S4109, _S4110, _S4111, _S4112);
    float _S4114 = as_type<float>((&kernelContext_0)->frameParameters_0[(13168U)>>2]);
    float _S4115 = as_type<float>((&kernelContext_0)->frameParameters_0[(13172U)>>2]);
    float _S4116 = as_type<float>((&kernelContext_0)->frameParameters_0[(13176U)>>2]);
    float _S4117 = as_type<float>((&kernelContext_0)->frameParameters_0[(13180U)>>2]);
    float4 _S4118 = float4(_S4114, _S4115, _S4116, _S4117);
    float _S4119 = as_type<float>((&kernelContext_0)->frameParameters_0[(13184U)>>2]);
    float _S4120 = as_type<float>((&kernelContext_0)->frameParameters_0[(13188U)>>2]);
    float _S4121 = as_type<float>((&kernelContext_0)->frameParameters_0[(13192U)>>2]);
    float _S4122 = as_type<float>((&kernelContext_0)->frameParameters_0[(13196U)>>2]);
    float4 _S4123 = float4(_S4119, _S4120, _S4121, _S4122);
    float _S4124 = as_type<float>((&kernelContext_0)->frameParameters_0[(13200U)>>2]);
    float _S4125 = as_type<float>((&kernelContext_0)->frameParameters_0[(13204U)>>2]);
    float _S4126 = as_type<float>((&kernelContext_0)->frameParameters_0[(13208U)>>2]);
    float _S4127 = as_type<float>((&kernelContext_0)->frameParameters_0[(13212U)>>2]);
    float4 _S4128 = float4(_S4124, _S4125, _S4126, _S4127);
    float _S4129 = as_type<float>((&kernelContext_0)->frameParameters_0[(13216U)>>2]);
    float _S4130 = as_type<float>((&kernelContext_0)->frameParameters_0[(13220U)>>2]);
    float _S4131 = as_type<float>((&kernelContext_0)->frameParameters_0[(13224U)>>2]);
    float _S4132 = as_type<float>((&kernelContext_0)->frameParameters_0[(13228U)>>2]);
    float4 _S4133 = float4(_S4129, _S4130, _S4131, _S4132);
    float _S4134 = as_type<float>((&kernelContext_0)->frameParameters_0[(13232U)>>2]);
    float _S4135 = as_type<float>((&kernelContext_0)->frameParameters_0[(13236U)>>2]);
    float _S4136 = as_type<float>((&kernelContext_0)->frameParameters_0[(13240U)>>2]);
    float _S4137 = as_type<float>((&kernelContext_0)->frameParameters_0[(13244U)>>2]);
    float4 _S4138 = float4(_S4134, _S4135, _S4136, _S4137);
    float _S4139 = as_type<float>((&kernelContext_0)->frameParameters_0[(13248U)>>2]);
    float _S4140 = as_type<float>((&kernelContext_0)->frameParameters_0[(13252U)>>2]);
    float _S4141 = as_type<float>((&kernelContext_0)->frameParameters_0[(13256U)>>2]);
    float _S4142 = as_type<float>((&kernelContext_0)->frameParameters_0[(13260U)>>2]);
    float4 _S4143 = float4(_S4139, _S4140, _S4141, _S4142);
    float _S4144 = as_type<float>((&kernelContext_0)->frameParameters_0[(13264U)>>2]);
    float _S4145 = as_type<float>((&kernelContext_0)->frameParameters_0[(13268U)>>2]);
    float _S4146 = as_type<float>((&kernelContext_0)->frameParameters_0[(13272U)>>2]);
    float _S4147 = as_type<float>((&kernelContext_0)->frameParameters_0[(13276U)>>2]);
    float4 _S4148 = float4(_S4144, _S4145, _S4146, _S4147);
    float _S4149 = as_type<float>((&kernelContext_0)->frameParameters_0[(13280U)>>2]);
    float _S4150 = as_type<float>((&kernelContext_0)->frameParameters_0[(13284U)>>2]);
    float _S4151 = as_type<float>((&kernelContext_0)->frameParameters_0[(13288U)>>2]);
    float _S4152 = as_type<float>((&kernelContext_0)->frameParameters_0[(13292U)>>2]);
    float4 _S4153 = float4(_S4149, _S4150, _S4151, _S4152);
    float _S4154 = as_type<float>((&kernelContext_0)->frameParameters_0[(13296U)>>2]);
    float _S4155 = as_type<float>((&kernelContext_0)->frameParameters_0[(13300U)>>2]);
    float _S4156 = as_type<float>((&kernelContext_0)->frameParameters_0[(13304U)>>2]);
    float _S4157 = as_type<float>((&kernelContext_0)->frameParameters_0[(13308U)>>2]);
    float4 _S4158 = float4(_S4154, _S4155, _S4156, _S4157);
    float _S4159 = as_type<float>((&kernelContext_0)->frameParameters_0[(13312U)>>2]);
    float _S4160 = as_type<float>((&kernelContext_0)->frameParameters_0[(13316U)>>2]);
    float _S4161 = as_type<float>((&kernelContext_0)->frameParameters_0[(13320U)>>2]);
    float _S4162 = as_type<float>((&kernelContext_0)->frameParameters_0[(13324U)>>2]);
    float4 _S4163 = float4(_S4159, _S4160, _S4161, _S4162);
    float _S4164 = as_type<float>((&kernelContext_0)->frameParameters_0[(13328U)>>2]);
    float _S4165 = as_type<float>((&kernelContext_0)->frameParameters_0[(13332U)>>2]);
    float _S4166 = as_type<float>((&kernelContext_0)->frameParameters_0[(13336U)>>2]);
    float _S4167 = as_type<float>((&kernelContext_0)->frameParameters_0[(13340U)>>2]);
    float4 _S4168 = float4(_S4164, _S4165, _S4166, _S4167);
    float _S4169 = as_type<float>((&kernelContext_0)->frameParameters_0[(13344U)>>2]);
    float _S4170 = as_type<float>((&kernelContext_0)->frameParameters_0[(13348U)>>2]);
    float _S4171 = as_type<float>((&kernelContext_0)->frameParameters_0[(13352U)>>2]);
    float _S4172 = as_type<float>((&kernelContext_0)->frameParameters_0[(13356U)>>2]);
    float4 _S4173 = float4(_S4169, _S4170, _S4171, _S4172);
    float _S4174 = as_type<float>((&kernelContext_0)->frameParameters_0[(13360U)>>2]);
    float _S4175 = as_type<float>((&kernelContext_0)->frameParameters_0[(13364U)>>2]);
    float _S4176 = as_type<float>((&kernelContext_0)->frameParameters_0[(13368U)>>2]);
    float _S4177 = as_type<float>((&kernelContext_0)->frameParameters_0[(13372U)>>2]);
    float4 _S4178 = float4(_S4174, _S4175, _S4176, _S4177);
    float _S4179 = as_type<float>((&kernelContext_0)->frameParameters_0[(13376U)>>2]);
    float _S4180 = as_type<float>((&kernelContext_0)->frameParameters_0[(13380U)>>2]);
    float _S4181 = as_type<float>((&kernelContext_0)->frameParameters_0[(13384U)>>2]);
    float _S4182 = as_type<float>((&kernelContext_0)->frameParameters_0[(13388U)>>2]);
    float4 _S4183 = float4(_S4179, _S4180, _S4181, _S4182);
    float _S4184 = as_type<float>((&kernelContext_0)->frameParameters_0[(13392U)>>2]);
    float _S4185 = as_type<float>((&kernelContext_0)->frameParameters_0[(13396U)>>2]);
    float _S4186 = as_type<float>((&kernelContext_0)->frameParameters_0[(13400U)>>2]);
    float _S4187 = as_type<float>((&kernelContext_0)->frameParameters_0[(13404U)>>2]);
    float4 _S4188 = float4(_S4184, _S4185, _S4186, _S4187);
    float _S4189 = as_type<float>((&kernelContext_0)->frameParameters_0[(13408U)>>2]);
    float _S4190 = as_type<float>((&kernelContext_0)->frameParameters_0[(13412U)>>2]);
    float _S4191 = as_type<float>((&kernelContext_0)->frameParameters_0[(13416U)>>2]);
    float _S4192 = as_type<float>((&kernelContext_0)->frameParameters_0[(13420U)>>2]);
    float4 _S4193 = float4(_S4189, _S4190, _S4191, _S4192);
    float _S4194 = as_type<float>((&kernelContext_0)->frameParameters_0[(13424U)>>2]);
    float _S4195 = as_type<float>((&kernelContext_0)->frameParameters_0[(13428U)>>2]);
    float _S4196 = as_type<float>((&kernelContext_0)->frameParameters_0[(13432U)>>2]);
    float _S4197 = as_type<float>((&kernelContext_0)->frameParameters_0[(13436U)>>2]);
    float4 _S4198 = float4(_S4194, _S4195, _S4196, _S4197);
    float _S4199 = as_type<float>((&kernelContext_0)->frameParameters_0[(13440U)>>2]);
    float _S4200 = as_type<float>((&kernelContext_0)->frameParameters_0[(13444U)>>2]);
    float _S4201 = as_type<float>((&kernelContext_0)->frameParameters_0[(13448U)>>2]);
    float _S4202 = as_type<float>((&kernelContext_0)->frameParameters_0[(13452U)>>2]);
    float4 _S4203 = float4(_S4199, _S4200, _S4201, _S4202);
    float _S4204 = as_type<float>((&kernelContext_0)->frameParameters_0[(13456U)>>2]);
    float _S4205 = as_type<float>((&kernelContext_0)->frameParameters_0[(13460U)>>2]);
    float _S4206 = as_type<float>((&kernelContext_0)->frameParameters_0[(13464U)>>2]);
    float _S4207 = as_type<float>((&kernelContext_0)->frameParameters_0[(13468U)>>2]);
    float4 _S4208 = float4(_S4204, _S4205, _S4206, _S4207);
    float _S4209 = as_type<float>((&kernelContext_0)->frameParameters_0[(13472U)>>2]);
    float _S4210 = as_type<float>((&kernelContext_0)->frameParameters_0[(13476U)>>2]);
    float _S4211 = as_type<float>((&kernelContext_0)->frameParameters_0[(13480U)>>2]);
    float _S4212 = as_type<float>((&kernelContext_0)->frameParameters_0[(13484U)>>2]);
    float4 _S4213 = float4(_S4209, _S4210, _S4211, _S4212);
    float _S4214 = as_type<float>((&kernelContext_0)->frameParameters_0[(13488U)>>2]);
    float _S4215 = as_type<float>((&kernelContext_0)->frameParameters_0[(13492U)>>2]);
    float _S4216 = as_type<float>((&kernelContext_0)->frameParameters_0[(13496U)>>2]);
    float _S4217 = as_type<float>((&kernelContext_0)->frameParameters_0[(13500U)>>2]);
    float4 _S4218 = float4(_S4214, _S4215, _S4216, _S4217);
    float _S4219 = as_type<float>((&kernelContext_0)->frameParameters_0[(13504U)>>2]);
    float _S4220 = as_type<float>((&kernelContext_0)->frameParameters_0[(13508U)>>2]);
    float _S4221 = as_type<float>((&kernelContext_0)->frameParameters_0[(13512U)>>2]);
    float _S4222 = as_type<float>((&kernelContext_0)->frameParameters_0[(13516U)>>2]);
    float4 _S4223 = float4(_S4219, _S4220, _S4221, _S4222);
    float _S4224 = as_type<float>((&kernelContext_0)->frameParameters_0[(13520U)>>2]);
    float _S4225 = as_type<float>((&kernelContext_0)->frameParameters_0[(13524U)>>2]);
    float _S4226 = as_type<float>((&kernelContext_0)->frameParameters_0[(13528U)>>2]);
    float _S4227 = as_type<float>((&kernelContext_0)->frameParameters_0[(13532U)>>2]);
    float4 _S4228 = float4(_S4224, _S4225, _S4226, _S4227);
    float _S4229 = as_type<float>((&kernelContext_0)->frameParameters_0[(13536U)>>2]);
    float _S4230 = as_type<float>((&kernelContext_0)->frameParameters_0[(13540U)>>2]);
    float _S4231 = as_type<float>((&kernelContext_0)->frameParameters_0[(13544U)>>2]);
    float _S4232 = as_type<float>((&kernelContext_0)->frameParameters_0[(13548U)>>2]);
    float4 _S4233 = float4(_S4229, _S4230, _S4231, _S4232);
    float _S4234 = as_type<float>((&kernelContext_0)->frameParameters_0[(13552U)>>2]);
    float _S4235 = as_type<float>((&kernelContext_0)->frameParameters_0[(13556U)>>2]);
    float _S4236 = as_type<float>((&kernelContext_0)->frameParameters_0[(13560U)>>2]);
    float _S4237 = as_type<float>((&kernelContext_0)->frameParameters_0[(13564U)>>2]);
    float4 _S4238 = float4(_S4234, _S4235, _S4236, _S4237);
    float _S4239 = as_type<float>((&kernelContext_0)->frameParameters_0[(13568U)>>2]);
    float _S4240 = as_type<float>((&kernelContext_0)->frameParameters_0[(13572U)>>2]);
    float _S4241 = as_type<float>((&kernelContext_0)->frameParameters_0[(13576U)>>2]);
    float _S4242 = as_type<float>((&kernelContext_0)->frameParameters_0[(13580U)>>2]);
    float4 _S4243 = float4(_S4239, _S4240, _S4241, _S4242);
    float _S4244 = as_type<float>((&kernelContext_0)->frameParameters_0[(13584U)>>2]);
    float _S4245 = as_type<float>((&kernelContext_0)->frameParameters_0[(13588U)>>2]);
    float _S4246 = as_type<float>((&kernelContext_0)->frameParameters_0[(13592U)>>2]);
    float _S4247 = as_type<float>((&kernelContext_0)->frameParameters_0[(13596U)>>2]);
    float4 _S4248 = float4(_S4244, _S4245, _S4246, _S4247);
    float _S4249 = as_type<float>((&kernelContext_0)->frameParameters_0[(13600U)>>2]);
    float _S4250 = as_type<float>((&kernelContext_0)->frameParameters_0[(13604U)>>2]);
    float _S4251 = as_type<float>((&kernelContext_0)->frameParameters_0[(13608U)>>2]);
    float _S4252 = as_type<float>((&kernelContext_0)->frameParameters_0[(13612U)>>2]);
    float4 _S4253 = float4(_S4249, _S4250, _S4251, _S4252);
    float _S4254 = as_type<float>((&kernelContext_0)->frameParameters_0[(13616U)>>2]);
    float _S4255 = as_type<float>((&kernelContext_0)->frameParameters_0[(13620U)>>2]);
    float _S4256 = as_type<float>((&kernelContext_0)->frameParameters_0[(13624U)>>2]);
    float _S4257 = as_type<float>((&kernelContext_0)->frameParameters_0[(13628U)>>2]);
    float4 _S4258 = float4(_S4254, _S4255, _S4256, _S4257);
    float _S4259 = as_type<float>((&kernelContext_0)->frameParameters_0[(13632U)>>2]);
    float _S4260 = as_type<float>((&kernelContext_0)->frameParameters_0[(13636U)>>2]);
    float _S4261 = as_type<float>((&kernelContext_0)->frameParameters_0[(13640U)>>2]);
    float _S4262 = as_type<float>((&kernelContext_0)->frameParameters_0[(13644U)>>2]);
    float4 _S4263 = float4(_S4259, _S4260, _S4261, _S4262);
    float _S4264 = as_type<float>((&kernelContext_0)->frameParameters_0[(13648U)>>2]);
    float _S4265 = as_type<float>((&kernelContext_0)->frameParameters_0[(13652U)>>2]);
    float _S4266 = as_type<float>((&kernelContext_0)->frameParameters_0[(13656U)>>2]);
    float _S4267 = as_type<float>((&kernelContext_0)->frameParameters_0[(13660U)>>2]);
    float4 _S4268 = float4(_S4264, _S4265, _S4266, _S4267);
    float _S4269 = as_type<float>((&kernelContext_0)->frameParameters_0[(13664U)>>2]);
    float _S4270 = as_type<float>((&kernelContext_0)->frameParameters_0[(13668U)>>2]);
    float _S4271 = as_type<float>((&kernelContext_0)->frameParameters_0[(13672U)>>2]);
    float _S4272 = as_type<float>((&kernelContext_0)->frameParameters_0[(13676U)>>2]);
    float4 _S4273 = float4(_S4269, _S4270, _S4271, _S4272);
    float _S4274 = as_type<float>((&kernelContext_0)->frameParameters_0[(13680U)>>2]);
    float _S4275 = as_type<float>((&kernelContext_0)->frameParameters_0[(13684U)>>2]);
    float _S4276 = as_type<float>((&kernelContext_0)->frameParameters_0[(13688U)>>2]);
    float _S4277 = as_type<float>((&kernelContext_0)->frameParameters_0[(13692U)>>2]);
    float4 _S4278 = float4(_S4274, _S4275, _S4276, _S4277);
    float _S4279 = as_type<float>((&kernelContext_0)->frameParameters_0[(13696U)>>2]);
    float _S4280 = as_type<float>((&kernelContext_0)->frameParameters_0[(13700U)>>2]);
    float _S4281 = as_type<float>((&kernelContext_0)->frameParameters_0[(13704U)>>2]);
    float _S4282 = as_type<float>((&kernelContext_0)->frameParameters_0[(13708U)>>2]);
    float4 _S4283 = float4(_S4279, _S4280, _S4281, _S4282);
    float _S4284 = as_type<float>((&kernelContext_0)->frameParameters_0[(13712U)>>2]);
    float _S4285 = as_type<float>((&kernelContext_0)->frameParameters_0[(13716U)>>2]);
    float _S4286 = as_type<float>((&kernelContext_0)->frameParameters_0[(13720U)>>2]);
    float _S4287 = as_type<float>((&kernelContext_0)->frameParameters_0[(13724U)>>2]);
    float4 _S4288 = float4(_S4284, _S4285, _S4286, _S4287);
    float _S4289 = as_type<float>((&kernelContext_0)->frameParameters_0[(13728U)>>2]);
    float _S4290 = as_type<float>((&kernelContext_0)->frameParameters_0[(13732U)>>2]);
    float _S4291 = as_type<float>((&kernelContext_0)->frameParameters_0[(13736U)>>2]);
    float _S4292 = as_type<float>((&kernelContext_0)->frameParameters_0[(13740U)>>2]);
    float4 _S4293 = float4(_S4289, _S4290, _S4291, _S4292);
    float _S4294 = as_type<float>((&kernelContext_0)->frameParameters_0[(13744U)>>2]);
    float _S4295 = as_type<float>((&kernelContext_0)->frameParameters_0[(13748U)>>2]);
    float _S4296 = as_type<float>((&kernelContext_0)->frameParameters_0[(13752U)>>2]);
    float _S4297 = as_type<float>((&kernelContext_0)->frameParameters_0[(13756U)>>2]);
    float4 _S4298 = float4(_S4294, _S4295, _S4296, _S4297);
    float _S4299 = as_type<float>((&kernelContext_0)->frameParameters_0[(13760U)>>2]);
    float _S4300 = as_type<float>((&kernelContext_0)->frameParameters_0[(13764U)>>2]);
    float _S4301 = as_type<float>((&kernelContext_0)->frameParameters_0[(13768U)>>2]);
    float _S4302 = as_type<float>((&kernelContext_0)->frameParameters_0[(13772U)>>2]);
    float4 _S4303 = float4(_S4299, _S4300, _S4301, _S4302);
    float _S4304 = as_type<float>((&kernelContext_0)->frameParameters_0[(13776U)>>2]);
    float _S4305 = as_type<float>((&kernelContext_0)->frameParameters_0[(13780U)>>2]);
    float _S4306 = as_type<float>((&kernelContext_0)->frameParameters_0[(13784U)>>2]);
    float _S4307 = as_type<float>((&kernelContext_0)->frameParameters_0[(13788U)>>2]);
    float4 _S4308 = float4(_S4304, _S4305, _S4306, _S4307);
    float _S4309 = as_type<float>((&kernelContext_0)->frameParameters_0[(13792U)>>2]);
    float _S4310 = as_type<float>((&kernelContext_0)->frameParameters_0[(13796U)>>2]);
    float _S4311 = as_type<float>((&kernelContext_0)->frameParameters_0[(13800U)>>2]);
    float _S4312 = as_type<float>((&kernelContext_0)->frameParameters_0[(13804U)>>2]);
    float4 _S4313 = float4(_S4309, _S4310, _S4311, _S4312);
    float _S4314 = as_type<float>((&kernelContext_0)->frameParameters_0[(13808U)>>2]);
    float _S4315 = as_type<float>((&kernelContext_0)->frameParameters_0[(13812U)>>2]);
    float _S4316 = as_type<float>((&kernelContext_0)->frameParameters_0[(13816U)>>2]);
    float _S4317 = as_type<float>((&kernelContext_0)->frameParameters_0[(13820U)>>2]);
    float4 _S4318 = float4(_S4314, _S4315, _S4316, _S4317);
    float _S4319 = as_type<float>((&kernelContext_0)->frameParameters_0[(13824U)>>2]);
    float _S4320 = as_type<float>((&kernelContext_0)->frameParameters_0[(13828U)>>2]);
    float _S4321 = as_type<float>((&kernelContext_0)->frameParameters_0[(13832U)>>2]);
    float _S4322 = as_type<float>((&kernelContext_0)->frameParameters_0[(13836U)>>2]);
    float4 _S4323 = float4(_S4319, _S4320, _S4321, _S4322);
    float _S4324 = as_type<float>((&kernelContext_0)->frameParameters_0[(13840U)>>2]);
    float _S4325 = as_type<float>((&kernelContext_0)->frameParameters_0[(13844U)>>2]);
    float _S4326 = as_type<float>((&kernelContext_0)->frameParameters_0[(13848U)>>2]);
    float _S4327 = as_type<float>((&kernelContext_0)->frameParameters_0[(13852U)>>2]);
    float4 _S4328 = float4(_S4324, _S4325, _S4326, _S4327);
    float _S4329 = as_type<float>((&kernelContext_0)->frameParameters_0[(13856U)>>2]);
    float _S4330 = as_type<float>((&kernelContext_0)->frameParameters_0[(13860U)>>2]);
    float _S4331 = as_type<float>((&kernelContext_0)->frameParameters_0[(13864U)>>2]);
    float _S4332 = as_type<float>((&kernelContext_0)->frameParameters_0[(13868U)>>2]);
    float4 _S4333 = float4(_S4329, _S4330, _S4331, _S4332);
    float _S4334 = as_type<float>((&kernelContext_0)->frameParameters_0[(13872U)>>2]);
    float _S4335 = as_type<float>((&kernelContext_0)->frameParameters_0[(13876U)>>2]);
    float _S4336 = as_type<float>((&kernelContext_0)->frameParameters_0[(13880U)>>2]);
    float _S4337 = as_type<float>((&kernelContext_0)->frameParameters_0[(13884U)>>2]);
    float4 _S4338 = float4(_S4334, _S4335, _S4336, _S4337);
    float _S4339 = as_type<float>((&kernelContext_0)->frameParameters_0[(13888U)>>2]);
    float _S4340 = as_type<float>((&kernelContext_0)->frameParameters_0[(13892U)>>2]);
    float _S4341 = as_type<float>((&kernelContext_0)->frameParameters_0[(13896U)>>2]);
    float _S4342 = as_type<float>((&kernelContext_0)->frameParameters_0[(13900U)>>2]);
    float4 _S4343 = float4(_S4339, _S4340, _S4341, _S4342);
    float _S4344 = as_type<float>((&kernelContext_0)->frameParameters_0[(13904U)>>2]);
    float _S4345 = as_type<float>((&kernelContext_0)->frameParameters_0[(13908U)>>2]);
    float _S4346 = as_type<float>((&kernelContext_0)->frameParameters_0[(13912U)>>2]);
    float _S4347 = as_type<float>((&kernelContext_0)->frameParameters_0[(13916U)>>2]);
    float4 _S4348 = float4(_S4344, _S4345, _S4346, _S4347);
    float _S4349 = as_type<float>((&kernelContext_0)->frameParameters_0[(13920U)>>2]);
    float _S4350 = as_type<float>((&kernelContext_0)->frameParameters_0[(13924U)>>2]);
    float _S4351 = as_type<float>((&kernelContext_0)->frameParameters_0[(13928U)>>2]);
    float _S4352 = as_type<float>((&kernelContext_0)->frameParameters_0[(13932U)>>2]);
    float4 _S4353 = float4(_S4349, _S4350, _S4351, _S4352);
    float _S4354 = as_type<float>((&kernelContext_0)->frameParameters_0[(13936U)>>2]);
    float _S4355 = as_type<float>((&kernelContext_0)->frameParameters_0[(13940U)>>2]);
    float _S4356 = as_type<float>((&kernelContext_0)->frameParameters_0[(13944U)>>2]);
    float _S4357 = as_type<float>((&kernelContext_0)->frameParameters_0[(13948U)>>2]);
    float4 _S4358 = float4(_S4354, _S4355, _S4356, _S4357);
    float _S4359 = as_type<float>((&kernelContext_0)->frameParameters_0[(13952U)>>2]);
    float _S4360 = as_type<float>((&kernelContext_0)->frameParameters_0[(13956U)>>2]);
    float _S4361 = as_type<float>((&kernelContext_0)->frameParameters_0[(13960U)>>2]);
    float _S4362 = as_type<float>((&kernelContext_0)->frameParameters_0[(13964U)>>2]);
    float4 _S4363 = float4(_S4359, _S4360, _S4361, _S4362);
    float _S4364 = as_type<float>((&kernelContext_0)->frameParameters_0[(13968U)>>2]);
    float _S4365 = as_type<float>((&kernelContext_0)->frameParameters_0[(13972U)>>2]);
    float _S4366 = as_type<float>((&kernelContext_0)->frameParameters_0[(13976U)>>2]);
    float _S4367 = as_type<float>((&kernelContext_0)->frameParameters_0[(13980U)>>2]);
    float4 _S4368 = float4(_S4364, _S4365, _S4366, _S4367);
    float _S4369 = as_type<float>((&kernelContext_0)->frameParameters_0[(13984U)>>2]);
    float _S4370 = as_type<float>((&kernelContext_0)->frameParameters_0[(13988U)>>2]);
    float _S4371 = as_type<float>((&kernelContext_0)->frameParameters_0[(13992U)>>2]);
    float _S4372 = as_type<float>((&kernelContext_0)->frameParameters_0[(13996U)>>2]);
    float4 _S4373 = float4(_S4369, _S4370, _S4371, _S4372);
    float _S4374 = as_type<float>((&kernelContext_0)->frameParameters_0[(14000U)>>2]);
    float _S4375 = as_type<float>((&kernelContext_0)->frameParameters_0[(14004U)>>2]);
    float _S4376 = as_type<float>((&kernelContext_0)->frameParameters_0[(14008U)>>2]);
    float _S4377 = as_type<float>((&kernelContext_0)->frameParameters_0[(14012U)>>2]);
    float4 _S4378 = float4(_S4374, _S4375, _S4376, _S4377);
    float _S4379 = as_type<float>((&kernelContext_0)->frameParameters_0[(14016U)>>2]);
    float _S4380 = as_type<float>((&kernelContext_0)->frameParameters_0[(14020U)>>2]);
    float _S4381 = as_type<float>((&kernelContext_0)->frameParameters_0[(14024U)>>2]);
    float _S4382 = as_type<float>((&kernelContext_0)->frameParameters_0[(14028U)>>2]);
    float4 _S4383 = float4(_S4379, _S4380, _S4381, _S4382);
    float _S4384 = as_type<float>((&kernelContext_0)->frameParameters_0[(14032U)>>2]);
    float _S4385 = as_type<float>((&kernelContext_0)->frameParameters_0[(14036U)>>2]);
    float _S4386 = as_type<float>((&kernelContext_0)->frameParameters_0[(14040U)>>2]);
    float _S4387 = as_type<float>((&kernelContext_0)->frameParameters_0[(14044U)>>2]);
    float4 _S4388 = float4(_S4384, _S4385, _S4386, _S4387);
    float _S4389 = as_type<float>((&kernelContext_0)->frameParameters_0[(14048U)>>2]);
    float _S4390 = as_type<float>((&kernelContext_0)->frameParameters_0[(14052U)>>2]);
    float _S4391 = as_type<float>((&kernelContext_0)->frameParameters_0[(14056U)>>2]);
    float _S4392 = as_type<float>((&kernelContext_0)->frameParameters_0[(14060U)>>2]);
    float4 _S4393 = float4(_S4389, _S4390, _S4391, _S4392);
    float _S4394 = as_type<float>((&kernelContext_0)->frameParameters_0[(14064U)>>2]);
    float _S4395 = as_type<float>((&kernelContext_0)->frameParameters_0[(14068U)>>2]);
    float _S4396 = as_type<float>((&kernelContext_0)->frameParameters_0[(14072U)>>2]);
    float _S4397 = as_type<float>((&kernelContext_0)->frameParameters_0[(14076U)>>2]);
    float4 _S4398 = float4(_S4394, _S4395, _S4396, _S4397);
    float _S4399 = as_type<float>((&kernelContext_0)->frameParameters_0[(14080U)>>2]);
    float _S4400 = as_type<float>((&kernelContext_0)->frameParameters_0[(14084U)>>2]);
    float _S4401 = as_type<float>((&kernelContext_0)->frameParameters_0[(14088U)>>2]);
    float _S4402 = as_type<float>((&kernelContext_0)->frameParameters_0[(14092U)>>2]);
    float4 _S4403 = float4(_S4399, _S4400, _S4401, _S4402);
    float _S4404 = as_type<float>((&kernelContext_0)->frameParameters_0[(14096U)>>2]);
    float _S4405 = as_type<float>((&kernelContext_0)->frameParameters_0[(14100U)>>2]);
    float _S4406 = as_type<float>((&kernelContext_0)->frameParameters_0[(14104U)>>2]);
    float _S4407 = as_type<float>((&kernelContext_0)->frameParameters_0[(14108U)>>2]);
    float4 _S4408 = float4(_S4404, _S4405, _S4406, _S4407);
    float _S4409 = as_type<float>((&kernelContext_0)->frameParameters_0[(14112U)>>2]);
    float _S4410 = as_type<float>((&kernelContext_0)->frameParameters_0[(14116U)>>2]);
    float _S4411 = as_type<float>((&kernelContext_0)->frameParameters_0[(14120U)>>2]);
    float _S4412 = as_type<float>((&kernelContext_0)->frameParameters_0[(14124U)>>2]);
    float4 _S4413 = float4(_S4409, _S4410, _S4411, _S4412);
    float _S4414 = as_type<float>((&kernelContext_0)->frameParameters_0[(14128U)>>2]);
    float _S4415 = as_type<float>((&kernelContext_0)->frameParameters_0[(14132U)>>2]);
    float _S4416 = as_type<float>((&kernelContext_0)->frameParameters_0[(14136U)>>2]);
    float _S4417 = as_type<float>((&kernelContext_0)->frameParameters_0[(14140U)>>2]);
    float4 _S4418 = float4(_S4414, _S4415, _S4416, _S4417);
    float _S4419 = as_type<float>((&kernelContext_0)->frameParameters_0[(14144U)>>2]);
    float _S4420 = as_type<float>((&kernelContext_0)->frameParameters_0[(14148U)>>2]);
    float _S4421 = as_type<float>((&kernelContext_0)->frameParameters_0[(14152U)>>2]);
    float _S4422 = as_type<float>((&kernelContext_0)->frameParameters_0[(14156U)>>2]);
    float4 _S4423 = float4(_S4419, _S4420, _S4421, _S4422);
    float _S4424 = as_type<float>((&kernelContext_0)->frameParameters_0[(14160U)>>2]);
    float _S4425 = as_type<float>((&kernelContext_0)->frameParameters_0[(14164U)>>2]);
    float _S4426 = as_type<float>((&kernelContext_0)->frameParameters_0[(14168U)>>2]);
    float _S4427 = as_type<float>((&kernelContext_0)->frameParameters_0[(14172U)>>2]);
    float4 _S4428 = float4(_S4424, _S4425, _S4426, _S4427);
    float _S4429 = as_type<float>((&kernelContext_0)->frameParameters_0[(14176U)>>2]);
    float _S4430 = as_type<float>((&kernelContext_0)->frameParameters_0[(14180U)>>2]);
    float _S4431 = as_type<float>((&kernelContext_0)->frameParameters_0[(14184U)>>2]);
    float _S4432 = as_type<float>((&kernelContext_0)->frameParameters_0[(14188U)>>2]);
    float4 _S4433 = float4(_S4429, _S4430, _S4431, _S4432);
    float _S4434 = as_type<float>((&kernelContext_0)->frameParameters_0[(14192U)>>2]);
    float _S4435 = as_type<float>((&kernelContext_0)->frameParameters_0[(14196U)>>2]);
    float _S4436 = as_type<float>((&kernelContext_0)->frameParameters_0[(14200U)>>2]);
    float _S4437 = as_type<float>((&kernelContext_0)->frameParameters_0[(14204U)>>2]);
    float4 _S4438 = float4(_S4434, _S4435, _S4436, _S4437);
    float _S4439 = as_type<float>((&kernelContext_0)->frameParameters_0[(14208U)>>2]);
    float _S4440 = as_type<float>((&kernelContext_0)->frameParameters_0[(14212U)>>2]);
    float _S4441 = as_type<float>((&kernelContext_0)->frameParameters_0[(14216U)>>2]);
    float _S4442 = as_type<float>((&kernelContext_0)->frameParameters_0[(14220U)>>2]);
    float4 _S4443 = float4(_S4439, _S4440, _S4441, _S4442);
    float _S4444 = as_type<float>((&kernelContext_0)->frameParameters_0[(14224U)>>2]);
    float _S4445 = as_type<float>((&kernelContext_0)->frameParameters_0[(14228U)>>2]);
    float _S4446 = as_type<float>((&kernelContext_0)->frameParameters_0[(14232U)>>2]);
    float _S4447 = as_type<float>((&kernelContext_0)->frameParameters_0[(14236U)>>2]);
    float4 _S4448 = float4(_S4444, _S4445, _S4446, _S4447);
    float _S4449 = as_type<float>((&kernelContext_0)->frameParameters_0[(14240U)>>2]);
    float _S4450 = as_type<float>((&kernelContext_0)->frameParameters_0[(14244U)>>2]);
    float _S4451 = as_type<float>((&kernelContext_0)->frameParameters_0[(14248U)>>2]);
    float _S4452 = as_type<float>((&kernelContext_0)->frameParameters_0[(14252U)>>2]);
    float4 _S4453 = float4(_S4449, _S4450, _S4451, _S4452);
    float _S4454 = as_type<float>((&kernelContext_0)->frameParameters_0[(14256U)>>2]);
    float _S4455 = as_type<float>((&kernelContext_0)->frameParameters_0[(14260U)>>2]);
    float _S4456 = as_type<float>((&kernelContext_0)->frameParameters_0[(14264U)>>2]);
    float _S4457 = as_type<float>((&kernelContext_0)->frameParameters_0[(14268U)>>2]);
    float4 _S4458 = float4(_S4454, _S4455, _S4456, _S4457);
    float _S4459 = as_type<float>((&kernelContext_0)->frameParameters_0[(14272U)>>2]);
    float _S4460 = as_type<float>((&kernelContext_0)->frameParameters_0[(14276U)>>2]);
    float _S4461 = as_type<float>((&kernelContext_0)->frameParameters_0[(14280U)>>2]);
    float _S4462 = as_type<float>((&kernelContext_0)->frameParameters_0[(14284U)>>2]);
    float4 _S4463 = float4(_S4459, _S4460, _S4461, _S4462);
    float _S4464 = as_type<float>((&kernelContext_0)->frameParameters_0[(14288U)>>2]);
    float _S4465 = as_type<float>((&kernelContext_0)->frameParameters_0[(14292U)>>2]);
    float _S4466 = as_type<float>((&kernelContext_0)->frameParameters_0[(14296U)>>2]);
    float _S4467 = as_type<float>((&kernelContext_0)->frameParameters_0[(14300U)>>2]);
    float4 _S4468 = float4(_S4464, _S4465, _S4466, _S4467);
    float _S4469 = as_type<float>((&kernelContext_0)->frameParameters_0[(14304U)>>2]);
    float _S4470 = as_type<float>((&kernelContext_0)->frameParameters_0[(14308U)>>2]);
    float _S4471 = as_type<float>((&kernelContext_0)->frameParameters_0[(14312U)>>2]);
    float _S4472 = as_type<float>((&kernelContext_0)->frameParameters_0[(14316U)>>2]);
    float4 _S4473 = float4(_S4469, _S4470, _S4471, _S4472);
    float _S4474 = as_type<float>((&kernelContext_0)->frameParameters_0[(14320U)>>2]);
    float _S4475 = as_type<float>((&kernelContext_0)->frameParameters_0[(14324U)>>2]);
    float _S4476 = as_type<float>((&kernelContext_0)->frameParameters_0[(14328U)>>2]);
    float _S4477 = as_type<float>((&kernelContext_0)->frameParameters_0[(14332U)>>2]);
    float4 _S4478 = float4(_S4474, _S4475, _S4476, _S4477);
    float _S4479 = as_type<float>((&kernelContext_0)->frameParameters_0[(14336U)>>2]);
    float _S4480 = as_type<float>((&kernelContext_0)->frameParameters_0[(14340U)>>2]);
    float _S4481 = as_type<float>((&kernelContext_0)->frameParameters_0[(14344U)>>2]);
    float _S4482 = as_type<float>((&kernelContext_0)->frameParameters_0[(14348U)>>2]);
    float4 _S4483 = float4(_S4479, _S4480, _S4481, _S4482);
    float _S4484 = as_type<float>((&kernelContext_0)->frameParameters_0[(14352U)>>2]);
    float _S4485 = as_type<float>((&kernelContext_0)->frameParameters_0[(14356U)>>2]);
    float _S4486 = as_type<float>((&kernelContext_0)->frameParameters_0[(14360U)>>2]);
    float _S4487 = as_type<float>((&kernelContext_0)->frameParameters_0[(14364U)>>2]);
    float4 _S4488 = float4(_S4484, _S4485, _S4486, _S4487);
    float _S4489 = as_type<float>((&kernelContext_0)->frameParameters_0[(14368U)>>2]);
    float _S4490 = as_type<float>((&kernelContext_0)->frameParameters_0[(14372U)>>2]);
    float _S4491 = as_type<float>((&kernelContext_0)->frameParameters_0[(14376U)>>2]);
    float _S4492 = as_type<float>((&kernelContext_0)->frameParameters_0[(14380U)>>2]);
    float4 _S4493 = float4(_S4489, _S4490, _S4491, _S4492);
    float _S4494 = as_type<float>((&kernelContext_0)->frameParameters_0[(14384U)>>2]);
    float _S4495 = as_type<float>((&kernelContext_0)->frameParameters_0[(14388U)>>2]);
    float _S4496 = as_type<float>((&kernelContext_0)->frameParameters_0[(14392U)>>2]);
    float _S4497 = as_type<float>((&kernelContext_0)->frameParameters_0[(14396U)>>2]);
    float4 _S4498 = float4(_S4494, _S4495, _S4496, _S4497);
    float _S4499 = as_type<float>((&kernelContext_0)->frameParameters_0[(14400U)>>2]);
    float _S4500 = as_type<float>((&kernelContext_0)->frameParameters_0[(14404U)>>2]);
    float _S4501 = as_type<float>((&kernelContext_0)->frameParameters_0[(14408U)>>2]);
    float _S4502 = as_type<float>((&kernelContext_0)->frameParameters_0[(14412U)>>2]);
    float4 _S4503 = float4(_S4499, _S4500, _S4501, _S4502);
    float _S4504 = as_type<float>((&kernelContext_0)->frameParameters_0[(14416U)>>2]);
    float _S4505 = as_type<float>((&kernelContext_0)->frameParameters_0[(14420U)>>2]);
    float _S4506 = as_type<float>((&kernelContext_0)->frameParameters_0[(14424U)>>2]);
    float _S4507 = as_type<float>((&kernelContext_0)->frameParameters_0[(14428U)>>2]);
    float4 _S4508 = float4(_S4504, _S4505, _S4506, _S4507);
    float _S4509 = as_type<float>((&kernelContext_0)->frameParameters_0[(14432U)>>2]);
    float _S4510 = as_type<float>((&kernelContext_0)->frameParameters_0[(14436U)>>2]);
    float _S4511 = as_type<float>((&kernelContext_0)->frameParameters_0[(14440U)>>2]);
    float _S4512 = as_type<float>((&kernelContext_0)->frameParameters_0[(14444U)>>2]);
    float4 _S4513 = float4(_S4509, _S4510, _S4511, _S4512);
    float _S4514 = as_type<float>((&kernelContext_0)->frameParameters_0[(14448U)>>2]);
    float _S4515 = as_type<float>((&kernelContext_0)->frameParameters_0[(14452U)>>2]);
    float _S4516 = as_type<float>((&kernelContext_0)->frameParameters_0[(14456U)>>2]);
    float _S4517 = as_type<float>((&kernelContext_0)->frameParameters_0[(14460U)>>2]);
    float4 _S4518 = float4(_S4514, _S4515, _S4516, _S4517);
    float _S4519 = as_type<float>((&kernelContext_0)->frameParameters_0[(14464U)>>2]);
    float _S4520 = as_type<float>((&kernelContext_0)->frameParameters_0[(14468U)>>2]);
    float _S4521 = as_type<float>((&kernelContext_0)->frameParameters_0[(14472U)>>2]);
    float _S4522 = as_type<float>((&kernelContext_0)->frameParameters_0[(14476U)>>2]);
    float4 _S4523 = float4(_S4519, _S4520, _S4521, _S4522);
    float _S4524 = as_type<float>((&kernelContext_0)->frameParameters_0[(14480U)>>2]);
    float _S4525 = as_type<float>((&kernelContext_0)->frameParameters_0[(14484U)>>2]);
    float _S4526 = as_type<float>((&kernelContext_0)->frameParameters_0[(14488U)>>2]);
    float _S4527 = as_type<float>((&kernelContext_0)->frameParameters_0[(14492U)>>2]);
    float4 _S4528 = float4(_S4524, _S4525, _S4526, _S4527);
    float _S4529 = as_type<float>((&kernelContext_0)->frameParameters_0[(14496U)>>2]);
    float _S4530 = as_type<float>((&kernelContext_0)->frameParameters_0[(14500U)>>2]);
    float _S4531 = as_type<float>((&kernelContext_0)->frameParameters_0[(14504U)>>2]);
    float _S4532 = as_type<float>((&kernelContext_0)->frameParameters_0[(14508U)>>2]);
    float4 _S4533 = float4(_S4529, _S4530, _S4531, _S4532);
    float _S4534 = as_type<float>((&kernelContext_0)->frameParameters_0[(14512U)>>2]);
    float _S4535 = as_type<float>((&kernelContext_0)->frameParameters_0[(14516U)>>2]);
    float _S4536 = as_type<float>((&kernelContext_0)->frameParameters_0[(14520U)>>2]);
    float _S4537 = as_type<float>((&kernelContext_0)->frameParameters_0[(14524U)>>2]);
    float4 _S4538 = float4(_S4534, _S4535, _S4536, _S4537);
    float _S4539 = as_type<float>((&kernelContext_0)->frameParameters_0[(14528U)>>2]);
    float _S4540 = as_type<float>((&kernelContext_0)->frameParameters_0[(14532U)>>2]);
    float _S4541 = as_type<float>((&kernelContext_0)->frameParameters_0[(14536U)>>2]);
    float _S4542 = as_type<float>((&kernelContext_0)->frameParameters_0[(14540U)>>2]);
    float4 _S4543 = float4(_S4539, _S4540, _S4541, _S4542);
    float _S4544 = as_type<float>((&kernelContext_0)->frameParameters_0[(14544U)>>2]);
    float _S4545 = as_type<float>((&kernelContext_0)->frameParameters_0[(14548U)>>2]);
    float _S4546 = as_type<float>((&kernelContext_0)->frameParameters_0[(14552U)>>2]);
    float _S4547 = as_type<float>((&kernelContext_0)->frameParameters_0[(14556U)>>2]);
    float4 _S4548 = float4(_S4544, _S4545, _S4546, _S4547);
    float _S4549 = as_type<float>((&kernelContext_0)->frameParameters_0[(14560U)>>2]);
    float _S4550 = as_type<float>((&kernelContext_0)->frameParameters_0[(14564U)>>2]);
    float _S4551 = as_type<float>((&kernelContext_0)->frameParameters_0[(14568U)>>2]);
    float _S4552 = as_type<float>((&kernelContext_0)->frameParameters_0[(14572U)>>2]);
    float4 _S4553 = float4(_S4549, _S4550, _S4551, _S4552);
    float _S4554 = as_type<float>((&kernelContext_0)->frameParameters_0[(14576U)>>2]);
    float _S4555 = as_type<float>((&kernelContext_0)->frameParameters_0[(14580U)>>2]);
    float _S4556 = as_type<float>((&kernelContext_0)->frameParameters_0[(14584U)>>2]);
    float _S4557 = as_type<float>((&kernelContext_0)->frameParameters_0[(14588U)>>2]);
    float4 _S4558 = float4(_S4554, _S4555, _S4556, _S4557);
    float _S4559 = as_type<float>((&kernelContext_0)->frameParameters_0[(14592U)>>2]);
    float _S4560 = as_type<float>((&kernelContext_0)->frameParameters_0[(14596U)>>2]);
    float _S4561 = as_type<float>((&kernelContext_0)->frameParameters_0[(14600U)>>2]);
    float _S4562 = as_type<float>((&kernelContext_0)->frameParameters_0[(14604U)>>2]);
    float4 _S4563 = float4(_S4559, _S4560, _S4561, _S4562);
    float _S4564 = as_type<float>((&kernelContext_0)->frameParameters_0[(14608U)>>2]);
    float _S4565 = as_type<float>((&kernelContext_0)->frameParameters_0[(14612U)>>2]);
    float _S4566 = as_type<float>((&kernelContext_0)->frameParameters_0[(14616U)>>2]);
    float _S4567 = as_type<float>((&kernelContext_0)->frameParameters_0[(14620U)>>2]);
    float4 _S4568 = float4(_S4564, _S4565, _S4566, _S4567);
    float _S4569 = as_type<float>((&kernelContext_0)->frameParameters_0[(14624U)>>2]);
    float _S4570 = as_type<float>((&kernelContext_0)->frameParameters_0[(14628U)>>2]);
    float _S4571 = as_type<float>((&kernelContext_0)->frameParameters_0[(14632U)>>2]);
    float _S4572 = as_type<float>((&kernelContext_0)->frameParameters_0[(14636U)>>2]);
    float4 _S4573 = float4(_S4569, _S4570, _S4571, _S4572);
    float _S4574 = as_type<float>((&kernelContext_0)->frameParameters_0[(14640U)>>2]);
    float _S4575 = as_type<float>((&kernelContext_0)->frameParameters_0[(14644U)>>2]);
    float _S4576 = as_type<float>((&kernelContext_0)->frameParameters_0[(14648U)>>2]);
    float _S4577 = as_type<float>((&kernelContext_0)->frameParameters_0[(14652U)>>2]);
    float4 _S4578 = float4(_S4574, _S4575, _S4576, _S4577);
    float _S4579 = as_type<float>((&kernelContext_0)->frameParameters_0[(14656U)>>2]);
    float _S4580 = as_type<float>((&kernelContext_0)->frameParameters_0[(14660U)>>2]);
    float _S4581 = as_type<float>((&kernelContext_0)->frameParameters_0[(14664U)>>2]);
    float _S4582 = as_type<float>((&kernelContext_0)->frameParameters_0[(14668U)>>2]);
    float4 _S4583 = float4(_S4579, _S4580, _S4581, _S4582);
    float _S4584 = as_type<float>((&kernelContext_0)->frameParameters_0[(14672U)>>2]);
    float _S4585 = as_type<float>((&kernelContext_0)->frameParameters_0[(14676U)>>2]);
    float _S4586 = as_type<float>((&kernelContext_0)->frameParameters_0[(14680U)>>2]);
    float _S4587 = as_type<float>((&kernelContext_0)->frameParameters_0[(14684U)>>2]);
    float4 _S4588 = float4(_S4584, _S4585, _S4586, _S4587);
    float _S4589 = as_type<float>((&kernelContext_0)->frameParameters_0[(14688U)>>2]);
    float _S4590 = as_type<float>((&kernelContext_0)->frameParameters_0[(14692U)>>2]);
    float _S4591 = as_type<float>((&kernelContext_0)->frameParameters_0[(14696U)>>2]);
    float _S4592 = as_type<float>((&kernelContext_0)->frameParameters_0[(14700U)>>2]);
    float4 _S4593 = float4(_S4589, _S4590, _S4591, _S4592);
    float _S4594 = as_type<float>((&kernelContext_0)->frameParameters_0[(14704U)>>2]);
    float _S4595 = as_type<float>((&kernelContext_0)->frameParameters_0[(14708U)>>2]);
    float _S4596 = as_type<float>((&kernelContext_0)->frameParameters_0[(14712U)>>2]);
    float _S4597 = as_type<float>((&kernelContext_0)->frameParameters_0[(14716U)>>2]);
    float4 _S4598 = float4(_S4594, _S4595, _S4596, _S4597);
    float _S4599 = as_type<float>((&kernelContext_0)->frameParameters_0[(14720U)>>2]);
    float _S4600 = as_type<float>((&kernelContext_0)->frameParameters_0[(14724U)>>2]);
    float _S4601 = as_type<float>((&kernelContext_0)->frameParameters_0[(14728U)>>2]);
    float _S4602 = as_type<float>((&kernelContext_0)->frameParameters_0[(14732U)>>2]);
    float4 _S4603 = float4(_S4599, _S4600, _S4601, _S4602);
    float _S4604 = as_type<float>((&kernelContext_0)->frameParameters_0[(14736U)>>2]);
    float _S4605 = as_type<float>((&kernelContext_0)->frameParameters_0[(14740U)>>2]);
    float _S4606 = as_type<float>((&kernelContext_0)->frameParameters_0[(14744U)>>2]);
    float _S4607 = as_type<float>((&kernelContext_0)->frameParameters_0[(14748U)>>2]);
    float4 _S4608 = float4(_S4604, _S4605, _S4606, _S4607);
    float _S4609 = as_type<float>((&kernelContext_0)->frameParameters_0[(14752U)>>2]);
    float _S4610 = as_type<float>((&kernelContext_0)->frameParameters_0[(14756U)>>2]);
    float _S4611 = as_type<float>((&kernelContext_0)->frameParameters_0[(14760U)>>2]);
    float _S4612 = as_type<float>((&kernelContext_0)->frameParameters_0[(14764U)>>2]);
    float4 _S4613 = float4(_S4609, _S4610, _S4611, _S4612);
    float _S4614 = as_type<float>((&kernelContext_0)->frameParameters_0[(14768U)>>2]);
    float _S4615 = as_type<float>((&kernelContext_0)->frameParameters_0[(14772U)>>2]);
    float _S4616 = as_type<float>((&kernelContext_0)->frameParameters_0[(14776U)>>2]);
    float _S4617 = as_type<float>((&kernelContext_0)->frameParameters_0[(14780U)>>2]);
    float4 _S4618 = float4(_S4614, _S4615, _S4616, _S4617);
    float _S4619 = as_type<float>((&kernelContext_0)->frameParameters_0[(14784U)>>2]);
    float _S4620 = as_type<float>((&kernelContext_0)->frameParameters_0[(14788U)>>2]);
    float _S4621 = as_type<float>((&kernelContext_0)->frameParameters_0[(14792U)>>2]);
    float _S4622 = as_type<float>((&kernelContext_0)->frameParameters_0[(14796U)>>2]);
    float4 _S4623 = float4(_S4619, _S4620, _S4621, _S4622);
    float _S4624 = as_type<float>((&kernelContext_0)->frameParameters_0[(14800U)>>2]);
    float _S4625 = as_type<float>((&kernelContext_0)->frameParameters_0[(14804U)>>2]);
    float _S4626 = as_type<float>((&kernelContext_0)->frameParameters_0[(14808U)>>2]);
    float _S4627 = as_type<float>((&kernelContext_0)->frameParameters_0[(14812U)>>2]);
    float4 _S4628 = float4(_S4624, _S4625, _S4626, _S4627);
    float _S4629 = as_type<float>((&kernelContext_0)->frameParameters_0[(14816U)>>2]);
    float _S4630 = as_type<float>((&kernelContext_0)->frameParameters_0[(14820U)>>2]);
    float _S4631 = as_type<float>((&kernelContext_0)->frameParameters_0[(14824U)>>2]);
    float _S4632 = as_type<float>((&kernelContext_0)->frameParameters_0[(14828U)>>2]);
    float4 _S4633 = float4(_S4629, _S4630, _S4631, _S4632);
    float _S4634 = as_type<float>((&kernelContext_0)->frameParameters_0[(14832U)>>2]);
    float _S4635 = as_type<float>((&kernelContext_0)->frameParameters_0[(14836U)>>2]);
    float _S4636 = as_type<float>((&kernelContext_0)->frameParameters_0[(14840U)>>2]);
    float _S4637 = as_type<float>((&kernelContext_0)->frameParameters_0[(14844U)>>2]);
    float4 _S4638 = float4(_S4634, _S4635, _S4636, _S4637);
    float _S4639 = as_type<float>((&kernelContext_0)->frameParameters_0[(14848U)>>2]);
    float _S4640 = as_type<float>((&kernelContext_0)->frameParameters_0[(14852U)>>2]);
    float _S4641 = as_type<float>((&kernelContext_0)->frameParameters_0[(14856U)>>2]);
    float _S4642 = as_type<float>((&kernelContext_0)->frameParameters_0[(14860U)>>2]);
    float4 _S4643 = float4(_S4639, _S4640, _S4641, _S4642);
    float _S4644 = as_type<float>((&kernelContext_0)->frameParameters_0[(14864U)>>2]);
    float _S4645 = as_type<float>((&kernelContext_0)->frameParameters_0[(14868U)>>2]);
    float _S4646 = as_type<float>((&kernelContext_0)->frameParameters_0[(14872U)>>2]);
    float _S4647 = as_type<float>((&kernelContext_0)->frameParameters_0[(14876U)>>2]);
    float4 _S4648 = float4(_S4644, _S4645, _S4646, _S4647);
    float _S4649 = as_type<float>((&kernelContext_0)->frameParameters_0[(14880U)>>2]);
    float _S4650 = as_type<float>((&kernelContext_0)->frameParameters_0[(14884U)>>2]);
    float _S4651 = as_type<float>((&kernelContext_0)->frameParameters_0[(14888U)>>2]);
    float _S4652 = as_type<float>((&kernelContext_0)->frameParameters_0[(14892U)>>2]);
    float4 _S4653 = float4(_S4649, _S4650, _S4651, _S4652);
    float _S4654 = as_type<float>((&kernelContext_0)->frameParameters_0[(14896U)>>2]);
    float _S4655 = as_type<float>((&kernelContext_0)->frameParameters_0[(14900U)>>2]);
    float _S4656 = as_type<float>((&kernelContext_0)->frameParameters_0[(14904U)>>2]);
    float _S4657 = as_type<float>((&kernelContext_0)->frameParameters_0[(14908U)>>2]);
    float4 _S4658 = float4(_S4654, _S4655, _S4656, _S4657);
    float _S4659 = as_type<float>((&kernelContext_0)->frameParameters_0[(14912U)>>2]);
    float _S4660 = as_type<float>((&kernelContext_0)->frameParameters_0[(14916U)>>2]);
    float _S4661 = as_type<float>((&kernelContext_0)->frameParameters_0[(14920U)>>2]);
    float _S4662 = as_type<float>((&kernelContext_0)->frameParameters_0[(14924U)>>2]);
    float4 _S4663 = float4(_S4659, _S4660, _S4661, _S4662);
    float _S4664 = as_type<float>((&kernelContext_0)->frameParameters_0[(14928U)>>2]);
    float _S4665 = as_type<float>((&kernelContext_0)->frameParameters_0[(14932U)>>2]);
    float _S4666 = as_type<float>((&kernelContext_0)->frameParameters_0[(14936U)>>2]);
    float _S4667 = as_type<float>((&kernelContext_0)->frameParameters_0[(14940U)>>2]);
    float4 _S4668 = float4(_S4664, _S4665, _S4666, _S4667);
    float _S4669 = as_type<float>((&kernelContext_0)->frameParameters_0[(14944U)>>2]);
    float _S4670 = as_type<float>((&kernelContext_0)->frameParameters_0[(14948U)>>2]);
    float _S4671 = as_type<float>((&kernelContext_0)->frameParameters_0[(14952U)>>2]);
    float _S4672 = as_type<float>((&kernelContext_0)->frameParameters_0[(14956U)>>2]);
    float4 _S4673 = float4(_S4669, _S4670, _S4671, _S4672);
    float _S4674 = as_type<float>((&kernelContext_0)->frameParameters_0[(14960U)>>2]);
    float _S4675 = as_type<float>((&kernelContext_0)->frameParameters_0[(14964U)>>2]);
    float _S4676 = as_type<float>((&kernelContext_0)->frameParameters_0[(14968U)>>2]);
    float _S4677 = as_type<float>((&kernelContext_0)->frameParameters_0[(14972U)>>2]);
    float4 _S4678 = float4(_S4674, _S4675, _S4676, _S4677);
    float _S4679 = as_type<float>((&kernelContext_0)->frameParameters_0[(14976U)>>2]);
    float _S4680 = as_type<float>((&kernelContext_0)->frameParameters_0[(14980U)>>2]);
    float _S4681 = as_type<float>((&kernelContext_0)->frameParameters_0[(14984U)>>2]);
    float _S4682 = as_type<float>((&kernelContext_0)->frameParameters_0[(14988U)>>2]);
    float4 _S4683 = float4(_S4679, _S4680, _S4681, _S4682);
    float _S4684 = as_type<float>((&kernelContext_0)->frameParameters_0[(14992U)>>2]);
    float _S4685 = as_type<float>((&kernelContext_0)->frameParameters_0[(14996U)>>2]);
    float _S4686 = as_type<float>((&kernelContext_0)->frameParameters_0[(15000U)>>2]);
    float _S4687 = as_type<float>((&kernelContext_0)->frameParameters_0[(15004U)>>2]);
    array<float4, int(128)> _S4688 = { _S4053, _S4058, _S4063, _S4068, _S4073, _S4078, _S4083, _S4088, _S4093, _S4098, _S4103, _S4108, _S4113, _S4118, _S4123, _S4128, _S4133, _S4138, _S4143, _S4148, _S4153, _S4158, _S4163, _S4168, _S4173, _S4178, _S4183, _S4188, _S4193, _S4198, _S4203, _S4208, _S4213, _S4218, _S4223, _S4228, _S4233, _S4238, _S4243, _S4248, _S4253, _S4258, _S4263, _S4268, _S4273, _S4278, _S4283, _S4288, _S4293, _S4298, _S4303, _S4308, _S4313, _S4318, _S4323, _S4328, _S4333, _S4338, _S4343, _S4348, _S4353, _S4358, _S4363, _S4368, _S4373, _S4378, _S4383, _S4388, _S4393, _S4398, _S4403, _S4408, _S4413, _S4418, _S4423, _S4428, _S4433, _S4438, _S4443, _S4448, _S4453, _S4458, _S4463, _S4468, _S4473, _S4478, _S4483, _S4488, _S4493, _S4498, _S4503, _S4508, _S4513, _S4518, _S4523, _S4528, _S4533, _S4538, _S4543, _S4548, _S4553, _S4558, _S4563, _S4568, _S4573, _S4578, _S4583, _S4588, _S4593, _S4598, _S4603, _S4608, _S4613, _S4618, _S4623, _S4628, _S4633, _S4638, _S4643, _S4648, _S4653, _S4658, _S4663, _S4668, _S4673, _S4678, _S4683, float4(_S4684, _S4685, _S4686, _S4687) };
    float _S4689 = as_type<float>((&kernelContext_0)->frameParameters_0[(15008U)>>2]);
    float _S4690 = as_type<float>((&kernelContext_0)->frameParameters_0[(15012U)>>2]);
    float _S4691 = as_type<float>((&kernelContext_0)->frameParameters_0[(15016U)>>2]);
    float _S4692 = as_type<float>((&kernelContext_0)->frameParameters_0[(15020U)>>2]);
    float _S4693 = as_type<float>((&kernelContext_0)->frameParameters_0[(15024U)>>2]);
    float _S4694 = as_type<float>((&kernelContext_0)->frameParameters_0[(15028U)>>2]);
    float _S4695 = as_type<float>((&kernelContext_0)->frameParameters_0[(15032U)>>2]);
    float _S4696 = as_type<float>((&kernelContext_0)->frameParameters_0[(15036U)>>2]);
    float _S4697 = as_type<float>((&kernelContext_0)->frameParameters_0[(15040U)>>2]);
    float _S4698 = as_type<float>((&kernelContext_0)->frameParameters_0[(15044U)>>2]);
    float _S4699 = as_type<float>((&kernelContext_0)->frameParameters_0[(15048U)>>2]);
    float _S4700 = as_type<float>((&kernelContext_0)->frameParameters_0[(15052U)>>2]);
    float4 _S4701 = float4(_S4697, _S4698, _S4699, _S4700);
    float _S4702 = as_type<float>((&kernelContext_0)->frameParameters_0[(15056U)>>2]);
    float _S4703 = as_type<float>((&kernelContext_0)->frameParameters_0[(15060U)>>2]);
    float _S4704 = as_type<float>((&kernelContext_0)->frameParameters_0[(15064U)>>2]);
    float _S4705 = as_type<float>((&kernelContext_0)->frameParameters_0[(15068U)>>2]);
    float4 _S4706 = float4(_S4702, _S4703, _S4704, _S4705);
    float _S4707 = as_type<float>((&kernelContext_0)->frameParameters_0[(15072U)>>2]);
    float _S4708 = as_type<float>((&kernelContext_0)->frameParameters_0[(15076U)>>2]);
    float _S4709 = as_type<float>((&kernelContext_0)->frameParameters_0[(15080U)>>2]);
    float _S4710 = as_type<float>((&kernelContext_0)->frameParameters_0[(15084U)>>2]);
    float4 _S4711 = float4(_S4707, _S4708, _S4709, _S4710);
    float _S4712 = as_type<float>((&kernelContext_0)->frameParameters_0[(15088U)>>2]);
    float _S4713 = as_type<float>((&kernelContext_0)->frameParameters_0[(15092U)>>2]);
    float _S4714 = as_type<float>((&kernelContext_0)->frameParameters_0[(15096U)>>2]);
    float _S4715 = as_type<float>((&kernelContext_0)->frameParameters_0[(15100U)>>2]);
    float4 _S4716 = float4(_S4712, _S4713, _S4714, _S4715);
    float _S4717 = as_type<float>((&kernelContext_0)->frameParameters_0[(15104U)>>2]);
    float _S4718 = as_type<float>((&kernelContext_0)->frameParameters_0[(15108U)>>2]);
    float _S4719 = as_type<float>((&kernelContext_0)->frameParameters_0[(15112U)>>2]);
    float _S4720 = as_type<float>((&kernelContext_0)->frameParameters_0[(15116U)>>2]);
    float4 _S4721 = float4(_S4717, _S4718, _S4719, _S4720);
    float _S4722 = as_type<float>((&kernelContext_0)->frameParameters_0[(15120U)>>2]);
    float _S4723 = as_type<float>((&kernelContext_0)->frameParameters_0[(15124U)>>2]);
    float _S4724 = as_type<float>((&kernelContext_0)->frameParameters_0[(15128U)>>2]);
    float _S4725 = as_type<float>((&kernelContext_0)->frameParameters_0[(15132U)>>2]);
    float4 _S4726 = float4(_S4722, _S4723, _S4724, _S4725);
    float _S4727 = as_type<float>((&kernelContext_0)->frameParameters_0[(15136U)>>2]);
    float _S4728 = as_type<float>((&kernelContext_0)->frameParameters_0[(15140U)>>2]);
    float _S4729 = as_type<float>((&kernelContext_0)->frameParameters_0[(15144U)>>2]);
    float _S4730 = as_type<float>((&kernelContext_0)->frameParameters_0[(15148U)>>2]);
    float4 _S4731 = float4(_S4727, _S4728, _S4729, _S4730);
    float _S4732 = as_type<float>((&kernelContext_0)->frameParameters_0[(15152U)>>2]);
    float _S4733 = as_type<float>((&kernelContext_0)->frameParameters_0[(15156U)>>2]);
    float _S4734 = as_type<float>((&kernelContext_0)->frameParameters_0[(15160U)>>2]);
    float _S4735 = as_type<float>((&kernelContext_0)->frameParameters_0[(15164U)>>2]);
    array<float4, int(8)> _S4736 = { _S4701, _S4706, _S4711, _S4716, _S4721, _S4726, _S4731, float4(_S4732, _S4733, _S4734, _S4735) };
    float _S4737 = as_type<float>((&kernelContext_0)->frameParameters_0[(15168U)>>2]);
    float _S4738 = as_type<float>((&kernelContext_0)->frameParameters_0[(15172U)>>2]);
    float _S4739 = as_type<float>((&kernelContext_0)->frameParameters_0[(15176U)>>2]);
    float _S4740 = as_type<float>((&kernelContext_0)->frameParameters_0[(15180U)>>2]);
    float4 _S4741 = float4(_S4737, _S4738, _S4739, _S4740);
    float _S4742 = as_type<float>((&kernelContext_0)->frameParameters_0[(15184U)>>2]);
    float _S4743 = as_type<float>((&kernelContext_0)->frameParameters_0[(15188U)>>2]);
    float _S4744 = as_type<float>((&kernelContext_0)->frameParameters_0[(15192U)>>2]);
    float _S4745 = as_type<float>((&kernelContext_0)->frameParameters_0[(15196U)>>2]);
    float4 _S4746 = float4(_S4742, _S4743, _S4744, _S4745);
    float _S4747 = as_type<float>((&kernelContext_0)->frameParameters_0[(15200U)>>2]);
    float _S4748 = as_type<float>((&kernelContext_0)->frameParameters_0[(15204U)>>2]);
    float _S4749 = as_type<float>((&kernelContext_0)->frameParameters_0[(15208U)>>2]);
    float _S4750 = as_type<float>((&kernelContext_0)->frameParameters_0[(15212U)>>2]);
    float4 _S4751 = float4(_S4747, _S4748, _S4749, _S4750);
    float _S4752 = as_type<float>((&kernelContext_0)->frameParameters_0[(15216U)>>2]);
    float _S4753 = as_type<float>((&kernelContext_0)->frameParameters_0[(15220U)>>2]);
    float _S4754 = as_type<float>((&kernelContext_0)->frameParameters_0[(15224U)>>2]);
    float _S4755 = as_type<float>((&kernelContext_0)->frameParameters_0[(15228U)>>2]);
    float4 _S4756 = float4(_S4752, _S4753, _S4754, _S4755);
    float _S4757 = as_type<float>((&kernelContext_0)->frameParameters_0[(15232U)>>2]);
    float _S4758 = as_type<float>((&kernelContext_0)->frameParameters_0[(15236U)>>2]);
    float _S4759 = as_type<float>((&kernelContext_0)->frameParameters_0[(15240U)>>2]);
    float _S4760 = as_type<float>((&kernelContext_0)->frameParameters_0[(15244U)>>2]);
    float4 _S4761 = float4(_S4757, _S4758, _S4759, _S4760);
    float _S4762 = as_type<float>((&kernelContext_0)->frameParameters_0[(15248U)>>2]);
    float _S4763 = as_type<float>((&kernelContext_0)->frameParameters_0[(15252U)>>2]);
    float _S4764 = as_type<float>((&kernelContext_0)->frameParameters_0[(15256U)>>2]);
    float _S4765 = as_type<float>((&kernelContext_0)->frameParameters_0[(15260U)>>2]);
    float4 _S4766 = float4(_S4762, _S4763, _S4764, _S4765);
    float _S4767 = as_type<float>((&kernelContext_0)->frameParameters_0[(15264U)>>2]);
    float _S4768 = as_type<float>((&kernelContext_0)->frameParameters_0[(15268U)>>2]);
    float _S4769 = as_type<float>((&kernelContext_0)->frameParameters_0[(15272U)>>2]);
    float _S4770 = as_type<float>((&kernelContext_0)->frameParameters_0[(15276U)>>2]);
    float4 _S4771 = float4(_S4767, _S4768, _S4769, _S4770);
    float _S4772 = as_type<float>((&kernelContext_0)->frameParameters_0[(15280U)>>2]);
    float _S4773 = as_type<float>((&kernelContext_0)->frameParameters_0[(15284U)>>2]);
    float _S4774 = as_type<float>((&kernelContext_0)->frameParameters_0[(15288U)>>2]);
    float _S4775 = as_type<float>((&kernelContext_0)->frameParameters_0[(15292U)>>2]);
    array<float4, int(8)> _S4776 = { _S4741, _S4746, _S4751, _S4756, _S4761, _S4766, _S4771, float4(_S4772, _S4773, _S4774, _S4775) };
    for(;;)
    {
        uint _S4777 = min(_S21, 8U);
        uint index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S4777)
            {
            }
            else
            {
                break;
            }
            if((dot(_S64[index_0].xyz, _S1.eyePosition_0) + _S64[index_0].w) < 0.0f)
            {
                discard_fragment();
            }
            index_0 = index_0 + 1U;
        }
        break;
    }
    float4 _S4778 = float4(_S4->clearcoatShaded_0) ;
    float shadedMode_0 = _S4778.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S4->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S1.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S4->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S1.tint_0.w;
    }
    float4 _S4779 = float4(_S4->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S4779.xyz;
    float _S4780 = _S4779.w;
    float3 unlitColor_0;
    if(unlit_0)
    {
        float3 unlitColor_1 = (diffuseColor_0 + emissiveColor_0) * float3(exp2((as_type<float>((_S23))))) ;
        if(_S22 == 1U)
        {
            unlitColor_0 = unlitColor_1 / (float3(1.0f)  + max(unlitColor_1, float3(0.0f, 0.0f, 0.0f)));
        }
        else
        {
            unlitColor_0 = unlitColor_1;
        }
        pixelOutput_0 _S4781 = { float4(unlitColor_0, opacity_0) };
        return _S4781;
    }
    float4 _S4782 = float4(_S4->reserved_0) ;
    if((_S4782.x) >= 0.5f)
    {
        pixelOutput_0 _S4783 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S4782.y) * max(0.0f, _S4782.z)))) , 1.0f) };
        return _S4783;
    }
    float4 _S4784 = float4(_S4->metallicRoughnessThresholdWorkflow_0) ;
    float _S4785 = saturate(_S4784.x);
    float _S4786 = clamp(_S4784.y, 0.00999999977648258f, 1.0f);
    float4 _S4787 = float4(_S4->specularIor_0) ;
    float3 _S4788 = _S4787.xyz;
    float _S4789 = _S4787.w;
    float _S4790 = _S4778.x;
    float _S4791 = _S4778.y;
    float4 _S4792 = float4(_S4->textureControls_0) ;
    uint textureMask_0 = uint(round(_S4792.x));
    uint udimMask_0 = uint(round(_S4792.y));
    float4 _S4793 = float4(_S4->uvTransformRow0_0) ;
    float4 _S4794 = float4(_S4->uvTransformRow1_0) ;
    float2 _S4795 = float2(dot(_S4793.xy, _S1.texCoord_0) + _S4793.z, dot(_S4794.xy, _S1.texCoord_0) + _S4794.z);
    bool hasSceneLighting_0;
    float4 _S4796;
    float4 _S4797;
    if((textureMask_0 & 2U) != 0U)
    {
        bool _S4798 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S4798)
            {
                _S4796 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S4795)));
                break;
            }
            texture2d<float, access::sample> _S4799 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S4799).get_width(0)),(*((&atlasHeight_0)) = (_S4799).get_height(0));
            int3 _S4800 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S4800)).xy), uint(((_S4800)).z))) * float4(255.0f) );
            int2 _S4801 = int2(metadata_0.zw);
            int2 tile_0 = int2(floor(_S4795)) - int2(metadata_0.xy);
            if(any(tile_0 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_0 >= _S4801);
            }
            if(hasSceneLighting_0)
            {
                int3 _S4802 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S4796 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S4802)).xy), uint(((_S4802)).z)));
                break;
            }
            uint _S4803 = atlasWidth_0 / uint(_S4801.x);
            float _S4804 = float(_S4803);
            uint _S4805 = (atlasHeight_0 - 1U) / uint(_S4801.y);
            float2 cellSize_0 = float2(_S4804, float(_S4805));
            _S4796 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        for(;;)
        {
            float4 _S4806 = float4(_S4->compositeControls_0) ;
            if((_S4806.x) != 2.0f)
            {
                break;
            }
            bool _S4807 = (_S4806.w) >= 0.5f;
            for(;;)
            {
                if(!_S4807)
                {
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S4795)));
                    break;
                }
                texture2d<float, access::sample> _S4808 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S4808).get_width(0)),(*((&atlasHeight_1)) = (_S4808).get_height(0));
                int3 _S4809 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4809)).xy), uint(((_S4809)).z))) * float4(255.0f) );
                int2 _S4810 = int2(metadata_1.zw);
                int2 tile_1 = int2(floor(_S4795)) - int2(metadata_1.xy);
                if(any(tile_1 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_1 >= _S4810);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S4811 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4811)).xy), uint(((_S4811)).z)));
                    break;
                }
                uint _S4812 = atlasWidth_1 / uint(_S4810.x);
                float _S4813 = float(_S4812);
                uint _S4814 = (atlasHeight_1 - 1U) / uint(_S4810.y);
                float2 cellSize_1 = float2(_S4813, float(_S4814));
                _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S4806.y));
            if(operation_0 == 1U)
            {
                _S4796 = _S4796 * _S4797;
                break;
            }
            if(operation_0 == 2U)
            {
                _S4796 = _S4796 + _S4797;
                break;
            }
            if(operation_0 == 3U)
            {
                _S4796 = _S4796 - _S4797;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S4806.z;
                _S4796 = _S4796 * float4((1.0f - factor_0))  + _S4797 * float4(factor_0) ;
                break;
            }
            break;
        }
        diffuseColor_0 = _S4796.xyz;
    }
    float roughness_0;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S4815 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S4815)
            {
                _S4796 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S4795)));
                break;
            }
            texture2d<float, access::sample> _S4816 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S4816).get_width(0)),(*((&atlasHeight_2)) = (_S4816).get_height(0));
            int3 _S4817 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S4817)).xy), uint(((_S4817)).z))) * float4(255.0f) );
            int2 _S4818 = int2(metadata_2.zw);
            int2 tile_2 = int2(floor(_S4795)) - int2(metadata_2.xy);
            if(any(tile_2 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_2 >= _S4818);
            }
            if(hasSceneLighting_0)
            {
                int3 _S4819 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S4796 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S4819)).xy), uint(((_S4819)).z)));
                break;
            }
            uint _S4820 = atlasWidth_2 / uint(_S4818.x);
            float _S4821 = float(_S4820);
            uint _S4822 = (atlasHeight_2 - 1U) / uint(_S4818.y);
            float2 cellSize_2 = float2(_S4821, float(_S4822));
            _S4796 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_2) * cellSize_2 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_2 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        for(;;)
        {
            float4 _S4823 = float4(_S4->compositeControls_0) ;
            if((_S4823.x) != 8.0f)
            {
                break;
            }
            bool _S4824 = (_S4823.w) >= 0.5f;
            for(;;)
            {
                if(!_S4824)
                {
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S4795)));
                    break;
                }
                texture2d<float, access::sample> _S4825 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_3;
                thread uint atlasHeight_3;
                (*((&atlasWidth_3)) = (_S4825).get_width(0)),(*((&atlasHeight_3)) = (_S4825).get_height(0));
                int3 _S4826 = int3(int(0), int(0), int(0));
                float4 metadata_3 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4826)).xy), uint(((_S4826)).z))) * float4(255.0f) );
                int2 _S4827 = int2(metadata_3.zw);
                int2 tile_3 = int2(floor(_S4795)) - int2(metadata_3.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S4827);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S4828 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4828)).xy), uint(((_S4828)).z)));
                    break;
                }
                uint _S4829 = atlasWidth_3 / uint(_S4827.x);
                float _S4830 = float(_S4829);
                uint _S4831 = (atlasHeight_3 - 1U) / uint(_S4827.y);
                float2 cellSize_3 = float2(_S4830, float(_S4831));
                _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            uint operation_1 = uint(round(_S4823.y));
            if(operation_1 == 1U)
            {
                _S4796 = _S4796 * _S4797;
                break;
            }
            if(operation_1 == 2U)
            {
                _S4796 = _S4796 + _S4797;
                break;
            }
            if(operation_1 == 3U)
            {
                _S4796 = _S4796 - _S4797;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S4823.z;
                _S4796 = _S4796 * float4((1.0f - factor_1))  + _S4797 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S4796.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S4786;
    }
    float metallic_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool _S4832 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S4832)
            {
                _S4796 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S4795)));
                break;
            }
            texture2d<float, access::sample> _S4833 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S4833).get_width(0)),(*((&atlasHeight_4)) = (_S4833).get_height(0));
            int3 _S4834 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S4834)).xy), uint(((_S4834)).z))) * float4(255.0f) );
            int2 _S4835 = int2(metadata_4.zw);
            int2 tile_4 = int2(floor(_S4795)) - int2(metadata_4.xy);
            if(any(tile_4 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_4 >= _S4835);
            }
            if(hasSceneLighting_0)
            {
                int3 _S4836 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S4796 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S4836)).xy), uint(((_S4836)).z)));
                break;
            }
            uint _S4837 = atlasWidth_4 / uint(_S4835.x);
            float _S4838 = float(_S4837);
            uint _S4839 = (atlasHeight_4 - 1U) / uint(_S4835.y);
            float2 cellSize_4 = float2(_S4838, float(_S4839));
            _S4796 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_4) * cellSize_4 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_4 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S4840 = float4(_S4->compositeControls_0) ;
            if((_S4840.x) != 32.0f)
            {
                break;
            }
            bool _S4841 = (_S4840.w) >= 0.5f;
            for(;;)
            {
                if(!_S4841)
                {
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S4795)));
                    break;
                }
                texture2d<float, access::sample> _S4842 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S4842).get_width(0)),(*((&atlasHeight_5)) = (_S4842).get_height(0));
                int3 _S4843 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4843)).xy), uint(((_S4843)).z))) * float4(255.0f) );
                int2 _S4844 = int2(metadata_5.zw);
                int2 tile_5 = int2(floor(_S4795)) - int2(metadata_5.xy);
                if(any(tile_5 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_5 >= _S4844);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S4845 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4845)).xy), uint(((_S4845)).z)));
                    break;
                }
                uint _S4846 = atlasWidth_5 / uint(_S4844.x);
                float _S4847 = float(_S4846);
                uint _S4848 = (atlasHeight_5 - 1U) / uint(_S4844.y);
                float2 cellSize_5 = float2(_S4847, float(_S4848));
                _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_2 = uint(round(_S4840.y));
            if(operation_2 == 1U)
            {
                _S4796 = _S4796 * _S4797;
                break;
            }
            if(operation_2 == 2U)
            {
                _S4796 = _S4796 + _S4797;
                break;
            }
            if(operation_2 == 3U)
            {
                _S4796 = _S4796 - _S4797;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S4840.z;
                _S4796 = _S4796 * float4((1.0f - factor_2))  + _S4797 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S4796.x);
    }
    else
    {
        metallic_0 = _S4785;
    }
    if((textureMask_0 & 16U) != 0U)
    {
        bool _S4849 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S4849)
            {
                _S4796 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S4795)));
                break;
            }
            texture2d<float, access::sample> _S4850 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S4850).get_width(0)),(*((&atlasHeight_6)) = (_S4850).get_height(0));
            int3 _S4851 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S4851)).xy), uint(((_S4851)).z))) * float4(255.0f) );
            int2 _S4852 = int2(metadata_6.zw);
            int2 tile_6 = int2(floor(_S4795)) - int2(metadata_6.xy);
            if(any(tile_6 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_6 >= _S4852);
            }
            if(hasSceneLighting_0)
            {
                int3 _S4853 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S4796 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S4853)).xy), uint(((_S4853)).z)));
                break;
            }
            uint _S4854 = atlasWidth_6 / uint(_S4852.x);
            float _S4855 = float(_S4854);
            uint _S4856 = (atlasHeight_6 - 1U) / uint(_S4852.y);
            float2 cellSize_6 = float2(_S4855, float(_S4856));
            _S4796 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_6) * cellSize_6 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_6 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        for(;;)
        {
            float4 _S4857 = float4(_S4->compositeControls_0) ;
            if((_S4857.x) != 16.0f)
            {
                break;
            }
            bool _S4858 = (_S4857.w) >= 0.5f;
            for(;;)
            {
                if(!_S4858)
                {
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S4795)));
                    break;
                }
                texture2d<float, access::sample> _S4859 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S4859).get_width(0)),(*((&atlasHeight_7)) = (_S4859).get_height(0));
                int3 _S4860 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4860)).xy), uint(((_S4860)).z))) * float4(255.0f) );
                int2 _S4861 = int2(metadata_7.zw);
                int2 tile_7 = int2(floor(_S4795)) - int2(metadata_7.xy);
                if(any(tile_7 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_7 >= _S4861);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S4862 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4862)).xy), uint(((_S4862)).z)));
                    break;
                }
                uint _S4863 = atlasWidth_7 / uint(_S4861.x);
                float _S4864 = float(_S4863);
                uint _S4865 = (atlasHeight_7 - 1U) / uint(_S4861.y);
                float2 cellSize_7 = float2(_S4864, float(_S4865));
                _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_3 = uint(round(_S4857.y));
            if(operation_3 == 1U)
            {
                _S4796 = _S4796 * _S4797;
                break;
            }
            if(operation_3 == 2U)
            {
                _S4796 = _S4796 + _S4797;
                break;
            }
            if(operation_3 == 3U)
            {
                _S4796 = _S4796 - _S4797;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S4857.z;
                _S4796 = _S4796 * float4((1.0f - factor_3))  + _S4797 * float4(factor_3) ;
                break;
            }
            break;
        }
        unlitColor_0 = _S4796.xyz;
    }
    else
    {
        unlitColor_0 = emissiveColor_0;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool _S4866 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S4866)
            {
                _S4796 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S4795)));
                break;
            }
            texture2d<float, access::sample> _S4867 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S4867).get_width(0)),(*((&atlasHeight_8)) = (_S4867).get_height(0));
            int3 _S4868 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S4868)).xy), uint(((_S4868)).z))) * float4(255.0f) );
            int2 _S4869 = int2(metadata_8.zw);
            int2 tile_8 = int2(floor(_S4795)) - int2(metadata_8.xy);
            if(any(tile_8 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_8 >= _S4869);
            }
            if(hasSceneLighting_0)
            {
                int3 _S4870 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S4796 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S4870)).xy), uint(((_S4870)).z)));
                break;
            }
            uint _S4871 = atlasWidth_8 / uint(_S4869.x);
            float _S4872 = float(_S4871);
            uint _S4873 = (atlasHeight_8 - 1U) / uint(_S4869.y);
            float2 cellSize_8 = float2(_S4872, float(_S4873));
            _S4796 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_8) * cellSize_8 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_8 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        for(;;)
        {
            float4 _S4874 = float4(_S4->compositeControls_0) ;
            if((_S4874.x) != 64.0f)
            {
                break;
            }
            bool _S4875 = (_S4874.w) >= 0.5f;
            for(;;)
            {
                if(!_S4875)
                {
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S4795)));
                    break;
                }
                texture2d<float, access::sample> _S4876 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S4876).get_width(0)),(*((&atlasHeight_9)) = (_S4876).get_height(0));
                int3 _S4877 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4877)).xy), uint(((_S4877)).z))) * float4(255.0f) );
                int2 _S4878 = int2(metadata_9.zw);
                int2 tile_9 = int2(floor(_S4795)) - int2(metadata_9.xy);
                if(any(tile_9 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_9 >= _S4878);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S4879 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4879)).xy), uint(((_S4879)).z)));
                    break;
                }
                uint _S4880 = atlasWidth_9 / uint(_S4878.x);
                float _S4881 = float(_S4880);
                uint _S4882 = (atlasHeight_9 - 1U) / uint(_S4878.y);
                float2 cellSize_9 = float2(_S4881, float(_S4882));
                _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_4 = uint(round(_S4874.y));
            if(operation_4 == 1U)
            {
                _S4796 = _S4796 * _S4797;
                break;
            }
            if(operation_4 == 2U)
            {
                _S4796 = _S4796 + _S4797;
                break;
            }
            if(operation_4 == 3U)
            {
                _S4796 = _S4796 - _S4797;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S4874.z;
                _S4796 = _S4796 * float4((1.0f - factor_4))  + _S4797 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S4796.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        bool _S4883 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S4883)
            {
                _S4796 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S4795)));
                break;
            }
            texture2d<float, access::sample> _S4884 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S4884).get_width(0)),(*((&atlasHeight_10)) = (_S4884).get_height(0));
            int3 _S4885 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S4885)).xy), uint(((_S4885)).z))) * float4(255.0f) );
            int2 _S4886 = int2(metadata_10.zw);
            int2 tile_10 = int2(floor(_S4795)) - int2(metadata_10.xy);
            if(any(tile_10 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_10 >= _S4886);
            }
            if(hasSceneLighting_0)
            {
                int3 _S4887 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S4796 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S4887)).xy), uint(((_S4887)).z)));
                break;
            }
            uint _S4888 = atlasWidth_10 / uint(_S4886.x);
            float _S4889 = float(_S4888);
            uint _S4890 = (atlasHeight_10 - 1U) / uint(_S4886.y);
            float2 cellSize_10 = float2(_S4889, float(_S4890));
            _S4796 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_10) * cellSize_10 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_10 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S4891 = float4(_S4->compositeControls_0) ;
            if((_S4891.x) != 128.0f)
            {
                break;
            }
            bool _S4892 = (_S4891.w) >= 0.5f;
            for(;;)
            {
                if(!_S4892)
                {
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S4795)));
                    break;
                }
                texture2d<float, access::sample> _S4893 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S4893).get_width(0)),(*((&atlasHeight_11)) = (_S4893).get_height(0));
                int3 _S4894 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4894)).xy), uint(((_S4894)).z))) * float4(255.0f) );
                int2 _S4895 = int2(metadata_11.zw);
                int2 tile_11 = int2(floor(_S4795)) - int2(metadata_11.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S4895);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S4896 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4896)).xy), uint(((_S4896)).z)));
                    break;
                }
                uint _S4897 = atlasWidth_11 / uint(_S4895.x);
                float _S4898 = float(_S4897);
                uint _S4899 = (atlasHeight_11 - 1U) / uint(_S4895.y);
                float2 cellSize_11 = float2(_S4898, float(_S4899));
                _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_5 = uint(round(_S4891.y));
            if(operation_5 == 1U)
            {
                _S4796 = _S4796 * _S4797;
                break;
            }
            if(operation_5 == 2U)
            {
                _S4796 = _S4796 + _S4797;
                break;
            }
            if(operation_5 == 3U)
            {
                _S4796 = _S4796 - _S4797;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S4891.z;
                _S4796 = _S4796 * float4((1.0f - factor_5))  + _S4797 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S4796.x);
    }
    else
    {
        occlusion_0 = _S4780;
    }
    float3 specularColor_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool _S4900 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S4900)
            {
                _S4796 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S4795)));
                break;
            }
            texture2d<float, access::sample> _S4901 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S4901).get_width(0)),(*((&atlasHeight_12)) = (_S4901).get_height(0));
            int3 _S4902 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S4902)).xy), uint(((_S4902)).z))) * float4(255.0f) );
            int2 _S4903 = int2(metadata_12.zw);
            int2 tile_12 = int2(floor(_S4795)) - int2(metadata_12.xy);
            if(any(tile_12 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_12 >= _S4903);
            }
            if(hasSceneLighting_0)
            {
                int3 _S4904 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S4796 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S4904)).xy), uint(((_S4904)).z)));
                break;
            }
            uint _S4905 = atlasWidth_12 / uint(_S4903.x);
            float _S4906 = float(_S4905);
            uint _S4907 = (atlasHeight_12 - 1U) / uint(_S4903.y);
            float2 cellSize_12 = float2(_S4906, float(_S4907));
            _S4796 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_12) * cellSize_12 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_12 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S4908 = float4(_S4->compositeControls_0) ;
            if((_S4908.x) != 256.0f)
            {
                break;
            }
            bool _S4909 = (_S4908.w) >= 0.5f;
            for(;;)
            {
                if(!_S4909)
                {
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S4795)));
                    break;
                }
                texture2d<float, access::sample> _S4910 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S4910).get_width(0)),(*((&atlasHeight_13)) = (_S4910).get_height(0));
                int3 _S4911 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4911)).xy), uint(((_S4911)).z))) * float4(255.0f) );
                int2 _S4912 = int2(metadata_13.zw);
                int2 tile_13 = int2(floor(_S4795)) - int2(metadata_13.xy);
                if(any(tile_13 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_13 >= _S4912);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S4913 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4913)).xy), uint(((_S4913)).z)));
                    break;
                }
                uint _S4914 = atlasWidth_13 / uint(_S4912.x);
                float _S4915 = float(_S4914);
                uint _S4916 = (atlasHeight_13 - 1U) / uint(_S4912.y);
                float2 cellSize_13 = float2(_S4915, float(_S4916));
                _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_6 = uint(round(_S4908.y));
            if(operation_6 == 1U)
            {
                _S4796 = _S4796 * _S4797;
                break;
            }
            if(operation_6 == 2U)
            {
                _S4796 = _S4796 + _S4797;
                break;
            }
            if(operation_6 == 3U)
            {
                _S4796 = _S4796 - _S4797;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S4908.z;
                _S4796 = _S4796 * float4((1.0f - factor_6))  + _S4797 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S4796.xyz);
    }
    else
    {
        specularColor_0 = _S4788;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 512U) != 0U)
    {
        bool _S4917 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S4917)
            {
                _S4796 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S4795)));
                break;
            }
            texture2d<float, access::sample> _S4918 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S4918).get_width(0)),(*((&atlasHeight_14)) = (_S4918).get_height(0));
            int3 _S4919 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S4919)).xy), uint(((_S4919)).z))) * float4(255.0f) );
            int2 _S4920 = int2(metadata_14.zw);
            int2 tile_14 = int2(floor(_S4795)) - int2(metadata_14.xy);
            if(any(tile_14 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_14 >= _S4920);
            }
            if(hasSceneLighting_0)
            {
                int3 _S4921 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S4796 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S4921)).xy), uint(((_S4921)).z)));
                break;
            }
            uint _S4922 = atlasWidth_14 / uint(_S4920.x);
            float _S4923 = float(_S4922);
            uint _S4924 = (atlasHeight_14 - 1U) / uint(_S4920.y);
            float2 cellSize_14 = float2(_S4923, float(_S4924));
            _S4796 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_14) * cellSize_14 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_14 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S4925 = float4(_S4->compositeControls_0) ;
            if((_S4925.x) != 512.0f)
            {
                break;
            }
            bool _S4926 = (_S4925.w) >= 0.5f;
            for(;;)
            {
                if(!_S4926)
                {
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S4795)));
                    break;
                }
                texture2d<float, access::sample> _S4927 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S4927).get_width(0)),(*((&atlasHeight_15)) = (_S4927).get_height(0));
                int3 _S4928 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4928)).xy), uint(((_S4928)).z))) * float4(255.0f) );
                int2 _S4929 = int2(metadata_15.zw);
                int2 tile_15 = int2(floor(_S4795)) - int2(metadata_15.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S4929);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S4930 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4930)).xy), uint(((_S4930)).z)));
                    break;
                }
                uint _S4931 = atlasWidth_15 / uint(_S4929.x);
                float _S4932 = float(_S4931);
                uint _S4933 = (atlasHeight_15 - 1U) / uint(_S4929.y);
                float2 cellSize_15 = float2(_S4932, float(_S4933));
                _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_7 = uint(round(_S4925.y));
            if(operation_7 == 1U)
            {
                _S4796 = _S4796 * _S4797;
                break;
            }
            if(operation_7 == 2U)
            {
                _S4796 = _S4796 + _S4797;
                break;
            }
            if(operation_7 == 3U)
            {
                _S4796 = _S4796 - _S4797;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S4925.z;
                _S4796 = _S4796 * float4((1.0f - factor_7))  + _S4797 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S4796.x);
    }
    else
    {
        clearcoatAmount_0 = _S4790;
    }
    float clearcoatRoughness_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool _S4934 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S4934)
            {
                _S4796 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S4795)));
                break;
            }
            texture2d<float, access::sample> _S4935 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S4935).get_width(0)),(*((&atlasHeight_16)) = (_S4935).get_height(0));
            int3 _S4936 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S4936)).xy), uint(((_S4936)).z))) * float4(255.0f) );
            int2 _S4937 = int2(metadata_16.zw);
            int2 tile_16 = int2(floor(_S4795)) - int2(metadata_16.xy);
            if(any(tile_16 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_16 >= _S4937);
            }
            if(hasSceneLighting_0)
            {
                int3 _S4938 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S4796 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S4938)).xy), uint(((_S4938)).z)));
                break;
            }
            uint _S4939 = atlasWidth_16 / uint(_S4937.x);
            float _S4940 = float(_S4939);
            uint _S4941 = (atlasHeight_16 - 1U) / uint(_S4937.y);
            float2 cellSize_16 = float2(_S4940, float(_S4941));
            _S4796 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_16) * cellSize_16 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_16 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S4942 = float4(_S4->compositeControls_0) ;
            if((_S4942.x) != 1024.0f)
            {
                break;
            }
            bool _S4943 = (_S4942.w) >= 0.5f;
            for(;;)
            {
                if(!_S4943)
                {
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S4795)));
                    break;
                }
                texture2d<float, access::sample> _S4944 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S4944).get_width(0)),(*((&atlasHeight_17)) = (_S4944).get_height(0));
                int3 _S4945 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4945)).xy), uint(((_S4945)).z))) * float4(255.0f) );
                int2 _S4946 = int2(metadata_17.zw);
                int2 tile_17 = int2(floor(_S4795)) - int2(metadata_17.xy);
                if(any(tile_17 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_17 >= _S4946);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S4947 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4947)).xy), uint(((_S4947)).z)));
                    break;
                }
                uint _S4948 = atlasWidth_17 / uint(_S4946.x);
                float _S4949 = float(_S4948);
                uint _S4950 = (atlasHeight_17 - 1U) / uint(_S4946.y);
                float2 cellSize_17 = float2(_S4949, float(_S4950));
                _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_8 = uint(round(_S4942.y));
            if(operation_8 == 1U)
            {
                _S4796 = _S4796 * _S4797;
                break;
            }
            if(operation_8 == 2U)
            {
                _S4796 = _S4796 + _S4797;
                break;
            }
            if(operation_8 == 3U)
            {
                _S4796 = _S4796 - _S4797;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S4942.z;
                _S4796 = _S4796 * float4((1.0f - factor_8))  + _S4797 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S4796.x);
    }
    else
    {
        clearcoatRoughness_0 = _S4791;
    }
    float ior_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool _S4951 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S4951)
            {
                _S4796 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S4795)));
                break;
            }
            texture2d<float, access::sample> _S4952 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S4952).get_width(0)),(*((&atlasHeight_18)) = (_S4952).get_height(0));
            int3 _S4953 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S4953)).xy), uint(((_S4953)).z))) * float4(255.0f) );
            int2 _S4954 = int2(metadata_18.zw);
            int2 tile_18 = int2(floor(_S4795)) - int2(metadata_18.xy);
            if(any(tile_18 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_18 >= _S4954);
            }
            if(hasSceneLighting_0)
            {
                int3 _S4955 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S4796 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S4955)).xy), uint(((_S4955)).z)));
                break;
            }
            uint _S4956 = atlasWidth_18 / uint(_S4954.x);
            float _S4957 = float(_S4956);
            uint _S4958 = (atlasHeight_18 - 1U) / uint(_S4954.y);
            float2 cellSize_18 = float2(_S4957, float(_S4958));
            _S4796 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_18) * cellSize_18 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_18 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S4959 = float4(_S4->compositeControls_0) ;
            if((_S4959.x) != 2048.0f)
            {
                break;
            }
            bool _S4960 = (_S4959.w) >= 0.5f;
            for(;;)
            {
                if(!_S4960)
                {
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S4795)));
                    break;
                }
                texture2d<float, access::sample> _S4961 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S4961).get_width(0)),(*((&atlasHeight_19)) = (_S4961).get_height(0));
                int3 _S4962 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4962)).xy), uint(((_S4962)).z))) * float4(255.0f) );
                int2 _S4963 = int2(metadata_19.zw);
                int2 tile_19 = int2(floor(_S4795)) - int2(metadata_19.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S4963);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S4964 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S4797 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S4964)).xy), uint(((_S4964)).z)));
                    break;
                }
                uint _S4965 = atlasWidth_19 / uint(_S4963.x);
                float _S4966 = float(_S4965);
                uint _S4967 = (atlasHeight_19 - 1U) / uint(_S4963.y);
                float2 cellSize_19 = float2(_S4966, float(_S4967));
                _S4797 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S4795) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_9 = uint(round(_S4959.y));
            if(operation_9 == 1U)
            {
                _S4796 = _S4796 * _S4797;
                break;
            }
            if(operation_9 == 2U)
            {
                _S4796 = _S4796 + _S4797;
                break;
            }
            if(operation_9 == 3U)
            {
                _S4796 = _S4796 - _S4797;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S4959.z;
                _S4796 = _S4796 * float4((1.0f - factor_9))  + _S4797 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S4796.x;
    }
    else
    {
        ior_0 = _S4789;
    }
    float opacityThreshold_0 = _S4784.z;
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
    float3 normal_1 = normalize(_S1.normal_0);
    float3 worldNormal_1 = normalize(_S1.worldNormal_0);
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
    float _S4968 = saturate(abs(dot(normal_2, irradiance_0)) + 0.00000999999974738f);
    float _S4969 = max(0.00100000004749745f, roughness_0);
    float _S4970 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S4971 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S4971;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S4784.w) >= 0.5f)
    {
        float3 _S4972 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S4972;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S4973 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S4973);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S4973);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    if(_S68 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S4974 = float3(_S65, _S66, _S67);
        hasSceneLighting_0 = (dot(_S4974, _S4974)) > 0.0f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = _S4689 >= 0.5f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = _S4692 >= 0.5f;
    }
    uint _S4975 = min(uint(_S68), 128U);
    float3 sceneEye_0 = normalize((((float4(irradiance_0, 0.0f)) * (_S3928))).xyz);
    float3 worldPosition_0 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S3928))).xyz;
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
    float4 _S4976 = float4(_S4->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S4976.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S4->domeLinkControls_0) ).x, 0.0f));
    uint _S4977 = min(uint(max(_S4693, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S4977) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S4978 = float3(_S65, _S66, _S67);
    uint lightCount_0;
    float3 domeAmbient_0;
    if(!allDomesLinked_0)
    {
        float3 _S4979 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_0 = _S4979;
        for(;;)
        {
            if(lightCount_0 < _S4977)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << lightCount_0)) != 0U)
            {
                domeAmbient_0 = domeAmbient_0 + _S4736[lightCount_0].xyz;
            }
            lightCount_0 = lightCount_0 + 1U;
        }
    }
    else
    {
        domeAmbient_0 = _S4978;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_0;
    bool _S4980 = !hasSceneLighting_0;
    if(_S4980)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S4975;
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
        bool _S4981;
        if(hasSceneLighting_0)
        {
            _S4981 = (((uint4(_S4->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S4981 = false;
        }
        if(_S4981)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S4982 = lightIndex_0 == 0U;
        bool _S4983;
        if(_S4982)
        {
            _S4983 = _S4980;
        }
        else
        {
            _S4983 = false;
        }
        float lightType_0;
        if(_S4983)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            lightType_0 = _S708[lightIndex_0].w;
        }
        bool _S4984;
        if(_S4982)
        {
            _S4984 = _S4980;
        }
        else
        {
            _S4984 = false;
        }
        if(_S4984)
        {
            lightDirection_0 = normalize((float4(_S4->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize(_S1348[lightIndex_0].xyz);
        }
        bool _S4985;
        if(_S4982)
        {
            _S4985 = _S4980;
        }
        else
        {
            _S4985 = false;
        }
        if(_S4985)
        {
            roughness_0 = (float4(_S4->lightDirectionIntensity_0) ).w;
        }
        else
        {
            roughness_0 = _S1988[lightIndex_0].w;
        }
        bool _S4986;
        if(_S4982)
        {
            _S4986 = _S4980;
        }
        else
        {
            _S4986 = false;
        }
        if(_S4986)
        {
            diffuseColor_0 = _S4976.xyz;
        }
        else
        {
            diffuseColor_0 = _S1988[lightIndex_0].xyz;
        }
        bool _S4987;
        if(_S4982)
        {
            _S4987 = _S4980;
        }
        else
        {
            _S4987 = false;
        }
        if(_S4987)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            metallic_0 = _S2628[lightIndex_0].x;
        }
        bool _S4988;
        if(_S4982)
        {
            _S4988 = _S4980;
        }
        else
        {
            _S4988 = false;
        }
        if(_S4988)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            clearcoatRoughness_0 = _S2628[lightIndex_0].y;
        }
        bool _S4989;
        if(_S4982)
        {
            _S4989 = _S4980;
        }
        else
        {
            _S4989 = false;
        }
        if(_S4989)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize(_S3268[lightIndex_0].xyz);
        }
        bool _S4990;
        if(_S4982)
        {
            _S4990 = _S4980;
        }
        else
        {
            _S4990 = false;
        }
        if(_S4990)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize(_S3908[lightIndex_0].xyz);
        }
        bool _S4991;
        if(_S4982)
        {
            _S4991 = _S4980;
        }
        else
        {
            _S4991 = false;
        }
        float shapeX_0;
        if(_S4991)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = _S3268[lightIndex_0].w;
        }
        bool _S4992;
        if(_S4982)
        {
            _S4992 = _S4980;
        }
        else
        {
            _S4992 = false;
        }
        float shapeY_0;
        if(_S4992)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = _S3908[lightIndex_0].w;
        }
        bool _S4993;
        if(_S4982)
        {
            _S4993 = _S4980;
        }
        else
        {
            _S4993 = false;
        }
        float lightRadius_0;
        if(_S4993)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = _S1348[lightIndex_0].w;
        }
        bool _S4994;
        if(_S4982)
        {
            _S4994 = _S4980;
        }
        else
        {
            _S4994 = false;
        }
        if(_S4994)
        {
            domeAmbient_0 = irradiance_0;
        }
        else
        {
            domeAmbient_0 = sceneEye_0;
        }
        float3 color_3;
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            int shadowSlot_0 = int(_S4688[lightIndex_0].x);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_3 = - specularColor_0;
                    }
                    else
                    {
                        color_3 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_3, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_3 * float3((_S4048[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S4008[shadowSlot_0])));
                    float _S4995 = lightClip_0.w;
                    if(_S4995 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S4995) ;
                    bool _S4996;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S4996 = true;
                    }
                    else
                    {
                        _S4996 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S4997;
                    if(_S4996)
                    {
                        _S4997 = true;
                    }
                    else
                    {
                        _S4997 = (ndc_0.z) < 0.0f;
                    }
                    bool _S4998;
                    if(_S4997)
                    {
                        _S4998 = true;
                    }
                    else
                    {
                        _S4998 = (ndc_0.z) > 1.0f;
                    }
                    if(_S4998)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S4999 = _S4028[shadowSlot_0].xy;
                    float2 _S5000 = _S4028[shadowSlot_0].zw;
                    float2 _S5001 = _S4999 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S5000;
                    float texel_0 = _S4048[shadowSlot_0].w;
                    float _S5002 = max(_S4048[shadowSlot_0].z, 0.0f);
                    float _S5003 = ndc_0.z - _S4048[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S5004 = float2((texel_0 * 0.5f)) ;
                    float2 _S5005 = _S4999 + _S5004;
                    float2 _S5006 = _S4999 + _S5000 - _S5004;
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
                            if(_S5003 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S5001 + float2(float(x_0), float(y_0)) * float2((_S5002 * texel_0)) , _S5005, _S5006)), level((0.0f))).x))
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
        float3 _S5007 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S5007;
        sampleOffsets_0[int(1)] = _S5007;
        sampleOffsets_0[int(2)] = _S5007;
        sampleOffsets_0[int(3)] = _S5007;
        sampleOffsets_0[int(4)] = _S5007;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S5008 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S5008 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S5008 - halfHeight_0;
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
                float3 toLight_0 = _S708[lightIndex_0].xyz + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S5009 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S5009)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S5009;
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
            float3 half_0 = normalize(sampleDirection_0 + domeAmbient_0);
            float normalDotLight_0 = saturate(dot(normal_2, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(normal_2, half_0));
            float3 _S5010 = float3(pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_0, half_0))), 5.0f)) ;
            float3 _S5011 = mix(normalIncidence_0, grazingIncidence_0, _S5010);
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - _S5011);
            float _S5012 = max(_S4969, 0.00100000004749745f);
            float alpha_0 = _S5012 * _S5012;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S5013 = 1.0f - k_0;
            float3 _S5014 = float3(max(4.0f * normalDotLight_0 * _S4968, 1.00000000317107685e-30f)) ;
            float3 _S5015 = _S5011 * float3((_S4968 / (_S4968 * _S5013 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S5013 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S5014;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S5016 = max(_S4970, 0.00100000004749745f);
                float alpha_1 = _S5016 * _S5016;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S5017 = 1.0f - k_1;
                directSpecular_0 = _S5015 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S5010) * float3((_S4968 / (_S4968 * _S5017 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S5017 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S5014);
            }
            else
            {
                directSpecular_0 = _S5015;
            }
            float3 _S5018 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S5018 * _S4971) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S5018);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    if(_S4689 >= 0.5f)
    {
        float _S5019 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S5020 = saturate(_S4969);
        float2 _S5021 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S5019, _S5020)), level((0.0f)))).xy;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S5021.x)  + grazingIncidence_0 * float3(_S5021.y) ;
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - specularWeight_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S5022 = max(_S4694, 1.0f);
        float3 _S5023 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S5022 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            for(;;)
            {
                bool _S5024 = _S5022 <= 1.0f;
                _S2 = _S5024;
                if(_S5024)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S4696, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (_S4695 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S5022)), level((0.0f)))).xyz;
                break;
            }
            for(;;)
            {
                if(_S2)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S5025 = max(_S4690, 1.0f);
                    float _S5026 = _S5025 - 1.0f;
                    float slice_0 = _S5020 * max(_S5026, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S4691, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S5025)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S5026) + v_0) / _S5025)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S5027 = max(_S4690, 1.0f);
                float total_0 = _S5027 * _S5022;
                float _S5028 = _S5027 - 1.0f;
                float slice_1 = _S5020 * max(_S5028, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S4691, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = _S4695 * _S5027;
                specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S5028) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S2)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_2 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S5029 = max(_S4690, 1.0f);
                        float _S5030 = _S5029 - 1.0f;
                        float slice_2 = saturate(_S4970) * max(_S5030, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S4691, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S5029)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S5030) + v_2) / _S5029)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S5031 = max(_S4690, 1.0f);
                    float total_1 = _S5031 * _S5022;
                    float _S5032 = _S5031 - 1.0f;
                    float slice_3 = saturate(_S4970) * max(_S5032, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S4691, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = _S4695 * _S5031;
                    irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S5032) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightTangent_0 = irradiance_0;
            }
            else
            {
                lightTangent_0 = _S5023;
            }
            irradiance_0 = diffuseColor_0;
            lightDirection_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S5023;
            lightDirection_0 = _S5023;
            lightTangent_0 = _S5023;
            for(;;)
            {
                if(sampleIndex_0 < _S4977)
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
                float domeGroup_0 = _S4776[sampleIndex_0].x;
                if(domeGroup_0 < 0.0f)
                {
                    sampleIndex_0 = sampleIndex_0 + 1U;
                    continue;
                }
                for(;;)
                {
                    bool _S5033 = _S5022 <= 1.0f;
                    _S3 = _S5033;
                    if(_S5033)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S4696, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S5022)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                for(;;)
                {
                    if(_S3)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S5034 = max(_S4690, 1.0f);
                        float _S5035 = _S5034 - 1.0f;
                        float slice_4 = _S5020 * max(_S5035, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S4691, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S5034)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S5035) + v_4) / _S5034)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S5036 = max(_S4690, 1.0f);
                    float total_2 = _S5036 * _S5022;
                    float _S5037 = _S5036 - 1.0f;
                    float slice_5 = _S5020 * max(_S5037, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S4691, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S5036;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S5037) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightDirection_0 + specularColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S3)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S5038 = max(_S4690, 1.0f);
                            float _S5039 = _S5038 - 1.0f;
                            float slice_6 = saturate(_S4970) * max(_S5039, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S4691, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S5038)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S5039) + v_6) / _S5038)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S5040 = max(_S4690, 1.0f);
                        float total_3 = _S5040 * _S5022;
                        float _S5041 = _S5040 - 1.0f;
                        float slice_7 = saturate(_S4970) * max(_S5041, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S4691, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S5040;
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S5041) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S5042 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S5019, saturate(_S4970))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightTangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S5042.x)  + float3(_S5042.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + unlitColor_0) * float3(exp2((as_type<float>((_S23))))) ;
    if(_S22 == 1U)
    {
        color_2 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S5043 = { float4(color_2, opacity_0) };
    return _S5043;
}
