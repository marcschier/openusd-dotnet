// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>Explicitly describes stored EXR alpha without changing any sample.</summary>
public enum ExrStoredAlphaPolicy
{
    /// <summary>No association claim is made; stored framebuffer samples are preserved.</summary>
    Unspecified = 0,

    /// <summary>The caller declares stored samples associated with alpha; no conversion occurs.</summary>
    Associated = 1,

    /// <summary>The caller declares stored samples unassociated with alpha; no conversion occurs.</summary>
    Unassociated = 2
}
