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

[[fragment]] pixelOutput_0 volumeFragmentMain(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture3d<float, access::sample> volumeDensityTexture_1 [[texture(9)]], sampler volumeDensitySampler_1 [[sampler(4)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_0;
    FragmentFrameParameters_0 _S2;
    uint4 _S3;
    float _S4;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    bool _S5;
    bool _S6;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->volumeDensityTexture_0 = volumeDensityTexture_1;
    (&kernelContext_0)->volumeDensitySampler_0 = volumeDensitySampler_1;
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
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 14880U;
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
        float _S40 = as_type<float>((&kernelContext_0)->frameParameters_0[(16928U)>>2]);
        float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(16932U)>>2]);
        float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(16936U)>>2]);
        float _S43 = as_type<float>((&kernelContext_0)->frameParameters_0[(16940U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S40, _S41, _S42, _S43);
        float _S44 = as_type<float>((&kernelContext_0)->frameParameters_0[(16944U)>>2]);
        float _S45 = as_type<float>((&kernelContext_0)->frameParameters_0[(16948U)>>2]);
        float _S46 = as_type<float>((&kernelContext_0)->frameParameters_0[(16952U)>>2]);
        float _S47 = as_type<float>((&kernelContext_0)->frameParameters_0[(16956U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S44, _S45, _S46, _S47);
        float _S48 = as_type<float>((&kernelContext_0)->frameParameters_0[(16960U)>>2]);
        float _S49 = as_type<float>((&kernelContext_0)->frameParameters_0[(16964U)>>2]);
        float _S50 = as_type<float>((&kernelContext_0)->frameParameters_0[(16968U)>>2]);
        float _S51 = as_type<float>((&kernelContext_0)->frameParameters_0[(16972U)>>2]);
        float4 _S52 = float4(_S48, _S49, _S50, _S51);
        float _S53 = as_type<float>((&kernelContext_0)->frameParameters_0[(16976U)>>2]);
        float _S54 = as_type<float>((&kernelContext_0)->frameParameters_0[(16980U)>>2]);
        float _S55 = as_type<float>((&kernelContext_0)->frameParameters_0[(16984U)>>2]);
        float _S56 = as_type<float>((&kernelContext_0)->frameParameters_0[(16988U)>>2]);
        float4 _S57 = float4(_S53, _S54, _S55, _S56);
        float _S58 = as_type<float>((&kernelContext_0)->frameParameters_0[(16992U)>>2]);
        float _S59 = as_type<float>((&kernelContext_0)->frameParameters_0[(16996U)>>2]);
        float _S60 = as_type<float>((&kernelContext_0)->frameParameters_0[(17000U)>>2]);
        float _S61 = as_type<float>((&kernelContext_0)->frameParameters_0[(17004U)>>2]);
        float4 _S62 = float4(_S58, _S59, _S60, _S61);
        float _S63 = as_type<float>((&kernelContext_0)->frameParameters_0[(17008U)>>2]);
        float _S64 = as_type<float>((&kernelContext_0)->frameParameters_0[(17012U)>>2]);
        float _S65 = as_type<float>((&kernelContext_0)->frameParameters_0[(17016U)>>2]);
        float _S66 = as_type<float>((&kernelContext_0)->frameParameters_0[(17020U)>>2]);
        float4 _S67 = float4(_S63, _S64, _S65, _S66);
        float _S68 = as_type<float>((&kernelContext_0)->frameParameters_0[(17024U)>>2]);
        float _S69 = as_type<float>((&kernelContext_0)->frameParameters_0[(17028U)>>2]);
        float _S70 = as_type<float>((&kernelContext_0)->frameParameters_0[(17032U)>>2]);
        float _S71 = as_type<float>((&kernelContext_0)->frameParameters_0[(17036U)>>2]);
        float4 _S72 = float4(_S68, _S69, _S70, _S71);
        float _S73 = as_type<float>((&kernelContext_0)->frameParameters_0[(17040U)>>2]);
        float _S74 = as_type<float>((&kernelContext_0)->frameParameters_0[(17044U)>>2]);
        float _S75 = as_type<float>((&kernelContext_0)->frameParameters_0[(17048U)>>2]);
        float _S76 = as_type<float>((&kernelContext_0)->frameParameters_0[(17052U)>>2]);
        float4 _S77 = float4(_S73, _S74, _S75, _S76);
        float _S78 = as_type<float>((&kernelContext_0)->frameParameters_0[(17056U)>>2]);
        float _S79 = as_type<float>((&kernelContext_0)->frameParameters_0[(17060U)>>2]);
        float _S80 = as_type<float>((&kernelContext_0)->frameParameters_0[(17064U)>>2]);
        float _S81 = as_type<float>((&kernelContext_0)->frameParameters_0[(17068U)>>2]);
        float4 _S82 = float4(_S78, _S79, _S80, _S81);
        float _S83 = as_type<float>((&kernelContext_0)->frameParameters_0[(17072U)>>2]);
        float _S84 = as_type<float>((&kernelContext_0)->frameParameters_0[(17076U)>>2]);
        float _S85 = as_type<float>((&kernelContext_0)->frameParameters_0[(17080U)>>2]);
        float _S86 = as_type<float>((&kernelContext_0)->frameParameters_0[(17084U)>>2]);
        float4 _S87 = float4(_S83, _S84, _S85, _S86);
        float _S88 = as_type<float>((&kernelContext_0)->frameParameters_0[(17088U)>>2]);
        float _S89 = as_type<float>((&kernelContext_0)->frameParameters_0[(17092U)>>2]);
        float _S90 = as_type<float>((&kernelContext_0)->frameParameters_0[(17096U)>>2]);
        float _S91 = as_type<float>((&kernelContext_0)->frameParameters_0[(17100U)>>2]);
        float4 _S92 = float4(_S88, _S89, _S90, _S91);
        float _S93 = as_type<float>((&kernelContext_0)->frameParameters_0[(17104U)>>2]);
        float _S94 = as_type<float>((&kernelContext_0)->frameParameters_0[(17108U)>>2]);
        float _S95 = as_type<float>((&kernelContext_0)->frameParameters_0[(17112U)>>2]);
        float _S96 = as_type<float>((&kernelContext_0)->frameParameters_0[(17116U)>>2]);
        float4 _S97 = float4(_S93, _S94, _S95, _S96);
        float _S98 = as_type<float>((&kernelContext_0)->frameParameters_0[(17120U)>>2]);
        float _S99 = as_type<float>((&kernelContext_0)->frameParameters_0[(17124U)>>2]);
        float _S100 = as_type<float>((&kernelContext_0)->frameParameters_0[(17128U)>>2]);
        float _S101 = as_type<float>((&kernelContext_0)->frameParameters_0[(17132U)>>2]);
        float4 _S102 = float4(_S98, _S99, _S100, _S101);
        float _S103 = as_type<float>((&kernelContext_0)->frameParameters_0[(17136U)>>2]);
        float _S104 = as_type<float>((&kernelContext_0)->frameParameters_0[(17140U)>>2]);
        float _S105 = as_type<float>((&kernelContext_0)->frameParameters_0[(17144U)>>2]);
        float _S106 = as_type<float>((&kernelContext_0)->frameParameters_0[(17148U)>>2]);
        float4 _S107 = float4(_S103, _S104, _S105, _S106);
        float _S108 = as_type<float>((&kernelContext_0)->frameParameters_0[(17152U)>>2]);
        float _S109 = as_type<float>((&kernelContext_0)->frameParameters_0[(17156U)>>2]);
        float _S110 = as_type<float>((&kernelContext_0)->frameParameters_0[(17160U)>>2]);
        float _S111 = as_type<float>((&kernelContext_0)->frameParameters_0[(17164U)>>2]);
        float4 _S112 = float4(_S108, _S109, _S110, _S111);
        float _S113 = as_type<float>((&kernelContext_0)->frameParameters_0[(17168U)>>2]);
        float _S114 = as_type<float>((&kernelContext_0)->frameParameters_0[(17172U)>>2]);
        float _S115 = as_type<float>((&kernelContext_0)->frameParameters_0[(17176U)>>2]);
        float _S116 = as_type<float>((&kernelContext_0)->frameParameters_0[(17180U)>>2]);
        float4 _S117 = float4(_S113, _S114, _S115, _S116);
        float _S118 = as_type<float>((&kernelContext_0)->frameParameters_0[(17184U)>>2]);
        float _S119 = as_type<float>((&kernelContext_0)->frameParameters_0[(17188U)>>2]);
        float _S120 = as_type<float>((&kernelContext_0)->frameParameters_0[(17192U)>>2]);
        float _S121 = as_type<float>((&kernelContext_0)->frameParameters_0[(17196U)>>2]);
        float4 _S122 = float4(_S118, _S119, _S120, _S121);
        float _S123 = as_type<float>((&kernelContext_0)->frameParameters_0[(17200U)>>2]);
        float _S124 = as_type<float>((&kernelContext_0)->frameParameters_0[(17204U)>>2]);
        float _S125 = as_type<float>((&kernelContext_0)->frameParameters_0[(17208U)>>2]);
        float _S126 = as_type<float>((&kernelContext_0)->frameParameters_0[(17212U)>>2]);
        float4 _S127 = float4(_S123, _S124, _S125, _S126);
        (&frame_0)->domeAmbient_0[int(0)] = _S52;
        (&frame_0)->domeAmbient_0[int(1)] = _S57;
        (&frame_0)->domeAmbient_0[int(2)] = _S62;
        (&frame_0)->domeAmbient_0[int(3)] = _S67;
        (&frame_0)->domeAmbient_0[int(4)] = _S72;
        (&frame_0)->domeAmbient_0[int(5)] = _S77;
        (&frame_0)->domeAmbient_0[int(6)] = _S82;
        (&frame_0)->domeAmbient_0[int(7)] = _S87;
        (&frame_0)->domeEnvironment_0[int(0)] = _S92;
        (&frame_0)->domeEnvironment_0[int(1)] = _S97;
        (&frame_0)->domeEnvironment_0[int(2)] = _S102;
        (&frame_0)->domeEnvironment_0[int(3)] = _S107;
        (&frame_0)->domeEnvironment_0[int(4)] = _S112;
        (&frame_0)->domeEnvironment_0[int(5)] = _S117;
        (&frame_0)->domeEnvironment_0[int(6)] = _S122;
        (&frame_0)->domeEnvironment_0[int(7)] = _S127;
        _S2 = frame_0;
        break;
    }
    for(;;)
    {
        _S3 = _S2.clipPlaneCount_0;
        uint _S128 = min(_S2.clipPlaneCount_0.x, 8U);
        index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S128)
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
    float4 _S129 = float4(_S7->clearcoatShaded_0) ;
    float shadedMode_0 = _S129.z;
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
    float4 _S130 = float4(_S7->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S130.xyz;
    float occlusion_0 = _S130.w;
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
        pixelOutput_0 _S131 = { float4(unlitColor_0, opacity_0) };
        return _S131;
    }
    float4 _S132 = float4(_S7->reserved_0) ;
    uint step_0;
    float density_0;
    if((_S132.x) >= 0.5f)
    {
        if((_S132.w) >= 0.5f)
        {
            float _S133 = (float4(_S7->textureControls_0) ).z;
            for(;;)
            {
                float2 _S134 = saturate(_S1.objectPosition_0.xy + float2(0.5f, 0.5f));
                uint stepCount_0 = uint(clamp(_S133, 1.0f, 512.0f));
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
                    float density_1 = density_0 + (((&kernelContext_0)->volumeDensityTexture_0).sample(((&kernelContext_0)->volumeDensitySampler_0), (float3(_S134, (float(step_0) + 0.5f) / float(stepCount_0)))).x);
                    step_0 = step_0 + 1U;
                    density_0 = density_1;
                }
                _S4 = density_0 / float(stepCount_0);
                break;
            }
            density_0 = _S4 * max(0.0f, _S132.y);
        }
        else
        {
            density_0 = max(0.0f, _S132.y);
        }
        pixelOutput_0 _S135 = { float4(diffuseColor_0 * float3((1.0f - exp(- density_0 * max(0.0f, _S132.z)))) , 1.0f) };
        return _S135;
    }
    float4 _S136 = float4(_S7->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S136.x);
    float roughness_0 = clamp(_S136.y, 0.00999999977648258f, 1.0f);
    float4 _S137 = float4(_S7->specularIor_0) ;
    float3 specularColor_0 = _S137.xyz;
    float ior_0 = _S137.w;
    float clearcoatAmount_0 = _S129.x;
    float clearcoatRoughness_0 = _S129.y;
    float opacityThreshold_0 = _S136.z;
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
    float _S138 = max(0.00100000004749745f, roughness_0);
    float _S139 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S140 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S140;
    bool omniPbrSpecular_0 = ((float4(_S7->textureControls_0) ).w) >= 0.5f;
    float dielectricLevel_0 = _S129.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S141 = float3(dielectricLevel_0) ;
        float3 _S142 = float3(metallic_0) ;
        float3 _S143 = mix(_S141, diffuseColor_0, _S142);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S141, diffuseColor_0, _S142);
        grazingIncidence_0 = _S143;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S136.w) >= 0.5f)
        {
            float3 _S144 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S144;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S145 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S145);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S145);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S146 = _S2;
    float _S147 = _S2.ambientLight_0.w;
    bool hasSceneLighting_0;
    if(_S147 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S148 = _S146.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S148, _S148)) > 0.0f;
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
    uint _S149 = min(uint(_S147), 128U);
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
    float4 _S150 = float4(_S7->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S150.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S7->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S151 = _S2;
    uint _S152 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S152) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S153 = _S146.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S154 = float3(0.0f, 0.0f, 0.0f);
        step_0 = 0U;
        domeAmbient_1 = _S154;
        for(;;)
        {
            if(step_0 < _S152)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << step_0)) != 0U)
            {
                domeAmbient_1 = domeAmbient_1 + _S2.domeAmbient_0[step_0].xyz;
            }
            step_0 = step_0 + 1U;
        }
    }
    else
    {
        domeAmbient_1 = _S153;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S155 = !hasSceneLighting_0;
    if(_S155)
    {
        step_0 = 1U;
    }
    else
    {
        step_0 = _S149;
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
        bool _S156;
        if(hasSceneLighting_0)
        {
            _S156 = (((uint4(_S7->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S156 = false;
        }
        if(_S156)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S157 = lightIndex_0 == 0U;
        bool _S158;
        if(_S157)
        {
            _S158 = _S155;
        }
        else
        {
            _S158 = false;
        }
        if(_S158)
        {
            density_0 = 1.0f;
        }
        else
        {
            uint _S159 = 224U + lightIndex_0 * 16U;
            float _S160 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S159)>>2]);
            float _S161 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S159 + 4U)>>2]);
            float _S162 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S159 + 8U)>>2]);
            float _S163 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S159 + 12U)>>2]);
            density_0 = _S163;
        }
        bool _S164;
        if(_S157)
        {
            _S164 = _S155;
        }
        else
        {
            _S164 = false;
        }
        if(_S164)
        {
            lightDirection_0 = normalize((float4(_S7->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S165 = 2272U + lightIndex_0 * 16U;
            float _S166 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S165)>>2]);
            float _S167 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S165 + 4U)>>2]);
            float _S168 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S165 + 8U)>>2]);
            float _S169 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S165 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S166, _S167, _S168));
        }
        bool _S170;
        if(_S157)
        {
            _S170 = _S155;
        }
        else
        {
            _S170 = false;
        }
        float _S171;
        if(_S170)
        {
            _S171 = (float4(_S7->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S172 = 4320U + lightIndex_0 * 16U;
            float _S173 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S172)>>2]);
            float _S174 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S172 + 4U)>>2]);
            float _S175 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S172 + 8U)>>2]);
            float _S176 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S172 + 12U)>>2]);
            _S171 = _S176;
        }
        bool _S177;
        if(_S157)
        {
            _S177 = _S155;
        }
        else
        {
            _S177 = false;
        }
        if(_S177)
        {
            diffuseColor_0 = _S150.xyz;
        }
        else
        {
            uint _S178 = 4320U + lightIndex_0 * 16U;
            float _S179 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S178)>>2]);
            float _S180 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S178 + 4U)>>2]);
            float _S181 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S178 + 8U)>>2]);
            float _S182 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S178 + 12U)>>2]);
            diffuseColor_0 = float3(_S179, _S180, _S181);
        }
        bool _S183;
        if(_S157)
        {
            _S183 = _S155;
        }
        else
        {
            _S183 = false;
        }
        float _S184;
        if(_S183)
        {
            _S184 = 1.0f;
        }
        else
        {
            uint _S185 = 6368U + lightIndex_0 * 16U;
            float _S186 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S185)>>2]);
            float _S187 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S185 + 4U)>>2]);
            float _S188 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S185 + 8U)>>2]);
            float _S189 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S185 + 12U)>>2]);
            _S184 = _S186;
        }
        bool _S190;
        if(_S157)
        {
            _S190 = _S155;
        }
        else
        {
            _S190 = false;
        }
        float _S191;
        if(_S190)
        {
            _S191 = 1.0f;
        }
        else
        {
            uint _S192 = 6368U + lightIndex_0 * 16U;
            float _S193 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S192)>>2]);
            float _S194 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S192 + 4U)>>2]);
            float _S195 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S192 + 8U)>>2]);
            float _S196 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S192 + 12U)>>2]);
            _S191 = _S194;
        }
        bool _S197;
        if(_S157)
        {
            _S197 = _S155;
        }
        else
        {
            _S197 = false;
        }
        if(_S197)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S198 = 8416U + lightIndex_0 * 16U;
            float _S199 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S198)>>2]);
            float _S200 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S198 + 4U)>>2]);
            float _S201 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S198 + 8U)>>2]);
            float _S202 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S198 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S199, _S200, _S201));
        }
        bool _S203;
        if(_S157)
        {
            _S203 = _S155;
        }
        else
        {
            _S203 = false;
        }
        float3 lightBitangent_0;
        if(_S203)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S204 = 10464U + lightIndex_0 * 16U;
            float _S205 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S204)>>2]);
            float _S206 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S204 + 4U)>>2]);
            float _S207 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S204 + 8U)>>2]);
            float _S208 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S204 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S205, _S206, _S207));
        }
        bool _S209;
        if(_S157)
        {
            _S209 = _S155;
        }
        else
        {
            _S209 = false;
        }
        float shapeX_0;
        if(_S209)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S210 = 8416U + lightIndex_0 * 16U;
            float _S211 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S210)>>2]);
            float _S212 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S210 + 4U)>>2]);
            float _S213 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S210 + 8U)>>2]);
            float _S214 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S210 + 12U)>>2]);
            shapeX_0 = _S214;
        }
        bool _S215;
        if(_S157)
        {
            _S215 = _S155;
        }
        else
        {
            _S215 = false;
        }
        float shapeY_0;
        if(_S215)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S216 = 10464U + lightIndex_0 * 16U;
            float _S217 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S216)>>2]);
            float _S218 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S216 + 4U)>>2]);
            float _S219 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S216 + 8U)>>2]);
            float _S220 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S216 + 12U)>>2]);
            shapeY_0 = _S220;
        }
        bool _S221;
        if(_S157)
        {
            _S221 = _S155;
        }
        else
        {
            _S221 = false;
        }
        float lightRadius_0;
        if(_S221)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S222 = 2272U + lightIndex_0 * 16U;
            float _S223 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S222)>>2]);
            float _S224 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S222 + 4U)>>2]);
            float _S225 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S222 + 8U)>>2]);
            float _S226 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S222 + 12U)>>2]);
            lightRadius_0 = _S226;
        }
        bool _S227;
        if(_S157)
        {
            _S227 = _S155;
        }
        else
        {
            _S227 = false;
        }
        float3 lightEyeBase_0;
        if(_S227)
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
            directNormal_0 = irradiance_0;
        }
        float _S228 = saturate(abs(dot(directNormal_0, lightEyeBase_0)) + 0.00000999999974738f);
        bool _S229;
        if(_S157)
        {
            _S229 = _S155;
        }
        else
        {
            _S229 = false;
        }
        float4 _S230;
        if(_S229)
        {
            _S230 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S231 = 17216U + lightIndex_0 * 16U;
            float _S232 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S231)>>2]);
            float _S233 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S231 + 4U)>>2]);
            float _S234 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S231 + 8U)>>2]);
            float _S235 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S231 + 12U)>>2]);
            _S230 = float4(_S232, _S233, _S234, _S235);
        }
        float3 color_3;
        float3 sampleDirection_0;
        float shadowVisibility_0;
        float sampleCount_0;
        if(hasSceneLighting_0)
        {
            uint _S236 = lightIndex_0 * 16U;
            uint _S237 = 14880U + _S236;
            float _S238 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S237)>>2]);
            float _S239 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S237 + 4U)>>2]);
            float _S240 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S237 + 8U)>>2]);
            float _S241 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S237 + 12U)>>2]);
            int shadowSlot_0 = int(_S238);
            if(shadowSlot_0 >= int(0))
            {
                int shadowSlot_1;
                int y_0;
                if(_S239 == 6.0f)
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
                        int _S242 = shadowSlot_0 + y_0;
                        uint _S243 = 12576U + uint(_S242) * 64U;
                        float _S244 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243)>>2]);
                        float _S245 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 4U)>>2]);
                        float _S246 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 8U)>>2]);
                        float _S247 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 12U)>>2]);
                        float4 _S248 = float4(_S244, _S245, _S246, _S247);
                        float _S249 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 16U)>>2]);
                        float _S250 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 20U)>>2]);
                        float _S251 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 24U)>>2]);
                        float _S252 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 28U)>>2]);
                        float4 _S253 = float4(_S249, _S250, _S251, _S252);
                        float _S254 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 32U)>>2]);
                        float _S255 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 36U)>>2]);
                        float _S256 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 40U)>>2]);
                        float _S257 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 44U)>>2]);
                        float4 _S258 = float4(_S254, _S255, _S256, _S257);
                        float _S259 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 48U)>>2]);
                        float _S260 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 52U)>>2]);
                        float _S261 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 56U)>>2]);
                        float _S262 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S243 + 60U)>>2]);
                        float _S263 = (((float4(worldPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S248, _S253, _S258, float4(_S259, _S260, _S261, _S262))))).w;
                        if(_S263 > shadowVisibility_0)
                        {
                            shadowVisibility_0 = _S263;
                            shadowSlot_1 = _S242;
                        }
                        y_0 = y_0 + int(1);
                    }
                }
                else
                {
                    shadowSlot_1 = shadowSlot_0;
                }
                if(density_0 == 2.0f)
                {
                    uint _S264 = 224U + _S236;
                    float _S265 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S264)>>2]);
                    float _S266 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S264 + 4U)>>2]);
                    float _S267 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S264 + 8U)>>2]);
                    float _S268 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S264 + 12U)>>2]);
                    float3 toShadowLight_0 = float3(_S265, _S266, _S267) - worldPosition_0;
                    color_3 = toShadowLight_0 * float3(rsqrt(max(dot(toShadowLight_0, toShadowLight_0), 0.00100000004749745f))) ;
                }
                else
                {
                    color_3 = lightDirection_0;
                }
                for(;;)
                {
                    uint _S269 = uint(shadowSlot_1);
                    uint _S270 = _S269 * 16U;
                    uint _S271 = 14496U + _S270;
                    float _S272 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S271)>>2]);
                    float _S273 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S271 + 4U)>>2]);
                    float _S274 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S271 + 8U)>>2]);
                    float _S275 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S271 + 12U)>>2]);
                    uint _S276 = 14112U + _S270;
                    float _S277 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S276)>>2]);
                    float _S278 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S276 + 4U)>>2]);
                    float _S279 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S276 + 8U)>>2]);
                    float _S280 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S276 + 12U)>>2]);
                    if((dot(prefiltered_0, color_3)) < 0.0f)
                    {
                        sampleDirection_0 = - prefiltered_0;
                    }
                    else
                    {
                        sampleDirection_0 = prefiltered_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(sampleDirection_0, color_3)), 0.0f, 1.0f);
                    float3 offsetPosition_0 = worldPosition_0 + sampleDirection_0 * float3((_S273 * slope_0)) ;
                    uint _S281 = 12576U + _S269 * 64U;
                    float _S282 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281)>>2]);
                    float _S283 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 4U)>>2]);
                    float _S284 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 8U)>>2]);
                    float _S285 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 12U)>>2]);
                    float4 _S286 = float4(_S282, _S283, _S284, _S285);
                    float _S287 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 16U)>>2]);
                    float _S288 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 20U)>>2]);
                    float _S289 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 24U)>>2]);
                    float _S290 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 28U)>>2]);
                    float4 _S291 = float4(_S287, _S288, _S289, _S290);
                    float _S292 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 32U)>>2]);
                    float _S293 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 36U)>>2]);
                    float _S294 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 40U)>>2]);
                    float _S295 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 44U)>>2]);
                    float4 _S296 = float4(_S292, _S293, _S294, _S295);
                    float _S297 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 48U)>>2]);
                    float _S298 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 52U)>>2]);
                    float _S299 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 56U)>>2]);
                    float _S300 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S281 + 60U)>>2]);
                    float4 lightClip_0 = (((float4(offsetPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S286, _S291, _S296, float4(_S297, _S298, _S299, _S300)))));
                    float _S301 = lightClip_0.w;
                    if(_S301 <= 0.0f)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S301) ;
                    bool _S302;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S302 = true;
                    }
                    else
                    {
                        _S302 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S303;
                    if(_S302)
                    {
                        _S303 = true;
                    }
                    else
                    {
                        _S303 = (ndc_0.z) < 0.0f;
                    }
                    bool _S304;
                    if(_S303)
                    {
                        _S304 = true;
                    }
                    else
                    {
                        _S304 = (ndc_0.z) > 1.0f;
                    }
                    if(_S304)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float2 _S305 = float2(_S277, _S278);
                    float2 _S306 = float2(_S279, _S280);
                    float2 _S307 = _S305 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S306;
                    float _S308 = max(_S274, 0.0f);
                    float _S309 = ndc_0.z - _S272 * (1.0f + 2.0f * slope_0);
                    float2 _S310 = float2((_S275 * 0.5f)) ;
                    float2 _S311 = _S305 + _S310;
                    float2 _S312 = _S305 + _S306 - _S310;
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
                            if(_S309 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S307 + float2(float(x_0), float(y_0)) * float2((_S308 * _S275)) , _S311, _S312)), level((0.0f))).x))
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
        float3 _S313 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S313;
        sampleOffsets_0[int(1)] = _S313;
        sampleOffsets_0[int(2)] = _S313;
        sampleOffsets_0[int(3)] = _S313;
        sampleOffsets_0[int(4)] = _S313;
        if(density_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S314 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S314 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S314 - halfHeight_0;
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
            float sampleIntensity_0 = _S171 / sampleCount_0;
            float sampleIntensity_1;
            float emissionScale_0;
            if(density_0 >= 2.0f)
            {
                uint _S315 = 224U + lightIndex_0 * 16U;
                float _S316 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S315)>>2]);
                float _S317 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S315 + 4U)>>2]);
                float _S318 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S315 + 8U)>>2]);
                float _S319 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S315 + 12U)>>2]);
                float3 toLight_0 = float3(_S316, _S317, _S318) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S320 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S320)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S320;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(density_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S230.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S230.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S321 = _S230.y;
                        if(_S321 > 0.0f)
                        {
                            emissionScale_0 = pow(max(emitterCosine_0, 0.0f), _S321);
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
            float _S322 = pow(max(0.0f, 1.0f - saturate(dot(lightEyeBase_0, half_0))), 5.0f);
            float3 diffuseFresnel_0;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_0 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S322)) ;
            }
            else
            {
                diffuseFresnel_0 = mix(normalIncidence_0, grazingIncidence_0, float3(_S322) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_0);
            float3 _S323 = float3(_S322) ;
            float _S324 = max(_S138, 0.00100000004749745f);
            float alpha_0 = _S324 * _S324;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S325 = 1.0f - k_0;
            float3 _S326 = float3(max(4.0f * normalDotLight_0 * _S228, 1.00000000317107685e-30f)) ;
            float3 _S327 = mix(normalIncidence_0, grazingIncidence_0, _S323) * float3((_S228 / (_S228 * _S325 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S325 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S326;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S328 = max(_S139, 0.00100000004749745f);
                float alpha_1 = _S328 * _S328;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S329 = 1.0f - k_1;
                directSpecular_0 = _S327 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S323) * float3((_S228 / (_S228 * _S329 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S329 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S326);
            }
            else
            {
                directSpecular_0 = _S327;
            }
            float3 _S330 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S184)  * (_S330 * _S140) + directSpecular_0 * float3(_S191)  * _S330);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S331 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S332 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S333 = saturate(_S138);
        float2 _S334 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S332, _S333)), level((0.0f)))).xy;
        float _S335 = _S334.x;
        float _S336 = _S334.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S335)  + grazingIncidence_0 * float3(_S336) ;
        if(omniPbrSpecular_0)
        {
            unlitColor_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S335 + _S336)) ;
        }
        else
        {
            unlitColor_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - unlitColor_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S337 = max(_S151.domeControls_0.y, 1.0f);
        float3 _S338 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S337 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S151.domeControls_0.z;
            float _S339 = _S151.domeControls_0.w;
            for(;;)
            {
                bool _S340 = _S337 <= 1.0f;
                _S5 = _S340;
                if(_S340)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S339, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S337)), level((0.0f)))).xyz;
                break;
            }
            float _S341 = _S331.environmentControls_0.y;
            float _S342 = _S331.environmentControls_0.z;
            for(;;)
            {
                if(_S5)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S343 = max(_S341, 1.0f);
                    float _S344 = _S343 - 1.0f;
                    float slice_0 = _S333 * max(_S344, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S342, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S343)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S344) + v_0) / _S343)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S345 = max(_S341, 1.0f);
                float total_0 = _S345 * _S337;
                float _S346 = _S345 - 1.0f;
                float slice_1 = _S333 * max(_S346, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S342, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S345;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S346) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_2 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S347 = max(_S341, 1.0f);
                        float _S348 = _S347 - 1.0f;
                        float slice_2 = saturate(_S139) * max(_S348, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S342, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S347)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S348) + v_2) / _S347)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S349 = max(_S341, 1.0f);
                    float total_1 = _S349 * _S337;
                    float _S350 = _S349 - 1.0f;
                    float slice_3 = saturate(_S139) * max(_S350, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S342, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S349;
                    irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S350) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightDirection_0 = irradiance_0;
            }
            else
            {
                lightDirection_0 = _S338;
            }
            irradiance_0 = diffuseColor_0;
            prefiltered_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S338;
            prefiltered_0 = _S338;
            lightDirection_0 = _S338;
            for(;;)
            {
                if(sampleIndex_0 < _S152)
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
                float _S351 = _S151.domeControls_0.w;
                for(;;)
                {
                    bool _S352 = _S337 <= 1.0f;
                    _S6 = _S352;
                    if(_S352)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S351, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S337)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                float _S353 = _S331.environmentControls_0.y;
                float _S354 = _S331.environmentControls_0.z;
                for(;;)
                {
                    if(_S6)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S355 = max(_S353, 1.0f);
                        float _S356 = _S355 - 1.0f;
                        float slice_4 = _S333 * max(_S356, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S354, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S355)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S356) + v_4) / _S355)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S357 = max(_S353, 1.0f);
                    float total_2 = _S357 * _S337;
                    float _S358 = _S357 - 1.0f;
                    float slice_5 = _S333 * max(_S358, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S354, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S357;
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S358) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_1 = prefiltered_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S6)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S359 = max(_S353, 1.0f);
                            float _S360 = _S359 - 1.0f;
                            float slice_6 = saturate(_S139) * max(_S360, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S354, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normalIncidence_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S359)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S360) + v_6) / _S359)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S361 = max(_S353, 1.0f);
                        float total_3 = _S361 * _S337;
                        float _S362 = _S361 - 1.0f;
                        float slice_7 = saturate(_S139) * max(_S362, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S354, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S361;
                        normalIncidence_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S362) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    lightTangent_0 = lightDirection_0 + normalIncidence_0;
                }
                else
                {
                    lightTangent_0 = lightDirection_0;
                }
                irradiance_0 = irradiance_1;
                prefiltered_0 = prefiltered_1;
                lightDirection_0 = lightTangent_0;
                sampleIndex_0 = sampleIndex_0 + 1U;
            }
        }
        float3 _S363 = float3(occlusion_0) ;
        float3 color_4 = color_2 + _S363 * diffuse_1 * irradiance_0 * diffuseWeight_0 + _S363 * prefiltered_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S364 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S332, saturate(_S139))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightDirection_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S364.x)  + float3(_S364.y) );
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
    pixelOutput_0 _S365 = { float4(color_2, opacity_0) };
    return _S365;
}
