// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the material's single two-image composite slot to every place that
/// independently states it.
/// </summary>
/// <remarks>
/// The slot exists in five places that no compiler relates to each other: the
/// Slang source, the shader manifest, the checked SPIR-V/DXIL reflection, the
/// checked Metal source, and the managed binding constants that the D3D12 and
/// Metal backends map to registers. A disagreement between any two of them is a
/// pipeline that either fails to create or samples the wrong image, and on Metal
/// it would only surface on a macOS runner.
///
/// It also pins the slot's *absence* from <c>mesh.volume.fragment</c>. That
/// program compiles the same source with <c>MAP_MATERIAL=0</c>, and its binding
/// layout declares only the density texture and sampler, so a composite resource
/// leaking into it would make every sampled-volume pipeline declare a resource it
/// never binds.
/// </remarks>
public sealed class MaterialCompositeSlotContractTests
{
    private const uint CompositeVulkanSamplerBinding = 28;
    private const uint CompositeVulkanTextureBinding = 29;
    private const int CompositeMetalSamplerIndex = 12;
    private const int CompositeMetalTextureIndex = 15;

    [Test]
    public async Task ShaderManifestDeclaresTheCompositeSlotOnlyForMaterialPermutations()
    {
        string root = FindRepositoryRoot();
        using JsonDocument manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(root, "eng", "shaders", "shader-manifest.json")));

        JsonElement meshFragment = manifest.RootElement
            .GetProperty("programs")
            .EnumerateArray()
            .Single(program => program.GetProperty("name").GetString() == "mesh.fragment");

        JsonElement[] composite = meshFragment
            .GetProperty("permutationResources")
            .EnumerateArray()
            .Where(resource =>
                resource.GetProperty("name").GetString()!.StartsWith(
                    "composite",
                    StringComparison.Ordinal))
            .ToArray();

        await Assert.That(composite.Length).IsEqualTo(2);
        foreach (JsonElement resource in composite)
        {
            // A permutation *feature*, not a base resource: the composite lives
            // inside MAP_MATERIAL rather than adding a permutation of its own.
            await Assert.That(resource.GetProperty("feature").GetString())
                .IsEqualTo("MAP_MATERIAL");
        }

        JsonElement sampler = composite.Single(
            resource => resource.GetProperty("name").GetString() == "compositeSampler");
        JsonElement texture = composite.Single(
            resource => resource.GetProperty("name").GetString() == "compositeTexture");
        await Assert.That(sampler.GetProperty("vulkan").GetProperty("binding").GetUInt32())
            .IsEqualTo(CompositeVulkanSamplerBinding);
        await Assert.That(texture.GetProperty("vulkan").GetProperty("binding").GetUInt32())
            .IsEqualTo(CompositeVulkanTextureBinding);
        await Assert.That(sampler.GetProperty("d3d").GetProperty("register").GetInt32())
            .IsEqualTo(CompositeMetalSamplerIndex);
        await Assert.That(texture.GetProperty("d3d").GetProperty("register").GetInt32())
            .IsEqualTo(CompositeMetalTextureIndex);

        // The permutation budget must be untouched: the whole point of one
        // universal slot is that it costs no shader variants.
        JsonElement budget = manifest.RootElement
            .GetProperty("permutationBudgets")
            .EnumerateArray()
            .Single(entry =>
                entry.GetProperty("family").GetString() == "mesh" &&
                entry.GetProperty("stage").GetString() == "fragment");
        await Assert.That(budget.GetProperty("maxPermutations").GetInt32()).IsEqualTo(8);
    }

    [Test]
    public async Task CheckedReflectionsCarryTheCompositeSlotOnlyWhereMaterialMapsAre()
    {
        string root = FindRepositoryRoot();
        string checkedRoot = Path.Combine(root, "eng", "shaders", "checked");

        (string Artifact, bool Expected)[] cases =
        [
            ("mesh.fragment.uv+material", true),
            ("mesh.fragment.uv+material+normal", true),
            ("mesh.fragment.uv", false),
            ("mesh.fragment.uv+normal", false),
            ("mesh.fragment", false),
            ("mesh.volume.fragment", false),
        ];

        foreach ((string artifact, bool expected) in cases)
        {
            HashSet<string> resources = await ReadReflectionResourcesAsync(
                Path.Combine(checkedRoot, $"{artifact}.reflection.json"));

            // Proves the reflection was really read rather than coming back empty,
            // which would satisfy every negative case vacuously.
            await Assert.That(resources).Contains("surfaceParameters");

            await Assert.That(resources.Contains("compositeTexture"))
                .IsEqualTo(expected)
                .Because($"{artifact} composite texture presence");
            await Assert.That(resources.Contains("compositeSampler"))
                .IsEqualTo(expected)
                .Because($"{artifact} composite sampler presence");
        }
    }

    [Test]
    public async Task CheckedMetalSourceBindsTheCompositeSlotAtItsMappedIndices()
    {
        string root = FindRepositoryRoot();
        string metal = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "shaders",
            "checked",
            "mesh.fragment.uv+material.metal"));

        // Metal has no register syntax, so the index is baked into the entry point
        // signature. This is the only check that runs on a non-macOS host and can
        // still catch a Metal index that disagrees with the managed mapping.
        Match texture = Regex.Match(
            metal,
            @"compositeTexture_\d+\s*\[\[texture\((?<index>\d+)\)\]\]",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        Match sampler = Regex.Match(
            metal,
            @"compositeSampler_\d+\s*\[\[sampler\((?<index>\d+)\)\]\]",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        await Assert.That(texture.Success).IsTrue();
        await Assert.That(sampler.Success).IsTrue();
        await Assert.That(int.Parse(texture.Groups["index"].Value, CultureInfo.InvariantCulture))
            .IsEqualTo(CompositeMetalTextureIndex);
        await Assert.That(int.Parse(sampler.Groups["index"].Value, CultureInfo.InvariantCulture))
            .IsEqualTo(CompositeMetalSamplerIndex);

        // The operator dispatch has to be in the shipped source too: a Metal binary
        // that sampled the second image but always multiplied would satisfy the
        // index checks above and still render the wrong picture for mix. Slang
        // lowers the four branches to comparisons against the rounded operator id,
        // so every id from 1 to 4 must appear as a literal beside compositeControls.
        await Assert.That(metal).Contains("compositeControls_0");
        Match operation = Regex.Match(
            metal,
            @"uint (?<name>operation_\d+) = uint\(round\(_S\d+\.y\)\);",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        await Assert.That(operation.Success)
            .IsTrue()
            .Because("the Metal source reads the composite operator id from compositeControls.y");
        string operationName = operation.Groups["name"].Value;
        foreach (int operatorId in (int[])[1, 2, 3, 4])
        {
            await Assert.That(metal)
                .Contains($"{operationName} == {operatorId}U")
                .Because($"the Metal source dispatches composite operator {operatorId}");
        }

        // The composite samples through its own UDIM bit in compositeControls.w.
        // Reusing the driven slot's bit sampled a plain image through the atlas
        // path, which reads its first texel as tile metadata.
        Match compositeUdim = Regex.Match(
            metal,
            @"bool _S\d+ = \(_S\d+\.w\) >= 0\.5f;",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        await Assert.That(compositeUdim.Success)
            .IsTrue()
            .Because("the composite samples through compositeControls.w, not the slot's UDIM bit");

        string volume = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "shaders",
            "checked",
            "mesh.volume.fragment.metal"));
        await Assert.That(volume).DoesNotContain("compositeTexture");
        await Assert.That(volume).DoesNotContain("compositeSampler");
    }

    [Test]
    public async Task MaterialBindingLayoutDeclaresTheCompositeSlotForEveryMaterialPermutation()
    {
        foreach (SilkShaderFeatures features in (SilkShaderFeatures[])
        [
            SilkShaderFeatures.Uv | SilkShaderFeatures.BaseColorMap,
            SilkShaderFeatures.Uv | SilkShaderFeatures.BaseColorMap | SilkShaderFeatures.NormalMap,
            SilkShaderFeatures.Uv | SilkShaderFeatures.MetallicMap,
        ])
        {
            SilkBindingLayoutDescriptor layout =
                new SilkShaderPermutationId(features).CreateMeshBindingLayout();
            (uint Binding, SilkBindingKind Kind)[] slots = layout.MaterialSlots
                .Select(slot => (slot.Binding, slot.Kind))
                .ToArray();
            await Assert.That(slots)
                .Contains((CompositeVulkanSamplerBinding, SilkBindingKind.Sampler));
            await Assert.That(slots)
                .Contains((CompositeVulkanTextureBinding, SilkBindingKind.SampledTexture));
        }

        // A permutation with no material maps at all declares nothing, so the slot
        // is not simply added everywhere.
        SilkBindingLayoutDescriptor uvOnly =
            new SilkShaderPermutationId(SilkShaderFeatures.Uv).CreateMeshBindingLayout();
        await Assert.That(uvOnly.MaterialSlots
                .Select(slot => slot.Binding)
                .Contains(CompositeVulkanTextureBinding))
            .IsFalse();
    }

    private static async Task<HashSet<string>> ReadReflectionResourcesAsync(string path)
    {
        using JsonDocument reflection = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement resource in
            reflection.RootElement.GetProperty("resources").EnumerateArray())
        {
            names.Add(resource.GetProperty("name").GetString()!);
        }
        return names;
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
