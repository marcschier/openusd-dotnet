// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>Reports that encoded render output would exceed its admitted byte quota.</summary>
public sealed class RenderOutputQuotaExceededException : InvalidOperationException
{
    /// <summary>Creates an output-quota failure with a bounded diagnostic.</summary>
    public RenderOutputQuotaExceededException(string message) : base(message)
    {
    }
}
