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
    texture2d<float, access::sample> normalTexture_0;
    sampler normalSampler_0;
    texture2d<float, access::sample> normalValuesTexture_0;
    texture2d<float, access::sample> detailNormalTexture_0;
    sampler detailNormalSampler_0;
    texture2d<float, access::sample> detailNormalValuesTexture_0;
    texture2d<float, access::sample> shadowAtlas_0;
    sampler shadowSampler_0;
    texture2d<float, access::sample> environmentBrdf_0;
    sampler environmentBrdfSampler_0;
    texture2d<float, access::sample> environmentIrradiance_0;
    sampler environmentSampler_0;
    texture2d<float, access::sample> environmentSpecular_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_normal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> normalValuesTexture_1 [[texture(20)]], texture2d<float, access::sample> detailNormalTexture_1 [[texture(9)]], sampler detailNormalSampler_1 [[sampler(4)]], texture2d<float, access::sample> detailNormalValuesTexture_1 [[texture(21)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_0;
    FragmentFrameParameters_0 _S2;
    uint4 _S3;
    float lightType_0;
    float _S4;
    bool hasSceneLighting_0;
    float4 _S5;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    float3 color_0;
    bool _S6;
    bool _S7;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->normalTexture_0 = normalTexture_1;
    (&kernelContext_0)->normalSampler_0 = normalSampler_1;
    (&kernelContext_0)->normalValuesTexture_0 = normalValuesTexture_1;
    (&kernelContext_0)->detailNormalTexture_0 = detailNormalTexture_1;
    (&kernelContext_0)->detailNormalSampler_0 = detailNormalSampler_1;
    (&kernelContext_0)->detailNormalValuesTexture_0 = detailNormalValuesTexture_1;
    (&kernelContext_0)->shadowAtlas_0 = shadowAtlas_1;
    (&kernelContext_0)->shadowSampler_0 = shadowSampler_1;
    (&kernelContext_0)->environmentBrdf_0 = environmentBrdf_1;
    (&kernelContext_0)->environmentBrdfSampler_0 = environmentBrdfSampler_1;
    (&kernelContext_0)->environmentIrradiance_0 = environmentIrradiance_1;
    (&kernelContext_0)->environmentSampler_0 = environmentSampler_1;
    (&kernelContext_0)->environmentSpecular_0 = environmentSpecular_1;
    SurfaceParameters_natural_0 device* _S8 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        uint _S9 = as_type<uint>((&kernelContext_0)->frameParameters_0[(64U)>>2]);
        uint _S10 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
        uint _S11 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
        uint _S12 = as_type<uint>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S9, _S10, _S11, _S12);
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
            uint _S13 = 80U + index_0 * 16U;
            float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S13)>>2]);
            float _S15 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S13 + 4U)>>2]);
            float _S16 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S13 + 8U)>>2]);
            float _S17 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S13 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_0] = float4(_S14, _S15, _S16, _S17);
            index_0 = index_0 + 1U;
        }
        float _S18 = as_type<float>((&kernelContext_0)->frameParameters_0[(208U)>>2]);
        float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(212U)>>2]);
        float _S20 = as_type<float>((&kernelContext_0)->frameParameters_0[(216U)>>2]);
        float _S21 = as_type<float>((&kernelContext_0)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S18, _S19, _S20, _S21);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 12960U;
        float _S22 = as_type<float>((&kernelContext_0)->frameParameters_0[(12512U)>>2]);
        float _S23 = as_type<float>((&kernelContext_0)->frameParameters_0[(12516U)>>2]);
        float _S24 = as_type<float>((&kernelContext_0)->frameParameters_0[(12520U)>>2]);
        float _S25 = as_type<float>((&kernelContext_0)->frameParameters_0[(12524U)>>2]);
        float4 _S26 = float4(_S22, _S23, _S24, _S25);
        float _S27 = as_type<float>((&kernelContext_0)->frameParameters_0[(12528U)>>2]);
        float _S28 = as_type<float>((&kernelContext_0)->frameParameters_0[(12532U)>>2]);
        float _S29 = as_type<float>((&kernelContext_0)->frameParameters_0[(12536U)>>2]);
        float _S30 = as_type<float>((&kernelContext_0)->frameParameters_0[(12540U)>>2]);
        float4 _S31 = float4(_S27, _S28, _S29, _S30);
        float _S32 = as_type<float>((&kernelContext_0)->frameParameters_0[(12544U)>>2]);
        float _S33 = as_type<float>((&kernelContext_0)->frameParameters_0[(12548U)>>2]);
        float _S34 = as_type<float>((&kernelContext_0)->frameParameters_0[(12552U)>>2]);
        float _S35 = as_type<float>((&kernelContext_0)->frameParameters_0[(12556U)>>2]);
        float4 _S36 = float4(_S32, _S33, _S34, _S35);
        float _S37 = as_type<float>((&kernelContext_0)->frameParameters_0[(12560U)>>2]);
        float _S38 = as_type<float>((&kernelContext_0)->frameParameters_0[(12564U)>>2]);
        float _S39 = as_type<float>((&kernelContext_0)->frameParameters_0[(12568U)>>2]);
        float _S40 = as_type<float>((&kernelContext_0)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S26, _S31, _S36, float4(_S37, _S38, _S39, _S40));
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
            uint _S41 = 12576U + index_0 * 64U;
            float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41)>>2]);
            float _S43 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 4U)>>2]);
            float _S44 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 8U)>>2]);
            float _S45 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 12U)>>2]);
            float4 _S46 = float4(_S42, _S43, _S44, _S45);
            float _S47 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 16U)>>2]);
            float _S48 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 20U)>>2]);
            float _S49 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 24U)>>2]);
            float _S50 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 28U)>>2]);
            float4 _S51 = float4(_S47, _S48, _S49, _S50);
            float _S52 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 32U)>>2]);
            float _S53 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 36U)>>2]);
            float _S54 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 40U)>>2]);
            float _S55 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 44U)>>2]);
            float4 _S56 = float4(_S52, _S53, _S54, _S55);
            float _S57 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 48U)>>2]);
            float _S58 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 52U)>>2]);
            float _S59 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 56U)>>2]);
            float _S60 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S41 + 60U)>>2]);
            (&frame_0)->shadowWorldToLightClip_0[index_0] = matrix<float,int(4),int(4)> (_S46, _S51, _S56, float4(_S57, _S58, _S59, _S60));
            uint _S61 = index_0 * 16U;
            uint _S62 = 12832U + _S61;
            float _S63 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S62)>>2]);
            float _S64 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S62 + 4U)>>2]);
            float _S65 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S62 + 8U)>>2]);
            float _S66 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S62 + 12U)>>2]);
            (&frame_0)->shadowTile_0[index_0] = float4(_S63, _S64, _S65, _S66);
            uint _S67 = 12896U + _S61;
            float _S68 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S67)>>2]);
            float _S69 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S67 + 4U)>>2]);
            float _S70 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S67 + 8U)>>2]);
            float _S71 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S67 + 12U)>>2]);
            (&frame_0)->shadowControls_0[index_0] = float4(_S68, _S69, _S70, _S71);
            index_0 = index_0 + 1U;
        }
        float _S72 = as_type<float>((&kernelContext_0)->frameParameters_0[(15008U)>>2]);
        float _S73 = as_type<float>((&kernelContext_0)->frameParameters_0[(15012U)>>2]);
        float _S74 = as_type<float>((&kernelContext_0)->frameParameters_0[(15016U)>>2]);
        float _S75 = as_type<float>((&kernelContext_0)->frameParameters_0[(15020U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S72, _S73, _S74, _S75);
        float _S76 = as_type<float>((&kernelContext_0)->frameParameters_0[(15024U)>>2]);
        float _S77 = as_type<float>((&kernelContext_0)->frameParameters_0[(15028U)>>2]);
        float _S78 = as_type<float>((&kernelContext_0)->frameParameters_0[(15032U)>>2]);
        float _S79 = as_type<float>((&kernelContext_0)->frameParameters_0[(15036U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S76, _S77, _S78, _S79);
        float _S80 = as_type<float>((&kernelContext_0)->frameParameters_0[(15040U)>>2]);
        float _S81 = as_type<float>((&kernelContext_0)->frameParameters_0[(15044U)>>2]);
        float _S82 = as_type<float>((&kernelContext_0)->frameParameters_0[(15048U)>>2]);
        float _S83 = as_type<float>((&kernelContext_0)->frameParameters_0[(15052U)>>2]);
        float4 _S84 = float4(_S80, _S81, _S82, _S83);
        float _S85 = as_type<float>((&kernelContext_0)->frameParameters_0[(15056U)>>2]);
        float _S86 = as_type<float>((&kernelContext_0)->frameParameters_0[(15060U)>>2]);
        float _S87 = as_type<float>((&kernelContext_0)->frameParameters_0[(15064U)>>2]);
        float _S88 = as_type<float>((&kernelContext_0)->frameParameters_0[(15068U)>>2]);
        float4 _S89 = float4(_S85, _S86, _S87, _S88);
        float _S90 = as_type<float>((&kernelContext_0)->frameParameters_0[(15072U)>>2]);
        float _S91 = as_type<float>((&kernelContext_0)->frameParameters_0[(15076U)>>2]);
        float _S92 = as_type<float>((&kernelContext_0)->frameParameters_0[(15080U)>>2]);
        float _S93 = as_type<float>((&kernelContext_0)->frameParameters_0[(15084U)>>2]);
        float4 _S94 = float4(_S90, _S91, _S92, _S93);
        float _S95 = as_type<float>((&kernelContext_0)->frameParameters_0[(15088U)>>2]);
        float _S96 = as_type<float>((&kernelContext_0)->frameParameters_0[(15092U)>>2]);
        float _S97 = as_type<float>((&kernelContext_0)->frameParameters_0[(15096U)>>2]);
        float _S98 = as_type<float>((&kernelContext_0)->frameParameters_0[(15100U)>>2]);
        float4 _S99 = float4(_S95, _S96, _S97, _S98);
        float _S100 = as_type<float>((&kernelContext_0)->frameParameters_0[(15104U)>>2]);
        float _S101 = as_type<float>((&kernelContext_0)->frameParameters_0[(15108U)>>2]);
        float _S102 = as_type<float>((&kernelContext_0)->frameParameters_0[(15112U)>>2]);
        float _S103 = as_type<float>((&kernelContext_0)->frameParameters_0[(15116U)>>2]);
        float4 _S104 = float4(_S100, _S101, _S102, _S103);
        float _S105 = as_type<float>((&kernelContext_0)->frameParameters_0[(15120U)>>2]);
        float _S106 = as_type<float>((&kernelContext_0)->frameParameters_0[(15124U)>>2]);
        float _S107 = as_type<float>((&kernelContext_0)->frameParameters_0[(15128U)>>2]);
        float _S108 = as_type<float>((&kernelContext_0)->frameParameters_0[(15132U)>>2]);
        float4 _S109 = float4(_S105, _S106, _S107, _S108);
        float _S110 = as_type<float>((&kernelContext_0)->frameParameters_0[(15136U)>>2]);
        float _S111 = as_type<float>((&kernelContext_0)->frameParameters_0[(15140U)>>2]);
        float _S112 = as_type<float>((&kernelContext_0)->frameParameters_0[(15144U)>>2]);
        float _S113 = as_type<float>((&kernelContext_0)->frameParameters_0[(15148U)>>2]);
        float4 _S114 = float4(_S110, _S111, _S112, _S113);
        float _S115 = as_type<float>((&kernelContext_0)->frameParameters_0[(15152U)>>2]);
        float _S116 = as_type<float>((&kernelContext_0)->frameParameters_0[(15156U)>>2]);
        float _S117 = as_type<float>((&kernelContext_0)->frameParameters_0[(15160U)>>2]);
        float _S118 = as_type<float>((&kernelContext_0)->frameParameters_0[(15164U)>>2]);
        float4 _S119 = float4(_S115, _S116, _S117, _S118);
        float _S120 = as_type<float>((&kernelContext_0)->frameParameters_0[(15168U)>>2]);
        float _S121 = as_type<float>((&kernelContext_0)->frameParameters_0[(15172U)>>2]);
        float _S122 = as_type<float>((&kernelContext_0)->frameParameters_0[(15176U)>>2]);
        float _S123 = as_type<float>((&kernelContext_0)->frameParameters_0[(15180U)>>2]);
        float4 _S124 = float4(_S120, _S121, _S122, _S123);
        float _S125 = as_type<float>((&kernelContext_0)->frameParameters_0[(15184U)>>2]);
        float _S126 = as_type<float>((&kernelContext_0)->frameParameters_0[(15188U)>>2]);
        float _S127 = as_type<float>((&kernelContext_0)->frameParameters_0[(15192U)>>2]);
        float _S128 = as_type<float>((&kernelContext_0)->frameParameters_0[(15196U)>>2]);
        float4 _S129 = float4(_S125, _S126, _S127, _S128);
        float _S130 = as_type<float>((&kernelContext_0)->frameParameters_0[(15200U)>>2]);
        float _S131 = as_type<float>((&kernelContext_0)->frameParameters_0[(15204U)>>2]);
        float _S132 = as_type<float>((&kernelContext_0)->frameParameters_0[(15208U)>>2]);
        float _S133 = as_type<float>((&kernelContext_0)->frameParameters_0[(15212U)>>2]);
        float4 _S134 = float4(_S130, _S131, _S132, _S133);
        float _S135 = as_type<float>((&kernelContext_0)->frameParameters_0[(15216U)>>2]);
        float _S136 = as_type<float>((&kernelContext_0)->frameParameters_0[(15220U)>>2]);
        float _S137 = as_type<float>((&kernelContext_0)->frameParameters_0[(15224U)>>2]);
        float _S138 = as_type<float>((&kernelContext_0)->frameParameters_0[(15228U)>>2]);
        float4 _S139 = float4(_S135, _S136, _S137, _S138);
        float _S140 = as_type<float>((&kernelContext_0)->frameParameters_0[(15232U)>>2]);
        float _S141 = as_type<float>((&kernelContext_0)->frameParameters_0[(15236U)>>2]);
        float _S142 = as_type<float>((&kernelContext_0)->frameParameters_0[(15240U)>>2]);
        float _S143 = as_type<float>((&kernelContext_0)->frameParameters_0[(15244U)>>2]);
        float4 _S144 = float4(_S140, _S141, _S142, _S143);
        float _S145 = as_type<float>((&kernelContext_0)->frameParameters_0[(15248U)>>2]);
        float _S146 = as_type<float>((&kernelContext_0)->frameParameters_0[(15252U)>>2]);
        float _S147 = as_type<float>((&kernelContext_0)->frameParameters_0[(15256U)>>2]);
        float _S148 = as_type<float>((&kernelContext_0)->frameParameters_0[(15260U)>>2]);
        float4 _S149 = float4(_S145, _S146, _S147, _S148);
        float _S150 = as_type<float>((&kernelContext_0)->frameParameters_0[(15264U)>>2]);
        float _S151 = as_type<float>((&kernelContext_0)->frameParameters_0[(15268U)>>2]);
        float _S152 = as_type<float>((&kernelContext_0)->frameParameters_0[(15272U)>>2]);
        float _S153 = as_type<float>((&kernelContext_0)->frameParameters_0[(15276U)>>2]);
        float4 _S154 = float4(_S150, _S151, _S152, _S153);
        float _S155 = as_type<float>((&kernelContext_0)->frameParameters_0[(15280U)>>2]);
        float _S156 = as_type<float>((&kernelContext_0)->frameParameters_0[(15284U)>>2]);
        float _S157 = as_type<float>((&kernelContext_0)->frameParameters_0[(15288U)>>2]);
        float _S158 = as_type<float>((&kernelContext_0)->frameParameters_0[(15292U)>>2]);
        float4 _S159 = float4(_S155, _S156, _S157, _S158);
        (&frame_0)->domeAmbient_0[int(0)] = _S84;
        (&frame_0)->domeAmbient_0[int(1)] = _S89;
        (&frame_0)->domeAmbient_0[int(2)] = _S94;
        (&frame_0)->domeAmbient_0[int(3)] = _S99;
        (&frame_0)->domeAmbient_0[int(4)] = _S104;
        (&frame_0)->domeAmbient_0[int(5)] = _S109;
        (&frame_0)->domeAmbient_0[int(6)] = _S114;
        (&frame_0)->domeAmbient_0[int(7)] = _S119;
        (&frame_0)->domeEnvironment_0[int(0)] = _S124;
        (&frame_0)->domeEnvironment_0[int(1)] = _S129;
        (&frame_0)->domeEnvironment_0[int(2)] = _S134;
        (&frame_0)->domeEnvironment_0[int(3)] = _S139;
        (&frame_0)->domeEnvironment_0[int(4)] = _S144;
        (&frame_0)->domeEnvironment_0[int(5)] = _S149;
        (&frame_0)->domeEnvironment_0[int(6)] = _S154;
        (&frame_0)->domeEnvironment_0[int(7)] = _S159;
        _S2 = frame_0;
        break;
    }
    for(;;)
    {
        _S3 = _S2.clipPlaneCount_0;
        uint _S160 = min(_S2.clipPlaneCount_0.x, 8U);
        index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S160)
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
    float4 _S161 = float4(_S8->clearcoatShaded_0) ;
    float shadedMode_0 = _S161.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S8->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S1.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S8->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S1.tint_0.w;
    }
    float4 _S162 = float4(_S8->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S162.xyz;
    float occlusion_0 = _S162.w;
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
    float4 _S164 = float4(_S8->reserved_0) ;
    if((_S164.x) >= 0.5f)
    {
        pixelOutput_0 _S165 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S164.y) * max(0.0f, _S164.z)))) , 1.0f) };
        return _S165;
    }
    float4 _S166 = float4(_S8->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S166.x);
    float roughness_0 = clamp(_S166.y, 0.00999999977648258f, 1.0f);
    float4 _S167 = float4(_S8->specularIor_0) ;
    float3 specularColor_0 = _S167.xyz;
    float ior_0 = _S167.w;
    float clearcoatAmount_0 = _S161.x;
    float clearcoatRoughness_0 = _S161.y;
    float4 _S168 = float4(_S8->textureControls_0) ;
    uint textureMask_0 = uint(round(_S168.x));
    uint udimMask_0 = uint(round(_S168.y));
    float4 _S169 = float4(_S8->uvTransformRow1_0) ;
    uint secondaryMask_0 = uint(round(_S169.w));
    float4 _S170 = float4(_S8->domeLinkControls_0) ;
    uint alternateMask_0 = uint(round(_S170.w));
    float4 _S171 = float4(_S8->uvTransformRow0_0) ;
    float2 _S172 = _S171.xy;
    float _S173 = _S171.z;
    float2 _S174 = _S169.xy;
    float _S175 = _S169.z;
    float2 _S176 = float2(dot(_S172, _S1.texCoord_0) + _S173, dot(_S174, _S1.texCoord_0) + _S175);
    float2 _S177 = float2(dot(_S172, _S1.texCoord1_0) + _S173, dot(_S174, _S1.texCoord1_0) + _S175);
    float4 _S178 = float4(_S8->alternateUvTransformRow0_0) ;
    float4 _S179 = float4(_S8->alternateUvTransformRow1_0) ;
    float2 _S180 = _S178.xy;
    float _S181 = _S178.z;
    float2 _S182 = _S179.xy;
    float _S183 = _S179.z;
    float2 _S184 = float2(dot(_S180, _S1.texCoord_0) + _S181, dot(_S182, _S1.texCoord_0) + _S183);
    float2 _S185 = float2(dot(_S180, _S1.texCoord1_0) + _S181, dot(_S182, _S1.texCoord1_0) + _S183);
    thread float4 baseNormalSample_0;
    bool useSecondary_0 = (secondaryMask_0 & 4U) != 0U;
    bool _S186 = (alternateMask_0 & 4U) != 0U;
    float2 detailSource_0;
    if(_S186)
    {
        if(useSecondary_0)
        {
            detailSource_0 = _S185;
        }
        else
        {
            detailSource_0 = _S184;
        }
    }
    else
    {
        if(useSecondary_0)
        {
            detailSource_0 = _S177;
        }
        else
        {
            detailSource_0 = _S176;
        }
    }
    bool _S187 = (udimMask_0 & 4U) != 0U;
    uint maskU_0 = uint(round(_S170.y));
    uint maskV_0 = uint(round(_S170.z));
    if((maskU_0 & 4U) != 0U)
    {
        index_0 = 1U;
    }
    else
    {
        index_0 = 0U;
    }
    uint lightCount_0;
    if((maskV_0 & 4U) != 0U)
    {
        lightCount_0 = 2U;
    }
    else
    {
        lightCount_0 = 0U;
    }
    uint _S188 = index_0 | lightCount_0;
    for(;;)
    {
        if(!_S187)
        {
            for(;;)
            {
                if(_S188 == 0U)
                {
                    break;
                }
                texture2d<float, access::sample> _S189 = (&kernelContext_0)->normalTexture_0;
                thread uint width_0;
                thread uint height_0;
                (*((&width_0)) = (_S189).get_width(0)),(*((&height_0)) = (_S189).get_height(0));
                float2 size_0 = float2(float(width_0), float(height_0));
                float2 _S190 = float2(0.5f) ;
                float2 remapped_0 = clamp((detailSource_0 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S190 / size_0, (size_0 - _S190) / size_0);
                if((_S188 & 1U) != 0U)
                {
                    lightType_0 = remapped_0.x;
                }
                else
                {
                    lightType_0 = detailSource_0.x;
                }
                if((_S188 & 2U) != 0U)
                {
                    _S4 = remapped_0.y;
                }
                else
                {
                    _S4 = detailSource_0.y;
                }
                detailSource_0 = float2(lightType_0, _S4);
                break;
            }
            _S5 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (detailSource_0)));
            break;
        }
        texture2d<float, access::sample> _S191 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_0;
        thread uint atlasHeight_0;
        (*((&atlasWidth_0)) = (_S191).get_width(0)),(*((&atlasHeight_0)) = (_S191).get_height(0));
        int3 _S192 = int3(int(0), int(0), int(0));
        float4 metadata_0 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S192)).xy), uint(((_S192)).z))) * float4(255.0f) );
        if((metadata_0.x) == 255.0f)
        {
            uint _S193 = atlasWidth_0;
            uint _S194 = 2U % atlasWidth_0;
            int _S195 = int(_S194);
            uint _S196 = 2U / _S193;
            int3 _S197 = int3(_S195, int(_S196), int(0));
            int _S198 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S197)).xy), uint(((_S197)).z))).x * 255.0f)));
            uint _S199 = atlasWidth_0;
            uint _S200 = 3U % atlasWidth_0;
            int _S201 = int(_S200);
            uint _S202 = 3U / _S199;
            int3 _S203 = int3(_S201, int(_S202), int(0));
            int2 minimumTile_0 = int2(_S198, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S203)).xy), uint(((_S203)).z))).x * 255.0f))));
            uint _S204 = atlasWidth_0;
            uint _S205 = 4U % atlasWidth_0;
            int _S206 = int(_S205);
            uint _S207 = 4U / _S204;
            int3 _S208 = int3(_S206, int(_S207), int(0));
            uint _S209 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S208)).xy), uint(((_S208)).z))).x * 255.0f));
            uint _S210 = atlasWidth_0;
            uint _S211 = 5U % atlasWidth_0;
            int _S212 = int(_S211);
            uint _S213 = 5U / _S210;
            int3 _S214 = int3(_S212, int(_S213), int(0));
            uint _S215 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S214)).xy), uint(((_S214)).z))).x * 255.0f));
            uint2 grid_0 = uint2(_S209, _S215);
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
                int3 _S216 = int3(int(1), int(0), int(0));
                _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S216)).xy), uint(((_S216)).z)));
                break;
            }
            uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S209 + uint(tile_0.x));
            uint _S217 = atlasWidth_0;
            uint _S218 = offset_0 % atlasWidth_0;
            int _S219 = int(_S218);
            uint _S220 = offset_0 / _S217;
            int3 _S221 = int3(_S219, int(_S220), int(0));
            uint _S222 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S221)).xy), uint(((_S221)).z))).x * 255.0f));
            uint _S223 = atlasWidth_0;
            uint _S224 = offset_0 + 1U;
            uint _S225 = _S224 % atlasWidth_0;
            int _S226 = int(_S225);
            uint _S227 = _S224 / _S223;
            int3 _S228 = int3(_S226, int(_S227), int(0));
            uint slot_0 = _S222 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S228)).xy), uint(((_S228)).z))).x * 255.0f)) << 8U);
            if(slot_0 == 0U)
            {
                int3 _S229 = int3(int(1), int(0), int(0));
                _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S229)).xy), uint(((_S229)).z)));
                break;
            }
            uint slot_1 = slot_0 - 1U;
            uint _S230 = atlasWidth_0;
            uint _S231 = 6U % atlasWidth_0;
            int _S232 = int(_S231);
            uint _S233 = 6U / _S230;
            int3 _S234 = int3(_S232, int(_S233), int(0));
            uint _S235 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S234)).xy), uint(((_S234)).z))).x * 255.0f));
            uint _S236 = atlasWidth_0;
            uint _S237 = 7U % atlasWidth_0;
            int _S238 = int(_S237);
            uint _S239 = 7U / _S236;
            int3 _S240 = int3(_S238, int(_S239), int(0));
            uint columns_0 = _S235 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S240)).xy), uint(((_S240)).z))).x * 255.0f)) << 8U);
            uint _S241 = atlasWidth_0;
            uint _S242 = 8U % atlasWidth_0;
            int _S243 = int(_S242);
            uint _S244 = 8U / _S241;
            int3 _S245 = int3(_S243, int(_S244), int(0));
            uint _S246 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S245)).xy), uint(((_S245)).z))).x * 255.0f));
            uint _S247 = atlasWidth_0;
            uint _S248 = 9U % atlasWidth_0;
            int _S249 = int(_S248);
            uint _S250 = 9U / _S247;
            int3 _S251 = int3(_S249, int(_S250), int(0));
            uint rows_0 = ((_S246 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S251)).xy), uint(((_S251)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
            uint metadataRows_0 = (10U + 2U * _S209 * _S215 + atlasWidth_0 - 1U) / atlasWidth_0;
            uint _S252 = atlasWidth_0 / columns_0;
            float _S253 = float(_S252);
            uint _S254 = (atlasHeight_0 - metadataRows_0) / rows_0;
            float2 cellSize_0 = float2(_S253, float(_S254));
            uint _S255 = slot_1 % columns_0;
            float _S256 = float(_S255);
            uint _S257 = slot_1 / columns_0;
            _S5 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S256, float(_S257)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(detailSource_0) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        int2 _S258 = int2(metadata_0.zw);
        int2 tile_1 = int2(floor(detailSource_0)) - int2(metadata_0.xy);
        if(any(tile_1 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_1 >= _S258);
        }
        if(hasSceneLighting_0)
        {
            int3 _S259 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
            _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S259)).xy), uint(((_S259)).z)));
            break;
        }
        uint _S260 = atlasWidth_0 / uint(_S258.x);
        float _S261 = float(_S260);
        uint _S262 = (atlasHeight_0 - 1U) / uint(_S258.y);
        float2 cellSize_1 = float2(_S261, float(_S262));
        _S5 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
        break;
    }
    baseNormalSample_0 = _S5;
    float4 _S263 = float4(_S8->detailUvTransformRow0_0) ;
    uint3 _S264;
    if((_S263.w) != 0.0f)
    {
        float4 _S265 = baseNormalSample_0;
        for(;;)
        {
            float _S266 = _S265.w;
            if(_S266 > 1.0f)
            {
                unlitColor_0 = _S265.xyz;
                break;
            }
            texture2d<float, access::sample> _S267 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_1;
            thread uint height_1;
            (*((&width_1)) = (_S267).get_width(0)),(*((&height_1)) = (_S267).get_height(0));
            uint3 _S268 = uint3(round(_S265.xyz * float3(255.0f) ));
            uint index_1 = ((_S268.x) | ((_S268.y) << 8U)) | ((_S268.z) << 16U);
            if(_S266 == 0.0f)
            {
                _S264 = _S268;
            }
            else
            {
                _S264 = uint3(index_1, index_1, index_1);
            }
            uint3 address_0 = _S264 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S269 = address_0.x;
            uint _S270 = _S269 % width_1;
            int _S271 = int(_S270);
            uint _S272 = _S269 / width_1;
            int3 _S273 = int3(_S271, int(_S272), int(0));
            float _S274 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S273)).xy), uint(((_S273)).z))).x;
            uint _S275 = address_0.y;
            uint _S276 = _S275 % width_1;
            int _S277 = int(_S276);
            uint _S278 = _S275 / width_1;
            int3 _S279 = int3(_S277, int(_S278), int(0));
            float _S280 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S279)).xy), uint(((_S279)).z))).x;
            uint _S281 = address_0.z;
            uint _S282 = _S281 % width_1;
            int _S283 = int(_S282);
            uint _S284 = _S281 / width_1;
            int3 _S285 = int3(_S283, int(_S284), int(0));
            unlitColor_0 = float3(_S274, _S280, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S285)).xy), uint(((_S285)).z))).x);
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
    if(_S186)
    {
        detailSource_0 = _S180;
    }
    else
    {
        detailSource_0 = _S172;
    }
    float2 tangent_1;
    if(_S186)
    {
        tangent_1 = _S182;
    }
    else
    {
        tangent_1 = _S174;
    }
    thread float3 _S286 = unlitColor_0;
    float2 _S287 = float2(0.0f, 0.0f);
    if(any(tangent_1 != _S287))
    {
        float _S288 = tangent_1.x;
        float _S289 = tangent_1.y;
        float2 tangent_2 = normalize(float2(_S289, - _S288) / float2(max(abs(_S288), abs(_S289))) );
        float2 bitangent_0 = float2(- tangent_2.y, tangent_2.x);
        if((dot(detailSource_0, tangent_2)) < 0.0f)
        {
            detailSource_0 = - tangent_2;
        }
        else
        {
            detailSource_0 = tangent_2;
        }
        _S286.xy = detailSource_0 * float2(_S286.x)  + bitangent_0 * float2(_S286.y) ;
    }
    float3 _S290 = _S286;
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
        float2 _S291 = _S263.xy;
        float _S292 = dot(_S291, detailSource_0) + _S263.z;
        float4 _S293 = float4(_S8->detailUvTransformRow1_0) ;
        float2 _S294 = _S293.xy;
        float _S295 = dot(_S294, detailSource_0) + _S293.z;
        float2 detailUv_0 = float2(_S292, _S295);
        thread float4 detailSample_0;
        bool _S296 = (udimMask_0 & 4096U) != 0U;
        if((maskU_0 & 4096U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_0 & 4096U) != 0U)
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
            if(!_S296)
            {
                for(;;)
                {
                    if(_S297 == 0U)
                    {
                        detailSource_0 = detailUv_0;
                        break;
                    }
                    texture2d<float, access::sample> _S298 = (&kernelContext_0)->detailNormalTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S298).get_width(0)),(*((&height_2)) = (_S298).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S299 = float2(0.5f) ;
                    float2 remapped_1 = clamp((detailUv_0 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S299 / size_1, (size_1 - _S299) / size_1);
                    if((_S297 & 1U) != 0U)
                    {
                        lightType_0 = remapped_1.x;
                    }
                    else
                    {
                        lightType_0 = _S292;
                    }
                    if((_S297 & 2U) != 0U)
                    {
                        _S4 = remapped_1.y;
                    }
                    else
                    {
                        _S4 = _S295;
                    }
                    detailSource_0 = float2(lightType_0, _S4);
                    break;
                }
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S300 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_1;
            thread uint atlasHeight_1;
            (*((&atlasWidth_1)) = (_S300).get_width(0)),(*((&atlasHeight_1)) = (_S300).get_height(0));
            int3 _S301 = int3(int(0), int(0), int(0));
            float4 metadata_1 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S301)).xy), uint(((_S301)).z))) * float4(255.0f) );
            if((metadata_1.x) == 255.0f)
            {
                uint _S302 = atlasWidth_1;
                uint _S303 = 2U % atlasWidth_1;
                int _S304 = int(_S303);
                uint _S305 = 2U / _S302;
                int3 _S306 = int3(_S304, int(_S305), int(0));
                int _S307 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S306)).xy), uint(((_S306)).z))).x * 255.0f)));
                uint _S308 = atlasWidth_1;
                uint _S309 = 3U % atlasWidth_1;
                int _S310 = int(_S309);
                uint _S311 = 3U / _S308;
                int3 _S312 = int3(_S310, int(_S311), int(0));
                int2 minimumTile_1 = int2(_S307, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S312)).xy), uint(((_S312)).z))).x * 255.0f))));
                uint _S313 = atlasWidth_1;
                uint _S314 = 4U % atlasWidth_1;
                int _S315 = int(_S314);
                uint _S316 = 4U / _S313;
                int3 _S317 = int3(_S315, int(_S316), int(0));
                uint _S318 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S317)).xy), uint(((_S317)).z))).x * 255.0f));
                uint _S319 = atlasWidth_1;
                uint _S320 = 5U % atlasWidth_1;
                int _S321 = int(_S320);
                uint _S322 = 5U / _S319;
                int3 _S323 = int3(_S321, int(_S322), int(0));
                uint _S324 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S323)).xy), uint(((_S323)).z))).x * 255.0f));
                uint2 grid_1 = uint2(_S318, _S324);
                int2 tile_2 = int2(floor(detailUv_0)) - minimumTile_1;
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
                    int3 _S325 = int3(int(1), int(0), int(0));
                    _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S325)).xy), uint(((_S325)).z)));
                    break;
                }
                uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S318 + uint(tile_2.x));
                uint _S326 = atlasWidth_1;
                uint _S327 = offset_1 % atlasWidth_1;
                int _S328 = int(_S327);
                uint _S329 = offset_1 / _S326;
                int3 _S330 = int3(_S328, int(_S329), int(0));
                uint _S331 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S330)).xy), uint(((_S330)).z))).x * 255.0f));
                uint _S332 = atlasWidth_1;
                uint _S333 = offset_1 + 1U;
                uint _S334 = _S333 % atlasWidth_1;
                int _S335 = int(_S334);
                uint _S336 = _S333 / _S332;
                int3 _S337 = int3(_S335, int(_S336), int(0));
                uint slot_2 = _S331 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S337)).xy), uint(((_S337)).z))).x * 255.0f)) << 8U);
                if(slot_2 == 0U)
                {
                    int3 _S338 = int3(int(1), int(0), int(0));
                    _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S338)).xy), uint(((_S338)).z)));
                    break;
                }
                uint slot_3 = slot_2 - 1U;
                uint _S339 = atlasWidth_1;
                uint _S340 = 6U % atlasWidth_1;
                int _S341 = int(_S340);
                uint _S342 = 6U / _S339;
                int3 _S343 = int3(_S341, int(_S342), int(0));
                uint _S344 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S343)).xy), uint(((_S343)).z))).x * 255.0f));
                uint _S345 = atlasWidth_1;
                uint _S346 = 7U % atlasWidth_1;
                int _S347 = int(_S346);
                uint _S348 = 7U / _S345;
                int3 _S349 = int3(_S347, int(_S348), int(0));
                uint columns_1 = _S344 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S349)).xy), uint(((_S349)).z))).x * 255.0f)) << 8U);
                uint _S350 = atlasWidth_1;
                uint _S351 = 8U % atlasWidth_1;
                int _S352 = int(_S351);
                uint _S353 = 8U / _S350;
                int3 _S354 = int3(_S352, int(_S353), int(0));
                uint _S355 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S354)).xy), uint(((_S354)).z))).x * 255.0f));
                uint _S356 = atlasWidth_1;
                uint _S357 = 9U % atlasWidth_1;
                int _S358 = int(_S357);
                uint _S359 = 9U / _S356;
                int3 _S360 = int3(_S358, int(_S359), int(0));
                uint rows_1 = ((_S355 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S360)).xy), uint(((_S360)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                uint metadataRows_1 = (10U + 2U * _S318 * _S324 + atlasWidth_1 - 1U) / atlasWidth_1;
                uint _S361 = atlasWidth_1 / columns_1;
                float _S362 = float(_S361);
                uint _S363 = (atlasHeight_1 - metadataRows_1) / rows_1;
                float2 cellSize_2 = float2(_S362, float(_S363));
                uint _S364 = slot_3 % columns_1;
                float _S365 = float(_S364);
                uint _S366 = slot_3 / columns_1;
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S365, float(_S366)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailUv_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            int2 _S367 = int2(metadata_1.zw);
            int2 tile_3 = int2(floor(detailUv_0)) - int2(metadata_1.xy);
            if(any(tile_3 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_3 >= _S367);
            }
            if(hasSceneLighting_0)
            {
                int3 _S368 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S368)).xy), uint(((_S368)).z)));
                break;
            }
            uint _S369 = atlasWidth_1 / uint(_S367.x);
            float _S370 = float(_S369);
            uint _S371 = (atlasHeight_1 - 1U) / uint(_S367.y);
            float2 cellSize_3 = float2(_S370, float(_S371));
            _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
            break;
        }
        detailSample_0 = _S5;
        if((_S293.w) != 0.0f)
        {
            float4 _S372 = detailSample_0;
            for(;;)
            {
                float _S373 = _S372.w;
                if(_S373 > 1.0f)
                {
                    unlitColor_0 = _S372.xyz;
                    break;
                }
                texture2d<float, access::sample> _S374 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_3;
                thread uint height_3;
                (*((&width_3)) = (_S374).get_width(0)),(*((&height_3)) = (_S374).get_height(0));
                uint3 _S375 = uint3(round(_S372.xyz * float3(255.0f) ));
                uint index_2 = ((_S375.x) | ((_S375.y) << 8U)) | ((_S375.z) << 16U);
                if(_S373 == 0.0f)
                {
                    _S264 = _S375;
                }
                else
                {
                    _S264 = uint3(index_2, index_2, index_2);
                }
                uint3 address_1 = _S264 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S376 = address_1.x;
                uint _S377 = _S376 % width_3;
                int _S378 = int(_S377);
                uint _S379 = _S376 / width_3;
                int3 _S380 = int3(_S378, int(_S379), int(0));
                float _S381 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S380)).xy), uint(((_S380)).z))).x;
                uint _S382 = address_1.y;
                uint _S383 = _S382 % width_3;
                int _S384 = int(_S383);
                uint _S385 = _S382 / width_3;
                int3 _S386 = int3(_S384, int(_S385), int(0));
                float _S387 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S386)).xy), uint(((_S386)).z))).x;
                uint _S388 = address_1.z;
                uint _S389 = _S388 % width_3;
                int _S390 = int(_S389);
                uint _S391 = _S388 / width_3;
                int3 _S392 = int3(_S390, int(_S391), int(0));
                unlitColor_0 = float3(_S381, _S387, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S392)).xy), uint(((_S392)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S393 = detail_0;
        if(any(_S294 != _S287))
        {
            float _S394 = _S294.x;
            float _S395 = _S294.y;
            float2 tangent_3 = normalize(float2(_S395, - _S394) / float2(max(abs(_S394), abs(_S395))) );
            float2 bitangent_1 = float2(- tangent_3.y, tangent_3.x);
            if((dot(_S291, tangent_3)) < 0.0f)
            {
                tangent_1 = - tangent_3;
            }
            else
            {
                tangent_1 = tangent_3;
            }
            _S393.xy = tangent_1 * float2(_S393.x)  + bitangent_1 * float2(_S393.y) ;
        }
        float3 _S396 = _S393;
        for(;;)
        {
            float3 t_0 = normalize(_S290) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S396) * float3(-1.0f, -1.0f, 1.0f);
            float _S397 = t_0.z;
            if(_S397 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S397))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S290;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S398 = float3(unlitColor_0.x) ;
    float3 _S399 = float3(unlitColor_0.y) ;
    float3 _S400 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S398 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S399 + _S1.normal_0 * _S400);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S398 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S399 + worldNormalBasis_0 * _S400);
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
    float _S401 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S402 = max(0.00100000004749745f, roughness_0);
    float _S403 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S404 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S404;
    bool omniPbrSpecular_0 = (_S168.w) >= 0.5f;
    float dielectricLevel_0 = _S161.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S405 = float3(dielectricLevel_0) ;
        float3 _S406 = float3(metallic_0) ;
        float3 _S407 = mix(_S405, diffuseColor_0, _S406);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S405, diffuseColor_0, _S406);
        grazingIncidence_0 = _S407;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S166.w) >= 0.5f)
        {
            float3 _S408 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S408;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S409 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S409);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S409);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S410 = _S2;
    float _S411 = _S2.ambientLight_0.w;
    if(_S411 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S412 = _S410.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S412, _S412)) > 0.0f;
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
    uint _S413 = min(uint(_S411), 128U);
    float3 sceneEye_0 = normalize((((float4(unlitColor_0, 0.0f)) * (_S2.eyeToWorld_0))).xyz);
    float3 worldPosition_0 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S2.eyeToWorld_0))).xyz;
    float3 worldGeometricNormal_0 = cross(dfdx(worldPosition_0), dfdy(worldPosition_0));
    float worldNormalLengthSquared_0 = dot(worldGeometricNormal_0, worldGeometricNormal_0);
    float3 diffuseFresnel_0;
    if(worldNormalLengthSquared_0 > 9.99999968265522539e-21f)
    {
        diffuseFresnel_0 = worldGeometricNormal_0 * float3(rsqrt(worldNormalLengthSquared_0)) ;
    }
    else
    {
        diffuseFresnel_0 = float3(0.0f, 0.0f, 1.0f);
    }
    float4 _S414 = float4(_S8->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S414.w) ;
    uint domeLinkMask_0 = uint(max(_S170.x, 0.0f));
    FragmentFrameParameters_0 _S415 = _S2;
    uint _S416 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S416) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S417 = _S410.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S418 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S418;
        for(;;)
        {
            if(lightCount_0 < _S416)
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
        domeAmbient_1 = _S417;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S419 = !hasSceneLighting_0;
    if(_S419)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S413;
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
        bool _S420;
        if(hasSceneLighting_0)
        {
            _S420 = (((uint4(_S8->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S420 = false;
        }
        if(_S420)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S421 = lightIndex_0 == 0U;
        bool _S422;
        if(_S421)
        {
            _S422 = _S419;
        }
        else
        {
            _S422 = false;
        }
        if(_S422)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S423 = 224U + lightIndex_0 * 16U;
            float _S424 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423)>>2]);
            float _S425 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423 + 4U)>>2]);
            float _S426 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423 + 8U)>>2]);
            float _S427 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423 + 12U)>>2]);
            lightType_0 = _S427;
        }
        bool _S428;
        if(_S421)
        {
            _S428 = _S419;
        }
        else
        {
            _S428 = false;
        }
        if(_S428)
        {
            lightDirection_0 = normalize((float4(_S8->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S429 = 2272U + lightIndex_0 * 16U;
            float _S430 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S429)>>2]);
            float _S431 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S429 + 4U)>>2]);
            float _S432 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S429 + 8U)>>2]);
            float _S433 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S429 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S430, _S431, _S432));
        }
        bool _S434;
        if(_S421)
        {
            _S434 = _S419;
        }
        else
        {
            _S434 = false;
        }
        if(_S434)
        {
            _S4 = (float4(_S8->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S435 = 4320U + lightIndex_0 * 16U;
            float _S436 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435)>>2]);
            float _S437 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435 + 4U)>>2]);
            float _S438 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435 + 8U)>>2]);
            float _S439 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435 + 12U)>>2]);
            _S4 = _S439;
        }
        bool _S440;
        if(_S421)
        {
            _S440 = _S419;
        }
        else
        {
            _S440 = false;
        }
        if(_S440)
        {
            diffuseColor_0 = _S414.xyz;
        }
        else
        {
            uint _S441 = 4320U + lightIndex_0 * 16U;
            float _S442 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S441)>>2]);
            float _S443 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S441 + 4U)>>2]);
            float _S444 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S441 + 8U)>>2]);
            float _S445 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S441 + 12U)>>2]);
            diffuseColor_0 = float3(_S442, _S443, _S444);
        }
        bool _S446;
        if(_S421)
        {
            _S446 = _S419;
        }
        else
        {
            _S446 = false;
        }
        float _S447;
        if(_S446)
        {
            _S447 = 1.0f;
        }
        else
        {
            uint _S448 = 6368U + lightIndex_0 * 16U;
            float _S449 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S448)>>2]);
            float _S450 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S448 + 4U)>>2]);
            float _S451 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S448 + 8U)>>2]);
            float _S452 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S448 + 12U)>>2]);
            _S447 = _S449;
        }
        bool _S453;
        if(_S421)
        {
            _S453 = _S419;
        }
        else
        {
            _S453 = false;
        }
        float _S454;
        if(_S453)
        {
            _S454 = 1.0f;
        }
        else
        {
            uint _S455 = 6368U + lightIndex_0 * 16U;
            float _S456 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S455)>>2]);
            float _S457 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S455 + 4U)>>2]);
            float _S458 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S455 + 8U)>>2]);
            float _S459 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S455 + 12U)>>2]);
            _S454 = _S457;
        }
        bool _S460;
        if(_S421)
        {
            _S460 = _S419;
        }
        else
        {
            _S460 = false;
        }
        if(_S460)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S461 = 8416U + lightIndex_0 * 16U;
            float _S462 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461)>>2]);
            float _S463 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461 + 4U)>>2]);
            float _S464 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461 + 8U)>>2]);
            float _S465 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S462, _S463, _S464));
        }
        bool _S466;
        if(_S421)
        {
            _S466 = _S419;
        }
        else
        {
            _S466 = false;
        }
        if(_S466)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S467 = 10464U + lightIndex_0 * 16U;
            float _S468 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467)>>2]);
            float _S469 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 4U)>>2]);
            float _S470 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 8U)>>2]);
            float _S471 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S468, _S469, _S470));
        }
        bool _S472;
        if(_S421)
        {
            _S472 = _S419;
        }
        else
        {
            _S472 = false;
        }
        float shapeX_0;
        if(_S472)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S473 = 8416U + lightIndex_0 * 16U;
            float _S474 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473)>>2]);
            float _S475 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 4U)>>2]);
            float _S476 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 8U)>>2]);
            float _S477 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 12U)>>2]);
            shapeX_0 = _S477;
        }
        bool _S478;
        if(_S421)
        {
            _S478 = _S419;
        }
        else
        {
            _S478 = false;
        }
        float shapeY_0;
        if(_S478)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S479 = 10464U + lightIndex_0 * 16U;
            float _S480 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S479)>>2]);
            float _S481 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S479 + 4U)>>2]);
            float _S482 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S479 + 8U)>>2]);
            float _S483 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S479 + 12U)>>2]);
            shapeY_0 = _S483;
        }
        bool _S484;
        if(_S421)
        {
            _S484 = _S419;
        }
        else
        {
            _S484 = false;
        }
        float lightRadius_0;
        if(_S484)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S485 = 2272U + lightIndex_0 * 16U;
            float _S486 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S485)>>2]);
            float _S487 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S485 + 4U)>>2]);
            float _S488 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S485 + 8U)>>2]);
            float _S489 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S485 + 12U)>>2]);
            lightRadius_0 = _S489;
        }
        bool _S490;
        if(_S421)
        {
            _S490 = _S419;
        }
        else
        {
            _S490 = false;
        }
        if(_S490)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S491;
        if(_S421)
        {
            _S491 = _S419;
        }
        else
        {
            _S491 = false;
        }
        if(_S491)
        {
            _S5 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S492 = 15296U + lightIndex_0 * 16U;
            float _S493 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S492)>>2]);
            float _S494 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S492 + 4U)>>2]);
            float _S495 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S492 + 8U)>>2]);
            float _S496 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S492 + 12U)>>2]);
            _S5 = float4(_S493, _S494, _S495, _S496);
        }
        float shadowVisibility_0;
        float sampleCount_0;
        if(hasSceneLighting_0)
        {
            uint _S497 = 12960U + lightIndex_0 * 16U;
            float _S498 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S497)>>2]);
            float _S499 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S497 + 4U)>>2]);
            float _S500 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S497 + 8U)>>2]);
            float _S501 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S497 + 12U)>>2]);
            int shadowSlot_0 = int(_S498);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S502 = _S2;
                    FragmentFrameParameters_0 _S503 = _S2;
                    if((dot(diffuseFresnel_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - diffuseFresnel_0;
                    }
                    else
                    {
                        color_0 = diffuseFresnel_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S502.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S504 = lightClip_0.w;
                    if(_S504 <= 0.0f)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S504) ;
                    bool _S505;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S505 = true;
                    }
                    else
                    {
                        _S505 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S506;
                    if(_S505)
                    {
                        _S506 = true;
                    }
                    else
                    {
                        _S506 = (ndc_0.z) < 0.0f;
                    }
                    bool _S507;
                    if(_S506)
                    {
                        _S507 = true;
                    }
                    else
                    {
                        _S507 = (ndc_0.z) > 1.0f;
                    }
                    if(_S507)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float2 _S508 = _S503.shadowTile_0[shadowSlot_0].xy;
                    float2 _S509 = _S503.shadowTile_0[shadowSlot_0].zw;
                    float2 _S510 = _S508 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S509;
                    float texel_0 = _S502.shadowControls_0[shadowSlot_0].w;
                    float _S511 = max(_S502.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S512 = ndc_0.z - _S502.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S513 = float2((texel_0 * 0.5f)) ;
                    float2 _S514 = _S508 + _S513;
                    float2 _S515 = _S508 + _S509 - _S513;
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
                            if(_S512 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S510 + float2(float(x_0), float(y_0)) * float2((_S511 * texel_0)) , _S514, _S515)), level((0.0f))).x))
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
        float3 _S516 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S516;
        sampleOffsets_0[int(1)] = _S516;
        sampleOffsets_0[int(2)] = _S516;
        sampleOffsets_0[int(3)] = _S516;
        sampleOffsets_0[int(4)] = _S516;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S517 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S517 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S517 - halfHeight_0;
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
            float sampleIntensity_0 = _S4 / sampleCount_0;
            float3 sampleDirection_0;
            float sampleIntensity_1;
            float emissionScale_0;
            if(lightType_0 >= 2.0f)
            {
                uint _S518 = 224U + lightIndex_0 * 16U;
                float _S519 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S518)>>2]);
                float _S520 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S518 + 4U)>>2]);
                float _S521 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S518 + 8U)>>2]);
                float _S522 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S518 + 12U)>>2]);
                float3 toLight_0 = float3(_S519, _S520, _S521) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S523 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S523)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S523;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S5.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S5.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S524 = _S5.y;
                        if(_S524 > 0.0f)
                        {
                            emissionScale_0 = pow(max(emitterCosine_0, 0.0f), _S524);
                        }
                        else
                        {
                            emissionScale_0 = 1.0f;
                        }
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
            float _S525 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S525)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S525) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S526 = float3(_S525) ;
            float _S527 = max(_S402, 0.00100000004749745f);
            float alpha_0 = _S527 * _S527;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S528 = 1.0f - k_0;
            float3 _S529 = float3(max(4.0f * normalDotLight_0 * _S401, 1.00000000317107685e-30f)) ;
            float3 _S530 = mix(normalIncidence_0, grazingIncidence_0, _S526) * float3((_S401 / (_S401 * _S528 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S528 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S529;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S531 = max(_S403, 0.00100000004749745f);
                float alpha_1 = _S531 * _S531;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S532 = 1.0f - k_1;
                directSpecular_0 = _S530 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S526) * float3((_S401 / (_S401 * _S532 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S532 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S529);
            }
            else
            {
                directSpecular_0 = _S530;
            }
            float3 _S533 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S447)  * (_S533 * _S404) + directSpecular_0 * float3(_S454)  * _S533);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S534 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S535 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S536 = saturate(_S402);
        float2 _S537 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S535, _S536)), level((0.0f)))).xy;
        float _S538 = _S537.x;
        float _S539 = _S537.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S538)  + grazingIncidence_0 * float3(_S539) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S538 + _S539)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S540 = max(_S415.domeControls_0.y, 1.0f);
        float3 _S541 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S540 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S415.domeControls_0.z;
            float _S542 = _S415.domeControls_0.w;
            for(;;)
            {
                bool _S543 = _S540 <= 1.0f;
                _S6 = _S543;
                if(_S543)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S542, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S540)), level((0.0f)))).xyz;
                break;
            }
            float _S544 = _S534.environmentControls_0.y;
            float _S545 = _S534.environmentControls_0.z;
            for(;;)
            {
                if(_S6)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S546 = max(_S544, 1.0f);
                    float _S547 = _S546 - 1.0f;
                    float slice_0 = _S536 * max(_S547, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S545, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S546)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S547) + v_0) / _S546)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S548 = max(_S544, 1.0f);
                float total_0 = _S548 * _S540;
                float _S549 = _S548 - 1.0f;
                float slice_1 = _S536 * max(_S549, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S545, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S548;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S549) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S6)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_3 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S550 = max(_S544, 1.0f);
                        float _S551 = _S550 - 1.0f;
                        float slice_2 = saturate(_S403) * max(_S551, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S545, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S550)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S551) + v_2) / _S550)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S552 = max(_S544, 1.0f);
                    float total_1 = _S552 * _S540;
                    float _S553 = _S552 - 1.0f;
                    float slice_3 = saturate(_S403) * max(_S553, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S545, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S552;
                    normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S553) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightBitangent_0 = normal_2;
            }
            else
            {
                lightBitangent_0 = _S541;
            }
            lightDirection_0 = diffuseColor_0;
            lightTangent_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightDirection_0 = _S541;
            lightTangent_0 = _S541;
            lightBitangent_0 = _S541;
            for(;;)
            {
                if(sampleIndex_0 < _S416)
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
                float _S554 = _S415.domeControls_0.w;
                for(;;)
                {
                    bool _S555 = _S540 <= 1.0f;
                    _S7 = _S555;
                    if(_S555)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S554, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S540)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightDirection_0 + diffuseColor_0;
                float _S556 = _S534.environmentControls_0.y;
                float _S557 = _S534.environmentControls_0.z;
                for(;;)
                {
                    if(_S7)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S558 = max(_S556, 1.0f);
                        float _S559 = _S558 - 1.0f;
                        float slice_4 = _S536 * max(_S559, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S557, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S558)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S559) + v_4) / _S558)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S560 = max(_S556, 1.0f);
                    float total_2 = _S560 * _S540;
                    float _S561 = _S560 - 1.0f;
                    float slice_5 = _S536 * max(_S561, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S557, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S560;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S561) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightTangent_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S7)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_7 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S562 = max(_S556, 1.0f);
                            float _S563 = _S562 - 1.0f;
                            float slice_6 = saturate(_S403) * max(_S563, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S557, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S562)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S563) + v_6) / _S562)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S564 = max(_S556, 1.0f);
                        float total_3 = _S564 * _S540;
                        float _S565 = _S564 - 1.0f;
                        float slice_7 = saturate(_S403) * max(_S565, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S557, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S564;
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S565) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    color_0 = lightBitangent_0 + normal_2;
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
        float3 _S566 = float3(occlusion_0) ;
        float3 color_4 = color_3 + _S566 * diffuse_1 * lightDirection_0 * diffuseWeight_0 + _S566 * lightTangent_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S567 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S535, saturate(_S403))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightBitangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S567.x)  + float3(_S567.y) );
        }
        else
        {
            color_3 = color_4;
        }
    }
    float3 color_5 = (color_3 + emissiveColor_0) * float3(exp2((as_type<float>((_S3.z))))) ;
    if((_S3.y) == 1U)
    {
        color_3 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_3 = color_5;
    }
    pixelOutput_0 _S568 = { float4(color_3, opacity_0) };
    return _S568;
}
