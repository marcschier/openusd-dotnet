// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace OpenUsd.Interop;

/// <summary>
/// Owns a native OpenColorIO CPU processor handle.
/// </summary>
internal sealed class OpenUsdNativeOcioProcessor : SafeHandleZeroOrMinusOneIsInvalid
{
    internal OpenUsdNativeOcioProcessor(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        OpenUsdNativeRuntime.ReleaseOcioProcessor(handle);
        return true;
    }
}
