#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct pixelOutput_0
{
    float4 output_0 [[color(0)]];
};

struct SLANG_ParameterGroup_VolumeCameraParameters_0
{
    uint4 volumeHeader_0;
    uint4 atlasShape_0;
};

struct KernelContext_0
{
    SLANG_ParameterGroup_VolumeCameraParameters_0 constant* VolumeCameraParameters_0;
    uint32_t device* frameParameters_0;
    texture2d<float, access::sample> opaqueDepthTexture_0;
    uint32_t device* volumeFields_0;
    texture3d<float, access::sample> densityAtlas_0;
    sampler densitySampler_0;
};

[[fragment]] pixelOutput_0 volumeCameraAtlasFragmentMain(float4 position_0 [[position]], SLANG_ParameterGroup_VolumeCameraParameters_0 constant* VolumeCameraParameters_1 [[buffer(0)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> opaqueDepthTexture_1 [[texture(28)]], uint32_t device* volumeFields_1 [[buffer(6)]], texture3d<float, access::sample> densityAtlas_1 [[texture(9)]], sampler densitySampler_1 [[sampler(4)]])
{
    float intervalStart_0;
    float intervalEnd_0;
    bool hasActiveField_0;
    uint axis_0;
    int _S1;
    uint cursor_0;
    bool _S2;
    uint field_0;
    uint stepCount_0;
    uint step_0;
    float power_0;
    float term_0;
    float sum_0;
    uint order_0;
    bool _S3;
    bool _S4;
    bool _S5;
    bool _S6;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->VolumeCameraParameters_0 = VolumeCameraParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->opaqueDepthTexture_0 = opaqueDepthTexture_1;
    (&kernelContext_0)->volumeFields_0 = volumeFields_1;
    (&kernelContext_0)->densityAtlas_0 = densityAtlas_1;
    (&kernelContext_0)->densitySampler_0 = densitySampler_1;
    uint4 frameShape_0 = VolumeCameraParameters_1->volumeHeader_0;
    uint _S7 = min(VolumeCameraParameters_1->volumeHeader_0.x, 8U);
    bool diagnosticPixel_0;
    if(_S7 == 0U)
    {
        diagnosticPixel_0 = true;
    }
    else
    {
        diagnosticPixel_0 = (frameShape_0.y) == 0U;
    }
    if(diagnosticPixel_0)
    {
        diagnosticPixel_0 = true;
    }
    else
    {
        diagnosticPixel_0 = (frameShape_0.z) == 0U;
    }
    if(diagnosticPixel_0)
    {
        diagnosticPixel_0 = true;
    }
    else
    {
        diagnosticPixel_0 = (VolumeCameraParameters_1->volumeHeader_0.x) > 8U;
    }
    if(diagnosticPixel_0)
    {
        pixelOutput_0 _S8 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S8;
    }
    uint2 _S9 = uint2(position_0.xy);
    float2 pixelCoordinate_0 = (float2(_S9) + float2(0.5f) ) / float2(frameShape_0.yz);
    float _S10 = pixelCoordinate_0.x * 2.0f - 1.0f;
    float intervalStart_1;
    if((frameShape_0.w) != 0U)
    {
        intervalStart_1 = pixelCoordinate_0.y * 2.0f - 1.0f;
    }
    else
    {
        intervalStart_1 = 1.0f - pixelCoordinate_0.y * 2.0f;
    }
    float2 clipXY_0 = float2(_S10, intervalStart_1);
    float _S11 = as_type<float>((&kernelContext_0)->frameParameters_0[(0U)>>2]);
    float _S12 = as_type<float>((&kernelContext_0)->frameParameters_0[(4U)>>2]);
    float _S13 = as_type<float>((&kernelContext_0)->frameParameters_0[(8U)>>2]);
    float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(12U)>>2]);
    float4 _S15 = float4(_S11, _S12, _S13, _S14);
    float _S16 = as_type<float>((&kernelContext_0)->frameParameters_0[(16U)>>2]);
    float _S17 = as_type<float>((&kernelContext_0)->frameParameters_0[(20U)>>2]);
    float _S18 = as_type<float>((&kernelContext_0)->frameParameters_0[(24U)>>2]);
    float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(28U)>>2]);
    float4 _S20 = float4(_S16, _S17, _S18, _S19);
    float _S21 = as_type<float>((&kernelContext_0)->frameParameters_0[(32U)>>2]);
    float _S22 = as_type<float>((&kernelContext_0)->frameParameters_0[(36U)>>2]);
    float _S23 = as_type<float>((&kernelContext_0)->frameParameters_0[(40U)>>2]);
    float _S24 = as_type<float>((&kernelContext_0)->frameParameters_0[(44U)>>2]);
    float4 _S25 = float4(_S21, _S22, _S23, _S24);
    float _S26 = as_type<float>((&kernelContext_0)->frameParameters_0[(48U)>>2]);
    float _S27 = as_type<float>((&kernelContext_0)->frameParameters_0[(52U)>>2]);
    float _S28 = as_type<float>((&kernelContext_0)->frameParameters_0[(56U)>>2]);
    float _S29 = as_type<float>((&kernelContext_0)->frameParameters_0[(60U)>>2]);
    matrix<float,int(4),int(4)>  _S30 = matrix<float,int(4),int(4)> (_S15, _S20, _S25, float4(_S26, _S27, _S28, _S29));
    float _S31 = as_type<float>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
    float4 nearHomogeneous_0 = (((float4(clipXY_0, 0.0f, 1.0f)) * (_S30)));
    float4 farHomogeneous_0 = (((float4(clipXY_0, 1.0f, 1.0f)) * (_S30)));
    float _S32 = nearHomogeneous_0.w;
    if(_S32 == 0.0f)
    {
        pixelOutput_0 _S33 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S33;
    }
    float3 rayOrigin_0 = nearHomogeneous_0.xyz / float3(_S32) ;
    float _S34 = farHomogeneous_0.w;
    bool _S35 = _S34 == 0.0f;
    float3 rayDirection_0;
    if(_S35)
    {
        rayDirection_0 = normalize(farHomogeneous_0.xyz * float3(float((int(sign((_S32)))))) );
    }
    else
    {
        rayDirection_0 = normalize(farHomogeneous_0.xyz / float3(_S34)  - rayOrigin_0);
    }
    if(_S35)
    {
        intervalStart_1 = 3.4028234663852886e+38f;
    }
    else
    {
        intervalStart_1 = dot(farHomogeneous_0.xyz / float3(_S34)  - rayOrigin_0, rayDirection_0);
    }
    uint _S36 = as_type<uint>((&kernelContext_0)->frameParameters_0[(64U)>>2]);
    uint _S37 = min(_S36, 8U);
    float _S38 = intervalStart_1;
    uint planeIndex_0 = 0U;
    intervalStart_1 = 0.0f;
    float intervalEnd_1 = _S38;
    for(;;)
    {
        if(planeIndex_0 < _S37)
        {
        }
        else
        {
            break;
        }
        uint _S39 = 80U + planeIndex_0 * 16U;
        float _S40 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39)>>2]);
        float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 4U)>>2]);
        float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 8U)>>2]);
        float _S43 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S39 + 12U)>>2]);
        float3 _S44 = float3(_S40, _S41, _S42);
        float offset_0 = dot(_S44, rayOrigin_0) + _S43;
        float slope_0 = dot(_S44, rayDirection_0);
        if(slope_0 == 0.0f)
        {
            if(offset_0 < 0.0f)
            {
                pixelOutput_0 _S45 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
                return _S45;
            }
        }
        else
        {
            if(slope_0 > 0.0f)
            {
                intervalStart_0 = max(intervalStart_1, - offset_0 / slope_0);
                intervalEnd_0 = intervalEnd_1;
            }
            else
            {
                float _S46 = min(intervalEnd_1, - offset_0 / slope_0);
                intervalStart_0 = intervalStart_1;
                intervalEnd_0 = _S46;
            }
            intervalStart_1 = intervalStart_0;
            intervalEnd_1 = intervalEnd_0;
        }
        planeIndex_0 = planeIndex_0 + 1U;
    }
    int3 _S47 = int3(int2(_S9), int(0));
    float opaqueDepth_0 = (((&kernelContext_0)->opaqueDepthTexture_0).read(vec<uint,2>(((_S47)).xy), uint(((_S47)).z)).x);
    if(opaqueDepth_0 != _S31)
    {
        float4 opaqueHomogeneous_0 = (((float4(clipXY_0, opaqueDepth_0, 1.0f)) * (_S30)));
        float _S48 = opaqueHomogeneous_0.w;
        if(_S48 != 0.0f)
        {
            intervalEnd_1 = min(intervalEnd_1, dot(opaqueHomogeneous_0.xyz / float3(_S48)  - rayOrigin_0, rayDirection_0));
        }
    }
    if(intervalEnd_1 <= intervalStart_1)
    {
        pixelOutput_0 _S49 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S49;
    }
    thread array<float, int(8)> entries_0;
    thread array<float, int(8)> exits_0;
    thread array<float, int(8)> densityScales_0;
    thread array<float, int(8)> normalizedDensityIntegrals_0;
    thread array<float, int(8)> fieldOpticalDepth_0;
    thread array<float3, int(8)> fieldTint_0;
    thread array<bool, int(8)> fieldEnabled_0;
    thread array<uint, int(8)> fieldOffsets_0;
    thread array<float, int(16)> events_0;
    uint field_1 = 0U;
    uint eventCount_0 = 0U;
    for(;;)
    {
        if(field_1 < _S7)
        {
        }
        else
        {
            break;
        }
        uint _S50 = field_1 * 128U;
        fieldOffsets_0[field_1] = _S50;
        float _S51 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50)>>2]);
        float _S52 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 4U)>>2]);
        float _S53 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 8U)>>2]);
        float _S54 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 12U)>>2]);
        float4 _S55 = float4(_S51, _S52, _S53, _S54);
        float _S56 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 16U)>>2]);
        float _S57 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 20U)>>2]);
        float _S58 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 24U)>>2]);
        float _S59 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 28U)>>2]);
        float4 _S60 = float4(_S56, _S57, _S58, _S59);
        float _S61 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 32U)>>2]);
        float _S62 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 36U)>>2]);
        float _S63 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 40U)>>2]);
        float _S64 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 44U)>>2]);
        float4 _S65 = float4(_S61, _S62, _S63, _S64);
        float _S66 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 48U)>>2]);
        float _S67 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 52U)>>2]);
        float _S68 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 56U)>>2]);
        float _S69 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S50 + 60U)>>2]);
        matrix<float,int(4),int(4)>  _S70 = matrix<float,int(4),int(4)> (_S55, _S60, _S65, float4(_S66, _S67, _S68, _S69));
        float3 _S71 = (((float4(rayOrigin_0, 1.0f)) * (_S70))).xyz;
        float3 _S72 = (((float4(rayDirection_0, 0.0f)) * (_S70))).xyz;
        for(;;)
        {
            axis_0 = 0U;
            intervalStart_0 = -3.4028234663852886e+38f;
            intervalEnd_0 = 3.4028234663852886e+38f;
            for(;;)
            {
                if(axis_0 < 3U)
                {
                }
                else
                {
                    _S1 = int(2);
                    break;
                }
                uint _S73 = axis_0;
                if((_S72[axis_0]) == 0.0f)
                {
                    uint _S74 = axis_0;
                    if((_S71[axis_0]) < -0.5f)
                    {
                        diagnosticPixel_0 = true;
                    }
                    else
                    {
                        diagnosticPixel_0 = (_S71[_S74]) > 0.5f;
                    }
                    if(diagnosticPixel_0)
                    {
                        _S1 = int(1);
                        break;
                    }
                }
                else
                {
                    float first_0 = (-0.5f - _S71[axis_0]) / _S72[_S73];
                    float second_0 = (0.5f - _S71[axis_0]) / _S72[_S73];
                    float _S75 = min(intervalEnd_0, max(first_0, second_0));
                    intervalStart_0 = max(intervalStart_0, min(first_0, second_0));
                    intervalEnd_0 = _S75;
                }
                axis_0 = axis_0 + 1U;
            }
            if(_S1 != int(2))
            {
                diagnosticPixel_0 = false;
                break;
            }
            diagnosticPixel_0 = intervalEnd_0 > intervalStart_0;
            break;
        }
        if(diagnosticPixel_0)
        {
            entries_0[field_1] = max(intervalStart_0, intervalStart_1);
            exits_0[field_1] = min(intervalEnd_0, intervalEnd_1);
        }
        else
        {
            entries_0[field_1] = 1.0f;
            exits_0[field_1] = 0.0f;
        }
        uint _S76 = _S50 + 96U;
        float _S77 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S76)>>2]);
        float _S78 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S76 + 4U)>>2]);
        float _S79 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S76 + 8U)>>2]);
        float _S80 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S76 + 12U)>>2]);
        fieldTint_0[field_1] = max(float3(_S77, _S78, _S79), float3(0.0f, 0.0f, 0.0f));
        if((saturate(_S80)) > 0.0f)
        {
            hasActiveField_0 = (exits_0[field_1]) > entries_0[field_1];
        }
        else
        {
            hasActiveField_0 = false;
        }
        fieldEnabled_0[field_1] = hasActiveField_0;
        densityScales_0[field_1] = 0.0f;
        normalizedDensityIntegrals_0[field_1] = 0.0f;
        fieldOpticalDepth_0[field_1] = 0.0f;
        if(hasActiveField_0)
        {
            uint eventCount_1 = eventCount_0 + 1U;
            events_0[eventCount_0] = entries_0[field_1];
            uint _S81 = eventCount_1 + 1U;
            events_0[eventCount_1] = exits_0[field_1];
            eventCount_0 = _S81;
        }
        field_1 = field_1 + 1U;
    }
    axis_0 = 1U;
    for(;;)
    {
        if(axis_0 < eventCount_0)
        {
        }
        else
        {
            break;
        }
        float value_0 = events_0[axis_0];
        cursor_0 = axis_0;
        for(;;)
        {
            if(cursor_0 > 0U)
            {
                diagnosticPixel_0 = (events_0[cursor_0 - 1U]) > value_0;
            }
            else
            {
                diagnosticPixel_0 = false;
            }
            if(diagnosticPixel_0)
            {
            }
            else
            {
                break;
            }
            uint _S82 = cursor_0 - 1U;
            events_0[cursor_0] = events_0[_S82];
            cursor_0 = _S82;
        }
        events_0[cursor_0] = value_0;
        axis_0 = axis_0 + 1U;
    }
    cursor_0 = 0U;
    for(;;)
    {
        uint _S83 = cursor_0 + 1U;
        if(_S83 < eventCount_0)
        {
        }
        else
        {
            break;
        }
        float _S84 = max(events_0[cursor_0], intervalStart_1);
        float _S85 = min(events_0[_S83], intervalEnd_1);
        if(_S85 <= _S84)
        {
            cursor_0 = _S83;
            continue;
        }
        array<float, int(8)> _S86 = entries_0;
        array<float, int(8)> _S87 = exits_0;
        array<bool, int(8)> _S88 = fieldEnabled_0;
        array<uint, int(8)> _S89 = fieldOffsets_0;
        for(;;)
        {
            field_1 = 0U;
            stepCount_0 = 0U;
            hasActiveField_0 = false;
            for(;;)
            {
                if(field_1 < _S7)
                {
                }
                else
                {
                    break;
                }
                if(_S88[field_1])
                {
                    diagnosticPixel_0 = (_S86[field_1]) <= _S84;
                }
                else
                {
                    diagnosticPixel_0 = false;
                }
                if(diagnosticPixel_0)
                {
                    _S2 = (_S87[field_1]) >= _S85;
                }
                else
                {
                    _S2 = false;
                }
                if(_S2)
                {
                    uint _S90 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S89[field_1] + 92U)>>2]);
                    uint stepCount_1 = stepCount_0 + 2U * _S90;
                    hasActiveField_0 = true;
                    stepCount_0 = stepCount_1;
                }
                field_1 = field_1 + 1U;
            }
            if(hasActiveField_0)
            {
                planeIndex_0 = max(1U, stepCount_0);
            }
            else
            {
                planeIndex_0 = 0U;
            }
            break;
        }
        if(planeIndex_0 == 0U)
        {
            cursor_0 = _S83;
            continue;
        }
        float _S91 = (_S85 - _S84) / float(planeIndex_0);
        step_0 = 0U;
        for(;;)
        {
            if(step_0 < planeIndex_0)
            {
            }
            else
            {
                break;
            }
            float _S92 = _S84 + (float(step_0) + 0.5f) * _S91;
            field_0 = 0U;
            for(;;)
            {
                if(field_0 < _S7)
                {
                }
                else
                {
                    break;
                }
                if(!fieldEnabled_0[field_0])
                {
                    diagnosticPixel_0 = true;
                }
                else
                {
                    diagnosticPixel_0 = (entries_0[field_0]) > _S84;
                }
                if(diagnosticPixel_0)
                {
                    _S2 = true;
                }
                else
                {
                    _S2 = (exits_0[field_0]) < _S85;
                }
                if(_S2)
                {
                    field_0 = field_0 + 1U;
                    continue;
                }
                uint _S93 = fieldOffsets_0[field_0];
                float _S94 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_0])>>2]);
                float _S95 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 4U)>>2]);
                float _S96 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 8U)>>2]);
                float _S97 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 12U)>>2]);
                float4 _S98 = float4(_S94, _S95, _S96, _S97);
                float _S99 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 16U)>>2]);
                float _S100 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 20U)>>2]);
                float _S101 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 24U)>>2]);
                float _S102 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 28U)>>2]);
                float4 _S103 = float4(_S99, _S100, _S101, _S102);
                float _S104 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 32U)>>2]);
                float _S105 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 36U)>>2]);
                float _S106 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 40U)>>2]);
                float _S107 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 44U)>>2]);
                float4 _S108 = float4(_S104, _S105, _S106, _S107);
                float _S109 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 48U)>>2]);
                float _S110 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 52U)>>2]);
                float _S111 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 56U)>>2]);
                float _S112 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S93 + 60U)>>2]);
                matrix<float,int(4),int(4)>  _S113 = matrix<float,int(4),int(4)> (_S98, _S103, _S108, float4(_S109, _S110, _S111, _S112));
                float3 _S114 = (((float4(rayDirection_0, 0.0f)) * (_S113))).xyz;
                float3 objectPosition_0 = (((float4(rayOrigin_0, 1.0f)) * (_S113))).xyz + _S114 * float3(_S92) ;
                float localStepLength_0 = length(_S114) * _S91;
                uint _S115 = fieldOffsets_0[field_0];
                for(;;)
                {
                    uint _S116 = _S115 + 80U;
                    uint _S117 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S116)>>2]);
                    uint _S118 = _S116 + 4U;
                    uint _S119 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S118)>>2]);
                    uint _S120 = _S116 + 8U;
                    uint _S121 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S120)>>2]);
                    uint _S122 = _S116 + 12U;
                    uint _S123 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S122)>>2]);
                    float _S124 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S115 + 112U)>>2]);
                    float _S125 = max(0.0f, _S124);
                    if((_S121 & 1U) != 0U)
                    {
                        float3 localCoordinate_0 = saturate(objectPosition_0 + float3(0.5f, 0.5f, 0.5f));
                        uint _S126 = _S115 + 64U;
                        uint _S127 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S126)>>2]);
                        uint _S128 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S126 + 4U)>>2]);
                        uint _S129 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S126 + 8U)>>2]);
                        uint _S130 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S126 + 12U)>>2]);
                        uint _S131 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S116)>>2]);
                        uint _S132 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S118)>>2]);
                        uint _S133 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S120)>>2]);
                        uint _S134 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S122)>>2]);
                        intervalStart_0 = max(0.0f, (((&kernelContext_0)->densityAtlas_0).sample(((&kernelContext_0)->densitySampler_0), ((float3(uint3(_S127, _S128, _S129)) + localCoordinate_0 * float3(float(_S131), float(_S132), float(_S130))) / float3((&kernelContext_0)->VolumeCameraParameters_0->atlasShape_0.xyz)), level((0.0f))).x)) * _S125;
                        break;
                    }
                    intervalStart_0 = _S125;
                    break;
                }
                if(intervalStart_0 > densityScales_0[field_0])
                {
                    float previousScale_0 = densityScales_0[field_0];
                    if((densityScales_0[field_0]) > 0.0f)
                    {
                        normalizedDensityIntegrals_0[field_0] = normalizedDensityIntegrals_0[field_0] * (previousScale_0 / intervalStart_0);
                    }
                    densityScales_0[field_0] = intervalStart_0;
                    normalizedDensityIntegrals_0[field_0] = normalizedDensityIntegrals_0[field_0] + localStepLength_0;
                }
                else
                {
                    if((densityScales_0[field_0]) > 0.0f)
                    {
                        normalizedDensityIntegrals_0[field_0] = normalizedDensityIntegrals_0[field_0] + intervalStart_0 / densityScales_0[field_0] * localStepLength_0;
                    }
                }
                field_0 = field_0 + 1U;
            }
            step_0 = step_0 + 1U;
        }
        cursor_0 = _S83;
    }
    uint _S135 = _S9.x;
    if(_S135 == 18U)
    {
        diagnosticPixel_0 = (_S9.y) == 14U;
    }
    else
    {
        diagnosticPixel_0 = false;
    }
    if(diagnosticPixel_0)
    {
        float _S136 = as_type<float>((&kernelContext_0)->volumeFields_0[(112U)>>2]);
        diagnosticPixel_0 = _S136 >= 1.00000002004087734e+20f;
    }
    else
    {
        diagnosticPixel_0 = false;
    }
    if(diagnosticPixel_0)
    {
        pixelOutput_0 _S137 = { float4(entries_0[int(0)], exits_0[int(0)], densityScales_0[int(0)], normalizedDensityIntegrals_0[int(0)]) };
        return _S137;
    }
    field_1 = 0U;
    for(;;)
    {
        if(field_1 < _S7)
        {
        }
        else
        {
            break;
        }
        float _S138 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_1] + 108U)>>2]);
        if(fieldEnabled_0[field_1])
        {
            float _S139 = densityScales_0[field_1];
            float _S140 = normalizedDensityIntegrals_0[field_1];
            for(;;)
            {
                float _S141 = max(0.0f, _S139);
                float _S142 = max(0.0f, _S140);
                if(_S141 <= 0.0f)
                {
                    diagnosticPixel_0 = true;
                }
                else
                {
                    diagnosticPixel_0 = _S142 <= 0.0f;
                }
                if(diagnosticPixel_0)
                {
                    intervalStart_0 = 0.0f;
                    break;
                }
                if(_S141 >= (2.12676466649080537e+37f / _S142))
                {
                    intervalStart_0 = 2.12676466649080537e+37f;
                }
                else
                {
                    intervalStart_0 = _S141 * _S142;
                }
                for(;;)
                {
                    float _S143 = max(0.0f, intervalStart_0);
                    float boundedOpacity_0 = saturate(_S138);
                    if(boundedOpacity_0 <= 0.0f)
                    {
                        hasActiveField_0 = true;
                    }
                    else
                    {
                        hasActiveField_0 = _S143 <= 0.0f;
                    }
                    if(hasActiveField_0)
                    {
                        intervalEnd_0 = 0.0f;
                        break;
                    }
                    if(boundedOpacity_0 >= 1.0f)
                    {
                        intervalEnd_0 = _S143;
                        break;
                    }
                    for(;;)
                    {
                        float _S144 = max(_S143, 0.0f);
                        if(_S144 >= 0.125f)
                        {
                            intervalEnd_0 = 1.0f - exp(- _S144);
                            break;
                        }
                        order_0 = 2U;
                        term_0 = _S144;
                        sum_0 = _S144;
                        for(;;)
                        {
                            if(order_0 <= 8U)
                            {
                            }
                            else
                            {
                                break;
                            }
                            float term_1 = term_0 * (_S144 / float(order_0));
                            if((order_0 & 1U) != 0U)
                            {
                                intervalEnd_0 = term_1;
                            }
                            else
                            {
                                intervalEnd_0 = - term_1;
                            }
                            float sum_1 = sum_0 + intervalEnd_0;
                            order_0 = order_0 + 1U;
                            term_0 = term_1;
                            sum_0 = sum_1;
                        }
                        intervalEnd_0 = sum_0;
                        break;
                    }
                    float removedFraction_0 = boundedOpacity_0 * intervalEnd_0;
                    for(;;)
                    {
                        if(removedFraction_0 >= 0.125f)
                        {
                            power_0 = - log(1.0f - removedFraction_0);
                            break;
                        }
                        order_0 = 2U;
                        power_0 = removedFraction_0;
                        sum_0 = removedFraction_0;
                        for(;;)
                        {
                            if(order_0 <= 8U)
                            {
                            }
                            else
                            {
                                break;
                            }
                            float power_1 = power_0 * removedFraction_0;
                            float sum_2 = sum_0 + power_1 / float(order_0);
                            order_0 = order_0 + 1U;
                            power_0 = power_1;
                            sum_0 = sum_2;
                        }
                        power_0 = sum_0;
                        break;
                    }
                    intervalEnd_0 = power_0;
                    break;
                }
                intervalStart_0 = intervalEnd_0;
                break;
            }
        }
        else
        {
            intervalStart_0 = 0.0f;
        }
        fieldOpticalDepth_0[field_1] = intervalStart_0;
        field_1 = field_1 + 1U;
    }
    float3 _S145 = float3(0.0f, 0.0f, 0.0f);
    if(_S135 == 20U)
    {
        diagnosticPixel_0 = (_S9.y) == 14U;
    }
    else
    {
        diagnosticPixel_0 = false;
    }
    if(diagnosticPixel_0)
    {
        float _S146 = as_type<float>((&kernelContext_0)->volumeFields_0[(112U)>>2]);
        diagnosticPixel_0 = _S146 >= 1.00000002004087734e+20f;
    }
    else
    {
        diagnosticPixel_0 = false;
    }
    field_1 = 0U;
    intervalStart_0 = 0.0f;
    float3 integratedTintedOpticalDepth_0 = _S145;
    for(;;)
    {
        if(field_1 < _S7)
        {
        }
        else
        {
            break;
        }
        float totalFieldOpticalDepth_0 = intervalStart_0 + fieldOpticalDepth_0[field_1];
        float3 integratedTintedOpticalDepth_1 = integratedTintedOpticalDepth_0 + fieldTint_0[field_1] * float3(fieldOpticalDepth_0[field_1]) ;
        field_1 = field_1 + 1U;
        intervalStart_0 = totalFieldOpticalDepth_0;
        integratedTintedOpticalDepth_0 = integratedTintedOpticalDepth_1;
    }
    if(_S135 == 19U)
    {
        hasActiveField_0 = (_S9.y) == 14U;
    }
    else
    {
        hasActiveField_0 = false;
    }
    if(hasActiveField_0)
    {
        float _S147 = as_type<float>((&kernelContext_0)->volumeFields_0[(112U)>>2]);
        hasActiveField_0 = _S147 >= 1.00000002004087734e+20f;
    }
    else
    {
        hasActiveField_0 = false;
    }
    if(hasActiveField_0)
    {
        pixelOutput_0 _S148 = { float4(fieldOpticalDepth_0[int(0)], intervalStart_0, integratedTintedOpticalDepth_0.x, normalizedDensityIntegrals_0[int(0)]) };
        return _S148;
    }
    float accumulatedOpticalDepth_0;
    float3 outputColor_0;
    float3 accumulatedColor_0;
    float3 diagnosticValues_0;
    if(intervalStart_0 <= 0.00100000004749745f)
    {
        if(intervalStart_0 <= 0.0f)
        {
            pixelOutput_0 _S149 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
            return _S149;
        }
        for(;;)
        {
            float _S150 = max(intervalStart_0, 0.0f);
            if(_S150 >= 0.125f)
            {
                intervalStart_1 = 1.0f - exp(- _S150);
                break;
            }
            order_0 = 2U;
            term_0 = _S150;
            sum_0 = _S150;
            for(;;)
            {
                if(order_0 <= 8U)
                {
                }
                else
                {
                    break;
                }
                float term_2 = term_0 * (_S150 / float(order_0));
                if((order_0 & 1U) != 0U)
                {
                    intervalStart_1 = term_2;
                }
                else
                {
                    intervalStart_1 = - term_2;
                }
                float sum_3 = sum_0 + intervalStart_1;
                order_0 = order_0 + 1U;
                term_0 = term_2;
                sum_0 = sum_3;
            }
            intervalStart_1 = sum_0;
            break;
        }
        float3 _S151 = integratedTintedOpticalDepth_0 * float3((intervalStart_1 / intervalStart_0)) ;
        accumulatedOpticalDepth_0 = intervalStart_0;
        accumulatedColor_0 = _S151;
        term_0 = 0.0f;
        diagnosticValues_0 = _S145;
    }
    else
    {
        cursor_0 = 0U;
        intervalEnd_0 = 0.0f;
        outputColor_0 = _S145;
        power_0 = 0.0f;
        float3 accumulatedColor_1 = _S145;
        for(;;)
        {
            uint _S152 = cursor_0 + 1U;
            if(_S152 < eventCount_0)
            {
            }
            else
            {
                break;
            }
            float _S153 = max(events_0[cursor_0], intervalStart_1);
            float _S154 = min(events_0[_S152], intervalEnd_1);
            if(_S154 <= _S153)
            {
                term_0 = intervalEnd_0;
                diagnosticValues_0 = outputColor_0;
                accumulatedOpticalDepth_0 = power_0;
                accumulatedColor_0 = accumulatedColor_1;
                cursor_0 = _S152;
                intervalEnd_0 = term_0;
                outputColor_0 = diagnosticValues_0;
                power_0 = accumulatedOpticalDepth_0;
                accumulatedColor_1 = accumulatedColor_0;
                continue;
            }
            thread array<bool, int(8)> active_0;
            field_1 = 0U;
            for(;;)
            {
                if(field_1 < _S7)
                {
                }
                else
                {
                    break;
                }
                if(fieldEnabled_0[field_1])
                {
                    hasActiveField_0 = (normalizedDensityIntegrals_0[field_1]) > 0.0f;
                }
                else
                {
                    hasActiveField_0 = false;
                }
                if(hasActiveField_0)
                {
                    _S2 = (entries_0[field_1]) <= _S153;
                }
                else
                {
                    _S2 = false;
                }
                if(_S2)
                {
                    _S3 = (exits_0[field_1]) >= _S154;
                }
                else
                {
                    _S3 = false;
                }
                active_0[field_1] = _S3;
                field_1 = field_1 + 1U;
            }
            array<float, int(8)> _S155 = entries_0;
            array<float, int(8)> _S156 = exits_0;
            array<bool, int(8)> _S157 = fieldEnabled_0;
            array<uint, int(8)> _S158 = fieldOffsets_0;
            for(;;)
            {
                field_0 = 0U;
                stepCount_0 = 0U;
                hasActiveField_0 = false;
                for(;;)
                {
                    if(field_0 < _S7)
                    {
                    }
                    else
                    {
                        break;
                    }
                    if(_S157[field_0])
                    {
                        _S2 = (_S155[field_0]) <= _S153;
                    }
                    else
                    {
                        _S2 = false;
                    }
                    if(_S2)
                    {
                        _S3 = (_S156[field_0]) >= _S154;
                    }
                    else
                    {
                        _S3 = false;
                    }
                    if(_S3)
                    {
                        uint _S159 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S158[field_0] + 92U)>>2]);
                        uint stepCount_2 = stepCount_0 + 2U * _S159;
                        hasActiveField_0 = true;
                        stepCount_0 = stepCount_2;
                    }
                    field_0 = field_0 + 1U;
                }
                if(hasActiveField_0)
                {
                    planeIndex_0 = max(1U, stepCount_0);
                }
                else
                {
                    planeIndex_0 = 0U;
                }
                break;
            }
            if(planeIndex_0 == 0U)
            {
                term_0 = intervalEnd_0;
                diagnosticValues_0 = outputColor_0;
                accumulatedOpticalDepth_0 = power_0;
                accumulatedColor_0 = accumulatedColor_1;
                cursor_0 = _S152;
                intervalEnd_0 = term_0;
                outputColor_0 = diagnosticValues_0;
                power_0 = accumulatedOpticalDepth_0;
                accumulatedColor_1 = accumulatedColor_0;
                continue;
            }
            float _S160 = (_S154 - _S153) / float(planeIndex_0);
            step_0 = 0U;
            float diagnosticStage_0 = intervalEnd_0;
            float3 diagnosticValues_1 = outputColor_0;
            accumulatedOpticalDepth_0 = power_0;
            accumulatedColor_0 = accumulatedColor_1;
            for(;;)
            {
                if(step_0 < planeIndex_0)
                {
                }
                else
                {
                    break;
                }
                float _S161 = _S153 + (float(step_0) + 0.5f) * _S160;
                thread array<float, int(8)> fieldDepths_0;
                uint field_2 = 0U;
                float diagnosticStage_1 = diagnosticStage_0;
                float3 diagnosticValues_2 = diagnosticValues_1;
                float opticalDepth_0 = 0.0f;
                for(;;)
                {
                    if(field_2 < _S7)
                    {
                    }
                    else
                    {
                        break;
                    }
                    fieldDepths_0[field_2] = 0.0f;
                    if(!active_0[field_2])
                    {
                        term_0 = diagnosticStage_1;
                        diagnosticValues_0 = diagnosticValues_2;
                        field_2 = field_2 + 1U;
                        diagnosticStage_1 = term_0;
                        diagnosticValues_2 = diagnosticValues_0;
                        continue;
                    }
                    uint _S162 = fieldOffsets_0[field_2];
                    float _S163 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_2])>>2]);
                    float _S164 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 4U)>>2]);
                    float _S165 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 8U)>>2]);
                    float _S166 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 12U)>>2]);
                    float4 _S167 = float4(_S163, _S164, _S165, _S166);
                    float _S168 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 16U)>>2]);
                    float _S169 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 20U)>>2]);
                    float _S170 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 24U)>>2]);
                    float _S171 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 28U)>>2]);
                    float4 _S172 = float4(_S168, _S169, _S170, _S171);
                    float _S173 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 32U)>>2]);
                    float _S174 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 36U)>>2]);
                    float _S175 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 40U)>>2]);
                    float _S176 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 44U)>>2]);
                    float4 _S177 = float4(_S173, _S174, _S175, _S176);
                    float _S178 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 48U)>>2]);
                    float _S179 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 52U)>>2]);
                    float _S180 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 56U)>>2]);
                    float _S181 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S162 + 60U)>>2]);
                    matrix<float,int(4),int(4)>  _S182 = matrix<float,int(4),int(4)> (_S167, _S172, _S177, float4(_S178, _S179, _S180, _S181));
                    float3 _S183 = (((float4(rayDirection_0, 0.0f)) * (_S182))).xyz;
                    float3 objectPosition_1 = (((float4(rayOrigin_0, 1.0f)) * (_S182))).xyz + _S183 * float3(_S161) ;
                    uint _S184 = fieldOffsets_0[field_2];
                    for(;;)
                    {
                        uint _S185 = _S184 + 80U;
                        uint _S186 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S185)>>2]);
                        uint _S187 = _S185 + 4U;
                        uint _S188 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S187)>>2]);
                        uint _S189 = _S185 + 8U;
                        uint _S190 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S189)>>2]);
                        uint _S191 = _S185 + 12U;
                        uint _S192 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S191)>>2]);
                        float _S193 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S184 + 112U)>>2]);
                        float _S194 = max(0.0f, _S193);
                        if((_S190 & 1U) != 0U)
                        {
                            float3 localCoordinate_1 = saturate(objectPosition_1 + float3(0.5f, 0.5f, 0.5f));
                            uint _S195 = _S184 + 64U;
                            uint _S196 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S195)>>2]);
                            uint _S197 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S195 + 4U)>>2]);
                            uint _S198 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S195 + 8U)>>2]);
                            uint _S199 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S195 + 12U)>>2]);
                            uint _S200 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S185)>>2]);
                            uint _S201 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S187)>>2]);
                            uint _S202 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S189)>>2]);
                            uint _S203 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S191)>>2]);
                            intervalStart_0 = max(0.0f, (((&kernelContext_0)->densityAtlas_0).sample(((&kernelContext_0)->densitySampler_0), ((float3(uint3(_S196, _S197, _S198)) + localCoordinate_1 * float3(float(_S200), float(_S201), float(_S199))) / float3((&kernelContext_0)->VolumeCameraParameters_0->atlasShape_0.xyz)), level((0.0f))).x)) * _S194;
                            break;
                        }
                        intervalStart_0 = _S194;
                        break;
                    }
                    float localStepLength_1 = length(_S183) * _S160;
                    float densityFraction_0 = intervalStart_0 / densityScales_0[field_2];
                    float stepFraction_0 = densityFraction_0 * localStepLength_1 / normalizedDensityIntegrals_0[field_2];
                    float _S204 = fieldOpticalDepth_0[field_2] * stepFraction_0;
                    fieldDepths_0[field_2] = _S204;
                    float opticalDepth_1 = opticalDepth_0 + _S204;
                    if(diagnosticPixel_0)
                    {
                        _S2 = diagnosticStage_1 == 0.0f;
                    }
                    else
                    {
                        _S2 = false;
                    }
                    if(_S2)
                    {
                        if(!isfinite(localStepLength_1))
                        {
                            _S3 = true;
                        }
                        else
                        {
                            _S3 = !isfinite(densityFraction_0);
                        }
                        if(_S3)
                        {
                            _S4 = true;
                        }
                        else
                        {
                            _S4 = !isfinite(stepFraction_0);
                        }
                        if(_S4)
                        {
                            _S5 = true;
                        }
                        else
                        {
                            _S5 = !isfinite(fieldDepths_0[field_2]);
                        }
                        if(_S5)
                        {
                            _S6 = true;
                        }
                        else
                        {
                            _S6 = !isfinite(opticalDepth_1);
                        }
                        _S3 = _S6;
                    }
                    else
                    {
                        _S3 = false;
                    }
                    if(_S3)
                    {
                        float3 _S205 = float3(localStepLength_1, stepFraction_0, fieldDepths_0[field_2]);
                        term_0 = 1.0f;
                        diagnosticValues_0 = _S205;
                    }
                    else
                    {
                        term_0 = diagnosticStage_1;
                        diagnosticValues_0 = diagnosticValues_2;
                    }
                    opticalDepth_0 = opticalDepth_1;
                    field_2 = field_2 + 1U;
                    diagnosticStage_1 = term_0;
                    diagnosticValues_2 = diagnosticValues_0;
                }
                if(opticalDepth_0 > 0.0f)
                {
                    float transmittance_0 = exp(- accumulatedOpticalDepth_0);
                    for(;;)
                    {
                        float _S206 = max(opticalDepth_0, 0.0f);
                        if(_S206 >= 0.125f)
                        {
                            intervalStart_0 = 1.0f - exp(- _S206);
                            break;
                        }
                        order_0 = 2U;
                        term_0 = _S206;
                        sum_0 = _S206;
                        for(;;)
                        {
                            if(order_0 <= 8U)
                            {
                            }
                            else
                            {
                                break;
                            }
                            float term_3 = term_0 * (_S206 / float(order_0));
                            if((order_0 & 1U) != 0U)
                            {
                                intervalStart_0 = term_3;
                            }
                            else
                            {
                                intervalStart_0 = - term_3;
                            }
                            float sum_4 = sum_0 + intervalStart_0;
                            order_0 = order_0 + 1U;
                            term_0 = term_3;
                            sum_0 = sum_4;
                        }
                        intervalStart_0 = sum_0;
                        break;
                    }
                    order_0 = 0U;
                    float3 source_0 = _S145;
                    for(;;)
                    {
                        if(order_0 < _S7)
                        {
                        }
                        else
                        {
                            break;
                        }
                        if((fieldDepths_0[order_0]) > 0.0f)
                        {
                            source_0 = source_0 + fieldTint_0[order_0] * float3((fieldDepths_0[order_0] / opticalDepth_0)) ;
                        }
                        order_0 = order_0 + 1U;
                    }
                    float3 accumulatedColor_2 = accumulatedColor_0 + float3((transmittance_0 * intervalStart_0))  * source_0;
                    float accumulatedOpticalDepth_1 = accumulatedOpticalDepth_0 + opticalDepth_0;
                    if(diagnosticPixel_0)
                    {
                        _S2 = diagnosticStage_1 == 0.0f;
                    }
                    else
                    {
                        _S2 = false;
                    }
                    if(_S2)
                    {
                        if(!isfinite(opticalDepth_0))
                        {
                            _S3 = true;
                        }
                        else
                        {
                            _S3 = !isfinite(transmittance_0);
                        }
                        if(_S3)
                        {
                            _S4 = true;
                        }
                        else
                        {
                            _S4 = !isfinite(intervalStart_0);
                        }
                        if(_S4)
                        {
                            _S5 = true;
                        }
                        else
                        {
                            _S5 = !isfinite(source_0.x);
                        }
                        if(_S5)
                        {
                            _S6 = true;
                        }
                        else
                        {
                            _S6 = !isfinite(accumulatedOpticalDepth_1);
                        }
                        bool _S207;
                        if(_S6)
                        {
                            _S207 = true;
                        }
                        else
                        {
                            _S207 = !isfinite(accumulatedColor_2.x);
                        }
                        _S3 = _S207;
                    }
                    else
                    {
                        _S3 = false;
                    }
                    if(_S3)
                    {
                        float3 _S208 = float3(opticalDepth_0, source_0.x, accumulatedOpticalDepth_1);
                        term_0 = 2.0f;
                        diagnosticValues_0 = _S208;
                    }
                    else
                    {
                        term_0 = diagnosticStage_1;
                        diagnosticValues_0 = diagnosticValues_2;
                    }
                    diagnosticStage_0 = term_0;
                    diagnosticValues_1 = diagnosticValues_0;
                    accumulatedOpticalDepth_0 = accumulatedOpticalDepth_1;
                    accumulatedColor_0 = accumulatedColor_2;
                }
                else
                {
                    diagnosticStage_0 = diagnosticStage_1;
                    diagnosticValues_1 = diagnosticValues_2;
                }
                step_0 = step_0 + 1U;
            }
            term_0 = diagnosticStage_0;
            diagnosticValues_0 = diagnosticValues_1;
            cursor_0 = _S152;
            intervalEnd_0 = term_0;
            outputColor_0 = diagnosticValues_0;
            power_0 = accumulatedOpticalDepth_0;
            accumulatedColor_1 = accumulatedColor_0;
        }
        accumulatedOpticalDepth_0 = power_0;
        accumulatedColor_0 = accumulatedColor_1;
        term_0 = intervalEnd_0;
        diagnosticValues_0 = outputColor_0;
    }
    if(accumulatedOpticalDepth_0 <= 0.0f)
    {
        pixelOutput_0 _S209 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S209;
    }
    for(;;)
    {
        float _S210 = max(accumulatedOpticalDepth_0, 0.0f);
        if(_S210 >= 0.125f)
        {
            intervalStart_1 = 1.0f - exp(- _S210);
            break;
        }
        order_0 = 2U;
        intervalEnd_0 = _S210;
        sum_0 = _S210;
        for(;;)
        {
            if(order_0 <= 8U)
            {
            }
            else
            {
                break;
            }
            float term_4 = intervalEnd_0 * (_S210 / float(order_0));
            if((order_0 & 1U) != 0U)
            {
                intervalStart_1 = term_4;
            }
            else
            {
                intervalStart_1 = - term_4;
            }
            float sum_5 = sum_0 + intervalStart_1;
            order_0 = order_0 + 1U;
            intervalEnd_0 = term_4;
            sum_0 = sum_5;
        }
        intervalStart_1 = sum_0;
        break;
    }
    float3 outputColor_1 = accumulatedColor_0 / float3(intervalStart_1) ;
    uint _S211 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
    float3 outputColor_2 = outputColor_1 * float3(exp2((as_type<float>((_S211))))) ;
    uint _S212 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
    if(_S212 == 1U)
    {
        outputColor_0 = outputColor_2 / (float3(1.0f)  + max(outputColor_2, _S145));
    }
    else
    {
        outputColor_0 = outputColor_2;
    }
    if(diagnosticPixel_0)
    {
        float4 _S213;
        if(term_0 == 0.0f)
        {
            _S213 = float4(0.0f, accumulatedOpticalDepth_0, accumulatedColor_0.x, outputColor_0.x);
        }
        else
        {
            _S213 = float4(term_0, diagnosticValues_0);
        }
        pixelOutput_0 _S214 = { _S213 };
        return _S214;
    }
    pixelOutput_0 _S215 = { float4(outputColor_0, intervalStart_1) };
    return _S215;
}
