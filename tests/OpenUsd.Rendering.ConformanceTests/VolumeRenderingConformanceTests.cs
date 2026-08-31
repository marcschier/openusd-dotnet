// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Numerics;
using System.Text;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class VolumeRenderingConformanceTests
{
    private const int Width = 160;
    private const int Height = 128;
    private const double TimeCode = 1;
    private const byte MaximumZeroDensityChannelDelta = 0;
    private const byte MinimumNonZeroDensityChannelDelta = 64;
    private const byte MinimumDoubledDensityChannelDelta = 16;
    private const double MinimumNonZeroDensityMeanDelta = 5.0;
    private const double MinimumDoubledDensityMeanDelta = 1.0;
    private const double MinimumSampledDeltaVariance = 4.0;
    private const byte MinimumSampledVsUniformDelta = 12;
    private const double MinimumSampledVsUniformMeanDelta = 1.0;
    private const double MinimumTranslatedMeanDelta = 1.0;

    // Measured, not guessed. D3D12 WARP and Vulkan SwiftShader render the sampled grid
    // bit-identically today: maxChannelDelta=0 and meanChannelDelta=0.000000, with the
    // same footprint variance 890.508958 on both. Both are software rasterizers running
    // the same checked shader source, so exact agreement is the expected result and the
    // budget below exists only so a future rounding or filtering difference in one
    // backend does not turn a correct render into a failure. It is deliberately far
    // below the divergence the bug this gate was written for produced: a backend that
    // ignores the density grid renders the flat authored density and lands at
    // maxChannelDelta=183 / meanChannelDelta=93.333333 against the sampled reference,
    // more than twenty times this mean budget and more than twenty times the channel
    // budget, so the gate cannot pass while a backend silently drops the volume.
    private const byte MaximumCrossBackendChannelDelta = 8;
    private const double MaximumCrossBackendMeanDelta = 0.5;

    [Test]
    public async Task UniformDensityVolumeGatesOnVulkan()
    {
        await RunUniformDensityVolumeGate(
            "vulkan",
            VulkanSilkGraphicsDevice.Create,
            "volume-density-vulkan-gates.txt").ConfigureAwait(false);
    }

    [Test]
    public async Task UniformDensityVolumeGatesOnMetal()
    {
        // Metal exists only on macOS, so this leg is genuinely platform-bound in the same
        // way the D3D12 legs are; it reports a platform skip everywhere else.
        RequireMetalHost();
        await RunUniformDensityVolumeGate(
            "metal",
            CreateMetalDevice,
            "volume-density-metal-gates.txt").ConfigureAwait(false);
    }

    [Test]
    public async Task SampledOpenVdbDensityGatesOnMetal()
    {
        // The same stage set, crops, and thresholds the Vulkan and D3D12 legs use, through
        // the same helper. Running a Metal-shaped variant of these assertions would prove
        // Metal agrees with itself; running the shared one is what makes a Metal backend
        // that ignores the density grid fail the way D3D12 did.
        RequireMetalHost();
        await RunSampledOpenVdbDensityGate(
            "metal",
            CreateMetalDevice,
            "volume-vdb-metal-gates.txt").ConfigureAwait(false);
    }

    private static async Task RunUniformDensityVolumeGate(
        string backendName,
        Func<ISilkGraphicsDevice> createDevice,
        string evidenceFileName)
    {
        ParityImage capture;
        try
        {
            capture = CaptureStage(
                WriteStage($"volume-density-{backendName}-gates"),
                createDevice);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            // The evidence file is written even here, and that is the point: a gate that
            // skipped for a named capability reason and a gate that never ran at all are
            // otherwise indistinguishable to anything reading the uploaded artifact.
            // eng/assert-volume-evidence.ps1 treats the first as a recorded non-render and
            // the second as a wiring fault, so both have to leave a distinct trace.
            WriteEvidence(
                evidenceFileName,
                [$"volume-density-{backendName} unavailable: the hdSilk native runtime " +
                    $"could not be loaded: {exception.Message}"]);
            Skip.Test($"The hdSilk native runtime is unavailable: {exception.Message}");
            return;
        }
        ParityImage empty = Crop(capture, 0, 0, 16, 16);
        ParityImage zeroDensity = Crop(capture, 28, 56, 16, 16);
        ParityImage unitDensity = Crop(capture, 72, 56, 16, 16);
        ParityImage doubleDensity = Crop(capture, 116, 56, 16, 16);

        ImageDelta zeroAgainstEmpty = ImageDelta.Compare(empty, zeroDensity);
        ImageDelta unitAgainstEmpty = ImageDelta.Compare(empty, unitDensity);
        ImageDelta doubleAgainstUnit = ImageDelta.Compare(unitDensity, doubleDensity);
        double unitBrightness = MeanRgb(unitDensity);
        double doubleBrightness = MeanRgb(doubleDensity);

        string[] evidence =
        [
            $"volume-zero-vs-empty maxChannelDelta={zeroAgainstEmpty.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(zeroAgainstEmpty.MeanChannelDelta)}",
            $"volume-unit-vs-empty maxChannelDelta={unitAgainstEmpty.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(unitAgainstEmpty.MeanChannelDelta)}",
            $"volume-double-vs-unit maxChannelDelta={doubleAgainstUnit.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(doubleAgainstUnit.MeanChannelDelta)}; " +
                $"unitMeanRgb={Format(unitBrightness)}; doubleMeanRgb={Format(doubleBrightness)}",
        ];
        WriteEvidence(evidenceFileName, evidence);

        await Assert.That(zeroAgainstEmpty.MaximumChannelDelta)
            .IsLessThanOrEqualTo(MaximumZeroDensityChannelDelta)
            .Because("zero-density UsdVol must match an empty scene exactly.");
        await Assert.That(unitAgainstEmpty.MaximumChannelDelta)
            .IsGreaterThanOrEqualTo(MinimumNonZeroDensityChannelDelta)
            .Because("non-zero density must prove the UsdVol density field reaches the shader.");
        await Assert.That(unitAgainstEmpty.MeanChannelDelta)
            .IsGreaterThanOrEqualTo(MinimumNonZeroDensityMeanDelta)
            .Because("non-zero density must differ over more than a single pixel.");
        await Assert.That(doubleAgainstUnit.MaximumChannelDelta)
            .IsGreaterThanOrEqualTo(MinimumDoubledDensityChannelDelta)
            .Because("doubling density must measurably change the integrated volume response.");
        await Assert.That(doubleAgainstUnit.MeanChannelDelta)
            .IsGreaterThanOrEqualTo(MinimumDoubledDensityMeanDelta)
            .Because("doubling density must move the image mean, not only one edge pixel.");
        await Assert.That(doubleBrightness)
            .IsGreaterThan(unitBrightness)
            .Because("the current emission-absorption model brightens as density increases.");
    }

    [Test]
    public async Task SampledOpenVdbDensityGatesOnVulkan()
    {
        await RunSampledOpenVdbDensityGate(
            "vulkan",
            VulkanSilkGraphicsDevice.Create,
            "volume-vdb-vulkan-gates.txt").ConfigureAwait(false);
    }

    [Test]
    public async Task SampledOpenVdbDensityGatesOnD3D12Warp()
    {
        // Direct3D 12 exists only on Windows, so this leg is genuinely platform-bound
        // rather than merely unproven elsewhere. The Vulkan legs above carry no such
        // guard: they run wherever the native profile and a Vulkan device are present,
        // and report a capability skip when either is missing.
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Direct3D 12 is available only on Windows.");
        }

        await RunSampledOpenVdbDensityGate(
            "d3d12",
            CreateD3D12WarpDevice,
            "volume-vdb-d3d12-gates.txt").ConfigureAwait(false);
    }

    [Test]
    public async Task SampledOpenVdbDensityAgreesBetweenD3D12AndVulkan()
    {
        // Self-divergence alone cannot tell a correctly sampled volume from a
        // consistently wrong one: the D3D12 backend used to pass the sampled-vs-uniform
        // check while rendering a flat authored density, because its DXIL mesh fragment
        // binary had no 3D density texture at all. Comparing the two backends'
        // sampled images against each other is what makes that failure visible.
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Direct3D 12 is available only on Windows, so the two backends cannot be compared here.");
        }

        ClearVolumeCache();
        string asset = ResolveOpenVdbAsset("sampled_density.vdb");
        ParityImage vulkan;
        try
        {
            vulkan = CaptureStage(
                WriteVdbStage("volume-vdb-cross-backend-vulkan", asset, 0, 1, sampleDensity: true),
                VulkanSilkGraphicsDevice.Create);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            Skip.Test($"The hdSilk native runtime is unavailable: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.", exception);
        }
        if (!TryReadMeanCachedDensity("volume-vdb-cross-backend-vulkan", out _))
        {
            WriteEvidence(
                "volume-vdb-cross-backend.txt",
                ["volume-vdb-cross-backend skipped: hioOpenVDB reader is unavailable in the native profile."]);
            Skip.Test("hioOpenVDB reader is unavailable in the native profile.");
        }
        ParityImage d3d12 = CaptureStage(
            WriteVdbStage("volume-vdb-cross-backend-d3d12", asset, 0, 1, sampleDensity: true),
            CreateD3D12WarpDevice);

        ImageDelta crossBackend = ImageDelta.Compare(vulkan, d3d12);
        double vulkanVariance = VarianceRgb(Crop(vulkan, 68, 52, 24, 24));
        double d3d12Variance = VarianceRgb(Crop(d3d12, 68, 52, 24, 24));
        WriteEvidence(
            "volume-vdb-cross-backend.txt",
            [
                $"volume-vdb-d3d12-vs-vulkan maxChannelDelta={crossBackend.MaximumChannelDelta}; " +
                    $"meanChannelDelta={Format(crossBackend.MeanChannelDelta)}",
                $"volume-vdb-footprint-variance vulkanRgb={Format(vulkanVariance)}; " +
                    $"d3d12Rgb={Format(d3d12Variance)}",
            ]);

        await Assert.That(crossBackend.MaximumChannelDelta)
            .IsLessThanOrEqualTo(MaximumCrossBackendChannelDelta)
            .Because("the D3D12 and Vulkan backends must raymarch the same density grid to the same image.");
        await Assert.That(crossBackend.MeanChannelDelta)
            .IsLessThanOrEqualTo(MaximumCrossBackendMeanDelta)
            .Because("a backend that ignores the sampled grid diverges across the whole footprint, not one pixel.");
    }

    [Test]
    public async Task SampledOpenVdbDensityUsesStormReferenceWhenAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The Storm VDB reference probe is currently exercised only by the Windows native profile.");
        }
        if (!StormGlContextFactory.IsCurrentPlatformSupported)
        {
            Skip.Test("The current platform cannot create the Storm OpenGL context used by this harness.");
        }

        ClearVolumeCache();
        string asset = ResolveOpenVdbAsset("sampled_density.vdb");
        string shiftedAsset = ResolveOpenVdbAsset("sampled_density_shifted.vdb");
        ParityImage silkSampled;
        try
        {
            silkSampled = CaptureStage(
                WriteVdbStage("volume-vdb-storm-reference-hdsilk-sampled", asset, 0, 1, sampleDensity: true),
                VulkanSilkGraphicsDevice.Create);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            Skip.Test($"The hdSilk native runtime is unavailable: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.", exception);
        }
        if (!TryReadMeanCachedDensity("volume-vdb-storm-reference-hdsilk-sampled", out double meanDensity))
        {
            WriteEvidence(
                "volume-vdb-storm-reference.txt",
                ["storm-reference skipped: hioOpenVDB reader is unavailable in the native profile."]);
            Skip.Test("hioOpenVDB reader is unavailable in the native profile.");
        }

        string sampledStage = WriteVdbStage(
            "volume-vdb-storm-reference-sampled",
            asset,
            0,
            1,
            sampleDensity: true);
        string uniformStage = WriteVdbStage(
            "volume-vdb-storm-reference-uniform-mean",
            asset,
            0,
            meanDensity,
            sampleDensity: false);
        string shiftedStage = WriteVdbStage(
            "volume-vdb-storm-reference-shifted",
            shiftedAsset,
            0,
            1,
            sampleDensity: true);
        ParityImage stormSampled;
        ParityImage stormUniform;
        ParityImage stormShifted;
        try
        {
            stormSampled = await CaptureStormStage(sampledStage).ConfigureAwait(false);
            stormUniform = await CaptureStormStage(uniformStage).ConfigureAwait(false);
            stormShifted = await CaptureStormStage(shiftedStage).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            WriteEvidence(
                "volume-vdb-storm-reference.txt",
                [$"storm-reference unavailable: {exception.GetType().Name}: {exception.Message}"]);
            Skip.Test($"Storm cannot render the VDB stage in this harness: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.", exception);
        }

        ParityImage stormSampledFootprint = Crop(stormSampled, 68, 52, 24, 24);
        ParityImage stormUniformFootprint = Crop(stormUniform, 68, 52, 24, 24);
        ImageDelta stormSampledVsUniform = ImageDelta.Compare(stormUniformFootprint, stormSampledFootprint);
        ImageDelta stormShiftedInterior = ImageDelta.Compare(stormSampled, stormShifted);
        ParityImage silkUniform = CaptureStage(uniformStage, VulkanSilkGraphicsDevice.Create);
        ImageDelta stormVsSilkSampled = ImageDelta.Compare(stormSampled, silkSampled);
        ImageDelta stormVsSilkUniform = ImageDelta.Compare(stormSampled, silkUniform);

        string[] evidence =
        [
            $"storm-vdb-sampled-vs-uniform maxChannelDelta={stormSampledVsUniform.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(stormSampledVsUniform.MeanChannelDelta)}; " +
                $"meanDensity={Format(meanDensity)}",
            $"storm-vdb-shifted-interior maxChannelDelta={stormShiftedInterior.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(stormShiftedInterior.MeanChannelDelta)}",
            $"storm-vs-hdsilk-sampled maxChannelDelta={stormVsSilkSampled.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(stormVsSilkSampled.MeanChannelDelta)}",
            $"storm-vs-hdsilk-uniform-proxy maxChannelDelta={stormVsSilkUniform.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(stormVsSilkUniform.MeanChannelDelta)}",
        ];
        WriteEvidence("volume-vdb-storm-reference.txt", evidence);

        if (stormSampledVsUniform.MaximumChannelDelta < MinimumSampledVsUniformDelta ||
            stormSampledVsUniform.MeanChannelDelta < MinimumSampledVsUniformMeanDelta ||
            stormShiftedInterior.MeanChannelDelta < MinimumTranslatedMeanDelta)
        {
            Skip.Test(
                "Storm does not expose a sampled VDB reference in this offscreen harness; see " +
                "volume-vdb-storm-reference.txt for sampled/uniform/shifted deltas.");
        }

        await Assert.That(stormVsSilkSampled.MeanChannelDelta)
            .IsLessThan(stormVsSilkUniform.MeanChannelDelta)
            .Because("hdSilk's sampled VDB image must be closer to Storm than the uniform-density fallback.");
    }

    private static async Task RunSampledOpenVdbDensityGate(
        string backendName,
        Func<ISilkGraphicsDevice> createDevice,
        string evidenceFileName)
    {
        ClearVolumeCache();
        string asset = ResolveOpenVdbAsset("sampled_density.vdb");
        string shiftedAsset = ResolveOpenVdbAsset("sampled_density_shifted.vdb");
        ParityImage sampled;
        try
        {
            sampled = CaptureStage(
                WriteVdbStage($"volume-vdb-{backendName}-sampled", asset, 0, 1, sampleDensity: true),
                createDevice);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            // Recorded rather than silent, for the same reason as the uniform gate: the
            // uploaded artifact has to distinguish a named capability skip from a gate
            // that never executed.
            WriteEvidence(
                evidenceFileName,
                [$"volume-vdb-{backendName}-sampled unavailable: the hdSilk native runtime " +
                    $"could not be loaded: {exception.Message}"]);
            Skip.Test($"The hdSilk native runtime is unavailable: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.", exception);
        }
        if (!TryReadMeanCachedDensity($"volume-vdb-{backendName}-sampled", out double meanDensity))
        {
            WriteEvidence(
                evidenceFileName,
                [$"volume-vdb-{backendName}-sampled skipped: hioOpenVDB reader is unavailable in the native profile."]);
            Skip.Test("hioOpenVDB reader is unavailable in the native profile.");
        }
        ParityImage uniform = CaptureStage(
            WriteVdbStage($"volume-vdb-{backendName}-uniform-mean", asset, 0, meanDensity, sampleDensity: false),
            createDevice);
        ParityImage shifted = CaptureStage(
            WriteVdbStage($"volume-vdb-{backendName}-shifted", shiftedAsset, 0, 1, sampleDensity: true),
            createDevice);

        ParityImage sampledFootprint = Crop(sampled, 68, 52, 24, 24);
        ParityImage uniformFootprint = Crop(uniform, 68, 52, 24, 24);
        ImageDelta sampledVsUniform = ImageDelta.Compare(uniformFootprint, sampledFootprint);
        double variance = VarianceRgb(sampledFootprint);
        double deltaVariance = VarianceDeltaRgb(uniformFootprint, sampledFootprint);
        ImageDelta shiftedInterior = ImageDelta.Compare(sampled, shifted);

        string[] evidence =
        [
            $"volume-vdb-nonuniform varianceRgb={Format(variance)}; " +
                $"deltaVarianceRgb={Format(deltaVariance)}; " +
                $"meanDensity={Format(meanDensity)}",
            $"volume-vdb-sampled-vs-uniform maxChannelDelta={sampledVsUniform.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(sampledVsUniform.MeanChannelDelta)}",
            $"volume-vdb-shifted-interior maxChannelDelta={shiftedInterior.MaximumChannelDelta}; " +
                $"meanChannelDelta={Format(shiftedInterior.MeanChannelDelta)}",
        ];
        WriteEvidence(evidenceFileName, evidence);

        await Assert.That(deltaVariance)
            .IsGreaterThanOrEqualTo(MinimumSampledDeltaVariance)
            .Because("a sampled non-uniform VDB must vary across the volume footprint.");
        await Assert.That(sampledVsUniform.MaximumChannelDelta)
            .IsGreaterThanOrEqualTo(MinimumSampledVsUniformDelta)
            .Because("a sampled VDB must differ from a uniform proxy at the same mean density.");
        await Assert.That(sampledVsUniform.MeanChannelDelta)
            .IsGreaterThanOrEqualTo(MinimumSampledVsUniformMeanDelta)
            .Because("sampled-vs-uniform must move the image mean, not only one edge pixel.");
        await Assert.That(shiftedInterior.MeanChannelDelta)
            .IsGreaterThanOrEqualTo(MinimumTranslatedMeanDelta)
            .Because("translating the VDB density grid inside the same proxy must move the sampled density footprint.");
    }

    private static ISilkGraphicsDevice CreateD3D12WarpDevice()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Direct3D 12 is available only on Windows.");
        }
        return D3D12SilkGraphicsDevice.Create(useWarp: true);
    }

    /// <summary>
    /// Reports a platform or capability skip unless this host can execute the Metal legs.
    /// </summary>
    /// <remarks>
    /// Two separate preconditions, and both have to be named rather than folded into the
    /// render itself. Metal only exists on macOS. The checked Metal shaders are a combined
    /// <c>mesh.metallib</c> built by the macOS shader toolchain and staged next to the test
    /// host by <c>OpenUsdRequireMetalShaderLibrary</c>; without it every pipeline creation
    /// fails on a missing artifact, which is a build-wiring fault and must not be reported
    /// as a volume rendering failure. Neither condition writes an evidence file, so on the
    /// macOS job -- where the platform condition cannot be the cause -- a missing metallib
    /// surfaces through <c>eng/assert-volume-evidence.ps1</c> as a wiring fault that fails
    /// the job, rather than as an allowed capability skip. A capability skip that does have
    /// a named runtime cause records itself in the evidence file instead.
    /// </remarks>
    private static void RequireMetalHost()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("Metal is available only on macOS.");
        }
        if (!SilkCheckedShaderAssets.HasPinnedMetalLibrary)
        {
            Skip.Test(
                "The pinned mesh.metallib is not staged beside the test host; build the " +
                "conformance project with -p:OpenUsdRequireMetalShaderLibrary=true.");
        }
    }

    private static ISilkGraphicsDevice CreateMetalDevice()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Metal is available only on macOS.");
        }
        return MetalSilkGraphicsDevice.Create();
    }

    private static async Task<ParityImage> CaptureStormStage(string stagePath)
    {
        PrependHdSilkNativeSearchPath();
        ParityCaptureSet capture = await ParityCaptureDriver.CaptureAsync(
            new ParityCaptureInput(
                ResolvePluginPath(),
                stagePath,
                Width,
                Height,
                TimeCode,
                new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, []),
                new SilkColor(0, 0, 0, 1),
                OpenUsdStormRuntime.Headlight,
                UseSceneLights: false),
            StormGlContextFactory.CreateForCurrentPlatform(),
            Array.Empty<SilkParityBackend>()).ConfigureAwait(false);
        return capture.Storm;
    }

    private static ParityImage CaptureStage(
        string stagePath,
        Func<ISilkGraphicsDevice> createDevice)
    {
        PrependHdSilkNativeSearchPath();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stagePath);
        using OpenUsdSilkPage page = session.Sync(
            Width,
            Height,
            TimeCode,
            new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, []));
        using ISilkGraphicsDevice device = createDevice();
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                checked((uint)Width),
                checked((uint)Height),
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(checked((uint)Width), checked((uint)Height)));
        using var renderer = new SilkMeshRenderer(device);
        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);

        _ = renderer.ApplyAndRender(page, color, depth, options);
        _ = renderer.ApplyAndRender(page, color, depth, options);

        byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(pixels);
        return new ParityImage(Width, Height, pixels);
    }

    private static string WriteStage(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "volumes");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".usda");
        File.WriteAllText(path, CreateStage(), new UTF8Encoding(false));
        return path;
    }

    private static string WriteVdbStage(
        string name,
        string assetPath,
        double x,
        double density,
        bool sampleDensity)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "volumes");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".usda");
        File.WriteAllText(
            path,
            CreateVdbStage(assetPath, x, density, sampleDensity),
            new UTF8Encoding(false));
        return path;
    }

    private static string CreateStage() =>
        $$"""
            #usda 1.0
            (
                defaultPrim = "World"
                startTimeCode = 1
                endTimeCode = 1
                framesPerSecond = 24
                timeCodesPerSecond = 24
                upAxis = "Y"
            )

            def Xform "World"
            {
            {{CreateVolumeXform("Zero", -0.55, 0)}}
            {{CreateVolumeXform("Unit", 0, 1)}}
            {{CreateVolumeXform("Double", 0.55, 2)}}
            }
            """;

    private static string CreateVdbStage(
        string assetPath,
        double x,
        double density,
        bool sampleDensity) =>
        $$"""
            #usda 1.0
            (
                defaultPrim = "World"
                startTimeCode = 1
                endTimeCode = 1
                framesPerSecond = 24
                timeCodesPerSecond = 24
                upAxis = "Y"
            )

            def Xform "World"
            {
              def Xform "Vdb"
              {
                  double3 xformOp:translate = ({{x.ToString("F6", CultureInfo.InvariantCulture)}}, 0, 0)
                  uniform token[] xformOpOrder = ["xformOp:translate"]
                  def Volume "Volume"
                  {
                      custom float hdsilk:density = {{density.ToString("F6", CultureInfo.InvariantCulture)}}
                      custom bool hdsilk:sampleDensity = {{(sampleDensity ? "true" : "false")}}
                      rel field:density = </World/Vdb/Volume/Density>
                      def OpenVDBAsset "Density"
                      {
                          asset filePath = @{{assetPath.Replace("\\", "/")}}@
                          token fieldName = "density"
                      }
                  }
              }
            }
            """;

    private static string CreateVolumeXform(string name, double x, double density) =>
        $$"""
              def Xform "{{name}}"
              {
                  double3 xformOp:translate = ({{x.ToString("F6", CultureInfo.InvariantCulture)}}, 0, 0)
                  uniform token[] xformOpOrder = ["xformOp:translate"]
                  def Volume "Volume"
                  {
                      custom float hdsilk:density = {{density.ToString("F6", CultureInfo.InvariantCulture)}}
                      rel field:density = </World/{{name}}/Volume/Density>
                      def OpenVDBAsset "Density"
                      {
                          asset filePath = @density.vdb@
                          token fieldName = "density"
                      }
                  }
              }
          """;

    private static ParityImage Crop(ParityImage image, int x, int y, int width, int height)
    {
        byte[] cropped = new byte[width * height * ParityImage.BytesPerPixel];
        ReadOnlySpan<byte> source = image.Rgba.Span;
        int sourceStride = image.Width * ParityImage.BytesPerPixel;
        int targetStride = width * ParityImage.BytesPerPixel;
        for (int row = 0; row < height; row++)
        {
            source.Slice(((y + row) * sourceStride) + (x * ParityImage.BytesPerPixel), targetStride)
                .CopyTo(cropped.AsSpan(row * targetStride, targetStride));
        }

        return new ParityImage(width, height, cropped);
    }

    private static double MeanRgb(ParityImage image)
    {
        ReadOnlySpan<byte> pixels = image.Rgba.Span;
        long sum = 0;
        int count = 0;
        for (int offset = 0; offset < pixels.Length; offset += ParityImage.BytesPerPixel)
        {
            sum += pixels[offset] + pixels[offset + 1] + pixels[offset + 2];
            count += 3;
        }

        return count == 0 ? 0 : (double)sum / count;
    }

    private static double VarianceRgb(ParityImage image)
    {
        double mean = MeanRgb(image);
        ReadOnlySpan<byte> pixels = image.Rgba.Span;
        double sumSquared = 0;
        int count = 0;
        for (int offset = 0; offset < pixels.Length; offset += ParityImage.BytesPerPixel)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                double delta = pixels[offset + channel] - mean;
                sumSquared += delta * delta;
                count++;
            }
        }
        return count == 0 ? 0 : sumSquared / count;
    }

    private static double VarianceDeltaRgb(ParityImage reference, ParityImage candidate)
    {
        if (reference.Width != candidate.Width || reference.Height != candidate.Height)
        {
            throw new ArgumentException("Images must have equal dimensions.", nameof(candidate));
        }

        ReadOnlySpan<byte> referencePixels = reference.Rgba.Span;
        ReadOnlySpan<byte> candidatePixels = candidate.Rgba.Span;
        double sum = 0;
        double sumSquared = 0;
        int count = 0;
        for (int offset = 0; offset < referencePixels.Length; offset += ParityImage.BytesPerPixel)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                int delta = Math.Abs(candidatePixels[offset + channel] - referencePixels[offset + channel]);
                sum += delta;
                sumSquared += delta * delta;
                count++;
            }
        }

        double mean = sum / count;
        return (sumSquared / count) - (mean * mean);
    }

    private static string ResolveOpenVdbAsset(string fileName)
    {
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            throw new DirectoryNotFoundException("Could not find repository root.");
        }
        string path = Path.Combine(
            root,
            "test-assets",
            "native-profile",
            "openvdb",
            fileName);
        if (File.Exists(path))
        {
            return path;
        }
        path = Path.Combine(
            root,
            "..",
            "openusd",
            "test-assets",
            "native-profile",
            "openvdb",
            fileName);
        if (File.Exists(path))
        {
            return Path.GetFullPath(path);
        }
        throw new FileNotFoundException("Could not find the OpenVDB test asset.", path);
    }

    private static bool TryReadMeanCachedDensity(string stageName, out double mean)
    {
        mean = 0;
        string directory = Path.Combine(
            AppContext.BaseDirectory,
            "TestResults",
            "volumes",
            "hdsilk-volume-cache");
        if (!Directory.Exists(directory))
        {
            return false;
        }
        string file = Directory.EnumerateFiles(directory, $"*{stageName}*.r32")
            .Concat(Directory.EnumerateFiles(directory, "*.r32"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? string.Empty;
        if (file.Length == 0)
        {
            return false;
        }
        byte[] bytes = File.ReadAllBytes(file);
        double sum = 0;
        int count = bytes.Length / sizeof(float);
        if (count == 0)
        {
            return false;
        }
        for (int index = 0; index < count; index++)
        {
            float value = BitConverter.ToSingle(bytes, index * sizeof(float));
            if (float.IsFinite(value))
            {
                sum += Math.Max(0, value);
            }
        }
        mean = sum / count;
        return true;
    }

    private static string Format(double value) =>
        value.ToString("F6", CultureInfo.InvariantCulture);

    private static void WriteEvidence(string fileName, IEnumerable<string> lines)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "volumes");
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, fileName), lines);
    }

    private static void ClearVolumeCache()
    {
        string directory = Path.Combine(
            AppContext.BaseDirectory,
            "TestResults",
            "volumes",
            "hdsilk-volume-cache");
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Prepends the locally built native runtime to the Windows DLL search path.
    /// </summary>
    /// <remarks>
    /// Windows-only by construction, not by policy. <c>PATH</c> is the Windows loader's
    /// search list and can still be changed after process start; the ELF and Mach-O
    /// loaders read <c>LD_LIBRARY_PATH</c>/<c>DYLD_LIBRARY_PATH</c> once at process
    /// start, so setting them from inside the test host would do nothing. On those
    /// platforms the caller (see the render workflow's Linux and macOS volume steps)
    /// exports the staged runtime before launching the test host, and a missing runtime
    /// surfaces here as the <see cref="DllNotFoundException"/> capability skip rather than
    /// as a silently different render.
    /// </remarks>
    private static void PrependHdSilkNativeSearchPath()
    {
        string? root = FindRepositoryRoot();
        if (root is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        string[] directories =
        [
            Path.Combine(AppContext.BaseDirectory, "volume-runtime", "bin"),
            Path.Combine(root, "native", "install", "shim", "win-x64", "bin"),
            Path.Combine(root, "native", "install", "win-x64", "bin"),
            Path.Combine(root, "native", "install", "win-x64", "lib"),
            Path.Combine(root, "..", "openusd", "native", "install", "win-x64", "bin"),
            Path.Combine(root, "..", "openusd", "native", "install", "win-x64", "lib"),
            Path.Combine(root, "..", "openusd", "native", "install", "vulkan-sdk-1.4.321.0", "Bin"),
        ];
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string prefix = string.Join(
            Path.PathSeparator,
            directories.Where(Directory.Exists));
        if (prefix.Length != 0 && !currentPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", prefix + Path.PathSeparator + currentPath);
        }
    }

    private static string ResolvePluginPath()
    {
        string? configured = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "plugInfo.json")))
        {
            return configured;
        }

        if (TryPrepareLocalPluginRuntime(out string localRuntime))
        {
            return localRuntime;
        }

        string packaged = Path.Combine(AppContext.BaseDirectory, "usd");
        if (File.Exists(Path.Combine(packaged, "plugInfo.json")))
        {
            return packaged;
        }

        throw new DirectoryNotFoundException(
            $"No OpenUSD plugin path was found under '{packaged}' or OPENUSD_PLUGIN_PATH.");
    }

    private static bool TryPrepareLocalPluginRuntime(out string pluginPath)
    {
        pluginPath = string.Empty;
        string? root = FindRepositoryRoot();
        // The developer-convenience staging below reads the win-x64 shim build tree and
        // copies DLLs. Non-Windows hosts reach this path only from CI, where the runtime
        // is already staged and named by OPENUSD_PLUGIN_PATH, so attempting a
        // Windows-shaped copy there would only turn a clean capability skip into a
        // confusing one.
        if (root is null || !OperatingSystem.IsWindows())
        {
            return false;
        }

        string build = Path.Combine(root, "native", "build", "shim", "win-x64");
        string stormPlugins = Path.Combine(
            build,
            "openusd_hydra",
            "tests",
            "storm-wgl-runtime",
            "plugin",
            "usd");
        string hdsilkPlugin = Path.Combine(build, "hdSilk", "resources", "plugInfo.json");
        string hdsilkLibrary = Path.Combine(build, "hdSilk", "openusd_hdsilk.dll");
        string openVdbPlugin = Path.Combine(
            root,
            "native",
            "install",
            "win-x64",
            "lib",
            "usd",
            "hioOpenVDB");
        string nativeLibraryDirectory = Path.Combine(root, "native", "install", "win-x64", "lib");
        if (!File.Exists(Path.Combine(stormPlugins, "plugInfo.json")) ||
            !File.Exists(hdsilkPlugin) ||
            !File.Exists(hdsilkLibrary) ||
            !Directory.Exists(nativeLibraryDirectory))
        {
            return false;
        }

        string runtime = Path.Combine(AppContext.BaseDirectory, "volume-runtime");
        string runtimePlugins = Path.Combine(runtime, "plugin", "usd");
        CopyDirectory(stormPlugins, runtimePlugins);
        string runtimeHdSilkResources = Path.Combine(runtimePlugins, "hdSilk", "resources");
        Directory.CreateDirectory(runtimeHdSilkResources);
        File.Copy(hdsilkPlugin, Path.Combine(runtimeHdSilkResources, "plugInfo.json"), overwrite: true);
        if (File.Exists(Path.Combine(openVdbPlugin, "resources", "plugInfo.json")))
        {
            CopyDirectory(openVdbPlugin, Path.Combine(runtimePlugins, "hioOpenVDB"));
        }
        Directory.CreateDirectory(Path.Combine(runtime, "bin"));
        string runtimeHdSilkLibrary = Path.Combine(runtime, "bin", "openusd_hdsilk.dll");
        try
        {
            File.Copy(hdsilkLibrary, runtimeHdSilkLibrary, overwrite: true);
        }
        catch (IOException) when (File.Exists(runtimeHdSilkLibrary))
        {
        }
        foreach (string library in Directory.EnumerateFiles(nativeLibraryDirectory, "*.dll"))
        {
            string target = Path.Combine(runtime, "bin", Path.GetFileName(library));
            try
            {
                File.Copy(library, target, overwrite: true);
            }
            catch (IOException) when (File.Exists(target))
            {
            }
        }

        pluginPath = runtimePlugins;
        return true;
    }

    private static string? FindRepositoryRoot()
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

        return null;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string child in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
        }
    }
}
