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
};

[[fragment]] pixelOutput_0 volumeCameraUniformFragmentMain(float4 position_0 [[position]], SLANG_ParameterGroup_VolumeCameraParameters_0 constant* VolumeCameraParameters_1 [[buffer(0)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> opaqueDepthTexture_1 [[texture(28)]], uint32_t device* volumeFields_1 [[buffer(6)]])
{
    float intervalStart_0;
    float intervalEnd_0;
    bool _S1;
    uint axis_0;
    int _S2;
    uint cursor_0;
    bool _S3;
    uint field_0;
    uint stepCount_0;
    uint step_0;
    float sum_0;
    float3 accumulatedColor_0;
    float term_0;
    uint field_1;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->VolumeCameraParameters_0 = VolumeCameraParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->opaqueDepthTexture_0 = opaqueDepthTexture_1;
    (&kernelContext_0)->volumeFields_0 = volumeFields_1;
    uint4 frameShape_0 = VolumeCameraParameters_1->volumeHeader_0;
    uint _S4 = min(VolumeCameraParameters_1->volumeHeader_0.x, 8U);
    bool hasActiveField_0;
    if(_S4 == 0U)
    {
        hasActiveField_0 = true;
    }
    else
    {
        hasActiveField_0 = (frameShape_0.y) == 0U;
    }
    if(hasActiveField_0)
    {
        hasActiveField_0 = true;
    }
    else
    {
        hasActiveField_0 = (frameShape_0.z) == 0U;
    }
    if(hasActiveField_0)
    {
        hasActiveField_0 = true;
    }
    else
    {
        hasActiveField_0 = (VolumeCameraParameters_1->volumeHeader_0.x) > 8U;
    }
    if(hasActiveField_0)
    {
        pixelOutput_0 _S5 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S5;
    }
    uint2 _S6 = uint2(position_0.xy);
    float2 pixelCoordinate_0 = (float2(_S6) + float2(0.5f) ) / float2(frameShape_0.yz);
    float _S7 = pixelCoordinate_0.x * 2.0f - 1.0f;
    float intervalStart_1;
    if((frameShape_0.w) != 0U)
    {
        intervalStart_1 = pixelCoordinate_0.y * 2.0f - 1.0f;
    }
    else
    {
        intervalStart_1 = 1.0f - pixelCoordinate_0.y * 2.0f;
    }
    float2 clipXY_0 = float2(_S7, intervalStart_1);
    float _S8 = as_type<float>((&kernelContext_0)->frameParameters_0[(0U)>>2]);
    float _S9 = as_type<float>((&kernelContext_0)->frameParameters_0[(4U)>>2]);
    float _S10 = as_type<float>((&kernelContext_0)->frameParameters_0[(8U)>>2]);
    float _S11 = as_type<float>((&kernelContext_0)->frameParameters_0[(12U)>>2]);
    float4 _S12 = float4(_S8, _S9, _S10, _S11);
    float _S13 = as_type<float>((&kernelContext_0)->frameParameters_0[(16U)>>2]);
    float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(20U)>>2]);
    float _S15 = as_type<float>((&kernelContext_0)->frameParameters_0[(24U)>>2]);
    float _S16 = as_type<float>((&kernelContext_0)->frameParameters_0[(28U)>>2]);
    float4 _S17 = float4(_S13, _S14, _S15, _S16);
    float _S18 = as_type<float>((&kernelContext_0)->frameParameters_0[(32U)>>2]);
    float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(36U)>>2]);
    float _S20 = as_type<float>((&kernelContext_0)->frameParameters_0[(40U)>>2]);
    float _S21 = as_type<float>((&kernelContext_0)->frameParameters_0[(44U)>>2]);
    float4 _S22 = float4(_S18, _S19, _S20, _S21);
    float _S23 = as_type<float>((&kernelContext_0)->frameParameters_0[(48U)>>2]);
    float _S24 = as_type<float>((&kernelContext_0)->frameParameters_0[(52U)>>2]);
    float _S25 = as_type<float>((&kernelContext_0)->frameParameters_0[(56U)>>2]);
    float _S26 = as_type<float>((&kernelContext_0)->frameParameters_0[(60U)>>2]);
    matrix<float,int(4),int(4)>  _S27 = matrix<float,int(4),int(4)> (_S12, _S17, _S22, float4(_S23, _S24, _S25, _S26));
    float _S28 = as_type<float>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
    float4 nearHomogeneous_0 = (((float4(clipXY_0, 0.0f, 1.0f)) * (_S27)));
    float4 farHomogeneous_0 = (((float4(clipXY_0, 1.0f, 1.0f)) * (_S27)));
    float _S29 = nearHomogeneous_0.w;
    if(_S29 == 0.0f)
    {
        pixelOutput_0 _S30 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S30;
    }
    float3 rayOrigin_0 = nearHomogeneous_0.xyz / float3(_S29) ;
    float _S31 = farHomogeneous_0.w;
    bool _S32 = _S31 == 0.0f;
    float3 rayDirection_0;
    if(_S32)
    {
        rayDirection_0 = normalize(farHomogeneous_0.xyz * float3(float((int(sign((_S29)))))) );
    }
    else
    {
        rayDirection_0 = normalize(farHomogeneous_0.xyz / float3(_S31)  - rayOrigin_0);
    }
    if(_S32)
    {
        intervalStart_1 = 3.4028234663852886e+38f;
    }
    else
    {
        intervalStart_1 = dot(farHomogeneous_0.xyz / float3(_S31)  - rayOrigin_0, rayDirection_0);
    }
    uint _S33 = as_type<uint>((&kernelContext_0)->frameParameters_0[(64U)>>2]);
    uint _S34 = min(_S33, 8U);
    float _S35 = intervalStart_1;
    uint planeIndex_0 = 0U;
    intervalStart_1 = 0.0f;
    float intervalEnd_1 = _S35;
    for(;;)
    {
        if(planeIndex_0 < _S34)
        {
        }
        else
        {
            break;
        }
        uint _S36 = 80U + planeIndex_0 * 16U;
        float _S37 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S36)>>2]);
        float _S38 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S36 + 4U)>>2]);
        float _S39 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S36 + 8U)>>2]);
        float _S40 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S36 + 12U)>>2]);
        float3 _S41 = float3(_S37, _S38, _S39);
        float offset_0 = dot(_S41, rayOrigin_0) + _S40;
        float slope_0 = dot(_S41, rayDirection_0);
        if(slope_0 == 0.0f)
        {
            if(offset_0 < 0.0f)
            {
                pixelOutput_0 _S42 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
                return _S42;
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
                float _S43 = min(intervalEnd_1, - offset_0 / slope_0);
                intervalStart_0 = intervalStart_1;
                intervalEnd_0 = _S43;
            }
            intervalStart_1 = intervalStart_0;
            intervalEnd_1 = intervalEnd_0;
        }
        planeIndex_0 = planeIndex_0 + 1U;
    }
    int3 _S44 = int3(int2(_S6), int(0));
    float opaqueDepth_0 = (((&kernelContext_0)->opaqueDepthTexture_0).read(vec<uint,2>(((_S44)).xy), uint(((_S44)).z)).x);
    if(opaqueDepth_0 != _S28)
    {
        float4 opaqueHomogeneous_0 = (((float4(clipXY_0, opaqueDepth_0, 1.0f)) * (_S27)));
        float _S45 = opaqueHomogeneous_0.w;
        if(_S45 != 0.0f)
        {
            intervalEnd_1 = min(intervalEnd_1, dot(opaqueHomogeneous_0.xyz / float3(_S45)  - rayOrigin_0, rayDirection_0));
        }
    }
    if(intervalEnd_1 <= intervalStart_1)
    {
        pixelOutput_0 _S46 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S46;
    }
    thread array<float, int(8)> entries_0;
    thread array<float, int(8)> exits_0;
    thread array<float, int(8)> densityIntegrals_0;
    thread array<float, int(8)> fieldOpticalDepth_0;
    thread array<float3, int(8)> fieldTint_0;
    thread array<bool, int(8)> fieldEnabled_0;
    thread array<uint, int(8)> fieldOffsets_0;
    thread array<float, int(16)> events_0;
    uint field_2 = 0U;
    uint eventCount_0 = 0U;
    for(;;)
    {
        if(field_2 < _S4)
        {
        }
        else
        {
            break;
        }
        uint _S47 = field_2 * 128U;
        fieldOffsets_0[field_2] = _S47;
        float _S48 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47)>>2]);
        float _S49 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 4U)>>2]);
        float _S50 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 8U)>>2]);
        float _S51 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 12U)>>2]);
        float4 _S52 = float4(_S48, _S49, _S50, _S51);
        float _S53 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 16U)>>2]);
        float _S54 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 20U)>>2]);
        float _S55 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 24U)>>2]);
        float _S56 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 28U)>>2]);
        float4 _S57 = float4(_S53, _S54, _S55, _S56);
        float _S58 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 32U)>>2]);
        float _S59 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 36U)>>2]);
        float _S60 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 40U)>>2]);
        float _S61 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 44U)>>2]);
        float4 _S62 = float4(_S58, _S59, _S60, _S61);
        float _S63 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 48U)>>2]);
        float _S64 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 52U)>>2]);
        float _S65 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 56U)>>2]);
        float _S66 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S47 + 60U)>>2]);
        matrix<float,int(4),int(4)>  _S67 = matrix<float,int(4),int(4)> (_S52, _S57, _S62, float4(_S63, _S64, _S65, _S66));
        float3 _S68 = (((float4(rayOrigin_0, 1.0f)) * (_S67))).xyz;
        float3 _S69 = (((float4(rayDirection_0, 0.0f)) * (_S67))).xyz;
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
                uint _S70 = axis_0;
                if((_S69[axis_0]) == 0.0f)
                {
                    uint _S71 = axis_0;
                    if((_S68[axis_0]) < -0.5f)
                    {
                        hasActiveField_0 = true;
                    }
                    else
                    {
                        hasActiveField_0 = (_S68[_S71]) > 0.5f;
                    }
                    if(hasActiveField_0)
                    {
                        _S2 = int(1);
                        break;
                    }
                }
                else
                {
                    float first_0 = (-0.5f - _S68[axis_0]) / _S69[_S70];
                    float second_0 = (0.5f - _S68[axis_0]) / _S69[_S70];
                    float _S72 = min(intervalEnd_0, max(first_0, second_0));
                    intervalStart_0 = max(intervalStart_0, min(first_0, second_0));
                    intervalEnd_0 = _S72;
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
            entries_0[field_2] = max(intervalStart_0, intervalStart_1);
            exits_0[field_2] = min(intervalEnd_0, intervalEnd_1);
        }
        else
        {
            entries_0[field_2] = 1.0f;
            exits_0[field_2] = 0.0f;
        }
        uint _S73 = _S47 + 96U;
        float _S74 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S73)>>2]);
        float _S75 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S73 + 4U)>>2]);
        float _S76 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S73 + 8U)>>2]);
        float _S77 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S73 + 12U)>>2]);
        fieldTint_0[field_2] = max(float3(_S74, _S75, _S76), float3(0.0f, 0.0f, 0.0f));
        if((saturate(_S77)) > 0.0f)
        {
            _S1 = (exits_0[field_2]) > entries_0[field_2];
        }
        else
        {
            _S1 = false;
        }
        fieldEnabled_0[field_2] = _S1;
        densityIntegrals_0[field_2] = 0.0f;
        fieldOpticalDepth_0[field_2] = 0.0f;
        if(_S1)
        {
            uint eventCount_1 = eventCount_0 + 1U;
            events_0[eventCount_0] = entries_0[field_2];
            uint _S78 = eventCount_1 + 1U;
            events_0[eventCount_1] = exits_0[field_2];
            eventCount_0 = _S78;
        }
        field_2 = field_2 + 1U;
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
            uint _S79 = cursor_0 - 1U;
            events_0[cursor_0] = events_0[_S79];
            cursor_0 = _S79;
        }
        events_0[cursor_0] = value_0;
        axis_0 = axis_0 + 1U;
    }
    cursor_0 = 0U;
    for(;;)
    {
        uint _S80 = cursor_0 + 1U;
        if(_S80 < eventCount_0)
        {
        }
        else
        {
            break;
        }
        float _S81 = max(events_0[cursor_0], intervalStart_1);
        float _S82 = min(events_0[_S80], intervalEnd_1);
        if(_S82 <= _S81)
        {
            cursor_0 = _S80;
            continue;
        }
        array<float, int(8)> _S83 = entries_0;
        array<float, int(8)> _S84 = exits_0;
        array<bool, int(8)> _S85 = fieldEnabled_0;
        array<uint, int(8)> _S86 = fieldOffsets_0;
        for(;;)
        {
            field_2 = 0U;
            stepCount_0 = 0U;
            hasActiveField_0 = false;
            for(;;)
            {
                if(field_2 < _S4)
                {
                }
                else
                {
                    break;
                }
                if(_S85[field_2])
                {
                    _S1 = (_S83[field_2]) <= _S81;
                }
                else
                {
                    _S1 = false;
                }
                if(_S1)
                {
                    _S3 = (_S84[field_2]) >= _S82;
                }
                else
                {
                    _S3 = false;
                }
                if(_S3)
                {
                    uint _S87 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S86[field_2] + 92U)>>2]);
                    uint stepCount_1 = stepCount_0 + 2U * _S87;
                    hasActiveField_0 = true;
                    stepCount_0 = stepCount_1;
                }
                field_2 = field_2 + 1U;
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
            cursor_0 = _S80;
            continue;
        }
        float _S88 = (_S82 - _S81) / float(planeIndex_0);
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
                    _S1 = (entries_0[field_0]) > _S81;
                }
                if(_S1)
                {
                    _S3 = true;
                }
                else
                {
                    _S3 = (exits_0[field_0]) < _S82;
                }
                if(_S3)
                {
                    field_0 = field_0 + 1U;
                    continue;
                }
                uint _S89 = fieldOffsets_0[field_0];
                float _S90 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_0])>>2]);
                float _S91 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 4U)>>2]);
                float _S92 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 8U)>>2]);
                float _S93 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 12U)>>2]);
                float4 _S94 = float4(_S90, _S91, _S92, _S93);
                float _S95 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 16U)>>2]);
                float _S96 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 20U)>>2]);
                float _S97 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 24U)>>2]);
                float _S98 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 28U)>>2]);
                float4 _S99 = float4(_S95, _S96, _S97, _S98);
                float _S100 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 32U)>>2]);
                float _S101 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 36U)>>2]);
                float _S102 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 40U)>>2]);
                float _S103 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 44U)>>2]);
                float4 _S104 = float4(_S100, _S101, _S102, _S103);
                float _S105 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 48U)>>2]);
                float _S106 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 52U)>>2]);
                float _S107 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 56U)>>2]);
                float _S108 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S89 + 60U)>>2]);
                float localStepLength_0 = length((((float4(rayDirection_0, 0.0f)) * (matrix<float,int(4),int(4)> (_S94, _S99, _S104, float4(_S105, _S106, _S107, _S108))))).xyz) * _S88;
                float _S109 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_0] + 112U)>>2]);
                densityIntegrals_0[field_0] = densityIntegrals_0[field_0] + max(0.0f, _S109) * localStepLength_0;
                field_0 = field_0 + 1U;
            }
            step_0 = step_0 + 1U;
        }
        cursor_0 = _S80;
    }
    field_2 = 0U;
    for(;;)
    {
        if(field_2 < _S4)
        {
        }
        else
        {
            break;
        }
        float _S110 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_2] + 108U)>>2]);
        if(fieldEnabled_0[field_2])
        {
            float _S111 = densityIntegrals_0[field_2];
            for(;;)
            {
                float _S112 = max(0.0f, _S111);
                float boundedOpacity_0 = saturate(_S110);
                if(boundedOpacity_0 <= 0.0f)
                {
                    hasActiveField_0 = true;
                }
                else
                {
                    hasActiveField_0 = _S112 <= 0.0f;
                }
                if(hasActiveField_0)
                {
                    intervalStart_0 = 0.0f;
                    break;
                }
                if(boundedOpacity_0 >= 1.0f)
                {
                    intervalStart_0 = _S112;
                    break;
                }
                for(;;)
                {
                    float _S113 = max(_S112, 0.0f);
                    if(_S113 >= 0.125f)
                    {
                        intervalStart_0 = 1.0f - exp(- _S113);
                        break;
                    }
                    field_0 = 2U;
                    intervalEnd_0 = _S113;
                    sum_0 = _S113;
                    for(;;)
                    {
                        if(field_0 <= 8U)
                        {
                        }
                        else
                        {
                            break;
                        }
                        float term_1 = intervalEnd_0 * (_S113 / float(field_0));
                        if((field_0 & 1U) != 0U)
                        {
                            intervalStart_0 = term_1;
                        }
                        else
                        {
                            intervalStart_0 = - term_1;
                        }
                        float sum_1 = sum_0 + intervalStart_0;
                        field_0 = field_0 + 1U;
                        intervalEnd_0 = term_1;
                        sum_0 = sum_1;
                    }
                    intervalStart_0 = sum_0;
                    break;
                }
                float removedFraction_0 = boundedOpacity_0 * intervalStart_0;
                for(;;)
                {
                    if(removedFraction_0 >= 0.125f)
                    {
                        intervalEnd_0 = - log(1.0f - removedFraction_0);
                        break;
                    }
                    field_0 = 2U;
                    intervalEnd_0 = removedFraction_0;
                    sum_0 = removedFraction_0;
                    for(;;)
                    {
                        if(field_0 <= 8U)
                        {
                        }
                        else
                        {
                            break;
                        }
                        float power_0 = intervalEnd_0 * removedFraction_0;
                        float sum_2 = sum_0 + power_0 / float(field_0);
                        field_0 = field_0 + 1U;
                        intervalEnd_0 = power_0;
                        sum_0 = sum_2;
                    }
                    intervalEnd_0 = sum_0;
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
        fieldOpticalDepth_0[field_2] = intervalStart_0;
        field_2 = field_2 + 1U;
    }
    float3 _S114 = float3(0.0f, 0.0f, 0.0f);
    cursor_0 = 0U;
    intervalStart_0 = 0.0f;
    float3 accumulatedColor_1 = _S114;
    for(;;)
    {
        uint _S115 = cursor_0 + 1U;
        if(_S115 < eventCount_0)
        {
        }
        else
        {
            break;
        }
        float _S116 = max(events_0[cursor_0], intervalStart_1);
        float _S117 = min(events_0[_S115], intervalEnd_1);
        if(_S117 <= _S116)
        {
            intervalEnd_0 = intervalStart_0;
            accumulatedColor_0 = accumulatedColor_1;
            cursor_0 = _S115;
            intervalStart_0 = intervalEnd_0;
            accumulatedColor_1 = accumulatedColor_0;
            continue;
        }
        thread array<bool, int(8)> active_0;
        field_2 = 0U;
        for(;;)
        {
            if(field_2 < _S4)
            {
            }
            else
            {
                break;
            }
            if(fieldEnabled_0[field_2])
            {
                hasActiveField_0 = (densityIntegrals_0[field_2]) > 0.0f;
            }
            else
            {
                hasActiveField_0 = false;
            }
            if(hasActiveField_0)
            {
                _S1 = (entries_0[field_2]) <= _S116;
            }
            else
            {
                _S1 = false;
            }
            if(_S1)
            {
                _S3 = (exits_0[field_2]) >= _S117;
            }
            else
            {
                _S3 = false;
            }
            active_0[field_2] = _S3;
            field_2 = field_2 + 1U;
        }
        array<float, int(8)> _S118 = entries_0;
        array<float, int(8)> _S119 = exits_0;
        array<bool, int(8)> _S120 = fieldEnabled_0;
        array<uint, int(8)> _S121 = fieldOffsets_0;
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
                if(_S120[field_0])
                {
                    _S1 = (_S118[field_0]) <= _S116;
                }
                else
                {
                    _S1 = false;
                }
                if(_S1)
                {
                    _S3 = (_S119[field_0]) >= _S117;
                }
                else
                {
                    _S3 = false;
                }
                if(_S3)
                {
                    uint _S122 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S121[field_0] + 92U)>>2]);
                    uint stepCount_2 = stepCount_0 + 2U * _S122;
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
            intervalEnd_0 = intervalStart_0;
            accumulatedColor_0 = accumulatedColor_1;
            cursor_0 = _S115;
            intervalStart_0 = intervalEnd_0;
            accumulatedColor_1 = accumulatedColor_0;
            continue;
        }
        float _S123 = (_S117 - _S116) / float(planeIndex_0);
        step_0 = 0U;
        intervalEnd_0 = intervalStart_0;
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
            thread array<float, int(8)> fieldDepths_0;
            uint field_3 = 0U;
            sum_0 = 0.0f;
            for(;;)
            {
                if(field_3 < _S4)
                {
                }
                else
                {
                    break;
                }
                fieldDepths_0[field_3] = 0.0f;
                if(!active_0[field_3])
                {
                    field_3 = field_3 + 1U;
                    continue;
                }
                uint _S124 = fieldOffsets_0[field_3];
                float _S125 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_3])>>2]);
                float _S126 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 4U)>>2]);
                float _S127 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 8U)>>2]);
                float _S128 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 12U)>>2]);
                float4 _S129 = float4(_S125, _S126, _S127, _S128);
                float _S130 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 16U)>>2]);
                float _S131 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 20U)>>2]);
                float _S132 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 24U)>>2]);
                float _S133 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 28U)>>2]);
                float4 _S134 = float4(_S130, _S131, _S132, _S133);
                float _S135 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 32U)>>2]);
                float _S136 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 36U)>>2]);
                float _S137 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 40U)>>2]);
                float _S138 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 44U)>>2]);
                float4 _S139 = float4(_S135, _S136, _S137, _S138);
                float _S140 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 48U)>>2]);
                float _S141 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 52U)>>2]);
                float _S142 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 56U)>>2]);
                float _S143 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S124 + 60U)>>2]);
                float3 _S144 = (((float4(rayDirection_0, 0.0f)) * (matrix<float,int(4),int(4)> (_S129, _S134, _S139, float4(_S140, _S141, _S142, _S143))))).xyz;
                float _S145 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_3] + 112U)>>2]);
                float _S146 = fieldOpticalDepth_0[field_3] * (max(0.0f, _S145) / densityIntegrals_0[field_3] * (length(_S144) * _S123));
                fieldDepths_0[field_3] = _S146;
                sum_0 = sum_0 + _S146;
                field_3 = field_3 + 1U;
            }
            if(sum_0 > 0.0f)
            {
                float transmittance_0 = exp(- intervalEnd_0);
                for(;;)
                {
                    float _S147 = max(sum_0, 0.0f);
                    if(_S147 >= 0.125f)
                    {
                        term_0 = 1.0f - exp(- _S147);
                        break;
                    }
                    field_1 = 2U;
                    term_0 = _S147;
                    float sum_3 = _S147;
                    for(;;)
                    {
                        if(field_1 <= 8U)
                        {
                        }
                        else
                        {
                            break;
                        }
                        float term_2 = term_0 * (_S147 / float(field_1));
                        float _S148;
                        if((field_1 & 1U) != 0U)
                        {
                            _S148 = term_2;
                        }
                        else
                        {
                            _S148 = - term_2;
                        }
                        float sum_4 = sum_3 + _S148;
                        field_1 = field_1 + 1U;
                        term_0 = term_2;
                        sum_3 = sum_4;
                    }
                    term_0 = sum_3;
                    break;
                }
                field_1 = 0U;
                float3 source_0 = _S114;
                for(;;)
                {
                    if(field_1 < _S4)
                    {
                    }
                    else
                    {
                        break;
                    }
                    if((fieldDepths_0[field_1]) > 0.0f)
                    {
                        source_0 = source_0 + fieldTint_0[field_1] * float3((fieldDepths_0[field_1] / sum_0)) ;
                    }
                    field_1 = field_1 + 1U;
                }
                float3 accumulatedColor_2 = accumulatedColor_0 + float3((transmittance_0 * term_0))  * source_0;
                intervalEnd_0 = intervalEnd_0 + sum_0;
                accumulatedColor_0 = accumulatedColor_2;
            }
            step_0 = step_0 + 1U;
        }
        cursor_0 = _S115;
        intervalStart_0 = intervalEnd_0;
        accumulatedColor_1 = accumulatedColor_0;
    }
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
        field_0 = 2U;
        intervalEnd_0 = _S150;
        sum_0 = _S150;
        for(;;)
        {
            if(field_0 <= 8U)
            {
            }
            else
            {
                break;
            }
            float term_3 = intervalEnd_0 * (_S150 / float(field_0));
            if((field_0 & 1U) != 0U)
            {
                intervalStart_1 = term_3;
            }
            else
            {
                intervalStart_1 = - term_3;
            }
            float sum_5 = sum_0 + intervalStart_1;
            field_0 = field_0 + 1U;
            intervalEnd_0 = term_3;
            sum_0 = sum_5;
        }
        intervalStart_1 = sum_0;
        break;
    }
    float3 outputColor_0 = accumulatedColor_1 / float3(intervalStart_1) ;
    uint _S151 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
    float3 outputColor_1 = outputColor_0 * float3(exp2((as_type<float>((_S151))))) ;
    uint _S152 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
    if(_S152 == 1U)
    {
        accumulatedColor_0 = outputColor_1 / (float3(1.0f)  + max(outputColor_1, _S114));
    }
    else
    {
        accumulatedColor_0 = outputColor_1;
    }
    pixelOutput_0 _S153 = { float4(accumulatedColor_0, intervalStart_1) };
    return _S153;
}
