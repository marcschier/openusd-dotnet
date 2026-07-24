// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Skel;

/// <summary>Specifies how joint-influence tuples are interpolated.</summary>
public enum UsdSkelInterpolation
{
    /// <summary>One influence tuple applies to the whole primitive.</summary>
    Constant = 0,
    /// <summary>One influence tuple applies to each point.</summary>
    Vertex = 1
}

/// <summary>Contains joint indices, matching weights, and their primvar shape metadata.</summary>
public sealed record UsdSkelJointInfluences(
    int[] JointIndices,
    float[] JointWeights,
    int ElementSize,
    UsdSkelInterpolation Interpolation) : IUsdDetachedResult;
