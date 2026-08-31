// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Pins the Metal half of the sampled density volume without a Metal device.
/// </summary>
/// <remarks>
/// The Metal sampled-volume path has no executed pixel evidence: it can only run on
/// macOS, and no render job captures a Metal volume image yet. That is exactly why these
/// checks exist and why they deliberately run everywhere. The failure they guard against
/// is silent, not loud: <see cref="SilkMeshRenderer"/> selects the sampled-volume pipeline
/// only when the device implements <c>ISilkVolumeTextureGraphicsDevice</c>, so a Metal
/// backend that loses that implementation does not fail -- it renders the proxy at the
/// authored uniform density and produces a plausible image with no volume in it. The
/// checked <c>mesh.volume.fragment.metal</c> source and the argument-index table are
/// files, so the wiring around that source can be proved on any host; only the resulting
/// pixels cannot.
/// </remarks>
public sealed class MetalSampledVolumeConformanceTests
{
    private const string CommandListTypeName =
        "OpenUsd.Rendering.Silk.Metal.MetalSilkGraphicsCommandList";

    // Slang emits the entry-point resources as `[[texture(n)]]` / `[[sampler(n)]]`
    // attributes on the function signature, which is what the Metal encoder must agree
    // with. The reflection JSON records the same allocation, but the generated source is
    // what the driver actually compiles.
    private static readonly Regex VolumeTexturePattern = new(
        @"texture3d<float,\s*access::sample>\s+volumeDensityTexture_\d+\s+\[\[texture\((?<index>\d+)\)\]\]",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex VolumeSamplerPattern = new(
        @"sampler\s+volumeDensitySampler_\d+\s+\[\[sampler\((?<index>\d+)\)\]\]",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AnyTexture3DPattern = new(
        @"texture3d<",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Test]
    public async Task TheMetalBackendImplementsTheSampledVolumeContracts()
    {
        // Resolved by name rather than with typeof, because the Metal device and its
        // command list are annotated for macOS and this assertion has to hold on the
        // Windows and Linux jobs that are the only ones running it today.
        Assembly metal = typeof(MetalShaderResourceIndices).Assembly;
        Assembly silk = typeof(SilkMeshRenderer).Assembly;
        Type volumeDevice = RequireType(silk, "OpenUsd.Rendering.Silk.ISilkVolumeTextureGraphicsDevice");
        Type volumeCommands = RequireType(silk, "OpenUsd.Rendering.Silk.ISilkVolumeTextureCommandList");
        Type device = RequireType(metal, "OpenUsd.Rendering.Silk.Metal.MetalSilkGraphicsDevice");
        Type commandList = RequireType(metal, CommandListTypeName);

        await Assert.That(volumeDevice.IsAssignableFrom(device)).IsTrue();
        await Assert.That(volumeCommands.IsAssignableFrom(commandList)).IsTrue();

        // The device implements the creation contract explicitly, so the interface map is
        // the only place the method is visible; asserting the shape here keeps a rename
        // from quietly reducing this class to an assignability check.
        InterfaceMapping mapping = device.GetInterfaceMap(volumeDevice);
        await Assert.That(mapping.TargetMethods.Length).IsEqualTo(1);
        MethodInfo create = mapping.TargetMethods[0];
        Type[] parameters = [.. create.GetParameters().Select(static parameter => parameter.ParameterType)];
        await Assert.That(parameters).IsEquivalentTo(
            [typeof(uint), typeof(uint), typeof(uint), typeof(SilkTextureFormat)]);
    }

    [Test]
    public async Task TheCheckedMetalVolumeSourceUsesTheEncodedArgumentIndices()
    {
        string source = ReadCheckedMetalSource("mesh.volume.fragment.metal");
        Match texture = VolumeTexturePattern.Match(source);
        Match sampler = VolumeSamplerPattern.Match(source);

        await Assert.That(texture.Success).IsTrue();
        await Assert.That(sampler.Success).IsTrue();
        await Assert.That(ParseIndex(texture)).IsEqualTo(
            MetalShaderResourceIndices.Map(
                SilkBindingKind.SampledTexture,
                SilkBindingLayoutDescriptor.VolumeDensityTextureBinding));
        await Assert.That(ParseIndex(sampler)).IsEqualTo(
            MetalShaderResourceIndices.Map(
                SilkBindingKind.Sampler,
                SilkBindingLayoutDescriptor.VolumeSamplerBinding));
    }

    [Test]
    public async Task OnlyTheVolumeProgramDeclaresAMetalTexture3D()
    {
        // The Metal mirror of the shader-isolation rule the D3D12 root signature forces.
        // Metal binds by argument index with no root signature to reject a stray
        // declaration, so nothing in the API would report an ordinary mesh fragment that
        // started declaring a texture3d; the checked sources are the only witness.
        string checkedRoot = Path.Combine(FindRepositoryRoot(), "eng", "shaders", "checked");
        string[] metalSources = [.. Directory
            .GetFiles(checkedRoot, "mesh.*.metal")
            .Order(StringComparer.Ordinal)];
        string volumeSource = Path.Combine(checkedRoot, "mesh.volume.fragment.metal");

        // Five fragment permutations, three vertex permutations, and the sampled-volume
        // program. A glob that matched fewer files would make the loop vacuous.
        await Assert.That(metalSources.Length).IsEqualTo(9);
        await Assert.That(metalSources).Contains(volumeSource);

        var declaringPrograms = new List<string>();
        foreach (string metalSource in metalSources)
        {
            string text = File.ReadAllText(metalSource);
            // Every checked Metal source names its own entry point, so this also proves
            // the enumeration read real generated sources rather than empty files.
            await Assert.That(text).Contains("[[");
            if (AnyTexture3DPattern.IsMatch(text))
            {
                declaringPrograms.Add(metalSource);
            }
        }

        await Assert.That(declaringPrograms).IsEquivalentTo([volumeSource]);
    }

    private static uint ParseIndex(Match match) =>
        uint.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);

    private static Type RequireType(Assembly assembly, string name) =>
        assembly.GetType(name, throwOnError: false) ??
        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{name}' is missing from '{assembly.GetName().Name}'."));

    private static string ReadCheckedMetalSource(string artifactName) =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "shaders",
            "checked",
            artifactName));

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
