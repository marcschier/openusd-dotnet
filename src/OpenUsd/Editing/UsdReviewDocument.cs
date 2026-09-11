// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Editing;

/// <summary>A detached portable review document, distinct from a stage or a flattened source export.</summary>
/// <remarks>
/// Only the native verifier decodes the authored review contents. Source, target and anchor paths are
/// provenance descriptors, not authorization to write files. Publication, physical alias checks and
/// explicit reconciliation are caller-owned. A same-process capture privately retains a save receipt;
/// <see cref="CopyBytes"/> never includes it and <see cref="Read"/> never creates one.
/// </remarks>
public sealed class UsdReviewDocument : IUsdDetachedResult
{
    private readonly byte[] _bytes;
    private readonly byte[] _receipt;

    internal UsdReviewDocument(
        string documentId, string sourceRootPath, string sourceFingerprint, string originalTargetIdentifier,
        string targetDocumentPath, string assetAnchor, ReadOnlySpan<UsdReviewDependency> dependencies,
        ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> receipt)
    {
        DocumentId = documentId;
        SourceRootPath = sourceRootPath;
        SourceFingerprint = sourceFingerprint;
        OriginalTargetIdentifier = originalTargetIdentifier;
        TargetDocumentPath = targetDocumentPath;
        AssetAnchor = assetAnchor;
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        _bytes = bytes.ToArray();
        _receipt = receipt.ToArray();
    }

    /// <summary>Gets the admitted portable document format version.</summary>
    public uint Version { get; } = 1;
    /// <summary>Gets the canonical document lineage identifier, not a native stage or layer identity.</summary>
    public string DocumentId { get; }
    /// <summary>Gets the verified original source-root path.</summary>
    public string SourceRootPath { get; }
    /// <summary>Gets the SHA-256 fingerprint of the admitted source-root bytes.</summary>
    /// <remarks>The complete dependency manifest is carried separately in <see cref="Dependencies"/>.</remarks>
    public string SourceFingerprint { get; }
    /// <summary>Gets the captured review layer's identifier as provenance, not a replay target.</summary>
    public string OriginalTargetIdentifier { get; }
    /// <summary>Gets the intended absolute publication path; reading does not authorize publication there.</summary>
    public string TargetDocumentPath { get; }
    /// <summary>Gets the native-verified asset anchor, independent of the publication directory.</summary>
    public string AssetAnchor { get; }
    /// <summary>Gets immutable verified dependency identities.</summary>
    public IReadOnlyList<UsdReviewDependency> Dependencies { get; }
    /// <summary>
    /// Gets the portable byte length without copying; excludes the private save receipt and metadata.
    /// </summary>
    public int ByteLength => _bytes.Length;

    /// <summary>Returns independent portable bytes, without the private same-process save receipt.</summary>
    public byte[] CopyBytes() => (byte[])_bytes.Clone();

    /// <summary>Compares the complete portable payload without allocating or comparing private save receipts.</summary>
    /// <remarks>Payload equality never grants permission to acknowledge saved state or import a document.</remarks>
    public bool HasSamePayload(UsdReviewDocument? other) =>
        other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    /// <summary>Inspects recorded provenance without opening files or creating an importable document.</summary>
    /// <remarks>
    /// Native checksum, type, extent and depth admission still apply. Returned paths and identities are
    /// untrusted claims, not authorization. Validate host path policy, then call
    /// <see cref="Read"/> with explicit source intent before verified opening/import.
    /// No document payload, stage handle or saved acknowledgement receipt is returned.
    /// </remarks>
    public static UsdReviewDocumentInfo Inspect(ReadOnlySpan<byte> bytes)
    {
        UsdReviewDocumentCodec.ValidateDocumentBytes(bytes);
        byte[] input = bytes.ToArray();
        return UsdReviewDocumentCodec.DecodeInspection(
            OpenUsdNativeRuntime.InspectReviewDocument(input), input.Length);
    }

    /// <summary>Strictly admits native portable bytes against an explicitly intended source path.</summary>
    /// <remarks>
    /// This does not import authored opinions, open a stage, publish a file or produce a save receipt.
    /// Import separately revalidates the source, dependencies, anchor and pristine destination session.
    /// </remarks>
    public static UsdReviewDocument Read(ReadOnlySpan<byte> bytes, string expectedSourcePath)
    {
        string sourcePath = UsdReviewDocumentCodec.FullSourcePath(expectedSourcePath, nameof(expectedSourcePath));
        UsdReviewDocumentCodec.ValidateDocumentBytes(bytes);
        byte[] input = bytes.ToArray();
        return UsdReviewDocumentCodec.DecodeReadDocument(
            OpenUsdNativeRuntime.ReadReviewDocument(input, sourcePath), input, sourcePath);
    }

    internal ReadOnlySpan<byte> Payload => _bytes;

    internal bool AcknowledgeSaved(OpenUsdNativeLayer layer) =>
        _receipt.Length != 0 && OpenUsdNativeRuntime.AcknowledgeReviewSaved(layer, _receipt);
}
