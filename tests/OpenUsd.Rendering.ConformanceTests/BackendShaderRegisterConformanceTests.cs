// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text.Json;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Pins the D3D12 and Metal binding translations to the checked shader reflection.
/// </summary>
/// <remarks>
/// The abstract binding numbers used by <see cref="SilkBindingLayoutDescriptor"/> and the
/// HLSL registers declared by the checked shaders were allocated independently, so the
/// backends translate between them. Nothing in either API reports a mismatch: a sampler
/// bound to the wrong register simply reads whatever descriptor happens to sit there.
/// That is exactly how the metallic slot was first added with abstract bindings 14/15
/// and no translation, which resolved to <c>s14</c>/<c>t13</c> instead of the declared
/// <c>s5</c>/<c>t4</c>. These tests compare the translation against the checked
/// <c>*.reflection.json</c>, so any future binding that skips the tables fails here.
/// </remarks>
public sealed class BackendShaderRegisterConformanceTests
{
    private static readonly (string Resource, SilkBindingKind Kind, uint Binding)[] MeshBindings =
    [
        ("baseColorSampler", SilkBindingKind.Sampler, 1),
        ("normalSampler", SilkBindingKind.Sampler, 10),
        ("roughnessMetallicSampler", SilkBindingKind.Sampler, 11),
        ("emissiveSampler", SilkBindingKind.Sampler, 12),
        ("metallicSampler", SilkBindingKind.Sampler, 14),
        ("opacitySampler", SilkBindingKind.Sampler, 16),
        ("occlusionSampler", SilkBindingKind.Sampler, 18),
        ("specularColorSampler", SilkBindingKind.Sampler, 20),
        ("clearcoatSampler", SilkBindingKind.Sampler, 22),
        ("clearcoatRoughnessSampler", SilkBindingKind.Sampler, 24),
        ("iorSampler", SilkBindingKind.Sampler, 26),
        ("baseColorTexture", SilkBindingKind.SampledTexture, 2),
        ("normalTexture", SilkBindingKind.SampledTexture, 3),
        ("roughnessMetallicTexture", SilkBindingKind.SampledTexture, 4),
        ("emissiveTexture", SilkBindingKind.SampledTexture, 5),
        ("metallicTexture", SilkBindingKind.SampledTexture, 15),
        ("opacityTexture", SilkBindingKind.SampledTexture, 17),
        ("occlusionTexture", SilkBindingKind.SampledTexture, 19),
        ("specularColorTexture", SilkBindingKind.SampledTexture, 21),
        ("clearcoatTexture", SilkBindingKind.SampledTexture, 23),
        ("clearcoatRoughnessTexture", SilkBindingKind.SampledTexture, 25),
        ("iorTexture", SilkBindingKind.SampledTexture, 27),
    ];

    // The sampled density volume is its own checked fragment program, because only
    // its binding layout declares the 3D texture and its sampler. Its registers are
    // still part of the same allocation, so they are pinned the same way.
    private static readonly (string Resource, SilkBindingKind Kind, uint Binding)[] VolumeBindings =
    [
        ("volumeDensitySampler", SilkBindingKind.Sampler, 13),
        ("volumeDensityTexture", SilkBindingKind.SampledTexture, 9),
    ];

    [Test]
    public async Task D3D12ShaderRegistersMatchTheCheckedReflection()
    {
        Dictionary<string, (string RegisterClass, uint Register, uint Binding)> declared =
            ReadCheckedMeshResources();
        foreach ((string resource, (string RegisterClass, uint Register, uint Binding) volume)
            in ReadCheckedVolumeResources())
        {
            declared[resource] = volume;
        }

        foreach ((string resource, SilkBindingKind kind, uint binding) in
            MeshBindings.Concat(VolumeBindings))
        {
            (string registerClass, uint register, uint reflectedBinding) = declared[resource];

            // The reflection is the single source of truth for both numbers, so this
            // also proves the abstract binding table above did not drift.
            await Assert.That(reflectedBinding).IsEqualTo(binding);
            await Assert.That(registerClass).IsEqualTo(
                kind == SilkBindingKind.Sampler ? "s" : "t");
            await Assert.That(
                D3D12ShaderRegisters.Map(
                    new SilkBindingSlot(0, binding, kind, 0, SilkShaderStageVisibility.Fragment)))
                .IsEqualTo(register);
        }
    }

    [Test]
    public async Task TheSampledVolumeProgramIsTheOnlyMeshFragmentDeclaringTheVolumeTexture()
    {
        // The concrete D3D12 failure this split exists to prevent: a root signature
        // must declare every resource its shader binary references, so a volume
        // texture compiled into the shared mesh fragment binary makes every ordinary
        // mesh pipeline fail to create. Checking one permutation would not prove that,
        // because the mesh fragment family expands into several checked binaries and
        // the manifest could reintroduce the resource into any one of them. Every
        // checked mesh reflection is therefore enumerated from disk.
        string checkedRoot = Path.Combine(FindRepositoryRoot(), "eng", "shaders", "checked");
        string[] meshReflections = Directory
            .GetFiles(checkedRoot, "mesh.*.reflection.json")
            .Order(StringComparer.Ordinal)
            .ToArray();
        string volumeReflection = Path.Combine(checkedRoot, "mesh.volume.fragment.reflection.json");

        // Five fragment permutations and three vertex permutations expand from the
        // manifest, plus the sampled-volume program. A glob that silently matched
        // fewer files would make the loop below vacuous.
        await Assert.That(meshReflections.Length).IsEqualTo(9);
        await Assert.That(meshReflections).Contains(volumeReflection);

        var declaringPrograms = new List<string>();
        foreach (string reflection in meshReflections)
        {
            Dictionary<string, (string RegisterClass, uint Register, uint Binding)> resources =
                ReadCheckedResources(Path.GetFileName(reflection));
            bool declaresAnyVolumeResource = false;
            foreach ((string resource, _, _) in VolumeBindings)
            {
                declaresAnyVolumeResource |= resources.ContainsKey(resource);
            }
            if (declaresAnyVolumeResource)
            {
                declaringPrograms.Add(reflection);
                // A program that declares one volume resource must declare both: a
                // texture with no sampler, or the reverse, cannot be bound at all.
                foreach ((string resource, _, _) in VolumeBindings)
                {
                    await Assert.That(resources.ContainsKey(resource)).IsTrue();
                }
            }
            // Every mesh fragment, volume or not, still reads the shared surface and
            // frame blocks, so this also proves the enumeration found real reflections
            // rather than empty documents.
            if (Path.GetFileName(reflection).StartsWith("mesh.vertex", StringComparison.Ordinal))
            {
                await Assert.That(resources.ContainsKey("frameParameters")).IsTrue();
            }
            else
            {
                await Assert.That(resources.ContainsKey("surfaceParameters")).IsTrue();
                await Assert.That(resources.ContainsKey("frameParameters")).IsTrue();
            }
        }

        await Assert.That(declaringPrograms).IsEquivalentTo([volumeReflection]);
    }

    [Test]
    public async Task MetalResourceIndicesMatchTheCheckedReflection()
    {
        Dictionary<string, (string RegisterClass, uint Register, uint Binding)> declared =
            ReadCheckedMeshResources();
        foreach ((string resource, (string RegisterClass, uint Register, uint Binding) volume)
            in ReadCheckedVolumeResources())
        {
            declared[resource] = volume;
        }

        foreach ((string resource, SilkBindingKind kind, uint binding) in
            MeshBindings.Concat(VolumeBindings))
        {
            (_, uint register, uint reflectedBinding) = declared[resource];

            // Slang derives Metal argument indices from the same HLSL registers, so
            // the Metal encoder must agree with the reflected register for its class.
            await Assert.That(reflectedBinding).IsEqualTo(binding);
            await Assert.That(MetalShaderResourceIndices.Map(kind, binding))
                .IsEqualTo(register);
        }
    }

    [Test]
    public async Task RoughnessAndMetallicResolveToDistinctBackendRegisters()
    {
        // The concrete failure this whole slice exists to prevent: two live material
        // textures collapsing onto one register pair.
        (uint roughnessSampler, uint roughnessTexture) =
            SilkBindingLayoutDescriptor.GetMaterialTextureBindings(
                SilkMaterialParameter.Roughness);
        (uint metallicSampler, uint metallicTexture) =
            SilkBindingLayoutDescriptor.GetMaterialTextureBindings(
                SilkMaterialParameter.Metallic);

        await Assert.That(roughnessSampler).IsNotEqualTo(metallicSampler);
        await Assert.That(roughnessTexture).IsNotEqualTo(metallicTexture);
        await Assert.That(
            D3D12ShaderRegisters.Map(new SilkBindingSlot(
                0, roughnessTexture, SilkBindingKind.SampledTexture, 0, SilkShaderStageVisibility.Fragment)))
            .IsNotEqualTo(
                D3D12ShaderRegisters.Map(new SilkBindingSlot(
                    0, metallicTexture, SilkBindingKind.SampledTexture, 0, SilkShaderStageVisibility.Fragment)));
        await Assert.That(MetalShaderResourceIndices.Map(
                SilkBindingKind.Sampler, roughnessSampler))
            .IsNotEqualTo(MetalShaderResourceIndices.Map(
                SilkBindingKind.Sampler, metallicSampler));
    }

    private static Dictionary<
        string, (string RegisterClass, uint Register, uint Binding)> ReadCheckedMeshResources() =>
        ReadCheckedResources("mesh.fragment.uv+material+normal.reflection.json");

    private static Dictionary<
        string, (string RegisterClass, uint Register, uint Binding)> ReadCheckedVolumeResources() =>
        ReadCheckedResources("mesh.volume.fragment.reflection.json");

    private static Dictionary<
        string, (string RegisterClass, uint Register, uint Binding)> ReadCheckedResources(
        string artifactName)
    {
        // The universal material shader with normal mapping declares every 2D slot
        // at once, which is what makes a collision visible.
        string path = Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "shaders",
            "checked",
            artifactName);
        using JsonDocument reflection = JsonDocument.Parse(File.ReadAllBytes(path));
        Dictionary<string, (string RegisterClass, uint Register, uint Binding)> declared =
            new(StringComparer.Ordinal);
        foreach (JsonElement resource in reflection.RootElement.GetProperty("resources").EnumerateArray())
        {
            JsonElement bindings = resource.GetProperty("bindings");
            if (!bindings.TryGetProperty("d3d", out JsonElement d3d) ||
                !bindings.TryGetProperty("vulkan", out JsonElement vulkan))
            {
                continue;
            }
            declared[resource.GetProperty("name").GetString()!] = (
                d3d.GetProperty("registerClass").GetString()!,
                d3d.GetProperty("register").GetUInt32(),
                vulkan.GetProperty("binding").GetUInt32());
        }
        if (declared.Count == 0)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The checked reflection at '{path}' declared no bindable resources."));
        }
        return declared;
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
