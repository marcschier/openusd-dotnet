// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

/// <summary>
/// Status codes returned by the OpenUsd native ABI.
/// </summary>
public enum OpenUsdNativeStatus
{
    /// <summary>The operation succeeded.</summary>
    Ok = 0,

    /// <summary>An argument was invalid.</summary>
    InvalidArgument = 1,

    /// <summary>The requested object or file was not found.</summary>
    NotFound = 2,

    /// <summary>The supplied output buffer was too small.</summary>
    BufferTooSmall = 3,

    /// <summary>A native exception or diagnostic stopped the operation.</summary>
    NativeError = 4,

    /// <summary>A thread-affine operation was attempted from a non-owner thread.</summary>
    WrongThread = 5
}
