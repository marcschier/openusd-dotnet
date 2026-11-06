// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkVolumeTextureExtentTests
{
    [Test]
    public async Task CameraRaysUseEyeSpaceAndClipSegmentsWithRelativeBoundaryTolerance()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpenUsd.slnx")))
        {
            root = root.Parent;
        }
        string source = await File.ReadAllTextAsync(Path.Combine(
            root?.FullName ?? throw new InvalidOperationException("Could not locate repository root."),
            "eng", "shaders", "sources", "mesh.slang"));
        await Assert.That(source).Contains("objectToEye = mul(frame.clipToEye, instance.objectToClip)");
        await Assert.That(source).Contains("dot(input.volumeEyeToObjectRow0, eyeDirection)");
        await Assert.That(source).DoesNotContain("worldCameraPosition - objectTranslation");
        await Assert.That(source).DoesNotContain("orthographicWorldDistance");
        await Assert.That(source).Contains("float boundaryTolerance = (rayExit - rayEntry) * 1e-4;");
        await Assert.That(source).DoesNotContain("max(1e-4, (rayExit - rayEntry)");
        await Assert.That(source).Contains("dot(plane.xyz, eyeRayOrigin) + plane.w");
        await Assert.That(source).Contains("startDistance = max(startDistance, -offset / slope)");
        await Assert.That(source).Contains("endDistance = min(endDistance, -offset / slope)");
        int metalFrame = source.IndexOf("struct FragmentFrameParameters", StringComparison.Ordinal);
        int metalFrameEnd = source.IndexOf("struct FragmentDomeParameters", metalFrame, StringComparison.Ordinal);
        await Assert.That(source[metalFrame..metalFrameEnd]).Contains("float4x4 clipToEye;");
        await Assert.That(source).Contains("frame.clipToEye = frameParameters.Load<float4x4>(0);");
    }

    [Test]
    public async Task SlabIntersectionPreservesSmallNonzeroDirectionsAndRejectsOnlyParallelOutsideAxes()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpenUsd.slnx")))
        {
            root = root.Parent;
        }
        string source = await File.ReadAllTextAsync(Path.Combine(
            root?.FullName ?? throw new InvalidOperationException("Could not locate repository root."),
            "eng", "shaders", "sources", "mesh.slang"));
        int start = source.IndexOf("bool IntersectVolumeBounds(", StringComparison.Ordinal);
        int end = source.IndexOf("float SampleVolumeDensity(", start, StringComparison.Ordinal);
        string slab = source[start..end];
        await Assert.That(slab).Contains("if (rayDirection[axis] == 0.0)");
        await Assert.That(slab).Contains("rayOrigin[axis] < -0.5 || rayOrigin[axis] > 0.5");
        await Assert.That(slab).Contains("(-0.5 - rayOrigin[axis]) / rayDirection[axis]");
        await Assert.That(slab).Contains("(0.5 - rayOrigin[axis]) / rayDirection[axis]");
        await Assert.That(slab).DoesNotContain("parallelThreshold");
        await Assert.That(slab).DoesNotContain("safeDirection");
    }

    [Test]
    public async Task CameraRayQuadratureDoesNotClampObliqueChordsToTheAxisLimit()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpenUsd.slnx")))
        {
            root = root.Parent;
        }
        string source = await File.ReadAllTextAsync(Path.Combine(
            root?.FullName ?? throw new InvalidOperationException("Could not locate repository root."),
            "eng", "shaders", "sources", "mesh.slang"));
        int start = source.IndexOf("float SampleVolumeDensity(", StringComparison.Ordinal);
        int end = source.IndexOf("float VolumeClipDistance(", start, StringComparison.Ordinal);
        string integration = source[start..end];
        await Assert.That(integration)
            .Contains("ceil(max(1.0, voxelCount) * localRayLength)");
        await Assert.That(integration).DoesNotContain("clamp(");
        await Assert.That(integration).DoesNotContain("maximumStepCount");
    }

    [Test]
    [Arguments("511,1,1", 511u, 1u, 1u)]
    [Arguments("512,1,1", 512u, 1u, 1u)]
    [Arguments("1,512,1", 1u, 512u, 1u)]
    [Arguments("1,1,512", 1u, 1u, 512u)]
    [Arguments("512,512,512", 512u, 512u, 512u)]
    public async Task EveryAxisAdmitsTheSamplingThreshold(
        string dimensions, uint width, uint height, uint depth)
    {
        await Assert.That(SilkVolumeTextureExtent.Parse(dimensions))
            .IsEqualTo(new SilkVolumeTextureExtent(width, height, depth));
    }

    [Test]
    [Arguments("513,1,1")]
    [Arguments("1,513,1")]
    [Arguments("1,1,513")]
    [Arguments("2048,1,1")]
    [Arguments("1,2048,1")]
    public async Task OversizedAxesRequireExplicitResampling(string dimensions)
    {
        await Assert.That(() => SilkVolumeTextureExtent.Parse(dimensions))
            .Throws<InvalidDataException>();
    }
}
