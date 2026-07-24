// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>A blittable, ABI-matching three-dimensional float extent.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeExtent3f
{
    /// <summary>Gets or sets the minimum corner.</summary>
    public OpenUsdNativeVec3f Minimum;

    /// <summary>Gets or sets the maximum corner.</summary>
    public OpenUsdNativeVec3f Maximum;
}
