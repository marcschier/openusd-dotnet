// Copyright (c) marcschier. Licensed under the MIT License.

[ForceInline]
float EncodeShadowDepth(float rasterDepth, float lightDepth, float2 pointDepth)
{
    return pointDepth.x > 0.0 ? lightDepth * pointDepth.x : rasterDepth;
}

[ForceInline]
bool PointShadowDepthOutsideRange(float depth, float2 pointDepth)
{
    return pointDepth.x > 0.0 && depth > pointDepth.y;
}
