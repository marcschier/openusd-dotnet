// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the authored shape of the committed UsdLux light-linking fixture.
/// </summary>
/// <remarks>
/// <para>
/// The fixture's whole value is what it authors: a light-link collection that
/// keeps `UsdLuxLightAPI`'s schema-default `includeRoot` and names one exclusion,
/// a second light that authors no collection at all, and a shadow-link
/// collection narrower than the light-link one. Those three shapes are what make
/// it a linking fixture rather than a lighting fixture, and each is easy to lose
/// in an edit that still parses.
/// </para>
/// <para>
/// This checks the authored text rather than a rendered result. The resolved
/// masks are gated elsewhere and against a stage the producer authors itself:
/// `native/hdSilk/tests/hdsilk_probe.cpp` proves a real UsdLux collection reaches
/// the page through UsdImaging and Hydra, and
/// `tests/OpenUsd.Rendering.ConformanceTests/SilkLightLinkConformance.cs` proves
/// the mask changes the image on two backends. What is missing without this file
/// is the fixture itself staying the thing those claims describe.
/// </para>
/// </remarks>
public sealed class OmniverseLightLinkingFixtureTests
{
    private static string FixturePath => Path.Combine(
        FindRepositoryRoot(),
        "test-assets",
        "omniverse",
        "lighting",
        "light-shadow-linking.usda");

    [Test]
    public async Task TheKeyLightExcludesOnePrimAndKeepsTheIncludeRootDefault()
    {
        string text = await File.ReadAllTextAsync(FixturePath);

        await Assert.That(text)
            .Contains("rel collection:lightLink:excludes = </World/Geom/Unlinked>");

        // An authored includeRoot would change the fixture from "everything but
        // this prim" into "only what is listed", which is a different case and
        // would make the excluded prim unlit by both lights.
        await Assert.That(text).DoesNotContain("collection:lightLink:includeRoot");
        await Assert.That(text).DoesNotContain("collection:lightLink:includes");
    }

    [Test]
    public async Task TheShadowLinkIsNarrowerThanTheLightLink()
    {
        string text = await File.ReadAllTextAsync(FixturePath);

        await Assert.That(text)
            .Contains("rel collection:shadowLink:excludes = </World/Geom/ShadowOnly>");

        // The shadow-only prim must not also be excluded from the light link, or
        // the fixture would stop distinguishing the two collections.
        await Assert.That(text).DoesNotContain("lightLink:excludes = </World/Geom/ShadowOnly>");
    }

    [Test]
    public async Task TheFillLightAuthorsNoCollectionAndTheTwoLightsShareNoChannel()
    {
        string text = await File.ReadAllTextAsync(FixturePath);
        int fill = text.IndexOf("def DistantLight \"Fill\"", StringComparison.Ordinal);

        await Assert.That(fill).IsGreaterThan(0);

        // A collection on the fill light would give it a non-empty category and
        // stop it standing in for "links to everything".
        string fillBody = text[fill..];
        await Assert.That(fillBody).DoesNotContain("collection:");

        // Pure red and pure blue, and neither emits green: that is what lets the
        // expected result be read per channel without a reference image.
        await Assert.That(text).Contains("color3f inputs:color = (1, 0, 0)");
        await Assert.That(text).Contains("color3f inputs:color = (0, 0, 1)");
    }

    [Test]
    public async Task TheFixtureReferencesNoExternalAsset()
    {
        // The lighting fixtures are redistributable because they carry no
        // external reference; a linking fixture needs no image at all. Checked
        // against the authored body rather than the header comment, which
        // describes that property in prose.
        string text = await File.ReadAllTextAsync(FixturePath);
        int body = text.IndexOf("def Xform \"World\"", StringComparison.Ordinal);

        await Assert.That(body).IsGreaterThan(0);
        await Assert.That(text[body..]).DoesNotContain("@");
        await Assert.That(text[body..]).DoesNotContain("references");
        await Assert.That(text[body..]).DoesNotContain("payload");
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
}
