// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Editing;

/// <summary>A detached, opaque native checkpoint of the registered review layer, never a flattened stage.</summary>
/// <remarks>
/// Checkpoints retain admitted unknown metadata names and IEEE bits. Restore is an explicit
/// same-document operation that invalidates per-edit history. Cross-process/session identity and
/// anchor rebinding are not implemented; copied packets are not portable recovery documents.
/// </remarks>
public sealed class UsdLayerCheckpoint : IUsdDetachedResult
{
    private readonly byte[] _bytes;

    internal UsdLayerCheckpoint(
        UsdLayerIdentity identity, ulong revision, string identifier, string assetAnchor, ReadOnlySpan<byte> bytes)
    {
        Identity = identity;
        Revision = revision;
        Identifier = identifier;
        AssetAnchor = assetAnchor;
        _bytes = bytes.ToArray();
    }

    /// <summary>Gets the logical source identity and generation.</summary>
    public UsdLayerIdentity Identity { get; }
    /// <summary>Gets the exact captured revision used for conditional saved acknowledgement.</summary>
    public ulong Revision { get; }
    /// <summary>Gets the exact source review-layer identifier.</summary>
    public string Identifier { get; }
    /// <summary>Gets the source layer's resolved asset anchor, empty for an anonymous review layer.</summary>
    public string AssetAnchor { get; }
    /// <summary>Gets the native packet's byte length without copying it; excludes decoded managed storage.</summary>
    public int ByteLength => _bytes.Length;
    /// <summary>Returns an independent native checkpoint packet; no identity-patching import is provided.</summary>
    public byte[] CopyBytes() => (byte[])_bytes.Clone();

    /// <summary>Compares complete native packets, including identity and revision, without allocating.</summary>
    /// <remarks>
    /// Equal content at a different revision or target is not an equal packet. This does not replace
    /// native restore preconditions or conditional saved acknowledgement.
    /// </remarks>
    public bool HasSamePayload(UsdLayerCheckpoint? other) =>
        other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    /// <summary>
    /// Returns independent UTF-8 USDA bytes for caller-owned publication; no file is opened or written.
    /// </summary>
    /// <param name="destinationPath">An absolute native filesystem path ending exactly in .usda.</param>
    /// <remarks>
    /// This exports only authored review opinions. Compose the delta against the validated original source.
    /// Native export rejects unsupported pseudo-root metadata, non-finite numbers and unsafe asset relocation.
    /// Export remains available after the stage and layer handles are disposed.
    /// </remarks>
    public byte[] ExportBytes(string destinationPath)
    {
        _ = UsdEditingValidation.Text(destinationPath, nameof(destinationPath));
        if (!Path.IsPathFullyQualified(destinationPath) ||
            !destinationPath.EndsWith(".usda", StringComparison.Ordinal))
        {
            throw new ArgumentException("An absolute destination ending exactly in .usda is required.",
                nameof(destinationPath));
        }
        byte[] result = OpenUsdNativeRuntime.ExportLayerCheckpoint(_bytes, destinationPath);
        try
        {
            _ = UsdEditingValidation.Utf8.GetCharCount(result);
        }
        catch (System.Text.DecoderFallbackException)
        {
            throw UsdEditingValidation.InvalidPacket("export contains invalid UTF-8");
        }
        if (result.AsSpan().Contains((byte)0))
        {
            throw UsdEditingValidation.InvalidPacket("export contains NUL");
        }
        return result;
    }

    internal ReadOnlySpan<byte> Payload => _bytes;
}
