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
    bool hasSceneLighting_0;
    float4 _S4;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    float3 color_0;
    bool _S5;
    bool _S6;
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
    float occlusion_0 = _S161.w;
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
        pixelOutput_0 _S162 = { float4(unlitColor_0, opacity_0) };
        return _S162;
    }
    float4 _S163 = float4(_S7->reserved_0) ;
    if((_S163.x) >= 0.5f)
    {
        pixelOutput_0 _S164 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S163.y) * max(0.0f, _S163.z)))) , 1.0f) };
        return _S164;
    }
    float4 _S165 = float4(_S7->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S165.x);
    float roughness_0 = clamp(_S165.y, 0.00999999977648258f, 1.0f);
    float4 _S166 = float4(_S7->specularIor_0) ;
    float3 specularColor_0 = _S166.xyz;
    float ior_0 = _S166.w;
    float clearcoatAmount_0 = _S160.x;
    float clearcoatRoughness_0 = _S160.y;
    float4 _S167 = float4(_S7->textureControls_0) ;
    uint textureMask_0 = uint(round(_S167.x));
    uint udimMask_0 = uint(round(_S167.y));
    float4 _S168 = float4(_S7->uvTransformRow0_0) ;
    float2 _S169 = _S168.xy;
    float _S170 = _S168.z;
    float4 _S171 = float4(_S7->uvTransformRow1_0) ;
    float2 _S172 = _S171.xy;
    float _S173 = _S171.z;
    float2 _S174 = float2(dot(_S169, _S1.texCoord_0) + _S170, dot(_S172, _S1.texCoord_0) + _S173);
    uint secondaryMask_0 = uint(round(_S171.w));
    float2 _S175 = float2(dot(_S169, _S1.texCoord1_0) + _S170, dot(_S172, _S1.texCoord1_0) + _S173);
    thread float4 baseNormalSample_0;
    float2 detailSource_0;
    if((secondaryMask_0 & 4U) != 0U)
    {
        detailSource_0 = _S175;
    }
    else
    {
        detailSource_0 = _S174;
    }
    bool _S176 = (udimMask_0 & 4U) != 0U;
    for(;;)
    {
        if(!_S176)
        {
            _S4 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (detailSource_0)));
            break;
        }
        texture2d<float, access::sample> _S177 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_0;
        thread uint atlasHeight_0;
        (*((&atlasWidth_0)) = (_S177).get_width(0)),(*((&atlasHeight_0)) = (_S177).get_height(0));
        int3 _S178 = int3(int(0), int(0), int(0));
        float4 metadata_0 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S178)).xy), uint(((_S178)).z))) * float4(255.0f) );
        if((metadata_0.x) == 255.0f)
        {
            uint _S179 = atlasWidth_0;
            uint _S180 = 2U % atlasWidth_0;
            int _S181 = int(_S180);
            uint _S182 = 2U / _S179;
            int3 _S183 = int3(_S181, int(_S182), int(0));
            int _S184 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S183)).xy), uint(((_S183)).z))).x * 255.0f)));
            uint _S185 = atlasWidth_0;
            uint _S186 = 3U % atlasWidth_0;
            int _S187 = int(_S186);
            uint _S188 = 3U / _S185;
            int3 _S189 = int3(_S187, int(_S188), int(0));
            int2 minimumTile_0 = int2(_S184, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S189)).xy), uint(((_S189)).z))).x * 255.0f))));
            uint _S190 = atlasWidth_0;
            uint _S191 = 4U % atlasWidth_0;
            int _S192 = int(_S191);
            uint _S193 = 4U / _S190;
            int3 _S194 = int3(_S192, int(_S193), int(0));
            uint _S195 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S194)).xy), uint(((_S194)).z))).x * 255.0f));
            uint _S196 = atlasWidth_0;
            uint _S197 = 5U % atlasWidth_0;
            int _S198 = int(_S197);
            uint _S199 = 5U / _S196;
            int3 _S200 = int3(_S198, int(_S199), int(0));
            uint _S201 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S200)).xy), uint(((_S200)).z))).x * 255.0f));
            uint2 grid_0 = uint2(_S195, _S201);
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
                int3 _S202 = int3(int(1), int(0), int(0));
                _S4 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S202)).xy), uint(((_S202)).z)));
                break;
            }
            uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S195 + uint(tile_0.x));
            uint _S203 = atlasWidth_0;
            uint _S204 = offset_0 % atlasWidth_0;
            int _S205 = int(_S204);
            uint _S206 = offset_0 / _S203;
            int3 _S207 = int3(_S205, int(_S206), int(0));
            uint _S208 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S207)).xy), uint(((_S207)).z))).x * 255.0f));
            uint _S209 = atlasWidth_0;
            uint _S210 = offset_0 + 1U;
            uint _S211 = _S210 % atlasWidth_0;
            int _S212 = int(_S211);
            uint _S213 = _S210 / _S209;
            int3 _S214 = int3(_S212, int(_S213), int(0));
            uint slot_0 = _S208 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S214)).xy), uint(((_S214)).z))).x * 255.0f)) << 8U);
            if(slot_0 == 0U)
            {
                int3 _S215 = int3(int(1), int(0), int(0));
                _S4 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S215)).xy), uint(((_S215)).z)));
                break;
            }
            uint slot_1 = slot_0 - 1U;
            uint _S216 = atlasWidth_0;
            uint _S217 = 6U % atlasWidth_0;
            int _S218 = int(_S217);
            uint _S219 = 6U / _S216;
            int3 _S220 = int3(_S218, int(_S219), int(0));
            uint _S221 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S220)).xy), uint(((_S220)).z))).x * 255.0f));
            uint _S222 = atlasWidth_0;
            uint _S223 = 7U % atlasWidth_0;
            int _S224 = int(_S223);
            uint _S225 = 7U / _S222;
            int3 _S226 = int3(_S224, int(_S225), int(0));
            uint columns_0 = _S221 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S226)).xy), uint(((_S226)).z))).x * 255.0f)) << 8U);
            uint _S227 = atlasWidth_0;
            uint _S228 = 8U % atlasWidth_0;
            int _S229 = int(_S228);
            uint _S230 = 8U / _S227;
            int3 _S231 = int3(_S229, int(_S230), int(0));
            uint _S232 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S231)).xy), uint(((_S231)).z))).x * 255.0f));
            uint _S233 = atlasWidth_0;
            uint _S234 = 9U % atlasWidth_0;
            int _S235 = int(_S234);
            uint _S236 = 9U / _S233;
            int3 _S237 = int3(_S235, int(_S236), int(0));
            uint rows_0 = ((_S232 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S237)).xy), uint(((_S237)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
            uint metadataRows_0 = (10U + 2U * _S195 * _S201 + atlasWidth_0 - 1U) / atlasWidth_0;
            uint _S238 = atlasWidth_0 / columns_0;
            float _S239 = float(_S238);
            uint _S240 = (atlasHeight_0 - metadataRows_0) / rows_0;
            float2 cellSize_0 = float2(_S239, float(_S240));
            uint _S241 = slot_1 % columns_0;
            float _S242 = float(_S241);
            uint _S243 = slot_1 / columns_0;
            _S4 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S242, float(_S243)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(detailSource_0) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        int2 _S244 = int2(metadata_0.zw);
        int2 tile_1 = int2(floor(detailSource_0)) - int2(metadata_0.xy);
        if(any(tile_1 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_1 >= _S244);
        }
        if(hasSceneLighting_0)
        {
            int3 _S245 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
            _S4 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S245)).xy), uint(((_S245)).z)));
            break;
        }
        uint _S246 = atlasWidth_0 / uint(_S244.x);
        float _S247 = float(_S246);
        uint _S248 = (atlasHeight_0 - 1U) / uint(_S244.y);
        float2 cellSize_1 = float2(_S247, float(_S248));
        _S4 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
        break;
    }
    baseNormalSample_0 = _S4;
    float4 _S249 = float4(_S7->detailUvTransformRow0_0) ;
    uint3 _S250;
    if((_S249.w) != 0.0f)
    {
        float4 _S251 = baseNormalSample_0;
        for(;;)
        {
            float _S252 = _S251.w;
            if(_S252 > 1.0f)
            {
                unlitColor_0 = _S251.xyz;
                break;
            }
            texture2d<float, access::sample> _S253 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_0;
            thread uint height_0;
            (*((&width_0)) = (_S253).get_width(0)),(*((&height_0)) = (_S253).get_height(0));
            uint3 _S254 = uint3(round(_S251.xyz * float3(255.0f) ));
            uint index_1 = ((_S254.x) | ((_S254.y) << 8U)) | ((_S254.z) << 16U);
            if(_S252 == 0.0f)
            {
                _S250 = _S254;
            }
            else
            {
                _S250 = uint3(index_1, index_1, index_1);
            }
            uint3 address_0 = _S250 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S255 = address_0.x;
            uint _S256 = _S255 % width_0;
            int _S257 = int(_S256);
            uint _S258 = _S255 / width_0;
            int3 _S259 = int3(_S257, int(_S258), int(0));
            float _S260 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S259)).xy), uint(((_S259)).z))).x;
            uint _S261 = address_0.y;
            uint _S262 = _S261 % width_0;
            int _S263 = int(_S262);
            uint _S264 = _S261 / width_0;
            int3 _S265 = int3(_S263, int(_S264), int(0));
            float _S266 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S265)).xy), uint(((_S265)).z))).x;
            uint _S267 = address_0.z;
            uint _S268 = _S267 % width_0;
            int _S269 = int(_S268);
            uint _S270 = _S267 / width_0;
            int3 _S271 = int3(_S269, int(_S270), int(0));
            unlitColor_0 = float3(_S260, _S266, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S271)).xy), uint(((_S271)).z))).x);
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
    thread float3 _S272 = unlitColor_0;
    float2 _S273 = float2(0.0f, 0.0f);
    if(any(_S172 != _S273))
    {
        float _S274 = _S172.x;
        float _S275 = _S172.y;
        float2 tangent_1 = normalize(float2(_S275, - _S274) / float2(max(abs(_S274), abs(_S275))) );
        float2 bitangent_0 = float2(- tangent_1.y, tangent_1.x);
        if((dot(_S169, tangent_1)) < 0.0f)
        {
            detailSource_0 = - tangent_1;
        }
        else
        {
            detailSource_0 = tangent_1;
        }
        _S272.xy = detailSource_0 * float2(_S272.x)  + bitangent_0 * float2(_S272.y) ;
    }
    float3 _S276 = _S272;
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
        float2 _S277 = _S249.xy;
        float4 _S278 = float4(_S7->detailUvTransformRow1_0) ;
        float2 _S279 = _S278.xy;
        float2 detailUv_0 = float2(dot(_S277, detailSource_0) + _S249.z, dot(_S279, detailSource_0) + _S278.z);
        thread float4 detailSample_0;
        bool _S280 = (udimMask_0 & 4096U) != 0U;
        for(;;)
        {
            if(!_S280)
            {
                _S4 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailUv_0)));
                break;
            }
            texture2d<float, access::sample> _S281 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_1;
            thread uint atlasHeight_1;
            (*((&atlasWidth_1)) = (_S281).get_width(0)),(*((&atlasHeight_1)) = (_S281).get_height(0));
            int3 _S282 = int3(int(0), int(0), int(0));
            float4 metadata_1 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S282)).xy), uint(((_S282)).z))) * float4(255.0f) );
            if((metadata_1.x) == 255.0f)
            {
                uint _S283 = atlasWidth_1;
                uint _S284 = 2U % atlasWidth_1;
                int _S285 = int(_S284);
                uint _S286 = 2U / _S283;
                int3 _S287 = int3(_S285, int(_S286), int(0));
                int _S288 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S287)).xy), uint(((_S287)).z))).x * 255.0f)));
                uint _S289 = atlasWidth_1;
                uint _S290 = 3U % atlasWidth_1;
                int _S291 = int(_S290);
                uint _S292 = 3U / _S289;
                int3 _S293 = int3(_S291, int(_S292), int(0));
                int2 minimumTile_1 = int2(_S288, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S293)).xy), uint(((_S293)).z))).x * 255.0f))));
                uint _S294 = atlasWidth_1;
                uint _S295 = 4U % atlasWidth_1;
                int _S296 = int(_S295);
                uint _S297 = 4U / _S294;
                int3 _S298 = int3(_S296, int(_S297), int(0));
                uint _S299 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S298)).xy), uint(((_S298)).z))).x * 255.0f));
                uint _S300 = atlasWidth_1;
                uint _S301 = 5U % atlasWidth_1;
                int _S302 = int(_S301);
                uint _S303 = 5U / _S300;
                int3 _S304 = int3(_S302, int(_S303), int(0));
                uint _S305 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S304)).xy), uint(((_S304)).z))).x * 255.0f));
                uint2 grid_1 = uint2(_S299, _S305);
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
                    int3 _S306 = int3(int(1), int(0), int(0));
                    _S4 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S306)).xy), uint(((_S306)).z)));
                    break;
                }
                uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S299 + uint(tile_2.x));
                uint _S307 = atlasWidth_1;
                uint _S308 = offset_1 % atlasWidth_1;
                int _S309 = int(_S308);
                uint _S310 = offset_1 / _S307;
                int3 _S311 = int3(_S309, int(_S310), int(0));
                uint _S312 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S311)).xy), uint(((_S311)).z))).x * 255.0f));
                uint _S313 = atlasWidth_1;
                uint _S314 = offset_1 + 1U;
                uint _S315 = _S314 % atlasWidth_1;
                int _S316 = int(_S315);
                uint _S317 = _S314 / _S313;
                int3 _S318 = int3(_S316, int(_S317), int(0));
                uint slot_2 = _S312 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S318)).xy), uint(((_S318)).z))).x * 255.0f)) << 8U);
                if(slot_2 == 0U)
                {
                    int3 _S319 = int3(int(1), int(0), int(0));
                    _S4 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S319)).xy), uint(((_S319)).z)));
                    break;
                }
                uint slot_3 = slot_2 - 1U;
                uint _S320 = atlasWidth_1;
                uint _S321 = 6U % atlasWidth_1;
                int _S322 = int(_S321);
                uint _S323 = 6U / _S320;
                int3 _S324 = int3(_S322, int(_S323), int(0));
                uint _S325 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S324)).xy), uint(((_S324)).z))).x * 255.0f));
                uint _S326 = atlasWidth_1;
                uint _S327 = 7U % atlasWidth_1;
                int _S328 = int(_S327);
                uint _S329 = 7U / _S326;
                int3 _S330 = int3(_S328, int(_S329), int(0));
                uint columns_1 = _S325 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S330)).xy), uint(((_S330)).z))).x * 255.0f)) << 8U);
                uint _S331 = atlasWidth_1;
                uint _S332 = 8U % atlasWidth_1;
                int _S333 = int(_S332);
                uint _S334 = 8U / _S331;
                int3 _S335 = int3(_S333, int(_S334), int(0));
                uint _S336 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S335)).xy), uint(((_S335)).z))).x * 255.0f));
                uint _S337 = atlasWidth_1;
                uint _S338 = 9U % atlasWidth_1;
                int _S339 = int(_S338);
                uint _S340 = 9U / _S337;
                int3 _S341 = int3(_S339, int(_S340), int(0));
                uint rows_1 = ((_S336 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S341)).xy), uint(((_S341)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                uint metadataRows_1 = (10U + 2U * _S299 * _S305 + atlasWidth_1 - 1U) / atlasWidth_1;
                uint _S342 = atlasWidth_1 / columns_1;
                float _S343 = float(_S342);
                uint _S344 = (atlasHeight_1 - metadataRows_1) / rows_1;
                float2 cellSize_2 = float2(_S343, float(_S344));
                uint _S345 = slot_3 % columns_1;
                float _S346 = float(_S345);
                uint _S347 = slot_3 / columns_1;
                _S4 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S346, float(_S347)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailUv_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            int2 _S348 = int2(metadata_1.zw);
            int2 tile_3 = int2(floor(detailUv_0)) - int2(metadata_1.xy);
            if(any(tile_3 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_3 >= _S348);
            }
            if(hasSceneLighting_0)
            {
                int3 _S349 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                _S4 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S349)).xy), uint(((_S349)).z)));
                break;
            }
            uint _S350 = atlasWidth_1 / uint(_S348.x);
            float _S351 = float(_S350);
            uint _S352 = (atlasHeight_1 - 1U) / uint(_S348.y);
            float2 cellSize_3 = float2(_S351, float(_S352));
            _S4 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
            break;
        }
        detailSample_0 = _S4;
        if((_S278.w) != 0.0f)
        {
            float4 _S353 = detailSample_0;
            for(;;)
            {
                float _S354 = _S353.w;
                if(_S354 > 1.0f)
                {
                    unlitColor_0 = _S353.xyz;
                    break;
                }
                texture2d<float, access::sample> _S355 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_1;
                thread uint height_1;
                (*((&width_1)) = (_S355).get_width(0)),(*((&height_1)) = (_S355).get_height(0));
                uint3 _S356 = uint3(round(_S353.xyz * float3(255.0f) ));
                uint index_2 = ((_S356.x) | ((_S356.y) << 8U)) | ((_S356.z) << 16U);
                if(_S354 == 0.0f)
                {
                    _S250 = _S356;
                }
                else
                {
                    _S250 = uint3(index_2, index_2, index_2);
                }
                uint3 address_1 = _S250 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S357 = address_1.x;
                uint _S358 = _S357 % width_1;
                int _S359 = int(_S358);
                uint _S360 = _S357 / width_1;
                int3 _S361 = int3(_S359, int(_S360), int(0));
                float _S362 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S361)).xy), uint(((_S361)).z))).x;
                uint _S363 = address_1.y;
                uint _S364 = _S363 % width_1;
                int _S365 = int(_S364);
                uint _S366 = _S363 / width_1;
                int3 _S367 = int3(_S365, int(_S366), int(0));
                float _S368 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S367)).xy), uint(((_S367)).z))).x;
                uint _S369 = address_1.z;
                uint _S370 = _S369 % width_1;
                int _S371 = int(_S370);
                uint _S372 = _S369 / width_1;
                int3 _S373 = int3(_S371, int(_S372), int(0));
                unlitColor_0 = float3(_S362, _S368, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S373)).xy), uint(((_S373)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S374 = detail_0;
        if(any(_S279 != _S273))
        {
            float _S375 = _S279.x;
            float _S376 = _S279.y;
            float2 tangent_2 = normalize(float2(_S376, - _S375) / float2(max(abs(_S375), abs(_S376))) );
            float2 bitangent_1 = float2(- tangent_2.y, tangent_2.x);
            float2 tangent_3;
            if((dot(_S277, tangent_2)) < 0.0f)
            {
                tangent_3 = - tangent_2;
            }
            else
            {
                tangent_3 = tangent_2;
            }
            _S374.xy = tangent_3 * float2(_S374.x)  + bitangent_1 * float2(_S374.y) ;
        }
        float3 _S377 = _S374;
        for(;;)
        {
            float3 t_0 = normalize(_S276) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S377) * float3(-1.0f, -1.0f, 1.0f);
            float _S378 = t_0.z;
            if(_S378 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S378))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S276;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S379 = float3(unlitColor_0.x) ;
    float3 _S380 = float3(unlitColor_0.y) ;
    float3 _S381 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S379 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S380 + _S1.normal_0 * _S381);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S379 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S380 + worldNormalBasis_0 * _S381);
    float opacityThreshold_0 = _S165.z;
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
    float _S382 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S383 = max(0.00100000004749745f, roughness_0);
    float _S384 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S385 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S385;
    bool omniPbrSpecular_0 = (_S167.w) >= 0.5f;
    float dielectricLevel_0 = _S160.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S386 = float3(dielectricLevel_0) ;
        float3 _S387 = float3(metallic_0) ;
        float3 _S388 = mix(_S386, diffuseColor_0, _S387);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S386, diffuseColor_0, _S387);
        grazingIncidence_0 = _S388;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S165.w) >= 0.5f)
        {
            float3 _S389 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S389;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S390 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S390);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S390);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S391 = _S2;
    float _S392 = _S2.ambientLight_0.w;
    if(_S392 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S393 = _S391.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S393, _S393)) > 0.0f;
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
    uint _S394 = min(uint(_S392), 128U);
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
    float4 _S395 = float4(_S7->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S395.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S7->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S396 = _S2;
    uint _S397 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S397) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S398 = _S391.ambientLight_0.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S399 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S399;
        for(;;)
        {
            if(lightCount_0 < _S397)
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
        domeAmbient_1 = _S398;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S400 = !hasSceneLighting_0;
    if(_S400)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S394;
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
        bool _S401;
        if(hasSceneLighting_0)
        {
            _S401 = (((uint4(_S7->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S401 = false;
        }
        if(_S401)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S402 = lightIndex_0 == 0U;
        bool _S403;
        if(_S402)
        {
            _S403 = _S400;
        }
        else
        {
            _S403 = false;
        }
        float lightType_0;
        if(_S403)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S404 = 224U + lightIndex_0 * 16U;
            float _S405 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S404)>>2]);
            float _S406 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S404 + 4U)>>2]);
            float _S407 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S404 + 8U)>>2]);
            float _S408 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S404 + 12U)>>2]);
            lightType_0 = _S408;
        }
        bool _S409;
        if(_S402)
        {
            _S409 = _S400;
        }
        else
        {
            _S409 = false;
        }
        if(_S409)
        {
            lightDirection_0 = normalize((float4(_S7->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S410 = 2272U + lightIndex_0 * 16U;
            float _S411 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410)>>2]);
            float _S412 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410 + 4U)>>2]);
            float _S413 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410 + 8U)>>2]);
            float _S414 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S411, _S412, _S413));
        }
        bool _S415;
        if(_S402)
        {
            _S415 = _S400;
        }
        else
        {
            _S415 = false;
        }
        float _S416;
        if(_S415)
        {
            _S416 = (float4(_S7->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S417 = 4320U + lightIndex_0 * 16U;
            float _S418 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S417)>>2]);
            float _S419 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S417 + 4U)>>2]);
            float _S420 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S417 + 8U)>>2]);
            float _S421 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S417 + 12U)>>2]);
            _S416 = _S421;
        }
        bool _S422;
        if(_S402)
        {
            _S422 = _S400;
        }
        else
        {
            _S422 = false;
        }
        if(_S422)
        {
            diffuseColor_0 = _S395.xyz;
        }
        else
        {
            uint _S423 = 4320U + lightIndex_0 * 16U;
            float _S424 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423)>>2]);
            float _S425 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423 + 4U)>>2]);
            float _S426 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423 + 8U)>>2]);
            float _S427 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423 + 12U)>>2]);
            diffuseColor_0 = float3(_S424, _S425, _S426);
        }
        bool _S428;
        if(_S402)
        {
            _S428 = _S400;
        }
        else
        {
            _S428 = false;
        }
        float _S429;
        if(_S428)
        {
            _S429 = 1.0f;
        }
        else
        {
            uint _S430 = 6368U + lightIndex_0 * 16U;
            float _S431 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S430)>>2]);
            float _S432 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S430 + 4U)>>2]);
            float _S433 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S430 + 8U)>>2]);
            float _S434 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S430 + 12U)>>2]);
            _S429 = _S431;
        }
        bool _S435;
        if(_S402)
        {
            _S435 = _S400;
        }
        else
        {
            _S435 = false;
        }
        float _S436;
        if(_S435)
        {
            _S436 = 1.0f;
        }
        else
        {
            uint _S437 = 6368U + lightIndex_0 * 16U;
            float _S438 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S437)>>2]);
            float _S439 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S437 + 4U)>>2]);
            float _S440 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S437 + 8U)>>2]);
            float _S441 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S437 + 12U)>>2]);
            _S436 = _S439;
        }
        bool _S442;
        if(_S402)
        {
            _S442 = _S400;
        }
        else
        {
            _S442 = false;
        }
        if(_S442)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S443 = 8416U + lightIndex_0 * 16U;
            float _S444 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S443)>>2]);
            float _S445 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S443 + 4U)>>2]);
            float _S446 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S443 + 8U)>>2]);
            float _S447 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S443 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S444, _S445, _S446));
        }
        bool _S448;
        if(_S402)
        {
            _S448 = _S400;
        }
        else
        {
            _S448 = false;
        }
        if(_S448)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S449 = 10464U + lightIndex_0 * 16U;
            float _S450 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S449)>>2]);
            float _S451 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S449 + 4U)>>2]);
            float _S452 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S449 + 8U)>>2]);
            float _S453 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S449 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S450, _S451, _S452));
        }
        bool _S454;
        if(_S402)
        {
            _S454 = _S400;
        }
        else
        {
            _S454 = false;
        }
        float shapeX_0;
        if(_S454)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S455 = 8416U + lightIndex_0 * 16U;
            float _S456 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S455)>>2]);
            float _S457 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S455 + 4U)>>2]);
            float _S458 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S455 + 8U)>>2]);
            float _S459 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S455 + 12U)>>2]);
            shapeX_0 = _S459;
        }
        bool _S460;
        if(_S402)
        {
            _S460 = _S400;
        }
        else
        {
            _S460 = false;
        }
        float shapeY_0;
        if(_S460)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S461 = 10464U + lightIndex_0 * 16U;
            float _S462 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461)>>2]);
            float _S463 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461 + 4U)>>2]);
            float _S464 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461 + 8U)>>2]);
            float _S465 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461 + 12U)>>2]);
            shapeY_0 = _S465;
        }
        bool _S466;
        if(_S402)
        {
            _S466 = _S400;
        }
        else
        {
            _S466 = false;
        }
        float lightRadius_0;
        if(_S466)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S467 = 2272U + lightIndex_0 * 16U;
            float _S468 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467)>>2]);
            float _S469 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 4U)>>2]);
            float _S470 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 8U)>>2]);
            float _S471 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 12U)>>2]);
            lightRadius_0 = _S471;
        }
        bool _S472;
        if(_S402)
        {
            _S472 = _S400;
        }
        else
        {
            _S472 = false;
        }
        if(_S472)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        bool _S473;
        if(_S402)
        {
            _S473 = _S400;
        }
        else
        {
            _S473 = false;
        }
        if(_S473)
        {
            _S4 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S474 = 15296U + lightIndex_0 * 16U;
            float _S475 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S474)>>2]);
            float _S476 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S474 + 4U)>>2]);
            float _S477 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S474 + 8U)>>2]);
            float _S478 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S474 + 12U)>>2]);
            _S4 = float4(_S475, _S476, _S477, _S478);
        }
        float shadowVisibility_0;
        float sampleCount_0;
        if(hasSceneLighting_0)
        {
            uint _S479 = 12960U + lightIndex_0 * 16U;
            float _S480 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S479)>>2]);
            float _S481 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S479 + 4U)>>2]);
            float _S482 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S479 + 8U)>>2]);
            float _S483 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S479 + 12U)>>2]);
            int shadowSlot_0 = int(_S480);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S484 = _S2;
                    FragmentFrameParameters_0 _S485 = _S2;
                    if((dot(diffuseFresnel_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - diffuseFresnel_0;
                    }
                    else
                    {
                        color_0 = diffuseFresnel_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S484.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S2.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S486 = lightClip_0.w;
                    if(_S486 <= 0.0f)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S486) ;
                    bool _S487;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S487 = true;
                    }
                    else
                    {
                        _S487 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S488;
                    if(_S487)
                    {
                        _S488 = true;
                    }
                    else
                    {
                        _S488 = (ndc_0.z) < 0.0f;
                    }
                    bool _S489;
                    if(_S488)
                    {
                        _S489 = true;
                    }
                    else
                    {
                        _S489 = (ndc_0.z) > 1.0f;
                    }
                    if(_S489)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float2 _S490 = _S485.shadowTile_0[shadowSlot_0].xy;
                    float2 _S491 = _S485.shadowTile_0[shadowSlot_0].zw;
                    float2 _S492 = _S490 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S491;
                    float texel_0 = _S484.shadowControls_0[shadowSlot_0].w;
                    float _S493 = max(_S484.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S494 = ndc_0.z - _S484.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S495 = float2((texel_0 * 0.5f)) ;
                    float2 _S496 = _S490 + _S495;
                    float2 _S497 = _S490 + _S491 - _S495;
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
                            if(_S494 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S492 + float2(float(x_0), float(y_0)) * float2((_S493 * texel_0)) , _S496, _S497)), level((0.0f))).x))
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
        float3 _S498 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S498;
        sampleOffsets_0[int(1)] = _S498;
        sampleOffsets_0[int(2)] = _S498;
        sampleOffsets_0[int(3)] = _S498;
        sampleOffsets_0[int(4)] = _S498;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S499 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S499 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S499 - halfHeight_0;
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
            float sampleIntensity_0 = _S416 / sampleCount_0;
            float3 sampleDirection_0;
            float sampleIntensity_1;
            float emissionScale_0;
            if(lightType_0 >= 2.0f)
            {
                uint _S500 = 224U + lightIndex_0 * 16U;
                float _S501 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S500)>>2]);
                float _S502 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S500 + 4U)>>2]);
                float _S503 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S500 + 8U)>>2]);
                float _S504 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S500 + 12U)>>2]);
                float3 toLight_0 = float3(_S501, _S502, _S503) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S505 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S505)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S505;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S4.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S4.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S506 = _S4.y;
                        if(_S506 > 0.0f)
                        {
                            emissionScale_0 = pow(max(emitterCosine_0, 0.0f), _S506);
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
            float _S507 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S507)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S507) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S508 = float3(_S507) ;
            float _S509 = max(_S383, 0.00100000004749745f);
            float alpha_0 = _S509 * _S509;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S510 = 1.0f - k_0;
            float3 _S511 = float3(max(4.0f * normalDotLight_0 * _S382, 1.00000000317107685e-30f)) ;
            float3 _S512 = mix(normalIncidence_0, grazingIncidence_0, _S508) * float3((_S382 / (_S382 * _S510 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S510 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S511;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S513 = max(_S384, 0.00100000004749745f);
                float alpha_1 = _S513 * _S513;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S514 = 1.0f - k_1;
                directSpecular_0 = _S512 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S508) * float3((_S382 / (_S382 * _S514 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S514 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S511);
            }
            else
            {
                directSpecular_0 = _S512;
            }
            float3 _S515 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S429)  * (_S515 * _S385) + directSpecular_0 * float3(_S436)  * _S515);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S516 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S517 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S518 = saturate(_S383);
        float2 _S519 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S517, _S518)), level((0.0f)))).xy;
        float _S520 = _S519.x;
        float _S521 = _S519.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S520)  + grazingIncidence_0 * float3(_S521) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S520 + _S521)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S522 = max(_S396.domeControls_0.y, 1.0f);
        float3 _S523 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S522 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S396.domeControls_0.z;
            float _S524 = _S396.domeControls_0.w;
            for(;;)
            {
                bool _S525 = _S522 <= 1.0f;
                _S5 = _S525;
                if(_S525)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S524, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S522)), level((0.0f)))).xyz;
                break;
            }
            float _S526 = _S516.environmentControls_0.y;
            float _S527 = _S516.environmentControls_0.z;
            for(;;)
            {
                if(_S5)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S528 = max(_S526, 1.0f);
                    float _S529 = _S528 - 1.0f;
                    float slice_0 = _S518 * max(_S529, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S527, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S528)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S529) + v_0) / _S528)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S530 = max(_S526, 1.0f);
                float total_0 = _S530 * _S522;
                float _S531 = _S530 - 1.0f;
                float slice_1 = _S518 * max(_S531, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S527, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S530;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S531) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S532 = max(_S526, 1.0f);
                        float _S533 = _S532 - 1.0f;
                        float slice_2 = saturate(_S384) * max(_S533, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S527, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S532)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S533) + v_2) / _S532)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S534 = max(_S526, 1.0f);
                    float total_1 = _S534 * _S522;
                    float _S535 = _S534 - 1.0f;
                    float slice_3 = saturate(_S384) * max(_S535, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S527, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S534;
                    normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S535) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightBitangent_0 = normal_2;
            }
            else
            {
                lightBitangent_0 = _S523;
            }
            lightDirection_0 = diffuseColor_0;
            lightTangent_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightDirection_0 = _S523;
            lightTangent_0 = _S523;
            lightBitangent_0 = _S523;
            for(;;)
            {
                if(sampleIndex_0 < _S397)
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
                float _S536 = _S396.domeControls_0.w;
                for(;;)
                {
                    bool _S537 = _S522 <= 1.0f;
                    _S6 = _S537;
                    if(_S537)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S536, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S522)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightDirection_0 + diffuseColor_0;
                float _S538 = _S516.environmentControls_0.y;
                float _S539 = _S516.environmentControls_0.z;
                for(;;)
                {
                    if(_S6)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S540 = max(_S538, 1.0f);
                        float _S541 = _S540 - 1.0f;
                        float slice_4 = _S518 * max(_S541, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S539, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S540)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S541) + v_4) / _S540)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S542 = max(_S538, 1.0f);
                    float total_2 = _S542 * _S522;
                    float _S543 = _S542 - 1.0f;
                    float slice_5 = _S518 * max(_S543, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S539, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S542;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S543) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
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
                            float _S544 = max(_S538, 1.0f);
                            float _S545 = _S544 - 1.0f;
                            float slice_6 = saturate(_S384) * max(_S545, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S539, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S544)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S545) + v_6) / _S544)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S546 = max(_S538, 1.0f);
                        float total_3 = _S546 * _S522;
                        float _S547 = _S546 - 1.0f;
                        float slice_7 = saturate(_S384) * max(_S547, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S539, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S546;
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S547) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
        float3 _S548 = float3(occlusion_0) ;
        float3 color_4 = color_3 + _S548 * diffuse_1 * lightDirection_0 * diffuseWeight_0 + _S548 * lightTangent_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S549 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S517, saturate(_S384))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightBitangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S549.x)  + float3(_S549.y) );
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
    pixelOutput_0 _S550 = { float4(color_3, opacity_0) };
    return _S550;
}
