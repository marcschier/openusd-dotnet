// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// The retained bounded raster shadow-map descriptor table for the current page.
/// </summary>
/// <remarks>
/// <para>
/// The table is a whole replacement, because a descriptor is resolved against the
/// ordered light table and the caster bounds of the page that carried it. hdSilk
/// publishes the command only when the resolved table changed, so
/// <see cref="Revision"/> moving is exactly the signal that every retained shadow
/// map has to be rendered again; a page that publishes no command leaves both the
/// table and every map it describes untouched.
/// </para>
/// <para>
/// A renderer that has never applied a shadow command retains no descriptors and
/// casts no shadows, which is the behaviour of every page ABI before 19.
/// </para>
/// </remarks>
public sealed class SilkShadowTable
{
    private readonly List<SilkShadowDescriptor> _descriptors = [];
    private readonly int[] _slotsByLight =
        new int[SilkFrameCommand.MaximumLights];

    /// <summary>Initializes an empty table that casts no shadows.</summary>
    public SilkShadowTable() => Array.Fill(_slotsByLight, -1);

    /// <summary>Gets the number of published shadow maps.</summary>
    public int Count => _descriptors.Count;

    /// <summary>Gets the number of direct lights the descriptors index.</summary>
    public uint LightCount { get; private set; }

    /// <summary>Gets shadow state hdSilk could not put on the wire.</summary>
    public SilkShadowUnsupportedFeatures UnsupportedFeatures { get; private set; }

    /// <summary>
    /// Gets the revision of the retained table, which changes only when the table
    /// itself changed.
    /// </summary>
    public ulong Revision { get; private set; }

    /// <summary>Gets whether any shadow map is described.</summary>
    public bool HasShadows => _descriptors.Count > 0;

    /// <summary>Gets the retained descriptors in ascending map order.</summary>
    public IReadOnlyList<SilkShadowDescriptor> Descriptors => _descriptors;

    /// <summary>
    /// Resolves the map slot a direct light samples, or <c>-1</c> when the light
    /// casts no shadow.
    /// </summary>
    /// <param name="lightIndex">The frame light table index.</param>
    /// <returns>The map slot, or <c>-1</c>.</returns>
    public int ResolveSlot(int lightIndex) =>
        (uint)lightIndex < SilkFrameCommand.MaximumLights ? _slotsByLight[lightIndex] : -1;

    /// <summary>
    /// Copies this table aside so a rejected page can be undone from it, and
    /// reserves the room the restore will need.
    /// </summary>
    /// <remarks>
    /// Taken before the page mutates anything, so a failure here leaves the
    /// retained table untouched. Reserving the backup's own length in the live
    /// list is what makes <see cref="RestoreFrom"/> allocation free: the list can
    /// only grow between here and there, so the room is already present.
    /// </remarks>
    internal void CopyAsideInto(SilkShadowTable backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        backup.CopyFrom(this);
        _descriptors.EnsureCapacity(backup._descriptors.Count);
    }

    /// <summary>
    /// Puts a rejected page's replacement back, in place and without allocating.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In place, and deliberately not by exchanging the two lists.
    /// <see cref="Descriptors"/> hands out the live list itself, so a consumer
    /// that resolved the retained descriptors once -- a shadow map cache holding
    /// them for the lifetime of the maps it rendered from them -- keeps that
    /// reference. Swapping the container would leave such a reader looking at the
    /// rejected page's table forever, which is the exact state the rollback
    /// exists to prevent.
    /// </para>
    /// <para>
    /// The capacity the restore needs was reserved by
    /// <see cref="CopyAsideInto"/> and a list never shrinks its capacity, so the
    /// refill below cannot allocate and therefore cannot fail: a rollback that
    /// can fail half way is not a rollback.
    /// </para>
    /// </remarks>
    internal void RestoreFrom(SilkShadowTable backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        _descriptors.Clear();
        for (int index = 0; index < backup._descriptors.Count; index++)
        {
            _descriptors.Add(backup._descriptors[index]);
        }
        backup._slotsByLight.CopyTo(_slotsByLight.AsSpan());
        LightCount = backup.LightCount;
        UnsupportedFeatures = backup.UnsupportedFeatures;
        Revision = backup.Revision;
    }

    /// <summary>
    /// Replaces this table with a copy of another, reusing the retained
    /// capacity.
    /// </summary>
    /// <remarks>
    /// A shadow command replaces the whole table, so a page that is rejected
    /// after publishing one can only be undone from a copy taken before it.
    /// </remarks>
    internal void CopyFrom(SilkShadowTable other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _descriptors.Clear();
        _descriptors.AddRange(other._descriptors);
        other._slotsByLight.CopyTo(_slotsByLight.AsSpan());
        LightCount = other.LightCount;
        UnsupportedFeatures = other.UnsupportedFeatures;
        Revision = other.Revision;
    }

    /// <summary>Replaces the whole retained table from one page command.</summary>
    internal void Update(SilkShadowCommand command)
    {
        _descriptors.Clear();
        Array.Fill(_slotsByLight, -1);
        for (uint index = 0; index < command.DescriptorCount; index++)
        {
            SilkShadowDescriptor descriptor = command.GetDescriptor(index);
            _descriptors.Add(descriptor);
            _slotsByLight[(int)descriptor.LightIndex] = (int)descriptor.MapIndex;
        }

        LightCount = command.LightCount;
        UnsupportedFeatures = command.UnsupportedFeatures;
        Revision++;
    }
}
