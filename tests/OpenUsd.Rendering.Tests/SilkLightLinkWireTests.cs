// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the page ABI 24 UsdLux light and shadow link table: its wire layout,
/// the validation that keeps a malformed table out of retained state, the sparse
/// resolution rules, and the per-draw constants the mask is packed into.
/// </summary>
/// <remarks>
/// <para>
/// The table is the whole of light linking on the managed side, and it is sparse
/// and default-free by contract: a prim that every light reaches is absent, and
/// its absence is what makes an unlinked scene cost nothing. That makes absence
/// load-bearing, so these tests pin both directions -- what a present entry
/// means, and what a missing one resolves to -- rather than only round-tripping
/// bytes.
/// </para>
/// <para>
/// The validation cases matter for the same reason the material table's do: the
/// masks index the frame light table of the page they arrive with, so a mask
/// naming a light the frame never published describes a scene the producer
/// cannot have meant and must be rejected rather than retained and drawn.
/// Illumination and shadow membership are independent for published lights.
/// </para>
/// </remarks>
public sealed class SilkLightLinkWireTests
{
    private const string CubePath = "/World/Geom/Cube";
    private const string SpherePath = "/World/Geom/Sphere";

    [Test]
    public async Task LightLinkRoundTripsEveryEntryFieldAtItsOwnOffset()
    {
        byte[] page = CreateLightLink(
            lightCount: 4,
            unsupported: SilkLightLinkUnsupportedFeatures.Truncated,
            entries:
            [
                (CubePath, SilkLightLinkCommand.AllInstances, 0b0101u, 0b0001u),
                (SpherePath, 7, 0b1110u, 0b1010u)
            ]);

        uint entryCount;
        uint lightCount;
        SilkLightLinkUnsupportedFeatures unsupported;
        List<SilkLightLinkEntry> entries = [];
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
                page,
                1,
                SilkCommandParser.PageAbiVersion);
            _ = commands.MoveNext();
            SilkLightLinkCommand command = commands.Current.AsLightLink();
            entryCount = command.EntryCount;
            lightCount = command.LightCount;
            unsupported = command.UnsupportedFeatures;
            foreach (SilkLightLinkEntry entry in command)
            {
                entries.Add(entry);
            }
        }

        await Assert.That(entryCount).IsEqualTo(2u);
        await Assert.That(lightCount).IsEqualTo(4u);
        await Assert.That(unsupported)
            .IsEqualTo(SilkLightLinkUnsupportedFeatures.Truncated);
        await Assert.That(entries.Count).IsEqualTo(2);

        // Every field of the first entry differs from the same field of the
        // second, so an offset error cannot pass by reading the neighbour.
        await Assert.That(entries[0].Path).IsEqualTo(CubePath);
        await Assert.That(entries[0].InstanceIndex)
            .IsEqualTo(SilkLightLinkCommand.AllInstances);
        await Assert.That(entries[0].LightMask).IsEqualTo(0b0101u);
        await Assert.That(entries[0].ShadowMask).IsEqualTo(0b0001u);
        await Assert.That(entries[1].Path).IsEqualTo(SpherePath);
        await Assert.That(entries[1].InstanceIndex).IsEqualTo(7);
        await Assert.That(entries[1].LightMask).IsEqualTo(0b1110u);
        await Assert.That(entries[1].ShadowMask).IsEqualTo(0b1010u);
    }

    [Test]
    public async Task TheManagedDomeBoundMatchesTheNativeOne()
    {
        // The producer bounds the dome table and the consumer rejects anything
        // past that bound, so the two constants have to be the same number or a
        // legally produced dome mask would be refused.
        string header = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "native",
            "hdSilk",
            "include",
            "openusd_hdsilk.h"));
        System.Text.RegularExpressions.Match match =
            System.Text.RegularExpressions.Regex.Match(
                header,
                @"#define\s+OPENUSD_SILK_MAX_DOME_LIGHTS\s+(?<value>\d+)u",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5));

        await Assert.That(match.Success)
            .IsTrue()
            .Because("The native dome table bound was not found.");
        await Assert.That(uint.Parse(
            match.Groups["value"].Value,
            System.Globalization.CultureInfo.InvariantCulture))
            .IsEqualTo(SilkFrameCommand.MaximumDomes);
    }

    [Test]
    public async Task AnEmptyLightLinkTableIsValidAndRetiresRetainedLinking()
    {
        var scene = new SilkSceneState();
        scene.Apply(
            [
                .. DomeFrame(0, lightCount: 2),
                .. CreateLightLink(
                    lightCount: 2,
                    entries: [(CubePath, SilkLightLinkCommand.AllInstances, 0b01u, 0b01u)]),
            ],
            2,
            1);

        bool linkedBefore = scene.LightLinks.HasLinks;
        SilkLightLinkMasks masksBefore = scene.LightLinks.Resolve(CubePath, 0);
        ulong revisionBefore = scene.LightLinks.Revision;

        scene.Apply(CreateLightLink(lightCount: 0), 1, 2);

        await Assert.That(linkedBefore).IsTrue();
        await Assert.That(masksBefore.LightMask).IsEqualTo(0b01u);
        await Assert.That(scene.LightLinks.HasLinks).IsFalse();
        await Assert.That(scene.LightLinks.Count).IsEqualTo(0);
        await Assert.That(scene.LightLinks.Revision).IsGreaterThan(revisionBefore);

        // A retired table must resolve to "every light", not to "no light":
        // publishing the empty table is how a scene says linking was removed.
        await Assert.That(scene.LightLinks.Resolve(CubePath, 0))
            .IsEqualTo(SilkLightLinkMasks.All);
    }

    [Test]
    public async Task AnInstanceEntryOverridesItsPathAndOtherInstancesFallBack()
    {
        var scene = new SilkSceneState();
        scene.Apply(
            [
                .. DomeFrame(0, lightCount: 3),
                .. CreateLightLink(
                    lightCount: 3,
                    entries:
                    [
                        (CubePath, SilkLightLinkCommand.AllInstances, 0b010u, 0b010u),
                        (CubePath, 4, 0b111u, 0b101u)
                    ]),
            ],
            2,
            1);

        SilkLightLinkMasks overridden = scene.LightLinks.Resolve(CubePath, 4);
        SilkLightLinkMasks sibling = scene.LightLinks.Resolve(CubePath, 5);
        SilkLightLinkMasks absent = scene.LightLinks.Resolve(SpherePath, 0);

        await Assert.That(overridden.LightMask).IsEqualTo(0b111u);
        await Assert.That(overridden.ShadowMask).IsEqualTo(0b101u);
        await Assert.That(overridden.IsLit(2)).IsTrue();
        await Assert.That(overridden.CastsShadow(1)).IsFalse();
        await Assert.That(sibling.LightMask).IsEqualTo(0b010u);
        await Assert.That(absent).IsEqualTo(SilkLightLinkMasks.All);
    }

    [Test]
    public async Task ALinkedPrimAndAnUnlinkedPrimGetDistinctSurfaceConstants()
    {
        // The mask reaches the shader through the surface constants, so the two
        // blocks must differ in exactly the one component that carries it and in
        // nothing else. That is what makes it safe to key the block cache by the
        // mask: a block written for a different prim would otherwise be shared.
        byte[] linked = new byte[SilkSurfaceUniformWriter.ByteSize];
        byte[] unlinked = new byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(
            material: null,
            RenderHeadlight.Deterministic,
            linked,
            supportsVolumeTextures: false,
            new SilkLightLinkMasks(0b0110u, 0b0010u, 0b0011u));
        SilkSurfaceUniformWriter.Write(
            material: null,
            RenderHeadlight.Deterministic,
            unlinked,
            supportsVolumeTextures: false,
            SilkLightLinkMasks.All);

        UInt128 linkedLightMask = BinaryPrimitives.ReadUInt128LittleEndian(linked.AsSpan(208, 16));
        UInt128 linkedShadowMask = BinaryPrimitives.ReadUInt128LittleEndian(linked.AsSpan(224, 16));
        float linkedDomeMask = BinaryPrimitives.ReadSingleLittleEndian(linked.AsSpan(192, 4));
        UInt128 unlinkedLightMask = BinaryPrimitives.ReadUInt128LittleEndian(unlinked.AsSpan(208, 16));
        UInt128 unlinkedShadowMask = BinaryPrimitives.ReadUInt128LittleEndian(unlinked.AsSpan(224, 16));
        float unlinkedDomeMask = BinaryPrimitives.ReadSingleLittleEndian(unlinked.AsSpan(192, 4));

        await Assert.That(linkedLightMask).IsEqualTo((UInt128)6);
        await Assert.That(linkedShadowMask).IsEqualTo((UInt128)2);
        await Assert.That(linkedDomeMask).IsEqualTo(3f);
        await Assert.That(unlinkedLightMask).IsEqualTo(UInt128.MaxValue);
        await Assert.That(unlinkedShadowMask).IsEqualTo(UInt128.MaxValue);
        await Assert.That(unlinkedDomeMask).IsEqualTo(255f);

        // Everything else in the block is identical, so the mask is the only
        // reason a second block exists.
        for (int index = 0; index < 208; index++)
        {
            if (index is < 192 or >= 196)
            {
                await Assert.That(linked[index]).IsEqualTo(unlinked[index]);
            }
        }
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(linked.AsSpan(76))).IsEqualTo(0u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(linked.AsSpan(140))).IsEqualTo(0u);
    }

    [Test]
    public async Task DomeMasksRoundTripAndResolveThroughTheSparseTable()
    {
        // The dome mask is a third, independent bit space over the frame dome
        // table. A prim absent from the sparse table resolves to every dome, and
        // a present one keeps exactly the domes its collection admits.
        byte[] page = CreateLightLink(
            lightCount: 2,
            entries:
            [
                (CubePath, SilkLightLinkCommand.AllInstances, 0b11u, 0b11u),
                (SpherePath, SilkLightLinkCommand.AllInstances, 0b11u, 0b11u)
            ],
            domeCount: 2,
            domeMasks: [0b01u, 0b10u]);

        var scene = new SilkSceneState();
        _ = scene.Apply([.. DomeFrame(2, lightCount: 2), .. page], 2, 1);

        SilkLightLinkMasks cube = scene.LightLinks.Resolve(CubePath, 0);
        SilkLightLinkMasks sphere = scene.LightLinks.Resolve(SpherePath, 0);
        SilkLightLinkMasks absent = scene.LightLinks.Resolve("/World/Geom/Other", 0);

        await Assert.That(scene.LightLinks.DomeCount).IsEqualTo(2u);
        await Assert.That(scene.LightLinks.AllDomesMask).IsEqualTo(0b11u);
        await Assert.That(scene.LightLinks.HasDomeLinks).IsTrue();
        await Assert.That(cube.DomeMask).IsEqualTo(0b01u);
        await Assert.That(cube.IsDomeLit(0)).IsTrue();
        await Assert.That(cube.IsDomeLit(1)).IsFalse();
        await Assert.That(sphere.DomeMask).IsEqualTo(0b10u);
        await Assert.That(sphere.IsDomeLit(0)).IsFalse();
        await Assert.That(sphere.IsDomeLit(1)).IsTrue();
        await Assert.That(absent).IsEqualTo(SilkLightLinkMasks.All);
    }

    [Test]
    public async Task ATableWhoseDomeMasksAreCompleteReportsNoDomeLinking()
    {
        // HasDomeLinks is what keeps an unlinked scene on the single-group
        // environment bake, so a table that narrows only the direct-light masks
        // must not claim dome linking: doing so would rebuild the environment
        // into a grouped atlas and move the pixels of a scene that links no dome.
        byte[] page = CreateLightLink(
            lightCount: 2,
            entries: [(CubePath, SilkLightLinkCommand.AllInstances, 0b01u, 0b11u)],
            domeCount: 2,
            domeMasks: [0b11u]);

        var scene = new SilkSceneState();
        _ = scene.Apply([.. DomeFrame(2, lightCount: 2), .. page], 2, 1);

        await Assert.That(scene.LightLinks.HasLinks).IsTrue();
        await Assert.That(scene.LightLinks.HasDomeLinks).IsFalse();
        await Assert.That(scene.LightLinks.Resolve(CubePath, 0).DomeMask).IsEqualTo(0b11u);
    }

    [Test]
    public async Task ADomeMaskNamingAnUnpublishedDomeIsRejected()
    {
        byte[] page = CreateLightLink(
            lightCount: 2,
            entries: [(CubePath, SilkLightLinkCommand.AllInstances, 0b01u, 0b01u)],
            domeCount: 1,
            domeMasks: [0b10u]);

        await Assert.That(() => new SilkSceneState().Apply([.. DomeFrame(1, lightCount: 2), .. page], 2, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ALinkTableThatDisagreesWithTheFrameDomeCountIsRejected()
    {
        // The masks index the frame's dome ordering, so a table that claims a
        // different number of domes names a different set of lights. It is
        // refused whole rather than applied against an ordering it was not
        // resolved from.
        byte[] page = CreateLightLink(
            lightCount: 1,
            entries: [(CubePath, SilkLightLinkCommand.AllInstances, 0b1u, 0b1u)],
            domeCount: 1,
            domeMasks: [0u]);

        await Assert.That(() => new SilkSceneState().Apply(
                [.. DomeFrame(2, lightCount: 1), .. page],
                2,
                1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ACanonicalEmptyTableIsAcceptedAgainstAnyFrameDomeTable()
    {
        // Retirement is the one table that indexes nothing at all, so it is valid
        // against any frame: it says "stop masking", not "mask against these".
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [.. DomeFrame(3), .. CreateLightLink(lightCount: 0)],
            2,
            1);

        await Assert.That(scene.Frame.DomeCount).IsEqualTo(3u);
        await Assert.That(scene.LightLinks.HasLinks).IsFalse();
        await Assert.That(scene.LightLinks.Resolve(CubePath, 0))
            .IsEqualTo(SilkLightLinkMasks.All);
    }

    /// <summary>
    /// Builds a frame publishing <paramref name="domeCount"/> textured domes, so
    /// a dome mask has an ordering to index.
    /// </summary>
    private static byte[] DomeFrame(int domeCount, uint lightCount = 0)
    {
        const int frameSize = 23368;
        const int lightCountOffset = 536;
        const int domeCountOffset = 23096;
        const int domeTableOffset = 23112;
        var bytes = new byte[frameSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)frameSize);
        for (int element = 0; element < 16; element++)
        {
            double value = element % 5 == 0 ? 1d : 0d;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (element * 8)), value);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (element * 8)), value);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightCountOffset), lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(domeCountOffset),
            (uint)domeCount);
        for (int dome = 0; dome < domeCount; dome++)
        {
            // OPENUSD_SILK_DOME_FLAG_PRESENT only. These cases are about the mask
            // bit space, not about images: a textured entry is one dome''s image
            // and would require an environment record to supply it.
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(domeTableOffset + (dome * 32) + 16),
                1u);
        }
        return bytes;
    }

    [Test]
    public async Task ATableIndexingMoreDomesThanAFrameCarriesIsRejected()
    {
        byte[] page = CreateLightLink(
            lightCount: 1,
            domeCount: SilkFrameCommand.MaximumDomes + 1);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task TheDomeBudgetOverflowIsRetainedAsAnUnsupportedFeature()
    {
        // A scene over the dome budget publishes no dome bits at all, which is
        // the same wire state as a scene with no dome. The flag is the only thing
        // that distinguishes "there is nothing to mask" from "there was something
        // to mask and it did not fit", so it has to survive into retained state.
        byte[] page = CreateLightLink(
            lightCount: 1,
            unsupported: SilkLightLinkUnsupportedFeatures.DomeBudget,
            entries: [(CubePath, SilkLightLinkCommand.AllInstances, 0u, 0b1u)]);

        var scene = new SilkSceneState();
        _ = scene.Apply([.. DomeFrame(0, lightCount: 1), .. page], 2, 1);

        await Assert.That(scene.LightLinks.UnsupportedFeatures)
            .IsEqualTo(SilkLightLinkUnsupportedFeatures.DomeBudget);
        await Assert.That(scene.LightLinks.DomeCount).IsEqualTo(0u);
        await Assert.That(scene.LightLinks.HasDomeLinks).IsFalse();
    }

    [Test]
    public async Task DirectMasksKeepLowBitsThatFloatEncodingWouldLose()
    {
        // A bounded replacement for the former eight-bit float domain. Each
        // high bit is paired with a low bit that a float conversion would lose.
        foreach (int highBit in new[] { 24, 31, 32, 63, 64, 95, 96, 127 })
        {
            UInt128 light = (UInt128.One << highBit) | UInt128.One;
            UInt128 shadow = (UInt128.One << highBit) | (UInt128)2;
            byte[] constants = new byte[SilkSurfaceUniformWriter.ByteSize];
            SilkSurfaceUniformWriter.Write(
                null, RenderHeadlight.Deterministic, constants, false,
                new SilkLightLinkMasks(light, shadow, 0x81u));
            await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(constants.AsSpan(208)))
                .IsEqualTo(light);
            await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(constants.AsSpan(224)))
                .IsEqualTo(shadow);
            await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(constants.AsSpan(192)))
                .IsEqualTo(129f);
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(constants.AsSpan(76)))
                .IsEqualTo(0u);
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(constants.AsSpan(140)))
                .IsEqualTo(0u);
        }
    }

    [Test]
    public async Task AMaskNamingAnUnpublishedLightIsRejected()
    {
        byte[] page = CreateLightLink(
            lightCount: 2,
            entries: [(CubePath, SilkLightLinkCommand.AllInstances, 0b100u, 0b100u)]);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AShadowBitWithoutItsLightBitIsAccepted()
    {
        // UsdLux resolves collection:lightLink and collection:shadowLink as two
        // separate collections over the same light, so a prim that casts a
        // light's shadow without being lit by it -- an unlit or off-screen
        // blocker that must still occlude other receivers -- is a valid
        // combination. Rejecting it, or intersecting the masks, would silently
        // delete that blocker's shadow.
        byte[] page = CreateLightLink(
            lightCount: 2,
            entries: [(CubePath, SilkLightLinkCommand.AllInstances, 0b01u, 0b10u)]);

        var scene = new SilkSceneState();
        _ = scene.Apply([.. DomeFrame(0, lightCount: 2), .. page], 2, 1);

        SilkLightLinkMasks masks = scene.LightLinks.Resolve(CubePath, 0);
        await Assert.That(masks.LightMask).IsEqualTo(0b01u);
        await Assert.That(masks.ShadowMask).IsEqualTo(0b10u);
        await Assert.That(masks.IsLit(0)).IsTrue();
        await Assert.That(masks.IsLit(1)).IsFalse();
        await Assert.That(masks.CastsShadow(0)).IsFalse();
        await Assert.That(masks.CastsShadow(1)).IsTrue();
    }

    [Test]
    public async Task AnUnlitPrimThatCastsEveryShadowIsAccepted()
    {
        // The extreme of the same rule: a blocker excluded from every light's
        // lightLink collection but included in every shadowLink collection.
        byte[] page = CreateLightLink(
            lightCount: 2,
            entries: [(CubePath, SilkLightLinkCommand.AllInstances, 0u, 0b11u)]);

        var scene = new SilkSceneState();
        _ = scene.Apply([.. DomeFrame(0, lightCount: 2), .. page], 2, 1);

        SilkLightLinkMasks masks = scene.LightLinks.Resolve(CubePath, 0);
        await Assert.That(masks.LightMask).IsEqualTo(0u);
        await Assert.That(masks.ShadowMask).IsEqualTo(0b11u);
    }

    [Test]
    public async Task ATableIndexingMoreLightsThanAFrameCarriesIsRejected()
    {
        byte[] page = CreateLightLink(lightCount: SilkFrameCommand.MaximumLights + 1);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ATruncatedOrOverlongEntryTableIsRejected()
    {
        byte[] page = CreateLightLink(
            lightCount: 2,
            entries: [(CubePath, SilkLightLinkCommand.AllInstances, 0b01u, 0b01u)]);

        byte[] truncated = page[..^1];
        BinaryPrimitives.WriteUInt32LittleEndian(
            truncated.AsSpan(4),
            (uint)truncated.Length);
        byte[] padded = [.. page, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(padded.AsSpan(4), (uint)padded.Length);

        await Assert.That(() => new SilkSceneState().Apply(truncated, 1, 1))
            .Throws<InvalidDataException>();
        await Assert.That(() => new SilkSceneState().Apply(padded, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AnEntryNamingANegativeInstanceOtherThanEveryInstanceIsRejected()
    {
        byte[] page = CreateLightLink(
            lightCount: 2,
            entries: [(CubePath, -2, 0b01u, 0b01u)]);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AnUnknownUnsupportedFeatureBitIsRejected()
    {
        byte[] page = CreateLightLink(lightCount: 2);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(16), 0xFFu);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ATableOverThePageBudgetIsRejected()
    {
        byte[] page = CreateLightLink(lightCount: 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            page.AsSpan(8),
            SilkLightLinkCommand.MaximumEntries + 1);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task TheManagedBudgetMatchesTheNativeOne()
    {
        // The producer bounds the table and the consumer rejects anything past
        // that bound, so the two constants have to be the same number or a
        // legally produced table would be refused.
        string header = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "native",
            "hdSilk",
            "include",
            "openusd_hdsilk.h"));
        System.Text.RegularExpressions.Match match =
            System.Text.RegularExpressions.Regex.Match(
                header,
                @"#define\s+OPENUSD_SILK_MAX_LINK_ENTRIES\s+(?<value>\d+)u",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5));

        await Assert.That(match.Success)
            .IsTrue()
            .Because("The native light link budget was not found.");
        await Assert.That(uint.Parse(
            match.Groups["value"].Value,
            System.Globalization.CultureInfo.InvariantCulture))
            .IsEqualTo(SilkLightLinkCommand.MaximumEntries);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The repository root was not found.");
    }

    private static byte[] CreateLightLink(
        uint lightCount,
        SilkLightLinkUnsupportedFeatures unsupported =
            SilkLightLinkUnsupportedFeatures.None,
        (string Path, int InstanceIndex, UInt128 LightMask, UInt128 ShadowMask)[]? entries = null,
        uint domeCount = 0,
        uint[]? domeMasks = null)
    {
        entries ??= [];
        domeMasks ??= new uint[entries.Length];
        List<byte> payload =
        [
            .. BitConverter.GetBytes((uint)entries.Length),
            .. BitConverter.GetBytes(lightCount),
            .. BitConverter.GetBytes((uint)unsupported),
            .. BitConverter.GetBytes(domeCount),
        ];
        for (int index = 0; index < entries.Length; index++)
        {
            (string path, int instanceIndex, UInt128 lightMask, UInt128 shadowMask) = entries[index];
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            byte[] prefix = new byte[44];
            BinaryPrimitives.WriteUInt128LittleEndian(prefix, lightMask);
            BinaryPrimitives.WriteUInt128LittleEndian(prefix.AsSpan(16), shadowMask);
            BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(32), domeMasks[index]);
            BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(36), instanceIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(40), (uint)pathBytes.Length);
            payload.AddRange(prefix);
            payload.AddRange(pathBytes);
        }

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    [Test]
    public async Task DefaultAndExplicitWideMasksKeepThreeIndependentSpaces()
    {
        SilkLightLinkMasks all = SilkLightLinkMasks.All;
        SilkLightLinkMasks zero = default;
        var table = new SilkLightLinkTable();
        var independent = new SilkLightLinkMasks(
            (UInt128.One << 64) | (UInt128.One << 31),
            (UInt128.One << 127) | (UInt128.One << 32),
            0x81u);

        await Assert.That(SilkLightLinkMasks.AllBits).IsEqualTo(UInt128.MaxValue);
        uint allDomeBits = SilkLightLinkMasks.AllDomeBits;
        await Assert.That(allDomeBits).IsEqualTo(255u);
        await Assert.That(all.LightMask).IsEqualTo(UInt128.MaxValue);
        await Assert.That(all.ShadowMask).IsEqualTo(UInt128.MaxValue);
        await Assert.That(all.DomeMask).IsEqualTo(255u);
        await Assert.That(zero.LightMask).IsEqualTo(UInt128.Zero);
        await Assert.That(zero.ShadowMask).IsEqualTo(UInt128.Zero);
        await Assert.That(zero.DomeMask).IsEqualTo(0u);
        await Assert.That(zero).IsNotEqualTo(all);
        await Assert.That(table.Resolve(CubePath, 7)).IsEqualTo(all);
        await Assert.That(table.Count).IsEqualTo(0);
        await Assert.That(independent.IsLit(64)).IsTrue();
        await Assert.That(independent.IsLit(127)).IsFalse();
        await Assert.That(independent.CastsShadow(127)).IsTrue();
        await Assert.That(independent.CastsShadow(64)).IsFalse();
        await Assert.That(independent.IsDomeLit(0)).IsTrue();
        await Assert.That(independent.IsDomeLit(7)).IsTrue();
        await Assert.That(independent.IsDomeLit(1)).IsFalse();
        await Assert.That(zero.IsLit(0)).IsFalse();
        await Assert.That(zero.CastsShadow(127)).IsFalse();
        await Assert.That(zero.IsDomeLit(0)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(31)]
    [Arguments(32)]
    [Arguments(63)]
    [Arguments(64)]
    [Arguments(95)]
    [Arguments(96)]
    [Arguments(127)]
    public async Task WideMaskMembershipDoesNotAliasWords(int index)
    {
        UInt128 bit = UInt128.One << index;
        var illumination = new SilkLightLinkMasks(bit, UInt128.Zero, 0u);
        var shadow = new SilkLightLinkMasks(UInt128.Zero, bit, 0u);

        // This is bounded by the contract, not by AllBits. It includes both
        // neighbours and every equal bit position in the other three words.
        for (int candidate = 0; candidate < 128; candidate++)
        {
            await Assert.That(illumination.IsLit(candidate)).IsEqualTo(candidate == index);
            await Assert.That(illumination.CastsShadow(candidate)).IsFalse();
            await Assert.That(shadow.CastsShadow(candidate)).IsEqualTo(candidate == index);
            await Assert.That(shadow.IsLit(candidate)).IsFalse();
        }
        await Assert.That(illumination.LightMask).IsEqualTo(bit);
        await Assert.That(shadow.ShadowMask).IsEqualTo(bit);
        await Assert.That(illumination.IsDomeLit(index % 8)).IsFalse();
        await Assert.That(shadow.IsDomeLit(index % 8)).IsFalse();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(-128)]
    [Arguments(int.MinValue)]
    [Arguments(128)]
    [Arguments(129)]
    [Arguments(int.MaxValue)]
    public async Task MaskMembershipGuardsBounds(int index)
    {
        SilkLightLinkMasks masks = SilkLightLinkMasks.All;
        await Assert.That(masks.IsLit(index)).IsFalse();
        await Assert.That(masks.CastsShadow(index)).IsFalse();
        await Assert.That(masks.IsLit(0)).IsTrue();
        await Assert.That(masks.IsLit(127)).IsTrue();
        await Assert.That(masks.CastsShadow(127)).IsTrue();
        await Assert.That(masks.IsDomeLit(7)).IsTrue();
    }

    [Test]
    [Arguments(-1, false)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(6, false)]
    [Arguments(7, true)]
    [Arguments(8, false)]
    [Arguments(127, false)]
    [Arguments(int.MinValue, false)]
    [Arguments(int.MaxValue, false)]
    public async Task DomeMaskMembershipKeepsItsEightBitBounds(int index, bool expected)
    {
        var masks = new SilkLightLinkMasks(UInt128.One << 64, UInt128.One << 127, 0x81u);
        await Assert.That(masks.IsDomeLit(index)).IsEqualTo(expected);
        await Assert.That(masks.IsLit(64)).IsTrue();
        await Assert.That(masks.CastsShadow(127)).IsTrue();
        await Assert.That(masks.IsLit(7)).IsFalse();
        await Assert.That(masks.DomeMask).IsEqualTo(0x81u);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(97)]
    [Arguments(128)]
    [Arguments(129)]
    [Arguments(130)]
    public async Task LinkLightCountPartitions(int count)
    {
        UInt128 last = count == 0 ? UInt128.Zero : UInt128.One << (Math.Min(count, 128) - 1);
        UInt128 shadow = count == 0 ? UInt128.Zero : UInt128.One;
        byte[] page = CreateWideLightLink(
            (uint)count, 8, [(CubePath, -1, last, shadow, 0x81u)]);
        if (count > 128)
        {
            await Assert.That(() => ReadWideLinkTable(page)).Throws<InvalidDataException>();
            SilkLightLinkTable control = ReadWideLinkTable(
                CreateWideLightLink(128, 8, [(CubePath, -1, last, shadow, 0x81u)]));
            await Assert.That(control.LightCount).IsEqualTo(128u);
            await Assert.That(control.Resolve(CubePath, 0).IsLit(127)).IsTrue();
            return;
        }

        SilkLightLinkTable table = ReadWideLinkTable(page);
        SilkLightLinkMasks masks = table.Resolve(CubePath, 3);
        await Assert.That(table.LightCount).IsEqualTo((uint)count);
        await Assert.That(table.DomeCount).IsEqualTo(8u);
        await Assert.That(table.Count).IsEqualTo(1);
        await Assert.That(table.AllDomesMask).IsEqualTo(255u);
        await Assert.That(table.HasDomeLinks).IsTrue();
        await Assert.That(masks.LightMask).IsEqualTo(last);
        await Assert.That(masks.ShadowMask).IsEqualTo(shadow);
        await Assert.That(masks.DomeMask).IsEqualTo(0x81u);
        await Assert.That(masks.CastsShadow(0)).IsEqualTo(count != 0);
        await Assert.That(table.Resolve(SpherePath, 3)).IsEqualTo(SilkLightLinkMasks.All);
        if (count > 0)
        {
            await Assert.That(masks.IsLit(count - 1)).IsTrue();
        }
        if (count < 128)
        {
            await Assert.That(masks.IsLit(count)).IsFalse();
        }
    }

    [Test]
    public async Task WideEntriesRoundTripIndependentFieldsAndUtf8Bytes()
    {
        const string unicodePath = "/World/灯/模型";
        byte[] utf8 =
        [
            0x2F, 0x57, 0x6F, 0x72, 0x6C, 0x64, 0x2F,
            0xE7, 0x81, 0xAF, 0x2F, 0xE6, 0xA8, 0xA1, 0xE5, 0x9E, 0x8B,
        ];
        byte[] secondPath = "/World/Other"u8.ToArray();
        UInt128 light = new(0x7654_3210_FEDC_BA98ul, 0x89AB_CDEF_0123_4567ul);
        UInt128 shadow = new(0x4B5A_6978_0F1E_2D3Cul, 0x2468_ACE0_1357_9BDFul);
        UInt128 secondLight = (UInt128.One << 127) | UInt128.One;
        UInt128 secondShadow = (UInt128.One << 96) | (UInt128.One << 64);

        // An independent literal layout, not the helper and reader agreeing on
        // an entry-size error: 24 + 44 + 17 + 44 + 12 is exactly 141 bytes.
        byte[] page = new byte[141];
        BinaryPrimitives.WriteUInt32LittleEndian(page, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(4), 141u);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(8), 2u);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(12), 128u);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(16), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(20), 8u);
        uint[] words =
        [
            0x0123_4567u, 0x89AB_CDEFu, 0xFEDC_BA98u, 0x7654_3210u,
            0x1357_9BDFu, 0x2468_ACE0u, 0x0F1E_2D3Cu, 0x4B5A_6978u,
        ];
        for (int word = 0; word < words.Length; word++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(24 + (word * 4)), words[word]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(56), 0xA5u);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(60), -1);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(64), 17u);
        utf8.CopyTo(page, 68);
        BinaryPrimitives.WriteUInt128LittleEndian(page.AsSpan(85), secondLight);
        BinaryPrimitives.WriteUInt128LittleEndian(page.AsSpan(101), secondShadow);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(117), 0x42u);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(121), 37);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(125), 12u);
        secondPath.CopyTo(page, 129);

        uint entryCount;
        uint lightCount;
        uint domeCount;
        SilkLightLinkUnsupportedFeatures unsupported;
        List<SilkLightLinkEntry> entries = [];
        bool finished;
        {
            using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(page, 1, 24u);
            _ = commands.MoveNext();
            SilkLightLinkCommand command = commands.Current.AsLightLink();
            entryCount = command.EntryCount;
            lightCount = command.LightCount;
            domeCount = command.DomeCount;
            unsupported = command.UnsupportedFeatures;
            foreach (SilkLightLinkEntry entry in command)
            {
                entries.Add(entry);
            }
            finished = !commands.MoveNext();
        }

        await Assert.That(entryCount).IsEqualTo(2u);
        await Assert.That(lightCount).IsEqualTo(128u);
        await Assert.That(domeCount).IsEqualTo(8u);
        await Assert.That(unsupported).IsEqualTo(SilkLightLinkUnsupportedFeatures.Truncated);
        await Assert.That(finished).IsTrue();
        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries[0].Path).IsEqualTo(unicodePath);
        await Assert.That(entries[0].InstanceIndex).IsEqualTo(-1);
        await Assert.That(entries[0].LightMask).IsEqualTo(light);
        await Assert.That(entries[0].ShadowMask).IsEqualTo(shadow);
        await Assert.That(entries[0].DomeMask).IsEqualTo(0xA5u);
        await Assert.That(entries[1].Path).IsEqualTo("/World/Other");
        await Assert.That(entries[1].InstanceIndex).IsEqualTo(37);
        await Assert.That(entries[1].LightMask).IsEqualTo(secondLight);
        await Assert.That(entries[1].ShadowMask).IsEqualTo(secondShadow);
        await Assert.That(entries[1].DomeMask).IsEqualTo(0x42u);
        await Assert.That(Encoding.UTF8.GetByteCount(entries[0].Path)).IsEqualTo(17);
        await Assert.That(entries[0].Path.Length).IsEqualTo(11);

        byte[] encoded = CreateWideLightLink(128, 8,
        [
            (unicodePath, -1, light, shadow, 0xA5u),
            ("/World/Other", 37, secondLight, secondShadow, 0x42u),
        ], SilkLightLinkUnsupportedFeatures.Truncated);
        await Assert.That(encoded.Length).IsEqualTo(141);
        await Assert.That(encoded.AsSpan().SequenceEqual(page)).IsTrue()
            .Because("The encoder must agree with the literal 24/44-byte wire oracle.");
        await Assert.That(encoded.AsSpan(68, 17).SequenceEqual(utf8)).IsTrue();
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(125)))
            .IsEqualTo(12u);

        SilkLightLinkTable table = ReadWideLinkTable(page);
        await Assert.That(table.Resolve(unicodePath, 37))
            .IsEqualTo(new SilkLightLinkMasks(light, shadow, 0xA5u));
        await Assert.That(table.Resolve("/World/Other", 37))
            .IsEqualTo(new SilkLightLinkMasks(secondLight, secondShadow, 0x42u));
        await Assert.That(table.Resolve("/World/Other", 38)).IsEqualTo(SilkLightLinkMasks.All);
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(8, false)]
    [Arguments(8, true)]
    [Arguments(9, false)]
    [Arguments(9, true)]
    [Arguments(31, false)]
    [Arguments(31, true)]
    [Arguments(32, false)]
    [Arguments(32, true)]
    [Arguments(33, false)]
    [Arguments(33, true)]
    [Arguments(63, false)]
    [Arguments(63, true)]
    [Arguments(64, false)]
    [Arguments(64, true)]
    [Arguments(65, false)]
    [Arguments(65, true)]
    [Arguments(95, false)]
    [Arguments(95, true)]
    [Arguments(96, false)]
    [Arguments(96, true)]
    [Arguments(97, false)]
    [Arguments(97, true)]
    [Arguments(127, false)]
    [Arguments(127, true)]
    public async Task WideEntryRejectsEachUnpublishedBit(int count, bool shadowSpace)
    {
        UInt128 published = count == 0 ? UInt128.Zero : UInt128.One << (count - 1);
        UInt128 unpublished = UInt128.One << count;
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. CreateWideLinkFrame(count, 0),
                .. CreateWideLightLink((uint)count, 0,
                    [(CubePath, -1, published, published, 0u)]),
            ], 2, 11);
        SilkLightLinkMasks before = scene.LightLinks.Resolve(CubePath, 5);
        ulong linkRevision = scene.LightLinks.Revision;
        ulong sceneRevision = scene.Revision;
        await Assert.That(before)
            .IsEqualTo(new SilkLightLinkMasks(published, published, 0u));

        byte[] precedingValid = CreateWideLightLink((uint)count, 0,
            [("/World/BeforeFailure", -1, UInt128.Zero, UInt128.Zero, 0u)]);
        byte[] invalid = CreateWideLightLink((uint)count, 0,
        [
            (CubePath, -1, published, published, 0u),
            (SpherePath, 3,
                shadowSpace ? published : unpublished,
                shadowSpace ? unpublished : published,
                0u),
        ]);

        await Assert.That(() => scene.Apply([.. precedingValid, .. invalid], 2, 12))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Revision).IsEqualTo(sceneRevision);
        await Assert.That(scene.LightLinks.Revision).IsEqualTo(linkRevision);
        await Assert.That(scene.LightLinks.Count).IsEqualTo(1);
        await Assert.That(scene.LightLinks.Resolve(CubePath, 5)).IsEqualTo(before);
        await Assert.That(scene.LightLinks.Resolve("/World/BeforeFailure", 0))
            .IsEqualTo(SilkLightLinkMasks.All);
        await Assert.That(scene.LightLinks.Resolve(SpherePath, 3))
            .IsEqualTo(SilkLightLinkMasks.All);
        await Assert.That(scene.Frame.LightCount).IsEqualTo((uint)count);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task Full128BitTableDoesNotWrapMaskLimit(int spaces)
    {
        UInt128 light = (spaces & 1) != 0 ? UInt128.MaxValue : UInt128.Zero;
        UInt128 shadow = (spaces & 2) != 0 ? UInt128.MaxValue : UInt128.Zero;
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. CreateWideLinkFrame(128, 8),
                .. CreateWideLightLink(128, 8, [(CubePath, -1, light, shadow, 0x81u)]),
            ], 2, 1);
        SilkLightLinkMasks masks = scene.LightLinks.Resolve(CubePath, 4);

        await Assert.That(scene.LightLinks.LightCount).IsEqualTo(128u);
        await Assert.That(scene.LightLinks.Count).IsEqualTo(1);
        await Assert.That(masks.LightMask).IsEqualTo(light);
        await Assert.That(masks.ShadowMask).IsEqualTo(shadow);
        await Assert.That(masks.DomeMask).IsEqualTo(0x81u);
        for (int index = 0; index < 128; index++)
        {
            await Assert.That(masks.IsLit(index)).IsEqualTo((spaces & 1) != 0);
            await Assert.That(masks.CastsShadow(index)).IsEqualTo((spaces & 2) != 0);
        }
        await Assert.That(masks.IsDomeLit(1)).IsFalse();
        await Assert.That(scene.LightLinks.Resolve(SpherePath, 4))
            .IsEqualTo(SilkLightLinkMasks.All);
    }

    [Test]
    public async Task HighWordInstanceOverridesPreservePathFallbackAndIndependentShadows()
    {
        var pathMasks = new SilkLightLinkMasks(
            (UInt128.One << 96) | (UInt128.One << 31),
            (UInt128.One << 127) | (UInt128.One << 32), 0x81u);
        var instanceMasks = new SilkLightLinkMasks(
            UInt128.Zero, (UInt128.One << 127) | (UInt128.One << 64), 0x02u);
        var otherMasks = new SilkLightLinkMasks(UInt128.One << 64, UInt128.One << 96, 0x40u);
        SilkLightLinkTable table = ReadWideLinkTable(CreateWideLightLink(128, 8,
        [
            (CubePath, -1, pathMasks.LightMask, pathMasks.ShadowMask, pathMasks.DomeMask),
            (CubePath, 7, instanceMasks.LightMask, instanceMasks.ShadowMask, instanceMasks.DomeMask),
            (SpherePath, 5, otherMasks.LightMask, otherMasks.ShadowMask, otherMasks.DomeMask),
        ]));

        await Assert.That(table.Count).IsEqualTo(3);
        await Assert.That(table.LightCount).IsEqualTo(128u);
        await Assert.That(table.DomeCount).IsEqualTo(8u);
        await Assert.That(table.Resolve(CubePath, 7)).IsEqualTo(instanceMasks);
        await Assert.That(table.Resolve(CubePath, 8)).IsEqualTo(pathMasks);
        await Assert.That(table.Resolve(CubePath, -1)).IsEqualTo(pathMasks);
        await Assert.That(table.Resolve(SpherePath, 5)).IsEqualTo(otherMasks);
        await Assert.That(table.Resolve(SpherePath, 6)).IsEqualTo(SilkLightLinkMasks.All);
        await Assert.That(table.Resolve("/World/Geom/cube", 7)).IsEqualTo(SilkLightLinkMasks.All);
        await Assert.That(table.Resolve("/World/Missing", 7)).IsEqualTo(SilkLightLinkMasks.All);
        await Assert.That(table.Resolve(CubePath, 7).IsLit(127)).IsFalse();
        await Assert.That(table.Resolve(CubePath, 7).CastsShadow(127)).IsTrue();
        await Assert.That(table.Resolve(CubePath, 7).CastsShadow(64)).IsTrue();
        await Assert.That(table.Resolve(CubePath, 7).IsDomeLit(1)).IsTrue();
        await Assert.That(table.Resolve(CubePath, 8).IsDomeLit(1)).IsFalse();
    }

    [Test]
    public async Task CanonicalEmptyWideLinksRetireEveryHighWord()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. CreateWideLinkFrame(128, 8),
                .. CreateWideLightLink(128, 8,
                [
                    (CubePath, -1, UInt128.One << 96, UInt128.One << 127, 0x81u),
                    (CubePath, 7, UInt128.One << 64, UInt128.One << 95, 0x02u),
                ], SilkLightLinkUnsupportedFeatures.Truncated),
            ], 2, 1);
        await Assert.That(scene.LightLinks.Count).IsEqualTo(2);
        await Assert.That(scene.LightLinks.Resolve(CubePath, 7).IsLit(64)).IsTrue();
        ulong revision = scene.LightLinks.Revision;

        _ = scene.Apply(CreateWideLightLink(0, 0, []), 1, 2);
        await Assert.That(scene.LightLinks.Count).IsEqualTo(0);
        await Assert.That(scene.LightLinks.IsCanonicalEmpty).IsTrue();
        await Assert.That(scene.LightLinks.HasLinks).IsFalse();
        await Assert.That(scene.LightLinks.HasDomeLinks).IsFalse();
        await Assert.That(scene.LightLinks.AllDomesMask).IsEqualTo(0u);
        await Assert.That(scene.LightLinks.UnsupportedFeatures)
            .IsEqualTo(SilkLightLinkUnsupportedFeatures.None);
        await Assert.That(scene.LightLinks.Revision).IsEqualTo(revision + 1);
        await Assert.That(scene.LightLinks.Resolve(CubePath, 7)).IsEqualTo(SilkLightLinkMasks.All);
        await Assert.That(scene.LightLinks.Resolve(CubePath, 8)).IsEqualTo(SilkLightLinkMasks.All);
        await Assert.That(scene.Frame.LightCount).IsEqualTo(128u);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(8u);
    }

    [Test]
    public async Task CollectMasksKeepsWholeValues()
    {
        var first = new SilkLightLinkMasks(
            (UInt128.One << 64) | 3, (UInt128.One << 96) | 5, 1);
        var highLight = first with { LightMask = (UInt128.One << 127) | 3 };
        var highShadow = first with { ShadowMask = (UInt128.One << 127) | 5 };
        var differentDome = first with { DomeMask = 0x80 };
        SilkLightLinkTable table = ReadWideLinkTable(CreateWideLightLink(128, 8,
        [
            (CubePath, -1, first.LightMask, first.ShadowMask, first.DomeMask),
            (CubePath, 7, highLight.LightMask, highLight.ShadowMask, highLight.DomeMask),
            (SpherePath, -1, highShadow.LightMask, highShadow.ShadowMask, highShadow.DomeMask),
            (SpherePath, 8, differentDome.LightMask, differentDome.ShadowMask, differentDome.DomeMask),
            ("/World/EqualValue", -1, first.LightMask, first.ShadowMask, first.DomeMask),
        ]));
        var stale = new SilkLightLinkMasks(0, 0, 0);
        HashSet<SilkLightLinkMasks> actual = [stale];
        ulong revision = table.Revision;

        table.CollectMasks(actual);

        SilkLightLinkMasks[] expected = [SilkLightLinkMasks.All, first, highLight, highShadow, differentDome];
        await Assert.That(actual.SetEquals(expected)).IsTrue()
            .Because("the live set must clear stale entries, preserve every word, " +
                "include sparse All and deduplicate full values");
        await Assert.That(actual.Count).IsEqualTo(5);
        await Assert.That(actual.Contains(stale)).IsFalse();
        await Assert.That(table.Count).IsEqualTo(5);
        await Assert.That(table.Revision).IsEqualTo(revision);
        await Assert.That(table.Resolve(CubePath, 7)).IsEqualTo(highLight);
        await Assert.That(table.Resolve(CubePath, 8)).IsEqualTo(first);
        await Assert.That(table.Resolve(SpherePath, 8)).IsEqualTo(differentDome);
        await Assert.That(() => table.CollectMasks(null!)).Throws<ArgumentNullException>();
        await Assert.That(table.Revision).IsEqualTo(revision);

        UpdateWideLinkTable(table, CreateWideLightLink(0, 0, []));
        table.CollectMasks(actual);
        await Assert.That(actual.SetEquals([SilkLightLinkMasks.All])).IsTrue();
        await Assert.That(table.Count).IsEqualTo(0);
        await Assert.That(table.Revision).IsEqualTo(revision + 1);
    }

    private static SilkLightLinkTable ReadWideLinkTable(byte[] page)
    {
        var table = new SilkLightLinkTable();
        UpdateWideLinkTable(table, page);
        return table;
    }

    private static void UpdateWideLinkTable(SilkLightLinkTable table, byte[] page)
    {
        using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(page, 1, 24u);
        if (!commands.MoveNext())
        {
            throw new InvalidDataException("The wide light-link command is missing.");
        }
        table.Update(commands.Current.AsLightLink());
        if (commands.MoveNext())
        {
            throw new InvalidDataException("The link fixture contains an extra command.");
        }
    }

    private static byte[] CreateWideLightLink(
        uint lightCount,
        uint domeCount,
        (string Path, int InstanceIndex, UInt128 LightMask, UInt128 ShadowMask, uint DomeMask)[] entries,
        SilkLightLinkUnsupportedFeatures unsupported = SilkLightLinkUnsupportedFeatures.None)
    {
        byte[][] paths = entries.Select(static entry => Encoding.UTF8.GetBytes(entry.Path)).ToArray();
        byte[] bytes = new byte[24 + paths.Sum(static path => 44 + path.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)unsupported);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), domeCount);
        int offset = 24;
        for (int index = 0; index < entries.Length; index++)
        {
            var (_, instance, light, shadow, dome) = entries[index];
            BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(offset), light);
            BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(offset + 16), shadow);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 32), dome);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 36), instance);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 40), (uint)paths[index].Length);
            paths[index].CopyTo(bytes, offset + 44);
            offset += 44 + paths[index].Length;
        }
        return bytes;
    }

    private static byte[] CreateWideLinkFrame(int lightCount, int domeCount)
    {
        byte[] bytes = new byte[23368];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 23368u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 160);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 128);
        for (int element = 0; element < 16; element++)
        {
            double value = element % 5 == 0 ? 1d : 0d;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (element * 8)), value);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (element * 8)), value);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(536), (uint)lightCount);
        for (int light = 0; light < lightCount; light++)
        {
            int entry = 552 + (light * 176);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4), 1u);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 16), 0.25f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 20), 0.5f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 24), 0.75f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 28), light + 1f);
            for (int element = 0; element < 16; element++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(
                    bytes.AsSpan(entry + 32 + (element * 8)), element % 5 == 0 ? 1d : 0d);
            }
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 164), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(entry + 168), 1f);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(23096), (uint)domeCount);
        for (int dome = 0; dome < domeCount; dome++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(23112 + (dome * 32) + 16), 1u);
        }
        return bytes;
    }

    [Test]
    [Arguments("header-truncated")]
    [Arguments("entry-prefix-truncated")]
    [Arguments("path-truncated")]
    [Arguments("trailing-byte")]
    [Arguments("entry-count-too-small")]
    [Arguments("entry-count-too-large")]
    [Arguments("instance-negative")]
    [Arguments("instance-minimum")]
    [Arguments("unknown-feature")]
    [Arguments("path-length-zero")]
    [Arguments("path-length-over-limit")]
    [Arguments("path-length-short")]
    [Arguments("path-length-past-page")]
    [Arguments("relative-path")]
    [Arguments("nul-path")]
    [Arguments("invalid-utf8")]
    public async Task WideLinkMalformedPayloadsAreRejectedAtTheirOwnOffsets(string defect)
    {
        byte[] page = CreateWideLightLink(128, 8,
            [(CubePath, -1, UInt128.One << 96, UInt128.One << 127, 0x81u)]);
        SilkLightLinkTable control = ReadWideLinkTable(page);
        await Assert.That(control.Count).IsEqualTo(1);
        await Assert.That(control.Resolve(CubePath, 2).IsLit(96)).IsTrue();
        await Assert.That(control.Resolve(CubePath, 2).CastsShadow(127)).IsTrue();
        await Assert.That(control.Resolve(CubePath, 2).DomeMask).IsEqualTo(0x81u);

        switch (defect)
        {
            case "header-truncated":
                page = page[..23];
                break;
            case "entry-prefix-truncated":
                page = page[..67];
                break;
            case "path-truncated":
                page = page[..^1];
                break;
            case "trailing-byte":
                page = [.. page, 0];
                break;
            case "entry-count-too-small":
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(8), 0u);
                break;
            case "entry-count-too-large":
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(8), 2u);
                break;
            case "instance-negative":
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(60), -2);
                break;
            case "instance-minimum":
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(60), int.MinValue);
                break;
            case "unknown-feature":
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(16), 4u);
                break;
            case "path-length-zero":
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(64), 0u);
                break;
            case "path-length-over-limit":
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(64), uint.MaxValue);
                break;
            case "path-length-short":
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(64), 1u);
                break;
            case "path-length-past-page":
                BinaryPrimitives.WriteUInt32LittleEndian(
                    page.AsSpan(64), (uint)Encoding.UTF8.GetByteCount(CubePath) + 1);
                break;
            case "relative-path":
                page[68] = (byte)'W';
                break;
            case "nul-path":
                page[70] = 0;
                break;
            case "invalid-utf8":
                page[69] = 0xFF;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(defect));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(4), (uint)page.Length);

        await Assert.That(() => ReadWideLinkTable(page)).Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(0, 0u, true)]
    [Arguments(0, 1u, false)]
    [Arguments(1, 1u, true)]
    [Arguments(1, 2u, false)]
    [Arguments(7, 127u, true)]
    [Arguments(7, 128u, false)]
    [Arguments(8, 255u, true)]
    [Arguments(8, 256u, false)]
    [Arguments(9, 0u, false)]
    public async Task WideLinkDomeCountAndMasksStayEightBit(int domeCount, uint domeMask, bool accepted)
    {
        byte[] page = CreateWideLightLink(128, (uint)domeCount,
            [(CubePath, -1, UInt128.One << 127, UInt128.One << 32, domeMask)]);
        if (!accepted)
        {
            await Assert.That(() => ReadWideLinkTable(page)).Throws<InvalidDataException>();
            SilkLightLinkTable control = ReadWideLinkTable(CreateWideLightLink(128, 8,
                [(CubePath, -1, UInt128.One << 127, UInt128.One << 32, 255u)]));
            await Assert.That(control.Resolve(CubePath, 0).IsDomeLit(7)).IsTrue();
            await Assert.That(control.Resolve(CubePath, 0).IsLit(127)).IsTrue();
            return;
        }

        SilkLightLinkTable table = ReadWideLinkTable(page);
        SilkLightLinkMasks masks = table.Resolve(CubePath, 0);
        await Assert.That(table.DomeCount).IsEqualTo((uint)domeCount);
        await Assert.That(table.AllDomesMask).IsEqualTo(domeMask);
        await Assert.That(table.HasDomeLinks).IsFalse();
        await Assert.That(masks.DomeMask).IsEqualTo(domeMask);
        await Assert.That(masks.IsLit(127)).IsTrue();
        await Assert.That(masks.CastsShadow(32)).IsTrue();
        await Assert.That(masks.CastsShadow(127)).IsFalse();
    }

    [Test]
    [Arguments(4096)]
    [Arguments(4097)]
    public async Task WideLinkEntryBudgetAccepts4096AndRejects4097(int entryCount)
    {
        var entries =
            new (string Path, int InstanceIndex, UInt128 LightMask, UInt128 ShadowMask, uint DomeMask)[entryCount];
        for (int index = 0; index < entryCount; index++)
        {
            entries[index] = ($"/World/Entry{index}", -1, UInt128.One << 96, UInt128.One << 127, 0u);
        }
        byte[] page = CreateWideLightLink(128, 0, entries);
        uint maximumEntries = SilkLightLinkCommand.MaximumEntries;
        await Assert.That(maximumEntries).IsEqualTo(4096u);
        if (entryCount == 4097)
        {
            await Assert.That(() => ReadWideLinkTable(page)).Throws<InvalidDataException>();
            SilkLightLinkTable control = ReadWideLinkTable(CreateWideLightLink(128, 0, entries[..4096]));
            await Assert.That(control.Count).IsEqualTo(4096);
            await Assert.That(control.Resolve("/World/Entry4095", 0).CastsShadow(127)).IsTrue();
            return;
        }

        SilkLightLinkTable table = ReadWideLinkTable(page);
        await Assert.That(table.Count).IsEqualTo(4096);
        await Assert.That(table.Resolve("/World/Entry0", 0).IsLit(96)).IsTrue();
        await Assert.That(table.Resolve("/World/Entry4095", 0))
            .IsEqualTo(new SilkLightLinkMasks(UInt128.One << 96, UInt128.One << 127, 0u));
        await Assert.That(table.Resolve("/World/Entry4096", 0)).IsEqualTo(SilkLightLinkMasks.All);
    }

    [Test]
    [Arguments(0u)]
    [Arguments(1u)]
    [Arguments(2u)]
    [Arguments(3u)]
    public async Task WideKnownUnsupportedFeaturesRemainIndependentOfHighMasks(uint flags)
    {
        SilkLightLinkTable table = ReadWideLinkTable(CreateWideLightLink(128, 0,
            [(CubePath, -1, UInt128.Zero, UInt128.One << 127, 0u)],
            (SilkLightLinkUnsupportedFeatures)flags));

        await Assert.That(table.UnsupportedFeatures).IsEqualTo((SilkLightLinkUnsupportedFeatures)flags);
        await Assert.That(table.Count).IsEqualTo(1);
        await Assert.That(table.DomeCount).IsEqualTo(0u);
        await Assert.That(table.HasDomeLinks).IsFalse();
        await Assert.That(table.Resolve(CubePath, 0).IsLit(127)).IsFalse();
        await Assert.That(table.Resolve(CubePath, 0).CastsShadow(127)).IsTrue();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(7)]
    public async Task WideLinkReplacementKeepsLastFullValueForEachExactKey(int instance)
    {
        SilkLightLinkTable table = ReadWideLinkTable(CreateWideLightLink(128, 8,
            [("/World/Retired", -1, UInt128.One << 64, UInt128.One << 96, 0x81u)]));
        ulong revision = table.Revision;
        var last = new SilkLightLinkMasks(UInt128.One << 127, UInt128.One << 95, 0x02u);
        UpdateWideLinkTable(table, CreateWideLightLink(128, 8,
        [
            (CubePath, instance, UInt128.One << 64, UInt128.One << 96, 0x01u),
            (SpherePath, -1, UInt128.One << 31, UInt128.One << 32, 0x40u),
            (CubePath, instance, last.LightMask, last.ShadowMask, last.DomeMask),
        ]));

        await Assert.That(table.Count).IsEqualTo(2);
        await Assert.That(table.Revision).IsEqualTo(revision + 1);
        await Assert.That(table.Resolve(CubePath, 7)).IsEqualTo(last);
        await Assert.That(table.Resolve(CubePath, 8))
            .IsEqualTo(instance == -1 ? last : SilkLightLinkMasks.All);
        await Assert.That(table.Resolve("/World/Retired", 0)).IsEqualTo(SilkLightLinkMasks.All);
        await Assert.That(table.Resolve(SpherePath, 7))
            .IsEqualTo(new SilkLightLinkMasks(UInt128.One << 31, UInt128.One << 32, 0x40u));
    }

    [Test]
    public async Task WideSparseResolutionRejectsNullWithoutChangingTheTable()
    {
        SilkLightLinkTable table = ReadWideLinkTable(CreateWideLightLink(128, 0,
            [(CubePath, -1, UInt128.One << 96, UInt128.One << 127, 0u)]));
        ulong revision = table.Revision;

        await Assert.That(() => table.Resolve(null!, 0)).Throws<ArgumentNullException>();
        await Assert.That(table.Count).IsEqualTo(1);
        await Assert.That(table.Revision).IsEqualTo(revision);
        await Assert.That(table.Resolve(CubePath, 0))
            .IsEqualTo(new SilkLightLinkMasks(UInt128.One << 96, UInt128.One << 127, 0u));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    public async Task SurfaceWritesRawWideMasksAndZeroesLegacySlots(int maskCase)
    {
        (UInt128 light, UInt128 shadow, uint dome) = maskCase switch
        {
            0 => (UInt128.Zero, UInt128.Zero, 0u),
            1 or 7 => (UInt128.MaxValue, UInt128.MaxValue, 255u),
            2 => (UInt128.One << 127, UInt128.Zero, 1u),
            3 => (UInt128.Zero, UInt128.One << 96, 128u),
            4 => (new UInt128(0x7654_3210_FEDC_BA98ul, 0x89AB_CDEF_0123_4567ul),
                new UInt128(0x4B5A_6978_0F1E_2D3Cul, 0x2468_ACE0_1357_9BDFul), 0x42u),
            5 => (new UInt128(1ul, 0x0102_0304ul),
                new UInt128(0x0000_0001_0000_0000ul, 0x0506_0708ul), 0x81u),
            6 => (new UInt128(0x8000_0000_0000_0000ul, 0x0102_0304ul),
                new UInt128(1ul, 0x0506_0708ul), 0x81u),
            8 => (UInt128.MaxValue, UInt128.Zero, 0x1FFu),
            _ => throw new ArgumentOutOfRangeException(nameof(maskCase)),
        };
        uint[] expectedWords = maskCase switch
        {
            0 => [0, 0, 0, 0, 0, 0, 0, 0],
            1 or 7 =>
            [
                uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue,
                uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue,
            ],
            2 => [0, 0, 0, 0x8000_0000u, 0, 0, 0, 0],
            3 => [0, 0, 0, 0, 0, 0, 0, 1],
            4 =>
            [
                0x0123_4567u, 0x89AB_CDEFu, 0xFEDC_BA98u, 0x7654_3210u,
                0x1357_9BDFu, 0x2468_ACE0u, 0x0F1E_2D3Cu, 0x4B5A_6978u,
            ],
            5 => [0x0102_0304u, 0, 1, 0, 0x0506_0708u, 0, 0, 1],
            6 => [0x0102_0304u, 0, 0, 0x8000_0000u, 0x0506_0708u, 0, 1, 0],
            8 => [uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, 0, 0, 0, 0],
            _ => throw new ArgumentOutOfRangeException(nameof(maskCase)),
        };
        byte[] storage = Enumerable.Repeat((byte)0xCD, 242).ToArray();
        var headlight = new RenderHeadlight(
            new System.Numerics.Vector3(0, 3, -4), 2.5f,
            new System.Numerics.Vector3(0.125f, 0.375f, 0.875f), 0.625f);
        SilkLightLinkMasks? masks = maskCase == 7 ? null : new SilkLightLinkMasks(light, shadow, dome);
        SilkSurfaceUniformWriter.Write(
            material: null, headlight, storage.AsSpan(1, 240),
            supportsVolumeTextures: false, linkMasks: masks);
        byte[] actual = storage[1..241];
        float[] expectedPrefix =
        [
            0.18f, 0.18f, 0.18f, 1f,
            0f, 0f, 0f, 1f,
            0f, 0f, 0f, 1.5f,
            0f, 0.5f, 0f, 0f,
            0f, 0.01f, 0f, 0f,
            0f, 0.6f, -0.8f, 2.5f,
            0.125f, 0.375f, 0.875f, 0.625f,
            0f, 0f, 2f, 0f,
            0f, 0f, 0f, 0f,
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 0f, 0f,
            dome & 255u, 0f, 0f, 0f,
        ];

        int surfaceSize = SilkSurfaceUniformWriter.ByteSize;
        await Assert.That(surfaceSize).IsEqualTo(240);
        await Assert.That(storage[0]).IsEqualTo((byte)0xCD);
        await Assert.That(storage[241]).IsEqualTo((byte)0xCD);
        for (int component = 0; component < expectedPrefix.Length; component++)
        {
            await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(actual.AsSpan(component * 4)))
                .IsEqualTo(expectedPrefix[component])
                .Because($"The unchanged surface prefix owns float {component}, not a direct mask word.");
        }
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(76))).IsEqualTo(0u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(140))).IsEqualTo(0u);
        await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(actual.AsSpan(208))).IsEqualTo(light);
        await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(actual.AsSpan(224))).IsEqualTo(shadow);
        for (int word = 0; word < 8; word++)
        {
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(208 + (word * 4))))
                .IsEqualTo(expectedWords[word]);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SurfaceHighWordsNeverAliasMatchingLowWords(bool shadowSpace)
    {
        UInt128 lowLight = 0x0123_4567u;
        UInt128 lowShadow = 0x1357_9BDFu;
        var firstMasks = new SilkLightLinkMasks(
            lowLight | (UInt128.One << (shadowSpace ? 127 : 64)),
            lowShadow | (UInt128.One << (shadowSpace ? 64 : 127)), 0x42u);
        var secondMasks = new SilkLightLinkMasks(
            lowLight | (UInt128.One << (shadowSpace ? 127 : 96)),
            lowShadow | (UInt128.One << (shadowSpace ? 96 : 127)), 0x42u);
        byte[] first = Enumerable.Repeat((byte)0xA5, 240).ToArray();
        byte[] second = Enumerable.Repeat((byte)0x5A, 240).ToArray();
        SilkSurfaceUniformWriter.Write(
            null, RenderHeadlight.Deterministic, first, false, firstMasks);
        SilkSurfaceUniformWriter.Write(
            null, RenderHeadlight.Deterministic, second, false, secondMasks);

        await Assert.That(first.AsSpan().SequenceEqual(second)).IsFalse();
        await Assert.That(first.AsSpan(0, 208).SequenceEqual(second.AsSpan(0, 208))).IsTrue();
        await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(first.AsSpan(208)))
            .IsEqualTo(firstMasks.LightMask);
        await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(second.AsSpan(208)))
            .IsEqualTo(secondMasks.LightMask);
        await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(first.AsSpan(224)))
            .IsEqualTo(firstMasks.ShadowMask);
        await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(second.AsSpan(224)))
            .IsEqualTo(secondMasks.ShadowMask);
        int offset = shadowSpace ? 224 : 208;
        List<int> differences = [];
        for (int index = 0; index < 240; index++)
        {
            if (first[index] != second[index])
            {
                differences.Add(index);
            }
        }
        await Assert.That(differences.Count).IsEqualTo(2);
        await Assert.That(differences[0]).IsEqualTo(offset + 8);
        await Assert.That(differences[1]).IsEqualTo(offset + 12);
        await Assert.That(first[offset + 8]).IsEqualTo((byte)1);
        await Assert.That(second[offset + 8]).IsEqualTo((byte)0);
        await Assert.That(first[offset + 12]).IsEqualTo((byte)0);
        await Assert.That(second[offset + 12]).IsEqualTo((byte)1);
    }

    [Test]
    [Arguments(0)]
    [Arguments(208)]
    [Arguments(239)]
    [Arguments(241)]
    public async Task SurfaceWriterRejectsNonExactWideDestinations(int length)
    {
        var masks = new SilkLightLinkMasks(UInt128.One << 96, UInt128.One << 127, 0x81u);
        byte[] invalid = Enumerable.Repeat((byte)0xCD, length).ToArray();
        await Assert.That(() => SilkSurfaceUniformWriter.Write(
                null, RenderHeadlight.Deterministic, invalid, false, masks))
            .Throws<ArgumentException>();
        await Assert.That(invalid.All(static value => value == 0xCD)).IsTrue();

        byte[] control = new byte[240];
        SilkSurfaceUniformWriter.Write(null, RenderHeadlight.Deterministic, control, false, masks);
        await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(control.AsSpan(208)))
            .IsEqualTo(UInt128.One << 96);
        await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(control.AsSpan(224)))
            .IsEqualTo(UInt128.One << 127);
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(control.AsSpan(192))).IsEqualTo(129f);
    }

    [Test]
    public async Task DomeMaskFloatEncodingIsBoundedAndExactForAllEightBitValues()
    {
        UInt128 light = (UInt128.One << 64) | (UInt128.One << 96);
        UInt128 shadow = (UInt128.One << 127) | (UInt128.One << 32);
        byte[] actual = new byte[240];
        for (int dome = 0; dome <= 255; dome++)
        {
            Array.Fill(actual, (byte)0xCD);
            SilkSurfaceUniformWriter.Write(
                null, RenderHeadlight.Deterministic, actual, false,
                new SilkLightLinkMasks(light, shadow, (uint)dome));
            await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(actual.AsSpan(192)))
                .IsEqualTo((float)dome);
            await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(actual.AsSpan(208)))
                .IsEqualTo(light);
            await Assert.That(BinaryPrimitives.ReadUInt128LittleEndian(actual.AsSpan(224)))
                .IsEqualTo(shadow);
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(76)))
                .IsEqualTo(0u);
            await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(140)))
                .IsEqualTo(0u);
        }
    }
}
