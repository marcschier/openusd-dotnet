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
    private const string D3D12WarpBackendName = "D3D12 WARP";
    private const string VulkanSwiftShaderBackendName = "Vulkan SwiftShader";

    /// <summary>
    /// Largest single-channel difference tolerated on a scene that binds a real
    /// UsdPreviewSurface, measured at 10 with a mean of 3.793 after hdSilk switched
    /// to Storm's per-pixel normalize(-Peye) eye vector. The previous constant-eye
    /// residual was 16 max / 11.677 mean; this leaves deliberate headroom while
    /// catching the specular-lobe regression the eye-space interpolant closed.
    /// </summary>
    private const byte MaximumShadedChannelDelta = 16;

    private const double MaximumShadedMeanChannelDelta = 8;
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
        ParityScene[] scenes = CreateScenes();
        VerifyExpectedSceneSet(scenes);
        WriteScenePlan(scenes);
        WriteMesaWglSceneExclusions();
        foreach (ParityScene scene in scenes)
        {
            if (!TryCreateInput(scene, out ParityCaptureInput input))
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

            bool stormDeterministic = CapturesAreDeterministic(
                scene,
                input,
                first.Storm,
                second.Storm,
                out ParityComparisonResult? stormDeterminism);
            StormLightSensitivityEvidence? lightSensitivity = null;
            if (scene.SceneLightSensitivityStagePath is not null)
            {
                ParityCaptureSet sensitivity = await ParityCaptureDriver.CaptureAsync(
                    input with { StagePath = scene.SceneLightSensitivityStagePath },
                    StormGlContextFactory.CreateForCurrentPlatform(),
                    Array.Empty<SilkParityBackend>()).ConfigureAwait(false);
                lightSensitivity = new StormLightSensitivityEvidence(
                    scene.SceneLightSensitivityStagePath,
                    Hash(sensitivity.Storm),
                    !first.Storm.Rgba.Span.SequenceEqual(sensitivity.Storm.Rgba.Span));
                evidence.Add(
                    $"{scene.Name} scene-light sensitivity doubledIntensityChangedStorm=" +
                    $"{lightSensitivity.ChangedStorm} doubledHash={lightSensitivity.DoubledIntensityHash}");
                await Assert.That(lightSensitivity.ChangedStorm)
                    .IsTrue()
                    .Because($"{scene.Name} must prove Storm responds to authored scene-light intensity.");
            }
            if (!stormDeterministic)
            {
                WriteCapture(scene.Name, "storm-first", first.Storm);
                WriteCapture(scene.Name, "storm-second", second.Storm);
                if (stormDeterminism is not null)
                {
                    WriteDiff(scene.Name, "storm-determinism", stormDeterminism);
                }
            }

            await Assert.That(stormDeterministic)
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
                await AssertPerformanceBudget(scene, firstSilk);
                ParityComparisonResult result = ParityImageComparer.Compare(
                    first.Storm,
                    firstSilk.Image,
                    input.BackgroundRgba,
                    CreateTolerance(scene));
                string metrics = FormatMetrics(scene, input, first.Storm, firstSilk, result);
                evidence.Add(metrics);
                Console.WriteLine(metrics);
                if (!result.Passed ||
                    result.ReferenceCoveragePixels == 0 ||
                    result.CandidateCoveragePixels == 0)
                {
                    WriteCapture(scene.Name, "storm", first.Storm);
                    WriteCapture(scene.Name, firstSilk.BackendName, firstSilk.Image);
                    WriteDiff(scene.Name, firstSilk.BackendName, result);
                }

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
                    performance = ToEvidence(
                        firstSilk.Statistics,
                        scene.GetPerformanceBudget(firstSilk.BackendName)),
                    firstSilk.Revision,
                    comparison = ToEvidence(result),
                    deterministic = firstSilk.Image.Rgba.Span.SequenceEqual(secondSilk.Image.Rgba.Span),
                });
            }

            evidence.Add($"{scene.Name} Storm sha256={Hash(first.Storm)}");
            if (first.OpenGlEvidence is not null)
            {
                evidence.Add(FormatOpenGlEvidence(scene, first.OpenGlEvidence));
            }
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
                stormOpenGl = first.OpenGlEvidence,
                sceneLightSensitivity = lightSensitivity,
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
            wglMesaExcludedScenes = CreateMesaWglSceneExclusionEvidence(),
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
        ParityScene[] scenes = CreateScenes();
        VerifyExpectedSceneSet(scenes);
        WriteScenePlan(scenes);
        WriteMesaWglSceneExclusions();
        foreach (ParityScene scene in scenes)
        {
            if (!TryCreateInput(scene, out ParityCaptureInput input))
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
            ParityComparisonResult? wrongTimeResult = null;
            if (scene.TimeCode != TimeCode)
            {
                ParityCaptureInput wrongTime = input with { TimeCode = TimeCode };
                ParityCaptureSet wrongTimeCapture = await ParityCaptureDriver.CaptureAsync(
                    wrongTime,
                    StormGlContextFactory.CreateForCurrentPlatform(),
                    backends).ConfigureAwait(false);
                wrongTimeResult = ComparePerturbation(
                    input,
                    scene,
                    baseline.Storm,
                    wrongTimeCapture.SilkCaptures[0].Image);
            }

            double weakestMargin = new[]
            {
                Margin(correct, vertical),
                Margin(correct, horizontal),
                Margin(correct, transposed),
                Margin(correct, shiftedResult),
                wrongTimeResult is null ? double.PositiveInfinity : Margin(correct, wrongTimeResult),
            }.Min();

            string summary = FormatPerturbation(
                scene,
                correct,
                vertical,
                horizontal,
                transposed,
                shiftedResult,
                wrongTimeResult);
            evidence.Add(summary);
            Console.WriteLine(summary);
            if (!correct.Passed || correct.ReferenceCoveragePixels == 0 || correct.CandidateCoveragePixels == 0)
            {
                WriteCapture(scene.Name, "perturbation-storm", baseline.Storm);
                WriteCapture(scene.Name, "perturbation-silk", silk.Image);
                WriteDiff(scene.Name, "perturbation-correct", correct);
            }

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

                if (wrongTimeResult is not null && wrongTimeResult.Passed)
                {
                    failures.Add($"{scene.Name} wrong time code passed the measured threshold.");
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
                wrongTimeCode = wrongTimeResult is null ? null : ToEvidence(wrongTimeResult),
                margins = new
                {
                    verticalFlip = Margin(correct, vertical),
                    horizontalMirror = Margin(correct, horizontal),
                    transposedAxes = Margin(correct, transposed),
                    shiftedCamera = Margin(correct, shiftedResult),
                    wrongTimeCode =
                        wrongTimeResult is null ? (double?)null : Margin(correct, wrongTimeResult),
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
            wglMesaExcludedScenes = CreateMesaWglSceneExclusionEvidence(),
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

    private static void WriteScenePlan(IReadOnlyCollection<ParityScene> scenes)
    {
        string[] gatedNames = scenes.Select(scene => scene.Name).ToArray();
        string[] excludedNames = CreateAllScenes()
            .Select(scene => scene.Name)
            .Except(gatedNames, StringComparer.Ordinal)
            .ToArray();
        string sceneSummary = $"parity scene gate: {gatedNames.Length} scenes";
        string exclusionSummary = excludedNames.Length == 0
            ? "excluded scenes: none"
            : $"excluded scenes: {string.Join(", ", excludedNames)}";
        Console.WriteLine($"[parity-capture] {sceneSummary}; {exclusionSummary}");
        Console.WriteLine($"[parity-capture] gated scenes: {string.Join(", ", gatedNames)}");
        WriteEvidence(
            "parity-capture-scenes.txt",
            [
                sceneSummary,
                exclusionSummary,
                $"gated scenes: {string.Join(", ", gatedNames)}",
            ]);
    }

    private static void VerifyExpectedSceneSet(IReadOnlyCollection<ParityScene> scenes)
    {
        string? expectedCountText = Environment.GetEnvironmentVariable("OPENUSD_PARITY_EXPECTED_SCENE_COUNT");
        if (!string.IsNullOrWhiteSpace(expectedCountText) &&
            (!int.TryParse(
                expectedCountText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int expectedCount) ||
            scenes.Count != expectedCount))
        {
            throw new InvalidOperationException(
                $"Parity gate expected {expectedCountText} scenes but selected {scenes.Count}: " +
                string.Join(", ", scenes.Select(scene => scene.Name)));
        }

        string expectedExcludedText =
            Environment.GetEnvironmentVariable("OPENUSD_PARITY_EXPECTED_EXCLUDED_SCENES") ?? string.Empty;
        string[] expectedExcluded = SplitSceneList(expectedExcludedText);
        string[] gatedNames = scenes.Select(scene => scene.Name).ToArray();
        string[] actualExcluded = CreateAllScenes()
            .Select(scene => scene.Name)
            .Except(gatedNames, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!actualExcluded.SequenceEqual(expectedExcluded.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Parity gate excluded scenes differed from expectation. Expected: " +
                $"{FormatSceneList(expectedExcluded)}; actual: {FormatSceneList(actualExcluded)}.");
        }
    }

    private static string[] SplitSceneList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string FormatSceneList(string[] scenes) =>
        scenes.Length == 0 ? "none" : string.Join(", ", scenes);

    private static void WriteMesaWglSceneExclusions()
    {
        IReadOnlyList<object> exclusions = CreateMesaWglSceneExclusionEvidence();
        if (exclusions.Count == 0)
        {
            return;
        }

        WriteJsonEvidence("parity-capture-mesa-wgl-exclusions.json", new
        {
            schemaVersion = 1,
            generatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            exclusions,
        });
        WriteEvidence(
            "parity-capture-mesa-wgl-exclusions.txt",
            exclusions.Select(exclusion => JsonSerializer.Serialize(exclusion)));
    }

    private static string FormatOpenGlEvidence(ParityScene scene, StormOpenGlEvidence evidence) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{scene.Name} OpenGL loadedOpenGl32={evidence.LoadedOpenGl32} " +
            $"sha256={evidence.LoadedOpenGl32Sha256} " +
            $"renderer='{evidence.Renderer}' version='{evidence.Version}' " +
            $"currentDC={evidence.CurrentDeviceContext} currentContext={evidence.CurrentContext}");

    private static void WriteCapture(string sceneName, string name, ParityImage image)
    {
        string path = Path.Combine(EvidenceDirectory(), $"{SanitizeFileName(sceneName)}-{SanitizeFileName(name)}.bmp");
        WriteBitmap(path, image.Width, image.Height, image.Rgba.Span);
    }

    private static void WriteDiff(string sceneName, string name, ParityComparisonResult result)
    {
        string fileName = $"{SanitizeFileName(sceneName)}-{SanitizeFileName(name)}-diff.bmp";
        string path = Path.Combine(EvidenceDirectory(), fileName);
        WriteBitmap(path, result.Width, result.Height, result.DiffRgba.Span);
    }

    private static string EvidenceDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "parity-capture");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string SanitizeFileName(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value)
        {
            builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), character) < 0 ? character : '-');
        }

        return builder.ToString().Replace(' ', '-');
    }

    private static void WriteBitmap(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        const int fileHeaderSize = 14;
        const int dibHeaderSize = 40;
        int stride = width * ParityImage.BytesPerPixel;
        int pixelBytes = stride * height;
        byte[] bytes = new byte[fileHeaderSize + dibHeaderSize + pixelBytes];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        WriteInt32(bytes, 2, bytes.Length);
        WriteInt32(bytes, 10, fileHeaderSize + dibHeaderSize);
        WriteInt32(bytes, 14, dibHeaderSize);
        WriteInt32(bytes, 18, width);
        WriteInt32(bytes, 22, -height);
        WriteInt16(bytes, 26, 1);
        WriteInt16(bytes, 28, 32);
        WriteInt32(bytes, 34, pixelBytes);
        for (int offset = 0; offset < width * height; offset++)
        {
            int source = offset * ParityImage.BytesPerPixel;
            int target = fileHeaderSize + dibHeaderSize + source;
            bytes[target] = rgba[source + 2];
            bytes[target + 1] = rgba[source + 1];
            bytes[target + 2] = rgba[source];
            bytes[target + 3] = rgba[source + 3];
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void WriteInt16(byte[] bytes, int offset, short value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static ParityComparisonResult ComparePerturbation(
        ParityCaptureInput input,
        ParityScene scene,
        ParityImage reference,
        ParityImage candidate) =>
        ParityImageComparer.Compare(reference, candidate, input.BackgroundRgba, CreateTolerance(scene));

    private static double Margin(ParityComparisonResult correct, ParityComparisonResult perturbation) =>
        correct.AdjustedCoverageIntersectionOverUnion - perturbation.AdjustedCoverageIntersectionOverUnion;

    private static bool CapturesAreDeterministic(
        ParityScene scene,
        ParityCaptureInput input,
        ParityImage first,
        ParityImage second,
        out ParityComparisonResult? comparison)
    {
        comparison = null;
        if (first.Rgba.Span.SequenceEqual(second.Rgba.Span))
        {
            return true;
        }

        if (!IsMesaWglParityRuntime() || scene.ColorComparisonReady)
        {
            return false;
        }

        comparison = ParityImageComparer.Compare(
            first,
            second,
            input.BackgroundRgba,
            ParityTolerance.Geometry with
            {
                MinimumCoverageIntersectionOverUnion = 1,
                MaximumCoverageDifferenceFraction = 0,
                EdgeDilationRadius = 0,
                CompareColor = false,
            });
        return comparison.Passed;
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

    private static bool TryCreateInput(ParityScene scene, out ParityCaptureInput input)
    {
        try
        {
            input = CreateInput(scene);
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
    private static ParityCaptureInput CreateInput(ParityScene scene)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsMesaWglRuntimeLoader.EnsureLoaded();
        }

        RenderHeadlight headlight = OpenUsdStormRuntime.Headlight;
        return new ParityCaptureInput(
            ResolvePluginPath(),
            scene.StagePath,
            Width,
            Height,
            scene.TimeCode,
            new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, scene.ClipPlanes),
            new SilkColor(0, 0, 0, 1),
            headlight,
            scene.UseSceneLights);
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

    private static float[] PlaneValues(Vector4 plane) =>
        [plane.X, plane.Y, plane.Z, plane.W];

    [SupportedOSPlatform("windows")]
    private static SilkParityBackend CreateD3D12WarpBackend() =>
        new(D3D12WarpBackendName, static () => D3D12SilkGraphicsDevice.Create(useWarp: true));

    private static SilkParityBackend CreateVulkanBackend() =>
        new(VulkanSwiftShaderBackendName, static () => VulkanSilkGraphicsDevice.Create());

    private static ParityTolerance CreateTolerance(ParityScene scene) =>
        ParityTolerance.Geometry with
        {
            MinimumCoverageIntersectionOverUnion = scene.RecommendedMinimumAdjustedIou,
            MaximumCoverageDifferenceFraction = 1,
            // Colour is always measured so the evidence records it, but it only
            // gates a scene that binds a real UsdPreviewSurface. The other scenes
            // have no material, so Storm shades them through its own fallback,
            // which hdSilk does not reproduce and which this gate is not about.
            CompareColor = true,
            MaximumChannelDifference =
                scene.ColorComparisonReady ? MaximumShadedChannelDelta : byte.MaxValue,
            MaximumMeanChannelDifference =
                scene.ColorComparisonReady ? MaximumShadedMeanChannelDelta : byte.MaxValue,
        };

    private static ParityScene[] CreateScenes()
    {
        ParityScene[] scenes = CreateAllScenes();
        if (!IsMesaWglParityRuntime())
        {
            return scenes;
        }

        HashSet<string> excluded = CreateMesaWglExcludedSceneNames();
        return scenes.Where(scene => !excluded.Contains(scene.Name)).ToArray();
    }

    private static ParityScene[] CreateAllScenes()
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
                    "Storm and hdSilk now agree exactly: 1.000000 correct adjusted IoU " +
                    "against a 0.592750 worst perturbation. 0.92 keeps 0.08 for " +
                    "rasterization differences on other backends while staying 0.33 " +
                    "clear of the nearest perturbation.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_028, 1_028, 0)),
            },
            new ParityScene(
                "clip-plane-asymmetric",
                Path.Combine(assetRoot, "parity-clip-plane-asymmetric.usda"),
                "Eye-space clip plane removes a substantial asymmetric lobe while an anchor survives.",
                ColorComparisonReady: false,
                GateEnabled: true,
                GateReason:
                    "1.000000 correct adjusted IoU against a 0.675442 worst perturbation, " +
                    "a 0.324558 margin. The plane (1,0,0,0.12) confirms Storm's " +
                    "dot(plane.xyz, Peye) + plane.w < 0 discard convention while " +
                    "removing an asymmetric lobe and leaving the anchor geometry.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                ClipPlanes = [new Vector4(1, 0, 0, 0.12f)],
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(2, 2, 2, 2, 2, 2, 1_072, 1_072, 0)),
            },
            new ParityScene(
                "depth-overlap-multiprim",
                Path.Combine(assetRoot, "parity-depth-overlap-multiprim.usda"),
                "Overlapping prims exercise retained draw order, depth, and per-prim transforms.",
                ColorComparisonReady: false,
                GateEnabled: true,
                GateReason:
                    "1.000000 correct adjusted IoU against a 0.815667 worst perturbation, " +
                    "a 0.184333 margin. It was rejected at 0.074416 only because the " +
                    "projection mismatch depressed the correct score; with exact agreement " +
                    "it clears the required margin.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(3, 3, 3, 3, 3, 3, 1_236, 1_236, 0)),
            },
            new ParityScene(
                "material-normals-uv",
                Path.Combine(assetRoot, "parity-material-normals-uv.usda"),
                "Bound PreviewSurface, authored normals, and UVs travel over the ABI.",
                ColorComparisonReady: true,
                GateEnabled: true,
                GateReason:
                    "1.000000 correct adjusted IoU against a 0.557888 worst perturbation, " +
                    "a 0.442112 margin. The original fan silhouette was close to " +
                    "vertically symmetric and still scored 0.865109 mirrored, a 0.134891 " +
                    "margin below the required 0.18; the pennant shape concentrates mass " +
                    "in the upper right so a flip now costs 0.689940.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_012, 932, 0)),
            },
            new ParityScene(
                "materials-textures",
                Path.Combine(assetRoot, "parity-material-texture-asymmetric.usda"),
                "Texture-backed PreviewSurface samples an asymmetric sRGB asset through UVs.",
                ColorComparisonReady: true,
                GateEnabled: true,
                GateReason:
                    "Texture-backed material coverage and colour now gate: 1.000000 " +
                    "correct adjusted IoU against a 0.781397 worst perturbation " +
                    "(0.218603 margin), with colour deltas max 13 / mean 4.476.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_028, 948, 262_144)),
            },
            new ParityScene(
                "materialx-standard-surface-constant",
                Path.Combine(assetRoot, "parity-materialx-standard-surface-constant.usda"),
                "MaterialX standard_surface uses constant base_color, roughness, and metalness inputs.",
                ColorComparisonReady: true,
                GateEnabled: false,
                GateReason:
                    "Storm renders only the PreviewSurface anchor in this harness while hdSilk shades " +
                    "the MaterialX standard_surface subset, so this records the honest divergence: " +
                    "0.071085 adjusted IoU, colour max 3 / mean 2.813. It stays ungated until " +
                    "Storm MaterialX shading is available in the measured harness.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(2, 2, 2, 2, 2, 2, 1_468, 1_308, 0)),
            },
            new ParityScene(
                "light-distant-exposure",
                Path.Combine(assetRoot, "parity-light-distant-exposure.usda"),
                "UsdLuxDistantLight colour, intensity, and exposure direct-light measurement.",
                ColorComparisonReady: true,
                GateEnabled: false,
                GateReason:
                    "Storm scene-light sensitivity is proven, but the measured direct-light residual " +
                    "still exceeds the colour gate at max 42 / mean 8.675.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                UseSceneLights = true,
                SceneLightSensitivityStagePath =
                    Path.Combine(assetRoot, "parity-light-distant-exposure-double.usda"),
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 988, 908, 0)),
            },
            new ParityScene(
                "light-sphere-point",
                Path.Combine(assetRoot, "parity-light-sphere-point.usda"),
                "UsdLuxSphereLight direct-light transport and point-attenuation measurement.",
                ColorComparisonReady: true,
                GateEnabled: false,
                GateReason:
                    "Storm scene-light sensitivity is proven, but the sphere attenuation/source " +
                    "residual still exceeds the colour gate at max 36 / mean 2.878.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                UseSceneLights = true,
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 988, 908, 0)),
            },
            new ParityScene(
                "light-dome-ambient",
                Path.Combine(assetRoot, "parity-light-dome-ambient.usda"),
                "UsdLuxDomeLight without a texture contributes ambient fill only.",
                ColorComparisonReady: true,
                GateEnabled: true,
                GateReason:
                    "Untextured dome ambient fill gates against Storm's measured fallback: " +
                    "1.000000 adjusted IoU, 0.462455 perturbation margin, and colour " +
                    "deltas max 11 / mean 3.619.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_012, 932, 0)),
            },
            new ParityScene(
                "point-instancer-cluster",
                Path.Combine(assetRoot, "parity-point-instancer-cluster.usda"),
                "Asymmetric point-instanced placement proves expansion and transform handling.",
                ColorComparisonReady: false,
                GateEnabled: true,
                GateReason:
                    "1.000000 correct adjusted IoU against a 0.126576 worst perturbation, " +
                    "a 0.873424 margin -- the widest of any scene. Four small separated " +
                    "triangles make a transform error move coverage rather than merely " +
                    "resize it, so a wrong instance transform collapses the score instead " +
                    "of nudging it.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 4, 1, 1, 1, 4, 1_076, 1_076, 0)),
            },
            new ParityScene(
                "points-asymmetric",
                Path.Combine(assetRoot, "parity-points-asymmetric.usda"),
                "UsdGeomPoints reaches hdSilk as point-list topology.",
                ColorComparisonReady: false,
                GateEnabled: true,
                GateReason:
                    "1.000000 correct adjusted IoU against a 0.436893 worst " +
                    "perturbation, a 0.563107 margin. The scene authors constant " +
                    "width 0.01 because Storm's default point width is world-space " +
                    "and intentionally covers most of the frame; at this measured " +
                    "width both Storm and hdSilk rasterize one pixel per point.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 3_832, 3_832, 0)),
            },
            new ParityScene(
                "cards-draw-mode",
                Path.Combine(assetRoot, "parity-cards-draw-mode.usda"),
                "UsdGeomModelAPI cards proxy geometry travels as an ordinary mesh Rprim.",
                ColorComparisonReady: false,
                GateEnabled: true,
                GateReason:
                    "1.000000 correct adjusted IoU against a 0.730337 worst perturbation, " +
                    "a 0.269663 margin. Cards proxy geometry reaches hdSilk as an ordinary " +
                    "mesh, so this was already at parity and had simply never been tested. " +
                    "The extent is off-centre and strongly non-square on purpose: a card is " +
                    "an axis-aligned rectangle, and a centred near-square one measured a " +
                    "0.012661 margin because mirroring barely changed it.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_472, 1_472, 0)),
            },
            new ParityScene(
                "single-sided-winding",
                Path.Combine(assetRoot, "parity-single-sided-winding.usda"),
                "Back-facing single-sided pennant proves authored double-sidedness is honoured.",
                ColorComparisonReady: false,
                GateEnabled: true,
                GateReason:
                    "1.000000 correct adjusted IoU. The scene pairs the back-facing " +
                    "single-sided pennant, which both renderers must cull, with a " +
                    "front-facing double-sided banner in a different region, which both " +
                    "must draw. That pairing is what makes the gate non-vacuous: an " +
                    "empty-versus-empty scene scores 1.000000 no matter why hdSilk drew " +
                    "nothing, and every perturbation of it also scores 1.000000, so it " +
                    "could not discriminate. With the banner present, failing to cull " +
                    "adds the pennant's coverage and collapses the score, drawing nothing " +
                    "at all fails the positive-coverage requirement, and the perturbations " +
                    "move real coverage.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(2, 2, 2, 2, 2, 2, 1_036, 1_036, 0)),
            },
            new ParityScene(
                "bounds-draw-mode",
                Path.Combine(assetRoot, "parity-bounds-draw-mode.usda"),
                "UsdImaging draw-mode bounds proxy reaches hdSilk as linear segmented basis curves.",
                ColorComparisonReady: false,
                GateEnabled: true,
                GateReason:
                    "1.000000 correct adjusted IoU against a 0.243309 worst perturbation, " +
                    "a 0.756691 margin. hdSilk emits the draw-mode basisCurves as line " +
                    "topology, matching Storm's 251 one-pixel line coverage exactly.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_424, 1_424, 0)),
            },
            new ParityScene(
                "origin-draw-mode",
                Path.Combine(assetRoot, "parity-origin-draw-mode.usda"),
                "UsdImaging draw-mode origin axes reach hdSilk as linear segmented basis curves.",
                ColorComparisonReady: false,
                GateEnabled: true,
                GateReason:
                    "1.000000 correct adjusted IoU against a 0.229167 worst perturbation, " +
                    "a 0.770833 margin. hdSilk emits the draw-mode basisCurves as line " +
                    "topology, matching Storm's 116 one-pixel origin-axis pixels exactly.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 920, 920, 0)),
            },
            new ParityScene(
                "time-varying-transform-primvar",
                Path.Combine(assetRoot, "parity-time-varying-transform-primvar.usda"),
                "Animated transform and displayColor are sampled at timeCode 2.",
                ColorComparisonReady: false,
                GateEnabled: true,
                GateReason:
                    "1.000000 correct adjusted IoU at timeCode 2 against a 0.401624 " +
                    "worst geometric perturbation, a 0.598376 margin. The wrong-time " +
                    "probe compares Storm timeCode 2 with hdSilk timeCode 1 and scores " +
                    "0.045334, so a missed time sample is red rather than silently equivalent.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                TimeCode = 2,
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(2, 2, 2, 2, 2, 2, 1_036, 1_036, 0)),
            },
            new ParityScene(
                "subdivision-catmull-clark",
                Path.Combine(assetRoot, "parity-subdivision-catmull-clark.usda"),
                "Catmull-Clark probe records Storm's harness-complexity subdivision behaviour.",
                ColorComparisonReady: false,
                GateEnabled: false,
                GateReason:
                    "Storm at the parity harness complexity renders this Catmull-Clark " +
                    "scene near hdSilk's coarse topology but not exactly: 0.931015 " +
                    "adjusted IoU, with disagreement split between silhouette and " +
                    "interior fill. It remains ungated until that divergence is " +
                    "eliminated.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(2, 2, 2, 2, 2, 2, 1_288, 1_288, 0)),
            },
            new ParityScene(
                "skinned-pennant",
                Path.Combine(assetRoot, "parity-skinned-pennant.usda"),
                "UsdSkel joint animation deforms an off-centre asymmetric pennant at timeCode 2.",
                ColorComparisonReady: false,
                GateEnabled: true,
                GateReason:
                    "1.000000 correct adjusted IoU at timeCode 2 against a 0.725652 " +
                    "worst geometric perturbation, a 0.274348 margin. The wrong-time " +
                    "probe scores 0.534601, so the scene proves Storm actually samples " +
                    "a deformed UsdSkel pose rather than the rest pose.",
                RecommendedMinimumAdjustedIou: 0.92)
            {
                TimeCode = 2,
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 944, 944, 0)),
            },
        ];
        // parity-curve-width-probe.usda is a diagnostic and is never gated: it
        // proved that Storm rasterizes linear basis curves as 1-pixel
        // screen-space lines, ignoring authored world-space widths -- Storm
        // draws 128 pixels for two segments authored 0.24 units wide. That is
        // why ribbon tessellation was measured and rejected in favour of line
        // topology; see "Ribbon tessellation was tried and measured" in
        // docs/testing.md.
    }

    private static bool IsMesaWglParityRuntime() =>
        OperatingSystem.IsWindows() &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENUSD_MESA_WGL_OPENGL32_PATH"));

    private static HashSet<string> CreateMesaWglExcludedSceneNames() =>
        new(StringComparer.Ordinal)
        {
            "single-sided-winding",
            "bounds-draw-mode",
            "origin-draw-mode",
            "subdivision-catmull-clark",
        };

    private static IReadOnlyList<object> CreateMesaWglSceneExclusionEvidence()
    {
        if (!IsMesaWglParityRuntime())
        {
            return [];
        }

        return
        [
            new
            {
                scene = "single-sided-winding",
                reason = "Mesa llvmpipe Storm draws the culled pennant instead of the double-sided banner.",
                mesaStormCoverage = 2201,
                hdsilkCoverage = 875,
                expectedStormCoverage = 875,
                adjustedIou = 0.0,
                assertion = "adjusted-IoU gate fails in CapturesStormAndHdSilkBackendsDeterministically",
            },
            new
            {
                scene = "bounds-draw-mode",
                reason = "Mesa llvmpipe Storm produces no coverage for the draw-mode basis-curves line proxy.",
                mesaStormCoverage = 0,
                hdsilkCoverage = 251,
                expectedStormCoverage = 251,
                adjustedIou = 0.0,
                assertion = "reference positive-coverage assertion would fail if the scene were gated",
            },
            new
            {
                scene = "origin-draw-mode",
                reason = "Mesa llvmpipe Storm produces no coverage for the draw-mode basis-curves line proxy.",
                mesaStormCoverage = 0,
                hdsilkCoverage = 116,
                expectedStormCoverage = 116,
                adjustedIou = 0.0,
                assertion = "reference positive-coverage assertion would fail if the scene were gated",
            },
        ];
    }

    private static CameraState ShiftCamera(CameraState camera, float x)
    {
        Matrix4x4 view = camera.View;
        view.M41 += x;
        return new CameraState(view, camera.Projection, camera.ClipPlanes);
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
            "src\\OpenUsd.Rendering.Silk\\SilkSceneState.cs",
            "tests\\OpenUsd.Rendering.ConformanceTests\\OpenUsd.Rendering.ConformanceTests.csproj",
            "tests\\OpenUsd.Rendering.ConformanceTests\\ParityCaptureDriver.cs",
            "tests\\OpenUsd.Rendering.ConformanceTests\\StormSilkParityCaptureDriverTests.cs",
            "tests\\OpenUsd.Rendering.ConformanceTests\\WindowsMesaWglRuntimeLoader.cs",
            "tests\\OpenUsd.Rendering.ConformanceTests\\WindowsWglStormContext.cs",
            "test-assets\\parity\\parity-orientation-asymmetric.usda",
            "test-assets\\parity\\parity-depth-overlap-multiprim.usda",
            "test-assets\\parity\\parity-material-normals-uv.usda",
            "test-assets\\parity\\parity-point-instancer-cluster.usda",
            "test-assets\\parity\\parity-single-sided-winding.usda",
            "test-assets\\parity\\parity-clip-plane-asymmetric.usda",
            "test-assets\\parity\\parity-cards-draw-mode.usda",
            "test-assets\\parity\\parity-bounds-draw-mode.usda",
            "test-assets\\parity\\parity-origin-draw-mode.usda",
            "test-assets\\parity\\parity-light-distant-exposure.usda",
            "test-assets\\parity\\parity-light-distant-exposure-double.usda",
            "test-assets\\parity\\parity-light-sphere-point.usda",
            "test-assets\\parity\\parity-light-dome-ambient.usda",
            "docs\\performance.md",
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
            clipPlanes = input.Camera.ClipPlanes.Select(PlaneValues).ToArray(),
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

    private static object ToEvidence(
        SilkSceneGpuStatistics statistics,
        ParityPerformanceBudget budget) =>
        new
        {
            statistics.MeshCount,
            statistics.GeometryBuilds,
            statistics.VertexUploads,
            statistics.IndexUploads,
            statistics.UniformUploads,
            statistics.BufferAllocationBytes,
            statistics.BufferWriteBytes,
            statistics.TextureUploadBytes,
            budget,
        };

    private static async Task AssertPerformanceBudget(
        ParityScene scene,
        SilkParityCapture capture)
    {
        ParityPerformanceBudget budget = scene.GetPerformanceBudget(capture.BackendName);
        await Assert.That(capture.DrawCount)
            .IsLessThanOrEqualTo(budget.MaxDrawCount)
            .Because($"{scene.Name} {capture.BackendName} draw count must stay within the curated-scene budget.");
        await Assert.That(capture.Statistics.MeshCount)
            .IsLessThanOrEqualTo(budget.MaxMeshCount)
            .Because($"{scene.Name} {capture.BackendName} mesh count must stay within the curated-scene budget.");
        await Assert.That(capture.Statistics.GeometryBuilds)
            .IsLessThanOrEqualTo(budget.MaxGeometryBuilds)
            .Because($"{scene.Name} {capture.BackendName} geometry builds must stay within the curated-scene budget.");
        await Assert.That(capture.Statistics.VertexUploads)
            .IsLessThanOrEqualTo(budget.MaxVertexUploads)
            .Because($"{scene.Name} {capture.BackendName} vertex uploads must stay within the curated-scene budget.");
        await Assert.That(capture.Statistics.IndexUploads)
            .IsLessThanOrEqualTo(budget.MaxIndexUploads)
            .Because($"{scene.Name} {capture.BackendName} index uploads must stay within the curated-scene budget.");
        await Assert.That(capture.Statistics.UniformUploads)
            .IsLessThanOrEqualTo(budget.MaxUniformUploads)
            .Because($"{scene.Name} {capture.BackendName} uniform uploads must stay within the curated-scene budget.");
        await Assert.That(capture.Statistics.BufferAllocationBytes)
            .IsLessThanOrEqualTo(budget.MaxBufferAllocationBytes)
            .Because(
                $"{scene.Name} {capture.BackendName} retained buffer bytes must stay within the curated-scene budget.");
        await Assert.That(capture.Statistics.BufferWriteBytes)
            .IsLessThanOrEqualTo(budget.MaxBufferWriteBytes)
            .Because($"{scene.Name} {capture.BackendName} upload bytes must stay within the curated-scene budget.");
        await Assert.That(capture.Statistics.TextureUploadBytes)
            .IsLessThanOrEqualTo(budget.MaxTextureUploadBytes)
            .Because(
                $"{scene.Name} {capture.BackendName} texture upload bytes must stay within the curated-scene budget.");
    }

    private static string FormatMetrics(
        ParityScene scene,
        ParityCaptureInput input,
        ParityImage storm,
        SilkParityCapture silk,
        ParityComparisonResult result) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Scene {0}; Storm vs {1}: storm={2}x{3}, silkRevision={4}, draws={5}, meshes={6}, " +
            "geometryBuilds={7}, vertexUploads={8}, indexUploads={9}, uniformUploads={10}, " +
            "bufferAllocationBytes={11}, bufferWriteBytes={12}, textureUploadBytes={13}, headlight={14}, " +
            "rawIoU={15:F6}, adjustedIoU={16:F6}, coverageDiff={17:F6}, referenceCoverage={18}, " +
            "candidateCoverage={19}, maxChannelDiff={20}, meanChannelDiff={21:F3}, passed={22}; ",
            scene.Name,
            silk.BackendName,
            storm.Width,
            storm.Height,
            silk.Revision,
            silk.DrawCount,
            silk.Statistics.MeshCount,
            silk.Statistics.GeometryBuilds,
            silk.Statistics.VertexUploads,
            silk.Statistics.IndexUploads,
            silk.Statistics.UniformUploads,
            silk.Statistics.BufferAllocationBytes,
            silk.Statistics.BufferWriteBytes,
            silk.Statistics.TextureUploadBytes,
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
        ParityComparisonResult shifted,
        ParityComparisonResult? wrongTime) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Scene {0}; correct={1:F6}; vertical={2:F6}; horizontal={3:F6}; transpose={4:F6}; " +
            "shift={5:F6}; wrongTime={6}; weakestMargin={7:F6}; threshold={8:F6}; gated={9}",
            scene.Name,
            correct.AdjustedCoverageIntersectionOverUnion,
            vertical.AdjustedCoverageIntersectionOverUnion,
            horizontal.AdjustedCoverageIntersectionOverUnion,
            transposed.AdjustedCoverageIntersectionOverUnion,
            shifted.AdjustedCoverageIntersectionOverUnion,
            wrongTime is null
                ? "n/a"
                : wrongTime.AdjustedCoverageIntersectionOverUnion.ToString("F6", CultureInfo.InvariantCulture),
            new[]
            {
                Margin(correct, vertical),
                Margin(correct, horizontal),
                Margin(correct, transposed),
                Margin(correct, shifted),
                wrongTime is null ? double.PositiveInfinity : Margin(correct, wrongTime),
            }.Min(),
            scene.RecommendedMinimumAdjustedIou,
            scene.GateEnabled);

    private static string FileHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static Dictionary<string, ParityPerformanceBudget> CurrentBackendBudgets(
        ParityPerformanceBudget budget) =>
        new Dictionary<string, ParityPerformanceBudget>(StringComparer.Ordinal)
        {
            [D3D12WarpBackendName] = budget,
            [VulkanSwiftShaderBackendName] = budget,
        };

    private sealed record ParityScene(
        string Name,
        string StagePath,
        string Purpose,
        bool ColorComparisonReady,
        bool GateEnabled,
        string GateReason,
        double RecommendedMinimumAdjustedIou)
    {
        public IReadOnlyList<Vector4> ClipPlanes { get; init; } = [];

        public double TimeCode { get; init; } = StormSilkParityCaptureDriverTests.TimeCode;

        public bool UseSceneLights { get; init; }

        public string? SceneLightSensitivityStagePath { get; init; }

        public required Dictionary<string, ParityPerformanceBudget> PerformanceBudgets { get; init; }

        public ParityPerformanceBudget GetPerformanceBudget(string backendName) =>
            PerformanceBudgets.TryGetValue(backendName, out ParityPerformanceBudget budget)
                ? budget
                : throw new InvalidOperationException(
                    $"Scene '{Name}' has no performance budget for backend '{backendName}'.");
    }

    private sealed record StormLightSensitivityEvidence(
        string DoubledIntensityStagePath,
        string DoubledIntensityHash,
        bool ChangedStorm);

    private readonly record struct ParityPerformanceBudget(
        int MeasuredDrawCount,
        int MaxDrawCount,
        int MeasuredMeshCount,
        int MaxMeshCount,
        ulong MeasuredGeometryBuilds,
        ulong MaxGeometryBuilds,
        ulong MeasuredVertexUploads,
        ulong MaxVertexUploads,
        ulong MeasuredIndexUploads,
        ulong MaxIndexUploads,
        ulong MeasuredUniformUploads,
        ulong MaxUniformUploads,
        ulong MeasuredBufferAllocationBytes,
        ulong MaxBufferAllocationBytes,
        ulong MeasuredBufferWriteBytes,
        ulong MaxBufferWriteBytes,
        ulong MeasuredTextureUploadBytes,
        ulong MaxTextureUploadBytes)
    {
        public static ParityPerformanceBudget FromMeasured(
            int drawCount,
            int meshCount,
            ulong geometryBuilds,
            ulong vertexUploads,
            ulong indexUploads,
            ulong uniformUploads,
            ulong bufferAllocationBytes,
            ulong bufferWriteBytes,
            ulong textureUploadBytes) =>
            new(
                drawCount,
                CountBudget(drawCount),
                meshCount,
                CountBudget(meshCount),
                geometryBuilds,
                CountBudget(geometryBuilds),
                vertexUploads,
                CountBudget(vertexUploads),
                indexUploads,
                CountBudget(indexUploads),
                uniformUploads,
                CountBudget(uniformUploads),
                bufferAllocationBytes,
                ByteBudget(bufferAllocationBytes),
                bufferWriteBytes,
                ByteBudget(bufferWriteBytes),
                textureUploadBytes,
                ByteBudget(textureUploadBytes));

        private static int CountBudget(int measured) =>
            measured == 0 ? 0 : measured + Math.Max(1, (measured + 3) / 4);

        private static ulong CountBudget(ulong measured) =>
            measured == 0 ? 0 : checked(measured + Math.Max(1UL, (measured + 3) / 4));

        private static ulong ByteBudget(ulong measured) =>
            measured == 0 ? 0 : checked(measured + Math.Max(256UL, (measured + 3) / 4));
    }
}
