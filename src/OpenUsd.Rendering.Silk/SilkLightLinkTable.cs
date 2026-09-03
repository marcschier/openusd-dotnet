// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// The direct lights that reach one prim, the direct lights whose shadows it
/// casts, and the dome lights that reach it, as bit masks over the frame tables.
/// </summary>
/// <param name="LightMask">
/// Bit <c>i</c> is set when direct light <c>i</c> illuminates the prim.
/// </param>
/// <param name="ShadowMask">
/// Bit <c>i</c> is set when the prim casts direct light <c>i</c>'s shadow,
/// independently of <paramref name="LightMask"/>.
/// </param>
/// <param name="DomeMask">
/// Bit <c>i</c> is set when dome light <c>i</c> of the frame dome table
/// illuminates the prim. It is a separate bit space from
/// <paramref name="LightMask"/> because the dome table and the direct-light
/// table are two orderings; there is no dome shadow mask, because no dome shadow
/// pass exists to restrict.
/// </param>
public readonly record struct SilkLightLinkMasks(
    uint LightMask,
    uint ShadowMask,
    uint DomeMask)
{
    /// <summary>
    /// Gets the masks of a prim that is linked to every light a frame can carry.
    /// </summary>
    /// <remarks>
    /// Every bit the frame tables can address is set rather than only the bits
    /// the current frame uses, so the value does not depend on how many lights a
    /// particular frame published. Consumers iterate the published light and dome
    /// counts, so the unused high bits are never read.
    /// </remarks>
    public static SilkLightLinkMasks All { get; } = new(AllBits, AllBits, AllDomeBits);

    /// <summary>Gets every bit the fixed frame light table can address.</summary>
    public const uint AllBits = (1u << (int)SilkFrameCommand.MaximumLights) - 1;

    /// <summary>Gets every bit the fixed frame dome table can address.</summary>
    public const uint AllDomeBits = (1u << (int)SilkFrameCommand.MaximumDomes) - 1;

    /// <summary>Reports whether direct light <paramref name="index"/> illuminates the prim.</summary>
    public bool IsLit(int index) =>
        (uint)index < SilkFrameCommand.MaximumLights && (LightMask & (1u << index)) != 0;

    /// <summary>Reports whether the prim casts direct light <paramref name="index"/>'s shadow.</summary>
    public bool CastsShadow(int index) =>
        (uint)index < SilkFrameCommand.MaximumLights && (ShadowMask & (1u << index)) != 0;

    /// <summary>Reports whether dome light <paramref name="index"/> illuminates the prim.</summary>
    public bool IsDomeLit(int index) =>
        (uint)index < SilkFrameCommand.MaximumDomes && (DomeMask & (1u << index)) != 0;

    /// <summary>
    /// Gets the three masks folded into the single key a surface block cache and a
    /// per-draw batch key both compare.
    /// </summary>
    /// <remarks>
    /// The dome mask is in the key, not merely in the block. Two prims that share
    /// a material but link different domes must not share a surface buffer or a
    /// draw: the dome mask is a per-draw constant, and batching them together
    /// would give both of them whichever mask was written last.
    /// </remarks>
    internal uint Packed =>
        (LightMask & AllBits) |
        ((ShadowMask & AllBits) << 8) |
        ((DomeMask & AllDomeBits) << 16);
}

/// <summary>
/// The retained UsdLux light and shadow link table for the current page.
/// </summary>
/// <remarks>
/// <para>
/// The table is sparse: hdSilk publishes an entry only for a prim whose masks are
/// not the default of "every light links", so a scene that authors no linking
/// retains nothing and every lookup resolves to
/// <see cref="SilkLightLinkMasks.All"/>. It is also a whole replacement, because
/// the masks index the ordered light table of the frame they were published with
/// and a per-prim delta against a different light ordering would name the wrong
/// lights.
/// </para>
/// <para>
/// Lookups fall back from the exact instance to the path-wide entry, matching the
/// wire contract that an entry with
/// <see cref="SilkLightLinkCommand.AllInstances"/> applies to every published
/// instance of a path.
/// </para>
/// </remarks>
public sealed class SilkLightLinkTable
{
    private Dictionary<(string Path, int InstanceIndex), SilkLightLinkMasks> _entries =
        [];
    private Dictionary<string, SilkLightLinkMasks> _pathEntries =
        new(StringComparer.Ordinal);
    private bool _hasDomeLinks;

    /// <summary>Gets the number of direct lights the retained masks index.</summary>
    public uint LightCount { get; private set; }

    /// <summary>Gets the number of dome lights the retained dome masks index.</summary>
    /// <remarks>
    /// Zero for a page that publishes no dome table, which is what a scene with
    /// no dome light and a scene over the dome budget both produce: in either
    /// case no dome is individually addressable and every dome lights every prim.
    /// </remarks>
    public uint DomeCount { get; private set; }

    /// <summary>Gets link state hdSilk could not fully express.</summary>
    public SilkLightLinkUnsupportedFeatures UnsupportedFeatures { get; private set; }

    /// <summary>Gets the number of retained per-prim and per-instance entries.</summary>
    public int Count => _entries.Count + _pathEntries.Count;

    /// <summary>
    /// Gets the revision of the retained table, which changes only when the table
    /// itself changed.
    /// </summary>
    public ulong Revision { get; private set; }

    /// <summary>
    /// Gets whether any prim's masks differ from the default of "every light
    /// links".
    /// </summary>
    /// <remarks>
    /// False for a scene that authors no linking and for one whose linking was
    /// retired, because the table is sparse and default-free in both cases: a
    /// prim that is absent resolves to <see cref="SilkLightLinkMasks.All"/>.
    /// </remarks>
    public bool HasLinks => Count > 0;

    /// <summary>
    /// Gets whether the retained table indexes nothing at all, which is the
    /// state a retirement leaves and the state a scene starts in.
    /// </summary>
    /// <remarks>
    /// A table in this shape is valid against any frame, because it names no
    /// light and no dome. Every other table's masks are read against the frame's
    /// orderings, so it stays valid only while the frame keeps publishing the
    /// counts it was resolved against -- which is why a frame-only page is
    /// checked against it too.
    /// </remarks>
    internal bool IsCanonicalEmpty => Count == 0 && LightCount == 0 && DomeCount == 0;

    /// <summary>
    /// Exchanges the whole retained table with another, without allocating.
    /// </summary>
    /// <remarks>
    /// This is how a rejected page's table is put back. Copying the previous
    /// contents in would have to grow the dictionaries again, and a rollback that
    /// can itself fail half way is not a rollback; exchanging the containers
    /// cannot fail at all. The identity of this object is preserved because
    /// consumers hold it.
    /// </remarks>
    internal void SwapWith(SilkLightLinkTable other)
    {
        ArgumentNullException.ThrowIfNull(other);
        (_entries, other._entries) = (other._entries, _entries);
        (_pathEntries, other._pathEntries) = (other._pathEntries, _pathEntries);
        (_hasDomeLinks, other._hasDomeLinks) = (other._hasDomeLinks, _hasDomeLinks);
        (LightCount, other.LightCount) = (other.LightCount, LightCount);
        (DomeCount, other.DomeCount) = (other.DomeCount, DomeCount);
        (UnsupportedFeatures, other.UnsupportedFeatures) =
            (other.UnsupportedFeatures, UnsupportedFeatures);
        (Revision, other.Revision) = (other.Revision, Revision);
    }

    /// <summary>
    /// Replaces this table with a copy of another, reusing the retained
    /// capacity.
    /// </summary>
    /// <remarks>
    /// A light link command replaces the whole table, so a page that is rejected
    /// after publishing one can only be undone from a copy taken before it. The
    /// copy is written into a table the scene keeps, so the dictionaries reuse
    /// their capacity and a rolled-back page allocates nothing steady state.
    /// </remarks>
    internal void CopyFrom(SilkLightLinkTable other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _entries.Clear();
        _pathEntries.Clear();
        foreach (KeyValuePair<(string, int), SilkLightLinkMasks> entry in other._entries)
        {
            _entries[entry.Key] = entry.Value;
        }
        foreach (KeyValuePair<string, SilkLightLinkMasks> entry in other._pathEntries)
        {
            _pathEntries[entry.Key] = entry.Value;
        }
        _hasDomeLinks = other._hasDomeLinks;
        LightCount = other.LightCount;
        DomeCount = other.DomeCount;
        UnsupportedFeatures = other.UnsupportedFeatures;
        Revision = other.Revision;
    }

    /// <summary>Replaces the whole retained table from one page command.</summary>
    internal void Update(SilkLightLinkCommand command)
    {
        _entries.Clear();
        _pathEntries.Clear();
        _hasDomeLinks = false;
        uint domeCount = command.DomeCount;
        uint allDomes = domeCount >= 32 ? uint.MaxValue : (1u << (int)domeCount) - 1;
        foreach (SilkLightLinkEntry entry in command)
        {
            var masks = new SilkLightLinkMasks(
                entry.LightMask,
                entry.ShadowMask,
                entry.DomeMask);
            if ((entry.DomeMask & allDomes) != allDomes)
            {
                _hasDomeLinks = true;
            }
            if (entry.InstanceIndex == SilkLightLinkCommand.AllInstances)
            {
                _pathEntries[entry.Path] = masks;
                continue;
            }

            _entries[(entry.Path, entry.InstanceIndex)] = masks;
        }

        LightCount = command.LightCount;
        DomeCount = domeCount;
        UnsupportedFeatures = command.UnsupportedFeatures;
        Revision++;
    }

    /// <summary>
    /// Gets whether any retained entry excludes a dome light the frame table
    /// published.
    /// </summary>
    /// <remarks>
    /// This is the switch that keeps a scene with no dome linking on exactly the
    /// resources and the code path it used before dome linking existed: the
    /// prefiltered environment is baked as one composed set rather than as one
    /// group per dome, so an unlinked scene samples the same texels through the
    /// same coordinates and renders byte-identical bytes.
    /// </remarks>
    public bool HasDomeLinks => _hasDomeLinks;

    /// <summary>
    /// Resolves the dome mask that applies to every prim the sparse table omits.
    /// </summary>
    /// <remarks>
    /// Every dome the frame published, which is what an absent entry means. It is
    /// resolved against the published <see cref="DomeCount"/> rather than against
    /// the full bit width so that the "all domes" comparison the shader and the
    /// resource path both make is the same predicate on both sides.
    /// </remarks>
    public uint AllDomesMask =>
        DomeCount >= 32 ? uint.MaxValue : (1u << (int)DomeCount) - 1;

    /// <summary>
    /// Collects every packed mask a lookup against this table can return.
    /// </summary>
    /// <remarks>
    /// The default masks are always included, because every prim the sparse table
    /// omits resolves to them. It exists so a consumer that caches per-mask
    /// resources can drop the ones the current table can no longer produce: a
    /// live-edited stage that walks a collection through many shapes would
    /// otherwise accumulate one retained resource per mask it ever resolved.
    /// </remarks>
    /// <param name="destination">Receives the masks, replacing its contents.</param>
    internal void CollectPackedMasks(HashSet<uint> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        _ = destination.Add(SilkLightLinkMasks.All.Packed);
        foreach (SilkLightLinkMasks masks in _pathEntries.Values)
        {
            _ = destination.Add(masks.Packed);
        }
        foreach (SilkLightLinkMasks masks in _entries.Values)
        {
            _ = destination.Add(masks.Packed);
        }
    }

    /// <summary>Resolves the masks that apply to one published mesh record.</summary>
    /// <param name="path">The prim's authoritative USD path.</param>
    /// <param name="instanceIndex">The instance ordinal inside its instancer.</param>
    /// <returns>
    /// The retained masks, or <see cref="SilkLightLinkMasks.All"/> when the prim
    /// is not in the sparse table and is therefore lit by every light.
    /// </returns>
    public SilkLightLinkMasks Resolve(string path, int instanceIndex)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_entries.Count > 0 &&
            _entries.TryGetValue((path, instanceIndex), out SilkLightLinkMasks instanceMasks))
        {
            return instanceMasks;
        }
        if (_pathEntries.Count > 0 &&
            _pathEntries.TryGetValue(path, out SilkLightLinkMasks pathMasks))
        {
            return pathMasks;
        }
        return SilkLightLinkMasks.All;
    }
}
