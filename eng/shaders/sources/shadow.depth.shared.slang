// Copyright (c) marcschier. Licensed under the MIT License.

[ForceInline]
float EncodeShadowDepth(float rasterDepth, float lightDepth, float pointDepthScale)
{
    return pointDepthScale > 0.0 ? lightDepth * pointDepthScale : rasterDepth;
}

[ForceInline]
bool PointShadowDepthOutsideRange(float depth, float pointDepthScale)
{
    return pointDepthScale > 0.0 && depth > 1.0;
}
