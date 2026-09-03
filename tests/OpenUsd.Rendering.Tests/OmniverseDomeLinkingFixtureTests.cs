// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the authored shape of the committed UsdLux dome-linking fixture.
/// </summary>
/// <remarks>
/// <para>
/// The fixture's whole value is what it authors, and it authors the three
/// collection shapes a dome light can take that a flat "excludes one prim" case
/// does not reach: an <c>includeRoot = false</c> collection that lists what it
/// lights, an include that names a <em>nested</em> collection rather than a prim,
/// and a <c>collection:shadowLink</c> on a dome, which hdSilk names rather than
/// applies because it casts no dome shadow. Each of those is easy to lose in an
/// edit that still parses.
/// </para>
/// <para>
/// This checks the authored text rather than a rendered result. The resolved
/// masks are gated elsewhere and against a stage the producer authors itself:
/// <c>native/hdSilk/tests/hdsilk_probe.cpp</c> proves a real dome collection
/// reaches the page through UsdImaging and Hydra, and
/// <c>tests/OpenUsd.Rendering.ConformanceTests/SilkDomeLinkConformance.cs</c>
/// proves the dome mask changes both the diffuse and the specular sky on two
/// backends. What is missing without this file is the fixture itself staying the
/// thing those claims describe.
/// </para>
/// </remarks>
public sealed class OmniverseDomeLinkingFixtureTests
{
    private static string FixturePath => Path.Combine(
        FindRepositoryRoot(),
        "test-assets",
        "omniverse",
        "lighting",
        "dome-light-linking.usda");

    [Test]
    public async Task TheFirstDomeOptsOutOfIncludeRootAndIncludesANestedCollection()
    {
        string text = await File.ReadAllTextAsync(FixturePath);

        await Assert.That(text).Contains("uniform bool collection:lightLink:includeRoot = 0");

        // A collection-valued include is the case UsdImaging expands before Hydra
        // reports a category. Naming a prim here instead would still resolve, and
        // would stop the fixture covering nested collections at all.
        await Assert.That(text)
            .Contains("rel collection:lightLink:includes = </World/Geom.collection:onlyA>");
        await Assert.That(text).Contains("rel collection:onlyA:includes = </World/Geom/UnderA>");
    }

    [Test]
    public async Task TheSecondDomeKeepsIncludeRootAndExcludesOnePrim()
    {
        string text = await File.ReadAllTextAsync(FixturePath);

        await Assert.That(text)
            .Contains("rel collection:lightLink:excludes = </World/Geom/UnderA>");

        // Exactly one authored includeRoot in the file, on the first dome. A
        // second one would turn this collection into "only what is listed" and
        // leave every prim unlit by it.
        await Assert.That(
            text.Split("collection:lightLink:includeRoot", StringSplitOptions.None).Length)
            .IsEqualTo(2);
    }

    [Test]
    public async Task TheDomeShadowCollectionIsAuthoredAndIsNotTheLightCollection()
    {
        string text = await File.ReadAllTextAsync(FixturePath);

        await Assert.That(text)
            .Contains("rel collection:shadowLink:excludes = </World/Geom/UnderBoth>");

        // The shadow-excluded prim must not also be excluded from a light link,
        // or the fixture would stop distinguishing a collection hdSilk applies
        // from one it only diagnoses.
        await Assert.That(text)
            .DoesNotContain("lightLink:excludes = </World/Geom/UnderBoth>");
    }

    [Test]
    public async Task TheTwoDomesShareNoChannelSoTheResultIsReadablePerChannel()
    {
        string text = await File.ReadAllTextAsync(FixturePath);

        await Assert.That(text).Contains("def DomeLight \"SkyA\"");
        await Assert.That(text).Contains("def DomeLight \"SkyB\"");
        await Assert.That(text).Contains("color3f inputs:color = (1, 0, 0)");
        await Assert.That(text).Contains("color3f inputs:color = (0, 0, 1)");

        // Neither dome emits green, which is what lets a per-channel reading
        // stand in for a reference image.
        await Assert.That(text).DoesNotContain("inputs:color = (0, 1, 0)");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ??
            throw new InvalidOperationException("Could not locate repository root.");
    }
}
