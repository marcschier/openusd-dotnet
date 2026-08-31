// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Pins which checked fragment program a sampled density volume draws with.
/// </summary>
/// <remarks>
/// The sampled density volume has exactly one checked fragment program, and it is the
/// only mesh fragment binary that declares the 3D density texture. Any volume mesh that
/// cannot use it has no correct pipeline at all, so hdSilk must say so. Quietly falling
/// back to an ordinary mesh pipeline renders the authored uniform density instead of the
/// grid, which is a plausible image that ignores the volume -- the exact failure mode
/// that hid a backend with no density texture behind a passing sampled-versus-uniform
/// gate. These tests drive a real device through
/// <see cref="SilkMeshRenderer.Render(ISilkGraphicsTexture, ISilkGraphicsTexture, SilkMeshRenderOptions)"/>
/// rather than a helper, because the selection only happens on the draw path.
/// </remarks>
[NotInParallel]
public sealed class VolumePipelineSelectionConformanceTests
{
    private const int Width = 64;
    private const int Height = 64;
    private const string MaterialPath = "/World/VolumeMaterial";
    private const string MeshPath = "/World/VolumeProxy";

    [Test]
    public async Task SampledVolumeWithoutMaterialMapsDrawsWithTheDedicatedVolumeProgram()
    {
        // The positive control. Without it the rejection below could pass because the
        // scene never reached the draw path at all.
        using ISilkGraphicsDevice device = CreateDevice();
        using ISilkGraphicsTexture color = CreateColorTarget(device);
        using ISilkGraphicsTexture depth = CreateDepthTarget(device);
        using var renderer = CreateRenderer(device);
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            SilkMeshRendererConformance.CreateFrameCommand(
                Width,
                Height,
                SilkMeshRendererConformance.Identity()),
            CreateVolumeMaterialCommand(includeDiffuseMap: false),
            CreateVolumeProxyMeshCommand());

        SilkMeshRenderResult result = renderer.Render(
            color,
            depth,
            new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1));

        await Assert.That(result.DrawCount).IsEqualTo(1);
    }

    [Test]
    public async Task SampledVolumeWithMaterialMapsIsRejectedRatherThanShadedFlat()
    {
        using ISilkGraphicsDevice device = CreateDevice();
        using ISilkGraphicsTexture color = CreateColorTarget(device);
        using ISilkGraphicsTexture depth = CreateDepthTarget(device);
        using var renderer = CreateRenderer(device);
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            SilkMeshRendererConformance.CreateFrameCommand(
                Width,
                Height,
                SilkMeshRendererConformance.Identity()),
            CreateVolumeMaterialCommand(includeDiffuseMap: true),
            CreateVolumeProxyMeshCommand());

        InvalidDataException exception = (await Assert.That(
            () => renderer.Render(
                color,
                depth,
                new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1)))
            .Throws<InvalidDataException>())!;

        // The message has to name both the prim and the combination, because the whole
        // point is that the operator can act on it instead of wondering why a volume
        // looks uniform.
        await Assert.That(exception.Message).Contains(MeshPath);
        await Assert.That(exception.Message).Contains("sampled density volume");
        await Assert.That(exception.Message).Contains(nameof(SilkShaderFeatures.BaseColorMap));
    }

    /// <summary>
    /// Creates the Vulkan device these tests drive, or reports a capability skip.
    /// </summary>
    /// <remarks>
    /// The contract under test is renderer-neutral -- <see cref="SilkMeshRenderer"/>
    /// refuses the impossible combination before it reaches any backend -- but proving it
    /// still needs a real device, because the selection only happens on the draw path.
    /// Vulkan is the one backend present in all three render jobs, so it is the device
    /// used. A host without it, such as the macOS job where the volume gates run against
    /// Metal, must report that plainly: an environment failure raised as a test failure
    /// would look exactly like hdSilk having stopped rejecting the combination, which is
    /// the opposite of what happened.
    /// </remarks>
    private static VulkanSilkGraphicsDevice CreateDevice()
    {
        try
        {
            return VulkanSilkGraphicsDevice.Create();
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or
                EntryPointNotFoundException or
                PlatformNotSupportedException or
                InvalidOperationException)
        {
            Skip.Test($"No Vulkan device is available on this host: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.", exception);
        }
    }

    /// <summary>
    /// Builds the renderer with a deterministic in-memory 2D decoder.
    /// </summary>
    /// <remarks>
    /// The 2D map here exists only to select a texture feature bit, and decoding it is
    /// not what these tests are about. Supplying the decoder keeps them free of the
    /// native image plugin, so a host without the native runtime still proves the
    /// pipeline-selection contract instead of reporting an unrelated load failure.
    /// </remarks>
    private static SilkMeshRenderer CreateRenderer(ISilkGraphicsDevice device) =>
        new(
            device,
            SilkShaderBinaryFormat.SpirV,
            static (_, _) => new SilkDecodedImage(1, 1, [128, 128, 128, 255]),
            static _ => []);

    private static ISilkGraphicsTexture CreateColorTarget(ISilkGraphicsDevice device) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            Width,
            Height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
    private static ISilkGraphicsTexture CreateDepthTarget(ISilkGraphicsDevice device) =>
        device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
    private static byte[] CreateVolumeProxyMeshCommand() =>
        VolumeCommandAuthoring.CreateVolumeProxyMeshCommand(MeshPath, MaterialPath);

    /// <summary>
    /// Builds the volume material, optionally with a 2D map that has no correct pipeline.
    /// </summary>
    /// <remarks>
    /// Routed through the shared authoring helper rather than kept as a private copy. The
    /// material upsert wire format changes as material features land -- the texture entry
    /// most recently grew by its composite operator and factor -- and two independent
    /// builders drift apart silently, reporting the mismatch as an unrelated truncation
    /// somewhere later in the stream.
    /// </remarks>
    private static byte[] CreateVolumeMaterialCommand(bool includeDiffuseMap)
    {
        string volumeAsset = VolumeCommandAuthoring.WriteDensityGrid(
            EvidenceDirectory,
            "density",
            2,
            2,
            2,
            static (x, y, z) => (((z * 2) + y) * 2 + x) / 8f);
        // A deliberately absent 2D asset: a missing texture degrades to the authored
        // fallback with a diagnostic, so it selects the BaseColorMap feature bit without
        // making the test depend on an image plugin being registered.
        var additional = new List<VolumeCommandAuthoring.TextureSpec>();
        if (includeDiffuseMap)
        {
            additional.Add(new VolumeCommandAuthoring.TextureSpec(
                Path.Combine(EvidenceDirectory, "absent-diffuse.png"),
                SilkMaterialParameter.DiffuseColor,
                "st",
                3,
                SilkTextureChannel.Rgb));
        }

        return VolumeCommandAuthoring.CreateVolumeMaterialCommand(
            MaterialPath,
            authoredDensity: 1,
            volumeAsset,
            "2,2,2",
            additional);
    }

    private static string EvidenceDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "TestResults",
        "volume-pipeline-selection");
}
