// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>Describes the physical row order of a tightly packed RGBA16Float plane.</summary>
/// <remarks>This is independent of the existing RGBA8 row-order contract.</remarks>
public enum Rgba16FloatRowOrder
{
    /// <summary>The first stored row is the top output row.</summary>
    TopDown = 0,

    /// <summary>The first stored row is the bottom output row.</summary>
    BottomUp = 1
}
