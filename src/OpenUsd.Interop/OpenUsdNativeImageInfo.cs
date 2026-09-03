// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>The effective colour space an image library resolved for a file.</summary>
internal enum OpenUsdNativeImageColorSpace : uint
{
    /// <summary>Linear, and never sRGB-decoded.</summary>
    Raw = 0,

    /// <summary>sRGB-encoded, and linearized on decode.</summary>
    Srgb = 1
}

/// <summary>One axis of sampler wrap metadata carried by an image file.</summary>
/// <remarks>Mirrors <c>HioAddressMode</c>.</remarks>
internal enum OpenUsdNativeImageAddressMode : uint
{
    /// <summary>Clamp to the edge texel.</summary>
    ClampToEdge = 0,

    /// <summary>Mirror once and then clamp.</summary>
    MirrorClampToEdge = 1,

    /// <summary>Repeat.</summary>
    Repeat = 2,

    /// <summary>Mirror and repeat.</summary>
    MirrorRepeat = 3,

    /// <summary>Clamp to a border colour.</summary>
    ClampToBorder = 4
}

/// <summary>Which image-info observation fields the image library answered for.</summary>
[Flags]
internal enum OpenUsdNativeImageObservation : uint
{
    /// <summary>Nothing beyond the shape was observed.</summary>
    None = 0,

    /// <summary>The source channel count was observed.</summary>
    ChannelCount = 1,

    /// <summary>The image library's own effective colour space was observed.</summary>
    ColorSpace = 2,

    /// <summary>The image carries horizontal sampler wrap metadata.</summary>
    AddressU = 4,

    /// <summary>The image carries vertical sampler wrap metadata.</summary>
    AddressV = 8
}

/// <summary>A pointer-free ABI result describing one decoded image.</summary>
/// <remarks>
/// <para>
/// Mirrors <c>openusd_image_info</c> in <c>native/openusd_dotnet/include/openusd_dotnet.h</c>,
/// and is the single managed definition of that seam: every managed caller binds
/// this struct rather than declaring its own, so a layout change cannot be made
/// on one path and missed on another.
/// </para>
/// <para>
/// The decode entry points are called twice: once with a null pixel buffer to
/// learn the required size and the observations, then again to fill it.
/// </para>
/// <para>
/// Version 2 appends observation fields. The seam stays backward compatible by
/// its own <see cref="StructSize"/> and <see cref="Version"/> pair rather than by
/// the whole-library ABI version: a caller that declares
/// <see cref="Version1Size"/> bytes and <see cref="Version1"/> is answered with
/// the shape alone and none of the appended fields is written, which is exactly
/// what a consumer compiled against the version-1 header expects.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeImageInfo
{
    /// <summary>The version this managed definition declares.</summary>
    internal const uint CurrentVersion = 2;

    /// <summary>The superseded version, still accepted by the native seam.</summary>
    internal const uint Version1 = 1;

    /// <summary>The byte size of the version-1 prefix.</summary>
    internal const uint Version1Size = 16;

    internal uint StructSize;
    internal uint Version;
    internal uint Width;
    internal uint Height;

    /// <summary>The source channel count, one to four, or zero when unobserved.</summary>
    internal uint ChannelCount;

    /// <summary>Which observation fields the image library answered for.</summary>
    internal OpenUsdNativeImageObservation Observed;

    /// <summary>The image library's effective colour space.</summary>
    internal OpenUsdNativeImageColorSpace ColorSpace;

    /// <summary>The horizontal sampler wrap metadata the file carries.</summary>
    internal OpenUsdNativeImageAddressMode AddressU;

    /// <summary>The vertical sampler wrap metadata the file carries.</summary>
    internal OpenUsdNativeImageAddressMode AddressV;

    /// <summary>Reserved; always zero.</summary>
    internal uint Reserved;

    /// <summary>Creates an input describing the current version of this seam.</summary>
    internal static unsafe OpenUsdNativeImageInfo Create() =>
        new()
        {
            StructSize = (uint)sizeof(OpenUsdNativeImageInfo),
            Version = CurrentVersion
        };
}
