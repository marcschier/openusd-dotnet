// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>A blittable ABI representation of a single-precision quaternion.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct OpenUsdNativeQuatf
{
    /// <summary>Initializes a quaternion from scalar and imaginary components.</summary>
    public OpenUsdNativeQuatf(float real, float x, float y, float z)
    {
        Real = real;
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets the scalar component.</summary>
    public float Real { get; }

    /// <summary>Gets the imaginary X component.</summary>
    public float X { get; }

    /// <summary>Gets the imaginary Y component.</summary>
    public float Y { get; }

    /// <summary>Gets the imaginary Z component.</summary>
    public float Z { get; }
}
