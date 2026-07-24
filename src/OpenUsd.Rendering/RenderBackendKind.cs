// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>
/// Identifies a renderer selectable by the viewer.
/// </summary>
public enum RenderBackendKind
{
    /// <summary>OpenUSD Hydra/Storm.</summary>
    Storm,

    /// <summary>The Silk.NET Direct3D 12 backend.</summary>
    D3D12,

    /// <summary>The Silk.NET Vulkan backend.</summary>
    Vulkan,

    /// <summary>The Silk.NET Metal backend.</summary>
    Metal
}
