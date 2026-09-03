// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the grouped prefiltered environment a scene with UsdLux dome linking
/// bakes, and the single-group layout every other scene keeps.
/// </summary>
/// <remarks>
/// <para>
/// The grouped bake is what makes a dome's <c>collection:lightLink</c> selectable
/// per draw without collapsing every dome into one inseparable sky. Each case
/// here is analytic: the domes are constant skies in disjoint channels, so a
/// group's contribution is readable in isolation and the composed group's value
/// is the exact sum of them.
/// </para>
/// <para>
/// The property that costs the most to get wrong is the one about the scene that
/// links <em>nothing</em>: it must keep the single-group layout, because the
/// grouped atlas is addressed through different texture coordinates and a scene
/// with no dome collection has to render the bytes it rendered before dome
/// linking existed rather than bytes that merely round to the same place.
/// </para>
/// </remarks>
public sealed class SilkDomeEnvironmentGroupTests
{
    private const float UnitDomeAmbient = 0.96f;

    [Test]
    public async Task AnUngroupedBakeKeepsExactlyOneGroup()
    {
        SilkEnvironmentMaps maps = SilkEnvironmentPrefilter.Build(
            [Source(Constant(1f, 0f, 0f)), Source(Constant(0f, 0f, 1f))],
            SilkEnvironmentPrefilterOptions.Default);

        await Assert.That(maps.GroupCount).IsEqualTo(1u);
        await Assert.That(maps.ComposedGroup).IsEqualTo(0u);
        await Assert.That(maps.IrradianceAtlasHeight).IsEqualTo(maps.IrradianceHeight);
        await Assert.That(maps.SpecularAtlasHeight)
            .IsEqualTo(maps.SpecularSliceHeight * maps.SpecularSliceCount);
    }

    [Test]
    public async Task AGroupedBakeCarriesOneGroupPerDomePlusTheComposedOne()
    {
        SilkEnvironmentMaps maps = BuildGrouped(
            Source(Constant(1f, 0f, 0f)),
            Source(Constant(0f, 0f, 1f)));

        await Assert.That(maps.DomeCount).IsEqualTo(2);
        await Assert.That(maps.GroupCount).IsEqualTo(3u);
        await Assert.That(maps.ComposedGroup).IsEqualTo(2u);
        await Assert.That(maps.IrradianceAtlasHeight)
            .IsEqualTo(maps.IrradianceHeight * 3);
        await Assert.That(maps.SpecularAtlasHeight)
            .IsEqualTo(maps.SpecularSliceHeight * maps.SpecularSliceCount * 3);
    }

    [Test]
    public async Task EachGroupCarriesExactlyOneDomesIrradiance()
    {
        // Two constant skies in disjoint channels. Group 0 must be red only,
        // group 1 blue only, and neither may carry any part of the other: a bake
        // that accumulated into the wrong group, or that wrote the composed sum
        // into every group, changes exactly this.
        SilkEnvironmentMaps maps = BuildGrouped(
            Source(Constant(1f, 0f, 0f)),
            Source(Constant(0f, 0f, 1f)));

        float expected = MathF.PI * UnitDomeAmbient;
        foreach (Vector3 direction in Directions())
        {
            Vector3 red = maps.SampleIrradiance(direction, 0);
            Vector3 blue = maps.SampleIrradiance(direction, 1);
            await Assert.That(red.X).IsEqualTo(expected).Within(expected * 0.02f);
            await Assert.That(red.Z).IsEqualTo(0f).Within(1e-3f);
            await Assert.That(blue.Z).IsEqualTo(expected).Within(expected * 0.02f);
            await Assert.That(blue.X).IsEqualTo(0f).Within(1e-3f);
        }
    }

    [Test]
    public async Task TheComposedGroupIsTheSumOfThePerDomeGroups()
    {
        // The composed group is not redundant: a prim linked to every dome reads
        // it rather than summing the per-dome groups, so it has to be the sum
        // they would have produced -- to within the half-float rounding that is
        // exactly why it exists as its own bake.
        SilkEnvironmentMaps maps = BuildGrouped(
            Source(Constant(0.5f, 0f, 0f)),
            Source(Constant(0f, 0.25f, 0f)),
            Source(Constant(0f, 0f, 0.125f)));

        foreach (Vector3 direction in Directions())
        {
            Vector3 composed = maps.SampleIrradiance(direction, maps.ComposedGroup);
            Vector3 summed =
                maps.SampleIrradiance(direction, 0) +
                maps.SampleIrradiance(direction, 1) +
                maps.SampleIrradiance(direction, 2);
            await Assert.That(composed.X).IsEqualTo(summed.X).Within(summed.X * 0.01f + 1e-4f);
            await Assert.That(composed.Y).IsEqualTo(summed.Y).Within(summed.Y * 0.01f + 1e-4f);
            await Assert.That(composed.Z).IsEqualTo(summed.Z).Within(summed.Z * 0.01f + 1e-4f);
        }
    }

    [Test]
    public async Task EachGroupCarriesItsOwnDomesSpecularStack()
    {
        // The specular half has to be grouped too. A bake that grouped only the
        // irradiance map would leave every mirror reflecting the composed sky
        // however its collections were authored.
        SilkEnvironmentMaps maps = BuildGrouped(
            Source(Constant(1f, 0f, 0f), specular: 1f, diffuse: 0f),
            Source(Constant(0f, 0f, 1f), specular: 1f, diffuse: 0f));

        for (uint slice = 0; slice < maps.SpecularSliceCount; slice++)
        {
            Vector3 red = maps.SampleSpecularSlice(Vector3.UnitZ, slice, 0);
            Vector3 blue = maps.SampleSpecularSlice(Vector3.UnitZ, slice, 1);
            await Assert.That(red.X).IsGreaterThan(0.1f);
            await Assert.That(red.Z).IsLessThan(1e-3f);
            await Assert.That(blue.Z).IsGreaterThan(0.1f);
            await Assert.That(blue.X).IsLessThan(1e-3f);
        }
    }

    [Test]
    public async Task AGroupedBakeIsCheckedAgainstTheByteBudgetBeforeItIsAllocated()
    {
        // The grouped footprint is a multiple of the single-group one, so a
        // budget that admits one group can refuse the grouped bake. It has to be
        // refused before anything is allocated, which is the only order in which
        // a byte budget bounds anything.
        var options = SilkEnvironmentPrefilterOptions.Default with
        {
            MaximumPrefilteredBytes = SilkEnvironmentPrefilterOptions.Default
                .GetPrefilteredByteSize(1),
        };

        await Assert.That(() => SilkEnvironmentPrefilter.Build(
                [Source(Constant(1f, 1f, 1f)), Source(Constant(1f, 1f, 1f))],
                options,
                perDomeGroups: true))
            .Throws<SilkEnvironmentBudgetExceededException>();

        // The same options still admit the ungrouped bake, so the refusal is the
        // group count rather than the shape.
        SilkEnvironmentMaps ungrouped = SilkEnvironmentPrefilter.Build(
            [Source(Constant(1f, 1f, 1f)), Source(Constant(1f, 1f, 1f))],
            options);
        await Assert.That(ungrouped.GroupCount).IsEqualTo(1u);
    }

    [Test]
    public async Task TheGroupedAndUngroupedBakesAreDifferentCacheEntries()
    {
        // Two bakes of the same domes with and without groups are two payloads
        // with two different atlas shapes. Serving one for the other would make
        // the shader read a group index that does not exist.
        var dome = SilkEnvironmentData.CopyFrom(
            ReadUpsert(SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                "/World/Lights/Dome",
                "/assets/sky.hdr",
                domeIndex: 0)));
        SilkEnvironmentAssetStamp[] stamps = [new SilkEnvironmentAssetStamp(1, 2)];

        string grouped = SilkEnvironmentIdentity.Compose(
            "context",
            [dome],
            stamps,
            SilkEnvironmentPrefilterOptions.Default,
            perDomeGroups: true);
        string composed = SilkEnvironmentIdentity.Compose(
            "context",
            [dome],
            stamps,
            SilkEnvironmentPrefilterOptions.Default);

        await Assert.That(grouped).IsNotEqualTo(composed);
    }

    [Test]
    public async Task ADomeIndexTravelsFromTheWireIntoRetainedState()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. SilkDomeFrameWireTests.CreateDomeFrame(4),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    "/World/Lights/Dome0",
                    "/assets/sky0.hdr",
                    domeIndex: 0),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    "/World/Lights/Dome1",
                    "/assets/sky1.hdr",
                    domeIndex: 1),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    "/World/Lights/Dome2",
                    "/assets/sky2.hdr",
                    domeIndex: 2),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    "/World/Lights/Dome",
                    "/assets/sky.hdr",
                    domeIndex: 3),
            ],
            5,
            1);

        SilkEnvironmentData dome = scene.Environments["/World/Lights/Dome"];
        await Assert.That(dome.DomeIndex).IsEqualTo(3u);
        await Assert.That(dome.HasDomeIndex).IsTrue();
    }

    [Test]
    public async Task ADomeWithNoIndexIsRetainedAsUnaddressable()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                "/World/Lights/Dome",
                "/assets/sky.hdr"),
            1,
            1);

        SilkEnvironmentData dome = scene.Environments.Values.Single();
        await Assert.That(dome.DomeIndex)
            .IsEqualTo(SilkEnvironmentUpsertCommand.NoDomeIndex);
        await Assert.That(dome.HasDomeIndex).IsFalse();
    }

    [Test]
    public async Task ADomeIndexOutsideTheBoundedTableIsRejected()
    {
        byte[] page = SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
            "/World/Lights/Dome",
            "/assets/sky.hdr",
            domeIndex: SilkFrameCommand.MaximumDomes);

        await Assert.That(() => new SilkSceneState().Apply(page, 1, 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task TheDomeGroupTableRoundTripsEveryBitAndDefaultsToNoGroup()
    {
        SilkDomeGroupTable table = SilkDomeGroupTable.Empty;
        for (int dome = 0; dome < SilkFrameCommand.MaximumDomes; dome++)
        {
            await Assert.That(table.GetGroup(dome)).IsEqualTo(SilkDomeGroupTable.NoGroup);
        }

        for (int dome = 0; dome < SilkFrameCommand.MaximumDomes; dome++)
        {
            table = table.WithGroup(dome, (uint)(SilkFrameCommand.MaximumDomes - dome));
        }
        for (int dome = 0; dome < SilkFrameCommand.MaximumDomes; dome++)
        {
            await Assert.That(table.GetGroup(dome))
                .IsEqualTo((int)SilkFrameCommand.MaximumDomes - dome);
        }

        // The table is compared by value so the frame constants re-pack exactly
        // when the mapping changed, rather than on every rebuild that produced
        // the same mapping.
        await Assert.That(table).IsEqualTo(
            Enumerable.Range(0, (int)SilkFrameCommand.MaximumDomes)
                .Aggregate(
                    SilkDomeGroupTable.Empty,
                    (current, dome) => current.WithGroup(
                        dome,
                        (uint)(SilkFrameCommand.MaximumDomes - dome))));
    }

    private static SilkEnvironmentUpsertCommand ReadUpsert(byte[] page)
    {
        using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
            page,
            1,
            SilkCommandParser.PageAbiVersion);
        _ = commands.MoveNext();
        return commands.Current.AsEnvironmentUpsert();
    }

    private static SilkEnvironmentMaps BuildGrouped(params SilkEnvironmentSource[] sources) =>
        SilkEnvironmentPrefilter.Build(
            sources,
            SilkEnvironmentPrefilterOptions.Default,
            perDomeGroups: true);

    private static SilkEnvironmentSource Source(
        SilkDecodedImage image,
        float diffuse = 1f,
        float specular = 0f)
    {
        Vector3 scale = Vector3.One * UnitDomeAmbient;
        return new SilkEnvironmentSource(
            image,
            SilkColorSpace.Raw,
            Matrix4x4.Identity,
            scale * diffuse,
            scale * specular);
    }

    private static SilkDecodedImage Constant(float red, float green, float blue)
    {
        const uint width = 16;
        const uint height = 8;
        float[] values = new float[width * height * 4];
        for (int texel = 0; texel < width * height; texel++)
        {
            values[(texel * 4) + 0] = red;
            values[(texel * 4) + 1] = green;
            values[(texel * 4) + 2] = blue;
            values[(texel * 4) + 3] = 1f;
        }
        byte[] pixels = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, pixels, 0, pixels.Length);
        return new SilkDecodedImage(width, height, pixels, SilkTextureFormat.Rgba32Float);
    }

    private static IEnumerable<Vector3> Directions() =>
    [
        Vector3.UnitX,
        -Vector3.UnitX,
        Vector3.UnitY,
        -Vector3.UnitY,
        Vector3.UnitZ,
        -Vector3.UnitZ,
    ];
}
