// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

/// <summary>
/// Holds the synthetic MaterialX fixtures to the exact input tables the hdSilk
/// projection carries.
/// </summary>
/// <remarks>
/// The fixtures exist to state what the renderer does and does not shade, so a
/// fixture that drifted from the projection would document the wrong thing. Both
/// halves are asserted: every authored input is classified as projected or
/// explicitly excluded, and neither list may be empty, because a fixture that
/// authored only supported inputs would never exercise the diagnostics.
///
/// The nodedef identifiers are asserted literally. <c>open_pbr_surface</c> is
/// the node name; <c>ND_open_pbr_surface_surfaceshader</c> is the nodedef the
/// pinned MaterialX 1.39.4 libraries declare, and it is the string
/// <c>info:id</c> must carry for hdSilk to recognise the surface at all.
/// </remarks>
public sealed class MaterialXSurfaceModelFixtureTests
{
    /// <summary>Inputs the hdSilk OpenPBR projection carries.</summary>
    private static readonly string[] OpenPbrProjectedInputs =
    [
        "base_weight",
        "base_color",
        "base_metalness",
        "specular_roughness",
        "specular_ior",
        "coat_weight",
        "coat_roughness",
        "geometry_opacity",
        "geometry_normal",
        "emission_luminance",
        "emission_color",
    ];

    /// <summary>Inputs the hdSilk OpenPBR projection reports and drops.</summary>
    private static readonly string[] OpenPbrExcludedInputs =
    [
        "specular_weight",
        "specular_color",
        "specular_roughness_anisotropy",
        "base_diffuse_roughness",
        "transmission_weight",
        "subsurface_weight",
        "fuzz_weight",
        "coat_color",
        "coat_ior",
        "coat_roughness_anisotropy",
        "coat_darkening",
        "thin_film_weight",
        "geometry_thin_walled",
        "geometry_coat_normal",
        "geometry_tangent",
    ];

    /// <summary>Inputs the hdSilk standard_surface projection carries.</summary>
    private static readonly string[] StandardSurfaceProjectedInputs =
    [
        "base",
        "base_color",
        "emission",
        "emission_color",
        "metalness",
        "specular_roughness",
        "specular_IOR",
        "coat",
        "coat_roughness",
        "opacity",
        "normal",
    ];

    /// <summary>Inputs the hdSilk standard_surface projection reports and drops.</summary>
    private static readonly string[] StandardSurfaceExcludedInputs =
    [
        "specular",
        "specular_color",
        "specular_anisotropy",
        "specular_rotation",
        "diffuse_roughness",
        "transmission",
        "subsurface",
        "sheen",
        "coat_color",
        "coat_IOR",
        "coat_normal",
        "coat_anisotropy",
        "coat_affect_color",
        "coat_affect_roughness",
        "thin_film_thickness",
        "thin_walled",
        "tangent",
    ];

    [Test]
    public async Task OpenPbrFixtureAuthorsOnlyClassifiedInputs()
    {
        using UsdStage stage = OpenFixtureOrSkip("materialx-openpbr-constant.usda");
        UsdPrim shader = FindShader(stage, "/World/Looks/OpenPbrMat/OpenPbr");

        await Assert.That(shader.GetToken("info:id"))
            .IsEqualTo("ND_open_pbr_surface_surfaceshader");

        (List<string> projected, List<string> excluded, List<string> unknown) =
            Classify(shader, OpenPbrProjectedInputs, OpenPbrExcludedInputs);

        await Assert.That(unknown)
            .IsEmpty()
            .Because(
                "The OpenPBR fixture authors inputs that are neither projected " +
                "nor listed as excluded: " + string.Join(", ", unknown));
        await Assert.That(projected.Count).IsGreaterThanOrEqualTo(8);
        await Assert.That(excluded.Count).IsGreaterThanOrEqualTo(4);
    }

    [Test]
    public async Task StandardSurfaceFixtureAuthorsOnlyClassifiedInputs()
    {
        using UsdStage stage = OpenFixtureOrSkip(
            "materialx-standard-surface-extended.usda");
        UsdPrim shader = FindShader(
            stage, "/World/Looks/StandardSurfaceMat/StandardSurface");

        await Assert.That(shader.GetToken("info:id"))
            .IsEqualTo("ND_standard_surface_surfaceshader");

        (List<string> projected, List<string> excluded, List<string> unknown) =
            Classify(shader, StandardSurfaceProjectedInputs, StandardSurfaceExcludedInputs);

        await Assert.That(unknown)
            .IsEmpty()
            .Because(
                "The standard_surface fixture authors inputs that are neither " +
                "projected nor listed as excluded: " + string.Join(", ", unknown));
        await Assert.That(projected.Count).IsGreaterThanOrEqualTo(8);
        await Assert.That(excluded.Count).IsGreaterThanOrEqualTo(4);
    }

    /// <summary>
    /// Proves both fixtures stay inside the single-coordinate-stream invariant.
    /// </summary>
    /// <remarks>
    /// hdSilk binds one texture-coordinate stream per material. A fixture that
    /// authored a second UV set would read as a claim of multi-UV support that
    /// neither the wire nor the shaders make, so the count is asserted rather
    /// than assumed.
    /// </remarks>
    [Test]
    [Arguments("materialx-openpbr-constant.usda", "/World/OpenPbrCard")]
    [Arguments("materialx-standard-surface-extended.usda", "/World/StandardSurfaceCard")]
    public async Task FixtureMeshCarriesExactlyOneTextureCoordinateSet(
        string fileName,
        string meshPath)
    {
        using UsdStage stage = OpenFixtureOrSkip(fileName);
        UsdPrim mesh = FindShader(stage, meshPath);

        // A texture-coordinate set is exactly a texCoord2f[] primvar. Counting the
        // authored type rather than the name is what makes a second UV set fail
        // here instead of quietly implying support the wire does not have.
        string[] coordinateSets = mesh.GetAttributes()
            .Where(attribute =>
                attribute.Name.StartsWith("primvars:", StringComparison.Ordinal) &&
                attribute.TypeName.StartsWith("texCoord2f", StringComparison.Ordinal))
            .Select(attribute => attribute.Name)
            .ToArray();

        await Assert.That(coordinateSets.Length)
            .IsEqualTo(1)
            .Because(
                "hdSilk binds one texture-coordinate stream per material, so a " +
                "fixture must not author a second UV set: " +
                string.Join(", ", coordinateSets));
    }

    private static (List<string> Projected, List<string> Excluded, List<string> Unknown)
        Classify(UsdPrim shader, string[] projectedInputs, string[] excludedInputs)
    {
        List<string> projected = [];
        List<string> excluded = [];
        List<string> unknown = [];
        foreach (string name in shader.GetAttributeNames())
        {
            if (!name.StartsWith("inputs:", StringComparison.Ordinal))
            {
                continue;
            }
            string input = name["inputs:".Length..];
            if (projectedInputs.Contains(input, StringComparer.Ordinal))
            {
                projected.Add(input);
            }
            else if (excludedInputs.Contains(input, StringComparer.Ordinal))
            {
                excluded.Add(input);
            }
            else
            {
                unknown.Add(input);
            }
        }
        return (projected, excluded, unknown);
    }

    private static UsdPrim FindShader(UsdStage stage, string path)
    {
        foreach (UsdPrim prim in stage.Traverse())
        {
            if (string.Equals(prim.Path, path, StringComparison.Ordinal))
            {
                return prim;
            }
        }
        throw new InvalidOperationException($"The fixture has no prim at {path}.");
    }

    private static UsdStage OpenFixtureOrSkip(string fileName)
    {
        try
        {
            return UsdStage.Open(Path.Combine(
                FindRepositoryRoot(),
                "test-assets",
                "materialx",
                fileName));
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
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
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
