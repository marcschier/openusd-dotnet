// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkPointPreparationNativeTests
{
    private const ulong PointCost = 3 * 48;

    [Test]
    public async Task ExactBoundaryMatchesLegacyPositionsAndAuthoredIdentities()
    {
        using var fixture = new Fixture(Points);
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage legacyPage = session.Sync(16, 16);
        var legacy = new SilkSceneState();
        legacy.Apply(legacyPage);
        SilkMeshData expected = legacy.Meshes.Values.Single();
        await Assert.That(expected.Path).IsEqualTo("/Points");
        await Assert.That(expected.TopologyKind).IsEqualTo(SilkTopologyKind.PointList);
        await Assert.That(expected.Points.Span.SequenceEqual(
            new float[] { -0.5f, -0.5f, 0, 0.5f, -0.5f, 0, 0, 0.5f, 0 })).IsTrue();
        await Assert.That(expected.Indices.Span.SequenceEqual(new uint[] { 0, 1, 2 })).IsTrue();
        await Assert.That(expected.TriangleSubprims.Span.SequenceEqual(new int[] { 0, 1, 2 })).IsTrue();
        await Assert.That(expected.PointOrigins.Span.SequenceEqual(new int[] { 0, 1, 2 })).IsTrue();
        await Assert.That(expected.AuthoredPointCount).IsEqualTo(3);
        await Assert.That(expected.SubprimIdentity).IsEqualTo(SilkSubprimIdentity.Point);
        await Assert.That(expected.SubprimUnsupported).IsEqualTo(SilkSubprimUnsupportedReason.TopologyMode);

        using OpenUsdSilkPage bounded = session.Sync(16, 16, Options(PointCost));
        await Assert.That(bounded.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);
        await Assert.That(bounded.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(PointCost);
        var accepted = new SilkSceneState();
        accepted.Apply(bounded);
        await AssertSameGeometry(expected, accepted.Meshes.Values.Single());
        await Assert.That(() => session.Sync(16, 16, Options(PointCost - 1)))
            .Throws<OpenUsdSilkException>()
            .WithMessageContaining("hdSilk mesh preparation refused");
        await AssertSameGeometry(expected, accepted.Meshes.Values.Single());
        using OpenUsdSilkPage retry = session.Sync(16, 16, Options(PointCost));
        await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);
        var retried = new SilkSceneState();
        retried.Apply(retry);
        await AssertSameGeometry(expected, retried.Meshes.Values.Single());
        using OpenUsdSilkPage quiet = session.Sync(16, 16, Options(PointCost));
        await Assert.That(quiet.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);
        int upserts = 0;
        using (SilkCommandEnumerator commands = quiet.GetEnumerator())
        {
            while (commands.MoveNext())
            {
                if (commands.Current.Type == SilkCommandType.MeshUpsert)
                {
                    upserts++;
                }
            }
        }
        await Assert.That(upserts).IsEqualTo(0);
    }

    [Test]
    public async Task GrowthRefusalPreservesTheOldPageAndRecoversAfterRemovalAndRestoration()
    {
        using var fixture = new Fixture(Points);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkPage initial = session.Sync(16, 16, Options(4096));
        var before = new SilkSceneState();
        before.Apply(initial);
        SilkMeshData expected = before.Meshes.Values.Single();
        await Assert.That(initial.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);
        UsdVec3f[] original = [new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0, 0.5f, 0)];
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Points").SetVec3fArray(
            "points", Enumerable.Repeat(new UsdVec3f(2, 3, 4), 1024).ToArray()));
        await Assert.That(() => session.Sync(16, 16, Options(4096)))
            .Throws<OpenUsdSilkException>()
            .WithMessageContaining("hdSilk mesh preparation refused");
        var stillReadable = new SilkSceneState();
        stillReadable.Apply(initial);
        await AssertSameGeometry(expected, stillReadable.Meshes.Values.Single());
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Points").SetVec3fArray("points", original));
        using OpenUsdSilkPage retry = session.Sync(16, 16, Options(4096));
        await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);
        var recovered = new SilkSceneState();
        recovered.Apply(retry);
        await AssertSameGeometry(expected, recovered.Meshes.Values.Single());

        await scheduler.InvokeAsync(stage => stage.RemovePrim("/Points"));
        using OpenUsdSilkPage removed = session.Sync(16, 16, Options(4096));
        await Assert.That(removed.MeshPreparationUsage!.ReservedBytes).IsEqualTo(0ul);
        await scheduler.InvokeAsync(stage =>
            stage.DefinePrim("/Points", "Points").SetVec3fArray("points", original));
        using OpenUsdSilkPage restored = session.Sync(16, 16, Options(4096));
        await Assert.That(restored.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);
        var replacement = new SilkSceneState();
        replacement.Apply(restored);
        await AssertSameGeometry(expected, replacement.Meshes.Values.Single());
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Points);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task BlockedPointsRetirePointPrimitiveAndRecover(SilkGraphicsBackend backend)
    {
        using var fixture = new Fixture(Points);
        using var emptyFixture = new Fixture(string.Empty);
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
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Points").GetAttribute("points").BlockValue());
        byte[] blocked = Capture(session, device, renderer);
        await Assert.That(renderer.Scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(renderer.GpuResources.MeshValues.Count).IsEqualTo(0);
        await Assert.That(blocked.SequenceEqual(background)).IsTrue();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Points").SetVec3fArray(
            "points", [new(-0.25f, -0.5f, 0), new(0.25f, -0.5f, 0), new(0, 0.25f, 0)]));
        byte[] recovered = Capture(session, device, renderer);
        await Assert.That(renderer.Scene.Meshes.Values.Single().Points.Span.SequenceEqual(
            new float[] { -0.25f, -0.5f, 0, 0.25f, -0.5f, 0, 0, 0.25f, 0 })).IsTrue();
        await Assert.That(recovered.SequenceEqual(initial)).IsFalse();
        await Assert.That(recovered.SequenceEqual(background)).IsFalse();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Points").SetVec3fArray(
            "points", [new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0, 0.5f, 0)]));
        await Assert.That(Capture(session, device, renderer).SequenceEqual(initial)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Points);
        renderer.Dispose();
        emptyRenderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task BlockedPrototypeRetiresEveryPointInstanceAndRecovers(SilkGraphicsBackend backend)
    {
        using var fixture = new Fixture(InstancedPoints);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);

        byte[] initial = Capture(session, device, renderer);
        await Assert.That(renderer.Scene.Meshes.Values.Count(
            mesh => mesh.Path.StartsWith("/Instances/Points/", StringComparison.Ordinal))).IsEqualTo(2);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Instances/Points").GetAttribute("points").BlockValue());
        byte[] blocked = Capture(session, device, renderer);
        await Assert.That(renderer.Scene.Meshes.Values.Any(
            mesh => mesh.Path.StartsWith("/Instances/Points/", StringComparison.Ordinal))).IsFalse();
        await Assert.That(renderer.Scene.Meshes.Values.Any(
            mesh => mesh.Path.StartsWith("/Instances/Other/", StringComparison.Ordinal))).IsTrue();
        await Assert.That(blocked.SequenceEqual(initial)).IsFalse();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Instances/Points").SetVec3fArray(
            "points", [new(0, 0, 0), new(0, 0.5f, 0)]));
        byte[] recovered = Capture(session, device, renderer);
        await Assert.That(renderer.Scene.Meshes.Values.Count(
            mesh => mesh.Path.StartsWith("/Instances/Points/", StringComparison.Ordinal))).IsEqualTo(2);
        await Assert.That(recovered.SequenceEqual(initial)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + InstancedPoints);
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task BoundedBlockedPointsRefuseAndRecoverWithoutReplacingRetainedGeometry()
    {
        using var fixture = new Fixture(Points);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkPage initial = session.Sync(16, 16, Options(4096));
        using var scene = new SilkSceneState();
        scene.Apply(initial);
        SilkMeshData expected = scene.Meshes.Values.Single();
        await Assert.That(initial.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Points").GetAttribute("points").BlockValue());
        await Assert.That(() => session.Sync(16, 16, Options(4096)))
            .Throws<OpenUsdSilkException>()
            .WithMessageContaining("requires resolved float3 or double3 points");
        using var retained = new SilkSceneState();
        retained.Apply(initial);
        await AssertSameGeometry(expected, retained.Meshes.Values.Single());
        await AssertSameGeometry(expected, scene.Meshes.Values.Single());

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Points").SetVec3fArray(
            "points", [new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0, 0.5f, 0)]));
        using OpenUsdSilkPage retry = session.Sync(16, 16, Options(4096));
        await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);
        scene.Apply(retry);
        await AssertSameGeometry(expected, scene.Meshes.Values.Single());
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Points);
    }

    [Test]
    public async Task EmptyPointsCostNothingAndCanBePopulatedAfterward()
    {
        using var fixture = new Fixture("""
            def Points "Points" {
                point3f[] points = []
            }
            """);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkPage empty = session.Sync(16, 16, Options(1));
        await Assert.That(empty.MeshPreparationUsage!.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(empty.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(0ul);
        var scene = new SilkSceneState();
        scene.Apply(empty);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Points").SetVec3fArray(
            "points", [new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0, 0.5f, 0)]));
        using OpenUsdSilkPage populated = session.Sync(16, 16, Options(PointCost));
        scene.Apply(populated);
        await Assert.That(populated.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);
        await Assert.That(scene.Meshes.Values.Single().PointOrigins.Span.SequenceEqual(
            new int[] { 0, 1, 2 })).IsTrue();
    }

    [Test]
    public async Task VisibilityRemovalAndRestorationKeepTheRetainedSourceReservation()
    {
        using var fixture = new Fixture(Points);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkPage initial = session.Sync(16, 16, Options(4096));
        var scene = new SilkSceneState();
        scene.Apply(initial);
        SilkMeshData expected = scene.Meshes.Values.Single();
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Points").SetToken("visibility", "invisible"));
        using OpenUsdSilkPage hidden = session.Sync(16, 16, Options(4096));
        scene.Apply(hidden);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(hidden.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Points").SetToken("visibility", "inherited"));
        using OpenUsdSilkPage visible = session.Sync(16, 16, Options(4096));
        scene.Apply(visible);
        await Assert.That(visible.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);
        await AssertSameGeometry(expected, scene.Meshes.Values.Single());
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Points);
    }

    [Test]
    public async Task IndependentPointsAggregateAndInstancesShareThePrototypeReservation()
    {
        using var pair = new Fixture(
            Points + "\n" + Points.Replace("\"Points\"", "\"Other\"", StringComparison.Ordinal));
        using OpenUsdSilkSession two = pair.Open();
        await Assert.That(() => two.Sync(16, 16, Options(PointCost * 2 - 1)))
            .Throws<OpenUsdSilkException>()
            .WithMessageContaining("hdSilk mesh preparation refused");
        using OpenUsdSilkPage accepted = two.Sync(16, 16, Options(PointCost * 2));
        await Assert.That(accepted.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost * 2);
        var scene = new SilkSceneState();
        scene.Apply(accepted);
        await Assert.That(scene.Meshes.Count).IsEqualTo(2);

        using var instances = new Fixture("""
            def PointInstancer "Instances" {
                rel prototypes = </Instances/Points>
                int[] protoIndices = [0,0,0]
                point3f[] positions = [(0,0,0),(2,0,0),(4,0,0)]
            """ + "\n" + Points + "\n}");
        using OpenUsdSilkSession instanced = instances.Open();
        using OpenUsdSilkPage page = instanced.Sync(16, 16, Options(PointCost));
        await Assert.That(page.MeshPreparationUsage!.ReservedBytes).IsEqualTo(PointCost);
        int payloads = 0;
        int references = 0;
        using (SilkCommandEnumerator commands = page.GetEnumerator())
        {
            while (commands.MoveNext())
            {
                if (commands.Current.Type == SilkCommandType.MeshUpsert)
                {
                    if (commands.Current.AsMeshUpsert().PointCount == 0)
                    {
                        references++;
                    }
                    else
                    {
                        payloads++;
                    }
                }
            }
        }
        await Assert.That(payloads).IsEqualTo(1);
        await Assert.That(references).IsEqualTo(2);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    public async Task NativePointComponentOutlineChangesVisiblePixels(
        SilkGraphicsBackend backend, bool visibleOnly)
    {
        using var fixture = new Fixture("""
            def Points "Points" {
                point3f[] points = [(0, 0.5, 0)]
            }
            """);
        using OpenUsdSilkSession session = fixture.Open();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(64, 64));
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));

        SilkPickFrameBinding binding = RenderPoints(session, renderer, color, depth, camera, 1);
        byte[] before = ReadColor(color);
        await Assert.That(RegionHasForeground(before, 32, 16)).IsTrue();
        renderer.UpdateSelection(
            new SelectionState([new SelectionItem("/Points", null, null, 0, SelectionElementKind.Point)]),
            new SilkSelectionOutlineSettings(true, new SilkColor(1, 0.55f, 0, 1), 2, visibleOnly));
        _ = renderer.Render(color, depth, binding);
        byte[] after = ReadColor(color);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status).IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(renderer.SelectionOutlineDiagnostics.ResolvedMeshCount).IsEqualTo(1);
        await Assert.That(RegionChanged(before, after, 32, 16)).IsTrue();
        await Assert.That(RegionChanged(before, after, 32, 48)).IsFalse();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task SparsePointInstanceSelectionSurvivesEditsRemovalAndRestoration(
        SilkGraphicsBackend backend)
    {
        using var fixture = new Fixture(InstancedPoints);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(64, 64));
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));

        SilkPickFrameBinding binding = RenderPoints(session, renderer, color, depth, camera, 1);
        _ = await AssertPointHit(session, renderer, color, depth, binding, 16, 32, 0, 0);
        _ = await AssertPointHit(session, renderer, color, depth, binding, 48, 32, 2, 0);
        _ = await AssertPointHit(session, renderer, color, depth, binding, 16, 16, 0, 1);
        SelectionItem selected = await AssertPointHit(session, renderer, color, depth, binding, 48, 16, 2, 1);
        byte[] beforeSelection = ReadColor(color);
        renderer.UpdateSelection(
            new SelectionState([selected]),
            new SilkSelectionOutlineSettings(true, new SilkColor(1, 0.55f, 0, 1), 2, true));
        _ = renderer.Render(color, depth, binding);
        byte[] selectedPixels = ReadColor(color);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status).IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(renderer.SelectionOutlineDiagnostics.ResolvedMeshCount).IsEqualTo(1);
        await Assert.That(RegionChanged(beforeSelection, selectedPixels, 48, 16)).IsTrue();
        await Assert.That(RegionChanged(beforeSelection, selectedPixels, 16, 16)).IsFalse();
        await Assert.That(RegionChanged(beforeSelection, selectedPixels, 48, 32)).IsFalse();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Instances/Points").SetVec3fArray(
            "points", [new(0, 0, 0), new(0, 0.25f, 0)]));
        binding = RenderPoints(session, renderer, color, depth, camera, 2);
        SelectionItem moved = await AssertPointHit(session, renderer, color, depth, binding, 48, 24, 2, 1);
        await Assert.That(moved).IsEqualTo(selected);
        await Assert.That((await PickNear(renderer, color, depth, binding, 48, 16)).Status)
            .IsEqualTo(RenderPickStatus.Miss);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status).IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(renderer.SelectionOutlineDiagnostics.ResolvedMeshCount).IsEqualTo(1);
        await AssertSelectionPixels(renderer, color, depth, binding, 48, 24, true, (16, 24), (48, 16));

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Instances").SetInt32Array("protoIndices", [0, 1, 1]));
        binding = RenderPoints(session, renderer, color, depth, camera, 3);
        await Assert.That((await PickNear(renderer, color, depth, binding, 48, 24)).Status)
            .IsEqualTo(RenderPickStatus.Miss);
        _ = await AssertPointHit(session, renderer, color, depth, binding, 16, 24, 0, 1);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.NoMatchingMeshes);
        await Assert.That(renderer.SelectionOutlineDiagnostics.ResolvedMeshCount).IsEqualTo(0);
        await AssertSelectionPixels(renderer, color, depth, binding, 48, 24, false);

        await scheduler.InvokeAsync(stage =>
        {
            stage.GetPrim("/Instances/Points").SetVec3fArray("points", [new(0, 0, 0), new(0, 0.5f, 0)]);
            stage.GetPrim("/Instances").SetInt32Array("protoIndices", [0, 1, 0]);
        });
        binding = RenderPoints(session, renderer, color, depth, camera, 4);
        SelectionItem restored = await AssertPointHit(session, renderer, color, depth, binding, 48, 16, 2, 1);
        await Assert.That(restored).IsEqualTo(selected);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status).IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(renderer.SelectionOutlineDiagnostics.ResolvedMeshCount).IsEqualTo(1);
        await AssertSelectionPixels(renderer, color, depth, binding, 48, 16, true, (16, 16), (48, 32));
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + InstancedPoints);

        renderer.Dispose();
        color.Dispose();
        depth.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task TimeSampledPrototypeAssignmentsKeepSelectionAcrossScrubbing(
        SilkGraphicsBackend backend)
    {
        using var fixture = new Fixture(InstancedPoints);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim instancer = stage.GetPrim("/Instances");
            instancer.SetInt32Array("protoIndices", [0, 1, 0], 1);
            instancer.SetInt32Array("protoIndices", [0, 1, 1], 2);
            instancer.SetInt32Array("protoIndices", [0, 1, 0], 3);
        });
        int[] sampledIndices = await scheduler.InvokeAsync(
            stage => stage.GetPrim("/Instances").GetInt32Array("protoIndices", 2));
        await Assert.That(sampledIndices.AsSpan().SequenceEqual([0, 1, 1])).IsTrue();

        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(64, 64));
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));

        SilkPickFrameBinding binding = RenderPoints(session, renderer, color, depth, camera, 1, 1);
        SelectionItem selected = await AssertPointHit(session, renderer, color, depth, binding, 48, 16, 2, 1);
        renderer.UpdateSelection(
            new SelectionState([selected]),
            new SilkSelectionOutlineSettings(true, new SilkColor(1, 0.55f, 0, 1), 2, true));
        _ = renderer.Render(color, depth, binding);
        await AssertSelectionPixels(renderer, color, depth, binding, 48, 16, true, (16, 16));

        binding = RenderPoints(session, renderer, color, depth, camera, 2, 2);
        await Assert.That((await PickNear(renderer, color, depth, binding, 48, 16)).Status)
            .IsEqualTo(RenderPickStatus.Miss);
        _ = await AssertPointHit(session, renderer, color, depth, binding, 16, 16, 0, 1);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.NoMatchingMeshes);
        await AssertSelectionPixels(renderer, color, depth, binding, 48, 16, false);

        foreach ((double time, ulong revision) in new (double, ulong)[] { (3, 3), (1, 4) })
        {
            binding = RenderPoints(session, renderer, color, depth, camera, revision, time);
            SelectionItem restored = await AssertPointHit(session, renderer, color, depth, binding, 48, 16, 2, 1);
            await Assert.That(restored).IsEqualTo(selected);
            await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
                .IsEqualTo(SilkSelectionOutlineStatus.Rendered);
            await AssertSelectionPixels(renderer, color, depth, binding, 48, 16, true, (16, 16));
        }

        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + InstancedPoints);
        renderer.Dispose();
        color.Dispose();
        depth.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task BoundedAndLegacyRenderingMatchWithVisiblePoints(SilkGraphicsBackend backend)
    {
        using var fixture = new Fixture(Points);
        using var emptyFixture = new Fixture(string.Empty);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using OpenUsdSilkSession legacy = fixture.Open();
        using OpenUsdSilkSession empty = emptyFixture.Open();
        using OpenUsdSilkSession bounded = OpenUsdSilkRuntime.Create(
            fixture.Plugins, fixture.Path, new SilkPreparationLimits(PointCost, 100_000));
        using var legacyRenderer = new SilkMeshRenderer(device);
        using var emptyRenderer = new SilkMeshRenderer(device);
        using var boundedRenderer = new SilkMeshRenderer(device);
        byte[] expected = Capture(legacy, device, legacyRenderer);
        byte[] background = Capture(empty, device, emptyRenderer);
        byte[] actual = Capture(bounded, device, boundedRenderer);
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        await Assert.That(expected.SequenceEqual(background)).IsFalse()
            .Because("The points must produce pixels distinct from an empty scene.");
        await AssertSameGeometry(
            legacyRenderer.Scene.Meshes.Values.Single(), boundedRenderer.Scene.Meshes.Values.Single());
        legacyRenderer.Dispose();
        emptyRenderer.Dispose();
        boundedRenderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    private static async Task AssertSameGeometry(SilkMeshData expected, SilkMeshData actual)
    {
        await Assert.That(actual.TopologyKind).IsEqualTo(expected.TopologyKind);
        await Assert.That(actual.Points.Span.SequenceEqual(expected.Points.Span)).IsTrue();
        await Assert.That(actual.Indices.Span.SequenceEqual(expected.Indices.Span)).IsTrue();
        await Assert.That(actual.TriangleSubprims.Span.SequenceEqual(expected.TriangleSubprims.Span)).IsTrue();
        await Assert.That(actual.AuthoredNormals.Span.SequenceEqual(expected.AuthoredNormals.Span)).IsTrue();
        await Assert.That(actual.PointOrigins.Span.SequenceEqual(expected.PointOrigins.Span)).IsTrue();
        await Assert.That(actual.AuthoredPointCount).IsEqualTo(expected.AuthoredPointCount);
        await Assert.That(actual.DisplayColor.Span.SequenceEqual(expected.DisplayColor.Span)).IsTrue();
        await Assert.That(actual.Transform.Span.SequenceEqual(expected.Transform.Span)).IsTrue();
        await Assert.That(actual.SubprimIdentity).IsEqualTo(expected.SubprimIdentity);
        await Assert.That(actual.SubprimUnsupported).IsEqualTo(expected.SubprimUnsupported);
    }

    private static SilkPickFrameBinding RenderPoints(
        OpenUsdSilkSession session,
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        CameraState camera,
        ulong stateRevision,
        double timeCode = 0)
    {
        using OpenUsdSilkPage page = session.Sync(64, 64, Options(4096), timeCode: timeCode, camera: camera);
        var binding = new SilkPickFrameBinding(stateRevision, page.Revision);
        _ = renderer.ApplyAndRender(page, color, depth, binding);
        return binding;
    }

    private static async Task<SelectionItem> AssertPointHit(
        OpenUsdSilkSession session,
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        SilkPickFrameBinding binding,
        int x,
        int y,
        int instance,
        int point)
    {
        RenderPickResult hit = await PickNear(renderer, color, depth, binding, x, y);
        await Assert.That(hit.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(hit.PrimPath.StartsWith("/Instances/Points/", StringComparison.Ordinal)).IsTrue();
        await Assert.That(hit.InstancerPath).IsEqualTo("/Instances");
        await Assert.That(hit.InstanceIndex).IsEqualTo(instance);
        await Assert.That(hit.ElementIndex).IsEqualTo(point);
        await Assert.That(hit.Item).IsNotNull();
        SelectionItem item = hit.Item!.Value;
        await Assert.That(item.ElementKind).IsEqualTo(SelectionElementKind.Point);
        await Assert.That(item.InstancerContext.Count).IsEqualTo(1);
        await Assert.That(item.InstancerContext[0].InstancerPath).IsEqualTo("/Instances");
        await Assert.That(item.InstancerContext[0].InstanceIndex).IsEqualTo(instance);
        IReadOnlyList<SelectionItem> source = session.ResolveSourceIdentities(
            binding.SceneRevision ?? throw new InvalidOperationException("The synchronized page has no revision."),
            [(hit.PrimPath, instance)]);
        await Assert.That(source.Count).IsEqualTo(1);
        await Assert.That(source[0].PrimPath).IsEqualTo("/Instances/Points");
        await Assert.That(source[0].InstancerContext.Count).IsEqualTo(1);
        await Assert.That(source[0].InstancerPath).IsEqualTo("/Instances");
        await Assert.That(source[0].InstanceIndex).IsEqualTo(instance);
        return item;
    }

    private static async Task<RenderPickResult> PickNear(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        SilkPickFrameBinding binding,
        int x,
        int y)
    {
        RenderPickResult result = default;
        for (int row = y - 1; row <= y + 1; row++)
        {
            for (int column = x - 1; column <= x + 1; column++)
            {
                var request = new RenderPickRequest(
                    column, row, new ViewportDimensions(64, 64),
                    binding.StateRevision, binding.SceneRevision, RenderPickTarget.Point);
                Task<RenderPickResult> pending = renderer.PickAsync(request).AsTask();
                for (int frame = 0; frame < 100 && !pending.IsCompleted; frame++)
                {
                    _ = renderer.Render(color, depth, binding);
                    await Task.Yield();
                }
                if (!pending.IsCompleted)
                {
                    throw new TimeoutException("The point pick did not complete.");
                }
                result = await pending;
                if (result.Status == RenderPickStatus.Hit)
                {
                    return result;
                }
                await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Miss);
            }
        }
        return result;
    }

    private static byte[] ReadColor(ISilkGraphicsTexture color)
    {
        var pixels = new byte[64 * 64 * 4];
        color.ReadbackForTesting(pixels);
        return pixels;
    }

    private static async Task AssertSelectionPixels(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        SilkPickFrameBinding binding,
        int x,
        int y,
        bool outlined,
        params (int X, int Y)[] unchanged)
    {
        byte[] selected = ReadColor(color);
        SelectionState selection = renderer.Selection;
        renderer.UpdateSelection(SelectionState.Empty);
        _ = renderer.Render(color, depth, binding);
        byte[] baseline = ReadColor(color);
        renderer.UpdateSelection(selection);
        _ = renderer.Render(color, depth, binding);
        if (!outlined)
        {
            await Assert.That(selected.SequenceEqual(baseline)).IsTrue();
            return;
        }
        await Assert.That(RegionChanged(baseline, selected, x, y)).IsTrue();
        foreach ((int otherX, int otherY) in unchanged)
        {
            await Assert.That(RegionChanged(baseline, selected, otherX, otherY)).IsFalse();
        }
    }

    private static bool RegionChanged(byte[] before, byte[] after, int x, int y)
    {
        for (int row = y - 4; row <= y + 4; row++)
        {
            for (int column = x - 4; column <= x + 4; column++)
            {
                int offset = ((row * 64) + column) * 4;
                if (!before.AsSpan(offset, 4).SequenceEqual(after.AsSpan(offset, 4)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool RegionHasForeground(byte[] pixels, int x, int y)
    {
        for (int row = y - 4; row <= y + 4; row++)
        {
            for (int column = x - 4; column <= x + 4; column++)
            {
                int offset = ((row * 64) + column) * 4;
                if (!pixels.AsSpan(offset, 4).SequenceEqual(pixels.AsSpan(0, 4)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static byte[] Capture(
        OpenUsdSilkSession session, ISilkGraphicsDevice device, SilkMeshRenderer renderer)
    {
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        return SilkFrameCapture.CaptureCore(
            session, device, renderer, 64, 64, RenderSettings.Default, 0, camera).Rgba.ToArray();
    }

    private static SilkSceneIngestionOptions Options(ulong maximum) =>
        new(RenderPurpose.Default | RenderPurpose.Render | RenderPurpose.Proxy, "full", maximum);

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;

        internal Fixture(string scene)
        {
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(plugins))
            {
                if (Environment.GetEnvironmentVariable("OPENUSD_MESH_PREPARATION_REQUIRED") == "1")
                {
                    throw new InvalidOperationException(
                        "The native points preparation profile requires matched plugins.");
                }
                Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to a matched native runtime.");
                throw new InvalidOperationException("Skip.Test returned unexpectedly.");
            }
            Plugins = plugins;
            _root = Directory.CreateTempSubdirectory("silk-point-preparation-").FullName;
            Path = System.IO.Path.Combine(_root, "points.usda");
            File.WriteAllText(Path, "#usda 1.0\n" + scene);
        }

        internal string Path { get; }
        internal string Plugins { get; }
        internal OpenUsdSilkSession Open() => OpenUsdSilkRuntime.Create(Plugins, Path);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private const string Points = """
        def Points "Points" {
            point3f[] points = [(-0.5,-0.5,0), (0.5,-0.5,0), (0,0.5,0)]
        }
        """;

    private const string InstancedPoints = """
        def PointInstancer "Instances" {
            rel prototypes = [</Instances/Points>, </Instances/Other>]
            int[] protoIndices = [0, 1, 0]
            point3f[] positions = [(-0.5, 0, 0), (0, 5, 0), (0.5, 0, 0)]
            def Points "Points" {
                point3f[] points = [(0, 0, 0), (0, 0.5, 0)]
            }
            def Points "Other" {
                point3f[] points = [(0, 2, 0)]
            }
        }
        """;
}
