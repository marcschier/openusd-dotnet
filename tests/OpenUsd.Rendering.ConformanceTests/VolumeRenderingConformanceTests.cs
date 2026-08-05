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

    private static string Format(double value) =>
        value.ToString("F6", CultureInfo.InvariantCulture);

    private static void WriteEvidence(string fileName, IEnumerable<string> lines)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "volumes");
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, fileName), lines);
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
        string packaged = Path.Combine(AppContext.BaseDirectory, "usd");
        if (File.Exists(Path.Combine(packaged, "plugInfo.json")))
        {
            return packaged;
        }

        if (TryPrepareLocalPluginRuntime(out string localRuntime))
        {
            return localRuntime;
        }

        string? configured = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "plugInfo.json")))
        {
            return configured;
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
        if (!File.Exists(Path.Combine(stormPlugins, "plugInfo.json")) ||
            !File.Exists(hdsilkPlugin) ||
            !File.Exists(hdsilkLibrary))
        {
            return false;
        }

        string runtime = Path.Combine(AppContext.BaseDirectory, "volume-runtime");
        string runtimePlugins = Path.Combine(runtime, "plugin", "usd");
        CopyDirectory(stormPlugins, runtimePlugins);
        string runtimeHdSilkResources = Path.Combine(runtimePlugins, "hdSilk", "resources");
        Directory.CreateDirectory(runtimeHdSilkResources);
        File.Copy(hdsilkPlugin, Path.Combine(runtimeHdSilkResources, "plugInfo.json"), overwrite: true);
        Directory.CreateDirectory(Path.Combine(runtime, "bin"));
        string runtimeHdSilkLibrary = Path.Combine(runtime, "bin", "openusd_hdsilk.dll");
        try
        {
            File.Copy(hdsilkLibrary, runtimeHdSilkLibrary, overwrite: true);
        }
        catch (IOException) when (File.Exists(runtimeHdSilkLibrary))
        {
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
