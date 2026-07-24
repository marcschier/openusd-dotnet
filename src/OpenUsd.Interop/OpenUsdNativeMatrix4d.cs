// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>A blittable, row-major ABI representation of a 4x4 double matrix.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeMatrix4d
{
    /// <summary>Gets or sets row 0, column 0.</summary>
    public double M00;
    /// <summary>Gets or sets row 0, column 1.</summary>
    public double M01;
    /// <summary>Gets or sets row 0, column 2.</summary>
    public double M02;
    /// <summary>Gets or sets row 0, column 3.</summary>
    public double M03;
    /// <summary>Gets or sets row 1, column 0.</summary>
    public double M10;
    /// <summary>Gets or sets row 1, column 1.</summary>
    public double M11;
    /// <summary>Gets or sets row 1, column 2.</summary>
    public double M12;
    /// <summary>Gets or sets row 1, column 3.</summary>
    public double M13;
    /// <summary>Gets or sets row 2, column 0.</summary>
    public double M20;
    /// <summary>Gets or sets row 2, column 1.</summary>
    public double M21;
    /// <summary>Gets or sets row 2, column 2.</summary>
    public double M22;
    /// <summary>Gets or sets row 2, column 3.</summary>
    public double M23;
    /// <summary>Gets or sets row 3, column 0.</summary>
    public double M30;
    /// <summary>Gets or sets row 3, column 1.</summary>
    public double M31;
    /// <summary>Gets or sets row 3, column 2.</summary>
    public double M32;
    /// <summary>Gets or sets row 3, column 3.</summary>
    public double M33;
}
