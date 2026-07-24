// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Represents a failure reported by the hdSilk native ABI.
/// </summary>
public sealed class OpenUsdSilkException : Exception
{
    internal OpenUsdSilkException(OpenUsdNativeStatus status, string message)
        : base(message)
    {
        Status = status;
    }

    /// <summary>Gets the native status code.</summary>
    public OpenUsdNativeStatus Status { get; }
}
