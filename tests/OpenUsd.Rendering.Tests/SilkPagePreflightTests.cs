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
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(1992 + 32 + 16), 1u);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task APublishedFrameDomeEntryMustBeMarkedPresent()
    {
        byte[] page = Frame(domeCount: 1, textured: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(1992 + 16), 2u);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AFrameDomeEntryWithANonFiniteAmbientIsRefused()
    {
        byte[] page = Frame(domeCount: 1, textured: 0);
        BinaryPrimitives.WriteSingleLittleEndian(page.AsSpan(1992), float.PositiveInfinity);

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
        int entrySize = entryCount == 0 ? 0 : 20 + pathBytes.Length;
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

        uint lightMask = lightCount >= 32 ? uint.MaxValue : (1u << (int)lightCount) - 1;
        uint domeMask = domeCount >= 32 ? uint.MaxValue : (1u << (int)domeCount) - 1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), lightMask);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), lightMask);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), domeMask == 0 ? 0u : 1u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(36), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes, 44);
        return bytes;
    }

    private static byte[] Frame(int domeCount, int textured = int.MaxValue, uint lightCount = 0)
    {
        const int frameSize = 2248;
        const int domeCountOffset = 1976;
        const int domeTableOffset = 1992;
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
}
