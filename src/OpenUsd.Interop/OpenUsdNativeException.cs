// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

/// <summary>
/// Represents an error returned by the project-owned OpenUSD native ABI.
/// </summary>
public sealed class OpenUsdNativeException : Exception
{
    internal OpenUsdNativeException(OpenUsdNativeStatus status, string message)
        : base(message)
    {
        Status = status;
    }

    /// <summary>Gets the native status code.</summary>
    public OpenUsdNativeStatus Status { get; }
}
