// Copyright (c) marcschier. Licensed under the MIT License.

struct SelectionMaskVertexOutput
{
    float4 position : SV_Position;
};

/// Rasterizes the whole selected silhouette into the mask's green channel.
///
/// The x-ray composite needs both silhouettes at once -- the unoccluded one and
/// the whole one -- because it has to choose the visible colour at a visible
/// edge pixel and the occluded colour only for the hidden remainder. Compositing
/// them one after the other cannot do that: the second composite blends over the
/// first, so a visible edge drawn with a partly transparent colour came out as a
/// mixture of the two styles rather than as exactly what the visible-only mode
/// draws.
///
/// The two silhouettes therefore share one mask texture and differ by channel.
/// This pass runs first and writes green only; the depth-tested visible pass runs
/// second and writes every channel, which is correct precisely because the
/// visible silhouette is a subset of the whole one, so green survives wherever
/// this pass set it.
[shader("fragment")]
float4 selectionMaskOccludedFragmentMain(
    SelectionMaskVertexOutput input) : SV_Target0
{
    return float4(0.0, 1.0, 0.0, 1.0);
}
