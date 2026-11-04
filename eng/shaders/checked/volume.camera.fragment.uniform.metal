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
    uint32_t device* volumeFields_0;
    uint32_t device* frameParameters_0;
    texture2d<float, access::sample> opaqueDepthTexture_0;
};

[[fragment]] pixelOutput_0 volumeCameraUniformFragmentMain(float4 position_0 [[position]], SLANG_ParameterGroup_VolumeCameraParameters_0 constant* VolumeCameraParameters_1 [[buffer(0)]], uint32_t device* volumeFields_1 [[buffer(6)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> opaqueDepthTexture_1 [[texture(28)]])
{
    float intervalStart_0;
    float intervalEnd_0;
    bool hasActiveField_0;
    bool _S1;
    uint axis_0;
    int _S2;
    uint cursor_0;
    bool _S3;
    uint field_0;
    uint stepCount_0;
    uint step_0;
    float power_0;
    float term_0;
    float sum_0;
    uint order_0;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->VolumeCameraParameters_0 = VolumeCameraParameters_1;
    (&kernelContext_0)->volumeFields_0 = volumeFields_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->opaqueDepthTexture_0 = opaqueDepthTexture_1;
    uint4 frameShape_0 = VolumeCameraParameters_1->volumeHeader_0;
    uint _S4 = min(VolumeCameraParameters_1->volumeHeader_0.x, 8U);
    bool diagnosticPixel_0;
    if(_S4 == 0U)
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
        pixelOutput_0 _S5 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S5;
    }
    uint2 _S6 = uint2(position_0.xy);
    if((_S6.x) == 16U)
    {
        diagnosticPixel_0 = (_S6.y) == 16U;
    }
    else
    {
        diagnosticPixel_0 = false;
    }
    if(diagnosticPixel_0)
    {
        float _S7 = as_type<float>((&kernelContext_0)->volumeFields_0[(112U)>>2]);
        diagnosticPixel_0 = _S7 >= 1.00000002004087734e+20f;
    }
    else
    {
        diagnosticPixel_0 = false;
    }
    float2 pixelCoordinate_0 = (float2(_S6) + float2(0.5f) ) / float2(frameShape_0.yz);
    float _S8 = pixelCoordinate_0.x * 2.0f - 1.0f;
    float intervalStart_1;
    if((frameShape_0.w) != 0U)
    {
        intervalStart_1 = pixelCoordinate_0.y * 2.0f - 1.0f;
    }
    else
    {
        intervalStart_1 = 1.0f - pixelCoordinate_0.y * 2.0f;
    }
    float2 clipXY_0 = float2(_S8, intervalStart_1);
    float _S9 = as_type<float>((&kernelContext_0)->frameParameters_0[(0U)>>2]);
    float _S10 = as_type<float>((&kernelContext_0)->frameParameters_0[(4U)>>2]);
    float _S11 = as_type<float>((&kernelContext_0)->frameParameters_0[(8U)>>2]);
    float _S12 = as_type<float>((&kernelContext_0)->frameParameters_0[(12U)>>2]);
    float4 _S13 = float4(_S9, _S10, _S11, _S12);
    float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(16U)>>2]);
    float _S15 = as_type<float>((&kernelContext_0)->frameParameters_0[(20U)>>2]);
    float _S16 = as_type<float>((&kernelContext_0)->frameParameters_0[(24U)>>2]);
    float _S17 = as_type<float>((&kernelContext_0)->frameParameters_0[(28U)>>2]);
    float4 _S18 = float4(_S14, _S15, _S16, _S17);
    float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(32U)>>2]);
    float _S20 = as_type<float>((&kernelContext_0)->frameParameters_0[(36U)>>2]);
    float _S21 = as_type<float>((&kernelContext_0)->frameParameters_0[(40U)>>2]);
    float _S22 = as_type<float>((&kernelContext_0)->frameParameters_0[(44U)>>2]);
    float4 _S23 = float4(_S19, _S20, _S21, _S22);
    float _S24 = as_type<float>((&kernelContext_0)->frameParameters_0[(48U)>>2]);
    float _S25 = as_type<float>((&kernelContext_0)->frameParameters_0[(52U)>>2]);
    float _S26 = as_type<float>((&kernelContext_0)->frameParameters_0[(56U)>>2]);
    float _S27 = as_type<float>((&kernelContext_0)->frameParameters_0[(60U)>>2]);
    matrix<float,int(4),int(4)>  _S28 = matrix<float,int(4),int(4)> (_S13, _S18, _S23, float4(_S24, _S25, _S26, _S27));
    float _S29 = as_type<float>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
    float4 nearHomogeneous_0 = (((float4(clipXY_0, 0.0f, 1.0f)) * (_S28)));
    float4 farHomogeneous_0 = (((float4(clipXY_0, 1.0f, 1.0f)) * (_S28)));
    float _S30 = nearHomogeneous_0.w;
    if(_S30 == 0.0f)
    {
        pixelOutput_0 _S31 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S31;
    }
    float3 rayOrigin_0 = nearHomogeneous_0.xyz / float3(_S30) ;
    float _S32 = farHomogeneous_0.w;
    bool _S33 = _S32 == 0.0f;
    float3 rayDirection_0;
    if(_S33)
    {
        rayDirection_0 = normalize(farHomogeneous_0.xyz * float3(float((int(sign((_S30)))))) );
    }
    else
    {
        rayDirection_0 = normalize(farHomogeneous_0.xyz / float3(_S32)  - rayOrigin_0);
    }
    if(_S33)
    {
        intervalStart_1 = 3.4028234663852886e+38f;
    }
    else
    {
        intervalStart_1 = dot(farHomogeneous_0.xyz / float3(_S32)  - rayOrigin_0, rayDirection_0);
    }
    uint _S34 = as_type<uint>((&kernelContext_0)->frameParameters_0[(64U)>>2]);
    uint _S35 = min(_S34, 8U);
    float _S36 = intervalStart_1;
    uint planeIndex_0 = 0U;
    intervalStart_1 = 0.0f;
    float intervalEnd_1 = _S36;
    for(;;)
    {
        if(planeIndex_0 < _S35)
        {
        }
        else
        {
            break;
        }
        uint _S37 = 80U + planeIndex_0 * 16U;
        float _S38 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S37)>>2]);
        float _S39 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S37 + 4U)>>2]);
        float _S40 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S37 + 8U)>>2]);
        float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S37 + 12U)>>2]);
        float3 _S42 = float3(_S38, _S39, _S40);
        float offset_0 = dot(_S42, rayOrigin_0) + _S41;
        float slope_0 = dot(_S42, rayDirection_0);
        if(slope_0 == 0.0f)
        {
            if(offset_0 < 0.0f)
            {
                pixelOutput_0 _S43 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
                return _S43;
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
                float _S44 = min(intervalEnd_1, - offset_0 / slope_0);
                intervalStart_0 = intervalStart_1;
                intervalEnd_0 = _S44;
            }
            intervalStart_1 = intervalStart_0;
            intervalEnd_1 = intervalEnd_0;
        }
        planeIndex_0 = planeIndex_0 + 1U;
    }
    int3 _S45 = int3(int2(_S6), int(0));
    float opaqueDepth_0 = (((&kernelContext_0)->opaqueDepthTexture_0).read(vec<uint,2>(((_S45)).xy), uint(((_S45)).z)).x);
    if(opaqueDepth_0 != _S29)
    {
        float4 opaqueHomogeneous_0 = (((float4(clipXY_0, opaqueDepth_0, 1.0f)) * (_S28)));
        float _S46 = opaqueHomogeneous_0.w;
        if(_S46 != 0.0f)
        {
            intervalEnd_1 = min(intervalEnd_1, dot(opaqueHomogeneous_0.xyz / float3(_S46)  - rayOrigin_0, rayDirection_0));
        }
    }
    if(intervalEnd_1 <= intervalStart_1)
    {
        pixelOutput_0 _S47 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S47;
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
        if(field_1 < _S4)
        {
        }
        else
        {
            break;
        }
        uint _S48 = field_1 * 128U;
        fieldOffsets_0[field_1] = _S48;
        float _S49 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48)>>2]);
        float _S50 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 4U)>>2]);
        float _S51 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 8U)>>2]);
        float _S52 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 12U)>>2]);
        float4 _S53 = float4(_S49, _S50, _S51, _S52);
        float _S54 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 16U)>>2]);
        float _S55 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 20U)>>2]);
        float _S56 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 24U)>>2]);
        float _S57 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 28U)>>2]);
        float4 _S58 = float4(_S54, _S55, _S56, _S57);
        float _S59 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 32U)>>2]);
        float _S60 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 36U)>>2]);
        float _S61 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 40U)>>2]);
        float _S62 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 44U)>>2]);
        float4 _S63 = float4(_S59, _S60, _S61, _S62);
        float _S64 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 48U)>>2]);
        float _S65 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 52U)>>2]);
        float _S66 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 56U)>>2]);
        float _S67 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S48 + 60U)>>2]);
        matrix<float,int(4),int(4)>  _S68 = matrix<float,int(4),int(4)> (_S53, _S58, _S63, float4(_S64, _S65, _S66, _S67));
        float3 _S69 = (((float4(rayOrigin_0, 1.0f)) * (_S68))).xyz;
        float3 _S70 = (((float4(rayDirection_0, 0.0f)) * (_S68))).xyz;
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
                    _S2 = int(2);
                    break;
                }
                uint _S71 = axis_0;
                if((_S70[axis_0]) == 0.0f)
                {
                    uint _S72 = axis_0;
                    if((_S69[axis_0]) < -0.5f)
                    {
                        hasActiveField_0 = true;
                    }
                    else
                    {
                        hasActiveField_0 = (_S69[_S72]) > 0.5f;
                    }
                    if(hasActiveField_0)
                    {
                        _S2 = int(1);
                        break;
                    }
                }
                else
                {
                    float first_0 = (-0.5f - _S69[axis_0]) / _S70[_S71];
                    float second_0 = (0.5f - _S69[axis_0]) / _S70[_S71];
                    float _S73 = min(intervalEnd_0, max(first_0, second_0));
                    intervalStart_0 = max(intervalStart_0, min(first_0, second_0));
                    intervalEnd_0 = _S73;
                }
                axis_0 = axis_0 + 1U;
            }
            if(_S2 != int(2))
            {
                hasActiveField_0 = false;
                break;
            }
            hasActiveField_0 = intervalEnd_0 > intervalStart_0;
            break;
        }
        if(hasActiveField_0)
        {
            entries_0[field_1] = max(intervalStart_0, intervalStart_1);
            exits_0[field_1] = min(intervalEnd_0, intervalEnd_1);
        }
        else
        {
            entries_0[field_1] = 1.0f;
            exits_0[field_1] = 0.0f;
        }
        uint _S74 = _S48 + 96U;
        float _S75 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S74)>>2]);
        float _S76 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S74 + 4U)>>2]);
        float _S77 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S74 + 8U)>>2]);
        float _S78 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S74 + 12U)>>2]);
        fieldTint_0[field_1] = max(float3(_S75, _S76, _S77), float3(0.0f, 0.0f, 0.0f));
        if((saturate(_S78)) > 0.0f)
        {
            _S1 = (exits_0[field_1]) > entries_0[field_1];
        }
        else
        {
            _S1 = false;
        }
        fieldEnabled_0[field_1] = _S1;
        densityScales_0[field_1] = 0.0f;
        normalizedDensityIntegrals_0[field_1] = 0.0f;
        fieldOpticalDepth_0[field_1] = 0.0f;
        if(_S1)
        {
            uint eventCount_1 = eventCount_0 + 1U;
            events_0[eventCount_0] = entries_0[field_1];
            uint _S79 = eventCount_1 + 1U;
            events_0[eventCount_1] = exits_0[field_1];
            eventCount_0 = _S79;
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
                hasActiveField_0 = (events_0[cursor_0 - 1U]) > value_0;
            }
            else
            {
                hasActiveField_0 = false;
            }
            if(hasActiveField_0)
            {
            }
            else
            {
                break;
            }
            uint _S80 = cursor_0 - 1U;
            events_0[cursor_0] = events_0[_S80];
            cursor_0 = _S80;
        }
        events_0[cursor_0] = value_0;
        axis_0 = axis_0 + 1U;
    }
    cursor_0 = 0U;
    for(;;)
    {
        uint _S81 = cursor_0 + 1U;
        if(_S81 < eventCount_0)
        {
        }
        else
        {
            break;
        }
        float _S82 = max(events_0[cursor_0], intervalStart_1);
        float _S83 = min(events_0[_S81], intervalEnd_1);
        if(_S83 <= _S82)
        {
            cursor_0 = _S81;
            continue;
        }
        array<float, int(8)> _S84 = entries_0;
        array<float, int(8)> _S85 = exits_0;
        array<bool, int(8)> _S86 = fieldEnabled_0;
        array<uint, int(8)> _S87 = fieldOffsets_0;
        for(;;)
        {
            field_1 = 0U;
            stepCount_0 = 0U;
            hasActiveField_0 = false;
            for(;;)
            {
                if(field_1 < _S4)
                {
                }
                else
                {
                    break;
                }
                if(_S86[field_1])
                {
                    _S1 = (_S84[field_1]) <= _S82;
                }
                else
                {
                    _S1 = false;
                }
                if(_S1)
                {
                    _S3 = (_S85[field_1]) >= _S83;
                }
                else
                {
                    _S3 = false;
                }
                if(_S3)
                {
                    uint _S88 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S87[field_1] + 92U)>>2]);
                    uint stepCount_1 = stepCount_0 + 2U * _S88;
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
            cursor_0 = _S81;
            continue;
        }
        float _S89 = (_S83 - _S82) / float(planeIndex_0);
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
            field_0 = 0U;
            for(;;)
            {
                if(field_0 < _S4)
                {
                }
                else
                {
                    break;
                }
                if(!fieldEnabled_0[field_0])
                {
                    _S1 = true;
                }
                else
                {
                    _S1 = (entries_0[field_0]) > _S82;
                }
                if(_S1)
                {
                    _S3 = true;
                }
                else
                {
                    _S3 = (exits_0[field_0]) < _S83;
                }
                if(_S3)
                {
                    field_0 = field_0 + 1U;
                    continue;
                }
                uint _S90 = fieldOffsets_0[field_0];
                float _S91 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_0])>>2]);
                float _S92 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 4U)>>2]);
                float _S93 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 8U)>>2]);
                float _S94 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 12U)>>2]);
                float4 _S95 = float4(_S91, _S92, _S93, _S94);
                float _S96 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 16U)>>2]);
                float _S97 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 20U)>>2]);
                float _S98 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 24U)>>2]);
                float _S99 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 28U)>>2]);
                float4 _S100 = float4(_S96, _S97, _S98, _S99);
                float _S101 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 32U)>>2]);
                float _S102 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 36U)>>2]);
                float _S103 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 40U)>>2]);
                float _S104 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 44U)>>2]);
                float4 _S105 = float4(_S101, _S102, _S103, _S104);
                float _S106 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 48U)>>2]);
                float _S107 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 52U)>>2]);
                float _S108 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 56U)>>2]);
                float _S109 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S90 + 60U)>>2]);
                float localStepLength_0 = length((((float4(rayDirection_0, 0.0f)) * (matrix<float,int(4),int(4)> (_S95, _S100, _S105, float4(_S106, _S107, _S108, _S109))))).xyz) * _S89;
                float _S110 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_0] + 112U)>>2]);
                float _S111 = max(0.0f, _S110);
                if(_S111 > densityScales_0[field_0])
                {
                    float previousScale_0 = densityScales_0[field_0];
                    if((densityScales_0[field_0]) > 0.0f)
                    {
                        normalizedDensityIntegrals_0[field_0] = normalizedDensityIntegrals_0[field_0] * (previousScale_0 / _S111);
                    }
                    densityScales_0[field_0] = _S111;
                    normalizedDensityIntegrals_0[field_0] = normalizedDensityIntegrals_0[field_0] + localStepLength_0;
                }
                else
                {
                    if((densityScales_0[field_0]) > 0.0f)
                    {
                        normalizedDensityIntegrals_0[field_0] = normalizedDensityIntegrals_0[field_0] + _S111 / densityScales_0[field_0] * localStepLength_0;
                    }
                }
                field_0 = field_0 + 1U;
            }
            step_0 = step_0 + 1U;
        }
        cursor_0 = _S81;
    }
    field_1 = 0U;
    for(;;)
    {
        if(field_1 < _S4)
        {
        }
        else
        {
            break;
        }
        float _S112 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_1] + 108U)>>2]);
        if(fieldEnabled_0[field_1])
        {
            float _S113 = densityScales_0[field_1];
            float _S114 = normalizedDensityIntegrals_0[field_1];
            for(;;)
            {
                float _S115 = max(0.0f, _S113);
                float _S116 = max(0.0f, _S114);
                if(_S115 <= 0.0f)
                {
                    hasActiveField_0 = true;
                }
                else
                {
                    hasActiveField_0 = _S116 <= 0.0f;
                }
                if(hasActiveField_0)
                {
                    intervalStart_0 = 0.0f;
                    break;
                }
                if(_S115 >= (2.12676466649080537e+37f / _S116))
                {
                    intervalStart_0 = 2.12676466649080537e+37f;
                }
                else
                {
                    intervalStart_0 = _S115 * _S116;
                }
                for(;;)
                {
                    float _S117 = max(0.0f, intervalStart_0);
                    float boundedOpacity_0 = saturate(_S112);
                    if(boundedOpacity_0 <= 0.0f)
                    {
                        _S1 = true;
                    }
                    else
                    {
                        _S1 = _S117 <= 0.0f;
                    }
                    if(_S1)
                    {
                        intervalEnd_0 = 0.0f;
                        break;
                    }
                    if(boundedOpacity_0 >= 1.0f)
                    {
                        intervalEnd_0 = _S117;
                        break;
                    }
                    for(;;)
                    {
                        float _S118 = max(_S117, 0.0f);
                        if(_S118 >= 0.125f)
                        {
                            intervalEnd_0 = 1.0f - exp(- _S118);
                            break;
                        }
                        order_0 = 2U;
                        term_0 = _S118;
                        sum_0 = _S118;
                        for(;;)
                        {
                            if(order_0 <= 8U)
                            {
                            }
                            else
                            {
                                break;
                            }
                            float term_1 = term_0 * (_S118 / float(order_0));
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
    float3 _S119 = float3(0.0f, 0.0f, 0.0f);
    field_1 = 0U;
    intervalStart_0 = 0.0f;
    for(;;)
    {
        if(field_1 < _S4)
        {
        }
        else
        {
            break;
        }
        float totalFieldOpticalDepth_0 = intervalStart_0 + fieldOpticalDepth_0[field_1];
        field_1 = field_1 + 1U;
        intervalStart_0 = totalFieldOpticalDepth_0;
    }
    float3 outputColor_0;
    float3 accumulatedColor_0;
    if(intervalStart_0 <= 0.00100000004749745f)
    {
        if(intervalStart_0 <= 0.0f)
        {
            pixelOutput_0 _S120 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
            return _S120;
        }
        field_1 = 0U;
        outputColor_0 = _S119;
        for(;;)
        {
            if(field_1 < _S4)
            {
            }
            else
            {
                break;
            }
            float3 integratedTintedOpticalDepth_0 = outputColor_0 + fieldTint_0[field_1] * float3(fieldOpticalDepth_0[field_1]) ;
            field_1 = field_1 + 1U;
            outputColor_0 = integratedTintedOpticalDepth_0;
        }
        for(;;)
        {
            float _S121 = max(intervalStart_0, 0.0f);
            if(_S121 >= 0.125f)
            {
                intervalStart_1 = 1.0f - exp(- _S121);
                break;
            }
            order_0 = 2U;
            term_0 = _S121;
            sum_0 = _S121;
            for(;;)
            {
                if(order_0 <= 8U)
                {
                }
                else
                {
                    break;
                }
                float term_2 = term_0 * (_S121 / float(order_0));
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
        float3 _S122 = outputColor_0 * float3((intervalStart_1 / intervalStart_0)) ;
        power_0 = intervalStart_0;
        accumulatedColor_0 = _S122;
    }
    else
    {
        cursor_0 = 0U;
        intervalEnd_0 = 0.0f;
        outputColor_0 = _S119;
        for(;;)
        {
            uint _S123 = cursor_0 + 1U;
            if(_S123 < eventCount_0)
            {
            }
            else
            {
                break;
            }
            float _S124 = max(events_0[cursor_0], intervalStart_1);
            float _S125 = min(events_0[_S123], intervalEnd_1);
            if(_S125 <= _S124)
            {
                power_0 = intervalEnd_0;
                accumulatedColor_0 = outputColor_0;
                cursor_0 = _S123;
                intervalEnd_0 = power_0;
                outputColor_0 = accumulatedColor_0;
                continue;
            }
            thread array<bool, int(8)> active_0;
            field_1 = 0U;
            for(;;)
            {
                if(field_1 < _S4)
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
                    _S1 = (entries_0[field_1]) <= _S124;
                }
                else
                {
                    _S1 = false;
                }
                if(_S1)
                {
                    _S3 = (exits_0[field_1]) >= _S125;
                }
                else
                {
                    _S3 = false;
                }
                active_0[field_1] = _S3;
                field_1 = field_1 + 1U;
            }
            array<float, int(8)> _S126 = entries_0;
            array<float, int(8)> _S127 = exits_0;
            array<bool, int(8)> _S128 = fieldEnabled_0;
            array<uint, int(8)> _S129 = fieldOffsets_0;
            for(;;)
            {
                field_0 = 0U;
                stepCount_0 = 0U;
                hasActiveField_0 = false;
                for(;;)
                {
                    if(field_0 < _S4)
                    {
                    }
                    else
                    {
                        break;
                    }
                    if(_S128[field_0])
                    {
                        _S1 = (_S126[field_0]) <= _S124;
                    }
                    else
                    {
                        _S1 = false;
                    }
                    if(_S1)
                    {
                        _S3 = (_S127[field_0]) >= _S125;
                    }
                    else
                    {
                        _S3 = false;
                    }
                    if(_S3)
                    {
                        uint _S130 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S129[field_0] + 92U)>>2]);
                        uint stepCount_2 = stepCount_0 + 2U * _S130;
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
                power_0 = intervalEnd_0;
                accumulatedColor_0 = outputColor_0;
                cursor_0 = _S123;
                intervalEnd_0 = power_0;
                outputColor_0 = accumulatedColor_0;
                continue;
            }
            float _S131 = (_S125 - _S124) / float(planeIndex_0);
            step_0 = 0U;
            power_0 = intervalEnd_0;
            accumulatedColor_0 = outputColor_0;
            for(;;)
            {
                if(step_0 < planeIndex_0)
                {
                }
                else
                {
                    break;
                }
                thread array<float, int(8)> fieldDepths_0;
                uint field_2 = 0U;
                float opticalDepth_0 = 0.0f;
                for(;;)
                {
                    if(field_2 < _S4)
                    {
                    }
                    else
                    {
                        break;
                    }
                    fieldDepths_0[field_2] = 0.0f;
                    if(!active_0[field_2])
                    {
                        field_2 = field_2 + 1U;
                        continue;
                    }
                    uint _S132 = fieldOffsets_0[field_2];
                    float _S133 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_2])>>2]);
                    float _S134 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 4U)>>2]);
                    float _S135 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 8U)>>2]);
                    float _S136 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 12U)>>2]);
                    float4 _S137 = float4(_S133, _S134, _S135, _S136);
                    float _S138 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 16U)>>2]);
                    float _S139 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 20U)>>2]);
                    float _S140 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 24U)>>2]);
                    float _S141 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 28U)>>2]);
                    float4 _S142 = float4(_S138, _S139, _S140, _S141);
                    float _S143 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 32U)>>2]);
                    float _S144 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 36U)>>2]);
                    float _S145 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 40U)>>2]);
                    float _S146 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 44U)>>2]);
                    float4 _S147 = float4(_S143, _S144, _S145, _S146);
                    float _S148 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 48U)>>2]);
                    float _S149 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 52U)>>2]);
                    float _S150 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 56U)>>2]);
                    float _S151 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S132 + 60U)>>2]);
                    float3 _S152 = (((float4(rayDirection_0, 0.0f)) * (matrix<float,int(4),int(4)> (_S137, _S142, _S147, float4(_S148, _S149, _S150, _S151))))).xyz;
                    float _S153 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_2] + 112U)>>2]);
                    float _S154 = fieldOpticalDepth_0[field_2] * (max(0.0f, _S153) / densityScales_0[field_2] * (length(_S152) * _S131) / normalizedDensityIntegrals_0[field_2]);
                    fieldDepths_0[field_2] = _S154;
                    opticalDepth_0 = opticalDepth_0 + _S154;
                    field_2 = field_2 + 1U;
                }
                if(opticalDepth_0 > 0.0f)
                {
                    float transmittance_0 = exp(- power_0);
                    for(;;)
                    {
                        float _S155 = max(opticalDepth_0, 0.0f);
                        if(_S155 >= 0.125f)
                        {
                            intervalStart_0 = 1.0f - exp(- _S155);
                            break;
                        }
                        order_0 = 2U;
                        term_0 = _S155;
                        sum_0 = _S155;
                        for(;;)
                        {
                            if(order_0 <= 8U)
                            {
                            }
                            else
                            {
                                break;
                            }
                            float term_3 = term_0 * (_S155 / float(order_0));
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
                    float3 source_0 = _S119;
                    for(;;)
                    {
                        if(order_0 < _S4)
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
                    float3 accumulatedColor_1 = accumulatedColor_0 + float3((transmittance_0 * intervalStart_0))  * source_0;
                    power_0 = power_0 + opticalDepth_0;
                    accumulatedColor_0 = accumulatedColor_1;
                }
                step_0 = step_0 + 1U;
            }
            cursor_0 = _S123;
            intervalEnd_0 = power_0;
            outputColor_0 = accumulatedColor_0;
        }
        power_0 = intervalEnd_0;
        accumulatedColor_0 = outputColor_0;
    }
    if(power_0 <= 0.0f)
    {
        pixelOutput_0 _S156 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S156;
    }
    for(;;)
    {
        float _S157 = max(power_0, 0.0f);
        if(_S157 >= 0.125f)
        {
            intervalStart_1 = 1.0f - exp(- _S157);
            break;
        }
        order_0 = 2U;
        term_0 = _S157;
        sum_0 = _S157;
        for(;;)
        {
            if(order_0 <= 8U)
            {
            }
            else
            {
                break;
            }
            float term_4 = term_0 * (_S157 / float(order_0));
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
            term_0 = term_4;
            sum_0 = sum_5;
        }
        intervalStart_1 = sum_0;
        break;
    }
    float3 outputColor_1 = accumulatedColor_0 / float3(intervalStart_1) ;
    uint _S158 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
    float3 outputColor_2 = outputColor_1 * float3(exp2((as_type<float>((_S158))))) ;
    uint _S159 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
    if(_S159 == 1U)
    {
        outputColor_0 = outputColor_2 / (float3(1.0f)  + max(outputColor_2, _S119));
    }
    else
    {
        outputColor_0 = outputColor_2;
    }
    if(diagnosticPixel_0)
    {
        pixelOutput_0 _S160 = { float4(power_0, accumulatedColor_0.x, intervalStart_1, outputColor_0.x) };
        return _S160;
    }
    pixelOutput_0 _S161 = { float4(outputColor_0, intervalStart_1) };
    return _S161;
}
