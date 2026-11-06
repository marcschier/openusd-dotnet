// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class StormSilkParityCaptureDriverTests
{
    [Test]
    [Arguments("none", true, false)]
    [Arguments("none", true, true)]
    [Arguments("none", false, true)]
    [Arguments("bilinear", true, false)]
    [Arguments("bilinear", true, true)]
    [Arguments("catmullClark", true, false)]
    [Arguments("catmullClark", true, true)]
    [Arguments("catmullClark", false, false)]
    [Arguments("catmullClark", false, true)]
    [Arguments("loop", true, false)]
    [Arguments("loop", true, true)]
    [Arguments("loop", false, true)]
    public async Task StormCoarseNormalsFollowSchemeAndPointIdentity(
        string scheme, bool sharedPoints, bool faceVarying)
    {
        bool smooth = (scheme is "catmullClark" or "loop") && sharedPoints;
        string expectedNormals = smooth ? CoarseMeshNormalFixture.SharedNormals : sharedPoints
            ? CoarseMeshNormalFixture.FlatCornerNormals : CoarseMeshNormalFixture.SeparateNormals;
        string incorrectNormals = smooth ? CoarseMeshNormalFixture.FlatCornerNormals : sharedPoints
            ? CoarseMeshNormalFixture.SharedNormals : CoarseMeshNormalFixture.JoinedSeparateNormals;
        string label = $"coarse-normals-{scheme}-{(sharedPoints ? "shared" : "disconnected")}-" +
            (faceVarying ? "seamed" : "plain");
        await AssertStormCoarseNormals(
            CoarseMeshNormalFixture.Mesh(scheme, sharedPoints, faceVarying, string.Empty),
            CoarseMeshNormalFixture.Mesh("none", sharedPoints, faceVarying, expectedNormals),
            CoarseMeshNormalFixture.Mesh("none", sharedPoints, faceVarying, incorrectNormals),
            label).ConfigureAwait(false);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StormCoarseNgonNormalsUseFaceAdjacency(bool faceVarying)
    {
        await AssertStormCoarseNormals(
            CoarseMeshNormalFixture.NgonMesh("catmullClark", faceVarying, string.Empty),
            CoarseMeshNormalFixture.NgonMesh("none", faceVarying, CoarseMeshNormalFixture.NgonNormals),
            CoarseMeshNormalFixture.NgonMesh(
                "none", faceVarying, CoarseMeshNormalFixture.NgonTriangulatedNormals),
            $"coarse-ngon-normals-{(faceVarying ? "seamed" : "plain")}").ConfigureAwait(false);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StormNonplanarNgonSmoothNormalsUseEachOriginalFaceCorner(bool faceVarying)
    {
        await AssertStormCoarseNormals(
            CoarseMeshNormalFixture.NgonMesh("catmullClark", faceVarying, string.Empty, skewed: true),
            CoarseMeshNormalFixture.NgonMesh(
                "none", faceVarying, CoarseMeshNormalFixture.SkewedNgonCornerNormals, skewed: true),
            CoarseMeshNormalFixture.NgonMesh(
                "none", faceVarying, CoarseMeshNormalFixture.SkewedNgonFaceAreaNormals, skewed: true),
            $"nonplanar-smooth-{(faceVarying ? "seamed" : "plain")}").ConfigureAwait(false);
    }

    [Test]
    [Arguments("catmullClark", false)]
    [Arguments("catmullClark", true)]
    [Arguments("loop", false)]
    [Arguments("loop", true)]
    public async Task StormCoarseSmoothNormalsIncludeHoleFaces(string scheme, bool faceVarying)
    {
        await AssertStormCoarseNormals(
            CoarseMeshNormalFixture.Mesh(
                scheme, true, faceVarying, string.Empty, holeSecondFace: true),
            CoarseMeshNormalFixture.Mesh(
                "none", true, faceVarying, CoarseMeshNormalFixture.SharedNormals, holeSecondFace: true),
            CoarseMeshNormalFixture.Mesh(
                "none", true, faceVarying, CoarseMeshNormalFixture.FlatCornerNormals, holeSecondFace: true),
            $"coarse-hole-normals-{scheme}-{(faceVarying ? "seamed" : "plain")}").ConfigureAwait(false);
    }

    [Test]
    [Arguments("none", false)]
    [Arguments("none", true)]
    [Arguments("bilinear", false)]
    [Arguments("bilinear", true)]
    public async Task StormNonplanarPolygonFlatNormalsUseEachEmittedTriangle(string scheme, bool faceVarying)
    {
        // At ee47c679, hdSt/mesh.cpp selects geometric normals for this coarse
        // unforced draw; meshNormal.glslfx crosses each emitted triangle.
        string? withoutFaceVaryingColor = faceVarying
            ? CoarseMeshNormalFixture.NgonMesh(
                "none", true, CoarseMeshNormalFixture.SkewedNgonTriangulatedFlatNormals,
                skewed: true, triangulated: true)
            : null;
        await AssertStormCoarseNormals(
            CoarseMeshNormalFixture.NgonMesh(
                scheme, faceVarying, string.Empty, skewed: true, includeFaceVaryingColor: faceVarying),
            CoarseMeshNormalFixture.NgonMesh(
                "none", faceVarying, CoarseMeshNormalFixture.SkewedNgonTriangulatedFlatNormals,
                skewed: true, triangulated: true, includeFaceVaryingColor: faceVarying),
            CoarseMeshNormalFixture.NgonMesh(
                "none", faceVarying, CoarseMeshNormalFixture.SkewedNgonFlatNormals,
                skewed: true, includeFaceVaryingColor: faceVarying),
            $"nonplanar-flat-{scheme}-{(faceVarying ? "seamed" : "plain")}",
            withoutFaceVaryingColor).ConfigureAwait(false);
    }

    private static async Task AssertStormCoarseNormals(
        string actualGeometry, string expectedGeometry, string incorrectGeometry, string label,
        string? withoutFaceVaryingColorGeometry = null)
    {
        if (!StormGlContextFactory.IsCurrentPlatformSupported)
        {
            SkipOrFail("coarse normal Storm reference", "The current platform has no Storm OpenGL context.");
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
            SkipOrFail("coarse normal Storm reference", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }

        string directory = Path.Combine(Path.GetTempPath(), $"openusd-coarse-normal-reference-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string actual = Path.Combine(directory, "actual.usda");
            string reference = Path.Combine(directory, "reference.usda");
            string incorrect = Path.Combine(directory, "incorrect.usda");
            File.WriteAllText(actual, "#usda 1.0\n" + actualGeometry);
            // Storm selects generated normals ahead of authored subdivision normals.
            File.WriteAllText(reference, "#usda 1.0\n" + expectedGeometry);
            File.WriteAllText(incorrect, "#usda 1.0\n" + incorrectGeometry);
            var camera = new CameraState(
                Matrix4x4.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.UnitY),
                Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
            var input = new ParityCaptureInput(
                pluginPath, actual, Width, Height, 1, camera,
                new SilkColor(0, 0, 0, 1), OpenUsdStormRuntime.Headlight, UseSceneLights: false);
            ParityCaptureSet captured = await ParityCaptureDriver.CaptureAsync(
                input, StormGlContextFactory.CreateForCurrentPlatform(), []).ConfigureAwait(false);
            ParityCaptureSet expected = await ParityCaptureDriver.CaptureAsync(
                input with { StagePath = reference },
                StormGlContextFactory.CreateForCurrentPlatform(), []).ConfigureAwait(false);
            ParityCaptureSet wrong = await ParityCaptureDriver.CaptureAsync(
                input with { StagePath = incorrect },
                StormGlContextFactory.CreateForCurrentPlatform(), []).ConfigureAwait(false);

            ImageDelta expectedDelta = ImageDelta.Compare(captured.Storm, expected.Storm);
            ImageDelta incorrectDelta = ImageDelta.Compare(expected.Storm, wrong.Storm);
            ImageDelta? faceVaryingControlDelta = null;
            string? faceVaryingControlHash = null;
            if (withoutFaceVaryingColorGeometry is not null)
            {
                string withoutColor = Path.Combine(directory, "without-face-varying-color.usda");
                File.WriteAllText(withoutColor, "#usda 1.0\n" + withoutFaceVaryingColorGeometry);
                ParityCaptureSet without = await ParityCaptureDriver.CaptureAsync(
                    input with { StagePath = withoutColor },
                    StormGlContextFactory.CreateForCurrentPlatform(), []).ConfigureAwait(false);
                faceVaryingControlDelta = ImageDelta.Compare(expected.Storm, without.Storm);
                faceVaryingControlHash = Hash(without.Storm);
            }
            int visiblePixels = CountNonBlackPixels(captured.Storm);
            string metrics = FormattableString.Invariant(
                $"visible={visiblePixels} expected-max={expectedDelta.MaximumChannelDelta} ") +
                FormattableString.Invariant($"expected-mean={expectedDelta.MeanChannelDelta:F4} ") +
                FormattableString.Invariant($"incorrect-mean={incorrectDelta.MeanChannelDelta:F4}");
            if (faceVaryingControlDelta is { } faceVaryingDelta)
            {
                metrics += FormattableString.Invariant(
                    $" face-varying-control-mean={faceVaryingDelta.MeanChannelDelta:F4}");
            }
            var evidence = new List<string>
            {
                metrics,
                $"actual hash={Hash(captured.Storm)}",
                $"reference hash={Hash(expected.Storm)}",
                $"incorrect hash={Hash(wrong.Storm)}",
            };
            if (faceVaryingControlHash is not null)
            {
                evidence.Add($"without face-varying color hash={faceVaryingControlHash}");
            }
            if (captured.OpenGlEvidence is { } openGl)
            {
                evidence.Add($"OpenGL vendor={openGl.Vendor} renderer={openGl.Renderer} version={openGl.Version}");
                evidence.Add($"OpenGL module={openGl.LoadedOpenGl32} sha256={openGl.LoadedOpenGl32Sha256}");
            }
            Console.WriteLine($"{label}: {metrics}");
            WriteEvidence(label + "-storm-reference.txt", evidence);
            await Assert.That(visiblePixels).IsGreaterThan(500);
            await Assert.That(incorrectDelta.MeanChannelDelta).IsGreaterThan(0.5);
            if (faceVaryingControlDelta is { } colorDelta)
            {
                await Assert.That(colorDelta.MeanChannelDelta).IsGreaterThan(0.5);
            }
            await Assert.That(expectedDelta.MaximumChannelDelta).IsLessThanOrEqualTo(8);
            await Assert.That(expectedDelta.MeanChannelDelta).IsLessThanOrEqualTo(0.25);
        }
        catch (DllNotFoundException exception)
        {
            SkipOrFail("coarse normal Storm reference", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
