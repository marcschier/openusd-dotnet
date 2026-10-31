// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkLinearCurveNativeTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "nonperiodic")]
    [Arguments(SilkGraphicsBackend.D3D12, "periodic")]
    [Arguments(SilkGraphicsBackend.D3D12, "pinned")]
    [Arguments(SilkGraphicsBackend.Vulkan, "nonperiodic")]
    [Arguments(SilkGraphicsBackend.Vulkan, "periodic")]
    [Arguments(SilkGraphicsBackend.Vulkan, "pinned")]
    public async Task LinearWrapsMatchIndependentSegmentsAcrossTime(SilkGraphicsBackend backend, string wrap)
    {
        using var fixture = new CurveFixture(wrap);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using OpenUsdSilkSession actual = OpenUsdSilkRuntime.Create(fixture.Plugins, fixture.ActualPath);
        using OpenUsdSilkSession expected = OpenUsdSilkRuntime.Create(fixture.Plugins, fixture.ReferencePath);
        using var actualRenderer = new SilkMeshRenderer(device);
        using var expectedRenderer = new SilkMeshRenderer(device);
        byte[]? first = null;
        int expectedEndpoints = wrap == "periodic" ? 8 : 6;

        for (int time = 0; time < 2; time++)
        {
            byte[] actualPixels = Capture(actual, device, actualRenderer, time);
            byte[] expectedPixels = Capture(expected, device, expectedRenderer, time);
            SilkMeshData actualMesh = actualRenderer.Scene.Meshes.Values.Single();
            SilkMeshData expectedMesh = expectedRenderer.Scene.Meshes.Values.Single();
            await Assert.That(actualMesh.TopologyKind).IsEqualTo(SilkTopologyKind.LineList);
            await Assert.That(actualMesh.Indices.Length).IsEqualTo(expectedEndpoints);
            await Assert.That(actualMesh.Points.Span.SequenceEqual(expectedMesh.Points.Span)).IsTrue();
            await Assert.That(actualMesh.Indices.Span.SequenceEqual(expectedMesh.Indices.Span)).IsTrue();
            await Assert.That(actualMesh.TriangleSubprims.Span.SequenceEqual(expectedMesh.TriangleSubprims.Span))
                .IsTrue();
            SilkVertexAttributeData actualWidths =
                actualMesh.Attributes.Single(attribute => attribute.Name == "widths");
            SilkVertexAttributeData expectedWidths =
                expectedMesh.Attributes.Single(attribute => attribute.Name == "widths");
            await Assert.That(actualWidths.Interpolation).IsEqualTo(SilkAttributeInterpolation.Vertex);
            await Assert.That(actualWidths.Data.Span.SequenceEqual(expectedWidths.Data.Span)).IsTrue();
            await Assert.That(actualPixels.SequenceEqual(expectedPixels)).IsTrue();
            int colors = Enumerable.Range(0, actualPixels.Length / 4)
                .Select(index => Convert.ToHexString(actualPixels.AsSpan(index * 4, 3))).Distinct().Count();
            await Assert.That(colors).IsGreaterThan(1)
                .Because("The independent segment comparison must contain visible lines.");
            if (first is not null)
            {
                await Assert.That(actualPixels.SequenceEqual(first)).IsFalse();
            }
            first = actualPixels;
        }

        actualRenderer.Dispose();
        expectedRenderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task WrapEditsAndInvalidTopologyRepublishAndRecover(SilkGraphicsBackend backend)
    {
        using var fixture = new CurveFixture("nonperiodic");
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.ActualPath);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession actual = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);

        byte[] open = Capture(actual, device, renderer, 0);
        SilkMeshData initial = renderer.Scene.Meshes.Values.Single();
        await Assert.That(initial.Indices.Length).IsEqualTo(6);
        ulong initialRevision = initial.TopologyRevision;
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetToken("wrap", "periodic"));
        byte[] closed = Capture(actual, device, renderer, 0);
        SilkMeshData periodic = renderer.Scene.Meshes.Values.Single();
        await Assert.That(periodic.Indices.Length).IsEqualTo(8);
        await Assert.That(periodic.TopologyRevision).IsGreaterThan(initialRevision);
        await Assert.That(closed.SequenceEqual(open)).IsFalse();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetInt32Array("curveVertexCounts", [1]));
        byte[] invalid = Capture(actual, device, renderer, 0);
        await Assert.That(renderer.Scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(invalid.SequenceEqual(closed)).IsFalse();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetInt32Array("curveVertexCounts", [4]));
        byte[] recovered = Capture(actual, device, renderer, 0);
        await Assert.That(recovered.SequenceEqual(closed)).IsTrue();
        await Assert.That(renderer.Scene.Meshes.Values.Single().Indices.Length).IsEqualTo(8);

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetToken("wrap", "nonperiodic"));
        byte[] reopened = Capture(actual, device, renderer, 0);
        await Assert.That(reopened.SequenceEqual(open)).IsTrue();
        await scheduler.InvokeAsync(stage => stage.RemovePrim("/Curve"));
        _ = Capture(actual, device, renderer, 0);
        await Assert.That(renderer.Scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(renderer.GpuResources.MeshValues.Count).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(fixture.ActualPath)).IsEqualTo(fixture.ActualText);

        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    private static byte[] Capture(
        OpenUsdSilkSession session,
        ISilkGraphicsDevice device,
        SilkMeshRenderer renderer,
        double time)
    {
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        return SilkFrameCapture.CaptureCore(
            session, device, renderer, 64, 64, RenderSettings.Default, time, camera).Rgba.ToArray();
    }

    private sealed class CurveFixture : IDisposable
    {
        private readonly string _root;

        internal CurveFixture(string wrap)
        {
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(plugins))
            {
                Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to a matched native runtime.");
                throw new InvalidOperationException("Skip.Test returned unexpectedly.");
            }
            Plugins = plugins;
            _root = Directory.CreateTempSubdirectory("silk-linear-curves-").FullName;
            ActualPath = Path.Combine(_root, "actual.usda");
            ReferencePath = Path.Combine(_root, "reference.usda");
            ActualText = Scene(wrap);
            File.WriteAllText(ActualPath, ActualText);
            File.WriteAllText(ReferencePath, Segments(wrap == "periodic"));
        }

        internal string Plugins { get; }
        internal string ActualPath { get; }
        internal string ReferencePath { get; }
        internal string ActualText { get; }

        public void Dispose() => Directory.Delete(_root, recursive: true);

        private static string Scene(string wrap) => $$"""
            #usda 1.0
            def BasisCurves "Curve" {
                uniform token type = "linear"
                uniform token wrap = "{{wrap}}"
                int[] curveVertexCounts = [4]
                point3f[] points.timeSamples = {
                    0: [(-0.7,-0.6,0), (0.7,-0.6,0), (0.7,0.6,0), (-0.7,0.6,0)],
                    1: [(-0.6,-0.4,0), (0.6,-0.7,0), (0.55,0.7,0), (-0.55,0.5,0)]
                }
                float[] widths = [0.1, 0.2, 0.3, 0.4] (interpolation = "vertex")
                color3f[] primvars:displayColor = [(1, 0.5, 0.1)] (interpolation = "constant")
            }
            """;

        private static string Segments(bool close)
        {
            string counts = close ? "[2,2,2,2]" : "[2,2,2]";
            string first = close
                ? "[(-0.7,-0.6,0), (0.7,-0.6,0), (0.7,-0.6,0), (0.7,0.6,0), " +
                    "(0.7,0.6,0), (-0.7,0.6,0), (-0.7,0.6,0), (-0.7,-0.6,0)]"
                : "[(-0.7,-0.6,0), (0.7,-0.6,0), (0.7,-0.6,0), (0.7,0.6,0), " +
                    "(0.7,0.6,0), (-0.7,0.6,0)]";
            string second = close
                ? "[(-0.6,-0.4,0), (0.6,-0.7,0), (0.6,-0.7,0), (0.55,0.7,0), " +
                    "(0.55,0.7,0), (-0.55,0.5,0), (-0.55,0.5,0), (-0.6,-0.4,0)]"
                : "[(-0.6,-0.4,0), (0.6,-0.7,0), (0.6,-0.7,0), (0.55,0.7,0), " +
                    "(0.55,0.7,0), (-0.55,0.5,0)]";
            string widths = close ? "[0.1,0.2,0.2,0.3,0.3,0.4,0.4,0.1]" : "[0.1,0.2,0.2,0.3,0.3,0.4]";
            return $$"""
                #usda 1.0
                def BasisCurves "Curve" {
                    uniform token type = "linear"
                    uniform token wrap = "segmented"
                    int[] curveVertexCounts = {{counts}}
                    point3f[] points.timeSamples = {0: {{first}}, 1: {{second}}}
                    float[] widths = {{widths}} (interpolation = "vertex")
                    color3f[] primvars:displayColor = [(1, 0.5, 0.1)] (interpolation = "constant")
                }
                """;
        }
    }
}
