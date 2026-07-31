// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const double MinimumDiscriminationMargin = 0.18;
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new() { WriteIndented = true };

    [Test]
    public async Task CapturesStormAndHdSilkBackendsDeterministically()
    {
        if (!StormGlContextFactory.IsCurrentPlatformSupported || !CanCreatePlatformGlContext())
        {
            return;
        }

        SilkParityBackend[] backends = CreateBackends();
        var evidence = new List<string>();
        var jsonEvidence = new List<object>();
        foreach (ParityScene scene in CreateScenes())
        {
            if (!TryCreateInput(scene.StagePath, out ParityCaptureInput input))
            {
                return;
            }

            ParityCaptureSet first = await ParityCaptureDriver.CaptureAsync(
                input,
                StormGlContextFactory.CreateForCurrentPlatform(),
                backends).ConfigureAwait(false);
            ParityCaptureSet second = await ParityCaptureDriver.CaptureAsync(
                input,
                StormGlContextFactory.CreateForCurrentPlatform(),
                backends).ConfigureAwait(false);

            await Assert.That(first.Storm.Rgba.Span.SequenceEqual(second.Storm.Rgba.Span))
                .IsTrue()
                .Because($"Storm parity capture for {scene.Name} must be byte-stable.");

            var backendEvidence = new List<object>();
            for (int i = 0; i < first.SilkCaptures.Count; i++)
            {
                SilkParityCapture firstSilk = first.SilkCaptures[i];
                SilkParityCapture secondSilk = second.SilkCaptures[i];
                await Assert.That(firstSilk.Image.Rgba.Span.SequenceEqual(secondSilk.Image.Rgba.Span))
                    .IsTrue()
                    .Because($"{firstSilk.BackendName} parity capture for {scene.Name} must be byte-stable.");
                await Assert.That(firstSilk.DrawCount).IsGreaterThan(0);
                ParityComparisonResult result = ParityImageComparer.Compare(
                    first.Storm,
                    firstSilk.Image,
                    input.BackgroundRgba,
                    CreateTolerance(scene));
                string metrics = FormatMetrics(scene, input, first.Storm, firstSilk, result);
                evidence.Add(metrics);
                Console.WriteLine(metrics);
                await Assert.That(result.ReferenceCoveragePixels).IsGreaterThan(0);
                await Assert.That(result.CandidateCoveragePixels).IsGreaterThan(0);
                if (scene.GateEnabled)
                {
                    await Assert.That(result.Passed)
                        .IsTrue()
                        .Because(
                            $"{scene.Name} {firstSilk.BackendName} must meet its measured adjusted-IoU floor.");
                }

                backendEvidence.Add(new
                {
                    backend = firstSilk.BackendName,
                    firstHash = Hash(firstSilk.Image),
                    secondHash = Hash(secondSilk.Image),
                    firstSilk.DrawCount,
                    firstSilk.Revision,
                    comparison = ToEvidence(result),
                    deterministic = firstSilk.Image.Rgba.Span.SequenceEqual(secondSilk.Image.Rgba.Span),
                });
            }

            evidence.Add($"{scene.Name} Storm sha256={Hash(first.Storm)}");
            Console.WriteLine($"{scene.Name} Storm sha256={Hash(first.Storm)}");
            jsonEvidence.Add(new
            {
                scene = scene.Name,
                scene.StagePath,
                scene.Purpose,
                scene.ColorComparisonReady,
                scene.GateEnabled,
                scene.GateReason,
                scene.RecommendedMinimumAdjustedIou,
                stormFirstHash = Hash(first.Storm),
                stormSecondHash = Hash(second.Storm),
                deterministic = first.Storm.Rgba.Span.SequenceEqual(second.Storm.Rgba.Span),
                stageIdentity = CreateStageIdentity(scene),
                cameraIdentity = CreateCameraIdentity(input),
                backends = backendEvidence,
            });
        }

        WriteEvidence("parity-capture-metrics.txt", evidence);
        WriteJsonEvidence("parity-capture-evidence.json", new
        {
            schemaVersion = 1,
            generatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            sourceIdentity = CreateSourceIdentity(),
            packageIdentity = CreatePackageIdentity(ResolvePluginPath()),
            normalization = new
            {
                rowOrder = "Storm GL readback is converted bottom-up to top-down; hdSilk is top-down.",
                clear = "The captured corner background is mapped to the requested opaque clear colour.",
                alpha = "Captures are normalized to opaque alpha before coverage comparison.",
                color = "Coverage is gated today; colour remains opt-in until hdSilk implements BRDF shading.",
            },
            scenes = jsonEvidence,
        });
    }

    [Test]
    public async Task ComparisonDetectsPerturbedCaptures()
    {
        if (!StormGlContextFactory.IsCurrentPlatformSupported || !CanCreatePlatformGlContext())
        {
            return;
        }

        SilkParityBackend[] backends = [CreatePrimaryPerturbationBackend()];
        var evidence = new List<string>();
        var jsonEvidence = new List<object>();
        var failures = new List<string>();
        foreach (ParityScene scene in CreateScenes())
        {
            if (!TryCreateInput(scene.StagePath, out ParityCaptureInput input))
            {
                return;
            }

            ParityCaptureSet baseline = await ParityCaptureDriver.CaptureAsync(
                input,
                StormGlContextFactory.CreateForCurrentPlatform(),
                backends).ConfigureAwait(false);
            SilkParityCapture silk = baseline.SilkCaptures[0];
            ParityComparisonResult correct = ParityImageComparer.Compare(
                baseline.Storm,
                silk.Image,
                input.BackgroundRgba,
                CreateTolerance(scene));
            ParityComparisonResult vertical = ComparePerturbation(
                input,
                scene,
                baseline.Storm,
                MirrorVertically(silk.Image));
            ParityComparisonResult horizontal = ComparePerturbation(
                input,
                scene,
                baseline.Storm,
                MirrorHorizontally(silk.Image));
            ParityComparisonResult transposed = ComparePerturbation(
                input,
                scene,
                baseline.Storm,
                TransposeWithinCanvas(silk.Image));

            ParityCaptureInput shifted = input with { Camera = ShiftCamera(input.Camera, 0.5f) };
            ParityCaptureSet shiftedCapture = await ParityCaptureDriver.CaptureAsync(
                shifted,
                StormGlContextFactory.CreateForCurrentPlatform(),
                backends).ConfigureAwait(false);
            ParityComparisonResult shiftedResult = ComparePerturbation(
                input,
                scene,
                baseline.Storm,
                shiftedCapture.SilkCaptures[0].Image);
            double weakestMargin = new[]
            {
                Margin(correct, vertical),
                Margin(correct, horizontal),
                Margin(correct, transposed),
                Margin(correct, shiftedResult),
            }.Min();

            string summary = FormatPerturbation(scene, correct, vertical, horizontal, transposed, shiftedResult);
            evidence.Add(summary);
            Console.WriteLine(summary);
            if (scene.GateEnabled)
            {
                if (vertical.Passed)
                {
                    failures.Add($"{scene.Name} vertical flip passed the measured threshold.");
                }

                if (horizontal.Passed)
                {
                    failures.Add($"{scene.Name} horizontal mirror passed the measured threshold.");
                }

                if (transposed.Passed)
                {
                    failures.Add($"{scene.Name} transpose passed the measured threshold.");
                }

                if (shiftedResult.Passed)
                {
                    failures.Add($"{scene.Name} shifted camera passed the measured threshold.");
                }

                if (weakestMargin < MinimumDiscriminationMargin)
                {
                    failures.Add(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} weakest margin {1:F6} is below {2:F6}.",
                            scene.Name,
                            weakestMargin,
                            MinimumDiscriminationMargin));
                }
            }

            jsonEvidence.Add(new
            {
                scene = scene.Name,
                scene.Purpose,
                scene.GateEnabled,
                scene.GateReason,
                correct = ToEvidence(correct),
                verticalFlip = ToEvidence(vertical),
                horizontalMirror = ToEvidence(horizontal),
                transposedAxes = ToEvidence(transposed),
                shiftedCamera = ToEvidence(shiftedResult),
                margins = new
                {
                    verticalFlip = Margin(correct, vertical),
                    horizontalMirror = Margin(correct, horizontal),
                    transposedAxes = Margin(correct, transposed),
                    shiftedCamera = Margin(correct, shiftedResult),
                    weakest = weakestMargin,
                },
                recommendation = new
                {
                    scene.RecommendedMinimumAdjustedIou,
                    scene.GateEnabled,
                    scene.GateReason,
                },
            });
        }

        WriteEvidence("parity-capture-perturbations.txt", evidence);
        WriteJsonEvidence("parity-capture-perturbations.json", new
        {
            schemaVersion = 1,
            generatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            minimumRequiredMargin = MinimumDiscriminationMargin,
            rejectedScenes = new[]
            {
                new
                {
                    name = "single-asymmetric-panel",
                    reason = "The original probe measured about 0.11 adjusted-IoU margin to a vertical flip.",
                },
            },
            scenes = jsonEvidence,
        });
        await Assert.That(failures.Count)
            .IsEqualTo(0)
            .Because(string.Join(Environment.NewLine, failures));
    }

    private static void WriteEvidence(string fileName, IEnumerable<string> lines)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "parity-capture");
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, fileName), lines, new UTF8Encoding(false));
    }

    private static void WriteJsonEvidence(string fileName, object evidence)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "parity-capture");
        Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(evidence, EvidenceJsonOptions);
        File.WriteAllText(Path.Combine(directory, fileName), json + "\n", new UTF8Encoding(false));
    }

    private static ParityComparisonResult ComparePerturbation(
        ParityCaptureInput input,
        ParityScene scene,
        ParityImage reference,
        ParityImage candidate) =>
        ParityImageComparer.Compare(reference, candidate, input.BackgroundRgba, CreateTolerance(scene));

    private static double Margin(ParityComparisonResult correct, ParityComparisonResult perturbation) =>
        correct.AdjustedCoverageIntersectionOverUnion - perturbation.AdjustedCoverageIntersectionOverUnion;

    private static bool CanCreatePlatformGlContext()
    {
        try
        {
            using IStormGlContext context = StormGlContextFactory.CreateForCurrentPlatform()
                .Create(1, 1, new SilkColor(0, 0, 0, 1));
            context.Finish();
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
            or DllNotFoundException or EntryPointNotFoundException
            or PlatformNotSupportedException)
        {
            SkipOrFail("platform OpenGL parity capture", exception.ToString());
            return false;
        }
    }

    /// <summary>
    /// Turns an unavailable capture into a hard failure. A parity gate that
    /// silently skips is worse than no gate: it reports success while proving
    /// nothing, which is exactly how these tests behaved before this existed --
    /// Storm could not load, every scene was skipped, and the suite stayed green.
    /// CI sets this so the gate is real there; it stays opt-in locally because a
    /// developer without a staged native runtime should not be blocked.
    /// </summary>
    private static bool RequireCapture =>
        Environment.GetEnvironmentVariable("OPENUSD_PARITY_CAPTURE_REQUIRED") is "1" or "true";

    private static void SkipOrFail(string reason, string detail)
    {
        WriteEvidence("parity-capture-skip.txt", [detail]);
        if (RequireCapture)
        {
            throw new InvalidOperationException(
                $"{reason} and OPENUSD_PARITY_CAPTURE_REQUIRED demands a real capture. {detail}");
        }
        Console.WriteLine($"Skipping {reason}: {detail}");
    }

    private static bool TryCreateInput(string stagePath, out ParityCaptureInput input)
    {
        try
        {
            input = CreateInput(stagePath);
            return true;
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or DirectoryNotFoundException
                or OpenUsdStormException)
        {
            SkipOrFail("Storm-to-hdSilk parity capture", exception.ToString());
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

        string runtime = Path.Combine(AppContext.BaseDirectory, "parity-capture", "runtime");
        string runtimePlugins = Path.Combine(runtime, "plugin", "usd");
        CopyDirectory(stormPlugins, runtimePlugins);
        string runtimeHdSilkResources = Path.Combine(runtimePlugins, "hdSilk", "resources");
        Directory.CreateDirectory(runtimeHdSilkResources);
        File.Copy(hdsilkPlugin, Path.Combine(runtimeHdSilkResources, "plugInfo.json"), overwrite: true);
        Directory.CreateDirectory(Path.Combine(runtime, "bin"));
        string runtimeHdSilkLibrary = Path.Combine(runtime, "bin", "openusd_hdsilk.dll");
        if (!File.Exists(runtimeHdSilkLibrary))
        {
            File.Copy(hdsilkLibrary, runtimeHdSilkLibrary);
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

    private static SilkParityBackend[] CreateBackends()
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindowsBackends();
        }

        if (OperatingSystem.IsLinux())
        {
            return [CreateVulkanBackend()];
        }

        throw new PlatformNotSupportedException("The parity harness supports Windows WGL and Linux GLX.");
    }

    private static SilkParityBackend CreatePrimaryPerturbationBackend()
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateD3D12WarpBackend();
        }

        if (OperatingSystem.IsLinux())
        {
            return CreateVulkanBackend();
        }

        throw new PlatformNotSupportedException("The parity harness supports Windows WGL and Linux GLX.");
    }

    [SupportedOSPlatform("windows")]
    private static SilkParityBackend[] CreateWindowsBackends() =>
        [
            CreateD3D12WarpBackend(),
            CreateVulkanBackend(),
        ];

    [SupportedOSPlatform("windows")]
    private static SilkParityBackend CreateD3D12WarpBackend() =>
        new("D3D12 WARP", static () => D3D12SilkGraphicsDevice.Create(useWarp: true));

    private static SilkParityBackend CreateVulkanBackend() =>
        new("Vulkan SwiftShader", static () => VulkanSilkGraphicsDevice.Create());

    private static ParityTolerance CreateTolerance(ParityScene scene) =>
        ParityTolerance.Geometry with
        {
            MinimumCoverageIntersectionOverUnion = scene.RecommendedMinimumAdjustedIou,
            MaximumCoverageDifferenceFraction = 1,
            CompareColor = false,
        };

    private static IReadOnlyList<ParityScene> CreateScenes()
    {
        string root = FindRepositoryRoot() ?? AppContext.BaseDirectory;
        string assetRoot = Path.Combine(root, "test-assets", "parity");
        return
        [
            new ParityScene(
                "orientation-asymmetric",
                Path.Combine(assetRoot, "parity-orientation-asymmetric.usda"),
                "Large L-shaped silhouette catches vertical flips, horizontal mirrors, and transposes.",
                ColorComparisonReady: false,
                GateEnabled: true,
                GateReason:
                    "Measured 0.709154 correct adjusted IoU against a 0.516599 worst perturbation; " +
                    "0.61 leaves about 0.09 headroom on both sides.",
                RecommendedMinimumAdjustedIou: 0.61),
            new ParityScene(
                "depth-overlap-multiprim",
                Path.Combine(assetRoot, "parity-depth-overlap-multiprim.usda"),
                "Overlapping prims exercise retained draw order, depth, and per-prim transforms.",
                ColorComparisonReady: false,
                GateEnabled: false,
                GateReason:
                    "Rejected for now: measured 0.817816 correct adjusted IoU but 0.743399 " +
                    "for horizontal mirror, only a 0.074416 margin.",
                RecommendedMinimumAdjustedIou: 0.78),
            new ParityScene(
                "material-normals-uv",
                Path.Combine(assetRoot, "parity-material-normals-uv.usda"),
                "Bound PreviewSurface, authored normals, and UVs travel over the ABI.",
                ColorComparisonReady: true,
                GateEnabled: false,
                GateReason:
                    "Rejected for now: measured 0.865894 correct adjusted IoU but 0.782579 " +
                    "for vertical flip, only a 0.083315 margin.",
                RecommendedMinimumAdjustedIou: 0.82),
            new ParityScene(
                "point-instancer-cluster",
                Path.Combine(assetRoot, "parity-point-instancer-cluster.usda"),
                "Asymmetric point-instanced placement proves expansion and transform handling.",
                ColorComparisonReady: false,
                GateEnabled: false,
                GateReason:
                    "Rejected for now: measured 0.175142 correct adjusted IoU and 0.142396 " +
                    "worst perturbation, only a 0.032745 margin.",
                RecommendedMinimumAdjustedIou: 0.16),
        ];
    }

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

    private static ParityImage MirrorHorizontally(ParityImage image)
    {
        ReadOnlySpan<byte> source = image.Rgba.Span;
        byte[] mirrored = new byte[source.Length];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int sourceOffset = (y * image.Width + x) * ParityImage.BytesPerPixel;
                int destinationOffset = (y * image.Width + image.Width - 1 - x) * ParityImage.BytesPerPixel;
                source.Slice(sourceOffset, ParityImage.BytesPerPixel)
                    .CopyTo(mirrored.AsSpan(destinationOffset, ParityImage.BytesPerPixel));
            }
        }

        return new ParityImage(image.Width, image.Height, mirrored);
    }

    private static ParityImage TransposeWithinCanvas(ParityImage image)
    {
        ReadOnlySpan<byte> source = image.Rgba.Span;
        byte[] transposed = new byte[source.Length];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int targetX = (int)MathF.Round(y * (image.Width - 1f) / (image.Height - 1f));
                int targetY = (int)MathF.Round(x * (image.Height - 1f) / (image.Width - 1f));
                int sourceOffset = (y * image.Width + x) * ParityImage.BytesPerPixel;
                int destinationOffset = (targetY * image.Width + targetX) * ParityImage.BytesPerPixel;
                source.Slice(sourceOffset, ParityImage.BytesPerPixel)
                    .CopyTo(transposed.AsSpan(destinationOffset, ParityImage.BytesPerPixel));
            }
        }

        return new ParityImage(image.Width, image.Height, transposed);
    }

    private static object CreateSourceIdentity()
    {
        string root = FindRepositoryRoot() ?? AppContext.BaseDirectory;
        string[] paths =
        [
            "Directory.Build.props",
            "Directory.Packages.props",
            "global.json",
            ".github\\workflows\\render.yml",
            "eng\\run-parity-capture.ps1",
            "src\\OpenUsd.Rendering\\ParityImageComparison.cs",
            "tests\\OpenUsd.Rendering.ConformanceTests\\OpenUsd.Rendering.ConformanceTests.csproj",
            "tests\\OpenUsd.Rendering.ConformanceTests\\ParityCaptureDriver.cs",
            "tests\\OpenUsd.Rendering.ConformanceTests\\StormSilkParityCaptureDriverTests.cs",
            "test-assets\\parity\\parity-orientation-asymmetric.usda",
            "test-assets\\parity\\parity-depth-overlap-multiprim.usda",
            "test-assets\\parity\\parity-material-normals-uv.usda",
            "test-assets\\parity\\parity-point-instancer-cluster.usda",
            "docs\\testing.md",
        ];
        var files = new List<object>();
        foreach (string relative in paths)
        {
            string path = Path.Combine(root, relative);
            if (!File.Exists(path))
            {
                continue;
            }

            var file = new FileInfo(path);
            files.Add(new
            {
                path = Path.GetRelativePath(root, path).Replace('\\', '/'),
                sha256 = FileHash(path),
                length = file.Length,
            });
        }

        string payload = JsonSerializer.Serialize(files);
        string sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new { sha256, fileCount = files.Count, files };
    }

    private static object CreateStageIdentity(ParityScene scene)
    {
        var file = new FileInfo(scene.StagePath);
        return new
        {
            path = Path.GetFileName(scene.StagePath),
            sha256 = FileHash(scene.StagePath),
            length = file.Length,
        };
    }

    private static object CreateCameraIdentity(ParityCaptureInput input) =>
        new
        {
            input.Width,
            input.Height,
            input.TimeCode,
            view = MatrixValues(input.Camera.View),
            projection = MatrixValues(input.Camera.Projection),
            clearColor = new[]
            {
                input.ClearColor.Red,
                input.ClearColor.Green,
                input.ClearColor.Blue,
                input.ClearColor.Alpha,
            },
            input.Headlight,
        };

    private static object CreatePackageIdentity(string pluginPath)
    {
        var roots = new List<string> { pluginPath };
        string runtimeRoot = Path.GetFullPath(Path.Combine(pluginPath, "..", ".."));
        string runtimeBin = Path.Combine(runtimeRoot, "bin");
        if (Directory.Exists(runtimeBin))
        {
            roots.Add(runtimeBin);
        }

        string? repoRoot = FindRepositoryRoot();
        string? metadataPath = repoRoot is null
            ? null
            : Path.Combine(repoRoot, "native", "install", "win-x64", ".openusd-install-metadata.json");
        var files = new List<object>();
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                files.Add(new
                {
                    path = Path.GetRelativePath(root, file).Replace('\\', '/'),
                    root = Path.GetFileName(root),
                    sha256 = FileHash(file),
                    length = info.Length,
                });
            }
        }

        string payload = JsonSerializer.Serialize(files);
        return new
        {
            pluginPath,
            sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            fileCount = files.Count,
            installMetadataSha256 = metadataPath is not null && File.Exists(metadataPath)
                ? FileHash(metadataPath)
                : null,
            files,
        };
    }

    private static float[] MatrixValues(Matrix4x4 matrix) =>
        [
            matrix.M11,
            matrix.M12,
            matrix.M13,
            matrix.M14,
            matrix.M21,
            matrix.M22,
            matrix.M23,
            matrix.M24,
            matrix.M31,
            matrix.M32,
            matrix.M33,
            matrix.M34,
            matrix.M41,
            matrix.M42,
            matrix.M43,
            matrix.M44,
        ];

    private static object ToEvidence(ParityComparisonResult result) =>
        new
        {
            result.CoverageIntersectionOverUnion,
            result.AdjustedCoverageIntersectionOverUnion,
            result.CoverageDifferenceFraction,
            result.ReferenceCoveragePixels,
            result.CandidateCoveragePixels,
            result.CoverageIntersectionPixels,
            result.CoverageUnionPixels,
            result.UnforgivenCoverageDifferencePixels,
            result.MaximumChannelDifference,
            result.MeanChannelDifference,
            result.Passed,
            result.Diagnostics,
        };

    private static string FormatMetrics(
        ParityScene scene,
        ParityCaptureInput input,
        ParityImage storm,
        SilkParityCapture silk,
        ParityComparisonResult result) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Scene {0}; Storm vs {1}: storm={2}x{3}, silkRevision={4}, draws={5}, headlight={6}, " +
            "rawIoU={7:F6}, adjustedIoU={8:F6}, coverageDiff={9:F6}, referenceCoverage={10}, " +
            "candidateCoverage={11}, maxChannelDiff={12}, meanChannelDiff={13:F3}, passed={14}; ",
            scene.Name,
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

    private static string FormatPerturbation(
        ParityScene scene,
        ParityComparisonResult correct,
        ParityComparisonResult vertical,
        ParityComparisonResult horizontal,
        ParityComparisonResult transposed,
        ParityComparisonResult shifted) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Scene {0}; correct={1:F6}; vertical={2:F6}; horizontal={3:F6}; transpose={4:F6}; " +
            "shift={5:F6}; weakestMargin={6:F6}; threshold={7:F6}; gated={8}",
            scene.Name,
            correct.AdjustedCoverageIntersectionOverUnion,
            vertical.AdjustedCoverageIntersectionOverUnion,
            horizontal.AdjustedCoverageIntersectionOverUnion,
            transposed.AdjustedCoverageIntersectionOverUnion,
            shifted.AdjustedCoverageIntersectionOverUnion,
            new[]
            {
                Margin(correct, vertical),
                Margin(correct, horizontal),
                Margin(correct, transposed),
                Margin(correct, shifted),
            }.Min(),
            scene.RecommendedMinimumAdjustedIou,
            scene.GateEnabled);

    private static string FileHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed record ParityScene(
        string Name,
        string StagePath,
        string Purpose,
        bool ColorComparisonReady,
        bool GateEnabled,
        string GateReason,
        double RecommendedMinimumAdjustedIou);
}
