// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Editing;

/// <summary>A detached native-verified source origin for one review-authoring stage.</summary>
/// <remarks>
/// Capture from a stage opened with <see cref="UsdStage.OpenForReview"/> before authoring review changes.
/// Native capture and import revalidate the source and its dependencies. This is not a portable stage
/// identity or permission to publish files, and does not establish an origin for a legacy open stage.
/// </remarks>
public sealed class UsdReviewSourceBinding : IUsdDetachedResult
{
    private readonly byte[] _bytes;

    internal UsdReviewSourceBinding(
        ulong stageId, string sourceRootPath, string sourceFingerprint, string assetAnchor,
        ReadOnlySpan<UsdReviewDependency> dependencies, ReadOnlySpan<byte> bytes)
    {
        StageId = stageId;
        SourceRootPath = sourceRootPath;
        SourceFingerprint = sourceFingerprint;
        AssetAnchor = assetAnchor;
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        _bytes = bytes.ToArray();
    }

    /// <summary>Gets the original, verified absolute source-root path.</summary>
    public string SourceRootPath { get; }
    /// <summary>Gets the SHA-256 fingerprint of the admitted source-root bytes.</summary>
    /// <remarks>
    /// Dependency identities are carried separately in <see cref="Dependencies"/> and revalidated natively.
    /// </remarks>
    public string SourceFingerprint { get; }
    /// <summary>Gets the native-verified filesystem anchor for review asset resolution.</summary>
    public string AssetAnchor { get; }
    /// <summary>Gets immutable verified source dependency identities.</summary>
    public IReadOnlyList<UsdReviewDependency> Dependencies { get; }
    /// <summary>Gets the native binding packet length without copying it.</summary>
    public int ByteLength => _bytes.Length;

    /// <summary>Compares the entire binding, including its process-local stage identity, without allocating.</summary>
    public bool HasSamePayload(UsdReviewSourceBinding? other) =>
        other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    internal ulong StageId { get; }
    internal ReadOnlySpan<byte> Payload => _bytes;
}
