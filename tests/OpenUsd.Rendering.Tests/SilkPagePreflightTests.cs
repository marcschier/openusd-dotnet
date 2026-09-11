// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the whole-page preflight: a page either applies completely or changes
/// nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// Constructing a command view is what validates it, and the mutating pass used
/// to construct them one at a time as it applied them -- so a page whose fourth
/// command was malformed retained the first three and then threw, leaving the
/// scene in a state no producer ever published. Every case here builds a page
/// whose <em>later</em> commands are the invalid ones and requires the earlier,
/// perfectly valid ones to have changed nothing.
/// </para>
/// <para>
/// The cross-command cases are the other half. The frame's dome table is the
/// authority the other two commands index: a <c>LIGHT_LINK</c> whose dome count
/// disagrees with it names a different set of domes, and an <c>ENVIRONMENT</c>
/// record whose <c>dome_index</c> is not a present textured dome names an entry
/// that does not exist. A page whose commands disagree describes no scene, and
/// applying part of it would light prims from domes the frame never published.
/// </para>
/// </remarks>
public sealed class SilkPagePreflightTests
{
    private const string MeshPath = "/World/Geom/Quad";
    private const string DomePath = "/World/Lights/Dome";
    private const string MaterialPath = "/World/Materials/Surface";

    [Test]
    public async Task AMalformedTrailingCommandLeavesEveryEarlierCommandUnapplied()
    {
        var scene = new SilkSceneState();
        byte[] malformed = SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
            DomePath,
            "/assets/sky.hdr");

        // A non-finite intensity, which no producer publishes and which would
        // reach the prefilter as a NaN sky.
        BinaryPrimitives.WriteSingleLittleEndian(malformed.AsSpan(52), float.NaN);

        await Assert.That(() => scene.Apply(
                [.. Frame(domeCount: 0), .. Mesh(), .. malformed],
                3,
                1))
            .Throws<InvalidDataException>();

        await Assert.That(scene.Meshes.Count)
            .IsEqualTo(0)
            .Because("The mesh preceded the malformed record and must not be retained.");
        await Assert.That(scene.Environments.Count).IsEqualTo(0);
        await Assert.That(scene.Frame.Revision)
            .IsEqualTo(0UL)
            .Because("The frame preceded it too, and is equally unapplied.");
        await Assert.That(scene.Revision).IsEqualTo(0UL);
    }

    [Test]
    public async Task AMalformedMaterialLeavesTheEarlierMeshUnretained()
    {
        var scene = new SilkSceneState();
        byte[] material = Material();

        // A scalar table that claims five entries and carries none: the material
        // command view walks both tables in its constructor, so the claim fails
        // there -- and the mesh before it must not survive the refusal.
        BinaryPrimitives.WriteUInt32LittleEndian(material.AsSpan(24), 5u);

        await Assert.That(() => scene.Apply([.. Mesh(), .. material], 2, 1))
            .Throws<InvalidDataException>();

        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(scene.Materials.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AnEnvironmentClaimingAnUnpublishedDomeIsRefusedWhole()
    {
        var scene = new SilkSceneState();

        await Assert.That(() => scene.Apply(
                [
                    .. Frame(domeCount: 1),
                    .. Mesh(),
                    .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                        DomePath,
                        "/assets/sky.hdr",
                        domeIndex: 1),
                ],
                3,
                1))
            .Throws<InvalidDataException>();

        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(0u);
    }

    [Test]
    public async Task AnEnvironmentClaimingAnUntexturedDomeIsRefused()
    {
        // The dome table publishes the entry, but as an untextured dome: its
        // whole contribution is an ambient colour, and no environment record can
        // belong to it. Accepting the claim would give one dome bit both an image
        // and an ambient term and light the scene twice from it.
        var scene = new SilkSceneState();

        await Assert.That(() => scene.Apply(
                [
                    .. Frame(domeCount: 1, textured: 0),
                    .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                        DomePath,
                        "/assets/sky.hdr",
                        domeIndex: 0),
                ],
                2,
                1))
            .Throws<InvalidDataException>();

        await Assert.That(scene.Environments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ADomeIndexAgainstTheRetainedFrameIsResolvedWhenThePageOmitsAFrame()
    {
        // A delta page that carries no frame is resolved against the retained
        // one, because the frame dome table is the authority whether or not this
        // page republished it. The frame publishes one textured dome and one
        // untextured one, so the record below has an entry to claim and the
        // untextured entry needs none.
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Frame(domeCount: 2, textured: 1),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    DomePath,
                    "/assets/sky.hdr",
                    domeIndex: 0),
            ],
            2,
            1);
        await Assert.That(scene.Environments.Count).IsEqualTo(1);

        await Assert.That(() => scene.Apply(
                SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    "/World/Lights/Other",
                    "/assets/sky.hdr",
                    domeIndex: 5),
                1,
                3))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Environments.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AFrameDomeEntryPastThePublishedCountMustBeZeroed()
    {
        byte[] page = Frame(domeCount: 1, textured: 0);

        // The fixed table's tail carries the flags of a dome the frame does not
        // publish. A reader that trusted the count would never see it; one that
        // trusted the entry would light a prim from a dome that does not exist.
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(23112 + 32 + 16), 1u);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task APublishedFrameDomeEntryMustBeMarkedPresent()
    {
        byte[] page = Frame(domeCount: 1, textured: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(23112 + 16), 2u);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AFrameDomeEntryWithANonFiniteAmbientIsRefused()
    {
        byte[] page = Frame(domeCount: 1, textured: 0);
        BinaryPrimitives.WriteSingleLittleEndian(page.AsSpan(23112), float.PositiveInfinity);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AnEnvironmentReservedFieldOrUnknownFlagIsRefused()
    {
        byte[] reserved = SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
            DomePath,
            "/assets/sky.hdr");
        BinaryPrimitives.WriteUInt32LittleEndian(reserved.AsSpan(68), 7u);

        byte[] unknownFlag = SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
            DomePath,
            "/assets/sky.hdr");
        BinaryPrimitives.WriteUInt32LittleEndian(unknownFlag.AsSpan(32), 0x40u);

        byte[] nonFiniteTransform = SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
            DomePath,
            "/assets/sky.hdr");
        BinaryPrimitives.WriteDoubleLittleEndian(
            nonFiniteTransform.AsSpan(72),
            double.NaN);

        await Assert.That(() => new SilkSceneState().Apply(reserved, 1, 1))
            .Throws<InvalidDataException>();
        await Assert.That(() => new SilkSceneState().Apply(unknownFlag, 1, 1))
            .Throws<InvalidDataException>();
        await Assert.That(() => new SilkSceneState().Apply(nonFiniteTransform, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AnEnvironmentWithNoDomeIndexIsRefusedOnceTheFrameHasADomeTable()
    {
        // Every textured dome the frame publishes has an entry in the dome table
        // by construction, so a record that declines to name one was resolved
        // against a different ordering than the table was. Accepting it hands
        // that dome's sky to every prim, including the ones whose collection
        // excludes it -- silently, because the mask has no bit to clear.
        var scene = new SilkSceneState();

        await Assert.That(() => scene.Apply(
                [
                    .. Frame(domeCount: 1),
                    .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                        DomePath,
                        "/assets/sky.hdr"),
                ],
                2,
                1))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Environments.Count).IsEqualTo(0);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(0u);
    }

    [Test]
    public async Task AnEnvironmentWithNoDomeIndexIsAcceptedWhileTheFrameHasNoDomeTable()
    {
        // The other half of the same rule, and the one every pre-ABI-21 producer
        // relies on: with no dome table there is nothing to index, so an
        // unindexed record is the only correct thing to publish.
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Frame(domeCount: 0),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    DomePath,
                    "/assets/sky.hdr"),
            ],
            2,
            1);

        await Assert.That(scene.Environments.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ANoncanonicalLightLinkMustIndexTheFramesLightCount()
    {
        // The light mask bit i names frame light i. A table resolved against a
        // different light count names different lights, and the entry that
        // excluded a key light would silently exclude another one.
        var scene = new SilkSceneState();

        await Assert.That(() => scene.Apply(
                [.. Frame(domeCount: 0, lightCount: 2), .. LightLink(lightCount: 1)],
                2,
                1))
            .Throws<InvalidDataException>();
        await Assert.That(scene.LightLinks.HasLinks).IsFalse();
        await Assert.That(scene.Frame.LightCount).IsEqualTo(0u);

        _ = scene.Apply(
            [.. Frame(domeCount: 0, lightCount: 2), .. LightLink(lightCount: 2)],
            2,
            2);
        await Assert.That(scene.LightLinks.LightCount).IsEqualTo(2u);
    }

    [Test]
    public async Task ANoncanonicalLightLinkMustIndexTheFramesDomeCount()
    {
        var scene = new SilkSceneState();

        await Assert.That(() => scene.Apply(
                [.. Frame(domeCount: 2, textured: 0), .. LightLink(domeCount: 1)],
                2,
                1))
            .Throws<InvalidDataException>();
        await Assert.That(scene.LightLinks.HasLinks).IsFalse();
    }

    [Test]
    public async Task TheCanonicalEmptyLightLinkRetiresAgainstAnyFrame()
    {
        // Retirement is the one table that indexes nothing, so it is valid
        // against every frame -- including one that publishes lights and domes
        // the retired table never described.
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Frame(domeCount: 2, textured: 0, lightCount: 2),
                .. LightLink(lightCount: 2, domeCount: 2),
            ],
            2,
            1);
        await Assert.That(scene.LightLinks.HasLinks).IsTrue();

        _ = scene.Apply(LightLink(entryCount: 0), 1, 2);
        await Assert.That(scene.LightLinks.HasLinks).IsFalse();
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(2u);
    }

    [Test]
    public async Task AFrameOnlyPageMustAgreeWithTheRetainedLightLinkTable()
    {
        // A frame command changes the ordering the *retained* masks index, so a
        // page that carries nothing else still has to be checked against the
        // table it leaves in place. Validating only what the page carries let a
        // camera update silently reinterpret every retained mask against a
        // different set of lights.
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [.. Frame(domeCount: 0, lightCount: 2), .. LightLink(lightCount: 2)],
            2,
            1);
        await Assert.That(scene.LightLinks.LightCount).IsEqualTo(2u);

        // Growing the light count.
        await Assert.That(() => scene.Apply(Frame(domeCount: 0, lightCount: 3), 1, 2))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Frame.LightCount).IsEqualTo(2u);

        // And shrinking it.
        await Assert.That(() => scene.Apply(Frame(domeCount: 0, lightCount: 1), 1, 3))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Frame.LightCount).IsEqualTo(2u);

        // A page that republishes the table alongside the new frame is the
        // correct shape, and is accepted.
        _ = scene.Apply(
            [.. Frame(domeCount: 0, lightCount: 3), .. LightLink(lightCount: 3)],
            2,
            4);
        await Assert.That(scene.Frame.LightCount).IsEqualTo(3u);
        await Assert.That(scene.LightLinks.LightCount).IsEqualTo(3u);
    }

    [Test]
    public async Task AFrameOnlyPageMustAgreeWithTheRetainedDomeCount()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [.. Frame(domeCount: 2, textured: 0), .. LightLink(domeCount: 2)],
            2,
            1);

        await Assert.That(() => scene.Apply(Frame(domeCount: 1, textured: 0), 1, 2))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(2u);

        await Assert.That(() => scene.Apply(Frame(domeCount: 3, textured: 0), 1, 3))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(2u);

        // Retiring the table first is what makes the frame free to change: a
        // canonical empty table indexes nothing and is valid against any frame.
        _ = scene.Apply(LightLink(entryCount: 0), 1, 4);
        _ = scene.Apply(Frame(domeCount: 3, textured: 0), 1, 5);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(3u);
    }

    [Test]
    public async Task AFrameOnlyPageMustAgreeWithEveryRetainedEnvironmentDomeIndex()
    {
        // The retained records point at entries of the frame dome table, and a
        // frame command republishes that table. A page that moves a dome out of
        // it, or that turns one untextured, leaves a record naming an entry that
        // no longer means what it did.
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Frame(domeCount: 2),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    DomePath,
                    "/assets/first.hdr",
                    domeIndex: 0),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    "/World/Lights/Second",
                    "/assets/second.hdr",
                    domeIndex: 1),
            ],
            3,
            1);

        // Shrinking the table past the second record's index.
        await Assert.That(() => scene.Apply(Frame(domeCount: 1), 1, 2))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(2u);

        // Turning the second record's dome untextured, which is the entry an
        // environment record may never belong to.
        await Assert.That(() => scene.Apply(Frame(domeCount: 2, textured: 1), 1, 3))
            .Throws<InvalidDataException>();

        // Retiring the dome table entirely while indexed records are retained.
        await Assert.That(() => scene.Apply(Frame(domeCount: 0), 1, 4))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Environments.Count).IsEqualTo(2);

        // Removing both records in the same page is what makes the frame free to
        // retire the table.
        _ = scene.Apply(
            [
                .. Frame(domeCount: 0),
                .. EnvironmentRemove(DomePath),
                .. EnvironmentRemove("/World/Lights/Second"),
            ],
            3,
            5);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(0u);
        await Assert.That(scene.Environments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AFrameThatPublishesADomeTableRefusesARetainedUnindexedRecord()
    {
        // The other direction: a record legitimately published with no dome index
        // while the frame had no dome table, and a later frame publishes one. The
        // record now belongs to a dome nobody can mask, so the page is refused
        // rather than handing that dome's sky to every prim.
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Frame(domeCount: 0),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    DomePath,
                    "/assets/sky.hdr"),
            ],
            2,
            1);
        await Assert.That(scene.Environments[DomePath].HasDomeIndex).IsFalse();

        await Assert.That(() => scene.Apply(Frame(domeCount: 1), 1, 2))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(0u);

        // Republishing the record with its index alongside the new frame is the
        // correct shape.
        _ = scene.Apply(
            [
                .. Frame(domeCount: 1),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    DomePath,
                    "/assets/sky.hdr",
                    domeIndex: 0),
            ],
            2,
            3);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(1u);
        await Assert.That(scene.Environments[DomePath].DomeIndex).IsEqualTo(0u);
    }

    [Test]
    public async Task ATexturedFrameDomeWithNoEnvironmentRecordIsRefused()
    {
        // A textured entry of the frame dome table *is* one dome's image, so a
        // page that publishes one with no record to supply it describes a dome
        // the renderer has no sky for and no prim can be excluded from.
        var scene = new SilkSceneState();

        await Assert.That(() => scene.Apply(Frame(domeCount: 1), 1, 1))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(0u);

        _ = scene.Apply(
            [
                .. Frame(domeCount: 1),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    DomePath,
                    "/assets/sky.hdr",
                    domeIndex: 0),
            ],
            2,
            2);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(1u);
    }

    [Test]
    public async Task RemovingTheOnlyRecordOfATexturedDomeIsRefused()
    {
        // The mapping is resolved from the state the page leaves behind, so a
        // removal that strands a textured dome is refused exactly as a frame that
        // publishes one with no record is.
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Frame(domeCount: 1),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    DomePath,
                    "/assets/sky.hdr",
                    domeIndex: 0),
            ],
            2,
            1);

        await Assert.That(() => scene.Apply(EnvironmentRemove(DomePath), 1, 2))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Environments.Count).IsEqualTo(1);

        // Retiring the dome table in the same page is what makes the removal
        // describe a complete scene again.
        _ = scene.Apply(
            [.. Frame(domeCount: 0), .. EnvironmentRemove(DomePath)],
            2,
            3);
        await Assert.That(scene.Environments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TwoRecordsClaimingOneFrameDomeIndexAreRefused()
    {
        // The mapping is a bijection: a duplicate claim makes one dome's mask
        // bit select the other dome's sky, which is not a rendering that any
        // authored collection describes.
        var scene = new SilkSceneState();

        await Assert.That(() => scene.Apply(
                [
                    .. Frame(domeCount: 1),
                    .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                        DomePath,
                        "/assets/a.hdr",
                        domeIndex: 0),
                    .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                        "/World/Lights/Other",
                        "/assets/b.hdr",
                        domeIndex: 0),
                ],
                3,
                1))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Environments.Count).IsEqualTo(0);

        // The same duplicate against the *retained* set is refused too: the
        // second record is published by a later page that cannot see the first.
        _ = scene.Apply(
            [
                .. Frame(domeCount: 1),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    DomePath,
                    "/assets/a.hdr",
                    domeIndex: 0),
            ],
            2,
            2);
        await Assert.That(() => scene.Apply(
                SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    "/World/Lights/Other",
                    "/assets/b.hdr",
                    domeIndex: 0),
                1,
                3))
            .Throws<InvalidDataException>();
        await Assert.That(scene.Environments.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ARecordSupersededByALaterOneOnTheSamePathIsNotValidated()
    {
        // Only the final shape of each path is a state the renderer will ever
        // resolve, so a record a later command on the same path replaces is not
        // a claim on anything. Validating every command instead would refuse a
        // page whose net effect is perfectly consistent.
        var scene = new SilkSceneState();

        _ = scene.Apply(
            [
                .. Frame(domeCount: 1),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    DomePath,
                    "/assets/stale.hdr",
                    domeIndex: 7),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    DomePath,
                    "/assets/sky.hdr",
                    domeIndex: 0),
            ],
            3,
            1);

        await Assert.That(scene.Environments.Count).IsEqualTo(1);
        await Assert.That(scene.Environments[DomePath].DomeIndex).IsEqualTo(0u);
        await Assert.That(scene.Environments[DomePath].TexturePath)
            .IsEqualTo("/assets/sky.hdr");
    }

    [Test]
    public async Task MoreEnvironmentCommandsThanTheDomeBudgetAreBoundedByPath()
    {
        // The number of commands bounds nothing: a page may republish the same
        // dome any number of times, and a fixed span indexed once per command
        // overran on the ninth. What is bounded is the number of distinct paths
        // that survive the page, which is what the mapping is keyed on.
        var scene = new SilkSceneState();
        var page = new List<byte[]> { Frame(domeCount: 1) };
        for (int index = 0; index < 12; index++)
        {
            page.Add(SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                DomePath,
                $"/assets/sky{index}.hdr",
                domeIndex: 0));
        }

        _ = scene.Apply(
            [.. page.SelectMany(static command => command)],
            (uint)page.Count,
            1);

        await Assert.That(scene.Environments.Count).IsEqualTo(1);
        await Assert.That(scene.Environments[DomePath].TexturePath)
            .IsEqualTo("/assets/sky11.hdr");
        await Assert.That(scene.Environments[DomePath].DomeIndex).IsEqualTo(0u);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(1u);
    }

    /// <summary>Retires one retained environment record.</summary>
    private static byte[] EnvironmentRemove(string path)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        var bytes = new byte[20 + pathBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.EnvironmentRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkEnvironmentLightingTests.ComputeStableHash(path));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes, 20);
        return bytes;
    }

    /// <summary>Builds one light link table over <c>MeshPath</c>.</summary>
    private static byte[] LightLink(
        uint lightCount = 0,
        uint domeCount = 0,
        uint entryCount = 1)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MeshPath);
        int entrySize = entryCount == 0 ? 0 : 44 + pathBytes.Length;
        var bytes = new byte[24 + entrySize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), entryCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), domeCount);
        if (entryCount == 0)
        {
            return bytes;
        }

        UInt128 lightMask = lightCount >= 128 ? UInt128.MaxValue : (UInt128.One << (int)lightCount) - 1;
        uint domeMask = domeCount >= 32 ? uint.MaxValue : (1u << (int)domeCount) - 1;
        BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(24), lightMask);
        BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(40), lightMask);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), domeMask == 0 ? 0u : 1u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(60), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes, 68);
        return bytes;
    }

    private static byte[] Frame(int domeCount, int textured = int.MaxValue, uint lightCount = 0)
    {
        const int frameSize = 23368;
        const int domeCountOffset = 23096;
        const int domeTableOffset = 23112;
        var bytes = new byte[frameSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)frameSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 4);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 4);
        for (int element = 0; element < 16; element++)
        {
            double value = element % 5 == 0 ? 1d : 0d;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (element * 8)), value);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (element * 8)), value);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(536), lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(domeCountOffset),
            (uint)domeCount);
        for (int dome = 0; dome < domeCount; dome++)
        {
            // OPENUSD_SILK_DOME_FLAG_PRESENT, plus TEXTURED for the domes that
            // publish an environment record.
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(domeTableOffset + (dome * 32) + 16),
                dome < textured ? 3u : 1u);
        }
        return bytes;
    }

    private static byte[] Mesh()
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MeshPath);
        float[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        uint[] indices = [0, 1, 2];
        int size = 268 +
            pathBytes.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkEnvironmentLightingTests.ComputeStableHash(MeshPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (component * 4)), 1f);
        }
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (element * 8)),
                element % 5 == 0 ? 1 : 0);
        }
        pathBytes.CopyTo(bytes, 268);
        int offset = 268 + pathBytes.Length;
        foreach (float value in points)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
            offset += sizeof(float);
        }
        foreach (uint index in indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), index);
            offset += sizeof(uint);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 0);
        return bytes;
    }

    private static byte[] Material()
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MaterialPath);
        List<byte> payload =
        [
            .. BitConverter.GetBytes(
                SilkEnvironmentLightingTests.ComputeStableHash(MaterialPath)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. BitConverter.GetBytes((uint)SilkSurfaceKind.PreviewSurface),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. pathBytes,
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
        ];
        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    [Test]
    [Arguments("high-light")]
    [Arguments("high-shadow")]
    [Arguments("entry-prefix-truncated")]
    [Arguments("path-truncated")]
    [Arguments("frame-overcount")]
    [Arguments("link-overcount")]
    [Arguments("entry-overcount")]
    [Arguments("frame-short")]
    [Arguments("link-long")]
    [Arguments("page-count")]
    [Arguments("retained-shadow-count")]
    [Arguments("shadow-frame-count")]
    [Arguments("nonfinite-high-light")]
    [Arguments("unrepresentable-high-transform")]
    public async Task InvalidWidePageIsRefusedBeforeMutation(string kind)
    {
        var scene = new SilkSceneState();
        UInt128 high96 = UInt128.One << 96;
        UInt128 high127 = UInt128.One << 127;
        _ = scene.Apply(
            [
                .. WideFrame(128, 2),
                .. Mesh(),
                .. Material(),
                .. WideLinks(128, 2,
                    (MeshPath, -1, high96 | 5, high127 | 2, 1u),
                    (MeshPath, 0, high127 | 1, high96 | 4, 2u)),
                .. WideShadow(128, 127),
            ], 5, 41);
        WideState before = CaptureWideState(scene);
        SilkMeshData retainedMesh = scene.MeshesByPath[(MeshPath, 0)];
        SilkMaterialData retainedMaterial = scene.Materials[MaterialPath];
        await Assert.That(scene.Frame.Lights[127].Color)
            .IsEqualTo(new System.Numerics.Vector3(1, 2, 4));
        await Assert.That(scene.LightLinks.Resolve(MeshPath, 0))
            .IsEqualTo(new SilkLightLinkMasks(high127 | 1, high96 | 4, 2));
        await Assert.That(scene.Shadows.ResolveSlot(127)).IsEqualTo(0);

        byte[] frame = WideFrame(97, 2, width: 640, flags: 0);
        byte[] links = WideLinks(97, 2,
            (MeshPath, -1, high96 | 1, high96 | 2, 2u),
            (MeshPath, 0, high96 | 4, high96 | 8, 1u));
        uint commands = 3;
        byte[] shadow = WideShadow(97, 96);
        switch (kind)
        {
            case "high-light":
                BinaryPrimitives.WriteUInt128LittleEndian(links.AsSpan(24), UInt128.One << 97);
                break;
            case "high-shadow":
                BinaryPrimitives.WriteUInt128LittleEndian(links.AsSpan(40), UInt128.One << 97);
                break;
            case "entry-prefix-truncated":
                links = links[..67]; // Header 24 plus only 43 of the 44 prefix bytes.
                BinaryPrimitives.WriteUInt32LittleEndian(links.AsSpan(4), (uint)links.Length);
                break;
            case "path-truncated":
                links = links[..^1];
                BinaryPrimitives.WriteUInt32LittleEndian(links.AsSpan(4), (uint)links.Length);
                break;
            case "frame-overcount":
                BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(536), 129u);
                break;
            case "link-overcount":
                BinaryPrimitives.WriteUInt32LittleEndian(links.AsSpan(12), 129u);
                break;
            case "entry-overcount":
                BinaryPrimitives.WriteUInt32LittleEndian(links.AsSpan(8), 3u);
                break;
            case "frame-short":
                frame = frame[..^1];
                BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), (uint)frame.Length);
                break;
            case "link-long":
                links = [.. links, 0];
                BinaryPrimitives.WriteUInt32LittleEndian(links.AsSpan(4), (uint)links.Length);
                break;
            case "page-count":
                commands = 4;
                break;
            case "retained-shadow-count":
                shadow = [];
                commands = 2;
                break;
            case "shadow-frame-count":
                shadow = WideShadow(128, 96);
                commands = 3;
                break;
            case "nonfinite-high-light":
                BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(552 + (96 * 176) + 16), float.NaN);
                break;
            case "unrepresentable-high-transform":
                BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(552 + (96 * 176) + 32), double.MaxValue);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        await Assert.That(() => scene.Apply([.. frame, .. links, .. shadow], commands, 42))
            .Throws<InvalidDataException>();

        await AssertWideStateUnchanged(scene, before);
        await Assert.That(scene.MeshesByPath[(MeshPath, 0)]).IsSameReferenceAs(retainedMesh);
        await Assert.That(scene.Materials[MaterialPath]).IsSameReferenceAs(retainedMaterial);
        await Assert.That(scene.LightLinks.Resolve(MeshPath, 17))
            .IsEqualTo(new SilkLightLinkMasks(high96 | 5, high127 | 2, 1));
        await Assert.That(scene.LightLinks.Resolve(MeshPath, 0))
            .IsEqualTo(new SilkLightLinkMasks(high127 | 1, high96 | 4, 2));
        await Assert.That(scene.LightLinks.UnsupportedFeatures)
            .IsEqualTo(SilkLightLinkUnsupportedFeatures.Truncated);
        await Assert.That(scene.Shadows.Descriptors[0].View[12]).IsEqualTo(7d);
        await Assert.That(scene.Shadows.Descriptors[0].Projection[5]).IsEqualTo(4d);
    }

    [Test]
    [Arguments("links-first")]
    [Arguments("links-between")]
    [Arguments("links-last")]
    public async Task EffectiveFinalFrameAndRetainedWideLinksMustAgree(string order)
    {
        var scene = new SilkSceneState();
        UInt128 high96 = UInt128.One << 96;
        UInt128 high127 = UInt128.One << 127;
        _ = scene.Apply(
            [
                .. WideFrame(97, 1),
                .. WideLinks(97, 1, (MeshPath, -1, high96 | 1, UInt128.One << 64, 1u)),
            ], 2, 1);
        WideState before = CaptureWideState(scene);

        // Each mismatch is a different effective retained-state boundary. No
        // replacement link command is available to repair any of these pages.
        foreach (byte[] mismatch in new[]
        {
            WideFrame(96, 1), WideFrame(128, 1),
            WideFrame(97, 0), WideFrame(97, 2),
        })
        {
            await Assert.That(() => scene.Apply(mismatch, 1, 2)).Throws<InvalidDataException>();
            await AssertWideStateUnchanged(scene, before);
        }

        ulong linkRevision = scene.LightLinks.Revision;
        _ = scene.Apply(WideFrame(97, 1, width: 321), 1, 3);
        await Assert.That(scene.Frame.Width).IsEqualTo(321);
        await Assert.That(scene.Frame.Revision).IsEqualTo(before.Revisions[5] + 1);
        await Assert.That(scene.LightLinks).IsSameReferenceAs(before.Links);
        await Assert.That(scene.LightLinks.Revision).IsEqualTo(linkRevision);
        await Assert.That(scene.LightLinks.Resolve(MeshPath, 12))
            .IsEqualTo(new SilkLightLinkMasks(high96 | 1, UInt128.One << 64, 1));

        byte[] first = WideFrame(96, 1, width: 322);
        byte[] final = WideFrame(128, 2, width: 640);
        byte[] finalLinks = WideLinks(128, 2,
            (MeshPath, -1, high127 | 3, high96 | 2, 2u));
        byte[] page = order switch
        {
            "links-first" => [.. finalLinks, .. first, .. final],
            "links-between" => [.. first, .. finalLinks, .. final],
            "links-last" => [.. first, .. final, .. finalLinks],
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
        ulong frameRevision = scene.Frame.Revision;
        _ = scene.Apply(page, 3, 4);

        await Assert.That(scene.Frame.LightCount).IsEqualTo(128u);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(2u);
        await Assert.That(scene.Frame.Width).IsEqualTo(640);
        await Assert.That(scene.Frame.Revision).IsEqualTo(frameRevision + 2);
        await Assert.That(scene.LightLinks.Revision).IsEqualTo(linkRevision + 1);
        await Assert.That(scene.LightLinks.LightCount).IsEqualTo(128u);
        await Assert.That(scene.LightLinks.DomeCount).IsEqualTo(2u);
        await Assert.That(scene.LightLinks.Resolve(MeshPath, 12))
            .IsEqualTo(new SilkLightLinkMasks(high127 | 3, high96 | 2, 2));
        await Assert.That(scene.Revision).IsEqualTo(4UL);

        WideState accepted = CaptureWideState(scene);
        // Reversing which frame is last must reverse acceptance, even though
        // the first frame and the table are now perfectly compatible.
        await Assert.That(() => scene.Apply(
                [.. final, .. finalLinks, .. WideFrame(97, 2)], 3, 5))
            .Throws<InvalidDataException>();
        await AssertWideStateUnchanged(scene, accepted);
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(97, 1)]
    [Arguments(128, 8)]
    public async Task CanonicalEmptyWideLinksRetireAgainstAnyFrame(int lights, int domes)
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. WideFrame(128, 8),
                .. WideLinks(128, 8,
                    (MeshPath, -1, UInt128.One << 127, UInt128.One << 96, 0x80u)),
            ], 2, 1);
        SilkLightLinkTable retained = scene.LightLinks;
        ulong revision = retained.Revision;
        _ = scene.Apply(
            [.. WideLinks(0, 0), .. WideFrame(lights, domes)], 2, 2);

        await Assert.That(scene.LightLinks).IsSameReferenceAs(retained);
        await Assert.That(retained.Revision).IsEqualTo(revision + 1);
        await Assert.That(retained.Count).IsEqualTo(0);
        await Assert.That(retained.LightCount).IsEqualTo(0u);
        await Assert.That(retained.DomeCount).IsEqualTo(0u);
        await Assert.That(retained.IsCanonicalEmpty).IsTrue();
        await Assert.That(retained.HasDomeLinks).IsFalse();
        await Assert.That(retained.Resolve(MeshPath, 0))
            .IsEqualTo(new SilkLightLinkMasks(UInt128.MaxValue, UInt128.MaxValue, 255));
        await Assert.That(scene.Frame.LightCount).IsEqualTo((uint)lights);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo((uint)domes);

        // Zero entries alone is NOT retirement. A nonzero count still indexes
        // the frame ordering even when every lookup happens to fall back.
        _ = scene.Apply([.. WideFrame(128, 8), .. WideLinks(128, 8)], 2, 3);
        await Assert.That(retained.HasLinks).IsFalse();
        await Assert.That(retained.IsCanonicalEmpty).IsFalse();
        await Assert.That(retained.LightCount).IsEqualTo(128u);
        await Assert.That(retained.DomeCount).IsEqualTo(8u);
        WideState before = CaptureWideState(scene);
        await Assert.That(() => scene.Apply(WideFrame(127, 8), 1, 4))
            .Throws<InvalidDataException>();
        await AssertWideStateUnchanged(scene, before);
        await Assert.That(() => scene.Apply(WideFrame(128, 7), 1, 5))
            .Throws<InvalidDataException>();
        await AssertWideStateUnchanged(scene, before);
    }

    private sealed record WideState(
        SilkFrameState Frame,
        SilkLightLinkTable Links,
        SilkShadowTable Shadows,
        IReadOnlyList<SilkShadowDescriptor> Descriptors,
        ulong[] Revisions,
        double[] View,
        double[] Projection,
        double[] ClipPlanes,
        SilkFrameLight[] Lights,
        SilkFrameDome[] Domes,
        System.Numerics.Vector4 Ambient,
        byte[] Gpu,
        int Width,
        int Height,
        uint LightCount,
        uint DomeCount,
        uint LinkLights,
        uint LinkDomes,
        int LinkCount,
        SilkLightLinkUnsupportedFeatures LinkFeatures,
        bool HasDomeLinks,
        SilkLightLinkMasks PathMasks,
        SilkLightLinkMasks InstanceMasks,
        SilkShadowDescriptor[] ShadowValues,
        uint ShadowLights,
        SilkShadowUnsupportedFeatures ShadowFeatures,
        int MeshCount,
        int MaterialCount,
        int EnvironmentCount,
        int PickRanges,
        ulong PickAllocated);

    private static WideState CaptureWideState(SilkSceneState scene)
    {
        var bytes = new byte[15296];
        SilkShadowFrameBinding binding = scene.Shadows.HasShadows
            ? SilkShadowFrameBinding.Create(scene.Shadows.Descriptors,
                SilkShadowAtlasLayout.Create(scene.Shadows.Descriptors)!)
            : SilkShadowFrameBinding.None;
        SilkFrameUniformWriter.Write(
            scene.Frame, bytes, false, RenderOutputTransform.Identity, 0, shadows: binding);
        return new WideState(
            scene.Frame, scene.LightLinks, scene.Shadows, scene.Shadows.Descriptors,
            WideRevisions(scene), scene.Frame.View.ToArray(), scene.Frame.Projection.ToArray(),
            scene.Frame.ClipPlanes.ToArray(), scene.Frame.Lights.ToArray(), scene.Frame.Domes.ToArray(),
            scene.Frame.AmbientLight, bytes, scene.Frame.Width, scene.Frame.Height,
            scene.Frame.LightCount, scene.Frame.DomeCount, scene.LightLinks.LightCount,
            scene.LightLinks.DomeCount, scene.LightLinks.Count, scene.LightLinks.UnsupportedFeatures,
            scene.LightLinks.HasDomeLinks,
            scene.LightLinks.Resolve(MeshPath, 17), scene.LightLinks.Resolve(MeshPath, 0),
            scene.Shadows.Descriptors.Select(value => value with
            {
                View = (double[])value.View.Clone(),
                Projection = (double[])value.Projection.Clone(),
            }).ToArray(),
            scene.Shadows.LightCount, scene.Shadows.UnsupportedFeatures,
            scene.Meshes.Count, scene.Materials.Count, scene.Environments.Count,
            scene.PickIdentities.ActiveRangeCount, scene.PickIdentities.AllocatedRangeCount);
    }

    private static ulong[] WideRevisions(SilkSceneState scene) =>
    [
        scene.Revision, scene.GeometryRevision, scene.MaterialRevision,
        scene.EnvironmentRevision, scene.DeformationRevision, scene.Frame.Revision,
        scene.LightLinks.Revision, scene.Shadows.Revision, scene.PickIdentities.Revision,
    ];

    private static async Task AssertWideStateUnchanged(SilkSceneState scene, WideState before)
    {
        WideState after = CaptureWideState(scene);
        await Assert.That(scene.Frame).IsSameReferenceAs(before.Frame);
        await Assert.That(scene.LightLinks).IsSameReferenceAs(before.Links);
        await Assert.That(scene.Shadows).IsSameReferenceAs(before.Shadows);
        await Assert.That(scene.Shadows.Descriptors).IsSameReferenceAs(before.Descriptors);
        await Assert.That(after.Revisions.SequenceEqual(before.Revisions)).IsTrue();
        await Assert.That(after.View.SequenceEqual(before.View)).IsTrue();
        await Assert.That(after.Projection.SequenceEqual(before.Projection)).IsTrue();
        await Assert.That(after.ClipPlanes.SequenceEqual(before.ClipPlanes)).IsTrue();
        await Assert.That(after.Lights.SequenceEqual(before.Lights)).IsTrue();
        await Assert.That(after.Domes.SequenceEqual(before.Domes)).IsTrue();
        await Assert.That(after.Ambient).IsEqualTo(before.Ambient);
        await Assert.That(after.Gpu.SequenceEqual(before.Gpu)).IsTrue();
        await Assert.That((after.Width, after.Height, after.LightCount, after.DomeCount))
            .IsEqualTo((before.Width, before.Height, before.LightCount, before.DomeCount));
        await Assert.That((after.LinkLights, after.LinkDomes, after.LinkCount, after.LinkFeatures, after.HasDomeLinks))
            .IsEqualTo((
                before.LinkLights, before.LinkDomes, before.LinkCount, before.LinkFeatures, before.HasDomeLinks));
        await Assert.That(after.PathMasks).IsEqualTo(before.PathMasks);
        await Assert.That(after.InstanceMasks).IsEqualTo(before.InstanceMasks);
        await Assert.That((after.ShadowLights, after.ShadowFeatures, after.ShadowValues.Length))
            .IsEqualTo((before.ShadowLights, before.ShadowFeatures, before.ShadowValues.Length));
        for (int index = 0; index < before.ShadowValues.Length; index++)
        {
            SilkShadowDescriptor expected = before.ShadowValues[index];
            SilkShadowDescriptor actual = after.ShadowValues[index];
            await Assert.That((actual.LightIndex, actual.MapIndex, actual.Resolution, actual.Flags,
                    actual.DepthBias, actual.NormalBias, actual.PcfRadius))
                .IsEqualTo((expected.LightIndex, expected.MapIndex, expected.Resolution, expected.Flags,
                    expected.DepthBias, expected.NormalBias, expected.PcfRadius));
            await Assert.That(actual.View.SequenceEqual(expected.View)).IsTrue();
            await Assert.That(actual.Projection.SequenceEqual(expected.Projection)).IsTrue();
        }
        await Assert.That((after.MeshCount, after.MaterialCount, after.EnvironmentCount,
                after.PickRanges, after.PickAllocated))
            .IsEqualTo((before.MeshCount, before.MaterialCount, before.EnvironmentCount,
                before.PickRanges, before.PickAllocated));
    }

    private static byte[] WideFrame(int lightCount, int domeCount, int width = 320, uint flags = 0x1)
    {
        var bytes = new byte[23368];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 23368u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 180);
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (index * 8)),
                index % 5 == 0 ? 1 : 0);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (index * 8)),
                index switch { 0 => 2, 5 => 4, 10 => 8, 15 => 1, _ => 0 });
        }
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(112), width / 64d);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(120), -4d);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(128), 5d);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(272), 1u);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(280 + (component * 8)), component + 1);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(536), (uint)lightCount);
        // Candidate numeric HAS_AUTHORED_DIRECT_LIGHTS encoding; no guessed accessor.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(540), flags);
        for (int light = 0; light < lightCount; light++)
        {
            int entry = 552 + (light * 176);
            float t = light + 1;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), (uint)((light % 5) + 1));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4), 1u);
            WideFloats(bytes, entry + 8, t / 4, t / 2, t / 128, t / 64, t / 32, t * 2);
            for (int index = 0; index < 16; index++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(entry + 32 + (index * 8)),
                    index switch { 12 => t, 13 => t * 2, 14 => -t, _ => index % 5 == 0 ? 1 : 0 });
            }
            WideFloats(bytes, entry + 160, 1, 0.5f, 0.75f, t / 16);
        }
        WideFloats(bytes, 23080, 0.25f, 0.5f, 0.75f, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(23096), (uint)domeCount);
        for (int dome = 0; dome < domeCount; dome++)
        {
            float t = dome + 1;
            WideFloats(bytes, 23112 + (dome * 32), t / 8, t / 4, t * 3 / 8);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(23128 + (dome * 32)), 1u);
        }
        return bytes;
    }

    private static byte[] WideLinks(
        uint lightCount, uint domeCount,
        params (string Path, int Instance, UInt128 Light, UInt128 Shadow, uint Dome)[] entries)
    {
        var bytes = new byte[24 + entries.Sum(entry => 44 + Encoding.UTF8.GetByteCount(entry.Path))];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16),
            entries.Length == 0 ? 0u : (uint)SilkLightLinkUnsupportedFeatures.Truncated);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), domeCount);
        int offset = 24;
        foreach ((string path, int instance, UInt128 light, UInt128 shadow, uint dome) in entries)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(offset), light);
            BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(offset + 16), shadow);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 32), dome);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 36), instance);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 40), (uint)pathBytes.Length);
            pathBytes.CopyTo(bytes, offset + 44);
            offset += 44 + pathBytes.Length;
        }
        return bytes;
    }

    private static byte[] WideShadow(uint lightCount, uint lightIndex)
    {
        var bytes = new byte[312];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Shadow);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 312u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)SilkShadowUnsupportedFeatures.MapBudget);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), lightIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), 512u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), 3u);
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(40 + (index * 8)),
                index == 12 ? 7 : index % 5 == 0 ? 1 : 0);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(168 + (index * 8)),
                index switch { 0 => 2, 5 => 4, 10 => 8, 15 => 1, _ => 0 });
        }
        WideFloats(bytes, 296, 0.125f, 0.25f, 2);
        return bytes;
    }

    private static void WideFloats(byte[] bytes, int offset, params float[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + (index * 4)), values[index]);
        }
    }
}
