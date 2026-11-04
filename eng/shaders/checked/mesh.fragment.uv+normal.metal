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
    texture2d<float, access::sample> normalTexture_0;
    sampler normalSampler_0;
    texture2d<float, access::sample> normalValuesTexture_0;
    texture2d<float, access::sample> detailNormalTexture_0;
    sampler detailNormalSampler_0;
    texture2d<float, access::sample> detailNormalValuesTexture_0;
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

[[fragment]] pixelOutput_0 fragmentMain_uv_normal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> normalValuesTexture_1 [[texture(20)]], texture2d<float, access::sample> detailNormalTexture_1 [[texture(9)]], sampler detailNormalSampler_1 [[sampler(4)]], texture2d<float, access::sample> detailNormalValuesTexture_1 [[texture(21)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> shadowTransmission0_1 [[texture(24)]], texture2d<float, access::sample> shadowTransmission1_1 [[texture(25)]], texture2d<float, access::sample> shadowTransmission2_1 [[texture(26)]], texture2d<float, access::sample> shadowTransmission3_1 [[texture(27)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_0;
    FragmentFrameParameters_0 _S2;
    uint4 _S3;
    float lightType_0;
    float _S4;
    bool hasSceneLighting_0;
    float4 _S5;
    uint sampleIndex_0;
    float3 lightDirectionAxis_0;
    float3 lightTangentAxis_0;
    float3 lightBitangentAxis_0;
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
    (&kernelContext_0)->shadowTransmission0_0 = shadowTransmission0_1;
    (&kernelContext_0)->shadowTransmission1_0 = shadowTransmission1_1;
    (&kernelContext_0)->shadowTransmission2_0 = shadowTransmission2_1;
    (&kernelContext_0)->shadowTransmission3_0 = shadowTransmission3_1;
    (&kernelContext_0)->environmentBrdf_0 = environmentBrdf_1;
    (&kernelContext_0)->environmentBrdfSampler_0 = environmentBrdfSampler_1;
    (&kernelContext_0)->environmentIrradiance_0 = environmentIrradiance_1;
    (&kernelContext_0)->environmentSampler_0 = environmentSampler_1;
    (&kernelContext_0)->environmentSpecular_0 = environmentSpecular_1;
    SurfaceParameters_natural_0 device* _S8 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        float _S9 = as_type<float>((&kernelContext_0)->frameParameters_0[(0U)>>2]);
        float _S10 = as_type<float>((&kernelContext_0)->frameParameters_0[(4U)>>2]);
        float _S11 = as_type<float>((&kernelContext_0)->frameParameters_0[(8U)>>2]);
        float _S12 = as_type<float>((&kernelContext_0)->frameParameters_0[(12U)>>2]);
        float4 _S13 = float4(_S9, _S10, _S11, _S12);
        float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(16U)>>2]);
        float _S15 = as_type<float>((&kernelContext_0)->frameParameters_0[(20U)>>2]);
        float _S16 = as_type<float>((&kernelContext_0)->frameParameters_0[(24U)>>2]);
        float _S17 = as_type<float>((&kernelContext_0)->frameParameters_0[(28U)>>2]);
        float4 _S18 = float4(_S14, _S15, _S16, _S17);
        float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(32U)>>2]);
        float _S20 = as_type<float>((&kernelContext_0)->frameParameters_0[(36U)>>2]);
        float _S21 = as_type<float>((&kernelContext_0)->frameParameters_0[(40U)>>2]);
        float _S22 = as_type<float>((&kernelContext_0)->frameParameters_0[(44U)>>2]);
        float4 _S23 = float4(_S19, _S20, _S21, _S22);
        float _S24 = as_type<float>((&kernelContext_0)->frameParameters_0[(48U)>>2]);
        float _S25 = as_type<float>((&kernelContext_0)->frameParameters_0[(52U)>>2]);
        float _S26 = as_type<float>((&kernelContext_0)->frameParameters_0[(56U)>>2]);
        float _S27 = as_type<float>((&kernelContext_0)->frameParameters_0[(60U)>>2]);
        (&frame_0)->clipToEye_0 = matrix<float,int(4),int(4)> (_S13, _S18, _S23, float4(_S24, _S25, _S26, _S27));
        uint _S28 = as_type<uint>((&kernelContext_0)->frameParameters_0[(64U)>>2]);
        uint _S29 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
        uint _S30 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
        uint _S31 = as_type<uint>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S28, _S29, _S30, _S31);
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
            uint _S32 = 80U + index_0 * 16U;
            float _S33 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S32)>>2]);
            float _S34 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S32 + 4U)>>2]);
            float _S35 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S32 + 8U)>>2]);
            float _S36 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S32 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_0] = float4(_S33, _S34, _S35, _S36);
            index_0 = index_0 + 1U;
        }
        float _S37 = as_type<float>((&kernelContext_0)->frameParameters_0[(208U)>>2]);
        float _S38 = as_type<float>((&kernelContext_0)->frameParameters_0[(212U)>>2]);
        float _S39 = as_type<float>((&kernelContext_0)->frameParameters_0[(216U)>>2]);
        float _S40 = as_type<float>((&kernelContext_0)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S37, _S38, _S39, _S40);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 14880U;
        float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(12512U)>>2]);
        float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(12516U)>>2]);
        float _S43 = as_type<float>((&kernelContext_0)->frameParameters_0[(12520U)>>2]);
        float _S44 = as_type<float>((&kernelContext_0)->frameParameters_0[(12524U)>>2]);
        float4 _S45 = float4(_S41, _S42, _S43, _S44);
        float _S46 = as_type<float>((&kernelContext_0)->frameParameters_0[(12528U)>>2]);
        float _S47 = as_type<float>((&kernelContext_0)->frameParameters_0[(12532U)>>2]);
        float _S48 = as_type<float>((&kernelContext_0)->frameParameters_0[(12536U)>>2]);
        float _S49 = as_type<float>((&kernelContext_0)->frameParameters_0[(12540U)>>2]);
        float4 _S50 = float4(_S46, _S47, _S48, _S49);
        float _S51 = as_type<float>((&kernelContext_0)->frameParameters_0[(12544U)>>2]);
        float _S52 = as_type<float>((&kernelContext_0)->frameParameters_0[(12548U)>>2]);
        float _S53 = as_type<float>((&kernelContext_0)->frameParameters_0[(12552U)>>2]);
        float _S54 = as_type<float>((&kernelContext_0)->frameParameters_0[(12556U)>>2]);
        float4 _S55 = float4(_S51, _S52, _S53, _S54);
        float _S56 = as_type<float>((&kernelContext_0)->frameParameters_0[(12560U)>>2]);
        float _S57 = as_type<float>((&kernelContext_0)->frameParameters_0[(12564U)>>2]);
        float _S58 = as_type<float>((&kernelContext_0)->frameParameters_0[(12568U)>>2]);
        float _S59 = as_type<float>((&kernelContext_0)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S45, _S50, _S55, float4(_S56, _S57, _S58, _S59));
        float _S60 = as_type<float>((&kernelContext_0)->frameParameters_0[(16928U)>>2]);
        float _S61 = as_type<float>((&kernelContext_0)->frameParameters_0[(16932U)>>2]);
        float _S62 = as_type<float>((&kernelContext_0)->frameParameters_0[(16936U)>>2]);
        float _S63 = as_type<float>((&kernelContext_0)->frameParameters_0[(16940U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S60, _S61, _S62, _S63);
        float _S64 = as_type<float>((&kernelContext_0)->frameParameters_0[(16944U)>>2]);
        float _S65 = as_type<float>((&kernelContext_0)->frameParameters_0[(16948U)>>2]);
        float _S66 = as_type<float>((&kernelContext_0)->frameParameters_0[(16952U)>>2]);
        float _S67 = as_type<float>((&kernelContext_0)->frameParameters_0[(16956U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S64, _S65, _S66, _S67);
        float _S68 = as_type<float>((&kernelContext_0)->frameParameters_0[(16960U)>>2]);
        float _S69 = as_type<float>((&kernelContext_0)->frameParameters_0[(16964U)>>2]);
        float _S70 = as_type<float>((&kernelContext_0)->frameParameters_0[(16968U)>>2]);
        float _S71 = as_type<float>((&kernelContext_0)->frameParameters_0[(16972U)>>2]);
        float4 _S72 = float4(_S68, _S69, _S70, _S71);
        float _S73 = as_type<float>((&kernelContext_0)->frameParameters_0[(16976U)>>2]);
        float _S74 = as_type<float>((&kernelContext_0)->frameParameters_0[(16980U)>>2]);
        float _S75 = as_type<float>((&kernelContext_0)->frameParameters_0[(16984U)>>2]);
        float _S76 = as_type<float>((&kernelContext_0)->frameParameters_0[(16988U)>>2]);
        float4 _S77 = float4(_S73, _S74, _S75, _S76);
        float _S78 = as_type<float>((&kernelContext_0)->frameParameters_0[(16992U)>>2]);
        float _S79 = as_type<float>((&kernelContext_0)->frameParameters_0[(16996U)>>2]);
        float _S80 = as_type<float>((&kernelContext_0)->frameParameters_0[(17000U)>>2]);
        float _S81 = as_type<float>((&kernelContext_0)->frameParameters_0[(17004U)>>2]);
        float4 _S82 = float4(_S78, _S79, _S80, _S81);
        float _S83 = as_type<float>((&kernelContext_0)->frameParameters_0[(17008U)>>2]);
        float _S84 = as_type<float>((&kernelContext_0)->frameParameters_0[(17012U)>>2]);
        float _S85 = as_type<float>((&kernelContext_0)->frameParameters_0[(17016U)>>2]);
        float _S86 = as_type<float>((&kernelContext_0)->frameParameters_0[(17020U)>>2]);
        float4 _S87 = float4(_S83, _S84, _S85, _S86);
        float _S88 = as_type<float>((&kernelContext_0)->frameParameters_0[(17024U)>>2]);
        float _S89 = as_type<float>((&kernelContext_0)->frameParameters_0[(17028U)>>2]);
        float _S90 = as_type<float>((&kernelContext_0)->frameParameters_0[(17032U)>>2]);
        float _S91 = as_type<float>((&kernelContext_0)->frameParameters_0[(17036U)>>2]);
        float4 _S92 = float4(_S88, _S89, _S90, _S91);
        float _S93 = as_type<float>((&kernelContext_0)->frameParameters_0[(17040U)>>2]);
        float _S94 = as_type<float>((&kernelContext_0)->frameParameters_0[(17044U)>>2]);
        float _S95 = as_type<float>((&kernelContext_0)->frameParameters_0[(17048U)>>2]);
        float _S96 = as_type<float>((&kernelContext_0)->frameParameters_0[(17052U)>>2]);
        float4 _S97 = float4(_S93, _S94, _S95, _S96);
        float _S98 = as_type<float>((&kernelContext_0)->frameParameters_0[(17056U)>>2]);
        float _S99 = as_type<float>((&kernelContext_0)->frameParameters_0[(17060U)>>2]);
        float _S100 = as_type<float>((&kernelContext_0)->frameParameters_0[(17064U)>>2]);
        float _S101 = as_type<float>((&kernelContext_0)->frameParameters_0[(17068U)>>2]);
        float4 _S102 = float4(_S98, _S99, _S100, _S101);
        float _S103 = as_type<float>((&kernelContext_0)->frameParameters_0[(17072U)>>2]);
        float _S104 = as_type<float>((&kernelContext_0)->frameParameters_0[(17076U)>>2]);
        float _S105 = as_type<float>((&kernelContext_0)->frameParameters_0[(17080U)>>2]);
        float _S106 = as_type<float>((&kernelContext_0)->frameParameters_0[(17084U)>>2]);
        float4 _S107 = float4(_S103, _S104, _S105, _S106);
        float _S108 = as_type<float>((&kernelContext_0)->frameParameters_0[(17088U)>>2]);
        float _S109 = as_type<float>((&kernelContext_0)->frameParameters_0[(17092U)>>2]);
        float _S110 = as_type<float>((&kernelContext_0)->frameParameters_0[(17096U)>>2]);
        float _S111 = as_type<float>((&kernelContext_0)->frameParameters_0[(17100U)>>2]);
        float4 _S112 = float4(_S108, _S109, _S110, _S111);
        float _S113 = as_type<float>((&kernelContext_0)->frameParameters_0[(17104U)>>2]);
        float _S114 = as_type<float>((&kernelContext_0)->frameParameters_0[(17108U)>>2]);
        float _S115 = as_type<float>((&kernelContext_0)->frameParameters_0[(17112U)>>2]);
        float _S116 = as_type<float>((&kernelContext_0)->frameParameters_0[(17116U)>>2]);
        float4 _S117 = float4(_S113, _S114, _S115, _S116);
        float _S118 = as_type<float>((&kernelContext_0)->frameParameters_0[(17120U)>>2]);
        float _S119 = as_type<float>((&kernelContext_0)->frameParameters_0[(17124U)>>2]);
        float _S120 = as_type<float>((&kernelContext_0)->frameParameters_0[(17128U)>>2]);
        float _S121 = as_type<float>((&kernelContext_0)->frameParameters_0[(17132U)>>2]);
        float4 _S122 = float4(_S118, _S119, _S120, _S121);
        float _S123 = as_type<float>((&kernelContext_0)->frameParameters_0[(17136U)>>2]);
        float _S124 = as_type<float>((&kernelContext_0)->frameParameters_0[(17140U)>>2]);
        float _S125 = as_type<float>((&kernelContext_0)->frameParameters_0[(17144U)>>2]);
        float _S126 = as_type<float>((&kernelContext_0)->frameParameters_0[(17148U)>>2]);
        float4 _S127 = float4(_S123, _S124, _S125, _S126);
        float _S128 = as_type<float>((&kernelContext_0)->frameParameters_0[(17152U)>>2]);
        float _S129 = as_type<float>((&kernelContext_0)->frameParameters_0[(17156U)>>2]);
        float _S130 = as_type<float>((&kernelContext_0)->frameParameters_0[(17160U)>>2]);
        float _S131 = as_type<float>((&kernelContext_0)->frameParameters_0[(17164U)>>2]);
        float4 _S132 = float4(_S128, _S129, _S130, _S131);
        float _S133 = as_type<float>((&kernelContext_0)->frameParameters_0[(17168U)>>2]);
        float _S134 = as_type<float>((&kernelContext_0)->frameParameters_0[(17172U)>>2]);
        float _S135 = as_type<float>((&kernelContext_0)->frameParameters_0[(17176U)>>2]);
        float _S136 = as_type<float>((&kernelContext_0)->frameParameters_0[(17180U)>>2]);
        float4 _S137 = float4(_S133, _S134, _S135, _S136);
        float _S138 = as_type<float>((&kernelContext_0)->frameParameters_0[(17184U)>>2]);
        float _S139 = as_type<float>((&kernelContext_0)->frameParameters_0[(17188U)>>2]);
        float _S140 = as_type<float>((&kernelContext_0)->frameParameters_0[(17192U)>>2]);
        float _S141 = as_type<float>((&kernelContext_0)->frameParameters_0[(17196U)>>2]);
        float4 _S142 = float4(_S138, _S139, _S140, _S141);
        float _S143 = as_type<float>((&kernelContext_0)->frameParameters_0[(17200U)>>2]);
        float _S144 = as_type<float>((&kernelContext_0)->frameParameters_0[(17204U)>>2]);
        float _S145 = as_type<float>((&kernelContext_0)->frameParameters_0[(17208U)>>2]);
        float _S146 = as_type<float>((&kernelContext_0)->frameParameters_0[(17212U)>>2]);
        float4 _S147 = float4(_S143, _S144, _S145, _S146);
        (&frame_0)->domeAmbient_0[int(0)] = _S72;
        (&frame_0)->domeAmbient_0[int(1)] = _S77;
        (&frame_0)->domeAmbient_0[int(2)] = _S82;
        (&frame_0)->domeAmbient_0[int(3)] = _S87;
        (&frame_0)->domeAmbient_0[int(4)] = _S92;
        (&frame_0)->domeAmbient_0[int(5)] = _S97;
        (&frame_0)->domeAmbient_0[int(6)] = _S102;
        (&frame_0)->domeAmbient_0[int(7)] = _S107;
        (&frame_0)->domeEnvironment_0[int(0)] = _S112;
        (&frame_0)->domeEnvironment_0[int(1)] = _S117;
        (&frame_0)->domeEnvironment_0[int(2)] = _S122;
        (&frame_0)->domeEnvironment_0[int(3)] = _S127;
        (&frame_0)->domeEnvironment_0[int(4)] = _S132;
        (&frame_0)->domeEnvironment_0[int(5)] = _S137;
        (&frame_0)->domeEnvironment_0[int(6)] = _S142;
        (&frame_0)->domeEnvironment_0[int(7)] = _S147;
        _S2 = frame_0;
        break;
    }
    for(;;)
    {
        _S3 = _S2.clipPlaneCount_0;
        uint _S148 = min(_S2.clipPlaneCount_0.x, 8U);
        index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S148)
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
    float4 _S149 = float4(_S8->clearcoatShaded_0) ;
    float shadedMode_0 = _S149.z;
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
    float4 _S150 = float4(_S8->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S150.xyz;
    float occlusion_0 = _S150.w;
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
    float4 _S152 = float4(_S8->reserved_0) ;
    if((_S152.x) >= 0.5f)
    {
        pixelOutput_0 _S153 = { float4(diffuseColor_0, saturate(opacity_0) * (1.0f - exp(- max(0.0f, max(0.0f, _S152.y) * max(0.0f, _S152.z))))) };
        return _S153;
    }
    float4 _S154 = float4(_S8->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S154.x);
    float roughness_0 = clamp(_S154.y, 0.00999999977648258f, 1.0f);
    float4 _S155 = float4(_S8->specularIor_0) ;
    float3 specularColor_0 = _S155.xyz;
    float ior_0 = _S155.w;
    float clearcoatAmount_0 = _S149.x;
    float clearcoatRoughness_0 = _S149.y;
    float4 _S156 = float4(_S8->textureControls_0) ;
    uint textureMask_0 = uint(round(_S156.x));
    uint udimMask_0 = uint(round(_S156.y));
    float4 _S157 = float4(_S8->uvTransformRow1_0) ;
    uint secondaryMask_0 = uint(round(_S157.w));
    float4 _S158 = float4(_S8->domeLinkControls_0) ;
    uint alternateMask_0 = uint(round(_S158.w));
    float4 _S159 = float4(_S8->uvTransformRow0_0) ;
    float2 _S160 = _S159.xy;
    float _S161 = _S159.z;
    float2 _S162 = _S157.xy;
    float _S163 = _S157.z;
    float2 _S164 = float2(dot(_S160, _S1.texCoord_0) + _S161, dot(_S162, _S1.texCoord_0) + _S163);
    float2 _S165 = float2(dot(_S160, _S1.texCoord1_0) + _S161, dot(_S162, _S1.texCoord1_0) + _S163);
    float4 _S166 = float4(_S8->alternateUvTransformRow0_0) ;
    float4 _S167 = float4(_S8->alternateUvTransformRow1_0) ;
    float2 _S168 = _S166.xy;
    float _S169 = _S166.z;
    float2 _S170 = _S167.xy;
    float _S171 = _S167.z;
    float2 _S172 = float2(dot(_S168, _S1.texCoord_0) + _S169, dot(_S170, _S1.texCoord_0) + _S171);
    float2 _S173 = float2(dot(_S168, _S1.texCoord1_0) + _S169, dot(_S170, _S1.texCoord1_0) + _S171);
    thread float4 baseNormalSample_0;
    bool useSecondary_0 = (secondaryMask_0 & 4U) != 0U;
    bool _S174 = (alternateMask_0 & 4U) != 0U;
    float2 detailSource_0;
    if(_S174)
    {
        if(useSecondary_0)
        {
            detailSource_0 = _S173;
        }
        else
        {
            detailSource_0 = _S172;
        }
    }
    else
    {
        if(useSecondary_0)
        {
            detailSource_0 = _S165;
        }
        else
        {
            detailSource_0 = _S164;
        }
    }
    bool _S175 = (udimMask_0 & 4U) != 0U;
    uint maskU_0 = uint(round(_S158.y));
    uint maskV_0 = uint(round(_S158.z));
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
    uint _S176 = index_0 | lightCount_0;
    for(;;)
    {
        if(!_S175)
        {
            for(;;)
            {
                if(_S176 == 0U)
                {
                    break;
                }
                texture2d<float, access::sample> _S177 = (&kernelContext_0)->normalTexture_0;
                thread uint width_0;
                thread uint height_0;
                (*((&width_0)) = (_S177).get_width(0)),(*((&height_0)) = (_S177).get_height(0));
                float2 size_0 = float2(float(width_0), float(height_0));
                float2 _S178 = float2(0.5f) ;
                float2 remapped_0 = clamp((detailSource_0 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S178 / size_0, (size_0 - _S178) / size_0);
                if((_S176 & 1U) != 0U)
                {
                    lightType_0 = remapped_0.x;
                }
                else
                {
                    lightType_0 = detailSource_0.x;
                }
                if((_S176 & 2U) != 0U)
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
        texture2d<float, access::sample> _S179 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_0;
        thread uint atlasHeight_0;
        (*((&atlasWidth_0)) = (_S179).get_width(0)),(*((&atlasHeight_0)) = (_S179).get_height(0));
        int3 _S180 = int3(int(0), int(0), int(0));
        float4 metadata_0 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S180)).xy), uint(((_S180)).z))) * float4(255.0f) );
        if((metadata_0.x) == 255.0f)
        {
            uint _S181 = atlasWidth_0;
            uint _S182 = 2U % atlasWidth_0;
            int _S183 = int(_S182);
            uint _S184 = 2U / _S181;
            int3 _S185 = int3(_S183, int(_S184), int(0));
            int _S186 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S185)).xy), uint(((_S185)).z))).x * 255.0f)));
            uint _S187 = atlasWidth_0;
            uint _S188 = 3U % atlasWidth_0;
            int _S189 = int(_S188);
            uint _S190 = 3U / _S187;
            int3 _S191 = int3(_S189, int(_S190), int(0));
            int2 minimumTile_0 = int2(_S186, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S191)).xy), uint(((_S191)).z))).x * 255.0f))));
            uint _S192 = atlasWidth_0;
            uint _S193 = 4U % atlasWidth_0;
            int _S194 = int(_S193);
            uint _S195 = 4U / _S192;
            int3 _S196 = int3(_S194, int(_S195), int(0));
            uint _S197 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S196)).xy), uint(((_S196)).z))).x * 255.0f));
            uint _S198 = atlasWidth_0;
            uint _S199 = 5U % atlasWidth_0;
            int _S200 = int(_S199);
            uint _S201 = 5U / _S198;
            int3 _S202 = int3(_S200, int(_S201), int(0));
            uint _S203 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S202)).xy), uint(((_S202)).z))).x * 255.0f));
            uint2 grid_0 = uint2(_S197, _S203);
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
                int3 _S204 = int3(int(1), int(0), int(0));
                _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S204)).xy), uint(((_S204)).z)));
                break;
            }
            uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S197 + uint(tile_0.x));
            uint _S205 = atlasWidth_0;
            uint _S206 = offset_0 % atlasWidth_0;
            int _S207 = int(_S206);
            uint _S208 = offset_0 / _S205;
            int3 _S209 = int3(_S207, int(_S208), int(0));
            uint _S210 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S209)).xy), uint(((_S209)).z))).x * 255.0f));
            uint _S211 = atlasWidth_0;
            uint _S212 = offset_0 + 1U;
            uint _S213 = _S212 % atlasWidth_0;
            int _S214 = int(_S213);
            uint _S215 = _S212 / _S211;
            int3 _S216 = int3(_S214, int(_S215), int(0));
            uint slot_0 = _S210 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S216)).xy), uint(((_S216)).z))).x * 255.0f)) << 8U);
            if(slot_0 == 0U)
            {
                int3 _S217 = int3(int(1), int(0), int(0));
                _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S217)).xy), uint(((_S217)).z)));
                break;
            }
            uint slot_1 = slot_0 - 1U;
            uint _S218 = atlasWidth_0;
            uint _S219 = 6U % atlasWidth_0;
            int _S220 = int(_S219);
            uint _S221 = 6U / _S218;
            int3 _S222 = int3(_S220, int(_S221), int(0));
            uint _S223 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S222)).xy), uint(((_S222)).z))).x * 255.0f));
            uint _S224 = atlasWidth_0;
            uint _S225 = 7U % atlasWidth_0;
            int _S226 = int(_S225);
            uint _S227 = 7U / _S224;
            int3 _S228 = int3(_S226, int(_S227), int(0));
            uint columns_0 = _S223 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S228)).xy), uint(((_S228)).z))).x * 255.0f)) << 8U);
            uint _S229 = atlasWidth_0;
            uint _S230 = 8U % atlasWidth_0;
            int _S231 = int(_S230);
            uint _S232 = 8U / _S229;
            int3 _S233 = int3(_S231, int(_S232), int(0));
            uint _S234 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S233)).xy), uint(((_S233)).z))).x * 255.0f));
            uint _S235 = atlasWidth_0;
            uint _S236 = 9U % atlasWidth_0;
            int _S237 = int(_S236);
            uint _S238 = 9U / _S235;
            int3 _S239 = int3(_S237, int(_S238), int(0));
            uint rows_0 = ((_S234 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S239)).xy), uint(((_S239)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
            uint metadataRows_0 = (10U + 2U * _S197 * _S203 + atlasWidth_0 - 1U) / atlasWidth_0;
            uint _S240 = atlasWidth_0 / columns_0;
            float _S241 = float(_S240);
            uint _S242 = (atlasHeight_0 - metadataRows_0) / rows_0;
            float2 cellSize_0 = float2(_S241, float(_S242));
            uint _S243 = slot_1 % columns_0;
            float _S244 = float(_S243);
            uint _S245 = slot_1 / columns_0;
            _S5 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S244, float(_S245)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(detailSource_0) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        int2 _S246 = int2(metadata_0.zw);
        int2 tile_1 = int2(floor(detailSource_0)) - int2(metadata_0.xy);
        if(any(tile_1 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_1 >= _S246);
        }
        if(hasSceneLighting_0)
        {
            int3 _S247 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
            _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S247)).xy), uint(((_S247)).z)));
            break;
        }
        uint _S248 = atlasWidth_0 / uint(_S246.x);
        float _S249 = float(_S248);
        uint _S250 = (atlasHeight_0 - 1U) / uint(_S246.y);
        float2 cellSize_1 = float2(_S249, float(_S250));
        _S5 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
        break;
    }
    baseNormalSample_0 = _S5;
    float4 _S251 = float4(_S8->detailUvTransformRow0_0) ;
    uint3 _S252;
    if((_S251.w) != 0.0f)
    {
        float4 _S253 = baseNormalSample_0;
        for(;;)
        {
            float _S254 = _S253.w;
            if(_S254 > 1.0f)
            {
                unlitColor_0 = _S253.xyz;
                break;
            }
            texture2d<float, access::sample> _S255 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_1;
            thread uint height_1;
            (*((&width_1)) = (_S255).get_width(0)),(*((&height_1)) = (_S255).get_height(0));
            uint3 _S256 = uint3(round(_S253.xyz * float3(255.0f) ));
            uint index_1 = ((_S256.x) | ((_S256.y) << 8U)) | ((_S256.z) << 16U);
            if(_S254 == 0.0f)
            {
                _S252 = _S256;
            }
            else
            {
                _S252 = uint3(index_1, index_1, index_1);
            }
            uint3 address_0 = _S252 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S257 = address_0.x;
            uint _S258 = _S257 % width_1;
            int _S259 = int(_S258);
            uint _S260 = _S257 / width_1;
            int3 _S261 = int3(_S259, int(_S260), int(0));
            float _S262 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S261)).xy), uint(((_S261)).z))).x;
            uint _S263 = address_0.y;
            uint _S264 = _S263 % width_1;
            int _S265 = int(_S264);
            uint _S266 = _S263 / width_1;
            int3 _S267 = int3(_S265, int(_S266), int(0));
            float _S268 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S267)).xy), uint(((_S267)).z))).x;
            uint _S269 = address_0.z;
            uint _S270 = _S269 % width_1;
            int _S271 = int(_S270);
            uint _S272 = _S269 / width_1;
            int3 _S273 = int3(_S271, int(_S272), int(0));
            unlitColor_0 = float3(_S262, _S268, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S273)).xy), uint(((_S273)).z))).x);
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
    if(_S174)
    {
        detailSource_0 = _S168;
    }
    else
    {
        detailSource_0 = _S160;
    }
    float2 tangent_1;
    if(_S174)
    {
        tangent_1 = _S170;
    }
    else
    {
        tangent_1 = _S162;
    }
    thread float3 _S274 = unlitColor_0;
    float2 _S275 = float2(0.0f, 0.0f);
    if(any(tangent_1 != _S275))
    {
        float _S276 = tangent_1.x;
        float _S277 = tangent_1.y;
        float2 tangent_2 = normalize(float2(_S277, - _S276) / float2(max(abs(_S276), abs(_S277))) );
        float2 bitangent_0 = float2(- tangent_2.y, tangent_2.x);
        if((dot(detailSource_0, tangent_2)) < 0.0f)
        {
            detailSource_0 = - tangent_2;
        }
        else
        {
            detailSource_0 = tangent_2;
        }
        _S274.xy = detailSource_0 * float2(_S274.x)  + bitangent_0 * float2(_S274.y) ;
    }
    float3 _S278 = _S274;
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
        float2 _S279 = _S251.xy;
        float _S280 = dot(_S279, detailSource_0) + _S251.z;
        float4 _S281 = float4(_S8->detailUvTransformRow1_0) ;
        float2 _S282 = _S281.xy;
        float _S283 = dot(_S282, detailSource_0) + _S281.z;
        float2 detailUv_0 = float2(_S280, _S283);
        thread float4 detailSample_0;
        bool _S284 = (udimMask_0 & 4096U) != 0U;
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
        uint _S285 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S284)
            {
                for(;;)
                {
                    if(_S285 == 0U)
                    {
                        detailSource_0 = detailUv_0;
                        break;
                    }
                    texture2d<float, access::sample> _S286 = (&kernelContext_0)->detailNormalTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S286).get_width(0)),(*((&height_2)) = (_S286).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S287 = float2(0.5f) ;
                    float2 remapped_1 = clamp((detailUv_0 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S287 / size_1, (size_1 - _S287) / size_1);
                    if((_S285 & 1U) != 0U)
                    {
                        lightType_0 = remapped_1.x;
                    }
                    else
                    {
                        lightType_0 = _S280;
                    }
                    if((_S285 & 2U) != 0U)
                    {
                        _S4 = remapped_1.y;
                    }
                    else
                    {
                        _S4 = _S283;
                    }
                    detailSource_0 = float2(lightType_0, _S4);
                    break;
                }
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S288 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_1;
            thread uint atlasHeight_1;
            (*((&atlasWidth_1)) = (_S288).get_width(0)),(*((&atlasHeight_1)) = (_S288).get_height(0));
            int3 _S289 = int3(int(0), int(0), int(0));
            float4 metadata_1 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S289)).xy), uint(((_S289)).z))) * float4(255.0f) );
            if((metadata_1.x) == 255.0f)
            {
                uint _S290 = atlasWidth_1;
                uint _S291 = 2U % atlasWidth_1;
                int _S292 = int(_S291);
                uint _S293 = 2U / _S290;
                int3 _S294 = int3(_S292, int(_S293), int(0));
                int _S295 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S294)).xy), uint(((_S294)).z))).x * 255.0f)));
                uint _S296 = atlasWidth_1;
                uint _S297 = 3U % atlasWidth_1;
                int _S298 = int(_S297);
                uint _S299 = 3U / _S296;
                int3 _S300 = int3(_S298, int(_S299), int(0));
                int2 minimumTile_1 = int2(_S295, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S300)).xy), uint(((_S300)).z))).x * 255.0f))));
                uint _S301 = atlasWidth_1;
                uint _S302 = 4U % atlasWidth_1;
                int _S303 = int(_S302);
                uint _S304 = 4U / _S301;
                int3 _S305 = int3(_S303, int(_S304), int(0));
                uint _S306 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S305)).xy), uint(((_S305)).z))).x * 255.0f));
                uint _S307 = atlasWidth_1;
                uint _S308 = 5U % atlasWidth_1;
                int _S309 = int(_S308);
                uint _S310 = 5U / _S307;
                int3 _S311 = int3(_S309, int(_S310), int(0));
                uint _S312 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S311)).xy), uint(((_S311)).z))).x * 255.0f));
                uint2 grid_1 = uint2(_S306, _S312);
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
                    int3 _S313 = int3(int(1), int(0), int(0));
                    _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S313)).xy), uint(((_S313)).z)));
                    break;
                }
                uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S306 + uint(tile_2.x));
                uint _S314 = atlasWidth_1;
                uint _S315 = offset_1 % atlasWidth_1;
                int _S316 = int(_S315);
                uint _S317 = offset_1 / _S314;
                int3 _S318 = int3(_S316, int(_S317), int(0));
                uint _S319 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S318)).xy), uint(((_S318)).z))).x * 255.0f));
                uint _S320 = atlasWidth_1;
                uint _S321 = offset_1 + 1U;
                uint _S322 = _S321 % atlasWidth_1;
                int _S323 = int(_S322);
                uint _S324 = _S321 / _S320;
                int3 _S325 = int3(_S323, int(_S324), int(0));
                uint slot_2 = _S319 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S325)).xy), uint(((_S325)).z))).x * 255.0f)) << 8U);
                if(slot_2 == 0U)
                {
                    int3 _S326 = int3(int(1), int(0), int(0));
                    _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S326)).xy), uint(((_S326)).z)));
                    break;
                }
                uint slot_3 = slot_2 - 1U;
                uint _S327 = atlasWidth_1;
                uint _S328 = 6U % atlasWidth_1;
                int _S329 = int(_S328);
                uint _S330 = 6U / _S327;
                int3 _S331 = int3(_S329, int(_S330), int(0));
                uint _S332 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S331)).xy), uint(((_S331)).z))).x * 255.0f));
                uint _S333 = atlasWidth_1;
                uint _S334 = 7U % atlasWidth_1;
                int _S335 = int(_S334);
                uint _S336 = 7U / _S333;
                int3 _S337 = int3(_S335, int(_S336), int(0));
                uint columns_1 = _S332 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S337)).xy), uint(((_S337)).z))).x * 255.0f)) << 8U);
                uint _S338 = atlasWidth_1;
                uint _S339 = 8U % atlasWidth_1;
                int _S340 = int(_S339);
                uint _S341 = 8U / _S338;
                int3 _S342 = int3(_S340, int(_S341), int(0));
                uint _S343 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S342)).xy), uint(((_S342)).z))).x * 255.0f));
                uint _S344 = atlasWidth_1;
                uint _S345 = 9U % atlasWidth_1;
                int _S346 = int(_S345);
                uint _S347 = 9U / _S344;
                int3 _S348 = int3(_S346, int(_S347), int(0));
                uint rows_1 = ((_S343 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S348)).xy), uint(((_S348)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                uint metadataRows_1 = (10U + 2U * _S306 * _S312 + atlasWidth_1 - 1U) / atlasWidth_1;
                uint _S349 = atlasWidth_1 / columns_1;
                float _S350 = float(_S349);
                uint _S351 = (atlasHeight_1 - metadataRows_1) / rows_1;
                float2 cellSize_2 = float2(_S350, float(_S351));
                uint _S352 = slot_3 % columns_1;
                float _S353 = float(_S352);
                uint _S354 = slot_3 / columns_1;
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S353, float(_S354)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailUv_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            int2 _S355 = int2(metadata_1.zw);
            int2 tile_3 = int2(floor(detailUv_0)) - int2(metadata_1.xy);
            if(any(tile_3 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_3 >= _S355);
            }
            if(hasSceneLighting_0)
            {
                int3 _S356 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S356)).xy), uint(((_S356)).z)));
                break;
            }
            uint _S357 = atlasWidth_1 / uint(_S355.x);
            float _S358 = float(_S357);
            uint _S359 = (atlasHeight_1 - 1U) / uint(_S355.y);
            float2 cellSize_3 = float2(_S358, float(_S359));
            _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
            break;
        }
        detailSample_0 = _S5;
        if((_S281.w) != 0.0f)
        {
            float4 _S360 = detailSample_0;
            for(;;)
            {
                float _S361 = _S360.w;
                if(_S361 > 1.0f)
                {
                    unlitColor_0 = _S360.xyz;
                    break;
                }
                texture2d<float, access::sample> _S362 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_3;
                thread uint height_3;
                (*((&width_3)) = (_S362).get_width(0)),(*((&height_3)) = (_S362).get_height(0));
                uint3 _S363 = uint3(round(_S360.xyz * float3(255.0f) ));
                uint index_2 = ((_S363.x) | ((_S363.y) << 8U)) | ((_S363.z) << 16U);
                if(_S361 == 0.0f)
                {
                    _S252 = _S363;
                }
                else
                {
                    _S252 = uint3(index_2, index_2, index_2);
                }
                uint3 address_1 = _S252 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S364 = address_1.x;
                uint _S365 = _S364 % width_3;
                int _S366 = int(_S365);
                uint _S367 = _S364 / width_3;
                int3 _S368 = int3(_S366, int(_S367), int(0));
                float _S369 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S368)).xy), uint(((_S368)).z))).x;
                uint _S370 = address_1.y;
                uint _S371 = _S370 % width_3;
                int _S372 = int(_S371);
                uint _S373 = _S370 / width_3;
                int3 _S374 = int3(_S372, int(_S373), int(0));
                float _S375 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S374)).xy), uint(((_S374)).z))).x;
                uint _S376 = address_1.z;
                uint _S377 = _S376 % width_3;
                int _S378 = int(_S377);
                uint _S379 = _S376 / width_3;
                int3 _S380 = int3(_S378, int(_S379), int(0));
                unlitColor_0 = float3(_S369, _S375, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S380)).xy), uint(((_S380)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S381 = detail_0;
        if(any(_S282 != _S275))
        {
            float _S382 = _S282.x;
            float _S383 = _S282.y;
            float2 tangent_3 = normalize(float2(_S383, - _S382) / float2(max(abs(_S382), abs(_S383))) );
            float2 bitangent_1 = float2(- tangent_3.y, tangent_3.x);
            if((dot(_S279, tangent_3)) < 0.0f)
            {
                tangent_1 = - tangent_3;
            }
            else
            {
                tangent_1 = tangent_3;
            }
            _S381.xy = tangent_1 * float2(_S381.x)  + bitangent_1 * float2(_S381.y) ;
        }
        float3 _S384 = _S381;
        for(;;)
        {
            float3 t_0 = normalize(_S278) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S384) * float3(-1.0f, -1.0f, 1.0f);
            float _S385 = t_0.z;
            if(_S385 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S385))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S278;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S386 = float3(unlitColor_0.x) ;
    float3 _S387 = float3(unlitColor_0.y) ;
    float3 _S388 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S386 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S387 + _S1.normal_0 * _S388);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S386 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S387 + worldNormalBasis_0 * _S388);
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
    float _S389 = max(0.00100000004749745f, roughness_0);
    float _S390 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S391 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S391;
    bool omniPbrSpecular_0 = (_S156.w) >= 0.5f;
    float dielectricLevel_0 = _S149.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S392 = float3(dielectricLevel_0) ;
        float3 _S393 = float3(metallic_0) ;
        float3 _S394 = mix(_S392, diffuseColor_0, _S393);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S392, diffuseColor_0, _S393);
        grazingIncidence_0 = _S394;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S154.w) >= 0.5f)
        {
            float3 _S395 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S395;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S396 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S396);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S396);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S397 = _S2;
    float _S398 = _S2.ambientLight_0.w;
    if(_S398 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S399 = _S397.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S399, _S399)) > 0.0f;
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
    uint _S400 = min(uint(_S398), 128U);
    float3 sceneEye_0 = normalize((((float4(unlitColor_0, 0.0f)) * (_S2.eyeToWorld_0))).xyz);
    float3 worldPosition_0 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S2.eyeToWorld_0))).xyz;
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
    float4 _S401 = float4(_S8->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S401.w) ;
    uint domeLinkMask_0 = uint(max(_S158.x, 0.0f));
    FragmentFrameParameters_0 _S402 = _S2;
    uint _S403 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S403) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S404 = _S397.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S405 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S405;
        for(;;)
        {
            if(lightCount_0 < _S403)
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
        domeAmbient_1 = _S404;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S406 = !hasSceneLighting_0;
    if(_S406)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S400;
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
        bool _S407;
        if(hasSceneLighting_0)
        {
            _S407 = (((uint4(_S8->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S407 = false;
        }
        if(_S407)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S408 = lightIndex_0 == 0U;
        bool _S409;
        if(_S408)
        {
            _S409 = _S406;
        }
        else
        {
            _S409 = false;
        }
        if(_S409)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S410 = 224U + lightIndex_0 * 16U;
            float _S411 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410)>>2]);
            float _S412 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410 + 4U)>>2]);
            float _S413 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410 + 8U)>>2]);
            float _S414 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S410 + 12U)>>2]);
            lightType_0 = _S414;
        }
        bool _S415;
        if(_S408)
        {
            _S415 = _S406;
        }
        else
        {
            _S415 = false;
        }
        if(_S415)
        {
            lightDirectionAxis_0 = normalize((float4(_S8->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S416 = 2272U + lightIndex_0 * 16U;
            float _S417 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416)>>2]);
            float _S418 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416 + 4U)>>2]);
            float _S419 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416 + 8U)>>2]);
            float _S420 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416 + 12U)>>2]);
            lightDirectionAxis_0 = float3(_S417, _S418, _S419);
        }
        float3 lightDirection_0 = normalize(lightDirectionAxis_0);
        bool _S421;
        if(_S408)
        {
            _S421 = _S406;
        }
        else
        {
            _S421 = false;
        }
        if(_S421)
        {
            _S4 = (float4(_S8->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S422 = 4320U + lightIndex_0 * 16U;
            float _S423 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S422)>>2]);
            float _S424 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S422 + 4U)>>2]);
            float _S425 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S422 + 8U)>>2]);
            float _S426 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S422 + 12U)>>2]);
            _S4 = _S426;
        }
        bool _S427;
        if(_S408)
        {
            _S427 = _S406;
        }
        else
        {
            _S427 = false;
        }
        if(_S427)
        {
            diffuseColor_0 = _S401.xyz;
        }
        else
        {
            uint _S428 = 4320U + lightIndex_0 * 16U;
            float _S429 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S428)>>2]);
            float _S430 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S428 + 4U)>>2]);
            float _S431 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S428 + 8U)>>2]);
            float _S432 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S428 + 12U)>>2]);
            diffuseColor_0 = float3(_S429, _S430, _S431);
        }
        bool _S433;
        if(_S408)
        {
            _S433 = _S406;
        }
        else
        {
            _S433 = false;
        }
        float _S434;
        if(_S433)
        {
            _S434 = 1.0f;
        }
        else
        {
            uint _S435 = 6368U + lightIndex_0 * 16U;
            float _S436 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435)>>2]);
            float _S437 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435 + 4U)>>2]);
            float _S438 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435 + 8U)>>2]);
            float _S439 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435 + 12U)>>2]);
            _S434 = _S436;
        }
        bool _S440;
        if(_S408)
        {
            _S440 = _S406;
        }
        else
        {
            _S440 = false;
        }
        float _S441;
        if(_S440)
        {
            _S441 = 1.0f;
        }
        else
        {
            uint _S442 = 6368U + lightIndex_0 * 16U;
            float _S443 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S442)>>2]);
            float _S444 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S442 + 4U)>>2]);
            float _S445 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S442 + 8U)>>2]);
            float _S446 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S442 + 12U)>>2]);
            _S441 = _S444;
        }
        bool _S447;
        if(_S408)
        {
            _S447 = _S406;
        }
        else
        {
            _S447 = false;
        }
        if(_S447)
        {
            lightTangentAxis_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S448 = 8416U + lightIndex_0 * 16U;
            float _S449 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S448)>>2]);
            float _S450 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S448 + 4U)>>2]);
            float _S451 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S448 + 8U)>>2]);
            float _S452 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S448 + 12U)>>2]);
            lightTangentAxis_0 = float3(_S449, _S450, _S451);
        }
        bool _S453;
        if(_S408)
        {
            _S453 = _S406;
        }
        else
        {
            _S453 = false;
        }
        if(_S453)
        {
            lightBitangentAxis_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S454 = 10464U + lightIndex_0 * 16U;
            float _S455 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S454)>>2]);
            float _S456 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S454 + 4U)>>2]);
            float _S457 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S454 + 8U)>>2]);
            float _S458 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S454 + 12U)>>2]);
            lightBitangentAxis_0 = float3(_S455, _S456, _S457);
        }
        bool _S459;
        if(_S408)
        {
            _S459 = _S406;
        }
        else
        {
            _S459 = false;
        }
        float shapeX_0;
        if(_S459)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S460 = 8416U + lightIndex_0 * 16U;
            float _S461 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S460)>>2]);
            float _S462 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S460 + 4U)>>2]);
            float _S463 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S460 + 8U)>>2]);
            float _S464 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S460 + 12U)>>2]);
            shapeX_0 = _S464;
        }
        bool _S465;
        if(_S408)
        {
            _S465 = _S406;
        }
        else
        {
            _S465 = false;
        }
        float shapeY_0;
        if(_S465)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S466 = 10464U + lightIndex_0 * 16U;
            float _S467 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S466)>>2]);
            float _S468 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S466 + 4U)>>2]);
            float _S469 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S466 + 8U)>>2]);
            float _S470 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S466 + 12U)>>2]);
            shapeY_0 = _S470;
        }
        bool _S471;
        if(_S408)
        {
            _S471 = _S406;
        }
        else
        {
            _S471 = false;
        }
        float lightRadius_0;
        if(_S471)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S472 = 2272U + lightIndex_0 * 16U;
            float _S473 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S472)>>2]);
            float _S474 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S472 + 4U)>>2]);
            float _S475 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S472 + 8U)>>2]);
            float _S476 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S472 + 12U)>>2]);
            lightRadius_0 = _S476;
        }
        bool _S477;
        if(_S408)
        {
            _S477 = _S406;
        }
        else
        {
            _S477 = false;
        }
        float3 lightEyeBase_0;
        if(_S477)
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
        float _S478 = saturate(abs(dot(directNormal_0, lightEyeBase_0)) + 0.00000999999974738f);
        bool _S479;
        if(_S408)
        {
            _S479 = _S406;
        }
        else
        {
            _S479 = false;
        }
        if(_S479)
        {
            _S5 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S480 = 17216U + lightIndex_0 * 16U;
            float _S481 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S480)>>2]);
            float _S482 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S480 + 4U)>>2]);
            float _S483 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S480 + 8U)>>2]);
            float _S484 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S480 + 12U)>>2]);
            _S5 = float4(_S481, _S482, _S483, _S484);
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S485 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S485;
        sampleOffsets_0[int(1)] = _S485;
        sampleOffsets_0[int(2)] = _S485;
        sampleOffsets_0[int(3)] = _S485;
        sampleOffsets_0[int(4)] = _S485;
        thread array<float3, int(5)> sampleEmitterNormals_0;
        float3 _S486 = - lightDirection_0;
        sampleEmitterNormals_0[int(0)] = _S486;
        sampleEmitterNormals_0[int(1)] = _S486;
        sampleEmitterNormals_0[int(2)] = _S486;
        sampleEmitterNormals_0[int(3)] = _S486;
        sampleEmitterNormals_0[int(4)] = _S486;
        bool _S487 = lightType_0 == 2.0f;
        bool _S488;
        if(_S487)
        {
            _S488 = lightRadius_0 > 0.0f;
        }
        else
        {
            _S488 = false;
        }
        float sampleCount_0;
        float shadowVisibility_0;
        float sampleIntensity_0;
        float emissionScale_0;
        float sampleShadowVisibility_0;
        bool useEmitterShadowSamples_0;
        if(_S488)
        {
            sampleOffsets_0[int(0)] = - lightDirectionAxis_0 * float3(lightRadius_0) ;
            sampleOffsets_0[int(1)] = lightTangentAxis_0 * float3(lightRadius_0) ;
            sampleOffsets_0[int(2)] = - lightTangentAxis_0 * float3(lightRadius_0) ;
            sampleOffsets_0[int(3)] = lightBitangentAxis_0 * float3(lightRadius_0) ;
            sampleOffsets_0[int(4)] = - lightBitangentAxis_0 * float3(lightRadius_0) ;
            float3 x_0 = normalize(lightTangentAxis_0);
            float3 y_0 = normalize(lightBitangentAxis_0);
            float3 _S489 = cross(y_0, lightDirection_0);
            bool _S490 = (dot(x_0, _S489)) < 0.0f;
            if(_S490)
            {
                sampleCount_0 = -1.0f;
            }
            else
            {
                sampleCount_0 = 1.0f;
            }
            bool _S491 = (abs(0.0f)) > 0.5f;
            float3 _S492 = float3(0.0f) ;
            float3 _S493 = _S492 * _S489;
            float3 _S494 = cross(lightDirection_0, x_0);
            float3 _S495 = _S492 * _S494;
            bool _S496 = (abs(-1.0f)) > 0.5f;
            float3 _S497 = cross(x_0, y_0);
            float3 _S498 = float3(-1.0f) ;
            float3 _S499 = normalize((select(_S485, _S493, _S491) + select(_S485, _S495, _S491) + select(_S485, _S498 * _S497, _S496)) * float3(sampleCount_0) );
            sampleEmitterNormals_0[int(0)] = _S499;
            if(_S490)
            {
                shadowVisibility_0 = -1.0f;
            }
            else
            {
                shadowVisibility_0 = 1.0f;
            }
            bool _S500 = (abs(1.0f)) > 0.5f;
            float3 _S501 = _S492 * _S497;
            float3 _S502 = normalize((select(_S485, _S489, _S500) + select(_S485, _S495, _S491) + select(_S485, _S501, _S491)) * float3(shadowVisibility_0) );
            sampleEmitterNormals_0[int(1)] = _S502;
            if(_S490)
            {
                sampleIntensity_0 = -1.0f;
            }
            else
            {
                sampleIntensity_0 = 1.0f;
            }
            float3 _S503 = normalize((select(_S485, _S498 * _S489, _S496) + select(_S485, _S495, _S491) + select(_S485, _S501, _S491)) * float3(sampleIntensity_0) );
            sampleEmitterNormals_0[int(2)] = _S503;
            if(_S490)
            {
                emissionScale_0 = -1.0f;
            }
            else
            {
                emissionScale_0 = 1.0f;
            }
            float3 _S504 = normalize((select(_S485, _S493, _S491) + select(_S485, _S494, _S500) + select(_S485, _S501, _S491)) * float3(emissionScale_0) );
            sampleEmitterNormals_0[int(3)] = _S504;
            if(_S490)
            {
                sampleShadowVisibility_0 = -1.0f;
            }
            else
            {
                sampleShadowVisibility_0 = 1.0f;
            }
            float3 _S505 = normalize((select(_S485, _S493, _S491) + select(_S485, _S498 * _S494, _S496) + select(_S485, _S501, _S491)) * float3(sampleShadowVisibility_0) );
            sampleEmitterNormals_0[int(4)] = _S505;
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
                float3 _S506 = - halfWidth_0;
                sampleOffsets_0[int(3)] = _S506 + halfHeight_0;
                sampleOffsets_0[int(4)] = _S506 - halfHeight_0;
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
                    float3 x_1 = normalize(lightTangentAxis_0);
                    float3 y_1 = normalize(lightBitangentAxis_0);
                    float3 _S507 = cross(y_1, lightDirection_0);
                    if((dot(x_1, _S507)) < 0.0f)
                    {
                        sampleCount_0 = -1.0f;
                    }
                    else
                    {
                        sampleCount_0 = 1.0f;
                    }
                    bool _S508 = (abs(0.0f)) > 0.5f;
                    float3 _S509 = float3(0.0f) ;
                    float3 _S510 = normalize((select(_S485, _S509 * _S507, _S508) + select(_S485, _S509 * cross(lightDirection_0, x_1), _S508) + select(_S485, float3(-1.0f)  * cross(x_1, y_1), (abs(-1.0f)) > 0.5f)) * float3(sampleCount_0) );
                    sampleEmitterNormals_0[int(0)] = _S510;
                    sampleEmitterNormals_0[int(1)] = _S510;
                    sampleEmitterNormals_0[int(2)] = _S510;
                    sampleEmitterNormals_0[int(3)] = _S510;
                    sampleEmitterNormals_0[int(4)] = _S510;
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
                            float3 _S511 = lightTangentAxis_0 * float3(lightRadius_0) ;
                            sampleOffsets_0[int(0)] = _S511 - halfLength_0;
                            sampleOffsets_0[int(1)] = _S511 + halfLength_0;
                            float3 _S512 = - lightTangentAxis_0 * float3(lightRadius_0) ;
                            sampleOffsets_0[int(2)] = _S512 - halfLength_0;
                            sampleOffsets_0[int(3)] = _S512 + halfLength_0;
                            sampleOffsets_0[int(4)] = lightBitangentAxis_0 * float3(lightRadius_0) ;
                            float3 x_2 = normalize(lightTangentAxis_0);
                            float3 y_2 = normalize(lightBitangentAxis_0);
                            float3 _S513 = cross(y_2, lightDirection_0);
                            bool _S514 = (dot(x_2, _S513)) < 0.0f;
                            if(_S514)
                            {
                                sampleCount_0 = -1.0f;
                            }
                            else
                            {
                                sampleCount_0 = 1.0f;
                            }
                            bool _S515 = (abs(1.0f)) > 0.5f;
                            bool _S516 = (abs(0.0f)) > 0.5f;
                            float3 _S517 = cross(lightDirection_0, x_2);
                            float3 _S518 = float3(0.0f) ;
                            float3 _S519 = _S518 * _S517;
                            float3 _S520 = _S518 * cross(x_2, y_2);
                            float3 _S521 = normalize((select(_S485, _S513, _S515) + select(_S485, _S519, _S516) + select(_S485, _S520, _S516)) * float3(sampleCount_0) );
                            sampleEmitterNormals_0[int(0)] = _S521;
                            sampleEmitterNormals_0[int(1)] = _S521;
                            if(_S514)
                            {
                                shadowVisibility_0 = -1.0f;
                            }
                            else
                            {
                                shadowVisibility_0 = 1.0f;
                            }
                            float3 _S522 = normalize((select(_S485, float3(-1.0f)  * _S513, (abs(-1.0f)) > 0.5f) + select(_S485, _S519, _S516) + select(_S485, _S520, _S516)) * float3(shadowVisibility_0) );
                            sampleEmitterNormals_0[int(2)] = _S522;
                            sampleEmitterNormals_0[int(3)] = _S522;
                            if(_S514)
                            {
                                sampleIntensity_0 = -1.0f;
                            }
                            else
                            {
                                sampleIntensity_0 = 1.0f;
                            }
                            float3 _S523 = normalize((select(_S485, _S518 * _S513, _S516) + select(_S485, _S517, _S515) + select(_S485, _S520, _S516)) * float3(sampleIntensity_0) );
                            sampleEmitterNormals_0[int(4)] = _S523;
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
        bool _S524;
        bool _S525;
        bool _S526;
        if(hasSceneLighting_0)
        {
            uint _S527 = lightIndex_0 * 16U;
            uint _S528 = 14880U + _S527;
            float _S529 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S528)>>2]);
            float _S530 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S528 + 4U)>>2]);
            float _S531 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S528 + 8U)>>2]);
            float _S532 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S528 + 12U)>>2]);
            int shadowSlot_0 = int(_S529);
            if(shadowSlot_0 >= int(0))
            {
                int shadowSlot_1;
                int y_3;
                if(_S530 == 6.0f)
                {
                    shadowVisibility_0 = -1.0f;
                    shadowSlot_1 = shadowSlot_0;
                    y_3 = int(0);
                    for(;;)
                    {
                        if(y_3 < int(6))
                        {
                        }
                        else
                        {
                            break;
                        }
                        int _S533 = shadowSlot_0 + y_3;
                        uint _S534 = 12576U + uint(_S533) * 64U;
                        float _S535 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534)>>2]);
                        float _S536 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 4U)>>2]);
                        float _S537 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 8U)>>2]);
                        float _S538 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 12U)>>2]);
                        float4 _S539 = float4(_S535, _S536, _S537, _S538);
                        float _S540 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 16U)>>2]);
                        float _S541 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 20U)>>2]);
                        float _S542 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 24U)>>2]);
                        float _S543 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 28U)>>2]);
                        float4 _S544 = float4(_S540, _S541, _S542, _S543);
                        float _S545 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 32U)>>2]);
                        float _S546 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 36U)>>2]);
                        float _S547 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 40U)>>2]);
                        float _S548 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 44U)>>2]);
                        float4 _S549 = float4(_S545, _S546, _S547, _S548);
                        float _S550 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 48U)>>2]);
                        float _S551 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 52U)>>2]);
                        float _S552 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 56U)>>2]);
                        float _S553 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S534 + 60U)>>2]);
                        float _S554 = (((float4(worldPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S539, _S544, _S549, float4(_S550, _S551, _S552, _S553))))).w;
                        if(_S554 > shadowVisibility_0)
                        {
                            shadowVisibility_0 = _S554;
                            shadowSlot_1 = _S533;
                        }
                        y_3 = y_3 + int(1);
                    }
                }
                else
                {
                    shadowSlot_1 = shadowSlot_0;
                }
                int x_3;
                if(_S530 == 5.0f)
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
                        uint _S555 = 224U + _S527;
                        float _S556 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S555)>>2]);
                        float _S557 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S555 + 4U)>>2]);
                        float _S558 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S555 + 8U)>>2]);
                        float _S559 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S555 + 12U)>>2]);
                        float3 toShadowSample_0 = float3(_S556, _S557, _S558) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                        float3 shadowLightDirection_0 = toShadowSample_0 * float3(rsqrt(max(dot(toShadowSample_0, toShadowSample_0), 0.00100000004749745f))) ;
                        int _S560 = shadowSlot_1 + int(sampleIndex_0);
                        for(;;)
                        {
                            uint _S561 = uint(_S560);
                            uint _S562 = _S561 * 16U;
                            uint _S563 = 14496U + _S562;
                            float _S564 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S563)>>2]);
                            float _S565 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S563 + 4U)>>2]);
                            float _S566 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S563 + 8U)>>2]);
                            float _S567 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S563 + 12U)>>2]);
                            uint _S568 = 14112U + _S562;
                            float _S569 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S568)>>2]);
                            float _S570 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S568 + 4U)>>2]);
                            float _S571 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S568 + 8U)>>2]);
                            float _S572 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S568 + 12U)>>2]);
                            if((dot(irradiance_0, shadowLightDirection_0)) < 0.0f)
                            {
                                sampleDirection_0 = - irradiance_0;
                            }
                            else
                            {
                                sampleDirection_0 = irradiance_0;
                            }
                            float slope_0 = clamp(1.0f - saturate(dot(sampleDirection_0, shadowLightDirection_0)), 0.0f, 1.0f);
                            float3 offsetPosition_0 = worldPosition_0 + sampleDirection_0 * float3((_S565 * slope_0)) ;
                            uint _S573 = 12576U + _S561 * 64U;
                            float _S574 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573)>>2]);
                            float _S575 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 4U)>>2]);
                            float _S576 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 8U)>>2]);
                            float _S577 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 12U)>>2]);
                            float4 _S578 = float4(_S574, _S575, _S576, _S577);
                            float _S579 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 16U)>>2]);
                            float _S580 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 20U)>>2]);
                            float _S581 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 24U)>>2]);
                            float _S582 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 28U)>>2]);
                            float4 _S583 = float4(_S579, _S580, _S581, _S582);
                            float _S584 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 32U)>>2]);
                            float _S585 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 36U)>>2]);
                            float _S586 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 40U)>>2]);
                            float _S587 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 44U)>>2]);
                            float4 _S588 = float4(_S584, _S585, _S586, _S587);
                            float _S589 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 48U)>>2]);
                            float _S590 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 52U)>>2]);
                            float _S591 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 56U)>>2]);
                            float _S592 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S573 + 60U)>>2]);
                            float4 lightClip_0 = (((float4(offsetPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S578, _S583, _S588, float4(_S589, _S590, _S591, _S592)))));
                            float _S593 = lightClip_0.w;
                            if(_S593 <= 0.0f)
                            {
                                shadowVisibility_0 = 1.0f;
                                break;
                            }
                            float3 ndc_0 = lightClip_0.xyz / float3(_S593) ;
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
                                _S524 = true;
                            }
                            else
                            {
                                _S524 = (ndc_0.z) < 0.0f;
                            }
                            if(_S524)
                            {
                                _S525 = true;
                            }
                            else
                            {
                                _S525 = (ndc_0.z) > 1.0f;
                            }
                            if(_S525)
                            {
                                shadowVisibility_0 = 1.0f;
                                break;
                            }
                            float2 _S594 = float2(_S569, _S570);
                            float2 _S595 = float2(_S571, _S572);
                            float2 _S596 = _S594 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S595;
                            bool transmission_0 = _S566 < 0.0f;
                            if(transmission_0)
                            {
                                shadowVisibility_0 = - _S566 - 1.0f;
                            }
                            else
                            {
                                shadowVisibility_0 = max(_S566, 0.0f);
                            }
                            float _S597 = ndc_0.z - _S564 * (1.0f + 2.0f * slope_0);
                            float2 _S598 = float2((_S567 * 0.5f)) ;
                            float2 _S599 = _S594 + _S598;
                            float2 _S600 = _S594 + _S595 - _S598;
                            y_3 = int(-1);
                            sampleIntensity_0 = 0.0f;
                            for(;;)
                            {
                                if(y_3 <= int(1))
                                {
                                }
                                else
                                {
                                    break;
                                }
                                x_3 = int(-1);
                                for(;;)
                                {
                                    if(x_3 <= int(1))
                                    {
                                    }
                                    else
                                    {
                                        break;
                                    }
                                    float2 coordinate_0 = clamp(_S596 + float2(float(x_3), float(y_3)) * float2((shadowVisibility_0 * _S567)) , _S599, _S600);
                                    if(_S597 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f))).x))
                                    {
                                        emissionScale_0 = 1.0f;
                                    }
                                    else
                                    {
                                        emissionScale_0 = 0.0f;
                                    }
                                    if(transmission_0)
                                    {
                                        _S526 = emissionScale_0 > 0.0f;
                                    }
                                    else
                                    {
                                        _S526 = false;
                                    }
                                    if(_S526)
                                    {
                                        float2 layer0_0 = (((&kernelContext_0)->shadowTransmission0_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float2 layer1_0 = (((&kernelContext_0)->shadowTransmission1_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float2 layer2_0 = (((&kernelContext_0)->shadowTransmission2_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float2 layer3_0 = (((&kernelContext_0)->shadowTransmission3_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                        float4 depths_0 = float4(layer0_0.x, layer1_0.x, layer2_0.x, layer3_0.x);
                                        float4 weights_0 = select(float4(1.0f, 1.0f, 1.0f, 1.0f), float4(layer0_0.y, layer1_0.y, layer2_0.y, layer3_0.y), (depths_0 >= (float4(0.0f) )) & ((float4(_S597) ) > depths_0));
                                        sampleShadowVisibility_0 = emissionScale_0 * weights_0.x * weights_0.y * weights_0.z * weights_0.w;
                                    }
                                    else
                                    {
                                        sampleShadowVisibility_0 = emissionScale_0;
                                    }
                                    float lit_0 = sampleIntensity_0 + sampleShadowVisibility_0;
                                    x_3 = x_3 + int(1);
                                    sampleIntensity_0 = lit_0;
                                }
                                y_3 = y_3 + int(1);
                            }
                            shadowVisibility_0 = sampleIntensity_0 * 0.1111111119389534f;
                            break;
                        }
                        shadowVisibilityBySample_0[sampleIndex_0] = shadowVisibility_0;
                        sampleIndex_0 = sampleIndex_0 + 1U;
                    }
                    useEmitterShadowSamples_0 = true;
                    shadowVisibility_0 = 1.0f;
                }
                else
                {
                    if(_S487)
                    {
                        uint _S601 = 224U + _S527;
                        float _S602 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S601)>>2]);
                        float _S603 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S601 + 4U)>>2]);
                        float _S604 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S601 + 8U)>>2]);
                        float _S605 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S601 + 12U)>>2]);
                        float3 toShadowLight_0 = float3(_S602, _S603, _S604) - worldPosition_0;
                        color_3 = toShadowLight_0 * float3(rsqrt(max(dot(toShadowLight_0, toShadowLight_0), 0.00100000004749745f))) ;
                    }
                    else
                    {
                        color_3 = lightDirection_0;
                    }
                    for(;;)
                    {
                        uint _S606 = uint(shadowSlot_1);
                        uint _S607 = _S606 * 16U;
                        uint _S608 = 14496U + _S607;
                        float _S609 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S608)>>2]);
                        float _S610 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S608 + 4U)>>2]);
                        float _S611 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S608 + 8U)>>2]);
                        float _S612 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S608 + 12U)>>2]);
                        uint _S613 = 14112U + _S607;
                        float _S614 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S613)>>2]);
                        float _S615 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S613 + 4U)>>2]);
                        float _S616 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S613 + 8U)>>2]);
                        float _S617 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S613 + 12U)>>2]);
                        if((dot(irradiance_0, color_3)) < 0.0f)
                        {
                            sampleDirection_0 = - irradiance_0;
                        }
                        else
                        {
                            sampleDirection_0 = irradiance_0;
                        }
                        float slope_1 = clamp(1.0f - saturate(dot(sampleDirection_0, color_3)), 0.0f, 1.0f);
                        float3 offsetPosition_1 = worldPosition_0 + sampleDirection_0 * float3((_S610 * slope_1)) ;
                        uint _S618 = 12576U + _S606 * 64U;
                        float _S619 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618)>>2]);
                        float _S620 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 4U)>>2]);
                        float _S621 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 8U)>>2]);
                        float _S622 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 12U)>>2]);
                        float4 _S623 = float4(_S619, _S620, _S621, _S622);
                        float _S624 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 16U)>>2]);
                        float _S625 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 20U)>>2]);
                        float _S626 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 24U)>>2]);
                        float _S627 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 28U)>>2]);
                        float4 _S628 = float4(_S624, _S625, _S626, _S627);
                        float _S629 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 32U)>>2]);
                        float _S630 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 36U)>>2]);
                        float _S631 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 40U)>>2]);
                        float _S632 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 44U)>>2]);
                        float4 _S633 = float4(_S629, _S630, _S631, _S632);
                        float _S634 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 48U)>>2]);
                        float _S635 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 52U)>>2]);
                        float _S636 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 56U)>>2]);
                        float _S637 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S618 + 60U)>>2]);
                        float4 lightClip_1 = (((float4(offsetPosition_1, 1.0f)) * (matrix<float,int(4),int(4)> (_S623, _S628, _S633, float4(_S634, _S635, _S636, _S637)))));
                        float _S638 = lightClip_1.w;
                        if(_S638 <= 0.0f)
                        {
                            shadowVisibility_0 = 1.0f;
                            break;
                        }
                        float3 ndc_1 = lightClip_1.xyz / float3(_S638) ;
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
                            _S524 = true;
                        }
                        else
                        {
                            _S524 = (ndc_1.z) < 0.0f;
                        }
                        if(_S524)
                        {
                            _S525 = true;
                        }
                        else
                        {
                            _S525 = (ndc_1.z) > 1.0f;
                        }
                        if(_S525)
                        {
                            shadowVisibility_0 = 1.0f;
                            break;
                        }
                        float2 _S639 = float2(_S614, _S615);
                        float2 _S640 = float2(_S616, _S617);
                        float2 _S641 = _S639 + (ndc_1.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S640;
                        bool transmission_1 = _S611 < 0.0f;
                        if(transmission_1)
                        {
                            shadowVisibility_0 = - _S611 - 1.0f;
                        }
                        else
                        {
                            shadowVisibility_0 = max(_S611, 0.0f);
                        }
                        float _S642 = ndc_1.z - _S609 * (1.0f + 2.0f * slope_1);
                        float2 _S643 = float2((_S612 * 0.5f)) ;
                        float2 _S644 = _S639 + _S643;
                        float2 _S645 = _S639 + _S640 - _S643;
                        y_3 = int(-1);
                        sampleIntensity_0 = 0.0f;
                        for(;;)
                        {
                            if(y_3 <= int(1))
                            {
                            }
                            else
                            {
                                break;
                            }
                            x_3 = int(-1);
                            for(;;)
                            {
                                if(x_3 <= int(1))
                                {
                                }
                                else
                                {
                                    break;
                                }
                                float2 coordinate_1 = clamp(_S641 + float2(float(x_3), float(y_3)) * float2((shadowVisibility_0 * _S612)) , _S644, _S645);
                                if(_S642 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f))).x))
                                {
                                    emissionScale_0 = 1.0f;
                                }
                                else
                                {
                                    emissionScale_0 = 0.0f;
                                }
                                if(transmission_1)
                                {
                                    _S526 = emissionScale_0 > 0.0f;
                                }
                                else
                                {
                                    _S526 = false;
                                }
                                if(_S526)
                                {
                                    float2 layer0_1 = (((&kernelContext_0)->shadowTransmission0_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float2 layer1_1 = (((&kernelContext_0)->shadowTransmission1_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float2 layer2_1 = (((&kernelContext_0)->shadowTransmission2_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float2 layer3_1 = (((&kernelContext_0)->shadowTransmission3_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                    float4 depths_1 = float4(layer0_1.x, layer1_1.x, layer2_1.x, layer3_1.x);
                                    float4 weights_1 = select(float4(1.0f, 1.0f, 1.0f, 1.0f), float4(layer0_1.y, layer1_1.y, layer2_1.y, layer3_1.y), (depths_1 >= (float4(0.0f) )) & ((float4(_S642) ) > depths_1));
                                    sampleShadowVisibility_0 = emissionScale_0 * weights_1.x * weights_1.y * weights_1.z * weights_1.w;
                                }
                                else
                                {
                                    sampleShadowVisibility_0 = emissionScale_0;
                                }
                                float lit_1 = sampleIntensity_0 + sampleShadowVisibility_0;
                                x_3 = x_3 + int(1);
                                sampleIntensity_0 = lit_1;
                            }
                            y_3 = y_3 + int(1);
                        }
                        shadowVisibility_0 = sampleIntensity_0 * 0.1111111119389534f;
                        break;
                    }
                    useEmitterShadowSamples_0 = false;
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
            float sampleIntensity_1 = _S4 / sampleCount_0;
            if(lightType_0 >= 2.0f)
            {
                uint _S646 = 224U + lightIndex_0 * 16U;
                float _S647 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S646)>>2]);
                float _S648 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S646 + 4U)>>2]);
                float _S649 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S646 + 8U)>>2]);
                float _S650 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S646 + 12U)>>2]);
                float3 toLight_0 = float3(_S647, _S648, _S649) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S651 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S651)) ;
                float sampleIntensity_2 = sampleIntensity_1 / _S651;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    _S524 = true;
                }
                else
                {
                    if(_S487)
                    {
                        _S524 = lightRadius_0 > 0.0f;
                    }
                    else
                    {
                        _S524 = false;
                    }
                }
                if(_S524)
                {
                    _S525 = true;
                }
                else
                {
                    if(lightType_0 == 5.0f)
                    {
                        _S525 = lightRadius_0 > 0.0f;
                    }
                    else
                    {
                        _S525 = false;
                    }
                    if(_S525)
                    {
                        _S526 = shapeX_0 > 0.0f;
                    }
                    else
                    {
                        _S526 = false;
                    }
                    _S525 = _S526;
                }
                if(_S525)
                {
                    sampleIntensity_0 = saturate(dot(sampleEmitterNormals_0[sampleIndex_0], - sampleDirection_1));
                }
                else
                {
                    sampleIntensity_0 = 1.0f;
                }
                if((_S5.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S5.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S652 = _S5.y;
                        if(_S652 > 0.0f)
                        {
                            emissionScale_0 = pow(max(emitterCosine_0, 0.0f), _S652);
                        }
                        else
                        {
                            emissionScale_0 = 1.0f;
                        }
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
            float _S653 = pow(max(0.0f, 1.0f - saturate(dot(lightEyeBase_0, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S653)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S653) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S654 = float3(_S653) ;
            float _S655 = max(_S389, 0.00100000004749745f);
            float alpha_0 = _S655 * _S655;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S656 = 1.0f - k_0;
            float3 _S657 = float3(max(4.0f * normalDotLight_0 * _S478, 1.00000000317107685e-30f)) ;
            float3 _S658 = mix(normalIncidence_0, grazingIncidence_0, _S654) * float3((_S478 / (_S478 * _S656 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S656 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S657;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S659 = max(_S390, 0.00100000004749745f);
                float alpha_1 = _S659 * _S659;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S660 = 1.0f - k_1;
                directSpecular_0 = _S658 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S654) * float3((_S478 / (_S478 * _S660 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S660 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S657);
            }
            else
            {
                directSpecular_0 = _S658;
            }
            float3 _S661 = diffuseColor_0 * float3(sampleIntensity_0) ;
            float3 diffuseIrradiance_0 = _S661 * _S391;
            if(useEmitterShadowSamples_0)
            {
                sampleShadowVisibility_0 = shadowVisibilityBySample_0[sampleIndex_0];
            }
            else
            {
                sampleShadowVisibility_0 = shadowVisibility_0;
            }
            color_3 = color_3 + float3((sampleShadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S434)  * diffuseIrradiance_0 + directSpecular_0 * float3(_S441)  * _S661);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S662 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S663 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S664 = saturate(_S389);
        float2 _S665 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S663, _S664)), level((0.0f)))).xy;
        float _S666 = _S665.x;
        float _S667 = _S665.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S666)  + grazingIncidence_0 * float3(_S667) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S666 + _S667)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S668 = max(_S402.domeControls_0.y, 1.0f);
        float3 _S669 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S668 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S402.domeControls_0.z;
            float _S670 = _S402.domeControls_0.w;
            for(;;)
            {
                bool _S671 = _S668 <= 1.0f;
                _S6 = _S671;
                if(_S671)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S670, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S668)), level((0.0f)))).xyz;
                break;
            }
            float _S672 = _S662.environmentControls_0.y;
            float _S673 = _S662.environmentControls_0.z;
            for(;;)
            {
                if(_S6)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S674 = max(_S672, 1.0f);
                    float _S675 = _S674 - 1.0f;
                    float slice_0 = _S664 * max(_S675, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S673, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S674)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S675) + v_0) / _S674)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S676 = max(_S672, 1.0f);
                float total_0 = _S676 * _S668;
                float _S677 = _S676 - 1.0f;
                float slice_1 = _S664 * max(_S677, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S673, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S676;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S677) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S678 = max(_S672, 1.0f);
                        float _S679 = _S678 - 1.0f;
                        float slice_2 = saturate(_S390) * max(_S679, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S673, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S678)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S679) + v_2) / _S678)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S680 = max(_S672, 1.0f);
                    float total_1 = _S680 * _S668;
                    float _S681 = _S680 - 1.0f;
                    float slice_3 = saturate(_S390) * max(_S681, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S673, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S680;
                    diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S681) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightTangentAxis_0 = diffuseFresnel_0;
            }
            else
            {
                lightTangentAxis_0 = _S669;
            }
            irradiance_0 = diffuseColor_0;
            lightDirectionAxis_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S669;
            lightDirectionAxis_0 = _S669;
            lightTangentAxis_0 = _S669;
            for(;;)
            {
                if(sampleIndex_0 < _S403)
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
                float _S682 = _S402.domeControls_0.w;
                for(;;)
                {
                    bool _S683 = _S668 <= 1.0f;
                    _S7 = _S683;
                    if(_S683)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S682, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S668)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                float _S684 = _S662.environmentControls_0.y;
                float _S685 = _S662.environmentControls_0.z;
                for(;;)
                {
                    if(_S7)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S686 = max(_S684, 1.0f);
                        float _S687 = _S686 - 1.0f;
                        float slice_4 = _S664 * max(_S687, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S685, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S686)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S687) + v_4) / _S686)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S688 = max(_S684, 1.0f);
                    float total_2 = _S688 * _S668;
                    float _S689 = _S688 - 1.0f;
                    float slice_5 = _S664 * max(_S689, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S685, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S688;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S689) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightDirectionAxis_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S7)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_7 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S690 = max(_S684, 1.0f);
                            float _S691 = _S690 - 1.0f;
                            float slice_6 = saturate(_S390) * max(_S691, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S685, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S690)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S691) + v_6) / _S690)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S692 = max(_S684, 1.0f);
                        float total_3 = _S692 * _S668;
                        float _S693 = _S692 - 1.0f;
                        float slice_7 = saturate(_S390) * max(_S693, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S685, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S692;
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S693) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
        float3 _S694 = float3(occlusion_0) ;
        float3 color_4 = color_2 + _S694 * diffuse_1 * irradiance_0 * diffuseWeight_0 + _S694 * lightDirectionAxis_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S695 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S663, saturate(_S390))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightTangentAxis_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S695.x)  + float3(_S695.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + emissiveColor_0) * float3(exp2((as_type<float>((_S3.z))))) ;
    if((_S3.y) == 1U)
    {
        color_2 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S696 = { float4(color_2, opacity_0) };
    return _S696;
}
