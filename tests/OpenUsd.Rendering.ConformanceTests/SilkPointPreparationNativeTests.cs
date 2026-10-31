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
}
