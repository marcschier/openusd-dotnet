// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

/// <summary>
/// Identifies the managed OpenUsd product and API generation.
/// </summary>
public static class OpenUsdInfo
{
    /// <summary>Gets the product name.</summary>
    public const string ProductName = "OpenUsd";

    /// <summary>Gets the generation of the managed API contract.</summary>
    public const int ManagedApiGeneration = 1;

    /// <summary>Gets the managed assembly version.</summary>
    public static string Version =>
        typeof(OpenUsdInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
}
