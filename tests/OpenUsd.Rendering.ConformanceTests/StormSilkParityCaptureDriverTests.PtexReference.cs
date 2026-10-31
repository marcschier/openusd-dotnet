// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Text.Json;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class StormSilkParityCaptureDriverTests
{
    [Test]
    public async Task PtexReferenceRespondsToAuthoredFaceOffsets()
    {
        if (!StormGlContextFactory.IsCurrentPlatformSupported)
        {
            SkipOrFail("Ptex Storm reference", "The current platform has no Storm OpenGL context.");
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
            SkipOrFail("Ptex Storm reference", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }
        string repository = FindRepositoryRoot()
            ?? throw new InvalidOperationException("Ptex reference evidence requires the repository inputs.");
        ParitySourceIdentity sourceIdentity = CreateSourceIdentity();
        ParitySourceIdentity fixtureIdentity = ParityEvidenceInputs.CreateFixtureIdentity(
            repository, ["test-assets\\native-profile\\ptex\\tetrahedron.ptx"]);
        ParityExecutionIdentity execution = ParityExecutionIdentity.Read(Environment.GetEnvironmentVariable);
        object packageIdentity = CreatePackageIdentity(pluginPath);
        string packageSnapshot = JsonSerializer.Serialize(packageIdentity, EvidenceJsonOptions);
        string directory = Path.Combine(Path.GetTempPath(), $"openusd-ptex-reference-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(
                ResolveTestAsset(Path.Combine("native-profile", "ptex", "tetrahedron.ptx")),
                Path.Combine(directory, "tetrahedron.ptx"));
            string stage = Path.Combine(directory, "reference.usda");
            File.WriteAllText(stage, PtexReferenceStage);
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
            ParityCaptureSet shifted = await ParityCaptureDriver.CaptureAsync(
                input with { TimeCode = 2 },
                StormGlContextFactory.CreateForCurrentPlatform(),
                []).ConfigureAwait(false);
            var separate = new List<ParityImage>();
            for (int time = 4; time <= 7; time++)
            {
                ParityCaptureSet capture = await ParityCaptureDriver.CaptureAsync(
                    input with { TimeCode = time },
                    StormGlContextFactory.CreateForCurrentPlatform(),
                    []).ConfigureAwait(false);
                separate.Add(capture.Storm);
            }
            string reorderedStage = Path.Combine(directory, "reordered.usda");
            File.WriteAllText(
                reorderedStage,
                ReplacePtexStageFragment(
                    "int[] faceVertexIndices = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]",
                    "int[] faceVertexIndices = [9, 10, 11, 0, 1, 2, 3, 4, 5, 6, 7, 8]"));
            ParityCaptureSet reordered = await ParityCaptureDriver.CaptureAsync(
                input with { StagePath = reorderedStage },
                StormGlContextFactory.CreateForCurrentPlatform(),
                []).ConfigureAwait(false);
            string indexedStage = Path.Combine(directory, "indexed.usda");
            File.WriteAllText(
                indexedStage,
                ReplacePtexStageFragment(
                    """
        int[] primvars:ptexFaceOffset.timeSamples = {
            1: [0, 0, 0, 0],
            2: [1, 1, 1, -3],
            4: [1, 0, 0, 0],
            5: [0, 1, 0, 0],
            6: [0, 0, 1, 0],
            7: [0, 0, 0, -3]
        }
""",
                    """
        int[] primvars:ptexFaceOffset = [1, -3]
        int[] primvars:ptexFaceOffset:indices = [0, 0, 0, 1]
"""));
            ParityCaptureSet indexed = await ParityCaptureDriver.CaptureAsync(
                input with { StagePath = indexedStage },
                StormGlContextFactory.CreateForCurrentPlatform(),
                []).ConfigureAwait(false);
            string faceIndexStage = Path.Combine(directory, "face-index.usda");
            File.WriteAllText(
                faceIndexStage,
                ReplacePtexStageFragment(
                    "int[] faceVertexCounts = [3, 3, 3, 3]",
                    """
        int[] faceVertexCounts = [3, 3, 3, 3]
        int[] primvars:ptexFaceIndex = [1, 2, 3, 0] (
            interpolation = "uniform"
        )
""".TrimStart()));
            ParityCaptureSet faceIndex = await ParityCaptureDriver.CaptureAsync(
                input with { StagePath = faceIndexStage },
                StormGlContextFactory.CreateForCurrentPlatform(),
                []).ConfigureAwait(false);
            string missingStage = Path.Combine(directory, "missing.usda");
            File.WriteAllText(
                missingStage,
                ReplacePtexStageFragment("@tetrahedron.ptx@", "@missing.ptx@"));
            ParityCaptureSet missing = await ParityCaptureDriver.CaptureAsync(
                input with { StagePath = missingStage },
                StormGlContextFactory.CreateForCurrentPlatform(),
                []).ConfigureAwait(false);
            ImageDelta reorderedDelta = ImageDelta.Compare(shifted.Storm, reordered.Storm);
            ImageDelta indexedDelta = ImageDelta.Compare(shifted.Storm, indexed.Storm);
            ImageDelta faceIndexBaselineDelta = ImageDelta.Compare(baseline.Storm, faceIndex.Storm);
            ImageDelta faceIndexShiftedDelta = ImageDelta.Compare(shifted.Storm, faceIndex.Storm);
            ImageDelta missingDelta = ImageDelta.Compare(baseline.Storm, missing.Storm);
            int[,] changedByFace = new int[4, 4];
            for (int changedFace = 0; changedFace < 4; changedFace++)
            {
                for (int face = 0; face < 4; face++)
                {
                    changedByFace[changedFace, face] =
                        CountPtexChangedPixels(baseline.Storm, separate[changedFace], face);
                }
            }
            var evidence = new List<string>
            {
                $"baseline nonblack={CountNonBlackPixels(baseline.Storm)}",
                $"baseline hash={Hash(baseline.Storm)}",
                $"offset hash={Hash(shifted.Storm)}",
                $"reordered hash={Hash(reordered.Storm)}",
                $"indexed hash={Hash(indexed.Storm)}",
                $"authored face-index hash={Hash(faceIndex.Storm)}",
                $"missing hash={Hash(missing.Storm)}",
                $"offset vs reordered max={reorderedDelta.MaximumChannelDelta} mean={reorderedDelta.MeanChannelDelta}",
                $"offset vs indexed max={indexedDelta.MaximumChannelDelta} mean={indexedDelta.MeanChannelDelta}",
                $"authored face-index vs baseline max={faceIndexBaselineDelta.MaximumChannelDelta} " +
                    $"mean={faceIndexBaselineDelta.MeanChannelDelta}",
                $"authored face-index vs offset max={faceIndexShiftedDelta.MaximumChannelDelta} " +
                    $"mean={faceIndexShiftedDelta.MeanChannelDelta}",
                $"baseline vs missing max={missingDelta.MaximumChannelDelta} mean={missingDelta.MeanChannelDelta}",
            };
            if (baseline.OpenGlEvidence is { } openGl)
            {
                evidence.Add($"OpenGL vendor={openGl.Vendor} renderer={openGl.Renderer} version={openGl.Version}");
                evidence.Add($"OpenGL module={openGl.LoadedOpenGl32} sha256={openGl.LoadedOpenGl32Sha256}");
            }
            (int X, int Y, int Red, int Green, int Blue)[] samples =
            [
                (40, 100, 255, 255, 255),
                (120, 100, 255, 0, 0),
                (40, 43, 0, 255, 0),
                (120, 43, 0, 0, 255),
            ];
            for (int face = 0; face < samples.Length; face++)
            {
                (int x, int y, _, _, _) = samples[face];
                int offset = ((y * Width) + x) * ParityImage.BytesPerPixel;
                evidence.Add(
                    $"sample {x},{y}: baseline={Convert.ToHexString(baseline.Storm.Rgba.Span.Slice(offset, 4))} " +
                    $"offset={Convert.ToHexString(shifted.Storm.Rgba.Span.Slice(offset, 4))}");
                evidence.Add(
                    $"single-face {face} changed pixels: " +
                    string.Join(",", Enumerable.Range(0, 4).Select(region => changedByFace[face, region])));
            }
            ParityEvidenceInputs.RequireUnchanged(sourceIdentity, CreateSourceIdentity());
            ParityEvidenceInputs.RequireUnchanged(
                fixtureIdentity,
                ParityEvidenceInputs.CreateFixtureIdentity(
                    repository, ["test-assets\\native-profile\\ptex\\tetrahedron.ptx"]));
            if (!string.Equals(
                packageSnapshot,
                JsonSerializer.Serialize(CreatePackageIdentity(pluginPath), EvidenceJsonOptions),
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Ptex reference runtime inputs changed during capture.");
            }
            WriteEvidence("ptex-storm-reference.txt", evidence);
            WriteJsonEvidence("ptex-storm-reference-provenance.json", new
            {
                scope = "storm-only",
                execution,
                sourceIdentity,
                fixtureIdentity,
                packageIdentity,
                stormOpenGl = baseline.OpenGlEvidence,
            });

            await Assert.That(CountNonBlackPixels(baseline.Storm)).IsGreaterThan(3_000);
            await Assert.That(reorderedDelta.MaximumChannelDelta).IsLessThanOrEqualTo(8);
            await Assert.That(reorderedDelta.MeanChannelDelta).IsLessThanOrEqualTo(0.25);
            await Assert.That(indexedDelta.MaximumChannelDelta).IsLessThanOrEqualTo(8);
            await Assert.That(indexedDelta.MeanChannelDelta).IsLessThanOrEqualTo(0.25);
            await Assert.That(faceIndexBaselineDelta.MaximumChannelDelta).IsLessThanOrEqualTo(8)
                .Because("This Storm fixture does not use the authored uniform ptexFaceIndex as a face remap.");
            await Assert.That(faceIndexBaselineDelta.MeanChannelDelta).IsLessThanOrEqualTo(0.25);
            await Assert.That(faceIndexShiftedDelta.MeanChannelDelta).IsGreaterThan(1.0);
            await Assert.That(missingDelta.MeanChannelDelta).IsGreaterThan(1.0);
            for (int face = 0; face < samples.Length; face++)
            {
                (int x, int y, int red, int green, int blue) = samples[face];
                await Assert.That(PtexPixelDelta(baseline.Storm, x, y, red, green, blue))
                    .IsLessThanOrEqualTo(16)
                    .Because($"The baseline must sample the expected color on face {face}.");
                await Assert.That(PtexPixelDelta(missing.Storm, x, y, 0, 0, 0))
                    .IsLessThanOrEqualTo(16)
                    .Because("A missing Ptex file must not masquerade as the reference.");
                for (int changedFace = 0; changedFace < separate.Count; changedFace++)
                {
                    if (face == changedFace)
                    {
                        await Assert.That(changedByFace[changedFace, face]).IsGreaterThan(300);
                        await Assert.That(PtexPixelDelta(baseline.Storm, separate[changedFace], x, y))
                            .IsGreaterThan(48);
                        await Assert.That(PtexPixelDelta(shifted.Storm, separate[changedFace], x, y))
                            .IsLessThanOrEqualTo(8);
                    }
                    else
                    {
                        await Assert.That(changedByFace[changedFace, face]).IsLessThanOrEqualTo(10);
                        await Assert.That(PtexPixelDelta(baseline.Storm, separate[changedFace], x, y))
                            .IsLessThanOrEqualTo(8);
                    }
                }
            }
        }
        catch (DllNotFoundException exception)
        {
            SkipOrFail("Ptex Storm reference", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ReplacePtexStageFragment(string original, string replacement)
    {
        if (!PtexReferenceStage.Contains(original, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ptex reference stage fragment was not found.");
        }
        return PtexReferenceStage.Replace(original, replacement, StringComparison.Ordinal);
    }

    private static int PtexPixelDelta(ParityImage first, ParityImage second, int x, int y)
    {
        int offset = ((y * first.Width) + x) * ParityImage.BytesPerPixel;
        ReadOnlySpan<byte> pixels = second.Rgba.Span;
        return PtexPixelDelta(first, x, y, pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }

    private static int PtexPixelDelta(ParityImage image, int x, int y, int red, int green, int blue)
    {
        int offset = ((y * image.Width) + x) * ParityImage.BytesPerPixel;
        ReadOnlySpan<byte> pixels = image.Rgba.Span;
        return Math.Max(
            Math.Abs(pixels[offset] - red),
            Math.Max(Math.Abs(pixels[offset + 1] - green), Math.Abs(pixels[offset + 2] - blue)));
    }

    private static int CountPtexChangedPixels(ParityImage baseline, ParityImage candidate, int face)
    {
        int xStart = (face % 2) * (baseline.Width / 2);
        int yStart = (face < 2 ? 1 : 0) * (baseline.Height / 2);
        int changed = 0;
        for (int y = yStart; y < yStart + (baseline.Height / 2); y++)
        {
            for (int x = xStart; x < xStart + (baseline.Width / 2); x++)
            {
                if (PtexPixelDelta(baseline, candidate, x, y) > 8)
                {
                    changed++;
                }
            }
        }
        return changed;
    }

    private const string PtexReferenceStage = """
#usda 1.0
(
    defaultPrim = "World"
    startTimeCode = 1
    endTimeCode = 7
    upAxis = "Y"
)

def Xform "World"
{
    def Mesh "Faces" (
        prepend apiSchemas = ["MaterialBindingAPI"]
    )
    {
        rel material:binding = </World/Looks/Ptex>
        uniform bool doubleSided = 1
        uniform token subdivisionScheme = "loop"
        point3f[] points = [
            (-0.9, -0.8, 0.1), (-0.1, -0.8, 0.1), (-0.5, -0.1, 0.1),
            (0.1, -0.8, 0.1), (0.9, -0.8, 0.1), (0.5, -0.1, 0.1),
            (-0.9, 0.1, 0.1), (-0.1, 0.1, 0.1), (-0.5, 0.8, 0.1),
            (0.1, 0.1, 0.1), (0.9, 0.1, 0.1), (0.5, 0.8, 0.1)
        ]
        int[] faceVertexCounts = [3, 3, 3, 3]
        int[] faceVertexIndices = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]
        int[] primvars:ptexFaceOffset (
            interpolation = "uniform"
        )
        int[] primvars:ptexFaceOffset.timeSamples = {
            1: [0, 0, 0, 0],
            2: [1, 1, 1, -3],
            4: [1, 0, 0, 0],
            5: [0, 1, 0, 0],
            6: [0, 0, 1, 0],
            7: [0, 0, 0, -3]
        }
    }

    def Scope "Looks"
    {
        def Material "Ptex"
        {
            token outputs:surface.connect = </World/Looks/Ptex/Surface.outputs:surface>
            def Shader "Surface"
            {
                uniform token info:id = "UsdPreviewSurface"
                color3f inputs:diffuseColor = (0, 0, 0)
                color3f inputs:emissiveColor.connect = </World/Looks/Ptex/Texture.outputs:rgb>
                token outputs:surface
            }
            def Shader "Texture"
            {
                uniform token info:id = "HwPtexTexture_1"
                asset inputs:file = @tetrahedron.ptx@
                float3 outputs:rgb
            }
        }
    }
}
""";
}
