#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct ShadowTransmissionOutput_0
{
    float4 color_0 [[color(0)]];
    float depth_0 [[depth(any)]];
};

struct pixelInput_0
{
    float2 texCoord_0 [[user(TEXCOORD)]];
    float2 texCoord1_0 [[user(TEXCOORD_1)]];
    float lightDepth_0 [[user(TEXCOORD_2)]];
    [[flat]] float2 pointDepth_0 [[user(TEXCOORD_3)]];
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

struct KernelContext_0
{
    SurfaceParameters_natural_0 device* surfaceParameters_0;
    texture2d<float, access::sample> opacityTexture_0;
    sampler opacitySampler_0;
    texture2d<float, access::sample> compositeTexture_0;
    sampler compositeSampler_0;
    texture2d<float, access::sample> previousShadowLayer_0;
};

[[fragment]] ShadowTransmissionOutput_0 shadowTransmissionProbeFragmentMain(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> previousShadowLayer_1 [[texture(24)]])
{
    float _S2;
    float _S3;
    float4 _S4;
    float2 _S5;
    float4 _S6;
    float _S7;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->opacityTexture_0 = opacityTexture_1;
    (&kernelContext_0)->opacitySampler_0 = opacitySampler_1;
    (&kernelContext_0)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_0)->compositeSampler_0 = compositeSampler_1;
    (&kernelContext_0)->previousShadowLayer_0 = previousShadowLayer_1;
    float _S8 = position_0.z;
    float _S9 = _S1.pointDepth_0.x;
    bool _S10 = _S9 > 0.0f;
    float _S11;
    if(_S10)
    {
        _S11 = _S1.lightDepth_0 * _S9;
    }
    else
    {
        _S11 = _S8;
    }
    bool _S12;
    if(_S10)
    {
        _S12 = _S11 > (_S1.pointDepth_0.y);
    }
    else
    {
        _S12 = false;
    }
    if(_S12)
    {
        discard_fragment();
    }
    for(;;)
    {
        SurfaceParameters_natural_0 device* _S13 = (&kernelContext_0)->surfaceParameters_0+int(0);
        float4 _S14 = float4(_S13->uvTransformRow1_0) ;
        uint secondaryMask_0 = uint(round(_S14.w));
        float4 _S15 = float4(_S13->domeLinkControls_0) ;
        uint alternateMask_0 = uint(round(_S15.w));
        float4 _S16 = float4(_S13->uvTransformRow0_0) ;
        float2 _S17 = _S16.xy;
        float _S18 = _S16.z;
        float2 _S19 = _S14.xy;
        float _S20 = _S14.z;
        float2 _S21 = float2(dot(_S17, _S1.texCoord_0) + _S18, dot(_S19, _S1.texCoord_0) + _S20);
        float2 _S22 = float2(dot(_S17, _S1.texCoord1_0) + _S18, dot(_S19, _S1.texCoord1_0) + _S20);
        float4 _S23 = float4(_S13->alternateUvTransformRow0_0) ;
        float4 _S24 = float4(_S13->alternateUvTransformRow1_0) ;
        float2 _S25 = _S23.xy;
        float _S26 = _S23.z;
        float2 _S27 = _S24.xy;
        float _S28 = _S24.z;
        float2 _S29 = float2(dot(_S25, _S1.texCoord_0) + _S26, dot(_S27, _S1.texCoord_0) + _S28);
        float2 _S30 = float2(dot(_S25, _S1.texCoord1_0) + _S26, dot(_S27, _S1.texCoord1_0) + _S28);
        bool useSecondary_0 = (secondaryMask_0 & 64U) != 0U;
        float2 _S31;
        if((alternateMask_0 & 64U) != 0U)
        {
            if(useSecondary_0)
            {
                _S31 = _S30;
            }
            else
            {
                _S31 = _S29;
            }
        }
        else
        {
            if(useSecondary_0)
            {
                _S31 = _S22;
            }
            else
            {
                _S31 = _S21;
            }
        }
        for(;;)
        {
            bool _S32 = (uint(round((float4(_S13->textureControls_0) ).y)) & 64U) != 0U;
            uint maskU_0 = uint(round(_S15.y));
            uint maskV_0 = uint(round(_S15.z));
            uint _S33;
            if((maskU_0 & 64U) != 0U)
            {
                _S33 = 1U;
            }
            else
            {
                _S33 = 0U;
            }
            uint _S34;
            if((maskV_0 & 64U) != 0U)
            {
                _S34 = 2U;
            }
            else
            {
                _S34 = 0U;
            }
            uint _S35 = _S33 | _S34;
            for(;;)
            {
                if(!_S32)
                {
                    for(;;)
                    {
                        if(_S35 == 0U)
                        {
                            _S5 = _S31;
                            break;
                        }
                        texture2d<float, access::sample> _S36 = (&kernelContext_0)->opacityTexture_0;
                        thread uint width_0;
                        thread uint height_0;
                        (*((&width_0)) = (_S36).get_width(0)),(*((&height_0)) = (_S36).get_height(0));
                        float2 size_0 = float2(float(width_0), float(height_0));
                        float2 _S37 = float2(0.5f) ;
                        float2 remapped_0 = clamp((_S31 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S37 / size_0, (size_0 - _S37) / size_0);
                        if((_S35 & 1U) != 0U)
                        {
                            _S2 = remapped_0.x;
                        }
                        else
                        {
                            _S2 = _S31.x;
                        }
                        if((_S35 & 2U) != 0U)
                        {
                            _S3 = remapped_0.y;
                        }
                        else
                        {
                            _S3 = _S31.y;
                        }
                        _S5 = float2(_S2, _S3);
                        break;
                    }
                    _S4 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S5)));
                    break;
                }
                texture2d<float, access::sample> _S38 = (&kernelContext_0)->opacityTexture_0;
                thread uint atlasWidth_0;
                thread uint atlasHeight_0;
                (*((&atlasWidth_0)) = (_S38).get_width(0)),(*((&atlasHeight_0)) = (_S38).get_height(0));
                int3 _S39 = int3(int(0), int(0), int(0));
                float4 metadata_0 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S39)).xy), uint(((_S39)).z))) * float4(255.0f) );
                if((metadata_0.x) == 255.0f)
                {
                    uint _S40 = atlasWidth_0;
                    uint _S41 = 2U % atlasWidth_0;
                    int _S42 = int(_S41);
                    uint _S43 = 2U / _S40;
                    int3 _S44 = int3(_S42, int(_S43), int(0));
                    int _S45 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S44)).xy), uint(((_S44)).z))).x * 255.0f)));
                    uint _S46 = atlasWidth_0;
                    uint _S47 = 3U % atlasWidth_0;
                    int _S48 = int(_S47);
                    uint _S49 = 3U / _S46;
                    int3 _S50 = int3(_S48, int(_S49), int(0));
                    int2 minimumTile_0 = int2(_S45, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S50)).xy), uint(((_S50)).z))).x * 255.0f))));
                    uint _S51 = atlasWidth_0;
                    uint _S52 = 4U % atlasWidth_0;
                    int _S53 = int(_S52);
                    uint _S54 = 4U / _S51;
                    int3 _S55 = int3(_S53, int(_S54), int(0));
                    uint _S56 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S55)).xy), uint(((_S55)).z))).x * 255.0f));
                    uint _S57 = atlasWidth_0;
                    uint _S58 = 5U % atlasWidth_0;
                    int _S59 = int(_S58);
                    uint _S60 = 5U / _S57;
                    int3 _S61 = int3(_S59, int(_S60), int(0));
                    uint _S62 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S61)).xy), uint(((_S61)).z))).x * 255.0f));
                    uint2 grid_0 = uint2(_S56, _S62);
                    int2 tile_0 = int2(floor(_S31)) - minimumTile_0;
                    if(any(tile_0 < (int2(int(0)) )))
                    {
                        _S12 = true;
                    }
                    else
                    {
                        _S12 = any(tile_0 >= int2(grid_0));
                    }
                    if(_S12)
                    {
                        int3 _S63 = int3(int(1), int(0), int(0));
                        _S4 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S63)).xy), uint(((_S63)).z)));
                        break;
                    }
                    uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S56 + uint(tile_0.x));
                    uint _S64 = atlasWidth_0;
                    uint _S65 = offset_0 % atlasWidth_0;
                    int _S66 = int(_S65);
                    uint _S67 = offset_0 / _S64;
                    int3 _S68 = int3(_S66, int(_S67), int(0));
                    uint _S69 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S68)).xy), uint(((_S68)).z))).x * 255.0f));
                    uint _S70 = atlasWidth_0;
                    uint _S71 = offset_0 + 1U;
                    uint _S72 = _S71 % atlasWidth_0;
                    int _S73 = int(_S72);
                    uint _S74 = _S71 / _S70;
                    int3 _S75 = int3(_S73, int(_S74), int(0));
                    uint slot_0 = _S69 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S75)).xy), uint(((_S75)).z))).x * 255.0f)) << 8U);
                    if(slot_0 == 0U)
                    {
                        int3 _S76 = int3(int(1), int(0), int(0));
                        _S4 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S76)).xy), uint(((_S76)).z)));
                        break;
                    }
                    uint slot_1 = slot_0 - 1U;
                    uint _S77 = atlasWidth_0;
                    uint _S78 = 6U % atlasWidth_0;
                    int _S79 = int(_S78);
                    uint _S80 = 6U / _S77;
                    int3 _S81 = int3(_S79, int(_S80), int(0));
                    uint _S82 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S81)).xy), uint(((_S81)).z))).x * 255.0f));
                    uint _S83 = atlasWidth_0;
                    uint _S84 = 7U % atlasWidth_0;
                    int _S85 = int(_S84);
                    uint _S86 = 7U / _S83;
                    int3 _S87 = int3(_S85, int(_S86), int(0));
                    uint columns_0 = _S82 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S87)).xy), uint(((_S87)).z))).x * 255.0f)) << 8U);
                    uint _S88 = atlasWidth_0;
                    uint _S89 = 8U % atlasWidth_0;
                    int _S90 = int(_S89);
                    uint _S91 = 8U / _S88;
                    int3 _S92 = int3(_S90, int(_S91), int(0));
                    uint _S93 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S92)).xy), uint(((_S92)).z))).x * 255.0f));
                    uint _S94 = atlasWidth_0;
                    uint _S95 = 9U % atlasWidth_0;
                    int _S96 = int(_S95);
                    uint _S97 = 9U / _S94;
                    int3 _S98 = int3(_S96, int(_S97), int(0));
                    uint rows_0 = ((_S93 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S98)).xy), uint(((_S98)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                    uint metadataRows_0 = (10U + 2U * _S56 * _S62 + atlasWidth_0 - 1U) / atlasWidth_0;
                    uint _S99 = atlasWidth_0 / columns_0;
                    float _S100 = float(_S99);
                    uint _S101 = (atlasHeight_0 - metadataRows_0) / rows_0;
                    float2 cellSize_0 = float2(_S100, float(_S101));
                    uint _S102 = slot_1 % columns_0;
                    float _S103 = float(_S102);
                    uint _S104 = slot_1 / columns_0;
                    _S4 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S103, float(_S104)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S31) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                    break;
                }
                int2 _S105 = int2(metadata_0.zw);
                int2 tile_1 = int2(floor(_S31)) - int2(metadata_0.xy);
                if(any(tile_1 < (int2(int(0)) )))
                {
                    _S12 = true;
                }
                else
                {
                    _S12 = any(tile_1 >= _S105);
                }
                if(_S12)
                {
                    int3 _S106 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                    _S4 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S106)).xy), uint(((_S106)).z)));
                    break;
                }
                uint _S107 = atlasWidth_0 / uint(_S105.x);
                float _S108 = float(_S107);
                uint _S109 = (atlasHeight_0 - 1U) / uint(_S105.y);
                float2 cellSize_1 = float2(_S108, float(_S109));
                _S4 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S31) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            for(;;)
            {
                float4 _S110 = float4(_S13->compositeControls_0) ;
                if((_S110.x) != 64.0f)
                {
                    break;
                }
                bool _S111 = (_S110.w) >= 0.5f;
                if((maskU_0 & 1U) != 0U)
                {
                    _S33 = 1U;
                }
                else
                {
                    _S33 = 0U;
                }
                if((maskV_0 & 1U) != 0U)
                {
                    _S34 = 2U;
                }
                else
                {
                    _S34 = 0U;
                }
                uint _S112 = _S33 | _S34;
                for(;;)
                {
                    if(!_S111)
                    {
                        for(;;)
                        {
                            if(_S112 == 0U)
                            {
                                break;
                            }
                            texture2d<float, access::sample> _S113 = (&kernelContext_0)->compositeTexture_0;
                            thread uint width_1;
                            thread uint height_1;
                            (*((&width_1)) = (_S113).get_width(0)),(*((&height_1)) = (_S113).get_height(0));
                            float2 size_1 = float2(float(width_1), float(height_1));
                            float2 _S114 = float2(0.5f) ;
                            float2 remapped_1 = clamp((_S31 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S114 / size_1, (size_1 - _S114) / size_1);
                            if((_S112 & 1U) != 0U)
                            {
                                _S2 = remapped_1.x;
                            }
                            else
                            {
                                _S2 = _S31.x;
                            }
                            if((_S112 & 2U) != 0U)
                            {
                                _S3 = remapped_1.y;
                            }
                            else
                            {
                                _S3 = _S31.y;
                            }
                            _S31 = float2(_S2, _S3);
                            break;
                        }
                        _S6 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S31)));
                        break;
                    }
                    texture2d<float, access::sample> _S115 = (&kernelContext_0)->compositeTexture_0;
                    thread uint atlasWidth_1;
                    thread uint atlasHeight_1;
                    (*((&atlasWidth_1)) = (_S115).get_width(0)),(*((&atlasHeight_1)) = (_S115).get_height(0));
                    int3 _S116 = int3(int(0), int(0), int(0));
                    float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S116)).xy), uint(((_S116)).z))) * float4(255.0f) );
                    if((metadata_1.x) == 255.0f)
                    {
                        uint _S117 = atlasWidth_1;
                        uint _S118 = 2U % atlasWidth_1;
                        int _S119 = int(_S118);
                        uint _S120 = 2U / _S117;
                        int3 _S121 = int3(_S119, int(_S120), int(0));
                        int _S122 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S121)).xy), uint(((_S121)).z))).x * 255.0f)));
                        uint _S123 = atlasWidth_1;
                        uint _S124 = 3U % atlasWidth_1;
                        int _S125 = int(_S124);
                        uint _S126 = 3U / _S123;
                        int3 _S127 = int3(_S125, int(_S126), int(0));
                        int2 minimumTile_1 = int2(_S122, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S127)).xy), uint(((_S127)).z))).x * 255.0f))));
                        uint _S128 = atlasWidth_1;
                        uint _S129 = 4U % atlasWidth_1;
                        int _S130 = int(_S129);
                        uint _S131 = 4U / _S128;
                        int3 _S132 = int3(_S130, int(_S131), int(0));
                        uint _S133 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S132)).xy), uint(((_S132)).z))).x * 255.0f));
                        uint _S134 = atlasWidth_1;
                        uint _S135 = 5U % atlasWidth_1;
                        int _S136 = int(_S135);
                        uint _S137 = 5U / _S134;
                        int3 _S138 = int3(_S136, int(_S137), int(0));
                        uint _S139 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S138)).xy), uint(((_S138)).z))).x * 255.0f));
                        uint2 grid_1 = uint2(_S133, _S139);
                        int2 tile_2 = int2(floor(_S31)) - minimumTile_1;
                        if(any(tile_2 < (int2(int(0)) )))
                        {
                            _S12 = true;
                        }
                        else
                        {
                            _S12 = any(tile_2 >= int2(grid_1));
                        }
                        if(_S12)
                        {
                            int3 _S140 = int3(int(1), int(0), int(0));
                            _S6 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S140)).xy), uint(((_S140)).z)));
                            break;
                        }
                        uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S133 + uint(tile_2.x));
                        uint _S141 = atlasWidth_1;
                        uint _S142 = offset_1 % atlasWidth_1;
                        int _S143 = int(_S142);
                        uint _S144 = offset_1 / _S141;
                        int3 _S145 = int3(_S143, int(_S144), int(0));
                        uint _S146 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S145)).xy), uint(((_S145)).z))).x * 255.0f));
                        uint _S147 = atlasWidth_1;
                        uint _S148 = offset_1 + 1U;
                        uint _S149 = _S148 % atlasWidth_1;
                        int _S150 = int(_S149);
                        uint _S151 = _S148 / _S147;
                        int3 _S152 = int3(_S150, int(_S151), int(0));
                        uint slot_2 = _S146 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S152)).xy), uint(((_S152)).z))).x * 255.0f)) << 8U);
                        if(slot_2 == 0U)
                        {
                            int3 _S153 = int3(int(1), int(0), int(0));
                            _S6 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S153)).xy), uint(((_S153)).z)));
                            break;
                        }
                        uint slot_3 = slot_2 - 1U;
                        uint _S154 = atlasWidth_1;
                        uint _S155 = 6U % atlasWidth_1;
                        int _S156 = int(_S155);
                        uint _S157 = 6U / _S154;
                        int3 _S158 = int3(_S156, int(_S157), int(0));
                        uint _S159 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S158)).xy), uint(((_S158)).z))).x * 255.0f));
                        uint _S160 = atlasWidth_1;
                        uint _S161 = 7U % atlasWidth_1;
                        int _S162 = int(_S161);
                        uint _S163 = 7U / _S160;
                        int3 _S164 = int3(_S162, int(_S163), int(0));
                        uint columns_1 = _S159 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S164)).xy), uint(((_S164)).z))).x * 255.0f)) << 8U);
                        uint _S165 = atlasWidth_1;
                        uint _S166 = 8U % atlasWidth_1;
                        int _S167 = int(_S166);
                        uint _S168 = 8U / _S165;
                        int3 _S169 = int3(_S167, int(_S168), int(0));
                        uint _S170 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S169)).xy), uint(((_S169)).z))).x * 255.0f));
                        uint _S171 = atlasWidth_1;
                        uint _S172 = 9U % atlasWidth_1;
                        int _S173 = int(_S172);
                        uint _S174 = 9U / _S171;
                        int3 _S175 = int3(_S173, int(_S174), int(0));
                        uint rows_1 = ((_S170 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S175)).xy), uint(((_S175)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                        uint metadataRows_1 = (10U + 2U * _S133 * _S139 + atlasWidth_1 - 1U) / atlasWidth_1;
                        uint _S176 = atlasWidth_1 / columns_1;
                        float _S177 = float(_S176);
                        uint _S178 = (atlasHeight_1 - metadataRows_1) / rows_1;
                        float2 cellSize_2 = float2(_S177, float(_S178));
                        uint _S179 = slot_3 % columns_1;
                        float _S180 = float(_S179);
                        uint _S181 = slot_3 / columns_1;
                        _S6 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S180, float(_S181)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S31) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                        break;
                    }
                    int2 _S182 = int2(metadata_1.zw);
                    int2 tile_3 = int2(floor(_S31)) - int2(metadata_1.xy);
                    if(any(tile_3 < (int2(int(0)) )))
                    {
                        _S12 = true;
                    }
                    else
                    {
                        _S12 = any(tile_3 >= _S182);
                    }
                    if(_S12)
                    {
                        int3 _S183 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                        _S6 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S183)).xy), uint(((_S183)).z)));
                        break;
                    }
                    uint _S184 = atlasWidth_1 / uint(_S182.x);
                    float _S185 = float(_S184);
                    uint _S186 = (atlasHeight_1 - 1U) / uint(_S182.y);
                    float2 cellSize_3 = float2(_S185, float(_S186));
                    _S6 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S31) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                uint operation_0 = uint(round(_S110.y));
                if(operation_0 == 1U)
                {
                    _S4 = _S4 * _S6;
                    break;
                }
                if(operation_0 == 2U)
                {
                    _S4 = _S4 + _S6;
                    break;
                }
                if(operation_0 == 3U)
                {
                    _S4 = _S4 - _S6;
                    break;
                }
                if(operation_0 == 4U)
                {
                    float factor_0 = _S110.z;
                    _S4 = _S4 * float4((1.0f - factor_0))  + _S6 * float4(factor_0) ;
                    break;
                }
                break;
            }
            _S7 = saturate(_S4.x);
            break;
        }
        break;
    }
    int3 _S187 = int3(int2(position_0.xy), int(0));
    float nearestDepth_0 = (((&kernelContext_0)->previousShadowLayer_0).read(vec<uint,2>(((_S187)).xy), uint(((_S187)).z))).x;
    if((saturate(_S7)) <= 0.0f)
    {
        _S12 = true;
    }
    else
    {
        _S12 = _S11 != nearestDepth_0;
    }
    if(_S12)
    {
        discard_fragment();
    }
    thread ShadowTransmissionOutput_0 output_0;
    (&output_0)->depth_0 = _S11;
    (&output_0)->color_0 = float4(0.0f, 0.0f, 0.0f, 0.5f);
    return output_0;
}
