// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>A pointer-free ABI result for one time-sampled camera-state query.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeCameraState
{
    internal const uint CurrentVersion = 1;

    internal uint StructSize;
    internal uint Version;
    internal int IsValid;
    internal int Projection;
    internal double WindowLeft;
    internal double WindowRight;
    internal double WindowBottom;
    internal double WindowTop;
    internal double ClippingNear;
    internal double ClippingFar;
    internal double FocalLength;
    internal double HorizontalAperture;
    internal double VerticalAperture;
    internal double HorizontalApertureOffset;
    internal double VerticalApertureOffset;
    internal double FocusDistance;
    internal double FStop;
}
