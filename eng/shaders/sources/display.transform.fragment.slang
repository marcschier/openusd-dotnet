// Copyright (c) marcschier. Licensed under the MIT License.
//
// Fullscreen display transform. The scene is rendered into a linear
// scene-referred RGBA16Float intermediate; this pass applies exposure and one
// baked display/view/look lookup table, then writes display-referred RGBA8.
//
// The lookup table is a bounded 3D lattice unrolled into a 2D strip of
// lutSize tiles, each lutSize x lutSize texels, so the whole table is a single
// sampled 2D texture on every backend. Red varies across a tile, green down a
// tile, and blue selects the tile; the blue axis is interpolated explicitly
// with two bilinear samples because hardware filtering cannot cross tiles.
//
// The lattice is indexed through a log2 shaper, which is what makes a bounded
// table cover an unbounded scene-referred range: shaperMinLog2 and
// shaperRangeLog2 define the closed interval of stops the table spans, and
// values outside it clamp to the nearest lattice edge.

struct DisplayTransformVertexOutput
{
    float4 position : SV_Position;
    float2 textureCoordinate : TEXCOORD0;
};

[[vk::binding(0, 0)]]
Texture2D<float4> sceneColor : register(t0);

[[vk::binding(1, 0)]]
Texture2D<float4> displayLut : register(t1);

[[vk::binding(2, 0)]]
SamplerState displaySampler : register(s0);

[[vk::binding(3, 0)]]
cbuffer DisplayTransformParameters : register(b0)
{
    // x: exp2(exposure stops), y: shaperMinLog2, z: shaperRangeLog2, w: lutSize
    float4 exposureShaper;
    // x: 1 / (lutSize * lutSize), y: 1 / lutSize, z: lutSize - 1,
    // w: 1 when the backend's framebuffer origin is opposite this triangle's clip-space
    //    Y, which is what makes one fullscreen mapping correct on every RHI
    float4 lutGrid;
};

[shader("fragment")]
float4 displayTransformFragmentMain(
    DisplayTransformVertexOutput input) : SV_Target0
{
    // The fullscreen triangle is generated in clip space from the vertex id, so the row
    // a fragment lands on is decided by the backend's framebuffer origin convention,
    // while the texture coordinate it carries is not. On a backend whose framebuffer
    // origin is opposite the clip-space Y direction, sampling the unmodified coordinate
    // writes the source's top row into the target's bottom row. Flipping the sampled V
    // here -- rather than mirroring the triangle -- keeps one checked vertex program for
    // every RHI and keeps the resolved orientation a runtime constant.
    float2 coordinate = float2(
        input.textureCoordinate.x,
        lutGrid.w > 0.5 ? 1.0 - input.textureCoordinate.y : input.textureCoordinate.y);

    float4 source = sceneColor.SampleLevel(
        displaySampler,
        coordinate,
        0.0);

    float shaperMinLog2 = exposureShaper.y;
    float shaperRangeLog2 = exposureShaper.z;
    float lutMaxIndex = lutGrid.z;
    float inverseStripWidth = lutGrid.x;
    float inverseTileCount = lutGrid.y;

    float3 exposed = source.rgb * exposureShaper.x;
    float3 floored = max(exposed, exp2(float3(shaperMinLog2, shaperMinLog2, shaperMinLog2)));
    float3 shaped = saturate((log2(floored) - shaperMinLog2) / shaperRangeLog2);

    float bluePosition = shaped.b * lutMaxIndex;
    float lowerTile = floor(bluePosition);
    float upperTile = min(lowerTile + 1.0, lutMaxIndex);
    float blueFraction = bluePosition - lowerTile;

    float tileOffset = (shaped.r * lutMaxIndex + 0.5) * inverseStripWidth;
    float verticalCoordinate = (shaped.g * lutMaxIndex + 0.5) * inverseTileCount;

    float4 lower = displayLut.SampleLevel(
        displaySampler,
        float2(lowerTile * inverseTileCount + tileOffset, verticalCoordinate),
        0.0);
    float4 upper = displayLut.SampleLevel(
        displaySampler,
        float2(upperTile * inverseTileCount + tileOffset, verticalCoordinate),
        0.0);

    return float4(
        lerp(lower.rgb, upper.rgb, blueFraction),
        saturate(source.a));
}
