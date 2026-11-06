// Copyright (c) marcschier. Licensed under the MIT License.

#include "shadow.depth.shared.slang"

struct ShadowVertexOutput
{
    float4 position : SV_Position;
    float lightDepth : TEXCOORD2;
    nointerpolation float2 pointDepth : TEXCOORD3;
};

[shader("fragment")]
float shadowFragmentMain(ShadowVertexOutput input) : SV_Depth
{
    float depth = EncodeShadowDepth(input.position.z, input.lightDepth, input.pointDepth);
    if (PointShadowDepthOutsideRange(depth, input.pointDepth))
    {
        discard;
    }
    return depth;
}
