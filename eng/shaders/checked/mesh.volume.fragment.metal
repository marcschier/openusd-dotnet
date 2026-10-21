#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
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

struct KernelContext_0
{
    SurfaceParameters_natural_0 device* surfaceParameters_0;
    uint32_t device* frameParameters_0;
    texture3d<float, access::sample> volumeDensityTexture_0;
    sampler volumeDensitySampler_0;
    texture2d<float, access::sample> shadowAtlas_0;
    sampler shadowSampler_0;
    texture2d<float, access::sample> environmentBrdf_0;
    sampler environmentBrdfSampler_0;
    texture2d<float, access::sample> environmentIrradiance_0;
    sampler environmentSampler_0;
    texture2d<float, access::sample> environmentSpecular_0;
};

float4 ReadFrameLight_0(uint field_0, uint index_0, KernelContext_0 thread* kernelContext_0)
{
    uint _S1 = 224U + field_0 * 2048U + index_0 * 16U;
    float _S2 = as_type<float>(kernelContext_0->frameParameters_0[(_S1)>>2]);
    float _S3 = as_type<float>(kernelContext_0->frameParameters_0[(_S1 + 4U)>>2]);
    float _S4 = as_type<float>(kernelContext_0->frameParameters_0[(_S1 + 8U)>>2]);
    float _S5 = as_type<float>(kernelContext_0->frameParameters_0[(_S1 + 12U)>>2]);
    return float4(_S2, _S3, _S4, _S5);
}

float4 ReadFrameShadowSlot_0(uint index_1, KernelContext_0 thread* kernelContext_1)
{
    uint _S6 = 12960U + index_1 * 16U;
    float _S7 = as_type<float>(kernelContext_1->frameParameters_0[(_S6)>>2]);
    float _S8 = as_type<float>(kernelContext_1->frameParameters_0[(_S6 + 4U)>>2]);
    float _S9 = as_type<float>(kernelContext_1->frameParameters_0[(_S6 + 8U)>>2]);
    float _S10 = as_type<float>(kernelContext_1->frameParameters_0[(_S6 + 12U)>>2]);
    return float4(_S7, _S8, _S9, _S10);
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

[[fragment]] pixelOutput_0 volumeFragmentMain(pixelInput_0 _S11 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture3d<float, access::sample> volumeDensityTexture_1 [[texture(9)]], sampler volumeDensitySampler_1 [[sampler(4)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_2;
    FragmentFrameParameters_0 _S12;
    uint4 _S13;
    float _S14;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    bool _S15;
    bool _S16;
    thread KernelContext_0 kernelContext_2;
    (&kernelContext_2)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_2)->frameParameters_0 = frameParameters_1;
    (&kernelContext_2)->volumeDensityTexture_0 = volumeDensityTexture_1;
    (&kernelContext_2)->volumeDensitySampler_0 = volumeDensitySampler_1;
    (&kernelContext_2)->shadowAtlas_0 = shadowAtlas_1;
    (&kernelContext_2)->shadowSampler_0 = shadowSampler_1;
    (&kernelContext_2)->environmentBrdf_0 = environmentBrdf_1;
    (&kernelContext_2)->environmentBrdfSampler_0 = environmentBrdfSampler_1;
    (&kernelContext_2)->environmentIrradiance_0 = environmentIrradiance_1;
    (&kernelContext_2)->environmentSampler_0 = environmentSampler_1;
    (&kernelContext_2)->environmentSpecular_0 = environmentSpecular_1;
    SurfaceParameters_natural_0 device* _S17 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        uint _S18 = as_type<uint>((&kernelContext_2)->frameParameters_0[(64U)>>2]);
        uint _S19 = as_type<uint>((&kernelContext_2)->frameParameters_0[(68U)>>2]);
        uint _S20 = as_type<uint>((&kernelContext_2)->frameParameters_0[(72U)>>2]);
        uint _S21 = as_type<uint>((&kernelContext_2)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S18, _S19, _S20, _S21);
        index_2 = 0U;
        for(;;)
        {
            if(index_2 < 8U)
            {
            }
            else
            {
                break;
            }
            uint _S22 = 80U + index_2 * 16U;
            float _S23 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S22)>>2]);
            float _S24 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S22 + 4U)>>2]);
            float _S25 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S22 + 8U)>>2]);
            float _S26 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S22 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_2] = float4(_S23, _S24, _S25, _S26);
            index_2 = index_2 + 1U;
        }
        float _S27 = as_type<float>((&kernelContext_2)->frameParameters_0[(208U)>>2]);
        float _S28 = as_type<float>((&kernelContext_2)->frameParameters_0[(212U)>>2]);
        float _S29 = as_type<float>((&kernelContext_2)->frameParameters_0[(216U)>>2]);
        float _S30 = as_type<float>((&kernelContext_2)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S27, _S28, _S29, _S30);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 12960U;
        float _S31 = as_type<float>((&kernelContext_2)->frameParameters_0[(12512U)>>2]);
        float _S32 = as_type<float>((&kernelContext_2)->frameParameters_0[(12516U)>>2]);
        float _S33 = as_type<float>((&kernelContext_2)->frameParameters_0[(12520U)>>2]);
        float _S34 = as_type<float>((&kernelContext_2)->frameParameters_0[(12524U)>>2]);
        float4 _S35 = float4(_S31, _S32, _S33, _S34);
        float _S36 = as_type<float>((&kernelContext_2)->frameParameters_0[(12528U)>>2]);
        float _S37 = as_type<float>((&kernelContext_2)->frameParameters_0[(12532U)>>2]);
        float _S38 = as_type<float>((&kernelContext_2)->frameParameters_0[(12536U)>>2]);
        float _S39 = as_type<float>((&kernelContext_2)->frameParameters_0[(12540U)>>2]);
        float4 _S40 = float4(_S36, _S37, _S38, _S39);
        float _S41 = as_type<float>((&kernelContext_2)->frameParameters_0[(12544U)>>2]);
        float _S42 = as_type<float>((&kernelContext_2)->frameParameters_0[(12548U)>>2]);
        float _S43 = as_type<float>((&kernelContext_2)->frameParameters_0[(12552U)>>2]);
        float _S44 = as_type<float>((&kernelContext_2)->frameParameters_0[(12556U)>>2]);
        float4 _S45 = float4(_S41, _S42, _S43, _S44);
        float _S46 = as_type<float>((&kernelContext_2)->frameParameters_0[(12560U)>>2]);
        float _S47 = as_type<float>((&kernelContext_2)->frameParameters_0[(12564U)>>2]);
        float _S48 = as_type<float>((&kernelContext_2)->frameParameters_0[(12568U)>>2]);
        float _S49 = as_type<float>((&kernelContext_2)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S35, _S40, _S45, float4(_S46, _S47, _S48, _S49));
        index_2 = 0U;
        for(;;)
        {
            if(index_2 < 4U)
            {
            }
            else
            {
                break;
            }
            uint _S50 = 12576U + index_2 * 64U;
            float _S51 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50)>>2]);
            float _S52 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 4U)>>2]);
            float _S53 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 8U)>>2]);
            float _S54 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 12U)>>2]);
            float4 _S55 = float4(_S51, _S52, _S53, _S54);
            float _S56 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 16U)>>2]);
            float _S57 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 20U)>>2]);
            float _S58 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 24U)>>2]);
            float _S59 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 28U)>>2]);
            float4 _S60 = float4(_S56, _S57, _S58, _S59);
            float _S61 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 32U)>>2]);
            float _S62 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 36U)>>2]);
            float _S63 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 40U)>>2]);
            float _S64 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 44U)>>2]);
            float4 _S65 = float4(_S61, _S62, _S63, _S64);
            float _S66 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 48U)>>2]);
            float _S67 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 52U)>>2]);
            float _S68 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 56U)>>2]);
            float _S69 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S50 + 60U)>>2]);
            (&frame_0)->shadowWorldToLightClip_0[index_2] = matrix<float,int(4),int(4)> (_S55, _S60, _S65, float4(_S66, _S67, _S68, _S69));
            uint _S70 = index_2 * 16U;
            uint _S71 = 12832U + _S70;
            float _S72 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S71)>>2]);
            float _S73 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S71 + 4U)>>2]);
            float _S74 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S71 + 8U)>>2]);
            float _S75 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S71 + 12U)>>2]);
            (&frame_0)->shadowTile_0[index_2] = float4(_S72, _S73, _S74, _S75);
            uint _S76 = 12896U + _S70;
            float _S77 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S76)>>2]);
            float _S78 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S76 + 4U)>>2]);
            float _S79 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S76 + 8U)>>2]);
            float _S80 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S76 + 12U)>>2]);
            (&frame_0)->shadowControls_0[index_2] = float4(_S77, _S78, _S79, _S80);
            index_2 = index_2 + 1U;
        }
        float _S81 = as_type<float>((&kernelContext_2)->frameParameters_0[(15008U)>>2]);
        float _S82 = as_type<float>((&kernelContext_2)->frameParameters_0[(15012U)>>2]);
        float _S83 = as_type<float>((&kernelContext_2)->frameParameters_0[(15016U)>>2]);
        float _S84 = as_type<float>((&kernelContext_2)->frameParameters_0[(15020U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S81, _S82, _S83, _S84);
        float _S85 = as_type<float>((&kernelContext_2)->frameParameters_0[(15024U)>>2]);
        float _S86 = as_type<float>((&kernelContext_2)->frameParameters_0[(15028U)>>2]);
        float _S87 = as_type<float>((&kernelContext_2)->frameParameters_0[(15032U)>>2]);
        float _S88 = as_type<float>((&kernelContext_2)->frameParameters_0[(15036U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S85, _S86, _S87, _S88);
        float _S89 = as_type<float>((&kernelContext_2)->frameParameters_0[(15040U)>>2]);
        float _S90 = as_type<float>((&kernelContext_2)->frameParameters_0[(15044U)>>2]);
        float _S91 = as_type<float>((&kernelContext_2)->frameParameters_0[(15048U)>>2]);
        float _S92 = as_type<float>((&kernelContext_2)->frameParameters_0[(15052U)>>2]);
        float4 _S93 = float4(_S89, _S90, _S91, _S92);
        float _S94 = as_type<float>((&kernelContext_2)->frameParameters_0[(15056U)>>2]);
        float _S95 = as_type<float>((&kernelContext_2)->frameParameters_0[(15060U)>>2]);
        float _S96 = as_type<float>((&kernelContext_2)->frameParameters_0[(15064U)>>2]);
        float _S97 = as_type<float>((&kernelContext_2)->frameParameters_0[(15068U)>>2]);
        float4 _S98 = float4(_S94, _S95, _S96, _S97);
        float _S99 = as_type<float>((&kernelContext_2)->frameParameters_0[(15072U)>>2]);
        float _S100 = as_type<float>((&kernelContext_2)->frameParameters_0[(15076U)>>2]);
        float _S101 = as_type<float>((&kernelContext_2)->frameParameters_0[(15080U)>>2]);
        float _S102 = as_type<float>((&kernelContext_2)->frameParameters_0[(15084U)>>2]);
        float4 _S103 = float4(_S99, _S100, _S101, _S102);
        float _S104 = as_type<float>((&kernelContext_2)->frameParameters_0[(15088U)>>2]);
        float _S105 = as_type<float>((&kernelContext_2)->frameParameters_0[(15092U)>>2]);
        float _S106 = as_type<float>((&kernelContext_2)->frameParameters_0[(15096U)>>2]);
        float _S107 = as_type<float>((&kernelContext_2)->frameParameters_0[(15100U)>>2]);
        float4 _S108 = float4(_S104, _S105, _S106, _S107);
        float _S109 = as_type<float>((&kernelContext_2)->frameParameters_0[(15104U)>>2]);
        float _S110 = as_type<float>((&kernelContext_2)->frameParameters_0[(15108U)>>2]);
        float _S111 = as_type<float>((&kernelContext_2)->frameParameters_0[(15112U)>>2]);
        float _S112 = as_type<float>((&kernelContext_2)->frameParameters_0[(15116U)>>2]);
        float4 _S113 = float4(_S109, _S110, _S111, _S112);
        float _S114 = as_type<float>((&kernelContext_2)->frameParameters_0[(15120U)>>2]);
        float _S115 = as_type<float>((&kernelContext_2)->frameParameters_0[(15124U)>>2]);
        float _S116 = as_type<float>((&kernelContext_2)->frameParameters_0[(15128U)>>2]);
        float _S117 = as_type<float>((&kernelContext_2)->frameParameters_0[(15132U)>>2]);
        float4 _S118 = float4(_S114, _S115, _S116, _S117);
        float _S119 = as_type<float>((&kernelContext_2)->frameParameters_0[(15136U)>>2]);
        float _S120 = as_type<float>((&kernelContext_2)->frameParameters_0[(15140U)>>2]);
        float _S121 = as_type<float>((&kernelContext_2)->frameParameters_0[(15144U)>>2]);
        float _S122 = as_type<float>((&kernelContext_2)->frameParameters_0[(15148U)>>2]);
        float4 _S123 = float4(_S119, _S120, _S121, _S122);
        float _S124 = as_type<float>((&kernelContext_2)->frameParameters_0[(15152U)>>2]);
        float _S125 = as_type<float>((&kernelContext_2)->frameParameters_0[(15156U)>>2]);
        float _S126 = as_type<float>((&kernelContext_2)->frameParameters_0[(15160U)>>2]);
        float _S127 = as_type<float>((&kernelContext_2)->frameParameters_0[(15164U)>>2]);
        float4 _S128 = float4(_S124, _S125, _S126, _S127);
        float _S129 = as_type<float>((&kernelContext_2)->frameParameters_0[(15168U)>>2]);
        float _S130 = as_type<float>((&kernelContext_2)->frameParameters_0[(15172U)>>2]);
        float _S131 = as_type<float>((&kernelContext_2)->frameParameters_0[(15176U)>>2]);
        float _S132 = as_type<float>((&kernelContext_2)->frameParameters_0[(15180U)>>2]);
        float4 _S133 = float4(_S129, _S130, _S131, _S132);
        float _S134 = as_type<float>((&kernelContext_2)->frameParameters_0[(15184U)>>2]);
        float _S135 = as_type<float>((&kernelContext_2)->frameParameters_0[(15188U)>>2]);
        float _S136 = as_type<float>((&kernelContext_2)->frameParameters_0[(15192U)>>2]);
        float _S137 = as_type<float>((&kernelContext_2)->frameParameters_0[(15196U)>>2]);
        float4 _S138 = float4(_S134, _S135, _S136, _S137);
        float _S139 = as_type<float>((&kernelContext_2)->frameParameters_0[(15200U)>>2]);
        float _S140 = as_type<float>((&kernelContext_2)->frameParameters_0[(15204U)>>2]);
        float _S141 = as_type<float>((&kernelContext_2)->frameParameters_0[(15208U)>>2]);
        float _S142 = as_type<float>((&kernelContext_2)->frameParameters_0[(15212U)>>2]);
        float4 _S143 = float4(_S139, _S140, _S141, _S142);
        float _S144 = as_type<float>((&kernelContext_2)->frameParameters_0[(15216U)>>2]);
        float _S145 = as_type<float>((&kernelContext_2)->frameParameters_0[(15220U)>>2]);
        float _S146 = as_type<float>((&kernelContext_2)->frameParameters_0[(15224U)>>2]);
        float _S147 = as_type<float>((&kernelContext_2)->frameParameters_0[(15228U)>>2]);
        float4 _S148 = float4(_S144, _S145, _S146, _S147);
        float _S149 = as_type<float>((&kernelContext_2)->frameParameters_0[(15232U)>>2]);
        float _S150 = as_type<float>((&kernelContext_2)->frameParameters_0[(15236U)>>2]);
        float _S151 = as_type<float>((&kernelContext_2)->frameParameters_0[(15240U)>>2]);
        float _S152 = as_type<float>((&kernelContext_2)->frameParameters_0[(15244U)>>2]);
        float4 _S153 = float4(_S149, _S150, _S151, _S152);
        float _S154 = as_type<float>((&kernelContext_2)->frameParameters_0[(15248U)>>2]);
        float _S155 = as_type<float>((&kernelContext_2)->frameParameters_0[(15252U)>>2]);
        float _S156 = as_type<float>((&kernelContext_2)->frameParameters_0[(15256U)>>2]);
        float _S157 = as_type<float>((&kernelContext_2)->frameParameters_0[(15260U)>>2]);
        float4 _S158 = float4(_S154, _S155, _S156, _S157);
        float _S159 = as_type<float>((&kernelContext_2)->frameParameters_0[(15264U)>>2]);
        float _S160 = as_type<float>((&kernelContext_2)->frameParameters_0[(15268U)>>2]);
        float _S161 = as_type<float>((&kernelContext_2)->frameParameters_0[(15272U)>>2]);
        float _S162 = as_type<float>((&kernelContext_2)->frameParameters_0[(15276U)>>2]);
        float4 _S163 = float4(_S159, _S160, _S161, _S162);
        float _S164 = as_type<float>((&kernelContext_2)->frameParameters_0[(15280U)>>2]);
        float _S165 = as_type<float>((&kernelContext_2)->frameParameters_0[(15284U)>>2]);
        float _S166 = as_type<float>((&kernelContext_2)->frameParameters_0[(15288U)>>2]);
        float _S167 = as_type<float>((&kernelContext_2)->frameParameters_0[(15292U)>>2]);
        float4 _S168 = float4(_S164, _S165, _S166, _S167);
        (&frame_0)->domeAmbient_0[int(0)] = _S93;
        (&frame_0)->domeAmbient_0[int(1)] = _S98;
        (&frame_0)->domeAmbient_0[int(2)] = _S103;
        (&frame_0)->domeAmbient_0[int(3)] = _S108;
        (&frame_0)->domeAmbient_0[int(4)] = _S113;
        (&frame_0)->domeAmbient_0[int(5)] = _S118;
        (&frame_0)->domeAmbient_0[int(6)] = _S123;
        (&frame_0)->domeAmbient_0[int(7)] = _S128;
        (&frame_0)->domeEnvironment_0[int(0)] = _S133;
        (&frame_0)->domeEnvironment_0[int(1)] = _S138;
        (&frame_0)->domeEnvironment_0[int(2)] = _S143;
        (&frame_0)->domeEnvironment_0[int(3)] = _S148;
        (&frame_0)->domeEnvironment_0[int(4)] = _S153;
        (&frame_0)->domeEnvironment_0[int(5)] = _S158;
        (&frame_0)->domeEnvironment_0[int(6)] = _S163;
        (&frame_0)->domeEnvironment_0[int(7)] = _S168;
        _S12 = frame_0;
        break;
    }
    for(;;)
    {
        _S13 = _S12.clipPlaneCount_0;
        uint _S169 = min(_S12.clipPlaneCount_0.x, 8U);
        index_2 = 0U;
        for(;;)
        {
            if(index_2 < _S169)
            {
            }
            else
            {
                break;
            }
            if((dot(_S12.clipPlanes_0[index_2].xyz, _S11.eyePosition_0) + _S12.clipPlanes_0[index_2].w) < 0.0f)
            {
                discard_fragment();
            }
            index_2 = index_2 + 1U;
        }
        break;
    }
    float4 _S170 = float4(_S17->clearcoatShaded_0) ;
    float shadedMode_0 = _S170.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S17->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S11.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S17->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S11.tint_0.w;
    }
    float4 _S171 = float4(_S17->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S171.xyz;
    float occlusion_0 = _S171.w;
    float3 unlitColor_0;
    if(unlit_0)
    {
        float3 unlitColor_1 = (diffuseColor_0 + emissiveColor_0) * float3(exp2((as_type<float>((_S13.z))))) ;
        if((_S13.y) == 1U)
        {
            unlitColor_0 = unlitColor_1 / (float3(1.0f)  + max(unlitColor_1, float3(0.0f, 0.0f, 0.0f)));
        }
        else
        {
            unlitColor_0 = unlitColor_1;
        }
        pixelOutput_0 _S172 = { float4(unlitColor_0, opacity_0) };
        return _S172;
    }
    float4 _S173 = float4(_S17->reserved_0) ;
    uint step_0;
    float density_0;
    if((_S173.x) >= 0.5f)
    {
        if((_S173.w) >= 0.5f)
        {
            float _S174 = (float4(_S17->textureControls_0) ).z;
            for(;;)
            {
                float2 _S175 = saturate(_S11.objectPosition_0.xy + float2(0.5f, 0.5f));
                uint stepCount_0 = uint(clamp(_S174, 1.0f, 512.0f));
                step_0 = 0U;
                density_0 = 0.0f;
                for(;;)
                {
                    if(step_0 < stepCount_0)
                    {
                    }
                    else
                    {
                        break;
                    }
                    float density_1 = density_0 + (((&kernelContext_2)->volumeDensityTexture_0).sample(((&kernelContext_2)->volumeDensitySampler_0), (float3(_S175, (float(step_0) + 0.5f) / float(stepCount_0)))).x);
                    step_0 = step_0 + 1U;
                    density_0 = density_1;
                }
                _S14 = density_0 / float(stepCount_0);
                break;
            }
            density_0 = _S14 * max(0.0f, _S173.y);
        }
        else
        {
            density_0 = max(0.0f, _S173.y);
        }
        pixelOutput_0 _S176 = { float4(diffuseColor_0 * float3((1.0f - exp(- density_0 * max(0.0f, _S173.z)))) , 1.0f) };
        return _S176;
    }
    float4 _S177 = float4(_S17->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S177.x);
    float roughness_0 = clamp(_S177.y, 0.00999999977648258f, 1.0f);
    float4 _S178 = float4(_S17->specularIor_0) ;
    float3 specularColor_0 = _S178.xyz;
    float ior_0 = _S178.w;
    float clearcoatAmount_0 = _S170.x;
    float clearcoatRoughness_0 = _S170.y;
    float opacityThreshold_0 = _S177.z;
    if(opacityThreshold_0 > 0.0f)
    {
        if(opacity_0 < opacityThreshold_0)
        {
            discard_fragment();
        }
        opacity_0 = 1.0f;
    }
    float3 normal_1 = normalize(_S11.normal_0);
    float3 worldNormal_1 = normalize(_S11.worldNormal_0);
    float lengthSquared_0 = dot(_S11.eyePosition_0, _S11.eyePosition_0);
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        unlitColor_0 = - _S11.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
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
    float _S179 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S180 = max(0.00100000004749745f, roughness_0);
    float _S181 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S182 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S182;
    bool omniPbrSpecular_0 = ((float4(_S17->textureControls_0) ).w) >= 0.5f;
    float dielectricLevel_0 = _S170.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S183 = float3(dielectricLevel_0) ;
        float3 _S184 = float3(metallic_0) ;
        float3 _S185 = mix(_S183, diffuseColor_0, _S184);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S183, diffuseColor_0, _S184);
        grazingIncidence_0 = _S185;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S177.w) >= 0.5f)
        {
            float3 _S186 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S186;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S187 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S187);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S187);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S188 = _S12;
    float _S189 = _S12.ambientLight_0.w;
    bool hasSceneLighting_0;
    if(_S189 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S190 = _S188.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S190, _S190)) > 0.0f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S12.environmentControls_0.x) >= 0.5f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S12.environmentControls_0.w) >= 0.5f;
    }
    uint _S191 = min(uint(_S189), 128U);
    float3 sceneEye_0 = normalize((((float4(unlitColor_0, 0.0f)) * (_S12.eyeToWorld_0))).xyz);
    float3 worldPosition_0 = (((float4(_S11.eyePosition_0, 1.0f)) * (_S12.eyeToWorld_0))).xyz;
    float3 worldGeometricNormal_0 = cross(dfdx(worldPosition_0), dfdy(worldPosition_0));
    float worldNormalLengthSquared_0 = dot(worldGeometricNormal_0, worldGeometricNormal_0);
    float3 irradiance_0;
    if(worldNormalLengthSquared_0 > 9.99999968265522539e-21f)
    {
        irradiance_0 = worldGeometricNormal_0 * float3(rsqrt(worldNormalLengthSquared_0)) ;
    }
    else
    {
        irradiance_0 = float3(0.0f, 0.0f, 1.0f);
    }
    float4 _S192 = float4(_S17->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S192.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S17->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S193 = _S12;
    uint _S194 = min(uint(max(_S12.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S194) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S195 = _S188.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S196 = float3(0.0f, 0.0f, 0.0f);
        step_0 = 0U;
        domeAmbient_1 = _S196;
        for(;;)
        {
            if(step_0 < _S194)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << step_0)) != 0U)
            {
                domeAmbient_1 = domeAmbient_1 + _S12.domeAmbient_0[step_0].xyz;
            }
            step_0 = step_0 + 1U;
        }
    }
    else
    {
        domeAmbient_1 = _S195;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S197 = !hasSceneLighting_0;
    if(_S197)
    {
        step_0 = 1U;
    }
    else
    {
        step_0 = _S191;
    }
    uint lightIndex_0 = 0U;
    float3 color_2 = color_1;
    for(;;)
    {
        if(lightIndex_0 < step_0)
        {
        }
        else
        {
            break;
        }
        bool _S198;
        if(hasSceneLighting_0)
        {
            _S198 = (((uint4(_S17->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S198 = false;
        }
        if(_S198)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S199 = lightIndex_0 == 0U;
        bool _S200;
        if(_S199)
        {
            _S200 = _S197;
        }
        else
        {
            _S200 = false;
        }
        if(_S200)
        {
            density_0 = 1.0f;
        }
        else
        {
            float4 _S201 = ReadFrameLight_0(0U, lightIndex_0, &kernelContext_2);
            density_0 = _S201.w;
        }
        bool _S202;
        if(_S199)
        {
            _S202 = _S197;
        }
        else
        {
            _S202 = false;
        }
        if(_S202)
        {
            lightDirection_0 = normalize((float4(_S17->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            float4 _S203 = ReadFrameLight_0(1U, lightIndex_0, &kernelContext_2);
            lightDirection_0 = normalize(_S203.xyz);
        }
        bool _S204;
        if(_S199)
        {
            _S204 = _S197;
        }
        else
        {
            _S204 = false;
        }
        float _S205;
        if(_S204)
        {
            _S205 = (float4(_S17->lightDirectionIntensity_0) ).w;
        }
        else
        {
            float4 _S206 = ReadFrameLight_0(2U, lightIndex_0, &kernelContext_2);
            _S205 = _S206.w;
        }
        bool _S207;
        if(_S199)
        {
            _S207 = _S197;
        }
        else
        {
            _S207 = false;
        }
        if(_S207)
        {
            diffuseColor_0 = _S192.xyz;
        }
        else
        {
            float4 _S208 = ReadFrameLight_0(2U, lightIndex_0, &kernelContext_2);
            diffuseColor_0 = _S208.xyz;
        }
        bool _S209;
        if(_S199)
        {
            _S209 = _S197;
        }
        else
        {
            _S209 = false;
        }
        float _S210;
        if(_S209)
        {
            _S210 = 1.0f;
        }
        else
        {
            float4 _S211 = ReadFrameLight_0(3U, lightIndex_0, &kernelContext_2);
            _S210 = _S211.x;
        }
        bool _S212;
        if(_S199)
        {
            _S212 = _S197;
        }
        else
        {
            _S212 = false;
        }
        float _S213;
        if(_S212)
        {
            _S213 = 1.0f;
        }
        else
        {
            float4 _S214 = ReadFrameLight_0(3U, lightIndex_0, &kernelContext_2);
            _S213 = _S214.y;
        }
        bool _S215;
        if(_S199)
        {
            _S215 = _S197;
        }
        else
        {
            _S215 = false;
        }
        if(_S215)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            float4 _S216 = ReadFrameLight_0(4U, lightIndex_0, &kernelContext_2);
            lightTangent_0 = normalize(_S216.xyz);
        }
        bool _S217;
        if(_S199)
        {
            _S217 = _S197;
        }
        else
        {
            _S217 = false;
        }
        if(_S217)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            float4 _S218 = ReadFrameLight_0(5U, lightIndex_0, &kernelContext_2);
            lightBitangent_0 = normalize(_S218.xyz);
        }
        bool _S219;
        if(_S199)
        {
            _S219 = _S197;
        }
        else
        {
            _S219 = false;
        }
        float shapeX_0;
        if(_S219)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            float4 _S220 = ReadFrameLight_0(4U, lightIndex_0, &kernelContext_2);
            shapeX_0 = _S220.w;
        }
        bool _S221;
        if(_S199)
        {
            _S221 = _S197;
        }
        else
        {
            _S221 = false;
        }
        float shapeY_0;
        if(_S221)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            float4 _S222 = ReadFrameLight_0(5U, lightIndex_0, &kernelContext_2);
            shapeY_0 = _S222.w;
        }
        bool _S223;
        if(_S199)
        {
            _S223 = _S197;
        }
        else
        {
            _S223 = false;
        }
        float lightRadius_0;
        if(_S223)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            float4 _S224 = ReadFrameLight_0(1U, lightIndex_0, &kernelContext_2);
            lightRadius_0 = _S224.w;
        }
        bool _S225;
        if(_S199)
        {
            _S225 = _S197;
        }
        else
        {
            _S225 = false;
        }
        if(_S225)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        float3 color_3;
        float shadowVisibility_0;
        float sampleCount_0;
        if(hasSceneLighting_0)
        {
            float4 _S226 = ReadFrameShadowSlot_0(lightIndex_0, &kernelContext_2);
            int shadowSlot_0 = int(_S226.x);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S227 = _S12;
                    FragmentFrameParameters_0 _S228 = _S12;
                    if((dot(irradiance_0, lightDirection_0)) < 0.0f)
                    {
                        color_3 = - irradiance_0;
                    }
                    else
                    {
                        color_3 = irradiance_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_3, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_3 * float3((_S227.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S12.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S229 = lightClip_0.w;
                    if(_S229 <= 0.0f)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S229) ;
                    bool _S230;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S230 = true;
                    }
                    else
                    {
                        _S230 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S231;
                    if(_S230)
                    {
                        _S231 = true;
                    }
                    else
                    {
                        _S231 = (ndc_0.z) < 0.0f;
                    }
                    bool _S232;
                    if(_S231)
                    {
                        _S232 = true;
                    }
                    else
                    {
                        _S232 = (ndc_0.z) > 1.0f;
                    }
                    if(_S232)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float2 _S233 = _S228.shadowTile_0[shadowSlot_0].xy;
                    float2 _S234 = _S228.shadowTile_0[shadowSlot_0].zw;
                    float2 _S235 = _S233 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S234;
                    float texel_0 = _S227.shadowControls_0[shadowSlot_0].w;
                    float _S236 = max(_S227.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S237 = ndc_0.z - _S227.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S238 = float2((texel_0 * 0.5f)) ;
                    float2 _S239 = _S233 + _S238;
                    float2 _S240 = _S233 + _S234 - _S238;
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
                            if(_S237 <= (((&kernelContext_2)->shadowAtlas_0).sample(((&kernelContext_2)->shadowSampler_0), (clamp(_S235 + float2(float(x_0), float(y_0)) * float2((_S236 * texel_0)) , _S239, _S240)), level((0.0f))).x))
                            {
                                sampleCount_0 = 1.0f;
                            }
                            else
                            {
                                sampleCount_0 = 0.0f;
                            }
                            float lit_0 = shadowVisibility_0 + sampleCount_0;
                            x_0 = x_0 + int(1);
                            shadowVisibility_0 = lit_0;
                        }
                        y_0 = y_0 + int(1);
                    }
                    shadowVisibility_0 = shadowVisibility_0 * 0.1111111119389534f;
                    break;
                }
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
        float3 _S241 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S241;
        sampleOffsets_0[int(1)] = _S241;
        sampleOffsets_0[int(2)] = _S241;
        sampleOffsets_0[int(3)] = _S241;
        sampleOffsets_0[int(4)] = _S241;
        if(density_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S242 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S242 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S242 - halfHeight_0;
            sampleCount_0 = 5.0f;
        }
        else
        {
            if(density_0 == 4.0f)
            {
                sampleOffsets_0[int(1)] = lightTangent_0 * float3(lightRadius_0) ;
                sampleOffsets_0[int(2)] = - lightTangent_0 * float3(lightRadius_0) ;
                sampleOffsets_0[int(3)] = lightBitangent_0 * float3(lightRadius_0) ;
                sampleOffsets_0[int(4)] = - lightBitangent_0 * float3(lightRadius_0) ;
                sampleCount_0 = 5.0f;
            }
            else
            {
                if(density_0 == 5.0f)
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
            float sampleIntensity_0 = _S205 / sampleCount_0;
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(density_0 >= 2.0f)
            {
                float4 _S243 = ReadFrameLight_0(0U, lightIndex_0, &kernelContext_2);
                float3 toLight_0 = _S243.xyz + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S244 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S244)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S244;
                if(density_0 == 4.0f)
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
            float _S245 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_0;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_0 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S245)) ;
            }
            else
            {
                diffuseFresnel_0 = mix(normalIncidence_0, grazingIncidence_0, float3(_S245) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_0);
            float3 _S246 = float3(_S245) ;
            float _S247 = max(_S180, 0.00100000004749745f);
            float alpha_0 = _S247 * _S247;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S248 = 1.0f - k_0;
            float3 _S249 = float3(max(4.0f * normalDotLight_0 * _S179, 1.00000000317107685e-30f)) ;
            float3 _S250 = mix(normalIncidence_0, grazingIncidence_0, _S246) * float3((_S179 / (_S179 * _S248 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S248 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S249;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S251 = max(_S181, 0.00100000004749745f);
                float alpha_1 = _S251 * _S251;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S252 = 1.0f - k_1;
                directSpecular_0 = _S250 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S246) * float3((_S179 / (_S179 * _S252 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S252 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S249);
            }
            else
            {
                directSpecular_0 = _S250;
            }
            float3 _S253 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S210)  * (_S253 * _S182) + directSpecular_0 * float3(_S213)  * _S253);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S254 = _S12;
    if((_S12.environmentControls_0.x) >= 0.5f)
    {
        float _S255 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S256 = saturate(_S180);
        float2 _S257 = (((&kernelContext_2)->environmentBrdf_0).sample(((&kernelContext_2)->environmentBrdfSampler_0), (float2(_S255, _S256)), level((0.0f)))).xy;
        float _S258 = _S257.x;
        float _S259 = _S257.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S258)  + grazingIncidence_0 * float3(_S259) ;
        if(omniPbrSpecular_0)
        {
            unlitColor_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S258 + _S259)) ;
        }
        else
        {
            unlitColor_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - unlitColor_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S260 = max(_S193.domeControls_0.y, 1.0f);
        float3 _S261 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S260 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S193.domeControls_0.z;
            float _S262 = _S193.domeControls_0.w;
            for(;;)
            {
                bool _S263 = _S260 <= 1.0f;
                _S15 = _S263;
                if(_S263)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S262, 1.0f);
                diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S260)), level((0.0f)))).xyz;
                break;
            }
            float _S264 = _S254.environmentControls_0.y;
            float _S265 = _S254.environmentControls_0.z;
            for(;;)
            {
                if(_S15)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S266 = max(_S264, 1.0f);
                    float _S267 = _S266 - 1.0f;
                    float slice_0 = _S256 * max(_S267, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S265, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S266)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S267) + v_0) / _S266)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S268 = max(_S264, 1.0f);
                float total_0 = _S268 * _S260;
                float _S269 = _S268 - 1.0f;
                float slice_1 = _S256 * max(_S269, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S265, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S268;
                unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S269) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S15)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_2 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S270 = max(_S264, 1.0f);
                        float _S271 = _S270 - 1.0f;
                        float slice_2 = saturate(_S181) * max(_S271, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S265, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S270)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S271) + v_2) / _S270)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S272 = max(_S264, 1.0f);
                    float total_1 = _S272 * _S260;
                    float _S273 = _S272 - 1.0f;
                    float slice_3 = saturate(_S181) * max(_S273, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S265, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S272;
                    normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S273) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightTangent_0 = normal_2;
            }
            else
            {
                lightTangent_0 = _S261;
            }
            irradiance_0 = diffuseColor_0;
            lightDirection_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S261;
            lightDirection_0 = _S261;
            lightTangent_0 = _S261;
            for(;;)
            {
                if(sampleIndex_0 < _S194)
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
                float domeGroup_0 = _S12.domeEnvironment_0[sampleIndex_0].x;
                if(domeGroup_0 < 0.0f)
                {
                    sampleIndex_0 = sampleIndex_0 + 1U;
                    continue;
                }
                float _S274 = _S193.domeControls_0.w;
                for(;;)
                {
                    bool _S275 = _S260 <= 1.0f;
                    _S16 = _S275;
                    if(_S275)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S274, 1.0f);
                    diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S260)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                float _S276 = _S254.environmentControls_0.y;
                float _S277 = _S254.environmentControls_0.z;
                for(;;)
                {
                    if(_S16)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S278 = max(_S276, 1.0f);
                        float _S279 = _S278 - 1.0f;
                        float slice_4 = _S256 * max(_S279, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S277, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S278)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S279) + v_4) / _S278)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S280 = max(_S276, 1.0f);
                    float total_2 = _S280 * _S260;
                    float _S281 = _S280 - 1.0f;
                    float slice_5 = _S256 * max(_S281, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S277, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S280;
                    unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S281) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightDirection_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S16)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S282 = max(_S276, 1.0f);
                            float _S283 = _S282 - 1.0f;
                            float slice_6 = saturate(_S181) * max(_S283, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S277, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S282)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S283) + v_6) / _S282)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S284 = max(_S276, 1.0f);
                        float total_3 = _S284 * _S260;
                        float _S285 = _S284 - 1.0f;
                        float slice_7 = saturate(_S181) * max(_S285, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S277, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S284;
                        normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S285) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
        float3 _S286 = float3(occlusion_0) ;
        float3 color_4 = color_2 + _S286 * diffuse_1 * irradiance_0 * diffuseWeight_0 + _S286 * lightDirection_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S287 = (((&kernelContext_2)->environmentBrdf_0).sample(((&kernelContext_2)->environmentBrdfSampler_0), (float2(_S255, saturate(_S181))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightTangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S287.x)  + float3(_S287.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + emissiveColor_0) * float3(exp2((as_type<float>((_S13.z))))) ;
    if((_S13.y) == 1U)
    {
        color_2 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S288 = { float4(color_2, opacity_0) };
    return _S288;
}
