#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct ShadowMaskedVertexOutput_0
{
    float4 position_0;
    float2 texCoord_0;
    float2 texCoord1_0;
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

float SampleShadowOpacity_0(const ShadowMaskedVertexOutput_0 thread* input_0, KernelContext_0 thread* kernelContext_0)
{
    bool _S1;
    float _S2;
    float _S3;
    float4 _S4;
    float2 _S5;
    float4 _S6;
    float _S7;
    SurfaceParameters_natural_0 device* _S8 = kernelContext_0->surfaceParameters_0+int(0);
    float4 _S9 = float4(_S8->uvTransformRow1_0) ;
    float4 _S10 = float4(_S8->domeLinkControls_0) ;
    float4 _S11 = float4(_S8->uvTransformRow0_0) ;
    float2 _S12 = _S11.xy;
    float _S13 = _S11.z;
    float2 _S14 = _S9.xy;
    float _S15 = _S9.z;
    float2 _S16 = float2(dot(_S12, input_0->texCoord_0) + _S13, dot(_S14, input_0->texCoord_0) + _S15);
    float2 _S17 = float2(dot(_S12, input_0->texCoord1_0) + _S13, dot(_S14, input_0->texCoord1_0) + _S15);
    float4 _S18 = float4(_S8->alternateUvTransformRow0_0) ;
    float4 _S19 = float4(_S8->alternateUvTransformRow1_0) ;
    float2 _S20 = _S18.xy;
    float _S21 = _S18.z;
    float2 _S22 = _S19.xy;
    float _S23 = _S19.z;
    float2 _S24 = float2(dot(_S20, input_0->texCoord_0) + _S21, dot(_S22, input_0->texCoord_0) + _S23);
    float2 _S25 = float2(dot(_S20, input_0->texCoord1_0) + _S21, dot(_S22, input_0->texCoord1_0) + _S23);
    bool useSecondary_0 = (uint(round(_S9.w)) & 64U) != 0U;
    float2 _S26;
    if((uint(round(_S10.w)) & 64U) != 0U)
    {
        if(useSecondary_0)
        {
            _S26 = _S25;
        }
        else
        {
            _S26 = _S24;
        }
    }
    else
    {
        if(useSecondary_0)
        {
            _S26 = _S17;
        }
        else
        {
            _S26 = _S16;
        }
    }
    for(;;)
    {
        bool _S27 = (uint(round((float4(_S8->textureControls_0) ).y)) & 64U) != 0U;
        uint maskU_0 = uint(round(_S10.y));
        uint maskV_0 = uint(round(_S10.z));
        uint _S28;
        if((maskU_0 & 64U) != 0U)
        {
            _S28 = 1U;
        }
        else
        {
            _S28 = 0U;
        }
        uint _S29;
        if((maskV_0 & 64U) != 0U)
        {
            _S29 = 2U;
        }
        else
        {
            _S29 = 0U;
        }
        uint _S30 = _S28 | _S29;
        for(;;)
        {
            if(!_S27)
            {
                for(;;)
                {
                    if(_S30 == 0U)
                    {
                        _S5 = _S26;
                        break;
                    }
                    texture2d<float, access::sample> _S31 = kernelContext_0->opacityTexture_0;
                    thread uint width_0;
                    thread uint height_0;
                    (*((&width_0)) = (_S31).get_width(0)),(*((&height_0)) = (_S31).get_height(0));
                    float2 size_0 = float2(float(width_0), float(height_0));
                    float2 _S32 = float2(0.5f) ;
                    float2 remapped_0 = clamp((_S26 * (size_0 - float2(2.0f) ) + float2(1.0f) ) / size_0, _S32 / size_0, (size_0 - _S32) / size_0);
                    if((_S30 & 1U) != 0U)
                    {
                        _S2 = remapped_0.x;
                    }
                    else
                    {
                        _S2 = _S26.x;
                    }
                    if((_S30 & 2U) != 0U)
                    {
                        _S3 = remapped_0.y;
                    }
                    else
                    {
                        _S3 = _S26.y;
                    }
                    _S5 = float2(_S2, _S3);
                    break;
                }
                _S4 = ((kernelContext_0->opacityTexture_0).sample((kernelContext_0->opacitySampler_0), (_S5)));
                break;
            }
            texture2d<float, access::sample> _S33 = kernelContext_0->opacityTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S33).get_width(0)),(*((&atlasHeight_0)) = (_S33).get_height(0));
            int3 _S34 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round(((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S34)).xy), uint(((_S34)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S35 = atlasWidth_0;
                uint _S36 = 2U % atlasWidth_0;
                int _S37 = int(_S36);
                uint _S38 = 2U / _S35;
                int3 _S39 = int3(_S37, int(_S38), int(0));
                int _S40 = int(uint(round(((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S39)).xy), uint(((_S39)).z))).x * 255.0f)));
                uint _S41 = atlasWidth_0;
                uint _S42 = 3U % atlasWidth_0;
                int _S43 = int(_S42);
                uint _S44 = 3U / _S41;
                int3 _S45 = int3(_S43, int(_S44), int(0));
                int2 minimumTile_0 = int2(_S40, int(uint(round(((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S45)).xy), uint(((_S45)).z))).x * 255.0f))));
                uint _S46 = atlasWidth_0;
                uint _S47 = 4U % atlasWidth_0;
                int _S48 = int(_S47);
                uint _S49 = 4U / _S46;
                int3 _S50 = int3(_S48, int(_S49), int(0));
                uint _S51 = uint(round(((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S50)).xy), uint(((_S50)).z))).x * 255.0f));
                uint _S52 = atlasWidth_0;
                uint _S53 = 5U % atlasWidth_0;
                int _S54 = int(_S53);
                uint _S55 = 5U / _S52;
                int3 _S56 = int3(_S54, int(_S55), int(0));
                uint _S57 = uint(round(((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S56)).xy), uint(((_S56)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S51, _S57);
                int2 tile_0 = int2(floor(_S26)) - minimumTile_0;
                if(any(tile_0 < (int2(int(0)) )))
                {
                    _S1 = true;
                }
                else
                {
                    _S1 = any(tile_0 >= int2(grid_0));
                }
                if(_S1)
                {
                    int3 _S58 = int3(int(1), int(0), int(0));
                    _S4 = ((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S58)).xy), uint(((_S58)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S51 + uint(tile_0.x));
                uint _S59 = atlasWidth_0;
                uint _S60 = offset_0 % atlasWidth_0;
                int _S61 = int(_S60);
                uint _S62 = offset_0 / _S59;
                int3 _S63 = int3(_S61, int(_S62), int(0));
                uint _S64 = uint(round(((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S63)).xy), uint(((_S63)).z))).x * 255.0f));
                uint _S65 = atlasWidth_0;
                uint _S66 = offset_0 + 1U;
                uint _S67 = _S66 % atlasWidth_0;
                int _S68 = int(_S67);
                uint _S69 = _S66 / _S65;
                int3 _S70 = int3(_S68, int(_S69), int(0));
                uint slot_0 = _S64 | (uint(round(((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S70)).xy), uint(((_S70)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S71 = int3(int(1), int(0), int(0));
                    _S4 = ((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S71)).xy), uint(((_S71)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S72 = atlasWidth_0;
                uint _S73 = 6U % atlasWidth_0;
                int _S74 = int(_S73);
                uint _S75 = 6U / _S72;
                int3 _S76 = int3(_S74, int(_S75), int(0));
                uint _S77 = uint(round(((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S76)).xy), uint(((_S76)).z))).x * 255.0f));
                uint _S78 = atlasWidth_0;
                uint _S79 = 7U % atlasWidth_0;
                int _S80 = int(_S79);
                uint _S81 = 7U / _S78;
                int3 _S82 = int3(_S80, int(_S81), int(0));
                uint columns_0 = _S77 | (uint(round(((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S82)).xy), uint(((_S82)).z))).x * 255.0f)) << 8U);
                uint _S83 = atlasWidth_0;
                uint _S84 = 8U % atlasWidth_0;
                int _S85 = int(_S84);
                uint _S86 = 8U / _S83;
                int3 _S87 = int3(_S85, int(_S86), int(0));
                uint _S88 = uint(round(((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S87)).xy), uint(((_S87)).z))).x * 255.0f));
                uint _S89 = atlasWidth_0;
                uint _S90 = 9U % atlasWidth_0;
                int _S91 = int(_S90);
                uint _S92 = 9U / _S89;
                int3 _S93 = int3(_S91, int(_S92), int(0));
                uint rows_0 = ((_S88 | (uint(round(((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S93)).xy), uint(((_S93)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S51 * _S57 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S94 = atlasWidth_0 / columns_0;
                float _S95 = float(_S94);
                uint _S96 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S95, float(_S96));
                uint _S97 = slot_1 % columns_0;
                float _S98 = float(_S97);
                uint _S99 = slot_1 / columns_0;
                _S4 = ((kernelContext_0->opacityTexture_0).sample((kernelContext_0->opacitySampler_0), ((float2(_S98, float(_S99)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S26) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S100 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(_S26)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                _S1 = true;
            }
            else
            {
                _S1 = any(tile_1 >= _S100);
            }
            if(_S1)
            {
                int3 _S101 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S4 = ((kernelContext_0->opacityTexture_0).read(vec<uint,2>(((_S101)).xy), uint(((_S101)).z)));
                break;
            }
            uint _S102 = atlasWidth_0 / uint(_S100.x);
            float _S103 = float(_S102);
            uint _S104 = (atlasHeight_0 - 1U) / uint(_S100.y);
            float2 cellSize_1 = float2(_S103, float(_S104));
            _S4 = ((kernelContext_0->opacityTexture_0).sample((kernelContext_0->opacitySampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S26) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        for(;;)
        {
            float4 _S105 = float4(_S8->compositeControls_0) ;
            if((_S105.x) != 64.0f)
            {
                break;
            }
            bool _S106 = (_S105.w) >= 0.5f;
            if((maskU_0 & 1U) != 0U)
            {
                _S28 = 1U;
            }
            else
            {
                _S28 = 0U;
            }
            if((maskV_0 & 1U) != 0U)
            {
                _S29 = 2U;
            }
            else
            {
                _S29 = 0U;
            }
            uint _S107 = _S28 | _S29;
            for(;;)
            {
                if(!_S106)
                {
                    for(;;)
                    {
                        if(_S107 == 0U)
                        {
                            break;
                        }
                        texture2d<float, access::sample> _S108 = kernelContext_0->compositeTexture_0;
                        thread uint width_1;
                        thread uint height_1;
                        (*((&width_1)) = (_S108).get_width(0)),(*((&height_1)) = (_S108).get_height(0));
                        float2 size_1 = float2(float(width_1), float(height_1));
                        float2 _S109 = float2(0.5f) ;
                        float2 remapped_1 = clamp((_S26 * (size_1 - float2(2.0f) ) + float2(1.0f) ) / size_1, _S109 / size_1, (size_1 - _S109) / size_1);
                        if((_S107 & 1U) != 0U)
                        {
                            _S2 = remapped_1.x;
                        }
                        else
                        {
                            _S2 = _S26.x;
                        }
                        if((_S107 & 2U) != 0U)
                        {
                            _S3 = remapped_1.y;
                        }
                        else
                        {
                            _S3 = _S26.y;
                        }
                        _S26 = float2(_S2, _S3);
                        break;
                    }
                    _S6 = ((kernelContext_0->compositeTexture_0).sample((kernelContext_0->compositeSampler_0), (_S26)));
                    break;
                }
                texture2d<float, access::sample> _S110 = kernelContext_0->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S110).get_width(0)),(*((&atlasHeight_1)) = (_S110).get_height(0));
                int3 _S111 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round(((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S111)).xy), uint(((_S111)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S112 = atlasWidth_1;
                    uint _S113 = 2U % atlasWidth_1;
                    int _S114 = int(_S113);
                    uint _S115 = 2U / _S112;
                    int3 _S116 = int3(_S114, int(_S115), int(0));
                    int _S117 = int(uint(round(((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S116)).xy), uint(((_S116)).z))).x * 255.0f)));
                    uint _S118 = atlasWidth_1;
                    uint _S119 = 3U % atlasWidth_1;
                    int _S120 = int(_S119);
                    uint _S121 = 3U / _S118;
                    int3 _S122 = int3(_S120, int(_S121), int(0));
                    int2 minimumTile_1 = int2(_S117, int(uint(round(((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S122)).xy), uint(((_S122)).z))).x * 255.0f))));
                    uint _S123 = atlasWidth_1;
                    uint _S124 = 4U % atlasWidth_1;
                    int _S125 = int(_S124);
                    uint _S126 = 4U / _S123;
                    int3 _S127 = int3(_S125, int(_S126), int(0));
                    uint _S128 = uint(round(((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S127)).xy), uint(((_S127)).z))).x * 255.0f));
                    uint _S129 = atlasWidth_1;
                    uint _S130 = 5U % atlasWidth_1;
                    int _S131 = int(_S130);
                    uint _S132 = 5U / _S129;
                    int3 _S133 = int3(_S131, int(_S132), int(0));
                    uint _S134 = uint(round(((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S133)).xy), uint(((_S133)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S128, _S134);
                    int2 tile_2 = int2(floor(_S26)) - minimumTile_1;
                    if(any(tile_2 < (int2(int(0)) )))
                    {
                        _S1 = true;
                    }
                    else
                    {
                        _S1 = any(tile_2 >= int2(grid_1));
                    }
                    if(_S1)
                    {
                        int3 _S135 = int3(int(1), int(0), int(0));
                        _S6 = ((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S135)).xy), uint(((_S135)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S128 + uint(tile_2.x));
                    uint _S136 = atlasWidth_1;
                    uint _S137 = offset_1 % atlasWidth_1;
                    int _S138 = int(_S137);
                    uint _S139 = offset_1 / _S136;
                    int3 _S140 = int3(_S138, int(_S139), int(0));
                    uint _S141 = uint(round(((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S140)).xy), uint(((_S140)).z))).x * 255.0f));
                    uint _S142 = atlasWidth_1;
                    uint _S143 = offset_1 + 1U;
                    uint _S144 = _S143 % atlasWidth_1;
                    int _S145 = int(_S144);
                    uint _S146 = _S143 / _S142;
                    int3 _S147 = int3(_S145, int(_S146), int(0));
                    uint slot_2 = _S141 | (uint(round(((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S147)).xy), uint(((_S147)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S148 = int3(int(1), int(0), int(0));
                        _S6 = ((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S148)).xy), uint(((_S148)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S149 = atlasWidth_1;
                    uint _S150 = 6U % atlasWidth_1;
                    int _S151 = int(_S150);
                    uint _S152 = 6U / _S149;
                    int3 _S153 = int3(_S151, int(_S152), int(0));
                    uint _S154 = uint(round(((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S153)).xy), uint(((_S153)).z))).x * 255.0f));
                    uint _S155 = atlasWidth_1;
                    uint _S156 = 7U % atlasWidth_1;
                    int _S157 = int(_S156);
                    uint _S158 = 7U / _S155;
                    int3 _S159 = int3(_S157, int(_S158), int(0));
                    uint columns_1 = _S154 | (uint(round(((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S159)).xy), uint(((_S159)).z))).x * 255.0f)) << 8U);
                    uint _S160 = atlasWidth_1;
                    uint _S161 = 8U % atlasWidth_1;
                    int _S162 = int(_S161);
                    uint _S163 = 8U / _S160;
                    int3 _S164 = int3(_S162, int(_S163), int(0));
                    uint _S165 = uint(round(((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S164)).xy), uint(((_S164)).z))).x * 255.0f));
                    uint _S166 = atlasWidth_1;
                    uint _S167 = 9U % atlasWidth_1;
                    int _S168 = int(_S167);
                    uint _S169 = 9U / _S166;
                    int3 _S170 = int3(_S168, int(_S169), int(0));
                    uint rows_1 = ((_S165 | (uint(round(((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S170)).xy), uint(((_S170)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S128 * _S134 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S171 = atlasWidth_1 / columns_1;
                    float _S172 = float(_S171);
                    uint _S173 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S172, float(_S173));
                    uint _S174 = slot_3 % columns_1;
                    float _S175 = float(_S174);
                    uint _S176 = slot_3 / columns_1;
                    _S6 = ((kernelContext_0->compositeTexture_0).sample((kernelContext_0->compositeSampler_0), ((float2(_S175, float(_S176)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S26) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S177 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(_S26)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    _S1 = true;
                }
                else
                {
                    _S1 = any(tile_3 >= _S177);
                }
                if(_S1)
                {
                    int3 _S178 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S6 = ((kernelContext_0->compositeTexture_0).read(vec<uint,2>(((_S178)).xy), uint(((_S178)).z)));
                    break;
                }
                uint _S179 = atlasWidth_1 / uint(_S177.x);
                float _S180 = float(_S179);
                uint _S181 = (atlasHeight_1 - 1U) / uint(_S177.y);
                float2 cellSize_3 = float2(_S180, float(_S181));
                _S6 = ((kernelContext_0->compositeTexture_0).sample((kernelContext_0->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S26) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S105.y));
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
                float factor_0 = _S105.z;
                _S4 = _S4 * float4((1.0f - factor_0))  + _S6 * float4(factor_0) ;
                break;
            }
            break;
        }
        _S7 = saturate(_S4.x);
        break;
    }
    return _S7;
}

struct pixelInput_0
{
    float2 texCoord_1 [[user(TEXCOORD)]];
    float2 texCoord1_1 [[user(TEXCOORD_1)]];
};

[[fragment]] void shadowMaskedFragmentMain(pixelInput_0 _S182 [[stage_in]], float4 position_1 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]])
{
    thread KernelContext_0 kernelContext_1;
    (&kernelContext_1)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_1)->opacityTexture_0 = opacityTexture_1;
    (&kernelContext_1)->opacitySampler_0 = opacitySampler_1;
    (&kernelContext_1)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_1)->compositeSampler_0 = compositeSampler_1;
    thread ShadowMaskedVertexOutput_0 _S183;
    (&_S183)->position_0 = position_1;
    (&_S183)->texCoord_0 = _S182.texCoord_1;
    (&_S183)->texCoord1_0 = _S182.texCoord1_1;
    float _S184 = SampleShadowOpacity_0(&_S183, &kernelContext_1);
    if(_S184 < ((float4(((&kernelContext_1)->surfaceParameters_0+int(0))->metallicRoughnessThresholdWorkflow_0) ).z))
    {
        discard_fragment();
    }
    return;
}
