// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Gates the sampled density integration against the grid's own Z resolution.
/// </summary>
/// <remarks>
/// The shader integrates the density column with one sample per voxel layer. It used to
/// take a fixed 32 steps, which reconstructs the column exactly only when the sample
/// lattice happens to align with the layers -- and the one checked-in VDB is 32 deep, so
/// the constant was right by coincidence and no gate could see the defect. Modelling the
/// two integrators over a two-layer slab shows what a differently sized grid costs: at
/// depth 96 the fixed lattice steps straight over the slab and integrates it to exactly
/// zero, while sampling at layer centres recovers the exact mean of 2/96. The volume still
/// rendered; it had simply lost the structure.
///
/// These cases author their scene commands directly rather than reading a <c>.vdb</c>,
/// for two reasons. The grid has to be chosen -- no checked-in asset has the resolution
/// that exposes this -- and the gate then needs no native runtime, so it keeps proving the
/// integration on both backends even while the hdSilk delegate is between ABI revisions.
/// </remarks>
[NotInParallel]
public sealed class VolumeDepthSamplingConformanceTests
{
    private const int Width = 96;
    private const int Height = 96;

    // The measured worst case. Over a 96-deep grid the retired fixed-32 lattice samples at
    // (i + 0.5) / 32, which lands either side of a two-layer slab at layer 11 and returns
    // 0.000000 for the whole column; one sample per layer returns 2/96 exactly.
    private const int SlabDepth = 96;
    private const int SlabFirstLayer = 11;
    private const int SlabLayerCount = 2;
    private const float SlabColumnMean = (float)SlabLayerCount / SlabDepth;

    // Measured, not guessed. The sampled 96-deep slab and the uniform proxy at its exact
    // column mean render bit-identically on both backends -- maxChannelDelta=0 and
    // meanChannelDelta=0.000000 -- so the integrator is exact rather than merely close.
    // The budget exists only so a future filtering or rounding difference in one backend
    // does not turn a correct render into a failure.
    private const byte MaximumExactnessChannelDelta = 2;
    private const double MaximumExactnessMeanDelta = 0.25;

    // Recovering the slab has to be visible, or "exact" could be satisfied by both renders
    // collapsing to the background. Measured at maxChannelDelta=9 and
    // meanChannelDelta=1.166667 against an empty proxy; the mean is diluted by the frame
    // area the proxy quad does not cover, which is why the floor is well under it. The
    // retired fixed-32 lattice steps over this slab and produces exactly 0 for both, so
    // any floor above zero separates the two integrators -- these leave roughly a factor
    // of two of headroom against the measurement instead of sitting on it.
    private const byte MinimumRecoveredChannelDelta = 4;
    private const double MinimumRecoveredMeanDelta = 0.5;

    private const byte MaximumCrossBackendChannelDelta = 8;
    private const double MaximumCrossBackendMeanDelta = 0.5;

    [Test]
    public async Task ThinSlabIsRecoveredRatherThanSteppedOverOnVulkan()
    {
        try
        {
            await RunThinSlabGate("vulkan", CreateVulkanDevice).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsVulkanUnavailable(exception))
        {
            WriteEvidence(
                "volume-depth-vulkan-gates.txt",
                [$"skipped: No Vulkan device is available on this host: {exception.Message}"]);
            Skip.Test($"No Vulkan device is available on this host: {exception.Message}");
        }
    }

    [Test]
    public async Task ThinSlabIsRecoveredRatherThanSteppedOverOnD3D12Warp()
    {
        if (!OperatingSystem.IsWindows())
        {
            WriteEvidence(
                "volume-depth-d3d12-gates.txt",
                ["skipped: Direct3D 12 is available only on Windows."]);
            Skip.Test("Direct3D 12 is available only on Windows.");
        }

        await RunThinSlabGate("d3d12", CreateD3D12WarpDevice).ConfigureAwait(false);
    }

    [Test]
    public async Task ThinSlabRecoveryAgreesBetweenD3D12AndVulkan()
    {
        // Self-consistency cannot separate a correct integrator from a consistently wrong
        // one; the sampled-versus-uniform gate for the checked VDB learned that the hard
        // way. Comparing the two backends' recovered slabs directly is what makes a
        // backend-specific sampling difference visible.
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Direct3D 12 is available only on Windows, so the two backends cannot be compared here.");
        }

        ParityImage vulkan = CaptureSampledSlab(CreateVulkanDevice);
        ParityImage d3d12 = CaptureSampledSlab(CreateD3D12WarpDevice);
        ImageDelta crossBackend = ImageDelta.Compare(vulkan, d3d12);
        WriteEvidence(
            "volume-depth-cross-backend.txt",
            [
                $"volume-depth-d3d12-vs-vulkan maxChannelDelta={crossBackend.MaximumChannelDelta}; " +
                    $"meanChannelDelta={Format(crossBackend.MeanChannelDelta)}",
            ]);

        await Assert.That(crossBackend.MaximumChannelDelta)
            .IsLessThanOrEqualTo(MaximumCrossBackendChannelDelta)
            .Because("both backends must integrate the same grid over the same layer count.");
        await Assert.That(crossBackend.MeanChannelDelta)
            .IsLessThanOrEqualTo(MaximumCrossBackendMeanDelta)
            .Because("a backend that steps the column differently diverges across the whole footprint.");
    }

    [Test]
    public async Task ThirtyTwoDeepGridStillIntegratesExactly()
    {
        // The control for the change itself. The checked VDB is 32 deep, so the retired
        // constant was exact for it; this requires the depth-aware integrator to keep
        // reproducing that result, which is what lets the recorded deltas in the existing
        // VDB gates stay valid rather than being silently re-baselined.
        const int depth = 32;
        const int firstLayer = 8;
        const int layerCount = 4;
        const float mean = (float)layerCount / depth;

        ParityImage sampled = CaptureSlab(
            CreateVulkanDevice,
            depth,
            firstLayer,
            layerCount,
            "depth32");
        ParityImage uniform = CaptureUniform(CreateVulkanDevice, mean, "depth32");
        ImageDelta exactness = ImageDelta.Compare(uniform, sampled);
        WriteEvidence(
            "volume-depth-32-control.txt",
            [
                $"volume-depth-32-sampled-vs-uniform maxChannelDelta={exactness.MaximumChannelDelta}; " +
                    $"meanChannelDelta={Format(exactness.MeanChannelDelta)}; columnMean={Format(mean)}",
            ]);

        await Assert.That(exactness.MaximumChannelDelta)
            .IsLessThanOrEqualTo(MaximumExactnessChannelDelta)
            .Because("a 32-deep grid must integrate to its exact column mean, as it always did.");
    }

    private static async Task RunThinSlabGate(
        string backendName,
        Func<ISilkGraphicsDevice> createDevice)
    {
        ParityImage sampled = CaptureSampledSlab(createDevice);
        ParityImage uniform = CaptureUniform(createDevice, SlabColumnMean, backendName);
        ParityImage empty = CaptureUniform(createDevice, 0, backendName);

        ImageDelta exactness = ImageDelta.Compare(uniform, sampled);
        ImageDelta recovered = ImageDelta.Compare(empty, sampled);
        ImageDelta uniformAgainstEmpty = ImageDelta.Compare(empty, uniform);

        string[] evidence =
        [
            $"volume-depth-slab-vs-uniform maxChannelDelta={exactness.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(exactness.MeanChannelDelta)}; " +
                $"columnMean={Format(SlabColumnMean)}; depth={SlabDepth}",
            $"volume-depth-slab-vs-empty maxChannelDelta={recovered.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(recovered.MeanChannelDelta)}",
            $"volume-depth-uniform-vs-empty maxChannelDelta={uniformAgainstEmpty.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(uniformAgainstEmpty.MeanChannelDelta)}",
        ];
        WriteEvidence($"volume-depth-{backendName}-gates.txt", evidence);

        // Ordered so a failure names the right cause. If the slab was stepped over the
        // sampled render collapses onto the empty one and this fires first.
        await Assert.That(recovered.MaximumChannelDelta)
            .IsGreaterThanOrEqualTo(MinimumRecoveredChannelDelta)
            .Because("a two-layer slab in a 96-deep grid must reach the image, not be stepped over.");
        await Assert.That(recovered.MeanChannelDelta)
            .IsGreaterThanOrEqualTo(MinimumRecoveredMeanDelta)
            .Because("the recovered slab must move the whole footprint, not a single pixel.");
        await Assert.That(exactness.MaximumChannelDelta)
            .IsLessThanOrEqualTo(MaximumExactnessChannelDelta)
            .Because("the integrated slab must equal a uniform proxy at the grid's exact column mean.");
        await Assert.That(exactness.MeanChannelDelta)
            .IsLessThanOrEqualTo(MaximumExactnessMeanDelta)
            .Because("an integrator that mis-weights layers biases the mean across the footprint.");
    }

    private static ParityImage CaptureSampledSlab(Func<ISilkGraphicsDevice> createDevice) =>
        CaptureSlab(createDevice, SlabDepth, SlabFirstLayer, SlabLayerCount, "slab");

    private static ParityImage CaptureSlab(
        Func<ISilkGraphicsDevice> createDevice,
        int depth,
        int firstLayer,
        int layerCount,
        string name)
    {
        // Deliberately coarse in X and Y. The contract is the Z integration, so a grid
        // that is constant across every column removes lateral filtering from the
        // comparison and leaves the layer weighting as the only thing under test.
        string grid = VolumeCommandAuthoring.WriteDensityGrid(
            EvidenceDirectory,
            name,
            4,
            4,
            depth,
            (_, _, z) => z >= firstLayer && z < firstLayer + layerCount ? 1f : 0f);
        return Capture(
            createDevice,
            VolumeCommandAuthoring.CreateVolumeMaterialCommand(
                MaterialPath,
                authoredDensity: 1,
                grid,
                string.Create(CultureInfo.InvariantCulture, $"4,4,{depth}")));
    }

    private static ParityImage CaptureUniform(
        Func<ISilkGraphicsDevice> createDevice,
        float density,
        string name)
    {
        _ = name;
        return Capture(
            createDevice,
            VolumeCommandAuthoring.CreateVolumeMaterialCommand(
                MaterialPath,
                density,
                volumeGrid: null,
                extent: null));
    }

    private static ParityImage Capture(
        Func<ISilkGraphicsDevice> createDevice,
        byte[] materialCommand)
    {
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                Width,
                Height,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Width, Height));
        using var renderer = new SilkMeshRenderer(device);
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            SilkMeshRendererConformance.CreateFrameCommand(
                Width,
                Height,
                SilkMeshRendererConformance.Identity()),
            materialCommand,
            VolumeCommandAuthoring.CreateVolumeProxyMeshCommand(MeshPath, MaterialPath));

        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        _ = renderer.Render(color, depth, options);

        byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(pixels);
        return new ParityImage(Width, Height, pixels);
    }

    private static ISilkGraphicsDevice CreateVulkanDevice()
    {
        return VulkanSilkGraphicsDevice.Create();
    }

    private static bool IsVulkanUnavailable(Exception exception) =>
        exception is DllNotFoundException or
            EntryPointNotFoundException or
            PlatformNotSupportedException or
            InvalidOperationException;

    private static ISilkGraphicsDevice CreateD3D12WarpDevice()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Direct3D 12 is available only on Windows.");
        }
        return D3D12SilkGraphicsDevice.Create(useWarp: true);
    }

    private static string Format(double value) =>
        value.ToString("F6", CultureInfo.InvariantCulture);

    private static string EvidenceDirectory =>
        Path.Combine(AppContext.BaseDirectory, "TestResults", "volumes");

    private static void WriteEvidence(string fileName, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(EvidenceDirectory);
        File.WriteAllLines(Path.Combine(EvidenceDirectory, fileName), lines);
    }

    private const string MaterialPath = "/World/VolumeDepthMaterial";
    private const string MeshPath = "/World/VolumeDepthProxy";
}
