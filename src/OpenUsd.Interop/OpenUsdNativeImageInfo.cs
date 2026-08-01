// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>A pointer-free ABI result describing one decoded image.</summary>
/// <remarks>
/// Mirrors <c>openusd_image_info</c> in <c>native/openusd_dotnet/include/openusd_dotnet.h</c>.
/// The decode entry point is called twice: once with a null pixel buffer to
/// learn the required size, then again to fill it.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeImageInfo
{
    internal const uint CurrentVersion = 1;

    internal uint StructSize;
    internal uint Version;
    internal uint Width;
    internal uint Height;
}
