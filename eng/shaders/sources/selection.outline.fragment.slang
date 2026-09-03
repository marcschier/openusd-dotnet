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
    float4 occludedOutlineColor;
};

/// Composites the selection outline from both silhouettes in one pass.
///
/// The mask's red channel carries the unoccluded selected fragments and its
/// green channel carries the whole selected silhouette, including the part
/// behind an occluder. A pixel that the visible-only mode would outline is
/// written with exactly the visible colour, and the occluded colour reaches only
/// the remainder that the visible-only mode leaves untouched.
///
/// This has to be one pass. Compositing the whole silhouette and then the
/// visible one over it blends the two styles wherever both cover a pixel, so an
/// outline colour with any transparency -- which the default is -- produced a
/// visible edge that did not match the visible-only image it is required to
/// reproduce exactly.
///
/// The visible-only mode uses this same shader with an occluded colour whose
/// alpha is zero, so its occluded branch composites nothing at all under
/// straight-alpha-over blending and the result is identical to a shader that
/// never had the branch.
[shader("fragment")]
float4 selectionOutlineFragmentMain(
    SelectionOutlineVertexOutput input) : SV_Target0
{
    float4 centerMask = selectionMask.SampleLevel(
        selectionSampler,
        input.textureCoordinate,
        0.0);
    float centerDepth = visibleDepth.SampleLevel(
        selectionSampler,
        input.textureCoordinate,
        0.0);
    int radius = int(ceil(outlineWidthPixels));
    float visibleEdge = 0.0;
    float occludedEdge = 0.0;
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
            float4 neighborMask = selectionMask.SampleLevel(
                selectionSampler,
                sampleCoordinate,
                0.0);

            // The whole silhouette is outlined without any depth comparison:
            // being behind an occluder is exactly what the occluded style
            // reports, so suppressing it there would erase the very pixels it
            // exists to draw.
            if (neighborMask.g >= 0.5)
            {
                occludedEdge = 1.0;
            }
            if (neighborMask.r < 0.5)
            {
                continue;
            }

            float neighborDepth = visibleDepth.SampleLevel(
                selectionSampler,
                sampleCoordinate,
                0.0);
            if (centerDepth + depthEpsilon >= neighborDepth)
            {
                visibleEdge = 1.0;
            }
        }
    }

    // An outline is drawn around a silhouette, never inside it, and each
    // silhouette suppresses its own interior.
    if (centerMask.r >= 0.5)
    {
        visibleEdge = 0.0;
    }
    if (centerMask.g >= 0.5)
    {
        occludedEdge = 0.0;
    }

    if (visibleEdge >= 0.5)
    {
        return float4(outlineColor.rgb, outlineColor.a);
    }
    if (occludedEdge >= 0.5)
    {
        return float4(occludedOutlineColor.rgb, occludedOutlineColor.a);
    }
    return float4(0.0, 0.0, 0.0, 0.0);
}
