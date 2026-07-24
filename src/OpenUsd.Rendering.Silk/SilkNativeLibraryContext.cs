// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Loads one explicit native library without Silk.NET's reflection-based path resolver.
/// </summary>
public sealed class SilkNativeLibraryContext : INativeContext
{
    private nint _handle;

    private SilkNativeLibraryContext(nint handle)
    {
        _handle = handle;
    }

    /// <summary>Loads the first available native library from the supplied names.</summary>
    public static SilkNativeLibraryContext Load(params string[] libraryNames)
    {
        foreach (string libraryName in libraryNames)
        {
            if (NativeLibrary.TryLoad(libraryName, out nint handle))
            {
                return new SilkNativeLibraryContext(handle);
            }
        }

        throw new DllNotFoundException(
            $"Could not load any of these native libraries: {string.Join(", ", libraryNames)}.");
    }

    /// <inheritdoc/>
    public nint GetProcAddress(string proc, int? slot = null)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        return NativeLibrary.GetExport(_handle, proc);
    }

    /// <inheritdoc/>
    public bool TryGetProcAddress(string proc, out nint addr, int? slot = null)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        return NativeLibrary.TryGetExport(_handle, proc, out addr);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_handle != 0)
        {
            NativeLibrary.Free(_handle);
            _handle = 0;
        }
    }
}
