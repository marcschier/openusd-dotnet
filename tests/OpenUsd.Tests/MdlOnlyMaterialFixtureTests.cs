// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Shade;

namespace OpenUsd.Tests;

/// <summary>
/// Pins the MDL-only material shape this runtime must recognise, using the
/// repository-authored fixture under <c>test-assets/omniverse/</c>.
/// </summary>
/// <remarks>
/// The fixture is the condition the MDL slice exists for: a material that
/// authors <c>outputs:mdl:surface</c> and nothing else. A renderer that reads
/// only the universal render context sees no surface terminal at all for it,
/// which is how such a material used to be drawn as an undiagnosed default
/// grey. These tests assert the authored shape and the registry's honest answer
/// about it; the rendering half is proven by the hdSilk native probe, which
/// drives this same fixture through UsdImaging and the page wire format.
/// </remarks>
public sealed class MdlOnlyMaterialFixtureTests
{
    private const string MaterialPath = "/World/Looks/OmniPbrMat";
    private const string ShaderPath = "/World/Looks/OmniPbrMat/Shader";

    [Test]
    public async Task MdlOnlyMaterialAuthorsTheMdlContextAndNoUniversalTerminal()
    {
        using UsdStage stage = OpenFixtureOrSkip("mdl-only-omnipbr.usda");
        UsdPrim materialPrim = stage.GetPrim(MaterialPath);

        // The absence of a universal terminal is the whole point of the fixture,
        // and it is asserted against the authored layer text rather than the
        // composed prim. UsdShadeMaterial declares outputs:surface as a built-in
        // schema property, so a composed-property query reports it whether or
        // not the fixture authors it, and a Create*/Get* call on the shading API
        // would author it as a side effect of looking.
        string fixtureText = await File.ReadAllTextAsync(FixturePath("mdl-only-omnipbr.usda"));
        await Assert.That(fixtureText).Contains("token outputs:mdl:surface.connect");
        await Assert.That(fixtureText).DoesNotContain("token outputs:surface.connect");
        await Assert.That(fixtureText).DoesNotContain("token outputs:mtlx:surface.connect");

        UsdShadeMaterial material = UsdShadeMaterial.Wrap(materialPrim);
        UsdShadeConnection mdl = material
            .CreateTerminalOutput(UsdShadeMaterialTerminal.Surface, "mdl")
            .GetConnectedSource();
        await Assert.That(mdl.SourcePrimPath).IsEqualTo(ShaderPath);
        await Assert.That(mdl.SourceName).IsEqualTo("out");
    }

    [Test]
    public async Task MdlShaderCarriesItsSourceAssetIdentityRatherThanAnInfoId()
    {
        using UsdStage stage = OpenFixtureOrSkip("mdl-only-omnipbr.usda");
        UsdPrim shader = stage.GetPrim(ShaderPath);

        await Assert.That(shader.GetToken("info:implementationSource")).IsEqualTo("sourceAsset");
        await Assert.That(shader.GetToken("info:mdl:sourceAsset:subIdentifier"))
            .IsEqualTo("OmniPBR");
        // An MDL shader states no info:id. A consumer that looks only there
        // finds nothing and cannot tell this shader from an empty one, which is
        // why hdSilk resolves the identity from the source asset instead.
        await Assert.That(shader.TryGetValue("info:id", out _)).IsFalse();
    }

    [Test]
    public async Task MdlShaderAuthorsTheAcceptedSubsetAndInputsOutsideIt()
    {
        using UsdStage stage = OpenFixtureOrSkip("mdl-only-omnipbr.usda");
        UsdPrim primitive = stage.GetPrim(ShaderPath);
        UsdShadeShader shader = UsdShadeShader.Wrap(primitive);

        UsdVec3f diffuse = shader.GetInput("diffuse_color_constant").GetColor();
        await Assert.That(diffuse.X).IsEqualTo(0.72f).Within(1e-5f);
        await Assert.That(shader.GetInput("reflection_roughness_constant").GetFloat())
            .IsEqualTo(0.35f).Within(1e-5f);
        await Assert.That(shader.GetInput("metallic_constant").GetFloat())
            .IsEqualTo(0.25f).Within(1e-5f);
        await Assert.That(primitive.GetBool("inputs:enable_opacity")).IsTrue();
        await Assert.That(primitive.GetBool("inputs:enable_emission")).IsTrue();

        // Authored away from the module defaults on purpose: these two are
        // outside the accepted distillation subset and must be reported by name
        // rather than folded into an unrelated parameter.
        await Assert.That(shader.GetInput("subsurface_weight").GetFloat())
            .IsEqualTo(0.4f).Within(1e-5f);
        await Assert.That(shader.GetInput("specular_level").GetFloat())
            .IsEqualTo(0.9f).Within(1e-5f);
    }

    [Test]
    public async Task MdlSourceAssetDoesNotResolveThroughTheSdrRegistry()
    {
        // The honest answer, not a failure: this runtime registers no MDL parser
        // plugin, so the Sdr registry cannot describe OmniPBR.mdl. That is
        // exactly why the node reaches Hydra with an empty identifier and why
        // hdSilk synthesizes one from the source asset instead of waiting for a
        // registry entry that never arrives.
        bool resolved = TryResolveOrSkip(
            "OmniPBR.mdl",
            "OmniPBR",
            "mdl",
            out UsdShaderNodeDefinition? definition);

        await Assert.That(resolved).IsFalse();
        await Assert.That(definition).IsNull();
    }

    private static bool TryResolveOrSkip(
        string sourceAsset,
        string subIdentifier,
        string shadingSystem,
        out UsdShaderNodeDefinition? definition)
    {
        try
        {
            return UsdShaderRegistry.TryGetNodeDefinitionFromAsset(
                sourceAsset,
                subIdentifier,
                shadingSystem,
                out definition);
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
    }

    private static string FixturePath(string fileName) => Path.Combine(
        FindRepositoryRoot(),
        "test-assets",
        "omniverse",
        fileName);

    private static UsdStage OpenFixtureOrSkip(string fileName)
    {
        try
        {
            return UsdStage.Open(FixturePath(fileName));
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

        throw new InvalidOperationException("Could not locate the OpenUsd repository root.");
    }
}
