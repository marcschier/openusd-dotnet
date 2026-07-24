// Copyright (c) marcschier. Licensed under the MIT License.

struct SelectionOutlineVertexOutput
{
    float4 position : SV_Position;
    float2 textureCoordinate : TEXCOORD0;
};

[[vk::binding(0, 0)]]
Texture2D<float4> selectionMask : register(t0);

[[vk::binding(1, 0)]]
Texture2D<float> visibleDepth : register(t1);

[[vk::binding(2, 0)]]
SamplerState selectionSampler : register(s0);

[[vk::binding(3, 0)]]
cbuffer SelectionOutlineParameters : register(b0)
{
    float4 outlineColor;
    float2 inverseViewportSize;
    float outlineWidthPixels;
    float depthEpsilon;
};

[shader("fragment")]
float4 selectionOutlineFragmentMain(
    SelectionOutlineVertexOutput input) : SV_Target0
{
    float centerMask = selectionMask.SampleLevel(
        selectionSampler,
        input.textureCoordinate,
        0.0).r;
    if (centerMask >= 0.5)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    float centerDepth = visibleDepth.SampleLevel(
        selectionSampler,
        input.textureCoordinate,
        0.0);
    int radius = int(ceil(outlineWidthPixels));
    float edge = 0.0;
    for (int y = -radius; y <= radius; ++y)
    {
        for (int x = -radius; x <= radius; ++x)
        {
            float2 offset = float2(float(x), float(y));
            if (dot(offset, offset) >
                outlineWidthPixels * outlineWidthPixels)
            {
                continue;
            }

            float2 sampleCoordinate =
                input.textureCoordinate + offset * inverseViewportSize;
            float neighborMask = selectionMask.SampleLevel(
                selectionSampler,
                sampleCoordinate,
                0.0).r;
            if (neighborMask < 0.5)
            {
                continue;
            }

            float neighborDepth = visibleDepth.SampleLevel(
                selectionSampler,
                sampleCoordinate,
                0.0);
            if (centerDepth + depthEpsilon >= neighborDepth)
            {
                edge = 1.0;
            }
        }
    }

    return float4(outlineColor.rgb, outlineColor.a * edge);
}
