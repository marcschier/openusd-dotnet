// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeTsKnotRecord
{
    internal double Time;
    internal double Value;
    internal double PreValue;
    internal double PreTangentWidth;
    internal double PreTangentSlope;
    internal double PostTangentWidth;
    internal double PostTangentSlope;
    internal int NextInterpolation;
    internal int PreTangentAlgorithm;
    internal int PostTangentAlgorithm;
    internal uint Flags;
}

internal readonly record struct OpenUsdNativeTsExtrapolation(int Mode, double Slope);

internal readonly record struct OpenUsdNativeTsSplineData(
    int CurveType,
    bool IsTimeValued,
    OpenUsdNativeTsExtrapolation PreExtrapolation,
    OpenUsdNativeTsExtrapolation PostExtrapolation,
    OpenUsdNativeTsKnotRecord[] Knots);
