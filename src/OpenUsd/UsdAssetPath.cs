// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

/// <summary>Represents an authored USD asset path.</summary>
public readonly record struct UsdAssetPath : IUsdDetachedResult
{
    /// <summary>Initializes an asset path.</summary>
    public UsdAssetPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
    }

    /// <summary>Gets the authored, unresolved asset path.</summary>
    public string Path { get; }

    /// <inheritdoc/>
    public override string ToString() => Path;
}
