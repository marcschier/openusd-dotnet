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
    float4 _S169 = float4(_S8->uvTransformRow0_0) ;
    float2 _S170 = _S169.xy;
    float _S171 = _S169.z;
    float4 _S172 = float4(_S8->uvTransformRow1_0) ;
    float2 _S173 = _S172.xy;
    float _S174 = _S172.z;
    float2 _S175 = float2(dot(_S170, _S1.texCoord_0) + _S171, dot(_S173, _S1.texCoord_0) + _S174);
    uint secondaryMask_0 = uint(round(_S172.w));
    float2 _S176 = float2(dot(_S170, _S1.texCoord1_0) + _S171, dot(_S173, _S1.texCoord1_0) + _S174);
    thread float4 baseNormalSample_0;
    float2 detailSource_0;
    if((secondaryMask_0 & 4U) != 0U)
    {
        detailSource_0 = _S176;
    }
    else
    {
        detailSource_0 = _S175;
    }
    bool _S177 = (udimMask_0 & 4U) != 0U;
    float4 _S178 = float4(_S8->domeLinkControls_0) ;
    uint maskU_0 = uint(round(_S178.y));
    uint maskV_0 = uint(round(_S178.z));
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
    uint _S179 = index_0 | lightCount_0;
    for(;;)
    {
        if(!_S177)
        {
            for(;;)
            {
                if(_S179 == 0U)
                {
                    break;
                }
                texture2d<float, access::sample> _S180 = (&kernelContext_0)->normalTexture_0;
                thread uint width_0;
                thread uint height_0;
                (*((&width_0)) = (_S180).get_width(0)),(*((&height_0)) = (_S180).get_height(0));
                float2 size_0 = float2(float(width_0), float(height_0));
                float2 _S181 = float2(0.5f) ;
                float2 remapped_0 = clamp((detailSource_0 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S181 / size_0, (size_0 - _S181) / size_0);
                if((_S179 & 1U) != 0U)
                {
                    lightType_0 = remapped_0.x;
                }
                else
                {
                    lightType_0 = detailSource_0.x;
                }
                if((_S179 & 2U) != 0U)
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
        texture2d<float, access::sample> _S182 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_0;
        thread uint atlasHeight_0;
        (*((&atlasWidth_0)) = (_S182).get_width(0)),(*((&atlasHeight_0)) = (_S182).get_height(0));
        int3 _S183 = int3(int(0), int(0), int(0));
        float4 metadata_0 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S183)).xy), uint(((_S183)).z))) * float4(255.0f) );
        if((metadata_0.x) == 255.0f)
        {
            uint _S184 = atlasWidth_0;
            uint _S185 = 2U % atlasWidth_0;
            int _S186 = int(_S185);
            uint _S187 = 2U / _S184;
            int3 _S188 = int3(_S186, int(_S187), int(0));
            int _S189 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S188)).xy), uint(((_S188)).z))).x * 255.0f)));
            uint _S190 = atlasWidth_0;
            uint _S191 = 3U % atlasWidth_0;
            int _S192 = int(_S191);
            uint _S193 = 3U / _S190;
            int3 _S194 = int3(_S192, int(_S193), int(0));
            int2 minimumTile_0 = int2(_S189, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S194)).xy), uint(((_S194)).z))).x * 255.0f))));
            uint _S195 = atlasWidth_0;
            uint _S196 = 4U % atlasWidth_0;
            int _S197 = int(_S196);
            uint _S198 = 4U / _S195;
            int3 _S199 = int3(_S197, int(_S198), int(0));
            uint _S200 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S199)).xy), uint(((_S199)).z))).x * 255.0f));
            uint _S201 = atlasWidth_0;
            uint _S202 = 5U % atlasWidth_0;
            int _S203 = int(_S202);
            uint _S204 = 5U / _S201;
            int3 _S205 = int3(_S203, int(_S204), int(0));
            uint _S206 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S205)).xy), uint(((_S205)).z))).x * 255.0f));
            uint2 grid_0 = uint2(_S200, _S206);
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
                int3 _S207 = int3(int(1), int(0), int(0));
                _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S207)).xy), uint(((_S207)).z)));
                break;
            }
            uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S200 + uint(tile_0.x));
            uint _S208 = atlasWidth_0;
            uint _S209 = offset_0 % atlasWidth_0;
            int _S210 = int(_S209);
            uint _S211 = offset_0 / _S208;
            int3 _S212 = int3(_S210, int(_S211), int(0));
            uint _S213 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S212)).xy), uint(((_S212)).z))).x * 255.0f));
            uint _S214 = atlasWidth_0;
            uint _S215 = offset_0 + 1U;
            uint _S216 = _S215 % atlasWidth_0;
            int _S217 = int(_S216);
            uint _S218 = _S215 / _S214;
            int3 _S219 = int3(_S217, int(_S218), int(0));
            uint slot_0 = _S213 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S219)).xy), uint(((_S219)).z))).x * 255.0f)) << 8U);
            if(slot_0 == 0U)
            {
                int3 _S220 = int3(int(1), int(0), int(0));
                _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S220)).xy), uint(((_S220)).z)));
                break;
            }
            uint slot_1 = slot_0 - 1U;
            uint _S221 = atlasWidth_0;
            uint _S222 = 6U % atlasWidth_0;
            int _S223 = int(_S222);
            uint _S224 = 6U / _S221;
            int3 _S225 = int3(_S223, int(_S224), int(0));
            uint _S226 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S225)).xy), uint(((_S225)).z))).x * 255.0f));
            uint _S227 = atlasWidth_0;
            uint _S228 = 7U % atlasWidth_0;
            int _S229 = int(_S228);
            uint _S230 = 7U / _S227;
            int3 _S231 = int3(_S229, int(_S230), int(0));
            uint columns_0 = _S226 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S231)).xy), uint(((_S231)).z))).x * 255.0f)) << 8U);
            uint _S232 = atlasWidth_0;
            uint _S233 = 8U % atlasWidth_0;
            int _S234 = int(_S233);
            uint _S235 = 8U / _S232;
            int3 _S236 = int3(_S234, int(_S235), int(0));
            uint _S237 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S236)).xy), uint(((_S236)).z))).x * 255.0f));
            uint _S238 = atlasWidth_0;
            uint _S239 = 9U % atlasWidth_0;
            int _S240 = int(_S239);
            uint _S241 = 9U / _S238;
            int3 _S242 = int3(_S240, int(_S241), int(0));
            uint rows_0 = ((_S237 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S242)).xy), uint(((_S242)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
            uint metadataRows_0 = (10U + 2U * _S200 * _S206 + atlasWidth_0 - 1U) / atlasWidth_0;
            uint _S243 = atlasWidth_0 / columns_0;
            float _S244 = float(_S243);
            uint _S245 = (atlasHeight_0 - metadataRows_0) / rows_0;
            float2 cellSize_0 = float2(_S244, float(_S245));
            uint _S246 = slot_1 % columns_0;
            float _S247 = float(_S246);
            uint _S248 = slot_1 / columns_0;
            _S5 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S247, float(_S248)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(detailSource_0) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        int2 _S249 = int2(metadata_0.zw);
        int2 tile_1 = int2(floor(detailSource_0)) - int2(metadata_0.xy);
        if(any(tile_1 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_1 >= _S249);
        }
        if(hasSceneLighting_0)
        {
            int3 _S250 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
            _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S250)).xy), uint(((_S250)).z)));
            break;
        }
        uint _S251 = atlasWidth_0 / uint(_S249.x);
        float _S252 = float(_S251);
        uint _S253 = (atlasHeight_0 - 1U) / uint(_S249.y);
        float2 cellSize_1 = float2(_S252, float(_S253));
        _S5 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
        break;
    }
    baseNormalSample_0 = _S5;
    float4 _S254 = float4(_S8->detailUvTransformRow0_0) ;
    uint3 _S255;
    if((_S254.w) != 0.0f)
    {
        float4 _S256 = baseNormalSample_0;
        for(;;)
        {
            float _S257 = _S256.w;
            if(_S257 > 1.0f)
            {
                unlitColor_0 = _S256.xyz;
                break;
            }
            texture2d<float, access::sample> _S258 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_1;
            thread uint height_1;
            (*((&width_1)) = (_S258).get_width(0)),(*((&height_1)) = (_S258).get_height(0));
            uint3 _S259 = uint3(round(_S256.xyz * float3(255.0f) ));
            uint index_1 = ((_S259.x) | ((_S259.y) << 8U)) | ((_S259.z) << 16U);
            if(_S257 == 0.0f)
            {
                _S255 = _S259;
            }
            else
            {
                _S255 = uint3(index_1, index_1, index_1);
            }
            uint3 address_0 = _S255 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S260 = address_0.x;
            uint _S261 = _S260 % width_1;
            int _S262 = int(_S261);
            uint _S263 = _S260 / width_1;
            int3 _S264 = int3(_S262, int(_S263), int(0));
            float _S265 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S264)).xy), uint(((_S264)).z))).x;
            uint _S266 = address_0.y;
            uint _S267 = _S266 % width_1;
            int _S268 = int(_S267);
            uint _S269 = _S266 / width_1;
            int3 _S270 = int3(_S268, int(_S269), int(0));
            float _S271 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S270)).xy), uint(((_S270)).z))).x;
            uint _S272 = address_0.z;
            uint _S273 = _S272 % width_1;
            int _S274 = int(_S273);
            uint _S275 = _S272 / width_1;
            int3 _S276 = int3(_S274, int(_S275), int(0));
            unlitColor_0 = float3(_S265, _S271, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S276)).xy), uint(((_S276)).z))).x);
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
    thread float3 _S277 = unlitColor_0;
    float2 _S278 = float2(0.0f, 0.0f);
    if(any(_S173 != _S278))
    {
        float _S279 = _S173.x;
        float _S280 = _S173.y;
        float2 tangent_1 = normalize(float2(_S280, - _S279) / float2(max(abs(_S279), abs(_S280))) );
        float2 bitangent_0 = float2(- tangent_1.y, tangent_1.x);
        if((dot(_S170, tangent_1)) < 0.0f)
        {
            detailSource_0 = - tangent_1;
        }
        else
        {
            detailSource_0 = tangent_1;
        }
        _S277.xy = detailSource_0 * float2(_S277.x)  + bitangent_0 * float2(_S277.y) ;
    }
    float3 _S281 = _S277;
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
        float2 _S282 = _S254.xy;
        float _S283 = dot(_S282, detailSource_0) + _S254.z;
        float4 _S284 = float4(_S8->detailUvTransformRow1_0) ;
        float2 _S285 = _S284.xy;
        float _S286 = dot(_S285, detailSource_0) + _S284.z;
        float2 detailUv_0 = float2(_S283, _S286);
        thread float4 detailSample_0;
        bool _S287 = (udimMask_0 & 4096U) != 0U;
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
        uint _S288 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S287)
            {
                for(;;)
                {
                    if(_S288 == 0U)
                    {
                        detailSource_0 = detailUv_0;
                        break;
                    }
                    texture2d<float, access::sample> _S289 = (&kernelContext_0)->detailNormalTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S289).get_width(0)),(*((&height_2)) = (_S289).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S290 = float2(0.5f) ;
                    float2 remapped_1 = clamp((detailUv_0 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S290 / size_1, (size_1 - _S290) / size_1);
                    if((_S288 & 1U) != 0U)
                    {
                        lightType_0 = remapped_1.x;
                    }
                    else
                    {
                        lightType_0 = _S283;
                    }
                    if((_S288 & 2U) != 0U)
                    {
                        _S4 = remapped_1.y;
                    }
                    else
                    {
                        _S4 = _S286;
                    }
                    detailSource_0 = float2(lightType_0, _S4);
                    break;
                }
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S291 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_1;
            thread uint atlasHeight_1;
            (*((&atlasWidth_1)) = (_S291).get_width(0)),(*((&atlasHeight_1)) = (_S291).get_height(0));
            int3 _S292 = int3(int(0), int(0), int(0));
            float4 metadata_1 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S292)).xy), uint(((_S292)).z))) * float4(255.0f) );
            if((metadata_1.x) == 255.0f)
            {
                uint _S293 = atlasWidth_1;
                uint _S294 = 2U % atlasWidth_1;
                int _S295 = int(_S294);
                uint _S296 = 2U / _S293;
                int3 _S297 = int3(_S295, int(_S296), int(0));
                int _S298 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S297)).xy), uint(((_S297)).z))).x * 255.0f)));
                uint _S299 = atlasWidth_1;
                uint _S300 = 3U % atlasWidth_1;
                int _S301 = int(_S300);
                uint _S302 = 3U / _S299;
                int3 _S303 = int3(_S301, int(_S302), int(0));
                int2 minimumTile_1 = int2(_S298, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S303)).xy), uint(((_S303)).z))).x * 255.0f))));
                uint _S304 = atlasWidth_1;
                uint _S305 = 4U % atlasWidth_1;
                int _S306 = int(_S305);
                uint _S307 = 4U / _S304;
                int3 _S308 = int3(_S306, int(_S307), int(0));
                uint _S309 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S308)).xy), uint(((_S308)).z))).x * 255.0f));
                uint _S310 = atlasWidth_1;
                uint _S311 = 5U % atlasWidth_1;
                int _S312 = int(_S311);
                uint _S313 = 5U / _S310;
                int3 _S314 = int3(_S312, int(_S313), int(0));
                uint _S315 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S314)).xy), uint(((_S314)).z))).x * 255.0f));
                uint2 grid_1 = uint2(_S309, _S315);
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
                    int3 _S316 = int3(int(1), int(0), int(0));
                    _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S316)).xy), uint(((_S316)).z)));
                    break;
                }
                uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S309 + uint(tile_2.x));
                uint _S317 = atlasWidth_1;
                uint _S318 = offset_1 % atlasWidth_1;
                int _S319 = int(_S318);
                uint _S320 = offset_1 / _S317;
                int3 _S321 = int3(_S319, int(_S320), int(0));
                uint _S322 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S321)).xy), uint(((_S321)).z))).x * 255.0f));
                uint _S323 = atlasWidth_1;
                uint _S324 = offset_1 + 1U;
                uint _S325 = _S324 % atlasWidth_1;
                int _S326 = int(_S325);
                uint _S327 = _S324 / _S323;
                int3 _S328 = int3(_S326, int(_S327), int(0));
                uint slot_2 = _S322 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S328)).xy), uint(((_S328)).z))).x * 255.0f)) << 8U);
                if(slot_2 == 0U)
                {
                    int3 _S329 = int3(int(1), int(0), int(0));
                    _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S329)).xy), uint(((_S329)).z)));
                    break;
                }
                uint slot_3 = slot_2 - 1U;
                uint _S330 = atlasWidth_1;
                uint _S331 = 6U % atlasWidth_1;
                int _S332 = int(_S331);
                uint _S333 = 6U / _S330;
                int3 _S334 = int3(_S332, int(_S333), int(0));
                uint _S335 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S334)).xy), uint(((_S334)).z))).x * 255.0f));
                uint _S336 = atlasWidth_1;
                uint _S337 = 7U % atlasWidth_1;
                int _S338 = int(_S337);
                uint _S339 = 7U / _S336;
                int3 _S340 = int3(_S338, int(_S339), int(0));
                uint columns_1 = _S335 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S340)).xy), uint(((_S340)).z))).x * 255.0f)) << 8U);
                uint _S341 = atlasWidth_1;
                uint _S342 = 8U % atlasWidth_1;
                int _S343 = int(_S342);
                uint _S344 = 8U / _S341;
                int3 _S345 = int3(_S343, int(_S344), int(0));
                uint _S346 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S345)).xy), uint(((_S345)).z))).x * 255.0f));
                uint _S347 = atlasWidth_1;
                uint _S348 = 9U % atlasWidth_1;
                int _S349 = int(_S348);
                uint _S350 = 9U / _S347;
                int3 _S351 = int3(_S349, int(_S350), int(0));
                uint rows_1 = ((_S346 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S351)).xy), uint(((_S351)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                uint metadataRows_1 = (10U + 2U * _S309 * _S315 + atlasWidth_1 - 1U) / atlasWidth_1;
                uint _S352 = atlasWidth_1 / columns_1;
                float _S353 = float(_S352);
                uint _S354 = (atlasHeight_1 - metadataRows_1) / rows_1;
                float2 cellSize_2 = float2(_S353, float(_S354));
                uint _S355 = slot_3 % columns_1;
                float _S356 = float(_S355);
                uint _S357 = slot_3 / columns_1;
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S356, float(_S357)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailUv_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            int2 _S358 = int2(metadata_1.zw);
            int2 tile_3 = int2(floor(detailUv_0)) - int2(metadata_1.xy);
            if(any(tile_3 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_3 >= _S358);
            }
            if(hasSceneLighting_0)
            {
                int3 _S359 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S359)).xy), uint(((_S359)).z)));
                break;
            }
            uint _S360 = atlasWidth_1 / uint(_S358.x);
            float _S361 = float(_S360);
            uint _S362 = (atlasHeight_1 - 1U) / uint(_S358.y);
            float2 cellSize_3 = float2(_S361, float(_S362));
            _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
            break;
        }
        detailSample_0 = _S5;
        if((_S284.w) != 0.0f)
        {
            float4 _S363 = detailSample_0;
            for(;;)
            {
                float _S364 = _S363.w;
                if(_S364 > 1.0f)
                {
                    unlitColor_0 = _S363.xyz;
                    break;
                }
                texture2d<float, access::sample> _S365 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_3;
                thread uint height_3;
                (*((&width_3)) = (_S365).get_width(0)),(*((&height_3)) = (_S365).get_height(0));
                uint3 _S366 = uint3(round(_S363.xyz * float3(255.0f) ));
                uint index_2 = ((_S366.x) | ((_S366.y) << 8U)) | ((_S366.z) << 16U);
                if(_S364 == 0.0f)
                {
                    _S255 = _S366;
                }
                else
                {
                    _S255 = uint3(index_2, index_2, index_2);
                }
                uint3 address_1 = _S255 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S367 = address_1.x;
                uint _S368 = _S367 % width_3;
                int _S369 = int(_S368);
                uint _S370 = _S367 / width_3;
                int3 _S371 = int3(_S369, int(_S370), int(0));
                float _S372 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S371)).xy), uint(((_S371)).z))).x;
                uint _S373 = address_1.y;
                uint _S374 = _S373 % width_3;
                int _S375 = int(_S374);
                uint _S376 = _S373 / width_3;
                int3 _S377 = int3(_S375, int(_S376), int(0));
                float _S378 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S377)).xy), uint(((_S377)).z))).x;
                uint _S379 = address_1.z;
                uint _S380 = _S379 % width_3;
                int _S381 = int(_S380);
                uint _S382 = _S379 / width_3;
                int3 _S383 = int3(_S381, int(_S382), int(0));
                unlitColor_0 = float3(_S372, _S378, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S383)).xy), uint(((_S383)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S384 = detail_0;
        if(any(_S285 != _S278))
        {
            float _S385 = _S285.x;
            float _S386 = _S285.y;
            float2 tangent_2 = normalize(float2(_S386, - _S385) / float2(max(abs(_S385), abs(_S386))) );
            float2 bitangent_1 = float2(- tangent_2.y, tangent_2.x);
            float2 tangent_3;
            if((dot(_S282, tangent_2)) < 0.0f)
            {
                tangent_3 = - tangent_2;
            }
            else
            {
                tangent_3 = tangent_2;
            }
            _S384.xy = tangent_3 * float2(_S384.x)  + bitangent_1 * float2(_S384.y) ;
        }
        float3 _S387 = _S384;
        for(;;)
        {
            float3 t_0 = normalize(_S281) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S387) * float3(-1.0f, -1.0f, 1.0f);
            float _S388 = t_0.z;
            if(_S388 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S388))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S281;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S389 = float3(unlitColor_0.x) ;
    float3 _S390 = float3(unlitColor_0.y) ;
    float3 _S391 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S389 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S390 + _S1.normal_0 * _S391);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S389 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S390 + worldNormalBasis_0 * _S391);
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
    float _S392 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S393 = max(0.00100000004749745f, roughness_0);
    float _S394 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S395 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S395;
    bool omniPbrSpecular_0 = (_S168.w) >= 0.5f;
    float dielectricLevel_0 = _S161.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S396 = float3(dielectricLevel_0) ;
        float3 _S397 = float3(metallic_0) ;
        float3 _S398 = mix(_S396, diffuseColor_0, _S397);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S396, diffuseColor_0, _S397);
        grazingIncidence_0 = _S398;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S166.w) >= 0.5f)
        {
            float3 _S399 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S399;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S400 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S400);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S400);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S401 = _S2;
    float _S402 = _S2.ambientLight_0.w;
    if(_S402 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S403 = _S401.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S403, _S403)) > 0.0f;
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
    uint _S404 = min(uint(_S402), 128U);
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
    float4 _S405 = float4(_S8->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S405.w) ;
    uint domeLinkMask_0 = uint(max(_S178.x, 0.0f));
    FragmentFrameParameters_0 _S406 = _S2;
    uint _S407 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S407) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S408 = _S401.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S409 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S409;
        for(;;)
        {
            if(lightCount_0 < _S407)
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
        domeAmbient_1 = _S408;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S410 = !hasSceneLighting_0;
    if(_S410)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S404;
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
        bool _S411;
        if(hasSceneLighting_0)
        {
            _S411 = (((uint4(_S8->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S411 = false;
        }
        if(_S411)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S412 = lightIndex_0 == 0U;
        bool _S413;
        if(_S412)
        {
            _S413 = _S410;
        }
        else
        {
            _S413 = false;
        }
        if(_S413)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S414 = 224U + lightIndex_0 * 16U;
            float _S415 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S414)>>2]);
            float _S416 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S414 + 4U)>>2]);
            float _S417 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S414 + 8U)>>2]);
            float _S418 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S414 + 12U)>>2]);
            lightType_0 = _S418;
        }
        bool _S419;
        if(_S412)
        {
            _S419 = _S410;
        }
        else
        {
            _S419 = false;
        }
        if(_S419)
        {
            lightDirection_0 = normalize((float4(_S8->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S420 = 2272U + lightIndex_0 * 16U;
            float _S421 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S420)>>2]);
            float _S422 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S420 + 4U)>>2]);
            float _S423 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S420 + 8U)>>2]);
            float _S424 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S420 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S421, _S422, _S423));
        }
        bool _S425;
        if(_S412)
        {
            _S425 = _S410;
        }
        else
        {
            _S425 = false;
        }
        if(_S425)
        {
            _S4 = (float4(_S8->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S426 = 4320U + lightIndex_0 * 16U;
            float _S427 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S426)>>2]);
            float _S428 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S426 + 4U)>>2]);
            float _S429 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S426 + 8U)>>2]);
            float _S430 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S426 + 12U)>>2]);
            _S4 = _S430;
        }
        bool _S431;
        if(_S412)
        {
            _S431 = _S410;
        }
        else
        {
            _S431 = false;
        }
        if(_S431)
        {
            diffuseColor_0 = _S405.xyz;
        }
        else
        {
            uint _S432 = 4320U + lightIndex_0 * 16U;
            float _S433 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S432)>>2]);
            float _S434 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S432 + 4U)>>2]);
            float _S435 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S432 + 8U)>>2]);
            float _S436 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S432 + 12U)>>2]);
            diffuseColor_0 = float3(_S433, _S434, _S435);
        }
        bool _S437;
        if(_S412)
        {
            _S437 = _S410;
        }
        else
        {
            _S437 = false;
        }
        float _S438;
        if(_S437)
        {
            _S438 = 1.0f;
        }
        else
        {
            uint _S439 = 6368U + lightIndex_0 * 16U;
            float _S440 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S439)>>2]);
            float _S441 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S439 + 4U)>>2]);
            float _S442 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S439 + 8U)>>2]);
            float _S443 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S439 + 12U)>>2]);
            _S438 = _S440;
        }
        bool _S444;
        if(_S412)
        {
            _S444 = _S410;
        }
        else
        {
            _S444 = false;
        }
        float _S445;
        if(_S444)
        {
            _S445 = 1.0f;
        }
        else
        {
            uint _S446 = 6368U + lightIndex_0 * 16U;
            float _S447 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S446)>>2]);
            float _S448 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S446 + 4U)>>2]);
            float _S449 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S446 + 8U)>>2]);
            float _S450 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S446 + 12U)>>2]);
            _S445 = _S448;
        }
        bool _S451;
        if(_S412)
        {
            _S451 = _S410;
        }
        else
        {
            _S451 = false;
        }
        if(_S451)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S452 = 8416U + lightIndex_0 * 16U;
            float _S453 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S452)>>2]);
            float _S454 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S452 + 4U)>>2]);
            float _S455 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S452 + 8U)>>2]);
            float _S456 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S452 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S453, _S454, _S455));
        }
        bool _S457;
        if(_S412)
        {
            _S457 = _S410;
        }
        else
        {
            _S457 = false;
        }
        if(_S457)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S458 = 10464U + lightIndex_0 * 16U;
            float _S459 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S458)>>2]);
            float _S460 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S458 + 4U)>>2]);
            float _S461 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S458 + 8U)>>2]);
            float _S462 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S458 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S459, _S460, _S461));
        }
        bool _S463;
        if(_S412)
        {
            _S463 = _S410;
        }
        else
        {
            _S463 = false;
        }
        float shapeX_0;
        if(_S463)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S464 = 8416U + lightIndex_0 * 16U;
            float _S465 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S464)>>2]);
            float _S466 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S464 + 4U)>>2]);
            float _S467 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S464 + 8U)>>2]);
            float _S468 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S464 + 12U)>>2]);
            shapeX_0 = _S468;
        }
        bool _S469;
        if(_S412)
        {
            _S469 = _S410;
        }
        else
        {
            _S469 = false;
        }
        float shapeY_0;
        if(_S469)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S470 = 10464U + lightIndex_0 * 16U;
            float _S471 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S470)>>2]);
            float _S472 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S470 + 4U)>>2]);
            float _S473 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S470 + 8U)>>2]);
            float _S474 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S470 + 12U)>>2]);
            shapeY_0 = _S474;
        }
        bool _S475;
        if(_S412)
        {
            _S475 = _S410;
        }
        else
        {
            _S475 = false;
        }
        float lightRadius_0;
        if(_S475)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S476 = 2272U + lightIndex_0 * 16U;
            float _S477 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S476)>>2]);
            float _S478 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S476 + 4U)>>2]);
            float _S479 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S476 + 8U)>>2]);
            float _S480 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S476 + 12U)>>2]);
            lightRadius_0 = _S480;
        }
        bool _S481;
        if(_S412)
        {
            _S481 = _S410;
        }
        else
        {
            _S481 = false;
        }
        if(_S481)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S482;
        if(_S412)
        {
            _S482 = _S410;
        }
        else
        {
            _S482 = false;
        }
        if(_S482)
        {
            _S5 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S483 = 15296U + lightIndex_0 * 16U;
            float _S484 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S483)>>2]);
            float _S485 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S483 + 4U)>>2]);
            float _S486 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S483 + 8U)>>2]);
            float _S487 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S483 + 12U)>>2]);
            _S5 = float4(_S484, _S485, _S486, _S487);
        }
        float shadowVisibility_0;
        float sampleCount_0;
        if(hasSceneLighting_0)
        {
            uint _S488 = 12960U + lightIndex_0 * 16U;
            float _S489 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S488)>>2]);
            float _S490 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S488 + 4U)>>2]);
            float _S491 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S488 + 8U)>>2]);
            float _S492 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S488 + 12U)>>2]);
            int shadowSlot_0 = int(_S489);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S493 = _S2;
                    FragmentFrameParameters_0 _S494 = _S2;
                    if((dot(diffuseFresnel_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - diffuseFresnel_0;
                    }
                    else
                    {
                        color_0 = diffuseFresnel_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S493.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S495 = lightClip_0.w;
                    if(_S495 <= 0.0f)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S495) ;
                    bool _S496;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S496 = true;
                    }
                    else
                    {
                        _S496 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S497;
                    if(_S496)
                    {
                        _S497 = true;
                    }
                    else
                    {
                        _S497 = (ndc_0.z) < 0.0f;
                    }
                    bool _S498;
                    if(_S497)
                    {
                        _S498 = true;
                    }
                    else
                    {
                        _S498 = (ndc_0.z) > 1.0f;
                    }
                    if(_S498)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float2 _S499 = _S494.shadowTile_0[shadowSlot_0].xy;
                    float2 _S500 = _S494.shadowTile_0[shadowSlot_0].zw;
                    float2 _S501 = _S499 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S500;
                    float texel_0 = _S493.shadowControls_0[shadowSlot_0].w;
                    float _S502 = max(_S493.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S503 = ndc_0.z - _S493.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S504 = float2((texel_0 * 0.5f)) ;
                    float2 _S505 = _S499 + _S504;
                    float2 _S506 = _S499 + _S500 - _S504;
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
                            if(_S503 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S501 + float2(float(x_0), float(y_0)) * float2((_S502 * texel_0)) , _S505, _S506)), level((0.0f))).x))
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
        float3 _S507 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S507;
        sampleOffsets_0[int(1)] = _S507;
        sampleOffsets_0[int(2)] = _S507;
        sampleOffsets_0[int(3)] = _S507;
        sampleOffsets_0[int(4)] = _S507;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S508 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S508 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S508 - halfHeight_0;
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
                uint _S509 = 224U + lightIndex_0 * 16U;
                float _S510 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S509)>>2]);
                float _S511 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S509 + 4U)>>2]);
                float _S512 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S509 + 8U)>>2]);
                float _S513 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S509 + 12U)>>2]);
                float3 toLight_0 = float3(_S510, _S511, _S512) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S514 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S514)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S514;
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
                        float _S515 = _S5.y;
                        if(_S515 > 0.0f)
                        {
                            emissionScale_0 = pow(max(emitterCosine_0, 0.0f), _S515);
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
            float _S516 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S516)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S516) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S517 = float3(_S516) ;
            float _S518 = max(_S393, 0.00100000004749745f);
            float alpha_0 = _S518 * _S518;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S519 = 1.0f - k_0;
            float3 _S520 = float3(max(4.0f * normalDotLight_0 * _S392, 1.00000000317107685e-30f)) ;
            float3 _S521 = mix(normalIncidence_0, grazingIncidence_0, _S517) * float3((_S392 / (_S392 * _S519 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S519 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S520;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S522 = max(_S394, 0.00100000004749745f);
                float alpha_1 = _S522 * _S522;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S523 = 1.0f - k_1;
                directSpecular_0 = _S521 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S517) * float3((_S392 / (_S392 * _S523 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S523 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S520);
            }
            else
            {
                directSpecular_0 = _S521;
            }
            float3 _S524 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S438)  * (_S524 * _S395) + directSpecular_0 * float3(_S445)  * _S524);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S525 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S526 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S527 = saturate(_S393);
        float2 _S528 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S526, _S527)), level((0.0f)))).xy;
        float _S529 = _S528.x;
        float _S530 = _S528.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S529)  + grazingIncidence_0 * float3(_S530) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S529 + _S530)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S531 = max(_S406.domeControls_0.y, 1.0f);
        float3 _S532 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S531 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S406.domeControls_0.z;
            float _S533 = _S406.domeControls_0.w;
            for(;;)
            {
                bool _S534 = _S531 <= 1.0f;
                _S6 = _S534;
                if(_S534)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S533, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S531)), level((0.0f)))).xyz;
                break;
            }
            float _S535 = _S525.environmentControls_0.y;
            float _S536 = _S525.environmentControls_0.z;
            for(;;)
            {
                if(_S6)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S537 = max(_S535, 1.0f);
                    float _S538 = _S537 - 1.0f;
                    float slice_0 = _S527 * max(_S538, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S536, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S537)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S538) + v_0) / _S537)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S539 = max(_S535, 1.0f);
                float total_0 = _S539 * _S531;
                float _S540 = _S539 - 1.0f;
                float slice_1 = _S527 * max(_S540, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S536, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S539;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S540) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S541 = max(_S535, 1.0f);
                        float _S542 = _S541 - 1.0f;
                        float slice_2 = saturate(_S394) * max(_S542, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S536, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S541)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S542) + v_2) / _S541)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S543 = max(_S535, 1.0f);
                    float total_1 = _S543 * _S531;
                    float _S544 = _S543 - 1.0f;
                    float slice_3 = saturate(_S394) * max(_S544, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S536, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S543;
                    normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S544) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightBitangent_0 = normal_2;
            }
            else
            {
                lightBitangent_0 = _S532;
            }
            lightDirection_0 = diffuseColor_0;
            lightTangent_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightDirection_0 = _S532;
            lightTangent_0 = _S532;
            lightBitangent_0 = _S532;
            for(;;)
            {
                if(sampleIndex_0 < _S407)
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
                float _S545 = _S406.domeControls_0.w;
                for(;;)
                {
                    bool _S546 = _S531 <= 1.0f;
                    _S7 = _S546;
                    if(_S546)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S545, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S531)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightDirection_0 + diffuseColor_0;
                float _S547 = _S525.environmentControls_0.y;
                float _S548 = _S525.environmentControls_0.z;
                for(;;)
                {
                    if(_S7)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S549 = max(_S547, 1.0f);
                        float _S550 = _S549 - 1.0f;
                        float slice_4 = _S527 * max(_S550, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S548, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S549)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S550) + v_4) / _S549)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S551 = max(_S547, 1.0f);
                    float total_2 = _S551 * _S531;
                    float _S552 = _S551 - 1.0f;
                    float slice_5 = _S527 * max(_S552, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S548, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S551;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S552) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
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
                            float _S553 = max(_S547, 1.0f);
                            float _S554 = _S553 - 1.0f;
                            float slice_6 = saturate(_S394) * max(_S554, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S548, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S553)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S554) + v_6) / _S553)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S555 = max(_S547, 1.0f);
                        float total_3 = _S555 * _S531;
                        float _S556 = _S555 - 1.0f;
                        float slice_7 = saturate(_S394) * max(_S556, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S548, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S555;
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S556) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
        float3 _S557 = float3(occlusion_0) ;
        float3 color_4 = color_3 + _S557 * diffuse_1 * lightDirection_0 * diffuseWeight_0 + _S557 * lightTangent_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S558 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S526, saturate(_S394))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightBitangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S558.x)  + float3(_S558.y) );
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
    pixelOutput_0 _S559 = { float4(color_3, opacity_0) };
    return _S559;
}
