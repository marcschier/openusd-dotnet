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
    texture2d<float, access::sample> baseColorTexture_0;
    sampler baseColorSampler_0;
    texture2d<float, access::sample> diffuseValuesTexture_0;
    texture2d<float, access::sample> compositeTexture_0;
    sampler compositeSampler_0;
    texture2d<float, access::sample> roughnessMetallicTexture_0;
    sampler roughnessMetallicSampler_0;
    texture2d<float, access::sample> metallicTexture_0;
    sampler metallicSampler_0;
    texture2d<float, access::sample> emissiveTexture_0;
    sampler emissiveSampler_0;
    texture2d<float, access::sample> emissiveValuesTexture_0;
    texture2d<float, access::sample> opacityTexture_0;
    sampler opacitySampler_0;
    texture2d<float, access::sample> occlusionTexture_0;
    sampler occlusionSampler_0;
    texture2d<float, access::sample> specularColorTexture_0;
    sampler specularColorSampler_0;
    texture2d<float, access::sample> clearcoatTexture_0;
    sampler clearcoatSampler_0;
    texture2d<float, access::sample> clearcoatRoughnessTexture_0;
    sampler clearcoatRoughnessSampler_0;
    texture2d<float, access::sample> iorTexture_0;
    sampler iorSampler_0;
    texture2d<float, access::sample> shadowAtlas_0;
    sampler shadowSampler_0;
    texture2d<float, access::sample> environmentBrdf_0;
    sampler environmentBrdfSampler_0;
    texture2d<float, access::sample> environmentIrradiance_0;
    sampler environmentSampler_0;
    texture2d<float, access::sample> environmentSpecular_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_material(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> diffuseValuesTexture_1 [[texture(22)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> emissiveValuesTexture_1 [[texture(23)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_0;
    FragmentFrameParameters_0 _S2;
    uint4 _S3;
    float _S4;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    bool _S5;
    bool _S6;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_0)->baseColorSampler_0 = baseColorSampler_1;
    (&kernelContext_0)->diffuseValuesTexture_0 = diffuseValuesTexture_1;
    (&kernelContext_0)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_0)->compositeSampler_0 = compositeSampler_1;
    (&kernelContext_0)->roughnessMetallicTexture_0 = roughnessMetallicTexture_1;
    (&kernelContext_0)->roughnessMetallicSampler_0 = roughnessMetallicSampler_1;
    (&kernelContext_0)->metallicTexture_0 = metallicTexture_1;
    (&kernelContext_0)->metallicSampler_0 = metallicSampler_1;
    (&kernelContext_0)->emissiveTexture_0 = emissiveTexture_1;
    (&kernelContext_0)->emissiveSampler_0 = emissiveSampler_1;
    (&kernelContext_0)->emissiveValuesTexture_0 = emissiveValuesTexture_1;
    (&kernelContext_0)->opacityTexture_0 = opacityTexture_1;
    (&kernelContext_0)->opacitySampler_0 = opacitySampler_1;
    (&kernelContext_0)->occlusionTexture_0 = occlusionTexture_1;
    (&kernelContext_0)->occlusionSampler_0 = occlusionSampler_1;
    (&kernelContext_0)->specularColorTexture_0 = specularColorTexture_1;
    (&kernelContext_0)->specularColorSampler_0 = specularColorSampler_1;
    (&kernelContext_0)->clearcoatTexture_0 = clearcoatTexture_1;
    (&kernelContext_0)->clearcoatSampler_0 = clearcoatSampler_1;
    (&kernelContext_0)->clearcoatRoughnessTexture_0 = clearcoatRoughnessTexture_1;
    (&kernelContext_0)->clearcoatRoughnessSampler_0 = clearcoatRoughnessSampler_1;
    (&kernelContext_0)->iorTexture_0 = iorTexture_1;
    (&kernelContext_0)->iorSampler_0 = iorSampler_1;
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
    float _S131 = _S130.w;
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
    float4 _S133 = float4(_S7->reserved_0) ;
    if((_S133.x) >= 0.5f)
    {
        pixelOutput_0 _S134 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S133.y) * max(0.0f, _S133.z)))) , 1.0f) };
        return _S134;
    }
    float4 _S135 = float4(_S7->metallicRoughnessThresholdWorkflow_0) ;
    float _S136 = saturate(_S135.x);
    float _S137 = clamp(_S135.y, 0.00999999977648258f, 1.0f);
    float4 _S138 = float4(_S7->specularIor_0) ;
    float3 _S139 = _S138.xyz;
    float _S140 = _S138.w;
    float _S141 = _S129.x;
    float _S142 = _S129.y;
    float4 _S143 = float4(_S7->textureControls_0) ;
    uint textureMask_0 = uint(round(_S143.x));
    uint udimMask_0 = uint(round(_S143.y));
    float4 _S144 = float4(_S7->uvTransformRow1_0) ;
    uint secondaryMask_0 = uint(round(_S144.w));
    float4 _S145 = float4(_S7->domeLinkControls_0) ;
    uint alternateMask_0 = uint(round(_S145.w));
    float4 _S146 = float4(_S7->uvTransformRow0_0) ;
    float2 _S147 = _S146.xy;
    float _S148 = _S146.z;
    float2 _S149 = _S144.xy;
    float _S150 = _S144.z;
    float2 _S151 = float2(dot(_S147, _S1.texCoord_0) + _S148, dot(_S149, _S1.texCoord_0) + _S150);
    float2 _S152 = float2(dot(_S147, _S1.texCoord1_0) + _S148, dot(_S149, _S1.texCoord1_0) + _S150);
    float4 _S153 = float4(_S7->alternateUvTransformRow0_0) ;
    float4 _S154 = float4(_S7->alternateUvTransformRow1_0) ;
    float2 _S155 = _S153.xy;
    float _S156 = _S153.z;
    float2 _S157 = _S154.xy;
    float _S158 = _S154.z;
    float2 _S159 = float2(dot(_S155, _S1.texCoord_0) + _S156, dot(_S157, _S1.texCoord_0) + _S158);
    float2 _S160 = float2(dot(_S155, _S1.texCoord1_0) + _S156, dot(_S157, _S1.texCoord1_0) + _S158);
    uint lightCount_0;
    float roughness_0;
    float metallic_0;
    float2 _S161;
    float2 _S162;
    bool hasSceneLighting_0;
    float4 _S163;
    uint3 _S164;
    if((textureMask_0 & 2U) != 0U)
    {
        bool useSecondary_0 = (secondaryMask_0 & 2U) != 0U;
        if((alternateMask_0 & 2U) != 0U)
        {
            if(useSecondary_0)
            {
                _S161 = _S160;
            }
            else
            {
                _S161 = _S159;
            }
        }
        else
        {
            if(useSecondary_0)
            {
                _S161 = _S152;
            }
            else
            {
                _S161 = _S151;
            }
        }
        thread float4 diffuseSample_0;
        bool _S165 = (udimMask_0 & 2U) != 0U;
        uint maskU_0 = uint(round(_S145.y));
        uint maskV_0 = uint(round(_S145.z));
        bool _S166 = (maskU_0 & 2U) != 0U;
        if(_S166)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S167 = (maskV_0 & 2U) != 0U;
        if(_S167)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S168 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S165)
            {
                for(;;)
                {
                    if(_S168 == 0U)
                    {
                        _S162 = _S161;
                        break;
                    }
                    texture2d<float, access::sample> _S169 = (&kernelContext_0)->baseColorTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S169).get_width(0)),(*((&height_0)) = (_S169).get_height(0));
                    float2 size_0 = float2(float(width_0), float(height_0));
                    float2 _S170 = float2(0.5f) ;
                    float2 remapped_0 = clamp((_S161 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S170 / size_0, (size_0 - _S170) / size_0);
                    if((_S168 & 1U) != 0U)
                    {
                        roughness_0 = remapped_0.x;
                    }
                    else
                    {
                        roughness_0 = _S161.x;
                    }
                    if((_S168 & 2U) != 0U)
                    {
                        metallic_0 = remapped_0.y;
                    }
                    else
                    {
                        metallic_0 = _S161.y;
                    }
                    _S162 = float2(roughness_0, metallic_0);
                    break;
                }
                _S163 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S162)));
                break;
            }
            texture2d<float, access::sample> _S171 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S171).get_width(0)),(*((&atlasHeight_0)) = (_S171).get_height(0));
            int3 _S172 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S172)).xy), uint(((_S172)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S173 = atlasWidth_0;
                uint _S174 = 2U % atlasWidth_0;
                int _S175 = int(_S174);
                uint _S176 = 2U / _S173;
                int3 _S177 = int3(_S175, int(_S176), int(0));
                int _S178 = int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S177)).xy), uint(((_S177)).z))).x * 255.0f)));
                uint _S179 = atlasWidth_0;
                uint _S180 = 3U % atlasWidth_0;
                int _S181 = int(_S180);
                uint _S182 = 3U / _S179;
                int3 _S183 = int3(_S181, int(_S182), int(0));
                int2 minimumTile_0 = int2(_S178, int(uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S183)).xy), uint(((_S183)).z))).x * 255.0f))));
                uint _S184 = atlasWidth_0;
                uint _S185 = 4U % atlasWidth_0;
                int _S186 = int(_S185);
                uint _S187 = 4U / _S184;
                int3 _S188 = int3(_S186, int(_S187), int(0));
                uint _S189 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S188)).xy), uint(((_S188)).z))).x * 255.0f));
                uint _S190 = atlasWidth_0;
                uint _S191 = 5U % atlasWidth_0;
                int _S192 = int(_S191);
                uint _S193 = 5U / _S190;
                int3 _S194 = int3(_S192, int(_S193), int(0));
                uint _S195 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S194)).xy), uint(((_S194)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S189, _S195);
                int2 tile_0 = int2(floor(_S161)) - minimumTile_0;
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
                    int3 _S196 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S196)).xy), uint(((_S196)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S189 + uint(tile_0.x));
                uint _S197 = atlasWidth_0;
                uint _S198 = offset_0 % atlasWidth_0;
                int _S199 = int(_S198);
                uint _S200 = offset_0 / _S197;
                int3 _S201 = int3(_S199, int(_S200), int(0));
                uint _S202 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S201)).xy), uint(((_S201)).z))).x * 255.0f));
                uint _S203 = atlasWidth_0;
                uint _S204 = offset_0 + 1U;
                uint _S205 = _S204 % atlasWidth_0;
                int _S206 = int(_S205);
                uint _S207 = _S204 / _S203;
                int3 _S208 = int3(_S206, int(_S207), int(0));
                uint slot_0 = _S202 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S208)).xy), uint(((_S208)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S209 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S209)).xy), uint(((_S209)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S210 = atlasWidth_0;
                uint _S211 = 6U % atlasWidth_0;
                int _S212 = int(_S211);
                uint _S213 = 6U / _S210;
                int3 _S214 = int3(_S212, int(_S213), int(0));
                uint _S215 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S214)).xy), uint(((_S214)).z))).x * 255.0f));
                uint _S216 = atlasWidth_0;
                uint _S217 = 7U % atlasWidth_0;
                int _S218 = int(_S217);
                uint _S219 = 7U / _S216;
                int3 _S220 = int3(_S218, int(_S219), int(0));
                uint columns_0 = _S215 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S220)).xy), uint(((_S220)).z))).x * 255.0f)) << 8U);
                uint _S221 = atlasWidth_0;
                uint _S222 = 8U % atlasWidth_0;
                int _S223 = int(_S222);
                uint _S224 = 8U / _S221;
                int3 _S225 = int3(_S223, int(_S224), int(0));
                uint _S226 = uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S225)).xy), uint(((_S225)).z))).x * 255.0f));
                uint _S227 = atlasWidth_0;
                uint _S228 = 9U % atlasWidth_0;
                int _S229 = int(_S228);
                uint _S230 = 9U / _S227;
                int3 _S231 = int3(_S229, int(_S230), int(0));
                uint rows_0 = ((_S226 | (uint(round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S231)).xy), uint(((_S231)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S189 * _S195 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S232 = atlasWidth_0 / columns_0;
                float _S233 = float(_S232);
                uint _S234 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S233, float(_S234));
                uint _S235 = slot_1 % columns_0;
                float _S236 = float(_S235);
                uint _S237 = slot_1 / columns_0;
                _S163 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(_S236, float(_S237)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S161) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S238 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(_S161)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S238);
            }
            if(hasSceneLighting_0)
            {
                int3 _S239 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S163 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S239)).xy), uint(((_S239)).z)));
                break;
            }
            uint _S240 = atlasWidth_0 / uint(_S238.x);
            float _S241 = float(_S240);
            uint _S242 = (atlasHeight_0 - 1U) / uint(_S238.y);
            float2 cellSize_1 = float2(_S241, float(_S242));
            _S163 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S163;
        float4 _S243 = float4(_S7->compositeControls_0) ;
        float _S244 = _S243.x;
        if(_S244 == 0.0f)
        {
            hasSceneLighting_0 = (_S243.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S245 = (&kernelContext_0)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S245).get_width(0)),(*((&sourceHeight_0)) = (_S245).get_height(0));
            texture2d<float, access::sample> _S246 = (&kernelContext_0)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S246).get_width(0)),(*((&valueHeight_0)) = (_S246).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float4 _S247 = diffuseSample_0;
                for(;;)
                {
                    float _S248 = _S247.w;
                    if(_S248 > 1.0f)
                    {
                        diffuseColor_0 = _S247.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S249 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_1;
                    thread uint height_1;
                    (*((&width_1)) = (_S249).get_width(0)),(*((&height_1)) = (_S249).get_height(0));
                    uint3 _S250 = uint3(round(_S247.xyz * float3(255.0f) ));
                    uint index_1 = ((_S250.x) | ((_S250.y) << 8U)) | ((_S250.z) << 16U);
                    if(_S248 == 0.0f)
                    {
                        _S164 = _S250;
                    }
                    else
                    {
                        _S164 = uint3(index_1, index_1, index_1);
                    }
                    uint3 address_0 = _S164 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S251 = address_0.x;
                    uint _S252 = _S251 % width_1;
                    int _S253 = int(_S252);
                    uint _S254 = _S251 / width_1;
                    int3 _S255 = int3(_S253, int(_S254), int(0));
                    float _S256 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S255)).xy), uint(((_S255)).z))).x;
                    uint _S257 = address_0.y;
                    uint _S258 = _S257 % width_1;
                    int _S259 = int(_S258);
                    uint _S260 = _S257 / width_1;
                    int3 _S261 = int3(_S259, int(_S260), int(0));
                    float _S262 = (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S261)).xy), uint(((_S261)).z))).x;
                    uint _S263 = address_0.z;
                    uint _S264 = _S263 % width_1;
                    int _S265 = int(_S264);
                    uint _S266 = _S263 / width_1;
                    int3 _S267 = int3(_S265, int(_S266), int(0));
                    diffuseColor_0 = float3(_S256, _S262, (((&kernelContext_0)->diffuseValuesTexture_0).read(vec<uint,2>(((_S267)).xy), uint(((_S267)).z))).x);
                    break;
                }
                diffuseSample_0.xyz = diffuseColor_0;
            }
            else
            {
                if(_S166)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S167)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S268 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S268 == 0U)
                    {
                        _S162 = _S161;
                        break;
                    }
                    texture2d<float, access::sample> _S269 = (&kernelContext_0)->diffuseValuesTexture_0;
                    thread uint width_2;
                    thread uint height_2;
                    (*((&width_2)) = (_S269).get_width(0)),(*((&height_2)) = (_S269).get_height(0));
                    float2 size_1 = float2(float(width_2), float(height_2));
                    float2 _S270 = float2(0.5f) ;
                    float2 remapped_1 = clamp((_S161 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S270 / size_1, (size_1 - _S270) / size_1);
                    if((_S268 & 1U) != 0U)
                    {
                        roughness_0 = remapped_1.x;
                    }
                    else
                    {
                        roughness_0 = _S161.x;
                    }
                    if((_S268 & 2U) != 0U)
                    {
                        metallic_0 = remapped_1.y;
                    }
                    else
                    {
                        metallic_0 = _S161.y;
                    }
                    _S162 = float2(roughness_0, metallic_0);
                    break;
                }
                diffuseSample_0.z = (((&kernelContext_0)->diffuseValuesTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S162))).x;
            }
        }
        float4 _S271 = diffuseSample_0;
        for(;;)
        {
            if(_S244 != 2.0f)
            {
                _S163 = _S271;
                break;
            }
            bool _S272 = (_S243.w) >= 0.5f;
            if((maskU_0 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_0 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S273 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S272)
                {
                    for(;;)
                    {
                        if(_S273 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S274 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_3;
                        thread uint height_3;
                        (*((&width_3)) = (_S274).get_width(0)),(*((&height_3)) = (_S274).get_height(0));
                        float2 size_2 = float2(float(width_3), float(height_3));
                        float2 _S275 = float2(0.5f) ;
                        float2 remapped_2 = clamp((_S161 * (size_2 - float2(2.0f) ) + float2(1.0f) ) / size_2, _S275 / size_2, (size_2 - _S275) / size_2);
                        if((_S273 & 1U) != 0U)
                        {
                            roughness_0 = remapped_2.x;
                        }
                        else
                        {
                            roughness_0 = _S161.x;
                        }
                        if((_S273 & 2U) != 0U)
                        {
                            metallic_0 = remapped_2.y;
                        }
                        else
                        {
                            metallic_0 = _S161.y;
                        }
                        _S161 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S163 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S161)));
                    break;
                }
                texture2d<float, access::sample> _S276 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S276).get_width(0)),(*((&atlasHeight_1)) = (_S276).get_height(0));
                int3 _S277 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S277)).xy), uint(((_S277)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S278 = atlasWidth_1;
                    uint _S279 = 2U % atlasWidth_1;
                    int _S280 = int(_S279);
                    uint _S281 = 2U / _S278;
                    int3 _S282 = int3(_S280, int(_S281), int(0));
                    int _S283 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S282)).xy), uint(((_S282)).z))).x * 255.0f)));
                    uint _S284 = atlasWidth_1;
                    uint _S285 = 3U % atlasWidth_1;
                    int _S286 = int(_S285);
                    uint _S287 = 3U / _S284;
                    int3 _S288 = int3(_S286, int(_S287), int(0));
                    int2 minimumTile_1 = int2(_S283, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S288)).xy), uint(((_S288)).z))).x * 255.0f))));
                    uint _S289 = atlasWidth_1;
                    uint _S290 = 4U % atlasWidth_1;
                    int _S291 = int(_S290);
                    uint _S292 = 4U / _S289;
                    int3 _S293 = int3(_S291, int(_S292), int(0));
                    uint _S294 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S293)).xy), uint(((_S293)).z))).x * 255.0f));
                    uint _S295 = atlasWidth_1;
                    uint _S296 = 5U % atlasWidth_1;
                    int _S297 = int(_S296);
                    uint _S298 = 5U / _S295;
                    int3 _S299 = int3(_S297, int(_S298), int(0));
                    uint _S300 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S299)).xy), uint(((_S299)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S294, _S300);
                    int2 tile_2 = int2(floor(_S161)) - minimumTile_1;
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
                        int3 _S301 = int3(int(1), int(0), int(0));
                        _S163 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S301)).xy), uint(((_S301)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S294 + uint(tile_2.x));
                    uint _S302 = atlasWidth_1;
                    uint _S303 = offset_1 % atlasWidth_1;
                    int _S304 = int(_S303);
                    uint _S305 = offset_1 / _S302;
                    int3 _S306 = int3(_S304, int(_S305), int(0));
                    uint _S307 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S306)).xy), uint(((_S306)).z))).x * 255.0f));
                    uint _S308 = atlasWidth_1;
                    uint _S309 = offset_1 + 1U;
                    uint _S310 = _S309 % atlasWidth_1;
                    int _S311 = int(_S310);
                    uint _S312 = _S309 / _S308;
                    int3 _S313 = int3(_S311, int(_S312), int(0));
                    uint slot_2 = _S307 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S313)).xy), uint(((_S313)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S314 = int3(int(1), int(0), int(0));
                        _S163 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S314)).xy), uint(((_S314)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S315 = atlasWidth_1;
                    uint _S316 = 6U % atlasWidth_1;
                    int _S317 = int(_S316);
                    uint _S318 = 6U / _S315;
                    int3 _S319 = int3(_S317, int(_S318), int(0));
                    uint _S320 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S319)).xy), uint(((_S319)).z))).x * 255.0f));
                    uint _S321 = atlasWidth_1;
                    uint _S322 = 7U % atlasWidth_1;
                    int _S323 = int(_S322);
                    uint _S324 = 7U / _S321;
                    int3 _S325 = int3(_S323, int(_S324), int(0));
                    uint columns_1 = _S320 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S325)).xy), uint(((_S325)).z))).x * 255.0f)) << 8U);
                    uint _S326 = atlasWidth_1;
                    uint _S327 = 8U % atlasWidth_1;
                    int _S328 = int(_S327);
                    uint _S329 = 8U / _S326;
                    int3 _S330 = int3(_S328, int(_S329), int(0));
                    uint _S331 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S330)).xy), uint(((_S330)).z))).x * 255.0f));
                    uint _S332 = atlasWidth_1;
                    uint _S333 = 9U % atlasWidth_1;
                    int _S334 = int(_S333);
                    uint _S335 = 9U / _S332;
                    int3 _S336 = int3(_S334, int(_S335), int(0));
                    uint rows_1 = ((_S331 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S336)).xy), uint(((_S336)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S294 * _S300 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S337 = atlasWidth_1 / columns_1;
                    float _S338 = float(_S337);
                    uint _S339 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S338, float(_S339));
                    uint _S340 = slot_3 % columns_1;
                    float _S341 = float(_S340);
                    uint _S342 = slot_3 / columns_1;
                    _S163 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S341, float(_S342)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S161) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S343 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(_S161)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S343);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S344 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S163 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S344)).xy), uint(((_S344)).z)));
                    break;
                }
                uint _S345 = atlasWidth_1 / uint(_S343.x);
                float _S346 = float(_S345);
                uint _S347 = (atlasHeight_1 - 1U) / uint(_S343.y);
                float2 cellSize_3 = float2(_S346, float(_S347));
                _S163 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S243.y));
            if(operation_0 == 1U)
            {
                _S163 = _S271 * _S163;
                break;
            }
            if(operation_0 == 2U)
            {
                _S163 = _S271 + _S163;
                break;
            }
            if(operation_0 == 3U)
            {
                _S163 = _S271 - _S163;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S243.z;
                _S163 = _S271 * float4((1.0f - factor_0))  + _S163 * float4(factor_0) ;
                break;
            }
            _S163 = _S271;
            break;
        }
        diffuseColor_0 = _S163.xyz;
    }
    float4 _S348;
    if((textureMask_0 & 8U) != 0U)
    {
        bool useSecondary_1 = (secondaryMask_0 & 8U) != 0U;
        if((alternateMask_0 & 8U) != 0U)
        {
            if(useSecondary_1)
            {
                _S161 = _S160;
            }
            else
            {
                _S161 = _S159;
            }
        }
        else
        {
            if(useSecondary_1)
            {
                _S161 = _S152;
            }
            else
            {
                _S161 = _S151;
            }
        }
        bool _S349 = (udimMask_0 & 8U) != 0U;
        uint maskU_1 = uint(round(_S145.y));
        uint maskV_1 = uint(round(_S145.z));
        if((maskU_1 & 8U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_1 & 8U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S350 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S349)
            {
                for(;;)
                {
                    if(_S350 == 0U)
                    {
                        _S162 = _S161;
                        break;
                    }
                    texture2d<float, access::sample> _S351 = (&kernelContext_0)->roughnessMetallicTexture_0;
                    thread uint width_4;
                    thread uint height_4;
                    (*((&width_4)) = (_S351).get_width(0)),(*((&height_4)) = (_S351).get_height(0));
                    float2 size_3 = float2(float(width_4), float(height_4));
                    float2 _S352 = float2(0.5f) ;
                    float2 remapped_3 = clamp((_S161 * (size_3 - float2(2.0f) ) + float2(1.0f) ) / size_3, _S352 / size_3, (size_3 - _S352) / size_3);
                    if((_S350 & 1U) != 0U)
                    {
                        roughness_0 = remapped_3.x;
                    }
                    else
                    {
                        roughness_0 = _S161.x;
                    }
                    if((_S350 & 2U) != 0U)
                    {
                        metallic_0 = remapped_3.y;
                    }
                    else
                    {
                        metallic_0 = _S161.y;
                    }
                    _S162 = float2(roughness_0, metallic_0);
                    break;
                }
                _S163 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S162)));
                break;
            }
            texture2d<float, access::sample> _S353 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S353).get_width(0)),(*((&atlasHeight_2)) = (_S353).get_height(0));
            int3 _S354 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S354)).xy), uint(((_S354)).z))) * float4(255.0f) );
            if((metadata_2.x) == 255.0f)
            {
                uint _S355 = atlasWidth_2;
                uint _S356 = 2U % atlasWidth_2;
                int _S357 = int(_S356);
                uint _S358 = 2U / _S355;
                int3 _S359 = int3(_S357, int(_S358), int(0));
                int _S360 = int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S359)).xy), uint(((_S359)).z))).x * 255.0f)));
                uint _S361 = atlasWidth_2;
                uint _S362 = 3U % atlasWidth_2;
                int _S363 = int(_S362);
                uint _S364 = 3U / _S361;
                int3 _S365 = int3(_S363, int(_S364), int(0));
                int2 minimumTile_2 = int2(_S360, int(uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S365)).xy), uint(((_S365)).z))).x * 255.0f))));
                uint _S366 = atlasWidth_2;
                uint _S367 = 4U % atlasWidth_2;
                int _S368 = int(_S367);
                uint _S369 = 4U / _S366;
                int3 _S370 = int3(_S368, int(_S369), int(0));
                uint _S371 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S370)).xy), uint(((_S370)).z))).x * 255.0f));
                uint _S372 = atlasWidth_2;
                uint _S373 = 5U % atlasWidth_2;
                int _S374 = int(_S373);
                uint _S375 = 5U / _S372;
                int3 _S376 = int3(_S374, int(_S375), int(0));
                uint _S377 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S376)).xy), uint(((_S376)).z))).x * 255.0f));
                uint2 grid_2 = uint2(_S371, _S377);
                int2 tile_4 = int2(floor(_S161)) - minimumTile_2;
                if(any(tile_4 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_4 >= int2(grid_2));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S378 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S378)).xy), uint(((_S378)).z)));
                    break;
                }
                uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S371 + uint(tile_4.x));
                uint _S379 = atlasWidth_2;
                uint _S380 = offset_2 % atlasWidth_2;
                int _S381 = int(_S380);
                uint _S382 = offset_2 / _S379;
                int3 _S383 = int3(_S381, int(_S382), int(0));
                uint _S384 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S383)).xy), uint(((_S383)).z))).x * 255.0f));
                uint _S385 = atlasWidth_2;
                uint _S386 = offset_2 + 1U;
                uint _S387 = _S386 % atlasWidth_2;
                int _S388 = int(_S387);
                uint _S389 = _S386 / _S385;
                int3 _S390 = int3(_S388, int(_S389), int(0));
                uint slot_4 = _S384 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S390)).xy), uint(((_S390)).z))).x * 255.0f)) << 8U);
                if(slot_4 == 0U)
                {
                    int3 _S391 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S391)).xy), uint(((_S391)).z)));
                    break;
                }
                uint slot_5 = slot_4 - 1U;
                uint _S392 = atlasWidth_2;
                uint _S393 = 6U % atlasWidth_2;
                int _S394 = int(_S393);
                uint _S395 = 6U / _S392;
                int3 _S396 = int3(_S394, int(_S395), int(0));
                uint _S397 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S396)).xy), uint(((_S396)).z))).x * 255.0f));
                uint _S398 = atlasWidth_2;
                uint _S399 = 7U % atlasWidth_2;
                int _S400 = int(_S399);
                uint _S401 = 7U / _S398;
                int3 _S402 = int3(_S400, int(_S401), int(0));
                uint columns_2 = _S397 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S402)).xy), uint(((_S402)).z))).x * 255.0f)) << 8U);
                uint _S403 = atlasWidth_2;
                uint _S404 = 8U % atlasWidth_2;
                int _S405 = int(_S404);
                uint _S406 = 8U / _S403;
                int3 _S407 = int3(_S405, int(_S406), int(0));
                uint _S408 = uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S407)).xy), uint(((_S407)).z))).x * 255.0f));
                uint _S409 = atlasWidth_2;
                uint _S410 = 9U % atlasWidth_2;
                int _S411 = int(_S410);
                uint _S412 = 9U / _S409;
                int3 _S413 = int3(_S411, int(_S412), int(0));
                uint rows_2 = ((_S408 | (uint(round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S413)).xy), uint(((_S413)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
                uint metadataRows_2 = (10U + 2U * _S371 * _S377 + atlasWidth_2 - 1U) / atlasWidth_2;
                uint _S414 = atlasWidth_2 / columns_2;
                float _S415 = float(_S414);
                uint _S416 = (atlasHeight_2 - metadataRows_2) / rows_2;
                float2 cellSize_4 = float2(_S415, float(_S416));
                uint _S417 = slot_5 % columns_2;
                float _S418 = float(_S417);
                uint _S419 = slot_5 / columns_2;
                _S163 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(_S418, float(_S419)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(_S161) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
                break;
            }
            int2 _S420 = int2(metadata_2.zw);
            int2 tile_5 = int2(floor(_S161)) - int2(metadata_2.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S420);
            }
            if(hasSceneLighting_0)
            {
                int3 _S421 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S163 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S421)).xy), uint(((_S421)).z)));
                break;
            }
            uint _S422 = atlasWidth_2 / uint(_S420.x);
            float _S423 = float(_S422);
            uint _S424 = (atlasHeight_2 - 1U) / uint(_S420.y);
            float2 cellSize_5 = float2(_S423, float(_S424));
            _S163 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        for(;;)
        {
            float4 _S425 = float4(_S7->compositeControls_0) ;
            if((_S425.x) != 8.0f)
            {
                break;
            }
            bool _S426 = (_S425.w) >= 0.5f;
            if((maskU_1 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_1 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S427 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S426)
                {
                    for(;;)
                    {
                        if(_S427 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S428 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_5;
                        thread uint height_5;
                        (*((&width_5)) = (_S428).get_width(0)),(*((&height_5)) = (_S428).get_height(0));
                        float2 size_4 = float2(float(width_5), float(height_5));
                        float2 _S429 = float2(0.5f) ;
                        float2 remapped_4 = clamp((_S161 * (size_4 - float2(2.0f) ) + float2(1.0f) ) / size_4, _S429 / size_4, (size_4 - _S429) / size_4);
                        if((_S427 & 1U) != 0U)
                        {
                            roughness_0 = remapped_4.x;
                        }
                        else
                        {
                            roughness_0 = _S161.x;
                        }
                        if((_S427 & 2U) != 0U)
                        {
                            metallic_0 = remapped_4.y;
                        }
                        else
                        {
                            metallic_0 = _S161.y;
                        }
                        _S161 = float2(roughness_0, metallic_0);
                        break;
                    }
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S161)));
                    break;
                }
                texture2d<float, access::sample> _S430 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_3;
                thread uint atlasHeight_3;
                (*((&atlasWidth_3)) = (_S430).get_width(0)),(*((&atlasHeight_3)) = (_S430).get_height(0));
                int3 _S431 = int3(int(0), int(0), int(0));
                float4 metadata_3 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S431)).xy), uint(((_S431)).z))) * float4(255.0f) );
                if((metadata_3.x) == 255.0f)
                {
                    uint _S432 = atlasWidth_3;
                    uint _S433 = 2U % atlasWidth_3;
                    int _S434 = int(_S433);
                    uint _S435 = 2U / _S432;
                    int3 _S436 = int3(_S434, int(_S435), int(0));
                    int _S437 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S436)).xy), uint(((_S436)).z))).x * 255.0f)));
                    uint _S438 = atlasWidth_3;
                    uint _S439 = 3U % atlasWidth_3;
                    int _S440 = int(_S439);
                    uint _S441 = 3U / _S438;
                    int3 _S442 = int3(_S440, int(_S441), int(0));
                    int2 minimumTile_3 = int2(_S437, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S442)).xy), uint(((_S442)).z))).x * 255.0f))));
                    uint _S443 = atlasWidth_3;
                    uint _S444 = 4U % atlasWidth_3;
                    int _S445 = int(_S444);
                    uint _S446 = 4U / _S443;
                    int3 _S447 = int3(_S445, int(_S446), int(0));
                    uint _S448 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S447)).xy), uint(((_S447)).z))).x * 255.0f));
                    uint _S449 = atlasWidth_3;
                    uint _S450 = 5U % atlasWidth_3;
                    int _S451 = int(_S450);
                    uint _S452 = 5U / _S449;
                    int3 _S453 = int3(_S451, int(_S452), int(0));
                    uint _S454 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S453)).xy), uint(((_S453)).z))).x * 255.0f));
                    uint2 grid_3 = uint2(_S448, _S454);
                    int2 tile_6 = int2(floor(_S161)) - minimumTile_3;
                    if(any(tile_6 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_6 >= int2(grid_3));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S455 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S455)).xy), uint(((_S455)).z)));
                        break;
                    }
                    uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S448 + uint(tile_6.x));
                    uint _S456 = atlasWidth_3;
                    uint _S457 = offset_3 % atlasWidth_3;
                    int _S458 = int(_S457);
                    uint _S459 = offset_3 / _S456;
                    int3 _S460 = int3(_S458, int(_S459), int(0));
                    uint _S461 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S460)).xy), uint(((_S460)).z))).x * 255.0f));
                    uint _S462 = atlasWidth_3;
                    uint _S463 = offset_3 + 1U;
                    uint _S464 = _S463 % atlasWidth_3;
                    int _S465 = int(_S464);
                    uint _S466 = _S463 / _S462;
                    int3 _S467 = int3(_S465, int(_S466), int(0));
                    uint slot_6 = _S461 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S467)).xy), uint(((_S467)).z))).x * 255.0f)) << 8U);
                    if(slot_6 == 0U)
                    {
                        int3 _S468 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S468)).xy), uint(((_S468)).z)));
                        break;
                    }
                    uint slot_7 = slot_6 - 1U;
                    uint _S469 = atlasWidth_3;
                    uint _S470 = 6U % atlasWidth_3;
                    int _S471 = int(_S470);
                    uint _S472 = 6U / _S469;
                    int3 _S473 = int3(_S471, int(_S472), int(0));
                    uint _S474 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S473)).xy), uint(((_S473)).z))).x * 255.0f));
                    uint _S475 = atlasWidth_3;
                    uint _S476 = 7U % atlasWidth_3;
                    int _S477 = int(_S476);
                    uint _S478 = 7U / _S475;
                    int3 _S479 = int3(_S477, int(_S478), int(0));
                    uint columns_3 = _S474 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S479)).xy), uint(((_S479)).z))).x * 255.0f)) << 8U);
                    uint _S480 = atlasWidth_3;
                    uint _S481 = 8U % atlasWidth_3;
                    int _S482 = int(_S481);
                    uint _S483 = 8U / _S480;
                    int3 _S484 = int3(_S482, int(_S483), int(0));
                    uint _S485 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S484)).xy), uint(((_S484)).z))).x * 255.0f));
                    uint _S486 = atlasWidth_3;
                    uint _S487 = 9U % atlasWidth_3;
                    int _S488 = int(_S487);
                    uint _S489 = 9U / _S486;
                    int3 _S490 = int3(_S488, int(_S489), int(0));
                    uint rows_3 = ((_S485 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S490)).xy), uint(((_S490)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                    uint metadataRows_3 = (10U + 2U * _S448 * _S454 + atlasWidth_3 - 1U) / atlasWidth_3;
                    uint _S491 = atlasWidth_3 / columns_3;
                    float _S492 = float(_S491);
                    uint _S493 = (atlasHeight_3 - metadataRows_3) / rows_3;
                    float2 cellSize_6 = float2(_S492, float(_S493));
                    uint _S494 = slot_7 % columns_3;
                    float _S495 = float(_S494);
                    uint _S496 = slot_7 / columns_3;
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S495, float(_S496)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(_S161) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                    break;
                }
                int2 _S497 = int2(metadata_3.zw);
                int2 tile_7 = int2(floor(_S161)) - int2(metadata_3.xy);
                if(any(tile_7 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_7 >= _S497);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S498 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                    _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S498)).xy), uint(((_S498)).z)));
                    break;
                }
                uint _S499 = atlasWidth_3 / uint(_S497.x);
                float _S500 = float(_S499);
                uint _S501 = (atlasHeight_3 - 1U) / uint(_S497.y);
                float2 cellSize_7 = float2(_S500, float(_S501));
                _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            uint operation_1 = uint(round(_S425.y));
            if(operation_1 == 1U)
            {
                _S163 = _S163 * _S348;
                break;
            }
            if(operation_1 == 2U)
            {
                _S163 = _S163 + _S348;
                break;
            }
            if(operation_1 == 3U)
            {
                _S163 = _S163 - _S348;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S425.z;
                _S163 = _S163 * float4((1.0f - factor_1))  + _S348 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S163.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S137;
    }
    float occlusion_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool useSecondary_2 = (secondaryMask_0 & 32U) != 0U;
        if((alternateMask_0 & 32U) != 0U)
        {
            if(useSecondary_2)
            {
                _S161 = _S160;
            }
            else
            {
                _S161 = _S159;
            }
        }
        else
        {
            if(useSecondary_2)
            {
                _S161 = _S152;
            }
            else
            {
                _S161 = _S151;
            }
        }
        bool _S502 = (udimMask_0 & 32U) != 0U;
        uint maskU_2 = uint(round(_S145.y));
        uint maskV_2 = uint(round(_S145.z));
        if((maskU_2 & 32U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_2 & 32U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S503 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S502)
            {
                for(;;)
                {
                    if(_S503 == 0U)
                    {
                        _S162 = _S161;
                        break;
                    }
                    texture2d<float, access::sample> _S504 = (&kernelContext_0)->metallicTexture_0;
                    thread uint width_6;
                    thread uint height_6;
                    (*((&width_6)) = (_S504).get_width(0)),(*((&height_6)) = (_S504).get_height(0));
                    float2 size_5 = float2(float(width_6), float(height_6));
                    float2 _S505 = float2(0.5f) ;
                    float2 remapped_5 = clamp((_S161 * (size_5 - float2(2.0f) ) + float2(1.0f) ) / size_5, _S505 / size_5, (size_5 - _S505) / size_5);
                    if((_S503 & 1U) != 0U)
                    {
                        metallic_0 = remapped_5.x;
                    }
                    else
                    {
                        metallic_0 = _S161.x;
                    }
                    if((_S503 & 2U) != 0U)
                    {
                        occlusion_0 = remapped_5.y;
                    }
                    else
                    {
                        occlusion_0 = _S161.y;
                    }
                    _S162 = float2(metallic_0, occlusion_0);
                    break;
                }
                _S163 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S162)));
                break;
            }
            texture2d<float, access::sample> _S506 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S506).get_width(0)),(*((&atlasHeight_4)) = (_S506).get_height(0));
            int3 _S507 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S507)).xy), uint(((_S507)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S508 = atlasWidth_4;
                uint _S509 = 2U % atlasWidth_4;
                int _S510 = int(_S509);
                uint _S511 = 2U / _S508;
                int3 _S512 = int3(_S510, int(_S511), int(0));
                int _S513 = int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S512)).xy), uint(((_S512)).z))).x * 255.0f)));
                uint _S514 = atlasWidth_4;
                uint _S515 = 3U % atlasWidth_4;
                int _S516 = int(_S515);
                uint _S517 = 3U / _S514;
                int3 _S518 = int3(_S516, int(_S517), int(0));
                int2 minimumTile_4 = int2(_S513, int(uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S518)).xy), uint(((_S518)).z))).x * 255.0f))));
                uint _S519 = atlasWidth_4;
                uint _S520 = 4U % atlasWidth_4;
                int _S521 = int(_S520);
                uint _S522 = 4U / _S519;
                int3 _S523 = int3(_S521, int(_S522), int(0));
                uint _S524 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S523)).xy), uint(((_S523)).z))).x * 255.0f));
                uint _S525 = atlasWidth_4;
                uint _S526 = 5U % atlasWidth_4;
                int _S527 = int(_S526);
                uint _S528 = 5U / _S525;
                int3 _S529 = int3(_S527, int(_S528), int(0));
                uint _S530 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S529)).xy), uint(((_S529)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S524, _S530);
                int2 tile_8 = int2(floor(_S161)) - minimumTile_4;
                if(any(tile_8 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_8 >= int2(grid_4));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S531 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S531)).xy), uint(((_S531)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S524 + uint(tile_8.x));
                uint _S532 = atlasWidth_4;
                uint _S533 = offset_4 % atlasWidth_4;
                int _S534 = int(_S533);
                uint _S535 = offset_4 / _S532;
                int3 _S536 = int3(_S534, int(_S535), int(0));
                uint _S537 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S536)).xy), uint(((_S536)).z))).x * 255.0f));
                uint _S538 = atlasWidth_4;
                uint _S539 = offset_4 + 1U;
                uint _S540 = _S539 % atlasWidth_4;
                int _S541 = int(_S540);
                uint _S542 = _S539 / _S538;
                int3 _S543 = int3(_S541, int(_S542), int(0));
                uint slot_8 = _S537 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S543)).xy), uint(((_S543)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S544 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S544)).xy), uint(((_S544)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S545 = atlasWidth_4;
                uint _S546 = 6U % atlasWidth_4;
                int _S547 = int(_S546);
                uint _S548 = 6U / _S545;
                int3 _S549 = int3(_S547, int(_S548), int(0));
                uint _S550 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S549)).xy), uint(((_S549)).z))).x * 255.0f));
                uint _S551 = atlasWidth_4;
                uint _S552 = 7U % atlasWidth_4;
                int _S553 = int(_S552);
                uint _S554 = 7U / _S551;
                int3 _S555 = int3(_S553, int(_S554), int(0));
                uint columns_4 = _S550 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S555)).xy), uint(((_S555)).z))).x * 255.0f)) << 8U);
                uint _S556 = atlasWidth_4;
                uint _S557 = 8U % atlasWidth_4;
                int _S558 = int(_S557);
                uint _S559 = 8U / _S556;
                int3 _S560 = int3(_S558, int(_S559), int(0));
                uint _S561 = uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S560)).xy), uint(((_S560)).z))).x * 255.0f));
                uint _S562 = atlasWidth_4;
                uint _S563 = 9U % atlasWidth_4;
                int _S564 = int(_S563);
                uint _S565 = 9U / _S562;
                int3 _S566 = int3(_S564, int(_S565), int(0));
                uint rows_4 = ((_S561 | (uint(round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S566)).xy), uint(((_S566)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S524 * _S530 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S567 = atlasWidth_4 / columns_4;
                float _S568 = float(_S567);
                uint _S569 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S568, float(_S569));
                uint _S570 = slot_9 % columns_4;
                float _S571 = float(_S570);
                uint _S572 = slot_9 / columns_4;
                _S163 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(_S571, float(_S572)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(_S161) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S573 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(_S161)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S573);
            }
            if(hasSceneLighting_0)
            {
                int3 _S574 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S163 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S574)).xy), uint(((_S574)).z)));
                break;
            }
            uint _S575 = atlasWidth_4 / uint(_S573.x);
            float _S576 = float(_S575);
            uint _S577 = (atlasHeight_4 - 1U) / uint(_S573.y);
            float2 cellSize_9 = float2(_S576, float(_S577));
            _S163 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S578 = float4(_S7->compositeControls_0) ;
            if((_S578.x) != 32.0f)
            {
                break;
            }
            bool _S579 = (_S578.w) >= 0.5f;
            if((maskU_2 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_2 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S580 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S579)
                {
                    for(;;)
                    {
                        if(_S580 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S581 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_7;
                        thread uint height_7;
                        (*((&width_7)) = (_S581).get_width(0)),(*((&height_7)) = (_S581).get_height(0));
                        float2 size_6 = float2(float(width_7), float(height_7));
                        float2 _S582 = float2(0.5f) ;
                        float2 remapped_6 = clamp((_S161 * (size_6 - float2(2.0f) ) + float2(1.0f) ) / size_6, _S582 / size_6, (size_6 - _S582) / size_6);
                        if((_S580 & 1U) != 0U)
                        {
                            metallic_0 = remapped_6.x;
                        }
                        else
                        {
                            metallic_0 = _S161.x;
                        }
                        if((_S580 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_6.y;
                        }
                        else
                        {
                            occlusion_0 = _S161.y;
                        }
                        _S161 = float2(metallic_0, occlusion_0);
                        break;
                    }
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S161)));
                    break;
                }
                texture2d<float, access::sample> _S583 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S583).get_width(0)),(*((&atlasHeight_5)) = (_S583).get_height(0));
                int3 _S584 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S584)).xy), uint(((_S584)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S585 = atlasWidth_5;
                    uint _S586 = 2U % atlasWidth_5;
                    int _S587 = int(_S586);
                    uint _S588 = 2U / _S585;
                    int3 _S589 = int3(_S587, int(_S588), int(0));
                    int _S590 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S589)).xy), uint(((_S589)).z))).x * 255.0f)));
                    uint _S591 = atlasWidth_5;
                    uint _S592 = 3U % atlasWidth_5;
                    int _S593 = int(_S592);
                    uint _S594 = 3U / _S591;
                    int3 _S595 = int3(_S593, int(_S594), int(0));
                    int2 minimumTile_5 = int2(_S590, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S595)).xy), uint(((_S595)).z))).x * 255.0f))));
                    uint _S596 = atlasWidth_5;
                    uint _S597 = 4U % atlasWidth_5;
                    int _S598 = int(_S597);
                    uint _S599 = 4U / _S596;
                    int3 _S600 = int3(_S598, int(_S599), int(0));
                    uint _S601 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S600)).xy), uint(((_S600)).z))).x * 255.0f));
                    uint _S602 = atlasWidth_5;
                    uint _S603 = 5U % atlasWidth_5;
                    int _S604 = int(_S603);
                    uint _S605 = 5U / _S602;
                    int3 _S606 = int3(_S604, int(_S605), int(0));
                    uint _S607 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S606)).xy), uint(((_S606)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S601, _S607);
                    int2 tile_10 = int2(floor(_S161)) - minimumTile_5;
                    if(any(tile_10 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_10 >= int2(grid_5));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S608 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S608)).xy), uint(((_S608)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S601 + uint(tile_10.x));
                    uint _S609 = atlasWidth_5;
                    uint _S610 = offset_5 % atlasWidth_5;
                    int _S611 = int(_S610);
                    uint _S612 = offset_5 / _S609;
                    int3 _S613 = int3(_S611, int(_S612), int(0));
                    uint _S614 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S613)).xy), uint(((_S613)).z))).x * 255.0f));
                    uint _S615 = atlasWidth_5;
                    uint _S616 = offset_5 + 1U;
                    uint _S617 = _S616 % atlasWidth_5;
                    int _S618 = int(_S617);
                    uint _S619 = _S616 / _S615;
                    int3 _S620 = int3(_S618, int(_S619), int(0));
                    uint slot_10 = _S614 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S620)).xy), uint(((_S620)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S621 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S621)).xy), uint(((_S621)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S622 = atlasWidth_5;
                    uint _S623 = 6U % atlasWidth_5;
                    int _S624 = int(_S623);
                    uint _S625 = 6U / _S622;
                    int3 _S626 = int3(_S624, int(_S625), int(0));
                    uint _S627 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S626)).xy), uint(((_S626)).z))).x * 255.0f));
                    uint _S628 = atlasWidth_5;
                    uint _S629 = 7U % atlasWidth_5;
                    int _S630 = int(_S629);
                    uint _S631 = 7U / _S628;
                    int3 _S632 = int3(_S630, int(_S631), int(0));
                    uint columns_5 = _S627 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S632)).xy), uint(((_S632)).z))).x * 255.0f)) << 8U);
                    uint _S633 = atlasWidth_5;
                    uint _S634 = 8U % atlasWidth_5;
                    int _S635 = int(_S634);
                    uint _S636 = 8U / _S633;
                    int3 _S637 = int3(_S635, int(_S636), int(0));
                    uint _S638 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S637)).xy), uint(((_S637)).z))).x * 255.0f));
                    uint _S639 = atlasWidth_5;
                    uint _S640 = 9U % atlasWidth_5;
                    int _S641 = int(_S640);
                    uint _S642 = 9U / _S639;
                    int3 _S643 = int3(_S641, int(_S642), int(0));
                    uint rows_5 = ((_S638 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S643)).xy), uint(((_S643)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S601 * _S607 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S644 = atlasWidth_5 / columns_5;
                    float _S645 = float(_S644);
                    uint _S646 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S645, float(_S646));
                    uint _S647 = slot_11 % columns_5;
                    float _S648 = float(_S647);
                    uint _S649 = slot_11 / columns_5;
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S648, float(_S649)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(_S161) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S650 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(_S161)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S650);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S651 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S651)).xy), uint(((_S651)).z)));
                    break;
                }
                uint _S652 = atlasWidth_5 / uint(_S650.x);
                float _S653 = float(_S652);
                uint _S654 = (atlasHeight_5 - 1U) / uint(_S650.y);
                float2 cellSize_11 = float2(_S653, float(_S654));
                _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_2 = uint(round(_S578.y));
            if(operation_2 == 1U)
            {
                _S163 = _S163 * _S348;
                break;
            }
            if(operation_2 == 2U)
            {
                _S163 = _S163 + _S348;
                break;
            }
            if(operation_2 == 3U)
            {
                _S163 = _S163 - _S348;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S578.z;
                _S163 = _S163 * float4((1.0f - factor_2))  + _S348 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S163.x);
    }
    else
    {
        metallic_0 = _S136;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 16U) != 0U)
    {
        bool useSecondary_3 = (secondaryMask_0 & 16U) != 0U;
        if((alternateMask_0 & 16U) != 0U)
        {
            if(useSecondary_3)
            {
                _S161 = _S160;
            }
            else
            {
                _S161 = _S159;
            }
        }
        else
        {
            if(useSecondary_3)
            {
                _S161 = _S152;
            }
            else
            {
                _S161 = _S151;
            }
        }
        thread float4 emissionSample_0;
        bool _S655 = (udimMask_0 & 16U) != 0U;
        uint maskU_3 = uint(round(_S145.y));
        uint maskV_3 = uint(round(_S145.z));
        bool _S656 = (maskU_3 & 16U) != 0U;
        if(_S656)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        bool _S657 = (maskV_3 & 16U) != 0U;
        if(_S657)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S658 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S655)
            {
                for(;;)
                {
                    if(_S658 == 0U)
                    {
                        _S162 = _S161;
                        break;
                    }
                    texture2d<float, access::sample> _S659 = (&kernelContext_0)->emissiveTexture_0;
                    thread uint width_8;
                    thread uint height_8;
                    (*((&width_8)) = (_S659).get_width(0)),(*((&height_8)) = (_S659).get_height(0));
                    float2 size_7 = float2(float(width_8), float(height_8));
                    float2 _S660 = float2(0.5f) ;
                    float2 remapped_7 = clamp((_S161 * (size_7 - float2(2.0f) ) + float2(1.0f) ) / size_7, _S660 / size_7, (size_7 - _S660) / size_7);
                    if((_S658 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_7.x;
                    }
                    else
                    {
                        occlusion_0 = _S161.x;
                    }
                    if((_S658 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_7.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S161.y;
                    }
                    _S162 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S163 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S162)));
                break;
            }
            texture2d<float, access::sample> _S661 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S661).get_width(0)),(*((&atlasHeight_6)) = (_S661).get_height(0));
            int3 _S662 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S662)).xy), uint(((_S662)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S663 = atlasWidth_6;
                uint _S664 = 2U % atlasWidth_6;
                int _S665 = int(_S664);
                uint _S666 = 2U / _S663;
                int3 _S667 = int3(_S665, int(_S666), int(0));
                int _S668 = int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S667)).xy), uint(((_S667)).z))).x * 255.0f)));
                uint _S669 = atlasWidth_6;
                uint _S670 = 3U % atlasWidth_6;
                int _S671 = int(_S670);
                uint _S672 = 3U / _S669;
                int3 _S673 = int3(_S671, int(_S672), int(0));
                int2 minimumTile_6 = int2(_S668, int(uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S673)).xy), uint(((_S673)).z))).x * 255.0f))));
                uint _S674 = atlasWidth_6;
                uint _S675 = 4U % atlasWidth_6;
                int _S676 = int(_S675);
                uint _S677 = 4U / _S674;
                int3 _S678 = int3(_S676, int(_S677), int(0));
                uint _S679 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S678)).xy), uint(((_S678)).z))).x * 255.0f));
                uint _S680 = atlasWidth_6;
                uint _S681 = 5U % atlasWidth_6;
                int _S682 = int(_S681);
                uint _S683 = 5U / _S680;
                int3 _S684 = int3(_S682, int(_S683), int(0));
                uint _S685 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S684)).xy), uint(((_S684)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S679, _S685);
                int2 tile_12 = int2(floor(_S161)) - minimumTile_6;
                if(any(tile_12 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_12 >= int2(grid_6));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S686 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S686)).xy), uint(((_S686)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S679 + uint(tile_12.x));
                uint _S687 = atlasWidth_6;
                uint _S688 = offset_6 % atlasWidth_6;
                int _S689 = int(_S688);
                uint _S690 = offset_6 / _S687;
                int3 _S691 = int3(_S689, int(_S690), int(0));
                uint _S692 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S691)).xy), uint(((_S691)).z))).x * 255.0f));
                uint _S693 = atlasWidth_6;
                uint _S694 = offset_6 + 1U;
                uint _S695 = _S694 % atlasWidth_6;
                int _S696 = int(_S695);
                uint _S697 = _S694 / _S693;
                int3 _S698 = int3(_S696, int(_S697), int(0));
                uint slot_12 = _S692 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S698)).xy), uint(((_S698)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S699 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S699)).xy), uint(((_S699)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S700 = atlasWidth_6;
                uint _S701 = 6U % atlasWidth_6;
                int _S702 = int(_S701);
                uint _S703 = 6U / _S700;
                int3 _S704 = int3(_S702, int(_S703), int(0));
                uint _S705 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S704)).xy), uint(((_S704)).z))).x * 255.0f));
                uint _S706 = atlasWidth_6;
                uint _S707 = 7U % atlasWidth_6;
                int _S708 = int(_S707);
                uint _S709 = 7U / _S706;
                int3 _S710 = int3(_S708, int(_S709), int(0));
                uint columns_6 = _S705 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S710)).xy), uint(((_S710)).z))).x * 255.0f)) << 8U);
                uint _S711 = atlasWidth_6;
                uint _S712 = 8U % atlasWidth_6;
                int _S713 = int(_S712);
                uint _S714 = 8U / _S711;
                int3 _S715 = int3(_S713, int(_S714), int(0));
                uint _S716 = uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S715)).xy), uint(((_S715)).z))).x * 255.0f));
                uint _S717 = atlasWidth_6;
                uint _S718 = 9U % atlasWidth_6;
                int _S719 = int(_S718);
                uint _S720 = 9U / _S717;
                int3 _S721 = int3(_S719, int(_S720), int(0));
                uint rows_6 = ((_S716 | (uint(round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S721)).xy), uint(((_S721)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S679 * _S685 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S722 = atlasWidth_6 / columns_6;
                float _S723 = float(_S722);
                uint _S724 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S723, float(_S724));
                uint _S725 = slot_13 % columns_6;
                float _S726 = float(_S725);
                uint _S727 = slot_13 / columns_6;
                _S163 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(_S726, float(_S727)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(_S161) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S728 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(_S161)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S728);
            }
            if(hasSceneLighting_0)
            {
                int3 _S729 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S163 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S729)).xy), uint(((_S729)).z)));
                break;
            }
            uint _S730 = atlasWidth_6 / uint(_S728.x);
            float _S731 = float(_S730);
            uint _S732 = (atlasHeight_6 - 1U) / uint(_S728.y);
            float2 cellSize_13 = float2(_S731, float(_S732));
            _S163 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        emissionSample_0 = _S163;
        if((_S146.w) != 0.0f)
        {
            texture2d<float, access::sample> _S733 = (&kernelContext_0)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S733).get_width(0)),(*((&sourceHeight_1)) = (_S733).get_height(0));
            texture2d<float, access::sample> _S734 = (&kernelContext_0)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S734).get_width(0)),(*((&valueHeight_1)) = (_S734).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float4 _S735 = emissionSample_0;
                for(;;)
                {
                    float _S736 = _S735.w;
                    if(_S736 > 1.0f)
                    {
                        unlitColor_0 = _S735.xyz;
                        break;
                    }
                    texture2d<float, access::sample> _S737 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_9;
                    thread uint height_9;
                    (*((&width_9)) = (_S737).get_width(0)),(*((&height_9)) = (_S737).get_height(0));
                    uint3 _S738 = uint3(round(_S735.xyz * float3(255.0f) ));
                    uint index_2 = ((_S738.x) | ((_S738.y) << 8U)) | ((_S738.z) << 16U);
                    if(_S736 == 0.0f)
                    {
                        _S164 = _S738;
                    }
                    else
                    {
                        _S164 = uint3(index_2, index_2, index_2);
                    }
                    uint3 address_1 = _S164 * uint3(3U)  + uint3(0U, 1U, 2U);
                    uint _S739 = address_1.x;
                    uint _S740 = _S739 % width_9;
                    int _S741 = int(_S740);
                    uint _S742 = _S739 / width_9;
                    int3 _S743 = int3(_S741, int(_S742), int(0));
                    float _S744 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S743)).xy), uint(((_S743)).z))).x;
                    uint _S745 = address_1.y;
                    uint _S746 = _S745 % width_9;
                    int _S747 = int(_S746);
                    uint _S748 = _S745 / width_9;
                    int3 _S749 = int3(_S747, int(_S748), int(0));
                    float _S750 = (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S749)).xy), uint(((_S749)).z))).x;
                    uint _S751 = address_1.z;
                    uint _S752 = _S751 % width_9;
                    int _S753 = int(_S752);
                    uint _S754 = _S751 / width_9;
                    int3 _S755 = int3(_S753, int(_S754), int(0));
                    unlitColor_0 = float3(_S744, _S750, (((&kernelContext_0)->emissiveValuesTexture_0).read(vec<uint,2>(((_S755)).xy), uint(((_S755)).z))).x);
                    break;
                }
                emissionSample_0.xyz = unlitColor_0;
            }
            else
            {
                if(_S656)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if(_S657)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S756 = index_0 | lightCount_0;
                for(;;)
                {
                    if(_S756 == 0U)
                    {
                        _S162 = _S161;
                        break;
                    }
                    texture2d<float, access::sample> _S757 = (&kernelContext_0)->emissiveValuesTexture_0;
                    thread uint width_10;
                    thread uint height_10;
                    (*((&width_10)) = (_S757).get_width(0)),(*((&height_10)) = (_S757).get_height(0));
                    float2 size_8 = float2(float(width_10), float(height_10));
                    float2 _S758 = float2(0.5f) ;
                    float2 remapped_8 = clamp((_S161 * (size_8 - float2(2.0f) ) + float2(1.0f) ) / size_8, _S758 / size_8, (size_8 - _S758) / size_8);
                    if((_S756 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_8.x;
                    }
                    else
                    {
                        occlusion_0 = _S161.x;
                    }
                    if((_S756 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_8.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S161.y;
                    }
                    _S162 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                emissionSample_0.z = (((&kernelContext_0)->emissiveValuesTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S162))).x;
            }
        }
        float4 _S759 = emissionSample_0;
        for(;;)
        {
            float4 _S760 = float4(_S7->compositeControls_0) ;
            if((_S760.x) != 16.0f)
            {
                _S163 = _S759;
                break;
            }
            bool _S761 = (_S760.w) >= 0.5f;
            if((maskU_3 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_3 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S762 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S761)
                {
                    for(;;)
                    {
                        if(_S762 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S763 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_11;
                        thread uint height_11;
                        (*((&width_11)) = (_S763).get_width(0)),(*((&height_11)) = (_S763).get_height(0));
                        float2 size_9 = float2(float(width_11), float(height_11));
                        float2 _S764 = float2(0.5f) ;
                        float2 remapped_9 = clamp((_S161 * (size_9 - float2(2.0f) ) + float2(1.0f) ) / size_9, _S764 / size_9, (size_9 - _S764) / size_9);
                        if((_S762 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_9.x;
                        }
                        else
                        {
                            occlusion_0 = _S161.x;
                        }
                        if((_S762 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_9.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S161.y;
                        }
                        _S161 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S163 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S161)));
                    break;
                }
                texture2d<float, access::sample> _S765 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S765).get_width(0)),(*((&atlasHeight_7)) = (_S765).get_height(0));
                int3 _S766 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S766)).xy), uint(((_S766)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S767 = atlasWidth_7;
                    uint _S768 = 2U % atlasWidth_7;
                    int _S769 = int(_S768);
                    uint _S770 = 2U / _S767;
                    int3 _S771 = int3(_S769, int(_S770), int(0));
                    int _S772 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S771)).xy), uint(((_S771)).z))).x * 255.0f)));
                    uint _S773 = atlasWidth_7;
                    uint _S774 = 3U % atlasWidth_7;
                    int _S775 = int(_S774);
                    uint _S776 = 3U / _S773;
                    int3 _S777 = int3(_S775, int(_S776), int(0));
                    int2 minimumTile_7 = int2(_S772, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S777)).xy), uint(((_S777)).z))).x * 255.0f))));
                    uint _S778 = atlasWidth_7;
                    uint _S779 = 4U % atlasWidth_7;
                    int _S780 = int(_S779);
                    uint _S781 = 4U / _S778;
                    int3 _S782 = int3(_S780, int(_S781), int(0));
                    uint _S783 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S782)).xy), uint(((_S782)).z))).x * 255.0f));
                    uint _S784 = atlasWidth_7;
                    uint _S785 = 5U % atlasWidth_7;
                    int _S786 = int(_S785);
                    uint _S787 = 5U / _S784;
                    int3 _S788 = int3(_S786, int(_S787), int(0));
                    uint _S789 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S788)).xy), uint(((_S788)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S783, _S789);
                    int2 tile_14 = int2(floor(_S161)) - minimumTile_7;
                    if(any(tile_14 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_14 >= int2(grid_7));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S790 = int3(int(1), int(0), int(0));
                        _S163 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S790)).xy), uint(((_S790)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S783 + uint(tile_14.x));
                    uint _S791 = atlasWidth_7;
                    uint _S792 = offset_7 % atlasWidth_7;
                    int _S793 = int(_S792);
                    uint _S794 = offset_7 / _S791;
                    int3 _S795 = int3(_S793, int(_S794), int(0));
                    uint _S796 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S795)).xy), uint(((_S795)).z))).x * 255.0f));
                    uint _S797 = atlasWidth_7;
                    uint _S798 = offset_7 + 1U;
                    uint _S799 = _S798 % atlasWidth_7;
                    int _S800 = int(_S799);
                    uint _S801 = _S798 / _S797;
                    int3 _S802 = int3(_S800, int(_S801), int(0));
                    uint slot_14 = _S796 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S802)).xy), uint(((_S802)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S803 = int3(int(1), int(0), int(0));
                        _S163 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S803)).xy), uint(((_S803)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S804 = atlasWidth_7;
                    uint _S805 = 6U % atlasWidth_7;
                    int _S806 = int(_S805);
                    uint _S807 = 6U / _S804;
                    int3 _S808 = int3(_S806, int(_S807), int(0));
                    uint _S809 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S808)).xy), uint(((_S808)).z))).x * 255.0f));
                    uint _S810 = atlasWidth_7;
                    uint _S811 = 7U % atlasWidth_7;
                    int _S812 = int(_S811);
                    uint _S813 = 7U / _S810;
                    int3 _S814 = int3(_S812, int(_S813), int(0));
                    uint columns_7 = _S809 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S814)).xy), uint(((_S814)).z))).x * 255.0f)) << 8U);
                    uint _S815 = atlasWidth_7;
                    uint _S816 = 8U % atlasWidth_7;
                    int _S817 = int(_S816);
                    uint _S818 = 8U / _S815;
                    int3 _S819 = int3(_S817, int(_S818), int(0));
                    uint _S820 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S819)).xy), uint(((_S819)).z))).x * 255.0f));
                    uint _S821 = atlasWidth_7;
                    uint _S822 = 9U % atlasWidth_7;
                    int _S823 = int(_S822);
                    uint _S824 = 9U / _S821;
                    int3 _S825 = int3(_S823, int(_S824), int(0));
                    uint rows_7 = ((_S820 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S825)).xy), uint(((_S825)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S783 * _S789 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S826 = atlasWidth_7 / columns_7;
                    float _S827 = float(_S826);
                    uint _S828 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S827, float(_S828));
                    uint _S829 = slot_15 % columns_7;
                    float _S830 = float(_S829);
                    uint _S831 = slot_15 / columns_7;
                    _S163 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S830, float(_S831)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(_S161) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S832 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(_S161)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S832);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S833 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S163 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S833)).xy), uint(((_S833)).z)));
                    break;
                }
                uint _S834 = atlasWidth_7 / uint(_S832.x);
                float _S835 = float(_S834);
                uint _S836 = (atlasHeight_7 - 1U) / uint(_S832.y);
                float2 cellSize_15 = float2(_S835, float(_S836));
                _S163 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_3 = uint(round(_S760.y));
            if(operation_3 == 1U)
            {
                _S163 = _S759 * _S163;
                break;
            }
            if(operation_3 == 2U)
            {
                _S163 = _S759 + _S163;
                break;
            }
            if(operation_3 == 3U)
            {
                _S163 = _S759 - _S163;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S760.z;
                _S163 = _S759 * float4((1.0f - factor_3))  + _S163 * float4(factor_3) ;
                break;
            }
            _S163 = _S759;
            break;
        }
        unlitColor_0 = _S163.xyz;
    }
    else
    {
        unlitColor_0 = emissiveColor_0;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool useSecondary_4 = (secondaryMask_0 & 64U) != 0U;
        if((alternateMask_0 & 64U) != 0U)
        {
            if(useSecondary_4)
            {
                _S161 = _S160;
            }
            else
            {
                _S161 = _S159;
            }
        }
        else
        {
            if(useSecondary_4)
            {
                _S161 = _S152;
            }
            else
            {
                _S161 = _S151;
            }
        }
        for(;;)
        {
            bool _S837 = (udimMask_0 & 64U) != 0U;
            uint maskU_4 = uint(round(_S145.y));
            uint maskV_4 = uint(round(_S145.z));
            if((maskU_4 & 64U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_4 & 64U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S838 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S837)
                {
                    for(;;)
                    {
                        if(_S838 == 0U)
                        {
                            _S162 = _S161;
                            break;
                        }
                        texture2d<float, access::sample> _S839 = (&kernelContext_0)->opacityTexture_0;
                        thread uint width_12;
                        thread uint height_12;
                        (*((&width_12)) = (_S839).get_width(0)),(*((&height_12)) = (_S839).get_height(0));
                        float2 size_10 = float2(float(width_12), float(height_12));
                        float2 _S840 = float2(0.5f) ;
                        float2 remapped_10 = clamp((_S161 * (size_10 - float2(2.0f) ) + float2(1.0f) ) / size_10, _S840 / size_10, (size_10 - _S840) / size_10);
                        if((_S838 & 1U) != 0U)
                        {
                            opacity_0 = remapped_10.x;
                        }
                        else
                        {
                            opacity_0 = _S161.x;
                        }
                        if((_S838 & 2U) != 0U)
                        {
                            occlusion_0 = remapped_10.y;
                        }
                        else
                        {
                            occlusion_0 = _S161.y;
                        }
                        _S162 = float2(opacity_0, occlusion_0);
                        break;
                    }
                    _S163 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S162)));
                    break;
                }
                texture2d<float, access::sample> _S841 = (&kernelContext_0)->opacityTexture_0;
                thread uint atlasWidth_8;
                thread uint atlasHeight_8;
                (*((&atlasWidth_8)) = (_S841).get_width(0)),(*((&atlasHeight_8)) = (_S841).get_height(0));
                int3 _S842 = int3(int(0), int(0), int(0));
                float4 metadata_8 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S842)).xy), uint(((_S842)).z))) * float4(255.0f) );
                if((metadata_8.x) == 255.0f)
                {
                    uint _S843 = atlasWidth_8;
                    uint _S844 = 2U % atlasWidth_8;
                    int _S845 = int(_S844);
                    uint _S846 = 2U / _S843;
                    int3 _S847 = int3(_S845, int(_S846), int(0));
                    int _S848 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S847)).xy), uint(((_S847)).z))).x * 255.0f)));
                    uint _S849 = atlasWidth_8;
                    uint _S850 = 3U % atlasWidth_8;
                    int _S851 = int(_S850);
                    uint _S852 = 3U / _S849;
                    int3 _S853 = int3(_S851, int(_S852), int(0));
                    int2 minimumTile_8 = int2(_S848, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S853)).xy), uint(((_S853)).z))).x * 255.0f))));
                    uint _S854 = atlasWidth_8;
                    uint _S855 = 4U % atlasWidth_8;
                    int _S856 = int(_S855);
                    uint _S857 = 4U / _S854;
                    int3 _S858 = int3(_S856, int(_S857), int(0));
                    uint _S859 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S858)).xy), uint(((_S858)).z))).x * 255.0f));
                    uint _S860 = atlasWidth_8;
                    uint _S861 = 5U % atlasWidth_8;
                    int _S862 = int(_S861);
                    uint _S863 = 5U / _S860;
                    int3 _S864 = int3(_S862, int(_S863), int(0));
                    uint _S865 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S864)).xy), uint(((_S864)).z))).x * 255.0f));
                    uint2 grid_8 = uint2(_S859, _S865);
                    int2 tile_16 = int2(floor(_S161)) - minimumTile_8;
                    if(any(tile_16 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_16 >= int2(grid_8));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S866 = int3(int(1), int(0), int(0));
                        _S163 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S866)).xy), uint(((_S866)).z)));
                        break;
                    }
                    uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S859 + uint(tile_16.x));
                    uint _S867 = atlasWidth_8;
                    uint _S868 = offset_8 % atlasWidth_8;
                    int _S869 = int(_S868);
                    uint _S870 = offset_8 / _S867;
                    int3 _S871 = int3(_S869, int(_S870), int(0));
                    uint _S872 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S871)).xy), uint(((_S871)).z))).x * 255.0f));
                    uint _S873 = atlasWidth_8;
                    uint _S874 = offset_8 + 1U;
                    uint _S875 = _S874 % atlasWidth_8;
                    int _S876 = int(_S875);
                    uint _S877 = _S874 / _S873;
                    int3 _S878 = int3(_S876, int(_S877), int(0));
                    uint slot_16 = _S872 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S878)).xy), uint(((_S878)).z))).x * 255.0f)) << 8U);
                    if(slot_16 == 0U)
                    {
                        int3 _S879 = int3(int(1), int(0), int(0));
                        _S163 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S879)).xy), uint(((_S879)).z)));
                        break;
                    }
                    uint slot_17 = slot_16 - 1U;
                    uint _S880 = atlasWidth_8;
                    uint _S881 = 6U % atlasWidth_8;
                    int _S882 = int(_S881);
                    uint _S883 = 6U / _S880;
                    int3 _S884 = int3(_S882, int(_S883), int(0));
                    uint _S885 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S884)).xy), uint(((_S884)).z))).x * 255.0f));
                    uint _S886 = atlasWidth_8;
                    uint _S887 = 7U % atlasWidth_8;
                    int _S888 = int(_S887);
                    uint _S889 = 7U / _S886;
                    int3 _S890 = int3(_S888, int(_S889), int(0));
                    uint columns_8 = _S885 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S890)).xy), uint(((_S890)).z))).x * 255.0f)) << 8U);
                    uint _S891 = atlasWidth_8;
                    uint _S892 = 8U % atlasWidth_8;
                    int _S893 = int(_S892);
                    uint _S894 = 8U / _S891;
                    int3 _S895 = int3(_S893, int(_S894), int(0));
                    uint _S896 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S895)).xy), uint(((_S895)).z))).x * 255.0f));
                    uint _S897 = atlasWidth_8;
                    uint _S898 = 9U % atlasWidth_8;
                    int _S899 = int(_S898);
                    uint _S900 = 9U / _S897;
                    int3 _S901 = int3(_S899, int(_S900), int(0));
                    uint rows_8 = ((_S896 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S901)).xy), uint(((_S901)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                    uint metadataRows_8 = (10U + 2U * _S859 * _S865 + atlasWidth_8 - 1U) / atlasWidth_8;
                    uint _S902 = atlasWidth_8 / columns_8;
                    float _S903 = float(_S902);
                    uint _S904 = (atlasHeight_8 - metadataRows_8) / rows_8;
                    float2 cellSize_16 = float2(_S903, float(_S904));
                    uint _S905 = slot_17 % columns_8;
                    float _S906 = float(_S905);
                    uint _S907 = slot_17 / columns_8;
                    _S163 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S906, float(_S907)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(_S161) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                    break;
                }
                int2 _S908 = int2(metadata_8.zw);
                int2 tile_17 = int2(floor(_S161)) - int2(metadata_8.xy);
                if(any(tile_17 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_17 >= _S908);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S909 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                    _S163 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S909)).xy), uint(((_S909)).z)));
                    break;
                }
                uint _S910 = atlasWidth_8 / uint(_S908.x);
                float _S911 = float(_S910);
                uint _S912 = (atlasHeight_8 - 1U) / uint(_S908.y);
                float2 cellSize_17 = float2(_S911, float(_S912));
                _S163 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            for(;;)
            {
                float4 _S913 = float4(_S7->compositeControls_0) ;
                if((_S913.x) != 64.0f)
                {
                    break;
                }
                bool _S914 = (_S913.w) >= 0.5f;
                if((maskU_4 & 1U) != 0U)
                {
                    index_0 = 1U;
                }
                else
                {
                    index_0 = 0U;
                }
                if((maskV_4 & 1U) != 0U)
                {
                    lightCount_0 = 2U;
                }
                else
                {
                    lightCount_0 = 0U;
                }
                uint _S915 = index_0 | lightCount_0;
                for(;;)
                {
                    if(!_S914)
                    {
                        for(;;)
                        {
                            if(_S915 == 0U)
                            {
                                break;
                            }
                            texture2d<float, access::sample> _S916 = (&kernelContext_0)->compositeTexture_0;
                            thread uint width_13;
                            thread uint height_13;
                            (*((&width_13)) = (_S916).get_width(0)),(*((&height_13)) = (_S916).get_height(0));
                            float2 size_11 = float2(float(width_13), float(height_13));
                            float2 _S917 = float2(0.5f) ;
                            float2 remapped_11 = clamp((_S161 * (size_11 - float2(2.0f) ) + float2(1.0f) ) / size_11, _S917 / size_11, (size_11 - _S917) / size_11);
                            if((_S915 & 1U) != 0U)
                            {
                                opacity_0 = remapped_11.x;
                            }
                            else
                            {
                                opacity_0 = _S161.x;
                            }
                            if((_S915 & 2U) != 0U)
                            {
                                occlusion_0 = remapped_11.y;
                            }
                            else
                            {
                                occlusion_0 = _S161.y;
                            }
                            _S161 = float2(opacity_0, occlusion_0);
                            break;
                        }
                        _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S161)));
                        break;
                    }
                    texture2d<float, access::sample> _S918 = (&kernelContext_0)->compositeTexture_0;
                    thread uint atlasWidth_9;
                    thread uint atlasHeight_9;
                    (*((&atlasWidth_9)) = (_S918).get_width(0)),(*((&atlasHeight_9)) = (_S918).get_height(0));
                    int3 _S919 = int3(int(0), int(0), int(0));
                    float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S919)).xy), uint(((_S919)).z))) * float4(255.0f) );
                    if((metadata_9.x) == 255.0f)
                    {
                        uint _S920 = atlasWidth_9;
                        uint _S921 = 2U % atlasWidth_9;
                        int _S922 = int(_S921);
                        uint _S923 = 2U / _S920;
                        int3 _S924 = int3(_S922, int(_S923), int(0));
                        int _S925 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S924)).xy), uint(((_S924)).z))).x * 255.0f)));
                        uint _S926 = atlasWidth_9;
                        uint _S927 = 3U % atlasWidth_9;
                        int _S928 = int(_S927);
                        uint _S929 = 3U / _S926;
                        int3 _S930 = int3(_S928, int(_S929), int(0));
                        int2 minimumTile_9 = int2(_S925, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S930)).xy), uint(((_S930)).z))).x * 255.0f))));
                        uint _S931 = atlasWidth_9;
                        uint _S932 = 4U % atlasWidth_9;
                        int _S933 = int(_S932);
                        uint _S934 = 4U / _S931;
                        int3 _S935 = int3(_S933, int(_S934), int(0));
                        uint _S936 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S935)).xy), uint(((_S935)).z))).x * 255.0f));
                        uint _S937 = atlasWidth_9;
                        uint _S938 = 5U % atlasWidth_9;
                        int _S939 = int(_S938);
                        uint _S940 = 5U / _S937;
                        int3 _S941 = int3(_S939, int(_S940), int(0));
                        uint _S942 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S941)).xy), uint(((_S941)).z))).x * 255.0f));
                        uint2 grid_9 = uint2(_S936, _S942);
                        int2 tile_18 = int2(floor(_S161)) - minimumTile_9;
                        if(any(tile_18 < (int2(int(0)) )))
                        {
                            hasSceneLighting_0 = true;
                        }
                        else
                        {
                            hasSceneLighting_0 = any(tile_18 >= int2(grid_9));
                        }
                        if(hasSceneLighting_0)
                        {
                            int3 _S943 = int3(int(1), int(0), int(0));
                            _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S943)).xy), uint(((_S943)).z)));
                            break;
                        }
                        uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S936 + uint(tile_18.x));
                        uint _S944 = atlasWidth_9;
                        uint _S945 = offset_9 % atlasWidth_9;
                        int _S946 = int(_S945);
                        uint _S947 = offset_9 / _S944;
                        int3 _S948 = int3(_S946, int(_S947), int(0));
                        uint _S949 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S948)).xy), uint(((_S948)).z))).x * 255.0f));
                        uint _S950 = atlasWidth_9;
                        uint _S951 = offset_9 + 1U;
                        uint _S952 = _S951 % atlasWidth_9;
                        int _S953 = int(_S952);
                        uint _S954 = _S951 / _S950;
                        int3 _S955 = int3(_S953, int(_S954), int(0));
                        uint slot_18 = _S949 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S955)).xy), uint(((_S955)).z))).x * 255.0f)) << 8U);
                        if(slot_18 == 0U)
                        {
                            int3 _S956 = int3(int(1), int(0), int(0));
                            _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S956)).xy), uint(((_S956)).z)));
                            break;
                        }
                        uint slot_19 = slot_18 - 1U;
                        uint _S957 = atlasWidth_9;
                        uint _S958 = 6U % atlasWidth_9;
                        int _S959 = int(_S958);
                        uint _S960 = 6U / _S957;
                        int3 _S961 = int3(_S959, int(_S960), int(0));
                        uint _S962 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S961)).xy), uint(((_S961)).z))).x * 255.0f));
                        uint _S963 = atlasWidth_9;
                        uint _S964 = 7U % atlasWidth_9;
                        int _S965 = int(_S964);
                        uint _S966 = 7U / _S963;
                        int3 _S967 = int3(_S965, int(_S966), int(0));
                        uint columns_9 = _S962 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S967)).xy), uint(((_S967)).z))).x * 255.0f)) << 8U);
                        uint _S968 = atlasWidth_9;
                        uint _S969 = 8U % atlasWidth_9;
                        int _S970 = int(_S969);
                        uint _S971 = 8U / _S968;
                        int3 _S972 = int3(_S970, int(_S971), int(0));
                        uint _S973 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S972)).xy), uint(((_S972)).z))).x * 255.0f));
                        uint _S974 = atlasWidth_9;
                        uint _S975 = 9U % atlasWidth_9;
                        int _S976 = int(_S975);
                        uint _S977 = 9U / _S974;
                        int3 _S978 = int3(_S976, int(_S977), int(0));
                        uint rows_9 = ((_S973 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S978)).xy), uint(((_S978)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                        uint metadataRows_9 = (10U + 2U * _S936 * _S942 + atlasWidth_9 - 1U) / atlasWidth_9;
                        uint _S979 = atlasWidth_9 / columns_9;
                        float _S980 = float(_S979);
                        uint _S981 = (atlasHeight_9 - metadataRows_9) / rows_9;
                        float2 cellSize_18 = float2(_S980, float(_S981));
                        uint _S982 = slot_19 % columns_9;
                        float _S983 = float(_S982);
                        uint _S984 = slot_19 / columns_9;
                        _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S983, float(_S984)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(_S161) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                        break;
                    }
                    int2 _S985 = int2(metadata_9.zw);
                    int2 tile_19 = int2(floor(_S161)) - int2(metadata_9.xy);
                    if(any(tile_19 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_19 >= _S985);
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S986 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S986)).xy), uint(((_S986)).z)));
                        break;
                    }
                    uint _S987 = atlasWidth_9 / uint(_S985.x);
                    float _S988 = float(_S987);
                    uint _S989 = (atlasHeight_9 - 1U) / uint(_S985.y);
                    float2 cellSize_19 = float2(_S988, float(_S989));
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                uint operation_4 = uint(round(_S913.y));
                if(operation_4 == 1U)
                {
                    _S163 = _S163 * _S348;
                    break;
                }
                if(operation_4 == 2U)
                {
                    _S163 = _S163 + _S348;
                    break;
                }
                if(operation_4 == 3U)
                {
                    _S163 = _S163 - _S348;
                    break;
                }
                if(operation_4 == 4U)
                {
                    float factor_4 = _S913.z;
                    _S163 = _S163 * float4((1.0f - factor_4))  + _S348 * float4(factor_4) ;
                    break;
                }
                break;
            }
            _S4 = saturate(_S163.x);
            break;
        }
        opacity_0 = _S4;
    }
    if((textureMask_0 & 128U) != 0U)
    {
        bool useSecondary_5 = (secondaryMask_0 & 128U) != 0U;
        if((alternateMask_0 & 128U) != 0U)
        {
            if(useSecondary_5)
            {
                _S161 = _S160;
            }
            else
            {
                _S161 = _S159;
            }
        }
        else
        {
            if(useSecondary_5)
            {
                _S161 = _S152;
            }
            else
            {
                _S161 = _S151;
            }
        }
        bool _S990 = (udimMask_0 & 128U) != 0U;
        uint maskU_5 = uint(round(_S145.y));
        uint maskV_5 = uint(round(_S145.z));
        if((maskU_5 & 128U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_5 & 128U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S991 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S990)
            {
                for(;;)
                {
                    if(_S991 == 0U)
                    {
                        _S162 = _S161;
                        break;
                    }
                    texture2d<float, access::sample> _S992 = (&kernelContext_0)->occlusionTexture_0;
                    thread uint width_14;
                    thread uint height_14;
                    (*((&width_14)) = (_S992).get_width(0)),(*((&height_14)) = (_S992).get_height(0));
                    float2 size_12 = float2(float(width_14), float(height_14));
                    float2 _S993 = float2(0.5f) ;
                    float2 remapped_12 = clamp((_S161 * (size_12 - float2(2.0f) ) + float2(1.0f) ) / size_12, _S993 / size_12, (size_12 - _S993) / size_12);
                    if((_S991 & 1U) != 0U)
                    {
                        occlusion_0 = remapped_12.x;
                    }
                    else
                    {
                        occlusion_0 = _S161.x;
                    }
                    if((_S991 & 2U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_12.y;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S161.y;
                    }
                    _S162 = float2(occlusion_0, clearcoatAmount_0);
                    break;
                }
                _S163 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S162)));
                break;
            }
            texture2d<float, access::sample> _S994 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S994).get_width(0)),(*((&atlasHeight_10)) = (_S994).get_height(0));
            int3 _S995 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S995)).xy), uint(((_S995)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S996 = atlasWidth_10;
                uint _S997 = 2U % atlasWidth_10;
                int _S998 = int(_S997);
                uint _S999 = 2U / _S996;
                int3 _S1000 = int3(_S998, int(_S999), int(0));
                int _S1001 = int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1000)).xy), uint(((_S1000)).z))).x * 255.0f)));
                uint _S1002 = atlasWidth_10;
                uint _S1003 = 3U % atlasWidth_10;
                int _S1004 = int(_S1003);
                uint _S1005 = 3U / _S1002;
                int3 _S1006 = int3(_S1004, int(_S1005), int(0));
                int2 minimumTile_10 = int2(_S1001, int(uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1006)).xy), uint(((_S1006)).z))).x * 255.0f))));
                uint _S1007 = atlasWidth_10;
                uint _S1008 = 4U % atlasWidth_10;
                int _S1009 = int(_S1008);
                uint _S1010 = 4U / _S1007;
                int3 _S1011 = int3(_S1009, int(_S1010), int(0));
                uint _S1012 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1011)).xy), uint(((_S1011)).z))).x * 255.0f));
                uint _S1013 = atlasWidth_10;
                uint _S1014 = 5U % atlasWidth_10;
                int _S1015 = int(_S1014);
                uint _S1016 = 5U / _S1013;
                int3 _S1017 = int3(_S1015, int(_S1016), int(0));
                uint _S1018 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1017)).xy), uint(((_S1017)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S1012, _S1018);
                int2 tile_20 = int2(floor(_S161)) - minimumTile_10;
                if(any(tile_20 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_20 >= int2(grid_10));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1019 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1019)).xy), uint(((_S1019)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S1012 + uint(tile_20.x));
                uint _S1020 = atlasWidth_10;
                uint _S1021 = offset_10 % atlasWidth_10;
                int _S1022 = int(_S1021);
                uint _S1023 = offset_10 / _S1020;
                int3 _S1024 = int3(_S1022, int(_S1023), int(0));
                uint _S1025 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1024)).xy), uint(((_S1024)).z))).x * 255.0f));
                uint _S1026 = atlasWidth_10;
                uint _S1027 = offset_10 + 1U;
                uint _S1028 = _S1027 % atlasWidth_10;
                int _S1029 = int(_S1028);
                uint _S1030 = _S1027 / _S1026;
                int3 _S1031 = int3(_S1029, int(_S1030), int(0));
                uint slot_20 = _S1025 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1031)).xy), uint(((_S1031)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1032 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1032)).xy), uint(((_S1032)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1033 = atlasWidth_10;
                uint _S1034 = 6U % atlasWidth_10;
                int _S1035 = int(_S1034);
                uint _S1036 = 6U / _S1033;
                int3 _S1037 = int3(_S1035, int(_S1036), int(0));
                uint _S1038 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1037)).xy), uint(((_S1037)).z))).x * 255.0f));
                uint _S1039 = atlasWidth_10;
                uint _S1040 = 7U % atlasWidth_10;
                int _S1041 = int(_S1040);
                uint _S1042 = 7U / _S1039;
                int3 _S1043 = int3(_S1041, int(_S1042), int(0));
                uint columns_10 = _S1038 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1043)).xy), uint(((_S1043)).z))).x * 255.0f)) << 8U);
                uint _S1044 = atlasWidth_10;
                uint _S1045 = 8U % atlasWidth_10;
                int _S1046 = int(_S1045);
                uint _S1047 = 8U / _S1044;
                int3 _S1048 = int3(_S1046, int(_S1047), int(0));
                uint _S1049 = uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1048)).xy), uint(((_S1048)).z))).x * 255.0f));
                uint _S1050 = atlasWidth_10;
                uint _S1051 = 9U % atlasWidth_10;
                int _S1052 = int(_S1051);
                uint _S1053 = 9U / _S1050;
                int3 _S1054 = int3(_S1052, int(_S1053), int(0));
                uint rows_10 = ((_S1049 | (uint(round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1054)).xy), uint(((_S1054)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S1012 * _S1018 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1055 = atlasWidth_10 / columns_10;
                float _S1056 = float(_S1055);
                uint _S1057 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1056, float(_S1057));
                uint _S1058 = slot_21 % columns_10;
                float _S1059 = float(_S1058);
                uint _S1060 = slot_21 / columns_10;
                _S163 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(_S1059, float(_S1060)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(_S161) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1061 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(_S161)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1061);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1062 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S163 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S1062)).xy), uint(((_S1062)).z)));
                break;
            }
            uint _S1063 = atlasWidth_10 / uint(_S1061.x);
            float _S1064 = float(_S1063);
            uint _S1065 = (atlasHeight_10 - 1U) / uint(_S1061.y);
            float2 cellSize_21 = float2(_S1064, float(_S1065));
            _S163 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1066 = float4(_S7->compositeControls_0) ;
            if((_S1066.x) != 128.0f)
            {
                break;
            }
            bool _S1067 = (_S1066.w) >= 0.5f;
            if((maskU_5 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_5 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1068 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1067)
                {
                    for(;;)
                    {
                        if(_S1068 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1069 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_15;
                        thread uint height_15;
                        (*((&width_15)) = (_S1069).get_width(0)),(*((&height_15)) = (_S1069).get_height(0));
                        float2 size_13 = float2(float(width_15), float(height_15));
                        float2 _S1070 = float2(0.5f) ;
                        float2 remapped_13 = clamp((_S161 * (size_13 - float2(2.0f) ) + float2(1.0f) ) / size_13, _S1070 / size_13, (size_13 - _S1070) / size_13);
                        if((_S1068 & 1U) != 0U)
                        {
                            occlusion_0 = remapped_13.x;
                        }
                        else
                        {
                            occlusion_0 = _S161.x;
                        }
                        if((_S1068 & 2U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_13.y;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S161.y;
                        }
                        _S161 = float2(occlusion_0, clearcoatAmount_0);
                        break;
                    }
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S161)));
                    break;
                }
                texture2d<float, access::sample> _S1071 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1071).get_width(0)),(*((&atlasHeight_11)) = (_S1071).get_height(0));
                int3 _S1072 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1072)).xy), uint(((_S1072)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1073 = atlasWidth_11;
                    uint _S1074 = 2U % atlasWidth_11;
                    int _S1075 = int(_S1074);
                    uint _S1076 = 2U / _S1073;
                    int3 _S1077 = int3(_S1075, int(_S1076), int(0));
                    int _S1078 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1077)).xy), uint(((_S1077)).z))).x * 255.0f)));
                    uint _S1079 = atlasWidth_11;
                    uint _S1080 = 3U % atlasWidth_11;
                    int _S1081 = int(_S1080);
                    uint _S1082 = 3U / _S1079;
                    int3 _S1083 = int3(_S1081, int(_S1082), int(0));
                    int2 minimumTile_11 = int2(_S1078, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1083)).xy), uint(((_S1083)).z))).x * 255.0f))));
                    uint _S1084 = atlasWidth_11;
                    uint _S1085 = 4U % atlasWidth_11;
                    int _S1086 = int(_S1085);
                    uint _S1087 = 4U / _S1084;
                    int3 _S1088 = int3(_S1086, int(_S1087), int(0));
                    uint _S1089 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1088)).xy), uint(((_S1088)).z))).x * 255.0f));
                    uint _S1090 = atlasWidth_11;
                    uint _S1091 = 5U % atlasWidth_11;
                    int _S1092 = int(_S1091);
                    uint _S1093 = 5U / _S1090;
                    int3 _S1094 = int3(_S1092, int(_S1093), int(0));
                    uint _S1095 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1094)).xy), uint(((_S1094)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1089, _S1095);
                    int2 tile_22 = int2(floor(_S161)) - minimumTile_11;
                    if(any(tile_22 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_22 >= int2(grid_11));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1096 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1096)).xy), uint(((_S1096)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1089 + uint(tile_22.x));
                    uint _S1097 = atlasWidth_11;
                    uint _S1098 = offset_11 % atlasWidth_11;
                    int _S1099 = int(_S1098);
                    uint _S1100 = offset_11 / _S1097;
                    int3 _S1101 = int3(_S1099, int(_S1100), int(0));
                    uint _S1102 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1101)).xy), uint(((_S1101)).z))).x * 255.0f));
                    uint _S1103 = atlasWidth_11;
                    uint _S1104 = offset_11 + 1U;
                    uint _S1105 = _S1104 % atlasWidth_11;
                    int _S1106 = int(_S1105);
                    uint _S1107 = _S1104 / _S1103;
                    int3 _S1108 = int3(_S1106, int(_S1107), int(0));
                    uint slot_22 = _S1102 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1108)).xy), uint(((_S1108)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1109 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1109)).xy), uint(((_S1109)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1110 = atlasWidth_11;
                    uint _S1111 = 6U % atlasWidth_11;
                    int _S1112 = int(_S1111);
                    uint _S1113 = 6U / _S1110;
                    int3 _S1114 = int3(_S1112, int(_S1113), int(0));
                    uint _S1115 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1114)).xy), uint(((_S1114)).z))).x * 255.0f));
                    uint _S1116 = atlasWidth_11;
                    uint _S1117 = 7U % atlasWidth_11;
                    int _S1118 = int(_S1117);
                    uint _S1119 = 7U / _S1116;
                    int3 _S1120 = int3(_S1118, int(_S1119), int(0));
                    uint columns_11 = _S1115 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1120)).xy), uint(((_S1120)).z))).x * 255.0f)) << 8U);
                    uint _S1121 = atlasWidth_11;
                    uint _S1122 = 8U % atlasWidth_11;
                    int _S1123 = int(_S1122);
                    uint _S1124 = 8U / _S1121;
                    int3 _S1125 = int3(_S1123, int(_S1124), int(0));
                    uint _S1126 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1125)).xy), uint(((_S1125)).z))).x * 255.0f));
                    uint _S1127 = atlasWidth_11;
                    uint _S1128 = 9U % atlasWidth_11;
                    int _S1129 = int(_S1128);
                    uint _S1130 = 9U / _S1127;
                    int3 _S1131 = int3(_S1129, int(_S1130), int(0));
                    uint rows_11 = ((_S1126 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1131)).xy), uint(((_S1131)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1089 * _S1095 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1132 = atlasWidth_11 / columns_11;
                    float _S1133 = float(_S1132);
                    uint _S1134 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1133, float(_S1134));
                    uint _S1135 = slot_23 % columns_11;
                    float _S1136 = float(_S1135);
                    uint _S1137 = slot_23 / columns_11;
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1136, float(_S1137)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(_S161) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1138 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(_S161)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1138);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1139 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1139)).xy), uint(((_S1139)).z)));
                    break;
                }
                uint _S1140 = atlasWidth_11 / uint(_S1138.x);
                float _S1141 = float(_S1140);
                uint _S1142 = (atlasHeight_11 - 1U) / uint(_S1138.y);
                float2 cellSize_23 = float2(_S1141, float(_S1142));
                _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_5 = uint(round(_S1066.y));
            if(operation_5 == 1U)
            {
                _S163 = _S163 * _S348;
                break;
            }
            if(operation_5 == 2U)
            {
                _S163 = _S163 + _S348;
                break;
            }
            if(operation_5 == 3U)
            {
                _S163 = _S163 - _S348;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1066.z;
                _S163 = _S163 * float4((1.0f - factor_5))  + _S348 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S163.x);
    }
    else
    {
        occlusion_0 = _S131;
    }
    float3 specularColor_0;
    float clearcoatRoughness_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool useSecondary_6 = (secondaryMask_0 & 256U) != 0U;
        if((alternateMask_0 & 256U) != 0U)
        {
            if(useSecondary_6)
            {
                _S161 = _S160;
            }
            else
            {
                _S161 = _S159;
            }
        }
        else
        {
            if(useSecondary_6)
            {
                _S161 = _S152;
            }
            else
            {
                _S161 = _S151;
            }
        }
        bool _S1143 = (udimMask_0 & 256U) != 0U;
        uint maskU_6 = uint(round(_S145.y));
        uint maskV_6 = uint(round(_S145.z));
        if((maskU_6 & 256U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_6 & 256U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1144 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1143)
            {
                for(;;)
                {
                    if(_S1144 == 0U)
                    {
                        _S162 = _S161;
                        break;
                    }
                    texture2d<float, access::sample> _S1145 = (&kernelContext_0)->specularColorTexture_0;
                    thread uint width_16;
                    thread uint height_16;
                    (*((&width_16)) = (_S1145).get_width(0)),(*((&height_16)) = (_S1145).get_height(0));
                    float2 size_14 = float2(float(width_16), float(height_16));
                    float2 _S1146 = float2(0.5f) ;
                    float2 remapped_14 = clamp((_S161 * (size_14 - float2(2.0f) ) + float2(1.0f) ) / size_14, _S1146 / size_14, (size_14 - _S1146) / size_14);
                    if((_S1144 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_14.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S161.x;
                    }
                    if((_S1144 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_14.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S161.y;
                    }
                    _S162 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S163 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S162)));
                break;
            }
            texture2d<float, access::sample> _S1147 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1147).get_width(0)),(*((&atlasHeight_12)) = (_S1147).get_height(0));
            int3 _S1148 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1148)).xy), uint(((_S1148)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1149 = atlasWidth_12;
                uint _S1150 = 2U % atlasWidth_12;
                int _S1151 = int(_S1150);
                uint _S1152 = 2U / _S1149;
                int3 _S1153 = int3(_S1151, int(_S1152), int(0));
                int _S1154 = int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1153)).xy), uint(((_S1153)).z))).x * 255.0f)));
                uint _S1155 = atlasWidth_12;
                uint _S1156 = 3U % atlasWidth_12;
                int _S1157 = int(_S1156);
                uint _S1158 = 3U / _S1155;
                int3 _S1159 = int3(_S1157, int(_S1158), int(0));
                int2 minimumTile_12 = int2(_S1154, int(uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1159)).xy), uint(((_S1159)).z))).x * 255.0f))));
                uint _S1160 = atlasWidth_12;
                uint _S1161 = 4U % atlasWidth_12;
                int _S1162 = int(_S1161);
                uint _S1163 = 4U / _S1160;
                int3 _S1164 = int3(_S1162, int(_S1163), int(0));
                uint _S1165 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1164)).xy), uint(((_S1164)).z))).x * 255.0f));
                uint _S1166 = atlasWidth_12;
                uint _S1167 = 5U % atlasWidth_12;
                int _S1168 = int(_S1167);
                uint _S1169 = 5U / _S1166;
                int3 _S1170 = int3(_S1168, int(_S1169), int(0));
                uint _S1171 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1170)).xy), uint(((_S1170)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1165, _S1171);
                int2 tile_24 = int2(floor(_S161)) - minimumTile_12;
                if(any(tile_24 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_24 >= int2(grid_12));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1172 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1172)).xy), uint(((_S1172)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1165 + uint(tile_24.x));
                uint _S1173 = atlasWidth_12;
                uint _S1174 = offset_12 % atlasWidth_12;
                int _S1175 = int(_S1174);
                uint _S1176 = offset_12 / _S1173;
                int3 _S1177 = int3(_S1175, int(_S1176), int(0));
                uint _S1178 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1177)).xy), uint(((_S1177)).z))).x * 255.0f));
                uint _S1179 = atlasWidth_12;
                uint _S1180 = offset_12 + 1U;
                uint _S1181 = _S1180 % atlasWidth_12;
                int _S1182 = int(_S1181);
                uint _S1183 = _S1180 / _S1179;
                int3 _S1184 = int3(_S1182, int(_S1183), int(0));
                uint slot_24 = _S1178 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1184)).xy), uint(((_S1184)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1185 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1185)).xy), uint(((_S1185)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1186 = atlasWidth_12;
                uint _S1187 = 6U % atlasWidth_12;
                int _S1188 = int(_S1187);
                uint _S1189 = 6U / _S1186;
                int3 _S1190 = int3(_S1188, int(_S1189), int(0));
                uint _S1191 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1190)).xy), uint(((_S1190)).z))).x * 255.0f));
                uint _S1192 = atlasWidth_12;
                uint _S1193 = 7U % atlasWidth_12;
                int _S1194 = int(_S1193);
                uint _S1195 = 7U / _S1192;
                int3 _S1196 = int3(_S1194, int(_S1195), int(0));
                uint columns_12 = _S1191 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1196)).xy), uint(((_S1196)).z))).x * 255.0f)) << 8U);
                uint _S1197 = atlasWidth_12;
                uint _S1198 = 8U % atlasWidth_12;
                int _S1199 = int(_S1198);
                uint _S1200 = 8U / _S1197;
                int3 _S1201 = int3(_S1199, int(_S1200), int(0));
                uint _S1202 = uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1201)).xy), uint(((_S1201)).z))).x * 255.0f));
                uint _S1203 = atlasWidth_12;
                uint _S1204 = 9U % atlasWidth_12;
                int _S1205 = int(_S1204);
                uint _S1206 = 9U / _S1203;
                int3 _S1207 = int3(_S1205, int(_S1206), int(0));
                uint rows_12 = ((_S1202 | (uint(round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1207)).xy), uint(((_S1207)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1165 * _S1171 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1208 = atlasWidth_12 / columns_12;
                float _S1209 = float(_S1208);
                uint _S1210 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1209, float(_S1210));
                uint _S1211 = slot_25 % columns_12;
                float _S1212 = float(_S1211);
                uint _S1213 = slot_25 / columns_12;
                _S163 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(_S1212, float(_S1213)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(_S161) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1214 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(_S161)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1214);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1215 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S163 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S1215)).xy), uint(((_S1215)).z)));
                break;
            }
            uint _S1216 = atlasWidth_12 / uint(_S1214.x);
            float _S1217 = float(_S1216);
            uint _S1218 = (atlasHeight_12 - 1U) / uint(_S1214.y);
            float2 cellSize_25 = float2(_S1217, float(_S1218));
            _S163 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1219 = float4(_S7->compositeControls_0) ;
            if((_S1219.x) != 256.0f)
            {
                break;
            }
            bool _S1220 = (_S1219.w) >= 0.5f;
            if((maskU_6 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_6 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1221 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1220)
                {
                    for(;;)
                    {
                        if(_S1221 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1222 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_17;
                        thread uint height_17;
                        (*((&width_17)) = (_S1222).get_width(0)),(*((&height_17)) = (_S1222).get_height(0));
                        float2 size_15 = float2(float(width_17), float(height_17));
                        float2 _S1223 = float2(0.5f) ;
                        float2 remapped_15 = clamp((_S161 * (size_15 - float2(2.0f) ) + float2(1.0f) ) / size_15, _S1223 / size_15, (size_15 - _S1223) / size_15);
                        if((_S1221 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_15.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S161.x;
                        }
                        if((_S1221 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_15.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S161.y;
                        }
                        _S161 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S161)));
                    break;
                }
                texture2d<float, access::sample> _S1224 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1224).get_width(0)),(*((&atlasHeight_13)) = (_S1224).get_height(0));
                int3 _S1225 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1225)).xy), uint(((_S1225)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1226 = atlasWidth_13;
                    uint _S1227 = 2U % atlasWidth_13;
                    int _S1228 = int(_S1227);
                    uint _S1229 = 2U / _S1226;
                    int3 _S1230 = int3(_S1228, int(_S1229), int(0));
                    int _S1231 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1230)).xy), uint(((_S1230)).z))).x * 255.0f)));
                    uint _S1232 = atlasWidth_13;
                    uint _S1233 = 3U % atlasWidth_13;
                    int _S1234 = int(_S1233);
                    uint _S1235 = 3U / _S1232;
                    int3 _S1236 = int3(_S1234, int(_S1235), int(0));
                    int2 minimumTile_13 = int2(_S1231, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1236)).xy), uint(((_S1236)).z))).x * 255.0f))));
                    uint _S1237 = atlasWidth_13;
                    uint _S1238 = 4U % atlasWidth_13;
                    int _S1239 = int(_S1238);
                    uint _S1240 = 4U / _S1237;
                    int3 _S1241 = int3(_S1239, int(_S1240), int(0));
                    uint _S1242 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1241)).xy), uint(((_S1241)).z))).x * 255.0f));
                    uint _S1243 = atlasWidth_13;
                    uint _S1244 = 5U % atlasWidth_13;
                    int _S1245 = int(_S1244);
                    uint _S1246 = 5U / _S1243;
                    int3 _S1247 = int3(_S1245, int(_S1246), int(0));
                    uint _S1248 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1247)).xy), uint(((_S1247)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1242, _S1248);
                    int2 tile_26 = int2(floor(_S161)) - minimumTile_13;
                    if(any(tile_26 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_26 >= int2(grid_13));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1249 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1249)).xy), uint(((_S1249)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1242 + uint(tile_26.x));
                    uint _S1250 = atlasWidth_13;
                    uint _S1251 = offset_13 % atlasWidth_13;
                    int _S1252 = int(_S1251);
                    uint _S1253 = offset_13 / _S1250;
                    int3 _S1254 = int3(_S1252, int(_S1253), int(0));
                    uint _S1255 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1254)).xy), uint(((_S1254)).z))).x * 255.0f));
                    uint _S1256 = atlasWidth_13;
                    uint _S1257 = offset_13 + 1U;
                    uint _S1258 = _S1257 % atlasWidth_13;
                    int _S1259 = int(_S1258);
                    uint _S1260 = _S1257 / _S1256;
                    int3 _S1261 = int3(_S1259, int(_S1260), int(0));
                    uint slot_26 = _S1255 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1261)).xy), uint(((_S1261)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1262 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1262)).xy), uint(((_S1262)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1263 = atlasWidth_13;
                    uint _S1264 = 6U % atlasWidth_13;
                    int _S1265 = int(_S1264);
                    uint _S1266 = 6U / _S1263;
                    int3 _S1267 = int3(_S1265, int(_S1266), int(0));
                    uint _S1268 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1267)).xy), uint(((_S1267)).z))).x * 255.0f));
                    uint _S1269 = atlasWidth_13;
                    uint _S1270 = 7U % atlasWidth_13;
                    int _S1271 = int(_S1270);
                    uint _S1272 = 7U / _S1269;
                    int3 _S1273 = int3(_S1271, int(_S1272), int(0));
                    uint columns_13 = _S1268 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1273)).xy), uint(((_S1273)).z))).x * 255.0f)) << 8U);
                    uint _S1274 = atlasWidth_13;
                    uint _S1275 = 8U % atlasWidth_13;
                    int _S1276 = int(_S1275);
                    uint _S1277 = 8U / _S1274;
                    int3 _S1278 = int3(_S1276, int(_S1277), int(0));
                    uint _S1279 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1278)).xy), uint(((_S1278)).z))).x * 255.0f));
                    uint _S1280 = atlasWidth_13;
                    uint _S1281 = 9U % atlasWidth_13;
                    int _S1282 = int(_S1281);
                    uint _S1283 = 9U / _S1280;
                    int3 _S1284 = int3(_S1282, int(_S1283), int(0));
                    uint rows_13 = ((_S1279 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1284)).xy), uint(((_S1284)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1242 * _S1248 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1285 = atlasWidth_13 / columns_13;
                    float _S1286 = float(_S1285);
                    uint _S1287 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1286, float(_S1287));
                    uint _S1288 = slot_27 % columns_13;
                    float _S1289 = float(_S1288);
                    uint _S1290 = slot_27 / columns_13;
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1289, float(_S1290)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(_S161) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1291 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(_S161)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1291);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1292 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1292)).xy), uint(((_S1292)).z)));
                    break;
                }
                uint _S1293 = atlasWidth_13 / uint(_S1291.x);
                float _S1294 = float(_S1293);
                uint _S1295 = (atlasHeight_13 - 1U) / uint(_S1291.y);
                float2 cellSize_27 = float2(_S1294, float(_S1295));
                _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_6 = uint(round(_S1219.y));
            if(operation_6 == 1U)
            {
                _S163 = _S163 * _S348;
                break;
            }
            if(operation_6 == 2U)
            {
                _S163 = _S163 + _S348;
                break;
            }
            if(operation_6 == 3U)
            {
                _S163 = _S163 - _S348;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1219.z;
                _S163 = _S163 * float4((1.0f - factor_6))  + _S348 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S163.xyz);
    }
    else
    {
        specularColor_0 = _S139;
    }
    if((textureMask_0 & 512U) != 0U)
    {
        bool useSecondary_7 = (secondaryMask_0 & 512U) != 0U;
        if((alternateMask_0 & 512U) != 0U)
        {
            if(useSecondary_7)
            {
                _S161 = _S160;
            }
            else
            {
                _S161 = _S159;
            }
        }
        else
        {
            if(useSecondary_7)
            {
                _S161 = _S152;
            }
            else
            {
                _S161 = _S151;
            }
        }
        bool _S1296 = (udimMask_0 & 512U) != 0U;
        uint maskU_7 = uint(round(_S145.y));
        uint maskV_7 = uint(round(_S145.z));
        if((maskU_7 & 512U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_7 & 512U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1297 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1296)
            {
                for(;;)
                {
                    if(_S1297 == 0U)
                    {
                        _S162 = _S161;
                        break;
                    }
                    texture2d<float, access::sample> _S1298 = (&kernelContext_0)->clearcoatTexture_0;
                    thread uint width_18;
                    thread uint height_18;
                    (*((&width_18)) = (_S1298).get_width(0)),(*((&height_18)) = (_S1298).get_height(0));
                    float2 size_16 = float2(float(width_18), float(height_18));
                    float2 _S1299 = float2(0.5f) ;
                    float2 remapped_16 = clamp((_S161 * (size_16 - float2(2.0f) ) + float2(1.0f) ) / size_16, _S1299 / size_16, (size_16 - _S1299) / size_16);
                    if((_S1297 & 1U) != 0U)
                    {
                        clearcoatAmount_0 = remapped_16.x;
                    }
                    else
                    {
                        clearcoatAmount_0 = _S161.x;
                    }
                    if((_S1297 & 2U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_16.y;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S161.y;
                    }
                    _S162 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                    break;
                }
                _S163 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S162)));
                break;
            }
            texture2d<float, access::sample> _S1300 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1300).get_width(0)),(*((&atlasHeight_14)) = (_S1300).get_height(0));
            int3 _S1301 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1301)).xy), uint(((_S1301)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1302 = atlasWidth_14;
                uint _S1303 = 2U % atlasWidth_14;
                int _S1304 = int(_S1303);
                uint _S1305 = 2U / _S1302;
                int3 _S1306 = int3(_S1304, int(_S1305), int(0));
                int _S1307 = int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1306)).xy), uint(((_S1306)).z))).x * 255.0f)));
                uint _S1308 = atlasWidth_14;
                uint _S1309 = 3U % atlasWidth_14;
                int _S1310 = int(_S1309);
                uint _S1311 = 3U / _S1308;
                int3 _S1312 = int3(_S1310, int(_S1311), int(0));
                int2 minimumTile_14 = int2(_S1307, int(uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1312)).xy), uint(((_S1312)).z))).x * 255.0f))));
                uint _S1313 = atlasWidth_14;
                uint _S1314 = 4U % atlasWidth_14;
                int _S1315 = int(_S1314);
                uint _S1316 = 4U / _S1313;
                int3 _S1317 = int3(_S1315, int(_S1316), int(0));
                uint _S1318 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1317)).xy), uint(((_S1317)).z))).x * 255.0f));
                uint _S1319 = atlasWidth_14;
                uint _S1320 = 5U % atlasWidth_14;
                int _S1321 = int(_S1320);
                uint _S1322 = 5U / _S1319;
                int3 _S1323 = int3(_S1321, int(_S1322), int(0));
                uint _S1324 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1323)).xy), uint(((_S1323)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1318, _S1324);
                int2 tile_28 = int2(floor(_S161)) - minimumTile_14;
                if(any(tile_28 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_28 >= int2(grid_14));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1325 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1325)).xy), uint(((_S1325)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1318 + uint(tile_28.x));
                uint _S1326 = atlasWidth_14;
                uint _S1327 = offset_14 % atlasWidth_14;
                int _S1328 = int(_S1327);
                uint _S1329 = offset_14 / _S1326;
                int3 _S1330 = int3(_S1328, int(_S1329), int(0));
                uint _S1331 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1330)).xy), uint(((_S1330)).z))).x * 255.0f));
                uint _S1332 = atlasWidth_14;
                uint _S1333 = offset_14 + 1U;
                uint _S1334 = _S1333 % atlasWidth_14;
                int _S1335 = int(_S1334);
                uint _S1336 = _S1333 / _S1332;
                int3 _S1337 = int3(_S1335, int(_S1336), int(0));
                uint slot_28 = _S1331 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1337)).xy), uint(((_S1337)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1338 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1338)).xy), uint(((_S1338)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1339 = atlasWidth_14;
                uint _S1340 = 6U % atlasWidth_14;
                int _S1341 = int(_S1340);
                uint _S1342 = 6U / _S1339;
                int3 _S1343 = int3(_S1341, int(_S1342), int(0));
                uint _S1344 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1343)).xy), uint(((_S1343)).z))).x * 255.0f));
                uint _S1345 = atlasWidth_14;
                uint _S1346 = 7U % atlasWidth_14;
                int _S1347 = int(_S1346);
                uint _S1348 = 7U / _S1345;
                int3 _S1349 = int3(_S1347, int(_S1348), int(0));
                uint columns_14 = _S1344 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1349)).xy), uint(((_S1349)).z))).x * 255.0f)) << 8U);
                uint _S1350 = atlasWidth_14;
                uint _S1351 = 8U % atlasWidth_14;
                int _S1352 = int(_S1351);
                uint _S1353 = 8U / _S1350;
                int3 _S1354 = int3(_S1352, int(_S1353), int(0));
                uint _S1355 = uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1354)).xy), uint(((_S1354)).z))).x * 255.0f));
                uint _S1356 = atlasWidth_14;
                uint _S1357 = 9U % atlasWidth_14;
                int _S1358 = int(_S1357);
                uint _S1359 = 9U / _S1356;
                int3 _S1360 = int3(_S1358, int(_S1359), int(0));
                uint rows_14 = ((_S1355 | (uint(round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1360)).xy), uint(((_S1360)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1318 * _S1324 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1361 = atlasWidth_14 / columns_14;
                float _S1362 = float(_S1361);
                uint _S1363 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1362, float(_S1363));
                uint _S1364 = slot_29 % columns_14;
                float _S1365 = float(_S1364);
                uint _S1366 = slot_29 / columns_14;
                _S163 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(_S1365, float(_S1366)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(_S161) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1367 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(_S161)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1367);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1368 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S163 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S1368)).xy), uint(((_S1368)).z)));
                break;
            }
            uint _S1369 = atlasWidth_14 / uint(_S1367.x);
            float _S1370 = float(_S1369);
            uint _S1371 = (atlasHeight_14 - 1U) / uint(_S1367.y);
            float2 cellSize_29 = float2(_S1370, float(_S1371));
            _S163 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1372 = float4(_S7->compositeControls_0) ;
            if((_S1372.x) != 512.0f)
            {
                break;
            }
            bool _S1373 = (_S1372.w) >= 0.5f;
            if((maskU_7 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_7 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1374 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1373)
                {
                    for(;;)
                    {
                        if(_S1374 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1375 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_19;
                        thread uint height_19;
                        (*((&width_19)) = (_S1375).get_width(0)),(*((&height_19)) = (_S1375).get_height(0));
                        float2 size_17 = float2(float(width_19), float(height_19));
                        float2 _S1376 = float2(0.5f) ;
                        float2 remapped_17 = clamp((_S161 * (size_17 - float2(2.0f) ) + float2(1.0f) ) / size_17, _S1376 / size_17, (size_17 - _S1376) / size_17);
                        if((_S1374 & 1U) != 0U)
                        {
                            clearcoatAmount_0 = remapped_17.x;
                        }
                        else
                        {
                            clearcoatAmount_0 = _S161.x;
                        }
                        if((_S1374 & 2U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_17.y;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S161.y;
                        }
                        _S161 = float2(clearcoatAmount_0, clearcoatRoughness_0);
                        break;
                    }
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S161)));
                    break;
                }
                texture2d<float, access::sample> _S1377 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1377).get_width(0)),(*((&atlasHeight_15)) = (_S1377).get_height(0));
                int3 _S1378 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1378)).xy), uint(((_S1378)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1379 = atlasWidth_15;
                    uint _S1380 = 2U % atlasWidth_15;
                    int _S1381 = int(_S1380);
                    uint _S1382 = 2U / _S1379;
                    int3 _S1383 = int3(_S1381, int(_S1382), int(0));
                    int _S1384 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1383)).xy), uint(((_S1383)).z))).x * 255.0f)));
                    uint _S1385 = atlasWidth_15;
                    uint _S1386 = 3U % atlasWidth_15;
                    int _S1387 = int(_S1386);
                    uint _S1388 = 3U / _S1385;
                    int3 _S1389 = int3(_S1387, int(_S1388), int(0));
                    int2 minimumTile_15 = int2(_S1384, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1389)).xy), uint(((_S1389)).z))).x * 255.0f))));
                    uint _S1390 = atlasWidth_15;
                    uint _S1391 = 4U % atlasWidth_15;
                    int _S1392 = int(_S1391);
                    uint _S1393 = 4U / _S1390;
                    int3 _S1394 = int3(_S1392, int(_S1393), int(0));
                    uint _S1395 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1394)).xy), uint(((_S1394)).z))).x * 255.0f));
                    uint _S1396 = atlasWidth_15;
                    uint _S1397 = 5U % atlasWidth_15;
                    int _S1398 = int(_S1397);
                    uint _S1399 = 5U / _S1396;
                    int3 _S1400 = int3(_S1398, int(_S1399), int(0));
                    uint _S1401 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1400)).xy), uint(((_S1400)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1395, _S1401);
                    int2 tile_30 = int2(floor(_S161)) - minimumTile_15;
                    if(any(tile_30 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_30 >= int2(grid_15));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1402 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1402)).xy), uint(((_S1402)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1395 + uint(tile_30.x));
                    uint _S1403 = atlasWidth_15;
                    uint _S1404 = offset_15 % atlasWidth_15;
                    int _S1405 = int(_S1404);
                    uint _S1406 = offset_15 / _S1403;
                    int3 _S1407 = int3(_S1405, int(_S1406), int(0));
                    uint _S1408 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1407)).xy), uint(((_S1407)).z))).x * 255.0f));
                    uint _S1409 = atlasWidth_15;
                    uint _S1410 = offset_15 + 1U;
                    uint _S1411 = _S1410 % atlasWidth_15;
                    int _S1412 = int(_S1411);
                    uint _S1413 = _S1410 / _S1409;
                    int3 _S1414 = int3(_S1412, int(_S1413), int(0));
                    uint slot_30 = _S1408 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1414)).xy), uint(((_S1414)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1415 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1415)).xy), uint(((_S1415)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1416 = atlasWidth_15;
                    uint _S1417 = 6U % atlasWidth_15;
                    int _S1418 = int(_S1417);
                    uint _S1419 = 6U / _S1416;
                    int3 _S1420 = int3(_S1418, int(_S1419), int(0));
                    uint _S1421 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1420)).xy), uint(((_S1420)).z))).x * 255.0f));
                    uint _S1422 = atlasWidth_15;
                    uint _S1423 = 7U % atlasWidth_15;
                    int _S1424 = int(_S1423);
                    uint _S1425 = 7U / _S1422;
                    int3 _S1426 = int3(_S1424, int(_S1425), int(0));
                    uint columns_15 = _S1421 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1426)).xy), uint(((_S1426)).z))).x * 255.0f)) << 8U);
                    uint _S1427 = atlasWidth_15;
                    uint _S1428 = 8U % atlasWidth_15;
                    int _S1429 = int(_S1428);
                    uint _S1430 = 8U / _S1427;
                    int3 _S1431 = int3(_S1429, int(_S1430), int(0));
                    uint _S1432 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1431)).xy), uint(((_S1431)).z))).x * 255.0f));
                    uint _S1433 = atlasWidth_15;
                    uint _S1434 = 9U % atlasWidth_15;
                    int _S1435 = int(_S1434);
                    uint _S1436 = 9U / _S1433;
                    int3 _S1437 = int3(_S1435, int(_S1436), int(0));
                    uint rows_15 = ((_S1432 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1437)).xy), uint(((_S1437)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1395 * _S1401 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1438 = atlasWidth_15 / columns_15;
                    float _S1439 = float(_S1438);
                    uint _S1440 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1439, float(_S1440));
                    uint _S1441 = slot_31 % columns_15;
                    float _S1442 = float(_S1441);
                    uint _S1443 = slot_31 / columns_15;
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1442, float(_S1443)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(_S161) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1444 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(_S161)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1444);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1445 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1445)).xy), uint(((_S1445)).z)));
                    break;
                }
                uint _S1446 = atlasWidth_15 / uint(_S1444.x);
                float _S1447 = float(_S1446);
                uint _S1448 = (atlasHeight_15 - 1U) / uint(_S1444.y);
                float2 cellSize_31 = float2(_S1447, float(_S1448));
                _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_7 = uint(round(_S1372.y));
            if(operation_7 == 1U)
            {
                _S163 = _S163 * _S348;
                break;
            }
            if(operation_7 == 2U)
            {
                _S163 = _S163 + _S348;
                break;
            }
            if(operation_7 == 3U)
            {
                _S163 = _S163 - _S348;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1372.z;
                _S163 = _S163 * float4((1.0f - factor_7))  + _S348 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S163.x);
    }
    else
    {
        clearcoatAmount_0 = _S141;
    }
    float ior_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool useSecondary_8 = (secondaryMask_0 & 1024U) != 0U;
        if((alternateMask_0 & 1024U) != 0U)
        {
            if(useSecondary_8)
            {
                _S161 = _S160;
            }
            else
            {
                _S161 = _S159;
            }
        }
        else
        {
            if(useSecondary_8)
            {
                _S161 = _S152;
            }
            else
            {
                _S161 = _S151;
            }
        }
        bool _S1449 = (udimMask_0 & 1024U) != 0U;
        uint maskU_8 = uint(round(_S145.y));
        uint maskV_8 = uint(round(_S145.z));
        if((maskU_8 & 1024U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_8 & 1024U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1450 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1449)
            {
                for(;;)
                {
                    if(_S1450 == 0U)
                    {
                        _S162 = _S161;
                        break;
                    }
                    texture2d<float, access::sample> _S1451 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
                    thread uint width_20;
                    thread uint height_20;
                    (*((&width_20)) = (_S1451).get_width(0)),(*((&height_20)) = (_S1451).get_height(0));
                    float2 size_18 = float2(float(width_20), float(height_20));
                    float2 _S1452 = float2(0.5f) ;
                    float2 remapped_18 = clamp((_S161 * (size_18 - float2(2.0f) ) + float2(1.0f) ) / size_18, _S1452 / size_18, (size_18 - _S1452) / size_18);
                    if((_S1450 & 1U) != 0U)
                    {
                        clearcoatRoughness_0 = remapped_18.x;
                    }
                    else
                    {
                        clearcoatRoughness_0 = _S161.x;
                    }
                    if((_S1450 & 2U) != 0U)
                    {
                        ior_0 = remapped_18.y;
                    }
                    else
                    {
                        ior_0 = _S161.y;
                    }
                    _S162 = float2(clearcoatRoughness_0, ior_0);
                    break;
                }
                _S163 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S162)));
                break;
            }
            texture2d<float, access::sample> _S1453 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1453).get_width(0)),(*((&atlasHeight_16)) = (_S1453).get_height(0));
            int3 _S1454 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1454)).xy), uint(((_S1454)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1455 = atlasWidth_16;
                uint _S1456 = 2U % atlasWidth_16;
                int _S1457 = int(_S1456);
                uint _S1458 = 2U / _S1455;
                int3 _S1459 = int3(_S1457, int(_S1458), int(0));
                int _S1460 = int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1459)).xy), uint(((_S1459)).z))).x * 255.0f)));
                uint _S1461 = atlasWidth_16;
                uint _S1462 = 3U % atlasWidth_16;
                int _S1463 = int(_S1462);
                uint _S1464 = 3U / _S1461;
                int3 _S1465 = int3(_S1463, int(_S1464), int(0));
                int2 minimumTile_16 = int2(_S1460, int(uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1465)).xy), uint(((_S1465)).z))).x * 255.0f))));
                uint _S1466 = atlasWidth_16;
                uint _S1467 = 4U % atlasWidth_16;
                int _S1468 = int(_S1467);
                uint _S1469 = 4U / _S1466;
                int3 _S1470 = int3(_S1468, int(_S1469), int(0));
                uint _S1471 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1470)).xy), uint(((_S1470)).z))).x * 255.0f));
                uint _S1472 = atlasWidth_16;
                uint _S1473 = 5U % atlasWidth_16;
                int _S1474 = int(_S1473);
                uint _S1475 = 5U / _S1472;
                int3 _S1476 = int3(_S1474, int(_S1475), int(0));
                uint _S1477 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1476)).xy), uint(((_S1476)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1471, _S1477);
                int2 tile_32 = int2(floor(_S161)) - minimumTile_16;
                if(any(tile_32 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_32 >= int2(grid_16));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1478 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1478)).xy), uint(((_S1478)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1471 + uint(tile_32.x));
                uint _S1479 = atlasWidth_16;
                uint _S1480 = offset_16 % atlasWidth_16;
                int _S1481 = int(_S1480);
                uint _S1482 = offset_16 / _S1479;
                int3 _S1483 = int3(_S1481, int(_S1482), int(0));
                uint _S1484 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1483)).xy), uint(((_S1483)).z))).x * 255.0f));
                uint _S1485 = atlasWidth_16;
                uint _S1486 = offset_16 + 1U;
                uint _S1487 = _S1486 % atlasWidth_16;
                int _S1488 = int(_S1487);
                uint _S1489 = _S1486 / _S1485;
                int3 _S1490 = int3(_S1488, int(_S1489), int(0));
                uint slot_32 = _S1484 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1490)).xy), uint(((_S1490)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1491 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1491)).xy), uint(((_S1491)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1492 = atlasWidth_16;
                uint _S1493 = 6U % atlasWidth_16;
                int _S1494 = int(_S1493);
                uint _S1495 = 6U / _S1492;
                int3 _S1496 = int3(_S1494, int(_S1495), int(0));
                uint _S1497 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1496)).xy), uint(((_S1496)).z))).x * 255.0f));
                uint _S1498 = atlasWidth_16;
                uint _S1499 = 7U % atlasWidth_16;
                int _S1500 = int(_S1499);
                uint _S1501 = 7U / _S1498;
                int3 _S1502 = int3(_S1500, int(_S1501), int(0));
                uint columns_16 = _S1497 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1502)).xy), uint(((_S1502)).z))).x * 255.0f)) << 8U);
                uint _S1503 = atlasWidth_16;
                uint _S1504 = 8U % atlasWidth_16;
                int _S1505 = int(_S1504);
                uint _S1506 = 8U / _S1503;
                int3 _S1507 = int3(_S1505, int(_S1506), int(0));
                uint _S1508 = uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1507)).xy), uint(((_S1507)).z))).x * 255.0f));
                uint _S1509 = atlasWidth_16;
                uint _S1510 = 9U % atlasWidth_16;
                int _S1511 = int(_S1510);
                uint _S1512 = 9U / _S1509;
                int3 _S1513 = int3(_S1511, int(_S1512), int(0));
                uint rows_16 = ((_S1508 | (uint(round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1513)).xy), uint(((_S1513)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1471 * _S1477 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1514 = atlasWidth_16 / columns_16;
                float _S1515 = float(_S1514);
                uint _S1516 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1515, float(_S1516));
                uint _S1517 = slot_33 % columns_16;
                float _S1518 = float(_S1517);
                uint _S1519 = slot_33 / columns_16;
                _S163 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(_S1518, float(_S1519)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(_S161) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1520 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(_S161)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1520);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1521 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S163 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1521)).xy), uint(((_S1521)).z)));
                break;
            }
            uint _S1522 = atlasWidth_16 / uint(_S1520.x);
            float _S1523 = float(_S1522);
            uint _S1524 = (atlasHeight_16 - 1U) / uint(_S1520.y);
            float2 cellSize_33 = float2(_S1523, float(_S1524));
            _S163 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1525 = float4(_S7->compositeControls_0) ;
            if((_S1525.x) != 1024.0f)
            {
                break;
            }
            bool _S1526 = (_S1525.w) >= 0.5f;
            if((maskU_8 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_8 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1527 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1526)
                {
                    for(;;)
                    {
                        if(_S1527 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1528 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_21;
                        thread uint height_21;
                        (*((&width_21)) = (_S1528).get_width(0)),(*((&height_21)) = (_S1528).get_height(0));
                        float2 size_19 = float2(float(width_21), float(height_21));
                        float2 _S1529 = float2(0.5f) ;
                        float2 remapped_19 = clamp((_S161 * (size_19 - float2(2.0f) ) + float2(1.0f) ) / size_19, _S1529 / size_19, (size_19 - _S1529) / size_19);
                        if((_S1527 & 1U) != 0U)
                        {
                            clearcoatRoughness_0 = remapped_19.x;
                        }
                        else
                        {
                            clearcoatRoughness_0 = _S161.x;
                        }
                        if((_S1527 & 2U) != 0U)
                        {
                            ior_0 = remapped_19.y;
                        }
                        else
                        {
                            ior_0 = _S161.y;
                        }
                        _S161 = float2(clearcoatRoughness_0, ior_0);
                        break;
                    }
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S161)));
                    break;
                }
                texture2d<float, access::sample> _S1530 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1530).get_width(0)),(*((&atlasHeight_17)) = (_S1530).get_height(0));
                int3 _S1531 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1531)).xy), uint(((_S1531)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1532 = atlasWidth_17;
                    uint _S1533 = 2U % atlasWidth_17;
                    int _S1534 = int(_S1533);
                    uint _S1535 = 2U / _S1532;
                    int3 _S1536 = int3(_S1534, int(_S1535), int(0));
                    int _S1537 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1536)).xy), uint(((_S1536)).z))).x * 255.0f)));
                    uint _S1538 = atlasWidth_17;
                    uint _S1539 = 3U % atlasWidth_17;
                    int _S1540 = int(_S1539);
                    uint _S1541 = 3U / _S1538;
                    int3 _S1542 = int3(_S1540, int(_S1541), int(0));
                    int2 minimumTile_17 = int2(_S1537, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1542)).xy), uint(((_S1542)).z))).x * 255.0f))));
                    uint _S1543 = atlasWidth_17;
                    uint _S1544 = 4U % atlasWidth_17;
                    int _S1545 = int(_S1544);
                    uint _S1546 = 4U / _S1543;
                    int3 _S1547 = int3(_S1545, int(_S1546), int(0));
                    uint _S1548 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1547)).xy), uint(((_S1547)).z))).x * 255.0f));
                    uint _S1549 = atlasWidth_17;
                    uint _S1550 = 5U % atlasWidth_17;
                    int _S1551 = int(_S1550);
                    uint _S1552 = 5U / _S1549;
                    int3 _S1553 = int3(_S1551, int(_S1552), int(0));
                    uint _S1554 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1553)).xy), uint(((_S1553)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1548, _S1554);
                    int2 tile_34 = int2(floor(_S161)) - minimumTile_17;
                    if(any(tile_34 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_34 >= int2(grid_17));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1555 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1555)).xy), uint(((_S1555)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1548 + uint(tile_34.x));
                    uint _S1556 = atlasWidth_17;
                    uint _S1557 = offset_17 % atlasWidth_17;
                    int _S1558 = int(_S1557);
                    uint _S1559 = offset_17 / _S1556;
                    int3 _S1560 = int3(_S1558, int(_S1559), int(0));
                    uint _S1561 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1560)).xy), uint(((_S1560)).z))).x * 255.0f));
                    uint _S1562 = atlasWidth_17;
                    uint _S1563 = offset_17 + 1U;
                    uint _S1564 = _S1563 % atlasWidth_17;
                    int _S1565 = int(_S1564);
                    uint _S1566 = _S1563 / _S1562;
                    int3 _S1567 = int3(_S1565, int(_S1566), int(0));
                    uint slot_34 = _S1561 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1567)).xy), uint(((_S1567)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1568 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1568)).xy), uint(((_S1568)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1569 = atlasWidth_17;
                    uint _S1570 = 6U % atlasWidth_17;
                    int _S1571 = int(_S1570);
                    uint _S1572 = 6U / _S1569;
                    int3 _S1573 = int3(_S1571, int(_S1572), int(0));
                    uint _S1574 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1573)).xy), uint(((_S1573)).z))).x * 255.0f));
                    uint _S1575 = atlasWidth_17;
                    uint _S1576 = 7U % atlasWidth_17;
                    int _S1577 = int(_S1576);
                    uint _S1578 = 7U / _S1575;
                    int3 _S1579 = int3(_S1577, int(_S1578), int(0));
                    uint columns_17 = _S1574 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1579)).xy), uint(((_S1579)).z))).x * 255.0f)) << 8U);
                    uint _S1580 = atlasWidth_17;
                    uint _S1581 = 8U % atlasWidth_17;
                    int _S1582 = int(_S1581);
                    uint _S1583 = 8U / _S1580;
                    int3 _S1584 = int3(_S1582, int(_S1583), int(0));
                    uint _S1585 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1584)).xy), uint(((_S1584)).z))).x * 255.0f));
                    uint _S1586 = atlasWidth_17;
                    uint _S1587 = 9U % atlasWidth_17;
                    int _S1588 = int(_S1587);
                    uint _S1589 = 9U / _S1586;
                    int3 _S1590 = int3(_S1588, int(_S1589), int(0));
                    uint rows_17 = ((_S1585 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1590)).xy), uint(((_S1590)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1548 * _S1554 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1591 = atlasWidth_17 / columns_17;
                    float _S1592 = float(_S1591);
                    uint _S1593 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1592, float(_S1593));
                    uint _S1594 = slot_35 % columns_17;
                    float _S1595 = float(_S1594);
                    uint _S1596 = slot_35 / columns_17;
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1595, float(_S1596)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(_S161) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1597 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(_S161)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1597);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1598 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1598)).xy), uint(((_S1598)).z)));
                    break;
                }
                uint _S1599 = atlasWidth_17 / uint(_S1597.x);
                float _S1600 = float(_S1599);
                uint _S1601 = (atlasHeight_17 - 1U) / uint(_S1597.y);
                float2 cellSize_35 = float2(_S1600, float(_S1601));
                _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_8 = uint(round(_S1525.y));
            if(operation_8 == 1U)
            {
                _S163 = _S163 * _S348;
                break;
            }
            if(operation_8 == 2U)
            {
                _S163 = _S163 + _S348;
                break;
            }
            if(operation_8 == 3U)
            {
                _S163 = _S163 - _S348;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1525.z;
                _S163 = _S163 * float4((1.0f - factor_8))  + _S348 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S163.x);
    }
    else
    {
        clearcoatRoughness_0 = _S142;
    }
    float lightType_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool useSecondary_9 = (secondaryMask_0 & 2048U) != 0U;
        if((alternateMask_0 & 2048U) != 0U)
        {
            if(useSecondary_9)
            {
                _S161 = _S160;
            }
            else
            {
                _S161 = _S159;
            }
        }
        else
        {
            if(useSecondary_9)
            {
                _S161 = _S152;
            }
            else
            {
                _S161 = _S151;
            }
        }
        bool _S1602 = (udimMask_0 & 2048U) != 0U;
        uint maskU_9 = uint(round(_S145.y));
        uint maskV_9 = uint(round(_S145.z));
        if((maskU_9 & 2048U) != 0U)
        {
            index_0 = 1U;
        }
        else
        {
            index_0 = 0U;
        }
        if((maskV_9 & 2048U) != 0U)
        {
            lightCount_0 = 2U;
        }
        else
        {
            lightCount_0 = 0U;
        }
        uint _S1603 = index_0 | lightCount_0;
        for(;;)
        {
            if(!_S1602)
            {
                for(;;)
                {
                    if(_S1603 == 0U)
                    {
                        _S162 = _S161;
                        break;
                    }
                    texture2d<float, access::sample> _S1604 = (&kernelContext_0)->iorTexture_0;
                    thread uint width_22;
                    thread uint height_22;
                    (*((&width_22)) = (_S1604).get_width(0)),(*((&height_22)) = (_S1604).get_height(0));
                    float2 size_20 = float2(float(width_22), float(height_22));
                    float2 _S1605 = float2(0.5f) ;
                    float2 remapped_20 = clamp((_S161 * (size_20 - float2(2.0f) ) + float2(1.0f) ) / size_20, _S1605 / size_20, (size_20 - _S1605) / size_20);
                    if((_S1603 & 1U) != 0U)
                    {
                        ior_0 = remapped_20.x;
                    }
                    else
                    {
                        ior_0 = _S161.x;
                    }
                    if((_S1603 & 2U) != 0U)
                    {
                        lightType_0 = remapped_20.y;
                    }
                    else
                    {
                        lightType_0 = _S161.y;
                    }
                    _S162 = float2(ior_0, lightType_0);
                    break;
                }
                _S163 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S162)));
                break;
            }
            texture2d<float, access::sample> _S1606 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1606).get_width(0)),(*((&atlasHeight_18)) = (_S1606).get_height(0));
            int3 _S1607 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1607)).xy), uint(((_S1607)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1608 = atlasWidth_18;
                uint _S1609 = 2U % atlasWidth_18;
                int _S1610 = int(_S1609);
                uint _S1611 = 2U / _S1608;
                int3 _S1612 = int3(_S1610, int(_S1611), int(0));
                int _S1613 = int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1612)).xy), uint(((_S1612)).z))).x * 255.0f)));
                uint _S1614 = atlasWidth_18;
                uint _S1615 = 3U % atlasWidth_18;
                int _S1616 = int(_S1615);
                uint _S1617 = 3U / _S1614;
                int3 _S1618 = int3(_S1616, int(_S1617), int(0));
                int2 minimumTile_18 = int2(_S1613, int(uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1618)).xy), uint(((_S1618)).z))).x * 255.0f))));
                uint _S1619 = atlasWidth_18;
                uint _S1620 = 4U % atlasWidth_18;
                int _S1621 = int(_S1620);
                uint _S1622 = 4U / _S1619;
                int3 _S1623 = int3(_S1621, int(_S1622), int(0));
                uint _S1624 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1623)).xy), uint(((_S1623)).z))).x * 255.0f));
                uint _S1625 = atlasWidth_18;
                uint _S1626 = 5U % atlasWidth_18;
                int _S1627 = int(_S1626);
                uint _S1628 = 5U / _S1625;
                int3 _S1629 = int3(_S1627, int(_S1628), int(0));
                uint _S1630 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1629)).xy), uint(((_S1629)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1624, _S1630);
                int2 tile_36 = int2(floor(_S161)) - minimumTile_18;
                if(any(tile_36 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_36 >= int2(grid_18));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1631 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1631)).xy), uint(((_S1631)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1624 + uint(tile_36.x));
                uint _S1632 = atlasWidth_18;
                uint _S1633 = offset_18 % atlasWidth_18;
                int _S1634 = int(_S1633);
                uint _S1635 = offset_18 / _S1632;
                int3 _S1636 = int3(_S1634, int(_S1635), int(0));
                uint _S1637 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1636)).xy), uint(((_S1636)).z))).x * 255.0f));
                uint _S1638 = atlasWidth_18;
                uint _S1639 = offset_18 + 1U;
                uint _S1640 = _S1639 % atlasWidth_18;
                int _S1641 = int(_S1640);
                uint _S1642 = _S1639 / _S1638;
                int3 _S1643 = int3(_S1641, int(_S1642), int(0));
                uint slot_36 = _S1637 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1643)).xy), uint(((_S1643)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1644 = int3(int(1), int(0), int(0));
                    _S163 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1644)).xy), uint(((_S1644)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1645 = atlasWidth_18;
                uint _S1646 = 6U % atlasWidth_18;
                int _S1647 = int(_S1646);
                uint _S1648 = 6U / _S1645;
                int3 _S1649 = int3(_S1647, int(_S1648), int(0));
                uint _S1650 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1649)).xy), uint(((_S1649)).z))).x * 255.0f));
                uint _S1651 = atlasWidth_18;
                uint _S1652 = 7U % atlasWidth_18;
                int _S1653 = int(_S1652);
                uint _S1654 = 7U / _S1651;
                int3 _S1655 = int3(_S1653, int(_S1654), int(0));
                uint columns_18 = _S1650 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1655)).xy), uint(((_S1655)).z))).x * 255.0f)) << 8U);
                uint _S1656 = atlasWidth_18;
                uint _S1657 = 8U % atlasWidth_18;
                int _S1658 = int(_S1657);
                uint _S1659 = 8U / _S1656;
                int3 _S1660 = int3(_S1658, int(_S1659), int(0));
                uint _S1661 = uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1660)).xy), uint(((_S1660)).z))).x * 255.0f));
                uint _S1662 = atlasWidth_18;
                uint _S1663 = 9U % atlasWidth_18;
                int _S1664 = int(_S1663);
                uint _S1665 = 9U / _S1662;
                int3 _S1666 = int3(_S1664, int(_S1665), int(0));
                uint rows_18 = ((_S1661 | (uint(round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1666)).xy), uint(((_S1666)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1624 * _S1630 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1667 = atlasWidth_18 / columns_18;
                float _S1668 = float(_S1667);
                uint _S1669 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1668, float(_S1669));
                uint _S1670 = slot_37 % columns_18;
                float _S1671 = float(_S1670);
                uint _S1672 = slot_37 / columns_18;
                _S163 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(_S1671, float(_S1672)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(_S161) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1673 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(_S161)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1673);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1674 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S163 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S1674)).xy), uint(((_S1674)).z)));
                break;
            }
            uint _S1675 = atlasWidth_18 / uint(_S1673.x);
            float _S1676 = float(_S1675);
            uint _S1677 = (atlasHeight_18 - 1U) / uint(_S1673.y);
            float2 cellSize_37 = float2(_S1676, float(_S1677));
            _S163 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1678 = float4(_S7->compositeControls_0) ;
            if((_S1678.x) != 2048.0f)
            {
                break;
            }
            bool _S1679 = (_S1678.w) >= 0.5f;
            if((maskU_9 & 1U) != 0U)
            {
                index_0 = 1U;
            }
            else
            {
                index_0 = 0U;
            }
            if((maskV_9 & 1U) != 0U)
            {
                lightCount_0 = 2U;
            }
            else
            {
                lightCount_0 = 0U;
            }
            uint _S1680 = index_0 | lightCount_0;
            for(;;)
            {
                if(!_S1679)
                {
                    for(;;)
                    {
                        if(_S1680 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S1681 = (&kernelContext_0)->compositeTexture_0;
                        thread uint width_23;
                        thread uint height_23;
                        (*((&width_23)) = (_S1681).get_width(0)),(*((&height_23)) = (_S1681).get_height(0));
                        float2 size_21 = float2(float(width_23), float(height_23));
                        float2 _S1682 = float2(0.5f) ;
                        float2 remapped_21 = clamp((_S161 * (size_21 - float2(2.0f) ) + float2(1.0f) ) / size_21, _S1682 / size_21, (size_21 - _S1682) / size_21);
                        if((_S1680 & 1U) != 0U)
                        {
                            ior_0 = remapped_21.x;
                        }
                        else
                        {
                            ior_0 = _S161.x;
                        }
                        if((_S1680 & 2U) != 0U)
                        {
                            lightType_0 = remapped_21.y;
                        }
                        else
                        {
                            lightType_0 = _S161.y;
                        }
                        _S161 = float2(ior_0, lightType_0);
                        break;
                    }
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S161)));
                    break;
                }
                texture2d<float, access::sample> _S1683 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1683).get_width(0)),(*((&atlasHeight_19)) = (_S1683).get_height(0));
                int3 _S1684 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1684)).xy), uint(((_S1684)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1685 = atlasWidth_19;
                    uint _S1686 = 2U % atlasWidth_19;
                    int _S1687 = int(_S1686);
                    uint _S1688 = 2U / _S1685;
                    int3 _S1689 = int3(_S1687, int(_S1688), int(0));
                    int _S1690 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1689)).xy), uint(((_S1689)).z))).x * 255.0f)));
                    uint _S1691 = atlasWidth_19;
                    uint _S1692 = 3U % atlasWidth_19;
                    int _S1693 = int(_S1692);
                    uint _S1694 = 3U / _S1691;
                    int3 _S1695 = int3(_S1693, int(_S1694), int(0));
                    int2 minimumTile_19 = int2(_S1690, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1695)).xy), uint(((_S1695)).z))).x * 255.0f))));
                    uint _S1696 = atlasWidth_19;
                    uint _S1697 = 4U % atlasWidth_19;
                    int _S1698 = int(_S1697);
                    uint _S1699 = 4U / _S1696;
                    int3 _S1700 = int3(_S1698, int(_S1699), int(0));
                    uint _S1701 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1700)).xy), uint(((_S1700)).z))).x * 255.0f));
                    uint _S1702 = atlasWidth_19;
                    uint _S1703 = 5U % atlasWidth_19;
                    int _S1704 = int(_S1703);
                    uint _S1705 = 5U / _S1702;
                    int3 _S1706 = int3(_S1704, int(_S1705), int(0));
                    uint _S1707 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1706)).xy), uint(((_S1706)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1701, _S1707);
                    int2 tile_38 = int2(floor(_S161)) - minimumTile_19;
                    if(any(tile_38 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_38 >= int2(grid_19));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1708 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1708)).xy), uint(((_S1708)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1701 + uint(tile_38.x));
                    uint _S1709 = atlasWidth_19;
                    uint _S1710 = offset_19 % atlasWidth_19;
                    int _S1711 = int(_S1710);
                    uint _S1712 = offset_19 / _S1709;
                    int3 _S1713 = int3(_S1711, int(_S1712), int(0));
                    uint _S1714 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1713)).xy), uint(((_S1713)).z))).x * 255.0f));
                    uint _S1715 = atlasWidth_19;
                    uint _S1716 = offset_19 + 1U;
                    uint _S1717 = _S1716 % atlasWidth_19;
                    int _S1718 = int(_S1717);
                    uint _S1719 = _S1716 / _S1715;
                    int3 _S1720 = int3(_S1718, int(_S1719), int(0));
                    uint slot_38 = _S1714 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1720)).xy), uint(((_S1720)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1721 = int3(int(1), int(0), int(0));
                        _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1721)).xy), uint(((_S1721)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1722 = atlasWidth_19;
                    uint _S1723 = 6U % atlasWidth_19;
                    int _S1724 = int(_S1723);
                    uint _S1725 = 6U / _S1722;
                    int3 _S1726 = int3(_S1724, int(_S1725), int(0));
                    uint _S1727 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1726)).xy), uint(((_S1726)).z))).x * 255.0f));
                    uint _S1728 = atlasWidth_19;
                    uint _S1729 = 7U % atlasWidth_19;
                    int _S1730 = int(_S1729);
                    uint _S1731 = 7U / _S1728;
                    int3 _S1732 = int3(_S1730, int(_S1731), int(0));
                    uint columns_19 = _S1727 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1732)).xy), uint(((_S1732)).z))).x * 255.0f)) << 8U);
                    uint _S1733 = atlasWidth_19;
                    uint _S1734 = 8U % atlasWidth_19;
                    int _S1735 = int(_S1734);
                    uint _S1736 = 8U / _S1733;
                    int3 _S1737 = int3(_S1735, int(_S1736), int(0));
                    uint _S1738 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1737)).xy), uint(((_S1737)).z))).x * 255.0f));
                    uint _S1739 = atlasWidth_19;
                    uint _S1740 = 9U % atlasWidth_19;
                    int _S1741 = int(_S1740);
                    uint _S1742 = 9U / _S1739;
                    int3 _S1743 = int3(_S1741, int(_S1742), int(0));
                    uint rows_19 = ((_S1738 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1743)).xy), uint(((_S1743)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1701 * _S1707 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1744 = atlasWidth_19 / columns_19;
                    float _S1745 = float(_S1744);
                    uint _S1746 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1745, float(_S1746));
                    uint _S1747 = slot_39 % columns_19;
                    float _S1748 = float(_S1747);
                    uint _S1749 = slot_39 / columns_19;
                    _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S1748, float(_S1749)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(_S161) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1750 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(_S161)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1750);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1751 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S348 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S1751)).xy), uint(((_S1751)).z)));
                    break;
                }
                uint _S1752 = atlasWidth_19 / uint(_S1750.x);
                float _S1753 = float(_S1752);
                uint _S1754 = (atlasHeight_19 - 1U) / uint(_S1750.y);
                float2 cellSize_39 = float2(_S1753, float(_S1754));
                _S348 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(_S161) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_9 = uint(round(_S1678.y));
            if(operation_9 == 1U)
            {
                _S163 = _S163 * _S348;
                break;
            }
            if(operation_9 == 2U)
            {
                _S163 = _S163 + _S348;
                break;
            }
            if(operation_9 == 3U)
            {
                _S163 = _S163 - _S348;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1678.z;
                _S163 = _S163 * float4((1.0f - factor_9))  + _S348 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S163.x;
    }
    else
    {
        ior_0 = _S140;
    }
    float opacityThreshold_0 = _S135.z;
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
    float3 diffuseFresnel_0;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        diffuseFresnel_0 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        diffuseFresnel_0 = float3(0.0f, 0.0f, 1.0f);
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
    float _S1755 = max(0.00100000004749745f, roughness_0);
    float _S1756 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1757 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1757;
    bool omniPbrSpecular_0 = (_S143.w) >= 0.5f;
    float dielectricLevel_0 = _S129.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1758 = float3(dielectricLevel_0) ;
        float3 _S1759 = float3(metallic_0) ;
        float3 _S1760 = mix(_S1758, diffuseColor_0, _S1759);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1758, diffuseColor_0, _S1759);
        grazingIncidence_0 = _S1760;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S135.w) >= 0.5f)
        {
            float3 _S1761 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1761;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1762 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1762);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1762);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1763 = _S2;
    float _S1764 = _S2.ambientLight_0.w;
    if(_S1764 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1765 = _S1763.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1765, _S1765)) > 0.0f;
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
    uint _S1766 = min(uint(_S1764), 128U);
    float3 sceneEye_0 = normalize((((float4(diffuseFresnel_0, 0.0f)) * (_S2.eyeToWorld_0))).xyz);
    float3 worldPosition_0 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S2.eyeToWorld_0))).xyz;
    float3 worldGeometricNormal_0 = cross(dfdx(worldPosition_0), dfdy(worldPosition_0));
    float worldNormalLengthSquared_0 = dot(worldGeometricNormal_0, worldGeometricNormal_0);
    if(worldNormalLengthSquared_0 > 9.99999968265522539e-21f)
    {
        specularColor_0 = worldGeometricNormal_0 * float3(rsqrt(worldNormalLengthSquared_0)) ;
    }
    else
    {
        specularColor_0 = float3(0.0f, 0.0f, 1.0f);
    }
    float4 _S1767 = float4(_S7->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S1767.w) ;
    uint domeLinkMask_0 = uint(max(_S145.x, 0.0f));
    FragmentFrameParameters_0 _S1768 = _S2;
    uint _S1769 = min(uint(max(_S2.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S1769) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S1770 = _S1763.ambientLight_0.xyz;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S1771 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S1771;
        for(;;)
        {
            if(lightCount_0 < _S1769)
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
        domeAmbient_1 = _S1770;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S1772 = !hasSceneLighting_0;
    if(_S1772)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1766;
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
        bool _S1773;
        if(hasSceneLighting_0)
        {
            _S1773 = (((uint4(_S7->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S1773 = false;
        }
        if(_S1773)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S1774 = lightIndex_0 == 0U;
        bool _S1775;
        if(_S1774)
        {
            _S1775 = _S1772;
        }
        else
        {
            _S1775 = false;
        }
        if(_S1775)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            uint _S1776 = 224U + lightIndex_0 * 16U;
            float _S1777 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1776)>>2]);
            float _S1778 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1776 + 4U)>>2]);
            float _S1779 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1776 + 8U)>>2]);
            float _S1780 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1776 + 12U)>>2]);
            lightType_0 = _S1780;
        }
        bool _S1781;
        if(_S1774)
        {
            _S1781 = _S1772;
        }
        else
        {
            _S1781 = false;
        }
        if(_S1781)
        {
            lightDirection_0 = normalize((float4(_S7->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            uint _S1782 = 2272U + lightIndex_0 * 16U;
            float _S1783 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1782)>>2]);
            float _S1784 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1782 + 4U)>>2]);
            float _S1785 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1782 + 8U)>>2]);
            float _S1786 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1782 + 12U)>>2]);
            lightDirection_0 = normalize(float3(_S1783, _S1784, _S1785));
        }
        bool _S1787;
        if(_S1774)
        {
            _S1787 = _S1772;
        }
        else
        {
            _S1787 = false;
        }
        if(_S1787)
        {
            roughness_0 = (float4(_S7->lightDirectionIntensity_0) ).w;
        }
        else
        {
            uint _S1788 = 4320U + lightIndex_0 * 16U;
            float _S1789 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1788)>>2]);
            float _S1790 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1788 + 4U)>>2]);
            float _S1791 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1788 + 8U)>>2]);
            float _S1792 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1788 + 12U)>>2]);
            roughness_0 = _S1792;
        }
        bool _S1793;
        if(_S1774)
        {
            _S1793 = _S1772;
        }
        else
        {
            _S1793 = false;
        }
        if(_S1793)
        {
            diffuseColor_0 = _S1767.xyz;
        }
        else
        {
            uint _S1794 = 4320U + lightIndex_0 * 16U;
            float _S1795 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1794)>>2]);
            float _S1796 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1794 + 4U)>>2]);
            float _S1797 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1794 + 8U)>>2]);
            float _S1798 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1794 + 12U)>>2]);
            diffuseColor_0 = float3(_S1795, _S1796, _S1797);
        }
        bool _S1799;
        if(_S1774)
        {
            _S1799 = _S1772;
        }
        else
        {
            _S1799 = false;
        }
        if(_S1799)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            uint _S1800 = 6368U + lightIndex_0 * 16U;
            float _S1801 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1800)>>2]);
            float _S1802 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1800 + 4U)>>2]);
            float _S1803 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1800 + 8U)>>2]);
            float _S1804 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1800 + 12U)>>2]);
            metallic_0 = _S1801;
        }
        bool _S1805;
        if(_S1774)
        {
            _S1805 = _S1772;
        }
        else
        {
            _S1805 = false;
        }
        if(_S1805)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            uint _S1806 = 6368U + lightIndex_0 * 16U;
            float _S1807 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1806)>>2]);
            float _S1808 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1806 + 4U)>>2]);
            float _S1809 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1806 + 8U)>>2]);
            float _S1810 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1806 + 12U)>>2]);
            clearcoatRoughness_0 = _S1808;
        }
        bool _S1811;
        if(_S1774)
        {
            _S1811 = _S1772;
        }
        else
        {
            _S1811 = false;
        }
        if(_S1811)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1812 = 8416U + lightIndex_0 * 16U;
            float _S1813 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1812)>>2]);
            float _S1814 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1812 + 4U)>>2]);
            float _S1815 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1812 + 8U)>>2]);
            float _S1816 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1812 + 12U)>>2]);
            lightTangent_0 = normalize(float3(_S1813, _S1814, _S1815));
        }
        bool _S1817;
        if(_S1774)
        {
            _S1817 = _S1772;
        }
        else
        {
            _S1817 = false;
        }
        if(_S1817)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            uint _S1818 = 10464U + lightIndex_0 * 16U;
            float _S1819 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1818)>>2]);
            float _S1820 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1818 + 4U)>>2]);
            float _S1821 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1818 + 8U)>>2]);
            float _S1822 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1818 + 12U)>>2]);
            lightBitangent_0 = normalize(float3(_S1819, _S1820, _S1821));
        }
        bool _S1823;
        if(_S1774)
        {
            _S1823 = _S1772;
        }
        else
        {
            _S1823 = false;
        }
        float shapeX_0;
        if(_S1823)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            uint _S1824 = 8416U + lightIndex_0 * 16U;
            float _S1825 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1824)>>2]);
            float _S1826 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1824 + 4U)>>2]);
            float _S1827 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1824 + 8U)>>2]);
            float _S1828 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1824 + 12U)>>2]);
            shapeX_0 = _S1828;
        }
        bool _S1829;
        if(_S1774)
        {
            _S1829 = _S1772;
        }
        else
        {
            _S1829 = false;
        }
        float shapeY_0;
        if(_S1829)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            uint _S1830 = 10464U + lightIndex_0 * 16U;
            float _S1831 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1830)>>2]);
            float _S1832 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1830 + 4U)>>2]);
            float _S1833 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1830 + 8U)>>2]);
            float _S1834 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1830 + 12U)>>2]);
            shapeY_0 = _S1834;
        }
        bool _S1835;
        if(_S1774)
        {
            _S1835 = _S1772;
        }
        else
        {
            _S1835 = false;
        }
        float lightRadius_0;
        if(_S1835)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            uint _S1836 = 2272U + lightIndex_0 * 16U;
            float _S1837 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1836)>>2]);
            float _S1838 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1836 + 4U)>>2]);
            float _S1839 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1836 + 8U)>>2]);
            float _S1840 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1836 + 12U)>>2]);
            lightRadius_0 = _S1840;
        }
        bool _S1841;
        if(_S1774)
        {
            _S1841 = _S1772;
        }
        else
        {
            _S1841 = false;
        }
        float3 lightEyeBase_0;
        if(_S1841)
        {
            lightEyeBase_0 = diffuseFresnel_0;
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
        float _S1842 = saturate(abs(dot(directNormal_0, lightEyeBase_0)) + 0.00000999999974738f);
        bool _S1843;
        if(_S1774)
        {
            _S1843 = _S1772;
        }
        else
        {
            _S1843 = false;
        }
        if(_S1843)
        {
            _S163 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
        else
        {
            uint _S1844 = 17216U + lightIndex_0 * 16U;
            float _S1845 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1844)>>2]);
            float _S1846 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1844 + 4U)>>2]);
            float _S1847 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1844 + 8U)>>2]);
            float _S1848 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1844 + 12U)>>2]);
            _S163 = float4(_S1845, _S1846, _S1847, _S1848);
        }
        float3 color_3;
        float3 sampleDirection_0;
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            uint _S1849 = lightIndex_0 * 16U;
            uint _S1850 = 14880U + _S1849;
            float _S1851 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1850)>>2]);
            float _S1852 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1850 + 4U)>>2]);
            float _S1853 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1850 + 8U)>>2]);
            float _S1854 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1850 + 12U)>>2]);
            int shadowSlot_0 = int(_S1851);
            if(shadowSlot_0 >= int(0))
            {
                int shadowSlot_1;
                int y_0;
                if(_S1852 == 6.0f)
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
                        int _S1855 = shadowSlot_0 + y_0;
                        uint _S1856 = 12576U + uint(_S1855) * 64U;
                        float _S1857 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856)>>2]);
                        float _S1858 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 4U)>>2]);
                        float _S1859 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 8U)>>2]);
                        float _S1860 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 12U)>>2]);
                        float4 _S1861 = float4(_S1857, _S1858, _S1859, _S1860);
                        float _S1862 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 16U)>>2]);
                        float _S1863 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 20U)>>2]);
                        float _S1864 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 24U)>>2]);
                        float _S1865 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 28U)>>2]);
                        float4 _S1866 = float4(_S1862, _S1863, _S1864, _S1865);
                        float _S1867 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 32U)>>2]);
                        float _S1868 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 36U)>>2]);
                        float _S1869 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 40U)>>2]);
                        float _S1870 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 44U)>>2]);
                        float4 _S1871 = float4(_S1867, _S1868, _S1869, _S1870);
                        float _S1872 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 48U)>>2]);
                        float _S1873 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 52U)>>2]);
                        float _S1874 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 56U)>>2]);
                        float _S1875 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1856 + 60U)>>2]);
                        float _S1876 = (((float4(worldPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S1861, _S1866, _S1871, float4(_S1872, _S1873, _S1874, _S1875))))).w;
                        if(_S1876 > shadowVisibility_0)
                        {
                            shadowVisibility_0 = _S1876;
                            shadowSlot_1 = _S1855;
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
                    uint _S1877 = 224U + _S1849;
                    float _S1878 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1877)>>2]);
                    float _S1879 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1877 + 4U)>>2]);
                    float _S1880 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1877 + 8U)>>2]);
                    float _S1881 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1877 + 12U)>>2]);
                    float3 toShadowLight_0 = float3(_S1878, _S1879, _S1880) - worldPosition_0;
                    color_3 = toShadowLight_0 * float3(rsqrt(max(dot(toShadowLight_0, toShadowLight_0), 0.00100000004749745f))) ;
                }
                else
                {
                    color_3 = lightDirection_0;
                }
                for(;;)
                {
                    uint _S1882 = uint(shadowSlot_1);
                    uint _S1883 = _S1882 * 16U;
                    uint _S1884 = 14496U + _S1883;
                    float _S1885 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1884)>>2]);
                    float _S1886 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1884 + 4U)>>2]);
                    float _S1887 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1884 + 8U)>>2]);
                    float _S1888 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1884 + 12U)>>2]);
                    uint _S1889 = 14112U + _S1883;
                    float _S1890 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1889)>>2]);
                    float _S1891 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1889 + 4U)>>2]);
                    float _S1892 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1889 + 8U)>>2]);
                    float _S1893 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1889 + 12U)>>2]);
                    if((dot(specularColor_0, color_3)) < 0.0f)
                    {
                        sampleDirection_0 = - specularColor_0;
                    }
                    else
                    {
                        sampleDirection_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(sampleDirection_0, color_3)), 0.0f, 1.0f);
                    float3 offsetPosition_0 = worldPosition_0 + sampleDirection_0 * float3((_S1886 * slope_0)) ;
                    uint _S1894 = 12576U + _S1882 * 64U;
                    float _S1895 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894)>>2]);
                    float _S1896 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 4U)>>2]);
                    float _S1897 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 8U)>>2]);
                    float _S1898 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 12U)>>2]);
                    float4 _S1899 = float4(_S1895, _S1896, _S1897, _S1898);
                    float _S1900 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 16U)>>2]);
                    float _S1901 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 20U)>>2]);
                    float _S1902 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 24U)>>2]);
                    float _S1903 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 28U)>>2]);
                    float4 _S1904 = float4(_S1900, _S1901, _S1902, _S1903);
                    float _S1905 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 32U)>>2]);
                    float _S1906 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 36U)>>2]);
                    float _S1907 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 40U)>>2]);
                    float _S1908 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 44U)>>2]);
                    float4 _S1909 = float4(_S1905, _S1906, _S1907, _S1908);
                    float _S1910 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 48U)>>2]);
                    float _S1911 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 52U)>>2]);
                    float _S1912 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 56U)>>2]);
                    float _S1913 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1894 + 60U)>>2]);
                    float4 lightClip_0 = (((float4(offsetPosition_0, 1.0f)) * (matrix<float,int(4),int(4)> (_S1899, _S1904, _S1909, float4(_S1910, _S1911, _S1912, _S1913)))));
                    float _S1914 = lightClip_0.w;
                    if(_S1914 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S1914) ;
                    bool _S1915;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S1915 = true;
                    }
                    else
                    {
                        _S1915 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S1916;
                    if(_S1915)
                    {
                        _S1916 = true;
                    }
                    else
                    {
                        _S1916 = (ndc_0.z) < 0.0f;
                    }
                    bool _S1917;
                    if(_S1916)
                    {
                        _S1917 = true;
                    }
                    else
                    {
                        _S1917 = (ndc_0.z) > 1.0f;
                    }
                    if(_S1917)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S1918 = float2(_S1890, _S1891);
                    float2 _S1919 = float2(_S1892, _S1893);
                    float2 _S1920 = _S1918 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S1919;
                    float _S1921 = max(_S1887, 0.0f);
                    float _S1922 = ndc_0.z - _S1885 * (1.0f + 2.0f * slope_0);
                    float2 _S1923 = float2((_S1888 * 0.5f)) ;
                    float2 _S1924 = _S1918 + _S1923;
                    float2 _S1925 = _S1918 + _S1919 - _S1923;
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
                            if(_S1922 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S1920 + float2(float(x_0), float(y_0)) * float2((_S1921 * _S1888)) , _S1924, _S1925)), level((0.0f))).x))
                            {
                                ior_0 = 1.0f;
                            }
                            else
                            {
                                ior_0 = 0.0f;
                            }
                            float lit_0 = shadowVisibility_0 + ior_0;
                            x_0 = x_0 + int(1);
                            shadowVisibility_0 = lit_0;
                        }
                        y_0 = y_0 + int(1);
                    }
                    ior_0 = shadowVisibility_0 * 0.1111111119389534f;
                    break;
                }
                shadowVisibility_0 = ior_0;
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
        float3 _S1926 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S1926;
        sampleOffsets_0[int(1)] = _S1926;
        sampleOffsets_0[int(2)] = _S1926;
        sampleOffsets_0[int(3)] = _S1926;
        sampleOffsets_0[int(4)] = _S1926;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S1927 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S1927 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S1927 - halfHeight_0;
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
            float sampleIntensity_0 = roughness_0 / sampleCount_0;
            float sampleIntensity_1;
            float emissionScale_0;
            if(lightType_0 >= 2.0f)
            {
                uint _S1928 = 224U + lightIndex_0 * 16U;
                float _S1929 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1928)>>2]);
                float _S1930 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1928 + 4U)>>2]);
                float _S1931 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1928 + 8U)>>2]);
                float _S1932 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S1928 + 12U)>>2]);
                float3 toLight_0 = float3(_S1929, _S1930, _S1931) + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S1933 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S1933)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S1933;
                float emitterCosine_0 = dot(lightDirection_0, sampleDirection_1);
                if(lightType_0 == 4.0f)
                {
                    sampleIntensity_1 = saturate(emitterCosine_0);
                }
                else
                {
                    sampleIntensity_1 = 1.0f;
                }
                if((_S163.z) > 0.5f)
                {
                    if(emitterCosine_0 < (_S163.x))
                    {
                        emissionScale_0 = 0.0f;
                    }
                    else
                    {
                        float _S1934 = _S163.y;
                        if(_S1934 > 0.0f)
                        {
                            ior_0 = pow(max(emitterCosine_0, 0.0f), _S1934);
                        }
                        else
                        {
                            ior_0 = 1.0f;
                        }
                        emissionScale_0 = ior_0;
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
            float _S1935 = pow(max(0.0f, 1.0f - saturate(dot(lightEyeBase_0, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S1935)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S1935) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S1936 = float3(_S1935) ;
            float _S1937 = max(_S1755, 0.00100000004749745f);
            float alpha_0 = _S1937 * _S1937;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S1938 = 1.0f - k_0;
            float3 _S1939 = float3(max(4.0f * normalDotLight_0 * _S1842, 1.00000000317107685e-30f)) ;
            float3 _S1940 = mix(normalIncidence_0, grazingIncidence_0, _S1936) * float3((_S1842 / (_S1842 * _S1938 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S1938 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S1939;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S1941 = max(_S1756, 0.00100000004749745f);
                float alpha_1 = _S1941 * _S1941;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S1942 = 1.0f - k_1;
                directSpecular_0 = _S1940 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S1936) * float3((_S1842 / (_S1842 * _S1942 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S1942 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S1939);
            }
            else
            {
                directSpecular_0 = _S1940;
            }
            float3 _S1943 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S1943 * _S1757) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S1943);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S1944 = _S2;
    if((_S2.environmentControls_0.x) >= 0.5f)
    {
        float _S1945 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S1946 = saturate(_S1755);
        float2 _S1947 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S1945, _S1946)), level((0.0f)))).xy;
        float _S1948 = _S1947.x;
        float _S1949 = _S1947.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S1948)  + grazingIncidence_0 * float3(_S1949) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S1948 + _S1949)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S1950 = max(_S1768.domeControls_0.y, 1.0f);
        float3 _S1951 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S1950 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1768.domeControls_0.z;
            float _S1952 = _S1768.domeControls_0.w;
            for(;;)
            {
                bool _S1953 = _S1950 <= 1.0f;
                _S5 = _S1953;
                if(_S1953)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S1952, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S1950)), level((0.0f)))).xyz;
                break;
            }
            float _S1954 = _S1944.environmentControls_0.y;
            float _S1955 = _S1944.environmentControls_0.z;
            for(;;)
            {
                if(_S5)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1956 = max(_S1954, 1.0f);
                    float _S1957 = _S1956 - 1.0f;
                    float slice_0 = _S1946 * max(_S1957, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S1955, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S1956)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S1957) + v_0) / _S1956)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S1958 = max(_S1954, 1.0f);
                float total_0 = _S1958 * _S1950;
                float _S1959 = _S1958 - 1.0f;
                float slice_1 = _S1946 * max(_S1959, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S1955, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S1958;
                specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S1959) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S1960 = max(_S1954, 1.0f);
                        float _S1961 = _S1960 - 1.0f;
                        float slice_2 = saturate(_S1756) * max(_S1961, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S1955, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S1960)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S1961) + v_2) / _S1960)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1962 = max(_S1954, 1.0f);
                    float total_1 = _S1962 * _S1950;
                    float _S1963 = _S1962 - 1.0f;
                    float slice_3 = saturate(_S1756) * max(_S1963, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S1955, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S1962;
                    diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S1963) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightTangent_0 = diffuseFresnel_0;
            }
            else
            {
                lightTangent_0 = _S1951;
            }
            irradiance_0 = diffuseColor_0;
            lightDirection_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S1951;
            lightDirection_0 = _S1951;
            lightTangent_0 = _S1951;
            for(;;)
            {
                if(sampleIndex_0 < _S1769)
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
                float _S1964 = _S1768.domeControls_0.w;
                for(;;)
                {
                    bool _S1965 = _S1950 <= 1.0f;
                    _S6 = _S1965;
                    if(_S1965)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S1964, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S1950)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                float _S1966 = _S1944.environmentControls_0.y;
                float _S1967 = _S1944.environmentControls_0.z;
                for(;;)
                {
                    if(_S6)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1968 = max(_S1966, 1.0f);
                        float _S1969 = _S1968 - 1.0f;
                        float slice_4 = _S1946 * max(_S1969, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S1967, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S1968)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S1969) + v_4) / _S1968)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1970 = max(_S1966, 1.0f);
                    float total_2 = _S1970 * _S1950;
                    float _S1971 = _S1970 - 1.0f;
                    float slice_5 = _S1946 * max(_S1971, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S1967, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S1970;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S1971) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightDirection_0 + specularColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S6)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S1972 = max(_S1966, 1.0f);
                            float _S1973 = _S1972 - 1.0f;
                            float slice_6 = saturate(_S1756) * max(_S1973, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S1967, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S1972)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S1973) + v_6) / _S1972)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1974 = max(_S1966, 1.0f);
                        float total_3 = _S1974 * _S1950;
                        float _S1975 = _S1974 - 1.0f;
                        float slice_7 = saturate(_S1756) * max(_S1975, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S1967, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S1974;
                        diffuseFresnel_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S1975) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
        float3 color_4 = color_2 + float3(occlusion_0)  * diffuse_1 * irradiance_0 * diffuseWeight_0 + float3(occlusion_0)  * lightDirection_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S1976 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S1945, saturate(_S1756))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightTangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S1976.x)  + float3(_S1976.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + unlitColor_0) * float3(exp2((as_type<float>((_S3.z))))) ;
    if((_S3.y) == 1U)
    {
        color_2 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S1977 = { float4(color_2, opacity_0) };
    return _S1977;
}
