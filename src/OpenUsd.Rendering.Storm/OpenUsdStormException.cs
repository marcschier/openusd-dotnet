// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Rendering.Storm;

/// <summary>
/// Represents a failure reported by the OpenUSD Hydra rendering ABI.
/// </summary>
public sealed class OpenUsdStormException : Exception
{
    internal OpenUsdStormException(OpenUsdNativeStatus status, string message)
        : base(message)
    {
        Status = status;
    }

    /// <summary>Gets the native status code.</summary>
    public OpenUsdNativeStatus Status { get; }
}
