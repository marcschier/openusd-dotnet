// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the space the shadow bias works in, in the checked mesh shader source and
/// in the emitted Metal it produces.
/// </summary>
/// <remarks>
/// <para>
/// The shadow bias offsets a receiver along its normal and then projects the
/// result with a world-to-light matrix, so the normal has to be in world space.
/// The interpolated vertex normal reaches the fragment stage in object space --
/// the checked mesh vertex stage passes <c>input.normal</c> through unchanged --
/// so feeding it to the bias makes the offset point somewhere else entirely under
/// any rotation or non-uniform scale.
/// </para>
/// <para>
/// That defect is invisible to an execution gate at conformance resolution: the
/// constant depth bias absorbs the error for every receiver a 64-pixel frame can
/// resolve, so a rendered gate passes with the object-space normal and would
/// report nothing until an asset with a rotated receiver and a tight bias reached
/// a user. The derivation is therefore pinned in source, exactly as the Vulkan
/// sampled-depth barrier aspect is, rather than left to a gate that cannot see
/// it. The rendered gate in <c>SilkShadowConformance</c> still exercises a
/// rotated, non-uniformly scaled receiver end to end.
/// </para>
/// </remarks>
public sealed class SilkShadowNormalSpaceSourceContractTests
{
    [Test]
    public async Task TheShadowBiasReceivesAWorldSpaceGeometricNormal()
    {
        string source = await ReadMeshShaderAsync();

        // Derived from the world position's screen-space derivatives, which
        // applies the object-to-world transform's inverse transpose exactly and
        // needs no normal matrix in the pinned 80-byte instance block.
        await Assert.That(source).Contains(
            "float3 worldGeometricNormal = cross(ddx(worldPosition), ddy(worldPosition));");

        // Computed in uniform control flow: a derivative taken inside the
        // per-light branch is undefined.
        int derivativeIndex = source.IndexOf(
            "cross(ddx(worldPosition), ddy(worldPosition))",
            StringComparison.Ordinal);
        int lightLoopIndex = source.IndexOf(
            "for (uint lightIndex = 0u;",
            StringComparison.Ordinal);
        await Assert.That(derivativeIndex).IsGreaterThan(0);
        await Assert.That(lightLoopIndex).IsGreaterThan(derivativeIndex)
            .Because(
                "The world geometric normal must be derived before the per-light " +
                "loop, because a derivative inside it is undefined.");
    }

    [Test]
    public async Task TheShadowBiasIsNotGivenTheInterpolatedObjectSpaceNormal()
    {
        string source = await ReadMeshShaderAsync();
        Match call = Regex.Match(
            source,
            @"ResolveShadowVisibility\(\s*frame,\s*shadowSlot,\s*worldPosition,\s*(?<normal>\w+),",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        await Assert.That(call.Success)
            .IsTrue()
            .Because("The shadow resolution call must be recognisable in source.");
        await Assert.That(call.Groups["normal"].Value)
            .IsEqualTo("worldGeometricNormal")
            .Because(
                "Passing the interpolated 'normal' offsets a world position along " +
                "an object-space direction, which is wrong under any rotation or " +
                "non-uniform scale.");
    }

    [Test]
    public async Task TheShadowNormalIsOrientedByTheLightRatherThanTheCamera()
    {
        string source = await ReadMeshShaderAsync();

        // A derivative-based normal carries the sign of the triangle's screen
        // winding. Orienting it toward the eye is wrong here: this renderer's
        // visible faces point away from a light behind the camera, so an
        // eye-oriented normal offsets the receiver into the surface and makes it
        // shadow itself. The light resolves the sign correctly by construction.
        await Assert.That(source).Contains(
            "float3 orientedNormal = dot(worldNormal, lightDirection) < 0.0");
        await Assert.That(source).DoesNotContain("dot(worldGeometricNormal, sceneEye)");
    }

    [Test]
    public async Task TheEmittedMetalShaderCarriesTheSameDerivation()
    {
        // The checked Metal source is the artifact an unproven backend would ship,
        // so the derivation is pinned there too rather than only in the input.
        string metal = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "shaders",
            "checked",
            "mesh.fragment.metal"));

        await Assert.That(metal).Contains("dfdx(worldPosition");
        await Assert.That(metal).Contains("dfdy(worldPosition");
    }

    private static Task<string> ReadMeshShaderAsync() =>
        File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "shaders",
            "sources",
            "mesh.slang"));

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
