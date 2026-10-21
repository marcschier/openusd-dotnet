#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
float3 DecodeIndexedRgb_0(float4 sampled_0, texture2d<float, access::sample> values_0)
{
    float _S1 = sampled_0.w;
    if(_S1 > 1.0f)
    {
        return sampled_0.xyz;
    }
    thread uint width_0;
    thread uint height_0;
    (*((&width_0)) = (values_0).get_width(0)),(*((&height_0)) = (values_0).get_height(0));
    uint3 _S2 = uint3(round(sampled_0.xyz * float3(255.0f) ));
    uint index_0 = ((_S2.x) | ((_S2.y) << 8U)) | ((_S2.z) << 16U);
    uint3 _S3;
    if(_S1 == 0.0f)
    {
        _S3 = _S2;
    }
    else
    {
        _S3 = uint3(index_0, index_0, index_0);
    }
    uint3 address_0 = _S3 * uint3(3U)  + uint3(0U, 1U, 2U);
    uint _S4 = address_0.x;
    uint _S5 = _S4 % width_0;
    int _S6 = int(_S5);
    uint _S7 = _S4 / width_0;
    int3 _S8 = int3(_S6, int(_S7), int(0));
    float _S9 = ((values_0).read(vec<uint,2>(((_S8)).xy), uint(((_S8)).z))).x;
    uint _S10 = address_0.y;
    uint _S11 = _S10 % width_0;
    int _S12 = int(_S11);
    uint _S13 = _S10 / width_0;
    int3 _S14 = int3(_S12, int(_S13), int(0));
    float _S15 = ((values_0).read(vec<uint,2>(((_S14)).xy), uint(((_S14)).z))).x;
    uint _S16 = address_0.z;
    uint _S17 = _S16 % width_0;
    int _S18 = int(_S17);
    uint _S19 = _S16 / width_0;
    int3 _S20 = int3(_S18, int(_S19), int(0));
    return float3(_S9, _S15, ((values_0).read(vec<uint,2>(((_S20)).xy), uint(((_S20)).z))).x);
}

float3 OrientMappedNormal_0(float3 mapped_0, float2 row0_0, float2 row1_0)
{
    thread float3 _S21 = mapped_0;
    if(any(row1_0 != float2(0.0f, 0.0f)))
    {
        float _S22 = row1_0.x;
        float _S23 = row1_0.y;
        float2 tangent_0 = normalize(float2(_S23, - _S22) / float2(max(abs(_S22), abs(_S23))) );
        float2 bitangent_0 = float2(- tangent_0.y, tangent_0.x);
        float2 tangent_1;
        if((dot(row0_0, tangent_0)) < 0.0f)
        {
            tangent_1 = - tangent_0;
        }
        else
        {
            tangent_1 = tangent_0;
        }
        _S21.xy = tangent_1 * float2(_S21.x)  + bitangent_0 * float2(_S21.y) ;
    }
    return _S21;
}

float3 CombineDetailNormal_0(float3 primary_0, float3 detail_0)
{
    float3 t_0 = normalize(primary_0) + float3(0.0f, 0.0f, 1.0f);
    float3 u_0 = normalize(detail_0) * float3(-1.0f, -1.0f, 1.0f);
    float _S24 = t_0.z;
    if(_S24 == 0.0f)
    {
        return float3(- u_0.x, u_0.y, - u_0.z);
    }
    return normalize(t_0 * float3((dot(t_0, u_0) / _S24))  - u_0);
}

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

float4 ReadFrameLight_0(uint field_0, uint index_1, KernelContext_0 thread* kernelContext_0)
{
    uint _S25 = 224U + field_0 * 2048U + index_1 * 16U;
    float _S26 = as_type<float>(kernelContext_0->frameParameters_0[(_S25)>>2]);
    float _S27 = as_type<float>(kernelContext_0->frameParameters_0[(_S25 + 4U)>>2]);
    float _S28 = as_type<float>(kernelContext_0->frameParameters_0[(_S25 + 8U)>>2]);
    float _S29 = as_type<float>(kernelContext_0->frameParameters_0[(_S25 + 12U)>>2]);
    return float4(_S26, _S27, _S28, _S29);
}

float4 ReadFrameShadowSlot_0(uint index_2, KernelContext_0 thread* kernelContext_1)
{
    uint _S30 = 12960U + index_2 * 16U;
    float _S31 = as_type<float>(kernelContext_1->frameParameters_0[(_S30)>>2]);
    float _S32 = as_type<float>(kernelContext_1->frameParameters_0[(_S30 + 4U)>>2]);
    float _S33 = as_type<float>(kernelContext_1->frameParameters_0[(_S30 + 8U)>>2]);
    float _S34 = as_type<float>(kernelContext_1->frameParameters_0[(_S30 + 12U)>>2]);
    return float4(_S31, _S32, _S33, _S34);
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
    float4 tangent_2 [[user(TANGENT)]];
    float4 worldTangent_0 [[user(TEXCOORD_4)]];
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

[[fragment]] pixelOutput_0 fragmentMain_uv_normal(pixelInput_0 _S35 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> normalValuesTexture_1 [[texture(20)]], texture2d<float, access::sample> detailNormalTexture_1 [[texture(9)]], sampler detailNormalSampler_1 [[sampler(4)]], texture2d<float, access::sample> detailNormalValuesTexture_1 [[texture(21)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_3;
    FragmentFrameParameters_0 _S36;
    uint4 _S37;
    bool hasSceneLighting_0;
    float4 _S38;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    float3 color_0;
    bool _S39;
    bool _S40;
    thread KernelContext_0 kernelContext_2;
    (&kernelContext_2)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_2)->frameParameters_0 = frameParameters_1;
    (&kernelContext_2)->normalTexture_0 = normalTexture_1;
    (&kernelContext_2)->normalSampler_0 = normalSampler_1;
    (&kernelContext_2)->normalValuesTexture_0 = normalValuesTexture_1;
    (&kernelContext_2)->detailNormalTexture_0 = detailNormalTexture_1;
    (&kernelContext_2)->detailNormalSampler_0 = detailNormalSampler_1;
    (&kernelContext_2)->detailNormalValuesTexture_0 = detailNormalValuesTexture_1;
    (&kernelContext_2)->shadowAtlas_0 = shadowAtlas_1;
    (&kernelContext_2)->shadowSampler_0 = shadowSampler_1;
    (&kernelContext_2)->environmentBrdf_0 = environmentBrdf_1;
    (&kernelContext_2)->environmentBrdfSampler_0 = environmentBrdfSampler_1;
    (&kernelContext_2)->environmentIrradiance_0 = environmentIrradiance_1;
    (&kernelContext_2)->environmentSampler_0 = environmentSampler_1;
    (&kernelContext_2)->environmentSpecular_0 = environmentSpecular_1;
    SurfaceParameters_natural_0 device* _S41 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        uint _S42 = as_type<uint>((&kernelContext_2)->frameParameters_0[(64U)>>2]);
        uint _S43 = as_type<uint>((&kernelContext_2)->frameParameters_0[(68U)>>2]);
        uint _S44 = as_type<uint>((&kernelContext_2)->frameParameters_0[(72U)>>2]);
        uint _S45 = as_type<uint>((&kernelContext_2)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S42, _S43, _S44, _S45);
        index_3 = 0U;
        for(;;)
        {
            if(index_3 < 8U)
            {
            }
            else
            {
                break;
            }
            uint _S46 = 80U + index_3 * 16U;
            float _S47 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S46)>>2]);
            float _S48 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S46 + 4U)>>2]);
            float _S49 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S46 + 8U)>>2]);
            float _S50 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S46 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_3] = float4(_S47, _S48, _S49, _S50);
            index_3 = index_3 + 1U;
        }
        float _S51 = as_type<float>((&kernelContext_2)->frameParameters_0[(208U)>>2]);
        float _S52 = as_type<float>((&kernelContext_2)->frameParameters_0[(212U)>>2]);
        float _S53 = as_type<float>((&kernelContext_2)->frameParameters_0[(216U)>>2]);
        float _S54 = as_type<float>((&kernelContext_2)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S51, _S52, _S53, _S54);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 12960U;
        float _S55 = as_type<float>((&kernelContext_2)->frameParameters_0[(12512U)>>2]);
        float _S56 = as_type<float>((&kernelContext_2)->frameParameters_0[(12516U)>>2]);
        float _S57 = as_type<float>((&kernelContext_2)->frameParameters_0[(12520U)>>2]);
        float _S58 = as_type<float>((&kernelContext_2)->frameParameters_0[(12524U)>>2]);
        float4 _S59 = float4(_S55, _S56, _S57, _S58);
        float _S60 = as_type<float>((&kernelContext_2)->frameParameters_0[(12528U)>>2]);
        float _S61 = as_type<float>((&kernelContext_2)->frameParameters_0[(12532U)>>2]);
        float _S62 = as_type<float>((&kernelContext_2)->frameParameters_0[(12536U)>>2]);
        float _S63 = as_type<float>((&kernelContext_2)->frameParameters_0[(12540U)>>2]);
        float4 _S64 = float4(_S60, _S61, _S62, _S63);
        float _S65 = as_type<float>((&kernelContext_2)->frameParameters_0[(12544U)>>2]);
        float _S66 = as_type<float>((&kernelContext_2)->frameParameters_0[(12548U)>>2]);
        float _S67 = as_type<float>((&kernelContext_2)->frameParameters_0[(12552U)>>2]);
        float _S68 = as_type<float>((&kernelContext_2)->frameParameters_0[(12556U)>>2]);
        float4 _S69 = float4(_S65, _S66, _S67, _S68);
        float _S70 = as_type<float>((&kernelContext_2)->frameParameters_0[(12560U)>>2]);
        float _S71 = as_type<float>((&kernelContext_2)->frameParameters_0[(12564U)>>2]);
        float _S72 = as_type<float>((&kernelContext_2)->frameParameters_0[(12568U)>>2]);
        float _S73 = as_type<float>((&kernelContext_2)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S59, _S64, _S69, float4(_S70, _S71, _S72, _S73));
        index_3 = 0U;
        for(;;)
        {
            if(index_3 < 4U)
            {
            }
            else
            {
                break;
            }
            uint _S74 = 12576U + index_3 * 64U;
            float _S75 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74)>>2]);
            float _S76 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 4U)>>2]);
            float _S77 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 8U)>>2]);
            float _S78 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 12U)>>2]);
            float4 _S79 = float4(_S75, _S76, _S77, _S78);
            float _S80 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 16U)>>2]);
            float _S81 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 20U)>>2]);
            float _S82 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 24U)>>2]);
            float _S83 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 28U)>>2]);
            float4 _S84 = float4(_S80, _S81, _S82, _S83);
            float _S85 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 32U)>>2]);
            float _S86 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 36U)>>2]);
            float _S87 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 40U)>>2]);
            float _S88 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 44U)>>2]);
            float4 _S89 = float4(_S85, _S86, _S87, _S88);
            float _S90 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 48U)>>2]);
            float _S91 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 52U)>>2]);
            float _S92 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 56U)>>2]);
            float _S93 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S74 + 60U)>>2]);
            (&frame_0)->shadowWorldToLightClip_0[index_3] = matrix<float,int(4),int(4)> (_S79, _S84, _S89, float4(_S90, _S91, _S92, _S93));
            uint _S94 = index_3 * 16U;
            uint _S95 = 12832U + _S94;
            float _S96 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S95)>>2]);
            float _S97 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S95 + 4U)>>2]);
            float _S98 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S95 + 8U)>>2]);
            float _S99 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S95 + 12U)>>2]);
            (&frame_0)->shadowTile_0[index_3] = float4(_S96, _S97, _S98, _S99);
            uint _S100 = 12896U + _S94;
            float _S101 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S100)>>2]);
            float _S102 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S100 + 4U)>>2]);
            float _S103 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S100 + 8U)>>2]);
            float _S104 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S100 + 12U)>>2]);
            (&frame_0)->shadowControls_0[index_3] = float4(_S101, _S102, _S103, _S104);
            index_3 = index_3 + 1U;
        }
        float _S105 = as_type<float>((&kernelContext_2)->frameParameters_0[(15008U)>>2]);
        float _S106 = as_type<float>((&kernelContext_2)->frameParameters_0[(15012U)>>2]);
        float _S107 = as_type<float>((&kernelContext_2)->frameParameters_0[(15016U)>>2]);
        float _S108 = as_type<float>((&kernelContext_2)->frameParameters_0[(15020U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S105, _S106, _S107, _S108);
        float _S109 = as_type<float>((&kernelContext_2)->frameParameters_0[(15024U)>>2]);
        float _S110 = as_type<float>((&kernelContext_2)->frameParameters_0[(15028U)>>2]);
        float _S111 = as_type<float>((&kernelContext_2)->frameParameters_0[(15032U)>>2]);
        float _S112 = as_type<float>((&kernelContext_2)->frameParameters_0[(15036U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S109, _S110, _S111, _S112);
        float _S113 = as_type<float>((&kernelContext_2)->frameParameters_0[(15040U)>>2]);
        float _S114 = as_type<float>((&kernelContext_2)->frameParameters_0[(15044U)>>2]);
        float _S115 = as_type<float>((&kernelContext_2)->frameParameters_0[(15048U)>>2]);
        float _S116 = as_type<float>((&kernelContext_2)->frameParameters_0[(15052U)>>2]);
        float4 _S117 = float4(_S113, _S114, _S115, _S116);
        float _S118 = as_type<float>((&kernelContext_2)->frameParameters_0[(15056U)>>2]);
        float _S119 = as_type<float>((&kernelContext_2)->frameParameters_0[(15060U)>>2]);
        float _S120 = as_type<float>((&kernelContext_2)->frameParameters_0[(15064U)>>2]);
        float _S121 = as_type<float>((&kernelContext_2)->frameParameters_0[(15068U)>>2]);
        float4 _S122 = float4(_S118, _S119, _S120, _S121);
        float _S123 = as_type<float>((&kernelContext_2)->frameParameters_0[(15072U)>>2]);
        float _S124 = as_type<float>((&kernelContext_2)->frameParameters_0[(15076U)>>2]);
        float _S125 = as_type<float>((&kernelContext_2)->frameParameters_0[(15080U)>>2]);
        float _S126 = as_type<float>((&kernelContext_2)->frameParameters_0[(15084U)>>2]);
        float4 _S127 = float4(_S123, _S124, _S125, _S126);
        float _S128 = as_type<float>((&kernelContext_2)->frameParameters_0[(15088U)>>2]);
        float _S129 = as_type<float>((&kernelContext_2)->frameParameters_0[(15092U)>>2]);
        float _S130 = as_type<float>((&kernelContext_2)->frameParameters_0[(15096U)>>2]);
        float _S131 = as_type<float>((&kernelContext_2)->frameParameters_0[(15100U)>>2]);
        float4 _S132 = float4(_S128, _S129, _S130, _S131);
        float _S133 = as_type<float>((&kernelContext_2)->frameParameters_0[(15104U)>>2]);
        float _S134 = as_type<float>((&kernelContext_2)->frameParameters_0[(15108U)>>2]);
        float _S135 = as_type<float>((&kernelContext_2)->frameParameters_0[(15112U)>>2]);
        float _S136 = as_type<float>((&kernelContext_2)->frameParameters_0[(15116U)>>2]);
        float4 _S137 = float4(_S133, _S134, _S135, _S136);
        float _S138 = as_type<float>((&kernelContext_2)->frameParameters_0[(15120U)>>2]);
        float _S139 = as_type<float>((&kernelContext_2)->frameParameters_0[(15124U)>>2]);
        float _S140 = as_type<float>((&kernelContext_2)->frameParameters_0[(15128U)>>2]);
        float _S141 = as_type<float>((&kernelContext_2)->frameParameters_0[(15132U)>>2]);
        float4 _S142 = float4(_S138, _S139, _S140, _S141);
        float _S143 = as_type<float>((&kernelContext_2)->frameParameters_0[(15136U)>>2]);
        float _S144 = as_type<float>((&kernelContext_2)->frameParameters_0[(15140U)>>2]);
        float _S145 = as_type<float>((&kernelContext_2)->frameParameters_0[(15144U)>>2]);
        float _S146 = as_type<float>((&kernelContext_2)->frameParameters_0[(15148U)>>2]);
        float4 _S147 = float4(_S143, _S144, _S145, _S146);
        float _S148 = as_type<float>((&kernelContext_2)->frameParameters_0[(15152U)>>2]);
        float _S149 = as_type<float>((&kernelContext_2)->frameParameters_0[(15156U)>>2]);
        float _S150 = as_type<float>((&kernelContext_2)->frameParameters_0[(15160U)>>2]);
        float _S151 = as_type<float>((&kernelContext_2)->frameParameters_0[(15164U)>>2]);
        float4 _S152 = float4(_S148, _S149, _S150, _S151);
        float _S153 = as_type<float>((&kernelContext_2)->frameParameters_0[(15168U)>>2]);
        float _S154 = as_type<float>((&kernelContext_2)->frameParameters_0[(15172U)>>2]);
        float _S155 = as_type<float>((&kernelContext_2)->frameParameters_0[(15176U)>>2]);
        float _S156 = as_type<float>((&kernelContext_2)->frameParameters_0[(15180U)>>2]);
        float4 _S157 = float4(_S153, _S154, _S155, _S156);
        float _S158 = as_type<float>((&kernelContext_2)->frameParameters_0[(15184U)>>2]);
        float _S159 = as_type<float>((&kernelContext_2)->frameParameters_0[(15188U)>>2]);
        float _S160 = as_type<float>((&kernelContext_2)->frameParameters_0[(15192U)>>2]);
        float _S161 = as_type<float>((&kernelContext_2)->frameParameters_0[(15196U)>>2]);
        float4 _S162 = float4(_S158, _S159, _S160, _S161);
        float _S163 = as_type<float>((&kernelContext_2)->frameParameters_0[(15200U)>>2]);
        float _S164 = as_type<float>((&kernelContext_2)->frameParameters_0[(15204U)>>2]);
        float _S165 = as_type<float>((&kernelContext_2)->frameParameters_0[(15208U)>>2]);
        float _S166 = as_type<float>((&kernelContext_2)->frameParameters_0[(15212U)>>2]);
        float4 _S167 = float4(_S163, _S164, _S165, _S166);
        float _S168 = as_type<float>((&kernelContext_2)->frameParameters_0[(15216U)>>2]);
        float _S169 = as_type<float>((&kernelContext_2)->frameParameters_0[(15220U)>>2]);
        float _S170 = as_type<float>((&kernelContext_2)->frameParameters_0[(15224U)>>2]);
        float _S171 = as_type<float>((&kernelContext_2)->frameParameters_0[(15228U)>>2]);
        float4 _S172 = float4(_S168, _S169, _S170, _S171);
        float _S173 = as_type<float>((&kernelContext_2)->frameParameters_0[(15232U)>>2]);
        float _S174 = as_type<float>((&kernelContext_2)->frameParameters_0[(15236U)>>2]);
        float _S175 = as_type<float>((&kernelContext_2)->frameParameters_0[(15240U)>>2]);
        float _S176 = as_type<float>((&kernelContext_2)->frameParameters_0[(15244U)>>2]);
        float4 _S177 = float4(_S173, _S174, _S175, _S176);
        float _S178 = as_type<float>((&kernelContext_2)->frameParameters_0[(15248U)>>2]);
        float _S179 = as_type<float>((&kernelContext_2)->frameParameters_0[(15252U)>>2]);
        float _S180 = as_type<float>((&kernelContext_2)->frameParameters_0[(15256U)>>2]);
        float _S181 = as_type<float>((&kernelContext_2)->frameParameters_0[(15260U)>>2]);
        float4 _S182 = float4(_S178, _S179, _S180, _S181);
        float _S183 = as_type<float>((&kernelContext_2)->frameParameters_0[(15264U)>>2]);
        float _S184 = as_type<float>((&kernelContext_2)->frameParameters_0[(15268U)>>2]);
        float _S185 = as_type<float>((&kernelContext_2)->frameParameters_0[(15272U)>>2]);
        float _S186 = as_type<float>((&kernelContext_2)->frameParameters_0[(15276U)>>2]);
        float4 _S187 = float4(_S183, _S184, _S185, _S186);
        float _S188 = as_type<float>((&kernelContext_2)->frameParameters_0[(15280U)>>2]);
        float _S189 = as_type<float>((&kernelContext_2)->frameParameters_0[(15284U)>>2]);
        float _S190 = as_type<float>((&kernelContext_2)->frameParameters_0[(15288U)>>2]);
        float _S191 = as_type<float>((&kernelContext_2)->frameParameters_0[(15292U)>>2]);
        float4 _S192 = float4(_S188, _S189, _S190, _S191);
        (&frame_0)->domeAmbient_0[int(0)] = _S117;
        (&frame_0)->domeAmbient_0[int(1)] = _S122;
        (&frame_0)->domeAmbient_0[int(2)] = _S127;
        (&frame_0)->domeAmbient_0[int(3)] = _S132;
        (&frame_0)->domeAmbient_0[int(4)] = _S137;
        (&frame_0)->domeAmbient_0[int(5)] = _S142;
        (&frame_0)->domeAmbient_0[int(6)] = _S147;
        (&frame_0)->domeAmbient_0[int(7)] = _S152;
        (&frame_0)->domeEnvironment_0[int(0)] = _S157;
        (&frame_0)->domeEnvironment_0[int(1)] = _S162;
        (&frame_0)->domeEnvironment_0[int(2)] = _S167;
        (&frame_0)->domeEnvironment_0[int(3)] = _S172;
        (&frame_0)->domeEnvironment_0[int(4)] = _S177;
        (&frame_0)->domeEnvironment_0[int(5)] = _S182;
        (&frame_0)->domeEnvironment_0[int(6)] = _S187;
        (&frame_0)->domeEnvironment_0[int(7)] = _S192;
        _S36 = frame_0;
        break;
    }
    for(;;)
    {
        _S37 = _S36.clipPlaneCount_0;
        uint _S193 = min(_S36.clipPlaneCount_0.x, 8U);
        index_3 = 0U;
        for(;;)
        {
            if(index_3 < _S193)
            {
            }
            else
            {
                break;
            }
            if((dot(_S36.clipPlanes_0[index_3].xyz, _S35.eyePosition_0) + _S36.clipPlanes_0[index_3].w) < 0.0f)
            {
                discard_fragment();
            }
            index_3 = index_3 + 1U;
        }
        break;
    }
    float4 _S194 = float4(_S41->clearcoatShaded_0) ;
    float shadedMode_0 = _S194.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S41->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S35.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S41->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S35.tint_0.w;
    }
    float4 _S195 = float4(_S41->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S195.xyz;
    float occlusion_0 = _S195.w;
    float3 unlitColor_0;
    if(unlit_0)
    {
        float3 unlitColor_1 = (diffuseColor_0 + emissiveColor_0) * float3(exp2((as_type<float>((_S37.z))))) ;
        if((_S37.y) == 1U)
        {
            unlitColor_0 = unlitColor_1 / (float3(1.0f)  + max(unlitColor_1, float3(0.0f, 0.0f, 0.0f)));
        }
        else
        {
            unlitColor_0 = unlitColor_1;
        }
        pixelOutput_0 _S196 = { float4(unlitColor_0, opacity_0) };
        return _S196;
    }
    float4 _S197 = float4(_S41->reserved_0) ;
    if((_S197.x) >= 0.5f)
    {
        pixelOutput_0 _S198 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S197.y) * max(0.0f, _S197.z)))) , 1.0f) };
        return _S198;
    }
    float4 _S199 = float4(_S41->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S199.x);
    float roughness_0 = clamp(_S199.y, 0.00999999977648258f, 1.0f);
    float4 _S200 = float4(_S41->specularIor_0) ;
    float3 specularColor_0 = _S200.xyz;
    float ior_0 = _S200.w;
    float clearcoatAmount_0 = _S194.x;
    float clearcoatRoughness_0 = _S194.y;
    float4 _S201 = float4(_S41->textureControls_0) ;
    uint textureMask_0 = uint(round(_S201.x));
    uint udimMask_0 = uint(round(_S201.y));
    float4 _S202 = float4(_S41->uvTransformRow0_0) ;
    float2 _S203 = _S202.xy;
    float4 _S204 = float4(_S41->uvTransformRow1_0) ;
    float2 _S205 = _S204.xy;
    float2 _S206 = float2(dot(_S203, _S35.texCoord_0) + _S202.z, dot(_S205, _S35.texCoord_0) + _S204.z);
    thread float4 baseNormalSample_0;
    bool _S207 = (udimMask_0 & 4U) != 0U;
    for(;;)
    {
        if(!_S207)
        {
            _S38 = (((&kernelContext_2)->normalTexture_0).sample(((&kernelContext_2)->normalSampler_0), (_S206)));
            break;
        }
        texture2d<float, access::sample> _S208 = (&kernelContext_2)->normalTexture_0;
        thread uint atlasWidth_0;
        thread uint atlasHeight_0;
        (*((&atlasWidth_0)) = (_S208).get_width(0)),(*((&atlasHeight_0)) = (_S208).get_height(0));
        int3 _S209 = int3(int(0), int(0), int(0));
        float4 metadata_0 = round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S209)).xy), uint(((_S209)).z))) * float4(255.0f) );
        if((metadata_0.x) == 255.0f)
        {
            uint _S210 = atlasWidth_0;
            uint _S211 = 2U % atlasWidth_0;
            int _S212 = int(_S211);
            uint _S213 = 2U / _S210;
            int3 _S214 = int3(_S212, int(_S213), int(0));
            int _S215 = int(uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S214)).xy), uint(((_S214)).z))).x * 255.0f)));
            uint _S216 = atlasWidth_0;
            uint _S217 = 3U % atlasWidth_0;
            int _S218 = int(_S217);
            uint _S219 = 3U / _S216;
            int3 _S220 = int3(_S218, int(_S219), int(0));
            int2 minimumTile_0 = int2(_S215, int(uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S220)).xy), uint(((_S220)).z))).x * 255.0f))));
            uint _S221 = atlasWidth_0;
            uint _S222 = 4U % atlasWidth_0;
            int _S223 = int(_S222);
            uint _S224 = 4U / _S221;
            int3 _S225 = int3(_S223, int(_S224), int(0));
            uint _S226 = uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S225)).xy), uint(((_S225)).z))).x * 255.0f));
            uint _S227 = atlasWidth_0;
            uint _S228 = 5U % atlasWidth_0;
            int _S229 = int(_S228);
            uint _S230 = 5U / _S227;
            int3 _S231 = int3(_S229, int(_S230), int(0));
            uint _S232 = uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S231)).xy), uint(((_S231)).z))).x * 255.0f));
            uint2 grid_0 = uint2(_S226, _S232);
            int2 tile_0 = int2(floor(_S206)) - minimumTile_0;
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
                int3 _S233 = int3(int(1), int(0), int(0));
                _S38 = (((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S233)).xy), uint(((_S233)).z)));
                break;
            }
            uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S226 + uint(tile_0.x));
            uint _S234 = atlasWidth_0;
            uint _S235 = offset_0 % atlasWidth_0;
            int _S236 = int(_S235);
            uint _S237 = offset_0 / _S234;
            int3 _S238 = int3(_S236, int(_S237), int(0));
            uint _S239 = uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S238)).xy), uint(((_S238)).z))).x * 255.0f));
            uint _S240 = atlasWidth_0;
            uint _S241 = offset_0 + 1U;
            uint _S242 = _S241 % atlasWidth_0;
            int _S243 = int(_S242);
            uint _S244 = _S241 / _S240;
            int3 _S245 = int3(_S243, int(_S244), int(0));
            uint slot_0 = _S239 | (uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S245)).xy), uint(((_S245)).z))).x * 255.0f)) << 8U);
            if(slot_0 == 0U)
            {
                int3 _S246 = int3(int(1), int(0), int(0));
                _S38 = (((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S246)).xy), uint(((_S246)).z)));
                break;
            }
            uint slot_1 = slot_0 - 1U;
            uint _S247 = atlasWidth_0;
            uint _S248 = 6U % atlasWidth_0;
            int _S249 = int(_S248);
            uint _S250 = 6U / _S247;
            int3 _S251 = int3(_S249, int(_S250), int(0));
            uint _S252 = uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S251)).xy), uint(((_S251)).z))).x * 255.0f));
            uint _S253 = atlasWidth_0;
            uint _S254 = 7U % atlasWidth_0;
            int _S255 = int(_S254);
            uint _S256 = 7U / _S253;
            int3 _S257 = int3(_S255, int(_S256), int(0));
            uint columns_0 = _S252 | (uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S257)).xy), uint(((_S257)).z))).x * 255.0f)) << 8U);
            uint _S258 = atlasWidth_0;
            uint _S259 = 8U % atlasWidth_0;
            int _S260 = int(_S259);
            uint _S261 = 8U / _S258;
            int3 _S262 = int3(_S260, int(_S261), int(0));
            uint _S263 = uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S262)).xy), uint(((_S262)).z))).x * 255.0f));
            uint _S264 = atlasWidth_0;
            uint _S265 = 9U % atlasWidth_0;
            int _S266 = int(_S265);
            uint _S267 = 9U / _S264;
            int3 _S268 = int3(_S266, int(_S267), int(0));
            uint rows_0 = ((_S263 | (uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S268)).xy), uint(((_S268)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
            uint metadataRows_0 = (10U + 2U * _S226 * _S232 + atlasWidth_0 - 1U) / atlasWidth_0;
            uint _S269 = atlasWidth_0 / columns_0;
            float _S270 = float(_S269);
            uint _S271 = (atlasHeight_0 - metadataRows_0) / rows_0;
            float2 cellSize_0 = float2(_S270, float(_S271));
            uint _S272 = slot_1 % columns_0;
            float _S273 = float(_S272);
            uint _S274 = slot_1 / columns_0;
            _S38 = (((&kernelContext_2)->normalTexture_0).sample(((&kernelContext_2)->normalSampler_0), ((float2(_S273, float(_S274)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S206) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        int2 _S275 = int2(metadata_0.zw);
        int2 tile_1 = int2(floor(_S206)) - int2(metadata_0.xy);
        if(any(tile_1 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_1 >= _S275);
        }
        if(hasSceneLighting_0)
        {
            int3 _S276 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
            _S38 = (((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S276)).xy), uint(((_S276)).z)));
            break;
        }
        uint _S277 = atlasWidth_0 / uint(_S275.x);
        float _S278 = float(_S277);
        uint _S279 = (atlasHeight_0 - 1U) / uint(_S275.y);
        float2 cellSize_1 = float2(_S278, float(_S279));
        _S38 = (((&kernelContext_2)->normalTexture_0).sample(((&kernelContext_2)->normalSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
        break;
    }
    baseNormalSample_0 = _S38;
    float4 _S280 = float4(_S41->detailUvTransformRow0_0) ;
    if((_S280.w) != 0.0f)
    {
        float3 _S281 = DecodeIndexedRgb_0(baseNormalSample_0, (&kernelContext_2)->normalValuesTexture_0);
        baseNormalSample_0.xyz = _S281;
    }
    if((textureMask_0 & 4U) == 0U)
    {
        unlitColor_0 = float3(0.0f, 0.0f, 1.0f);
    }
    else
    {
        unlitColor_0 = baseNormalSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
    }
    float3 sampledNormal_0 = OrientMappedNormal_0(unlitColor_0, _S203, _S205);
    if((textureMask_0 & 4096U) != 0U)
    {
        float2 _S282 = _S280.xy;
        float4 _S283 = float4(_S41->detailUvTransformRow1_0) ;
        float2 _S284 = _S283.xy;
        float2 detailUv_0 = float2(dot(_S282, _S35.texCoord_0) + _S280.z, dot(_S284, _S35.texCoord_0) + _S283.z);
        thread float4 detailSample_0;
        bool _S285 = (udimMask_0 & 4096U) != 0U;
        for(;;)
        {
            if(!_S285)
            {
                _S38 = (((&kernelContext_2)->detailNormalTexture_0).sample(((&kernelContext_2)->detailNormalSampler_0), (detailUv_0)));
                break;
            }
            texture2d<float, access::sample> _S286 = (&kernelContext_2)->detailNormalTexture_0;
            thread uint atlasWidth_1;
            thread uint atlasHeight_1;
            (*((&atlasWidth_1)) = (_S286).get_width(0)),(*((&atlasHeight_1)) = (_S286).get_height(0));
            int3 _S287 = int3(int(0), int(0), int(0));
            float4 metadata_1 = round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S287)).xy), uint(((_S287)).z))) * float4(255.0f) );
            if((metadata_1.x) == 255.0f)
            {
                uint _S288 = atlasWidth_1;
                uint _S289 = 2U % atlasWidth_1;
                int _S290 = int(_S289);
                uint _S291 = 2U / _S288;
                int3 _S292 = int3(_S290, int(_S291), int(0));
                int _S293 = int(uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S292)).xy), uint(((_S292)).z))).x * 255.0f)));
                uint _S294 = atlasWidth_1;
                uint _S295 = 3U % atlasWidth_1;
                int _S296 = int(_S295);
                uint _S297 = 3U / _S294;
                int3 _S298 = int3(_S296, int(_S297), int(0));
                int2 minimumTile_1 = int2(_S293, int(uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S298)).xy), uint(((_S298)).z))).x * 255.0f))));
                uint _S299 = atlasWidth_1;
                uint _S300 = 4U % atlasWidth_1;
                int _S301 = int(_S300);
                uint _S302 = 4U / _S299;
                int3 _S303 = int3(_S301, int(_S302), int(0));
                uint _S304 = uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S303)).xy), uint(((_S303)).z))).x * 255.0f));
                uint _S305 = atlasWidth_1;
                uint _S306 = 5U % atlasWidth_1;
                int _S307 = int(_S306);
                uint _S308 = 5U / _S305;
                int3 _S309 = int3(_S307, int(_S308), int(0));
                uint _S310 = uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S309)).xy), uint(((_S309)).z))).x * 255.0f));
                uint2 grid_1 = uint2(_S304, _S310);
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
                    int3 _S311 = int3(int(1), int(0), int(0));
                    _S38 = (((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S311)).xy), uint(((_S311)).z)));
                    break;
                }
                uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S304 + uint(tile_2.x));
                uint _S312 = atlasWidth_1;
                uint _S313 = offset_1 % atlasWidth_1;
                int _S314 = int(_S313);
                uint _S315 = offset_1 / _S312;
                int3 _S316 = int3(_S314, int(_S315), int(0));
                uint _S317 = uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S316)).xy), uint(((_S316)).z))).x * 255.0f));
                uint _S318 = atlasWidth_1;
                uint _S319 = offset_1 + 1U;
                uint _S320 = _S319 % atlasWidth_1;
                int _S321 = int(_S320);
                uint _S322 = _S319 / _S318;
                int3 _S323 = int3(_S321, int(_S322), int(0));
                uint slot_2 = _S317 | (uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S323)).xy), uint(((_S323)).z))).x * 255.0f)) << 8U);
                if(slot_2 == 0U)
                {
                    int3 _S324 = int3(int(1), int(0), int(0));
                    _S38 = (((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S324)).xy), uint(((_S324)).z)));
                    break;
                }
                uint slot_3 = slot_2 - 1U;
                uint _S325 = atlasWidth_1;
                uint _S326 = 6U % atlasWidth_1;
                int _S327 = int(_S326);
                uint _S328 = 6U / _S325;
                int3 _S329 = int3(_S327, int(_S328), int(0));
                uint _S330 = uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S329)).xy), uint(((_S329)).z))).x * 255.0f));
                uint _S331 = atlasWidth_1;
                uint _S332 = 7U % atlasWidth_1;
                int _S333 = int(_S332);
                uint _S334 = 7U / _S331;
                int3 _S335 = int3(_S333, int(_S334), int(0));
                uint columns_1 = _S330 | (uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S335)).xy), uint(((_S335)).z))).x * 255.0f)) << 8U);
                uint _S336 = atlasWidth_1;
                uint _S337 = 8U % atlasWidth_1;
                int _S338 = int(_S337);
                uint _S339 = 8U / _S336;
                int3 _S340 = int3(_S338, int(_S339), int(0));
                uint _S341 = uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S340)).xy), uint(((_S340)).z))).x * 255.0f));
                uint _S342 = atlasWidth_1;
                uint _S343 = 9U % atlasWidth_1;
                int _S344 = int(_S343);
                uint _S345 = 9U / _S342;
                int3 _S346 = int3(_S344, int(_S345), int(0));
                uint rows_1 = ((_S341 | (uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S346)).xy), uint(((_S346)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                uint metadataRows_1 = (10U + 2U * _S304 * _S310 + atlasWidth_1 - 1U) / atlasWidth_1;
                uint _S347 = atlasWidth_1 / columns_1;
                float _S348 = float(_S347);
                uint _S349 = (atlasHeight_1 - metadataRows_1) / rows_1;
                float2 cellSize_2 = float2(_S348, float(_S349));
                uint _S350 = slot_3 % columns_1;
                float _S351 = float(_S350);
                uint _S352 = slot_3 / columns_1;
                _S38 = (((&kernelContext_2)->detailNormalTexture_0).sample(((&kernelContext_2)->detailNormalSampler_0), ((float2(_S351, float(_S352)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailUv_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            int2 _S353 = int2(metadata_1.zw);
            int2 tile_3 = int2(floor(detailUv_0)) - int2(metadata_1.xy);
            if(any(tile_3 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_3 >= _S353);
            }
            if(hasSceneLighting_0)
            {
                int3 _S354 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                _S38 = (((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S354)).xy), uint(((_S354)).z)));
                break;
            }
            uint _S355 = atlasWidth_1 / uint(_S353.x);
            float _S356 = float(_S355);
            uint _S357 = (atlasHeight_1 - 1U) / uint(_S353.y);
            float2 cellSize_3 = float2(_S356, float(_S357));
            _S38 = (((&kernelContext_2)->detailNormalTexture_0).sample(((&kernelContext_2)->detailNormalSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
            break;
        }
        detailSample_0 = _S38;
        if((_S283.w) != 0.0f)
        {
            float3 _S358 = DecodeIndexedRgb_0(detailSample_0, (&kernelContext_2)->detailNormalValuesTexture_0);
            detailSample_0.xyz = _S358;
        }
        unlitColor_0 = CombineDetailNormal_0(sampledNormal_0, OrientMappedNormal_0(detailSample_0.xyz * float3(2.0f)  - float3(1.0f) , _S282, _S284));
    }
    else
    {
        unlitColor_0 = sampledNormal_0;
    }
    float3 tangent_3 = normalize(_S35.tangent_2.xyz);
    float3 _S359 = float3(unlitColor_0.x) ;
    float3 _S360 = float3(unlitColor_0.y) ;
    float3 _S361 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_3 * _S359 + cross(normalize(_S35.normal_0), tangent_3) * float3(_S35.tangent_2.w)  * _S360 + _S35.normal_0 * _S361);
    float3 worldNormalBasis_0 = normalize(_S35.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S35.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S359 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S35.worldTangent_0.w)  * _S360 + worldNormalBasis_0 * _S361);
    float opacityThreshold_0 = _S199.z;
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
    float lengthSquared_0 = dot(_S35.eyePosition_0, _S35.eyePosition_0);
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        unlitColor_0 = - _S35.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
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
    float _S362 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S363 = max(0.00100000004749745f, roughness_0);
    float _S364 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S365 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S365;
    bool omniPbrSpecular_0 = (_S201.w) >= 0.5f;
    float dielectricLevel_0 = _S194.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S366 = float3(dielectricLevel_0) ;
        float3 _S367 = float3(metallic_0) ;
        float3 _S368 = mix(_S366, diffuseColor_0, _S367);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S366, diffuseColor_0, _S367);
        grazingIncidence_0 = _S368;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S199.w) >= 0.5f)
        {
            float3 _S369 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S369;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S370 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S370);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S370);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S371 = _S36;
    float _S372 = _S36.ambientLight_0.w;
    if(_S372 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S373 = _S371.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S373, _S373)) > 0.0f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S36.environmentControls_0.x) >= 0.5f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S36.environmentControls_0.w) >= 0.5f;
    }
    uint _S374 = min(uint(_S372), 128U);
    float3 sceneEye_0 = normalize((((float4(unlitColor_0, 0.0f)) * (_S36.eyeToWorld_0))).xyz);
    float3 worldPosition_0 = (((float4(_S35.eyePosition_0, 1.0f)) * (_S36.eyeToWorld_0))).xyz;
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
    float4 _S375 = float4(_S41->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S375.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S41->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S376 = _S36;
    uint _S377 = min(uint(max(_S36.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S377) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S378 = _S371.ambientLight_0.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S379 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S379;
        for(;;)
        {
            if(lightCount_0 < _S377)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << lightCount_0)) != 0U)
            {
                domeAmbient_1 = domeAmbient_1 + _S36.domeAmbient_0[lightCount_0].xyz;
            }
            lightCount_0 = lightCount_0 + 1U;
        }
    }
    else
    {
        domeAmbient_1 = _S378;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S380 = !hasSceneLighting_0;
    if(_S380)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S374;
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
        bool _S381;
        if(hasSceneLighting_0)
        {
            _S381 = (((uint4(_S41->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S381 = false;
        }
        if(_S381)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S382 = lightIndex_0 == 0U;
        bool _S383;
        if(_S382)
        {
            _S383 = _S380;
        }
        else
        {
            _S383 = false;
        }
        float lightType_0;
        if(_S383)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            float4 _S384 = ReadFrameLight_0(0U, lightIndex_0, &kernelContext_2);
            lightType_0 = _S384.w;
        }
        bool _S385;
        if(_S382)
        {
            _S385 = _S380;
        }
        else
        {
            _S385 = false;
        }
        if(_S385)
        {
            lightDirection_0 = normalize((float4(_S41->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            float4 _S386 = ReadFrameLight_0(1U, lightIndex_0, &kernelContext_2);
            lightDirection_0 = normalize(_S386.xyz);
        }
        bool _S387;
        if(_S382)
        {
            _S387 = _S380;
        }
        else
        {
            _S387 = false;
        }
        float _S388;
        if(_S387)
        {
            _S388 = (float4(_S41->lightDirectionIntensity_0) ).w;
        }
        else
        {
            float4 _S389 = ReadFrameLight_0(2U, lightIndex_0, &kernelContext_2);
            _S388 = _S389.w;
        }
        bool _S390;
        if(_S382)
        {
            _S390 = _S380;
        }
        else
        {
            _S390 = false;
        }
        if(_S390)
        {
            diffuseColor_0 = _S375.xyz;
        }
        else
        {
            float4 _S391 = ReadFrameLight_0(2U, lightIndex_0, &kernelContext_2);
            diffuseColor_0 = _S391.xyz;
        }
        bool _S392;
        if(_S382)
        {
            _S392 = _S380;
        }
        else
        {
            _S392 = false;
        }
        float _S393;
        if(_S392)
        {
            _S393 = 1.0f;
        }
        else
        {
            float4 _S394 = ReadFrameLight_0(3U, lightIndex_0, &kernelContext_2);
            _S393 = _S394.x;
        }
        bool _S395;
        if(_S382)
        {
            _S395 = _S380;
        }
        else
        {
            _S395 = false;
        }
        float _S396;
        if(_S395)
        {
            _S396 = 1.0f;
        }
        else
        {
            float4 _S397 = ReadFrameLight_0(3U, lightIndex_0, &kernelContext_2);
            _S396 = _S397.y;
        }
        bool _S398;
        if(_S382)
        {
            _S398 = _S380;
        }
        else
        {
            _S398 = false;
        }
        if(_S398)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            float4 _S399 = ReadFrameLight_0(4U, lightIndex_0, &kernelContext_2);
            lightTangent_0 = normalize(_S399.xyz);
        }
        bool _S400;
        if(_S382)
        {
            _S400 = _S380;
        }
        else
        {
            _S400 = false;
        }
        if(_S400)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            float4 _S401 = ReadFrameLight_0(5U, lightIndex_0, &kernelContext_2);
            lightBitangent_0 = normalize(_S401.xyz);
        }
        bool _S402;
        if(_S382)
        {
            _S402 = _S380;
        }
        else
        {
            _S402 = false;
        }
        float shapeX_0;
        if(_S402)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            float4 _S403 = ReadFrameLight_0(4U, lightIndex_0, &kernelContext_2);
            shapeX_0 = _S403.w;
        }
        bool _S404;
        if(_S382)
        {
            _S404 = _S380;
        }
        else
        {
            _S404 = false;
        }
        float shapeY_0;
        if(_S404)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            float4 _S405 = ReadFrameLight_0(5U, lightIndex_0, &kernelContext_2);
            shapeY_0 = _S405.w;
        }
        bool _S406;
        if(_S382)
        {
            _S406 = _S380;
        }
        else
        {
            _S406 = false;
        }
        float lightRadius_0;
        if(_S406)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            float4 _S407 = ReadFrameLight_0(1U, lightIndex_0, &kernelContext_2);
            lightRadius_0 = _S407.w;
        }
        bool _S408;
        if(_S382)
        {
            _S408 = _S380;
        }
        else
        {
            _S408 = false;
        }
        if(_S408)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        float shadowVisibility_0;
        float sampleCount_0;
        if(hasSceneLighting_0)
        {
            float4 _S409 = ReadFrameShadowSlot_0(lightIndex_0, &kernelContext_2);
            int shadowSlot_0 = int(_S409.x);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S410 = _S36;
                    FragmentFrameParameters_0 _S411 = _S36;
                    if((dot(diffuseFresnel_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - diffuseFresnel_0;
                    }
                    else
                    {
                        color_0 = diffuseFresnel_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S410.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S36.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S412 = lightClip_0.w;
                    if(_S412 <= 0.0f)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S412) ;
                    bool _S413;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S413 = true;
                    }
                    else
                    {
                        _S413 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S414;
                    if(_S413)
                    {
                        _S414 = true;
                    }
                    else
                    {
                        _S414 = (ndc_0.z) < 0.0f;
                    }
                    bool _S415;
                    if(_S414)
                    {
                        _S415 = true;
                    }
                    else
                    {
                        _S415 = (ndc_0.z) > 1.0f;
                    }
                    if(_S415)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float2 _S416 = _S411.shadowTile_0[shadowSlot_0].xy;
                    float2 _S417 = _S411.shadowTile_0[shadowSlot_0].zw;
                    float2 _S418 = _S416 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S417;
                    float texel_0 = _S410.shadowControls_0[shadowSlot_0].w;
                    float _S419 = max(_S410.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S420 = ndc_0.z - _S410.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S421 = float2((texel_0 * 0.5f)) ;
                    float2 _S422 = _S416 + _S421;
                    float2 _S423 = _S416 + _S417 - _S421;
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
                            if(_S420 <= (((&kernelContext_2)->shadowAtlas_0).sample(((&kernelContext_2)->shadowSampler_0), (clamp(_S418 + float2(float(x_0), float(y_0)) * float2((_S419 * texel_0)) , _S422, _S423)), level((0.0f))).x))
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
        float3 _S424 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S424;
        sampleOffsets_0[int(1)] = _S424;
        sampleOffsets_0[int(2)] = _S424;
        sampleOffsets_0[int(3)] = _S424;
        sampleOffsets_0[int(4)] = _S424;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S425 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S425 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S425 - halfHeight_0;
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
            float sampleIntensity_0 = _S388 / sampleCount_0;
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(lightType_0 >= 2.0f)
            {
                float4 _S426 = ReadFrameLight_0(0U, lightIndex_0, &kernelContext_2);
                float3 toLight_0 = _S426.xyz + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S427 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S427)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S427;
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
            float _S428 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S428)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S428) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S429 = float3(_S428) ;
            float _S430 = max(_S363, 0.00100000004749745f);
            float alpha_0 = _S430 * _S430;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S431 = 1.0f - k_0;
            float3 _S432 = float3(max(4.0f * normalDotLight_0 * _S362, 1.00000000317107685e-30f)) ;
            float3 _S433 = mix(normalIncidence_0, grazingIncidence_0, _S429) * float3((_S362 / (_S362 * _S431 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S431 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S432;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S434 = max(_S364, 0.00100000004749745f);
                float alpha_1 = _S434 * _S434;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S435 = 1.0f - k_1;
                directSpecular_0 = _S433 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S429) * float3((_S362 / (_S362 * _S435 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S435 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S432);
            }
            else
            {
                directSpecular_0 = _S433;
            }
            float3 _S436 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S393)  * (_S436 * _S365) + directSpecular_0 * float3(_S396)  * _S436);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S437 = _S36;
    if((_S36.environmentControls_0.x) >= 0.5f)
    {
        float _S438 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S439 = saturate(_S363);
        float2 _S440 = (((&kernelContext_2)->environmentBrdf_0).sample(((&kernelContext_2)->environmentBrdfSampler_0), (float2(_S438, _S439)), level((0.0f)))).xy;
        float _S441 = _S440.x;
        float _S442 = _S440.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S441)  + grazingIncidence_0 * float3(_S442) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S441 + _S442)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S443 = max(_S376.domeControls_0.y, 1.0f);
        float3 _S444 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S443 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S376.domeControls_0.z;
            float _S445 = _S376.domeControls_0.w;
            for(;;)
            {
                bool _S446 = _S443 <= 1.0f;
                _S39 = _S446;
                if(_S446)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S445, 1.0f);
                diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S443)), level((0.0f)))).xyz;
                break;
            }
            float _S447 = _S437.environmentControls_0.y;
            float _S448 = _S437.environmentControls_0.z;
            for(;;)
            {
                if(_S39)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S449 = max(_S447, 1.0f);
                    float _S450 = _S449 - 1.0f;
                    float slice_0 = _S439 * max(_S450, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S448, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S449)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S450) + v_0) / _S449)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S451 = max(_S447, 1.0f);
                float total_0 = _S451 * _S443;
                float _S452 = _S451 - 1.0f;
                float slice_1 = _S439 * max(_S452, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S448, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S451;
                unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S452) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S39)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_3 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S453 = max(_S447, 1.0f);
                        float _S454 = _S453 - 1.0f;
                        float slice_2 = saturate(_S364) * max(_S454, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S448, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S453)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S454) + v_2) / _S453)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S455 = max(_S447, 1.0f);
                    float total_1 = _S455 * _S443;
                    float _S456 = _S455 - 1.0f;
                    float slice_3 = saturate(_S364) * max(_S456, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S448, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S455;
                    normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S456) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightBitangent_0 = normal_2;
            }
            else
            {
                lightBitangent_0 = _S444;
            }
            lightDirection_0 = diffuseColor_0;
            lightTangent_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightDirection_0 = _S444;
            lightTangent_0 = _S444;
            lightBitangent_0 = _S444;
            for(;;)
            {
                if(sampleIndex_0 < _S377)
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
                float domeGroup_0 = _S36.domeEnvironment_0[sampleIndex_0].x;
                if(domeGroup_0 < 0.0f)
                {
                    sampleIndex_0 = sampleIndex_0 + 1U;
                    continue;
                }
                float _S457 = _S376.domeControls_0.w;
                for(;;)
                {
                    bool _S458 = _S443 <= 1.0f;
                    _S40 = _S458;
                    if(_S458)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S457, 1.0f);
                    diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S443)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightDirection_0 + diffuseColor_0;
                float _S459 = _S437.environmentControls_0.y;
                float _S460 = _S437.environmentControls_0.z;
                for(;;)
                {
                    if(_S40)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S461 = max(_S459, 1.0f);
                        float _S462 = _S461 - 1.0f;
                        float slice_4 = _S439 * max(_S462, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S460, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S461)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S462) + v_4) / _S461)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S463 = max(_S459, 1.0f);
                    float total_2 = _S463 * _S443;
                    float _S464 = _S463 - 1.0f;
                    float slice_5 = _S439 * max(_S464, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S460, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S463;
                    unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S464) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightTangent_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S40)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_7 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S465 = max(_S459, 1.0f);
                            float _S466 = _S465 - 1.0f;
                            float slice_6 = saturate(_S364) * max(_S466, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S460, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S465)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S466) + v_6) / _S465)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S467 = max(_S459, 1.0f);
                        float total_3 = _S467 * _S443;
                        float _S468 = _S467 - 1.0f;
                        float slice_7 = saturate(_S364) * max(_S468, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S460, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S467;
                        normal_2 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S468) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
        float3 _S469 = float3(occlusion_0) ;
        float3 color_4 = color_3 + _S469 * diffuse_1 * lightDirection_0 * diffuseWeight_0 + _S469 * lightTangent_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S470 = (((&kernelContext_2)->environmentBrdf_0).sample(((&kernelContext_2)->environmentBrdfSampler_0), (float2(_S438, saturate(_S364))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightBitangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S470.x)  + float3(_S470.y) );
        }
        else
        {
            color_3 = color_4;
        }
    }
    float3 color_5 = (color_3 + emissiveColor_0) * float3(exp2((as_type<float>((_S37.z))))) ;
    if((_S37.y) == 1U)
    {
        color_3 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_3 = color_5;
    }
    pixelOutput_0 _S471 = { float4(color_3, opacity_0) };
    return _S471;
}
