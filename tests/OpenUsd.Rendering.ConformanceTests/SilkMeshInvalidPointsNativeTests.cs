// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkMeshInvalidPointsNativeTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task InvalidMeshPointsRetireGeometryAndRecover(SilkGraphicsBackend backend, bool shortPoints)
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

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            if (shortPoints)
            {
                mesh.SetVec3fArray("points", [.. OriginalPoints.Take(2)]);
            }
            else
            {
                mesh.GetAttribute("points").BlockValue();
            }
        });
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
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task InvalidMeshPrototypeRetiresEveryInstanceAndRecovers(
        SilkGraphicsBackend backend, bool shortPoints)
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
        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Instances/Mesh");
            if (shortPoints)
            {
                mesh.SetVec3fArray("points", [.. OriginalPoints.Take(2)]);
            }
            else
            {
                mesh.GetAttribute("points").BlockValue();
            }
        });
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
    [Arguments(RenderComplexity.Low, false)]
    [Arguments(RenderComplexity.Low, true)]
    [Arguments(RenderComplexity.Medium, false)]
    [Arguments(RenderComplexity.Medium, true)]
    public async Task MeshRetiresAndRecoversAfterShortPoints(RenderComplexity complexity, bool attributes)
    {
        string geometry = attributes ? RefinedMesh : SimpleRefinedMesh;
        using Fixture fixture = Create(geometry);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage initial = session.Sync(16, 16, complexity: complexity);
        scene.Apply(initial);
        SilkMeshData original = scene.Meshes.Values.Single();
        float[] points = original.Points.ToArray();
        uint[] indices = original.Indices.ToArray();
        float[] normals = original.AuthoredNormals.ToArray();
        float[]? uvs = original.FindTexCoord("st")?.Data.ToArray();
        await Assert.That(points.Length).IsGreaterThanOrEqualTo(12);
        if (complexity == RenderComplexity.Medium)
        {
            await Assert.That(points.Length).IsGreaterThan(12);
        }

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", [.. QuadPoints.Take(3)]));
        using OpenUsdSilkPage incompatible = session.Sync(16, 16, complexity: complexity);
        scene.Apply(incompatible);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        using var retained = new SilkSceneState();
        retained.Apply(initial);
        await Assert.That(retained.Meshes.Values.Single().Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(retained.Meshes.Values.Single().Indices.Span.SequenceEqual(indices)).IsTrue();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points", QuadPoints));
        using OpenUsdSilkPage recovered = session.Sync(16, 16, complexity: complexity);
        scene.Apply(recovered);
        SilkMeshData restored = scene.Meshes.Values.Single();
        await Assert.That(restored.Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(restored.Indices.Span.SequenceEqual(indices)).IsTrue();
        await Assert.That(restored.AuthoredNormals.Span.SequenceEqual(normals)).IsTrue();
        if (uvs is null)
        {
            await Assert.That(restored.FindTexCoord("st")).IsNull();
        }
        else
        {
            await Assert.That(restored.FindTexCoord("st")!.Data.Span.SequenceEqual(uvs)).IsTrue();
        }
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + geometry);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task BoundedMissingPointsRefuseWithoutReplacingRetainedGeometry(bool instanced, bool changeTopology)
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

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim(path);
            if (changeTopology)
            {
                mesh.SetInt32Array("faceVertexIndices", [0, 1, 3]);
            }
            else
            {
                mesh.SetVec3fArray("points", [.. OriginalPoints.Take(2)]);
            }
        });
        await Assert.That(() => session.Sync(16, 16, Options))
            .Throws<OpenUsdSilkException>()
            .WithMessageContaining("topology references a missing point");
        using var retained = new SilkSceneState();
        retained.Apply(initial);
        await AssertGeometry(expected, retained);
        await AssertGeometry(expected, scene);

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim(path);
            if (changeTopology)
            {
                mesh.SetInt32Array("faceVertexIndices", [0, 1, 2]);
            }
            else
            {
                mesh.SetVec3fArray("points", OriginalPoints);
            }
        });
        using OpenUsdSilkPage retry = session.Sync(16, 16, Options);
        scene.Apply(retry);
        await AssertGeometry(expected, scene);
        await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsEqualTo(reserved);
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + geometry);
    }

    [Test]
    [Arguments(RenderComplexity.Low)]
    [Arguments(RenderComplexity.Medium)]
    public async Task MeshRetiresAndRecoversAfterTopologyReferencesMissingPoints(RenderComplexity complexity)
    {
        using Fixture fixture = Create(SimpleRefinedMesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage initial = session.Sync(16, 16, complexity: complexity);
        scene.Apply(initial);
        float[] points = scene.Meshes.Values.Single().Points.ToArray();
        uint[] indices = scene.Meshes.Values.Single().Indices.ToArray();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetInt32Array(
            "faceVertexIndices", [0, 1, 2, 4]));
        using OpenUsdSilkPage invalid = session.Sync(16, 16, complexity: complexity);
        scene.Apply(invalid);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetInt32Array(
            "faceVertexIndices", [0, 1, 2, 3]));
        using OpenUsdSilkPage recovered = session.Sync(16, 16, complexity: complexity);
        scene.Apply(recovered);
        await Assert.That(scene.Meshes.Values.Single().Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(scene.Meshes.Values.Single().Indices.Span.SequenceEqual(indices)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + SimpleRefinedMesh);
    }

    [Test]
    public Task EmptyPointsRetireBeforeIncompleteTopologyIsTriangulated() =>
        InvalidTopologyRetiresAndRecovers(Array.Empty<UsdVec3f>(), [3]);

    [Test]
    public Task ResolvedPointsRetireBeforeIncompleteTopologyIsTriangulated() =>
        InvalidTopologyRetiresAndRecovers(OriginalPoints, [3]);

    [Test]
    public Task NegativeFaceCountsRetireBeforeTopologyIsTriangulated() =>
        InvalidTopologyRetiresAndRecovers(OriginalPoints, [-1]);

    private static async Task InvalidTopologyRetiresAndRecovers(
        UsdVec3f[] points,
        int[] faceVertexCounts)
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            mesh.SetVec3fArray("points", points);
            mesh.SetInt32Array("faceVertexCounts", faceVertexCounts);
            mesh.SetInt32Array("faceVertexIndices", Array.Empty<int>());
        });
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage invalid = session.Sync(16, 16);
        scene.Apply(invalid);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", points.Length == 0 ? OriginalPoints : Array.Empty<UsdVec3f>()));
        using OpenUsdSilkPage stillInvalid = session.Sync(
            16, 16, complexity: RenderComplexity.Medium);
        scene.Apply(stillInvalid);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            mesh.SetVec3fArray("points", OriginalPoints);
            mesh.SetInt32Array("faceVertexCounts", [3]);
            mesh.SetInt32Array("faceVertexIndices", [0, 1, 2]);
        });
        using OpenUsdSilkPage recovered = session.Sync(16, 16);
        scene.Apply(recovered);
        await Assert.That(scene.Meshes.Count).IsEqualTo(1);
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
    }

    [Test]
    [Arguments(0, false, -1)]
    [Arguments(0, false, 9)]
    [Arguments(0, true, -1)]
    [Arguments(0, true, 9)]
    [Arguments(1, false, -1)]
    [Arguments(1, false, 9)]
    [Arguments(1, true, -1)]
    [Arguments(1, true, 9)]
    [Arguments(2, false, -1)]
    [Arguments(2, false, 9)]
    [Arguments(2, true, -1)]
    [Arguments(2, true, 9)]
    public async Task UnemittedTopologyIndicesDoNotInvalidateMesh(int topologyKind, bool bounded, int unusedIndex)
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage initial = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(initial);
        float[] points = scene.Meshes.Values.Single().Points.ToArray();

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            switch (topologyKind)
            {
                case 0:
                    mesh.SetInt32Array("faceVertexCounts", [3, 3]);
                    mesh.SetInt32Array("faceVertexIndices", [0, 1, 2, 0, 1, unusedIndex]);
                    mesh.SetInt32Array("holeIndices", [1]);
                    break;
                case 1:
                    mesh.SetInt32Array("faceVertexCounts", [3, 2, 3]);
                    mesh.SetInt32Array("faceVertexIndices", [0, 1, 2, 0, unusedIndex, 0, 1, 2]);
                    break;
                default:
                    mesh.SetInt32Array("faceVertexIndices", [0, 1, 2, unusedIndex]);
                    break;
            }
        });
        using OpenUsdSilkPage updated = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(updated);
        SilkMeshData updatedMesh = scene.Meshes.Values.Single();
        await Assert.That(updatedMesh.Points.Span.SequenceEqual(points)).IsTrue();
        uint[] expectedIndices = topologyKind == 1 ? [0, 1, 2, 0, 1, 2] : [0, 1, 2];
        await Assert.That(updatedMesh.Indices.Span.SequenceEqual(expectedIndices)).IsTrue();

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            mesh.SetInt32Array("faceVertexCounts", [3]);
            mesh.SetInt32Array("faceVertexIndices", [0, 1, 2]);
            mesh.SetInt32Array("holeIndices", []);
        });
        using OpenUsdSilkPage restored = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(restored);
        await Assert.That(scene.Meshes.Values.Single().Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(scene.Meshes.Values.Single().Indices.Span.SequenceEqual(new uint[] { 0, 1, 2 })).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
    }

    [Test]
    public async Task BoundedIncompleteFaceIndicesRefuseWithoutReplacingRetainedGeometry()
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage initial = session.Sync(16, 16, Options);
        scene.Apply(initial);
        float[] points = scene.Meshes.Values.Single().Points.ToArray();
        uint[] indices = scene.Meshes.Values.Single().Indices.ToArray();
        ulong reserved = initial.MeshPreparationUsage!.ReservedBytes;

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetInt32Array("faceVertexCounts", [4]));
        await Assert.That(() => session.Sync(16, 16, Options))
            .Throws<OpenUsdSilkException>()
            .WithMessageContaining("fewer indices than its face counts require");
        await Assert.That(scene.Meshes.Values.Single().Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(scene.Meshes.Values.Single().Indices.Span.SequenceEqual(indices)).IsTrue();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetInt32Array("faceVertexCounts", [3]));
        using OpenUsdSilkPage restored = session.Sync(16, 16, Options);
        scene.Apply(restored);
        await Assert.That(restored.MeshPreparationUsage!.ReservedBytes).IsEqualTo(reserved);
        await Assert.That(scene.Meshes.Values.Single().Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(scene.Meshes.Values.Single().Indices.Span.SequenceEqual(indices)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DegenerateHoleDoesNotMaskMissingPointsInLaterEmittedFace(bool bounded)
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage initial = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(initial);
        float[] points = scene.Meshes.Values.Single().Points.ToArray();
        uint[] indices = scene.Meshes.Values.Single().Indices.ToArray();

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            mesh.SetInt32Array("faceVertexCounts", [3, 2, 3]);
            mesh.SetInt32Array("faceVertexIndices", [0, 1, 2, 0, 1, 0, 1, 9]);
            mesh.SetInt32Array("holeIndices", [1, 2]);
        });
        if (bounded)
        {
            await Assert.That(() => SyncMesh(session, RenderComplexity.Low, bounded))
                .Throws<OpenUsdSilkException>()
                .WithMessageContaining("topology references a missing point");
            await Assert.That(scene.Meshes.Values.Single().Points.Span.SequenceEqual(points)).IsTrue();
            await Assert.That(scene.Meshes.Values.Single().Indices.Span.SequenceEqual(indices)).IsTrue();
        }
        else
        {
            using OpenUsdSilkPage retired = SyncMesh(session, RenderComplexity.Low, bounded);
            scene.Apply(retired);
            await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        }

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            mesh.SetInt32Array("faceVertexCounts", [3]);
            mesh.SetInt32Array("faceVertexIndices", [0, 1, 2]);
            mesh.SetInt32Array("holeIndices", []);
        });
        using OpenUsdSilkPage restored = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(restored);
        await Assert.That(scene.Meshes.Values.Single().Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(scene.Meshes.Values.Single().Indices.Span.SequenceEqual(indices)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
    }

    [Test]
    [Arguments(RenderComplexity.Low)]
    [Arguments(RenderComplexity.Medium)]
    public async Task RefinedMeshRequiresControlPointsUsedOnlyByHoleFaces(RenderComplexity complexity)
    {
        using Fixture fixture = Create(SimpleRefinedMesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            mesh.SetInt32Array("faceVertexCounts", [3, 3]);
            mesh.SetInt32Array("faceVertexIndices", [0, 1, 2, 1, 0, 3]);
            mesh.SetInt32Array("holeIndices", [1]);
        });
        using OpenUsdSilkPage initial = session.Sync(16, 16, complexity: complexity);
        scene.Apply(initial);
        float[] points = scene.Meshes.Values.Single().Points.ToArray();
        uint[] indices = scene.Meshes.Values.Single().Indices.ToArray();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", [.. QuadPoints.Take(3)]));
        using OpenUsdSilkPage shorter = session.Sync(16, 16, complexity: complexity);
        scene.Apply(shorter);
        if (complexity == RenderComplexity.Medium)
        {
            await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        }
        else
        {
            await Assert.That(scene.Meshes.Values.Single().Points.Length).IsEqualTo(9);
            await Assert.That(scene.Meshes.Values.Single().Indices.Span.SequenceEqual(indices)).IsTrue();
        }

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points", QuadPoints));
        using OpenUsdSilkPage restored = session.Sync(16, 16, complexity: complexity);
        scene.Apply(restored);
        await Assert.That(scene.Meshes.Values.Single().Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(scene.Meshes.Values.Single().Indices.Span.SequenceEqual(indices)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + SimpleRefinedMesh);
    }

    [Test]
    [Arguments(RenderComplexity.Low, false)]
    [Arguments(RenderComplexity.Low, true)]
    [Arguments(RenderComplexity.Medium, false)]
    public async Task UnreferencedTrailingPointsRemainValid(RenderComplexity complexity, bool bounded)
    {
        using Fixture fixture = Create(SimpleRefinedMesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage initial = SyncMesh(session, complexity, bounded);
        scene.Apply(initial);
        float[] points = scene.Meshes.Values.Single().Points.ToArray();
        uint[] indices = scene.Meshes.Values.Single().Indices.ToArray();
        ulong initialTopologyRevision = scene.Meshes.Values.Single().TopologyRevision;

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", [.. QuadPoints, new(3, 4, 5)]));
        using OpenUsdSilkPage updated = SyncMesh(session, complexity, bounded);
        scene.Apply(updated);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        await Assert.That(mesh.Indices.Span.SequenceEqual(indices)).IsTrue();
        if (complexity == RenderComplexity.Medium)
        {
            await Assert.That(mesh.Points.Span.SequenceEqual(points)).IsTrue();
            await Assert.That(mesh.TopologyRevision).IsEqualTo(initialTopologyRevision);
        }
        else
        {
            await Assert.That(mesh.Points.Span.SequenceEqual([.. points, 3f, 4f, 5f])).IsTrue();
            await Assert.That(mesh.TopologyRevision).IsGreaterThan(initialTopologyRevision);
            await Assert.That(mesh.PointOrigins.Span.SequenceEqual([0, 1, 2, 3, -1])).IsTrue();
            await Assert.That(mesh.AuthoredPointCount).IsEqualTo(4);
        }
        ulong updatedTopologyRevision = mesh.TopologyRevision;
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points", QuadPoints));
        using OpenUsdSilkPage restored = SyncMesh(session, complexity, bounded);
        scene.Apply(restored);
        mesh = scene.Meshes.Values.Single();
        await Assert.That(mesh.Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(mesh.Indices.Span.SequenceEqual(indices)).IsTrue();
        if (complexity == RenderComplexity.Medium)
        {
            await Assert.That(mesh.TopologyRevision).IsEqualTo(updatedTopologyRevision);
        }
        else
        {
            await Assert.That(mesh.TopologyRevision).IsGreaterThan(updatedTopologyRevision);
            await Assert.That(mesh.PointOrigins.Span.SequenceEqual([0, 1, 2, 3])).IsTrue();
        }
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + SimpleRefinedMesh);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PointCountChangesRefreshVertexAttributes(bool bounded)
    {
        using Fixture fixture = Create(VertexNormalRefinedMesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage initial = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(initial);
        float[] normals = scene.Meshes.Values.Single().AuthoredNormals.ToArray();
        await Assert.That(normals.Length).IsEqualTo(12);

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", [.. QuadPoints, new(3, 4, 5)]));
        using OpenUsdSilkPage extraPoint = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(extraPoint);
        await Assert.That(scene.Meshes.Values.Single().AuthoredNormals.IsEmpty).IsTrue();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "normals", [.. OriginalNormals, new(0, 0, 1)]));
        using OpenUsdSilkPage extraNormal = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(extraNormal);
        await Assert.That(scene.Meshes.Values.Single().AuthoredNormals.Length).IsEqualTo(15);

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points", QuadPoints));
        using OpenUsdSilkPage restoredPoints = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(restoredPoints);
        await Assert.That(scene.Meshes.Values.Single().AuthoredNormals.IsEmpty).IsTrue();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("normals", OriginalNormals));
        using OpenUsdSilkPage restoredNormals = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(restoredNormals);
        await Assert.That(scene.Meshes.Values.Single().AuthoredNormals.Span.SequenceEqual(normals)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + VertexNormalRefinedMesh);
    }

    [Test]
    [Arguments(RenderComplexity.Low, false)]
    [Arguments(RenderComplexity.Low, true)]
    [Arguments(RenderComplexity.Medium, false)]
    [Arguments(RenderComplexity.Medium, true)]
    public async Task ConstantNormalsAndUvEditsRecoverAfterPointsAreRemoved(
        RenderComplexity complexity, bool shortPoints)
    {
        using Fixture fixture = Create(ConstantNormalRefinedMesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage initial = session.Sync(16, 16, complexity: complexity);
        scene.Apply(initial);
        SilkMeshData original = scene.Meshes.Values.Single();
        float[] points = original.Points.ToArray();
        uint[] indices = original.Indices.ToArray();
        float[] normals = original.AuthoredNormals.ToArray();
        float[] uvs = original.FindTexCoord("st")!.Data.ToArray();
        await Assert.That(normals.Length).IsGreaterThan(0);
        await Assert.That(uvs.Length).IsGreaterThan(0);

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            if (shortPoints)
            {
                mesh.SetVec3fArray("points", [.. QuadPoints.Take(3)]);
            }
            else
            {
                mesh.SetVec3fArray("points", []);
            }
        });
        using OpenUsdSilkPage invalid = session.Sync(16, 16, complexity: complexity);
        scene.Apply(invalid);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec2fArray("primvars:st", HalfUvs));
        using OpenUsdSilkPage uvEdit = session.Sync(16, 16, complexity: complexity);
        scene.Apply(uvEdit);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points", QuadPoints));
        using OpenUsdSilkPage recovered = session.Sync(16, 16, complexity: complexity);
        scene.Apply(recovered);
        SilkMeshData restored = scene.Meshes.Values.Single();
        await Assert.That(restored.Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(restored.Indices.Span.SequenceEqual(indices)).IsTrue();
        await Assert.That(restored.AuthoredNormals.Span.SequenceEqual(normals)).IsTrue();
        await Assert.That(restored.FindTexCoord("st")!.Data.Span.SequenceEqual(
            uvs.Select(static value => value * 0.5f).ToArray())).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path))
            .IsEqualTo("#usda 1.0\n" + ConstantNormalRefinedMesh);
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
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    public async Task RetiredMeshAttributeEditsMatchIndependentReferencePixels(
        SilkGraphicsBackend backend, bool refined, bool shortPoints)
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

        await actualScheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            if (shortPoints)
            {
                mesh.SetVec3fArray("points", [.. QuadPoints.Take(3)]);
            }
            else
            {
                mesh.GetAttribute("points").BlockValue();
            }
        });
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

    [Test]
    [MethodDataSource(nameof(NormalBackends))]
    public async Task ComputedAreaWeightedNormalsMatchAuthoredReferencePixels(SilkGraphicsBackend backend)
    {
        const string weightedNormals = """
            normal3f[] normals = [
                (0,0.242535625,0.9701425), (0,0.242535625,0.9701425),
                (0,0,1), (0,1,0)
            ] (interpolation = "vertex")
            """;
        const string equalWeightedNormals = """
            normal3f[] normals = [
                (0,0.70710678,0.70710678), (0,0.70710678,0.70710678),
                (0,0,1), (0,1,0)
            ] (interpolation = "vertex")
            """;
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using Fixture actualFixture = Create(AreaWeightedMesh(string.Empty));
        using Fixture referenceFixture = Create(AreaWeightedMesh(weightedNormals));
        using Fixture equalWeightedFixture = Create(AreaWeightedMesh(equalWeightedNormals));
        await using UsdStageScheduler actualScheduler = UsdStageScheduler.Open(actualFixture.Path);
        await using UsdStageScheduler referenceScheduler = UsdStageScheduler.Open(referenceFixture.Path);
        await using UsdStageScheduler equalWeightedScheduler = UsdStageScheduler.Open(equalWeightedFixture.Path);
        using UsdStageRenderSource actualSource = await actualScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource referenceSource = await referenceScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource equalWeightedSource = await equalWeightedScheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession actual = OpenUsdSilkRuntime.Create(actualFixture.Plugins, actualSource);
        using OpenUsdSilkSession reference = OpenUsdSilkRuntime.Create(referenceFixture.Plugins, referenceSource);
        using OpenUsdSilkSession equalWeighted =
            OpenUsdSilkRuntime.Create(equalWeightedFixture.Plugins, equalWeightedSource);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using var referenceRenderer = new SilkMeshRenderer(device);
        using var equalWeightedRenderer = new SilkMeshRenderer(device);

        byte[] initial = Capture(actual, device, renderer, enableShadows: false);
        byte[] expected = Capture(reference, device, referenceRenderer, enableShadows: false);
        byte[] equalWeightedPixels = Capture(equalWeighted, device, equalWeightedRenderer, enableShadows: false);
        SilkMeshData actualMesh = renderer.Scene.Meshes.Values.Single();
        await Assert.That(actualMesh.AuthoredNormals.IsEmpty).IsTrue();
        await Assert.That(actualMesh.Points.Length).IsEqualTo(12);
        await Assert.That(actualMesh.Indices.Span.SequenceEqual(new uint[] { 0, 1, 2, 0, 3, 1 })).IsTrue();
        await AssertPixelsNear(initial, expected);
        await Assert.That(CountDifferentPixels(initial, equalWeightedPixels)).IsGreaterThan(50);
        ulong builds = renderer.GpuResources.Statistics.GeometryBuilds;
        await Assert.That(Capture(actual, device, renderer, enableShadows: false).SequenceEqual(initial)).IsTrue();
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsEqualTo(builds);

        UsdVec3f[] changedPoints =
            [new(-0.6f, -0.6f, 0), new(0.6f, -0.6f, 0), new(-0.6f, 0.6f, 0), new(-0.6f, -0.6f, 0.6f)];
        await actualScheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points", changedPoints));
        await referenceScheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            mesh.SetVec3fArray("points", changedPoints);
            mesh.SetVec3fArray("normals",
                [new(0, 0.4472136f, 0.8944272f), new(0, 0.4472136f, 0.8944272f), new(0, 0, 1), new(0, 1, 0)]);
        });
        byte[] updated = Capture(actual, device, renderer, enableShadows: false);
        byte[] updatedReference = Capture(reference, device, referenceRenderer, enableShadows: false);
        await AssertPixelsNear(updated, updatedReference);
        await Assert.That(updated.SequenceEqual(initial)).IsFalse();
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsGreaterThan(builds);
        renderer.Dispose();
        referenceRenderer.Dispose();
        equalWeightedRenderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SmallTriangleNormalsFollowSimulationAndRestore(bool authored, bool bounded)
    {
        string normals = authored ? """
            normal3f[] normals = [
                (0,0.6,0.8), (0,0.6,0.8), (0,0,1), (0,0,1)
            ] (interpolation = "vertex")
            """ : string.Empty;
        string geometry = $$"""
            def Mesh "Mesh" {
                uniform token subdivisionScheme = "catmullClark"
                point3f[] points = [(0,0,0), (1,0,0), (0,1.2e-15,0), (0,0,0.9e-15)]
                int[] faceVertexCounts = [3,3]
                int[] faceVertexIndices = [0,1,2,0,3,1]
                {{normals}}
            }
            """;
        using Fixture fixture = Create(geometry);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(page);

        SilkMeshData original = scene.Meshes.Values.Single();
        float[] originalPoints = original.Points.ToArray();
        int[] originalOrigins = original.PointOrigins.ToArray();
        await Assert.That(originalPoints.Length).IsEqualTo(12);
        await Assert.That(original.Indices.Span.SequenceEqual([0u, 1u, 2u, 0u, 3u, 1u])).IsTrue();
        await Assert.That(original.AuthoredNormals.IsEmpty).IsEqualTo(!authored);
        await AssertSmallTriangleNormals(original, 0.6f, 0.8f);

        ulong revision = scene.GeometryRevision;
        SilkDeformationResult unchanged = scene.ReplacePoints("/Mesh", 0, originalPoints, out ulong meshId);
        await Assert.That(unchanged).IsEqualTo(SilkDeformationResult.Unchanged);
        await Assert.That(meshId).IsEqualTo(original.Id);
        await Assert.That(scene.GeometryRevision).IsEqualTo(revision);
        await Assert.That(scene.HasAuthoredGeometry(meshId)).IsFalse();

        float[] simulatedPoints = originalPoints.ToArray();
        simulatedPoints[11] = 0.3e-15f;
        SilkDeformationResult changed = scene.ReplacePoints("/Mesh", 0, simulatedPoints, out meshId);
        await Assert.That(changed).IsEqualTo(SilkDeformationResult.Applied);
        SilkMeshData simulated = scene.Meshes[meshId];
        await Assert.That(simulated.Points.Span.SequenceEqual(simulatedPoints)).IsTrue();
        await Assert.That(simulated.AuthoredNormals.IsEmpty).IsTrue();
        await Assert.That(simulated.TopologyFingerprint).IsEqualTo(original.TopologyFingerprint);
        await Assert.That(simulated.TopologyRevision).IsEqualTo(original.TopologyRevision);
        await Assert.That(simulated.PointOrigins.Span.SequenceEqual(originalOrigins)).IsTrue();
        await Assert.That(scene.GeometryRevision).IsEqualTo(revision + 1);
        await Assert.That(scene.HasAuthoredGeometry(meshId)).IsTrue();
        await AssertSmallTriangleNormals(simulated, 0.242535625f, 0.9701425f);

        await Assert.That(scene.RestoreAuthoredPoints(meshId)).IsTrue();
        await Assert.That(scene.Meshes[meshId]).IsSameReferenceAs(original);
        await Assert.That(scene.Meshes[meshId].Points.Span.SequenceEqual(originalPoints)).IsTrue();
        await Assert.That(scene.GeometryRevision).IsEqualTo(revision + 2);
        await Assert.That(scene.HasAuthoredGeometry(meshId)).IsFalse();
        await Assert.That(scene.RestoreAuthoredPoints(meshId)).IsFalse();
        await AssertSmallTriangleNormals(scene.Meshes[meshId], 0.6f, 0.8f);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CoplanarSeamNormalsFollowEqualCopiesAndRestore(bool authored, bool bounded)
    {
        string normals = authored ? """
            normal3f[] normals = [(0,0,1), (0,0,1), (0,0,1), (0,0,1)] (interpolation = "vertex")
            """ : string.Empty;
        string geometry = $$"""
            def Mesh "Mesh" {
                uniform token subdivisionScheme = "catmullClark"
                point3f[] points = [(-0.6,-0.6,0), (0.6,-0.6,0), (0.6,0.6,0), (-0.6,0.6,0)]
                int[] faceVertexCounts = [3,3]
                int[] faceVertexIndices = [0,1,2,0,2,3]
                texCoord2f[] primvars:st = [
                    (0,0), (1,0), (1,1), (0.2,0.2), (0.8,0.8), (0,1)
                ] (interpolation = "faceVarying")
                {{normals}}
            }
            """;
        using Fixture fixture = Create(geometry);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(page);

        SilkMeshData original = scene.Meshes.Values.Single();
        float[] originalPoints = original.Points.ToArray();
        float[] originalNormals = original.AuthoredNormals.ToArray();
        float[] originalUvs = original.FindTexCoord("st")!.Data.ToArray();
        int[] originalOrigins = original.PointOrigins.ToArray();
        await Assert.That(originalPoints.Length).IsEqualTo(18);
        await Assert.That(originalNormals.Length).IsEqualTo(authored ? originalPoints.Length : 0);
        int[] copies = Enumerable.Range(0, originalPoints.Length / 3)
            .Where(vertex => originalPoints[vertex * 3] == -0.6f &&
                originalPoints[(vertex * 3) + 1] == -0.6f &&
                originalPoints[(vertex * 3) + 2] == 0)
            .ToArray();
        await Assert.That(copies.Length).IsEqualTo(2);
        await AssertCoplanarSeamNormals(original, 0, 1, originalUvs);

        float[] simulatedPoints = originalPoints.ToArray();
        for (int component = 0; component < simulatedPoints.Length; component += 3)
        {
            simulatedPoints[component + 2] = simulatedPoints[component + 1] * 0.5f;
        }
        await Assert.That(simulatedPoints[(copies[0] * 3) + 2])
            .IsEqualTo(simulatedPoints[(copies[1] * 3) + 2]);
        SilkDeformationResult result = scene.ReplacePoints("/Mesh", 0, simulatedPoints, out ulong meshId);
        await Assert.That(result).IsEqualTo(SilkDeformationResult.Applied);
        SilkMeshData simulated = scene.Meshes[meshId];
        await Assert.That(simulated.Points.Span.SequenceEqual(simulatedPoints)).IsTrue();
        await Assert.That(simulated.AuthoredNormals.IsEmpty).IsTrue();
        await Assert.That(simulated.PointOrigins.Span.SequenceEqual(originalOrigins)).IsTrue();
        await Assert.That(simulated.TopologyFingerprint).IsEqualTo(original.TopologyFingerprint);
        await Assert.That(simulated.TopologyRevision).IsEqualTo(original.TopologyRevision);
        await AssertCoplanarSeamNormals(simulated, -0.4472136f, 0.8944272f, originalUvs);

        await Assert.That(scene.RestoreAuthoredPoints(meshId)).IsTrue();
        await Assert.That(scene.Meshes[meshId]).IsSameReferenceAs(original);
        await Assert.That(scene.Meshes[meshId].Points.Span.SequenceEqual(originalPoints)).IsTrue();
        await Assert.That(scene.Meshes[meshId].AuthoredNormals.Span.SequenceEqual(originalNormals)).IsTrue();
        await AssertCoplanarSeamNormals(scene.Meshes[meshId], 0, 1, originalUvs);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DivergentEmittedCopiesRemainAcceptedAndRestoreTheAuthoredMesh(bool authored, bool bounded)
    {
        string geometry = CoarseMeshNormalFixture.Mesh(
            "catmullClark", true, true, authored ? CoarseMeshNormalFixture.SharedNormals : string.Empty);
        using Fixture fixture = Create(geometry);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();
        using OpenUsdSilkPage page = SyncMesh(session, RenderComplexity.Low, bounded);
        scene.Apply(page);

        SilkMeshData original = scene.Meshes.Values.Single();
        float[] originalPoints = original.Points.ToArray();
        float[] originalNormals = original.AuthoredNormals.ToArray();
        int[] originalOrigins = original.PointOrigins.ToArray();
        var copies = new List<int>();
        for (int vertex = 0; vertex < originalPoints.Length / 3; vertex++)
        {
            int component = vertex * 3;
            if (originalPoints[component] == -0.6f &&
                originalPoints[component + 1] == -0.6f &&
                originalPoints[component + 2] == 0)
            {
                copies.Add(vertex);
            }
        }
        await Assert.That(copies.Count).IsEqualTo(2);
        await Assert.That(originalNormals.Length).IsEqualTo(authored ? originalPoints.Length : 0);

        float[] simulatedPoints = originalPoints.ToArray();
        simulatedPoints[(copies[0] * 3) + 2] = 0.2f;
        simulatedPoints[(copies[1] * 3) + 2] = -0.3f;
        SilkDeformationResult result = scene.ReplacePoints("/Mesh", 0, simulatedPoints, out ulong meshId);
        await Assert.That(result).IsEqualTo(SilkDeformationResult.Applied);
        SilkMeshData simulated = scene.Meshes[meshId];
        await Assert.That(simulated.Points.Span.SequenceEqual(simulatedPoints)).IsTrue();
        await Assert.That(simulated.AuthoredNormals.IsEmpty).IsTrue();
        await Assert.That(simulated.Indices.Span.SequenceEqual(original.Indices.Span)).IsTrue();
        await Assert.That(simulated.TriangleSubprims.Span.SequenceEqual(original.TriangleSubprims.Span)).IsTrue();
        await Assert.That(simulated.PointOrigins.Span.SequenceEqual(originalOrigins)).IsTrue();
        await Assert.That(simulated.TopologyFingerprint).IsEqualTo(original.TopologyFingerprint);
        await Assert.That(simulated.TopologyRevision).IsEqualTo(original.TopologyRevision);

        await Assert.That(scene.RestoreAuthoredPoints(meshId)).IsTrue();
        await Assert.That(scene.Meshes[meshId]).IsSameReferenceAs(original);
        await Assert.That(scene.Meshes[meshId].Points.Span.SequenceEqual(originalPoints)).IsTrue();
        await Assert.That(scene.Meshes[meshId].AuthoredNormals.Span.SequenceEqual(originalNormals)).IsTrue();
        await Assert.That(scene.RestoreAuthoredPoints(meshId)).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DistinctCoarseNormalSourcesHaveIdenticalVisibleTriangles(bool hiddenFace, bool bounded)
    {
        string first = CoarseMeshNormalFixture.Mesh(
            hiddenFace ? "catmullClark" : "none", true, false, string.Empty, holeSecondFace: hiddenFace);
        string second = hiddenFace ? """
            def Mesh "Mesh" {
                uniform token subdivisionScheme = "catmullClark"
                point3f[] points = [(-0.6,-0.6,0), (0.6,-0.6,0), (-0.6,0.6,0), (-0.6,-0.6,1.2)]
                int[] faceVertexCounts = [3]
                int[] faceVertexIndices = [0,1,2]
            }
            """ : CoarseMeshNormalFixture.Mesh("catmullClark", true, false, string.Empty);
        using Fixture firstFixture = Create(first);
        using Fixture secondFixture = Create(second);
        await using UsdStageScheduler firstScheduler = UsdStageScheduler.Open(firstFixture.Path);
        await using UsdStageScheduler secondScheduler = UsdStageScheduler.Open(secondFixture.Path);
        using UsdStageRenderSource firstSource = await firstScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource secondSource = await secondScheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession firstSession = OpenUsdSilkRuntime.Create(firstFixture.Plugins, firstSource);
        using OpenUsdSilkSession secondSession = OpenUsdSilkRuntime.Create(secondFixture.Plugins, secondSource);
        using var firstScene = new SilkSceneState();
        using var secondScene = new SilkSceneState();
        using OpenUsdSilkPage firstPage = SyncMesh(firstSession, RenderComplexity.Low, bounded);
        using OpenUsdSilkPage secondPage = SyncMesh(secondSession, RenderComplexity.Low, bounded);
        firstScene.Apply(firstPage);
        secondScene.Apply(secondPage);

        SilkMeshData firstMesh = firstScene.Meshes.Values.Single();
        SilkMeshData secondMesh = secondScene.Meshes.Values.Single();
        float[] expected = hiddenFace
            ? [-0.6f, -0.6f, 0, 0.6f, -0.6f, 0, -0.6f, 0.6f, 0]
            : [
                -0.6f, -0.6f, 0, 0.6f, -0.6f, 0, -0.6f, 0.6f, 0,
                -0.6f, -0.6f, 0, -0.6f, -0.6f, 1.2f, 0.6f, -0.6f, 0
            ];
        int[] expectedSubprims = hiddenFace ? [0] : [0, 1];
        await Assert.That(first == second).IsFalse();
        await Assert.That(ExpandTrianglePoints(firstMesh).AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(ExpandTrianglePoints(secondMesh).AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(firstMesh.TriangleSubprims.Span.SequenceEqual(expectedSubprims)).IsTrue();
        await Assert.That(secondMesh.TriangleSubprims.Span.SequenceEqual(expectedSubprims)).IsTrue();
        await Assert.That(firstMesh.AuthoredNormals.IsEmpty).IsTrue();
        await Assert.That(secondMesh.AuthoredNormals.IsEmpty).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(FallbackNormalCases))]
    public async Task FallbackNormalsRespectSubdivisionAndPointIdentity(
        SilkGraphicsBackend backend, string scheme, bool sharedPoints, bool faceVarying)
    {
        bool smooth = (scheme is "catmullClark" or "loop") && sharedPoints;
        string geometry = CoarseMeshNormalFixture.Mesh(scheme, sharedPoints, faceVarying, string.Empty);
        using Fixture actualFixture = Create(geometry);
        using Fixture referenceFixture = Create(CoarseMeshNormalFixture.Mesh(
            scheme, sharedPoints, faceVarying,
            smooth ? CoarseMeshNormalFixture.SharedNormals : sharedPoints
                ? CoarseMeshNormalFixture.FlatCornerNormals : CoarseMeshNormalFixture.SeparateNormals));
        using Fixture incorrectFixture = Create(CoarseMeshNormalFixture.Mesh(
            scheme, sharedPoints, faceVarying,
            smooth ? CoarseMeshNormalFixture.FlatCornerNormals : sharedPoints
                ? CoarseMeshNormalFixture.SharedNormals : CoarseMeshNormalFixture.JoinedSeparateNormals));
        await using UsdStageScheduler actualScheduler = UsdStageScheduler.Open(actualFixture.Path);
        await using UsdStageScheduler referenceScheduler = UsdStageScheduler.Open(referenceFixture.Path);
        await using UsdStageScheduler incorrectScheduler = UsdStageScheduler.Open(incorrectFixture.Path);
        using UsdStageRenderSource actualSource = await actualScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource referenceSource = await referenceScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource incorrectSource = await incorrectScheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession actual = OpenUsdSilkRuntime.Create(actualFixture.Plugins, actualSource);
        using OpenUsdSilkSession reference = OpenUsdSilkRuntime.Create(referenceFixture.Plugins, referenceSource);
        using OpenUsdSilkSession incorrect = OpenUsdSilkRuntime.Create(incorrectFixture.Plugins, incorrectSource);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using var referenceRenderer = new SilkMeshRenderer(device);
        using var incorrectRenderer = new SilkMeshRenderer(device);

        byte[] initial = Capture(actual, device, renderer, enableShadows: false);
        byte[] expected = Capture(reference, device, referenceRenderer, enableShadows: false);
        byte[] incorrectPixels = Capture(incorrect, device, incorrectRenderer, enableShadows: false);
        SilkMeshData actualMesh = renderer.Scene.Meshes.Values.Single();
        await Assert.That(actualMesh.AuthoredNormals.IsEmpty).IsTrue();
        if (faceVarying)
        {
            await Assert.That(actualMesh.FindTexCoord("st")).IsNotNull();
            await Assert.That(actualMesh.Points.Length).IsEqualTo(18);
            int[] origins = sharedPoints ? [0, 1, 2, 0, 3, 1] : [0, 1, 2, 3, 4, 5];
            await Assert.That(actualMesh.PointOrigins.Span.SequenceEqual(origins)).IsTrue();
        }
        else
        {
            await Assert.That(actualMesh.FindTexCoord("st")).IsNull();
        }
        await Assert.That(CountDifferentPixels(expected, incorrectPixels)).IsGreaterThan(50);
        await AssertPixelsNear(initial, expected);

        ulong builds = renderer.GpuResources.Statistics.GeometryBuilds;
        await Assert.That(Capture(actual, device, renderer, enableShadows: false).SequenceEqual(initial)).IsTrue();
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsEqualTo(builds);
        UsdVec3f[] changedPoints = sharedPoints
            ? [new(-0.6f, -0.6f, 0), new(0.6f, -0.6f, 0), new(-0.6f, 0.6f, 0), new(-0.6f, -0.6f, 0.3f)]
            : [
                new(-0.6f, -0.6f, 0), new(0.6f, -0.6f, 0), new(-0.6f, 0.6f, 0),
                new(-0.6f, -0.6f, 0), new(-0.6f, -0.6f, 0.3f), new(0.6f, -0.6f, 0)
            ];
        UsdVec3f[] changedNormals = smooth
            ? [new(0, 0.242535625f, 0.9701425f), new(0, 0.242535625f, 0.9701425f), new(0, 0, 1), new(0, 1, 0)]
            : [new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 1, 0), new(0, 1, 0), new(0, 1, 0)];
        await actualScheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points", changedPoints));
        await referenceScheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            mesh.SetVec3fArray("points", changedPoints);
            mesh.SetVec3fArray("normals", changedNormals);
        });
        byte[] updated = Capture(actual, device, renderer, enableShadows: false);
        await AssertPixelsNear(updated, Capture(reference, device, referenceRenderer, enableShadows: false));
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsGreaterThan(builds);
        if (smooth)
        {
            await Assert.That(updated.SequenceEqual(initial)).IsFalse();
        }
        await Assert.That(await File.ReadAllTextAsync(actualFixture.Path)).IsEqualTo("#usda 1.0\n" + geometry);
        renderer.Dispose();
        referenceRenderer.Dispose();
        incorrectRenderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [MethodDataSource(nameof(NgonNormalCases))]
    public async Task CoarseNgonFallbackNormalsUseFaceAdjacency(
        SilkGraphicsBackend backend, bool faceVarying, bool bounded)
    {
        string geometry = CoarseMeshNormalFixture.NgonMesh("catmullClark", faceVarying, string.Empty);
        using Fixture actualFixture = Create(geometry);
        using Fixture referenceFixture = Create(CoarseMeshNormalFixture.NgonMesh(
            "catmullClark", faceVarying, CoarseMeshNormalFixture.NgonNormals));
        using Fixture incorrectFixture = Create(CoarseMeshNormalFixture.NgonMesh(
            "catmullClark", faceVarying, CoarseMeshNormalFixture.NgonTriangulatedNormals));
        await using UsdStageScheduler actualScheduler = UsdStageScheduler.Open(actualFixture.Path);
        await using UsdStageScheduler referenceScheduler = UsdStageScheduler.Open(referenceFixture.Path);
        await using UsdStageScheduler incorrectScheduler = UsdStageScheduler.Open(incorrectFixture.Path);
        using UsdStageRenderSource actualSource = await actualScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource referenceSource = await referenceScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource incorrectSource = await incorrectScheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession actual = OpenUsdSilkRuntime.Create(actualFixture.Plugins, actualSource);
        using OpenUsdSilkSession reference = OpenUsdSilkRuntime.Create(referenceFixture.Plugins, referenceSource);
        using OpenUsdSilkSession incorrect = OpenUsdSilkRuntime.Create(incorrectFixture.Plugins, incorrectSource);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using var referenceRenderer = new SilkMeshRenderer(device);
        using var incorrectRenderer = new SilkMeshRenderer(device);

        byte[] initial = Capture(actual, device, renderer, enableShadows: false, bounded: bounded);
        byte[] expected = Capture(reference, device, referenceRenderer, enableShadows: false, bounded: bounded);
        byte[] incorrectPixels = Capture(incorrect, device, incorrectRenderer, enableShadows: false, bounded: bounded);
        SilkMeshData actualMesh = renderer.Scene.Meshes.Values.Single();
        await Assert.That(actualMesh.AuthoredNormals.IsEmpty).IsTrue();
        await Assert.That(actualMesh.FindTexCoord("st") is not null).IsEqualTo(faceVarying);
        await Assert.That(CountDifferentPixels(expected, incorrectPixels)).IsGreaterThan(50);
        await AssertPixelsNear(initial, expected);

        ulong builds = renderer.GpuResources.Statistics.GeometryBuilds;
        await Assert.That(Capture(actual, device, renderer, enableShadows: false, bounded: bounded)
            .SequenceEqual(initial)).IsTrue();
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsEqualTo(builds);
        float[] retainedPoints = actualMesh.Points.ToArray();
        UsdVec3f[] changedPoints = CreateChangedNgonPoints(0.3f);
        await UpdateNgonReferenceAsync(referenceScheduler, changedPoints, 0.242535625f, 0.9701425f);

        float[] simulatedPoints = CreateSimulatedNgonPoints(retainedPoints, 0.3f, out int movedVertices);
        await Assert.That(movedVertices).IsGreaterThan(0);
        SilkDeformationResult mismatch = renderer.Scene.ReplacePoints(
            "/Mesh", 0, [0f], out ulong refusedId);
        await Assert.That(mismatch).IsEqualTo(SilkDeformationResult.VertexCountMismatch);
        await Assert.That(refusedId).IsEqualTo(actualMesh.Id);
        float[] invalidPoints = simulatedPoints.ToArray();
        invalidPoints[0] = float.NaN;
        SilkDeformationResult invalid = renderer.Scene.ReplacePoints(
            "/Mesh", 0, invalidPoints, out refusedId);
        await Assert.That(invalid).IsEqualTo(SilkDeformationResult.NonFiniteValue);
        await Assert.That(refusedId).IsEqualTo(actualMesh.Id);
        await Assert.That(renderer.Scene.Meshes[refusedId].Points.Span.SequenceEqual(retainedPoints)).IsTrue();
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsEqualTo(builds);
        await Assert.That(Capture(actual, device, renderer, enableShadows: false, bounded: bounded)
            .SequenceEqual(initial)).IsTrue();

        SilkDeformationResult result = renderer.Scene.ReplacePoints(
            "/Mesh", 0, simulatedPoints, out ulong meshId);
        await Assert.That(result).IsEqualTo(SilkDeformationResult.Applied);
        renderer.GpuResources.Apply(
            renderer.Scene, new SilkSceneDelta(new[] { meshId }, ReadOnlyMemory<ulong>.Empty));
        byte[] simulated = Capture(actual, device, renderer, enableShadows: false, bounded: bounded);
        await Assert.That(renderer.Scene.Meshes[meshId].Points.Span.SequenceEqual(simulatedPoints)).IsTrue();
        await AssertPixelsNear(
            simulated, Capture(reference, device, referenceRenderer, enableShadows: false, bounded: bounded));
        await Assert.That(simulated.SequenceEqual(initial)).IsFalse();
        ulong simulatedBuilds = renderer.GpuResources.Statistics.GeometryBuilds;
        await Assert.That(simulatedBuilds).IsGreaterThan(builds);

        await Assert.That(renderer.Scene.RestoreAuthoredPoints(meshId)).IsTrue();
        renderer.GpuResources.Apply(
            renderer.Scene, new SilkSceneDelta(new[] { meshId }, ReadOnlyMemory<ulong>.Empty));
        byte[] restored = Capture(actual, device, renderer, enableShadows: false, bounded: bounded);
        await Assert.That(renderer.Scene.Meshes[meshId].Points.Span.SequenceEqual(retainedPoints)).IsTrue();
        await AssertPixelsNear(restored, expected);
        await Assert.That(renderer.Scene.RestoreAuthoredPoints(meshId)).IsFalse();
        builds = renderer.GpuResources.Statistics.GeometryBuilds;
        await Assert.That(builds).IsGreaterThan(simulatedBuilds);

        await actualScheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points", changedPoints));
        if (bounded)
        {
            var refused = new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render | RenderPurpose.Proxy, "full", 1);
            await Assert.That(() => actual.Sync(64, 64, refused)).Throws<OpenUsdSilkException>();
            await Assert.That(renderer.Scene.Meshes.Values.Single().Points.Span.SequenceEqual(retainedPoints)).IsTrue();
            await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsEqualTo(builds);
        }
        byte[] updated = Capture(actual, device, renderer, enableShadows: false, bounded: bounded);
        await AssertPixelsNear(
            updated, Capture(reference, device, referenceRenderer, enableShadows: false, bounded: bounded));
        await Assert.That(updated.SequenceEqual(initial)).IsFalse();
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsGreaterThan(builds);
        await Assert.That(await File.ReadAllTextAsync(actualFixture.Path)).IsEqualTo("#usda 1.0\n" + geometry);
        renderer.Dispose();
        referenceRenderer.Dispose();
        incorrectRenderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [MethodDataSource(nameof(NonplanarNgonNormalCases))]
    public async Task NonplanarNgonNormalsUseEachOriginalFaceCorner(
        SilkGraphicsBackend backend, bool faceVarying, bool bounded)
    {
        string geometry = CoarseMeshNormalFixture.NgonMesh(
            "catmullClark", faceVarying, string.Empty, skewed: true);
        using Fixture actualFixture = Create(geometry);
        using Fixture referenceFixture = Create(CoarseMeshNormalFixture.NgonMesh(
            "catmullClark", faceVarying, CoarseMeshNormalFixture.SkewedNgonCornerNormals, skewed: true));
        using Fixture incorrectFixture = Create(CoarseMeshNormalFixture.NgonMesh(
            "catmullClark", faceVarying, CoarseMeshNormalFixture.SkewedNgonFaceAreaNormals, skewed: true));
        await using UsdStageScheduler actualScheduler = UsdStageScheduler.Open(actualFixture.Path);
        await using UsdStageScheduler referenceScheduler = UsdStageScheduler.Open(referenceFixture.Path);
        await using UsdStageScheduler incorrectScheduler = UsdStageScheduler.Open(incorrectFixture.Path);
        using UsdStageRenderSource actualSource = await actualScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource referenceSource = await referenceScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource incorrectSource = await incorrectScheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession actual = OpenUsdSilkRuntime.Create(actualFixture.Plugins, actualSource);
        using OpenUsdSilkSession reference = OpenUsdSilkRuntime.Create(referenceFixture.Plugins, referenceSource);
        using OpenUsdSilkSession incorrect = OpenUsdSilkRuntime.Create(incorrectFixture.Plugins, incorrectSource);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using var referenceRenderer = new SilkMeshRenderer(device);
        using var incorrectRenderer = new SilkMeshRenderer(device);

        byte[] expected = Capture(reference, device, referenceRenderer, enableShadows: false, bounded: bounded);
        byte[] incorrectPixels = Capture(incorrect, device, incorrectRenderer, enableShadows: false, bounded: bounded);
        await Assert.That(CountDifferentPixels(expected, incorrectPixels)).IsGreaterThan(0);
        await AssertPixelsNear(
            Capture(actual, device, renderer, enableShadows: false, bounded: bounded), expected);

        renderer.Dispose();
        referenceRenderer.Dispose();
        incorrectRenderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(await File.ReadAllTextAsync(actualFixture.Path)).IsEqualTo("#usda 1.0\n" + geometry);
    }

    [Test]
    [MethodDataSource(nameof(FlatNonplanarNgonNormalCases))]
    public async Task NonplanarPolygonFlatNormalsUseEachEmittedTriangle(
        SilkGraphicsBackend backend, string scheme, bool faceVarying, bool bounded)
    {
        string geometry = CoarseMeshNormalFixture.NgonMesh(scheme, faceVarying, string.Empty, skewed: true);
        using Fixture actualFixture = Create(geometry);
        using Fixture referenceFixture = Create(CoarseMeshNormalFixture.NgonMesh(
            scheme, faceVarying, CoarseMeshNormalFixture.SkewedNgonTriangulatedFlatNormals,
            skewed: true, triangulated: true));
        using Fixture incorrectFixture = Create(CoarseMeshNormalFixture.NgonMesh(
            scheme, faceVarying, CoarseMeshNormalFixture.SkewedNgonFlatNormals, skewed: true));
        await using UsdStageScheduler actualScheduler = UsdStageScheduler.Open(actualFixture.Path);
        await using UsdStageScheduler referenceScheduler = UsdStageScheduler.Open(referenceFixture.Path);
        await using UsdStageScheduler incorrectScheduler = UsdStageScheduler.Open(incorrectFixture.Path);
        using UsdStageRenderSource actualSource = await actualScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource referenceSource = await referenceScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource incorrectSource = await incorrectScheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession actual = OpenUsdSilkRuntime.Create(actualFixture.Plugins, actualSource);
        using OpenUsdSilkSession reference = OpenUsdSilkRuntime.Create(referenceFixture.Plugins, referenceSource);
        using OpenUsdSilkSession incorrect = OpenUsdSilkRuntime.Create(incorrectFixture.Plugins, incorrectSource);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using var referenceRenderer = new SilkMeshRenderer(device);
        using var incorrectRenderer = new SilkMeshRenderer(device);

        byte[] initial = Capture(actual, device, renderer, enableShadows: false, bounded: bounded);
        byte[] expected = Capture(reference, device, referenceRenderer, enableShadows: false, bounded: bounded);
        byte[] incorrectPixels = Capture(incorrect, device, incorrectRenderer, enableShadows: false, bounded: bounded);
        await Assert.That(CountDifferentPixels(expected, incorrectPixels)).IsGreaterThan(0);
        await AssertPixelsNear(initial, expected);

        SilkMeshData mesh = renderer.Scene.Meshes.Values.Single();
        await Assert.That(mesh.AuthoredNormals.IsEmpty).IsTrue();
        float[] originalPoints = mesh.Points.ToArray();
        float[] simulatedPoints = originalPoints.ToArray();
        int movedVertices = 0;
        for (int index = 0; index < simulatedPoints.Length; index += 3)
        {
            if (MathF.Abs(simulatedPoints[index]) < 0.00001f &&
                MathF.Abs(simulatedPoints[index + 1] - 0.4f) < 0.00001f &&
                MathF.Abs(simulatedPoints[index + 2] - 0.8f) < 0.00001f)
            {
                simulatedPoints[index + 2] = 0.4f;
                movedVertices++;
            }
        }
        await Assert.That(movedVertices).IsGreaterThan(0);
        await referenceScheduler.InvokeAsync(stage =>
        {
            UsdPrim referenceMesh = stage.GetPrim("/Mesh");
            referenceMesh.SetVec3fArray("points",
            [
                new(-0.8f, -0.4f, 0), new(0.8f, -0.4f, 0), new(0, 0.4f, 0.4f),
                new(-0.8f, 0.4f, 0), new(-0.8f, -0.4f, 0.8f)
            ]);
            referenceMesh.SetVec3fArray("normals",
            [
                new(0, -0.4472136f, 0.8944272f),
                new(0, -0.4472136f, 0.8944272f),
                new(0, -0.4472136f, 0.8944272f),
                new(-0.4472136f, 0, 0.8944272f),
                new(-0.4472136f, 0, 0.8944272f),
                new(-0.4472136f, 0, 0.8944272f),
                new(0, 1, 0), new(0, 1, 0), new(0, 1, 0)
            ]);
        });

        ulong builds = renderer.GpuResources.Statistics.GeometryBuilds;
        SilkDeformationResult result = renderer.Scene.ReplacePoints(
            "/Mesh", 0, simulatedPoints, out ulong meshId);
        await Assert.That(result).IsEqualTo(SilkDeformationResult.Applied);
        renderer.GpuResources.Apply(
            renderer.Scene, new SilkSceneDelta(new[] { meshId }, ReadOnlyMemory<ulong>.Empty));
        byte[] simulated = Capture(actual, device, renderer, enableShadows: false, bounded: bounded);
        await Assert.That(renderer.Scene.Meshes[meshId].Points.Span.SequenceEqual(simulatedPoints)).IsTrue();
        await AssertPixelsNear(
            simulated, Capture(reference, device, referenceRenderer, enableShadows: false, bounded: bounded));
        await Assert.That(simulated.SequenceEqual(initial)).IsFalse();
        ulong simulatedBuilds = renderer.GpuResources.Statistics.GeometryBuilds;
        await Assert.That(simulatedBuilds).IsGreaterThan(builds);

        await Assert.That(renderer.Scene.RestoreAuthoredPoints(meshId)).IsTrue();
        renderer.GpuResources.Apply(
            renderer.Scene, new SilkSceneDelta(new[] { meshId }, ReadOnlyMemory<ulong>.Empty));
        byte[] restored = Capture(actual, device, renderer, enableShadows: false, bounded: bounded);
        await Assert.That(renderer.Scene.Meshes[meshId].Points.Span.SequenceEqual(originalPoints)).IsTrue();
        await AssertPixelsNear(restored, expected);
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsGreaterThan(simulatedBuilds);
        await Assert.That(await File.ReadAllTextAsync(actualFixture.Path)).IsEqualTo("#usda 1.0\n" + geometry);
        renderer.Dispose();
        referenceRenderer.Dispose();
        incorrectRenderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [MethodDataSource(nameof(SimulatedNgonNormalCases))]
    public async Task CoarseNgonSimulationRecomputesAuthoredNormalsFromFaceAdjacency(
        SilkGraphicsBackend backend, bool faceVarying)
    {
        string geometry = CoarseMeshNormalFixture.NgonMesh(
            "catmullClark", faceVarying, CoarseMeshNormalFixture.NgonNormals);
        using Fixture actualFixture = Create(geometry);
        using Fixture referenceFixture = Create(geometry);
        using Fixture incorrectFixture = Create(CoarseMeshNormalFixture.NgonMesh(
            "catmullClark", faceVarying, CoarseMeshNormalFixture.HalfHeightNgonTriangulatedNormals));
        await using UsdStageScheduler actualScheduler = UsdStageScheduler.Open(actualFixture.Path);
        await using UsdStageScheduler referenceScheduler = UsdStageScheduler.Open(referenceFixture.Path);
        await using UsdStageScheduler incorrectScheduler = UsdStageScheduler.Open(incorrectFixture.Path);
        using UsdStageRenderSource actualSource = await actualScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource referenceSource = await referenceScheduler.AcquireRenderSourceAsync();
        using UsdStageRenderSource incorrectSource = await incorrectScheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession actual = OpenUsdSilkRuntime.Create(actualFixture.Plugins, actualSource);
        using OpenUsdSilkSession reference = OpenUsdSilkRuntime.Create(referenceFixture.Plugins, referenceSource);
        using OpenUsdSilkSession incorrect = OpenUsdSilkRuntime.Create(incorrectFixture.Plugins, incorrectSource);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using var referenceRenderer = new SilkMeshRenderer(device);
        using var incorrectRenderer = new SilkMeshRenderer(device);

        byte[] initial = Capture(actual, device, renderer, enableShadows: false);
        await AssertPixelsNear(initial, Capture(reference, device, referenceRenderer, enableShadows: false));
        SilkMeshData authored = renderer.Scene.Meshes.Values.Single();
        await Assert.That(authored.AuthoredNormals.IsEmpty).IsFalse();
        float[] originalPoints = authored.Points.ToArray();
        float[] simulatedPoints = CreateSimulatedNgonPoints(originalPoints, 0.6f, out int movedVertices);
        await Assert.That(movedVertices).IsGreaterThan(0);
        UsdVec3f[] changedPoints = CreateChangedNgonPoints(0.6f);
        await UpdateNgonReferenceAsync(referenceScheduler, changedPoints, 0.4472136f, 0.8944272f);
        await incorrectScheduler.InvokeAsync(
            stage => stage.GetPrim("/Mesh").SetVec3fArray("points", changedPoints));

        ulong builds = renderer.GpuResources.Statistics.GeometryBuilds;
        SilkDeformationResult result = renderer.Scene.ReplacePoints(
            "/Mesh", 0, simulatedPoints, out ulong meshId);
        await Assert.That(result).IsEqualTo(SilkDeformationResult.Applied);
        await Assert.That(renderer.Scene.Meshes[meshId].AuthoredNormals.IsEmpty).IsTrue();
        renderer.GpuResources.Apply(
            renderer.Scene, new SilkSceneDelta(new[] { meshId }, ReadOnlyMemory<ulong>.Empty));
        byte[] simulated = Capture(actual, device, renderer, enableShadows: false);
        await Assert.That(renderer.Scene.Meshes[meshId].Points.Span.SequenceEqual(simulatedPoints)).IsTrue();
        byte[] expected = Capture(reference, device, referenceRenderer, enableShadows: false);
        byte[] incorrectPixels = Capture(incorrect, device, incorrectRenderer, enableShadows: false);
        await Assert.That(CountDifferentPixels(expected, incorrectPixels)).IsGreaterThan(0);
        await AssertPixelsNear(simulated, expected);
        await Assert.That(simulated.SequenceEqual(initial)).IsFalse();
        ulong simulatedBuilds = renderer.GpuResources.Statistics.GeometryBuilds;
        await Assert.That(simulatedBuilds).IsGreaterThan(builds);

        await Assert.That(renderer.Scene.RestoreAuthoredPoints(meshId)).IsTrue();
        renderer.GpuResources.Apply(
            renderer.Scene, new SilkSceneDelta(new[] { meshId }, ReadOnlyMemory<ulong>.Empty));
        byte[] restored = Capture(actual, device, renderer, enableShadows: false);
        await Assert.That(renderer.Scene.Meshes[meshId].Points.Span.SequenceEqual(originalPoints)).IsTrue();
        await Assert.That(renderer.Scene.Meshes[meshId].AuthoredNormals.IsEmpty).IsFalse();
        await Assert.That(restored.SequenceEqual(initial)).IsTrue();
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds).IsGreaterThan(simulatedBuilds);
        renderer.Dispose();
        referenceRenderer.Dispose();
        incorrectRenderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    public static IEnumerable<SilkGraphicsBackend> NormalBackends()
    {
        if (OperatingSystem.IsWindows())
        {
            return [SilkGraphicsBackend.D3D12, SilkGraphicsBackend.Vulkan];
        }
        if (OperatingSystem.IsLinux())
        {
            return [SilkGraphicsBackend.Vulkan];
        }
        if (OperatingSystem.IsMacOS())
        {
            return [SilkGraphicsBackend.Metal];
        }
        throw new PlatformNotSupportedException("Normal conformance requires D3D12, Vulkan, or Metal.");
    }

    public static IEnumerable<(SilkGraphicsBackend, string, bool, bool)> FallbackNormalCases()
    {
        foreach (SilkGraphicsBackend backend in NormalBackends())
        {
            yield return (backend, "none", true, false);
            yield return (backend, "none", true, true);
            yield return (backend, "none", false, true);
            yield return (backend, "bilinear", true, false);
            yield return (backend, "bilinear", true, true);
            yield return (backend, "catmullClark", true, false);
            yield return (backend, "catmullClark", true, true);
            yield return (backend, "catmullClark", false, false);
            yield return (backend, "catmullClark", false, true);
            yield return (backend, "loop", true, false);
            yield return (backend, "loop", true, true);
            yield return (backend, "loop", false, true);
        }
    }

    public static IEnumerable<(SilkGraphicsBackend, bool, bool)> NgonNormalCases()
    {
        foreach (SilkGraphicsBackend backend in NormalBackends())
        {
            yield return (backend, false, false);
            yield return (backend, true, false);
            yield return (backend, false, true);
            yield return (backend, true, true);
        }
    }

    public static IEnumerable<(SilkGraphicsBackend, bool, bool)> NonplanarNgonNormalCases()
    {
        foreach (SilkGraphicsBackend backend in NormalBackends())
        {
            yield return (backend, false, false);
            yield return (backend, true, false);
            yield return (backend, false, true);
            yield return (backend, true, true);
        }
    }

    public static IEnumerable<(SilkGraphicsBackend, string, bool, bool)> FlatNonplanarNgonNormalCases()
    {
        foreach (SilkGraphicsBackend backend in NormalBackends())
        {
            foreach (string scheme in new[] { "none", "bilinear" })
            {
                yield return (backend, scheme, false, false);
                yield return (backend, scheme, true, false);
                yield return (backend, scheme, false, true);
                yield return (backend, scheme, true, true);
            }
        }
    }

    public static IEnumerable<(SilkGraphicsBackend, bool)> SimulatedNgonNormalCases()
    {
        foreach (SilkGraphicsBackend backend in NormalBackends())
        {
            yield return (backend, false);
            yield return (backend, true);
        }
    }

    private static UsdVec3f[] CreateChangedNgonPoints(float height) =>
    [
        new(-0.6f, -0.6f, 0), new(0.6f, -0.6f, 0), new(0.6f, 0.6f, 0),
        new(-0.6f, 0.6f, 0), new(-0.6f, -0.6f, height)
    ];

    private static ValueTask UpdateNgonReferenceAsync(
        UsdStageScheduler scheduler, UsdVec3f[] points, float normalY, float normalZ) =>
        scheduler.InvokeAsync(stage =>
        {
            UsdPrim mesh = stage.GetPrim("/Mesh");
            mesh.SetVec3fArray("points", points);
            mesh.SetVec3fArray("normals", [
                new(0, normalY, normalZ), new(0, normalY, normalZ),
                new(0, 0, 1), new(0, 0, 1), new(0, 1, 0)
            ]);
        });

    private static float[] CreateSimulatedNgonPoints(
        ReadOnlySpan<float> points, float height, out int movedVertices)
    {
        float[] simulated = points.ToArray();
        movedVertices = 0;
        for (int component = 2; component < simulated.Length; component += 3)
        {
            if (simulated[component] == 1.2f)
            {
                simulated[component] = height;
                movedVertices++;
            }
        }
        return simulated;
    }

    private static async Task AssertSmallTriangleNormals(SilkMeshData mesh, float sharedY, float sharedZ)
    {
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        float[] expected =
        [
            0, sharedY, sharedZ,
            0, sharedY, sharedZ,
            0, 0, 1,
            0, 0, 1,
        ];
        await Assert.That(geometry.Vertices.Length).IsEqualTo(expected.Length * 2);
        for (int vertex = 0; vertex < expected.Length / 3; vertex++)
        {
            for (int component = 0; component < 3; component++)
            {
                await Assert.That(geometry.Vertices[(vertex * 6) + 3 + component])
                    .IsEqualTo(expected[(vertex * 3) + component]).Within(1e-6f);
            }
        }
    }

    private static async Task AssertCoplanarSeamNormals(
        SilkMeshData mesh, float normalY, float normalZ, float[] expectedUvs)
    {
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh, "st");
        await Assert.That(geometry.Vertices.Length).IsEqualTo(48);
        await Assert.That(expectedUvs.Length).IsEqualTo(12);
        for (int vertex = 0; vertex < 6; vertex++)
        {
            int offset = vertex * 8;
            await Assert.That(geometry.Vertices[offset + 3]).IsEqualTo(0f).Within(1e-6f);
            await Assert.That(geometry.Vertices[offset + 4]).IsEqualTo(normalY).Within(1e-6f);
            await Assert.That(geometry.Vertices[offset + 5]).IsEqualTo(normalZ).Within(1e-6f);
            await Assert.That(geometry.Vertices[offset + 6]).IsEqualTo(expectedUvs[vertex * 2]);
            await Assert.That(geometry.Vertices[offset + 7]).IsEqualTo(expectedUvs[(vertex * 2) + 1]);
        }
    }

    private static float[] ExpandTrianglePoints(SilkMeshData mesh)
    {
        ReadOnlySpan<float> points = mesh.Points.Span;
        ReadOnlySpan<uint> indices = mesh.Indices.Span;
        float[] expanded = new float[checked(indices.Length * 3)];
        for (int corner = 0; corner < indices.Length; corner++)
        {
            int source = checked((int)indices[corner] * 3);
            points.Slice(source, 3).CopyTo(expanded.AsSpan(corner * 3, 3));
        }
        return expanded;
    }

    private static int CountDifferentPixels(byte[] first, byte[] second) =>
        Enumerable.Range(0, first.Length / 4).Count(index =>
            Enumerable.Range(0, 3).Any(channel =>
                Math.Abs(first[(index * 4) + channel] - second[(index * 4) + channel]) > 5));

    private static async Task AssertPixelsNear(byte[] actual, byte[] expected)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        int largestDifference = Enumerable.Range(0, actual.Length)
            .Max(index => Math.Abs(actual[index] - expected[index]));
        await Assert.That(largestDifference).IsLessThanOrEqualTo(2);
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
        RenderComplexity complexity = RenderComplexity.Low, bool enableShadows = true, bool bounded = false)
    {
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        var settings = new RenderSettings(1, true, enableShadows, new Vector4(0, 0, 0, 1),
            backfaceCulling: true, useSceneMaterials: true, complexity: complexity);
        return SilkFrameCapture.CaptureCore(
            session, device, renderer, 64, 64, settings, 0, camera, ingestionOptions: bounded ? Options : null)
            .Rgba.ToArray();
    }

    private static string AreaWeightedMesh(string normals) => $$"""
        def Mesh "Mesh" {
            uniform token subdivisionScheme = "catmullClark"
            point3f[] points = [(-0.6,-0.6,0), (0.6,-0.6,0), (-0.6,0.6,0), (-0.6,-0.6,0.3)]
            int[] faceVertexCounts = [3,3]
            int[] faceVertexIndices = [0,1,2,0,3,1]
            {{normals}}
        }
        def DistantLight "Key" {
            float inputs:intensity = 1
        }
        """;

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

    private const string SimpleRefinedMesh = """
        def Mesh "Mesh" {
            uniform token subdivisionScheme = "catmullClark"
            point3f[] points = [(-0.5,-0.5,0), (0.5,-0.5,0), (0.5,0.5,0), (-0.5,0.5,0)]
            int[] faceVertexCounts = [4]
            int[] faceVertexIndices = [0,1,2,3]
        }
        """;

    private const string ConstantNormalRefinedMesh = """
        def Mesh "Mesh" {
            uniform token subdivisionScheme = "catmullClark"
            point3f[] points = [(-0.5,-0.5,0), (0.5,-0.5,0), (0.5,0.5,0), (-0.5,0.5,0)]
            int[] faceVertexCounts = [4]
            int[] faceVertexIndices = [0,1,2,3]
            normal3f[] normals = [(0,0,1)] (interpolation = "constant")
            texCoord2f[] primvars:st = [(0,0), (1,0), (1,1), (0,1)] (interpolation = "faceVarying")
        }
        """;

    private const string VertexNormalRefinedMesh = """
        def Mesh "Mesh" {
            uniform token subdivisionScheme = "catmullClark"
            point3f[] points = [(-0.5,-0.5,0), (0.5,-0.5,0), (0.5,0.5,0), (-0.5,0.5,0)]
            int[] faceVertexCounts = [4]
            int[] faceVertexIndices = [0,1,2,3]
            normal3f[] normals = [
                (-0.2,0,1), (0.2,0,1), (0,0.2,1), (0,-0.2,1)
            ] (interpolation = "vertex")
        }
        """;
}
