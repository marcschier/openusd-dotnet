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
    float2 texCoord_0 [[user(TEXCOORD)]];
    float2 texCoord1_0 [[user(TEXCOORD_1)]];
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
};

[[fragment]] pixelOutput_0 shadowTransmissionFirstFragmentMain(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]])
{
    bool _S2;
    float _S3;
    float _S4;
    float4 _S5;
    float2 _S6;
    float4 _S7;
    float _S8;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->opacityTexture_0 = opacityTexture_1;
    (&kernelContext_0)->opacitySampler_0 = opacitySampler_1;
    (&kernelContext_0)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_0)->compositeSampler_0 = compositeSampler_1;
    for(;;)
    {
        SurfaceParameters_natural_0 device* _S9 = (&kernelContext_0)->surfaceParameters_0+int(0);
        float4 _S10 = float4(_S9->uvTransformRow1_0) ;
        uint secondaryMask_0 = uint(round(_S10.w));
        float4 _S11 = float4(_S9->domeLinkControls_0) ;
        uint alternateMask_0 = uint(round(_S11.w));
        float4 _S12 = float4(_S9->uvTransformRow0_0) ;
        float2 _S13 = _S12.xy;
        float _S14 = _S12.z;
        float2 _S15 = _S10.xy;
        float _S16 = _S10.z;
        float2 _S17 = float2(dot(_S13, _S1.texCoord_0) + _S14, dot(_S15, _S1.texCoord_0) + _S16);
        float2 _S18 = float2(dot(_S13, _S1.texCoord1_0) + _S14, dot(_S15, _S1.texCoord1_0) + _S16);
        float4 _S19 = float4(_S9->alternateUvTransformRow0_0) ;
        float4 _S20 = float4(_S9->alternateUvTransformRow1_0) ;
        float2 _S21 = _S19.xy;
        float _S22 = _S19.z;
        float2 _S23 = _S20.xy;
        float _S24 = _S20.z;
        float2 _S25 = float2(dot(_S21, _S1.texCoord_0) + _S22, dot(_S23, _S1.texCoord_0) + _S24);
        float2 _S26 = float2(dot(_S21, _S1.texCoord1_0) + _S22, dot(_S23, _S1.texCoord1_0) + _S24);
        bool useSecondary_0 = (secondaryMask_0 & 64U) != 0U;
        float2 _S27;
        if((alternateMask_0 & 64U) != 0U)
        {
            if(useSecondary_0)
            {
                _S27 = _S26;
            }
            else
            {
                _S27 = _S25;
            }
        }
        else
        {
            if(useSecondary_0)
            {
                _S27 = _S18;
            }
            else
            {
                _S27 = _S17;
            }
        }
        for(;;)
        {
            bool _S28 = (uint(round((float4(_S9->textureControls_0) ).y)) & 64U) != 0U;
            uint maskU_0 = uint(round(_S11.y));
            uint maskV_0 = uint(round(_S11.z));
            uint _S29;
            if((maskU_0 & 64U) != 0U)
            {
                _S29 = 1U;
            }
            else
            {
                _S29 = 0U;
            }
            uint _S30;
            if((maskV_0 & 64U) != 0U)
            {
                _S30 = 2U;
            }
            else
            {
                _S30 = 0U;
            }
            uint _S31 = _S29 | _S30;
            for(;;)
            {
                if(!_S28)
                {
                    for(;;)
                    {
                        if(_S31 == 0U)
                        {
                            _S6 = _S27;
                            break;
                        }
                        texture2d<float, access::sample> _S32 = (&kernelContext_0)->opacityTexture_0;
                        thread uint width_0;
                        thread uint height_0;
                        (*((&width_0)) = (_S32).get_width(0)),(*((&height_0)) = (_S32).get_height(0));
                        float2 size_0 = float2(float(width_0), float(height_0));
                        float2 _S33 = float2(0.5f) ;
                        float2 remapped_0 = clamp((_S27 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S33 / size_0, (size_0 - _S33) / size_0);
                        if((_S31 & 1U) != 0U)
                        {
                            _S3 = remapped_0.x;
                        }
                        else
                        {
                            _S3 = _S27.x;
                        }
                        if((_S31 & 2U) != 0U)
                        {
                            _S4 = remapped_0.y;
                        }
                        else
                        {
                            _S4 = _S27.y;
                        }
                        _S6 = float2(_S3, _S4);
                        break;
                    }
                    _S5 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S6)));
                    break;
                }
                texture2d<float, access::sample> _S34 = (&kernelContext_0)->opacityTexture_0;
                thread uint atlasWidth_0;
                thread uint atlasHeight_0;
                (*((&atlasWidth_0)) = (_S34).get_width(0)),(*((&atlasHeight_0)) = (_S34).get_height(0));
                int3 _S35 = int3(int(0), int(0), int(0));
                float4 metadata_0 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S35)).xy), uint(((_S35)).z))) * float4(255.0f) );
                if((metadata_0.x) == 255.0f)
                {
                    uint _S36 = atlasWidth_0;
                    uint _S37 = 2U % atlasWidth_0;
                    int _S38 = int(_S37);
                    uint _S39 = 2U / _S36;
                    int3 _S40 = int3(_S38, int(_S39), int(0));
                    int _S41 = int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S40)).xy), uint(((_S40)).z))).x * 255.0f)));
                    uint _S42 = atlasWidth_0;
                    uint _S43 = 3U % atlasWidth_0;
                    int _S44 = int(_S43);
                    uint _S45 = 3U / _S42;
                    int3 _S46 = int3(_S44, int(_S45), int(0));
                    int2 minimumTile_0 = int2(_S41, int(uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S46)).xy), uint(((_S46)).z))).x * 255.0f))));
                    uint _S47 = atlasWidth_0;
                    uint _S48 = 4U % atlasWidth_0;
                    int _S49 = int(_S48);
                    uint _S50 = 4U / _S47;
                    int3 _S51 = int3(_S49, int(_S50), int(0));
                    uint _S52 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S51)).xy), uint(((_S51)).z))).x * 255.0f));
                    uint _S53 = atlasWidth_0;
                    uint _S54 = 5U % atlasWidth_0;
                    int _S55 = int(_S54);
                    uint _S56 = 5U / _S53;
                    int3 _S57 = int3(_S55, int(_S56), int(0));
                    uint _S58 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S57)).xy), uint(((_S57)).z))).x * 255.0f));
                    uint2 grid_0 = uint2(_S52, _S58);
                    int2 tile_0 = int2(floor(_S27)) - minimumTile_0;
                    if(any(tile_0 < (int2(int(0)) )))
                    {
                        _S2 = true;
                    }
                    else
                    {
                        _S2 = any(tile_0 >= int2(grid_0));
                    }
                    if(_S2)
                    {
                        int3 _S59 = int3(int(1), int(0), int(0));
                        _S5 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S59)).xy), uint(((_S59)).z)));
                        break;
                    }
                    uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S52 + uint(tile_0.x));
                    uint _S60 = atlasWidth_0;
                    uint _S61 = offset_0 % atlasWidth_0;
                    int _S62 = int(_S61);
                    uint _S63 = offset_0 / _S60;
                    int3 _S64 = int3(_S62, int(_S63), int(0));
                    uint _S65 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S64)).xy), uint(((_S64)).z))).x * 255.0f));
                    uint _S66 = atlasWidth_0;
                    uint _S67 = offset_0 + 1U;
                    uint _S68 = _S67 % atlasWidth_0;
                    int _S69 = int(_S68);
                    uint _S70 = _S67 / _S66;
                    int3 _S71 = int3(_S69, int(_S70), int(0));
                    uint slot_0 = _S65 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S71)).xy), uint(((_S71)).z))).x * 255.0f)) << 8U);
                    if(slot_0 == 0U)
                    {
                        int3 _S72 = int3(int(1), int(0), int(0));
                        _S5 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S72)).xy), uint(((_S72)).z)));
                        break;
                    }
                    uint slot_1 = slot_0 - 1U;
                    uint _S73 = atlasWidth_0;
                    uint _S74 = 6U % atlasWidth_0;
                    int _S75 = int(_S74);
                    uint _S76 = 6U / _S73;
                    int3 _S77 = int3(_S75, int(_S76), int(0));
                    uint _S78 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S77)).xy), uint(((_S77)).z))).x * 255.0f));
                    uint _S79 = atlasWidth_0;
                    uint _S80 = 7U % atlasWidth_0;
                    int _S81 = int(_S80);
                    uint _S82 = 7U / _S79;
                    int3 _S83 = int3(_S81, int(_S82), int(0));
                    uint columns_0 = _S78 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S83)).xy), uint(((_S83)).z))).x * 255.0f)) << 8U);
                    uint _S84 = atlasWidth_0;
                    uint _S85 = 8U % atlasWidth_0;
                    int _S86 = int(_S85);
                    uint _S87 = 8U / _S84;
                    int3 _S88 = int3(_S86, int(_S87), int(0));
                    uint _S89 = uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S88)).xy), uint(((_S88)).z))).x * 255.0f));
                    uint _S90 = atlasWidth_0;
                    uint _S91 = 9U % atlasWidth_0;
                    int _S92 = int(_S91);
                    uint _S93 = 9U / _S90;
                    int3 _S94 = int3(_S92, int(_S93), int(0));
                    uint rows_0 = ((_S89 | (uint(round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S94)).xy), uint(((_S94)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                    uint metadataRows_0 = (10U + 2U * _S52 * _S58 + atlasWidth_0 - 1U) / atlasWidth_0;
                    uint _S95 = atlasWidth_0 / columns_0;
                    float _S96 = float(_S95);
                    uint _S97 = (atlasHeight_0 - metadataRows_0) / rows_0;
                    float2 cellSize_0 = float2(_S96, float(_S97));
                    uint _S98 = slot_1 % columns_0;
                    float _S99 = float(_S98);
                    uint _S100 = slot_1 / columns_0;
                    _S5 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(_S99, float(_S100)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S27) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                    break;
                }
                int2 _S101 = int2(metadata_0.zw);
                int2 tile_1 = int2(floor(_S27)) - int2(metadata_0.xy);
                if(any(tile_1 < (int2(int(0)) )))
                {
                    _S2 = true;
                }
                else
                {
                    _S2 = any(tile_1 >= _S101);
                }
                if(_S2)
                {
                    int3 _S102 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                    _S5 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S102)).xy), uint(((_S102)).z)));
                    break;
                }
                uint _S103 = atlasWidth_0 / uint(_S101.x);
                float _S104 = float(_S103);
                uint _S105 = (atlasHeight_0 - 1U) / uint(_S101.y);
                float2 cellSize_1 = float2(_S104, float(_S105));
                _S5 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            for(;;)
            {
                float4 _S106 = float4(_S9->compositeControls_0) ;
                if((_S106.x) != 64.0f)
                {
                    break;
                }
                bool _S107 = (_S106.w) >= 0.5f;
                if((maskU_0 & 1U) != 0U)
                {
                    _S29 = 1U;
                }
                else
                {
                    _S29 = 0U;
                }
                if((maskV_0 & 1U) != 0U)
                {
                    _S30 = 2U;
                }
                else
                {
                    _S30 = 0U;
                }
                uint _S108 = _S29 | _S30;
                for(;;)
                {
                    if(!_S107)
                    {
                        for(;;)
                        {
                            if(_S108 == 0U)
                            {
                                break;
                            }
                            texture2d<float, access::sample> _S109 = (&kernelContext_0)->compositeTexture_0;
                            thread uint width_1;
                            thread uint height_1;
                            (*((&width_1)) = (_S109).get_width(0)),(*((&height_1)) = (_S109).get_height(0));
                            float2 size_1 = float2(float(width_1), float(height_1));
                            float2 _S110 = float2(0.5f) ;
                            float2 remapped_1 = clamp((_S27 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S110 / size_1, (size_1 - _S110) / size_1);
                            if((_S108 & 1U) != 0U)
                            {
                                _S3 = remapped_1.x;
                            }
                            else
                            {
                                _S3 = _S27.x;
                            }
                            if((_S108 & 2U) != 0U)
                            {
                                _S4 = remapped_1.y;
                            }
                            else
                            {
                                _S4 = _S27.y;
                            }
                            _S27 = float2(_S3, _S4);
                            break;
                        }
                        _S7 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                        break;
                    }
                    texture2d<float, access::sample> _S111 = (&kernelContext_0)->compositeTexture_0;
                    thread uint atlasWidth_1;
                    thread uint atlasHeight_1;
                    (*((&atlasWidth_1)) = (_S111).get_width(0)),(*((&atlasHeight_1)) = (_S111).get_height(0));
                    int3 _S112 = int3(int(0), int(0), int(0));
                    float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S112)).xy), uint(((_S112)).z))) * float4(255.0f) );
                    if((metadata_1.x) == 255.0f)
                    {
                        uint _S113 = atlasWidth_1;
                        uint _S114 = 2U % atlasWidth_1;
                        int _S115 = int(_S114);
                        uint _S116 = 2U / _S113;
                        int3 _S117 = int3(_S115, int(_S116), int(0));
                        int _S118 = int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S117)).xy), uint(((_S117)).z))).x * 255.0f)));
                        uint _S119 = atlasWidth_1;
                        uint _S120 = 3U % atlasWidth_1;
                        int _S121 = int(_S120);
                        uint _S122 = 3U / _S119;
                        int3 _S123 = int3(_S121, int(_S122), int(0));
                        int2 minimumTile_1 = int2(_S118, int(uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S123)).xy), uint(((_S123)).z))).x * 255.0f))));
                        uint _S124 = atlasWidth_1;
                        uint _S125 = 4U % atlasWidth_1;
                        int _S126 = int(_S125);
                        uint _S127 = 4U / _S124;
                        int3 _S128 = int3(_S126, int(_S127), int(0));
                        uint _S129 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S128)).xy), uint(((_S128)).z))).x * 255.0f));
                        uint _S130 = atlasWidth_1;
                        uint _S131 = 5U % atlasWidth_1;
                        int _S132 = int(_S131);
                        uint _S133 = 5U / _S130;
                        int3 _S134 = int3(_S132, int(_S133), int(0));
                        uint _S135 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S134)).xy), uint(((_S134)).z))).x * 255.0f));
                        uint2 grid_1 = uint2(_S129, _S135);
                        int2 tile_2 = int2(floor(_S27)) - minimumTile_1;
                        if(any(tile_2 < (int2(int(0)) )))
                        {
                            _S2 = true;
                        }
                        else
                        {
                            _S2 = any(tile_2 >= int2(grid_1));
                        }
                        if(_S2)
                        {
                            int3 _S136 = int3(int(1), int(0), int(0));
                            _S7 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S136)).xy), uint(((_S136)).z)));
                            break;
                        }
                        uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S129 + uint(tile_2.x));
                        uint _S137 = atlasWidth_1;
                        uint _S138 = offset_1 % atlasWidth_1;
                        int _S139 = int(_S138);
                        uint _S140 = offset_1 / _S137;
                        int3 _S141 = int3(_S139, int(_S140), int(0));
                        uint _S142 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S141)).xy), uint(((_S141)).z))).x * 255.0f));
                        uint _S143 = atlasWidth_1;
                        uint _S144 = offset_1 + 1U;
                        uint _S145 = _S144 % atlasWidth_1;
                        int _S146 = int(_S145);
                        uint _S147 = _S144 / _S143;
                        int3 _S148 = int3(_S146, int(_S147), int(0));
                        uint slot_2 = _S142 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S148)).xy), uint(((_S148)).z))).x * 255.0f)) << 8U);
                        if(slot_2 == 0U)
                        {
                            int3 _S149 = int3(int(1), int(0), int(0));
                            _S7 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S149)).xy), uint(((_S149)).z)));
                            break;
                        }
                        uint slot_3 = slot_2 - 1U;
                        uint _S150 = atlasWidth_1;
                        uint _S151 = 6U % atlasWidth_1;
                        int _S152 = int(_S151);
                        uint _S153 = 6U / _S150;
                        int3 _S154 = int3(_S152, int(_S153), int(0));
                        uint _S155 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S154)).xy), uint(((_S154)).z))).x * 255.0f));
                        uint _S156 = atlasWidth_1;
                        uint _S157 = 7U % atlasWidth_1;
                        int _S158 = int(_S157);
                        uint _S159 = 7U / _S156;
                        int3 _S160 = int3(_S158, int(_S159), int(0));
                        uint columns_1 = _S155 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S160)).xy), uint(((_S160)).z))).x * 255.0f)) << 8U);
                        uint _S161 = atlasWidth_1;
                        uint _S162 = 8U % atlasWidth_1;
                        int _S163 = int(_S162);
                        uint _S164 = 8U / _S161;
                        int3 _S165 = int3(_S163, int(_S164), int(0));
                        uint _S166 = uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S165)).xy), uint(((_S165)).z))).x * 255.0f));
                        uint _S167 = atlasWidth_1;
                        uint _S168 = 9U % atlasWidth_1;
                        int _S169 = int(_S168);
                        uint _S170 = 9U / _S167;
                        int3 _S171 = int3(_S169, int(_S170), int(0));
                        uint rows_1 = ((_S166 | (uint(round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S171)).xy), uint(((_S171)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                        uint metadataRows_1 = (10U + 2U * _S129 * _S135 + atlasWidth_1 - 1U) / atlasWidth_1;
                        uint _S172 = atlasWidth_1 / columns_1;
                        float _S173 = float(_S172);
                        uint _S174 = (atlasHeight_1 - metadataRows_1) / rows_1;
                        float2 cellSize_2 = float2(_S173, float(_S174));
                        uint _S175 = slot_3 % columns_1;
                        float _S176 = float(_S175);
                        uint _S177 = slot_3 / columns_1;
                        _S7 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(_S176, float(_S177)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S27) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                        break;
                    }
                    int2 _S178 = int2(metadata_1.zw);
                    int2 tile_3 = int2(floor(_S27)) - int2(metadata_1.xy);
                    if(any(tile_3 < (int2(int(0)) )))
                    {
                        _S2 = true;
                    }
                    else
                    {
                        _S2 = any(tile_3 >= _S178);
                    }
                    if(_S2)
                    {
                        int3 _S179 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                        _S7 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S179)).xy), uint(((_S179)).z)));
                        break;
                    }
                    uint _S180 = atlasWidth_1 / uint(_S178.x);
                    float _S181 = float(_S180);
                    uint _S182 = (atlasHeight_1 - 1U) / uint(_S178.y);
                    float2 cellSize_3 = float2(_S181, float(_S182));
                    _S7 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                uint operation_0 = uint(round(_S106.y));
                if(operation_0 == 1U)
                {
                    _S5 = _S5 * _S7;
                    break;
                }
                if(operation_0 == 2U)
                {
                    _S5 = _S5 + _S7;
                    break;
                }
                if(operation_0 == 3U)
                {
                    _S5 = _S5 - _S7;
                    break;
                }
                if(operation_0 == 4U)
                {
                    float factor_0 = _S106.z;
                    _S5 = _S5 * float4((1.0f - factor_0))  + _S7 * float4(factor_0) ;
                    break;
                }
                break;
            }
            _S8 = saturate(_S5.x);
            break;
        }
        break;
    }
    float opacity_0 = saturate(_S8);
    if(opacity_0 <= 0.0f)
    {
        discard_fragment();
    }
    pixelOutput_0 _S183 = { float4(position_0.z, 1.0f - opacity_0, 0.0f, 1.0f) };
    return _S183;
}
