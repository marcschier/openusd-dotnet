// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>
/// A blittable, ABI-matching three-component float vector used to marshal
/// <c>openusd_vec3f</c> values across the native boundary.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeVec3f
{
    /// <summary>Initializes a new vector.</summary>
    public OpenUsdNativeVec3f(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets or sets the first component.</summary>
    public float X;

    /// <summary>Gets or sets the second component.</summary>
    public float Y;

    /// <summary>Gets or sets the third component.</summary>
    public float Z;
}
