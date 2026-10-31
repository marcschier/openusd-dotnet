// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>A blittable, ABI-matching four-component float vector.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeVec4f
{
    /// <summary>Initializes a new vector.</summary>
    public OpenUsdNativeVec4f(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>Gets or sets the first component.</summary>
    public float X;

    /// <summary>Gets or sets the second component.</summary>
    public float Y;

    /// <summary>Gets or sets the third component.</summary>
    public float Z;

    /// <summary>Gets or sets the fourth component.</summary>
    public float W;
}
