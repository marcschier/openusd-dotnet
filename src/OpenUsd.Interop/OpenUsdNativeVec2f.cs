// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>A blittable, ABI-matching two-component float vector.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeVec2f
{
    /// <summary>Initializes a new vector.</summary>
    public OpenUsdNativeVec2f(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Gets or sets the first component.</summary>
    public float X;

    /// <summary>Gets or sets the second component.</summary>
    public float Y;
}
