// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Text.Json;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class StormSilkParityCaptureDriverTests
{
    [Test]
    public async Task PtexQuadReferenceRespondsToOffsetsAndAuthoredFaceIndices()
    {
        if (!StormGlContextFactory.IsCurrentPlatformSupported)
        {
            SkipOrFail("Ptex quad Storm reference", "The current platform has no Storm OpenGL context.");
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.");
        }
        if (!CanCreatePlatformGlContext())
        {
            return;
        }

        string pluginPath;
        try
        {
            PrependHdSilkNativeSearchPath();
            pluginPath = ResolvePluginPath();
        }
        catch (DirectoryNotFoundException exception)
        {
            SkipOrFail("Ptex quad Storm reference", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        string repository = FindRepositoryRoot()
            ?? throw new InvalidOperationException("Ptex reference evidence requires the repository inputs.");
        string[] fixtures =
        [
            "test-assets\\native-profile\\ptex\\quad-colors.ptx",
            "test-assets\\native-profile\\ptex\\generate_quad_fixture.cpp",
        ];
        ParitySourceIdentity sourceIdentity = CreateSourceIdentity();
        ParitySourceIdentity fixtureIdentity = ParityEvidenceInputs.CreateFixtureIdentity(repository, fixtures);
        ParityExecutionIdentity execution = ParityExecutionIdentity.Read(Environment.GetEnvironmentVariable);
        object packageIdentity = CreatePackageIdentity(pluginPath);
        string packageSnapshot = JsonSerializer.Serialize(packageIdentity, EvidenceJsonOptions);
        string directory = Path.Combine(Path.GetTempPath(), $"openusd-ptex-quad-reference-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var records = new List<(string Scheme, List<string> Lines, StormOpenGlEvidence? OpenGl)>();

        try
        {
            File.Copy(
                ResolveTestAsset(Path.Combine("native-profile", "ptex", "quad-colors.ptx")),
                Path.Combine(directory, "quad-colors.ptx"));
            foreach (string scheme in new[] { "catmullClark", "bilinear" })
            {
                string contents = CreatePtexQuadStage(scheme);
                string stage = Path.Combine(directory, $"{scheme}.usda");
                File.WriteAllText(stage, contents);
                var input = new ParityCaptureInput(
                    pluginPath,
                    stage,
                    Width,
                    Height,
                    1,
                    new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, []),
                    new SilkColor(0, 0, 0, 1),
                    OpenUsdStormRuntime.Headlight,
                    UseSceneLights: false);
                ParityCaptureSet baseline = await ParityCaptureDriver.CaptureAsync(
                    input, StormGlContextFactory.CreateForCurrentPlatform(), []).ConfigureAwait(false);
                ParityCaptureSet offset = await ParityCaptureDriver.CaptureAsync(
                    input with { TimeCode = 2 },
                    StormGlContextFactory.CreateForCurrentPlatform(),
                    []).ConfigureAwait(false);

                string reorder = Path.Combine(directory, $"{scheme}-reordered.usda");
                File.WriteAllText(
                    reorder,
                    ReplacePtexQuadFragment(
                        contents,
                        "int[] faceVertexIndices = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]",
                        "int[] faceVertexIndices = [12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]"));
                ParityCaptureSet reordered = await ParityCaptureDriver.CaptureAsync(
                    input with { StagePath = reorder },
                    StormGlContextFactory.CreateForCurrentPlatform(),
                    []).ConfigureAwait(false);

                string indexed = Path.Combine(directory, $"{scheme}-face-index.usda");
                File.WriteAllText(
                    indexed,
                    ReplacePtexQuadFragment(
                        contents,
                        "int[] faceVertexCounts = [4, 4, 4, 4]",
                        """
                        int[] faceVertexCounts = [4, 4, 4, 4]
                        int[] primvars:ptexFaceIndex = [1, 2, 3, 0] (
                            interpolation = "uniform"
                        )
                        """));
                ParityCaptureSet faceIndex = await ParityCaptureDriver.CaptureAsync(
                    input with { StagePath = indexed },
                    StormGlContextFactory.CreateForCurrentPlatform(),
                    []).ConfigureAwait(false);
                ParityCaptureSet faceIndexAndOffset = await ParityCaptureDriver.CaptureAsync(
                    input with { StagePath = indexed, TimeCode = 2 },
                    StormGlContextFactory.CreateForCurrentPlatform(),
                    []).ConfigureAwait(false);

                ImageDelta offsetDelta = ImageDelta.Compare(baseline.Storm, offset.Storm);
                ImageDelta reorderDelta = ImageDelta.Compare(offset.Storm, reordered.Storm);
                ImageDelta faceIndexBaselineDelta = ImageDelta.Compare(baseline.Storm, faceIndex.Storm);
                ImageDelta faceIndexOffsetDelta = ImageDelta.Compare(offset.Storm, faceIndex.Storm);
                ImageDelta combinedOffsetDelta = ImageDelta.Compare(offset.Storm, faceIndexAndOffset.Storm);
                var evidence = new List<string>
                {
                    $"scheme={scheme}",
                    $"baseline nonblack={CountNonBlackPixels(baseline.Storm)}",
                    $"baseline hash={Hash(baseline.Storm)}",
                    $"offset hash={Hash(offset.Storm)}",
                    $"reordered hash={Hash(reordered.Storm)}",
                    $"authored face-index hash={Hash(faceIndex.Storm)}",
                    $"offset vs baseline max={offsetDelta.MaximumChannelDelta} mean={offsetDelta.MeanChannelDelta}",
                    $"offset vs reordered max={reorderDelta.MaximumChannelDelta} mean={reorderDelta.MeanChannelDelta}",
                    $"face-index vs baseline max={faceIndexBaselineDelta.MaximumChannelDelta} " +
                        $"mean={faceIndexBaselineDelta.MeanChannelDelta}",
                    $"face-index vs offset max={faceIndexOffsetDelta.MaximumChannelDelta} " +
                        $"mean={faceIndexOffsetDelta.MeanChannelDelta}",
                    $"face-index and offset vs offset max={combinedOffsetDelta.MaximumChannelDelta} " +
                        $"mean={combinedOffsetDelta.MeanChannelDelta}",
                };
                if (baseline.OpenGlEvidence is { } openGl)
                {
                    evidence.Add($"OpenGL vendor={openGl.Vendor} renderer={openGl.Renderer} version={openGl.Version}");
                    evidence.Add($"OpenGL module={openGl.LoadedOpenGl32} sha256={openGl.LoadedOpenGl32Sha256}");
                }
                records.Add((scheme, evidence, baseline.OpenGlEvidence));

                await Assert.That(CountNonBlackPixels(baseline.Storm)).IsGreaterThan(3_000);
                await Assert.That(offsetDelta.MeanChannelDelta).IsGreaterThan(1.0);
                await Assert.That(reorderDelta.MaximumChannelDelta).IsLessThanOrEqualTo(8);
                await Assert.That(reorderDelta.MeanChannelDelta).IsLessThanOrEqualTo(0.25);
                await Assert.That(faceIndexBaselineDelta.MaximumChannelDelta).IsLessThanOrEqualTo(8)
                    .Because("This Storm fixture does not use the authored uniform ptexFaceIndex as a face remap.");
                await Assert.That(faceIndexBaselineDelta.MeanChannelDelta).IsLessThanOrEqualTo(0.25);
                await Assert.That(faceIndexOffsetDelta.MeanChannelDelta).IsGreaterThan(1.0);
                await Assert.That(combinedOffsetDelta.MaximumChannelDelta).IsLessThanOrEqualTo(8);
                await Assert.That(combinedOffsetDelta.MeanChannelDelta).IsLessThanOrEqualTo(0.25);

                (int X, int Y, int Red, int Green, int Blue)[] samples =
                [
                    (40, 100, 255, 255, 255),
                    (120, 100, 255, 0, 0),
                    (40, 43, 0, 255, 0),
                    (120, 43, 0, 0, 255),
                ];
                foreach ((int x, int y, int red, int green, int blue) in samples)
                {
                    await Assert.That(PtexPixelDelta(baseline.Storm, x, y, red, green, blue))
                        .IsLessThanOrEqualTo(16);
                }
            }

            ParityEvidenceInputs.RequireUnchanged(sourceIdentity, CreateSourceIdentity());
            ParityEvidenceInputs.RequireUnchanged(
                fixtureIdentity, ParityEvidenceInputs.CreateFixtureIdentity(repository, fixtures));
            if (!string.Equals(
                packageSnapshot,
                JsonSerializer.Serialize(CreatePackageIdentity(pluginPath), EvidenceJsonOptions),
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Ptex quad reference runtime inputs changed during capture.");
            }
            foreach ((string scheme, List<string> evidence, StormOpenGlEvidence? openGl) in records)
            {
                WriteEvidence($"ptex-quad-{scheme}-storm-reference.txt", evidence);
                WriteJsonEvidence($"ptex-quad-{scheme}-storm-reference-provenance.json", new
                {
                    scope = "storm-only",
                    execution,
                    sourceIdentity,
                    fixtureIdentity,
                    packageIdentity,
                    stormOpenGl = openGl,
                });
            }
        }
        catch (DllNotFoundException exception)
        {
            SkipOrFail("Ptex quad Storm reference", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreatePtexQuadStage(string scheme)
    {
        string result = ReplacePtexQuadFragment(
            PtexReferenceStage,
            "(-0.9, -0.8, 0.1), (-0.1, -0.8, 0.1), (-0.5, -0.1, 0.1)",
            "(-0.9, -0.8, 0.1), (-0.1, -0.8, 0.1), (-0.1, -0.1, 0.1), (-0.9, -0.1, 0.1)");
        result = ReplacePtexQuadFragment(
            result,
            "(0.1, -0.8, 0.1), (0.9, -0.8, 0.1), (0.5, -0.1, 0.1)",
            "(0.1, -0.8, 0.1), (0.9, -0.8, 0.1), (0.9, -0.1, 0.1), (0.1, -0.1, 0.1)");
        result = ReplacePtexQuadFragment(
            result,
            "(-0.9, 0.1, 0.1), (-0.1, 0.1, 0.1), (-0.5, 0.8, 0.1)",
            "(-0.9, 0.1, 0.1), (-0.1, 0.1, 0.1), (-0.1, 0.8, 0.1), (-0.9, 0.8, 0.1)");
        result = ReplacePtexQuadFragment(
            result,
            "(0.1, 0.1, 0.1), (0.9, 0.1, 0.1), (0.5, 0.8, 0.1)",
            "(0.1, 0.1, 0.1), (0.9, 0.1, 0.1), (0.9, 0.8, 0.1), (0.1, 0.8, 0.1)");
        result = ReplacePtexQuadFragment(
            result, "int[] faceVertexCounts = [3, 3, 3, 3]", "int[] faceVertexCounts = [4, 4, 4, 4]");
        result = ReplacePtexQuadFragment(
            result,
            "int[] faceVertexIndices = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]",
            "int[] faceVertexIndices = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]");
        result = ReplacePtexQuadFragment(
            result, "uniform token subdivisionScheme = \"loop\"", $"uniform token subdivisionScheme = \"{scheme}\"");
        return ReplacePtexQuadFragment(result, "@tetrahedron.ptx@", "@quad-colors.ptx@");
    }

    private static string ReplacePtexQuadFragment(string stage, string original, string replacement)
    {
        int first = stage.IndexOf(original, StringComparison.Ordinal);
        if (first < 0 ||
            stage.IndexOf(original, first + original.Length, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException("Ptex quad reference stage fragment must occur exactly once.");
        }
        return stage.Replace(original, replacement, StringComparison.Ordinal);
    }
}
