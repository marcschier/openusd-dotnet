// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace OpenUsd.Rendering.Storm;

internal sealed class StormAovOwnerHandle<TCall>() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    where TCall : struct, OpenUsdStormRuntime.IStormAovCall
{
    private bool _initialized;

    internal void Initialize(nint value)
    {
        if (_initialized)
        {
            throw new InvalidOperationException("A native AOV owner can only be initialized once.");
        }
        SetHandle(value);
        _initialized = true;
    }

    protected override bool ReleaseHandle()
    {
        TCall.Release(handle);
        return true;
    }
}
