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

[[fragment]] pixelOutput_0 fragmentMain(pixelInput_0 _S11 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_2;
    FragmentFrameParameters_0 _S12;
    uint4 _S13;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    bool _S14;
    bool _S15;
    thread KernelContext_0 kernelContext_2;
    (&kernelContext_2)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_2)->frameParameters_0 = frameParameters_1;
    (&kernelContext_2)->shadowAtlas_0 = shadowAtlas_1;
    (&kernelContext_2)->shadowSampler_0 = shadowSampler_1;
    (&kernelContext_2)->environmentBrdf_0 = environmentBrdf_1;
    (&kernelContext_2)->environmentBrdfSampler_0 = environmentBrdfSampler_1;
    (&kernelContext_2)->environmentIrradiance_0 = environmentIrradiance_1;
    (&kernelContext_2)->environmentSampler_0 = environmentSampler_1;
    (&kernelContext_2)->environmentSpecular_0 = environmentSpecular_1;
    SurfaceParameters_natural_0 device* _S16 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        uint _S17 = as_type<uint>((&kernelContext_2)->frameParameters_0[(64U)>>2]);
        uint _S18 = as_type<uint>((&kernelContext_2)->frameParameters_0[(68U)>>2]);
        uint _S19 = as_type<uint>((&kernelContext_2)->frameParameters_0[(72U)>>2]);
        uint _S20 = as_type<uint>((&kernelContext_2)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S17, _S18, _S19, _S20);
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
            uint _S21 = 80U + index_2 * 16U;
            float _S22 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S21)>>2]);
            float _S23 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S21 + 4U)>>2]);
            float _S24 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S21 + 8U)>>2]);
            float _S25 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S21 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_2] = float4(_S22, _S23, _S24, _S25);
            index_2 = index_2 + 1U;
        }
        float _S26 = as_type<float>((&kernelContext_2)->frameParameters_0[(208U)>>2]);
        float _S27 = as_type<float>((&kernelContext_2)->frameParameters_0[(212U)>>2]);
        float _S28 = as_type<float>((&kernelContext_2)->frameParameters_0[(216U)>>2]);
        float _S29 = as_type<float>((&kernelContext_2)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S26, _S27, _S28, _S29);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 12960U;
        float _S30 = as_type<float>((&kernelContext_2)->frameParameters_0[(12512U)>>2]);
        float _S31 = as_type<float>((&kernelContext_2)->frameParameters_0[(12516U)>>2]);
        float _S32 = as_type<float>((&kernelContext_2)->frameParameters_0[(12520U)>>2]);
        float _S33 = as_type<float>((&kernelContext_2)->frameParameters_0[(12524U)>>2]);
        float4 _S34 = float4(_S30, _S31, _S32, _S33);
        float _S35 = as_type<float>((&kernelContext_2)->frameParameters_0[(12528U)>>2]);
        float _S36 = as_type<float>((&kernelContext_2)->frameParameters_0[(12532U)>>2]);
        float _S37 = as_type<float>((&kernelContext_2)->frameParameters_0[(12536U)>>2]);
        float _S38 = as_type<float>((&kernelContext_2)->frameParameters_0[(12540U)>>2]);
        float4 _S39 = float4(_S35, _S36, _S37, _S38);
        float _S40 = as_type<float>((&kernelContext_2)->frameParameters_0[(12544U)>>2]);
        float _S41 = as_type<float>((&kernelContext_2)->frameParameters_0[(12548U)>>2]);
        float _S42 = as_type<float>((&kernelContext_2)->frameParameters_0[(12552U)>>2]);
        float _S43 = as_type<float>((&kernelContext_2)->frameParameters_0[(12556U)>>2]);
        float4 _S44 = float4(_S40, _S41, _S42, _S43);
        float _S45 = as_type<float>((&kernelContext_2)->frameParameters_0[(12560U)>>2]);
        float _S46 = as_type<float>((&kernelContext_2)->frameParameters_0[(12564U)>>2]);
        float _S47 = as_type<float>((&kernelContext_2)->frameParameters_0[(12568U)>>2]);
        float _S48 = as_type<float>((&kernelContext_2)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S34, _S39, _S44, float4(_S45, _S46, _S47, _S48));
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
            uint _S49 = 12576U + index_2 * 64U;
            float _S50 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49)>>2]);
            float _S51 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 4U)>>2]);
            float _S52 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 8U)>>2]);
            float _S53 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 12U)>>2]);
            float4 _S54 = float4(_S50, _S51, _S52, _S53);
            float _S55 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 16U)>>2]);
            float _S56 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 20U)>>2]);
            float _S57 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 24U)>>2]);
            float _S58 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 28U)>>2]);
            float4 _S59 = float4(_S55, _S56, _S57, _S58);
            float _S60 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 32U)>>2]);
            float _S61 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 36U)>>2]);
            float _S62 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 40U)>>2]);
            float _S63 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 44U)>>2]);
            float4 _S64 = float4(_S60, _S61, _S62, _S63);
            float _S65 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 48U)>>2]);
            float _S66 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 52U)>>2]);
            float _S67 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 56U)>>2]);
            float _S68 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S49 + 60U)>>2]);
            (&frame_0)->shadowWorldToLightClip_0[index_2] = matrix<float,int(4),int(4)> (_S54, _S59, _S64, float4(_S65, _S66, _S67, _S68));
            uint _S69 = index_2 * 16U;
            uint _S70 = 12832U + _S69;
            float _S71 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S70)>>2]);
            float _S72 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S70 + 4U)>>2]);
            float _S73 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S70 + 8U)>>2]);
            float _S74 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S70 + 12U)>>2]);
            (&frame_0)->shadowTile_0[index_2] = float4(_S71, _S72, _S73, _S74);
            uint _S75 = 12896U + _S69;
            float _S76 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S75)>>2]);
            float _S77 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S75 + 4U)>>2]);
            float _S78 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S75 + 8U)>>2]);
            float _S79 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S75 + 12U)>>2]);
            (&frame_0)->shadowControls_0[index_2] = float4(_S76, _S77, _S78, _S79);
            index_2 = index_2 + 1U;
        }
        float _S80 = as_type<float>((&kernelContext_2)->frameParameters_0[(15008U)>>2]);
        float _S81 = as_type<float>((&kernelContext_2)->frameParameters_0[(15012U)>>2]);
        float _S82 = as_type<float>((&kernelContext_2)->frameParameters_0[(15016U)>>2]);
        float _S83 = as_type<float>((&kernelContext_2)->frameParameters_0[(15020U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S80, _S81, _S82, _S83);
        float _S84 = as_type<float>((&kernelContext_2)->frameParameters_0[(15024U)>>2]);
        float _S85 = as_type<float>((&kernelContext_2)->frameParameters_0[(15028U)>>2]);
        float _S86 = as_type<float>((&kernelContext_2)->frameParameters_0[(15032U)>>2]);
        float _S87 = as_type<float>((&kernelContext_2)->frameParameters_0[(15036U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S84, _S85, _S86, _S87);
        float _S88 = as_type<float>((&kernelContext_2)->frameParameters_0[(15040U)>>2]);
        float _S89 = as_type<float>((&kernelContext_2)->frameParameters_0[(15044U)>>2]);
        float _S90 = as_type<float>((&kernelContext_2)->frameParameters_0[(15048U)>>2]);
        float _S91 = as_type<float>((&kernelContext_2)->frameParameters_0[(15052U)>>2]);
        float4 _S92 = float4(_S88, _S89, _S90, _S91);
        float _S93 = as_type<float>((&kernelContext_2)->frameParameters_0[(15056U)>>2]);
        float _S94 = as_type<float>((&kernelContext_2)->frameParameters_0[(15060U)>>2]);
        float _S95 = as_type<float>((&kernelContext_2)->frameParameters_0[(15064U)>>2]);
        float _S96 = as_type<float>((&kernelContext_2)->frameParameters_0[(15068U)>>2]);
        float4 _S97 = float4(_S93, _S94, _S95, _S96);
        float _S98 = as_type<float>((&kernelContext_2)->frameParameters_0[(15072U)>>2]);
        float _S99 = as_type<float>((&kernelContext_2)->frameParameters_0[(15076U)>>2]);
        float _S100 = as_type<float>((&kernelContext_2)->frameParameters_0[(15080U)>>2]);
        float _S101 = as_type<float>((&kernelContext_2)->frameParameters_0[(15084U)>>2]);
        float4 _S102 = float4(_S98, _S99, _S100, _S101);
        float _S103 = as_type<float>((&kernelContext_2)->frameParameters_0[(15088U)>>2]);
        float _S104 = as_type<float>((&kernelContext_2)->frameParameters_0[(15092U)>>2]);
        float _S105 = as_type<float>((&kernelContext_2)->frameParameters_0[(15096U)>>2]);
        float _S106 = as_type<float>((&kernelContext_2)->frameParameters_0[(15100U)>>2]);
        float4 _S107 = float4(_S103, _S104, _S105, _S106);
        float _S108 = as_type<float>((&kernelContext_2)->frameParameters_0[(15104U)>>2]);
        float _S109 = as_type<float>((&kernelContext_2)->frameParameters_0[(15108U)>>2]);
        float _S110 = as_type<float>((&kernelContext_2)->frameParameters_0[(15112U)>>2]);
        float _S111 = as_type<float>((&kernelContext_2)->frameParameters_0[(15116U)>>2]);
        float4 _S112 = float4(_S108, _S109, _S110, _S111);
        float _S113 = as_type<float>((&kernelContext_2)->frameParameters_0[(15120U)>>2]);
        float _S114 = as_type<float>((&kernelContext_2)->frameParameters_0[(15124U)>>2]);
        float _S115 = as_type<float>((&kernelContext_2)->frameParameters_0[(15128U)>>2]);
        float _S116 = as_type<float>((&kernelContext_2)->frameParameters_0[(15132U)>>2]);
        float4 _S117 = float4(_S113, _S114, _S115, _S116);
        float _S118 = as_type<float>((&kernelContext_2)->frameParameters_0[(15136U)>>2]);
        float _S119 = as_type<float>((&kernelContext_2)->frameParameters_0[(15140U)>>2]);
        float _S120 = as_type<float>((&kernelContext_2)->frameParameters_0[(15144U)>>2]);
        float _S121 = as_type<float>((&kernelContext_2)->frameParameters_0[(15148U)>>2]);
        float4 _S122 = float4(_S118, _S119, _S120, _S121);
        float _S123 = as_type<float>((&kernelContext_2)->frameParameters_0[(15152U)>>2]);
        float _S124 = as_type<float>((&kernelContext_2)->frameParameters_0[(15156U)>>2]);
        float _S125 = as_type<float>((&kernelContext_2)->frameParameters_0[(15160U)>>2]);
        float _S126 = as_type<float>((&kernelContext_2)->frameParameters_0[(15164U)>>2]);
        float4 _S127 = float4(_S123, _S124, _S125, _S126);
        float _S128 = as_type<float>((&kernelContext_2)->frameParameters_0[(15168U)>>2]);
        float _S129 = as_type<float>((&kernelContext_2)->frameParameters_0[(15172U)>>2]);
        float _S130 = as_type<float>((&kernelContext_2)->frameParameters_0[(15176U)>>2]);
        float _S131 = as_type<float>((&kernelContext_2)->frameParameters_0[(15180U)>>2]);
        float4 _S132 = float4(_S128, _S129, _S130, _S131);
        float _S133 = as_type<float>((&kernelContext_2)->frameParameters_0[(15184U)>>2]);
        float _S134 = as_type<float>((&kernelContext_2)->frameParameters_0[(15188U)>>2]);
        float _S135 = as_type<float>((&kernelContext_2)->frameParameters_0[(15192U)>>2]);
        float _S136 = as_type<float>((&kernelContext_2)->frameParameters_0[(15196U)>>2]);
        float4 _S137 = float4(_S133, _S134, _S135, _S136);
        float _S138 = as_type<float>((&kernelContext_2)->frameParameters_0[(15200U)>>2]);
        float _S139 = as_type<float>((&kernelContext_2)->frameParameters_0[(15204U)>>2]);
        float _S140 = as_type<float>((&kernelContext_2)->frameParameters_0[(15208U)>>2]);
        float _S141 = as_type<float>((&kernelContext_2)->frameParameters_0[(15212U)>>2]);
        float4 _S142 = float4(_S138, _S139, _S140, _S141);
        float _S143 = as_type<float>((&kernelContext_2)->frameParameters_0[(15216U)>>2]);
        float _S144 = as_type<float>((&kernelContext_2)->frameParameters_0[(15220U)>>2]);
        float _S145 = as_type<float>((&kernelContext_2)->frameParameters_0[(15224U)>>2]);
        float _S146 = as_type<float>((&kernelContext_2)->frameParameters_0[(15228U)>>2]);
        float4 _S147 = float4(_S143, _S144, _S145, _S146);
        float _S148 = as_type<float>((&kernelContext_2)->frameParameters_0[(15232U)>>2]);
        float _S149 = as_type<float>((&kernelContext_2)->frameParameters_0[(15236U)>>2]);
        float _S150 = as_type<float>((&kernelContext_2)->frameParameters_0[(15240U)>>2]);
        float _S151 = as_type<float>((&kernelContext_2)->frameParameters_0[(15244U)>>2]);
        float4 _S152 = float4(_S148, _S149, _S150, _S151);
        float _S153 = as_type<float>((&kernelContext_2)->frameParameters_0[(15248U)>>2]);
        float _S154 = as_type<float>((&kernelContext_2)->frameParameters_0[(15252U)>>2]);
        float _S155 = as_type<float>((&kernelContext_2)->frameParameters_0[(15256U)>>2]);
        float _S156 = as_type<float>((&kernelContext_2)->frameParameters_0[(15260U)>>2]);
        float4 _S157 = float4(_S153, _S154, _S155, _S156);
        float _S158 = as_type<float>((&kernelContext_2)->frameParameters_0[(15264U)>>2]);
        float _S159 = as_type<float>((&kernelContext_2)->frameParameters_0[(15268U)>>2]);
        float _S160 = as_type<float>((&kernelContext_2)->frameParameters_0[(15272U)>>2]);
        float _S161 = as_type<float>((&kernelContext_2)->frameParameters_0[(15276U)>>2]);
        float4 _S162 = float4(_S158, _S159, _S160, _S161);
        float _S163 = as_type<float>((&kernelContext_2)->frameParameters_0[(15280U)>>2]);
        float _S164 = as_type<float>((&kernelContext_2)->frameParameters_0[(15284U)>>2]);
        float _S165 = as_type<float>((&kernelContext_2)->frameParameters_0[(15288U)>>2]);
        float _S166 = as_type<float>((&kernelContext_2)->frameParameters_0[(15292U)>>2]);
        float4 _S167 = float4(_S163, _S164, _S165, _S166);
        (&frame_0)->domeAmbient_0[int(0)] = _S92;
        (&frame_0)->domeAmbient_0[int(1)] = _S97;
        (&frame_0)->domeAmbient_0[int(2)] = _S102;
        (&frame_0)->domeAmbient_0[int(3)] = _S107;
        (&frame_0)->domeAmbient_0[int(4)] = _S112;
        (&frame_0)->domeAmbient_0[int(5)] = _S117;
        (&frame_0)->domeAmbient_0[int(6)] = _S122;
        (&frame_0)->domeAmbient_0[int(7)] = _S127;
        (&frame_0)->domeEnvironment_0[int(0)] = _S132;
        (&frame_0)->domeEnvironment_0[int(1)] = _S137;
        (&frame_0)->domeEnvironment_0[int(2)] = _S142;
        (&frame_0)->domeEnvironment_0[int(3)] = _S147;
        (&frame_0)->domeEnvironment_0[int(4)] = _S152;
        (&frame_0)->domeEnvironment_0[int(5)] = _S157;
        (&frame_0)->domeEnvironment_0[int(6)] = _S162;
        (&frame_0)->domeEnvironment_0[int(7)] = _S167;
        _S12 = frame_0;
        break;
    }
    for(;;)
    {
        _S13 = _S12.clipPlaneCount_0;
        uint _S168 = min(_S12.clipPlaneCount_0.x, 8U);
        index_2 = 0U;
        for(;;)
        {
            if(index_2 < _S168)
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
    float4 _S169 = float4(_S16->clearcoatShaded_0) ;
    float shadedMode_0 = _S169.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S16->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S11.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S16->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S11.tint_0.w;
    }
    float4 _S170 = float4(_S16->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S170.xyz;
    float occlusion_0 = _S170.w;
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
        pixelOutput_0 _S171 = { float4(unlitColor_0, opacity_0) };
        return _S171;
    }
    float4 _S172 = float4(_S16->reserved_0) ;
    if((_S172.x) >= 0.5f)
    {
        pixelOutput_0 _S173 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S172.y) * max(0.0f, _S172.z)))) , 1.0f) };
        return _S173;
    }
    float4 _S174 = float4(_S16->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S174.x);
    float roughness_0 = clamp(_S174.y, 0.00999999977648258f, 1.0f);
    float4 _S175 = float4(_S16->specularIor_0) ;
    float3 specularColor_0 = _S175.xyz;
    float ior_0 = _S175.w;
    float clearcoatAmount_0 = _S169.x;
    float clearcoatRoughness_0 = _S169.y;
    float opacityThreshold_0 = _S174.z;
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
    float _S176 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S177 = max(0.00100000004749745f, roughness_0);
    float _S178 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S179 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S179;
    bool omniPbrSpecular_0 = ((float4(_S16->textureControls_0) ).w) >= 0.5f;
    float dielectricLevel_0 = _S169.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S180 = float3(dielectricLevel_0) ;
        float3 _S181 = float3(metallic_0) ;
        float3 _S182 = mix(_S180, diffuseColor_0, _S181);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S180, diffuseColor_0, _S181);
        grazingIncidence_0 = _S182;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S174.w) >= 0.5f)
        {
            float3 _S183 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S183;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S184 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S184);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S184);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S185 = _S12;
    float _S186 = _S12.ambientLight_0.w;
    bool hasSceneLighting_0;
    if(_S186 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S187 = _S185.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S187, _S187)) > 0.0f;
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
    uint _S188 = min(uint(_S186), 128U);
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
    float4 _S189 = float4(_S16->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S189.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S16->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S190 = _S12;
    uint _S191 = min(uint(max(_S12.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S191) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S192 = _S185.ambientLight_0.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S193 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S193;
        for(;;)
        {
            if(lightCount_0 < _S191)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << lightCount_0)) != 0U)
            {
                domeAmbient_1 = domeAmbient_1 + _S12.domeAmbient_0[lightCount_0].xyz;
            }
            lightCount_0 = lightCount_0 + 1U;
        }
    }
    else
    {
        domeAmbient_1 = _S192;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S194 = !hasSceneLighting_0;
    if(_S194)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S188;
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
        bool _S195;
        if(hasSceneLighting_0)
        {
            _S195 = (((uint4(_S16->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S195 = false;
        }
        if(_S195)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S196 = lightIndex_0 == 0U;
        bool _S197;
        if(_S196)
        {
            _S197 = _S194;
        }
        else
        {
            _S197 = false;
        }
        float lightType_0;
        if(_S197)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            float4 _S198 = ReadFrameLight_0(0U, lightIndex_0, &kernelContext_2);
            lightType_0 = _S198.w;
        }
        bool _S199;
        if(_S196)
        {
            _S199 = _S194;
        }
        else
        {
            _S199 = false;
        }
        if(_S199)
        {
            lightDirection_0 = normalize((float4(_S16->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            float4 _S200 = ReadFrameLight_0(1U, lightIndex_0, &kernelContext_2);
            lightDirection_0 = normalize(_S200.xyz);
        }
        bool _S201;
        if(_S196)
        {
            _S201 = _S194;
        }
        else
        {
            _S201 = false;
        }
        float _S202;
        if(_S201)
        {
            _S202 = (float4(_S16->lightDirectionIntensity_0) ).w;
        }
        else
        {
            float4 _S203 = ReadFrameLight_0(2U, lightIndex_0, &kernelContext_2);
            _S202 = _S203.w;
        }
        bool _S204;
        if(_S196)
        {
            _S204 = _S194;
        }
        else
        {
            _S204 = false;
        }
        if(_S204)
        {
            diffuseColor_0 = _S189.xyz;
        }
        else
        {
            float4 _S205 = ReadFrameLight_0(2U, lightIndex_0, &kernelContext_2);
            diffuseColor_0 = _S205.xyz;
        }
        bool _S206;
        if(_S196)
        {
            _S206 = _S194;
        }
        else
        {
            _S206 = false;
        }
        float _S207;
        if(_S206)
        {
            _S207 = 1.0f;
        }
        else
        {
            float4 _S208 = ReadFrameLight_0(3U, lightIndex_0, &kernelContext_2);
            _S207 = _S208.x;
        }
        bool _S209;
        if(_S196)
        {
            _S209 = _S194;
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
            _S210 = _S211.y;
        }
        bool _S212;
        if(_S196)
        {
            _S212 = _S194;
        }
        else
        {
            _S212 = false;
        }
        if(_S212)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            float4 _S213 = ReadFrameLight_0(4U, lightIndex_0, &kernelContext_2);
            lightTangent_0 = normalize(_S213.xyz);
        }
        bool _S214;
        if(_S196)
        {
            _S214 = _S194;
        }
        else
        {
            _S214 = false;
        }
        if(_S214)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            float4 _S215 = ReadFrameLight_0(5U, lightIndex_0, &kernelContext_2);
            lightBitangent_0 = normalize(_S215.xyz);
        }
        bool _S216;
        if(_S196)
        {
            _S216 = _S194;
        }
        else
        {
            _S216 = false;
        }
        float shapeX_0;
        if(_S216)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            float4 _S217 = ReadFrameLight_0(4U, lightIndex_0, &kernelContext_2);
            shapeX_0 = _S217.w;
        }
        bool _S218;
        if(_S196)
        {
            _S218 = _S194;
        }
        else
        {
            _S218 = false;
        }
        float shapeY_0;
        if(_S218)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            float4 _S219 = ReadFrameLight_0(5U, lightIndex_0, &kernelContext_2);
            shapeY_0 = _S219.w;
        }
        bool _S220;
        if(_S196)
        {
            _S220 = _S194;
        }
        else
        {
            _S220 = false;
        }
        float lightRadius_0;
        if(_S220)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            float4 _S221 = ReadFrameLight_0(1U, lightIndex_0, &kernelContext_2);
            lightRadius_0 = _S221.w;
        }
        bool _S222;
        if(_S196)
        {
            _S222 = _S194;
        }
        else
        {
            _S222 = false;
        }
        if(_S222)
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
            float4 _S223 = ReadFrameShadowSlot_0(lightIndex_0, &kernelContext_2);
            int shadowSlot_0 = int(_S223.x);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S224 = _S12;
                    FragmentFrameParameters_0 _S225 = _S12;
                    if((dot(irradiance_0, lightDirection_0)) < 0.0f)
                    {
                        color_3 = - irradiance_0;
                    }
                    else
                    {
                        color_3 = irradiance_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_3, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_3 * float3((_S224.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S12.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S226 = lightClip_0.w;
                    if(_S226 <= 0.0f)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S226) ;
                    bool _S227;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S227 = true;
                    }
                    else
                    {
                        _S227 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S228;
                    if(_S227)
                    {
                        _S228 = true;
                    }
                    else
                    {
                        _S228 = (ndc_0.z) < 0.0f;
                    }
                    bool _S229;
                    if(_S228)
                    {
                        _S229 = true;
                    }
                    else
                    {
                        _S229 = (ndc_0.z) > 1.0f;
                    }
                    if(_S229)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float2 _S230 = _S225.shadowTile_0[shadowSlot_0].xy;
                    float2 _S231 = _S225.shadowTile_0[shadowSlot_0].zw;
                    float2 _S232 = _S230 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S231;
                    float texel_0 = _S224.shadowControls_0[shadowSlot_0].w;
                    float _S233 = max(_S224.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S234 = ndc_0.z - _S224.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S235 = float2((texel_0 * 0.5f)) ;
                    float2 _S236 = _S230 + _S235;
                    float2 _S237 = _S230 + _S231 - _S235;
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
                            if(_S234 <= (((&kernelContext_2)->shadowAtlas_0).sample(((&kernelContext_2)->shadowSampler_0), (clamp(_S232 + float2(float(x_0), float(y_0)) * float2((_S233 * texel_0)) , _S236, _S237)), level((0.0f))).x))
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
        float3 _S238 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S238;
        sampleOffsets_0[int(1)] = _S238;
        sampleOffsets_0[int(2)] = _S238;
        sampleOffsets_0[int(3)] = _S238;
        sampleOffsets_0[int(4)] = _S238;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S239 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S239 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S239 - halfHeight_0;
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
            float sampleIntensity_0 = _S202 / sampleCount_0;
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(lightType_0 >= 2.0f)
            {
                float4 _S240 = ReadFrameLight_0(0U, lightIndex_0, &kernelContext_2);
                float3 toLight_0 = _S240.xyz + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S241 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S241)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S241;
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
            float _S242 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_0;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_0 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S242)) ;
            }
            else
            {
                diffuseFresnel_0 = mix(normalIncidence_0, grazingIncidence_0, float3(_S242) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_0);
            float3 _S243 = float3(_S242) ;
            float _S244 = max(_S177, 0.00100000004749745f);
            float alpha_0 = _S244 * _S244;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S245 = 1.0f - k_0;
            float3 _S246 = float3(max(4.0f * normalDotLight_0 * _S176, 1.00000000317107685e-30f)) ;
            float3 _S247 = mix(normalIncidence_0, grazingIncidence_0, _S243) * float3((_S176 / (_S176 * _S245 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S245 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S246;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S248 = max(_S178, 0.00100000004749745f);
                float alpha_1 = _S248 * _S248;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S249 = 1.0f - k_1;
                directSpecular_0 = _S247 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S243) * float3((_S176 / (_S176 * _S249 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S249 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S246);
            }
            else
            {
                directSpecular_0 = _S247;
            }
            float3 _S250 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S207)  * (_S250 * _S179) + directSpecular_0 * float3(_S210)  * _S250);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S251 = _S12;
    if((_S12.environmentControls_0.x) >= 0.5f)
    {
        float _S252 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S253 = saturate(_S177);
        float2 _S254 = (((&kernelContext_2)->environmentBrdf_0).sample(((&kernelContext_2)->environmentBrdfSampler_0), (float2(_S252, _S253)), level((0.0f)))).xy;
        float _S255 = _S254.x;
        float _S256 = _S254.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S255)  + grazingIncidence_0 * float3(_S256) ;
        if(omniPbrSpecular_0)
        {
            unlitColor_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S255 + _S256)) ;
        }
        else
        {
            unlitColor_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - unlitColor_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S257 = max(_S190.domeControls_0.y, 1.0f);
        float3 _S258 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S257 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S190.domeControls_0.z;
            float _S259 = _S190.domeControls_0.w;
            for(;;)
            {
                bool _S260 = _S257 <= 1.0f;
                _S14 = _S260;
                if(_S260)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S259, 1.0f);
                diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S257)), level((0.0f)))).xyz;
                break;
            }
            float _S261 = _S251.environmentControls_0.y;
            float _S262 = _S251.environmentControls_0.z;
            for(;;)
            {
                if(_S14)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S263 = max(_S261, 1.0f);
                    float _S264 = _S263 - 1.0f;
                    float slice_0 = _S253 * max(_S264, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S262, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S263)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S264) + v_0) / _S263)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S265 = max(_S261, 1.0f);
                float total_0 = _S265 * _S257;
                float _S266 = _S265 - 1.0f;
                float slice_1 = _S253 * max(_S266, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S262, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S265;
                unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S266) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S14)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_2 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S267 = max(_S261, 1.0f);
                        float _S268 = _S267 - 1.0f;
                        float slice_2 = saturate(_S178) * max(_S268, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S262, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S267)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S268) + v_2) / _S267)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S269 = max(_S261, 1.0f);
                    float total_1 = _S269 * _S257;
                    float _S270 = _S269 - 1.0f;
                    float slice_3 = saturate(_S178) * max(_S270, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S262, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S269;
                    normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S270) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightTangent_0 = normal_2;
            }
            else
            {
                lightTangent_0 = _S258;
            }
            irradiance_0 = diffuseColor_0;
            lightDirection_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S258;
            lightDirection_0 = _S258;
            lightTangent_0 = _S258;
            for(;;)
            {
                if(sampleIndex_0 < _S191)
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
                float _S271 = _S190.domeControls_0.w;
                for(;;)
                {
                    bool _S272 = _S257 <= 1.0f;
                    _S15 = _S272;
                    if(_S272)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S271, 1.0f);
                    diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S257)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                float _S273 = _S251.environmentControls_0.y;
                float _S274 = _S251.environmentControls_0.z;
                for(;;)
                {
                    if(_S15)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S275 = max(_S273, 1.0f);
                        float _S276 = _S275 - 1.0f;
                        float slice_4 = _S253 * max(_S276, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S274, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S275)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S276) + v_4) / _S275)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S277 = max(_S273, 1.0f);
                    float total_2 = _S277 * _S257;
                    float _S278 = _S277 - 1.0f;
                    float slice_5 = _S253 * max(_S278, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S274, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S277;
                    unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S278) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightDirection_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S15)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S279 = max(_S273, 1.0f);
                            float _S280 = _S279 - 1.0f;
                            float slice_6 = saturate(_S178) * max(_S280, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S274, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S279)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S280) + v_6) / _S279)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S281 = max(_S273, 1.0f);
                        float total_3 = _S281 * _S257;
                        float _S282 = _S281 - 1.0f;
                        float slice_7 = saturate(_S178) * max(_S282, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S274, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S281;
                        normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S282) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
        float3 _S283 = float3(occlusion_0) ;
        float3 color_4 = color_2 + _S283 * diffuse_1 * irradiance_0 * diffuseWeight_0 + _S283 * lightDirection_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S284 = (((&kernelContext_2)->environmentBrdf_0).sample(((&kernelContext_2)->environmentBrdfSampler_0), (float2(_S252, saturate(_S178))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightTangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S284.x)  + float3(_S284.y) );
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
    pixelOutput_0 _S285 = { float4(color_2, opacity_0) };
    return _S285;
}
