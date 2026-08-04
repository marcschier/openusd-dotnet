// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativePrimClassification
{
    internal const uint CurrentVersion = 1;

    internal uint StructSize;
    internal uint Version;
    internal int IsDefined;
    internal int IsAbstract;
    internal int IsInPrototype;
    internal int Specifier;
}
