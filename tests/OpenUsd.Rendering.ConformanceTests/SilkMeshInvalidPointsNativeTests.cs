// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkMeshInvalidPointsNativeTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task BlockedMeshPointsRetireGeometryAndRecover(SilkGraphicsBackend backend)
    {
        using Fixture fixture = Create(Mesh);
        using Fixture emptyFixture = Create(string.Empty);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkSession empty = emptyFixture.Open();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using var emptyRenderer = new SilkMeshRenderer(device);

        byte[] background = Capture(empty, device, emptyRenderer);
        byte[] initial = Capture(session, device, renderer);
        await Assert.That(initial.SequenceEqual(background)).IsFalse();
        await Assert.That(renderer.Scene.Meshes.Values.Single().Points.Length).IsEqualTo(9);

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").GetAttribute("points").BlockValue());
        byte[] blocked = Capture(session, device, renderer);
        await Assert.That(renderer.Scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(renderer.GpuResources.MeshValues.Count).IsEqualTo(0);
        await Assert.That(blocked.SequenceEqual(background)).IsTrue();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", [new(-0.25f, -0.5f, 0), new(0.25f, -0.5f, 0), new(0, 0.25f, 0)]));
        byte[] recovered = Capture(session, device, renderer);
        await Assert.That(renderer.Scene.Meshes.Values.Single().Points.Span.SequenceEqual(
            new float[] { -0.25f, -0.5f, 0, 0.25f, -0.5f, 0, 0, 0.25f, 0 })).IsTrue();
        await Assert.That(recovered.SequenceEqual(initial)).IsFalse();
        await Assert.That(recovered.SequenceEqual(background)).IsFalse();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points", OriginalPoints));
        await Assert.That(Capture(session, device, renderer).SequenceEqual(initial)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
        renderer.Dispose();
        emptyRenderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task BlockedMeshPrototypeRetiresEveryInstanceAndRecovers(SilkGraphicsBackend backend)
    {
        using Fixture fixture = Create(InstancedMesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);

        byte[] initial = Capture(session, device, renderer);
        await Assert.That(renderer.Scene.Meshes.Values.Count(
            mesh => mesh.Path.StartsWith("/Instances/Mesh/", StringComparison.Ordinal))).IsEqualTo(2);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Instances/Mesh").GetAttribute("points").BlockValue());
        byte[] blocked = Capture(session, device, renderer);
        await Assert.That(renderer.Scene.Meshes.Values.Any(
            mesh => mesh.Path.StartsWith("/Instances/Mesh/", StringComparison.Ordinal))).IsFalse();
        await Assert.That(renderer.Scene.Meshes.Values.Any(
            mesh => mesh.Path.StartsWith("/Instances/Other/", StringComparison.Ordinal))).IsTrue();
        await Assert.That(blocked.SequenceEqual(initial)).IsFalse();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Instances/Mesh").SetVec3fArray("points", OriginalPoints));
        byte[] recovered = Capture(session, device, renderer);
        await Assert.That(renderer.Scene.Meshes.Values.Count(
            mesh => mesh.Path.StartsWith("/Instances/Mesh/", StringComparison.Ordinal))).IsEqualTo(2);
        await Assert.That(recovered.SequenceEqual(initial)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + InstancedMesh);
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BoundedBlockedPointsRefuseWithoutReplacingRetainedGeometry(bool instanced)
    {
        string geometry = instanced ? InstancedMesh : Mesh;
        string path = instanced ? "/Instances/Mesh" : "/Mesh";
        using Fixture fixture = Create(geometry);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkPage initial = session.Sync(16, 16, Options);
        using var scene = new SilkSceneState();
        scene.Apply(initial);
        var expected = scene.Meshes.Values
            .OrderBy(static mesh => mesh.Path, StringComparer.Ordinal)
            .Select(static mesh => (mesh.Path, Points: mesh.Points.ToArray(), Indices: mesh.Indices.ToArray()))
            .ToArray();
        await Assert.That(expected.Length).IsEqualTo(instanced ? 3 : 1);
        ulong reserved = initial.MeshPreparationUsage!.ReservedBytes;
        await Assert.That(reserved).IsGreaterThan(0ul);

        await scheduler.InvokeAsync(stage => stage.GetPrim(path).GetAttribute("points").BlockValue());
        await Assert.That(() => session.Sync(16, 16, Options))
            .Throws<OpenUsdSilkException>()
            .WithMessageContaining("requires resolved float3 or double3 points");
        using var retained = new SilkSceneState();
        retained.Apply(initial);
        await AssertGeometry(expected, retained);
        await AssertGeometry(expected, scene);

        await scheduler.InvokeAsync(stage => stage.GetPrim(path).SetVec3fArray("points", OriginalPoints));
        using OpenUsdSilkPage retry = session.Sync(16, 16, Options);
        await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsEqualTo(reserved);
        scene.Apply(retry);
        await AssertGeometry(expected, scene);
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + geometry);
    }

    [Test]
    [Arguments(false, false, false, true, false)]
    [Arguments(true, false, false, true, false)]
    [Arguments(false, true, false, true, false)]
    [Arguments(true, true, false, true, false)]
    [Arguments(false, false, true, true, false)]
    [Arguments(true, false, true, true, false)]
    [Arguments(false, true, true, true, false)]
    [Arguments(false, true, false, false, false)]
    [Arguments(true, true, false, false, false)]
    [Arguments(false, false, true, false, false)]
    [Arguments(true, false, true, false, false)]
    [Arguments(true, true, true, false, false)]
    [Arguments(true, true, true, false, true)]
    public async Task MeshRetiresAndRecoversAttributesAfterPointsAreRemoved(
        bool empty, bool editNormalsWhileRetired, bool editUvsWhileRetired, bool refined, bool bounded)
    {
        using Fixture fixture = Create(RefinedMesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        RenderComplexity complexity = refined ? RenderComplexity.Medium : RenderComplexity.Low;
        using OpenUsdSilkPage initial = SyncMesh(session, complexity, bounded);
        scene.Apply(initial);
        if (bounded)
        {
            await Assert.That(initial.MeshPreparationUsage!.ReservedBytes).IsGreaterThan(0ul);
        }
        float[] expected = scene.Meshes.Values.Single().Points.ToArray();
        uint[] expectedIndices = scene.Meshes.Values.Single().Indices.ToArray();
        float[] initialNormals = scene.Meshes.Values.Single().AuthoredNormals.ToArray();
        float[] expectedNormals = initialNormals;
        SilkVertexAttributeData initialUv = scene.Meshes.Values.Single().FindTexCoord("st")!;
        float[] initialUvValues = initialUv.Data.ToArray();
        float[] expectedUv = initialUvValues;
        await Assert.That(expected.Length).IsGreaterThan(refined ? 12 : 11);
        await Assert.That(expectedNormals.Length).IsEqualTo(expected.Length);
        await Assert.That(expectedNormals.Distinct().Count()).IsGreaterThan(2);
        await Assert.That(expectedUv.Length).IsGreaterThan(7);

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            if (empty)
            {
                mesh.SetVec3fArray("points", []);
            }
            else
            {
                mesh.GetAttribute("points").BlockValue();
            }
        });
        using OpenUsdSilkPage blocked = SyncMesh(session, complexity, bounded);
        scene.Apply(blocked);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        using var retained = new SilkSceneState();
        retained.Apply(initial);
        await Assert.That(retained.Meshes.Values.Single().Points.Span.SequenceEqual(expected)).IsTrue();
        await Assert.That(retained.Meshes.Values.Single().Indices.Span.SequenceEqual(expectedIndices)).IsTrue();
        await Assert.That(retained.Meshes.Values.Single().AuthoredNormals.Span.SequenceEqual(initialNormals)).IsTrue();
        await Assert.That(retained.Meshes.Values.Single().FindTexCoord("st")!.Data.Span.SequenceEqual(initialUvValues))
            .IsTrue();

        if (editNormalsWhileRetired)
        {
            await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
                "normals", FlatNormals));
            using OpenUsdSilkPage normalEdit = SyncMesh(session, complexity, bounded);
            scene.Apply(normalEdit);
            await Assert.That(scene.Meshes.Count).IsEqualTo(0);

            expectedNormals = new float[expected.Length];
            for (int i = 2; i < expectedNormals.Length; i += 3)
            {
                expectedNormals[i] = 1;
            }
        }
        if (editUvsWhileRetired)
        {
            await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec2fArray(
                "primvars:st", HalfUvs));
            using OpenUsdSilkPage uvEdit = SyncMesh(session, complexity, bounded);
            scene.Apply(uvEdit);
            await Assert.That(scene.Meshes.Count).IsEqualTo(0);
            expectedUv = initialUvValues.Select(static value => value * 0.5f).ToArray();
        }

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", QuadPoints));
        using OpenUsdSilkPage retry = SyncMesh(session, complexity, bounded);
        scene.Apply(retry);
        if (bounded)
        {
            await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsGreaterThan(0ul);
        }
        await Assert.That(scene.Meshes.Values.Single().Points.Span.SequenceEqual(expected)).IsTrue();
        await Assert.That(scene.Meshes.Values.Single().Indices.Span.SequenceEqual(expectedIndices)).IsTrue();
        await Assert.That(scene.Meshes.Values.Single().AuthoredNormals.Span.SequenceEqual(expectedNormals)).IsTrue();
        await Assert.That(scene.Meshes.Values.Single().FindTexCoord("st")!.Data.Span.SequenceEqual(expectedUv))
            .IsTrue();

        if (editNormalsWhileRetired)
        {
            await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
                "normals", OriginalNormals));
            using OpenUsdSilkPage normalRestore = SyncMesh(session, complexity, bounded);
            scene.Apply(normalRestore);
            await Assert.That(scene.Meshes.Values.Single().AuthoredNormals.Span.SequenceEqual(initialNormals)).IsTrue();
            await Assert.That(scene.Meshes.Values.Single().FindTexCoord("st")!.Data.Span.SequenceEqual(expectedUv))
                .IsTrue();
        }
        if (editUvsWhileRetired)
        {
            await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec2fArray(
                "primvars:st", OriginalUvs));
            using OpenUsdSilkPage uvRestore = SyncMesh(session, complexity, bounded);
            scene.Apply(uvRestore);
            await Assert.That(scene.Meshes.Values.Single().AuthoredNormals.Span.SequenceEqual(initialNormals)).IsTrue();
            await Assert.That(scene.Meshes.Values.Single().FindTexCoord("st")!.Data.Span.SequenceEqual(initialUvValues))
                .IsTrue();
        }
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + RefinedMesh);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task RetiredMeshAttributeEditsMatchIndependentReferencePixels(
        SilkGraphicsBackend backend, bool refined)
    {
        string geometry = TexturedMesh();
        using Fixture actualFixture = Create(geometry);
        using Fixture referenceFixture = Create(geometry);
        foreach (Fixture fixture in new[] { actualFixture, referenceFixture })
        {
            using FileStream image = File.Create(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(fixture.Path)!, "colors.png"));
            _ = PngRgba8Writer.Write(image, 2, 2,
                [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255], 1024);
        }
        await using UsdStageScheduler actualScheduler = UsdStageScheduler.Open(actualFixture.Path);
        await using UsdStageScheduler referenceScheduler = UsdStageScheduler.Open(referenceFixture.Path);
        using UsdStageRenderSource actualSource = await actualScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource referenceSource = await referenceScheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession actual = OpenUsdSilkRuntime.Create(actualFixture.Plugins, actualSource);
        using OpenUsdSilkSession reference = OpenUsdSilkRuntime.Create(referenceFixture.Plugins, referenceSource);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var bufferBudget = new SilkGpuBufferBudget(4_194_304);
        bufferBudget.ConfigureDevice(device);
        var textureBudgets = new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304));
        textureBudgets.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using var referenceRenderer = new SilkMeshRenderer(device);
        RenderComplexity complexity = refined ? RenderComplexity.Medium : RenderComplexity.Low;

        byte[] initial = Capture(actual, device, renderer, complexity);
        await referenceScheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            mesh.SetVec3fArray("normals", FlatNormals);
            mesh.SetVec2fArray("primvars:st", HalfUvs);
        });
        byte[] expected = Capture(reference, device, referenceRenderer, complexity);
        await Assert.That(expected.SequenceEqual(initial)).IsFalse();
        int colors = Enumerable.Range(0, initial.Length / 4)
            .Select(index => Convert.ToHexString(initial.AsSpan(index * 4, 3))).Distinct().Count();
        await Assert.That(colors).IsGreaterThan(4);

        await actualScheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").GetAttribute("points").BlockValue());
        byte[] retired = Capture(actual, device, renderer, complexity);
        await Assert.That(retired.SequenceEqual(initial)).IsFalse();
        await Assert.That(renderer.Scene.Meshes.Count).IsEqualTo(0);
        await actualScheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("normals", FlatNormals));
        await Assert.That(Capture(actual, device, renderer, complexity).SequenceEqual(retired)).IsTrue();
        await actualScheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec2fArray("primvars:st", HalfUvs));
        await Assert.That(Capture(actual, device, renderer, complexity).SequenceEqual(retired)).IsTrue();

        await actualScheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points", QuadPoints));
        byte[] recovered = Capture(actual, device, renderer, complexity);
        await Assert.That(recovered.SequenceEqual(expected)).IsTrue();
        await Assert.That(renderer.Scene.Meshes.Values.Single().AuthoredNormals.Span.SequenceEqual(
            referenceRenderer.Scene.Meshes.Values.Single().AuthoredNormals.Span)).IsTrue();
        await Assert.That(renderer.Scene.Meshes.Values.Single().FindTexCoord("st")!.Data.Span.SequenceEqual(
            referenceRenderer.Scene.Meshes.Values.Single().FindTexCoord("st")!.Data.Span)).IsTrue();

        await actualScheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            mesh.SetVec3fArray("normals", OriginalNormals);
            mesh.SetVec2fArray("primvars:st", OriginalUvs);
        });
        await Assert.That(Capture(actual, device, renderer, complexity).SequenceEqual(initial)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(actualFixture.Path)).IsEqualTo("#usda 1.0\n" + geometry);
        renderer.Dispose();
        referenceRenderer.Dispose();
        await Assert.That(bufferBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textureBudgets.CpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textureBudgets.GpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    private static async Task AssertGeometry(
        (string Path, float[] Points, uint[] Indices)[] expected, SilkSceneState scene)
    {
        SilkMeshData[] meshes = scene.Meshes.Values
            .OrderBy(static mesh => mesh.Path, StringComparer.Ordinal).ToArray();
        await Assert.That(meshes.Length).IsEqualTo(expected.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            await Assert.That(meshes[index].Path).IsEqualTo(expected[index].Path);
            await Assert.That(meshes[index].Points.Span.SequenceEqual(expected[index].Points)).IsTrue();
            await Assert.That(meshes[index].Indices.Span.SequenceEqual(expected[index].Indices)).IsTrue();
        }
    }

    private static OpenUsdSilkPage SyncMesh(
        OpenUsdSilkSession session, RenderComplexity complexity, bool bounded) =>
        bounded ? session.Sync(16, 16, Options) : session.Sync(16, 16, complexity: complexity);

    private static byte[] Capture(
        OpenUsdSilkSession session, ISilkGraphicsDevice device, SilkMeshRenderer renderer,
        RenderComplexity complexity = RenderComplexity.Low)
    {
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        var settings = new RenderSettings(1, true, true, new Vector4(0, 0, 0, 1),
            backfaceCulling: true, useSceneMaterials: true, complexity: complexity);
        return SilkFrameCapture.CaptureCore(
            session, device, renderer, 64, 64, settings, 0, camera).Rgba.ToArray();
    }

    private static string TexturedMesh() =>
        RefinedMesh.Replace("def Mesh \"Mesh\" {",
            "def Mesh \"Mesh\" (prepend apiSchemas = [\"MaterialBindingAPI\"]) {\n" +
            "rel material:binding = </Material>", StringComparison.Ordinal) + "\n" + """
        def Material "Material" {
            token outputs:surface.connect = </Material/Surface.outputs:surface>
            def Shader "Surface" {
                uniform token info:id = "UsdPreviewSurface"
                color3f inputs:diffuseColor = (0,0,0)
                color3f inputs:emissiveColor.connect = </Material/Texture.outputs:rgb>
                token outputs:surface
            }
            def Shader "Texture" {
                uniform token info:id = "UsdUVTexture"
                asset inputs:file = @colors.png@
                token inputs:sourceColorSpace = "raw"
                token inputs:wrapS = "clamp"
                token inputs:wrapT = "clamp"
                float2 inputs:st.connect = </Material/Coordinates.outputs:result>
                float3 outputs:rgb
            }
            def Shader "Coordinates" {
                uniform token info:id = "UsdPrimvarReader_float2"
                token inputs:varname = "st"
                float2 outputs:result
            }
        }
        """;

    private static Fixture Create(string geometry)
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrEmpty(plugins))
        {
            if (Environment.GetEnvironmentVariable("OPENUSD_MESH_PREPARATION_REQUIRED") == "1")
            {
                throw new InvalidOperationException("The native mesh recovery tests require matched plugin inputs.");
            }
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to a runtime with the hdSilk plugin.");
        }
        string root = Directory.CreateTempSubdirectory("silk-mesh-invalid-points-").FullName;
        string path = System.IO.Path.Combine(root, "scene.usda");
        File.WriteAllText(path, "#usda 1.0\n" + geometry);
        return new Fixture(root, path, plugins);
    }

    private sealed class Fixture(string root, string path, string plugins) : IDisposable
    {
        internal string Path { get; } = path;
        internal string Plugins { get; } = plugins;
        internal OpenUsdSilkSession Open() => OpenUsdSilkRuntime.Create(Plugins, Path);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }

    private static readonly UsdVec3f[] OriginalPoints =
        [new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0, 0.5f, 0)];
    private static readonly UsdVec3f[] QuadPoints =
        [new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0.5f, 0.5f, 0), new(-0.5f, 0.5f, 0)];
    private static readonly UsdVec3f[] OriginalNormals =
        [new(-0.2f, 0, 1), new(0.2f, 0, 1), new(0, 0.2f, 1), new(0, -0.2f, 1)];
    private static readonly UsdVec3f[] FlatNormals = [new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1)];
    private static readonly UsdVec2f[] OriginalUvs = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
    private static readonly UsdVec2f[] HalfUvs = [new(0, 0), new(0.5f, 0), new(0.5f, 0.5f), new(0, 0.5f)];

    private static SilkSceneIngestionOptions Options =>
        new(RenderPurpose.Default | RenderPurpose.Render | RenderPurpose.Proxy, "full", 4096);

    private const string Mesh = """
        def Mesh "Mesh" {
            uniform token subdivisionScheme = "none"
            point3f[] points = [(-0.5,-0.5,0), (0.5,-0.5,0), (0,0.5,0)]
            int[] faceVertexCounts = [3]
            int[] faceVertexIndices = [0,1,2]
            normal3f[] normals = [(0,0,1)] (interpolation = "constant")
        }
        """;

    private const string InstancedMesh = """
        def PointInstancer "Instances" {
            rel prototypes = [</Instances/Mesh>, </Instances/Other>]
            int[] protoIndices = [0, 1, 0]
            point3f[] positions = [(-0.5, 0, 0), (0, 5, 0), (0.5, 0, 0)]
        """ + "\n" + Mesh + "\n" +
        """
            def Mesh "Other" {
                uniform token subdivisionScheme = "none"
                point3f[] points = [(-0.5,-0.5,0), (0.5,-0.5,0), (0,0.5,0)]
                int[] faceVertexCounts = [3]
                int[] faceVertexIndices = [0,1,2]
                normal3f[] normals = [(0,0,1)] (interpolation = "constant")
            }
        }
        """;

    private const string RefinedMesh = """
        def Mesh "Mesh" {
            uniform token subdivisionScheme = "catmullClark"
            point3f[] points = [(-0.5,-0.5,0), (0.5,-0.5,0), (0.5,0.5,0), (-0.5,0.5,0)]
            int[] faceVertexCounts = [4]
            int[] faceVertexIndices = [0,1,2,3]
            normal3f[] normals = [
                (-0.2,0,1), (0.2,0,1), (0,0.2,1), (0,-0.2,1)
            ] (interpolation = "vertex")
            texCoord2f[] primvars:st = [(0,0), (1,0), (1,1), (0,1)] (interpolation = "faceVarying")
        }
        """;
}
