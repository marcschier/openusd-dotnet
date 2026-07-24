// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>A pointer-free ABI result for one world-space axis-aligned bounds query.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeBounds3d
{
    internal const uint CurrentVersion = 1;

    internal uint StructSize;
    internal uint Version;
    internal int IsValid;
    internal int IsEmpty;
    internal double MinimumX;
    internal double MinimumY;
    internal double MinimumZ;
    internal double MaximumX;
    internal double MaximumY;
    internal double MaximumZ;
}
