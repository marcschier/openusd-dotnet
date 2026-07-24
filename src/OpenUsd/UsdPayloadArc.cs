// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

/// <summary>
/// Describes one composed direct payload arc using detached authored values.
/// </summary>
public readonly record struct UsdPayloadArc
{
    /// <summary>Creates a detached payload-arc value.</summary>
    /// <param name="assetPath">
    /// The authored payload asset path. This is empty for an internal payload and remains relative
    /// when authored relative.
    /// </param>
    /// <param name="targetPrimPath">
    /// The authored target prim path, or an empty string when the payload relies on the target
    /// layer's default prim.
    /// </param>
    /// <param name="sourceLayerIdentifier">
    /// The identifier of the layer whose list operation introduces the composed arc.
    /// </param>
    public UsdPayloadArc(
        string assetPath,
        string targetPrimPath,
        string sourceLayerIdentifier)
    {
        ArgumentNullException.ThrowIfNull(assetPath);
        ArgumentNullException.ThrowIfNull(targetPrimPath);
        ArgumentException.ThrowIfNullOrEmpty(sourceLayerIdentifier);
        AssetPath = assetPath;
        TargetPrimPath = targetPrimPath;
        SourceLayerIdentifier = sourceLayerIdentifier;
    }

    /// <summary>Gets the authored payload asset path.</summary>
    public string AssetPath { get; }

    /// <summary>Gets the authored target prim path, or an empty string when omitted.</summary>
    public string TargetPrimPath { get; }

    /// <summary>Gets the identifier of the layer that introduces this composed arc.</summary>
    public string SourceLayerIdentifier { get; }
}
