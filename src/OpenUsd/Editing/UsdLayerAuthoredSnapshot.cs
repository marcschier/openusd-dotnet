// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Editing;

/// <summary>Presence and kind of the target-layer property spec, independent of composition.</summary>
public enum UsdLayerPropertyKind
{
    /// <summary>No property spec exists in this layer.</summary>
    Absent = 0,
    /// <summary>An attribute spec exists in this layer.</summary>
    Attribute = 1,
    /// <summary>A relationship spec exists in this layer.</summary>
    Relationship = 2
}

/// <summary>An immutable summary of an exact target-layer declaration and affected opinion.</summary>
public sealed class UsdLayerAuthoredOpinion : IUsdDetachedResult
{
    internal UsdLayerAuthoredOpinion(
        UsdLayerEditAddress address, UsdLayerPropertyKind propertyKind,
        string? typeName, UsdLayerEditVariability? variability, bool? custom, UsdLayerEditValue value)
    {
        Address = address;
        PropertyKind = propertyKind;
        TypeName = typeName;
        Variability = variability;
        Custom = custom;
        Value = value;
    }

    /// <summary>Gets the exact field address.</summary>
    public UsdLayerEditAddress Address { get; }
    /// <summary>Gets target-layer spec presence and kind.</summary>
    public UsdLayerPropertyKind PropertyKind { get; }
    /// <summary>Gets the exact authored type token, including role aliases, or null for field absence.</summary>
    public string? TypeName { get; }
    /// <summary>Gets the exact authored variability, or null for field absence.</summary>
    public UsdLayerEditVariability? Variability { get; }
    /// <summary>Gets the exact authored custom flag, or null for field absence.</summary>
    public bool? Custom { get; }
    /// <summary>Gets absence, block, or a concrete affected opinion; never a weaker composed value.</summary>
    public UsdLayerEditValue Value { get; }
}

/// <summary>A deeply immutable native-authored snapshot of ordered affected addresses in one layer.</summary>
/// <remarks>
/// The private packet is copied before its native owner is released. It preserves declaration field
/// presence and exact native values. This is per-edit state, not a full-layer checkpoint.
/// </remarks>
public sealed class UsdLayerAuthoredSnapshot : IUsdDetachedResult
{
    private readonly byte[] _bytes;

    internal UsdLayerAuthoredSnapshot(
        UsdLayerIdentity identity, ulong revision, UsdLayerAuthoredOpinion[] opinions, ReadOnlySpan<byte> bytes)
    {
        Identity = identity;
        Revision = revision;
        _bytes = bytes.ToArray();
        Opinions = Array.AsReadOnly((UsdLayerAuthoredOpinion[])opinions.Clone());
        var addresses = new UsdLayerEditAddress[opinions.Length];
        for (int index = 0; index < opinions.Length; index++)
        {
            addresses[index] = opinions[index].Address;
        }
        Addresses = Array.AsReadOnly(addresses);
    }

    /// <summary>Gets the logical target identity and generation.</summary>
    public UsdLayerIdentity Identity { get; }
    /// <summary>Gets the capture revision; unrelated revisions do not cause per-edit conflicts.</summary>
    public ulong Revision { get; }
    /// <summary>Gets the immutable caller-ordered addresses.</summary>
    public IReadOnlyList<UsdLayerEditAddress> Addresses { get; }
    /// <summary>Gets exact caller-ordered authored declaration and value summaries.</summary>
    public IReadOnlyList<UsdLayerAuthoredOpinion> Opinions { get; }
    /// <summary>Gets the native packet's byte length without copying it; excludes decoded managed storage.</summary>
    public int ByteLength => _bytes.Length;
    /// <summary>Returns an independent copy of the native packet; it cannot be rebound to another document.</summary>
    public byte[] CopyBytes() => (byte[])_bytes.Clone();

    /// <summary>Compares complete native packets, including identity and revision, without allocating.</summary>
    /// <remarks>
    /// Unrelated revisions may differ even when affected opinions match. This is not the affected-state
    /// comparison performed by native compare-and-apply or compare-and-restore.
    /// </remarks>
    public bool HasSamePayload(UsdLayerAuthoredSnapshot? other) =>
        other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    internal ReadOnlySpan<byte> Payload => _bytes;
}
