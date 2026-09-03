// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.RegularExpressions;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the Metal half of the prefiltered environment against the checked MSL
/// the Windows producer emits, on every host.
/// </summary>
/// <remarks>
/// <para>
/// This is a *source and translation* gate and nothing more. It proves the
/// environment resources reached the Metal shading language output of the same
/// deterministic build that produced the DXIL and SPIR-V, that they landed on
/// the argument indices Slang derives from the manifest's HLSL registers, and
/// that <see cref="MetalShaderResourceIndices"/> maps the renderer-neutral
/// bindings onto exactly those indices. It does <b>not</b> execute anything: no
/// Metal device is created, no <c>metallib</c> is linked, and no pixel is
/// produced or compared. The executed evidence for image-based lighting is the
/// D3D12 WARP and Vulkan SwiftShader pixel gates, and this file must not be
/// cited as anything more than translation coverage.
/// </para>
/// <para>
/// It runs on every host precisely because the checked MSL is a build artifact
/// rather than a machine capability: a mis-mapped argument index binds a texture
/// to a slot nothing reads, which Metal reports as neither an error nor a
/// warning, and which no Windows or Linux test would otherwise see until a macOS
/// leg of CI produced a black reflection.
/// </para>
/// </remarks>
public sealed class MetalEnvironmentSourceContractTests
{
    private static readonly string[] MeshFragmentArtifacts =
    [
        "mesh.fragment.metal",
        "mesh.fragment.uv.metal",
        "mesh.fragment.uv+material.metal",
        "mesh.fragment.uv+material+normal.metal",
        "mesh.fragment.uv+normal.metal",
        "mesh.volume.fragment.metal",
    ];

    [Test]
    public async Task EveryCheckedMetalMeshFragmentDeclaresTheEnvironmentResources()
    {
        // Every permutation, not one: the mesh fragment family expands into
        // several checked binaries and a resource that reached only some of them
        // would make the pipeline layout wrong for exactly the permutations that
        // did not get it.
        string root = Path.Combine(FindRepositoryRoot(), "eng", "shaders", "checked");
        foreach (string artifact in MeshFragmentArtifacts)
        {
            string path = Path.Combine(root, artifact);
            await Assert.That(File.Exists(path))
                .IsTrue()
                .Because($"The checked Metal source {artifact} was not found.");

            string source = await File.ReadAllTextAsync(path);
            await Assert.That(source).Contains("environmentIrradiance");
            await Assert.That(source).Contains("environmentSpecular");
            await Assert.That(source).Contains("environmentSampler");
            await Assert.That(source).Contains("environmentControls");
            await Assert.That(source).Contains("environmentBrdf");

            // The argument indices Slang derived, compared against the table the
            // Metal encoder binds through.
            await Assert.That(FindTextureIndex(source, "environmentIrradiance"))
                .IsEqualTo(MetalShaderResourceIndices.Map(
                    SilkBindingKind.SampledTexture,
                    SilkBindingLayoutDescriptor.EnvironmentIrradianceTextureBinding));
            await Assert.That(FindTextureIndex(source, "environmentSpecular"))
                .IsEqualTo(MetalShaderResourceIndices.Map(
                    SilkBindingKind.SampledTexture,
                    SilkBindingLayoutDescriptor.EnvironmentSpecularTextureBinding));
            await Assert.That(FindSamplerIndex(source, "environmentSampler"))
                .IsEqualTo(MetalShaderResourceIndices.Map(
                    SilkBindingKind.Sampler,
                    SilkBindingLayoutDescriptor.EnvironmentSamplerBinding));
            await Assert.That(FindTextureIndex(source, "environmentBrdf"))
                .IsEqualTo(MetalShaderResourceIndices.Map(
                    SilkBindingKind.SampledTexture,
                    SilkBindingLayoutDescriptor.EnvironmentBrdfTextureBinding));
            await Assert.That(FindSamplerIndex(source, "environmentBrdfSampler"))
                .IsEqualTo(MetalShaderResourceIndices.Map(
                    SilkBindingKind.Sampler,
                    SilkBindingLayoutDescriptor.EnvironmentBrdfSamplerBinding));

            // The shadow atlas is checked beside them because its Metal mapping
            // was missing entirely until the environment slots were added: the
            // identity fallback resolved it to argument index 31 while the
            // checked MSL declares 16.
            await Assert.That(FindTextureIndex(source, "shadowAtlas"))
                .IsEqualTo(MetalShaderResourceIndices.Map(
                    SilkBindingKind.SampledTexture,
                    SilkBindingLayoutDescriptor.ShadowAtlasTextureBinding));
            await Assert.That(FindSamplerIndex(source, "shadowSampler"))
                .IsEqualTo(MetalShaderResourceIndices.Map(
                    SilkBindingKind.Sampler,
                    SilkBindingLayoutDescriptor.ShadowSamplerBinding));
        }
    }

    [Test]
    public async Task TheCheckedMetalFragmentTranslatesTheEnvironmentShadingItself()
    {
        // Declaring the resources is not the same as reading them. The
        // translated source has to carry the sampling and the analytic
        // environment BRDF too, or the Metal binary would bind two textures it
        // never reads and every reflection on macOS would be black while the
        // binding contract above still passed.
        string path = Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "shaders",
            "checked",
            "mesh.fragment.uv+material+normal.metal");
        string source = await File.ReadAllTextAsync(path);

        await Assert.That(source).Contains("sample(");
        await Assert.That(Regex.Count(source, @"environmentSpecular_\d+\)\.sample"))
            .IsGreaterThanOrEqualTo(2)
            .Because(
                "Two slices are sampled and blended, because the roughness axis " +
                "is a slice stack rather than a mip chain.");
        await Assert.That(Regex.Count(source, @"environmentIrradiance_\d+\)\.sample"))
            .IsGreaterThanOrEqualTo(1)
            .Because("The irradiance map must actually be sampled.");
        await Assert.That(Regex.Count(source, @"environmentBrdf_\d+\)\.sample"))
            .IsGreaterThanOrEqualTo(1)
            .Because(
                "The split-sum term must come from the integrated table rather " +
                "than from an analytic fit.");

        // The analytic fit's own exponent must be *gone*: its presence would mean
        // the translated fragment still evaluates a curve fit instead of reading
        // the numerically integrated table.
        await Assert.That(source).DoesNotContain("-9.27999973297119141f");
    }

    [Test]
    public async Task TheCheckedMetalFragmentCarriesTheDomeLinkingBlock()
    {
        // Dome linking is per-draw constants and a loop, not a resource, so the
        // binding contract above cannot see it at all. If the dome block failed
        // to translate, Metal would bind exactly the resources it binds today and
        // light every prim with every dome -- a wrong image that no binding check
        // would notice. This is source and translation coverage only: no Metal
        // device is created and no pixel is produced here.
        string root = Path.Combine(FindRepositoryRoot(), "eng", "shaders", "checked");
        foreach (string artifact in MeshFragmentArtifacts)
        {
            string path = Path.Combine(root, artifact);
            string source = await File.ReadAllTextAsync(path);

            await Assert.That(source)
                .Contains("domeControls")
                .Because($"{artifact} must carry the frame dome table controls.");
            await Assert.That(source)
                .Contains("domeAmbient")
                .Because($"{artifact} must carry the per-dome ambient table.");
            await Assert.That(source)
                .Contains("domeEnvironment")
                .Because($"{artifact} must carry the per-dome environment group table.");
            await Assert.That(source)
                .Contains("domeLinkControls")
                .Because($"{artifact} must carry the per-draw dome link mask.");
        }
    }

    [Test]
    public async Task TheMetalIndexTableAgreesWithTheDirect3DRegisterAllocation()
    {
        // Slang derives the Metal argument index from the HLSL register, so the
        // two backend tables have to agree for every environment slot. Restated
        // here as well as in the conformance reflection comparison because this
        // assembly runs on hosts where the conformance suite does not.
        await Assert.That(MetalShaderResourceIndices.Map(
                SilkBindingKind.SampledTexture,
                SilkBindingLayoutDescriptor.EnvironmentIrradianceTextureBinding))
            .IsEqualTo(17u);
        await Assert.That(MetalShaderResourceIndices.Map(
                SilkBindingKind.SampledTexture,
                SilkBindingLayoutDescriptor.EnvironmentSpecularTextureBinding))
            .IsEqualTo(18u);
        await Assert.That(MetalShaderResourceIndices.Map(
                SilkBindingKind.Sampler,
                SilkBindingLayoutDescriptor.EnvironmentSamplerBinding))
            .IsEqualTo(14u);
        await Assert.That(MetalShaderResourceIndices.Map(
                SilkBindingKind.SampledTexture,
                SilkBindingLayoutDescriptor.EnvironmentBrdfTextureBinding))
            .IsEqualTo(19u);
        await Assert.That(MetalShaderResourceIndices.Map(
                SilkBindingKind.Sampler,
                SilkBindingLayoutDescriptor.EnvironmentBrdfSamplerBinding))
            .IsEqualTo(15u);

        // Distinctness, which is the failure the whole table exists to prevent:
        // two live resources collapsing onto one argument index.
        await Assert.That(MetalShaderResourceIndices.Map(
                SilkBindingKind.SampledTexture,
                SilkBindingLayoutDescriptor.EnvironmentIrradianceTextureBinding))
            .IsNotEqualTo(MetalShaderResourceIndices.Map(
                SilkBindingKind.SampledTexture,
                SilkBindingLayoutDescriptor.ShadowAtlasTextureBinding));
    }

    private static uint FindTextureIndex(string source, string resource) =>
        FindIndex(source, resource, "texture");

    private static uint FindSamplerIndex(string source, string resource) =>
        FindIndex(source, resource, "sampler");

    private static uint FindIndex(string source, string resource, string kind)
    {
        Match match = Regex.Match(
            source,
            $@"{Regex.Escape(resource)}_\d+\s*\[\[{kind}\((?<index>\d+)\)\]\]",
            RegexOptions.CultureInvariant);
        return match.Success
            ? uint.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture)
            : throw new InvalidOperationException(
                $"The checked Metal source declares no [[{kind}(n)]] index for {resource}.");
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
