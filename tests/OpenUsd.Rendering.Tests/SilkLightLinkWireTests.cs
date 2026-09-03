// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the page ABI 18 UsdLux light and shadow link table: its wire layout,
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
/// naming a light the frame never published, or a shadow bit for a light the
/// prim is not linked to, describes a scene the producer cannot have meant and
/// must be rejected rather than retained and drawn.
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

        float linkedLightMask = BinaryPrimitives.ReadSingleLittleEndian(linked.AsSpan(76, 4));
        float linkedShadowMask = BinaryPrimitives.ReadSingleLittleEndian(linked.AsSpan(140, 4));
        float linkedDomeMask = BinaryPrimitives.ReadSingleLittleEndian(linked.AsSpan(192, 4));
        float unlinkedLightMask = BinaryPrimitives.ReadSingleLittleEndian(unlinked.AsSpan(76, 4));
        float unlinkedShadowMask = BinaryPrimitives.ReadSingleLittleEndian(unlinked.AsSpan(140, 4));
        float unlinkedDomeMask = BinaryPrimitives.ReadSingleLittleEndian(unlinked.AsSpan(192, 4));

        await Assert.That(linkedLightMask).IsEqualTo(6f);
        await Assert.That(linkedShadowMask).IsEqualTo(2f);
        await Assert.That(linkedDomeMask).IsEqualTo(3f);
        await Assert.That(unlinkedLightMask).IsEqualTo(255f);
        await Assert.That(unlinkedShadowMask).IsEqualTo(255f);
        await Assert.That(unlinkedDomeMask).IsEqualTo(255f);

        // Everything else in the block is identical, so the mask is the only
        // reason a second block exists.
        int differences = 0;
        for (int index = 0; index < linked.Length; index++)
        {
            if (linked[index] != unlinked[index])
            {
                differences++;
            }
        }
        await Assert.That(differences).IsLessThanOrEqualTo(12);
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
        const int frameSize = 2248;
        const int lightCountOffset = 536;
        const int domeCountOffset = 1976;
        const int domeTableOffset = 1992;
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
    public async Task MasksAreExactlyRepresentableAsTheFloatTheShaderReads()
    {
        // The shader recovers the mask with a float-to-uint conversion, so every
        // value the eight-bit mask can take must survive the round trip exactly.
        // A value that rounded would silently mask the wrong lights.
        List<uint> recovered = [];
        for (uint mask = 0; mask <= SilkLightLinkMasks.AllBits; mask++)
        {
            recovered.Add((uint)(float)mask);
        }

        for (uint mask = 0; mask <= SilkLightLinkMasks.AllBits; mask++)
        {
            await Assert.That(recovered[(int)mask]).IsEqualTo(mask);
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
        (string Path, int InstanceIndex, uint LightMask, uint ShadowMask)[]? entries = null,
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
            (string path, int instanceIndex, uint lightMask, uint shadowMask) = entries[index];
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            payload.AddRange(BitConverter.GetBytes(lightMask));
            payload.AddRange(BitConverter.GetBytes(shadowMask));
            payload.AddRange(BitConverter.GetBytes(domeMasks[index]));
            payload.AddRange(BitConverter.GetBytes(instanceIndex));
            payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
            payload.AddRange(pathBytes);
        }

        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }
}
