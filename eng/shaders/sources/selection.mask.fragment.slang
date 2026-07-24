// Copyright (c) marcschier. Licensed under the MIT License.

struct SelectionMaskVertexOutput
{
    float4 position : SV_Position;
};

[shader("fragment")]
float4 selectionMaskFragmentMain(SelectionMaskVertexOutput input) : SV_Target0
{
    return float4(1.0, 1.0, 1.0, 1.0);
}
