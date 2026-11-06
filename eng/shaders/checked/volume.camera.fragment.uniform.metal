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
    texture2d<float, access::sample> backgroundColorTexture_0;
    SLANG_ParameterGroup_VolumeCameraParameters_0 constant* VolumeCameraParameters_0;
    uint32_t device* frameParameters_0;
    texture2d<float, access::sample> opaqueDepthTexture_0;
    uint32_t device* volumeFields_0;
};

struct VolumeLogSum_0
{
    float maximum_0;
    float sum_0;
    float compensation_0;
};

[[fragment]] pixelOutput_0 volumeCameraUniformFragmentMain(float4 position_0 [[position]], texture2d<float, access::sample> backgroundColorTexture_1 [[texture(0)]], SLANG_ParameterGroup_VolumeCameraParameters_0 constant* VolumeCameraParameters_1 [[buffer(0)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> opaqueDepthTexture_1 [[texture(28)]], uint32_t device* volumeFields_1 [[buffer(6)]])
{
    float intervalStart_0;
    float intervalEnd_0;
    bool hasActiveField_0;
    bool _S1;
    bool _S2;
    bool _S3;
    float power_0;
    float accumulatedOpticalDepth_0;
    float opticalDepth_0;
    uint axis_0;
    int _S4;
    uint cursor_0;
    uint field_0;
    uint stepCount_0;
    uint step_0;
    bool _S5;
    float term_0;
    float sum_1;
    uint order_0;
    float contribution_0;
    float _S6;
    float opticalDepth_1;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->backgroundColorTexture_0 = backgroundColorTexture_1;
    (&kernelContext_0)->VolumeCameraParameters_0 = VolumeCameraParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->opaqueDepthTexture_0 = opaqueDepthTexture_1;
    (&kernelContext_0)->volumeFields_0 = volumeFields_1;
    uint2 _S7 = uint2(position_0.xy);
    int3 _S8 = int3(int2(_S7), int(0));
    float4 background_0 = ((backgroundColorTexture_1).read(vec<uint,2>(((_S8)).xy), uint(((_S8)).z)));
    uint4 frameShape_0 = VolumeCameraParameters_1->volumeHeader_0;
    uint _S9 = min(VolumeCameraParameters_1->volumeHeader_0.x, 8U);
    bool smallOpticalDepth_0;
    if(_S9 == 0U)
    {
        smallOpticalDepth_0 = true;
    }
    else
    {
        smallOpticalDepth_0 = (frameShape_0.y) == 0U;
    }
    if(smallOpticalDepth_0)
    {
        smallOpticalDepth_0 = true;
    }
    else
    {
        smallOpticalDepth_0 = (frameShape_0.z) == 0U;
    }
    if(smallOpticalDepth_0)
    {
        smallOpticalDepth_0 = true;
    }
    else
    {
        smallOpticalDepth_0 = (VolumeCameraParameters_1->volumeHeader_0.x) > 8U;
    }
    if(smallOpticalDepth_0)
    {
        pixelOutput_0 _S10 = { background_0 };
        return _S10;
    }
    float2 pixelCoordinate_0 = (float2(_S7) + float2(0.5f) ) / float2(frameShape_0.yz);
    float _S11 = pixelCoordinate_0.x * 2.0f - 1.0f;
    float intervalStart_1;
    if((frameShape_0.w) != 0U)
    {
        intervalStart_1 = pixelCoordinate_0.y * 2.0f - 1.0f;
    }
    else
    {
        intervalStart_1 = 1.0f - pixelCoordinate_0.y * 2.0f;
    }
    float2 clipXY_0 = float2(_S11, intervalStart_1);
    float _S12 = as_type<float>((&kernelContext_0)->frameParameters_0[(0U)>>2]);
    float _S13 = as_type<float>((&kernelContext_0)->frameParameters_0[(4U)>>2]);
    float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(8U)>>2]);
    float _S15 = as_type<float>((&kernelContext_0)->frameParameters_0[(12U)>>2]);
    float4 _S16 = float4(_S12, _S13, _S14, _S15);
    float _S17 = as_type<float>((&kernelContext_0)->frameParameters_0[(16U)>>2]);
    float _S18 = as_type<float>((&kernelContext_0)->frameParameters_0[(20U)>>2]);
    float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(24U)>>2]);
    float _S20 = as_type<float>((&kernelContext_0)->frameParameters_0[(28U)>>2]);
    float4 _S21 = float4(_S17, _S18, _S19, _S20);
    float _S22 = as_type<float>((&kernelContext_0)->frameParameters_0[(32U)>>2]);
    float _S23 = as_type<float>((&kernelContext_0)->frameParameters_0[(36U)>>2]);
    float _S24 = as_type<float>((&kernelContext_0)->frameParameters_0[(40U)>>2]);
    float _S25 = as_type<float>((&kernelContext_0)->frameParameters_0[(44U)>>2]);
    float4 _S26 = float4(_S22, _S23, _S24, _S25);
    float _S27 = as_type<float>((&kernelContext_0)->frameParameters_0[(48U)>>2]);
    float _S28 = as_type<float>((&kernelContext_0)->frameParameters_0[(52U)>>2]);
    float _S29 = as_type<float>((&kernelContext_0)->frameParameters_0[(56U)>>2]);
    float _S30 = as_type<float>((&kernelContext_0)->frameParameters_0[(60U)>>2]);
    matrix<float,int(4),int(4)>  _S31 = matrix<float,int(4),int(4)> (_S16, _S21, _S26, float4(_S27, _S28, _S29, _S30));
    float _S32 = as_type<float>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
    float4 nearHomogeneous_0 = (((float4(clipXY_0, 0.0f, 1.0f)) * (_S31)));
    float4 farHomogeneous_0 = (((float4(clipXY_0, 1.0f, 1.0f)) * (_S31)));
    float _S33 = nearHomogeneous_0.w;
    if(_S33 == 0.0f)
    {
        pixelOutput_0 _S34 = { background_0 };
        return _S34;
    }
    float3 rayOrigin_0 = nearHomogeneous_0.xyz / float3(_S33) ;
    float _S35 = farHomogeneous_0.w;
    bool _S36 = _S35 == 0.0f;
    float3 rayDirection_0;
    if(_S36)
    {
        rayDirection_0 = normalize(farHomogeneous_0.xyz * float3(float((int(sign((_S33)))))) );
    }
    else
    {
        rayDirection_0 = normalize(farHomogeneous_0.xyz / float3(_S35)  - rayOrigin_0);
    }
    if(_S36)
    {
        intervalStart_1 = 3.4028234663852886e+38f;
    }
    else
    {
        intervalStart_1 = dot(farHomogeneous_0.xyz / float3(_S35)  - rayOrigin_0, rayDirection_0);
    }
    uint _S37 = as_type<uint>((&kernelContext_0)->frameParameters_0[(64U)>>2]);
    uint _S38 = min(_S37, 8U);
    float _S39 = intervalStart_1;
    uint planeIndex_0 = 0U;
    intervalStart_1 = 0.0f;
    float intervalEnd_1 = _S39;
    for(;;)
    {
        if(planeIndex_0 < _S38)
        {
        }
        else
        {
            break;
        }
        uint _S40 = 80U + planeIndex_0 * 16U;
        float _S41 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40)>>2]);
        float _S42 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 4U)>>2]);
        float _S43 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 8U)>>2]);
        float _S44 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S40 + 12U)>>2]);
        float3 _S45 = float3(_S41, _S42, _S43);
        float offset_0 = dot(_S45, rayOrigin_0) + _S44;
        float slope_0 = dot(_S45, rayDirection_0);
        if(slope_0 == 0.0f)
        {
            if(offset_0 < 0.0f)
            {
                pixelOutput_0 _S46 = { background_0 };
                return _S46;
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
                float _S47 = min(intervalEnd_1, - offset_0 / slope_0);
                intervalStart_0 = intervalStart_1;
                intervalEnd_0 = _S47;
            }
            intervalStart_1 = intervalStart_0;
            intervalEnd_1 = intervalEnd_0;
        }
        planeIndex_0 = planeIndex_0 + 1U;
    }
    float opaqueDepth_0 = (((&kernelContext_0)->opaqueDepthTexture_0).read(vec<uint,2>(((_S8)).xy), uint(((_S8)).z)).x);
    if(opaqueDepth_0 != _S32)
    {
        float4 opaqueHomogeneous_0 = (((float4(clipXY_0, opaqueDepth_0, 1.0f)) * (_S31)));
        float _S48 = opaqueHomogeneous_0.w;
        if(_S48 != 0.0f)
        {
            intervalEnd_1 = min(intervalEnd_1, dot(opaqueHomogeneous_0.xyz / float3(_S48)  - rayOrigin_0, rayDirection_0));
        }
    }
    if(intervalEnd_1 <= intervalStart_1)
    {
        pixelOutput_0 _S49 = { background_0 };
        return _S49;
    }
    thread array<float, int(8)> entries_0;
    thread array<float, int(8)> exits_0;
    thread array<float, int(8)> densityScales_0;
    thread array<float, int(8)> normalizedDensityIntegrals_0;
    thread array<float, int(8)> fieldOpticalDepth_0;
    thread array<float, int(8)> fieldLogOpticalDepth_0;
    thread array<float3, int(8)> fieldLogTint_0;
    thread array<bool, int(8)> fieldEnabled_0;
    thread array<uint, int(8)> fieldOffsets_0;
    thread array<float, int(16)> events_0;
    uint field_1 = 0U;
    uint eventCount_0 = 0U;
    for(;;)
    {
        if(field_1 < _S9)
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
                    _S4 = int(2);
                    break;
                }
                uint _S73 = axis_0;
                if((_S72[axis_0]) == 0.0f)
                {
                    uint _S74 = axis_0;
                    if((_S71[axis_0]) < -0.5f)
                    {
                        smallOpticalDepth_0 = true;
                    }
                    else
                    {
                        smallOpticalDepth_0 = (_S71[_S74]) > 0.5f;
                    }
                    if(smallOpticalDepth_0)
                    {
                        _S4 = int(1);
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
            if(_S4 != int(2))
            {
                smallOpticalDepth_0 = false;
                break;
            }
            smallOpticalDepth_0 = intervalEnd_0 > intervalStart_0;
            break;
        }
        if(smallOpticalDepth_0)
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
        for(;;)
        {
            uint bits_0 = (as_type<uint>((_S77)));
            if((bits_0 & 2147483648U) != 0U)
            {
                hasActiveField_0 = true;
            }
            else
            {
                hasActiveField_0 = (bits_0 & 2147483647U) == 0U;
            }
            if(hasActiveField_0)
            {
                power_0 = -3.4028234663852886e+38f;
                break;
            }
            if((bits_0 & 2139095040U) == 0U)
            {
                power_0 = log(float(bits_0 & 8388607U)) - 103.2789306640625f;
                break;
            }
            power_0 = log(_S77);
            break;
        }
        for(;;)
        {
            uint bits_1 = (as_type<uint>((_S78)));
            if((bits_1 & 2147483648U) != 0U)
            {
                _S1 = true;
            }
            else
            {
                _S1 = (bits_1 & 2147483647U) == 0U;
            }
            if(_S1)
            {
                accumulatedOpticalDepth_0 = -3.4028234663852886e+38f;
                break;
            }
            if((bits_1 & 2139095040U) == 0U)
            {
                accumulatedOpticalDepth_0 = log(float(bits_1 & 8388607U)) - 103.2789306640625f;
                break;
            }
            accumulatedOpticalDepth_0 = log(_S78);
            break;
        }
        for(;;)
        {
            uint bits_2 = (as_type<uint>((_S79)));
            if((bits_2 & 2147483648U) != 0U)
            {
                _S2 = true;
            }
            else
            {
                _S2 = (bits_2 & 2147483647U) == 0U;
            }
            if(_S2)
            {
                opticalDepth_0 = -3.4028234663852886e+38f;
                break;
            }
            if((bits_2 & 2139095040U) == 0U)
            {
                opticalDepth_0 = log(float(bits_2 & 8388607U)) - 103.2789306640625f;
                break;
            }
            opticalDepth_0 = log(_S79);
            break;
        }
        fieldLogTint_0[field_1] = float3(power_0, accumulatedOpticalDepth_0, opticalDepth_0);
        if((saturate(_S80)) > 0.0f)
        {
            _S3 = (exits_0[field_1]) > entries_0[field_1];
        }
        else
        {
            _S3 = false;
        }
        fieldEnabled_0[field_1] = _S3;
        densityScales_0[field_1] = 0.0f;
        normalizedDensityIntegrals_0[field_1] = 0.0f;
        fieldOpticalDepth_0[field_1] = 0.0f;
        fieldLogOpticalDepth_0[field_1] = -3.4028234663852886e+38f;
        if(_S3)
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
                smallOpticalDepth_0 = (events_0[cursor_0 - 1U]) > value_0;
            }
            else
            {
                smallOpticalDepth_0 = false;
            }
            if(smallOpticalDepth_0)
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
                if(field_1 < _S9)
                {
                }
                else
                {
                    break;
                }
                if(_S88[field_1])
                {
                    smallOpticalDepth_0 = (_S86[field_1]) <= _S84;
                }
                else
                {
                    smallOpticalDepth_0 = false;
                }
                if(smallOpticalDepth_0)
                {
                    _S1 = (_S87[field_1]) >= _S85;
                }
                else
                {
                    _S1 = false;
                }
                if(_S1)
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
            field_0 = 0U;
            for(;;)
            {
                if(field_0 < _S9)
                {
                }
                else
                {
                    break;
                }
                if(!fieldEnabled_0[field_0])
                {
                    smallOpticalDepth_0 = true;
                }
                else
                {
                    smallOpticalDepth_0 = (entries_0[field_0]) > _S84;
                }
                if(smallOpticalDepth_0)
                {
                    _S1 = true;
                }
                else
                {
                    _S1 = (exits_0[field_0]) < _S85;
                }
                if(_S1)
                {
                    field_0 = field_0 + 1U;
                    continue;
                }
                uint _S92 = fieldOffsets_0[field_0];
                float _S93 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_0])>>2]);
                float _S94 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 4U)>>2]);
                float _S95 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 8U)>>2]);
                float _S96 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 12U)>>2]);
                float4 _S97 = float4(_S93, _S94, _S95, _S96);
                float _S98 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 16U)>>2]);
                float _S99 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 20U)>>2]);
                float _S100 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 24U)>>2]);
                float _S101 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 28U)>>2]);
                float4 _S102 = float4(_S98, _S99, _S100, _S101);
                float _S103 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 32U)>>2]);
                float _S104 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 36U)>>2]);
                float _S105 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 40U)>>2]);
                float _S106 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 44U)>>2]);
                float4 _S107 = float4(_S103, _S104, _S105, _S106);
                float _S108 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 48U)>>2]);
                float _S109 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 52U)>>2]);
                float _S110 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 56U)>>2]);
                float _S111 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S92 + 60U)>>2]);
                float localStepLength_0 = length((((float4(rayDirection_0, 0.0f)) * (matrix<float,int(4),int(4)> (_S97, _S102, _S107, float4(_S108, _S109, _S110, _S111))))).xyz) * _S91;
                float _S112 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_0] + 112U)>>2]);
                float _S113 = max(0.0f, _S112);
                if(_S113 > densityScales_0[field_0])
                {
                    float previousScale_0 = densityScales_0[field_0];
                    if((densityScales_0[field_0]) > 0.0f)
                    {
                        normalizedDensityIntegrals_0[field_0] = normalizedDensityIntegrals_0[field_0] * (previousScale_0 / _S113);
                    }
                    densityScales_0[field_0] = _S113;
                    normalizedDensityIntegrals_0[field_0] = normalizedDensityIntegrals_0[field_0] + localStepLength_0;
                }
                else
                {
                    if((densityScales_0[field_0]) > 0.0f)
                    {
                        normalizedDensityIntegrals_0[field_0] = normalizedDensityIntegrals_0[field_0] + _S113 / densityScales_0[field_0] * localStepLength_0;
                    }
                }
                field_0 = field_0 + 1U;
            }
            step_0 = step_0 + 1U;
        }
        cursor_0 = _S83;
    }
    field_1 = 0U;
    for(;;)
    {
        if(field_1 < _S9)
        {
        }
        else
        {
            break;
        }
        float _S114 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_1] + 108U)>>2]);
        if(fieldEnabled_0[field_1])
        {
            float _S115 = densityScales_0[field_1];
            float _S116 = normalizedDensityIntegrals_0[field_1];
            for(;;)
            {
                float _S117 = max(0.0f, _S115);
                float _S118 = max(0.0f, _S116);
                if(_S117 <= 0.0f)
                {
                    smallOpticalDepth_0 = true;
                }
                else
                {
                    smallOpticalDepth_0 = _S118 <= 0.0f;
                }
                if(smallOpticalDepth_0)
                {
                    intervalStart_0 = 0.0f;
                    break;
                }
                if(_S117 >= (2.12676466649080537e+37f / _S118))
                {
                    intervalStart_0 = 2.12676466649080537e+37f;
                }
                else
                {
                    intervalStart_0 = _S117 * _S118;
                }
                for(;;)
                {
                    float _S119 = max(0.0f, intervalStart_0);
                    float boundedOpacity_0 = saturate(_S114);
                    if(boundedOpacity_0 <= 0.0f)
                    {
                        hasActiveField_0 = true;
                    }
                    else
                    {
                        hasActiveField_0 = _S119 <= 0.0f;
                    }
                    if(hasActiveField_0)
                    {
                        intervalEnd_0 = 0.0f;
                        break;
                    }
                    if(boundedOpacity_0 >= 1.0f)
                    {
                        intervalEnd_0 = _S119;
                        break;
                    }
                    for(;;)
                    {
                        float _S120 = max(_S119, 0.0f);
                        if(_S120 >= 0.125f)
                        {
                            intervalEnd_0 = 1.0f - exp(- _S120);
                            break;
                        }
                        order_0 = 2U;
                        term_0 = _S120;
                        sum_1 = _S120;
                        for(;;)
                        {
                            if(order_0 <= 8U)
                            {
                            }
                            else
                            {
                                break;
                            }
                            float term_1 = term_0 * (_S120 / float(order_0));
                            if((order_0 & 1U) != 0U)
                            {
                                intervalEnd_0 = term_1;
                            }
                            else
                            {
                                intervalEnd_0 = - term_1;
                            }
                            float sum_2 = sum_1 + intervalEnd_0;
                            order_0 = order_0 + 1U;
                            term_0 = term_1;
                            sum_1 = sum_2;
                        }
                        intervalEnd_0 = sum_1;
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
                        sum_1 = removedFraction_0;
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
                            float sum_3 = sum_1 + power_1 / float(order_0);
                            order_0 = order_0 + 1U;
                            power_0 = power_1;
                            sum_1 = sum_3;
                        }
                        power_0 = sum_1;
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
        if(fieldEnabled_0[field_1])
        {
            float _S121 = densityScales_0[field_1];
            float _S122 = normalizedDensityIntegrals_0[field_1];
            float _S123 = fieldOpticalDepth_0[field_1];
            for(;;)
            {
                if(_S123 > 0.0f)
                {
                    for(;;)
                    {
                        uint bits_3 = (as_type<uint>((_S123)));
                        if((bits_3 & 2147483648U) != 0U)
                        {
                            smallOpticalDepth_0 = true;
                        }
                        else
                        {
                            smallOpticalDepth_0 = (bits_3 & 2147483647U) == 0U;
                        }
                        if(smallOpticalDepth_0)
                        {
                            intervalEnd_0 = -3.4028234663852886e+38f;
                            break;
                        }
                        if((bits_3 & 2139095040U) == 0U)
                        {
                            intervalEnd_0 = log(float(bits_3 & 8388607U)) - 103.2789306640625f;
                            break;
                        }
                        intervalEnd_0 = log(_S123);
                        break;
                    }
                    break;
                }
                for(;;)
                {
                    uint bits_4 = (as_type<uint>((_S121)));
                    if((bits_4 & 2147483648U) != 0U)
                    {
                        smallOpticalDepth_0 = true;
                    }
                    else
                    {
                        smallOpticalDepth_0 = (bits_4 & 2147483647U) == 0U;
                    }
                    if(smallOpticalDepth_0)
                    {
                        intervalEnd_0 = -3.4028234663852886e+38f;
                        break;
                    }
                    if((bits_4 & 2139095040U) == 0U)
                    {
                        intervalEnd_0 = log(float(bits_4 & 8388607U)) - 103.2789306640625f;
                        break;
                    }
                    intervalEnd_0 = log(_S121);
                    break;
                }
                for(;;)
                {
                    uint bits_5 = (as_type<uint>((_S122)));
                    if((bits_5 & 2147483648U) != 0U)
                    {
                        hasActiveField_0 = true;
                    }
                    else
                    {
                        hasActiveField_0 = (bits_5 & 2147483647U) == 0U;
                    }
                    if(hasActiveField_0)
                    {
                        power_0 = -3.4028234663852886e+38f;
                        break;
                    }
                    if((bits_5 & 2139095040U) == 0U)
                    {
                        power_0 = log(float(bits_5 & 8388607U)) - 103.2789306640625f;
                        break;
                    }
                    power_0 = log(_S122);
                    break;
                }
                float boundedOpacity_1 = saturate(_S114);
                for(;;)
                {
                    uint bits_6 = (as_type<uint>((boundedOpacity_1)));
                    if((bits_6 & 2147483648U) != 0U)
                    {
                        _S1 = true;
                    }
                    else
                    {
                        _S1 = (bits_6 & 2147483647U) == 0U;
                    }
                    if(_S1)
                    {
                        accumulatedOpticalDepth_0 = -3.4028234663852886e+38f;
                        break;
                    }
                    if((bits_6 & 2139095040U) == 0U)
                    {
                        accumulatedOpticalDepth_0 = log(float(bits_6 & 8388607U)) - 103.2789306640625f;
                        break;
                    }
                    accumulatedOpticalDepth_0 = log(boundedOpacity_1);
                    break;
                }
                if(intervalEnd_0 == -3.4028234663852886e+38f)
                {
                    _S2 = true;
                }
                else
                {
                    _S2 = power_0 == -3.4028234663852886e+38f;
                }
                if(_S2)
                {
                    _S3 = true;
                }
                else
                {
                    _S3 = accumulatedOpticalDepth_0 == -3.4028234663852886e+38f;
                }
                if(_S3)
                {
                    intervalEnd_0 = -3.4028234663852886e+38f;
                    break;
                }
                float logIntegral_0 = power_0 + intervalEnd_0;
                if(boundedOpacity_1 >= 1.0f)
                {
                    _S5 = true;
                }
                else
                {
                    _S5 = logIntegral_0 <= (log(0.00100000004749745f));
                }
                if(_S5)
                {
                    intervalEnd_0 = logIntegral_0 + accumulatedOpticalDepth_0;
                    break;
                }
                float densityIntegral_0 = exp(min(logIntegral_0, log(2.12676466649080537e+37f)));
                for(;;)
                {
                    float _S124 = max(densityIntegral_0, 0.0f);
                    if(_S124 >= 0.125f)
                    {
                        opticalDepth_0 = 1.0f - exp(- _S124);
                        break;
                    }
                    order_0 = 2U;
                    term_0 = _S124;
                    sum_1 = _S124;
                    for(;;)
                    {
                        if(order_0 <= 8U)
                        {
                        }
                        else
                        {
                            break;
                        }
                        float term_2 = term_0 * (_S124 / float(order_0));
                        if((order_0 & 1U) != 0U)
                        {
                            opticalDepth_0 = term_2;
                        }
                        else
                        {
                            opticalDepth_0 = - term_2;
                        }
                        float sum_4 = sum_1 + opticalDepth_0;
                        order_0 = order_0 + 1U;
                        term_0 = term_2;
                        sum_1 = sum_4;
                    }
                    opticalDepth_0 = sum_1;
                    break;
                }
                intervalEnd_0 = accumulatedOpticalDepth_0 + log(opticalDepth_0);
                break;
            }
        }
        else
        {
            intervalEnd_0 = -3.4028234663852886e+38f;
        }
        fieldLogOpticalDepth_0[field_1] = intervalEnd_0;
        field_1 = field_1 + 1U;
    }
    thread VolumeLogSum_0 result_0;
    (&result_0)->maximum_0 = -3.4028234663852886e+38f;
    (&result_0)->sum_0 = 0.0f;
    (&result_0)->compensation_0 = 0.0f;
    thread VolumeLogSum_0 accumulatedRed_0 = result_0;
    thread VolumeLogSum_0 result_1;
    (&result_1)->maximum_0 = -3.4028234663852886e+38f;
    (&result_1)->sum_0 = 0.0f;
    (&result_1)->compensation_0 = 0.0f;
    thread VolumeLogSum_0 accumulatedGreen_0 = result_1;
    thread VolumeLogSum_0 result_2;
    (&result_2)->maximum_0 = -3.4028234663852886e+38f;
    (&result_2)->sum_0 = 0.0f;
    (&result_2)->compensation_0 = 0.0f;
    thread VolumeLogSum_0 accumulatedBlue_0 = result_2;
    thread VolumeLogSum_0 result_3;
    (&result_3)->maximum_0 = -3.4028234663852886e+38f;
    (&result_3)->sum_0 = 0.0f;
    (&result_3)->compensation_0 = 0.0f;
    thread VolumeLogSum_0 totalLogDepth_0 = result_3;
    field_1 = 0U;
    intervalStart_0 = 0.0f;
    for(;;)
    {
        if(field_1 < _S9)
        {
        }
        else
        {
            break;
        }
        float totalFieldOpticalDepth_0 = intervalStart_0 + fieldOpticalDepth_0[field_1];
        float _S125 = fieldLogOpticalDepth_0[field_1];
        for(;;)
        {
            if(_S125 == -3.4028234663852886e+38f)
            {
                break;
            }
            if(((&totalLogDepth_0)->sum_0) == 0.0f)
            {
                (&totalLogDepth_0)->maximum_0 = _S125;
                (&totalLogDepth_0)->sum_0 = 1.0f;
                break;
            }
            if(_S125 > ((&totalLogDepth_0)->maximum_0))
            {
                float scale_0 = exp((&totalLogDepth_0)->maximum_0 - _S125);
                (&totalLogDepth_0)->sum_0 = (&totalLogDepth_0)->sum_0 * scale_0;
                (&totalLogDepth_0)->compensation_0 = (&totalLogDepth_0)->compensation_0 * scale_0;
                (&totalLogDepth_0)->maximum_0 = _S125;
                contribution_0 = 1.0f;
            }
            else
            {
                contribution_0 = exp(_S125 - (&totalLogDepth_0)->maximum_0);
            }
            float adjusted_0 = contribution_0 - (&totalLogDepth_0)->compensation_0;
            float next_0 = (&totalLogDepth_0)->sum_0 + adjusted_0;
            (&totalLogDepth_0)->compensation_0 = next_0 - (&totalLogDepth_0)->sum_0 - adjusted_0;
            (&totalLogDepth_0)->sum_0 = next_0;
            break;
        }
        field_1 = field_1 + 1U;
        intervalStart_0 = totalFieldOpticalDepth_0;
    }
    VolumeLogSum_0 _S126 = totalLogDepth_0;
    if((_S126.sum_0) == 0.0f)
    {
        intervalEnd_0 = -3.4028234663852886e+38f;
    }
    else
    {
        intervalEnd_0 = _S126.maximum_0 + log(_S126.sum_0);
    }
    if(intervalEnd_0 != -3.4028234663852886e+38f)
    {
        smallOpticalDepth_0 = intervalEnd_0 <= (log(0.00100000004749745f));
    }
    else
    {
        smallOpticalDepth_0 = false;
    }
    if(smallOpticalDepth_0)
    {
        field_1 = 0U;
        for(;;)
        {
            if(field_1 < _S9)
            {
            }
            else
            {
                break;
            }
            if((fieldLogOpticalDepth_0[field_1]) != -3.4028234663852886e+38f)
            {
                float3 _S127 = fieldLogTint_0[field_1];
                float _S128 = fieldLogOpticalDepth_0[field_1];
                float _S129 = fieldLogTint_0[field_1].x;
                if(_S129 != -3.4028234663852886e+38f)
                {
                    float _S130 = _S129 + _S128;
                    for(;;)
                    {
                        if(_S130 == -3.4028234663852886e+38f)
                        {
                            break;
                        }
                        if(((&accumulatedRed_0)->sum_0) == 0.0f)
                        {
                            (&accumulatedRed_0)->maximum_0 = _S130;
                            (&accumulatedRed_0)->sum_0 = 1.0f;
                            break;
                        }
                        if(_S130 > ((&accumulatedRed_0)->maximum_0))
                        {
                            float scale_1 = exp((&accumulatedRed_0)->maximum_0 - _S130);
                            (&accumulatedRed_0)->sum_0 = (&accumulatedRed_0)->sum_0 * scale_1;
                            (&accumulatedRed_0)->compensation_0 = (&accumulatedRed_0)->compensation_0 * scale_1;
                            (&accumulatedRed_0)->maximum_0 = _S130;
                            contribution_0 = 1.0f;
                        }
                        else
                        {
                            contribution_0 = exp(_S130 - (&accumulatedRed_0)->maximum_0);
                        }
                        float adjusted_1 = contribution_0 - (&accumulatedRed_0)->compensation_0;
                        float next_1 = (&accumulatedRed_0)->sum_0 + adjusted_1;
                        (&accumulatedRed_0)->compensation_0 = next_1 - (&accumulatedRed_0)->sum_0 - adjusted_1;
                        (&accumulatedRed_0)->sum_0 = next_1;
                        break;
                    }
                }
                float _S131 = _S127.y;
                if(_S131 != -3.4028234663852886e+38f)
                {
                    float _S132 = _S131 + _S128;
                    for(;;)
                    {
                        if(_S132 == -3.4028234663852886e+38f)
                        {
                            break;
                        }
                        if(((&accumulatedGreen_0)->sum_0) == 0.0f)
                        {
                            (&accumulatedGreen_0)->maximum_0 = _S132;
                            (&accumulatedGreen_0)->sum_0 = 1.0f;
                            break;
                        }
                        if(_S132 > ((&accumulatedGreen_0)->maximum_0))
                        {
                            float scale_2 = exp((&accumulatedGreen_0)->maximum_0 - _S132);
                            (&accumulatedGreen_0)->sum_0 = (&accumulatedGreen_0)->sum_0 * scale_2;
                            (&accumulatedGreen_0)->compensation_0 = (&accumulatedGreen_0)->compensation_0 * scale_2;
                            (&accumulatedGreen_0)->maximum_0 = _S132;
                            contribution_0 = 1.0f;
                        }
                        else
                        {
                            contribution_0 = exp(_S132 - (&accumulatedGreen_0)->maximum_0);
                        }
                        float adjusted_2 = contribution_0 - (&accumulatedGreen_0)->compensation_0;
                        float next_2 = (&accumulatedGreen_0)->sum_0 + adjusted_2;
                        (&accumulatedGreen_0)->compensation_0 = next_2 - (&accumulatedGreen_0)->sum_0 - adjusted_2;
                        (&accumulatedGreen_0)->sum_0 = next_2;
                        break;
                    }
                }
                float _S133 = _S127.z;
                if(_S133 != -3.4028234663852886e+38f)
                {
                    float _S134 = _S133 + _S128;
                    for(;;)
                    {
                        if(_S134 == -3.4028234663852886e+38f)
                        {
                            break;
                        }
                        if(((&accumulatedBlue_0)->sum_0) == 0.0f)
                        {
                            (&accumulatedBlue_0)->maximum_0 = _S134;
                            (&accumulatedBlue_0)->sum_0 = 1.0f;
                            break;
                        }
                        if(_S134 > ((&accumulatedBlue_0)->maximum_0))
                        {
                            float scale_3 = exp((&accumulatedBlue_0)->maximum_0 - _S134);
                            (&accumulatedBlue_0)->sum_0 = (&accumulatedBlue_0)->sum_0 * scale_3;
                            (&accumulatedBlue_0)->compensation_0 = (&accumulatedBlue_0)->compensation_0 * scale_3;
                            (&accumulatedBlue_0)->maximum_0 = _S134;
                            contribution_0 = 1.0f;
                        }
                        else
                        {
                            contribution_0 = exp(_S134 - (&accumulatedBlue_0)->maximum_0);
                        }
                        float adjusted_3 = contribution_0 - (&accumulatedBlue_0)->compensation_0;
                        float next_3 = (&accumulatedBlue_0)->sum_0 + adjusted_3;
                        (&accumulatedBlue_0)->compensation_0 = next_3 - (&accumulatedBlue_0)->sum_0 - adjusted_3;
                        (&accumulatedBlue_0)->sum_0 = next_3;
                        break;
                    }
                }
            }
            field_1 = field_1 + 1U;
        }
        accumulatedOpticalDepth_0 = intervalStart_0;
    }
    else
    {
        cursor_0 = 0U;
        power_0 = 0.0f;
        for(;;)
        {
            uint _S135 = cursor_0 + 1U;
            if(_S135 < eventCount_0)
            {
            }
            else
            {
                break;
            }
            float _S136 = max(events_0[cursor_0], intervalStart_1);
            float _S137 = min(events_0[_S135], intervalEnd_1);
            if(_S137 <= _S136)
            {
                accumulatedOpticalDepth_0 = power_0;
                cursor_0 = _S135;
                power_0 = accumulatedOpticalDepth_0;
                continue;
            }
            thread array<bool, int(8)> active_0;
            field_1 = 0U;
            for(;;)
            {
                if(field_1 < _S9)
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
                    _S1 = (entries_0[field_1]) <= _S136;
                }
                else
                {
                    _S1 = false;
                }
                if(_S1)
                {
                    _S2 = (exits_0[field_1]) >= _S137;
                }
                else
                {
                    _S2 = false;
                }
                active_0[field_1] = _S2;
                field_1 = field_1 + 1U;
            }
            array<float, int(8)> _S138 = entries_0;
            array<float, int(8)> _S139 = exits_0;
            array<bool, int(8)> _S140 = fieldEnabled_0;
            array<uint, int(8)> _S141 = fieldOffsets_0;
            for(;;)
            {
                field_0 = 0U;
                stepCount_0 = 0U;
                hasActiveField_0 = false;
                for(;;)
                {
                    if(field_0 < _S9)
                    {
                    }
                    else
                    {
                        break;
                    }
                    if(_S140[field_0])
                    {
                        _S1 = (_S138[field_0]) <= _S136;
                    }
                    else
                    {
                        _S1 = false;
                    }
                    if(_S1)
                    {
                        _S2 = (_S139[field_0]) >= _S137;
                    }
                    else
                    {
                        _S2 = false;
                    }
                    if(_S2)
                    {
                        uint _S142 = as_type<uint>((&kernelContext_0)->volumeFields_0[(_S141[field_0] + 92U)>>2]);
                        uint stepCount_2 = stepCount_0 + 2U * _S142;
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
                accumulatedOpticalDepth_0 = power_0;
                cursor_0 = _S135;
                power_0 = accumulatedOpticalDepth_0;
                continue;
            }
            float _S143 = (_S137 - _S136) / float(planeIndex_0);
            step_0 = 0U;
            accumulatedOpticalDepth_0 = power_0;
            for(;;)
            {
                if(step_0 < planeIndex_0)
                {
                }
                else
                {
                    break;
                }
                thread array<float, int(8)> fieldLogStepDepths_0;
                thread VolumeLogSum_0 result_4;
                (&result_4)->maximum_0 = -3.4028234663852886e+38f;
                (&result_4)->sum_0 = 0.0f;
                (&result_4)->compensation_0 = 0.0f;
                thread VolumeLogSum_0 stepLogDepth_0 = result_4;
                uint field_2 = 0U;
                opticalDepth_0 = 0.0f;
                for(;;)
                {
                    if(field_2 < _S9)
                    {
                    }
                    else
                    {
                        break;
                    }
                    fieldLogStepDepths_0[field_2] = -3.4028234663852886e+38f;
                    if(!active_0[field_2])
                    {
                        opticalDepth_1 = opticalDepth_0;
                        field_2 = field_2 + 1U;
                        opticalDepth_0 = opticalDepth_1;
                        continue;
                    }
                    uint _S144 = fieldOffsets_0[field_2];
                    float _S145 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_2])>>2]);
                    float _S146 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 4U)>>2]);
                    float _S147 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 8U)>>2]);
                    float _S148 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 12U)>>2]);
                    float4 _S149 = float4(_S145, _S146, _S147, _S148);
                    float _S150 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 16U)>>2]);
                    float _S151 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 20U)>>2]);
                    float _S152 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 24U)>>2]);
                    float _S153 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 28U)>>2]);
                    float4 _S154 = float4(_S150, _S151, _S152, _S153);
                    float _S155 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 32U)>>2]);
                    float _S156 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 36U)>>2]);
                    float _S157 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 40U)>>2]);
                    float _S158 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 44U)>>2]);
                    float4 _S159 = float4(_S155, _S156, _S157, _S158);
                    float _S160 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 48U)>>2]);
                    float _S161 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 52U)>>2]);
                    float _S162 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 56U)>>2]);
                    float _S163 = as_type<float>((&kernelContext_0)->volumeFields_0[(_S144 + 60U)>>2]);
                    float3 _S164 = (((float4(rayDirection_0, 0.0f)) * (matrix<float,int(4),int(4)> (_S149, _S154, _S159, float4(_S160, _S161, _S162, _S163))))).xyz;
                    float _S165 = as_type<float>((&kernelContext_0)->volumeFields_0[(fieldOffsets_0[field_2] + 112U)>>2]);
                    float _S166 = max(0.0f, _S165);
                    float localSpeed_0 = length(_S164);
                    float fieldDepth_0 = fieldOpticalDepth_0[field_2] * (_S166 / densityScales_0[field_2] * (localSpeed_0 * _S143) / normalizedDensityIntegrals_0[field_2]);
                    float opticalDepth_2 = opticalDepth_0 + fieldDepth_0;
                    if(fieldDepth_0 > 0.0f)
                    {
                        for(;;)
                        {
                            uint bits_7 = (as_type<uint>((fieldDepth_0)));
                            if((bits_7 & 2147483648U) != 0U)
                            {
                                _S1 = true;
                            }
                            else
                            {
                                _S1 = (bits_7 & 2147483647U) == 0U;
                            }
                            if(_S1)
                            {
                                intervalStart_0 = -3.4028234663852886e+38f;
                                break;
                            }
                            if((bits_7 & 2139095040U) == 0U)
                            {
                                intervalStart_0 = log(float(bits_7 & 8388607U)) - 103.2789306640625f;
                                break;
                            }
                            intervalStart_0 = log(fieldDepth_0);
                            break;
                        }
                        fieldLogStepDepths_0[field_2] = intervalStart_0;
                        opticalDepth_1 = opticalDepth_2;
                    }
                    else
                    {
                        for(;;)
                        {
                            uint bits_8 = (as_type<uint>((_S166)));
                            if((bits_8 & 2147483648U) != 0U)
                            {
                                _S1 = true;
                            }
                            else
                            {
                                _S1 = (bits_8 & 2147483647U) == 0U;
                            }
                            if(_S1)
                            {
                                intervalStart_0 = -3.4028234663852886e+38f;
                                break;
                            }
                            if((bits_8 & 2139095040U) == 0U)
                            {
                                intervalStart_0 = log(float(bits_8 & 8388607U)) - 103.2789306640625f;
                                break;
                            }
                            intervalStart_0 = log(_S166);
                            break;
                        }
                        float _S167 = densityScales_0[field_2];
                        for(;;)
                        {
                            uint bits_9 = (as_type<uint>((_S167)));
                            if((bits_9 & 2147483648U) != 0U)
                            {
                                _S2 = true;
                            }
                            else
                            {
                                _S2 = (bits_9 & 2147483647U) == 0U;
                            }
                            if(_S2)
                            {
                                term_0 = -3.4028234663852886e+38f;
                                break;
                            }
                            if((bits_9 & 2139095040U) == 0U)
                            {
                                term_0 = log(float(bits_9 & 8388607U)) - 103.2789306640625f;
                                break;
                            }
                            term_0 = log(_S167);
                            break;
                        }
                        float _S168 = normalizedDensityIntegrals_0[field_2];
                        for(;;)
                        {
                            uint bits_10 = (as_type<uint>((_S168)));
                            if((bits_10 & 2147483648U) != 0U)
                            {
                                _S3 = true;
                            }
                            else
                            {
                                _S3 = (bits_10 & 2147483647U) == 0U;
                            }
                            if(_S3)
                            {
                                sum_1 = -3.4028234663852886e+38f;
                                break;
                            }
                            if((bits_10 & 2139095040U) == 0U)
                            {
                                sum_1 = log(float(bits_10 & 8388607U)) - 103.2789306640625f;
                                break;
                            }
                            sum_1 = log(_S168);
                            break;
                        }
                        for(;;)
                        {
                            uint bits_11 = (as_type<uint>((localSpeed_0)));
                            if((bits_11 & 2147483648U) != 0U)
                            {
                                _S5 = true;
                            }
                            else
                            {
                                _S5 = (bits_11 & 2147483647U) == 0U;
                            }
                            if(_S5)
                            {
                                _S6 = -3.4028234663852886e+38f;
                                break;
                            }
                            if((bits_11 & 2139095040U) == 0U)
                            {
                                _S6 = log(float(bits_11 & 8388607U)) - 103.2789306640625f;
                                break;
                            }
                            _S6 = log(localSpeed_0);
                            break;
                        }
                        for(;;)
                        {
                            uint bits_12 = (as_type<uint>((_S143)));
                            bool _S169;
                            if((bits_12 & 2147483648U) != 0U)
                            {
                                _S169 = true;
                            }
                            else
                            {
                                _S169 = (bits_12 & 2147483647U) == 0U;
                            }
                            if(_S169)
                            {
                                contribution_0 = -3.4028234663852886e+38f;
                                break;
                            }
                            if((bits_12 & 2139095040U) == 0U)
                            {
                                contribution_0 = log(float(bits_12 & 8388607U)) - 103.2789306640625f;
                                break;
                            }
                            contribution_0 = log(_S143);
                            break;
                        }
                        bool _S170;
                        if((fieldLogOpticalDepth_0[field_2]) != -3.4028234663852886e+38f)
                        {
                            _S170 = intervalStart_0 != -3.4028234663852886e+38f;
                        }
                        else
                        {
                            _S170 = false;
                        }
                        bool _S171;
                        if(_S170)
                        {
                            _S171 = term_0 != -3.4028234663852886e+38f;
                        }
                        else
                        {
                            _S171 = false;
                        }
                        bool _S172;
                        if(_S171)
                        {
                            _S172 = sum_1 != -3.4028234663852886e+38f;
                        }
                        else
                        {
                            _S172 = false;
                        }
                        bool _S173;
                        if(_S172)
                        {
                            _S173 = _S6 != -3.4028234663852886e+38f;
                        }
                        else
                        {
                            _S173 = false;
                        }
                        bool _S174;
                        if(_S173)
                        {
                            _S174 = contribution_0 != -3.4028234663852886e+38f;
                        }
                        else
                        {
                            _S174 = false;
                        }
                        if(_S174)
                        {
                            float _S175 = fieldLogOpticalDepth_0[field_2] + intervalStart_0 - term_0 + _S6 + contribution_0 - sum_1;
                            fieldLogStepDepths_0[field_2] = _S175;
                            opticalDepth_1 = opticalDepth_2 + exp(_S175);
                        }
                        else
                        {
                            opticalDepth_1 = opticalDepth_2;
                        }
                    }
                    float _S176 = fieldLogStepDepths_0[field_2];
                    for(;;)
                    {
                        if(_S176 == -3.4028234663852886e+38f)
                        {
                            break;
                        }
                        if(((&stepLogDepth_0)->sum_0) == 0.0f)
                        {
                            (&stepLogDepth_0)->maximum_0 = _S176;
                            (&stepLogDepth_0)->sum_0 = 1.0f;
                            break;
                        }
                        if(_S176 > ((&stepLogDepth_0)->maximum_0))
                        {
                            float scale_4 = exp((&stepLogDepth_0)->maximum_0 - _S176);
                            (&stepLogDepth_0)->sum_0 = (&stepLogDepth_0)->sum_0 * scale_4;
                            (&stepLogDepth_0)->compensation_0 = (&stepLogDepth_0)->compensation_0 * scale_4;
                            (&stepLogDepth_0)->maximum_0 = _S176;
                            contribution_0 = 1.0f;
                        }
                        else
                        {
                            contribution_0 = exp(_S176 - (&stepLogDepth_0)->maximum_0);
                        }
                        float adjusted_4 = contribution_0 - (&stepLogDepth_0)->compensation_0;
                        float next_4 = (&stepLogDepth_0)->sum_0 + adjusted_4;
                        (&stepLogDepth_0)->compensation_0 = next_4 - (&stepLogDepth_0)->sum_0 - adjusted_4;
                        (&stepLogDepth_0)->sum_0 = next_4;
                        break;
                    }
                    field_2 = field_2 + 1U;
                    opticalDepth_0 = opticalDepth_1;
                }
                VolumeLogSum_0 _S177 = stepLogDepth_0;
                if((_S177.sum_0) == 0.0f)
                {
                    intervalStart_0 = -3.4028234663852886e+38f;
                }
                else
                {
                    intervalStart_0 = _S177.maximum_0 + log(_S177.sum_0);
                }
                if(intervalStart_0 != -3.4028234663852886e+38f)
                {
                    for(;;)
                    {
                        float _S178 = max(opticalDepth_0, 0.0f);
                        if(_S178 >= 0.125f)
                        {
                            term_0 = 1.0f - exp(- _S178);
                            break;
                        }
                        order_0 = 2U;
                        term_0 = _S178;
                        sum_1 = _S178;
                        for(;;)
                        {
                            if(order_0 <= 8U)
                            {
                            }
                            else
                            {
                                break;
                            }
                            float term_3 = term_0 * (_S178 / float(order_0));
                            if((order_0 & 1U) != 0U)
                            {
                                _S6 = term_3;
                            }
                            else
                            {
                                _S6 = - term_3;
                            }
                            float sum_5 = sum_1 + _S6;
                            order_0 = order_0 + 1U;
                            term_0 = term_3;
                            sum_1 = sum_5;
                        }
                        term_0 = sum_1;
                        break;
                    }
                    thread VolumeLogSum_0 result_5;
                    (&result_5)->maximum_0 = -3.4028234663852886e+38f;
                    (&result_5)->sum_0 = 0.0f;
                    (&result_5)->compensation_0 = 0.0f;
                    thread VolumeLogSum_0 sourceRed_0 = result_5;
                    thread VolumeLogSum_0 result_6;
                    (&result_6)->maximum_0 = -3.4028234663852886e+38f;
                    (&result_6)->sum_0 = 0.0f;
                    (&result_6)->compensation_0 = 0.0f;
                    thread VolumeLogSum_0 sourceGreen_0 = result_6;
                    thread VolumeLogSum_0 result_7;
                    (&result_7)->maximum_0 = -3.4028234663852886e+38f;
                    (&result_7)->sum_0 = 0.0f;
                    (&result_7)->compensation_0 = 0.0f;
                    thread VolumeLogSum_0 sourceBlue_0 = result_7;
                    order_0 = 0U;
                    for(;;)
                    {
                        if(order_0 < _S9)
                        {
                        }
                        else
                        {
                            break;
                        }
                        if((fieldLogStepDepths_0[order_0]) != -3.4028234663852886e+38f)
                        {
                            float3 _S179 = fieldLogTint_0[order_0];
                            float _S180 = fieldLogStepDepths_0[order_0];
                            float _S181 = fieldLogTint_0[order_0].x;
                            if(_S181 != -3.4028234663852886e+38f)
                            {
                                float _S182 = _S181 + _S180;
                                for(;;)
                                {
                                    if(_S182 == -3.4028234663852886e+38f)
                                    {
                                        break;
                                    }
                                    if(((&sourceRed_0)->sum_0) == 0.0f)
                                    {
                                        (&sourceRed_0)->maximum_0 = _S182;
                                        (&sourceRed_0)->sum_0 = 1.0f;
                                        break;
                                    }
                                    if(_S182 > ((&sourceRed_0)->maximum_0))
                                    {
                                        float scale_5 = exp((&sourceRed_0)->maximum_0 - _S182);
                                        (&sourceRed_0)->sum_0 = (&sourceRed_0)->sum_0 * scale_5;
                                        (&sourceRed_0)->compensation_0 = (&sourceRed_0)->compensation_0 * scale_5;
                                        (&sourceRed_0)->maximum_0 = _S182;
                                        contribution_0 = 1.0f;
                                    }
                                    else
                                    {
                                        contribution_0 = exp(_S182 - (&sourceRed_0)->maximum_0);
                                    }
                                    float adjusted_5 = contribution_0 - (&sourceRed_0)->compensation_0;
                                    float next_5 = (&sourceRed_0)->sum_0 + adjusted_5;
                                    (&sourceRed_0)->compensation_0 = next_5 - (&sourceRed_0)->sum_0 - adjusted_5;
                                    (&sourceRed_0)->sum_0 = next_5;
                                    break;
                                }
                            }
                            float _S183 = _S179.y;
                            if(_S183 != -3.4028234663852886e+38f)
                            {
                                float _S184 = _S183 + _S180;
                                for(;;)
                                {
                                    if(_S184 == -3.4028234663852886e+38f)
                                    {
                                        break;
                                    }
                                    if(((&sourceGreen_0)->sum_0) == 0.0f)
                                    {
                                        (&sourceGreen_0)->maximum_0 = _S184;
                                        (&sourceGreen_0)->sum_0 = 1.0f;
                                        break;
                                    }
                                    if(_S184 > ((&sourceGreen_0)->maximum_0))
                                    {
                                        float scale_6 = exp((&sourceGreen_0)->maximum_0 - _S184);
                                        (&sourceGreen_0)->sum_0 = (&sourceGreen_0)->sum_0 * scale_6;
                                        (&sourceGreen_0)->compensation_0 = (&sourceGreen_0)->compensation_0 * scale_6;
                                        (&sourceGreen_0)->maximum_0 = _S184;
                                        contribution_0 = 1.0f;
                                    }
                                    else
                                    {
                                        contribution_0 = exp(_S184 - (&sourceGreen_0)->maximum_0);
                                    }
                                    float adjusted_6 = contribution_0 - (&sourceGreen_0)->compensation_0;
                                    float next_6 = (&sourceGreen_0)->sum_0 + adjusted_6;
                                    (&sourceGreen_0)->compensation_0 = next_6 - (&sourceGreen_0)->sum_0 - adjusted_6;
                                    (&sourceGreen_0)->sum_0 = next_6;
                                    break;
                                }
                            }
                            float _S185 = _S179.z;
                            if(_S185 != -3.4028234663852886e+38f)
                            {
                                float _S186 = _S185 + _S180;
                                for(;;)
                                {
                                    if(_S186 == -3.4028234663852886e+38f)
                                    {
                                        break;
                                    }
                                    if(((&sourceBlue_0)->sum_0) == 0.0f)
                                    {
                                        (&sourceBlue_0)->maximum_0 = _S186;
                                        (&sourceBlue_0)->sum_0 = 1.0f;
                                        break;
                                    }
                                    if(_S186 > ((&sourceBlue_0)->maximum_0))
                                    {
                                        float scale_7 = exp((&sourceBlue_0)->maximum_0 - _S186);
                                        (&sourceBlue_0)->sum_0 = (&sourceBlue_0)->sum_0 * scale_7;
                                        (&sourceBlue_0)->compensation_0 = (&sourceBlue_0)->compensation_0 * scale_7;
                                        (&sourceBlue_0)->maximum_0 = _S186;
                                        contribution_0 = 1.0f;
                                    }
                                    else
                                    {
                                        contribution_0 = exp(_S186 - (&sourceBlue_0)->maximum_0);
                                    }
                                    float adjusted_7 = contribution_0 - (&sourceBlue_0)->compensation_0;
                                    float next_7 = (&sourceBlue_0)->sum_0 + adjusted_7;
                                    (&sourceBlue_0)->compensation_0 = next_7 - (&sourceBlue_0)->sum_0 - adjusted_7;
                                    (&sourceBlue_0)->sum_0 = next_7;
                                    break;
                                }
                            }
                        }
                        order_0 = order_0 + 1U;
                    }
                    if(term_0 > 0.0f)
                    {
                        for(;;)
                        {
                            uint bits_13 = (as_type<uint>((term_0)));
                            if((bits_13 & 2147483648U) != 0U)
                            {
                                _S1 = true;
                            }
                            else
                            {
                                _S1 = (bits_13 & 2147483647U) == 0U;
                            }
                            if(_S1)
                            {
                                sum_1 = -3.4028234663852886e+38f;
                                break;
                            }
                            if((bits_13 & 2139095040U) == 0U)
                            {
                                sum_1 = log(float(bits_13 & 8388607U)) - 103.2789306640625f;
                                break;
                            }
                            sum_1 = log(term_0);
                            break;
                        }
                    }
                    else
                    {
                        sum_1 = intervalStart_0;
                    }
                    VolumeLogSum_0 _S187 = sourceRed_0;
                    if((_S187.sum_0) == 0.0f)
                    {
                        _S6 = -3.4028234663852886e+38f;
                    }
                    else
                    {
                        _S6 = _S187.maximum_0 + log(_S187.sum_0);
                    }
                    if(_S6 != -3.4028234663852886e+38f)
                    {
                        float _S188 = _S6 - intervalStart_0 + sum_1 - accumulatedOpticalDepth_0;
                        for(;;)
                        {
                            if(_S188 == -3.4028234663852886e+38f)
                            {
                                break;
                            }
                            if(((&accumulatedRed_0)->sum_0) == 0.0f)
                            {
                                (&accumulatedRed_0)->maximum_0 = _S188;
                                (&accumulatedRed_0)->sum_0 = 1.0f;
                                break;
                            }
                            if(_S188 > ((&accumulatedRed_0)->maximum_0))
                            {
                                float scale_8 = exp((&accumulatedRed_0)->maximum_0 - _S188);
                                (&accumulatedRed_0)->sum_0 = (&accumulatedRed_0)->sum_0 * scale_8;
                                (&accumulatedRed_0)->compensation_0 = (&accumulatedRed_0)->compensation_0 * scale_8;
                                (&accumulatedRed_0)->maximum_0 = _S188;
                                contribution_0 = 1.0f;
                            }
                            else
                            {
                                contribution_0 = exp(_S188 - (&accumulatedRed_0)->maximum_0);
                            }
                            float adjusted_8 = contribution_0 - (&accumulatedRed_0)->compensation_0;
                            float next_8 = (&accumulatedRed_0)->sum_0 + adjusted_8;
                            (&accumulatedRed_0)->compensation_0 = next_8 - (&accumulatedRed_0)->sum_0 - adjusted_8;
                            (&accumulatedRed_0)->sum_0 = next_8;
                            break;
                        }
                    }
                    VolumeLogSum_0 _S189 = sourceGreen_0;
                    if((sourceGreen_0.sum_0) == 0.0f)
                    {
                        contribution_0 = -3.4028234663852886e+38f;
                    }
                    else
                    {
                        contribution_0 = _S189.maximum_0 + log(_S189.sum_0);
                    }
                    if(contribution_0 != -3.4028234663852886e+38f)
                    {
                        float _S190 = contribution_0 - intervalStart_0 + sum_1 - accumulatedOpticalDepth_0;
                        for(;;)
                        {
                            if(_S190 == -3.4028234663852886e+38f)
                            {
                                break;
                            }
                            if(((&accumulatedGreen_0)->sum_0) == 0.0f)
                            {
                                (&accumulatedGreen_0)->maximum_0 = _S190;
                                (&accumulatedGreen_0)->sum_0 = 1.0f;
                                break;
                            }
                            if(_S190 > ((&accumulatedGreen_0)->maximum_0))
                            {
                                float scale_9 = exp((&accumulatedGreen_0)->maximum_0 - _S190);
                                (&accumulatedGreen_0)->sum_0 = (&accumulatedGreen_0)->sum_0 * scale_9;
                                (&accumulatedGreen_0)->compensation_0 = (&accumulatedGreen_0)->compensation_0 * scale_9;
                                (&accumulatedGreen_0)->maximum_0 = _S190;
                                opticalDepth_1 = 1.0f;
                            }
                            else
                            {
                                opticalDepth_1 = exp(_S190 - (&accumulatedGreen_0)->maximum_0);
                            }
                            float adjusted_9 = opticalDepth_1 - (&accumulatedGreen_0)->compensation_0;
                            float next_9 = (&accumulatedGreen_0)->sum_0 + adjusted_9;
                            (&accumulatedGreen_0)->compensation_0 = next_9 - (&accumulatedGreen_0)->sum_0 - adjusted_9;
                            (&accumulatedGreen_0)->sum_0 = next_9;
                            break;
                        }
                    }
                    VolumeLogSum_0 _S191 = sourceBlue_0;
                    if((sourceBlue_0.sum_0) == 0.0f)
                    {
                        opticalDepth_1 = -3.4028234663852886e+38f;
                    }
                    else
                    {
                        opticalDepth_1 = _S191.maximum_0 + log(_S191.sum_0);
                    }
                    if(opticalDepth_1 != -3.4028234663852886e+38f)
                    {
                        float _S192 = opticalDepth_1 - intervalStart_0 + sum_1 - accumulatedOpticalDepth_0;
                        for(;;)
                        {
                            if(_S192 == -3.4028234663852886e+38f)
                            {
                                break;
                            }
                            if(((&accumulatedBlue_0)->sum_0) == 0.0f)
                            {
                                (&accumulatedBlue_0)->maximum_0 = _S192;
                                (&accumulatedBlue_0)->sum_0 = 1.0f;
                                break;
                            }
                            float contribution_1;
                            if(_S192 > ((&accumulatedBlue_0)->maximum_0))
                            {
                                float scale_10 = exp((&accumulatedBlue_0)->maximum_0 - _S192);
                                (&accumulatedBlue_0)->sum_0 = (&accumulatedBlue_0)->sum_0 * scale_10;
                                (&accumulatedBlue_0)->compensation_0 = (&accumulatedBlue_0)->compensation_0 * scale_10;
                                (&accumulatedBlue_0)->maximum_0 = _S192;
                                contribution_1 = 1.0f;
                            }
                            else
                            {
                                contribution_1 = exp(_S192 - (&accumulatedBlue_0)->maximum_0);
                            }
                            float adjusted_10 = contribution_1 - (&accumulatedBlue_0)->compensation_0;
                            float next_10 = (&accumulatedBlue_0)->sum_0 + adjusted_10;
                            (&accumulatedBlue_0)->compensation_0 = next_10 - (&accumulatedBlue_0)->sum_0 - adjusted_10;
                            (&accumulatedBlue_0)->sum_0 = next_10;
                            break;
                        }
                    }
                    accumulatedOpticalDepth_0 = accumulatedOpticalDepth_0 + opticalDepth_0;
                }
                step_0 = step_0 + 1U;
            }
            cursor_0 = _S135;
            power_0 = accumulatedOpticalDepth_0;
        }
        accumulatedOpticalDepth_0 = power_0;
    }
    if(!smallOpticalDepth_0)
    {
        hasActiveField_0 = accumulatedOpticalDepth_0 <= 0.0f;
    }
    else
    {
        hasActiveField_0 = false;
    }
    if(hasActiveField_0)
    {
        pixelOutput_0 _S193 = { background_0 };
        return _S193;
    }
    for(;;)
    {
        float _S194 = max(accumulatedOpticalDepth_0, 0.0f);
        if(_S194 >= 0.125f)
        {
            intervalStart_1 = 1.0f - exp(- _S194);
            break;
        }
        order_0 = 2U;
        term_0 = _S194;
        sum_1 = _S194;
        for(;;)
        {
            if(order_0 <= 8U)
            {
            }
            else
            {
                break;
            }
            float term_4 = term_0 * (_S194 / float(order_0));
            if((order_0 & 1U) != 0U)
            {
                intervalStart_1 = term_4;
            }
            else
            {
                intervalStart_1 = - term_4;
            }
            float sum_6 = sum_1 + intervalStart_1;
            order_0 = order_0 + 1U;
            term_0 = term_4;
            sum_1 = sum_6;
        }
        intervalStart_1 = sum_1;
        break;
    }
    if(intervalStart_1 > 0.0f)
    {
        for(;;)
        {
            uint bits_14 = (as_type<uint>((intervalStart_1)));
            if((bits_14 & 2147483648U) != 0U)
            {
                hasActiveField_0 = true;
            }
            else
            {
                hasActiveField_0 = (bits_14 & 2147483647U) == 0U;
            }
            if(hasActiveField_0)
            {
                intervalEnd_1 = -3.4028234663852886e+38f;
                break;
            }
            if((bits_14 & 2139095040U) == 0U)
            {
                intervalEnd_1 = log(float(bits_14 & 8388607U)) - 103.2789306640625f;
                break;
            }
            intervalEnd_1 = log(intervalStart_1);
            break;
        }
        power_0 = intervalEnd_1;
    }
    else
    {
        power_0 = intervalEnd_0;
    }
    if(smallOpticalDepth_0)
    {
    }
    else
    {
        intervalEnd_0 = power_0;
    }
    uint _S195 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
    float logExposure_0 = (as_type<float>((_S195))) * 0.69314718246459961f;
    VolumeLogSum_0 _S196 = accumulatedRed_0;
    if((_S196.sum_0) == 0.0f)
    {
        intervalEnd_1 = -3.4028234663852886e+38f;
    }
    else
    {
        intervalEnd_1 = _S196.maximum_0 + log(_S196.sum_0);
    }
    VolumeLogSum_0 _S197 = accumulatedGreen_0;
    if((accumulatedGreen_0.sum_0) == 0.0f)
    {
        intervalStart_0 = -3.4028234663852886e+38f;
    }
    else
    {
        intervalStart_0 = _S197.maximum_0 + log(_S197.sum_0);
    }
    VolumeLogSum_0 _S198 = accumulatedBlue_0;
    if((accumulatedBlue_0.sum_0) == 0.0f)
    {
        opticalDepth_0 = -3.4028234663852886e+38f;
    }
    else
    {
        opticalDepth_0 = _S198.maximum_0 + log(_S198.sum_0);
    }
    uint _S199 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
    float3 outputColor_0;
    if(_S199 == 1U)
    {
        if(intervalEnd_1 == -3.4028234663852886e+38f)
        {
            intervalEnd_1 = -3.4028234663852886e+38f;
        }
        else
        {
            intervalEnd_1 = intervalEnd_1 - intervalEnd_0 + logExposure_0;
        }
        for(;;)
        {
            if(intervalEnd_1 == -3.4028234663852886e+38f)
            {
                intervalEnd_1 = 0.0f;
                break;
            }
            if(!isfinite(intervalEnd_1))
            {
                intervalEnd_1 = exp(intervalEnd_1);
                break;
            }
            if(intervalEnd_1 >= 88.72283935546875f)
            {
                intervalEnd_1 = 3.4028234663852886e+38f;
                break;
            }
            if(intervalEnd_1 >= 88.029693603515625f)
            {
                intervalEnd_1 = min(exp(intervalEnd_1 - 0.69314718246459961f), 1.7014117331926443e+38f) * 2.0f;
                break;
            }
            intervalEnd_1 = exp(intervalEnd_1);
            break;
        }
        if(intervalStart_0 == -3.4028234663852886e+38f)
        {
            intervalStart_0 = -3.4028234663852886e+38f;
        }
        else
        {
            intervalStart_0 = intervalStart_0 - intervalEnd_0 + logExposure_0;
        }
        for(;;)
        {
            if(intervalStart_0 == -3.4028234663852886e+38f)
            {
                intervalStart_0 = 0.0f;
                break;
            }
            if(!isfinite(intervalStart_0))
            {
                intervalStart_0 = exp(intervalStart_0);
                break;
            }
            if(intervalStart_0 >= 88.72283935546875f)
            {
                intervalStart_0 = 3.4028234663852886e+38f;
                break;
            }
            if(intervalStart_0 >= 88.029693603515625f)
            {
                intervalStart_0 = min(exp(intervalStart_0 - 0.69314718246459961f), 1.7014117331926443e+38f) * 2.0f;
                break;
            }
            intervalStart_0 = exp(intervalStart_0);
            break;
        }
        if(opticalDepth_0 == -3.4028234663852886e+38f)
        {
            intervalEnd_0 = -3.4028234663852886e+38f;
        }
        else
        {
            intervalEnd_0 = opticalDepth_0 - intervalEnd_0 + logExposure_0;
        }
        for(;;)
        {
            if(intervalEnd_0 == -3.4028234663852886e+38f)
            {
                intervalEnd_0 = 0.0f;
                break;
            }
            if(!isfinite(intervalEnd_0))
            {
                intervalEnd_0 = exp(intervalEnd_0);
                break;
            }
            if(intervalEnd_0 >= 88.72283935546875f)
            {
                intervalEnd_0 = 3.4028234663852886e+38f;
                break;
            }
            if(intervalEnd_0 >= 88.029693603515625f)
            {
                intervalEnd_0 = min(exp(intervalEnd_0 - 0.69314718246459961f), 1.7014117331926443e+38f) * 2.0f;
                break;
            }
            intervalEnd_0 = exp(intervalEnd_0);
            break;
        }
        float3 outputColor_1 = float3(intervalEnd_1, intervalStart_0, intervalEnd_0);
        outputColor_0 = outputColor_1 / (float3(1.0f)  + max(outputColor_1, float3(0.0f, 0.0f, 0.0f))) * float3(intervalStart_1) ;
    }
    else
    {
        float logScale_0 = power_0 - intervalEnd_0 + logExposure_0;
        if(intervalEnd_1 == -3.4028234663852886e+38f)
        {
            intervalEnd_1 = -3.4028234663852886e+38f;
        }
        else
        {
            intervalEnd_1 = intervalEnd_1 + logScale_0;
        }
        for(;;)
        {
            if(intervalEnd_1 == -3.4028234663852886e+38f)
            {
                intervalEnd_1 = 0.0f;
                break;
            }
            if(!isfinite(intervalEnd_1))
            {
                intervalEnd_1 = exp(intervalEnd_1);
                break;
            }
            if(intervalEnd_1 >= 88.72283935546875f)
            {
                intervalEnd_1 = 3.4028234663852886e+38f;
                break;
            }
            if(intervalEnd_1 >= 88.029693603515625f)
            {
                intervalEnd_1 = min(exp(intervalEnd_1 - 0.69314718246459961f), 1.7014117331926443e+38f) * 2.0f;
                break;
            }
            intervalEnd_1 = exp(intervalEnd_1);
            break;
        }
        if(intervalStart_0 == -3.4028234663852886e+38f)
        {
            intervalStart_0 = -3.4028234663852886e+38f;
        }
        else
        {
            intervalStart_0 = intervalStart_0 + logScale_0;
        }
        for(;;)
        {
            if(intervalStart_0 == -3.4028234663852886e+38f)
            {
                intervalStart_0 = 0.0f;
                break;
            }
            if(!isfinite(intervalStart_0))
            {
                intervalStart_0 = exp(intervalStart_0);
                break;
            }
            if(intervalStart_0 >= 88.72283935546875f)
            {
                intervalStart_0 = 3.4028234663852886e+38f;
                break;
            }
            if(intervalStart_0 >= 88.029693603515625f)
            {
                intervalStart_0 = min(exp(intervalStart_0 - 0.69314718246459961f), 1.7014117331926443e+38f) * 2.0f;
                break;
            }
            intervalStart_0 = exp(intervalStart_0);
            break;
        }
        if(opticalDepth_0 == -3.4028234663852886e+38f)
        {
            intervalEnd_0 = -3.4028234663852886e+38f;
        }
        else
        {
            intervalEnd_0 = opticalDepth_0 + logScale_0;
        }
        for(;;)
        {
            if(intervalEnd_0 == -3.4028234663852886e+38f)
            {
                intervalEnd_0 = 0.0f;
                break;
            }
            if(!isfinite(intervalEnd_0))
            {
                intervalEnd_0 = exp(intervalEnd_0);
                break;
            }
            if(intervalEnd_0 >= 88.72283935546875f)
            {
                intervalEnd_0 = 3.4028234663852886e+38f;
                break;
            }
            if(intervalEnd_0 >= 88.029693603515625f)
            {
                intervalEnd_0 = min(exp(intervalEnd_0 - 0.69314718246459961f), 1.7014117331926443e+38f) * 2.0f;
                break;
            }
            intervalEnd_0 = exp(intervalEnd_0);
            break;
        }
        outputColor_0 = float3(intervalEnd_1, intervalStart_0, intervalEnd_0);
    }
    float4 _S200 = float4(exp(- accumulatedOpticalDepth_0 / 3.0f)) ;
    float4 _S201 = background_0 * _S200 * _S200 * _S200;
    pixelOutput_0 _S202 = { float4(outputColor_0 + _S201.xyz, intervalStart_1 + _S201.w) };
    return _S202;
}
