// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
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
    private const string MetalBackendName = "Metal";
    private const double ExactCuratedParityAdjustedIou = 1.0;
    private const string EmptyStage = """
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
}
""";

    private const string GeneratedMaterialXSelfConsistencyStage = """
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
    def Mesh "GeneratedMtlxQuad" (
        prepend apiSchemas = ["MaterialBindingAPI"]
    )
    {
        rel material:binding = </World/Looks/GeneratedMtlx>
        uniform bool doubleSided = 1
        uniform token subdivisionScheme = "none"
        point3f[] points = [
            (-0.925, -0.52, 0.08), (-0.325, -0.52, 0.08),
            (-0.325, 0.48, 0.08), (-0.925, 0.48, 0.08)
        ]
        int[] faceVertexCounts = [3, 3]
        int[] faceVertexIndices = [0, 1, 2, 0, 2, 3]
        normal3f[] normals = [(0, 0, 1), (0, 0, 1), (0, 0, 1), (0, 0, 1)] (
            interpolation = "vertex"
        )
        color3f[] primvars:displayColor = [(0.12, 0.12, 0.12)]
        uniform token primvars:displayColor:interpolation = "constant"
    }

    def Mesh "PreviewQuad" (
        prepend apiSchemas = ["MaterialBindingAPI"]
    )
    {
        rel material:binding = </World/Looks/PreviewEquivalent>
        uniform bool doubleSided = 1
        uniform token subdivisionScheme = "none"
        point3f[] points = [
            (0.325, -0.52, 0.08), (0.925, -0.52, 0.08),
            (0.925, 0.48, 0.08), (0.325, 0.48, 0.08)
        ]
        int[] faceVertexCounts = [3, 3]
        int[] faceVertexIndices = [0, 1, 2, 0, 2, 3]
        normal3f[] normals = [(0, 0, 1), (0, 0, 1), (0, 0, 1), (0, 0, 1)] (
            interpolation = "vertex"
        )
        color3f[] primvars:displayColor = [(0.12, 0.12, 0.12)]
        uniform token primvars:displayColor:interpolation = "constant"
    }

    def Scope "Looks"
    {
        def Material "GeneratedMtlx" (
            prepend apiSchemas = ["MaterialXConfigAPI"]
        )
        {
            string config:mtlx:version = "1.38"
            token outputs:surface.connect = </World/Looks/GeneratedMtlx/Unlit.outputs:out>
            token outputs:mtlx:surface.connect = </World/Looks/GeneratedMtlx/Unlit.outputs:out>
            def Shader "Color"
            {
                uniform token info:id = "ND_constant_color3"
                color3f inputs:value = (0.45, 0.18, 0.06)
                color3f outputs:out
            }
            def Shader "Multiply"
            {
                uniform token info:id = "ND_multiply_color3FA"
                color3f inputs:in1.connect = </World/Looks/GeneratedMtlx/Color.outputs:out>
                float inputs:in2 = 2.0
                color3f outputs:out
            }
            def Shader "Unlit"
            {
                uniform token info:id = "ND_surface_unlit"
                color3f inputs:emission_color.connect = </World/Looks/GeneratedMtlx/Multiply.outputs:out>
                token outputs:out
            }
        }
        def Material "PreviewEquivalent"
        {
            token outputs:surface.connect = </World/Looks/PreviewEquivalent/PreviewSurface.outputs:surface>
            def Shader "PreviewSurface"
            {
                uniform token info:id = "UsdPreviewSurface"
                color3f inputs:diffuseColor = (0, 0, 0)
                color3f inputs:emissiveColor = (0.90, 0.36, 0.12)
                float inputs:roughness = 1.0
                token outputs:surface
            }
        }
    }
}
""";
    private const byte MinimumTextureDivergenceChannelDelta = 24;
    private const byte MinimumColorSpaceDivergenceChannelDelta = 24;
    private const byte MinimumCullDivergenceChannelDelta = 96;
    private const byte MinimumLightDivergenceChannelDelta = 16;
    private const byte MinimumMaterialTextureDivergenceChannelDelta = 16;
    private static readonly Lazy<nuint> ImagePluginsRegistered = new(
        RegisterImagePlugins,
        LazyThreadSafetyMode.ExecutionAndPublication);

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
    public async Task CuratedSceneParityClaimsAreStructured()
    {
        ParityScene[] scenes = CreateAllScenes();
        ParityScene[] gatedScenes = scenes.Where(static scene => scene.GateEnabled).ToArray();
        ParityScene[] ungatedScenes = scenes.Where(static scene => !scene.GateEnabled).ToArray();

        await Assert.That(scenes.Length).IsEqualTo(25);
        await Assert.That(gatedScenes.Length).IsEqualTo(22);
        await Assert.That(ungatedScenes.Length).IsEqualTo(3);
        foreach (ParityScene scene in gatedScenes)
        {
            await Assert.That(scene.RequiredAdjustedIou).IsEqualTo(ExactCuratedParityAdjustedIou)
                .Because($"{scene.Name} is a documented exact-parity gate.");
        }

        foreach (ParityScene scene in ungatedScenes)
        {
            await Assert.That(scene.RequiredAdjustedIou).IsNull()
                .Because($"{scene.Name} is measured but deliberately not an exact-parity gate.");
        }
    }

    [Test]
    public async Task CapturesStormAndHdSilkBackendsDeterministically()
    {
        ParityScene[] scenes = CreateScenes();
        VerifyExpectedSceneSet(scenes);
        WriteScenePlan(scenes);
        WriteMesaWglSceneExclusions();
        if (!StormGlContextFactory.IsCurrentPlatformSupported)
        {
            SkipOrFail("platform OpenGL parity capture", "Storm GL contexts are not supported on this platform.");
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.");
        }
        if (!CanCreatePlatformGlContext())
        {
            return;
        }

        SilkParityBackend[] backends = CreateBackends();
        var evidence = new List<string>();
        var adjustedIouEvidence = new List<string>
        {
            "scene\tbackend\tachievedAdjustedIoU\trequiredAdjustedIoU",
        };
        var jsonEvidence = new List<object>();
        foreach (ParityScene scene in scenes)
        {
            if (!TryCreateInput(scene, out ParityCaptureInput input))
            {
                if (RequireCapture)
                {
                    throw new InvalidOperationException($"{scene.Name} did not produce parity-capture input.");
                }

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
            StormShadowSensitivityEvidence? shadowSensitivity = null;
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
            if (scene.ShadowDisabledStagePath is not null)
            {
                ParityCaptureSet disabled = await ParityCaptureDriver.CaptureAsync(
                    input with { StagePath = scene.ShadowDisabledStagePath },
                    StormGlContextFactory.CreateForCurrentPlatform(),
                    Array.Empty<SilkParityBackend>()).ConfigureAwait(false);
                ParityComparisonResult disabledResult = ParityImageComparer.Compare(
                    first.Storm,
                    disabled.Storm,
                    input.BackgroundRgba,
                    CreateTolerance(scene));
                shadowSensitivity = new StormShadowSensitivityEvidence(
                    scene.ShadowDisabledStagePath,
                    Hash(disabled.Storm),
                    !first.Storm.Rgba.Span.SequenceEqual(disabled.Storm.Rgba.Span),
                    ToEvidence(disabledResult));
                evidence.Add(
                    $"{scene.Name} shadow-disabled sensitivity changedStorm=" +
                    $"{shadowSensitivity.ChangedStorm} disabledHash={shadowSensitivity.DisabledHash} " +
                    "disabledAdjustedIoU=" +
                    disabledResult.AdjustedCoverageIntersectionOverUnion.ToString(
                        "F6",
                        CultureInfo.InvariantCulture));
                if (scene.GateEnabled)
                {
                    await Assert.That(shadowSensitivity.ChangedStorm)
                        .IsTrue()
                        .Because($"{scene.Name} must prove Storm responds to disabling authored shadows.");
                    await Assert.That(disabledResult.Passed)
                        .IsFalse()
                        .Because($"{scene.Name} must fail when authored shadows are disabled.");
                }
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
                adjustedIouEvidence.Add(FormatAdjustedIouEvidence(scene, firstSilk, result));
                Console.WriteLine(metrics);
                if (!result.Passed ||
                    !MeetsRequiredAdjustedIou(scene, result) ||
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
                    await AssertRequiredAdjustedIou(scene, firstSilk.BackendName, result)
                        .ConfigureAwait(false);
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
                scene.RequiredAdjustedIou,
                stormFirstHash = Hash(first.Storm),
                stormSecondHash = Hash(second.Storm),
                stormOpenGl = first.OpenGlEvidence,
                sceneLightSensitivity = lightSensitivity,
                shadowSensitivity,
                deterministic = first.Storm.Rgba.Span.SequenceEqual(second.Storm.Rgba.Span),
                stageIdentity = CreateStageIdentity(scene),
                cameraIdentity = CreateCameraIdentity(input),
                backends = backendEvidence,
            });
        }

        WriteEvidence("parity-capture-metrics.txt", evidence);
        WriteEvidence("parity-capture-adjusted-iou.tsv", adjustedIouEvidence);
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
        ParityScene[] scenes = CreateScenes();
        VerifyExpectedSceneSet(scenes);
        WriteScenePlan(scenes);
        WriteMesaWglSceneExclusions();
        if (!StormGlContextFactory.IsCurrentPlatformSupported)
        {
            SkipOrFail("platform OpenGL parity capture", "Storm GL contexts are not supported on this platform.");
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.");
        }
        if (!CanCreatePlatformGlContext())
        {
            return;
        }

        SilkParityBackend[] backends = [CreatePrimaryPerturbationBackend()];
        var evidence = new List<string>();
        var jsonEvidence = new List<object>();
        var failures = new List<string>();
        foreach (ParityScene scene in scenes)
        {
            if (!TryCreateInput(scene, out ParityCaptureInput input))
            {
                if (RequireCapture)
                {
                    throw new InvalidOperationException($"{scene.Name} did not produce parity-capture input.");
                }

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
                    scene.RequiredAdjustedIou,
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

    [Test]
    public async Task SilkComplexityDefaultPreservesExplicitLowPointPage()
    {
        MeshPageStats defaultStats;
        MeshPageStats explicitLowStats;
        try
        {
            PrependHdSilkNativeSearchPath();
            string stagePath = ResolveParityAsset("parity-points-asymmetric.usda");
            string pluginPath = ResolvePluginPath();

            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
            defaultStats = CaptureMeshPageStats(session, complexity: null);
            _ = CaptureMeshPageStats(session, RenderComplexity.Medium);
            explicitLowStats = CaptureMeshPageStats(session, RenderComplexity.Low);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk complexity default page", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        await Assert.That(defaultStats.PointListPointCount).IsGreaterThan(0);
        await Assert.That(explicitLowStats).IsEqualTo(defaultStats);
    }

    [Test]
    public async Task HdSilkDomeAmbientPreservesAuthoredColorIntensityAndExposure()
    {
        Vector3 ambient;
        try
        {
            PrependHdSilkNativeSearchPath();
            string stagePath = ResolveParityAsset("parity-light-dome-authored.usda");
            string pluginPath = ResolvePluginPath();

            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
            using OpenUsdSilkPage page = session.Sync(64, 64);
            using SilkCommandEnumerator commands = page.GetEnumerator();
            if (!commands.MoveNext())
            {
                throw new InvalidDataException("The hdSilk page contains no frame command.");
            }
            SilkFrameCommand frame = commands.Current.AsFrame();
            ambient = new Vector3(
                frame.GetAmbientColor(0),
                frame.GetAmbientColor(1),
                frame.GetAmbientColor(2));
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk authored dome ambient", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        await Assert.That(ambient.X).IsEqualTo(0.24f).Within(0.0001f);
        await Assert.That(ambient.Y).IsEqualTo(0.48f).Within(0.0001f);
        await Assert.That(ambient.Z).IsEqualTo(0.72f).Within(0.0001f);
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public async Task ChasePresentationRetainsHighlightsOnD3D12()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        OutputTransformSaturation saturation;
        try
        {
            using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
            saturation = CaptureChaseOutputTransform(device);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk chase presentation D3D12", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        await AssertChaseOutputTransform(saturation);
    }

    [Test]
    public async Task ChasePresentationRetainsHighlightsOnVulkan()
    {
        OutputTransformSaturation saturation;
        try
        {
            using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
            saturation = CaptureChaseOutputTransform(device);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk chase presentation Vulkan", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        await AssertChaseOutputTransform(saturation);
    }

    [Test]
    public async Task SilkComplexityMediumChangesPointPage()
    {
        MeshPageStats lowStats;
        MeshPageStats mediumStats;
        try
        {
            PrependHdSilkNativeSearchPath();
            string stagePath = ResolveParityAsset("parity-points-asymmetric.usda");
            string pluginPath = ResolvePluginPath();

            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
            lowStats = CaptureMeshPageStats(session, RenderComplexity.Low);
            mediumStats = CaptureMeshPageStats(session, RenderComplexity.Medium);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk complexity medium page", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        await Assert.That(lowStats.PointListPointCount).IsGreaterThan(0);
        await Assert.That(mediumStats.PointListPointCount).IsGreaterThan(lowStats.PointListPointCount);
        await Assert.That(mediumStats.PointListIndexCount).IsGreaterThan(lowStats.PointListIndexCount);
    }


    [Test]
    public async Task SilkWireframeDrawModeDivergesFromSmoothShadedPixelsOnD3D12()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        ParityImage smooth;
        ParityImage wireframe;
        try
        {
            smooth = CaptureHdSilkDrawModeD3D12(RenderDrawMode.SmoothShaded);
            wireframe = CaptureHdSilkDrawModeD3D12(RenderDrawMode.Wireframe);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk draw-mode D3D12 divergence", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        int differentBytes = CountDifferentBytes(smooth.Rgba.Span, wireframe.Rgba.Span);
        string line = "draw-mode-divergence smoothHash=" + Hash(smooth) +
            " wireframeHash=" + Hash(wireframe) +
            " differentBytes=" + differentBytes.ToString(CultureInfo.InvariantCulture);
        Console.WriteLine(line);
        WriteEvidence("draw-mode-divergence.txt", [line]);
        if (differentBytes == 0)
        {
            WriteCapture("draw-mode-divergence", "smooth", smooth);
            WriteCapture("draw-mode-divergence", "wireframe", wireframe);
        }

        await Assert.That(ContainsPixelDifferentFromClear(smooth.Rgba.Span, new Vector4(0, 0, 0, 1))).IsTrue();
        await Assert.That(ContainsPixelDifferentFromClear(wireframe.Rgba.Span, new Vector4(0, 0, 0, 1))).IsTrue();
        await Assert.That(differentBytes).IsGreaterThan(0);
    }

    [Test]
    public async Task SilkFrameCaptureReturnsDimensionsAndNonTrivialPixels()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        SilkFrameCaptureResult capture;
        var settings = new RenderSettings(
            1,
            enableLighting: true,
            enableShadows: true,
            new Vector4(0.07f, 0.11f, 0.17f, 1),
            backfaceCulling: true,
            useSceneMaterials: true,
            RenderComplexity.Low);
        try
        {
            PrependHdSilkNativeSearchPath();
            string stagePath = ResolveParityAsset("parity-points-asymmetric.usda");
            string pluginPath = ResolvePluginPath();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
            using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);

            capture = SilkFrameCapture.Capture(
                session,
                device,
                64,
                48,
                settings,
                TimeCode,
                CameraState.Default);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk frame capture", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        await Assert.That(capture.Width).IsEqualTo(64);
        await Assert.That(capture.Height).IsEqualTo(48);
        await Assert.That(capture.Rgba.Length).IsEqualTo(64 * 48 * ParityImage.BytesPerPixel);
        ReadOnlySpan<byte> pixels = capture.Rgba.Span;
        AssertNonZeroPixel(pixels, 0);
        AssertNonZeroPixel(pixels, (64 * 48 / 2) * ParityImage.BytesPerPixel);
        AssertNonZeroPixel(pixels, ((64 * 48) - 1) * ParityImage.BytesPerPixel);
        await Assert.That(ContainsPixelDifferentFromClear(pixels, settings.ClearColor)).IsTrue();
    }

    [Test]
    public async Task SilkFrameCaptureRendersEveryFrameFromTheSameSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        SilkFrameCaptureResult first;
        SilkFrameCaptureResult second;
        SilkFrameCaptureResult third;
        var settings = new RenderSettings(
            1,
            enableLighting: true,
            enableShadows: true,
            new Vector4(0.07f, 0.11f, 0.17f, 1),
            backfaceCulling: true,
            useSceneMaterials: true,
            RenderComplexity.Low);
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 0.6f, 3.2f), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(0.9f, 64f / 48f, 0.05f, 100f);
        var explicitCamera = new CameraState(view, projection);
        try
        {
            PrependHdSilkNativeSearchPath();
            string stagePath = ResolveParityAsset("parity-points-asymmetric.usda");
            string pluginPath = ResolvePluginPath();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
            using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
            using var capturer = new SilkFrameCapturer(device);

            first = capturer.Capture(session, 64, 48, settings, TimeCode, CameraState.Default);
            second = capturer.Capture(session, 64, 48, settings, TimeCode, CameraState.Default);
            third = capturer.Capture(session, 64, 48, settings, TimeCode, explicitCamera);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk repeated frame capture", exception.ToString());
            return;
        }

        Console.WriteLine(
            "repeated-capture first.DrawCount=" +
            first.RenderResult.DrawCount.ToString(CultureInfo.InvariantCulture) +
            " second.DrawCount=" + second.RenderResult.DrawCount.ToString(CultureInfo.InvariantCulture) +
            " third.DrawCount=" + third.RenderResult.DrawCount.ToString(CultureInfo.InvariantCulture));

        await Assert.That(ContainsPixelDifferentFromClear(first.Rgba.Span, settings.ClearColor)).IsTrue();

        // A repeated capture from the same session must render the same retained scene. hdSilk
        // Sync reports only what changed, so a capturer that rebuilt its scene state per call
        // would see a page with no geometry here and silently produce a blank frame - which a
        // caller cannot distinguish from a legitimately empty view.
        await Assert.That(second.RenderResult.DrawCount).IsGreaterThan(0);
        await Assert.That(ContainsPixelDifferentFromClear(second.Rgba.Span, settings.ClearColor)).IsTrue();

        // Changing the camera on an existing session must re-render from the new viewpoint.
        await Assert.That(third.RenderResult.DrawCount).IsGreaterThan(0);
        await Assert.That(ContainsPixelDifferentFromClear(third.Rgba.Span, settings.ClearColor)).IsTrue();
    }

    [Test]
    public async Task SilkFrameCaptureRefusesToCaptureABlankFrameFromASynchronizedSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        try
        {
            PrependHdSilkNativeSearchPath();
            string stagePath = ResolveParityAsset("parity-points-asymmetric.usda");
            string pluginPath = ResolvePluginPath();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
            using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);

            SilkFrameCaptureResult first = SilkFrameCapture.Capture(
                session, device, 64, 48, TimeCode, CameraState.Default);
            await Assert.That(first.RenderResult.DrawCount).IsGreaterThan(0);

            // Recapturing at the same time code yields an empty delta page, and the one-shot
            // helper builds a renderer per call with no retained scene, so this would be a
            // silently blank frame. A repeat capture whose stage actually changed still works.
            await Assert.That(() => SilkFrameCapture.Capture(
                session, device, 64, 48, TimeCode, CameraState.Default))
                .Throws<InvalidOperationException>();
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk one-shot capture guard", exception.ToString());
        }
    }

    [Test]
    public async Task SilkFrameCaptureRetainedRendersTheSceneALiveRendererSynchronized()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        const ulong retainedPageRevision = 7;
        SilkMeshRenderResult presented;
        SilkFrameCaptureResult capture;
        var settings = new RenderSettings(
            1,
            enableLighting: true,
            enableShadows: true,
            new Vector4(0.07f, 0.11f, 0.17f, 1),
            backfaceCulling: true,
            useSceneMaterials: true,
            RenderComplexity.Low);
        var options = new SilkMeshRenderOptions(
            new SilkColor(
                settings.ClearColor.X,
                settings.ClearColor.Y,
                settings.ClearColor.Z,
                settings.ClearColor.W),
            1,
            settings.BackfaceCulling,
            settings.UseSceneMaterials);
        try
        {
            PrependHdSilkNativeSearchPath();
            string stagePath = ResolveParityAsset("parity-points-asymmetric.usda");
            string pluginPath = ResolvePluginPath();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
            using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
            using var renderer = new SilkMeshRenderer(device);
            using ISilkGraphicsTexture color = device.CreateTexture2D(
                new SilkTextureDescriptor(
                    64,
                    48,
                    SilkTextureFormat.Rgba8Unorm,
                    SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
            using ISilkGraphicsTexture depth = device.CreateTexture2D(
                SilkTextureDescriptor.DepthTarget(64, 48));

            // Stand in for the viewer's render loop, which synchronizes the session and renders
            // through the renderer it keeps alive for the lifetime of the backend session.
            presented = renderer.SyncAndRender(session, color, depth, TimeCode, options);

            capture = SilkFrameCapture.CaptureRetained(
                renderer,
                device,
                64,
                48,
                settings,
                retainedPageRevision);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk retained frame capture", exception.ToString());
            return;
        }

        await Assert.That(presented.DrawCount).IsGreaterThan(0);

        // Capturing the same session again would be refused, because Sync has nothing left to
        // report. That is the viewer's Capture Frame command: it captured through the session
        // its presentation renderer had already synchronized.
        await Assert.That(capture.RenderResult.DrawCount).IsEqualTo(presented.DrawCount);
        await Assert.That(ContainsPixelDifferentFromClear(capture.Rgba.Span, settings.ClearColor))
            .IsTrue();

        // A retained capture applies no page, so it reports the revision it was told about and
        // consumes no hdSilk commands.
        await Assert.That(capture.PageRevision).IsEqualTo(retainedPageRevision);
        await Assert.That(capture.CommandCount).IsEqualTo(0u);
    }

    [Test]
    public async Task SilkFrameCaptureReturnsBlankFrameForEmptyUnsynchronizedSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        SilkFrameCaptureResult capture;
        var settings = new RenderSettings(
            1,
            enableLighting: true,
            enableShadows: false,
            new Vector4(0.19f, 0.23f, 0.29f, 1),
            backfaceCulling: true,
            useSceneMaterials: true,
            RenderComplexity.Low);
        try
        {
            PrependHdSilkNativeSearchPath();
            string stagePath = WriteEmptyStage();
            string pluginPath = ResolvePluginPath();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
            using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);

            capture = SilkFrameCapture.Capture(
                session,
                device,
                32,
                24,
                settings,
                TimeCode,
                CameraState.Default);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk empty-stage one-shot capture", exception.ToString());
            return;
        }

        await Assert.That(capture.Width).IsEqualTo(32);
        await Assert.That(capture.Height).IsEqualTo(24);
        await Assert.That(capture.RenderResult.DrawCount).IsEqualTo(0);
        await Assert.That(ContainsPixelDifferentFromClear(capture.Rgba.Span, settings.ClearColor)).IsFalse();
    }

    [Test]
    public async Task DisplayColorReachesPixelsForImplicitSurfacesAndMeshes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        SilkFrameCaptureResult capture;
        var settings = new RenderSettings(
            1,
            enableLighting: true,
            enableShadows: false,
            new Vector4(0, 0, 0, 1),
            backfaceCulling: false,
            useSceneMaterials: true,
            RenderComplexity.Low);
        try
        {
            PrependHdSilkNativeSearchPath();
            string? repositoryRoot = FindRepositoryRoot()
                ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
            string stagePath = Path.Combine(repositoryRoot, "test-assets", "viewer-stage-camera-smoke.usda");
            string pluginPath = ResolvePluginPath();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
            using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
            capture = SilkFrameCapture.Capture(session, device, 256, 192, settings, 0, CameraState.Default);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk displayColor capture", exception.ToString());
            return;
        }

        // The stage authors a red Cube (0.9, 0.16, 0.08), a green Cube (0.1, 0.85, 0.28) and a
        // blue-grey Mesh backdrop, all as constant-interpolation primvars:displayColor. Lighting
        // and tone mapping shift the absolute values, so assert only that pixels of each hue
        // reach the frame - which is what a consumer distinguishing parts by colour relies on.
        int reddish = 0;
        int greenish = 0;
        int bluish = 0;
        ReadOnlySpan<byte> pixels = capture.Rgba.Span;
        for (int offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            int r = pixels[offset];
            int g = pixels[offset + 1];
            int b = pixels[offset + 2];
            if (r > g + 24 && r > b + 24)
            {
                reddish++;
            }
            else if (g > r + 24 && g > b + 24)
            {
                greenish++;
            }
            else if (b > r + 24 && b > g + 24)
            {
                bluish++;
            }
        }

        Console.WriteLine(
            "displayColor-pixels reddish=" + reddish.ToString(CultureInfo.InvariantCulture) +
            " greenish=" + greenish.ToString(CultureInfo.InvariantCulture) +
            " bluish=" + bluish.ToString(CultureInfo.InvariantCulture));

        await Assert.That(reddish).IsGreaterThan(0);
        await Assert.That(greenish).IsGreaterThan(0);
    }

    [Test]
    public async Task MaterialXStandardSurfaceMatchesPreviewSelfConsistencyOnVulkan()
    {
        ParityImage image;
        try
        {
            image = CaptureSyntheticMaterialXSelfConsistency();
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("MaterialX Vulkan self-consistency", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
        Console.WriteLine(
            "materialx-self-consistency maxChannelDelta=" +
            maxChannelDelta.ToString(CultureInfo.InvariantCulture) +
            " meanChannelDelta=" + meanChannelDelta.ToString("F3", CultureInfo.InvariantCulture));
        WriteEvidence(
            "materialx-self-consistency.txt",
            [
                "materialx-self-consistency maxChannelDelta=" +
                maxChannelDelta.ToString(CultureInfo.InvariantCulture) +
                " meanChannelDelta=" + meanChannelDelta.ToString("F3", CultureInfo.InvariantCulture),
            ]);

        await Assert.That(maxChannelDelta).IsLessThanOrEqualTo(MaximumShadedChannelDelta);
        await Assert.That(meanChannelDelta).IsLessThanOrEqualTo(MaximumShadedMeanChannelDelta);
    }

    [Test]
    public async Task MaterialXGeneratedUnlitMatchesPreviewSelfConsistencyOnVulkan()
    {
        ParityImage image;
        try
        {
            image = CaptureGeneratedMaterialXSelfConsistency(VulkanSilkGraphicsDevice.Create);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("MaterialX generated Vulkan self-consistency", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
        Console.WriteLine(
            "materialx-generated-self-consistency maxChannelDelta=" +
            maxChannelDelta.ToString(CultureInfo.InvariantCulture) +
            " meanChannelDelta=" + meanChannelDelta.ToString("F3", CultureInfo.InvariantCulture));
        const string pairDescription =
            "generated side: ND_constant_color3 -> ND_multiply_color3FA -> ND_surface_unlit " +
            "compiled by VkShaderGenerator/glslang and selected as generated SPIR-V; " +
            "reference side: checked UsdPreviewSurface shader with only emissiveColor set. " +
            "Breaking generation, cache keying, or generated shader selection changes only the generated half.";
        WriteEvidence(
            "materialx-generated-self-consistency.txt",
            [
                "materialx-generated-self-consistency maxChannelDelta=" +
                maxChannelDelta.ToString(CultureInfo.InvariantCulture) +
                " meanChannelDelta=" + meanChannelDelta.ToString("F3", CultureInfo.InvariantCulture),
                pairDescription,
            ]);

        if (maxChannelDelta > MaximumShadedChannelDelta ||
            meanChannelDelta > MaximumShadedMeanChannelDelta)
        {
            WriteCapture("materialx-generated-self-consistency", "mismatch", image);
        }

        await Assert.That(maxChannelDelta).IsLessThanOrEqualTo(MaximumShadedChannelDelta);
        await Assert.That(meanChannelDelta).IsLessThanOrEqualTo(MaximumShadedMeanChannelDelta);
    }

    [Test]
    public async Task MaterialXGeneratedUnlitMatchesPreviewSelfConsistencyOnMetal()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        ParityImage image;
        try
        {
            image = CaptureGeneratedMaterialXSelfConsistency(MetalSilkGraphicsDevice.Create);
        }
        catch (Exception exception) when (exception is
            DllNotFoundException or DirectoryNotFoundException or PlatformNotSupportedException)
        {
            SkipOrFail("MaterialX generated Metal self-consistency", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
        Console.WriteLine(
            "materialx-generated-metal-self-consistency maxChannelDelta=" +
            maxChannelDelta.ToString(CultureInfo.InvariantCulture) +
            " meanChannelDelta=" + meanChannelDelta.ToString("F3", CultureInfo.InvariantCulture));

        await Assert.That(maxChannelDelta).IsLessThanOrEqualTo(MaximumShadedChannelDelta);
        await Assert.That(meanChannelDelta).IsLessThanOrEqualTo(MaximumShadedMeanChannelDelta);
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public async Task MaterialXGeneratedUnlitMatchesPreviewSelfConsistencyOnD3D12()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        ParityImage image;
        try
        {
            image = CaptureGeneratedMaterialXSelfConsistency(
                static () => D3D12SilkGraphicsDevice.Create(useWarp: true));
        }
        catch (Exception exception) when (exception is
            DllNotFoundException or DirectoryNotFoundException or PlatformNotSupportedException)
        {
            SkipOrFail("MaterialX generated D3D12 self-consistency", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
        Console.WriteLine(
            "materialx-generated-d3d12-self-consistency maxChannelDelta=" +
            maxChannelDelta.ToString(CultureInfo.InvariantCulture) +
            " meanChannelDelta=" + meanChannelDelta.ToString("F3", CultureInfo.InvariantCulture));
        const string pairDescription =
            "generated side: ND_constant_color3 -> ND_multiply_color3FA -> ND_surface_unlit " +
            "compiled by VkShaderGenerator/glslang to SPIR-V, translated by SPIRV-Cross to HLSL, " +
            "compiled by DXC to DXIL, and selected as generated D3D12 shader code; " +
            "reference side: checked UsdPreviewSurface shader with only emissiveColor set. " +
            "Breaking generation, SPIRV-Cross translation, DXC, cache keying, or generated shader " +
            "selection changes only the generated half.";
        WriteEvidence(
            "materialx-generated-d3d12-self-consistency.txt",
            [
                "materialx-generated-d3d12-self-consistency maxChannelDelta=" +
                maxChannelDelta.ToString(CultureInfo.InvariantCulture) +
                " meanChannelDelta=" + meanChannelDelta.ToString("F3", CultureInfo.InvariantCulture),
                pairDescription,
            ]);
        if (maxChannelDelta > MaximumShadedChannelDelta ||
            meanChannelDelta > MaximumShadedMeanChannelDelta)
        {
            WriteSelfConsistencyMismatchEvidence(
                "materialx-generated-d3d12-self-consistency",
                image,
                pairDescription);
        }

        await Assert.That(maxChannelDelta).IsLessThanOrEqualTo(MaximumShadedChannelDelta);
        await Assert.That(meanChannelDelta).IsLessThanOrEqualTo(MaximumShadedMeanChannelDelta);
    }

    [Test]
    public async Task TextureWrapModesMatchRepeatWithinUnitUvRangeOnVulkan()
    {
        var cases = new[]
        {
            new SelfConsistencyCase("texture-wrap-clamp", SilkTextureWrap.Clamp),
            new SelfConsistencyCase("texture-wrap-mirror", SilkTextureWrap.Mirror),
            new SelfConsistencyCase("texture-wrap-use-metadata", SilkTextureWrap.Black),
        };

        var evidence = new List<string>();
        foreach (SelfConsistencyCase testCase in cases)
        {
            ParityImage image;
            try
            {
                image = CaptureSyntheticTextureWrapSelfConsistency(testCase.TextureWrap);
            }
            catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
            {
                SkipOrFail(testCase.Name + " Vulkan self-consistency", exception.ToString());
                throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
            }

            (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
            string line = FormatSelfConsistencyMetrics(testCase.Name, maxChannelDelta, meanChannelDelta);
            Console.WriteLine(line);
            evidence.Add(line);
            await Assert.That(maxChannelDelta).IsLessThanOrEqualTo((byte)3);
            await Assert.That(meanChannelDelta).IsLessThanOrEqualTo(0.500);
        }

        WriteEvidence("texture-wrap-self-consistency.txt", evidence);
    }

    [Test]
    public async Task TextureWrapModesDivergeOutsideUnitUvRangeOnVulkan()
    {
        var cases = new[]
        {
            new SelfConsistencyCase("texture-wrap-clamp", SilkTextureWrap.Clamp),
            new SelfConsistencyCase("texture-wrap-mirror", SilkTextureWrap.Mirror),
            new SelfConsistencyCase("texture-wrap-use-metadata", SilkTextureWrap.Black),
        };
        var evidence = new List<string>();
        foreach (SelfConsistencyCase testCase in cases)
        {
            ParityImage image;
            try
            {
                image = CaptureSyntheticTextureWrapSelfConsistency(
                    testCase.TextureWrap,
                    OutsideUnitTextureCoordinates());
            }
            catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
            {
                SkipOrFail(testCase.Name + " Vulkan divergence", exception.ToString());
                throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
            }

            (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
            string line = FormatSelfConsistencyMetrics(
                testCase.Name + "-divergence",
                maxChannelDelta,
                meanChannelDelta);
            Console.WriteLine(line);
            evidence.Add(line);
            await Assert.That(maxChannelDelta).IsGreaterThan(MinimumTextureDivergenceChannelDelta);
        }

        WriteEvidence("texture-wrap-divergence.txt", evidence);
    }

    [Test]
    public async Task TextureAutoColorSpaceMatchesSrgbOnDiffuseTextureOnVulkan()
    {
        ParityImage image;
        try
        {
            image = CaptureSyntheticTextureColorSpaceSelfConsistency();
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("texture-colorspace-auto Vulkan self-consistency", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
        string line = FormatSelfConsistencyMetrics(
            "texture-colorspace-auto",
            maxChannelDelta,
            meanChannelDelta);
        Console.WriteLine(line);
        WriteEvidence("texture-colorspace-auto-self-consistency.txt", [line]);
        await Assert.That(maxChannelDelta).IsLessThanOrEqualTo((byte)3);
        await Assert.That(meanChannelDelta).IsLessThanOrEqualTo(0.500);
    }

    [Test]
    public async Task TextureRawColorSpaceDivergesFromSrgbOnDiffuseTextureOnVulkan()
    {
        ParityImage image;
        try
        {
            image = CaptureSyntheticTextureRawColorSpaceDivergence();
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("texture-colorspace-raw Vulkan divergence", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
        string line = FormatSelfConsistencyMetrics(
            "texture-colorspace-raw-divergence",
            maxChannelDelta,
            meanChannelDelta);
        Console.WriteLine(line);
        WriteEvidence("texture-colorspace-raw-divergence.txt", [line]);
        await Assert.That(maxChannelDelta).IsGreaterThan(MinimumColorSpaceDivergenceChannelDelta);
    }

    [Test]
    public async Task TextureScaleBiasFallbackMatchesEquivalentConstantOnVulkan()
    {
        ParityImage image;
        try
        {
            image = CaptureSyntheticTextureScaleBiasFallbackSelfConsistency();
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("texture-scale-bias-fallback Vulkan self-consistency", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
        string line = FormatSelfConsistencyMetrics(
            "texture-scale-bias-fallback",
            maxChannelDelta,
            meanChannelDelta);
        Console.WriteLine(line);
        WriteEvidence("texture-scale-bias-fallback-self-consistency.txt", [line]);
        await Assert.That(maxChannelDelta).IsLessThanOrEqualTo((byte)3);
        await Assert.That(meanChannelDelta).IsLessThanOrEqualTo(0.500);
    }

    [Test]
    public async Task TextureScaleBiasFallbackDivergesWhenBiasIsRemovedOnVulkan()
    {
        ParityImage image;
        try
        {
            image = CaptureSyntheticTextureScaleBiasFallbackDivergence();
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("texture-scale-bias-fallback Vulkan divergence", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
        string line = FormatSelfConsistencyMetrics(
            "texture-scale-bias-fallback-divergence",
            maxChannelDelta,
            meanChannelDelta);
        Console.WriteLine(line);
        WriteEvidence("texture-scale-bias-fallback-divergence.txt", [line]);
        await Assert.That(maxChannelDelta).IsGreaterThan(MinimumTextureDivergenceChannelDelta);
    }

    [Test]
    public async Task NonDiffuseTextureSlotsMatchNeutralInputsOnVulkan()
    {
        var evidence = new List<string>();
        foreach (MaterialTextureSlotCase testCase in CreateMaterialTextureSlotCases())
        {
            ParityImage image;
            try
            {
                image = CaptureSyntheticMaterialTextureSlotSelfConsistency(testCase, testCase.NeutralFallback);
            }
            catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
            {
                SkipOrFail(testCase.Name + " Vulkan self-consistency", exception.ToString());
                throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
            }

            (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
            string line = FormatSelfConsistencyMetrics(testCase.Name, maxChannelDelta, meanChannelDelta);
            Console.WriteLine(line);
            evidence.Add(line);
            await Assert.That(maxChannelDelta).IsLessThanOrEqualTo(MaximumShadedChannelDelta);
            await Assert.That(meanChannelDelta).IsLessThanOrEqualTo(2.000);
        }

        WriteEvidence("material-texture-slot-self-consistency.txt", evidence);
    }

    [Test]
    public async Task NonDiffuseTextureSlotsDivergeFromNeutralInputsOnVulkan()
    {
        var evidence = new List<string>();
        foreach (MaterialTextureSlotCase testCase in CreateMaterialTextureSlotCases())
        {
            ParityImage image;
            try
            {
                image = CaptureSyntheticMaterialTextureSlotSelfConsistency(testCase, testCase.DivergentFallback);
            }
            catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
            {
                SkipOrFail(testCase.Name + " Vulkan divergence", exception.ToString());
                throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
            }

            (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
            string line = FormatSelfConsistencyMetrics(
                testCase.Name + "-divergence",
                maxChannelDelta,
                meanChannelDelta);
            Console.WriteLine(line);
            evidence.Add(line);
            await Assert.That(maxChannelDelta).IsGreaterThan(MinimumMaterialTextureDivergenceChannelDelta);
        }

        WriteEvidence("material-texture-slot-divergence.txt", evidence);
    }

    [Test]
    public async Task RemainingPreviewSurfaceConstantInputsMatchEquivalentMaterialsOnVulkan()
    {
        var evidence = new List<string>();
        foreach (ConstantMaterialInputCase testCase in CreateConstantMaterialInputCases())
        {
            ParityImage image;
            try
            {
                image = CaptureSyntheticConstantMaterialInputSelfConsistency(testCase, diverge: false);
            }
            catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
            {
                SkipOrFail(testCase.Name + " Vulkan self-consistency", exception.ToString());
                throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
            }

            (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
            string line = FormatSelfConsistencyMetrics(testCase.Name, maxChannelDelta, meanChannelDelta);
            Console.WriteLine(line);
            evidence.Add(line);
            await Assert.That(maxChannelDelta).IsLessThanOrEqualTo(MaximumShadedChannelDelta);
            await Assert.That(meanChannelDelta).IsLessThanOrEqualTo(2.000);
        }

        WriteEvidence("preview-surface-constant-input-self-consistency.txt", evidence);
    }

    [Test]
    public async Task RemainingPreviewSurfaceConstantInputsDivergeFromEquivalentMaterialsOnVulkan()
    {
        var evidence = new List<string>();
        foreach (ConstantMaterialInputCase testCase in CreateConstantMaterialInputCases())
        {
            ParityImage image;
            try
            {
                image = CaptureSyntheticConstantMaterialInputSelfConsistency(testCase, diverge: true);
            }
            catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
            {
                SkipOrFail(testCase.Name + " Vulkan divergence", exception.ToString());
                throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
            }

            (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
            string line = FormatSelfConsistencyMetrics(
                testCase.Name + "-divergence",
                maxChannelDelta,
                meanChannelDelta);
            Console.WriteLine(line);
            evidence.Add(line);
            await Assert.That(maxChannelDelta).IsGreaterThan(MinimumMaterialTextureDivergenceChannelDelta);
        }

        WriteEvidence("preview-surface-constant-input-divergence.txt", evidence);
    }

    [Test]
    public async Task CullStyleBackMatchesBackUnlessDoubleSidedForSingleSidedMeshOnVulkan()
    {
        ParityImage image;
        try
        {
            image = CaptureSyntheticCullStyleSelfConsistency(SilkMeshCullStyle.Back);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("cull-style-back Vulkan self-consistency", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
        string line = FormatSelfConsistencyMetrics("cull-style-back", maxChannelDelta, meanChannelDelta);
        Console.WriteLine(line);
        WriteEvidence("cull-style-back-self-consistency.txt", [line]);
        await Assert.That(maxChannelDelta).IsLessThanOrEqualTo((byte)3);
        await Assert.That(meanChannelDelta).IsLessThanOrEqualTo(0.500);
    }

    [Test]
    public async Task CullStyleBackDivergesFromBackUnlessDoubleSidedForDoubleSidedBackFacesOnVulkan()
    {
        ParityImage image;
        try
        {
            image = CaptureSyntheticCullStyleSelfConsistency(
                SilkMeshCullStyle.Back,
                doubleSided: true,
                backFacing: true);
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("cull-style-back Vulkan divergence", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
        string line = FormatSelfConsistencyMetrics("cull-style-back-divergence", maxChannelDelta, meanChannelDelta);
        Console.WriteLine(line);
        WriteEvidence("cull-style-back-divergence.txt", [line]);
        await Assert.That(maxChannelDelta).IsGreaterThan(MinimumCullDivergenceChannelDelta);
    }

    [Test]
    public async Task RectLightZeroAreaMatchesSphereLightOnVulkan()
    {
        await AssertLightSelfConsistency(
            "light-rect-zero-area",
            CaptureSyntheticAreaLightSelfConsistency(AreaLightGate.RectEquivalent),
            expectDivergence: false);
    }

    [Test]
    public async Task RectLightFullAreaDivergesFromSphereLightOnVulkan()
    {
        await AssertLightSelfConsistency(
            "light-rect-full-area",
            CaptureSyntheticAreaLightSelfConsistency(AreaLightGate.RectDivergence),
            expectDivergence: true);
    }

    [Test]
    public async Task DiskLightEdgeOnMatchesUnlitSceneOnVulkan()
    {
        await AssertLightSelfConsistency(
            "light-disk-edge-on",
            CaptureSyntheticAreaLightSelfConsistency(AreaLightGate.DiskEquivalent),
            expectDivergence: false);
    }

    [Test]
    public async Task DiskLightFaceOnDivergesFromEdgeOnLightOnVulkan()
    {
        await AssertLightSelfConsistency(
            "light-disk-face-on",
            CaptureSyntheticAreaLightSelfConsistency(AreaLightGate.DiskDivergence),
            expectDivergence: true);
    }

    [Test]
    public async Task CylinderLightZeroLengthMatchesSphereLightOnVulkan()
    {
        await AssertLightSelfConsistency(
            "light-cylinder-zero-length",
            CaptureSyntheticAreaLightSelfConsistency(AreaLightGate.CylinderEquivalent),
            expectDivergence: false);
    }

    [Test]
    public async Task CylinderLightFullLengthDivergesFromSphereLightOnVulkan()
    {
        await AssertLightSelfConsistency(
            "light-cylinder-full-length",
            CaptureSyntheticAreaLightSelfConsistency(AreaLightGate.CylinderDivergence),
            expectDivergence: true);
    }

    private static async Task AssertLightSelfConsistency(
        string name,
        ParityImage image,
        bool expectDivergence)
    {
        (byte maxChannelDelta, double meanChannelDelta) = CompareTranslatedHalves(image);
        string line = FormatSelfConsistencyMetrics(
            expectDivergence ? name + "-divergence" : name,
            maxChannelDelta,
            meanChannelDelta);
        Console.WriteLine(line);
        WriteEvidence(name + (expectDivergence ? "-divergence.txt" : "-self-consistency.txt"), [line]);
        if (expectDivergence)
        {
            await Assert.That(maxChannelDelta).IsGreaterThan(MinimumLightDivergenceChannelDelta);
            return;
        }

        await Assert.That(maxChannelDelta).IsLessThanOrEqualTo(MaximumShadedChannelDelta);
        await Assert.That(meanChannelDelta).IsLessThanOrEqualTo(2.000);
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

    private static void WriteSelfConsistencyMismatchEvidence(
        string name,
        ParityImage image,
        string pairDescription)
    {
        TranslatedHalfDelta delta = CompareTranslatedHalvesDetailed(image);
        ParityImage left = ExtractTranslatedHalf(image, left: true);
        ParityImage right = ExtractTranslatedHalf(image, left: false);
        WriteCapture(name, "mismatch", image);
        WriteCapture(name, "generated-left", left);
        WriteCapture(name, "preview-right", right);
        WriteEvidence(
            name + "-mismatch.txt",
            [
                FormatTranslatedHalfDelta(name, delta),
                "wholeSha256=" + Hash(image),
                "generatedLeftSha256=" + Hash(left),
                "previewRightSha256=" + Hash(right),
                pairDescription,
            ]);
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
            if (Environment.GetEnvironmentVariable("OPENUSD_PARITY_FORCE_CONTEXT_UNAVAILABLE") is "1" or "true")
            {
                SkipOrFail(
                    "platform OpenGL parity capture",
                    "OPENUSD_PARITY_FORCE_CONTEXT_UNAVAILABLE forced context creation to fail.");
                throw new InvalidOperationException("SkipOrFail returned unexpectedly.");
            }

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
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
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
        Skip.Test($"Skipping {reason}: {detail}");
        throw new InvalidOperationException("Skip.Test returned unexpectedly.");
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
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
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

    private sealed record SelfConsistencyCase(string Name, SilkTextureWrap TextureWrap);

    private sealed record MaterialTextureSlotCase(
        string Name,
        SilkMaterialParameter Parameter,
        SilkColorSpace ColorSpace,
        float[] NeutralFallback,
        float[] DivergentFallback,
        MaterialScalarSpec[] LeftScalars,
        MaterialScalarSpec[] RightScalars);

    private sealed record ConstantMaterialInputCase(
        string Name,
        MaterialScalarSpec[] ReferenceScalars,
        MaterialScalarSpec[] EquivalentScalars,
        MaterialScalarSpec[] DivergentScalars);

    private readonly record struct MaterialScalarSpec(SilkMaterialParameter Parameter, float[] Values);

    private readonly record struct MeshPageStats(
        int PointListPointCount,
        int PointListIndexCount,
        int LineListPrimitiveCount);

    private readonly record struct TranslatedHalfDelta(
        byte MaxChannelDelta,
        double MeanChannelDelta,
        int MaxX,
        int MaxY,
        int MaxChannel,
        int MaxLeftChannelValue,
        int MaxRightChannelValue,
        double LeftMeanRed,
        double LeftMeanGreen,
        double LeftMeanBlue,
        double RightMeanRed,
        double RightMeanGreen,
        double RightMeanBlue);

    private enum AreaLightGate
    {
        RectEquivalent,
        RectDivergence,
        DiskEquivalent,
        DiskDivergence,
        CylinderEquivalent,
        CylinderDivergence,
    }

    private static string FormatSelfConsistencyMetrics(
        string name,
        byte maxChannelDelta,
        double meanChannelDelta) =>
        name + "-self-consistency maxChannelDelta=" +
        maxChannelDelta.ToString(CultureInfo.InvariantCulture) +
        " meanChannelDelta=" + meanChannelDelta.ToString("F3", CultureInfo.InvariantCulture);

    private static ParityImage CaptureSyntheticTextureWrapSelfConsistency(
        SilkTextureWrap candidateWrap,
        ReadOnlySpan<float> textureCoordinates = default) =>
        CaptureSyntheticMaterialPair(
            CreateTexturedMaterialCommand(
                "/Repeat",
                TextureAssetPath(),
                SilkMaterialParameter.DiffuseColor,
                SilkTextureWrap.Repeat,
                SilkColorSpace.Srgb,
                [1, 1, 1, 1],
                [0, 0, 0, 0],
                [1, 0, 1, 1],
                "st"),
            CreateTexturedMaterialCommand(
                "/Candidate",
                TextureAssetPath(),
                SilkMaterialParameter.DiffuseColor,
                candidateWrap,
                SilkColorSpace.Srgb,
                [1, 1, 1, 1],
                [0, 0, 0, 0],
                [1, 0, 1, 1],
                "st"),
            textureCoordinates);

    private static ParityImage CaptureSyntheticTextureColorSpaceSelfConsistency() =>
        CaptureSyntheticMaterialPair(
            CreateTexturedMaterialCommand(
                "/Repeat",
                TextureAssetPath(),
                SilkMaterialParameter.DiffuseColor,
                SilkTextureWrap.Repeat,
                SilkColorSpace.Srgb,
                [1, 1, 1, 1],
                [0, 0, 0, 0],
                [1, 0, 1, 1],
                "st"),
            CreateTexturedMaterialCommand(
                "/Candidate",
                TextureAssetPath(),
                SilkMaterialParameter.DiffuseColor,
                SilkTextureWrap.Repeat,
                SilkColorSpace.Auto,
                [1, 1, 1, 1],
                [0, 0, 0, 0],
                [1, 0, 1, 1],
                "st"));

    private static ParityImage CaptureSyntheticTextureRawColorSpaceDivergence() =>
        CaptureSyntheticMaterialPair(
            CreateTexturedMaterialCommand(
                "/Repeat",
                TextureAssetPath(),
                SilkMaterialParameter.DiffuseColor,
                SilkTextureWrap.Repeat,
                SilkColorSpace.Srgb,
                [1, 1, 1, 1],
                [0, 0, 0, 0],
                [1, 0, 1, 1],
                "st"),
            CreateTexturedMaterialCommand(
                "/Candidate",
                TextureAssetPath(),
                SilkMaterialParameter.DiffuseColor,
                SilkTextureWrap.Repeat,
                SilkColorSpace.Raw,
                [1, 1, 1, 1],
                [0, 0, 0, 0],
                [1, 0, 1, 1],
                "st"));

    private static ParityImage CaptureSyntheticTextureScaleBiasFallbackSelfConsistency()
    {
        float[] target = [77f / 255f, 90f / 255f, 102f / 255f];
        float[] source = [0.40f, 0.50f, 0.60f, 1];
        float[] scale = [0.50f, 0.50f, 0.50f, 1];
        float[] bias =
        [
            target[0] - (source[0] * scale[0]),
            target[1] - (source[1] * scale[1]),
            target[2] - (source[2] * scale[2]),
            0,
        ];
        return CaptureSyntheticMaterialPair(
            CreateMaterialCommand("/Repeat", SilkSurfaceKind.PreviewSurface, target),
            CreateTexturedMaterialCommand(
                "/Candidate",
                Path.Combine(FindRepositoryRoot() ?? AppContext.BaseDirectory, "test-assets", "parity", "missing.png"),
                SilkMaterialParameter.DiffuseColor,
                SilkTextureWrap.Repeat,
                SilkColorSpace.Srgb,
                scale,
                bias,
                source,
                "st"));
    }

    private static ParityImage CaptureSyntheticTextureScaleBiasFallbackDivergence()
    {
        float[] target = [77f / 255f, 90f / 255f, 102f / 255f];
        float[] source = [0.40f, 0.50f, 0.60f, 1];
        return CaptureSyntheticMaterialPair(
            CreateMaterialCommand("/Repeat", SilkSurfaceKind.PreviewSurface, target),
            CreateTexturedMaterialCommand(
                "/Candidate",
                Path.Combine(FindRepositoryRoot() ?? AppContext.BaseDirectory, "test-assets", "parity", "missing.png"),
                SilkMaterialParameter.DiffuseColor,
                SilkTextureWrap.Repeat,
                SilkColorSpace.Srgb,
                [0.50f, 0.50f, 0.50f, 1],
                [0, 0, 0, 0],
                source,
                "st"));
    }

    private static MaterialTextureSlotCase[] CreateMaterialTextureSlotCases()
    {
        MaterialScalarSpec[] shadedDefaults =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.62f, 0.38f, 0.14f]),
            new(SilkMaterialParameter.Roughness, [0.62f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
            new(SilkMaterialParameter.SpecularColor, [0.45f, 0.45f, 0.45f]),
            new(SilkMaterialParameter.UseSpecularWorkflow, [1.0f]),
        ];
        MaterialScalarSpec[] texturedMetallic =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.62f, 0.38f, 0.14f]),
            new(SilkMaterialParameter.Roughness, [0.62f]),
            new(SilkMaterialParameter.Metallic, [1.0f]),
        ];
        MaterialScalarSpec[] untexturedMetallic =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.62f, 0.38f, 0.14f]),
            new(SilkMaterialParameter.Roughness, [0.62f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] roughnessFocused =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.08f, 0.08f, 0.08f]),
            new(SilkMaterialParameter.Roughness, [0.92f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
            new(SilkMaterialParameter.SpecularColor, [1.0f, 1.0f, 1.0f]),
            new(SilkMaterialParameter.UseSpecularWorkflow, [1.0f]),
        ];
        return
        [
            new(
                "texture-slot-emissive",
                SilkMaterialParameter.EmissiveColor,
                SilkColorSpace.Raw,
                [0, 0, 0, 1],
                [0.28f, 0.10f, 0.02f, 1],
                shadedDefaults,
                shadedDefaults),
            new(
                "texture-slot-roughness",
                SilkMaterialParameter.Roughness,
                SilkColorSpace.Raw,
                [1, 1, 1, 1],
                [1, 0.02f, 1, 1],
                roughnessFocused,
                roughnessFocused),
            new(
                "texture-slot-metallic",
                SilkMaterialParameter.Metallic,
                SilkColorSpace.Raw,
                [1, 1, 0, 1],
                [1, 1, 1, 1],
                untexturedMetallic,
                texturedMetallic),
            new(
                "texture-slot-normal",
                SilkMaterialParameter.Normal,
                SilkColorSpace.Raw,
                [0.5f, 0.5f, 1, 1],
                [1, 0.5f, 0.5f, 1],
                shadedDefaults,
                shadedDefaults),
        ];
    }

    private static ConstantMaterialInputCase[] CreateConstantMaterialInputCases()
    {
        MaterialScalarSpec[] matte =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.64f, 0.36f, 0.12f]),
            new(SilkMaterialParameter.Roughness, [0.76f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] black =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.Roughness, [0.76f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] emissiveOnly =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.EmissiveColor, [0.18f, 0.08f, 0.02f]),
            new(SilkMaterialParameter.Roughness, [0.76f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] occludedEmissive =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.64f, 0.36f, 0.12f]),
            new(SilkMaterialParameter.EmissiveColor, [0.18f, 0.08f, 0.02f]),
            new(SilkMaterialParameter.Occlusion, [0.0f]),
            new(SilkMaterialParameter.Roughness, [0.76f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] thresholdDiscard =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.64f, 0.36f, 0.12f]),
            new(SilkMaterialParameter.Opacity, [0.25f]),
            new(SilkMaterialParameter.OpacityThreshold, [0.50f]),
            new(SilkMaterialParameter.Roughness, [0.76f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] thresholdVisible =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.64f, 0.36f, 0.12f]),
            new(SilkMaterialParameter.Opacity, [0.75f]),
            new(SilkMaterialParameter.OpacityThreshold, [0.50f]),
            new(SilkMaterialParameter.Roughness, [0.76f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] clearcoatSpecular =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.SpecularColor, [0.60493827f, 0.60493827f, 0.60493827f]),
            new(SilkMaterialParameter.UseSpecularWorkflow, [1.0f]),
            new(SilkMaterialParameter.Roughness, [0.65f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] clearcoatEquivalent =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.SpecularColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.UseSpecularWorkflow, [1.0f]),
            new(SilkMaterialParameter.Ior, [8.0f]),
            new(SilkMaterialParameter.Clearcoat, [1.0f]),
            new(SilkMaterialParameter.ClearcoatRoughness, [0.65f]),
            new(SilkMaterialParameter.Roughness, [1.0f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] clearcoatIgnored =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.SpecularColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.UseSpecularWorkflow, [1.0f]),
            new(SilkMaterialParameter.Ior, [8.0f]),
            new(SilkMaterialParameter.Clearcoat, [0.0f]),
            new(SilkMaterialParameter.ClearcoatRoughness, [0.65f]),
            new(SilkMaterialParameter.Roughness, [1.0f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] roughClearcoatSpecular =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.SpecularColor, [0.60493827f, 0.60493827f, 0.60493827f]),
            new(SilkMaterialParameter.UseSpecularWorkflow, [1.0f]),
            new(SilkMaterialParameter.Roughness, [0.82f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] roughClearcoatEquivalent =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.SpecularColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.UseSpecularWorkflow, [1.0f]),
            new(SilkMaterialParameter.Ior, [8.0f]),
            new(SilkMaterialParameter.Clearcoat, [1.0f]),
            new(SilkMaterialParameter.ClearcoatRoughness, [0.82f]),
            new(SilkMaterialParameter.Roughness, [1.0f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] smoothClearcoatDivergent =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.SpecularColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.UseSpecularWorkflow, [1.0f]),
            new(SilkMaterialParameter.Ior, [8.0f]),
            new(SilkMaterialParameter.Clearcoat, [1.0f]),
            new(SilkMaterialParameter.ClearcoatRoughness, [0.08f]),
            new(SilkMaterialParameter.Roughness, [1.0f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] highIorSpecular =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.SpecularColor, [0.60493827f, 0.60493827f, 0.60493827f]),
            new(SilkMaterialParameter.UseSpecularWorkflow, [1.0f]),
            new(SilkMaterialParameter.Roughness, [0.65f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] highIorEquivalent =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.Ior, [8.0f]),
            new(SilkMaterialParameter.Roughness, [0.65f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        MaterialScalarSpec[] defaultIorDivergent =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.0f, 0.0f, 0.0f]),
            new(SilkMaterialParameter.Ior, [1.5f]),
            new(SilkMaterialParameter.Roughness, [0.65f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        return
        [
            new("preview-constant-occlusion", emissiveOnly, occludedEmissive, AddScalars(occludedEmissive,
                new MaterialScalarSpec(SilkMaterialParameter.Occlusion, [1.0f]))),
            new("preview-constant-opacity-threshold", black, thresholdDiscard, thresholdVisible),
            new("preview-constant-emissive", matte, AddScalars(matte,
                new MaterialScalarSpec(SilkMaterialParameter.EmissiveColor, [0.0f, 0.0f, 0.0f])), AddScalars(matte,
                new MaterialScalarSpec(SilkMaterialParameter.EmissiveColor, [0.24f, 0.08f, 0.02f]))),
            new("preview-constant-clearcoat", clearcoatSpecular, clearcoatEquivalent, clearcoatIgnored),
            new(
                "preview-constant-clearcoat-roughness",
                roughClearcoatSpecular,
                roughClearcoatEquivalent,
                smoothClearcoatDivergent),
            new("preview-constant-ior", highIorSpecular, highIorEquivalent, defaultIorDivergent),
        ];
    }

    private static MaterialScalarSpec[] AddScalars(
        ReadOnlySpan<MaterialScalarSpec> scalars,
        params MaterialScalarSpec[] additions)
    {
        var values = scalars.ToArray();
        foreach (MaterialScalarSpec addition in additions)
        {
            int index = Array.FindIndex(values, scalar => scalar.Parameter == addition.Parameter);
            if (index >= 0)
            {
                values[index] = addition;
                continue;
            }

            Array.Resize(ref values, values.Length + 1);
            values[^1] = addition;
        }

        return values;
    }

    private static ParityImage CaptureSyntheticMaterialTextureSlotSelfConsistency(
        MaterialTextureSlotCase testCase,
        float[] fallback) =>
        CaptureSyntheticMaterialPair(
            CreateMaterialCommandWithScalars("/Repeat", SilkSurfaceKind.PreviewSurface, testCase.LeftScalars),
            CreateTexturedMaterialCommand(
                "/Candidate",
                MissingTextureAssetPath(),
                testCase.Parameter,
                SilkTextureWrap.Repeat,
                testCase.ColorSpace,
                [1, 1, 1, 1],
                [0, 0, 0, 0],
                fallback,
                "st",
                testCase.RightScalars));

    private static ParityImage CaptureSyntheticConstantMaterialInputSelfConsistency(
        ConstantMaterialInputCase testCase,
        bool diverge) =>
        CaptureSyntheticMaterialPair(
            CreateMaterialCommandWithScalars(
                "/Repeat",
                SilkSurfaceKind.PreviewSurface,
                testCase.ReferenceScalars),
            CreateMaterialCommandWithScalars(
                "/Candidate",
                SilkSurfaceKind.PreviewSurface,
                diverge ? testCase.DivergentScalars : testCase.EquivalentScalars),
            requireImagePlugins: false);

    private static ParityImage CaptureSyntheticCullStyleSelfConsistency(
        SilkMeshCullStyle candidateCullStyle,
        bool doubleSided = false,
        bool backFacing = false)
    {
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
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            SilkMeshRendererConformance.CreateFrameCommand(
                checked((uint)Width),
                checked((uint)Height),
                IdentityMatrix()),
            CreateDisplayMeshCommand(
                1,
                "/BackUnlessDoubleSided",
                -0.5f,
                SilkMeshCullStyle.BackUnlessDoubleSided,
                doubleSided,
                backFacing),
            CreateDisplayMeshCommand(2, "/Back", 0.5f, candidateCullStyle, doubleSided, backFacing));
        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        _ = renderer.Render(color, depth, options);
        _ = renderer.Render(color, depth, options);
        byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(pixels);
        return new ParityImage(Width, Height, pixels);
    }

    private static ParityImage CaptureSyntheticAreaLightSelfConsistency(AreaLightGate gate)
    {
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
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            CreateAreaLightFrameCommand(gate),
            CreateDisplayMeshCommand(
                1,
                "/LeftLightMesh",
                -0.5f,
                SilkMeshCullStyle.BackUnlessDoubleSided,
                doubleSided: true),
            CreateDisplayMeshCommand(
                2,
                "/RightLightMesh",
                0.5f,
                SilkMeshCullStyle.BackUnlessDoubleSided,
                doubleSided: true));
        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        _ = renderer.Render(color, depth, options);
        _ = renderer.Render(color, depth, options);
        byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(pixels);
        return new ParityImage(Width, Height, pixels);
    }

    private static ParityImage CaptureSyntheticMaterialPair(
        byte[] leftMaterial,
        byte[] rightMaterial,
        ReadOnlySpan<float> textureCoordinates = default,
        bool requireImagePlugins = true)
    {
        if (requireImagePlugins)
        {
            _ = ImagePluginsRegistered.Value;
        }

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
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            SilkMeshRendererConformance.CreateFrameCommand(
                checked((uint)Width),
                checked((uint)Height),
                IdentityMatrix()),
            leftMaterial,
            rightMaterial,
            CreateTexturedMeshCommand(1, "/LeftMesh", "/Repeat", -0.5f, textureCoordinates),
            CreateTexturedMeshCommand(2, "/RightMesh", "/Candidate", 0.5f, textureCoordinates));
        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        _ = renderer.Render(color, depth, options);
        _ = renderer.Render(color, depth, options);
        byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(pixels);
        return new ParityImage(Width, Height, pixels);
    }

    private static string TextureAssetPath() =>
        Path.Combine(
            FindRepositoryRoot() ?? AppContext.BaseDirectory,
            "test-assets",
            "parity",
            "parity-texture-asymmetric.png");

    private static string MissingTextureAssetPath() =>
        Path.Combine(FindRepositoryRoot() ?? AppContext.BaseDirectory, "test-assets", "parity", "missing.png");

    private static string ResolveParityAsset(string fileName)
    {
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
        return Path.Combine(root, "test-assets", "parity", fileName);
    }

    private static string ResolveTestAsset(string fileName)
    {
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
        return Path.Combine(root, "test-assets", fileName);
    }

    private static OutputTransformSaturation CaptureChaseOutputTransform(
        ISilkGraphicsDevice device)
    {
        const int width = 320;
        const int height = 180;
        PrependHdSilkNativeSearchPath();
        string stagePath = ResolveTestAsset("mcp-monkey-car-city.usda");
        string pluginPath = ResolvePluginPath();
        using UsdStage stage = UsdStage.Open(stagePath);
        CameraState camera = CameraState.FromStageCamera(
            stage,
            "/World/MonkeyChaseCamera",
            24,
            width,
            height);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
        using OpenUsdSilkPage page = session.Sync(width, height, 24, camera);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(width, height));
        using var renderer = new SilkMeshRenderer(device);
        _ = renderer.ApplyAndRender(page, color, depth);
        byte[] identity = new byte[width * height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(identity);

        var presentation = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1)
        {
            OutputTransform = RenderSettings.PresentationDefault.OutputTransform,
            Exposure = RenderSettings.PresentationDefault.Exposure,
        };
        _ = renderer.Render(color, depth, presentation);
        byte[] mapped = new byte[identity.Length];
        color.ReadbackForTesting(mapped);
        return new OutputTransformSaturation(
            ExactWhiteFraction(identity),
            ExactWhiteFraction(mapped));
    }

    private static double ExactWhiteFraction(ReadOnlySpan<byte> pixels)
    {
        int white = 0;
        for (int index = 0; index < pixels.Length; index += ParityImage.BytesPerPixel)
        {
            if (pixels[index] == byte.MaxValue &&
                pixels[index + 1] == byte.MaxValue &&
                pixels[index + 2] == byte.MaxValue)
            {
                white++;
            }
        }
        return white / (double)(pixels.Length / ParityImage.BytesPerPixel);
    }

    private static async Task AssertChaseOutputTransform(OutputTransformSaturation saturation)
    {
        await Assert.That(saturation.IdentityWhiteFraction).IsGreaterThan(0.4);
        await Assert.That(saturation.PresentationWhiteFraction).IsLessThan(0.01);
    }

    private static float[] OutsideUnitTextureCoordinates() =>
    [
        1.18f, -0.27f,
        1.86f, 0.21f,
        1.72f, 1.34f,
        1.24f, 1.12f,
    ];

    private static ParityImage CaptureSyntheticMaterialXSelfConsistency()
    {
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
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            SilkMeshRendererConformance.CreateFrameCommand(
                checked((uint)Width),
                checked((uint)Height),
                IdentityMatrix()),
            CreateMaterialCommand("/Mtlx", SilkSurfaceKind.MaterialXProjected),
            CreateMaterialCommand("/Preview", SilkSurfaceKind.PreviewSurface),
            CreateMaterialMeshCommand(1, "/MtlxMesh", "/Mtlx", -0.5f),
            CreateMaterialMeshCommand(2, "/PreviewMesh", "/Preview", 0.5f));
        var options = new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1);
        _ = renderer.Render(color, depth, options);
        _ = renderer.Render(color, depth, options);
        byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(pixels);
        return new ParityImage(Width, Height, pixels);
    }

    private static ParityImage CaptureGeneratedMaterialXSelfConsistency(
        Func<ISilkGraphicsDevice> createDevice)
    {
        string stagePath = WriteGeneratedMaterialXSelfConsistencyStage();
        string pluginPath = ResolvePluginPath();
        PrependHdSilkNativeSearchPath();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
        using OpenUsdSilkPage page = session.Sync(
            Width,
            Height,
            TimeCode,
            new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, []));
        EnsureGeneratedMaterialPublished(page);
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
        for (int attempt = 0; attempt < 60; attempt++)
        {
            _ = renderer.ApplyAndRender(page, color, depth, options);
            Thread.Sleep(10);
        }
        byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(pixels);
        return new ParityImage(Width, Height, pixels);
    }

    private static void EnsureGeneratedMaterialPublished(OpenUsdSilkPage page)
    {
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            if (commands.Current.Type != SilkCommandType.MaterialUpsert)
            {
                continue;
            }
            SilkMaterialUpsertCommand material = commands.Current.AsMaterialUpsert();
            if (material.SurfaceKind == SilkSurfaceKind.MaterialXGenerated &&
                !material.GeneratedFragmentSpirV.IsEmpty)
            {
                return;
            }
        }

        throw new InvalidOperationException("The generated MaterialX material was not published in the hdSilk page.");
    }

    private static string WriteGeneratedMaterialXSelfConsistencyStage()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "materialx-generated");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "materialx-generated-self-consistency.usda");
        File.WriteAllText(path, GeneratedMaterialXSelfConsistencyStage, new UTF8Encoding(false));
        return path;
    }

    private static string WriteEmptyStage()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "empty-stage");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "empty-stage.usda");
        File.WriteAllText(path, EmptyStage, new UTF8Encoding(false));
        return path;
    }

    private static MeshPageStats CaptureMeshPageStats(
        OpenUsdSilkSession session,
        RenderComplexity? complexity)
    {
        using OpenUsdSilkPage page = complexity is { } requested
            ? session.Sync(Width, Height, TimeCode, CameraState.Default, requested)
            : session.Sync(Width, Height, TimeCode, CameraState.Default);
        int pointListPoints = 0;
        int pointListIndices = 0;
        int lineListPrimitives = 0;
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            if (commands.Current.Type != SilkCommandType.MeshUpsert)
            {
                continue;
            }
            SilkMeshUpsertCommand mesh = commands.Current.AsMeshUpsert();
            if (mesh.TopologyKind == SilkTopologyKind.PointList)
            {
                pointListPoints += mesh.PointCount;
                pointListIndices += mesh.IndexCount;
            }
            else if (mesh.TopologyKind == SilkTopologyKind.LineList)
            {
                lineListPrimitives += mesh.TriangleCount;
            }
        }
        return new MeshPageStats(pointListPoints, pointListIndices, lineListPrimitives);
    }


    [SupportedOSPlatform("windows")]
    private static ParityImage CaptureHdSilkDrawModeD3D12(RenderDrawMode drawMode)
    {
        PrependHdSilkNativeSearchPath();
        string stagePath = ResolveParityAsset("parity-orientation-asymmetric.usda");
        string pluginPath = ResolvePluginPath();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
        using OpenUsdSilkPage page = session.Sync(
            Width,
            Height,
            TimeCode,
            new CameraState(Matrix4x4.Identity, Matrix4x4.Identity),
            RenderComplexity.Low,
            drawMode);
        using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            checked((uint)Width),
            checked((uint)Height),
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(
            checked((uint)Width),
            checked((uint)Height)));
        using var renderer = new SilkMeshRenderer(device);
        _ = renderer.ApplyAndRender(
            page,
            color,
            depth,
            new SilkMeshRenderOptions(new SilkColor(0, 0, 0, 1), 1));
        byte[] pixels = new byte[Width * Height * ParityImage.BytesPerPixel];
        color.ReadbackForTesting(pixels);
        return new ParityImage(Width, Height, pixels);
    }

    private static int CountDifferentBytes(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Pixel buffers must have equal length.");
        }

        int count = 0;
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                count++;
            }
        }
        return count;
    }

    private static void AssertNonZeroPixel(ReadOnlySpan<byte> pixels, int offset)
    {
        if (pixels[offset] == 0 &&
            pixels[offset + 1] == 0 &&
            pixels[offset + 2] == 0 &&
            pixels[offset + 3] == 0)
        {
            throw new InvalidOperationException($"Expected non-zero pixel at byte offset {offset}.");
        }
    }

    private static bool ContainsPixelDifferentFromClear(ReadOnlySpan<byte> pixels, Vector4 clearColor)
    {
        Span<byte> clear = stackalloc byte[ParityImage.BytesPerPixel];
        clear[0] = convertChannel(clearColor.X);
        clear[1] = convertChannel(clearColor.Y);
        clear[2] = convertChannel(clearColor.Z);
        clear[3] = convertChannel(clearColor.W);
        for (int offset = 0; offset < pixels.Length; offset += ParityImage.BytesPerPixel)
        {
            if (!pixels.Slice(offset, ParityImage.BytesPerPixel).SequenceEqual(clear))
            {
                return true;
            }
        }
        return false;

        static byte convertChannel(float value) =>
            (byte)Math.Clamp((int)MathF.Round(value * byte.MaxValue), byte.MinValue, byte.MaxValue);
    }

    private static void PrependHdSilkNativeSearchPath()
    {
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            return;
        }

        PrependNativeSearchPath(
            Path.Combine(AppContext.BaseDirectory, "parity-capture", "runtime", "bin"),
            Path.Combine(root, "native", "install", "shim", "win-x64", "bin"),
            Path.Combine(root, "native", "install", "win-x64", "bin"),
            Path.Combine(root, "native", "install", "win-x64", "lib"),
            Path.Combine(root, "native", "install", "vulkan-sdk-1.4.321.0", "Bin"),
            Path.Combine(root, "..", "openusd", "native", "install", "win-x64", "bin"),
            Path.Combine(root, "..", "openusd", "native", "install", "win-x64", "lib"),
            Path.Combine(root, "..", "openusd", "native", "install", "vulkan-sdk-1.4.321.0", "Bin"));
    }

    private static void PrependNativeSearchPath(params string[] directories)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string prefix = string.Join(
            Path.PathSeparator,
            directories.Where(Directory.Exists).Select(Path.GetFullPath));
        Environment.SetEnvironmentVariable("PATH", prefix + Path.PathSeparator + currentPath);
    }

    private static byte[] CreateMaterialCommand(
        string path,
        SilkSurfaceKind kind,
        ReadOnlySpan<float> diffuseColor = default)
    {
        MaterialScalarSpec[] scalars =
        [
            new(
                SilkMaterialParameter.DiffuseColor,
                diffuseColor.IsEmpty ? [0.90f, 0.28f, 0.08f] : diffuseColor.ToArray()),
            new(SilkMaterialParameter.Roughness, [0.72f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        return CreateMaterialCommandWithScalars(path, kind, scalars);
    }

    private static byte[] CreateMaterialCommandWithScalars(
        string path,
        SilkSurfaceKind kind,
        ReadOnlySpan<MaterialScalarSpec> scalars)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        var bytes = new byte[
            32 + pathBytes.Length + GetScalarByteCount(scalars) + (2 * sizeof(uint))];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), ComputeStableHash(path));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)kind);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), checked((uint)scalars.Length));
        pathBytes.CopyTo(bytes.AsSpan(32));
        int offset = 32 + pathBytes.Length;
        foreach (MaterialScalarSpec scalar in scalars)
        {
            WriteScalar(bytes, ref offset, scalar.Parameter, scalar.Values);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + sizeof(uint)), 0);
        return bytes;
    }

    private static byte[] CreateTexturedMaterialCommand(
        string path,
        string asset,
        SilkMaterialParameter parameter,
        SilkTextureWrap wrap,
        SilkColorSpace colorSpace,
        ReadOnlySpan<float> scale,
        ReadOnlySpan<float> bias,
        ReadOnlySpan<float> fallback,
        string uvPrimvar)
    {
        MaterialScalarSpec[] scalars =
        [
            new(SilkMaterialParameter.DiffuseColor, [0.90f, 0.28f, 0.08f]),
            new(SilkMaterialParameter.Roughness, [0.72f]),
            new(SilkMaterialParameter.Metallic, [0.0f]),
        ];
        return CreateTexturedMaterialCommand(
            path,
            asset,
            parameter,
            wrap,
            colorSpace,
            scale,
            bias,
            fallback,
            uvPrimvar,
            scalars);
    }

    private static byte[] CreateTexturedMaterialCommand(
        string path,
        string asset,
        SilkMaterialParameter parameter,
        SilkTextureWrap wrap,
        SilkColorSpace colorSpace,
        ReadOnlySpan<float> scale,
        ReadOnlySpan<float> bias,
        ReadOnlySpan<float> fallback,
        string uvPrimvar,
        ReadOnlySpan<MaterialScalarSpec> scalars)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        byte[] assetBytes = Encoding.UTF8.GetBytes(asset);
        byte[] uvBytes = Encoding.UTF8.GetBytes(uvPrimvar);
        var bytes = new byte[
            32 + pathBytes.Length + GetScalarByteCount(scalars) + 76 +
            assetBytes.Length + uvBytes.Length + (2 * sizeof(uint))];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), ComputeStableHash(path));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)SilkSurfaceKind.PreviewSurface);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), checked((uint)scalars.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 1);
        pathBytes.CopyTo(bytes.AsSpan(32));
        int offset = 32 + pathBytes.Length;
        foreach (MaterialScalarSpec scalar in scalars)
        {
            WriteScalar(bytes, ref offset, scalar.Parameter, scalar.Values);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), (uint)parameter);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), (uint)wrap);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8), (uint)wrap);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 12), (uint)colorSpace);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 16), (uint)assetBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 20), (uint)uvBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24), 4);
        WriteVector4(bytes.AsSpan(offset + 28), scale);
        WriteVector4(bytes.AsSpan(offset + 44), bias);
        WriteVector4(bytes.AsSpan(offset + 60), fallback);
        assetBytes.CopyTo(bytes.AsSpan(offset + 76));
        uvBytes.CopyTo(bytes.AsSpan(offset + 76 + assetBytes.Length));
        int generatedOffset = offset + 76 + assetBytes.Length + uvBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(generatedOffset), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(generatedOffset + sizeof(uint)), 0);
        return bytes;
    }

    private static void WriteVector4(Span<byte> bytes, ReadOnlySpan<float> values)
    {
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes[(component * sizeof(float))..], values[component]);
        }
    }

    private static int GetScalarByteCount(ReadOnlySpan<MaterialScalarSpec> scalars)
    {
        int length = 0;
        foreach (MaterialScalarSpec scalar in scalars)
        {
            length = checked(length + (2 * sizeof(uint)) + (scalar.Values.Length * sizeof(float)));
        }
        return length;
    }

    private static byte[] CreateTexturedMeshCommand(
        ulong id,
        string path,
        string materialPath,
        float x,
        ReadOnlySpan<float> textureCoordinates = default)
    {
        float[] defaultTextureCoordinates =
        [
            0.08f, 0.12f,
            0.86f, 0.16f,
            0.72f, 0.82f,
            0.22f, 0.78f,
        ];
        ReadOnlySpan<float> resolvedTextureCoordinates = textureCoordinates.IsEmpty
            ? defaultTextureCoordinates
            : textureCoordinates;
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            id,
            path,
            [
                -0.30f, -0.52f, 0.08f, 0.30f, -0.36f, 0.08f,
                0.24f, 0.48f, 0.08f, -0.24f, 0.32f, 0.08f,
            ],
            [0, 1, 2, 0, 2, 3],
            x,
            0,
            [1, 1, 1, 1]);
        byte[] materialBytes = Encoding.UTF8.GetBytes(materialPath);
        byte[] attributes = CreateAttribute(
            SilkAttributeSemantic.TexCoord,
            2,
            SilkAttributeInterpolation.Vertex,
            "st",
            resolvedTextureCoordinates);
        Array.Resize(ref mesh, mesh.Length + materialBytes.Length + attributes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(4), (uint)mesh.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(mesh.AsSpan(208), ComputeStableHash(materialPath));
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(216), (uint)materialBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(220), 1);
        int materialOffset = mesh.Length - materialBytes.Length - attributes.Length;
        materialBytes.CopyTo(mesh.AsSpan(materialOffset));
        attributes.CopyTo(mesh.AsSpan(materialOffset + materialBytes.Length));
        return mesh;
    }

    private static byte[] CreateDisplayMeshCommand(
        ulong id,
        string path,
        float x,
        SilkMeshCullStyle cullStyle,
        bool doubleSided = false,
        bool backFacing = false)
    {
        uint[] indices = backFacing
            ? [2, 1, 0, 3, 2, 0]
            : [0, 1, 2, 0, 2, 3];
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            id,
            path,
            [
                -0.30f, -0.52f, 0.08f, 0.30f, -0.36f, 0.08f,
                0.24f, 0.48f, 0.08f, -0.24f, 0.32f, 0.08f,
            ],
            indices,
            x,
            0,
            [0.70f, 0.42f, 0.18f, 1]);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(40), doubleSided ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(44), (uint)cullStyle);
        return mesh;
    }

    private static byte[] CreateAreaLightFrameCommand(AreaLightGate gate)
    {
        const int lightingSize = 1272;
        const int lightCountOffset = 536;
        const int lightTableOffset = 552;
        const int lightEntrySize = 176;
        byte[] bytes = SilkMeshRendererConformance.CreateFrameCommand(
            checked((uint)Width),
            checked((uint)Height),
            IdentityMatrix());
        Array.Resize(ref bytes, lightingSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(lightCountOffset), 2);
        AreaLightSpec left = CreateLeftAreaLight(gate);
        AreaLightSpec right = CreateRightAreaLight(gate);
        WriteAreaLight(bytes, lightTableOffset, left);
        WriteAreaLight(bytes, lightTableOffset + lightEntrySize, right);
        return bytes;
    }

    private static AreaLightSpec CreateLeftAreaLight(AreaLightGate gate) =>
        gate switch
        {
            AreaLightGate.DiskEquivalent =>
                new(2, 0, 0, 0, 0, new Vector3(0, 1, 0), -0.5f, 0),
            AreaLightGate.DiskDivergence =>
                new(4, 0, 0, 0, 0.12f, new Vector3(0, 1, 0), -0.5f, 0),
            _ => new(2, 0, 0, 0, 0.12f, new Vector3(0, 0, 1), -0.5f, 0),
        };

    private static AreaLightSpec CreateRightAreaLight(AreaLightGate gate) =>
        gate switch
        {
            AreaLightGate.RectEquivalent =>
                new(3, 0, 0, 0, 0.12f, new Vector3(0, 0, 1), 0.5f, 0),
            AreaLightGate.RectDivergence =>
                new(3, 0.85f, 0.65f, 0, 0.12f, new Vector3(0, 0, 1), 0.5f, 0),
            AreaLightGate.DiskEquivalent =>
                new(4, 0, 0, 0, 0.12f, new Vector3(0, 1, 0), 0.5f, 0),
            AreaLightGate.DiskDivergence =>
                new(4, 0, 0, 0, 0.12f, new Vector3(0, 0, -1), 0.5f, 0),
            AreaLightGate.CylinderEquivalent =>
                new(5, 0, 0, 0.16f, 0.12f, new Vector3(1, 0, 0), 0.5f, 0),
            AreaLightGate.CylinderDivergence =>
                new(5, 0.95f, 0, 0.16f, 0.12f, new Vector3(1, 0, 0), 0.5f, 0),
            _ => new(2, 0, 0, 0, 0.12f, new Vector3(0, 0, 1), 0.5f, 0),
        };

    private static void WriteAreaLight(byte[] bytes, int offset, AreaLightSpec light)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), light.Type);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 8), light.ShapeX);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 12), light.ShapeY);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 16), 1);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 20), 1);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 24), 1);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 28), light.Intensity);
        double[] transform = AreaLightTransform(light.X, light.Y, 0.70f, light.Direction);
        for (int index = 0; index < transform.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(offset + 32 + (index * sizeof(double))),
                transform[index]);
        }
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 164), 1);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 168), 0);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 172), light.Radius);
    }

    private static double[] AreaLightTransform(float x, float y, float z, Vector3 direction)
    {
        Vector3 zAxis = Vector3.Normalize(direction);
        Vector3 xAxis = MathF.Abs(Vector3.Dot(zAxis, Vector3.UnitX)) > 0.95f
            ? Vector3.UnitY
            : Vector3.UnitX;
        Vector3 yAxis = Vector3.Normalize(Vector3.Cross(zAxis, xAxis));
        xAxis = Vector3.Normalize(Vector3.Cross(yAxis, zAxis));
        return
        [
            xAxis.X, xAxis.Y, xAxis.Z, 0,
            yAxis.X, yAxis.Y, yAxis.Z, 0,
            zAxis.X, zAxis.Y, zAxis.Z, 0,
            x, y, z, 1,
        ];
    }

    private readonly record struct AreaLightSpec(
        uint Type,
        float ShapeX,
        float ShapeY,
        float Radius,
        float Intensity,
        Vector3 Direction,
        float X,
        float Y);

    private static byte[] CreateAttribute(
        SilkAttributeSemantic semantic,
        int componentCount,
        SilkAttributeInterpolation interpolation,
        string name,
        ReadOnlySpan<float> values)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        int elementCount = interpolation == SilkAttributeInterpolation.Constant
            ? 1
            : values.Length / componentCount;
        var bytes = new byte[20 + nameBytes.Length + (values.Length * sizeof(float))];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)semantic);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)componentCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)interpolation);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)nameBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)elementCount);
        nameBytes.CopyTo(bytes.AsSpan(20));
        int valueOffset = 20 + nameBytes.Length;
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(valueOffset + (index * sizeof(float))),
                values[index]);
        }
        return bytes;
    }

    private static byte[] CreateMaterialMeshCommand(ulong id, string path, string materialPath, float x)
    {
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            id,
            path,
            [-0.30f, -0.52f, 0.08f, 0.30f, -0.36f, 0.08f, 0.24f, 0.48f, 0.08f, -0.24f, 0.32f, 0.08f],
            [0, 1, 2, 0, 2, 3],
            x,
            0,
            [0.12f, 0.12f, 0.12f, 1]);
        byte[] materialBytes = Encoding.UTF8.GetBytes(materialPath);
        Array.Resize(ref mesh, mesh.Length + materialBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(4), (uint)mesh.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(mesh.AsSpan(208), ComputeStableHash(materialPath));
        BinaryPrimitives.WriteUInt32LittleEndian(mesh.AsSpan(216), (uint)materialBytes.Length);
        materialBytes.CopyTo(mesh.AsSpan(mesh.Length - materialBytes.Length));
        return mesh;
    }

    private static void WriteScalar(
        byte[] bytes,
        ref int offset,
        SilkMaterialParameter parameter,
        ReadOnlySpan<float> values)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), (uint)parameter);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), (uint)values.Length);
        offset += 8;
        foreach (float value in values)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
            offset += sizeof(float);
        }
    }

    private static ulong ComputeStableHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }
        return hash == 0 ? 1 : hash;
    }

    private static double[] IdentityMatrix()
    {
        double[] matrix = new double[16];
        matrix[0] = 1;
        matrix[5] = 1;
        matrix[10] = 1;
        matrix[15] = 1;
        return matrix;
    }

    private static (byte MaxChannelDelta, double MeanChannelDelta) CompareTranslatedHalves(ParityImage image)
    {
        int halfWidth = image.Width / 2;
        int max = 0;
        long sum = 0;
        long count = 0;
        ReadOnlySpan<byte> pixels = image.Rgba.Span;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < halfWidth; x++)
            {
                int left = ((y * image.Width) + x) * ParityImage.BytesPerPixel;
                int right = ((y * image.Width) + x + halfWidth) * ParityImage.BytesPerPixel;
                for (int channel = 0; channel < 3; channel++)
                {
                    int delta = Math.Abs(pixels[left + channel] - pixels[right + channel]);
                    max = Math.Max(max, delta);
                    sum += delta;
                    count++;
                }
            }
        }

        return ((byte)max, count == 0 ? 0 : (double)sum / count);
    }

    private static TranslatedHalfDelta CompareTranslatedHalvesDetailed(ParityImage image)
    {
        int halfWidth = image.Width / 2;
        int max = 0;
        int maxX = 0;
        int maxY = 0;
        int maxChannel = 0;
        int maxLeft = 0;
        int maxRight = 0;
        long sum = 0;
        long count = 0;
        long leftRed = 0;
        long leftGreen = 0;
        long leftBlue = 0;
        long rightRed = 0;
        long rightGreen = 0;
        long rightBlue = 0;
        ReadOnlySpan<byte> pixels = image.Rgba.Span;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < halfWidth; x++)
            {
                int left = ((y * image.Width) + x) * ParityImage.BytesPerPixel;
                int right = ((y * image.Width) + x + halfWidth) * ParityImage.BytesPerPixel;
                leftRed += pixels[left];
                leftGreen += pixels[left + 1];
                leftBlue += pixels[left + 2];
                rightRed += pixels[right];
                rightGreen += pixels[right + 1];
                rightBlue += pixels[right + 2];
                for (int channel = 0; channel < 3; channel++)
                {
                    int delta = Math.Abs(pixels[left + channel] - pixels[right + channel]);
                    if (delta > max)
                    {
                        max = delta;
                        maxX = x;
                        maxY = y;
                        maxChannel = channel;
                        maxLeft = pixels[left + channel];
                        maxRight = pixels[right + channel];
                    }
                    sum += delta;
                    count++;
                }
            }
        }

        double pixelsPerHalf = Math.Max(1, halfWidth * image.Height);
        return new TranslatedHalfDelta(
            (byte)max,
            count == 0 ? 0 : (double)sum / count,
            maxX,
            maxY,
            maxChannel,
            maxLeft,
            maxRight,
            leftRed / pixelsPerHalf,
            leftGreen / pixelsPerHalf,
            leftBlue / pixelsPerHalf,
            rightRed / pixelsPerHalf,
            rightGreen / pixelsPerHalf,
            rightBlue / pixelsPerHalf);
    }

    private static string FormatTranslatedHalfDelta(
        string name,
        TranslatedHalfDelta delta) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} maxChannelDelta={1} meanChannelDelta={2:F3} " +
            "maxAt=({3},{4}) channel={5} generated={6} preview={7} " +
            "generatedMeanRgb=({8:F3},{9:F3},{10:F3}) previewMeanRgb=({11:F3},{12:F3},{13:F3})",
            name,
            delta.MaxChannelDelta,
            delta.MeanChannelDelta,
            delta.MaxX,
            delta.MaxY,
            ChannelName(delta.MaxChannel),
            delta.MaxLeftChannelValue,
            delta.MaxRightChannelValue,
            delta.LeftMeanRed,
            delta.LeftMeanGreen,
            delta.LeftMeanBlue,
            delta.RightMeanRed,
            delta.RightMeanGreen,
            delta.RightMeanBlue);

    private static string ChannelName(int channel) =>
        channel switch
        {
            0 => "R",
            1 => "G",
            2 => "B",
            _ => channel.ToString(CultureInfo.InvariantCulture),
        };

    private static ParityImage ExtractTranslatedHalf(ParityImage image, bool left)
    {
        int halfWidth = image.Width / 2;
        int sourceX = left ? 0 : halfWidth;
        byte[] extracted = new byte[halfWidth * image.Height * ParityImage.BytesPerPixel];
        ReadOnlySpan<byte> source = image.Rgba.Span;
        int targetStride = halfWidth * ParityImage.BytesPerPixel;
        int sourceStride = image.Width * ParityImage.BytesPerPixel;
        for (int y = 0; y < image.Height; y++)
        {
            source.Slice(
                    (y * sourceStride) + (sourceX * ParityImage.BytesPerPixel),
                    targetStride)
                .CopyTo(extracted.AsSpan(y * targetStride, targetStride));
        }

        return new ParityImage(halfWidth, image.Height, extracted);
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

    private static nuint RegisterImagePlugins()
    {
        string pluginPath = ResolveImagePluginPath();
        return OpenUsdNativeRuntime.RegisterPlugins(pluginPath);
    }

    private static string ResolveImagePluginPath()
    {
        string packaged = Path.Combine(AppContext.BaseDirectory, "usd");
        if (File.Exists(Path.Combine(packaged, "plugInfo.json")))
        {
            return packaged;
        }

        string? openUsdRoot = Environment.GetEnvironmentVariable("OPENUSD_ROOT");
        if (!string.IsNullOrWhiteSpace(openUsdRoot))
        {
            string installed = Path.Combine(openUsdRoot, "plugin", "usd");
            if (File.Exists(Path.Combine(installed, "plugInfo.json")))
            {
                return installed;
            }
        }

        string? configured = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "plugInfo.json")))
        {
            return configured;
        }

        throw new DirectoryNotFoundException(
            $"No OpenUSD image plugin path was found under '{packaged}', OPENUSD_ROOT, or OPENUSD_PLUGIN_PATH.");
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

        if (OperatingSystem.IsMacOS())
        {
            return [CreateMetalBackend()];
        }

        throw new PlatformNotSupportedException("The parity harness supports Windows WGL, Linux GLX, and macOS CGL.");
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

        if (OperatingSystem.IsMacOS())
        {
            return CreateMetalBackend();
        }

        throw new PlatformNotSupportedException("The parity harness supports Windows WGL, Linux GLX, and macOS CGL.");
    }

    [SupportedOSPlatform("windows")]
    private static SilkParityBackend[] CreateWindowsBackends()
    {
        if (string.Equals(
            Environment.GetEnvironmentVariable("OPENUSD_PARITY_WINDOWS_BACKENDS"),
            "D3D12",
            StringComparison.OrdinalIgnoreCase))
        {
            return [CreateD3D12WarpBackend()];
        }

        return
        [
            CreateD3D12WarpBackend(),
            CreateVulkanBackend(),
        ];
    }

    private static float[] PlaneValues(Vector4 plane) =>
        [plane.X, plane.Y, plane.Z, plane.W];

    [SupportedOSPlatform("windows")]
    private static SilkParityBackend CreateD3D12WarpBackend() =>
        new(D3D12WarpBackendName, static () => D3D12SilkGraphicsDevice.Create(useWarp: true));

    private static SilkParityBackend CreateVulkanBackend() =>
        new(VulkanSwiftShaderBackendName, static () => VulkanSilkGraphicsDevice.Create());

    [SupportedOSPlatform("macos")]
    private static SilkParityBackend CreateMetalBackend() =>
        new(MetalBackendName, static () => MetalSilkGraphicsDevice.Create());

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

    private static bool MeetsRequiredAdjustedIou(
        ParityScene scene,
        ParityComparisonResult result) =>
        scene.RequiredAdjustedIou is not { } required ||
        result.AdjustedCoverageIntersectionOverUnion >= required;

    private static async Task AssertRequiredAdjustedIou(
        ParityScene scene,
        string backendName,
        ParityComparisonResult result)
    {
        if (scene.RequiredAdjustedIou is not { } required)
        {
            return;
        }

        await Assert.That(result.AdjustedCoverageIntersectionOverUnion)
            .IsGreaterThanOrEqualTo(required)
            .Because(
                $"{scene.Name} {backendName} must not regress below the documented " +
                $"{required:F6} adjusted-IoU parity claim.");
    }

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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_028, 948, 262_144)),
            },
            new ParityScene(
                "primvar-st-varying-texture",
                Path.Combine(assetRoot, "parity-primvar-st-varying-texture.usda"),
                "Texture-backed PreviewSurface samples a varying st primvar.",
                ColorComparisonReady: true,
                GateEnabled: true,
                GateReason:
                    "Varying st interpolation uses the same authored values as the vertex-texture " +
                    "pennant but travels through Hydra's varying primvar descriptor before hdSilk " +
                    "emits the vertex attribute. It gates at 1.000000 adjusted IoU, " +
                    "0.218603 perturbation margin, and colour deltas max 4 / mean 1.717.",
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_156, 1_076, 262_144)),
            },
            new ParityScene(
                "primvar-st-facevarying-texture",
                Path.Combine(assetRoot, "parity-primvar-st-facevarying-texture.usda"),
                "Texture-backed PreviewSurface samples a faceVarying st primvar.",
                ColorComparisonReady: true,
                GateEnabled: true,
                GateReason:
                    "Face-varying st interpolation forces hdSilk's expanded-topology path: one " +
                    "texture coordinate per emitted face vertex after HdMeshUtil triangulation. " +
                    "It gates at 1.000000 adjusted IoU, 0.218603 perturbation margin, and " +
                    "colour deltas max 4 / mean 1.717.",
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_284, 1_204, 262_144)),
            },
            new ParityScene(
                "primvar-st-uniform-texture",
                Path.Combine(assetRoot, "parity-primvar-st-uniform-texture.usda"),
                "Texture-backed PreviewSurface samples a uniform st primvar.",
                ColorComparisonReady: true,
                GateEnabled: true,
                GateReason:
                    "Uniform st interpolation forces one sampled coordinate per source face, then " +
                    "hdSilk expands that value across each emitted triangle before shading. It " +
                    "gates at 1.000000 adjusted IoU, 0.218603 perturbation margin, and colour " +
                    "deltas max 4 / mean 1.347.",
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_284, 1_204, 262_144)),
            },
            new ParityScene(
                "material-metallic-workflow",
                Path.Combine(assetRoot, "parity-material-metallic-workflow.usda"),
                "PreviewSurface metallic workflow lights a metallic prim beside a dielectric one.",
                ColorComparisonReady: true,
                GateEnabled: true,
                GateReason:
                    "Coverage and colour gate after splitting direct diffuse and specular irradiance: " +
                    "Storm's PreviewSurface path applies the light's pi-scaled diffuse irradiance to " +
                    "Lambert but not to direct specular. The all-pi path measured max 25 / mean 14.112; " +
                    "removing only the specular pi reduces the metallic residual to max 8 / mean 4.558.",
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
            {
                UseSceneLights = true,
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(2, 2, 2, 2, 2, 2, 1_396, 1_236, 0)),
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: null)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(2, 2, 2, 2, 2, 2, 1_468, 1_308, 0)),
            },
            new ParityScene(
                "materialx-standard-surface-preview-equivalent",
                Path.Combine(assetRoot, "parity-materialx-standard-surface-preview-equivalent.usda"),
                "UsdPreviewSurface equivalent of the registered MaterialX standard_surface constant projection.",
                ColorComparisonReady: true,
                GateEnabled: true,
                GateReason:
                    "The hand-authored PreviewSurface equivalent of the MaterialX constant projection gates " +
                    "at 1.000000 adjusted IoU, 0.442112 perturbation margin, and colour deltas max 10 / " +
                    "mean 4.424. This verifies the projection arithmetic while the authored MaterialX " +
                    "scene remains an ungated Storm capability gap.",
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_012, 932, 0)),
            },
            new ParityScene(
                "light-distant-exposure",
                Path.Combine(assetRoot, "parity-light-distant-exposure.usda"),
                "UsdLuxDistantLight colour, intensity, and exposure direct-light measurement.",
                ColorComparisonReady: true,
                GateEnabled: true,
                GateReason:
                    "Matte off-centre hook gates direct distant light at 1.000000 adjusted IoU, " +
                    "0.609274 perturbation margin, and colour deltas max 4 / mean 1.095. " +
                    "The previous concentrated max 42 residual was the authored specular lobe.",
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
            {
                UseSceneLights = true,
                SceneLightSensitivityStagePath =
                    Path.Combine(assetRoot, "parity-light-distant-exposure-double.usda"),
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_108, 1_028, 0)),
            },
            new ParityScene(
                "light-distant-specular",
                Path.Combine(assetRoot, "parity-light-distant-specular.usda"),
                "UsdLuxDistantLight lights a glossy PreviewSurface specular lobe.",
                ColorComparisonReady: true,
                GateEnabled: true,
                GateReason:
                    "Glossy direct specular under a UsdLuxDistantLight gates at 1.000000 adjusted IoU, " +
                    "0.609274 perturbation margin, and colour deltas max 10 / mean 6.027. This covers " +
                    "the specular lobe that the matte direct-light transport scenes intentionally exclude.",
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
            {
                UseSceneLights = true,
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_108, 1_028, 0)),
            },
            new ParityScene(
                "light-sphere-point",
                Path.Combine(assetRoot, "parity-light-sphere-point.usda"),
                "UsdLuxSphereLight direct-light transport and point-attenuation measurement.",
                ColorComparisonReady: true,
                GateEnabled: true,
                GateReason:
                    "Matte off-centre boomerang gates point attenuation at 1.000000 adjusted IoU, " +
                    "0.542752 perturbation margin, and colour deltas max 13 / mean 0.782. " +
                    "The previous concentrated max 36 residual was the authored specular lobe.",
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
            {
                UseSceneLights = true,
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_108, 1_028, 0)),
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
            {
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(1, 1, 1, 1, 1, 1, 1_012, 932, 0)),
            },
            new ParityScene(
                "light-distant-shadow",
                Path.Combine(assetRoot, "parity-light-distant-shadow.usda"),
                "UsdLuxDistantLight authored shadowEnable with an offset receiver and blocker.",
                ColorComparisonReady: true,
                GateEnabled: false,
                GateReason:
                    "Storm and hdSilk agree on the direct-light image at 1.000000 " +
                    "adjusted IoU, 0.339119 perturbation margin, and colour deltas " +
                    "max 3 / mean 0.161, but the paired shadow-disabled stage is " +
                    "byte-identical (disabledAdjustedIoU 1.000000). Storm's measured " +
                    "offscreen harness therefore does not render this authored shadow, " +
                    "so the scene records the exclusion and stays ungated.",
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: null)
            {
                UseSceneLights = true,
                ShadowDisabledStagePath =
                    Path.Combine(assetRoot, "parity-light-distant-shadow-disabled.usda"),
                PerformanceBudgets = CurrentBackendBudgets(
                    ParityPerformanceBudget.FromMeasured(2, 2, 2, 2, 2, 2, 1_444, 1_284, 0)),
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
                    "scene near hdSilk's coarse HdMeshUtil topology but not exactly: " +
                    "0.931015 adjusted IoU. hdSilk splits quads on the face-local " +
                    "0-2 diagonal; forcing the opposite 1-3 split worsened the " +
                    "score to 0.872473, so Storm's coarse all-quad handling is not " +
                    "the same triangle topology. It remains ungated.",
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: null)
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
                RecommendedMinimumAdjustedIou: 0.92,
                RequiredAdjustedIou: ExactCuratedParityAdjustedIou)
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
            "test-assets\\parity\\parity-primvar-st-varying-texture.usda",
            "test-assets\\parity\\parity-primvar-st-facevarying-texture.usda",
            "test-assets\\parity\\parity-primvar-st-uniform-texture.usda",
            "test-assets\\parity\\parity-point-instancer-cluster.usda",
            "test-assets\\parity\\parity-single-sided-winding.usda",
            "test-assets\\parity\\parity-clip-plane-asymmetric.usda",
            "test-assets\\parity\\parity-cards-draw-mode.usda",
            "test-assets\\parity\\parity-bounds-draw-mode.usda",
            "test-assets\\parity\\parity-origin-draw-mode.usda",
            "test-assets\\parity\\parity-light-distant-exposure.usda",
            "test-assets\\parity\\parity-light-distant-exposure-double.usda",
            "test-assets\\parity\\parity-light-distant-specular.usda",
            "test-assets\\parity\\parity-light-sphere-point.usda",
            "test-assets\\parity\\parity-light-dome-ambient.usda",
            "test-assets\\parity\\parity-material-metallic-workflow.usda",
            "test-assets\\parity\\parity-materialx-standard-surface-constant.usda",
            "test-assets\\parity\\parity-materialx-standard-surface-preview-equivalent.usda",
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

    private static string FormatAdjustedIouEvidence(
        ParityScene scene,
        SilkParityCapture silk,
        ParityComparisonResult result) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}\t{1}\t{2:F6}\t{3}",
            scene.Name,
            silk.BackendName,
            result.AdjustedCoverageIntersectionOverUnion,
            scene.RequiredAdjustedIou is { } required
                ? required.ToString("F6", CultureInfo.InvariantCulture)
                : "n/a");

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
            [MetalBackendName] = budget,
        };

    private sealed record ParityScene(
        string Name,
        string StagePath,
        string Purpose,
        bool ColorComparisonReady,
        bool GateEnabled,
        string GateReason,
        double RecommendedMinimumAdjustedIou,
        double? RequiredAdjustedIou)
    {
        public IReadOnlyList<Vector4> ClipPlanes { get; init; } = [];

        public double TimeCode { get; init; } = StormSilkParityCaptureDriverTests.TimeCode;

        public bool UseSceneLights { get; init; }

        public string? SceneLightSensitivityStagePath { get; init; }

        public string? ShadowDisabledStagePath { get; init; }

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

    private sealed record StormShadowSensitivityEvidence(
        string DisabledStagePath,
        string DisabledHash,
        bool ChangedStorm,
        object Comparison);

    private readonly record struct OutputTransformSaturation(
        double IdentityWhiteFraction,
        double PresentationWhiteFraction);

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
