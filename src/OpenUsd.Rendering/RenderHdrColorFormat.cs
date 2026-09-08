// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>Chooses the container for an actual pre-display RGBA16Float job plane.</summary>
public enum RenderHdrColorFormat
{
    /// <summary>Packed top-down little-endian RGBA binary16, without an image container.</summary>
    RawRgba16Float,
    /// <summary>Lossless half EXR with origin-zero equal windows and unspecified primaries/alpha association.</summary>
    Exr
}
