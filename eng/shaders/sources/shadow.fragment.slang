// Copyright (c) marcschier. Licensed under the MIT License.

struct ShadowVertexOutput
{
    float4 position : SV_Position;
};

// Writes no colour at all. A shadow pass binds no colour attachment, so a
// fragment stage returning SV_Target0 would describe an output the pass cannot
// store; the depth value is produced by the fixed-function stage alone.
[shader("fragment")]
void shadowFragmentMain(ShadowVertexOutput input)
{
}
