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

[[fragment]] pixelOutput_0 volumeFragmentMain(pixelInput_0 _S6 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture3d<float, access::sample> volumeDensityTexture_1 [[texture(9)]], sampler volumeDensitySampler_1 [[sampler(4)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_1;
    FragmentFrameParameters_0 _S7;
    uint4 _S8;
    float _S9;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    bool _S10;
    bool _S11;
    thread KernelContext_0 kernelContext_1;
    (&kernelContext_1)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_1)->frameParameters_0 = frameParameters_1;
    (&kernelContext_1)->volumeDensityTexture_0 = volumeDensityTexture_1;
    (&kernelContext_1)->volumeDensitySampler_0 = volumeDensitySampler_1;
    (&kernelContext_1)->shadowAtlas_0 = shadowAtlas_1;
    (&kernelContext_1)->shadowSampler_0 = shadowSampler_1;
    (&kernelContext_1)->environmentBrdf_0 = environmentBrdf_1;
    (&kernelContext_1)->environmentBrdfSampler_0 = environmentBrdfSampler_1;
    (&kernelContext_1)->environmentIrradiance_0 = environmentIrradiance_1;
    (&kernelContext_1)->environmentSampler_0 = environmentSampler_1;
    (&kernelContext_1)->environmentSpecular_0 = environmentSpecular_1;
    SurfaceParameters_natural_0 device* _S12 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        uint _S13 = as_type<uint>((&kernelContext_1)->frameParameters_0[(64U)>>2]);
        uint _S14 = as_type<uint>((&kernelContext_1)->frameParameters_0[(68U)>>2]);
        uint _S15 = as_type<uint>((&kernelContext_1)->frameParameters_0[(72U)>>2]);
        uint _S16 = as_type<uint>((&kernelContext_1)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S13, _S14, _S15, _S16);
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
            uint _S17 = 80U + index_1 * 16U;
            float _S18 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S17)>>2]);
            float _S19 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S17 + 4U)>>2]);
            float _S20 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S17 + 8U)>>2]);
            float _S21 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S17 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_1] = float4(_S18, _S19, _S20, _S21);
            index_1 = index_1 + 1U;
        }
        float _S22 = as_type<float>((&kernelContext_1)->frameParameters_0[(208U)>>2]);
        float _S23 = as_type<float>((&kernelContext_1)->frameParameters_0[(212U)>>2]);
        float _S24 = as_type<float>((&kernelContext_1)->frameParameters_0[(216U)>>2]);
        float _S25 = as_type<float>((&kernelContext_1)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S22, _S23, _S24, _S25);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 12960U;
        float _S26 = as_type<float>((&kernelContext_1)->frameParameters_0[(12512U)>>2]);
        float _S27 = as_type<float>((&kernelContext_1)->frameParameters_0[(12516U)>>2]);
        float _S28 = as_type<float>((&kernelContext_1)->frameParameters_0[(12520U)>>2]);
        float _S29 = as_type<float>((&kernelContext_1)->frameParameters_0[(12524U)>>2]);
        float4 _S30 = float4(_S26, _S27, _S28, _S29);
        float _S31 = as_type<float>((&kernelContext_1)->frameParameters_0[(12528U)>>2]);
        float _S32 = as_type<float>((&kernelContext_1)->frameParameters_0[(12532U)>>2]);
        float _S33 = as_type<float>((&kernelContext_1)->frameParameters_0[(12536U)>>2]);
        float _S34 = as_type<float>((&kernelContext_1)->frameParameters_0[(12540U)>>2]);
        float4 _S35 = float4(_S31, _S32, _S33, _S34);
        float _S36 = as_type<float>((&kernelContext_1)->frameParameters_0[(12544U)>>2]);
        float _S37 = as_type<float>((&kernelContext_1)->frameParameters_0[(12548U)>>2]);
        float _S38 = as_type<float>((&kernelContext_1)->frameParameters_0[(12552U)>>2]);
        float _S39 = as_type<float>((&kernelContext_1)->frameParameters_0[(12556U)>>2]);
        float4 _S40 = float4(_S36, _S37, _S38, _S39);
        float _S41 = as_type<float>((&kernelContext_1)->frameParameters_0[(12560U)>>2]);
        float _S42 = as_type<float>((&kernelContext_1)->frameParameters_0[(12564U)>>2]);
        float _S43 = as_type<float>((&kernelContext_1)->frameParameters_0[(12568U)>>2]);
        float _S44 = as_type<float>((&kernelContext_1)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S30, _S35, _S40, float4(_S41, _S42, _S43, _S44));
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
            uint _S45 = 12576U + index_1 * 64U;
            float _S46 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45)>>2]);
            float _S47 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 4U)>>2]);
            float _S48 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 8U)>>2]);
            float _S49 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 12U)>>2]);
            float4 _S50 = float4(_S46, _S47, _S48, _S49);
            float _S51 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 16U)>>2]);
            float _S52 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 20U)>>2]);
            float _S53 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 24U)>>2]);
            float _S54 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 28U)>>2]);
            float4 _S55 = float4(_S51, _S52, _S53, _S54);
            float _S56 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 32U)>>2]);
            float _S57 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 36U)>>2]);
            float _S58 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 40U)>>2]);
            float _S59 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 44U)>>2]);
            float4 _S60 = float4(_S56, _S57, _S58, _S59);
            float _S61 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 48U)>>2]);
            float _S62 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 52U)>>2]);
            float _S63 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 56U)>>2]);
            float _S64 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S45 + 60U)>>2]);
            (&frame_0)->shadowWorldToLightClip_0[index_1] = matrix<float,int(4),int(4)> (_S50, _S55, _S60, float4(_S61, _S62, _S63, _S64));
            uint _S65 = index_1 * 16U;
            uint _S66 = 12832U + _S65;
            float _S67 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S66)>>2]);
            float _S68 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S66 + 4U)>>2]);
            float _S69 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S66 + 8U)>>2]);
            float _S70 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S66 + 12U)>>2]);
            (&frame_0)->shadowTile_0[index_1] = float4(_S67, _S68, _S69, _S70);
            uint _S71 = 12896U + _S65;
            float _S72 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S71)>>2]);
            float _S73 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S71 + 4U)>>2]);
            float _S74 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S71 + 8U)>>2]);
            float _S75 = as_type<float>((&kernelContext_1)->frameParameters_0[(_S71 + 12U)>>2]);
            (&frame_0)->shadowControls_0[index_1] = float4(_S72, _S73, _S74, _S75);
            index_1 = index_1 + 1U;
        }
        float _S76 = as_type<float>((&kernelContext_1)->frameParameters_0[(15008U)>>2]);
        float _S77 = as_type<float>((&kernelContext_1)->frameParameters_0[(15012U)>>2]);
        float _S78 = as_type<float>((&kernelContext_1)->frameParameters_0[(15016U)>>2]);
        float _S79 = as_type<float>((&kernelContext_1)->frameParameters_0[(15020U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S76, _S77, _S78, _S79);
        float _S80 = as_type<float>((&kernelContext_1)->frameParameters_0[(15024U)>>2]);
        float _S81 = as_type<float>((&kernelContext_1)->frameParameters_0[(15028U)>>2]);
        float _S82 = as_type<float>((&kernelContext_1)->frameParameters_0[(15032U)>>2]);
        float _S83 = as_type<float>((&kernelContext_1)->frameParameters_0[(15036U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S80, _S81, _S82, _S83);
        float _S84 = as_type<float>((&kernelContext_1)->frameParameters_0[(15040U)>>2]);
        float _S85 = as_type<float>((&kernelContext_1)->frameParameters_0[(15044U)>>2]);
        float _S86 = as_type<float>((&kernelContext_1)->frameParameters_0[(15048U)>>2]);
        float _S87 = as_type<float>((&kernelContext_1)->frameParameters_0[(15052U)>>2]);
        float4 _S88 = float4(_S84, _S85, _S86, _S87);
        float _S89 = as_type<float>((&kernelContext_1)->frameParameters_0[(15056U)>>2]);
        float _S90 = as_type<float>((&kernelContext_1)->frameParameters_0[(15060U)>>2]);
        float _S91 = as_type<float>((&kernelContext_1)->frameParameters_0[(15064U)>>2]);
        float _S92 = as_type<float>((&kernelContext_1)->frameParameters_0[(15068U)>>2]);
        float4 _S93 = float4(_S89, _S90, _S91, _S92);
        float _S94 = as_type<float>((&kernelContext_1)->frameParameters_0[(15072U)>>2]);
        float _S95 = as_type<float>((&kernelContext_1)->frameParameters_0[(15076U)>>2]);
        float _S96 = as_type<float>((&kernelContext_1)->frameParameters_0[(15080U)>>2]);
        float _S97 = as_type<float>((&kernelContext_1)->frameParameters_0[(15084U)>>2]);
        float4 _S98 = float4(_S94, _S95, _S96, _S97);
        float _S99 = as_type<float>((&kernelContext_1)->frameParameters_0[(15088U)>>2]);
        float _S100 = as_type<float>((&kernelContext_1)->frameParameters_0[(15092U)>>2]);
        float _S101 = as_type<float>((&kernelContext_1)->frameParameters_0[(15096U)>>2]);
        float _S102 = as_type<float>((&kernelContext_1)->frameParameters_0[(15100U)>>2]);
        float4 _S103 = float4(_S99, _S100, _S101, _S102);
        float _S104 = as_type<float>((&kernelContext_1)->frameParameters_0[(15104U)>>2]);
        float _S105 = as_type<float>((&kernelContext_1)->frameParameters_0[(15108U)>>2]);
        float _S106 = as_type<float>((&kernelContext_1)->frameParameters_0[(15112U)>>2]);
        float _S107 = as_type<float>((&kernelContext_1)->frameParameters_0[(15116U)>>2]);
        float4 _S108 = float4(_S104, _S105, _S106, _S107);
        float _S109 = as_type<float>((&kernelContext_1)->frameParameters_0[(15120U)>>2]);
        float _S110 = as_type<float>((&kernelContext_1)->frameParameters_0[(15124U)>>2]);
        float _S111 = as_type<float>((&kernelContext_1)->frameParameters_0[(15128U)>>2]);
        float _S112 = as_type<float>((&kernelContext_1)->frameParameters_0[(15132U)>>2]);
        float4 _S113 = float4(_S109, _S110, _S111, _S112);
        float _S114 = as_type<float>((&kernelContext_1)->frameParameters_0[(15136U)>>2]);
        float _S115 = as_type<float>((&kernelContext_1)->frameParameters_0[(15140U)>>2]);
        float _S116 = as_type<float>((&kernelContext_1)->frameParameters_0[(15144U)>>2]);
        float _S117 = as_type<float>((&kernelContext_1)->frameParameters_0[(15148U)>>2]);
        float4 _S118 = float4(_S114, _S115, _S116, _S117);
        float _S119 = as_type<float>((&kernelContext_1)->frameParameters_0[(15152U)>>2]);
        float _S120 = as_type<float>((&kernelContext_1)->frameParameters_0[(15156U)>>2]);
        float _S121 = as_type<float>((&kernelContext_1)->frameParameters_0[(15160U)>>2]);
        float _S122 = as_type<float>((&kernelContext_1)->frameParameters_0[(15164U)>>2]);
        float4 _S123 = float4(_S119, _S120, _S121, _S122);
        float _S124 = as_type<float>((&kernelContext_1)->frameParameters_0[(15168U)>>2]);
        float _S125 = as_type<float>((&kernelContext_1)->frameParameters_0[(15172U)>>2]);
        float _S126 = as_type<float>((&kernelContext_1)->frameParameters_0[(15176U)>>2]);
        float _S127 = as_type<float>((&kernelContext_1)->frameParameters_0[(15180U)>>2]);
        float4 _S128 = float4(_S124, _S125, _S126, _S127);
        float _S129 = as_type<float>((&kernelContext_1)->frameParameters_0[(15184U)>>2]);
        float _S130 = as_type<float>((&kernelContext_1)->frameParameters_0[(15188U)>>2]);
        float _S131 = as_type<float>((&kernelContext_1)->frameParameters_0[(15192U)>>2]);
        float _S132 = as_type<float>((&kernelContext_1)->frameParameters_0[(15196U)>>2]);
        float4 _S133 = float4(_S129, _S130, _S131, _S132);
        float _S134 = as_type<float>((&kernelContext_1)->frameParameters_0[(15200U)>>2]);
        float _S135 = as_type<float>((&kernelContext_1)->frameParameters_0[(15204U)>>2]);
        float _S136 = as_type<float>((&kernelContext_1)->frameParameters_0[(15208U)>>2]);
        float _S137 = as_type<float>((&kernelContext_1)->frameParameters_0[(15212U)>>2]);
        float4 _S138 = float4(_S134, _S135, _S136, _S137);
        float _S139 = as_type<float>((&kernelContext_1)->frameParameters_0[(15216U)>>2]);
        float _S140 = as_type<float>((&kernelContext_1)->frameParameters_0[(15220U)>>2]);
        float _S141 = as_type<float>((&kernelContext_1)->frameParameters_0[(15224U)>>2]);
        float _S142 = as_type<float>((&kernelContext_1)->frameParameters_0[(15228U)>>2]);
        float4 _S143 = float4(_S139, _S140, _S141, _S142);
        float _S144 = as_type<float>((&kernelContext_1)->frameParameters_0[(15232U)>>2]);
        float _S145 = as_type<float>((&kernelContext_1)->frameParameters_0[(15236U)>>2]);
        float _S146 = as_type<float>((&kernelContext_1)->frameParameters_0[(15240U)>>2]);
        float _S147 = as_type<float>((&kernelContext_1)->frameParameters_0[(15244U)>>2]);
        float4 _S148 = float4(_S144, _S145, _S146, _S147);
        float _S149 = as_type<float>((&kernelContext_1)->frameParameters_0[(15248U)>>2]);
        float _S150 = as_type<float>((&kernelContext_1)->frameParameters_0[(15252U)>>2]);
        float _S151 = as_type<float>((&kernelContext_1)->frameParameters_0[(15256U)>>2]);
        float _S152 = as_type<float>((&kernelContext_1)->frameParameters_0[(15260U)>>2]);
        float4 _S153 = float4(_S149, _S150, _S151, _S152);
        float _S154 = as_type<float>((&kernelContext_1)->frameParameters_0[(15264U)>>2]);
        float _S155 = as_type<float>((&kernelContext_1)->frameParameters_0[(15268U)>>2]);
        float _S156 = as_type<float>((&kernelContext_1)->frameParameters_0[(15272U)>>2]);
        float _S157 = as_type<float>((&kernelContext_1)->frameParameters_0[(15276U)>>2]);
        float4 _S158 = float4(_S154, _S155, _S156, _S157);
        float _S159 = as_type<float>((&kernelContext_1)->frameParameters_0[(15280U)>>2]);
        float _S160 = as_type<float>((&kernelContext_1)->frameParameters_0[(15284U)>>2]);
        float _S161 = as_type<float>((&kernelContext_1)->frameParameters_0[(15288U)>>2]);
        float _S162 = as_type<float>((&kernelContext_1)->frameParameters_0[(15292U)>>2]);
        float4 _S163 = float4(_S159, _S160, _S161, _S162);
        (&frame_0)->domeAmbient_0[int(0)] = _S88;
        (&frame_0)->domeAmbient_0[int(1)] = _S93;
        (&frame_0)->domeAmbient_0[int(2)] = _S98;
        (&frame_0)->domeAmbient_0[int(3)] = _S103;
        (&frame_0)->domeAmbient_0[int(4)] = _S108;
        (&frame_0)->domeAmbient_0[int(5)] = _S113;
        (&frame_0)->domeAmbient_0[int(6)] = _S118;
        (&frame_0)->domeAmbient_0[int(7)] = _S123;
        (&frame_0)->domeEnvironment_0[int(0)] = _S128;
        (&frame_0)->domeEnvironment_0[int(1)] = _S133;
        (&frame_0)->domeEnvironment_0[int(2)] = _S138;
        (&frame_0)->domeEnvironment_0[int(3)] = _S143;
        (&frame_0)->domeEnvironment_0[int(4)] = _S148;
        (&frame_0)->domeEnvironment_0[int(5)] = _S153;
        (&frame_0)->domeEnvironment_0[int(6)] = _S158;
        (&frame_0)->domeEnvironment_0[int(7)] = _S163;
        _S7 = frame_0;
        break;
    }
    for(;;)
    {
        _S8 = _S7.clipPlaneCount_0;
        uint _S164 = min(_S7.clipPlaneCount_0.x, 8U);
        index_1 = 0U;
        for(;;)
        {
            if(index_1 < _S164)
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
    float4 _S165 = float4(_S12->clearcoatShaded_0) ;
    float shadedMode_0 = _S165.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S12->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S6.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S12->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S6.tint_0.w;
    }
    float4 _S166 = float4(_S12->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S166.xyz;
    float occlusion_0 = _S166.w;
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
    float4 _S168 = float4(_S12->reserved_0) ;
    uint step_0;
    float density_0;
    if((_S168.x) >= 0.5f)
    {
        if((_S168.w) >= 0.5f)
        {
            float _S169 = (float4(_S12->textureControls_0) ).z;
            for(;;)
            {
                float2 _S170 = saturate(_S6.objectPosition_0.xy + float2(0.5f, 0.5f));
                uint stepCount_0 = uint(clamp(_S169, 1.0f, 512.0f));
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
                    float density_1 = density_0 + (((&kernelContext_1)->volumeDensityTexture_0).sample(((&kernelContext_1)->volumeDensitySampler_0), (float3(_S170, (float(step_0) + 0.5f) / float(stepCount_0)))).x);
                    step_0 = step_0 + 1U;
                    density_0 = density_1;
                }
                _S9 = density_0 / float(stepCount_0);
                break;
            }
            density_0 = _S9 * max(0.0f, _S168.y);
        }
        else
        {
            density_0 = max(0.0f, _S168.y);
        }
        pixelOutput_0 _S171 = { float4(diffuseColor_0 * float3((1.0f - exp(- density_0 * max(0.0f, _S168.z)))) , 1.0f) };
        return _S171;
    }
    float4 _S172 = float4(_S12->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S172.x);
    float roughness_0 = clamp(_S172.y, 0.00999999977648258f, 1.0f);
    float4 _S173 = float4(_S12->specularIor_0) ;
    float3 specularColor_0 = _S173.xyz;
    float ior_0 = _S173.w;
    float clearcoatAmount_0 = _S165.x;
    float clearcoatRoughness_0 = _S165.y;
    float opacityThreshold_0 = _S172.z;
    bool hasSceneLighting_0;
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
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        unlitColor_0 = - _S6.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
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
    float _S174 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S175 = max(0.00100000004749745f, roughness_0);
    float _S176 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S177 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S177;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S172.w) >= 0.5f)
    {
        float3 _S178 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S178;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S179 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S179);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S179);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    FragmentFrameParameters_0 _S180 = _S7;
    float _S181 = _S7.ambientLight_0.w;
    if(_S181 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S182 = _S180.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S182, _S182)) > 0.0f;
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
    uint _S183 = min(uint(_S181), 128U);
    float3 sceneEye_0 = normalize((((float4(unlitColor_0, 0.0f)) * (_S7.eyeToWorld_0))).xyz);
    float3 worldPosition_0 = (((float4(_S6.eyePosition_0, 1.0f)) * (_S7.eyeToWorld_0))).xyz;
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
    float4 _S184 = float4(_S12->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S184.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S12->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S185 = _S7;
    uint _S186 = min(uint(max(_S7.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S186) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S187 = _S180.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S188 = float3(0.0f, 0.0f, 0.0f);
        step_0 = 0U;
        domeAmbient_1 = _S188;
        for(;;)
        {
            if(step_0 < _S186)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << step_0)) != 0U)
            {
                domeAmbient_1 = domeAmbient_1 + _S7.domeAmbient_0[step_0].xyz;
            }
            step_0 = step_0 + 1U;
        }
    }
    else
    {
        domeAmbient_1 = _S187;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S189 = !hasSceneLighting_0;
    if(_S189)
    {
        step_0 = 1U;
    }
    else
    {
        step_0 = _S183;
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
        bool _S190;
        if(hasSceneLighting_0)
        {
            _S190 = (((uint4(_S12->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S190 = false;
        }
        if(_S190)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S191 = lightIndex_0 == 0U;
        bool _S192;
        if(_S191)
        {
            _S192 = _S189;
        }
        else
        {
            _S192 = false;
        }
        if(_S192)
        {
            density_0 = 1.0f;
        }
        else
        {
            thread FrameVectorView_0 _S193 = _S7.lightPositionType_0;
            float4 _S194 = FrameVectorView_operatorx5Bx5D_get_0(&_S193, lightIndex_0, &kernelContext_1);
            density_0 = _S194.w;
        }
        bool _S195;
        if(_S191)
        {
            _S195 = _S189;
        }
        else
        {
            _S195 = false;
        }
        if(_S195)
        {
            lightDirection_0 = normalize((float4(_S12->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            thread FrameVectorView_0 _S196 = _S7.lightDirectionRadius_0;
            float4 _S197 = FrameVectorView_operatorx5Bx5D_get_0(&_S196, lightIndex_0, &kernelContext_1);
            lightDirection_0 = normalize(_S197.xyz);
        }
        bool _S198;
        if(_S191)
        {
            _S198 = _S189;
        }
        else
        {
            _S198 = false;
        }
        float _S199;
        if(_S198)
        {
            _S199 = (float4(_S12->lightDirectionIntensity_0) ).w;
        }
        else
        {
            thread FrameVectorView_0 _S200 = _S7.lightColorIntensity_0;
            float4 _S201 = FrameVectorView_operatorx5Bx5D_get_0(&_S200, lightIndex_0, &kernelContext_1);
            _S199 = _S201.w;
        }
        bool _S202;
        if(_S191)
        {
            _S202 = _S189;
        }
        else
        {
            _S202 = false;
        }
        if(_S202)
        {
            diffuseColor_0 = _S184.xyz;
        }
        else
        {
            thread FrameVectorView_0 _S203 = _S7.lightColorIntensity_0;
            float4 _S204 = FrameVectorView_operatorx5Bx5D_get_0(&_S203, lightIndex_0, &kernelContext_1);
            diffuseColor_0 = _S204.xyz;
        }
        bool _S205;
        if(_S191)
        {
            _S205 = _S189;
        }
        else
        {
            _S205 = false;
        }
        float _S206;
        if(_S205)
        {
            _S206 = 1.0f;
        }
        else
        {
            thread FrameVectorView_0 _S207 = _S7.lightControls_0;
            float4 _S208 = FrameVectorView_operatorx5Bx5D_get_0(&_S207, lightIndex_0, &kernelContext_1);
            _S206 = _S208.x;
        }
        bool _S209;
        if(_S191)
        {
            _S209 = _S189;
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
            thread FrameVectorView_0 _S211 = _S7.lightControls_0;
            float4 _S212 = FrameVectorView_operatorx5Bx5D_get_0(&_S211, lightIndex_0, &kernelContext_1);
            _S210 = _S212.y;
        }
        bool _S213;
        if(_S191)
        {
            _S213 = _S189;
        }
        else
        {
            _S213 = false;
        }
        if(_S213)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            thread FrameVectorView_0 _S214 = _S7.lightTangentShapeX_0;
            float4 _S215 = FrameVectorView_operatorx5Bx5D_get_0(&_S214, lightIndex_0, &kernelContext_1);
            lightTangent_0 = normalize(_S215.xyz);
        }
        bool _S216;
        if(_S191)
        {
            _S216 = _S189;
        }
        else
        {
            _S216 = false;
        }
        float3 lightBitangent_0;
        if(_S216)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            thread FrameVectorView_0 _S217 = _S7.lightBitangentShapeY_0;
            float4 _S218 = FrameVectorView_operatorx5Bx5D_get_0(&_S217, lightIndex_0, &kernelContext_1);
            lightBitangent_0 = normalize(_S218.xyz);
        }
        bool _S219;
        if(_S191)
        {
            _S219 = _S189;
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
            thread FrameVectorView_0 _S220 = _S7.lightTangentShapeX_0;
            float4 _S221 = FrameVectorView_operatorx5Bx5D_get_0(&_S220, lightIndex_0, &kernelContext_1);
            shapeX_0 = _S221.w;
        }
        bool _S222;
        if(_S191)
        {
            _S222 = _S189;
        }
        else
        {
            _S222 = false;
        }
        float shapeY_0;
        if(_S222)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            thread FrameVectorView_0 _S223 = _S7.lightBitangentShapeY_0;
            float4 _S224 = FrameVectorView_operatorx5Bx5D_get_0(&_S223, lightIndex_0, &kernelContext_1);
            shapeY_0 = _S224.w;
        }
        bool _S225;
        if(_S191)
        {
            _S225 = _S189;
        }
        else
        {
            _S225 = false;
        }
        float lightRadius_0;
        if(_S225)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            thread FrameVectorView_0 _S226 = _S7.lightDirectionRadius_0;
            float4 _S227 = FrameVectorView_operatorx5Bx5D_get_0(&_S226, lightIndex_0, &kernelContext_1);
            lightRadius_0 = _S227.w;
        }
        bool _S228;
        if(_S191)
        {
            _S228 = _S189;
        }
        else
        {
            _S228 = false;
        }
        if(_S228)
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
            thread FrameVectorView_0 _S229 = _S7.shadowSlots_0;
            float4 _S230 = FrameVectorView_operatorx5Bx5D_get_0(&_S229, lightIndex_0, &kernelContext_1);
            int shadowSlot_0 = int(_S230.x);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S231 = _S7;
                    FragmentFrameParameters_0 _S232 = _S7;
                    if((dot(prefiltered_0, lightDirection_0)) < 0.0f)
                    {
                        color_3 = - prefiltered_0;
                    }
                    else
                    {
                        color_3 = prefiltered_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_3, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_3 * float3((_S231.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S7.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S233 = lightClip_0.w;
                    if(_S233 <= 0.0f)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S233) ;
                    bool _S234;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S234 = true;
                    }
                    else
                    {
                        _S234 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S235;
                    if(_S234)
                    {
                        _S235 = true;
                    }
                    else
                    {
                        _S235 = (ndc_0.z) < 0.0f;
                    }
                    bool _S236;
                    if(_S235)
                    {
                        _S236 = true;
                    }
                    else
                    {
                        _S236 = (ndc_0.z) > 1.0f;
                    }
                    if(_S236)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float2 _S237 = _S232.shadowTile_0[shadowSlot_0].xy;
                    float2 _S238 = _S232.shadowTile_0[shadowSlot_0].zw;
                    float2 _S239 = _S237 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S238;
                    float texel_0 = _S231.shadowControls_0[shadowSlot_0].w;
                    float _S240 = max(_S231.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S241 = ndc_0.z - _S231.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S242 = float2((texel_0 * 0.5f)) ;
                    float2 _S243 = _S237 + _S242;
                    float2 _S244 = _S237 + _S238 - _S242;
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
                            if(_S241 <= (((&kernelContext_1)->shadowAtlas_0).sample(((&kernelContext_1)->shadowSampler_0), (clamp(_S239 + float2(float(x_0), float(y_0)) * float2((_S240 * texel_0)) , _S243, _S244)), level((0.0f))).x))
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
        float3 _S245 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S245;
        sampleOffsets_0[int(1)] = _S245;
        sampleOffsets_0[int(2)] = _S245;
        sampleOffsets_0[int(3)] = _S245;
        sampleOffsets_0[int(4)] = _S245;
        if(density_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S246 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S246 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S246 - halfHeight_0;
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
            float sampleIntensity_0 = _S199 / sampleCount_0;
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(density_0 >= 2.0f)
            {
                thread FrameVectorView_0 _S247 = _S7.lightPositionType_0;
                float4 _S248 = FrameVectorView_operatorx5Bx5D_get_0(&_S247, lightIndex_0, &kernelContext_1);
                float3 toLight_0 = _S248.xyz + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S249 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S249)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S249;
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
            float3 _S250 = float3(pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f)) ;
            float3 _S251 = mix(normalIncidence_0, grazingIncidence_0, _S250);
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - _S251);
            float _S252 = max(_S175, 0.00100000004749745f);
            float alpha_0 = _S252 * _S252;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S253 = 1.0f - k_0;
            float3 _S254 = float3(max(4.0f * normalDotLight_0 * _S174, 1.00000000317107685e-30f)) ;
            float3 _S255 = _S251 * float3((_S174 / (_S174 * _S253 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S253 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S254;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S256 = max(_S176, 0.00100000004749745f);
                float alpha_1 = _S256 * _S256;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S257 = 1.0f - k_1;
                directSpecular_0 = _S255 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S250) * float3((_S174 / (_S174 * _S257 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S257 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S254);
            }
            else
            {
                directSpecular_0 = _S255;
            }
            float3 _S258 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S206)  * (_S258 * _S177) + directSpecular_0 * float3(_S210)  * _S258);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S259 = _S7;
    if((_S7.environmentControls_0.x) >= 0.5f)
    {
        float _S260 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S261 = saturate(_S175);
        float2 _S262 = (((&kernelContext_1)->environmentBrdf_0).sample(((&kernelContext_1)->environmentBrdfSampler_0), (float2(_S260, _S261)), level((0.0f)))).xy;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S262.x)  + grazingIncidence_0 * float3(_S262.y) ;
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - specularWeight_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S263 = max(_S185.domeControls_0.y, 1.0f);
        float3 _S264 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S263 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S185.domeControls_0.z;
            float _S265 = _S185.domeControls_0.w;
            for(;;)
            {
                bool _S266 = _S263 <= 1.0f;
                _S10 = _S266;
                if(_S266)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_1)->environmentIrradiance_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S265, 1.0f);
                diffuseColor_0 = (((&kernelContext_1)->environmentIrradiance_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S263)), level((0.0f)))).xyz;
                break;
            }
            float _S267 = _S259.environmentControls_0.y;
            float _S268 = _S259.environmentControls_0.z;
            for(;;)
            {
                if(_S10)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S269 = max(_S267, 1.0f);
                    float _S270 = _S269 - 1.0f;
                    float slice_0 = _S261 * max(_S270, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S268, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S269)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S270) + v_0) / _S269)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S271 = max(_S267, 1.0f);
                float total_0 = _S271 * _S263;
                float _S272 = _S271 - 1.0f;
                float slice_1 = _S261 * max(_S272, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S268, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S271;
                unlitColor_0 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S272) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S10)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_2 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S273 = max(_S267, 1.0f);
                        float _S274 = _S273 - 1.0f;
                        float slice_2 = saturate(_S176) * max(_S274, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S268, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        normal_2 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S273)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S274) + v_2) / _S273)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S275 = max(_S267, 1.0f);
                    float total_1 = _S275 * _S263;
                    float _S276 = _S275 - 1.0f;
                    float slice_3 = saturate(_S176) * max(_S276, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S268, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S275;
                    normal_2 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S276) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightDirection_0 = normal_2;
            }
            else
            {
                lightDirection_0 = _S264;
            }
            float3 _S277 = unlitColor_0;
            unlitColor_0 = diffuseColor_0;
            prefiltered_0 = _S277;
        }
        else
        {
            sampleIndex_0 = 0U;
            unlitColor_0 = _S264;
            prefiltered_0 = _S264;
            lightDirection_0 = _S264;
            for(;;)
            {
                if(sampleIndex_0 < _S186)
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
                float _S278 = _S185.domeControls_0.w;
                for(;;)
                {
                    bool _S279 = _S263 <= 1.0f;
                    _S11 = _S279;
                    if(_S279)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_1)->environmentIrradiance_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S278, 1.0f);
                    diffuseColor_0 = (((&kernelContext_1)->environmentIrradiance_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S263)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = unlitColor_0 + diffuseColor_0;
                float _S280 = _S259.environmentControls_0.y;
                float _S281 = _S259.environmentControls_0.z;
                for(;;)
                {
                    if(_S11)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S282 = max(_S280, 1.0f);
                        float _S283 = _S282 - 1.0f;
                        float slice_4 = _S261 * max(_S283, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S281, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        normal_2 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S282)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S283) + v_4) / _S282)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S284 = max(_S280, 1.0f);
                    float total_2 = _S284 * _S263;
                    float _S285 = _S284 - 1.0f;
                    float slice_5 = _S261 * max(_S285, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S281, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S284;
                    normal_2 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S285) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_1 = prefiltered_0 + normal_2;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S11)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S286 = max(_S280, 1.0f);
                            float _S287 = _S286 - 1.0f;
                            float slice_6 = saturate(_S176) * max(_S287, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S281, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normalIncidence_0 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S286)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S287) + v_6) / _S286)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S288 = max(_S280, 1.0f);
                        float total_3 = _S288 * _S263;
                        float _S289 = _S288 - 1.0f;
                        float slice_7 = saturate(_S176) * max(_S289, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S281, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S288;
                        normalIncidence_0 = mix((((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_1)->environmentSpecular_0).sample(((&kernelContext_1)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S289) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    lightTangent_0 = lightDirection_0 + normalIncidence_0;
                }
                else
                {
                    lightTangent_0 = lightDirection_0;
                }
                unlitColor_0 = irradiance_0;
                prefiltered_0 = prefiltered_1;
                lightDirection_0 = lightTangent_0;
                sampleIndex_0 = sampleIndex_0 + 1U;
            }
        }
        float3 _S290 = float3(occlusion_0) ;
        float3 color_4 = color_2 + _S290 * diffuse_1 * unlitColor_0 * diffuseWeight_0 + _S290 * prefiltered_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S291 = (((&kernelContext_1)->environmentBrdf_0).sample(((&kernelContext_1)->environmentBrdfSampler_0), (float2(_S260, saturate(_S176))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightDirection_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S291.x)  + float3(_S291.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + emissiveColor_0) * float3(exp2((as_type<float>((_S8.z))))) ;
    if((_S8.y) == 1U)
    {
        color_2 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S292 = { float4(color_2, opacity_0) };
    return _S292;
}
