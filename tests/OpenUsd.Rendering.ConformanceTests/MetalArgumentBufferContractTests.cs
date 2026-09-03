// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text.RegularExpressions;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Pins the Metal argument-buffer refusal against the checked Metal sources.
/// </summary>
/// <remarks>
/// The bug these guard against is silent on every axis. Metal does not report a fragment
/// argument buffer that no shader reads, it does not report a direct texture argument left
/// unbound, and <c>MTLArgumentDescriptor</c> happily accepts <c>Type2D</c> for a slot the
/// shader declares as <c>texture3d</c>. A Tier 2 device would therefore have encoded the
/// sampled density grid into a buffer nobody reads, left <c>[[texture(9)]]</c> unset, and
/// still produced a plausible volume image -- the exact class of failure the dedicated
/// volume program exists to prevent. None of that can be observed from a Metal API result,
/// so it is checked against the generated sources instead, on every host.
/// </remarks>
public sealed class MetalArgumentBufferContractTests
{
    // Slang emits entry-point resources as attributes on the function signature. An
    // argument buffer would appear as a struct pointer parameter instead, so the presence
    // of these attributes is the direct evidence that the checked programs bind directly.
    private static readonly Regex DirectTexturePattern = new(
        @"\[\[texture\((?<index>\d+)\)\]\]",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex DirectSamplerPattern = new(
        @"\[\[sampler\((?<index>\d+)\)\]\]",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Test]
    public async Task TheSampledVolumeLayoutIsRefusedAnArgumentBuffer()
    {
        bool rejected = MetalArgumentBufferCompatibility.TryGetRejectionReason(
            SilkBindingLayoutDescriptor.SampledVolumeParameters,
            out string reason);

        await Assert.That(rejected).IsTrue();
        // The reason has to name the binding and the texture3d, because the operator
        // acting on it needs to know it is a type mismatch and not a missing feature.
        await Assert.That(reason).Contains(
            SilkBindingLayoutDescriptor.VolumeDensityTextureBinding.ToString(
                CultureInfo.InvariantCulture));
        await Assert.That(reason).Contains("texture3d");
    }

    [Test]
    public async Task TheVolumeRefusalSurvivesTheGeneralRuleBeingLifted()
    {
        // The volume rejection must not depend on the blanket direct-argument rule that
        // currently subsumes it. A layout carrying only the volume texture -- no sampler,
        // nothing else that would trip the general rule -- must still be refused, and the
        // reason must be the type mismatch rather than the blanket one.
        var volumeOnly = SilkBindingLayoutDescriptor.SceneParameters with
        {
            MaterialSlots =
            [
                new SilkBindingSlot(
                    0,
                    SilkBindingLayoutDescriptor.VolumeDensityTextureBinding,
                    SilkBindingKind.SampledTexture,
                    0,
                    SilkShaderStageVisibility.Fragment)
            ]
        };

        bool rejected = MetalArgumentBufferCompatibility.TryGetRejectionReason(
            volumeOnly,
            out string reason);

        await Assert.That(rejected).IsTrue();
        await Assert.That(reason).Contains("texture3d");
    }

    [Test]
    public async Task EveryMaterialTextureLayoutIsRefusedWhileCheckedProgramsBindDirectly()
    {
        // Not only the volume: while no checked program declares an argument buffer, any
        // layout carrying a texture or sampler must be refused, or the encoder would write
        // a table for a 2D material draw whose shader reads direct arguments.
        var materialLayout = SilkBindingLayoutDescriptor.SceneParameters with
        {
            MaterialSlots =
            [
                new SilkBindingSlot(
                    0,
                    SilkBindingLayoutDescriptor.BaseColorSamplerBinding,
                    SilkBindingKind.Sampler,
                    0,
                    SilkShaderStageVisibility.Fragment),
                new SilkBindingSlot(
                    0,
                    SilkBindingLayoutDescriptor.BaseColorTextureBinding,
                    SilkBindingKind.SampledTexture,
                    0,
                    SilkShaderStageVisibility.Fragment)
            ]
        };

        bool rejected = MetalArgumentBufferCompatibility.TryGetRejectionReason(
            materialLayout,
            out string reason);

        await Assert.That(MetalArgumentBufferCompatibility.CheckedProgramsUseDirectArguments)
            .IsTrue();
        await Assert.That(rejected).IsTrue();
        await Assert.That(reason).Contains("argument buffer");
    }

    [Test]
    public async Task ALayoutWithNoTextureOrSamplerIsNotRefused()
    {
        // The negative control. Without it every assertion above could pass because the
        // predicate refuses everything unconditionally, which would also be wrong: the
        // buffer-only depth-only shadow caster layout never reaches the table path and
        // must not be reported as an argument-buffer incompatibility. Every mesh layout
        // now carries the shadow atlas and its sampler, so the shadow caster layout is
        // the buffer-only one this control needs.
        bool rejected = MetalArgumentBufferCompatibility.TryGetRejectionReason(
            SilkBindingLayoutDescriptor.ShadowParameters,
            out _);

        await Assert.That(rejected).IsFalse();
    }

    [Test]
    public async Task TheAdvertisedCapabilityMatchesTheRefusal()
    {
        // The capability is documented as "material textures can be bound through a
        // descriptor-indexed table". While every texture-bearing layout is refused, a
        // device advertising it would be lying to the viewer diagnostics that print it.
        bool rejected = MetalArgumentBufferCompatibility.TryGetCapabilityRejectionReason(
            out string reason);

        await Assert.That(rejected)
            .IsEqualTo(MetalArgumentBufferCompatibility.CheckedProgramsUseDirectArguments);
        await Assert.That(reason).Contains("argument buffer");
    }

    [Test]
    public async Task NoCheckedMetalProgramDeclaresAnArgumentBuffer()
    {
        // The source of truth for the switch above. If a checked program ever adopts an
        // argument buffer, this fails and points at the switch that has to change with it,
        // rather than letting the refusal quietly outlive its reason.
        string checkedRoot = Path.Combine(FindRepositoryRoot(), "eng", "shaders", "checked");
        string[] sources = [.. Directory
            .GetFiles(checkedRoot, "*.metal")
            .Order(StringComparer.Ordinal)];

        // Twenty-seven checked programs expand from the manifest, including the
        // two display-transform programs, the subprim pick vertex stage, the two
        // unbiased whole-resource stages, and the occluded selection-mask
        // fragment stage the one-pass x-ray composite reads its second
        // silhouette channel from. A glob that silently matched fewer files
        // would make the loop below vacuous.
        await Assert.That(sources.Length).IsEqualTo(27);

        var directBindingPrograms = new List<string>();
        foreach (string source in sources)
        {
            string text = await File.ReadAllTextAsync(source);
            // Slang names an argument-buffer parameter with the same [[buffer(n)]]
            // attribute it uses for a plain constant buffer, so the attribute alone proves
            // nothing. What does prove it is the direct texture and sampler attributes:
            // a program that receives its textures through an argument buffer cannot also
            // declare them as direct arguments.
            bool bindsDirectly =
                DirectTexturePattern.IsMatch(text) || DirectSamplerPattern.IsMatch(text);
            if (bindsDirectly)
            {
                directBindingPrograms.Add(Path.GetFileName(source));
            }
        }

        // Every checked program that consumes a texture or sampler at all consumes it
        // directly. The volume program is one of them, which is what makes the Tier 2
        // path unusable for it.
        await Assert.That(directBindingPrograms).Contains("mesh.volume.fragment.metal");
        await Assert.That(directBindingPrograms).Contains("mesh.fragment.uv+material+normal.metal");
        await Assert.That(MetalArgumentBufferCompatibility.CheckedProgramsUseDirectArguments)
            .IsTrue();
    }

    [Test]
    public async Task TheCheckedVolumeProgramDeclaresATexture3DAtTheEncodedIndex()
    {
        // Ties the refusal to the concrete thing it protects: the checked volume program
        // declares a texture3d, and the direct index it declares is the one the Metal
        // encoder resolves the abstract binding to.
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "shaders",
            "checked",
            "mesh.volume.fragment.metal"));
        Match texture = new Regex(
            @"texture3d<float,\s*access::sample>\s+volumeDensityTexture_\d+\s+\[\[texture\((?<index>\d+)\)\]\]",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5)).Match(source);

        await Assert.That(texture.Success).IsTrue();
        await Assert.That(uint.Parse(
                texture.Groups["index"].Value,
                CultureInfo.InvariantCulture))
            .IsEqualTo(MetalShaderResourceIndices.Map(
                SilkBindingKind.SampledTexture,
                SilkBindingLayoutDescriptor.VolumeDensityTextureBinding));
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
