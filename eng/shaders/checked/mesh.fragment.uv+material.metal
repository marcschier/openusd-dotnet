#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct FrameVectorView_0
{
    uint byteOffset_0;
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

float4 FrameVectorView_operatorx5Bx5D_get_0(const FrameVectorView_0 thread* this_0, uint index_0, KernelContext_0 thread* kernelContext_0)
{
    uint _S1 = this_0->byteOffset_0 + index_0 * 16U;
    float _S2 = as_type<float>(kernelContext_0->frameParameters_0[(_S1)>>2]);
    float _S3 = as_type<float>(kernelContext_0->frameParameters_0[(_S1 + 4U)>>2]);
    float _S4 = as_type<float>(kernelContext_0->frameParameters_0[(_S1 + 8U)>>2]);
    float _S5 = as_type<float>(kernelContext_0->frameParameters_0[(_S1 + 12U)>>2]);
    return float4(_S2, _S3, _S4, _S5);
}

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

[[fragment]] pixelOutput_0 fragmentMain_uv_material(pixelInput_0 _S6 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_1;
    FragmentFrameParameters_0 _S7;
    uint4 _S8;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    bool _S9;
    bool _S10;
    thread KernelContext_0 kernelContext_1;
    (&kernelContext_1)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_1)->frameParameters_0 = frameParameters_1;
    (&kernelContext_1)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_1)->baseColorSampler_0 = baseColorSampler_1;
    (&kernelContext_1)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_1)->compositeSampler_0 = compositeSampler_1;
    (&kernelContext_1)->roughnessMetallicTexture_0 = roughnessMetallicTexture_1;
    (&kernelContext_1)->roughnessMetallicSampler_0 = roughnessMetallicSampler_1;
    (&kernelContext_1)->metallicTexture_0 = metallicTexture_1;
    (&kernelContext_1)->metallicSampler_0 = metallicSampler_1;
    (&kernelContext_1)->emissiveTexture_0 = emissiveTexture_1;
    (&kernelContext_1)->emissiveSampler_0 = emissiveSampler_1;
    (&kernelContext_1)->opacityTexture_0 = opacityTexture_1;
    (&kernelContext_1)->opacitySampler_0 = opacitySampler_1;
    (&kernelContext_1)->occlusionTexture_0 = occlusionTexture_1;
    (&kernelContext_1)->occlusionSampler_0 = occlusionSampler_1;
    (&kernelContext_1)->specularColorTexture_0 = specularColorTexture_1;
    (&kernelContext_1)->specularColorSampler_0 = specularColorSampler_1;
    (&kernelContext_1)->clearcoatTexture_0 = clearcoatTexture_1;
    (&kernelContext_1)->clearcoatSampler_0 = clearcoatSampler_1;
    (&kernelContext_1)->clearcoatRoughnessTexture_0 = clearcoatRoughnessTexture_1;
    (&kernelContext_1)->clearcoatRoughnessSampler_0 = clearcoatRoughnessSampler_1;
    (&kernelContext_1)->iorTexture_0 = iorTexture_1;
    (&kernelContext_1)->iorSampler_0 = iorSampler_1;
    (&kernelContext_1)->shadowAtlas_0 = shadowAtlas_1;
    (&kernelContext_1)->shadowSampler_0 = shadowSampler_1;
    (&kernelContext_1)->environmentBrdf_0 = environmentBrdf_1;
    (&kernelContext_1)->environmentBrdfSampler_0 = environmentBrdfSampler_1;
    (&kernelContext_1)->environmentIrradiance_0 = environmentIrradiance_1;
    (&kernelContext_1)->environmentSampler_0 = environmentSampler_1;
    (&kernelContext_1)->environmentSpecular_0 = environmentSpecular_1;
    SurfaceParameters_natural_0 device* _S11 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        uint _S12 = as_type<uint>((&kernelContext_1)->frameParameters_0[(64U)>>2]);
        uint _S13 = as_type<uint>((&kernelContext_1)->frameParameters_0[(68U)>>2]);
        uint _S14 = as_type<uint>((&kernelContext_1)->frameParameters_0[(72U)>>2]);
        uint _S15 = as_type<uint>((&kernelContext_1)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S12, _S13, _S14, _S15);
        index_1 = 0U;
        for(;;)
        {
            if(index_1 < 8U)
            {
            }
            else
            {
                break;
            }
            uint _S16 = 80U + index_1 * 16U;
            float _S17 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S16)>>2]);
            float _S18 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S16 + 4U)>>2]);
            float _S19 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S16 + 8U)>>2]);
            float _S20 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S16 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_1] = float4(_S17, _S18, _S19, _S20);
            index_1 = index_1 + 1U;
        }
        float _S21 = as_type<float>((&kernelContext_1)->frameParameters_0[(208U)>>2]);
        float _S22 = as_type<float>((&kernelContext_1)->frameParameters_0[(212U)>>2]);
        float _S23 = as_type<float>((&kernelContext_1)->frameParameters_0[(216U)>>2]);
        float _S24 = as_type<float>((&kernelContext_1)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S21, _S22, _S23, _S24);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 12960U;
        float _S25 = as_type<float>((&kernelContext_1)->frameParameters_0[(12512U)>>2]);
        float _S26 = as_type<float>((&kernelContext_1)->frameParameters_0[(12516U)>>2]);
        float _S27 = as_type<float>((&kernelContext_1)->frameParameters_0[(12520U)>>2]);
        float _S28 = as_type<float>((&kernelContext_1)->frameParameters_0[(12524U)>>2]);
        float4 _S29 = float4(_S25, _S26, _S27, _S28);
        float _S30 = as_type<float>((&kernelContext_1)->frameParameters_0[(12528U)>>2]);
        float _S31 = as_type<float>((&kernelContext_1)->frameParameters_0[(12532U)>>2]);
        float _S32 = as_type<float>((&kernelContext_1)->frameParameters_0[(12536U)>>2]);
        float _S33 = as_type<float>((&kernelContext_1)->frameParameters_0[(12540U)>>2]);
        float4 _S34 = float4(_S30, _S31, _S32, _S33);
        float _S35 = as_type<float>((&kernelContext_1)->frameParameters_0[(12544U)>>2]);
        float _S36 = as_type<float>((&kernelContext_1)->frameParameters_0[(12548U)>>2]);
        float _S37 = as_type<float>((&kernelContext_1)->frameParameters_0[(12552U)>>2]);
        float _S38 = as_type<float>((&kernelContext_1)->frameParameters_0[(12556U)>>2]);
        float4 _S39 = float4(_S35, _S36, _S37, _S38);
        float _S40 = as_type<float>((&kernelContext_1)->frameParameters_0[(12560U)>>2]);
        float _S41 = as_type<float>((&kernelContext_1)->frameParameters_0[(12564U)>>2]);
        float _S42 = as_type<float>((&kernelContext_1)->frameParameters_0[(12568U)>>2]);
        float _S43 = as_type<float>((&kernelContext_1)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S29, _S34, _S39, float4(_S40, _S41, _S42, _S43));
        index_1 = 0U;
        for(;;)
        {
            if(index_1 < 4U)
            {
            }
            else
            {
                break;
            }
            uint _S44 = 12576U + index_1 * 64U;
            float _S45 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44)>>2]);
            float _S46 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 4U)>>2]);
            float _S47 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 8U)>>2]);
            float _S48 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 12U)>>2]);
            float4 _S49 = float4(_S45, _S46, _S47, _S48);
            float _S50 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 16U)>>2]);
            float _S51 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 20U)>>2]);
            float _S52 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 24U)>>2]);
            float _S53 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 28U)>>2]);
            float4 _S54 = float4(_S50, _S51, _S52, _S53);
            float _S55 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 32U)>>2]);
            float _S56 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 36U)>>2]);
            float _S57 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 40U)>>2]);
            float _S58 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 44U)>>2]);
            float4 _S59 = float4(_S55, _S56, _S57, _S58);
            float _S60 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 48U)>>2]);
            float _S61 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 52U)>>2]);
            float _S62 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 56U)>>2]);
            float _S63 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S44 + 60U)>>2]);
            (&frame_0)->shadowWorldToLightClip_0[index_1] = matrix<float,int(4),int(4)> (_S49, _S54, _S59, float4(_S60, _S61, _S62, _S63));
            uint _S64 = index_1 * 16U;
            uint _S65 = 12832U + _S64;
            float _S66 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S65)>>2]);
            float _S67 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S65 + 4U)>>2]);
            float _S68 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S65 + 8U)>>2]);
            float _S69 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S65 + 12U)>>2]);
            (&frame_0)->shadowTile_0[index_1] = float4(_S66, _S67, _S68, _S69);
            uint _S70 = 12896U + _S64;
            float _S71 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S70)>>2]);
            float _S72 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S70 + 4U)>>2]);
            float _S73 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S70 + 8U)>>2]);
            float _S74 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S70 + 12U)>>2]);
            (&frame_0)->shadowControls_0[index_1] = float4(_S71, _S72, _S73, _S74);
            index_1 = index_1 + 1U;
        }
        float _S75 = as_type<float>((&kernelContext_1)->frameParameters_0[(15008U)>>2]);
        float _S76 = as_type<float>((&kernelContext_1)->frameParameters_0[(15012U)>>2]);
        float _S77 = as_type<float>((&kernelContext_1)->frameParameters_0[(15016U)>>2]);
        float _S78 = as_type<float>((&kernelContext_1)->frameParameters_0[(15020U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S75, _S76, _S77, _S78);
        float _S79 = as_type<float>((&kernelContext_1)->frameParameters_0[(15024U)>>2]);
        float _S80 = as_type<float>((&kernelContext_1)->frameParameters_0[(15028U)>>2]);
        float _S81 = as_type<float>((&kernelContext_1)->frameParameters_0[(15032U)>>2]);
        float _S82 = as_type<float>((&kernelContext_1)->frameParameters_0[(15036U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S79, _S80, _S81, _S82);
        float _S83 = as_type<float>((&kernelContext_1)->frameParameters_0[(15040U)>>2]);
        float _S84 = as_type<float>((&kernelContext_1)->frameParameters_0[(15044U)>>2]);
        float _S85 = as_type<float>((&kernelContext_1)->frameParameters_0[(15048U)>>2]);
        float _S86 = as_type<float>((&kernelContext_1)->frameParameters_0[(15052U)>>2]);
        float4 _S87 = float4(_S83, _S84, _S85, _S86);
        float _S88 = as_type<float>((&kernelContext_1)->frameParameters_0[(15056U)>>2]);
        float _S89 = as_type<float>((&kernelContext_1)->frameParameters_0[(15060U)>>2]);
        float _S90 = as_type<float>((&kernelContext_1)->frameParameters_0[(15064U)>>2]);
        float _S91 = as_type<float>((&kernelContext_1)->frameParameters_0[(15068U)>>2]);
        float4 _S92 = float4(_S88, _S89, _S90, _S91);
        float _S93 = as_type<float>((&kernelContext_1)->frameParameters_0[(15072U)>>2]);
        float _S94 = as_type<float>((&kernelContext_1)->frameParameters_0[(15076U)>>2]);
        float _S95 = as_type<float>((&kernelContext_1)->frameParameters_0[(15080U)>>2]);
        float _S96 = as_type<float>((&kernelContext_1)->frameParameters_0[(15084U)>>2]);
        float4 _S97 = float4(_S93, _S94, _S95, _S96);
        float _S98 = as_type<float>((&kernelContext_1)->frameParameters_0[(15088U)>>2]);
        float _S99 = as_type<float>((&kernelContext_1)->frameParameters_0[(15092U)>>2]);
        float _S100 = as_type<float>((&kernelContext_1)->frameParameters_0[(15096U)>>2]);
        float _S101 = as_type<float>((&kernelContext_1)->frameParameters_0[(15100U)>>2]);
        float4 _S102 = float4(_S98, _S99, _S100, _S101);
        float _S103 = as_type<float>((&kernelContext_1)->frameParameters_0[(15104U)>>2]);
        float _S104 = as_type<float>((&kernelContext_1)->frameParameters_0[(15108U)>>2]);
        float _S105 = as_type<float>((&kernelContext_1)->frameParameters_0[(15112U)>>2]);
        float _S106 = as_type<float>((&kernelContext_1)->frameParameters_0[(15116U)>>2]);
        float4 _S107 = float4(_S103, _S104, _S105, _S106);
        float _S108 = as_type<float>((&kernelContext_1)->frameParameters_0[(15120U)>>2]);
        float _S109 = as_type<float>((&kernelContext_1)->frameParameters_0[(15124U)>>2]);
        float _S110 = as_type<float>((&kernelContext_1)->frameParameters_0[(15128U)>>2]);
        float _S111 = as_type<float>((&kernelContext_1)->frameParameters_0[(15132U)>>2]);
        float4 _S112 = float4(_S108, _S109, _S110, _S111);
        float _S113 = as_type<float>((&kernelContext_1)->frameParameters_0[(15136U)>>2]);
        float _S114 = as_type<float>((&kernelContext_1)->frameParameters_0[(15140U)>>2]);
        float _S115 = as_type<float>((&kernelContext_1)->frameParameters_0[(15144U)>>2]);
        float _S116 = as_type<float>((&kernelContext_1)->frameParameters_0[(15148U)>>2]);
        float4 _S117 = float4(_S113, _S114, _S115, _S116);
        float _S118 = as_type<float>((&kernelContext_1)->frameParameters_0[(15152U)>>2]);
        float _S119 = as_type<float>((&kernelContext_1)->frameParameters_0[(15156U)>>2]);
        float _S120 = as_type<float>((&kernelContext_1)->frameParameters_0[(15160U)>>2]);
        float _S121 = as_type<float>((&kernelContext_1)->frameParameters_0[(15164U)>>2]);
        float4 _S122 = float4(_S118, _S119, _S120, _S121);
        float _S123 = as_type<float>((&kernelContext_1)->frameParameters_0[(15168U)>>2]);
        float _S124 = as_type<float>((&kernelContext_1)->frameParameters_0[(15172U)>>2]);
        float _S125 = as_type<float>((&kernelContext_1)->frameParameters_0[(15176U)>>2]);
        float _S126 = as_type<float>((&kernelContext_1)->frameParameters_0[(15180U)>>2]);
        float4 _S127 = float4(_S123, _S124, _S125, _S126);
        float _S128 = as_type<float>((&kernelContext_1)->frameParameters_0[(15184U)>>2]);
        float _S129 = as_type<float>((&kernelContext_1)->frameParameters_0[(15188U)>>2]);
        float _S130 = as_type<float>((&kernelContext_1)->frameParameters_0[(15192U)>>2]);
        float _S131 = as_type<float>((&kernelContext_1)->frameParameters_0[(15196U)>>2]);
        float4 _S132 = float4(_S128, _S129, _S130, _S131);
        float _S133 = as_type<float>((&kernelContext_1)->frameParameters_0[(15200U)>>2]);
        float _S134 = as_type<float>((&kernelContext_1)->frameParameters_0[(15204U)>>2]);
        float _S135 = as_type<float>((&kernelContext_1)->frameParameters_0[(15208U)>>2]);
        float _S136 = as_type<float>((&kernelContext_1)->frameParameters_0[(15212U)>>2]);
        float4 _S137 = float4(_S133, _S134, _S135, _S136);
        float _S138 = as_type<float>((&kernelContext_1)->frameParameters_0[(15216U)>>2]);
        float _S139 = as_type<float>((&kernelContext_1)->frameParameters_0[(15220U)>>2]);
        float _S140 = as_type<float>((&kernelContext_1)->frameParameters_0[(15224U)>>2]);
        float _S141 = as_type<float>((&kernelContext_1)->frameParameters_0[(15228U)>>2]);
        float4 _S142 = float4(_S138, _S139, _S140, _S141);
        float _S143 = as_type<float>((&kernelContext_1)->frameParameters_0[(15232U)>>2]);
        float _S144 = as_type<float>((&kernelContext_1)->frameParameters_0[(15236U)>>2]);
        float _S145 = as_type<float>((&kernelContext_1)->frameParameters_0[(15240U)>>2]);
        float _S146 = as_type<float>((&kernelContext_1)->frameParameters_0[(15244U)>>2]);
        float4 _S147 = float4(_S143, _S144, _S145, _S146);
        float _S148 = as_type<float>((&kernelContext_1)->frameParameters_0[(15248U)>>2]);
        float _S149 = as_type<float>((&kernelContext_1)->frameParameters_0[(15252U)>>2]);
        float _S150 = as_type<float>((&kernelContext_1)->frameParameters_0[(15256U)>>2]);
        float _S151 = as_type<float>((&kernelContext_1)->frameParameters_0[(15260U)>>2]);
        float4 _S152 = float4(_S148, _S149, _S150, _S151);
        float _S153 = as_type<float>((&kernelContext_1)->frameParameters_0[(15264U)>>2]);
        float _S154 = as_type<float>((&kernelContext_1)->frameParameters_0[(15268U)>>2]);
        float _S155 = as_type<float>((&kernelContext_1)->frameParameters_0[(15272U)>>2]);
        float _S156 = as_type<float>((&kernelContext_1)->frameParameters_0[(15276U)>>2]);
        float4 _S157 = float4(_S153, _S154, _S155, _S156);
        float _S158 = as_type<float>((&kernelContext_1)->frameParameters_0[(15280U)>>2]);
        float _S159 = as_type<float>((&kernelContext_1)->frameParameters_0[(15284U)>>2]);
        float _S160 = as_type<float>((&kernelContext_1)->frameParameters_0[(15288U)>>2]);
        float _S161 = as_type<float>((&kernelContext_1)->frameParameters_0[(15292U)>>2]);
        float4 _S162 = float4(_S158, _S159, _S160, _S161);
        (&frame_0)->domeAmbient_0[int(0)] = _S87;
        (&frame_0)->domeAmbient_0[int(1)] = _S92;
        (&frame_0)->domeAmbient_0[int(2)] = _S97;
        (&frame_0)->domeAmbient_0[int(3)] = _S102;
        (&frame_0)->domeAmbient_0[int(4)] = _S107;
        (&frame_0)->domeAmbient_0[int(5)] = _S112;
        (&frame_0)->domeAmbient_0[int(6)] = _S117;
        (&frame_0)->domeAmbient_0[int(7)] = _S122;
        (&frame_0)->domeEnvironment_0[int(0)] = _S127;
        (&frame_0)->domeEnvironment_0[int(1)] = _S132;
        (&frame_0)->domeEnvironment_0[int(2)] = _S137;
        (&frame_0)->domeEnvironment_0[int(3)] = _S142;
        (&frame_0)->domeEnvironment_0[int(4)] = _S147;
        (&frame_0)->domeEnvironment_0[int(5)] = _S152;
        (&frame_0)->domeEnvironment_0[int(6)] = _S157;
        (&frame_0)->domeEnvironment_0[int(7)] = _S162;
        _S7 = frame_0;
        break;
    }
    for(;;)
    {
        _S8 = _S7.clipPlaneCount_0;
        uint _S163 = min(_S7.clipPlaneCount_0.x, 8U);
        index_1 = 0U;
        for(;;)
        {
            if(index_1 < _S163)
            {
            }
            else
            {
                break;
            }
            if((dot(_S7.clipPlanes_0[index_1].xyz, _S6.eyePosition_0) + _S7.clipPlanes_0[index_1].w) < 0.0f)
            {
                discard_fragment();
            }
            index_1 = index_1 + 1U;
        }
        break;
    }
    float4 _S164 = float4(_S11->clearcoatShaded_0) ;
    float shadedMode_0 = _S164.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S11->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S6.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S11->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S6.tint_0.w;
    }
    float4 _S165 = float4(_S11->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S165.xyz;
    float _S166 = _S165.w;
    float3 unlitColor_0;
    if(unlit_0)
    {
        float3 unlitColor_1 = (diffuseColor_0 + emissiveColor_0) * float3(exp2((as_type<float>((_S8.z))))) ;
        if((_S8.y) == 1U)
        {
            unlitColor_0 = unlitColor_1 / (float3(1.0f)  + max(unlitColor_1, float3(0.0f, 0.0f, 0.0f)));
        }
        else
        {
            unlitColor_0 = unlitColor_1;
        }
        pixelOutput_0 _S167 = { float4(unlitColor_0, opacity_0) };
        return _S167;
    }
    float4 _S168 = float4(_S11->reserved_0) ;
    if((_S168.x) >= 0.5f)
    {
        pixelOutput_0 _S169 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S168.y) * max(0.0f, _S168.z)))) , 1.0f) };
        return _S169;
    }
    float4 _S170 = float4(_S11->metallicRoughnessThresholdWorkflow_0) ;
    float _S171 = saturate(_S170.x);
    float _S172 = clamp(_S170.y, 0.00999999977648258f, 1.0f);
    float4 _S173 = float4(_S11->specularIor_0) ;
    float3 _S174 = _S173.xyz;
    float _S175 = _S173.w;
    float _S176 = _S164.x;
    float _S177 = _S164.y;
    float4 _S178 = float4(_S11->textureControls_0) ;
    uint textureMask_0 = uint(round(_S178.x));
    uint udimMask_0 = uint(round(_S178.y));
    float4 _S179 = float4(_S11->uvTransformRow0_0) ;
    float4 _S180 = float4(_S11->uvTransformRow1_0) ;
    float2 _S181 = float2(dot(_S179.xy, _S6.texCoord_0) + _S179.z, dot(_S180.xy, _S6.texCoord_0) + _S180.z);
    bool hasSceneLighting_0;
    float4 _S182;
    float4 _S183;
    if((textureMask_0 & 2U) != 0U)
    {
        bool _S184 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S184)
            {
                _S182 = (((&kernelContext_1)->baseColorTexture_0).sample(((&kernelContext_1)->baseColorSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S185 = (&kernelContext_1)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S185).get_width(0)),(*((&atlasHeight_0)) = (_S185).get_height(0));
            int3 _S186 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_1)->baseColorTexture_0).read(vec<uint,2>(((_S186)).xy), uint(((_S186)).z))) * float4(255.0f) );
            int2 _S187 = int2(metadata_0.zw);
            int2 tile_0 = int2(floor(_S181)) - int2(metadata_0.xy);
            if(any(tile_0 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_0 >= _S187);
            }
            if(hasSceneLighting_0)
            {
                int3 _S188 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_1)->baseColorTexture_0).read(vec<uint,2>(((_S188)).xy), uint(((_S188)).z)));
                break;
            }
            uint _S189 = atlasWidth_0 / uint(_S187.x);
            float _S190 = float(_S189);
            uint _S191 = (atlasHeight_0 - 1U) / uint(_S187.y);
            float2 cellSize_0 = float2(_S190, float(_S191));
            _S182 = (((&kernelContext_1)->baseColorTexture_0).sample(((&kernelContext_1)->baseColorSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        for(;;)
        {
            float4 _S192 = float4(_S11->compositeControls_0) ;
            if((_S192.x) != 2.0f)
            {
                break;
            }
            bool _S193 = (_S192.w) >= 0.5f;
            for(;;)
            {
                if(!_S193)
                {
                    _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), (_S181)));
                    break;
                }
                texture2d<float, access::sample> _S194 = (&kernelContext_1)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S194).get_width(0)),(*((&atlasHeight_1)) = (_S194).get_height(0));
                int3 _S195 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S195)).xy), uint(((_S195)).z))) * float4(255.0f) );
                int2 _S196 = int2(metadata_1.zw);
                int2 tile_1 = int2(floor(_S181)) - int2(metadata_1.xy);
                if(any(tile_1 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_1 >= _S196);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S197 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S197)).xy), uint(((_S197)).z)));
                    break;
                }
                uint _S198 = atlasWidth_1 / uint(_S196.x);
                float _S199 = float(_S198);
                uint _S200 = (atlasHeight_1 - 1U) / uint(_S196.y);
                float2 cellSize_1 = float2(_S199, float(_S200));
                _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S192.y));
            if(operation_0 == 1U)
            {
                _S182 = _S182 * _S183;
                break;
            }
            if(operation_0 == 2U)
            {
                _S182 = _S182 + _S183;
                break;
            }
            if(operation_0 == 3U)
            {
                _S182 = _S182 - _S183;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S192.z;
                _S182 = _S182 * float4((1.0f - factor_0))  + _S183 * float4(factor_0) ;
                break;
            }
            break;
        }
        diffuseColor_0 = _S182.xyz;
    }
    float roughness_0;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S201 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S201)
            {
                _S182 = (((&kernelContext_1)->roughnessMetallicTexture_0).sample(((&kernelContext_1)->roughnessMetallicSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S202 = (&kernelContext_1)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S202).get_width(0)),(*((&atlasHeight_2)) = (_S202).get_height(0));
            int3 _S203 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_1)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S203)).xy), uint(((_S203)).z))) * float4(255.0f) );
            int2 _S204 = int2(metadata_2.zw);
            int2 tile_2 = int2(floor(_S181)) - int2(metadata_2.xy);
            if(any(tile_2 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_2 >= _S204);
            }
            if(hasSceneLighting_0)
            {
                int3 _S205 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_1)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S205)).xy), uint(((_S205)).z)));
                break;
            }
            uint _S206 = atlasWidth_2 / uint(_S204.x);
            float _S207 = float(_S206);
            uint _S208 = (atlasHeight_2 - 1U) / uint(_S204.y);
            float2 cellSize_2 = float2(_S207, float(_S208));
            _S182 = (((&kernelContext_1)->roughnessMetallicTexture_0).sample(((&kernelContext_1)->roughnessMetallicSampler_0), ((float2(tile_2) * cellSize_2 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_2 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        for(;;)
        {
            float4 _S209 = float4(_S11->compositeControls_0) ;
            if((_S209.x) != 8.0f)
            {
                break;
            }
            bool _S210 = (_S209.w) >= 0.5f;
            for(;;)
            {
                if(!_S210)
                {
                    _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), (_S181)));
                    break;
                }
                texture2d<float, access::sample> _S211 = (&kernelContext_1)->compositeTexture_0;
                thread uint atlasWidth_3;
                thread uint atlasHeight_3;
                (*((&atlasWidth_3)) = (_S211).get_width(0)),(*((&atlasHeight_3)) = (_S211).get_height(0));
                int3 _S212 = int3(int(0), int(0), int(0));
                float4 metadata_3 = round((((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S212)).xy), uint(((_S212)).z))) * float4(255.0f) );
                int2 _S213 = int2(metadata_3.zw);
                int2 tile_3 = int2(floor(_S181)) - int2(metadata_3.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S213);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S214 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S214)).xy), uint(((_S214)).z)));
                    break;
                }
                uint _S215 = atlasWidth_3 / uint(_S213.x);
                float _S216 = float(_S215);
                uint _S217 = (atlasHeight_3 - 1U) / uint(_S213.y);
                float2 cellSize_3 = float2(_S216, float(_S217));
                _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            uint operation_1 = uint(round(_S209.y));
            if(operation_1 == 1U)
            {
                _S182 = _S182 * _S183;
                break;
            }
            if(operation_1 == 2U)
            {
                _S182 = _S182 + _S183;
                break;
            }
            if(operation_1 == 3U)
            {
                _S182 = _S182 - _S183;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S209.z;
                _S182 = _S182 * float4((1.0f - factor_1))  + _S183 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S182.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S172;
    }
    float metallic_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool _S218 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S218)
            {
                _S182 = (((&kernelContext_1)->metallicTexture_0).sample(((&kernelContext_1)->metallicSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S219 = (&kernelContext_1)->metallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S219).get_width(0)),(*((&atlasHeight_4)) = (_S219).get_height(0));
            int3 _S220 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_1)->metallicTexture_0).read(vec<uint,2>(((_S220)).xy), uint(((_S220)).z))) * float4(255.0f) );
            int2 _S221 = int2(metadata_4.zw);
            int2 tile_4 = int2(floor(_S181)) - int2(metadata_4.xy);
            if(any(tile_4 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_4 >= _S221);
            }
            if(hasSceneLighting_0)
            {
                int3 _S222 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_1)->metallicTexture_0).read(vec<uint,2>(((_S222)).xy), uint(((_S222)).z)));
                break;
            }
            uint _S223 = atlasWidth_4 / uint(_S221.x);
            float _S224 = float(_S223);
            uint _S225 = (atlasHeight_4 - 1U) / uint(_S221.y);
            float2 cellSize_4 = float2(_S224, float(_S225));
            _S182 = (((&kernelContext_1)->metallicTexture_0).sample(((&kernelContext_1)->metallicSampler_0), ((float2(tile_4) * cellSize_4 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_4 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S226 = float4(_S11->compositeControls_0) ;
            if((_S226.x) != 32.0f)
            {
                break;
            }
            bool _S227 = (_S226.w) >= 0.5f;
            for(;;)
            {
                if(!_S227)
                {
                    _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), (_S181)));
                    break;
                }
                texture2d<float, access::sample> _S228 = (&kernelContext_1)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S228).get_width(0)),(*((&atlasHeight_5)) = (_S228).get_height(0));
                int3 _S229 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S229)).xy), uint(((_S229)).z))) * float4(255.0f) );
                int2 _S230 = int2(metadata_5.zw);
                int2 tile_5 = int2(floor(_S181)) - int2(metadata_5.xy);
                if(any(tile_5 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_5 >= _S230);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S231 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S231)).xy), uint(((_S231)).z)));
                    break;
                }
                uint _S232 = atlasWidth_5 / uint(_S230.x);
                float _S233 = float(_S232);
                uint _S234 = (atlasHeight_5 - 1U) / uint(_S230.y);
                float2 cellSize_5 = float2(_S233, float(_S234));
                _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_2 = uint(round(_S226.y));
            if(operation_2 == 1U)
            {
                _S182 = _S182 * _S183;
                break;
            }
            if(operation_2 == 2U)
            {
                _S182 = _S182 + _S183;
                break;
            }
            if(operation_2 == 3U)
            {
                _S182 = _S182 - _S183;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S226.z;
                _S182 = _S182 * float4((1.0f - factor_2))  + _S183 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S182.x);
    }
    else
    {
        metallic_0 = _S171;
    }
    if((textureMask_0 & 16U) != 0U)
    {
        bool _S235 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S235)
            {
                _S182 = (((&kernelContext_1)->emissiveTexture_0).sample(((&kernelContext_1)->emissiveSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S236 = (&kernelContext_1)->emissiveTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S236).get_width(0)),(*((&atlasHeight_6)) = (_S236).get_height(0));
            int3 _S237 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_1)->emissiveTexture_0).read(vec<uint,2>(((_S237)).xy), uint(((_S237)).z))) * float4(255.0f) );
            int2 _S238 = int2(metadata_6.zw);
            int2 tile_6 = int2(floor(_S181)) - int2(metadata_6.xy);
            if(any(tile_6 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_6 >= _S238);
            }
            if(hasSceneLighting_0)
            {
                int3 _S239 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_1)->emissiveTexture_0).read(vec<uint,2>(((_S239)).xy), uint(((_S239)).z)));
                break;
            }
            uint _S240 = atlasWidth_6 / uint(_S238.x);
            float _S241 = float(_S240);
            uint _S242 = (atlasHeight_6 - 1U) / uint(_S238.y);
            float2 cellSize_6 = float2(_S241, float(_S242));
            _S182 = (((&kernelContext_1)->emissiveTexture_0).sample(((&kernelContext_1)->emissiveSampler_0), ((float2(tile_6) * cellSize_6 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_6 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        for(;;)
        {
            float4 _S243 = float4(_S11->compositeControls_0) ;
            if((_S243.x) != 16.0f)
            {
                break;
            }
            bool _S244 = (_S243.w) >= 0.5f;
            for(;;)
            {
                if(!_S244)
                {
                    _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), (_S181)));
                    break;
                }
                texture2d<float, access::sample> _S245 = (&kernelContext_1)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S245).get_width(0)),(*((&atlasHeight_7)) = (_S245).get_height(0));
                int3 _S246 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S246)).xy), uint(((_S246)).z))) * float4(255.0f) );
                int2 _S247 = int2(metadata_7.zw);
                int2 tile_7 = int2(floor(_S181)) - int2(metadata_7.xy);
                if(any(tile_7 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_7 >= _S247);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S248 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S248)).xy), uint(((_S248)).z)));
                    break;
                }
                uint _S249 = atlasWidth_7 / uint(_S247.x);
                float _S250 = float(_S249);
                uint _S251 = (atlasHeight_7 - 1U) / uint(_S247.y);
                float2 cellSize_7 = float2(_S250, float(_S251));
                _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_3 = uint(round(_S243.y));
            if(operation_3 == 1U)
            {
                _S182 = _S182 * _S183;
                break;
            }
            if(operation_3 == 2U)
            {
                _S182 = _S182 + _S183;
                break;
            }
            if(operation_3 == 3U)
            {
                _S182 = _S182 - _S183;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S243.z;
                _S182 = _S182 * float4((1.0f - factor_3))  + _S183 * float4(factor_3) ;
                break;
            }
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
        bool _S252 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S252)
            {
                _S182 = (((&kernelContext_1)->opacityTexture_0).sample(((&kernelContext_1)->opacitySampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S253 = (&kernelContext_1)->opacityTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S253).get_width(0)),(*((&atlasHeight_8)) = (_S253).get_height(0));
            int3 _S254 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_1)->opacityTexture_0).read(vec<uint,2>(((_S254)).xy), uint(((_S254)).z))) * float4(255.0f) );
            int2 _S255 = int2(metadata_8.zw);
            int2 tile_8 = int2(floor(_S181)) - int2(metadata_8.xy);
            if(any(tile_8 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_8 >= _S255);
            }
            if(hasSceneLighting_0)
            {
                int3 _S256 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_1)->opacityTexture_0).read(vec<uint,2>(((_S256)).xy), uint(((_S256)).z)));
                break;
            }
            uint _S257 = atlasWidth_8 / uint(_S255.x);
            float _S258 = float(_S257);
            uint _S259 = (atlasHeight_8 - 1U) / uint(_S255.y);
            float2 cellSize_8 = float2(_S258, float(_S259));
            _S182 = (((&kernelContext_1)->opacityTexture_0).sample(((&kernelContext_1)->opacitySampler_0), ((float2(tile_8) * cellSize_8 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_8 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        for(;;)
        {
            float4 _S260 = float4(_S11->compositeControls_0) ;
            if((_S260.x) != 64.0f)
            {
                break;
            }
            bool _S261 = (_S260.w) >= 0.5f;
            for(;;)
            {
                if(!_S261)
                {
                    _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), (_S181)));
                    break;
                }
                texture2d<float, access::sample> _S262 = (&kernelContext_1)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S262).get_width(0)),(*((&atlasHeight_9)) = (_S262).get_height(0));
                int3 _S263 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S263)).xy), uint(((_S263)).z))) * float4(255.0f) );
                int2 _S264 = int2(metadata_9.zw);
                int2 tile_9 = int2(floor(_S181)) - int2(metadata_9.xy);
                if(any(tile_9 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_9 >= _S264);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S265 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S265)).xy), uint(((_S265)).z)));
                    break;
                }
                uint _S266 = atlasWidth_9 / uint(_S264.x);
                float _S267 = float(_S266);
                uint _S268 = (atlasHeight_9 - 1U) / uint(_S264.y);
                float2 cellSize_9 = float2(_S267, float(_S268));
                _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_4 = uint(round(_S260.y));
            if(operation_4 == 1U)
            {
                _S182 = _S182 * _S183;
                break;
            }
            if(operation_4 == 2U)
            {
                _S182 = _S182 + _S183;
                break;
            }
            if(operation_4 == 3U)
            {
                _S182 = _S182 - _S183;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S260.z;
                _S182 = _S182 * float4((1.0f - factor_4))  + _S183 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S182.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        bool _S269 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S269)
            {
                _S182 = (((&kernelContext_1)->occlusionTexture_0).sample(((&kernelContext_1)->occlusionSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S270 = (&kernelContext_1)->occlusionTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S270).get_width(0)),(*((&atlasHeight_10)) = (_S270).get_height(0));
            int3 _S271 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_1)->occlusionTexture_0).read(vec<uint,2>(((_S271)).xy), uint(((_S271)).z))) * float4(255.0f) );
            int2 _S272 = int2(metadata_10.zw);
            int2 tile_10 = int2(floor(_S181)) - int2(metadata_10.xy);
            if(any(tile_10 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_10 >= _S272);
            }
            if(hasSceneLighting_0)
            {
                int3 _S273 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_1)->occlusionTexture_0).read(vec<uint,2>(((_S273)).xy), uint(((_S273)).z)));
                break;
            }
            uint _S274 = atlasWidth_10 / uint(_S272.x);
            float _S275 = float(_S274);
            uint _S276 = (atlasHeight_10 - 1U) / uint(_S272.y);
            float2 cellSize_10 = float2(_S275, float(_S276));
            _S182 = (((&kernelContext_1)->occlusionTexture_0).sample(((&kernelContext_1)->occlusionSampler_0), ((float2(tile_10) * cellSize_10 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_10 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S277 = float4(_S11->compositeControls_0) ;
            if((_S277.x) != 128.0f)
            {
                break;
            }
            bool _S278 = (_S277.w) >= 0.5f;
            for(;;)
            {
                if(!_S278)
                {
                    _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), (_S181)));
                    break;
                }
                texture2d<float, access::sample> _S279 = (&kernelContext_1)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S279).get_width(0)),(*((&atlasHeight_11)) = (_S279).get_height(0));
                int3 _S280 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S280)).xy), uint(((_S280)).z))) * float4(255.0f) );
                int2 _S281 = int2(metadata_11.zw);
                int2 tile_11 = int2(floor(_S181)) - int2(metadata_11.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S281);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S282 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S282)).xy), uint(((_S282)).z)));
                    break;
                }
                uint _S283 = atlasWidth_11 / uint(_S281.x);
                float _S284 = float(_S283);
                uint _S285 = (atlasHeight_11 - 1U) / uint(_S281.y);
                float2 cellSize_11 = float2(_S284, float(_S285));
                _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_5 = uint(round(_S277.y));
            if(operation_5 == 1U)
            {
                _S182 = _S182 * _S183;
                break;
            }
            if(operation_5 == 2U)
            {
                _S182 = _S182 + _S183;
                break;
            }
            if(operation_5 == 3U)
            {
                _S182 = _S182 - _S183;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S277.z;
                _S182 = _S182 * float4((1.0f - factor_5))  + _S183 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S182.x);
    }
    else
    {
        occlusion_0 = _S166;
    }
    float3 specularColor_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool _S286 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S286)
            {
                _S182 = (((&kernelContext_1)->specularColorTexture_0).sample(((&kernelContext_1)->specularColorSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S287 = (&kernelContext_1)->specularColorTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S287).get_width(0)),(*((&atlasHeight_12)) = (_S287).get_height(0));
            int3 _S288 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_1)->specularColorTexture_0).read(vec<uint,2>(((_S288)).xy), uint(((_S288)).z))) * float4(255.0f) );
            int2 _S289 = int2(metadata_12.zw);
            int2 tile_12 = int2(floor(_S181)) - int2(metadata_12.xy);
            if(any(tile_12 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_12 >= _S289);
            }
            if(hasSceneLighting_0)
            {
                int3 _S290 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_1)->specularColorTexture_0).read(vec<uint,2>(((_S290)).xy), uint(((_S290)).z)));
                break;
            }
            uint _S291 = atlasWidth_12 / uint(_S289.x);
            float _S292 = float(_S291);
            uint _S293 = (atlasHeight_12 - 1U) / uint(_S289.y);
            float2 cellSize_12 = float2(_S292, float(_S293));
            _S182 = (((&kernelContext_1)->specularColorTexture_0).sample(((&kernelContext_1)->specularColorSampler_0), ((float2(tile_12) * cellSize_12 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_12 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S294 = float4(_S11->compositeControls_0) ;
            if((_S294.x) != 256.0f)
            {
                break;
            }
            bool _S295 = (_S294.w) >= 0.5f;
            for(;;)
            {
                if(!_S295)
                {
                    _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), (_S181)));
                    break;
                }
                texture2d<float, access::sample> _S296 = (&kernelContext_1)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S296).get_width(0)),(*((&atlasHeight_13)) = (_S296).get_height(0));
                int3 _S297 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S297)).xy), uint(((_S297)).z))) * float4(255.0f) );
                int2 _S298 = int2(metadata_13.zw);
                int2 tile_13 = int2(floor(_S181)) - int2(metadata_13.xy);
                if(any(tile_13 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_13 >= _S298);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S299 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S299)).xy), uint(((_S299)).z)));
                    break;
                }
                uint _S300 = atlasWidth_13 / uint(_S298.x);
                float _S301 = float(_S300);
                uint _S302 = (atlasHeight_13 - 1U) / uint(_S298.y);
                float2 cellSize_13 = float2(_S301, float(_S302));
                _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_6 = uint(round(_S294.y));
            if(operation_6 == 1U)
            {
                _S182 = _S182 * _S183;
                break;
            }
            if(operation_6 == 2U)
            {
                _S182 = _S182 + _S183;
                break;
            }
            if(operation_6 == 3U)
            {
                _S182 = _S182 - _S183;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S294.z;
                _S182 = _S182 * float4((1.0f - factor_6))  + _S183 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S182.xyz);
    }
    else
    {
        specularColor_0 = _S174;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 512U) != 0U)
    {
        bool _S303 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S303)
            {
                _S182 = (((&kernelContext_1)->clearcoatTexture_0).sample(((&kernelContext_1)->clearcoatSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S304 = (&kernelContext_1)->clearcoatTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S304).get_width(0)),(*((&atlasHeight_14)) = (_S304).get_height(0));
            int3 _S305 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_1)->clearcoatTexture_0).read(vec<uint,2>(((_S305)).xy), uint(((_S305)).z))) * float4(255.0f) );
            int2 _S306 = int2(metadata_14.zw);
            int2 tile_14 = int2(floor(_S181)) - int2(metadata_14.xy);
            if(any(tile_14 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_14 >= _S306);
            }
            if(hasSceneLighting_0)
            {
                int3 _S307 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_1)->clearcoatTexture_0).read(vec<uint,2>(((_S307)).xy), uint(((_S307)).z)));
                break;
            }
            uint _S308 = atlasWidth_14 / uint(_S306.x);
            float _S309 = float(_S308);
            uint _S310 = (atlasHeight_14 - 1U) / uint(_S306.y);
            float2 cellSize_14 = float2(_S309, float(_S310));
            _S182 = (((&kernelContext_1)->clearcoatTexture_0).sample(((&kernelContext_1)->clearcoatSampler_0), ((float2(tile_14) * cellSize_14 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_14 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S311 = float4(_S11->compositeControls_0) ;
            if((_S311.x) != 512.0f)
            {
                break;
            }
            bool _S312 = (_S311.w) >= 0.5f;
            for(;;)
            {
                if(!_S312)
                {
                    _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), (_S181)));
                    break;
                }
                texture2d<float, access::sample> _S313 = (&kernelContext_1)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S313).get_width(0)),(*((&atlasHeight_15)) = (_S313).get_height(0));
                int3 _S314 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S314)).xy), uint(((_S314)).z))) * float4(255.0f) );
                int2 _S315 = int2(metadata_15.zw);
                int2 tile_15 = int2(floor(_S181)) - int2(metadata_15.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S315);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S316 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S316)).xy), uint(((_S316)).z)));
                    break;
                }
                uint _S317 = atlasWidth_15 / uint(_S315.x);
                float _S318 = float(_S317);
                uint _S319 = (atlasHeight_15 - 1U) / uint(_S315.y);
                float2 cellSize_15 = float2(_S318, float(_S319));
                _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_7 = uint(round(_S311.y));
            if(operation_7 == 1U)
            {
                _S182 = _S182 * _S183;
                break;
            }
            if(operation_7 == 2U)
            {
                _S182 = _S182 + _S183;
                break;
            }
            if(operation_7 == 3U)
            {
                _S182 = _S182 - _S183;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S311.z;
                _S182 = _S182 * float4((1.0f - factor_7))  + _S183 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S182.x);
    }
    else
    {
        clearcoatAmount_0 = _S176;
    }
    float clearcoatRoughness_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool _S320 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S320)
            {
                _S182 = (((&kernelContext_1)->clearcoatRoughnessTexture_0).sample(((&kernelContext_1)->clearcoatRoughnessSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S321 = (&kernelContext_1)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S321).get_width(0)),(*((&atlasHeight_16)) = (_S321).get_height(0));
            int3 _S322 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_1)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S322)).xy), uint(((_S322)).z))) * float4(255.0f) );
            int2 _S323 = int2(metadata_16.zw);
            int2 tile_16 = int2(floor(_S181)) - int2(metadata_16.xy);
            if(any(tile_16 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_16 >= _S323);
            }
            if(hasSceneLighting_0)
            {
                int3 _S324 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_1)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S324)).xy), uint(((_S324)).z)));
                break;
            }
            uint _S325 = atlasWidth_16 / uint(_S323.x);
            float _S326 = float(_S325);
            uint _S327 = (atlasHeight_16 - 1U) / uint(_S323.y);
            float2 cellSize_16 = float2(_S326, float(_S327));
            _S182 = (((&kernelContext_1)->clearcoatRoughnessTexture_0).sample(((&kernelContext_1)->clearcoatRoughnessSampler_0), ((float2(tile_16) * cellSize_16 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_16 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S328 = float4(_S11->compositeControls_0) ;
            if((_S328.x) != 1024.0f)
            {
                break;
            }
            bool _S329 = (_S328.w) >= 0.5f;
            for(;;)
            {
                if(!_S329)
                {
                    _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), (_S181)));
                    break;
                }
                texture2d<float, access::sample> _S330 = (&kernelContext_1)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S330).get_width(0)),(*((&atlasHeight_17)) = (_S330).get_height(0));
                int3 _S331 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S331)).xy), uint(((_S331)).z))) * float4(255.0f) );
                int2 _S332 = int2(metadata_17.zw);
                int2 tile_17 = int2(floor(_S181)) - int2(metadata_17.xy);
                if(any(tile_17 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_17 >= _S332);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S333 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S333)).xy), uint(((_S333)).z)));
                    break;
                }
                uint _S334 = atlasWidth_17 / uint(_S332.x);
                float _S335 = float(_S334);
                uint _S336 = (atlasHeight_17 - 1U) / uint(_S332.y);
                float2 cellSize_17 = float2(_S335, float(_S336));
                _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_8 = uint(round(_S328.y));
            if(operation_8 == 1U)
            {
                _S182 = _S182 * _S183;
                break;
            }
            if(operation_8 == 2U)
            {
                _S182 = _S182 + _S183;
                break;
            }
            if(operation_8 == 3U)
            {
                _S182 = _S182 - _S183;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S328.z;
                _S182 = _S182 * float4((1.0f - factor_8))  + _S183 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S182.x);
    }
    else
    {
        clearcoatRoughness_0 = _S177;
    }
    float ior_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool _S337 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S337)
            {
                _S182 = (((&kernelContext_1)->iorTexture_0).sample(((&kernelContext_1)->iorSampler_0), (_S181)));
                break;
            }
            texture2d<float, access::sample> _S338 = (&kernelContext_1)->iorTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S338).get_width(0)),(*((&atlasHeight_18)) = (_S338).get_height(0));
            int3 _S339 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_1)->iorTexture_0).read(vec<uint,2>(((_S339)).xy), uint(((_S339)).z))) * float4(255.0f) );
            int2 _S340 = int2(metadata_18.zw);
            int2 tile_18 = int2(floor(_S181)) - int2(metadata_18.xy);
            if(any(tile_18 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_18 >= _S340);
            }
            if(hasSceneLighting_0)
            {
                int3 _S341 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S182 = (((&kernelContext_1)->iorTexture_0).read(vec<uint,2>(((_S341)).xy), uint(((_S341)).z)));
                break;
            }
            uint _S342 = atlasWidth_18 / uint(_S340.x);
            float _S343 = float(_S342);
            uint _S344 = (atlasHeight_18 - 1U) / uint(_S340.y);
            float2 cellSize_18 = float2(_S343, float(_S344));
            _S182 = (((&kernelContext_1)->iorTexture_0).sample(((&kernelContext_1)->iorSampler_0), ((float2(tile_18) * cellSize_18 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_18 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S345 = float4(_S11->compositeControls_0) ;
            if((_S345.x) != 2048.0f)
            {
                break;
            }
            bool _S346 = (_S345.w) >= 0.5f;
            for(;;)
            {
                if(!_S346)
                {
                    _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), (_S181)));
                    break;
                }
                texture2d<float, access::sample> _S347 = (&kernelContext_1)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S347).get_width(0)),(*((&atlasHeight_19)) = (_S347).get_height(0));
                int3 _S348 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S348)).xy), uint(((_S348)).z))) * float4(255.0f) );
                int2 _S349 = int2(metadata_19.zw);
                int2 tile_19 = int2(floor(_S181)) - int2(metadata_19.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S349);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S350 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S183 = (((&kernelContext_1)->compositeTexture_0).read(vec<uint,2>(((_S350)).xy), uint(((_S350)).z)));
                    break;
                }
                uint _S351 = atlasWidth_19 / uint(_S349.x);
                float _S352 = float(_S351);
                uint _S353 = (atlasHeight_19 - 1U) / uint(_S349.y);
                float2 cellSize_19 = float2(_S352, float(_S353));
                _S183 = (((&kernelContext_1)->compositeTexture_0).sample(((&kernelContext_1)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S181) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_9 = uint(round(_S345.y));
            if(operation_9 == 1U)
            {
                _S182 = _S182 * _S183;
                break;
            }
            if(operation_9 == 2U)
            {
                _S182 = _S182 + _S183;
                break;
            }
            if(operation_9 == 3U)
            {
                _S182 = _S182 - _S183;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S345.z;
                _S182 = _S182 * float4((1.0f - factor_9))  + _S183 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S182.x;
    }
    else
    {
        ior_0 = _S175;
    }
    float opacityThreshold_0 = _S170.z;
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
    float3 normal_1 = normalize(_S6.normal_0);
    float3 worldNormal_1 = normalize(_S6.worldNormal_0);
    float lengthSquared_0 = dot(_S6.eyePosition_0, _S6.eyePosition_0);
    float3 irradiance_0;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        irradiance_0 = - _S6.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
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
    float _S354 = saturate(abs(dot(normal_2, irradiance_0)) + 0.00000999999974738f);
    float _S355 = max(0.00100000004749745f, roughness_0);
    float _S356 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S357 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S357;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S170.w) >= 0.5f)
    {
        float3 _S358 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S358;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S359 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S359);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S359);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    FragmentFrameParameters_0 _S360 = _S7;
    float _S361 = _S7.ambientLight_0.w;
    if(_S361 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S362 = _S360.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S362, _S362)) > 0.0f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S7.environmentControls_0.x) >= 0.5f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S7.environmentControls_0.w) >= 0.5f;
    }
    uint _S363 = min(uint(_S361), 128U);
    float3 sceneEye_0 = normalize((((float4(irradiance_0, 0.0f)) * (_S7.eyeToWorld_0))).xyz);
    float3 worldPosition_0 = (((float4(_S6.eyePosition_0, 1.0f)) * (_S7.eyeToWorld_0))).xyz;
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
    float4 _S364 = float4(_S11->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S364.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S11->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S365 = _S7;
    uint _S366 = min(uint(max(_S7.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S366) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S367 = _S360.ambientLight_0.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S368 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S368;
        for(;;)
        {
            if(lightCount_0 < _S366)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << lightCount_0)) != 0U)
            {
                domeAmbient_1 = domeAmbient_1 + _S7.domeAmbient_0[lightCount_0].xyz;
            }
            lightCount_0 = lightCount_0 + 1U;
        }
    }
    else
    {
        domeAmbient_1 = _S367;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S369 = !hasSceneLighting_0;
    if(_S369)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S363;
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
        bool _S370;
        if(hasSceneLighting_0)
        {
            _S370 = (((uint4(_S11->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S370 = false;
        }
        if(_S370)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S371 = lightIndex_0 == 0U;
        bool _S372;
        if(_S371)
        {
            _S372 = _S369;
        }
        else
        {
            _S372 = false;
        }
        float lightType_0;
        if(_S372)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            thread FrameVectorView_0 _S373 = _S7.lightPositionType_0;
            float4 _S374 = FrameVectorView_operatorx5Bx5D_get_0(&_S373, lightIndex_0, &kernelContext_1);
            lightType_0 = _S374.w;
        }
        bool _S375;
        if(_S371)
        {
            _S375 = _S369;
        }
        else
        {
            _S375 = false;
        }
        if(_S375)
        {
            lightDirection_0 = normalize((float4(_S11->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            thread FrameVectorView_0 _S376 = _S7.lightDirectionRadius_0;
            float4 _S377 = FrameVectorView_operatorx5Bx5D_get_0(&_S376, lightIndex_0, &kernelContext_1);
            lightDirection_0 = normalize(_S377.xyz);
        }
        bool _S378;
        if(_S371)
        {
            _S378 = _S369;
        }
        else
        {
            _S378 = false;
        }
        if(_S378)
        {
            roughness_0 = (float4(_S11->lightDirectionIntensity_0) ).w;
        }
        else
        {
            thread FrameVectorView_0 _S379 = _S7.lightColorIntensity_0;
            float4 _S380 = FrameVectorView_operatorx5Bx5D_get_0(&_S379, lightIndex_0, &kernelContext_1);
            roughness_0 = _S380.w;
        }
        bool _S381;
        if(_S371)
        {
            _S381 = _S369;
        }
        else
        {
            _S381 = false;
        }
        if(_S381)
        {
            diffuseColor_0 = _S364.xyz;
        }
        else
        {
            thread FrameVectorView_0 _S382 = _S7.lightColorIntensity_0;
            float4 _S383 = FrameVectorView_operatorx5Bx5D_get_0(&_S382, lightIndex_0, &kernelContext_1);
            diffuseColor_0 = _S383.xyz;
        }
        bool _S384;
        if(_S371)
        {
            _S384 = _S369;
        }
        else
        {
            _S384 = false;
        }
        if(_S384)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            thread FrameVectorView_0 _S385 = _S7.lightControls_0;
            float4 _S386 = FrameVectorView_operatorx5Bx5D_get_0(&_S385, lightIndex_0, &kernelContext_1);
            metallic_0 = _S386.x;
        }
        bool _S387;
        if(_S371)
        {
            _S387 = _S369;
        }
        else
        {
            _S387 = false;
        }
        if(_S387)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            thread FrameVectorView_0 _S388 = _S7.lightControls_0;
            float4 _S389 = FrameVectorView_operatorx5Bx5D_get_0(&_S388, lightIndex_0, &kernelContext_1);
            clearcoatRoughness_0 = _S389.y;
        }
        bool _S390;
        if(_S371)
        {
            _S390 = _S369;
        }
        else
        {
            _S390 = false;
        }
        if(_S390)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            thread FrameVectorView_0 _S391 = _S7.lightTangentShapeX_0;
            float4 _S392 = FrameVectorView_operatorx5Bx5D_get_0(&_S391, lightIndex_0, &kernelContext_1);
            lightTangent_0 = normalize(_S392.xyz);
        }
        bool _S393;
        if(_S371)
        {
            _S393 = _S369;
        }
        else
        {
            _S393 = false;
        }
        if(_S393)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            thread FrameVectorView_0 _S394 = _S7.lightBitangentShapeY_0;
            float4 _S395 = FrameVectorView_operatorx5Bx5D_get_0(&_S394, lightIndex_0, &kernelContext_1);
            lightBitangent_0 = normalize(_S395.xyz);
        }
        bool _S396;
        if(_S371)
        {
            _S396 = _S369;
        }
        else
        {
            _S396 = false;
        }
        float shapeX_0;
        if(_S396)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            thread FrameVectorView_0 _S397 = _S7.lightTangentShapeX_0;
            float4 _S398 = FrameVectorView_operatorx5Bx5D_get_0(&_S397, lightIndex_0, &kernelContext_1);
            shapeX_0 = _S398.w;
        }
        bool _S399;
        if(_S371)
        {
            _S399 = _S369;
        }
        else
        {
            _S399 = false;
        }
        float shapeY_0;
        if(_S399)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            thread FrameVectorView_0 _S400 = _S7.lightBitangentShapeY_0;
            float4 _S401 = FrameVectorView_operatorx5Bx5D_get_0(&_S400, lightIndex_0, &kernelContext_1);
            shapeY_0 = _S401.w;
        }
        bool _S402;
        if(_S371)
        {
            _S402 = _S369;
        }
        else
        {
            _S402 = false;
        }
        float lightRadius_0;
        if(_S402)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            thread FrameVectorView_0 _S403 = _S7.lightDirectionRadius_0;
            float4 _S404 = FrameVectorView_operatorx5Bx5D_get_0(&_S403, lightIndex_0, &kernelContext_1);
            lightRadius_0 = _S404.w;
        }
        bool _S405;
        if(_S371)
        {
            _S405 = _S369;
        }
        else
        {
            _S405 = false;
        }
        if(_S405)
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
            thread FrameVectorView_0 _S406 = _S7.shadowSlots_0;
            float4 _S407 = FrameVectorView_operatorx5Bx5D_get_0(&_S406, lightIndex_0, &kernelContext_1);
            int shadowSlot_0 = int(_S407.x);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S408 = _S7;
                    FragmentFrameParameters_0 _S409 = _S7;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_3 = - specularColor_0;
                    }
                    else
                    {
                        color_3 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_3, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_3 * float3((_S408.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S7.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S410 = lightClip_0.w;
                    if(_S410 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S410) ;
                    bool _S411;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S411 = true;
                    }
                    else
                    {
                        _S411 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S412;
                    if(_S411)
                    {
                        _S412 = true;
                    }
                    else
                    {
                        _S412 = (ndc_0.z) < 0.0f;
                    }
                    bool _S413;
                    if(_S412)
                    {
                        _S413 = true;
                    }
                    else
                    {
                        _S413 = (ndc_0.z) > 1.0f;
                    }
                    if(_S413)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S414 = _S409.shadowTile_0[shadowSlot_0].xy;
                    float2 _S415 = _S409.shadowTile_0[shadowSlot_0].zw;
                    float2 _S416 = _S414 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S415;
                    float texel_0 = _S408.shadowControls_0[shadowSlot_0].w;
                    float _S417 = max(_S408.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S418 = ndc_0.z - _S408.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S419 = float2((texel_0 * 0.5f)) ;
                    float2 _S420 = _S414 + _S419;
                    float2 _S421 = _S414 + _S415 - _S419;
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
                            if(_S418 <= (((&kernelContext_1)->shadowAtlas_0).sample(((&kernelContext_1)->shadowSampler_0), (clamp(_S416 + float2(float(x_0), float(y_0)) * float2((_S417 * texel_0)) , _S420, _S421)), level((0.0f))).x))
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
        float3 _S422 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S422;
        sampleOffsets_0[int(1)] = _S422;
        sampleOffsets_0[int(2)] = _S422;
        sampleOffsets_0[int(3)] = _S422;
        sampleOffsets_0[int(4)] = _S422;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S423 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S423 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S423 - halfHeight_0;
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
                thread FrameVectorView_0 _S424 = _S7.lightPositionType_0;
                float4 _S425 = FrameVectorView_operatorx5Bx5D_get_0(&_S424, lightIndex_0, &kernelContext_1);
                float3 toLight_0 = _S425.xyz + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S426 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S426)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S426;
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
            float3 _S427 = float3(pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f)) ;
            float3 _S428 = mix(normalIncidence_0, grazingIncidence_0, _S427);
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - _S428);
            float _S429 = max(_S355, 0.00100000004749745f);
            float alpha_0 = _S429 * _S429;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S430 = 1.0f - k_0;
            float3 _S431 = float3(max(4.0f * normalDotLight_0 * _S354, 1.00000000317107685e-30f)) ;
            float3 _S432 = _S428 * float3((_S354 / (_S354 * _S430 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S430 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S431;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S433 = max(_S356, 0.00100000004749745f);
                float alpha_1 = _S433 * _S433;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S434 = 1.0f - k_1;
                directSpecular_0 = _S432 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S427) * float3((_S354 / (_S354 * _S434 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S434 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S431);
            }
            else
            {
                directSpecular_0 = _S432;
            }
            float3 _S435 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S435 * _S357) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S435);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S436 = _S7;
    if((_S7.environmentControls_0.x) >= 0.5f)
    {
        float _S437 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S438 = saturate(_S355);
        float2 _S439 = (((&kernelContext_1)->environmentBrdf_0).sample(((&kernelContext_1)->environmentBrdfSampler_0), (float2(_S437, _S438)), level((0.0f)))).xy;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S439.x)  + grazingIncidence_0 * float3(_S439.y) ;
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - specularWeight_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S440 = max(_S365.domeControls_0.y, 1.0f);
        float3 _S441 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S440 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S365.domeControls_0.z;
            float _S442 = _S365.domeControls_0.w;
            for(;;)
            {
                bool _S443 = _S440 <= 1.0f;
                _S9 = _S443;
                if(_S443)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_1)->environmentIrradiance_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S442, 1.0f);
                diffuseColor_0 = (((&kernelContext_1)->environmentIrradiance_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S440)), level((0.0f)))).xyz;
                break;
            }
            float _S444 = _S436.environmentControls_0.y;
            float _S445 = _S436.environmentControls_0.z;
            for(;;)
            {
                if(_S9)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S446 = max(_S444, 1.0f);
                    float _S447 = _S446 - 1.0f;
                    float slice_0 = _S438 * max(_S447, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S445, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S446)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S447) + v_0) / _S446)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S448 = max(_S444, 1.0f);
                float total_0 = _S448 * _S440;
                float _S449 = _S448 - 1.0f;
                float slice_1 = _S438 * max(_S449, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S445, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S448;
                specularColor_0 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S449) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S9)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_2 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S450 = max(_S444, 1.0f);
                        float _S451 = _S450 - 1.0f;
                        float slice_2 = saturate(_S356) * max(_S451, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S445, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        irradiance_0 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S450)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S451) + v_2) / _S450)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S452 = max(_S444, 1.0f);
                    float total_1 = _S452 * _S440;
                    float _S453 = _S452 - 1.0f;
                    float slice_3 = saturate(_S356) * max(_S453, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S445, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S452;
                    irradiance_0 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S453) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightTangent_0 = irradiance_0;
            }
            else
            {
                lightTangent_0 = _S441;
            }
            irradiance_0 = diffuseColor_0;
            lightDirection_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S441;
            lightDirection_0 = _S441;
            lightTangent_0 = _S441;
            for(;;)
            {
                if(sampleIndex_0 < _S366)
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
                float domeGroup_0 = _S7.domeEnvironment_0[sampleIndex_0].x;
                if(domeGroup_0 < 0.0f)
                {
                    sampleIndex_0 = sampleIndex_0 + 1U;
                    continue;
                }
                float _S454 = _S365.domeControls_0.w;
                for(;;)
                {
                    bool _S455 = _S440 <= 1.0f;
                    _S10 = _S455;
                    if(_S455)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_1)->environmentIrradiance_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S454, 1.0f);
                    diffuseColor_0 = (((&kernelContext_1)->environmentIrradiance_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S440)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                float _S456 = _S436.environmentControls_0.y;
                float _S457 = _S436.environmentControls_0.z;
                for(;;)
                {
                    if(_S10)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S458 = max(_S456, 1.0f);
                        float _S459 = _S458 - 1.0f;
                        float slice_4 = _S438 * max(_S459, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S457, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S458)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S459) + v_4) / _S458)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S460 = max(_S456, 1.0f);
                    float total_2 = _S460 * _S440;
                    float _S461 = _S460 - 1.0f;
                    float slice_5 = _S438 * max(_S461, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S457, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S460;
                    specularColor_0 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S461) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightDirection_0 + specularColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S10)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S462 = max(_S456, 1.0f);
                            float _S463 = _S462 - 1.0f;
                            float slice_6 = saturate(_S356) * max(_S463, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S457, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normal_2 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S462)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S463) + v_6) / _S462)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S464 = max(_S456, 1.0f);
                        float total_3 = _S464 * _S440;
                        float _S465 = _S464 - 1.0f;
                        float slice_7 = saturate(_S356) * max(_S465, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S457, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S464;
                        normal_2 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S465) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S466 = (((&kernelContext_1)->environmentBrdf_0).sample(((&kernelContext_1)->environmentBrdfSampler_0), (float2(_S437, saturate(_S356))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightTangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S466.x)  + float3(_S466.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + unlitColor_0) * float3(exp2((as_type<float>((_S8.z))))) ;
    if((_S8.y) == 1U)
    {
        color_2 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S467 = { float4(color_2, opacity_0) };
    return _S467;
}
