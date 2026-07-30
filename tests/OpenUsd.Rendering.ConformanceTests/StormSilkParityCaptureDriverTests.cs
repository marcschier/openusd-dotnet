// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Numerics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Vulkan;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class StormSilkParityCaptureDriverTests
{
    private const int Width = 160;
    private const int Height = 128;
    private const double TimeCode = 1;
    private const string RequiredEnvironmentVariable = "OPENUSD_PARITY_CAPTURE_REQUIRED";

    [Test]
    public async Task CapturesStormAndHdSilkBackendsDeterministically()
    {
        if (!StormGlContextFactory.IsCurrentPlatformSupported)
        {
            if (IsParityCaptureRequired())
            {
                throw new PlatformNotSupportedException(
                    "Required Storm parity capture has no OpenGL context shim for this platform.");
            }

            return;
        }

        if (!CanCreatePlatformGlContext())
        {
            return;
        }

        if (!TryCreateInput(CreateStagePath("parity-fixed.usda"), out ParityCaptureInput input))
        {
            return;
        }
        SilkParityBackend[] backends = CreatePlatformBackends();
        ParityCaptureSet first = await ParityCaptureDriver.CaptureAsync(
            input,
            StormGlContextFactory.CreateForCurrentPlatform(),
            backends).ConfigureAwait(false);
        ParityCaptureSet second = await ParityCaptureDriver.CaptureAsync(
            input,
            StormGlContextFactory.CreateForCurrentPlatform(),
            backends).ConfigureAwait(false);

        var evidence = new List<string>();
        await Assert.That(first.Storm.Rgba.Span.SequenceEqual(second.Storm.Rgba.Span))
            .IsTrue()
            .Because("Storm parity capture must be byte-stable for the same scene and camera.");

        for (int i = 0; i < first.SilkCaptures.Count; i++)
        {
            SilkParityCapture firstSilk = first.SilkCaptures[i];
            SilkParityCapture secondSilk = second.SilkCaptures[i];
            await Assert.That(firstSilk.Image.Rgba.Span.SequenceEqual(secondSilk.Image.Rgba.Span))
                .IsTrue()
                .Because($"{firstSilk.BackendName} parity capture must be byte-stable.");
            await Assert.That(firstSilk.DrawCount).IsGreaterThan(0);
            ParityComparisonResult result = ParityImageComparer.Compare(
                first.Storm,
                firstSilk.Image,
                input.BackgroundRgba,
                ParityTolerance.Geometry);
            string metrics = FormatMetrics(input, first.Storm, firstSilk, result);
            evidence.Add(metrics);
            Console.WriteLine(metrics);
            await Assert.That(result.ReferenceCoveragePixels).IsGreaterThan(0);
            await Assert.That(result.CandidateCoveragePixels).IsGreaterThan(0);
        }

        evidence.Add($"Storm sha256={Hash(first.Storm)}");
        Console.WriteLine($"Storm sha256={Hash(first.Storm)}");
        foreach (SilkParityCapture capture in first.SilkCaptures)
        {
            evidence.Add($"{capture.BackendName} sha256={Hash(capture.Image)}");
            Console.WriteLine($"{capture.BackendName} sha256={Hash(capture.Image)}");
        }
        WriteEvidence("parity-capture-metrics.txt", evidence);
    }

    [Test]
    public async Task ComparisonDetectsPerturbedCaptures()
    {
        if (!StormGlContextFactory.IsCurrentPlatformSupported)
        {
            if (IsParityCaptureRequired())
            {
                throw new PlatformNotSupportedException(
                    "Required Storm parity capture has no OpenGL context shim for this platform.");
            }

            return;
        }

        if (!CanCreatePlatformGlContext())
        {
            return;
        }

        if (!TryCreateInput(CreateStagePath("parity-perturb.usda"), out ParityCaptureInput input))
        {
            return;
        }
        SilkParityBackend[] backends = [CreatePrimaryPlatformBackend()];
        ParityCaptureSet baseline = await ParityCaptureDriver.CaptureAsync(
            input,
            StormGlContextFactory.CreateForCurrentPlatform(),
            backends).ConfigureAwait(false);

        ParityComparisonResult flipped = ParityImageComparer.Compare(
            baseline.SilkCaptures[0].Image,
            MirrorVertically(baseline.SilkCaptures[0].Image),
            input.BackgroundRgba,
            ParityTolerance.Geometry);
        var evidence = new List<string>();
        evidence.Add($"Vertical flip detection: {flipped.Diagnostics}");
        Console.WriteLine($"Vertical flip detection: {flipped.Diagnostics}");
        await Assert.That(flipped.Passed)
            .IsFalse()
            .Because("A vertically flipped Storm capture must not compare equal to itself.");

        ParityCaptureInput shifted = input with { Camera = ShiftCamera(input.Camera, 0.14f) };
        ParityCaptureSet shiftedCapture = await ParityCaptureDriver.CaptureAsync(
            shifted,
            StormGlContextFactory.CreateForCurrentPlatform(),
            backends).ConfigureAwait(false);
        ParityComparisonResult shiftedResult = ParityImageComparer.Compare(
            baseline.SilkCaptures[0].Image,
            shiftedCapture.SilkCaptures[0].Image,
            input.BackgroundRgba,
            ParityTolerance.Geometry);
        evidence.Add($"Shifted camera detection: {shiftedResult.Diagnostics}");
        Console.WriteLine($"Shifted camera detection: {shiftedResult.Diagnostics}");
        await Assert.That(shiftedResult.Passed)
            .IsFalse()
            .Because("A deliberately shifted camera must change the captured coverage.");
        WriteEvidence("parity-capture-perturbations.txt", evidence);
    }

    private static void WriteEvidence(string fileName, IEnumerable<string> lines)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "parity-capture");
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, fileName), lines, new UTF8Encoding(false));
    }

    private static bool CanCreatePlatformGlContext()
    {
        try
        {
            using IStormGlContext context = StormGlContextFactory.CreateForCurrentPlatform()
                .Create(1, 1, new SilkColor(0, 0, 0, 1));
            context.Finish();
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
            or InvalidOperationException or PlatformNotSupportedException)
        {
            WriteEvidence("parity-capture-skip.txt", [$"OpenGL context: {exception}"]);
            if (IsParityCaptureRequired())
            {
                throw new InvalidOperationException(
                    "Required Storm parity capture could not create its platform OpenGL context.",
                    exception);
            }

            Console.WriteLine($"Skipping Storm-to-hdSilk parity capture: {exception.Message}");
            return false;
        }
    }

    private static string CreateStagePath(string fileName)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "parity-capture");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, ParityStageText, new UTF8Encoding(false));
        return path;
    }

    private static bool TryCreateInput(string stagePath, out ParityCaptureInput input)
    {
        try
        {
            input = CreateInput(stagePath);
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            WriteEvidence("parity-capture-skip.txt", [$"Input: {exception}"]);
            if (IsParityCaptureRequired())
            {
                throw new InvalidOperationException(
                    "Required Storm parity capture could not create its native runtime input.",
                    exception);
            }

            Console.WriteLine($"Skipping Storm-to-hdSilk parity capture: {exception.Message}");
            input = null!;
            return false;
        }
    }
    private static ParityCaptureInput CreateInput(string stagePath)
    {
        RenderHeadlight headlight = OpenUsdStormRuntime.Headlight;
        return new ParityCaptureInput(
            ResolvePluginPath(),
            stagePath,
            Width,
            Height,
            TimeCode,
            new CameraState(Matrix4x4.Identity, Matrix4x4.Identity),
            new SilkColor(0, 0, 0, 1),
            headlight);
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

        string rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        string build = Path.Combine(root, "native", "build", "shim", rid);
        string stormPlugins = Path.Combine(
            build,
            "openusd_hydra",
            "tests",
            "storm-wgl-runtime",
            "plugin",
            "usd");
        if (OperatingSystem.IsLinux())
        {
            stormPlugins = Path.Combine(root, "native", "install", rid, "plugin", "usd");
        }

        string hdsilkPlugin = Path.Combine(build, "hdSilk", "resources", "plugInfo.json");
        string hdsilkLibrary = Path.Combine(build, "hdSilk", GetHdSilkLibraryName());
        if (OperatingSystem.IsLinux())
        {
            hdsilkPlugin = Path.Combine(
                root,
                "native",
                "install",
                "shim",
                rid,
                "plugin",
                "usd",
                "hdSilk",
                "resources",
                "plugInfo.json");
            hdsilkLibrary = Path.Combine(
                root,
                "native",
                "install",
                "shim",
                rid,
                "lib",
                GetHdSilkLibraryName());
        }

        if (!File.Exists(Path.Combine(stormPlugins, "plugInfo.json")) ||
            !File.Exists(hdsilkPlugin) ||
            !File.Exists(hdsilkLibrary))
        {
            return false;
        }

        string runtime = Path.Combine(AppContext.BaseDirectory, "parity-capture", "runtime");
        string runtimePlugins = Path.Combine(runtime, "plugin", "usd");
        CopyDirectory(stormPlugins, runtimePlugins);
        string runtimeHdSilkResources = Path.Combine(runtimePlugins, "hdSilk", "resources");
        Directory.CreateDirectory(runtimeHdSilkResources);
        File.Copy(hdsilkPlugin, Path.Combine(runtimeHdSilkResources, "plugInfo.json"), overwrite: true);
        string runtimeLibraryDirectory = Path.Combine(runtime, OperatingSystem.IsWindows() ? "bin" : "lib");
        Directory.CreateDirectory(runtimeLibraryDirectory);
        string runtimeHdSilkLibrary = Path.Combine(runtimeLibraryDirectory, GetHdSilkLibraryName());
        if (!File.Exists(runtimeHdSilkLibrary))
        {
            File.Copy(hdsilkLibrary, runtimeHdSilkLibrary);
        }

        pluginPath = runtimePlugins;
        return true;
    }

    private static string GetHdSilkLibraryName() =>
        OperatingSystem.IsWindows() ? "openusd_hdsilk.dll" : "libopenusd_hdsilk.so";

    private static bool IsParityCaptureRequired() =>
        string.Equals(
            Environment.GetEnvironmentVariable(RequiredEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

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

    private static SilkParityBackend[] CreateWindowsBackends()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("D3D12 WARP parity capture requires Windows.");
        }

        return
        [
            CreateD3D12WarpBackend(),
            new SilkParityBackend("Vulkan SwiftShader", static () => VulkanSilkGraphicsDevice.Create()),
        ];
    }

    private static SilkParityBackend[] CreatePlatformBackends()
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindowsBackends();
        }

        if (OperatingSystem.IsLinux())
        {
            return [CreateLinuxVulkanBackend()];
        }

        throw new PlatformNotSupportedException(
            "Storm parity capture currently supports Windows WGL and Linux GLX.");
    }

    private static SilkParityBackend CreatePrimaryPlatformBackend()
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateD3D12WarpBackend();
        }

        if (OperatingSystem.IsLinux())
        {
            return CreateLinuxVulkanBackend();
        }

        throw new PlatformNotSupportedException(
            "Storm parity capture currently supports Windows WGL and Linux GLX.");
    }

    [SupportedOSPlatform("windows")]
    private static SilkParityBackend CreateD3D12WarpBackend() =>
        new("D3D12 WARP", static () => D3D12SilkGraphicsDevice.Create(useWarp: true));

    private static SilkParityBackend CreateLinuxVulkanBackend() =>
        new("Vulkan software", static () => VulkanSilkGraphicsDevice.Create());

    private static CameraState ShiftCamera(CameraState camera, float x)
    {
        Matrix4x4 view = camera.View;
        view.M41 += x;
        return new CameraState(view, camera.Projection);
    }

    private static ParityImage MirrorVertically(ParityImage image)
    {
        int stride = image.Width * ParityImage.BytesPerPixel;
        ReadOnlySpan<byte> source = image.Rgba.Span;
        byte[] mirrored = new byte[source.Length];
        for (int row = 0; row < image.Height; row++)
        {
            source.Slice(row * stride, stride)
                .CopyTo(mirrored.AsSpan((image.Height - 1 - row) * stride, stride));
        }

        return new ParityImage(image.Width, image.Height, mirrored);
    }

    private static string FormatMetrics(
        ParityCaptureInput input,
        ParityImage storm,
        SilkParityCapture silk,
        ParityComparisonResult result) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Storm vs {0}: storm={1}x{2}, silkRevision={3}, draws={4}, headlight={5}, " +
            "rawIoU={6:F6}, adjustedIoU={7:F6}, coverageDiff={8:F6}, referenceCoverage={9}, " +
            "candidateCoverage={10}, maxChannelDiff={11}, meanChannelDiff={12:F3}, passed={13}; ",
            silk.BackendName,
            storm.Width,
            storm.Height,
            silk.Revision,
            silk.DrawCount,
            input.Headlight,
            result.CoverageIntersectionOverUnion,
            result.AdjustedCoverageIntersectionOverUnion,
            result.CoverageDifferenceFraction,
            result.ReferenceCoveragePixels,
            result.CandidateCoveragePixels,
            result.MaximumChannelDifference,
            result.MeanChannelDifference,
            result.Passed) +
        result.Diagnostics;

    private static string Hash(ParityImage image) =>
        Convert.ToHexString(SHA256.HashData(image.Rgba.Span));

    private const string ParityStageText = """
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
    def Mesh "AsymmetricPanel"
    {
        uniform bool doubleSided = 1
        uniform token subdivisionScheme = "none"
        point3f[] points = [
            (-0.62, -0.45, 0.10),
            ( 0.25, -0.45, 0.10),
            ( 0.25,  0.10, 0.10),
            (-0.15,  0.55, 0.10),
            (-0.62,  0.10, 0.10)
        ]
        int[] faceVertexCounts = [3, 3, 3]
        int[] faceVertexIndices = [0, 1, 2, 0, 2, 4, 4, 2, 3]
        color3f[] primvars:displayColor = [(0.85, 0.55, 0.20)]
        uniform token primvars:displayColor:interpolation = "constant"
    }
}
""";
}
