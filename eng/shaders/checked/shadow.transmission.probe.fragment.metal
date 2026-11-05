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
    [[flat]] float pointDepthScale_0 [[user(TEXCOORD_3)]];
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
    bool _S9 = (_S1.pointDepthScale_0) > 0.0f;
    float _S10;
    if(_S9)
    {
        _S10 = _S1.lightDepth_0 * _S1.pointDepthScale_0;
    }
    else
    {
        _S10 = _S8;
    }
    bool _S11;
    if(_S9)
    {
        _S11 = _S10 > 1.0f;
    }
    else
    {
        _S11 = false;
    }
    if(_S11)
    {
        discard_fragment();
    }
    for(;;)
    {
        SurfaceParameters_natural_0 device* _S12 = (&kernelContext_0)->surfaceParameters_0+int(0);
        float4 _S13 = float4(_S12->uvTransformRow1_0) ;
        uint secondaryMask_0 = uint(round(_S13.w));
        float4 _S14 = float4(_S12->domeLinkControls_0) ;
        uint alternateMask_0 = uint(round(_S14.w));
        float4 _S15 = float4(_S12->uvTransformRow0_0) ;
        float2 _S16 = _S15.xy;
        float _S17 = _S15.z;
        float2 _S18 = _S13.xy;
        float _S19 = _S13.z;
        float2 _S20 = float2(dot(_S16, _S1.texCoord_0) + _S17, dot(_S18, _S1.texCoord_0) + _S19);
        float2 _S21 = float2(dot(_S16, _S1.texCoord1_0) + _S17, dot(_S18, _S1.texCoord1_0) + _S19);
        float4 _S22 = float4(_S12->alternateUvTransformRow0_0) ;
        float4 _S23 = float4(_S12->alternateUvTransformRow1_0) ;
        float2 _S24 = _S22.xy;
        float _S25 = _S22.z;
        float2 _S26 = _S23.xy;
        float _S27 = _S23.z;
        float2 _S28 = float2(dot(_S24, _S1.texCoord_0) + _S25, dot(_S26, _S1.texCoord_0) + _S27);
        float2 _S29 = float2(dot(_S24, _S1.texCoord1_0) + _S25, dot(_S26, _S1.texCoord1_0) + _S27);
        bool useSecondary_0 = (secondaryMask_0 & 64U) != 0U;
        float2 _S30;
        if((alternateMask_0 & 64U) != 0U)
        {
            if(useSecondary_0)
            {
                _S30 = _S29;
            }
            else
            {
                _S30 = _S28;
            }
        }
        else
        {
            if(useSecondary_0)
            {
                _S30 = _S21;
            }
            else
            {
                _S30 = _S20;
            }
        }
        for(;;)
        {
            bool _S31 = (uint(round((float4(_S12->textureControls_0) ).y)) & 64U) != 0U;
            uint maskU_0 = uint(round(_S14.y));
            uint maskV_0 = uint(round(_S14.z));
            uint _S32;
            if((maskU_0 & 64U) != 0U)
            {
                _S32 = 1U;
            }
            else
            {
                _S32 = 0U;
            }
            uint _S33;
            if((maskV_0 & 64U) != 0U)
            {
                _S33 = 2U;
            }
            else
            {
                _S33 = 0U;
            }
            uint _S34 = _S32 | _S33;
            for(;;)
            {
                if(!_S31)
                {
                    for(;;)
                    {
                        if(_S34 == 0U)
                        {
                            _S5 = _S30;
                            break;
                        }
                        texture2d<float, access::sample> _S35 = (&kernelContext_0)->opacityTexture_0;
                        thread uint width_0;
                        thread uint height_0;
                        (*((&width_0)) = (_S35).get_width(0)),(*((&height_0)) = (_S35).get_height(0));
                        float2 size_0 = float2(float(width_0), float(height_0));
                        float2 _S36 = float2(0.5f) ;
                        float2 remapped_0 = clamp((_S30 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S36 / size_0, (size_0 - _S36) / size_0);
                        if((_S34 & 1U) != 0U)
                        {
                            _S2 = remapped_0.x;
                        }
                        else
                        {
                            _S2 = _S30.x;
                        }
                        if((_S34 & 2U) != 0U)
                        {
                            _S3 = remapped_0.y;
                        }
                        else
                        {
                            _S3 = _S30.y;
                        }
                        _S5 = float2(_S2, _S3);
                        break;
                    }
                    _S4 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S5)));
                    break;
                }
                texture2d<float, access::sample> _S37 = (&kernelContext_0)->opacityTexture_0;
                thread uint atlasWidth_0;
                thread uint atlasHeight_0;
                (*((&atlasWidth_0)) = (_S37).get_width(0)),(*((&atlasHeight_0)) = (_S37).get_height(0));
                int3 _S38 = int3(int(0), int(0), int(0));
                float4 metadata_0 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S38)).xy), uint(((_S38)).z))) * float4(255.0f) );
                if((metadata_0.x) == 255.0f)
                {
                    uint _S39 = atlasWidth_0;
                    uint _S40 = 2U % atlasWidth_0;
                    int _S41 = int(_S40);
                    uint _S42 = 2U / _S39;
                    int3 _S43 = int3(_S41, int(_S42), int(0));
                    int _S44 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S43)).xy), uint(((_S43)).z))).x * 255.0f)));
                    uint _S45 = atlasWidth_0;
                    uint _S46 = 3U % atlasWidth_0;
                    int _S47 = int(_S46);
                    uint _S48 = 3U / _S45;
                    int3 _S49 = int3(_S47, int(_S48), int(0));
                    int2 minimumTile_0 = int2(_S44, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S49)).xy), uint(((_S49)).z))).x * 255.0f))));
                    uint _S50 = atlasWidth_0;
                    uint _S51 = 4U % atlasWidth_0;
                    int _S52 = int(_S51);
                    uint _S53 = 4U / _S50;
                    int3 _S54 = int3(_S52, int(_S53), int(0));
                    uint _S55 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S54)).xy), uint(((_S54)).z))).x * 255.0f));
                    uint _S56 = atlasWidth_0;
                    uint _S57 = 5U % atlasWidth_0;
                    int _S58 = int(_S57);
                    uint _S59 = 5U / _S56;
                    int3 _S60 = int3(_S58, int(_S59), int(0));
                    uint _S61 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S60)).xy), uint(((_S60)).z))).x * 255.0f));
                    uint2 grid_0 = uint2(_S55, _S61);
                    int2 tile_0 = int2(floor(_S30)) - minimumTile_0;
                    if(any(tile_0 < (int2(int(0)) )))
                    {
                        _S11 = true;
                    }
                    else
                    {
                        _S11 = any(tile_0 >= int2(grid_0));
                    }
                    if(_S11)
                    {
                        int3 _S62 = int3(int(1), int(0), int(0));
                        _S4 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S62)).xy), uint(((_S62)).z)));
                        break;
                    }
                    uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S55 + uint(tile_0.x));
                    uint _S63 = atlasWidth_0;
                    uint _S64 = offset_0 % atlasWidth_0;
                    int _S65 = int(_S64);
                    uint _S66 = offset_0 / _S63;
                    int3 _S67 = int3(_S65, int(_S66), int(0));
                    uint _S68 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S67)).xy), uint(((_S67)).z))).x * 255.0f));
                    uint _S69 = atlasWidth_0;
                    uint _S70 = offset_0 + 1U;
                    uint _S71 = _S70 % atlasWidth_0;
                    int _S72 = int(_S71);
                    uint _S73 = _S70 / _S69;
                    int3 _S74 = int3(_S72, int(_S73), int(0));
                    uint slot_0 = _S68 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S74)).xy), uint(((_S74)).z))).x * 255.0f)) << 8U);
                    if(slot_0 == 0U)
                    {
                        int3 _S75 = int3(int(1), int(0), int(0));
                        _S4 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S75)).xy), uint(((_S75)).z)));
                        break;
                    }
                    uint slot_1 = slot_0 - 1U;
                    uint _S76 = atlasWidth_0;
                    uint _S77 = 6U % atlasWidth_0;
                    int _S78 = int(_S77);
                    uint _S79 = 6U / _S76;
                    int3 _S80 = int3(_S78, int(_S79), int(0));
                    uint _S81 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S80)).xy), uint(((_S80)).z))).x * 255.0f));
                    uint _S82 = atlasWidth_0;
                    uint _S83 = 7U % atlasWidth_0;
                    int _S84 = int(_S83);
                    uint _S85 = 7U / _S82;
                    int3 _S86 = int3(_S84, int(_S85), int(0));
                    uint columns_0 = _S81 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S86)).xy), uint(((_S86)).z))).x * 255.0f)) << 8U);
                    uint _S87 = atlasWidth_0;
                    uint _S88 = 8U % atlasWidth_0;
                    int _S89 = int(_S88);
                    uint _S90 = 8U / _S87;
                    int3 _S91 = int3(_S89, int(_S90), int(0));
                    uint _S92 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S91)).xy), uint(((_S91)).z))).x * 255.0f));
                    uint _S93 = atlasWidth_0;
                    uint _S94 = 9U % atlasWidth_0;
                    int _S95 = int(_S94);
                    uint _S96 = 9U / _S93;
                    int3 _S97 = int3(_S95, int(_S96), int(0));
                    uint rows_0 = ((_S92 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S97)).xy), uint(((_S97)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                    uint metadataRows_0 = (10U + 2U * _S55 * _S61 + atlasWidth_0 - 1U) / atlasWidth_0;
                    uint _S98 = atlasWidth_0 / columns_0;
                    float _S99 = float(_S98);
                    uint _S100 = (atlasHeight_0 - metadataRows_0) / rows_0;
                    float2 cellSize_0 = float2(_S99, float(_S100));
                    uint _S101 = slot_1 % columns_0;
                    float _S102 = float(_S101);
                    uint _S103 = slot_1 / columns_0;
                    _S4 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S102, float(_S103)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S30) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                    break;
                }
                int2 _S104 = int2(metadata_0.zw);
                int2 tile_1 = int2(floor(_S30)) - int2(metadata_0.xy);
                if(any(tile_1 < (int2(int(0)) )))
                {
                    _S11 = true;
                }
                else
                {
                    _S11 = any(tile_1 >= _S104);
                }
                if(_S11)
                {
                    int3 _S105 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                    _S4 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S105)).xy), uint(((_S105)).z)));
                    break;
                }
                uint _S106 = atlasWidth_0 / uint(_S104.x);
                float _S107 = float(_S106);
                uint _S108 = (atlasHeight_0 - 1U) / uint(_S104.y);
                float2 cellSize_1 = float2(_S107, float(_S108));
                _S4 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S30) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            for(;;)
            {
                float4 _S109 = float4(_S12->compositeControls_0) ;
                if((_S109.x) != 64.0f)
                {
                    break;
                }
                bool _S110 = (_S109.w) >= 0.5f;
                if((maskU_0 & 1U) != 0U)
                {
                    _S32 = 1U;
                }
                else
                {
                    _S32 = 0U;
                }
                if((maskV_0 & 1U) != 0U)
                {
                    _S33 = 2U;
                }
                else
                {
                    _S33 = 0U;
                }
                uint _S111 = _S32 | _S33;
                for(;;)
                {
                    if(!_S110)
                    {
                        for(;;)
                        {
                            if(_S111 == 0U)
                            {
                                break;
                            }
                            texture2d<float, access::sample> _S112 = (&kernelContext_0)->compositeTexture_0;
                            thread uint width_1;
                            thread uint height_1;
                            (*((&width_1)) = (_S112).get_width(0)),(*((&height_1)) = (_S112).get_height(0));
                            float2 size_1 = float2(float(width_1), float(height_1));
                            float2 _S113 = float2(0.5f) ;
                            float2 remapped_1 = clamp((_S30 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S113 / size_1, (size_1 - _S113) / size_1);
                            if((_S111 & 1U) != 0U)
                            {
                                _S2 = remapped_1.x;
                            }
                            else
                            {
                                _S2 = _S30.x;
                            }
                            if((_S111 & 2U) != 0U)
                            {
                                _S3 = remapped_1.y;
                            }
                            else
                            {
                                _S3 = _S30.y;
                            }
                            _S30 = float2(_S2, _S3);
                            break;
                        }
                        _S6 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S30)));
                        break;
                    }
                    texture2d<float, access::sample> _S114 = (&kernelContext_0)->compositeTexture_0;
                    thread uint atlasWidth_1;
                    thread uint atlasHeight_1;
                    (*((&atlasWidth_1)) = (_S114).get_width(0)),(*((&atlasHeight_1)) = (_S114).get_height(0));
                    int3 _S115 = int3(int(0), int(0), int(0));
                    float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S115)).xy), uint(((_S115)).z))) * float4(255.0f) );
                    if((metadata_1.x) == 255.0f)
                    {
                        uint _S116 = atlasWidth_1;
                        uint _S117 = 2U % atlasWidth_1;
                        int _S118 = int(_S117);
                        uint _S119 = 2U / _S116;
                        int3 _S120 = int3(_S118, int(_S119), int(0));
                        int _S121 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S120)).xy), uint(((_S120)).z))).x * 255.0f)));
                        uint _S122 = atlasWidth_1;
                        uint _S123 = 3U % atlasWidth_1;
                        int _S124 = int(_S123);
                        uint _S125 = 3U / _S122;
                        int3 _S126 = int3(_S124, int(_S125), int(0));
                        int2 minimumTile_1 = int2(_S121, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S126)).xy), uint(((_S126)).z))).x * 255.0f))));
                        uint _S127 = atlasWidth_1;
                        uint _S128 = 4U % atlasWidth_1;
                        int _S129 = int(_S128);
                        uint _S130 = 4U / _S127;
                        int3 _S131 = int3(_S129, int(_S130), int(0));
                        uint _S132 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S131)).xy), uint(((_S131)).z))).x * 255.0f));
                        uint _S133 = atlasWidth_1;
                        uint _S134 = 5U % atlasWidth_1;
                        int _S135 = int(_S134);
                        uint _S136 = 5U / _S133;
                        int3 _S137 = int3(_S135, int(_S136), int(0));
                        uint _S138 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S137)).xy), uint(((_S137)).z))).x * 255.0f));
                        uint2 grid_1 = uint2(_S132, _S138);
                        int2 tile_2 = int2(floor(_S30)) - minimumTile_1;
                        if(any(tile_2 < (int2(int(0)) )))
                        {
                            _S11 = true;
                        }
                        else
                        {
                            _S11 = any(tile_2 >= int2(grid_1));
                        }
                        if(_S11)
                        {
                            int3 _S139 = int3(int(1), int(0), int(0));
                            _S6 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S139)).xy), uint(((_S139)).z)));
                            break;
                        }
                        uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S132 + uint(tile_2.x));
                        uint _S140 = atlasWidth_1;
                        uint _S141 = offset_1 % atlasWidth_1;
                        int _S142 = int(_S141);
                        uint _S143 = offset_1 / _S140;
                        int3 _S144 = int3(_S142, int(_S143), int(0));
                        uint _S145 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S144)).xy), uint(((_S144)).z))).x * 255.0f));
                        uint _S146 = atlasWidth_1;
                        uint _S147 = offset_1 + 1U;
                        uint _S148 = _S147 % atlasWidth_1;
                        int _S149 = int(_S148);
                        uint _S150 = _S147 / _S146;
                        int3 _S151 = int3(_S149, int(_S150), int(0));
                        uint slot_2 = _S145 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S151)).xy), uint(((_S151)).z))).x * 255.0f)) << 8U);
                        if(slot_2 == 0U)
                        {
                            int3 _S152 = int3(int(1), int(0), int(0));
                            _S6 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S152)).xy), uint(((_S152)).z)));
                            break;
                        }
                        uint slot_3 = slot_2 - 1U;
                        uint _S153 = atlasWidth_1;
                        uint _S154 = 6U % atlasWidth_1;
                        int _S155 = int(_S154);
                        uint _S156 = 6U / _S153;
                        int3 _S157 = int3(_S155, int(_S156), int(0));
                        uint _S158 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S157)).xy), uint(((_S157)).z))).x * 255.0f));
                        uint _S159 = atlasWidth_1;
                        uint _S160 = 7U % atlasWidth_1;
                        int _S161 = int(_S160);
                        uint _S162 = 7U / _S159;
                        int3 _S163 = int3(_S161, int(_S162), int(0));
                        uint columns_1 = _S158 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S163)).xy), uint(((_S163)).z))).x * 255.0f)) << 8U);
                        uint _S164 = atlasWidth_1;
                        uint _S165 = 8U % atlasWidth_1;
                        int _S166 = int(_S165);
                        uint _S167 = 8U / _S164;
                        int3 _S168 = int3(_S166, int(_S167), int(0));
                        uint _S169 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S168)).xy), uint(((_S168)).z))).x * 255.0f));
                        uint _S170 = atlasWidth_1;
                        uint _S171 = 9U % atlasWidth_1;
                        int _S172 = int(_S171);
                        uint _S173 = 9U / _S170;
                        int3 _S174 = int3(_S172, int(_S173), int(0));
                        uint rows_1 = ((_S169 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S174)).xy), uint(((_S174)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                        uint metadataRows_1 = (10U + 2U * _S132 * _S138 + atlasWidth_1 - 1U) / atlasWidth_1;
                        uint _S175 = atlasWidth_1 / columns_1;
                        float _S176 = float(_S175);
                        uint _S177 = (atlasHeight_1 - metadataRows_1) / rows_1;
                        float2 cellSize_2 = float2(_S176, float(_S177));
                        uint _S178 = slot_3 % columns_1;
                        float _S179 = float(_S178);
                        uint _S180 = slot_3 / columns_1;
                        _S6 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S179, float(_S180)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S30) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                        break;
                    }
                    int2 _S181 = int2(metadata_1.zw);
                    int2 tile_3 = int2(floor(_S30)) - int2(metadata_1.xy);
                    if(any(tile_3 < (int2(int(0)) )))
                    {
                        _S11 = true;
                    }
                    else
                    {
                        _S11 = any(tile_3 >= _S181);
                    }
                    if(_S11)
                    {
                        int3 _S182 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                        _S6 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S182)).xy), uint(((_S182)).z)));
                        break;
                    }
                    uint _S183 = atlasWidth_1 / uint(_S181.x);
                    float _S184 = float(_S183);
                    uint _S185 = (atlasHeight_1 - 1U) / uint(_S181.y);
                    float2 cellSize_3 = float2(_S184, float(_S185));
                    _S6 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S30) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                uint operation_0 = uint(round(_S109.y));
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
                    float factor_0 = _S109.z;
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
    int3 _S186 = int3(int2(position_0.xy), int(0));
    float nearestDepth_0 = (((&kernelContext_0)->previousShadowLayer_0).read(vec<uint,2>(((_S186)).xy), uint(((_S186)).z))).x;
    if((saturate(_S7)) <= 0.0f)
    {
        _S11 = true;
    }
    else
    {
        _S11 = _S10 != nearestDepth_0;
    }
    if(_S11)
    {
        discard_fragment();
    }
    thread ShadowTransmissionOutput_0 output_0;
    (&output_0)->depth_0 = _S10;
    (&output_0)->color_0 = float4(0.0f, 0.0f, 0.0f, 0.5f);
    return output_0;
}
