// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace OpenUsd.Rendering.Silk.Metal;

[SupportedOSPlatform("macos")]
internal static class MetalObjectOwnership
{
    // SharpMetal 1.1 wraps +0 factory results without retaining them, but Dispose releases.
    // Acquire our own +1 before a wrapper is stored or balanced by Dispose.
    private static nint Retain(nint handle) =>
        handle == 0 ? 0 : ObjectiveC.IntPtr_objc_msgSend(handle, new Selector("retain"));

    internal static string? GetString(NSString value)
    {
        if (value.NativePtr == 0)
        {
            return null;
        }
        // SharpMetal's UTF-16 conversion does not reserve a terminator in its buffer.
        nint utf8 = ObjectiveC.IntPtr_objc_msgSend(value.NativePtr, new Selector("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8);
    }

    internal static MTLTextureDescriptor Retain(MTLTextureDescriptor value) => new(Retain(value.NativePtr));
    internal static MTLCommandBuffer Retain(MTLCommandBuffer value) => new(Retain(value.NativePtr));
    internal static MTLBlitCommandEncoder Retain(MTLBlitCommandEncoder value) => new(Retain(value.NativePtr));
    internal static MTLRenderCommandEncoder Retain(MTLRenderCommandEncoder value) => new(Retain(value.NativePtr));
    internal static MTLComputeCommandEncoder Retain(MTLComputeCommandEncoder value) => new(Retain(value.NativePtr));
    internal static NSURL Retain(NSURL value) => new(Retain(value.NativePtr));
    internal static NSString Retain(NSString value) => new(Retain(value.NativePtr));
}
