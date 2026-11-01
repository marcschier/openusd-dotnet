// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Represents a failure reported while preparing or using hdSilk resources.
/// </summary>
public sealed class OpenUsdSilkException : Exception
{
    internal OpenUsdSilkException(OpenUsdNativeStatus status, string message)
        : base(message)
    {
        Status = status;
    }

    internal OpenUsdSilkException(OpenUsdNativeStatus status, string message, Exception innerException)
        : base(message, innerException)
    {
        Status = status;
    }

    /// <summary>Gets the status associated with the failure.</summary>
    public OpenUsdNativeStatus Status { get; }
}
