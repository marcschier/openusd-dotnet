// Copyright (c) marcschier. Licensed under the MIT License.

#include "shadow.depth.shared.slang"

struct ShadowVertexOutput
{
    float4 position : SV_Position;
    float lightDepth : TEXCOORD2;
    nointerpolation float pointDepthScale : TEXCOORD3;
};

[shader("fragment")]
float shadowFragmentMain(ShadowVertexOutput input) : SV_Depth
{
    float depth = EncodeShadowDepth(input.position.z, input.lightDepth, input.pointDepthScale);
    if (PointShadowDepthOutsideRange(depth, input.pointDepthScale))
    {
        discard;
    }
    return depth;
}
