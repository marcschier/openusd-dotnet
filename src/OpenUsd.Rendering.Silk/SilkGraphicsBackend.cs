// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Identifies a managed GPU backend.
/// </summary>
public enum SilkGraphicsBackend
{
    /// <summary>Direct3D 12 on Windows.</summary>
    D3D12,

    /// <summary>Vulkan on Windows or Linux.</summary>
    Vulkan,

    /// <summary>Metal on macOS.</summary>
    Metal
}
