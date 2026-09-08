// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>Specifies the row order of tightly packed RGBA8 pixels.</summary>
public enum Rgba8RowOrder
{
    /// <summary>The first row is the top of the image.</summary>
    TopDown = 0,
    /// <summary>The first row is the bottom of the image.</summary>
    BottomUp = 1
}
