// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Identifies a command in an hdSilk command page.
/// </summary>
public enum SilkCommandType : uint
{
    /// <summary>Frame camera and viewport state.</summary>
    Frame = 1,

    /// <summary>Create or update a mesh resource.</summary>
    MeshUpsert = 2,

    /// <summary>Remove a mesh resource.</summary>
    MeshRemove = 3,

    /// <summary>Create or update a material resource.</summary>
    MaterialUpsert = 4,

    /// <summary>Remove a material resource.</summary>
    MaterialRemove = 5,

    /// <summary>Create or update a textured dome-light environment resource.</summary>
    EnvironmentUpsert = 6,

    /// <summary>Remove a textured dome-light environment resource.</summary>
    EnvironmentRemove = 7,

    /// <summary>Replace the sparse UsdLux light and shadow link table.</summary>
    LightLink = 8,

    /// <summary>Replace the bounded raster shadow-map descriptor table.</summary>
    Shadow = 9
}
