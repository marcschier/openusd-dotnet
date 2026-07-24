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
    MeshRemove = 3
}
