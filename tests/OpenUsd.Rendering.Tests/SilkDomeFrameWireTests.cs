// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the ABI v21 frame dome table: its wire layout, the bounds that keep a
/// malformed table out of retained state, and the frame constants block the
/// checked mesh fragment reads it through.
/// </summary>
/// <remarks>
/// <para>
/// The dome table is what makes a <c>UsdLuxDomeLight</c> addressable by a
/// per-prim collection at all, and its load-bearing property is arithmetic
/// rather than structural: the scene-wide ambient term is the sum of the per-dome
/// summands, produced by the same expression in the same order, so a prim linked
/// to every dome and a prim that sums its linked domes cannot disagree. These
/// cases pin that identity directly.
/// </para>
/// <para>
/// The constants block is checked against the shader's own struct rather than
/// against a restated number, so a field appended to one and not the other is a
/// failure here rather than an out-of-bounds read on the backend that happens to
/// return zeros.
/// </para>
/// </remarks>
public sealed class SilkDomeFrameWireTests
{
    private const int FrameSize = 2248;
    private const int AmbientOffset = 536 + 16 + (8 * 176);
    private const int DomeCountOffset = 1976;
    private const int DomeTableOffset = 1992;
    private const int DomeControlOffset = 1584;
    private const int DomeAmbientOffset = 1600;
    private const int DomeEnvironmentOffset = 1728;

    [Test]
    public async Task TheDomeTableRoundTripsEveryEntryAtItsOwnOffset()
    {
        (byte[] page, uint commands) = FramePage(
            ambient: new Vector3(0.25f, 0.5f, 0.75f),
            domes:
            [
                (new Vector3(0.25f, 0.5f, 0.75f), Present: true, Textured: false),
                (Vector3.Zero, Present: true, Textured: true),
            ]);

        var scene = new SilkSceneState();
        _ = scene.Apply(page, commands, 1);

        await Assert.That(scene.Frame.DomeCount).IsEqualTo(2u);
        SilkFrameDome[] domes = scene.Frame.Domes.ToArray();
        await Assert.That(domes[0].IsPresent).IsTrue();
        await Assert.That(domes[0].IsTextured).IsFalse();
        await Assert.That(domes[0].AmbientColor)
            .IsEqualTo(new Vector3(0.25f, 0.5f, 0.75f));
        await Assert.That(domes[1].IsPresent).IsTrue();
        await Assert.That(domes[1].IsTextured).IsTrue();
        await Assert.That(domes[1].AmbientColor).IsEqualTo(Vector3.Zero);

        // The tail of the fixed table is zeroed, so an entry beyond the published
        // count can never be mistaken for a dome.
        await Assert.That(domes[2].IsPresent).IsFalse();
    }

    [Test]
    public async Task ADomeCountBeyondTheBoundedTableIsRejected()
    {
        byte[] page = CreateFrame(ambient: Vector3.Zero, domes: []);
        BinaryPrimitives.WriteUInt32LittleEndian(
            page.AsSpan(DomeCountOffset),
            SilkFrameCommand.MaximumDomes + 1);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task APageWithoutADomeTableRetainsNoDomes()
    {
        // The pre-v21 frame is still a legal frame: it publishes no dome table,
        // which means no dome is individually addressable and every dome lights
        // every prim.
        byte[] page = CreateFrame(ambient: new Vector3(0.5f), domes: [], legacy: true);

        var scene = new SilkSceneState();
        _ = scene.Apply(page, 1, 1);

        await Assert.That(scene.Frame.DomeCount).IsEqualTo(0u);
        await Assert.That(scene.Frame.Domes[0].IsPresent).IsFalse();
        await Assert.That(scene.Frame.AmbientLight.X).IsEqualTo(0.5f);
    }

    [Test]
    public async Task SummingThePublishedDomesReproducesTheSceneWideAmbientExactly()
    {
        // The identity the whole design rests on. hdSilk accumulates the
        // scene-wide term from these exact summands in this exact order, so the
        // sum is bit-identical rather than approximately equal -- which is what
        // lets a masked prim and an unmasked one agree.
        Vector3 first = new(0.1f, 0.2f, 0.3f);
        Vector3 second = new(0.03f, 0.007f, 0.9f);
        Vector3 aggregate = first + second;

        byte[] page = CreateFrame(
            ambient: aggregate,
            domes:
            [
                (first, Present: true, Textured: false),
                (second, Present: true, Textured: false),
            ]);

        var scene = new SilkSceneState();
        _ = scene.Apply(page, 1, 1);

        SilkFrameDome[] domes = scene.Frame.Domes.ToArray();
        Vector3 summed = domes[0].AmbientColor + domes[1].AmbientColor;
        await Assert.That(summed.X).IsEqualTo(scene.Frame.AmbientLight.X);
        await Assert.That(summed.Y).IsEqualTo(scene.Frame.AmbientLight.Y);
        await Assert.That(summed.Z).IsEqualTo(scene.Frame.AmbientLight.Z);
    }

    [Test]
    public async Task TheFrameConstantsCarryTheDomeBlockAtTheShaderOffsets()
    {
        (byte[] page, uint commands) = FramePage(
            ambient: new Vector3(0.25f, 0f, 0f),
            domes:
            [
                (new Vector3(0.25f, 0f, 0f), Present: true, Textured: false),
                (Vector3.Zero, Present: true, Textured: true),
            ]);
        var scene = new SilkSceneState();
        _ = scene.Apply(page, commands, 1);

        var binding = new SilkEnvironmentFrameBinding(
            true,
            6,
            32,
            true,
            GroupCount: 3,
            ComposedGroup: 2,
            IrradianceSliceHeight: 16,
            DomeGroups: SilkDomeGroupTable.Empty.WithGroup(1, 0));

        byte[] constants = new byte[SilkFrameUniformWriter.ByteSize];
        SilkFrameUniformWriter.Write(
            scene.Frame,
            constants,
            flipClipSpaceY: false,
            RenderOutputTransform.Identity,
            exposure: 0f,
            environmentAmbient: default,
            shadows: null,
            environment: binding);

        await Assert.That(ReadSingle(constants, DomeControlOffset)).IsEqualTo(2f);
        await Assert.That(ReadSingle(constants, DomeControlOffset + 4)).IsEqualTo(3f);
        await Assert.That(ReadSingle(constants, DomeControlOffset + 8)).IsEqualTo(2f);
        await Assert.That(ReadSingle(constants, DomeControlOffset + 12)).IsEqualTo(16f);

        await Assert.That(ReadSingle(constants, DomeAmbientOffset)).IsEqualTo(0.25f);
        await Assert.That(ReadSingle(constants, DomeAmbientOffset + 12)).IsEqualTo(1f);
        await Assert.That(ReadSingle(constants, DomeAmbientOffset + 16)).IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, DomeAmbientOffset + 28)).IsEqualTo(1f);

        // A dome the prefilter did not carry resolves to no group, which is what
        // makes the fragment skip it rather than read whichever group inherited
        // its index.
        await Assert.That(ReadSingle(constants, DomeEnvironmentOffset)).IsEqualTo(-1f);
        await Assert.That(ReadSingle(constants, DomeEnvironmentOffset + 16)).IsEqualTo(0f);

        // The unpublished tail is zeroed, and its group is "none".
        await Assert.That(ReadSingle(constants, DomeAmbientOffset + (2 * 16) + 12))
            .IsEqualTo(0f);
        await Assert.That(ReadSingle(constants, DomeEnvironmentOffset + (2 * 16)))
            .IsEqualTo(-1f);
    }

    [Test]
    public async Task AnInterleavedUntexturedAndFallbackDomeSumsToTheAggregateExactly()
    {
        // The adversarial case. U1 and U2 are untextured domes whose ambient the
        // producer accumulated; T1 between them is a textured dome the prefilter
        // refused, whose mean-radiance term the consumer resolves. The producer's
        // grouping is (U1 + U2) + T1 and the consumer's fallback sum is T1, so a
        // writer that added the two would produce a value the per-dome table does
        // not sum to -- in float, (U1 + U2) + T1 is not U1 + T1 + U2.
        //
        // The values are chosen so the regrouping is observable: a large term, a
        // tiny one that is lost when added to the large one, and a second large
        // one that brings the running sum back into range.
        Vector3 first = new(1f, 1f, 1f);
        Vector3 fallback = new(1e-7f, 1e-7f, 1e-7f);
        Vector3 third = new(-1f, -1f, -1f);
        Vector3 producerAggregate = first + third;

        (byte[] page, uint commands) = FramePage(
            ambient: producerAggregate,
            domes:
            [
                (first, Present: true, Textured: false),
                (Vector3.Zero, Present: true, Textured: true),
                (third, Present: true, Textured: false),
            ]);
        var scene = new SilkSceneState();
        _ = scene.Apply(page, commands, 1);

        var domeAmbient = default(SilkDomeAmbientTable);
        domeAmbient.AddAmbient(1, fallback);

        byte[] constants = new byte[SilkFrameUniformWriter.ByteSize];
        SilkFrameUniformWriter.Write(
            scene.Frame,
            constants,
            flipClipSpaceY: false,
            RenderOutputTransform.Identity,
            exposure: 0f,
            environmentAmbient: fallback,
            shadows: null,
            environment: SilkEnvironmentFrameBinding.FallbackOnly,
            domeAmbient: domeAmbient);

        // The aggregate the shader reads for a prim linked to every dome.
        float aggregate = ReadSingle(constants, 208);

        // The sum the shader computes for a prim that lists every dome, in the
        // order the dome table publishes them.
        float summed = ReadSingle(constants, DomeAmbientOffset) +
            ReadSingle(constants, DomeAmbientOffset + 16) +
            ReadSingle(constants, DomeAmbientOffset + 32);

        await Assert.That(aggregate)
            .IsEqualTo(summed)
            .Because(
                "A prim linked to every dome and a prim that sums its linked " +
                "domes must read the same bits, whatever order the producer and " +
                "the fallback happened to accumulate in.");

        // Non-vacuity: the term the interleaved dome contributes is not zero, and
        // the pre-v21 grouping really would have produced a different number.
        await Assert.That(ReadSingle(constants, DomeAmbientOffset + 16))
            .IsEqualTo(fallback.X);
        await Assert.That(producerAggregate.X + fallback.X).IsNotEqualTo(aggregate);
    }

    [Test]
    public async Task TheMeanRadianceFallbackIsAttributedToItsOwnDomeBit()
    {
        // A textured dome contributes zero to the frame dome table, because its
        // emission is an image. When the prefilter refuses it, the consumer's own
        // mean-radiance term takes its place -- and it has to land on that dome's
        // bit, or a prim that excludes the dome would still receive its fallback.
        (byte[] page, uint commands) = FramePage(
            ambient: Vector3.Zero,
            domes: [(Vector3.Zero, Present: true, Textured: true)]);
        var scene = new SilkSceneState();
        _ = scene.Apply(page, commands, 1);

        var fallback = default(SilkDomeAmbientTable);
        fallback.AddAmbient(0, new Vector3(0.4f, 0.2f, 0.1f));

        byte[] constants = new byte[SilkFrameUniformWriter.ByteSize];
        SilkFrameUniformWriter.Write(
            scene.Frame,
            constants,
            flipClipSpaceY: false,
            RenderOutputTransform.Identity,
            exposure: 0f,
            environmentAmbient: new Vector3(0.4f, 0.2f, 0.1f),
            shadows: null,
            environment: SilkEnvironmentFrameBinding.FallbackOnly,
            domeAmbient: fallback);

        await Assert.That(ReadSingle(constants, DomeAmbientOffset)).IsEqualTo(0.4f);
        await Assert.That(ReadSingle(constants, DomeAmbientOffset + 4)).IsEqualTo(0.2f);
        await Assert.That(ReadSingle(constants, DomeAmbientOffset + 8)).IsEqualTo(0.1f);

        // The aggregate the fragment reads for a fully linked prim carries the
        // same term, so the two paths agree.
        await Assert.That(ReadSingle(constants, 208)).IsEqualTo(0.4f);
    }

    private static float ReadSingle(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset, 4));

    /// <summary>
    /// Builds a whole page: the frame, plus the environment record every textured
    /// dome entry needs.
    /// </summary>
    /// <remarks>
    /// A textured entry of the frame dome table <em>is</em> one dome's image, so a
    /// page that publishes one without a record to supply it describes a dome the
    /// renderer has no sky for. These cases are about the table's own layout, so
    /// the records are the minimum a complete page needs rather than the subject.
    /// </remarks>
    private static (byte[] Page, uint Commands) FramePage(
        Vector3 ambient,
        (Vector3 Ambient, bool Present, bool Textured)[] domes)
    {
        List<byte[]> parts = [CreateFrame(ambient, domes)];
        for (int dome = 0; dome < domes.Length; dome++)
        {
            if (domes[dome].Textured)
            {
                parts.Add(SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    $"/World/Lights/Dome{dome}",
                    $"/assets/dome{dome}.hdr",
                    domeIndex: (uint)dome));
            }
        }
        return ([.. parts.SelectMany(static part => part)], (uint)parts.Count);
    }

    /// <summary>
    /// Builds a frame publishing <paramref name="domeCount"/> textured domes, for
    /// scenes whose environment records claim a dome bit.
    /// </summary>
    internal static byte[] CreateDomeFrame(int domeCount)
    {
        var domes = new (Vector3 Ambient, bool Present, bool Textured)[domeCount];
        for (int dome = 0; dome < domeCount; dome++)
        {
            domes[dome] = (Vector3.Zero, true, true);
        }
        return CreateFrame(Vector3.Zero, domes);
    }

    private static byte[] CreateFrame(
        Vector3 ambient,
        (Vector3 Ambient, bool Present, bool Textured)[] domes,
        bool legacy = false)
    {
        int size = legacy ? 1976 : FrameSize;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 4);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 4);
        for (int element = 0; element < 16; element++)
        {
            double value = element % 5 == 0 ? 1d : 0d;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (element * 8)), value);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (element * 8)), value);
        }
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(AmbientOffset), ambient.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(AmbientOffset + 4), ambient.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(AmbientOffset + 8), ambient.Z);
        BinaryPrimitives.WriteSingleLittleEndian(
            bytes.AsSpan(AmbientOffset + 12),
            domes.Length > 0 ? 1f : 0f);
        if (legacy)
        {
            return bytes;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(DomeCountOffset),
            (uint)domes.Length);
        for (int dome = 0; dome < domes.Length; dome++)
        {
            int entry = DomeTableOffset + (dome * 32);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry),
                domes[dome].Ambient.X);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 4),
                domes[dome].Ambient.Y);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(entry + 8),
                domes[dome].Ambient.Z);

            // OPENUSD_SILK_DOME_FLAG_PRESENT is bit 0 and TEXTURED is bit 1. The
            // raw wire values are written here rather than through the managed
            // enum, which is internal to the parser.
            uint flags = 0;
            if (domes[dome].Present)
            {
                flags |= 1u;
            }
            if (domes[dome].Textured)
            {
                flags |= 2u;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 16), flags);
        }
        return bytes;
    }
}
