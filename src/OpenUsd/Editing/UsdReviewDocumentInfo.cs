// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Editing;

/// <summary>Detached recorded provenance from a structurally valid portable review document.</summary>
/// <remarks>
/// All values are untrusted document claims, not verified filesystem identities or authorization.
/// Inspection does not open a stage or file, import opinions, or create a save receipt.
/// Validate host path policy and explicit source intent before reading and importing the document.
/// </remarks>
public sealed class UsdReviewDocumentInfo : IUsdDetachedResult
{
    internal UsdReviewDocumentInfo(
        int documentByteLength, string documentId, string sourceRootPath, string sourceFingerprint,
        string originalTargetIdentifier, string targetDocumentPath, string assetAnchor,
        ReadOnlySpan<UsdReviewDependency> dependencies)
    {
        DocumentByteLength = documentByteLength;
        DocumentId = documentId;
        SourceRootPath = sourceRootPath;
        SourceFingerprint = sourceFingerprint;
        OriginalTargetIdentifier = originalTargetIdentifier;
        TargetDocumentPath = targetDocumentPath;
        AssetAnchor = assetAnchor;
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
    }

    /// <summary>Gets the structurally admitted URD document version.</summary>
    public uint Version { get; } = 1;
    /// <summary>Gets the inspected document's byte length, not the descriptor's storage size.</summary>
    public int DocumentByteLength { get; }
    /// <summary>Gets the recorded document lineage claim.</summary>
    public string DocumentId { get; }
    /// <summary>Gets the recorded source path, without opening or authorizing access to it.</summary>
    public string SourceRootPath { get; }
    /// <summary>Gets the recorded source-root SHA-256 claim, without checking a source file.</summary>
    public string SourceFingerprint { get; }
    /// <summary>Gets the recorded original review identifier as provenance only.</summary>
    public string OriginalTargetIdentifier { get; }
    /// <summary>Gets the recorded publication target, without granting write permission.</summary>
    public string TargetDocumentPath { get; }
    /// <summary>Gets the recorded asset-anchor claim, without filesystem resolution.</summary>
    public string AssetAnchor { get; }
    /// <summary>Gets immutable recorded dependency claims, without opening those dependencies.</summary>
    public IReadOnlyList<UsdReviewDependency> Dependencies { get; }
}
