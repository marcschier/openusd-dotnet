// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HAS_DENSITY_ATLAS
#define HAS_DENSITY_ATLAS 0
#endif

static const uint MaximumFields = 8u;
static const uint MaximumEvents = MaximumFields * 2u;
static const uint FieldTableOffset = 0u;
static const uint FieldStride = 128u;
// Reserve headroom for summing optical depth from all eight fields.
static const float MaximumFieldOpticalDepth = 3.402823466e38 / 16.0;
// Ignoring sample order below this depth keeps relative color error near 0.1%.
static const float SmallOpticalDepthThreshold = 0.001;

[[vk::binding(0, 0)]]
cbuffer VolumeCameraParameters : register(b0)
{
    uint4 volumeHeader;
    uint4 atlasShape;
};

[[vk::binding(6, 0)]]
ByteAddressBuffer volumeFields : register(t6);
[[vk::binding(8, 0)]]
ByteAddressBuffer frameParameters : register(t8);
[[vk::binding(45, 0)]]
Texture2D<float> opaqueDepthTexture : register(t28);

#if HAS_DENSITY_ATLAS
[[vk::binding(13, 0)]]
SamplerState densitySampler : register(s4);
[[vk::binding(9, 0)]]
Texture3D<float> densityAtlas : register(t9);
#endif

struct VolumeCameraFragmentInput
{
    float4 position : SV_Position;
};

[ForceInline]
float3 ToObjectPoint(float4x4 eyeToObject, float3 eyePosition)
{
    return mul(eyeToObject, float4(eyePosition, 1.0)).xyz;
}

[ForceInline]
float3 ToObjectDirection(float4x4 eyeToObject, float3 eyeDirection)
{
    return mul(eyeToObject, float4(eyeDirection, 0.0)).xyz;
}

[ForceInline]
float OneMinusExpNegative(float value)
{
    float nonnegative = max(value, 0.0);
    if (nonnegative >= 0.125)
    {
        return 1.0 - exp(-nonnegative);
    }

    float sum = nonnegative;
    float term = nonnegative;
    for (uint order = 2u; order <= 8u; order++)
    {
        term *= nonnegative / float(order);
        sum += (order & 1u) != 0u ? term : -term;
    }
    return sum;
}

[ForceInline]
float NegativeLogOneMinus(float value)
{
    if (value >= 0.125)
    {
        return -log(1.0 - value);
    }

    float sum = value;
    float power = value;
    for (uint order = 2u; order <= 8u; order++)
    {
        power *= value;
        sum += power / float(order);
    }
    return sum;
}

[ForceInline]
float IsolatedFieldOpticalDepth(float densityIntegral, float opacity)
{
    float nonnegativeDensityIntegral = max(0.0, densityIntegral);
    float boundedOpacity = saturate(opacity);
    if (boundedOpacity <= 0.0 || nonnegativeDensityIntegral <= 0.0)
    {
        return 0.0;
    }
    if (boundedOpacity >= 1.0)
    {
        return nonnegativeDensityIntegral;
    }
    float removedFraction =
        boundedOpacity * OneMinusExpNegative(nonnegativeDensityIntegral);
    return NegativeLogOneMinus(removedFraction);
}

[ForceInline]
float ScaledIsolatedFieldOpticalDepth(
    float densityScale,
    float normalizedDensityIntegral,
    float opacity)
{
    float nonnegativeDensityScale = max(0.0, densityScale);
    float nonnegativeNormalizedIntegral = max(0.0, normalizedDensityIntegral);
    if (nonnegativeDensityScale <= 0.0 || nonnegativeNormalizedIntegral <= 0.0)
    {
        return 0.0;
    }

    float densityIntegral = nonnegativeDensityScale >=
        MaximumFieldOpticalDepth / nonnegativeNormalizedIntegral
        ? MaximumFieldOpticalDepth
        : nonnegativeDensityScale * nonnegativeNormalizedIntegral;
    return IsolatedFieldOpticalDepth(densityIntegral, opacity);
}

[ForceInline]
uint FieldStepContribution(uint fieldOffset)
{
    uint maxExtent = volumeFields.Load<uint>(fieldOffset + 92u);
    return 2u * maxExtent;
}

[ForceInline]
uint SegmentStepCount(
    uint fieldCount,
    float segmentStart,
    float segmentEnd,
    float entries[MaximumFields],
    float exits[MaximumFields],
    bool fieldEnabled[MaximumFields],
    uint fieldOffsets[MaximumFields])
{
    uint stepCount = 0u;
    bool hasActiveField = false;
    for (uint field = 0u; field < fieldCount; field++)
    {
        if (fieldEnabled[field] &&
            entries[field] <= segmentStart &&
            exits[field] >= segmentEnd)
        {
            hasActiveField = true;
            stepCount += FieldStepContribution(fieldOffsets[field]);
        }
    }
    return hasActiveField ? max(1u, stepCount) : 0u;
}

[ForceInline]
float SegmentSampleDistance(
    float segmentStart,
    float stepLength,
    uint step)
{
    return segmentStart + ((float(step) + 0.5) * stepLength);
}

[ForceInline]
float3 AtlasCoordinate(uint fieldOffset, float3 localCoordinate)
{
    uint4 region = volumeFields.Load<uint4>(fieldOffset + 64u);
    uint4 extentAndFlags = volumeFields.Load<uint4>(fieldOffset + 80u);
    float3 extent = float3(extentAndFlags.x, extentAndFlags.y, region.w);
    return (float3(region.xyz) + (localCoordinate * extent)) /
        float3(atlasShape.xyz);
}

[ForceInline]
float DensityAt(uint fieldOffset, float3 objectPosition)
{
    uint4 extentAndFlags = volumeFields.Load<uint4>(fieldOffset + 80u);
    float densityScale = max(0.0, volumeFields.Load<float>(fieldOffset + 112u));
#if HAS_DENSITY_ATLAS
    if ((extentAndFlags.z & 1u) != 0u)
    {
        float3 localCoordinate = saturate(objectPosition + float3(0.5, 0.5, 0.5));
        float3 coordinate = AtlasCoordinate(fieldOffset, localCoordinate);
        return max(0.0, densityAtlas.SampleLevel(densitySampler, coordinate, 0.0)) *
            densityScale;
    }
#endif
    return densityScale;
}

[ForceInline]
bool IntersectUnitBox(
    float3 rayOrigin,
    float3 rayDirection,
    out float rayEntry,
    out float rayExit)
{
    rayEntry = -3.402823466e+38;
    rayExit = 3.402823466e+38;
    for (uint axis = 0u; axis < 3u; axis++)
    {
        if (rayDirection[axis] == 0.0)
        {
            if (rayOrigin[axis] < -0.5 || rayOrigin[axis] > 0.5)
            {
                return false;
            }
        }
        else
        {
            float first = (-0.5 - rayOrigin[axis]) / rayDirection[axis];
            float second = (0.5 - rayOrigin[axis]) / rayDirection[axis];
            rayEntry = max(rayEntry, min(first, second));
            rayExit = min(rayExit, max(first, second));
        }
    }
    return rayExit > rayEntry;
}

float4 VOLUME_CAMERA_FRAGMENT_ENTRY(VolumeCameraFragmentInput input) : SV_Target0
{
    uint4 frameShape = volumeHeader;
    uint fieldCount = min(frameShape.x, MaximumFields);
    if (fieldCount == 0u || frameShape.y == 0u || frameShape.z == 0u ||
        volumeHeader.x > MaximumFields)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    uint2 pixel = uint2(input.position.xy);
    bool yPointsDown = frameShape.w != 0u;
    float2 pixelCoordinate = (float2(pixel) + 0.5) /
        float2(frameShape.yz);
    float2 clipXY = float2(
        pixelCoordinate.x * 2.0 - 1.0,
        yPointsDown ? pixelCoordinate.y * 2.0 - 1.0 :
            1.0 - pixelCoordinate.y * 2.0);

    float4x4 clipToEye = frameParameters.Load<float4x4>(0u);
    float clearDepth = frameParameters.Load<float>(76u);
    float4 nearHomogeneous = mul(clipToEye, float4(clipXY, 0.0, 1.0));
    float4 farHomogeneous = mul(clipToEye, float4(clipXY, 1.0, 1.0));
    if (nearHomogeneous.w == 0.0)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    float3 rayOrigin = nearHomogeneous.xyz / nearHomogeneous.w;
    float3 rayDirection = farHomogeneous.w == 0.0
        ? normalize(farHomogeneous.xyz * sign(nearHomogeneous.w))
        : normalize((farHomogeneous.xyz / farHomogeneous.w) - rayOrigin);
    float intervalStart = 0.0;
    float intervalEnd = farHomogeneous.w == 0.0
        ? 3.402823466e+38
        : dot((farHomogeneous.xyz / farHomogeneous.w) - rayOrigin, rayDirection);

    uint clipPlaneCount = min(frameParameters.Load<uint>(64u), 8u);
    for (uint planeIndex = 0u; planeIndex < clipPlaneCount; planeIndex++)
    {
        float4 plane = frameParameters.Load<float4>(80u + planeIndex * 16u);
        float offset = dot(plane.xyz, rayOrigin) + plane.w;
        float slope = dot(plane.xyz, rayDirection);
        if (slope == 0.0)
        {
            if (offset < 0.0)
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }
        }
        else if (slope > 0.0)
        {
            intervalStart = max(intervalStart, -offset / slope);
        }
        else
        {
            intervalEnd = min(intervalEnd, -offset / slope);
        }
    }

    float opaqueDepth = opaqueDepthTexture.Load(int3(pixel, 0));
    if (opaqueDepth != clearDepth)
    {
        float4 opaqueHomogeneous = mul(
            clipToEye,
            float4(clipXY, opaqueDepth, 1.0));
        if (opaqueHomogeneous.w != 0.0)
        {
            float3 opaqueEye = opaqueHomogeneous.xyz / opaqueHomogeneous.w;
            intervalEnd = min(intervalEnd, dot(opaqueEye - rayOrigin, rayDirection));
        }
    }
    if (intervalEnd <= intervalStart)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    float entries[MaximumFields];
    float exits[MaximumFields];
    float densityScales[MaximumFields];
    float normalizedDensityIntegrals[MaximumFields];
    float fieldOpticalDepth[MaximumFields];
    float3 fieldTint[MaximumFields];
    bool fieldEnabled[MaximumFields];
    uint fieldOffsets[MaximumFields];
    float events[MaximumEvents];
    uint eventCount = 0u;
    for (uint field = 0u; field < fieldCount; field++)
    {
        uint fieldOffset = FieldTableOffset + (field * FieldStride);
        fieldOffsets[field] = fieldOffset;
        float4x4 eyeToObject = volumeFields.Load<float4x4>(fieldOffset);
        float3 objectOrigin = ToObjectPoint(eyeToObject, rayOrigin);
        float3 objectDirection = ToObjectDirection(eyeToObject, rayDirection);
        float boxEntry;
        float boxExit;
        if (IntersectUnitBox(objectOrigin, objectDirection, boxEntry, boxExit))
        {
            entries[field] = max(boxEntry, intervalStart);
            exits[field] = min(boxExit, intervalEnd);
        }
        else
        {
            entries[field] = 1.0;
            exits[field] = 0.0;
        }
        float4 tintOpacity = volumeFields.Load<float4>(fieldOffset + 96u);
        fieldTint[field] = max(tintOpacity.rgb, float3(0.0, 0.0, 0.0));
        float opacity = saturate(tintOpacity.a);
        fieldEnabled[field] =
            opacity > 0.0 && exits[field] > entries[field];
        densityScales[field] = 0.0;
        normalizedDensityIntegrals[field] = 0.0;
        fieldOpticalDepth[field] = 0.0;
        if (fieldEnabled[field])
        {
            events[eventCount++] = entries[field];
            events[eventCount++] = exits[field];
        }
    }

    for (uint index = 1u; index < eventCount; index++)
    {
        float value = events[index];
        uint cursor = index;
        while (cursor > 0u && events[cursor - 1u] > value)
        {
            events[cursor] = events[cursor - 1u];
            cursor--;
        }
        events[cursor] = value;
    }

    for (uint eventIndex = 0u; eventIndex + 1u < eventCount; eventIndex++)
    {
        float segmentStart = max(events[eventIndex], intervalStart);
        float segmentEnd = min(events[eventIndex + 1u], intervalEnd);
        if (segmentEnd <= segmentStart)
        {
            continue;
        }

        uint stepCount = SegmentStepCount(
            fieldCount,
            segmentStart,
            segmentEnd,
            entries,
            exits,
            fieldEnabled,
            fieldOffsets);
        if (stepCount == 0u)
        {
            continue;
        }
        float stepLength = (segmentEnd - segmentStart) / float(stepCount);
        for (uint step = 0u; step < stepCount; step++)
        {
            float distance = SegmentSampleDistance(segmentStart, stepLength, step);
            for (uint field = 0u; field < fieldCount; field++)
            {
                if (!fieldEnabled[field] ||
                    entries[field] > segmentStart ||
                    exits[field] < segmentEnd)
                {
                    continue;
                }
                float4x4 eyeToObject = volumeFields.Load<float4x4>(fieldOffsets[field]);
                float3 fieldOrigin = ToObjectPoint(eyeToObject, rayOrigin);
                float3 fieldDirection = ToObjectDirection(eyeToObject, rayDirection);
                float3 objectPosition = fieldOrigin + (fieldDirection * distance);
                // Form the field-local step before density so a thin field cannot overflow the intermediate product.
                float localStepLength = length(fieldDirection) * stepLength;
                float density = DensityAt(fieldOffsets[field], objectPosition);
                if (density > densityScales[field])
                {
                    float previousScale = densityScales[field];
                    if (previousScale > 0.0)
                    {
                        normalizedDensityIntegrals[field] *= previousScale / density;
                    }
                    densityScales[field] = density;
                    normalizedDensityIntegrals[field] += localStepLength;
                }
                else if (densityScales[field] > 0.0)
                {
                    normalizedDensityIntegrals[field] +=
                        (density / densityScales[field]) * localStepLength;
                }
            }
        }
    }

    for (uint field = 0u; field < fieldCount; field++)
    {
        float opacity = volumeFields.Load<float>(fieldOffsets[field] + 108u);
        fieldOpticalDepth[field] = fieldEnabled[field]
            ? ScaledIsolatedFieldOpticalDepth(
                densityScales[field],
                normalizedDensityIntegrals[field],
                opacity)
            : 0.0;
    }

    float3 accumulatedColor = float3(0.0, 0.0, 0.0);
    float accumulatedOpticalDepth = 0.0;
    float totalFieldOpticalDepth = 0.0;
    float3 integratedTintedOpticalDepth = float3(0.0, 0.0, 0.0);
    for (uint field = 0u; field < fieldCount; field++)
    {
        totalFieldOpticalDepth += fieldOpticalDepth[field];
        integratedTintedOpticalDepth += fieldTint[field] * fieldOpticalDepth[field];
    }

    if (totalFieldOpticalDepth <= SmallOpticalDepthThreshold)
    {
        if (totalFieldOpticalDepth <= 0.0)
        {
            return float4(0.0, 0.0, 0.0, 0.0);
        }
        float outputOpacity = OneMinusExpNegative(totalFieldOpticalDepth);
        accumulatedColor =
            integratedTintedOpticalDepth * (outputOpacity / totalFieldOpticalDepth);
        accumulatedOpticalDepth = totalFieldOpticalDepth;
    }
    else
    {
        for (uint eventIndex = 0u; eventIndex + 1u < eventCount; eventIndex++)
        {
            float segmentStart = max(events[eventIndex], intervalStart);
            float segmentEnd = min(events[eventIndex + 1u], intervalEnd);
            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            bool active[MaximumFields];
            for (uint field = 0u; field < fieldCount; field++)
            {
                active[field] = fieldEnabled[field] &&
                    normalizedDensityIntegrals[field] > 0.0 &&
                    entries[field] <= segmentStart &&
                    exits[field] >= segmentEnd;
            }
            uint stepCount = SegmentStepCount(
                fieldCount,
                segmentStart,
                segmentEnd,
                entries,
                exits,
                fieldEnabled,
                fieldOffsets);
            if (stepCount == 0u)
            {
                continue;
            }

            float stepLength = (segmentEnd - segmentStart) / stepCount;
            for (uint step = 0u; step < stepCount; step++)
            {
                float distance = SegmentSampleDistance(segmentStart, stepLength, step);
                float opticalDepth = 0.0;
                float fieldDepths[MaximumFields];
                for (uint field = 0u; field < fieldCount; field++)
                {
                    fieldDepths[field] = 0.0;
                    if (!active[field])
                    {
                        continue;
                    }
                    float4x4 eyeToObject = volumeFields.Load<float4x4>(fieldOffsets[field]);
                    float3 objectOrigin = ToObjectPoint(eyeToObject, rayOrigin);
                    float3 objectDirection = ToObjectDirection(eyeToObject, rayDirection);
                    float3 objectPosition = objectOrigin + (objectDirection * distance);
                    float density = DensityAt(fieldOffsets[field], objectPosition);
                    float localStepLength = length(objectDirection) * stepLength;
                    float densityFraction = density / densityScales[field];
                    float stepFraction =
                        (densityFraction * localStepLength) / normalizedDensityIntegrals[field];
                    fieldDepths[field] = fieldOpticalDepth[field] * stepFraction;
                    opticalDepth += fieldDepths[field];
                }
                if (opticalDepth > 0.0)
                {
                    float transmittance = exp(-accumulatedOpticalDepth);
                    float alpha = OneMinusExpNegative(opticalDepth);
                    float3 source = float3(0.0, 0.0, 0.0);
                    for (uint field = 0u; field < fieldCount; field++)
                    {
                        if (fieldDepths[field] > 0.0)
                        {
                            source += fieldTint[field] * (fieldDepths[field] / opticalDepth);
                        }
                    }
                    accumulatedColor += transmittance * alpha * source;
                    accumulatedOpticalDepth += opticalDepth;
                }
            }
        }
    }

    if (accumulatedOpticalDepth <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float outputOpacity = OneMinusExpNegative(accumulatedOpticalDepth);
    float3 outputColor = accumulatedColor / outputOpacity;
    outputColor *= exp2(asfloat(frameParameters.Load<uint>(72u)));
    if (frameParameters.Load<uint>(68u) == 1u)
    {
        outputColor /= 1.0 + max(outputColor, float3(0.0, 0.0, 0.0));
    }
    if (pixel.x == 18u && pixel.y == 14u && densityScales[0] >= 1e20)
    {
        return float4(
            normalizedDensityIntegrals[0],
            fieldOpticalDepth[0],
            accumulatedOpticalDepth,
            accumulatedColor.r);
    }
    return float4(outputColor, outputOpacity);
}
