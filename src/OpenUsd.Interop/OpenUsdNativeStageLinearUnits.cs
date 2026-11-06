// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>A fixed 24-byte ABI result for composed UsdGeom stage linear-unit metadata.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeStageLinearUnits
{
    internal const uint UpAxisY = 1;
    internal const uint UpAxisZ = 2;

    internal double MetersPerUnit;
    internal uint MetersPerUnitAuthored;
    internal uint UpAxis;
    internal uint UpAxisAuthored;
    internal uint Reserved;
}
