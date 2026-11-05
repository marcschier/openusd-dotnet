#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct volumeFragmentMain_Result_0
{
    float4 result_0 [[color(0)]];
    float outputDepth_0 [[depth(any)]];
};

struct pixelInput_0
{
    float3 eyePosition_0 [[user(TEXCOORD_1)]];
    float3 objectPosition_0 [[user(TEXCOORD_2)]];
    float3 volumeEyeToObjectRow0_0 [[user(TEXCOORD_6)]];
    float3 volumeEyeToObjectRow1_0 [[user(TEXCOORD_7)]];
    float3 volumeEyeToObjectRow2_0 [[user(TEXCOORD_8)]];
    float3 volumeCameraPosition_0 [[user(TEXCOORD_9)]];
    float volumeRayValid_0 [[user(TEXCOORD_11)]];
    float4 volumeClipPosition_0 [[user(TEXCOORD_12)]];
    float3 normal_0 [[user(NORMAL)]];
    float4 tint_0 [[user(COLOR)]];
    float3 worldNormal_0 [[user(TEXCOORD_3)]];
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
    texture2d<float, access::sample> volumeOpaqueDepthTexture_0;
    texture3d<float, access::sample> volumeDensityTexture_0;
    sampler volumeDensitySampler_0;
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

[[fragment]] volumeFragmentMain_Result_0 volumeFragmentMain(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> volumeOpaqueDepthTexture_1 [[texture(28)]], texture3d<float, access::sample> volumeDensityTexture_1 [[texture(9)]], sampler volumeDensitySampler_1 [[sampler(4)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> shadowTransmission0_1 [[texture(24)]], texture2d<float, access::sample> shadowTransmission1_1 [[texture(25)]], texture2d<float, access::sample> shadowTransmission2_1 [[texture(26)]], texture2d<float, access::sample> shadowTransmission3_1 [[texture(27)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_0;
    FragmentFrameParameters_0 _S2;
    float opacity_0;
    float4 finiteBoundary_0;
    float _S3;
    float3 lightDirectionAxis_0;
    float3 lightTangentAxis_0;
    bool _S4;
    bool _S5;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->volumeOpaqueDepthTexture_0 = volumeOpaqueDepthTexture_1;
    (&kernelContext_0)->volumeDensityTexture_0 = volumeDensityTexture_1;
    (&kernelContext_0)->volumeDensitySampler_0 = volumeDensitySampler_1;
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
    for(;;)
    {
        SurfaceParameters_natural_0 device* _S6 = (&kernelContext_0)->surfaceParameters_0+int(0);
        for(;;)
        {
            thread FragmentFrameParameters_0 frame_0;
            float _S7 = as_type<float>((&kernelContext_0)->frameParameters_0[(0U)>>2]);
            float _S8 = as_type<float>((&kernelContext_0)->frameParameters_0[(4U)>>2]);
            float _S9 = as_type<float>((&kernelContext_0)->frameParameters_0[(8U)>>2]);
            float _S10 = as_type<float>((&kernelContext_0)->frameParameters_0[(12U)>>2]);
            float4 _S11 = float4(_S7, _S8, _S9, _S10);
            float _S12 = as_type<float>((&kernelContext_0)->frameParameters_0[(16U)>>2]);
            float _S13 = as_type<float>((&kernelContext_0)->frameParameters_0[(20U)>>2]);
            float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(24U)>>2]);
            float _S15 = as_type<float>((&kernelContext_0)->frameParameters_0[(28U)>>2]);
            float4 _S16 = float4(_S12, _S13, _S14, _S15);
            float _S17 = as_type<float>((&kernelContext_0)->frameParameters_0[(32U)>>2]);
            float _S18 = as_type<float>((&kernelContext_0)->frameParameters_0[(36U)>>2]);
            float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(40U)>>2]);
            float _S20 = as_type<float>((&kernelContext_0)->frameParameters_0[(44U)>>2]);
            float4 _S21 = float4(_S17, _S18, _S19, _S20);
            float _S22 = as_type<float>((&kernelContext_0)->frameParameters_0[(48U)>>2]);
            float _S23 = as_type<float>((&kernelContext_0)->frameParameters_0[(52U)>>2]);
            float _S24 = as_type<float>((&kernelContext_0)->frameParameters_0[(56U)>>2]);
            float _S25 = as_type<float>((&kernelContext_0)->frameParameters_0[(60U)>>2]);
            (&frame_0)->clipToEye_0 = matrix<float,int(4),int(4)> (_S11, _S16, _S21, float4(_S22, _S23, _S24, _S25));
            uint _S26 = as_type<uint>((&kernelContext_0)->frameParameters_0[(64U)>>2]);
            uint _S27 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
            uint _S28 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
            uint _S29 = as_type<uint>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
            (&frame_0)->clipPlaneCount_0 = uint4(_S26, _S27, _S28, _S29);
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
                uint _S30 = 80U + index_0 * 16U;
                float _S31 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S30)>>2]);
                float _S32 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S30 + 4U)>>2]);
                float _S33 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S30 + 8U)>>2]);
                float _S34 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S30 + 12U)>>2]);
                (&frame_0)->clipPlanes_0[index_0] = float4(_S31, _S32, _S33, _S34);
                index_0 = index_0 + 1U;
            }
            float _S35 = as_type<float>((&kernelContext_0)->frameParameters_0[(208U)>>2]);
            float _S36 = as_type<float>((&kernelContext_0)->frameParameters_0[(212U)>>2]);
            float _S37 = as_type<float>((&kernelContext_0)->frameParameters_0[(216U)>>2]);
            float _S38 = as_type<float>((&kernelContext_0)->frameParameters_0[(220U)>>2]);
            (&frame_0)->ambientLight_0 = float4(_S35, _S36, _S37, _S38);
            (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
            (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
            (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
            (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
            (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
            (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
            (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 14880U;
            float _S39 = as_type<float>((&kernelContext_0)->frameParameters_0[(12512U)>>2]);
            float _S40 = as_type<float>((&kernelContext_0)->frameParameters_0[(12516U)>>2]);
            float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(12520U)>>2]);
            float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(12524U)>>2]);
            float4 _S43 = float4(_S39, _S40, _S41, _S42);
            float _S44 = as_type<float>((&kernelContext_0)->frameParameters_0[(12528U)>>2]);
            float _S45 = as_type<float>((&kernelContext_0)->frameParameters_0[(12532U)>>2]);
            float _S46 = as_type<float>((&kernelContext_0)->frameParameters_0[(12536U)>>2]);
            float _S47 = as_type<float>((&kernelContext_0)->frameParameters_0[(12540U)>>2]);
            float4 _S48 = float4(_S44, _S45, _S46, _S47);
            float _S49 = as_type<float>((&kernelContext_0)->frameParameters_0[(12544U)>>2]);
            float _S50 = as_type<float>((&kernelContext_0)->frameParameters_0[(12548U)>>2]);
            float _S51 = as_type<float>((&kernelContext_0)->frameParameters_0[(12552U)>>2]);
            float _S52 = as_type<float>((&kernelContext_0)->frameParameters_0[(12556U)>>2]);
            float4 _S53 = float4(_S49, _S50, _S51, _S52);
            float _S54 = as_type<float>((&kernelContext_0)->frameParameters_0[(12560U)>>2]);
            float _S55 = as_type<float>((&kernelContext_0)->frameParameters_0[(12564U)>>2]);
            float _S56 = as_type<float>((&kernelContext_0)->frameParameters_0[(12568U)>>2]);
            float _S57 = as_type<float>((&kernelContext_0)->frameParameters_0[(12572U)>>2]);
            (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S43, _S48, _S53, float4(_S54, _S55, _S56, _S57));
            float _S58 = as_type<float>((&kernelContext_0)->frameParameters_0[(16928U)>>2]);
            float _S59 = as_type<float>((&kernelContext_0)->frameParameters_0[(16932U)>>2]);
            float _S60 = as_type<float>((&kernelContext_0)->frameParameters_0[(16936U)>>2]);
            float _S61 = as_type<float>((&kernelContext_0)->frameParameters_0[(16940U)>>2]);
            (&frame_0)->environmentControls_0 = float4(_S58, _S59, _S60, _S61);
            float _S62 = as_type<float>((&kernelContext_0)->frameParameters_0[(16944U)>>2]);
            float _S63 = as_type<float>((&kernelContext_0)->frameParameters_0[(16948U)>>2]);
            float _S64 = as_type<float>((&kernelContext_0)->frameParameters_0[(16952U)>>2]);
            float _S65 = as_type<float>((&kernelContext_0)->frameParameters_0[(16956U)>>2]);
            (&frame_0)->domeControls_0 = float4(_S62, _S63, _S64, _S65);
            float _S66 = as_type<float>((&kernelContext_0)->frameParameters_0[(16960U)>>2]);
            float _S67 = as_type<float>((&kernelContext_0)->frameParameters_0[(16964U)>>2]);
            float _S68 = as_type<float>((&kernelContext_0)->frameParameters_0[(16968U)>>2]);
            float _S69 = as_type<float>((&kernelContext_0)->frameParameters_0[(16972U)>>2]);
            float4 _S70 = float4(_S66, _S67, _S68, _S69);
            float _S71 = as_type<float>((&kernelContext_0)->frameParameters_0[(16976U)>>2]);
            float _S72 = as_type<float>((&kernelContext_0)->frameParameters_0[(16980U)>>2]);
            float _S73 = as_type<float>((&kernelContext_0)->frameParameters_0[(16984U)>>2]);
            float _S74 = as_type<float>((&kernelContext_0)->frameParameters_0[(16988U)>>2]);
            float4 _S75 = float4(_S71, _S72, _S73, _S74);
            float _S76 = as_type<float>((&kernelContext_0)->frameParameters_0[(16992U)>>2]);
            float _S77 = as_type<float>((&kernelContext_0)->frameParameters_0[(16996U)>>2]);
            float _S78 = as_type<float>((&kernelContext_0)->frameParameters_0[(17000U)>>2]);
            float _S79 = as_type<float>((&kernelContext_0)->frameParameters_0[(17004U)>>2]);
            float4 _S80 = float4(_S76, _S77, _S78, _S79);
            float _S81 = as_type<float>((&kernelContext_0)->frameParameters_0[(17008U)>>2]);
            float _S82 = as_type<float>((&kernelContext_0)->frameParameters_0[(17012U)>>2]);
            float _S83 = as_type<float>((&kernelContext_0)->frameParameters_0[(17016U)>>2]);
            float _S84 = as_type<float>((&kernelContext_0)->frameParameters_0[(17020U)>>2]);
            float4 _S85 = float4(_S81, _S82, _S83, _S84);
            float _S86 = as_type<float>((&kernelContext_0)->frameParameters_0[(17024U)>>2]);
            float _S87 = as_type<float>((&kernelContext_0)->frameParameters_0[(17028U)>>2]);
            float _S88 = as_type<float>((&kernelContext_0)->frameParameters_0[(17032U)>>2]);
            float _S89 = as_type<float>((&kernelContext_0)->frameParameters_0[(17036U)>>2]);
            float4 _S90 = float4(_S86, _S87, _S88, _S89);
            float _S91 = as_type<float>((&kernelContext_0)->frameParameters_0[(17040U)>>2]);
            float _S92 = as_type<float>((&kernelContext_0)->frameParameters_0[(17044U)>>2]);
            float _S93 = as_type<float>((&kernelContext_0)->frameParameters_0[(17048U)>>2]);
            float _S94 = as_type<float>((&kernelContext_0)->frameParameters_0[(17052U)>>2]);
            float4 _S95 = float4(_S91, _S92, _S93, _S94);
            float _S96 = as_type<float>((&kernelContext_0)->frameParameters_0[(17056U)>>2]);
            float _S97 = as_type<float>((&kernelContext_0)->frameParameters_0[(17060U)>>2]);
            float _S98 = as_type<float>((&kernelContext_0)->frameParameters_0[(17064U)>>2]);
            float _S99 = as_type<float>((&kernelContext_0)->frameParameters_0[(17068U)>>2]);
            float4 _S100 = float4(_S96, _S97, _S98, _S99);
            float _S101 = as_type<float>((&kernelContext_0)->frameParameters_0[(17072U)>>2]);
            float _S102 = as_type<float>((&kernelContext_0)->frameParameters_0[(17076U)>>2]);
            float _S103 = as_type<float>((&kernelContext_0)->frameParameters_0[(17080U)>>2]);
            float _S104 = as_type<float>((&kernelContext_0)->frameParameters_0[(17084U)>>2]);
            float4 _S105 = float4(_S101, _S102, _S103, _S104);
            float _S106 = as_type<float>((&kernelContext_0)->frameParameters_0[(17088U)>>2]);
            float _S107 = as_type<float>((&kernelContext_0)->frameParameters_0[(17092U)>>2]);
            float _S108 = as_type<float>((&kernelContext_0)->frameParameters_0[(17096U)>>2]);
            float _S109 = as_type<float>((&kernelContext_0)->frameParameters_0[(17100U)>>2]);
            float4 _S110 = float4(_S106, _S107, _S108, _S109);
            float _S111 = as_type<float>((&kernelContext_0)->frameParameters_0[(17104U)>>2]);
            float _S112 = as_type<float>((&kernelContext_0)->frameParameters_0[(17108U)>>2]);
            float _S113 = as_type<float>((&kernelContext_0)->frameParameters_0[(17112U)>>2]);
            float _S114 = as_type<float>((&kernelContext_0)->frameParameters_0[(17116U)>>2]);
            float4 _S115 = float4(_S111, _S112, _S113, _S114);
            float _S116 = as_type<float>((&kernelContext_0)->frameParameters_0[(17120U)>>2]);
            float _S117 = as_type<float>((&kernelContext_0)->frameParameters_0[(17124U)>>2]);
            float _S118 = as_type<float>((&kernelContext_0)->frameParameters_0[(17128U)>>2]);
            float _S119 = as_type<float>((&kernelContext_0)->frameParameters_0[(17132U)>>2]);
            float4 _S120 = float4(_S116, _S117, _S118, _S119);
            float _S121 = as_type<float>((&kernelContext_0)->frameParameters_0[(17136U)>>2]);
            float _S122 = as_type<float>((&kernelContext_0)->frameParameters_0[(17140U)>>2]);
            float _S123 = as_type<float>((&kernelContext_0)->frameParameters_0[(17144U)>>2]);
            float _S124 = as_type<float>((&kernelContext_0)->frameParameters_0[(17148U)>>2]);
            float4 _S125 = float4(_S121, _S122, _S123, _S124);
            float _S126 = as_type<float>((&kernelContext_0)->frameParameters_0[(17152U)>>2]);
            float _S127 = as_type<float>((&kernelContext_0)->frameParameters_0[(17156U)>>2]);
            float _S128 = as_type<float>((&kernelContext_0)->frameParameters_0[(17160U)>>2]);
            float _S129 = as_type<float>((&kernelContext_0)->frameParameters_0[(17164U)>>2]);
            float4 _S130 = float4(_S126, _S127, _S128, _S129);
            float _S131 = as_type<float>((&kernelContext_0)->frameParameters_0[(17168U)>>2]);
            float _S132 = as_type<float>((&kernelContext_0)->frameParameters_0[(17172U)>>2]);
            float _S133 = as_type<float>((&kernelContext_0)->frameParameters_0[(17176U)>>2]);
            float _S134 = as_type<float>((&kernelContext_0)->frameParameters_0[(17180U)>>2]);
            float4 _S135 = float4(_S131, _S132, _S133, _S134);
            float _S136 = as_type<float>((&kernelContext_0)->frameParameters_0[(17184U)>>2]);
            float _S137 = as_type<float>((&kernelContext_0)->frameParameters_0[(17188U)>>2]);
            float _S138 = as_type<float>((&kernelContext_0)->frameParameters_0[(17192U)>>2]);
            float _S139 = as_type<float>((&kernelContext_0)->frameParameters_0[(17196U)>>2]);
            float4 _S140 = float4(_S136, _S137, _S138, _S139);
            float _S141 = as_type<float>((&kernelContext_0)->frameParameters_0[(17200U)>>2]);
            float _S142 = as_type<float>((&kernelContext_0)->frameParameters_0[(17204U)>>2]);
            float _S143 = as_type<float>((&kernelContext_0)->frameParameters_0[(17208U)>>2]);
            float _S144 = as_type<float>((&kernelContext_0)->frameParameters_0[(17212U)>>2]);
            float4 _S145 = float4(_S141, _S142, _S143, _S144);
            (&frame_0)->domeAmbient_0[int(0)] = _S70;
            (&frame_0)->domeAmbient_0[int(1)] = _S75;
            (&frame_0)->domeAmbient_0[int(2)] = _S80;
            (&frame_0)->domeAmbient_0[int(3)] = _S85;
            (&frame_0)->domeAmbient_0[int(4)] = _S90;
            (&frame_0)->domeAmbient_0[int(5)] = _S95;
            (&frame_0)->domeAmbient_0[int(6)] = _S100;
            (&frame_0)->domeAmbient_0[int(7)] = _S105;
            (&frame_0)->domeEnvironment_0[int(0)] = _S110;
            (&frame_0)->domeEnvironment_0[int(1)] = _S115;
            (&frame_0)->domeEnvironment_0[int(2)] = _S120;
            (&frame_0)->domeEnvironment_0[int(3)] = _S125;
            (&frame_0)->domeEnvironment_0[int(4)] = _S130;
            (&frame_0)->domeEnvironment_0[int(5)] = _S135;
            (&frame_0)->domeEnvironment_0[int(6)] = _S140;
            (&frame_0)->domeEnvironment_0[int(7)] = _S145;
            _S2 = frame_0;
            break;
        }
        float4 _S146 = float4(_S6->reserved_0) ;
        float _S147 = _S146.x;
        if(_S147 < 0.5f)
        {
            for(;;)
            {
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
        }
        float _S149 = position_0.z;
        float4 _S150 = float4(_S6->clearcoatShaded_0) ;
        float shadedMode_0 = _S150.z;
        bool shaded_0 = shadedMode_0 >= 0.5f;
        bool unlit_0 = shadedMode_0 >= 1.5f;
        float3 diffuseColor_0;
        if(shaded_0)
        {
            diffuseColor_0 = (float4(_S6->diffuseOpacity_0) ).xyz;
        }
        else
        {
            diffuseColor_0 = _S1.tint_0.xyz;
        }
        if(shaded_0)
        {
            opacity_0 = (float4(_S6->diffuseOpacity_0) ).w;
        }
        else
        {
            opacity_0 = _S1.tint_0.w;
        }
        float4 _S151 = float4(_S6->emissiveOcclusion_0) ;
        float3 emissiveColor_0 = _S151.xyz;
        float occlusion_0 = _S151.w;
        float3 unlitColor_0;
        if(unlit_0)
        {
            float3 unlitColor_1 = (diffuseColor_0 + emissiveColor_0) * float3(exp2((as_type<float>((_S2.clipPlaneCount_0.z))))) ;
            if((_S2.clipPlaneCount_0.y) == 1U)
            {
                unlitColor_0 = unlitColor_1 / (float3(1.0f)  + max(unlitColor_1, float3(0.0f, 0.0f, 0.0f)));
            }
            else
            {
                unlitColor_0 = unlitColor_1;
            }
            float4 _S152 = float4(unlitColor_0, opacity_0);
            opacity_0 = _S149;
            finiteBoundary_0 = _S152;
            break;
        }
        uint axis_0;
        uint planeIndex_0;
        uint step_0;
        float rayEntry_0;
        float rayExit_0;
        float startDistance_0;
        float endDistance_0;
        float startDistance_1;
        float endDistance_1;
        bool cameraInside_0;
        bool isEntryBoundary_0;
        bool _S153;
        int y_0;
        if(_S147 >= 0.5f)
        {
            if((_S1.volumeRayValid_0) < 0.5f)
            {
                discard_fragment();
            }
            float2 clipXY_0 = _S1.volumeClipPosition_0.xy / float2(_S1.volumeClipPosition_0.w) ;
            FragmentFrameParameters_0 _S154 = _S2;
            float4 clipStart_0 = (((float4(clipXY_0, 0.0f, 1.0f)) * (_S2.clipToEye_0)));
            float4 clipEnd_0 = (((float4(clipXY_0, 1.0f, 1.0f)) * (_S2.clipToEye_0)));
            float _S155 = clipStart_0.w;
            bool finiteStart_0 = _S155 != 0.0f;
            if(finiteStart_0)
            {
                finiteBoundary_0 = clipStart_0;
            }
            else
            {
                finiteBoundary_0 = clipEnd_0;
            }
            float4 otherBoundary_0;
            if(finiteStart_0)
            {
                otherBoundary_0 = clipEnd_0;
            }
            else
            {
                otherBoundary_0 = clipStart_0;
            }
            float _S156 = finiteBoundary_0.w;
            float3 eyeRayOrigin_0 = finiteBoundary_0.xyz / float3(_S156) ;
            float _S157 = otherBoundary_0.w;
            if(_S157 == 0.0f)
            {
                unlitColor_0 = otherBoundary_0.xyz * float3(float((int(sign((_S156)))))) ;
            }
            else
            {
                unlitColor_0 = otherBoundary_0.xyz / float3(_S157)  - eyeRayOrigin_0;
            }
            float3 eyeDirection_0 = normalize(unlitColor_0);
            float3 rayDirection_0 = float3(dot(_S1.volumeEyeToObjectRow0_0, eyeDirection_0), dot(_S1.volumeEyeToObjectRow1_0, eyeDirection_0), dot(_S1.volumeEyeToObjectRow2_0, eyeDirection_0));
            float3 rayOrigin_0 = _S1.volumeCameraPosition_0 + float3(dot(_S1.volumeEyeToObjectRow0_0, eyeRayOrigin_0), dot(_S1.volumeEyeToObjectRow1_0, eyeRayOrigin_0), dot(_S1.volumeEyeToObjectRow2_0, eyeRayOrigin_0));
            if(all(rayOrigin_0 >= float3(-0.5f, -0.5f, -0.5f)))
            {
                cameraInside_0 = all(rayOrigin_0 <= float3(0.5f, 0.5f, 0.5f));
            }
            else
            {
                cameraInside_0 = false;
            }
            for(;;)
            {
                axis_0 = 0U;
                rayEntry_0 = -3.4028234663852886e+38f;
                rayExit_0 = 3.4028234663852886e+38f;
                for(;;)
                {
                    if(axis_0 < 3U)
                    {
                    }
                    else
                    {
                        y_0 = int(2);
                        break;
                    }
                    uint _S158 = axis_0;
                    if((rayDirection_0[axis_0]) == 0.0f)
                    {
                        uint _S159 = axis_0;
                        if((rayOrigin_0[axis_0]) < -0.5f)
                        {
                            isEntryBoundary_0 = true;
                        }
                        else
                        {
                            isEntryBoundary_0 = (rayOrigin_0[_S159]) > 0.5f;
                        }
                        if(isEntryBoundary_0)
                        {
                            y_0 = int(1);
                            break;
                        }
                    }
                    else
                    {
                        float first_0 = (-0.5f - rayOrigin_0[axis_0]) / rayDirection_0[_S158];
                        float second_0 = (0.5f - rayOrigin_0[axis_0]) / rayDirection_0[_S158];
                        float _S160 = min(rayExit_0, max(first_0, second_0));
                        rayEntry_0 = max(rayEntry_0, min(first_0, second_0));
                        rayExit_0 = _S160;
                    }
                    axis_0 = axis_0 + 1U;
                }
                if(y_0 != int(2))
                {
                    isEntryBoundary_0 = false;
                    break;
                }
                isEntryBoundary_0 = rayExit_0 > (max(rayEntry_0, 0.0f));
                break;
            }
            if(!isEntryBoundary_0)
            {
                discard_fragment();
            }
            for(;;)
            {
                if(_S155 == 0.0f)
                {
                    startDistance_0 = 3.4028234663852886e+38f;
                    break;
                }
                startDistance_0 = dot(clipStart_0.xyz / float3(_S155)  - eyeRayOrigin_0, eyeDirection_0);
                break;
            }
            for(;;)
            {
                float _S161 = clipEnd_0.w;
                if(_S161 == 0.0f)
                {
                    endDistance_0 = 3.4028234663852886e+38f;
                    break;
                }
                endDistance_0 = dot(clipEnd_0.xyz / float3(_S161)  - eyeRayOrigin_0, eyeDirection_0);
                break;
            }
            float _S162 = max(max(rayEntry_0, 0.0f), min(startDistance_0, endDistance_0));
            float _S163 = min(rayExit_0, max(startDistance_0, endDistance_0));
            FragmentFrameParameters_0 _S164 = _S2;
            uint _S165 = min(_S2.clipPlaneCount_0.x, 8U);
            planeIndex_0 = 0U;
            startDistance_0 = _S162;
            endDistance_0 = _S163;
            for(;;)
            {
                if(planeIndex_0 < _S165)
                {
                }
                else
                {
                    break;
                }
                float3 _S166 = _S2.clipPlanes_0[planeIndex_0].xyz;
                float offset_0 = dot(_S166, eyeRayOrigin_0) + _S2.clipPlanes_0[planeIndex_0].w;
                float slope_0 = dot(_S166, eyeDirection_0);
                if(slope_0 == 0.0f)
                {
                    if(offset_0 < 0.0f)
                    {
                        discard_fragment();
                    }
                }
                else
                {
                    if(slope_0 > 0.0f)
                    {
                        startDistance_1 = max(startDistance_0, - offset_0 / slope_0);
                        endDistance_1 = endDistance_0;
                    }
                    else
                    {
                        float _S167 = min(endDistance_0, - offset_0 / slope_0);
                        startDistance_1 = startDistance_0;
                        endDistance_1 = _S167;
                    }
                    startDistance_0 = startDistance_1;
                    endDistance_0 = endDistance_1;
                }
                planeIndex_0 = planeIndex_0 + 1U;
            }
            int3 _S168 = int3(int2(position_0.xy), int(0));
            float opaqueDepth_0 = (((&kernelContext_0)->volumeOpaqueDepthTexture_0).read(vec<uint,2>(((_S168)).xy), uint(((_S168)).z)).x);
            if(opaqueDepth_0 != (as_type<float>((_S164.clipPlaneCount_0.w))))
            {
                for(;;)
                {
                    float4 eyePosition_1 = (((float4(clipXY_0, opaqueDepth_0, 1.0f)) * (_S154.clipToEye_0)));
                    float _S169 = eyePosition_1.w;
                    if(_S169 == 0.0f)
                    {
                        startDistance_1 = 3.4028234663852886e+38f;
                        break;
                    }
                    startDistance_1 = dot(eyePosition_1.xyz / float3(_S169)  - eyeRayOrigin_0, eyeDirection_0);
                    break;
                }
                endDistance_0 = min(endDistance_0, startDistance_1);
            }
            if(endDistance_0 <= startDistance_0)
            {
                discard_fragment();
            }
            float3 _S170 = eyeRayOrigin_0 + eyeDirection_0 * float3(startDistance_0) ;
            for(;;)
            {
                float4 slope_1 = (((float4(0.0f, 0.0f, 1.0f, 0.0f)) * (_S154.clipToEye_0)));
                float3 numerator_0 = _S170 * float3(_S155)  - clipStart_0.xyz;
                float3 denominator_0 = slope_1.xyz - _S170 * float3(slope_1.w) ;
                float3 magnitude_0 = abs(denominator_0);
                float _S171 = magnitude_0.x;
                float _S172 = magnitude_0.y;
                if(_S171 >= _S172)
                {
                    isEntryBoundary_0 = _S171 >= (magnitude_0.z);
                }
                else
                {
                    isEntryBoundary_0 = false;
                }
                if(isEntryBoundary_0)
                {
                    startDistance_1 = numerator_0.x / denominator_0.x;
                    break;
                }
                if(_S172 >= (magnitude_0.z))
                {
                    startDistance_1 = numerator_0.y / denominator_0.y;
                }
                else
                {
                    startDistance_1 = numerator_0.z / denominator_0.z;
                }
                break;
            }
            float surfaceDistance_0 = dot(_S1.objectPosition_0 - rayOrigin_0, rayDirection_0) / dot(rayDirection_0, rayDirection_0);
            float boundaryTolerance_0 = (rayExit_0 - rayEntry_0) * 0.00009999999747379f;
            if(rayEntry_0 >= 0.0f)
            {
                isEntryBoundary_0 = (abs(surfaceDistance_0 - rayEntry_0)) <= boundaryTolerance_0;
            }
            else
            {
                isEntryBoundary_0 = false;
            }
            bool isExitBoundary_0 = (abs(surfaceDistance_0 - rayExit_0)) <= boundaryTolerance_0;
            bool entryWasClipped_0 = rayEntry_0 < (startDistance_0 - boundaryTolerance_0);
            if(entryWasClipped_0)
            {
                _S153 = !isExitBoundary_0;
            }
            else
            {
                _S153 = false;
            }
            if(_S153)
            {
                cameraInside_0 = true;
            }
            else
            {
                if(!entryWasClipped_0)
                {
                    isEntryBoundary_0 = !isEntryBoundary_0;
                }
                else
                {
                    isEntryBoundary_0 = false;
                }
                if(isEntryBoundary_0)
                {
                    if(cameraInside_0)
                    {
                        cameraInside_0 = isExitBoundary_0;
                    }
                    else
                    {
                        cameraInside_0 = false;
                    }
                    cameraInside_0 = !cameraInside_0;
                }
                else
                {
                    cameraInside_0 = false;
                }
            }
            if(cameraInside_0)
            {
                discard_fragment();
            }
            if((_S146.w) >= 0.5f)
            {
                float _S173 = (float4(_S6->textureControls_0) ).z;
                for(;;)
                {
                    float rayLength_0 = endDistance_0 - startDistance_0;
                    float localRayLength_0 = length(rayDirection_0) * rayLength_0;
                    uint stepCount_0 = uint(max(1.0f, ceil(max(1.0f, _S173) * localRayLength_0)));
                    step_0 = 0U;
                    endDistance_1 = 0.0f;
                    for(;;)
                    {
                        if(step_0 < stepCount_0)
                        {
                        }
                        else
                        {
                            break;
                        }
                        float density_0 = endDistance_1 + (((&kernelContext_0)->volumeDensityTexture_0).sample(((&kernelContext_0)->volumeDensitySampler_0), (saturate(rayOrigin_0 + rayDirection_0 * float3((startDistance_0 + (float(step_0) + 0.5f) / float(stepCount_0) * rayLength_0))  + float3(0.5f, 0.5f, 0.5f)))).x);
                        step_0 = step_0 + 1U;
                        endDistance_1 = density_0;
                    }
                    _S3 = endDistance_1 / float(stepCount_0) * localRayLength_0;
                    break;
                }
                endDistance_1 = _S3 * max(0.0f, _S146.y);
            }
            else
            {
                endDistance_1 = max(0.0f, _S146.y) * (length(rayDirection_0) * (endDistance_0 - startDistance_0));
            }
            float4 _S174 = float4(diffuseColor_0, saturate(opacity_0) * (1.0f - exp(- max(0.0f, endDistance_1))));
            opacity_0 = startDistance_1;
            finiteBoundary_0 = _S174;
            break;
        }
        float4 _S175 = float4(_S6->metallicRoughnessThresholdWorkflow_0) ;
        float metallic_0 = saturate(_S175.x);
        float roughness_0 = clamp(_S175.y, 0.00999999977648258f, 1.0f);
        float4 _S176 = float4(_S6->specularIor_0) ;
        float3 specularColor_0 = _S176.xyz;
        float ior_0 = _S176.w;
        float clearcoatAmount_0 = _S150.x;
        float clearcoatRoughness_0 = _S150.y;
        float opacityThreshold_0 = _S175.z;
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
        if(lengthSquared_0 > 0.00100000004749745f)
        {
            unlitColor_0 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
        }
        else
        {
            unlitColor_0 = float3(0.0f, 0.0f, 1.0f);
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
        float _S177 = max(0.00100000004749745f, roughness_0);
        float _S178 = max(0.00100000004749745f, clearcoatRoughness_0);
        float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
        float3 _S179 = float3(3.14159274101257324f) ;
        float3 diffuse_0 = diffuseColor_0 / _S179;
        bool omniPbrSpecular_0 = ((float4(_S6->textureControls_0) ).w) >= 0.5f;
        float dielectricLevel_0 = _S150.w;
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
            if((_S175.w) >= 0.5f)
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
        FragmentFrameParameters_0 _S185 = _S2;
        float _S186 = _S2.ambientLight_0.w;
        if(_S186 > 0.5f)
        {
            cameraInside_0 = true;
        }
        else
        {
            float3 _S187 = _S185.ambientLight_0.xyz;
            cameraInside_0 = (dot(_S187, _S187)) > 0.0f;
        }
        if(cameraInside_0)
        {
            cameraInside_0 = true;
        }
        else
        {
            cameraInside_0 = (_S2.environmentControls_0.x) >= 0.5f;
        }
        if(cameraInside_0)
        {
            cameraInside_0 = true;
        }
        else
        {
            cameraInside_0 = (_S2.environmentControls_0.w) >= 0.5f;
        }
        uint _S188 = min(uint(_S186), 128U);
        float3 sceneEye_0 = normalize((((float4(unlitColor_0, 0.0f)) * (_S2.eyeToWorld_0))).xyz);
        float3 worldPosition_0 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S2.eyeToWorld_0))).xyz;
        float3 worldGeometricNormal_0 = cross(dfdx(worldPosition_0), dfdy(worldPosition_0));
        float worldNormalLengthSquared_0 = dot(worldGeometricNormal_0, worldGeometricNormal_0);
        float3 prefiltered_0;
        if(worldNormalLengthSquared_0 > 9.99999968265522539e-21f)
        {
            prefiltered_0 = worldGeometricNormal_0 * float3(rsqrt(worldNormalLengthSquared_0)) ;
        }
        else
        {
            prefiltered_0 = float3(0.0f, 0.0f, 1.0f);
        }
        float4 _S189 = float4(_S6->lightColorAmbient_0) ;
        float3 color_0 = diffuseColor_0 * float3(_S189.w) ;
        uint domeLinkMask_0 = uint(max((float4(_S6->domeLinkControls_0) ).x, 0.0f));
        FragmentFrameParameters_0 _S190 = _S2;
        uint _S191 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
        uint allDomes_0 = (1U << _S191) - 1U;
        bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
        float3 _S192 = _S185.ambientLight_0.xyz;
        float3 domeAmbient_1;
        if(!allDomesLinked_0)
        {
            float3 _S193 = float3(0.0f, 0.0f, 0.0f);
            axis_0 = 0U;
            domeAmbient_1 = _S193;
            for(;;)
            {
                if(axis_0 < _S191)
                {
                }
                else
                {
                    break;
                }
                if((domeLinkMask_0 & (1U << axis_0)) != 0U)
                {
                    domeAmbient_1 = domeAmbient_1 + _S2.domeAmbient_0[axis_0].xyz;
                }
                axis_0 = axis_0 + 1U;
            }
        }
        else
        {
            domeAmbient_1 = _S192;
        }
        float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
        bool _S194 = !cameraInside_0;
        if(_S194)
        {
            axis_0 = 1U;
        }
        else
        {
            axis_0 = _S188;
        }
        planeIndex_0 = 0U;
        float3 color_2 = color_1;
        for(;;)
        {
            if(planeIndex_0 < axis_0)
            {
            }
            else
            {
                break;
            }
            if(cameraInside_0)
            {
                isEntryBoundary_0 = (((uint4(_S6->lightLinkMask_0) )[planeIndex_0 / 32U]) & (1U << (planeIndex_0 % 32U))) == 0U;
            }
            else
            {
                isEntryBoundary_0 = false;
            }
            if(isEntryBoundary_0)
            {
                planeIndex_0 = planeIndex_0 + 1U;
                continue;
            }
            bool _S195 = planeIndex_0 == 0U;
            if(_S195)
            {
                _S153 = _S194;
            }
            else
            {
                _S153 = false;
            }
            if(_S153)
            {
                rayEntry_0 = 1.0f;
            }
            else
            {
                uint _S196 = 224U + planeIndex_0 * 16U;
                float _S197 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S196)>>2]);
                float _S198 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S196 + 4U)>>2]);
                float _S199 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S196 + 8U)>>2]);
                float _S200 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S196 + 12U)>>2]);
                rayEntry_0 = _S200;
            }
            bool _S201;
            if(_S195)
            {
                _S201 = _S194;
            }
            else
            {
                _S201 = false;
            }
            if(_S201)
            {
                lightDirectionAxis_0 = normalize((float4(_S6->lightDirectionIntensity_0) ).xyz);
            }
            else
            {
                uint _S202 = 2272U + planeIndex_0 * 16U;
                float _S203 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S202)>>2]);
                float _S204 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S202 + 4U)>>2]);
                float _S205 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S202 + 8U)>>2]);
                float _S206 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S202 + 12U)>>2]);
                lightDirectionAxis_0 = float3(_S203, _S204, _S205);
            }
            float3 lightDirection_0 = normalize(lightDirectionAxis_0);
            bool _S207;
            if(_S195)
            {
                _S207 = _S194;
            }
            else
            {
                _S207 = false;
            }
            if(_S207)
            {
                rayExit_0 = (float4(_S6->lightDirectionIntensity_0) ).w;
            }
            else
            {
                uint _S208 = 4320U + planeIndex_0 * 16U;
                float _S209 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S208)>>2]);
                float _S210 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S208 + 4U)>>2]);
                float _S211 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S208 + 8U)>>2]);
                float _S212 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S208 + 12U)>>2]);
                rayExit_0 = _S212;
            }
            bool _S213;
            if(_S195)
            {
                _S213 = _S194;
            }
            else
            {
                _S213 = false;
            }
            if(_S213)
            {
                diffuseColor_0 = _S189.xyz;
            }
            else
            {
                uint _S214 = 4320U + planeIndex_0 * 16U;
                float _S215 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S214)>>2]);
                float _S216 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S214 + 4U)>>2]);
                float _S217 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S214 + 8U)>>2]);
                float _S218 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S214 + 12U)>>2]);
                diffuseColor_0 = float3(_S215, _S216, _S217);
            }
            bool _S219;
            if(_S195)
            {
                _S219 = _S194;
            }
            else
            {
                _S219 = false;
            }
            if(_S219)
            {
                startDistance_0 = 1.0f;
            }
            else
            {
                uint _S220 = 6368U + planeIndex_0 * 16U;
                float _S221 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S220)>>2]);
                float _S222 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S220 + 4U)>>2]);
                float _S223 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S220 + 8U)>>2]);
                float _S224 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S220 + 12U)>>2]);
                startDistance_0 = _S221;
            }
            bool _S225;
            if(_S195)
            {
                _S225 = _S194;
            }
            else
            {
                _S225 = false;
            }
            if(_S225)
            {
                endDistance_0 = 1.0f;
            }
            else
            {
                uint _S226 = 6368U + planeIndex_0 * 16U;
                float _S227 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S226)>>2]);
                float _S228 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S226 + 4U)>>2]);
                float _S229 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S226 + 8U)>>2]);
                float _S230 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S226 + 12U)>>2]);
                endDistance_0 = _S228;
            }
            bool _S231;
            if(_S195)
            {
                _S231 = _S194;
            }
            else
            {
                _S231 = false;
            }
            if(_S231)
            {
                lightTangentAxis_0 = float3(1.0f, 0.0f, 0.0f);
            }
            else
            {
                uint _S232 = 8416U + planeIndex_0 * 16U;
                float _S233 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S232)>>2]);
                float _S234 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S232 + 4U)>>2]);
                float _S235 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S232 + 8U)>>2]);
                float _S236 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S232 + 12U)>>2]);
                lightTangentAxis_0 = float3(_S233, _S234, _S235);
            }
            bool _S237;
            if(_S195)
            {
                _S237 = _S194;
            }
            else
            {
                _S237 = false;
            }
            float3 lightBitangentAxis_0;
            if(_S237)
            {
                lightBitangentAxis_0 = float3(0.0f, 1.0f, 0.0f);
            }
            else
            {
                uint _S238 = 10464U + planeIndex_0 * 16U;
                float _S239 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S238)>>2]);
                float _S240 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S238 + 4U)>>2]);
                float _S241 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S238 + 8U)>>2]);
                float _S242 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S238 + 12U)>>2]);
                lightBitangentAxis_0 = float3(_S239, _S240, _S241);
            }
            bool _S243;
            if(_S195)
            {
                _S243 = _S194;
            }
            else
            {
                _S243 = false;
            }
            if(_S243)
            {
                startDistance_1 = 0.0f;
            }
            else
            {
                uint _S244 = 8416U + planeIndex_0 * 16U;
                float _S245 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S244)>>2]);
                float _S246 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S244 + 4U)>>2]);
                float _S247 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S244 + 8U)>>2]);
                float _S248 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S244 + 12U)>>2]);
                startDistance_1 = _S248;
            }
            bool _S249;
            if(_S195)
            {
                _S249 = _S194;
            }
            else
            {
                _S249 = false;
            }
            if(_S249)
            {
                endDistance_1 = 0.0f;
            }
            else
            {
                uint _S250 = 10464U + planeIndex_0 * 16U;
                float _S251 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S250)>>2]);
                float _S252 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S250 + 4U)>>2]);
                float _S253 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S250 + 8U)>>2]);
                float _S254 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S250 + 12U)>>2]);
                endDistance_1 = _S254;
            }
            bool _S255;
            if(_S195)
            {
                _S255 = _S194;
            }
            else
            {
                _S255 = false;
            }
            float lightRadius_0;
            if(_S255)
            {
                lightRadius_0 = 0.0f;
            }
            else
            {
                uint _S256 = 2272U + planeIndex_0 * 16U;
                float _S257 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S256)>>2]);
                float _S258 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S256 + 4U)>>2]);
                float _S259 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S256 + 8U)>>2]);
                float _S260 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S256 + 12U)>>2]);
                lightRadius_0 = _S260;
            }
            bool _S261;
            if(_S195)
            {
                _S261 = _S194;
            }
            else
            {
                _S261 = false;
            }
            float3 lightEyeBase_0;
            if(_S261)
            {
                lightEyeBase_0 = unlitColor_0;
            }
            else
            {
                lightEyeBase_0 = sceneEye_0;
            }
            float3 directNormal_0;
            if(cameraInside_0)
            {
                directNormal_0 = worldNormal_2;
            }
            else
            {
                directNormal_0 = irradiance_0;
            }
            float _S262 = saturate(abs(dot(directNormal_0, lightEyeBase_0)) + 0.00000999999974738f);
            bool _S263;
            if(_S195)
            {
                _S263 = _S194;
            }
            else
            {
                _S263 = false;
            }
            if(_S263)
            {
                finiteBoundary_0 = float4(0.0f, 0.0f, 0.0f, 0.0f);
            }
            else
            {
                uint _S264 = 17216U + planeIndex_0 * 16U;
                float _S265 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S264)>>2]);
                float _S266 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S264 + 4U)>>2]);
                float _S267 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S264 + 8U)>>2]);
                float _S268 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S264 + 12U)>>2]);
                finiteBoundary_0 = float4(_S265, _S266, _S267, _S268);
            }
            thread array<float3, int(5)> sampleOffsets_0;
            float3 _S269 = float3(0.0f, 0.0f, 0.0f);
            sampleOffsets_0[int(0)] = _S269;
            sampleOffsets_0[int(1)] = _S269;
            sampleOffsets_0[int(2)] = _S269;
            sampleOffsets_0[int(3)] = _S269;
            sampleOffsets_0[int(4)] = _S269;
            thread array<float3, int(5)> sampleEmitterNormals_0;
            float3 _S270 = - lightDirection_0;
            sampleEmitterNormals_0[int(0)] = _S270;
            sampleEmitterNormals_0[int(1)] = _S270;
            sampleEmitterNormals_0[int(2)] = _S270;
            sampleEmitterNormals_0[int(3)] = _S270;
            sampleEmitterNormals_0[int(4)] = _S270;
            bool _S271 = rayEntry_0 == 2.0f;
            bool _S272;
            if(_S271)
            {
                _S272 = lightRadius_0 > 0.0f;
            }
            else
            {
                _S272 = false;
            }
            float sampleCount_0;
            float shadowVisibility_0;
            float sampleIntensity_0;
            bool useEmitterShadowSamples_0;
            if(_S272)
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
                if(rayEntry_0 == 3.0f)
                {
                    float3 halfWidth_0 = lightTangentAxis_0 * float3((startDistance_1 * 0.5f)) ;
                    float3 halfHeight_0 = lightBitangentAxis_0 * float3((endDistance_1 * 0.5f)) ;
                    sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
                    sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
                    float3 _S273 = - halfWidth_0;
                    sampleOffsets_0[int(3)] = _S273 + halfHeight_0;
                    sampleOffsets_0[int(4)] = _S273 - halfHeight_0;
                    sampleCount_0 = 5.0f;
                }
                else
                {
                    if(rayEntry_0 == 4.0f)
                    {
                        sampleOffsets_0[int(1)] = lightTangentAxis_0 * float3(lightRadius_0) ;
                        sampleOffsets_0[int(2)] = - lightTangentAxis_0 * float3(lightRadius_0) ;
                        sampleOffsets_0[int(3)] = lightBitangentAxis_0 * float3(lightRadius_0) ;
                        sampleOffsets_0[int(4)] = - lightBitangentAxis_0 * float3(lightRadius_0) ;
                        float3 x_0 = normalize(lightTangentAxis_0);
                        float3 y_1 = normalize(lightBitangentAxis_0);
                        float3 _S274 = cross(y_1, lightDirection_0);
                        if((dot(x_0, _S274)) < 0.0f)
                        {
                            sampleCount_0 = -1.0f;
                        }
                        else
                        {
                            sampleCount_0 = 1.0f;
                        }
                        bool _S275 = (abs(0.0f)) > 0.5f;
                        float3 _S276 = float3(0.0f) ;
                        float3 _S277 = normalize((select(_S269, _S276 * _S274, _S275) + select(_S269, _S276 * cross(lightDirection_0, x_0), _S275) + select(_S269, float3(-1.0f)  * cross(x_0, y_1), (abs(-1.0f)) > 0.5f)) * float3(sampleCount_0) );
                        sampleEmitterNormals_0[int(0)] = _S277;
                        sampleEmitterNormals_0[int(1)] = _S277;
                        sampleEmitterNormals_0[int(2)] = _S277;
                        sampleEmitterNormals_0[int(3)] = _S277;
                        sampleEmitterNormals_0[int(4)] = _S277;
                        sampleCount_0 = 5.0f;
                    }
                    else
                    {
                        if(rayEntry_0 == 5.0f)
                        {
                            float3 halfLength_0 = lightDirectionAxis_0 * float3((startDistance_1 * 0.5f)) ;
                            if(lightRadius_0 > 0.0f)
                            {
                                useEmitterShadowSamples_0 = startDistance_1 > 0.0f;
                            }
                            else
                            {
                                useEmitterShadowSamples_0 = false;
                            }
                            if(useEmitterShadowSamples_0)
                            {
                                float3 _S278 = lightTangentAxis_0 * float3(lightRadius_0) ;
                                sampleOffsets_0[int(0)] = _S278 - halfLength_0;
                                sampleOffsets_0[int(1)] = _S278 + halfLength_0;
                                float3 _S279 = - lightTangentAxis_0 * float3(lightRadius_0) ;
                                sampleOffsets_0[int(2)] = _S279 - halfLength_0;
                                sampleOffsets_0[int(3)] = _S279 + halfLength_0;
                                sampleOffsets_0[int(4)] = lightBitangentAxis_0 * float3(lightRadius_0) ;
                                float3 x_1 = normalize(lightTangentAxis_0);
                                float3 y_2 = normalize(lightBitangentAxis_0);
                                float3 _S280 = cross(y_2, lightDirection_0);
                                bool _S281 = (dot(x_1, _S280)) < 0.0f;
                                if(_S281)
                                {
                                    sampleCount_0 = -1.0f;
                                }
                                else
                                {
                                    sampleCount_0 = 1.0f;
                                }
                                bool _S282 = (abs(1.0f)) > 0.5f;
                                bool _S283 = (abs(0.0f)) > 0.5f;
                                float3 _S284 = cross(lightDirection_0, x_1);
                                float3 _S285 = float3(0.0f) ;
                                float3 _S286 = _S285 * _S284;
                                float3 _S287 = _S285 * cross(x_1, y_2);
                                float3 _S288 = normalize((select(_S269, _S280, _S282) + select(_S269, _S286, _S283) + select(_S269, _S287, _S283)) * float3(sampleCount_0) );
                                sampleEmitterNormals_0[int(0)] = _S288;
                                sampleEmitterNormals_0[int(1)] = _S288;
                                if(_S281)
                                {
                                    shadowVisibility_0 = -1.0f;
                                }
                                else
                                {
                                    shadowVisibility_0 = 1.0f;
                                }
                                float3 _S289 = normalize((select(_S269, float3(-1.0f)  * _S280, (abs(-1.0f)) > 0.5f) + select(_S269, _S286, _S283) + select(_S269, _S287, _S283)) * float3(shadowVisibility_0) );
                                sampleEmitterNormals_0[int(2)] = _S289;
                                sampleEmitterNormals_0[int(3)] = _S289;
                                if(_S281)
                                {
                                    sampleIntensity_0 = -1.0f;
                                }
                                else
                                {
                                    sampleIntensity_0 = 1.0f;
                                }
                                float3 _S290 = normalize((select(_S269, _S285 * _S280, _S283) + select(_S269, _S284, _S282) + select(_S269, _S287, _S283)) * float3(sampleIntensity_0) );
                                sampleEmitterNormals_0[int(4)] = _S290;
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
            bool _S291;
            bool _S292;
            if(cameraInside_0)
            {
                uint _S293 = planeIndex_0 * 16U;
                uint _S294 = 14880U + _S293;
                float _S295 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S294)>>2]);
                float _S296 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S294 + 4U)>>2]);
                float _S297 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S294 + 8U)>>2]);
                float _S298 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S294 + 12U)>>2]);
                int shadowSlot_0 = int(_S295);
                if(shadowSlot_0 >= int(0))
                {
                    int shadowSlot_1;
                    if(_S296 == 6.0f)
                    {
                        y_0 = int(0);
                        shadowVisibility_0 = -1.0f;
                        shadowSlot_1 = shadowSlot_0;
                        for(;;)
                        {
                            if(y_0 < int(6))
                            {
                            }
                            else
                            {
                                break;
                            }
                            int _S299 = shadowSlot_0 + y_0;
                            uint _S300 = 12576U + uint(_S299) * 64U;
                            float _S301 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300)>>2]);
                            float _S302 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 4U)>>2]);
                            float _S303 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 8U)>>2]);
                            float _S304 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 12U)>>2]);
                            float4 _S305 = float4(_S301, _S302, _S303, _S304);
                            float _S306 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 16U)>>2]);
                            float _S307 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 20U)>>2]);
                            float _S308 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 24U)>>2]);
                            float _S309 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 28U)>>2]);
                            float4 _S310 = float4(_S306, _S307, _S308, _S309);
                            float _S311 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 32U)>>2]);
                            float _S312 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 36U)>>2]);
                            float _S313 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 40U)>>2]);
                            float _S314 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 44U)>>2]);
                            float4 _S315 = float4(_S311, _S312, _S313, _S314);
                            float _S316 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 48U)>>2]);
                            float _S317 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 52U)>>2]);
                            float _S318 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 56U)>>2]);
                            float _S319 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S300 + 60U)>>2]);
                            float _S320 = (((float4(worldPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S305, _S310, _S315, float4(_S316, _S317, _S318, _S319))))).w;
                            if(_S320 > shadowVisibility_0)
                            {
                                shadowVisibility_0 = _S320;
                                shadowSlot_1 = _S299;
                            }
                            y_0 = y_0 + int(1);
                        }
                    }
                    else
                    {
                        shadowSlot_1 = shadowSlot_0;
                    }
                    bool _S321;
                    int x_2;
                    if(_S296 == 5.0f)
                    {
                        step_0 = 0U;
                        for(;;)
                        {
                            if(step_0 < 5U)
                            {
                            }
                            else
                            {
                                break;
                            }
                            uint _S322 = 224U + _S293;
                            float _S323 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S322)>>2]);
                            float _S324 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S322 + 4U)>>2]);
                            float _S325 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S322 + 8U)>>2]);
                            float _S326 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S322 + 12U)>>2]);
                            float3 toShadowSample_0 = float3(_S323, _S324, _S325) + sampleOffsets_0[step_0] - worldPosition_0;
                            float3 shadowLightDirection_0 = toShadowSample_0 * float3(rsqrt(max(dot(toShadowSample_0, toShadowSample_0), 0.00100000004749745f))) ;
                            int _S327 = shadowSlot_1 + int(step_0);
                            for(;;)
                            {
                                uint _S328 = uint(_S327);
                                uint _S329 = _S328 * 16U;
                                uint _S330 = 14496U + _S329;
                                float _S331 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S330)>>2]);
                                float _S332 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S330 + 4U)>>2]);
                                float _S333 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S330 + 8U)>>2]);
                                float _S334 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S330 + 12U)>>2]);
                                uint _S335 = 14112U + _S329;
                                float _S336 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S335)>>2]);
                                float _S337 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S335 + 4U)>>2]);
                                float _S338 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S335 + 8U)>>2]);
                                float _S339 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S335 + 12U)>>2]);
                                if((dot(prefiltered_0, shadowLightDirection_0)) < 0.0f)
                                {
                                    sampleDirection_0 = - prefiltered_0;
                                }
                                else
                                {
                                    sampleDirection_0 = prefiltered_0;
                                }
                                float slope_2 = clamp(1.0f - saturate(dot(sampleDirection_0, shadowLightDirection_0)), 0.0f, 1.0f);
                                float3 offsetPosition_0 = worldPosition_0 + sampleDirection_0 * float3((_S332 * slope_2)) ;
                                uint _S340 = 12576U + _S328 * 64U;
                                float _S341 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340)>>2]);
                                float _S342 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 4U)>>2]);
                                float _S343 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 8U)>>2]);
                                float _S344 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 12U)>>2]);
                                float4 _S345 = float4(_S341, _S342, _S343, _S344);
                                float _S346 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 16U)>>2]);
                                float _S347 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 20U)>>2]);
                                float _S348 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 24U)>>2]);
                                float _S349 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 28U)>>2]);
                                float4 _S350 = float4(_S346, _S347, _S348, _S349);
                                float _S351 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 32U)>>2]);
                                float _S352 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 36U)>>2]);
                                float _S353 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 40U)>>2]);
                                float _S354 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 44U)>>2]);
                                float4 _S355 = float4(_S351, _S352, _S353, _S354);
                                float _S356 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 48U)>>2]);
                                float _S357 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 52U)>>2]);
                                float _S358 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 56U)>>2]);
                                float _S359 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S340 + 60U)>>2]);
                                float4 lightClip_0 = (((float4(offsetPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S345, _S350, _S355, float4(_S356, _S357, _S358, _S359)))));
                                float _S360 = lightClip_0.w;
                                if(_S360 <= 0.0f)
                                {
                                    shadowVisibility_0 = 1.0f;
                                    break;
                                }
                                float3 ndc_0 = lightClip_0.xyz / float3(_S360) ;
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
                                    _S291 = true;
                                }
                                else
                                {
                                    _S291 = (ndc_0.z) < 0.0f;
                                }
                                if(_S291)
                                {
                                    _S292 = true;
                                }
                                else
                                {
                                    _S292 = (ndc_0.z) > 1.0f;
                                }
                                if(_S292)
                                {
                                    shadowVisibility_0 = 1.0f;
                                    break;
                                }
                                float2 _S361 = float2(_S336, _S337);
                                float2 _S362 = float2(_S338, _S339);
                                float2 _S363 = _S361 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S362;
                                bool transmission_0 = _S333 < 0.0f;
                                if(transmission_0)
                                {
                                    shadowVisibility_0 = - _S333 - 1.0f;
                                }
                                else
                                {
                                    shadowVisibility_0 = max(_S333, 0.0f);
                                }
                                float _S364 = ndc_0.z - _S331 * (1.0f + 2.0f * slope_2);
                                float2 _S365 = float2((_S334 * 0.5f)) ;
                                float2 _S366 = _S361 + _S365;
                                float2 _S367 = _S361 + _S362 - _S365;
                                y_0 = int(-1);
                                sampleIntensity_0 = 0.0f;
                                for(;;)
                                {
                                    if(y_0 <= int(1))
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
                                        float2 coordinate_0 = clamp(_S363 + float2(float(x_2), float(y_0)) * float2((shadowVisibility_0 * _S334)) , _S366, _S367);
                                        if(_S364 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f))).x))
                                        {
                                            emissionScale_0 = 1.0f;
                                        }
                                        else
                                        {
                                            emissionScale_0 = 0.0f;
                                        }
                                        if(transmission_0)
                                        {
                                            _S321 = emissionScale_0 > 0.0f;
                                        }
                                        else
                                        {
                                            _S321 = false;
                                        }
                                        if(_S321)
                                        {
                                            float2 layer0_0 = (((&kernelContext_0)->shadowTransmission0_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                            float2 layer1_0 = (((&kernelContext_0)->shadowTransmission1_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                            float2 layer2_0 = (((&kernelContext_0)->shadowTransmission2_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                            float2 layer3_0 = (((&kernelContext_0)->shadowTransmission3_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_0), level((0.0f)))).xy;
                                            float4 depths_0 = float4(layer0_0.x, layer1_0.x, layer2_0.x, layer3_0.x);
                                            float4 weights_0 = select(float4(1.0f, 1.0f, 1.0f, 1.0f), float4(layer0_0.y, layer1_0.y, layer2_0.y, layer3_0.y), (depths_0 >= (float4(0.0f) )) & ((float4(_S364) ) > depths_0));
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
                                    y_0 = y_0 + int(1);
                                }
                                shadowVisibility_0 = sampleIntensity_0 * 0.1111111119389534f;
                                break;
                            }
                            shadowVisibilityBySample_0[step_0] = shadowVisibility_0;
                            step_0 = step_0 + 1U;
                        }
                        useEmitterShadowSamples_0 = true;
                        shadowVisibility_0 = 1.0f;
                    }
                    else
                    {
                        if(_S271)
                        {
                            uint _S368 = 224U + _S293;
                            float _S369 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S368)>>2]);
                            float _S370 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S368 + 4U)>>2]);
                            float _S371 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S368 + 8U)>>2]);
                            float _S372 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S368 + 12U)>>2]);
                            float3 toShadowLight_0 = float3(_S369, _S370, _S371) - worldPosition_0;
                            color_3 = toShadowLight_0 * float3(rsqrt(max(dot(toShadowLight_0, toShadowLight_0), 0.00100000004749745f))) ;
                        }
                        else
                        {
                            color_3 = lightDirection_0;
                        }
                        for(;;)
                        {
                            uint _S373 = uint(shadowSlot_1);
                            uint _S374 = _S373 * 16U;
                            uint _S375 = 14496U + _S374;
                            float _S376 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S375)>>2]);
                            float _S377 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S375 + 4U)>>2]);
                            float _S378 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S375 + 8U)>>2]);
                            float _S379 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S375 + 12U)>>2]);
                            uint _S380 = 14112U + _S374;
                            float _S381 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S380)>>2]);
                            float _S382 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S380 + 4U)>>2]);
                            float _S383 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S380 + 8U)>>2]);
                            float _S384 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S380 + 12U)>>2]);
                            if((dot(prefiltered_0, color_3)) < 0.0f)
                            {
                                sampleDirection_0 = - prefiltered_0;
                            }
                            else
                            {
                                sampleDirection_0 = prefiltered_0;
                            }
                            float slope_3 = clamp(1.0f - saturate(dot(sampleDirection_0, color_3)), 0.0f, 1.0f);
                            float3 offsetPosition_1 = worldPosition_0 + sampleDirection_0 * float3((_S377 * slope_3)) ;
                            uint _S385 = 12576U + _S373 * 64U;
                            float _S386 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385)>>2]);
                            float _S387 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 4U)>>2]);
                            float _S388 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 8U)>>2]);
                            float _S389 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 12U)>>2]);
                            float4 _S390 = float4(_S386, _S387, _S388, _S389);
                            float _S391 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 16U)>>2]);
                            float _S392 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 20U)>>2]);
                            float _S393 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 24U)>>2]);
                            float _S394 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 28U)>>2]);
                            float4 _S395 = float4(_S391, _S392, _S393, _S394);
                            float _S396 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 32U)>>2]);
                            float _S397 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 36U)>>2]);
                            float _S398 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 40U)>>2]);
                            float _S399 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 44U)>>2]);
                            float4 _S400 = float4(_S396, _S397, _S398, _S399);
                            float _S401 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 48U)>>2]);
                            float _S402 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 52U)>>2]);
                            float _S403 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 56U)>>2]);
                            float _S404 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S385 + 60U)>>2]);
                            float4 lightClip_1 = (((float4(offsetPosition_1, 1.0f)) * (matrix<float,int(4),int(4)> (_S390, _S395, _S400, float4(_S401, _S402, _S403, _S404)))));
                            float _S405 = lightClip_1.w;
                            if(_S405 <= 0.0f)
                            {
                                shadowVisibility_0 = 1.0f;
                                break;
                            }
                            float3 ndc_1 = lightClip_1.xyz / float3(_S405) ;
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
                                _S291 = true;
                            }
                            else
                            {
                                _S291 = (ndc_1.z) < 0.0f;
                            }
                            if(_S291)
                            {
                                _S292 = true;
                            }
                            else
                            {
                                _S292 = (ndc_1.z) > 1.0f;
                            }
                            if(_S292)
                            {
                                shadowVisibility_0 = 1.0f;
                                break;
                            }
                            float2 _S406 = float2(_S381, _S382);
                            float2 _S407 = float2(_S383, _S384);
                            float2 _S408 = _S406 + (ndc_1.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S407;
                            bool transmission_1 = _S378 < 0.0f;
                            if(transmission_1)
                            {
                                shadowVisibility_0 = - _S378 - 1.0f;
                            }
                            else
                            {
                                shadowVisibility_0 = max(_S378, 0.0f);
                            }
                            float _S409 = ndc_1.z - _S376 * (1.0f + 2.0f * slope_3);
                            float2 _S410 = float2((_S379 * 0.5f)) ;
                            float2 _S411 = _S406 + _S410;
                            float2 _S412 = _S406 + _S407 - _S410;
                            y_0 = int(-1);
                            sampleIntensity_0 = 0.0f;
                            for(;;)
                            {
                                if(y_0 <= int(1))
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
                                    float2 coordinate_1 = clamp(_S408 + float2(float(x_2), float(y_0)) * float2((shadowVisibility_0 * _S379)) , _S411, _S412);
                                    if(_S409 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f))).x))
                                    {
                                        emissionScale_0 = 1.0f;
                                    }
                                    else
                                    {
                                        emissionScale_0 = 0.0f;
                                    }
                                    if(transmission_1)
                                    {
                                        _S321 = emissionScale_0 > 0.0f;
                                    }
                                    else
                                    {
                                        _S321 = false;
                                    }
                                    if(_S321)
                                    {
                                        float2 layer0_1 = (((&kernelContext_0)->shadowTransmission0_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                        float2 layer1_1 = (((&kernelContext_0)->shadowTransmission1_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                        float2 layer2_1 = (((&kernelContext_0)->shadowTransmission2_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                        float2 layer3_1 = (((&kernelContext_0)->shadowTransmission3_0).sample(((&kernelContext_0)->shadowSampler_0), (coordinate_1), level((0.0f)))).xy;
                                        float4 depths_1 = float4(layer0_1.x, layer1_1.x, layer2_1.x, layer3_1.x);
                                        float4 weights_1 = select(float4(1.0f, 1.0f, 1.0f, 1.0f), float4(layer0_1.y, layer1_1.y, layer2_1.y, layer3_1.y), (depths_1 >= (float4(0.0f) )) & ((float4(_S409) ) > depths_1));
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
                                y_0 = y_0 + int(1);
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
            step_0 = 0U;
            color_3 = color_2;
            for(;;)
            {
                if(step_0 < 5U)
                {
                }
                else
                {
                    break;
                }
                if(float(step_0) >= sampleCount_0)
                {
                    step_0 = step_0 + 1U;
                    continue;
                }
                float sampleIntensity_1 = rayExit_0 / sampleCount_0;
                if(rayEntry_0 >= 2.0f)
                {
                    uint _S413 = 224U + planeIndex_0 * 16U;
                    float _S414 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S413)>>2]);
                    float _S415 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S413 + 4U)>>2]);
                    float _S416 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S413 + 8U)>>2]);
                    float _S417 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S413 + 12U)>>2]);
                    float3 toLight_0 = float3(_S414, _S415, _S416) + sampleOffsets_0[step_0] - worldPosition_0;
                    float _S418 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                    float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S418)) ;
                    float sampleIntensity_2 = sampleIntensity_1 / _S418;
                    float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                    if(rayEntry_0 == 4.0f)
                    {
                        _S291 = true;
                    }
                    else
                    {
                        if(rayEntry_0 == 5.0f)
                        {
                            _S291 = lightRadius_0 > 0.0f;
                        }
                        else
                        {
                            _S291 = false;
                        }
                        if(_S291)
                        {
                            _S292 = startDistance_1 > 0.0f;
                        }
                        else
                        {
                            _S292 = false;
                        }
                        _S291 = _S292;
                    }
                    if(_S291)
                    {
                        sampleIntensity_0 = saturate(dot(sampleEmitterNormals_0[step_0], - sampleDirection_1));
                    }
                    else
                    {
                        sampleIntensity_0 = 1.0f;
                    }
                    if((finiteBoundary_0.z) > 0.5f)
                    {
                        if(emitterCosine_0 < (finiteBoundary_0.x))
                        {
                            emissionScale_0 = 0.0f;
                        }
                        else
                        {
                            float _S419 = finiteBoundary_0.y;
                            if(_S419 > 0.0f)
                            {
                                emissionScale_0 = pow(max(emitterCosine_0, 0.0f), _S419);
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
                float _S420 = pow(max(0.0f, 1.0f - saturate(dot(lightEyeBase_0, half_0))), 5.0f);
                float3 diffuseFresnel_0;
                if(omniPbrSpecular_0)
                {
                    diffuseFresnel_0 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S420)) ;
                }
                else
                {
                    diffuseFresnel_0 = mix(normalIncidence_0, grazingIncidence_0, float3(_S420) );
                }
                float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_0);
                float3 _S421 = float3(_S420) ;
                float _S422 = max(_S177, 0.00100000004749745f);
                float alpha_0 = _S422 * _S422;
                float alphaSquared_0 = alpha_0 * alpha_0;
                float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
                float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
                float k_0 = alpha_0 * 0.5f;
                float _S423 = 1.0f - k_0;
                float3 _S424 = float3(max(4.0f * normalDotLight_0 * _S262, 1.00000000317107685e-30f)) ;
                float3 _S425 = mix(normalIncidence_0, grazingIncidence_0, _S421) * float3((_S262 / (_S262 * _S423 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S423 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S424;
                float3 directSpecular_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    float _S426 = max(_S178, 0.00100000004749745f);
                    float alpha_1 = _S426 * _S426;
                    float alphaSquared_1 = alpha_1 * alpha_1;
                    float denominator_2 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                    float k_1 = alpha_1 * 0.5f;
                    float _S427 = 1.0f - k_1;
                    directSpecular_0 = _S425 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S421) * float3((_S262 / (_S262 * _S427 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S427 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_2 * denominator_2, 1.00000000317107685e-30f)))  / _S424);
                }
                else
                {
                    directSpecular_0 = _S425;
                }
                float3 _S428 = diffuseColor_0 * float3(sampleIntensity_0) ;
                float3 diffuseIrradiance_0 = _S428 * _S179;
                if(useEmitterShadowSamples_0)
                {
                    sampleShadowVisibility_0 = shadowVisibilityBySample_0[step_0];
                }
                else
                {
                    sampleShadowVisibility_0 = shadowVisibility_0;
                }
                color_3 = color_3 + float3((sampleShadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(startDistance_0)  * diffuseIrradiance_0 + directSpecular_0 * float3(endDistance_0)  * _S428);
                step_0 = step_0 + 1U;
            }
            color_2 = color_3;
            planeIndex_0 = planeIndex_0 + 1U;
        }
        FragmentFrameParameters_0 _S429 = _S2;
        if((_S2.environmentControls_0.x) >= 0.5f)
        {
            float _S430 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
            float _S431 = saturate(_S177);
            float2 _S432 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S430, _S431)), level((0.0f)))).xy;
            float _S433 = _S432.x;
            float _S434 = _S432.y;
            float3 specularWeight_0 = normalIncidence_0 * float3(_S433)  + grazingIncidence_0 * float3(_S434) ;
            if(omniPbrSpecular_0)
            {
                unlitColor_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S433 + _S434)) ;
            }
            else
            {
                unlitColor_0 = specularWeight_0;
            }
            float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - unlitColor_0);
            float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
            float _S435 = max(_S190.domeControls_0.y, 1.0f);
            float3 _S436 = float3(0.0f, 0.0f, 0.0f);
            if(allDomesLinked_0)
            {
                cameraInside_0 = true;
            }
            else
            {
                cameraInside_0 = _S435 <= 1.0f;
            }
            if(cameraInside_0)
            {
                float composedGroup_0 = _S190.domeControls_0.z;
                float _S437 = _S190.domeControls_0.w;
                for(;;)
                {
                    bool _S438 = _S435 <= 1.0f;
                    _S4 = _S438;
                    if(_S438)
                    {
                        float3 unit_0 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_1 = normalize(worldNormal_2);
                    float inset_0 = 0.5f / max(_S437, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S435)), level((0.0f)))).xyz;
                    break;
                }
                float _S439 = _S429.environmentControls_0.y;
                float _S440 = _S429.environmentControls_0.z;
                for(;;)
                {
                    if(_S4)
                    {
                        float3 unit_2 = normalize(reflectionDirection_0);
                        float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S441 = max(_S439, 1.0f);
                        float _S442 = _S441 - 1.0f;
                        float slice_0 = _S431 * max(_S442, 0.0f);
                        float lower_0 = floor(slice_0);
                        float inset_1 = 0.5f / max(_S440, 1.0f);
                        float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S441)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S442) + v_0) / _S441)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                        break;
                    }
                    float3 unit_3 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S443 = max(_S439, 1.0f);
                    float total_0 = _S443 * _S435;
                    float _S444 = _S443 - 1.0f;
                    float slice_1 = _S431 * max(_S444, 0.0f);
                    float lower_1 = floor(slice_1);
                    float inset_2 = 0.5f / max(_S440, 1.0f);
                    float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                    float base_0 = composedGroup_0 * _S443;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S444) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                    break;
                }
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S4)
                        {
                            float3 unit_4 = normalize(reflectionDirection_0);
                            float u_2 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S445 = max(_S439, 1.0f);
                            float _S446 = _S445 - 1.0f;
                            float slice_2 = saturate(_S178) * max(_S446, 0.0f);
                            float lower_2 = floor(slice_2);
                            float inset_3 = 0.5f / max(_S440, 1.0f);
                            float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                            irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S445)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S446) + v_2) / _S445)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                            break;
                        }
                        float3 unit_5 = normalize(reflectionDirection_0);
                        float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S447 = max(_S439, 1.0f);
                        float total_1 = _S447 * _S435;
                        float _S448 = _S447 - 1.0f;
                        float slice_3 = saturate(_S178) * max(_S448, 0.0f);
                        float lower_3 = floor(slice_3);
                        float inset_4 = 0.5f / max(_S440, 1.0f);
                        float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                        float base_1 = composedGroup_0 * _S447;
                        irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S448) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                        break;
                    }
                    lightDirectionAxis_0 = irradiance_0;
                }
                else
                {
                    lightDirectionAxis_0 = _S436;
                }
                irradiance_0 = diffuseColor_0;
                prefiltered_0 = unlitColor_0;
            }
            else
            {
                step_0 = 0U;
                irradiance_0 = _S436;
                prefiltered_0 = _S436;
                lightDirectionAxis_0 = _S436;
                for(;;)
                {
                    if(step_0 < _S191)
                    {
                    }
                    else
                    {
                        break;
                    }
                    if((domeLinkMask_0 & (1U << step_0)) == 0U)
                    {
                        step_0 = step_0 + 1U;
                        continue;
                    }
                    float domeGroup_0 = _S2.domeEnvironment_0[step_0].x;
                    if(domeGroup_0 < 0.0f)
                    {
                        step_0 = step_0 + 1U;
                        continue;
                    }
                    float _S449 = _S190.domeControls_0.w;
                    for(;;)
                    {
                        bool _S450 = _S435 <= 1.0f;
                        _S5 = _S450;
                        if(_S450)
                        {
                            float3 unit_6 = normalize(worldNormal_2);
                            diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                            break;
                        }
                        float3 unit_7 = normalize(worldNormal_2);
                        float inset_5 = 0.5f / max(_S449, 1.0f);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S435)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                    float _S451 = _S429.environmentControls_0.y;
                    float _S452 = _S429.environmentControls_0.z;
                    for(;;)
                    {
                        if(_S5)
                        {
                            float3 unit_8 = normalize(reflectionDirection_0);
                            float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S453 = max(_S451, 1.0f);
                            float _S454 = _S453 - 1.0f;
                            float slice_4 = _S431 * max(_S454, 0.0f);
                            float lower_4 = floor(slice_4);
                            float inset_6 = 0.5f / max(_S452, 1.0f);
                            float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                            unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S453)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S454) + v_4) / _S453)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                            break;
                        }
                        float3 unit_9 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S455 = max(_S451, 1.0f);
                        float total_2 = _S455 * _S435;
                        float _S456 = _S455 - 1.0f;
                        float slice_5 = _S431 * max(_S456, 0.0f);
                        float lower_5 = floor(slice_5);
                        float inset_7 = 0.5f / max(_S452, 1.0f);
                        float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                        float base_2 = domeGroup_0 * _S455;
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S456) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                        break;
                    }
                    float3 prefiltered_1 = prefiltered_0 + unlitColor_0;
                    if(clearcoatAmount_0 > 0.0f)
                    {
                        for(;;)
                        {
                            if(_S5)
                            {
                                float3 unit_10 = normalize(reflectionDirection_0);
                                float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                                float _S457 = max(_S451, 1.0f);
                                float _S458 = _S457 - 1.0f;
                                float slice_6 = saturate(_S178) * max(_S458, 0.0f);
                                float lower_6 = floor(slice_6);
                                float inset_8 = 0.5f / max(_S452, 1.0f);
                                float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                                normalIncidence_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S457)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S458) + v_6) / _S457)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                                break;
                            }
                            float3 unit_11 = normalize(reflectionDirection_0);
                            float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S459 = max(_S451, 1.0f);
                            float total_3 = _S459 * _S435;
                            float _S460 = _S459 - 1.0f;
                            float slice_7 = saturate(_S178) * max(_S460, 0.0f);
                            float lower_7 = floor(slice_7);
                            float inset_9 = 0.5f / max(_S452, 1.0f);
                            float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                            float base_3 = domeGroup_0 * _S459;
                            normalIncidence_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S460) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                            break;
                        }
                        lightTangentAxis_0 = lightDirectionAxis_0 + normalIncidence_0;
                    }
                    else
                    {
                        lightTangentAxis_0 = lightDirectionAxis_0;
                    }
                    irradiance_0 = irradiance_1;
                    prefiltered_0 = prefiltered_1;
                    lightDirectionAxis_0 = lightTangentAxis_0;
                    step_0 = step_0 + 1U;
                }
            }
            float3 _S461 = float3(occlusion_0) ;
            float3 color_4 = color_2 + _S461 * diffuse_1 * irradiance_0 * diffuseWeight_0 + _S461 * prefiltered_0 * specularWeight_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float2 _S462 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S430, saturate(_S178))), level((0.0f)))).xy;
                color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightDirectionAxis_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S462.x)  + float3(_S462.y) );
            }
            else
            {
                color_2 = color_4;
            }
        }
        float3 color_5 = (color_2 + emissiveColor_0) * float3(exp2((as_type<float>((_S2.clipPlaneCount_0.z))))) ;
        if((_S2.clipPlaneCount_0.y) == 1U)
        {
            color_2 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
        }
        else
        {
            color_2 = color_5;
        }
        float4 _S463 = float4(color_2, opacity_0);
        opacity_0 = _S149;
        finiteBoundary_0 = _S463;
        break;
    }
    volumeFragmentMain_Result_0 _S464 = { finiteBoundary_0, opacity_0 };
    return _S464;
}
