// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Numerics;
using System.Text;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

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

    [Test]
    public async Task UniformDensityVolumeGatesOnVulkan()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ParityImage capture = CaptureStage(WriteStage("volume-density-gates"));
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
        WriteEvidence("volume-density-vulkan-gates.txt", evidence);

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
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ClearVolumeCache();
        string asset = ResolveOpenVdbAsset("sampled_density.vdb");
        string shiftedAsset = ResolveOpenVdbAsset("sampled_density_shifted.vdb");
        ParityImage sampled = CaptureStage(WriteVdbStage("volume-vdb-sampled", asset, 0, 1, sampleDensity: true));
        if (!TryReadMeanCachedDensity("volume-vdb-sampled", out double meanDensity))
        {
            WriteEvidence(
                "volume-vdb-vulkan-gates.txt",
                ["volume-vdb-sampled skipped: hioOpenVDB reader is unavailable in the native profile."]);
            Skip.Test("hioOpenVDB reader is unavailable in the native profile.");
            return;
        }
        ParityImage uniform = CaptureStage(
            WriteVdbStage("volume-vdb-uniform-mean", asset, 0, meanDensity, sampleDensity: false));
        ParityImage shifted = CaptureStage(
            WriteVdbStage("volume-vdb-shifted", shiftedAsset, 0, 1, sampleDensity: true));

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
        WriteEvidence("volume-vdb-vulkan-gates.txt", evidence);

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

    private static ParityImage CaptureStage(string stagePath)
    {
        PrependHdSilkNativeSearchPath();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stagePath);
        using OpenUsdSilkPage page = session.Sync(
            Width,
            Height,
            TimeCode,
            new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, []));
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
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
        if (root is null)
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

    private readonly record struct ImageDelta(int MaximumChannelDelta, double MeanChannelDelta)
    {
        internal static ImageDelta Compare(ParityImage reference, ParityImage candidate)
        {
            reference.Validate(nameof(reference));
            candidate.Validate(nameof(candidate));
            if (reference.Width != candidate.Width || reference.Height != candidate.Height)
            {
                throw new ArgumentException("Images must have matching dimensions.", nameof(candidate));
            }

            ReadOnlySpan<byte> referencePixels = reference.Rgba.Span;
            ReadOnlySpan<byte> candidatePixels = candidate.Rgba.Span;
            int maximum = 0;
            long sum = 0;
            int count = 0;
            for (int offset = 0; offset < referencePixels.Length; offset += ParityImage.BytesPerPixel)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    int delta = Math.Abs(referencePixels[offset + channel] - candidatePixels[offset + channel]);
                    maximum = Math.Max(maximum, delta);
                    sum += delta;
                    count++;
                }
            }

            return new ImageDelta(maximum, count == 0 ? 0 : (double)sum / count);
        }
    }
}
