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
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 14880U;
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
        float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(16928U)>>2]);
        float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(16932U)>>2]);
        float _S43 = as_type<float>((&kernelContext_0)->frameParameters_0[(16936U)>>2]);
        float _S44 = as_type<float>((&kernelContext_0)->frameParameters_0[(16940U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S41, _S42, _S43, _S44);
        float _S45 = as_type<float>((&kernelContext_0)->frameParameters_0[(16944U)>>2]);
        float _S46 = as_type<float>((&kernelContext_0)->frameParameters_0[(16948U)>>2]);
        float _S47 = as_type<float>((&kernelContext_0)->frameParameters_0[(16952U)>>2]);
        float _S48 = as_type<float>((&kernelContext_0)->frameParameters_0[(16956U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S45, _S46, _S47, _S48);
        float _S49 = as_type<float>((&kernelContext_0)->frameParameters_0[(16960U)>>2]);
        float _S50 = as_type<float>((&kernelContext_0)->frameParameters_0[(16964U)>>2]);
        float _S51 = as_type<float>((&kernelContext_0)->frameParameters_0[(16968U)>>2]);
        float _S52 = as_type<float>((&kernelContext_0)->frameParameters_0[(16972U)>>2]);
        float4 _S53 = float4(_S49, _S50, _S51, _S52);
        float _S54 = as_type<float>((&kernelContext_0)->frameParameters_0[(16976U)>>2]);
        float _S55 = as_type<float>((&kernelContext_0)->frameParameters_0[(16980U)>>2]);
        float _S56 = as_type<float>((&kernelContext_0)->frameParameters_0[(16984U)>>2]);
        float _S57 = as_type<float>((&kernelContext_0)->frameParameters_0[(16988U)>>2]);
        float4 _S58 = float4(_S54, _S55, _S56, _S57);
        float _S59 = as_type<float>((&kernelContext_0)->frameParameters_0[(16992U)>>2]);
        float _S60 = as_type<float>((&kernelContext_0)->frameParameters_0[(16996U)>>2]);
        float _S61 = as_type<float>((&kernelContext_0)->frameParameters_0[(17000U)>>2]);
        float _S62 = as_type<float>((&kernelContext_0)->frameParameters_0[(17004U)>>2]);
        float4 _S63 = float4(_S59, _S60, _S61, _S62);
        float _S64 = as_type<float>((&kernelContext_0)->frameParameters_0[(17008U)>>2]);
        float _S65 = as_type<float>((&kernelContext_0)->frameParameters_0[(17012U)>>2]);
        float _S66 = as_type<float>((&kernelContext_0)->frameParameters_0[(17016U)>>2]);
        float _S67 = as_type<float>((&kernelContext_0)->frameParameters_0[(17020U)>>2]);
        float4 _S68 = float4(_S64, _S65, _S66, _S67);
        float _S69 = as_type<float>((&kernelContext_0)->frameParameters_0[(17024U)>>2]);
        float _S70 = as_type<float>((&kernelContext_0)->frameParameters_0[(17028U)>>2]);
        float _S71 = as_type<float>((&kernelContext_0)->frameParameters_0[(17032U)>>2]);
        float _S72 = as_type<float>((&kernelContext_0)->frameParameters_0[(17036U)>>2]);
        float4 _S73 = float4(_S69, _S70, _S71, _S72);
        float _S74 = as_type<float>((&kernelContext_0)->frameParameters_0[(17040U)>>2]);
        float _S75 = as_type<float>((&kernelContext_0)->frameParameters_0[(17044U)>>2]);
        float _S76 = as_type<float>((&kernelContext_0)->frameParameters_0[(17048U)>>2]);
        float _S77 = as_type<float>((&kernelContext_0)->frameParameters_0[(17052U)>>2]);
        float4 _S78 = float4(_S74, _S75, _S76, _S77);
        float _S79 = as_type<float>((&kernelContext_0)->frameParameters_0[(17056U)>>2]);
        float _S80 = as_type<float>((&kernelContext_0)->frameParameters_0[(17060U)>>2]);
        float _S81 = as_type<float>((&kernelContext_0)->frameParameters_0[(17064U)>>2]);
        float _S82 = as_type<float>((&kernelContext_0)->frameParameters_0[(17068U)>>2]);
        float4 _S83 = float4(_S79, _S80, _S81, _S82);
        float _S84 = as_type<float>((&kernelContext_0)->frameParameters_0[(17072U)>>2]);
        float _S85 = as_type<float>((&kernelContext_0)->frameParameters_0[(17076U)>>2]);
        float _S86 = as_type<float>((&kernelContext_0)->frameParameters_0[(17080U)>>2]);
        float _S87 = as_type<float>((&kernelContext_0)->frameParameters_0[(17084U)>>2]);
        float4 _S88 = float4(_S84, _S85, _S86, _S87);
        float _S89 = as_type<float>((&kernelContext_0)->frameParameters_0[(17088U)>>2]);
        float _S90 = as_type<float>((&kernelContext_0)->frameParameters_0[(17092U)>>2]);
        float _S91 = as_type<float>((&kernelContext_0)->frameParameters_0[(17096U)>>2]);
        float _S92 = as_type<float>((&kernelContext_0)->frameParameters_0[(17100U)>>2]);
        float4 _S93 = float4(_S89, _S90, _S91, _S92);
        float _S94 = as_type<float>((&kernelContext_0)->frameParameters_0[(17104U)>>2]);
        float _S95 = as_type<float>((&kernelContext_0)->frameParameters_0[(17108U)>>2]);
        float _S96 = as_type<float>((&kernelContext_0)->frameParameters_0[(17112U)>>2]);
        float _S97 = as_type<float>((&kernelContext_0)->frameParameters_0[(17116U)>>2]);
        float4 _S98 = float4(_S94, _S95, _S96, _S97);
        float _S99 = as_type<float>((&kernelContext_0)->frameParameters_0[(17120U)>>2]);
        float _S100 = as_type<float>((&kernelContext_0)->frameParameters_0[(17124U)>>2]);
        float _S101 = as_type<float>((&kernelContext_0)->frameParameters_0[(17128U)>>2]);
        float _S102 = as_type<float>((&kernelContext_0)->frameParameters_0[(17132U)>>2]);
        float4 _S103 = float4(_S99, _S100, _S101, _S102);
        float _S104 = as_type<float>((&kernelContext_0)->frameParameters_0[(17136U)>>2]);
        float _S105 = as_type<float>((&kernelContext_0)->frameParameters_0[(17140U)>>2]);
        float _S106 = as_type<float>((&kernelContext_0)->frameParameters_0[(17144U)>>2]);
        float _S107 = as_type<float>((&kernelContext_0)->frameParameters_0[(17148U)>>2]);
        float4 _S108 = float4(_S104, _S105, _S106, _S107);
        float _S109 = as_type<float>((&kernelContext_0)->frameParameters_0[(17152U)>>2]);
        float _S110 = as_type<float>((&kernelContext_0)->frameParameters_0[(17156U)>>2]);
        float _S111 = as_type<float>((&kernelContext_0)->frameParameters_0[(17160U)>>2]);
        float _S112 = as_type<float>((&kernelContext_0)->frameParameters_0[(17164U)>>2]);
        float4 _S113 = float4(_S109, _S110, _S111, _S112);
        float _S114 = as_type<float>((&kernelContext_0)->frameParameters_0[(17168U)>>2]);
        float _S115 = as_type<float>((&kernelContext_0)->frameParameters_0[(17172U)>>2]);
        float _S116 = as_type<float>((&kernelContext_0)->frameParameters_0[(17176U)>>2]);
        float _S117 = as_type<float>((&kernelContext_0)->frameParameters_0[(17180U)>>2]);
        float4 _S118 = float4(_S114, _S115, _S116, _S117);
        float _S119 = as_type<float>((&kernelContext_0)->frameParameters_0[(17184U)>>2]);
        float _S120 = as_type<float>((&kernelContext_0)->frameParameters_0[(17188U)>>2]);
        float _S121 = as_type<float>((&kernelContext_0)->frameParameters_0[(17192U)>>2]);
        float _S122 = as_type<float>((&kernelContext_0)->frameParameters_0[(17196U)>>2]);
        float4 _S123 = float4(_S119, _S120, _S121, _S122);
        float _S124 = as_type<float>((&kernelContext_0)->frameParameters_0[(17200U)>>2]);
        float _S125 = as_type<float>((&kernelContext_0)->frameParameters_0[(17204U)>>2]);
        float _S126 = as_type<float>((&kernelContext_0)->frameParameters_0[(17208U)>>2]);
        float _S127 = as_type<float>((&kernelContext_0)->frameParameters_0[(17212U)>>2]);
        float4 _S128 = float4(_S124, _S125, _S126, _S127);
        (&frame_0)->domeAmbient_0[int(0)] = _S53;
        (&frame_0)->domeAmbient_0[int(1)] = _S58;
        (&frame_0)->domeAmbient_0[int(2)] = _S63;
        (&frame_0)->domeAmbient_0[int(3)] = _S68;
        (&frame_0)->domeAmbient_0[int(4)] = _S73;
        (&frame_0)->domeAmbient_0[int(5)] = _S78;
        (&frame_0)->domeAmbient_0[int(6)] = _S83;
        (&frame_0)->domeAmbient_0[int(7)] = _S88;
        (&frame_0)->domeEnvironment_0[int(0)] = _S93;
        (&frame_0)->domeEnvironment_0[int(1)] = _S98;
        (&frame_0)->domeEnvironment_0[int(2)] = _S103;
        (&frame_0)->domeEnvironment_0[int(3)] = _S108;
        (&frame_0)->domeEnvironment_0[int(4)] = _S113;
        (&frame_0)->domeEnvironment_0[int(5)] = _S118;
        (&frame_0)->domeEnvironment_0[int(6)] = _S123;
        (&frame_0)->domeEnvironment_0[int(7)] = _S128;
        _S2 = frame_0;
        break;
    }
    for(;;)
    {
        _S3 = _S2.clipPlaneCount_0;
        uint _S129 = min(_S2.clipPlaneCount_0.x, 8U);
        index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S129)
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
    float4 _S130 = float4(_S8->clearcoatShaded_0) ;
    float shadedMode_0 = _S130.z;
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
    float4 _S131 = float4(_S8->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S131.xyz;
    float occlusion_0 = _S131.w;
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
        pixelOutput_0 _S132 = { float4(unlitColor_0, opacity_0) };
        return _S132;
    }
    float4 _S133 = float4(_S8->reserved_0) ;
    if((_S133.x) >= 0.5f)
    {
        pixelOutput_0 _S134 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S133.y) * max(0.0f, _S133.z)))) , 1.0f) };
        return _S134;
    }
    float4 _S135 = float4(_S8->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S135.x);
    float roughness_0 = clamp(_S135.y, 0.00999999977648258f, 1.0f);
    float4 _S136 = float4(_S8->specularIor_0) ;
    float3 specularColor_0 = _S136.xyz;
    float ior_0 = _S136.w;
    float clearcoatAmount_0 = _S130.x;
    float clearcoatRoughness_0 = _S130.y;
    float4 _S137 = float4(_S8->textureControls_0) ;
    uint textureMask_0 = uint(round(_S137.x));
    uint udimMask_0 = uint(round(_S137.y));
    float4 _S138 = float4(_S8->uvTransformRow1_0) ;
    uint secondaryMask_0 = uint(round(_S138.w));
    float4 _S139 = float4(_S8->domeLinkControls_0) ;
    uint alternateMask_0 = uint(round(_S139.w));
    float4 _S140 = float4(_S8->uvTransformRow0_0) ;
    float2 _S141 = _S140.xy;
    float _S142 = _S140.z;
    float2 _S143 = _S138.xy;
    float _S144 = _S138.z;
    float2 _S145 = float2(dot(_S141, _S1.texCoord_0) + _S142, dot(_S143, _S1.texCoord_0) + _S144);
    float2 _S146 = float2(dot(_S141, _S1.texCoord1_0) + _S142, dot(_S143, _S1.texCoord1_0) + _S144);
    float4 _S147 = float4(_S8->alternateUvTransformRow0_0) ;
    float4 _S148 = float4(_S8->alternateUvTransformRow1_0) ;
    float2 _S149 = _S147.xy;
    float _S150 = _S147.z;
    float2 _S151 = _S148.xy;
    float _S152 = _S148.z;
    float2 _S153 = float2(dot(_S149, _S1.texCoord_0) + _S150, dot(_S151, _S1.texCoord_0) + _S152);
    float2 _S154 = float2(dot(_S149, _S1.texCoord1_0) + _S150, dot(_S151, _S1.texCoord1_0) + _S152);
    thread float4 baseNormalSample_0;
    bool useSecondary_0 = (secondaryMask_0 & 4U) != 0U;
    bool _S155 = (alternateMask_0 & 4U) != 0U;
    float2 detailSource_0;
    if(_S155)
    {
        if(useSecondary_0)
        {
            detailSource_0 = _S154;
        }
        else
        {
            detailSource_0 = _S153;
        }
    }
    else
    {
        if(useSecondary_0)
        {
            detailSource_0 = _S146;
        }
        else
        {
            detailSource_0 = _S145;
        }
    }
    bool _S156 = (udimMask_0 & 4U) != 0U;
    uint maskU_0 = uint(round(_S139.y));
    uint maskV_0 = uint(round(_S139.z));
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
    uint _S157 = index_0 | lightCount_0;
    for(;;)
    {
        if(!_S156)
        {
            for(;;)
            {
                if(_S157 == 0U)
                {
                    break;
                }
                texture2d<float, access::sample> _S158 = (&kernelContext_0)->normalTexture_0;
                thread uint width_0;
                thread uint height_0;
                (*((&width_0)) = (_S158).get_width(0)),(*((&height_0)) = (_S158).get_height(0));
                float2 size_0 = float2(float(width_0), float(height_0));
                float2 _S159 = float2(0.5f) ;
                float2 remapped_0 = clamp((detailSource_0 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S159 / size_0, (size_0 - _S159) / size_0);
                if((_S157 & 1U) != 0U)
                {
                    lightType_0 = remapped_0.x;
                }
                else
                {
                    lightType_0 = detailSource_0.x;
                }
                if((_S157 & 2U) != 0U)
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
        texture2d<float, access::sample> _S160 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_0;
        thread uint atlasHeight_0;
        (*((&atlasWidth_0)) = (_S160).get_width(0)),(*((&atlasHeight_0)) = (_S160).get_height(0));
        int3 _S161 = int3(int(0), int(0), int(0));
        float4 metadata_0 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S161)).xy), uint(((_S161)).z))) * float4(255.0f) );
        if((metadata_0.x) == 255.0f)
        {
            uint _S162 = atlasWidth_0;
            uint _S163 = 2U % atlasWidth_0;
            int _S164 = int(_S163);
            uint _S165 = 2U / _S162;
            int3 _S166 = int3(_S164, int(_S165), int(0));
            int _S167 = int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S166)).xy), uint(((_S166)).z))).x * 255.0f)));
            uint _S168 = atlasWidth_0;
            uint _S169 = 3U % atlasWidth_0;
            int _S170 = int(_S169);
            uint _S171 = 3U / _S168;
            int3 _S172 = int3(_S170, int(_S171), int(0));
            int2 minimumTile_0 = int2(_S167, int(uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S172)).xy), uint(((_S172)).z))).x * 255.0f))));
            uint _S173 = atlasWidth_0;
            uint _S174 = 4U % atlasWidth_0;
            int _S175 = int(_S174);
            uint _S176 = 4U / _S173;
            int3 _S177 = int3(_S175, int(_S176), int(0));
            uint _S178 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S177)).xy), uint(((_S177)).z))).x * 255.0f));
            uint _S179 = atlasWidth_0;
            uint _S180 = 5U % atlasWidth_0;
            int _S181 = int(_S180);
            uint _S182 = 5U / _S179;
            int3 _S183 = int3(_S181, int(_S182), int(0));
            uint _S184 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S183)).xy), uint(((_S183)).z))).x * 255.0f));
            uint2 grid_0 = uint2(_S178, _S184);
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
                int3 _S185 = int3(int(1), int(0), int(0));
                _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S185)).xy), uint(((_S185)).z)));
                break;
            }
            uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S178 + uint(tile_0.x));
            uint _S186 = atlasWidth_0;
            uint _S187 = offset_0 % atlasWidth_0;
            int _S188 = int(_S187);
            uint _S189 = offset_0 / _S186;
            int3 _S190 = int3(_S188, int(_S189), int(0));
            uint _S191 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S190)).xy), uint(((_S190)).z))).x * 255.0f));
            uint _S192 = atlasWidth_0;
            uint _S193 = offset_0 + 1U;
            uint _S194 = _S193 % atlasWidth_0;
            int _S195 = int(_S194);
            uint _S196 = _S193 / _S192;
            int3 _S197 = int3(_S195, int(_S196), int(0));
            uint slot_0 = _S191 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S197)).xy), uint(((_S197)).z))).x * 255.0f)) << 8U);
            if(slot_0 == 0U)
            {
                int3 _S198 = int3(int(1), int(0), int(0));
                _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S198)).xy), uint(((_S198)).z)));
                break;
            }
            uint slot_1 = slot_0 - 1U;
            uint _S199 = atlasWidth_0;
            uint _S200 = 6U % atlasWidth_0;
            int _S201 = int(_S200);
            uint _S202 = 6U / _S199;
            int3 _S203 = int3(_S201, int(_S202), int(0));
            uint _S204 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S203)).xy), uint(((_S203)).z))).x * 255.0f));
            uint _S205 = atlasWidth_0;
            uint _S206 = 7U % atlasWidth_0;
            int _S207 = int(_S206);
            uint _S208 = 7U / _S205;
            int3 _S209 = int3(_S207, int(_S208), int(0));
            uint columns_0 = _S204 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S209)).xy), uint(((_S209)).z))).x * 255.0f)) << 8U);
            uint _S210 = atlasWidth_0;
            uint _S211 = 8U % atlasWidth_0;
            int _S212 = int(_S211);
            uint _S213 = 8U / _S210;
            int3 _S214 = int3(_S212, int(_S213), int(0));
            uint _S215 = uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S214)).xy), uint(((_S214)).z))).x * 255.0f));
            uint _S216 = atlasWidth_0;
            uint _S217 = 9U % atlasWidth_0;
            int _S218 = int(_S217);
            uint _S219 = 9U / _S216;
            int3 _S220 = int3(_S218, int(_S219), int(0));
            uint rows_0 = ((_S215 | (uint(round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S220)).xy), uint(((_S220)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
            uint metadataRows_0 = (10U + 2U * _S178 * _S184 + atlasWidth_0 - 1U) / atlasWidth_0;
            uint _S221 = atlasWidth_0 / columns_0;
            float _S222 = float(_S221);
            uint _S223 = (atlasHeight_0 - metadataRows_0) / rows_0;
            float2 cellSize_0 = float2(_S222, float(_S223));
            uint _S224 = slot_1 % columns_0;
            float _S225 = float(_S224);
            uint _S226 = slot_1 / columns_0;
            _S5 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(_S225, float(_S226)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(detailSource_0) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        int2 _S227 = int2(metadata_0.zw);
        int2 tile_1 = int2(floor(detailSource_0)) - int2(metadata_0.xy);
        if(any(tile_1 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_1 >= _S227);
        }
        if(hasSceneLighting_0)
        {
            int3 _S228 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
            _S5 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S228)).xy), uint(((_S228)).z)));
            break;
        }
        uint _S229 = atlasWidth_0 / uint(_S227.x);
        float _S230 = float(_S229);
        uint _S231 = (atlasHeight_0 - 1U) / uint(_S227.y);
        float2 cellSize_1 = float2(_S230, float(_S231));
        _S5 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(detailSource_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
        break;
    }
    baseNormalSample_0 = _S5;
    float4 _S232 = float4(_S8->detailUvTransformRow0_0) ;
    uint3 _S233;
    if((_S232.w) != 0.0f)
    {
        float4 _S234 = baseNormalSample_0;
        for(;;)
        {
            float _S235 = _S234.w;
            if(_S235 > 1.0f)
            {
                unlitColor_0 = _S234.xyz;
                break;
            }
            texture2d<float, access::sample> _S236 = (&kernelContext_0)->normalValuesTexture_0;
            thread uint width_1;
            thread uint height_1;
            (*((&width_1)) = (_S236).get_width(0)),(*((&height_1)) = (_S236).get_height(0));
            uint3 _S237 = uint3(round(_S234.xyz * float3(255.0f) ));
            uint index_1 = ((_S237.x) | ((_S237.y) << 8U)) | ((_S237.z) << 16U);
            if(_S235 == 0.0f)
            {
                _S233 = _S237;
            }
            else
            {
                _S233 = uint3(index_1, index_1, index_1);
            }
            uint3 address_0 = _S233 * uint3(3U)  + uint3(0U, 1U, 2U);
            uint _S238 = address_0.x;
            uint _S239 = _S238 % width_1;
            int _S240 = int(_S239);
            uint _S241 = _S238 / width_1;
            int3 _S242 = int3(_S240, int(_S241), int(0));
            float _S243 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S242)).xy), uint(((_S242)).z))).x;
            uint _S244 = address_0.y;
            uint _S245 = _S244 % width_1;
            int _S246 = int(_S245);
            uint _S247 = _S244 / width_1;
            int3 _S248 = int3(_S246, int(_S247), int(0));
            float _S249 = (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S248)).xy), uint(((_S248)).z))).x;
            uint _S250 = address_0.z;
            uint _S251 = _S250 % width_1;
            int _S252 = int(_S251);
            uint _S253 = _S250 / width_1;
            int3 _S254 = int3(_S252, int(_S253), int(0));
            unlitColor_0 = float3(_S243, _S249, (((&kernelContext_0)->normalValuesTexture_0).read(vec<uint,2>(((_S254)).xy), uint(((_S254)).z))).x);
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
    if(_S155)
    {
        detailSource_0 = _S149;
    }
    else
    {
        detailSource_0 = _S141;
    }
    float2 tangent_1;
    if(_S155)
    {
        tangent_1 = _S151;
    }
    else
    {
        tangent_1 = _S143;
    }
    thread float3 _S255 = unlitColor_0;
    float2 _S256 = float2(0.0f, 0.0f);
    if(any(tangent_1 != _S256))
    {
        float _S257 = tangent_1.x;
        float _S258 = tangent_1.y;
        float2 tangent_2 = normalize(float2(_S258, - _S257) / float2(max(abs(_S257), abs(_S258))) );
        float2 bitangent_0 = float2(- tangent_2.y, tangent_2.x);
        if((dot(detailSource_0, tangent_2)) < 0.0f)
        {
            detailSource_0 = - tangent_2;
        }
        else
        {
            detailSource_0 = tangent_2;
        }
        _S255.xy = detailSource_0 * float2(_S255.x)  + bitangent_0 * float2(_S255.y) ;
    }
    float3 _S259 = _S255;
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
        float2 _S260 = _S232.xy;
        float _S261 = dot(_S260, detailSource_0) + _S232.z;
        float4 _S262 = float4(_S8->detailUvTransformRow1_0) ;
        float2 _S263 = _S262.xy;
        float _S264 = dot(_S263, detailSource_0) + _S262.z;
        float2 detailUv_0 = float2(_S261, _S264);
        thread float4 detailSample_0;
        bool _S265 = (udimMask_0 & 4096U) != 0U;
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
        uint _S266 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S265)
            {
                for(;;)
                {
                    if(_S266 == 0U)
                    {
                        detailSource_0 = detailUv_0;
                        break;
                    }
                    texture2d<float, access::sample> _S267 = (&kernelContext_0)->detailNormalTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S267).get_width(0)),(*((&height_2)) = (_S267).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S268 = float2(0.5f) ;
                    float2 remapped_1 = clamp((detailUv_0 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S268 / size_1, (size_1 - _S268) / size_1);
                    if((_S266 & 1U) != 0U)
                    {
                        lightType_0 = remapped_1.x;
                    }
                    else
                    {
                        lightType_0 = _S261;
                    }
                    if((_S266 & 2U) != 0U)
                    {
                        _S4 = remapped_1.y;
                    }
                    else
                    {
                        _S4 = _S264;
                    }
                    detailSource_0 = float2(lightType_0, _S4);
                    break;
                }
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), (detailSource_0)));
                break;
            }
            texture2d<float, access::sample> _S269 = (&kernelContext_0)->detailNormalTexture_0;
            thread uint atlasWidth_1;
            thread uint atlasHeight_1;
            (*((&atlasWidth_1)) = (_S269).get_width(0)),(*((&atlasHeight_1)) = (_S269).get_height(0));
            int3 _S270 = int3(int(0), int(0), int(0));
            float4 metadata_1 = round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S270)).xy), uint(((_S270)).z))) * float4(255.0f) );
            if((metadata_1.x) == 255.0f)
            {
                uint _S271 = atlasWidth_1;
                uint _S272 = 2U % atlasWidth_1;
                int _S273 = int(_S272);
                uint _S274 = 2U / _S271;
                int3 _S275 = int3(_S273, int(_S274), int(0));
                int _S276 = int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S275)).xy), uint(((_S275)).z))).x * 255.0f)));
                uint _S277 = atlasWidth_1;
                uint _S278 = 3U % atlasWidth_1;
                int _S279 = int(_S278);
                uint _S280 = 3U / _S277;
                int3 _S281 = int3(_S279, int(_S280), int(0));
                int2 minimumTile_1 = int2(_S276, int(uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S281)).xy), uint(((_S281)).z))).x * 255.0f))));
                uint _S282 = atlasWidth_1;
                uint _S283 = 4U % atlasWidth_1;
                int _S284 = int(_S283);
                uint _S285 = 4U / _S282;
                int3 _S286 = int3(_S284, int(_S285), int(0));
                uint _S287 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S286)).xy), uint(((_S286)).z))).x * 255.0f));
                uint _S288 = atlasWidth_1;
                uint _S289 = 5U % atlasWidth_1;
                int _S290 = int(_S289);
                uint _S291 = 5U / _S288;
                int3 _S292 = int3(_S290, int(_S291), int(0));
                uint _S293 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S292)).xy), uint(((_S292)).z))).x * 255.0f));
                uint2 grid_1 = uint2(_S287, _S293);
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
                    int3 _S294 = int3(int(1), int(0), int(0));
                    _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S294)).xy), uint(((_S294)).z)));
                    break;
                }
                uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S287 + uint(tile_2.x));
                uint _S295 = atlasWidth_1;
                uint _S296 = offset_1 % atlasWidth_1;
                int _S297 = int(_S296);
                uint _S298 = offset_1 / _S295;
                int3 _S299 = int3(_S297, int(_S298), int(0));
                uint _S300 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S299)).xy), uint(((_S299)).z))).x * 255.0f));
                uint _S301 = atlasWidth_1;
                uint _S302 = offset_1 + 1U;
                uint _S303 = _S302 % atlasWidth_1;
                int _S304 = int(_S303);
                uint _S305 = _S302 / _S301;
                int3 _S306 = int3(_S304, int(_S305), int(0));
                uint slot_2 = _S300 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S306)).xy), uint(((_S306)).z))).x * 255.0f)) << 8U);
                if(slot_2 == 0U)
                {
                    int3 _S307 = int3(int(1), int(0), int(0));
                    _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S307)).xy), uint(((_S307)).z)));
                    break;
                }
                uint slot_3 = slot_2 - 1U;
                uint _S308 = atlasWidth_1;
                uint _S309 = 6U % atlasWidth_1;
                int _S310 = int(_S309);
                uint _S311 = 6U / _S308;
                int3 _S312 = int3(_S310, int(_S311), int(0));
                uint _S313 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S312)).xy), uint(((_S312)).z))).x * 255.0f));
                uint _S314 = atlasWidth_1;
                uint _S315 = 7U % atlasWidth_1;
                int _S316 = int(_S315);
                uint _S317 = 7U / _S314;
                int3 _S318 = int3(_S316, int(_S317), int(0));
                uint columns_1 = _S313 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S318)).xy), uint(((_S318)).z))).x * 255.0f)) << 8U);
                uint _S319 = atlasWidth_1;
                uint _S320 = 8U % atlasWidth_1;
                int _S321 = int(_S320);
                uint _S322 = 8U / _S319;
                int3 _S323 = int3(_S321, int(_S322), int(0));
                uint _S324 = uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S323)).xy), uint(((_S323)).z))).x * 255.0f));
                uint _S325 = atlasWidth_1;
                uint _S326 = 9U % atlasWidth_1;
                int _S327 = int(_S326);
                uint _S328 = 9U / _S325;
                int3 _S329 = int3(_S327, int(_S328), int(0));
                uint rows_1 = ((_S324 | (uint(round((((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S329)).xy), uint(((_S329)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                uint metadataRows_1 = (10U + 2U * _S287 * _S293 + atlasWidth_1 - 1U) / atlasWidth_1;
                uint _S330 = atlasWidth_1 / columns_1;
                float _S331 = float(_S330);
                uint _S332 = (atlasHeight_1 - metadataRows_1) / rows_1;
                float2 cellSize_2 = float2(_S331, float(_S332));
                uint _S333 = slot_3 % columns_1;
                float _S334 = float(_S333);
                uint _S335 = slot_3 / columns_1;
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(_S334, float(_S335)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(detailUv_0) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            int2 _S336 = int2(metadata_1.zw);
            int2 tile_3 = int2(floor(detailUv_0)) - int2(metadata_1.xy);
            if(any(tile_3 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_3 >= _S336);
            }
            if(hasSceneLighting_0)
            {
                int3 _S337 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                _S5 = (((&kernelContext_0)->detailNormalTexture_0).read(vec<uint,2>(((_S337)).xy), uint(((_S337)).z)));
                break;
            }
            uint _S338 = atlasWidth_1 / uint(_S336.x);
            float _S339 = float(_S338);
            uint _S340 = (atlasHeight_1 - 1U) / uint(_S336.y);
            float2 cellSize_3 = float2(_S339, float(_S340));
            _S5 = (((&kernelContext_0)->detailNormalTexture_0).sample(((&kernelContext_0)->detailNormalSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
            break;
        }
        detailSample_0 = _S5;
        if((_S262.w) != 0.0f)
        {
            float4 _S341 = detailSample_0;
            for(;;)
            {
                float _S342 = _S341.w;
                if(_S342 > 1.0f)
                {
                    unlitColor_0 = _S341.xyz;
                    break;
                }
                texture2d<float, access::sample> _S343 = (&kernelContext_0)->detailNormalValuesTexture_0;
                thread uint width_3;
                thread uint height_3;
                (*((&width_3)) = (_S343).get_width(0)),(*((&height_3)) = (_S343).get_height(0));
                uint3 _S344 = uint3(round(_S341.xyz * float3(255.0f) ));
                uint index_2 = ((_S344.x) | ((_S344.y) << 8U)) | ((_S344.z) << 16U);
                if(_S342 == 0.0f)
                {
                    _S233 = _S344;
                }
                else
                {
                    _S233 = uint3(index_2, index_2, index_2);
                }
                uint3 address_1 = _S233 * uint3(3U)  + uint3(0U, 1U, 2U);
                uint _S345 = address_1.x;
                uint _S346 = _S345 % width_3;
                int _S347 = int(_S346);
                uint _S348 = _S345 / width_3;
                int3 _S349 = int3(_S347, int(_S348), int(0));
                float _S350 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S349)).xy), uint(((_S349)).z))).x;
                uint _S351 = address_1.y;
                uint _S352 = _S351 % width_3;
                int _S353 = int(_S352);
                uint _S354 = _S351 / width_3;
                int3 _S355 = int3(_S353, int(_S354), int(0));
                float _S356 = (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S355)).xy), uint(((_S355)).z))).x;
                uint _S357 = address_1.z;
                uint _S358 = _S357 % width_3;
                int _S359 = int(_S358);
                uint _S360 = _S357 / width_3;
                int3 _S361 = int3(_S359, int(_S360), int(0));
                unlitColor_0 = float3(_S350, _S356, (((&kernelContext_0)->detailNormalValuesTexture_0).read(vec<uint,2>(((_S361)).xy), uint(((_S361)).z))).x);
                break;
            }
            detailSample_0.xyz = unlitColor_0;
        }
        float3 detail_0 = detailSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
        thread float3 _S362 = detail_0;
        if(any(_S263 != _S256))
        {
            float _S363 = _S263.x;
            float _S364 = _S263.y;
            float2 tangent_3 = normalize(float2(_S364, - _S363) / float2(max(abs(_S363), abs(_S364))) );
            float2 bitangent_1 = float2(- tangent_3.y, tangent_3.x);
            if((dot(_S260, tangent_3)) < 0.0f)
            {
                tangent_1 = - tangent_3;
            }
            else
            {
                tangent_1 = tangent_3;
            }
            _S362.xy = tangent_1 * float2(_S362.x)  + bitangent_1 * float2(_S362.y) ;
        }
        float3 _S365 = _S362;
        for(;;)
        {
            float3 t_0 = normalize(_S259) + float3(0.0f, 0.0f, 1.0f);
            float3 u_0 = normalize(_S365) * float3(-1.0f, -1.0f, 1.0f);
            float _S366 = t_0.z;
            if(_S366 == 0.0f)
            {
                unlitColor_0 = float3(- u_0.x, u_0.y, - u_0.z);
                break;
            }
            unlitColor_0 = normalize(t_0 * float3((dot(t_0, u_0) / _S366))  - u_0);
            break;
        }
    }
    else
    {
        unlitColor_0 = _S259;
    }
    float3 tangent_4 = normalize(_S1.tangent_0.xyz);
    float3 _S367 = float3(unlitColor_0.x) ;
    float3 _S368 = float3(unlitColor_0.y) ;
    float3 _S369 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_4 * _S367 + cross(normalize(_S1.normal_0), tangent_4) * float3(_S1.tangent_0.w)  * _S368 + _S1.normal_0 * _S369);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S367 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S368 + worldNormalBasis_0 * _S369);
    float opacityThreshold_0 = _S135.z;
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
    float _S370 = max(0.00100000004749745f, roughness_0);
    float _S371 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S372 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S372;
    bool omniPbrSpecular_0 = (_S137.w) >= 0.5f;
    float dielectricLevel_0 = _S130.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S373 = float3(dielectricLevel_0) ;
        float3 _S374 = float3(metallic_0) ;
        float3 _S375 = mix(_S373, diffuseColor_0, _S374);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S373, diffuseColor_0, _S374);
        grazingIncidence_0 = _S375;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S135.w) >= 0.5f)
        {
            float3 _S376 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S376;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S377 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S377);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S377);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S378 = _S2;
    float _S379 = _S2.ambientLight_0.w;
    if(_S379 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S380 = _S378.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S380, _S380)) > 0.0f;
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
    uint _S381 = min(uint(_S379), 128U);
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
    float4 _S382 = float4(_S8->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S382.w) ;
    uint domeLinkMask_0 = uint(max(_S139.x, 0.0f));
    FragmentFrameParameters_0 _S383 = _S2;
    uint _S384 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S384) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S385 = _S378.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S386 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S386;
        for(;;)
        {
            if(lightCount_0 < _S384)
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
        domeAmbient_1 = _S385;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S387 = !hasSceneLighting_0;
    if(_S387)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S381;
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
        bool _S388;
        if(hasSceneLighting_0)
        {
            _S388 = (((uint4(_S8->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S388 = false;
        }
        if(_S388)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S389 = lightIndex_0 == 0U;
        bool _S390;
        if(_S389)
        {
            _S390 = _S387;
        }
        else
        {
            _S390 = false;
        }
        if(_S390)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S391 = 224U + lightIndex_0 * 16U;
            float _S392 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S391)>>2]);
            float _S393 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S391 + 4U)>>2]);
            float _S394 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S391 + 8U)>>2]);
            float _S395 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S391 + 12U)>>2]);
            lightType_0 = _S395;
        }
        bool _S396;
        if(_S389)
        {
            _S396 = _S387;
        }
        else
        {
            _S396 = false;
        }
        if(_S396)
        {
            lightDirection_0 = normalize((float4(_S8->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S397 = 2272U + lightIndex_0 * 16U;
            float _S398 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S397)>>2]);
            float _S399 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S397 + 4U)>>2]);
            float _S400 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S397 + 8U)>>2]);
            float _S401 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S397 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S398, _S399, _S400));
        }
        bool _S402;
        if(_S389)
        {
            _S402 = _S387;
        }
        else
        {
            _S402 = false;
        }
        if(_S402)
        {
            _S4 = (float4(_S8->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S403 = 4320U + lightIndex_0 * 16U;
            float _S404 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S403)>>2]);
            float _S405 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S403 + 4U)>>2]);
            float _S406 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S403 + 8U)>>2]);
            float _S407 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S403 + 12U)>>2]);
            _S4 = _S407;
        }
        bool _S408;
        if(_S389)
        {
            _S408 = _S387;
        }
        else
        {
            _S408 = false;
        }
        if(_S408)
        {
            diffuseColor_0 = _S382.xyz;
        }
        else
        {
            uint _S409 = 4320U + lightIndex_0 * 16U;
            float _S410 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S409)>>2]);
            float _S411 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S409 + 4U)>>2]);
            float _S412 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S409 + 8U)>>2]);
            float _S413 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S409 + 12U)>>2]);
            diffuseColor_0 = float3(_S410, _S411, _S412);
        }
        bool _S414;
        if(_S389)
        {
            _S414 = _S387;
        }
        else
        {
            _S414 = false;
        }
        float _S415;
        if(_S414)
        {
            _S415 = 1.0f;
        }
        else
        {
            uint _S416 = 6368U + lightIndex_0 * 16U;
            float _S417 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416)>>2]);
            float _S418 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416 + 4U)>>2]);
            float _S419 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416 + 8U)>>2]);
            float _S420 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S416 + 12U)>>2]);
            _S415 = _S417;
        }
        bool _S421;
        if(_S389)
        {
            _S421 = _S387;
        }
        else
        {
            _S421 = false;
        }
        float _S422;
        if(_S421)
        {
            _S422 = 1.0f;
        }
        else
        {
            uint _S423 = 6368U + lightIndex_0 * 16U;
            float _S424 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423)>>2]);
            float _S425 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423 + 4U)>>2]);
            float _S426 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423 + 8U)>>2]);
            float _S427 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S423 + 12U)>>2]);
            _S422 = _S425;
        }
        bool _S428;
        if(_S389)
        {
            _S428 = _S387;
        }
        else
        {
            _S428 = false;
        }
        if(_S428)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S429 = 8416U + lightIndex_0 * 16U;
            float _S430 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S429)>>2]);
            float _S431 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S429 + 4U)>>2]);
            float _S432 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S429 + 8U)>>2]);
            float _S433 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S429 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S430, _S431, _S432));
        }
        bool _S434;
        if(_S389)
        {
            _S434 = _S387;
        }
        else
        {
            _S434 = false;
        }
        if(_S434)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S435 = 10464U + lightIndex_0 * 16U;
            float _S436 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435)>>2]);
            float _S437 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435 + 4U)>>2]);
            float _S438 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435 + 8U)>>2]);
            float _S439 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S435 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S436, _S437, _S438));
        }
        bool _S440;
        if(_S389)
        {
            _S440 = _S387;
        }
        else
        {
            _S440 = false;
        }
        float shapeX_0;
        if(_S440)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S441 = 8416U + lightIndex_0 * 16U;
            float _S442 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S441)>>2]);
            float _S443 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S441 + 4U)>>2]);
            float _S444 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S441 + 8U)>>2]);
            float _S445 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S441 + 12U)>>2]);
            shapeX_0 = _S445;
        }
        bool _S446;
        if(_S389)
        {
            _S446 = _S387;
        }
        else
        {
            _S446 = false;
        }
        float shapeY_0;
        if(_S446)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S447 = 10464U + lightIndex_0 * 16U;
            float _S448 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S447)>>2]);
            float _S449 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S447 + 4U)>>2]);
            float _S450 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S447 + 8U)>>2]);
            float _S451 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S447 + 12U)>>2]);
            shapeY_0 = _S451;
        }
        bool _S452;
        if(_S389)
        {
            _S452 = _S387;
        }
        else
        {
            _S452 = false;
        }
        float lightRadius_0;
        if(_S452)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S453 = 2272U + lightIndex_0 * 16U;
            float _S454 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S453)>>2]);
            float _S455 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S453 + 4U)>>2]);
            float _S456 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S453 + 8U)>>2]);
            float _S457 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S453 + 12U)>>2]);
            lightRadius_0 = _S457;
        }
        bool _S458;
        if(_S389)
        {
            _S458 = _S387;
        }
        else
        {
            _S458 = false;
        }
        float3 lightEyeBase_0;
        if(_S458)
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
        float _S459 = saturate(abs(dot(directNormal_0, lightEyeBase_0)) + 0.00000999999974738f);
        bool _S460;
        if(_S389)
        {
            _S460 = _S387;
        }
        else
        {
            _S460 = false;
        }
        if(_S460)
        {
            _S5 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S461 = 17216U + lightIndex_0 * 16U;
            float _S462 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461)>>2]);
            float _S463 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461 + 4U)>>2]);
            float _S464 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461 + 8U)>>2]);
            float _S465 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S461 + 12U)>>2]);
            _S5 = float4(_S462, _S463, _S464, _S465);
        }
        float3 color_3;
        float3 sampleDirection_0;
        float shadowVisibility_0;
        float sampleCount_0;
        if(hasSceneLighting_0)
        {
            uint _S466 = lightIndex_0 * 16U;
            uint _S467 = 14880U + _S466;
            float _S468 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467)>>2]);
            float _S469 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 4U)>>2]);
            float _S470 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 8U)>>2]);
            float _S471 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S467 + 12U)>>2]);
            int shadowSlot_0 = int(_S468);
            if(shadowSlot_0 >= int(0))
            {
                int shadowSlot_1;
                int y_0;
                if(_S469 == 6.0f)
                {
                    shadowVisibility_0 = -1.0f;
                    shadowSlot_1 = shadowSlot_0;
                    y_0 = int(0);
                    for(;;)
                    {
                        if(y_0 < int(6))
                        {
                        }
                        else
                        {
                            break;
                        }
                        int _S472 = shadowSlot_0 + y_0;
                        uint _S473 = 12576U + uint(_S472) * 64U;
                        float _S474 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473)>>2]);
                        float _S475 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 4U)>>2]);
                        float _S476 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 8U)>>2]);
                        float _S477 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 12U)>>2]);
                        float4 _S478 = float4(_S474, _S475, _S476, _S477);
                        float _S479 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 16U)>>2]);
                        float _S480 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 20U)>>2]);
                        float _S481 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 24U)>>2]);
                        float _S482 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 28U)>>2]);
                        float4 _S483 = float4(_S479, _S480, _S481, _S482);
                        float _S484 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 32U)>>2]);
                        float _S485 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 36U)>>2]);
                        float _S486 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 40U)>>2]);
                        float _S487 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 44U)>>2]);
                        float4 _S488 = float4(_S484, _S485, _S486, _S487);
                        float _S489 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 48U)>>2]);
                        float _S490 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 52U)>>2]);
                        float _S491 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 56U)>>2]);
                        float _S492 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S473 + 60U)>>2]);
                        float _S493 = (((float4(worldPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S478, _S483, _S488, float4(_S489, _S490, _S491, _S492))))).w;
                        if(_S493 > shadowVisibility_0)
                        {
                            shadowVisibility_0 = _S493;
                            shadowSlot_1 = _S472;
                        }
                        y_0 = y_0 + int(1);
                    }
                }
                else
                {
                    shadowSlot_1 = shadowSlot_0;
                }
                if(lightType_0 == 2.0f)
                {
                    uint _S494 = 224U + _S466;
                    float _S495 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S494)>>2]);
                    float _S496 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S494 + 4U)>>2]);
                    float _S497 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S494 + 8U)>>2]);
                    float _S498 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S494 + 12U)>>2]);
                    float3 toShadowLight_0 = float3(_S495, _S496, _S497) - worldPosition_0;
                    color_3 = toShadowLight_0 * float3(rsqrt(max(dot(toShadowLight_0, toShadowLight_0), 0.00100000004749745f))) ;
                }
                else
                {
                    color_3 = lightDirection_0;
                }
                for(;;)
                {
                    uint _S499 = uint(shadowSlot_1);
                    uint _S500 = _S499 * 16U;
                    uint _S501 = 14496U + _S500;
                    float _S502 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S501)>>2]);
                    float _S503 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S501 + 4U)>>2]);
                    float _S504 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S501 + 8U)>>2]);
                    float _S505 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S501 + 12U)>>2]);
                    uint _S506 = 14112U + _S500;
                    float _S507 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S506)>>2]);
                    float _S508 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S506 + 4U)>>2]);
                    float _S509 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S506 + 8U)>>2]);
                    float _S510 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S506 + 12U)>>2]);
                    if((dot(irradiance_0, color_3)) < 0.0f)
                    {
                        sampleDirection_0 = - irradiance_0;
                    }
                    else
                    {
                        sampleDirection_0 = irradiance_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(sampleDirection_0, color_3)), 0.0f, 1.0f);
                    float3 offsetPosition_0 = worldPosition_0 + sampleDirection_0 * float3((_S503 * slope_0)) ;
                    uint _S511 = 12576U + _S499 * 64U;
                    float _S512 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511)>>2]);
                    float _S513 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 4U)>>2]);
                    float _S514 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 8U)>>2]);
                    float _S515 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 12U)>>2]);
                    float4 _S516 = float4(_S512, _S513, _S514, _S515);
                    float _S517 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 16U)>>2]);
                    float _S518 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 20U)>>2]);
                    float _S519 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 24U)>>2]);
                    float _S520 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 28U)>>2]);
                    float4 _S521 = float4(_S517, _S518, _S519, _S520);
                    float _S522 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 32U)>>2]);
                    float _S523 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 36U)>>2]);
                    float _S524 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 40U)>>2]);
                    float _S525 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 44U)>>2]);
                    float4 _S526 = float4(_S522, _S523, _S524, _S525);
                    float _S527 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 48U)>>2]);
                    float _S528 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 52U)>>2]);
                    float _S529 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 56U)>>2]);
                    float _S530 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S511 + 60U)>>2]);
                    float4 lightClip_0 = (((float4(offsetPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S516, _S521, _S526, float4(_S527, _S528, _S529, _S530)))));
                    float _S531 = lightClip_0.w;
                    if(_S531 <= 0.0f)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S531) ;
                    bool _S532;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S532 = true;
                    }
                    else
                    {
                        _S532 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S533;
                    if(_S532)
                    {
                        _S533 = true;
                    }
                    else
                    {
                        _S533 = (ndc_0.z) < 0.0f;
                    }
                    bool _S534;
                    if(_S533)
                    {
                        _S534 = true;
                    }
                    else
                    {
                        _S534 = (ndc_0.z) > 1.0f;
                    }
                    if(_S534)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float2 _S535 = float2(_S507, _S508);
                    float2 _S536 = float2(_S509, _S510);
                    float2 _S537 = _S535 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S536;
                    float _S538 = max(_S504, 0.0f);
                    float _S539 = ndc_0.z - _S502 * (1.0f + 2.0f * slope_0);
                    float2 _S540 = float2((_S505 * 0.5f)) ;
                    float2 _S541 = _S535 + _S540;
                    float2 _S542 = _S535 + _S536 - _S540;
                    y_0 = int(-1);
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
                            if(_S539 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S537 + float2(float(x_0), float(y_0)) * float2((_S538 * _S505)) , _S541, _S542)), level((0.0f))).x))
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
        float3 _S543 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S543;
        sampleOffsets_0[int(1)] = _S543;
        sampleOffsets_0[int(2)] = _S543;
        sampleOffsets_0[int(3)] = _S543;
        sampleOffsets_0[int(4)] = _S543;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S544 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S544 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S544 - halfHeight_0;
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
            float sampleIntensity_0 = _S4 / sampleCount_0;
            float sampleIntensity_1;
            float emissionScale_0;
            if(lightType_0 >= 2.0f)
            {
                uint _S545 = 224U + lightIndex_0 * 16U;
                float _S546 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S545)>>2]);
                float _S547 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S545 + 4U)>>2]);
                float _S548 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S545 + 8U)>>2]);
                float _S549 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S545 + 12U)>>2]);
                float3 toLight_0 = float3(_S546, _S547, _S548) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S550 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S550)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S550;
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
                        float _S551 = _S5.y;
                        if(_S551 > 0.0f)
                        {
                            emissionScale_0 = pow(max(emitterCosine_0, 0.0f), _S551);
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
            float3 half_0 = normalize(sampleDirection_0 + lightEyeBase_0);
            float normalDotLight_0 = saturate(dot(directNormal_0, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(directNormal_0, half_0));
            float _S552 = pow(max(0.0f, 1.0f - saturate(dot(lightEyeBase_0, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S552)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S552) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S553 = float3(_S552) ;
            float _S554 = max(_S370, 0.00100000004749745f);
            float alpha_0 = _S554 * _S554;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S555 = 1.0f - k_0;
            float3 _S556 = float3(max(4.0f * normalDotLight_0 * _S459, 1.00000000317107685e-30f)) ;
            float3 _S557 = mix(normalIncidence_0, grazingIncidence_0, _S553) * float3((_S459 / (_S459 * _S555 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S555 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S556;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S558 = max(_S371, 0.00100000004749745f);
                float alpha_1 = _S558 * _S558;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S559 = 1.0f - k_1;
                directSpecular_0 = _S557 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S553) * float3((_S459 / (_S459 * _S559 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S559 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S556);
            }
            else
            {
                directSpecular_0 = _S557;
            }
            float3 _S560 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S415)  * (_S560 * _S372) + directSpecular_0 * float3(_S422)  * _S560);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S561 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S562 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S563 = saturate(_S370);
        float2 _S564 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S562, _S563)), level((0.0f)))).xy;
        float _S565 = _S564.x;
        float _S566 = _S564.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S565)  + grazingIncidence_0 * float3(_S566) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S565 + _S566)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S567 = max(_S383.domeControls_0.y, 1.0f);
        float3 _S568 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S567 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S383.domeControls_0.z;
            float _S569 = _S383.domeControls_0.w;
            for(;;)
            {
                bool _S570 = _S567 <= 1.0f;
                _S6 = _S570;
                if(_S570)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S569, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S567)), level((0.0f)))).xyz;
                break;
            }
            float _S571 = _S561.environmentControls_0.y;
            float _S572 = _S561.environmentControls_0.z;
            for(;;)
            {
                if(_S6)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S573 = max(_S571, 1.0f);
                    float _S574 = _S573 - 1.0f;
                    float slice_0 = _S563 * max(_S574, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S572, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S573)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S574) + v_0) / _S573)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S575 = max(_S571, 1.0f);
                float total_0 = _S575 * _S567;
                float _S576 = _S575 - 1.0f;
                float slice_1 = _S563 * max(_S576, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S572, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S575;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S576) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S577 = max(_S571, 1.0f);
                        float _S578 = _S577 - 1.0f;
                        float slice_2 = saturate(_S371) * max(_S578, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S572, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S577)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S578) + v_2) / _S577)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S579 = max(_S571, 1.0f);
                    float total_1 = _S579 * _S567;
                    float _S580 = _S579 - 1.0f;
                    float slice_3 = saturate(_S371) * max(_S580, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S572, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S579;
                    diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S580) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightTangent_0 = diffuseFresnel_0;
            }
            else
            {
                lightTangent_0 = _S568;
            }
            irradiance_0 = diffuseColor_0;
            lightDirection_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S568;
            lightDirection_0 = _S568;
            lightTangent_0 = _S568;
            for(;;)
            {
                if(sampleIndex_0 < _S384)
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
                float _S581 = _S383.domeControls_0.w;
                for(;;)
                {
                    bool _S582 = _S567 <= 1.0f;
                    _S7 = _S582;
                    if(_S582)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S581, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S567)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                float _S583 = _S561.environmentControls_0.y;
                float _S584 = _S561.environmentControls_0.z;
                for(;;)
                {
                    if(_S7)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S585 = max(_S583, 1.0f);
                        float _S586 = _S585 - 1.0f;
                        float slice_4 = _S563 * max(_S586, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S584, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S585)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S586) + v_4) / _S585)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S587 = max(_S583, 1.0f);
                    float total_2 = _S587 * _S567;
                    float _S588 = _S587 - 1.0f;
                    float slice_5 = _S563 * max(_S588, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S584, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S587;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S588) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightDirection_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S7)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_7 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S589 = max(_S583, 1.0f);
                            float _S590 = _S589 - 1.0f;
                            float slice_6 = saturate(_S371) * max(_S590, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S584, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S589)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S590) + v_6) / _S589)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S591 = max(_S583, 1.0f);
                        float total_3 = _S591 * _S567;
                        float _S592 = _S591 - 1.0f;
                        float slice_7 = saturate(_S371) * max(_S592, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S584, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S591;
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S592) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    lightBitangent_0 = lightTangent_0 + diffuseFresnel_0;
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
        float3 _S593 = float3(occlusion_0) ;
        float3 color_4 = color_2 + _S593 * diffuse_1 * irradiance_0 * diffuseWeight_0 + _S593 * lightDirection_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S594 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S562, saturate(_S371))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightTangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S594.x)  + float3(_S594.y) );
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
    pixelOutput_0 _S595 = { float4(color_2, opacity_0) };
    return _S595;
}
