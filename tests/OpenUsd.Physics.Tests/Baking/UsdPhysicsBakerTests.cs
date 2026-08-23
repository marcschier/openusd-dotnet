// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Baking;

namespace OpenUsd.Physics.Tests.Baking;

/// <summary>
/// Native-backed tests for <see cref="UsdPhysicsBaker"/> transactional semantics.
/// </summary>
public sealed class UsdPhysicsBakerTests
{
    [Test]
    public void Constructor_RejectsNullScheduler()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new UsdPhysicsBaker(null!));
    }

    [Test]
    public async Task Preflight_AcceptsAWritableFileBackedSublayer()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("preflight-ok");
        await using (world)
        {
            UsdPhysicsBakePreflightResult result = await world.Baker.PreflightAsync(
                world.Spec(), BakeFixture.CreateBatch(1, 1), BakeFixture.CreateBindings());

            await Assert.That(result.CanBake).IsTrue();
            await Assert.That(result.Layer.IsFileBacked).IsTrue();
            await Assert.That(result.Layer.IsLocal).IsTrue();
            await Assert.That(result.Layer.IsRootLayer).IsFalse();
            await Assert.That(result.SampleCount).IsEqualTo(3);
            await Assert.That(result.Diagnostics.HasErrors).IsFalse();
        }
    }

    [Test]
    public async Task Preflight_RejectsTheRootLayer()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("preflight-root");
        await using (world)
        {
            UsdPhysicsBakePreflightResult result = await world.Baker.PreflightAsync(
                new UsdPhysicsBakeSpec(world.RootLayerIdentifier),
                BakeFixture.CreateBatch(1, 1),
                BakeFixture.CreateBindings());

            await Assert.That(result.CanBake).IsFalse();
            await Assert.That(result.Layer.IsRootLayer).IsTrue();
            await Assert.That(result.Diagnostics.Entries.Any(
                d => d.Message.Contains("root layer", StringComparison.Ordinal))).IsTrue();
        }
    }

    [Test]
    public async Task Preflight_RejectsAnAnonymousLayer()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("preflight-anonymous");
        await using (world)
        {
            string anonymous = world.Overlay.PhysicsLayerIdentifier;

            UsdPhysicsBakePreflightResult result = await world.Baker.PreflightAsync(
                new UsdPhysicsBakeSpec(anonymous),
                BakeFixture.CreateBatch(1, 1),
                BakeFixture.CreateBindings());

            await Assert.That(result.CanBake).IsFalse();
            await Assert.That(result.Layer.IsAnonymous).IsTrue();
            await Assert.That(result.Diagnostics.Entries.Any(
                d => d.Message.Contains("file backed", StringComparison.Ordinal))).IsTrue();
        }
    }

    [Test]
    public async Task Preflight_RejectsAMutedLayer()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("preflight-muted");
        await using (world)
        {
            string destination = world.DestinationIdentifier;
            await world.Scheduler.EditAsync(
                stage =>
                {
                    stage.MuteLayer(destination);
                    return true;
                },
                UsdStageInvalidationKind.Composition);

            UsdPhysicsBakePreflightResult result = await world.Baker.PreflightAsync(
                world.Spec(), BakeFixture.CreateBatch(1, 1), BakeFixture.CreateBindings());

            await Assert.That(result.CanBake).IsFalse();
            await Assert.That(result.Layer.IsMuted || !result.Layer.IsLocal).IsTrue();
            await Assert.That(string.Join(
                " | ", result.Diagnostics.Entries.Select(d => d.Message)))
                .Contains("cannot be a bake destination");
        }
    }

    [Test]
    public async Task Preflight_RejectsAReadOnlyLayer()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("preflight-readonly", readOnlyDestination: true);
        await using (world)
        {
            try
            {
                UsdPhysicsBakePreflightResult result = await world.Baker.PreflightAsync(
                    world.Spec(), BakeFixture.CreateBatch(1, 1), BakeFixture.CreateBindings());

                await Assert.That(result.CanBake).IsFalse();
                await Assert.That(result.Layer.IsSaveable).IsFalse();
                await Assert.That(string.Join(
                    " | ", result.Diagnostics.Entries.Select(d => d.Message)))
                    .Contains("read only");
            }
            finally
            {
                new FileInfo(world.DestinationPath).IsReadOnly = false;
            }
        }
    }

    [Test]
    public async Task Preflight_RejectsAnUnresolvableLayer()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("preflight-missing");
        await using (world)
        {
            UsdPhysicsBakePreflightResult result = await world.Baker.PreflightAsync(
                new UsdPhysicsBakeSpec("./does-not-exist.usda"),
                BakeFixture.CreateBatch(1, 1),
                BakeFixture.CreateBindings());

            await Assert.That(result.CanBake).IsFalse();
            await Assert.That(result.Layer.Exists).IsFalse();
        }
    }

    [Test]
    public async Task Bake_WritesTimeSamplesIntoTheSelectedLayerOnly()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-selected-layer");
        await using (world)
        {
            byte[] rootBefore = File.ReadAllBytes(world.RootPath);

            UsdPhysicsBakeTransactionResult result = await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());

            await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
            await Assert.That(result.SampleCount).IsEqualTo(3);
            await Assert.That(result.RecordCount).IsEqualTo(6);
            await Assert.That(result.WasSaved).IsTrue();
            await Assert.That(result.WasRolledBack).IsFalse();
            await Assert.That(File.ReadAllBytes(world.RootPath)).IsEquivalentTo(rootBefore);

            string baked = File.ReadAllText(world.DestinationPath);
            await Assert.That(baked).Contains("xformOp:transform");
            await Assert.That(baked).Contains("points");
            await Assert.That(baked).Contains("velocities");
            await Assert.That(baked).Contains("extent");
            await Assert.That(baked).Contains("openUsdPhysics:simulation:identity");
            await Assert.That(baked).Contains("1: ");
            await Assert.That(baked).Contains("3: ");
        }
    }

    [Test]
    public async Task Bake_IsDeterministicAcrossTwoIdenticalRuns()
    {
        BakeFixture.SkipIfUnavailable();

        string first = await BakeToTextAsync("bake-determinism-a");
        string second = await BakeToTextAsync("bake-determinism-b");

        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task CommitFrame_AuthorsExactlyOneSample()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("commit-frame");
        await using (world)
        {
            UsdPhysicsBakeTransactionResult result = await world.Baker.CommitFrameAsync(
                world.Spec(), BakeFixture.CreateBatch(2, 9), BakeFixture.CreateBindings());

            await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
            await Assert.That(result.SampleCount).IsEqualTo(1);

            string baked = File.ReadAllText(world.DestinationPath);
            await Assert.That(baked).Contains("2: ");
            await Assert.That(baked).DoesNotContain("1: ");
        }
    }

    [Test]
    public async Task Bake_RollsBackCompletelyWhenAuthoringFaults()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-fault-rollback");
        await using (world)
        {
            byte[] before = File.ReadAllBytes(world.DestinationPath);
            world.Baker.FaultInjector = point => point == UsdPhysicsBakeFaultPoint.AfterFirstSample
                ? new InvalidOperationException("injected") : null;

            UsdPhysicsBakeTransactionResult result = await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());

            await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
            await Assert.That(result.WasRolledBack).IsTrue();
            await Assert.That(result.WasSaved).IsFalse();
            await Assert.That(File.ReadAllBytes(world.DestinationPath)).IsEquivalentTo(before);

            bool authored = await world.Scheduler.InvokeAsync(
                stage => stage.GetPrim("/Body").GetAttributeNames()
                    .Contains("xformOp:transform", StringComparer.Ordinal));
            await Assert.That(authored).IsFalse();
        }
    }

    [Test]
    public async Task Bake_RollsBackCompletelyWhenCanceled()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-cancel-rollback");
        await using (world)
        {
            byte[] before = File.ReadAllBytes(world.DestinationPath);
            using var cancellation = new CancellationTokenSource();
            var source = new CancelingSource(cancellation);

            UsdPhysicsBakeTransactionResult result = await world.Baker.BakeAsync(
                world.Spec(),
                source,
                BakeFixture.CreateBindings(),
                progress: null,
                cancellation.Token);

            await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Canceled);
            await Assert.That(result.WasRolledBack).IsTrue();
            await Assert.That(File.ReadAllBytes(world.DestinationPath)).IsEquivalentTo(before);
        }
    }

    [Test]
    public async Task Bake_RollsBackWhenTheCommitStepFaults()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-commit-rollback");
        await using (world)
        {
            byte[] before = File.ReadAllBytes(world.DestinationPath);
            world.Baker.FaultInjector = point => point == UsdPhysicsBakeFaultPoint.BeforeCommit
                ? new InvalidOperationException("injected") : null;

            UsdPhysicsBakeTransactionResult result = await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());

            await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
            await Assert.That(result.WasRolledBack).IsTrue();
            await Assert.That(result.WasSaved).IsFalse();
            await Assert.That(File.ReadAllBytes(world.DestinationPath)).IsEquivalentTo(before);
        }
    }

    [Test]
    public async Task Bake_RollsBackWhenABatchIsStale()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-stale-rollback");
        await using (world)
        {
            byte[] before = File.ReadAllBytes(world.DestinationPath);

            UsdPhysicsBakeTransactionResult result = await world.Baker.BakeAsync(
                world.Spec(),
                new BakeFixture.RampSource(identityRevision: 99),
                BakeFixture.CreateBindings());

            await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
            await Assert.That(result.WasRolledBack).IsTrue();
            await Assert.That(File.ReadAllBytes(world.DestinationPath)).IsEquivalentTo(before);
        }
    }

    [Test]
    public async Task Bake_RestoresTheCallersEditTarget()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-edit-target");
        await using (world)
        {
            string expected = await world.Scheduler.EditAsync(
                stage =>
                {
                    stage.SetEditTargetToSessionLayer();
                    return stage.EditTargetLayerIdentifier;
                },
                UsdStageInvalidationKind.Property);

            await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());

            string actual = await world.Scheduler.InvokeAsync(
                stage => stage.EditTargetLayerIdentifier);

            await Assert.That(actual).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task Bake_ReportsProgressBetweenBoundedChunks()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-progress");
        await using (world)
        {
            var reports = new List<UsdPhysicsBakeProgress>();
            var progress = new SynchronousProgress(reports);

            await world.Baker.BakeAsync(
                world.Spec(UsdPhysicsBakeOptions.Default with { ChunkSize = 1 }),
                new BakeFixture.RampSource(),
                BakeFixture.CreateBindings(),
                progress);

            await Assert.That(reports.Count).IsGreaterThanOrEqualTo(6);
            await Assert.That(reports[^1].CompletedSamples).IsEqualTo(3);
            await Assert.That(reports[^1].TotalSamples).IsEqualTo(3);
            await Assert.That(reports.All(r => r.CompletedRecords <= 6)).IsTrue();
        }
    }

    [Test]
    public async Task Bake_RejectsAnExistingSampleUnderTheRejectPolicy()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-existing-reject");
        await using (world)
        {
            await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());
            string afterFirst = File.ReadAllText(world.DestinationPath);

            UsdPhysicsBakeTransactionResult second = await world.Baker.BakeAsync(
                world.Spec(UsdPhysicsBakeOptions.Default with
                {
                    ExistingSamplePolicy = UsdPhysicsBakeExistingSamplePolicy.Reject
                }),
                new BakeFixture.RampSource(),
                BakeFixture.CreateBindings());

            await Assert.That(second.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
            await Assert.That(second.WasRolledBack).IsTrue();
            await Assert.That(second.Outcomes.Any(
                o => o.Status == UsdPhysicsBakeRecordStatus.ExistingSample)).IsTrue();
            await Assert.That(File.ReadAllText(world.DestinationPath)).IsEqualTo(afterFirst);
        }
    }

    [Test]
    public async Task Bake_SkipsAnExistingSampleUnderTheSkipPolicy()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-existing-skip");
        await using (world)
        {
            await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());
            string afterFirst = File.ReadAllText(world.DestinationPath);

            UsdPhysicsBakeTransactionResult second = await world.Baker.BakeAsync(
                world.Spec(UsdPhysicsBakeOptions.Default with
                {
                    ExistingSamplePolicy = UsdPhysicsBakeExistingSamplePolicy.Skip
                }),
                new ShiftedSource(),
                BakeFixture.CreateBindings());

            await Assert.That(second.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
            await Assert.That(File.ReadAllText(world.DestinationPath)).IsEqualTo(afterFirst);
        }
    }

    [Test]
    public async Task Bake_OverwritesAnExistingSampleByDefault()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-existing-overwrite");
        await using (world)
        {
            await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());
            string afterFirst = File.ReadAllText(world.DestinationPath);

            UsdPhysicsBakeTransactionResult second = await world.Baker.BakeAsync(
                world.Spec(), new ShiftedSource(), BakeFixture.CreateBindings());

            await Assert.That(second.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
            await Assert.That(File.ReadAllText(world.DestinationPath)).IsNotEqualTo(afterFirst);
        }
    }

    [Test]
    public async Task Bake_RefusesToAuthorInsideAnInstancedPrim()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-instanced", instanceable: true);
        await using (world)
        {
            byte[] before = File.ReadAllBytes(world.DestinationPath);
            var bindings = new UsdPhysicsBakeBindings(
                7,
                [
                    new UsdPhysicsBakeBinding(BakeFixture.BodyId, "/Body/Child"),
                    new UsdPhysicsBakeBinding(BakeFixture.ClothId, "/Cloth", -1, 3)
                ]);

            UsdPhysicsBakeTransactionResult result = await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), bindings);

            await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
            await Assert.That(result.WasRolledBack).IsTrue();
            await Assert.That(result.Outcomes.Any(
                o => o.Status is UsdPhysicsBakeRecordStatus.InstanceProxy
                    or UsdPhysicsBakeRecordStatus.InPrototype
                    or UsdPhysicsBakeRecordStatus.PathMissing)).IsTrue();
            await Assert.That(File.ReadAllBytes(world.DestinationPath)).IsEquivalentTo(before);
        }
    }

    [Test]
    public async Task Bake_RejectsASampleWhoseCapacityDoesNotMatchTheComposedTopology()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-capacity");
        await using (world)
        {
            byte[] before = File.ReadAllBytes(world.DestinationPath);
            var sample = new UsdPhysicsPointSample(
                BakeFixture.ClothId,
                UsdPhysicsPointSampleDomain.Cloth,
                3,
                [new UsdVec3d(0, 0, 0), new UsdVec3d(1, 0, 0)]);
            var batch = new UsdPhysicsResultBatch(7, 1, [], [sample]);

            UsdPhysicsBakeTransactionResult result = await world.Baker.CommitFrameAsync(
                world.Spec(), batch, BakeFixture.CreateBindings());

            await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
            await Assert.That(result.WasRolledBack).IsTrue();
            await Assert.That(result.Outcomes.Any(
                o => o.Status == UsdPhysicsBakeRecordStatus.SampleCountMismatch)).IsTrue();
            await Assert.That(File.ReadAllBytes(world.DestinationPath)).IsEquivalentTo(before);
        }
    }

    [Test]
    public async Task Bake_RejectsAMissingPrimPath()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-missing-prim");
        await using (world)
        {
            byte[] before = File.ReadAllBytes(world.DestinationPath);
            var bindings = new UsdPhysicsBakeBindings(
                7,
                [
                    new UsdPhysicsBakeBinding(BakeFixture.BodyId, "/NoSuchPrim"),
                    new UsdPhysicsBakeBinding(BakeFixture.ClothId, "/Cloth", -1, 3)
                ]);

            UsdPhysicsBakeTransactionResult result = await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), bindings);

            await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
            await Assert.That(result.WasRolledBack).IsTrue();
            await Assert.That(result.Outcomes.Any(
                o => o.Status == UsdPhysicsBakeRecordStatus.PathMissing)).IsTrue();
            await Assert.That(File.ReadAllBytes(world.DestinationPath)).IsEquivalentTo(before);
        }
    }

    [Test]
    public async Task Bake_LeavesThePhysicsOverlayUntouched()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await World.CreateAsync("bake-overlay-untouched");
        await using (world)
        {
            string overlayIdentifier = world.Overlay.PhysicsLayerIdentifier;

            await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());

            await Assert.That(world.Overlay.IsDisposed).IsFalse();
            await Assert.That(world.Overlay.PhysicsLayerIdentifier).IsEqualTo(overlayIdentifier);
            await Assert.That(world.Overlay.DetectContamination()).IsFalse();
        }
    }

    private static async Task<string> BakeToTextAsync(string name)
    {
        var world = await World.CreateAsync(name);
        await using (world)
        {
            await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());
            string text = File.ReadAllText(world.DestinationPath);
            // The identifier of the layer is the only thing two work directories differ by.
            return text.Replace(name, "<name>", StringComparison.Ordinal);
        }
    }

    private sealed class SynchronousProgress(List<UsdPhysicsBakeProgress> sink)
        : IProgress<UsdPhysicsBakeProgress>
    {
        public void Report(UsdPhysicsBakeProgress value) => sink.Add(value);
    }

    private sealed class CancelingSource(CancellationTokenSource cancellation)
        : IUsdPhysicsBakeSource
    {
        private int _calls;

        public ValueTask<UsdPhysicsResultBatch?> GetBatchAsync(
            double timeCode, CancellationToken cancellationToken)
        {
            if (++_calls > 1)
            {
                cancellation.Cancel();
            }
            return ValueTask.FromResult<UsdPhysicsResultBatch?>(
                BakeFixture.CreateBatch(timeCode, timeCode));
        }
    }

    private sealed class ShiftedSource : IUsdPhysicsBakeSource
    {
        public ValueTask<UsdPhysicsResultBatch?> GetBatchAsync(
            double timeCode, CancellationToken cancellationToken) =>
            ValueTask.FromResult<UsdPhysicsResultBatch?>(
                BakeFixture.CreateBatch(timeCode, timeCode + 100));
    }

    /// <summary>A stage whose root layer sublayers a writable, file-backed bake destination.</summary>
    internal sealed class World : IAsyncDisposable
    {
        private World(
            string directory,
            string rootPath,
            string destinationPath,
            UsdStageScheduler scheduler,
            UsdSessionOverlay overlay,
            UsdPhysicsBaker baker)
        {
            Directory = directory;
            RootPath = rootPath;
            DestinationPath = destinationPath;
            Scheduler = scheduler;
            Overlay = overlay;
            Baker = baker;
        }

        public string Directory { get; }

        public string RootPath { get; }

        public string DestinationPath { get; }

        public UsdStageScheduler Scheduler { get; }

        public UsdSessionOverlay Overlay { get; }

        public UsdPhysicsBaker Baker { get; }

        public string RootLayerIdentifier { get; private set; } = string.Empty;

        public string DestinationIdentifier { get; private set; } = string.Empty;

        public static async Task<World> CreateAsync(
            string name, bool instanceable = false, bool readOnlyDestination = false)
        {
            string directory = BakeFixture.CreateWorkDirectory(name);
            string destination = BakeFixture.WriteDestinationLayer(directory);
            string rootPath = WriteSublayeredRoot(directory, instanceable);
            if (readOnlyDestination)
            {
                // The save permission is captured when the layer is opened, so it must be set
                // before the stage composes the destination.
                new FileInfo(destination).IsReadOnly = true;
            }

            UsdStageScheduler scheduler = UsdStageScheduler.Open(rootPath);
            UsdSessionOverlay? overlay = null;
            string rootIdentifier = string.Empty;
            string[] stackIdentifiers = [];
            await scheduler.InvokeAsync(stage =>
            {
                overlay = stage.NormalizeSessionOverlay();
                using UsdLayer root = stage.GetRootLayer();
                rootIdentifier = root.Identifier;
                stackIdentifiers = stage.GetLayerStackIdentifiers();
            });

            string destinationIdentifier = Array.Find(
                stackIdentifiers,
                identifier => identifier.EndsWith("bake.usda", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    "The bake destination was not composed into the stage layer stack: " +
                    string.Join(", ", stackIdentifiers));

            return new World(
                directory,
                rootPath,
                destination,
                scheduler,
                overlay!,
                new UsdPhysicsBaker(scheduler))
            {
                RootLayerIdentifier = rootIdentifier,
                DestinationIdentifier = destinationIdentifier
            };
        }

        public UsdPhysicsBakeSpec Spec(UsdPhysicsBakeOptions? options = null) =>
            new(DestinationIdentifier, 1, 3, 1, options);

        /// <summary>
        /// Opens a second, independent scheduler and baker over the same root layer so tests can
        /// exercise two callers contending for one destination layer.
        /// </summary>
        public async Task<(UsdStageScheduler Scheduler, UsdPhysicsBaker Baker)> CreateRivalAsync()
        {
            UsdStageScheduler scheduler = UsdStageScheduler.Open(RootPath);
            await scheduler.InvokeAsync(stage =>
            {
                using UsdLayer root = stage.GetRootLayer();
                return root.Identifier;
            });
            return (scheduler, new UsdPhysicsBaker(scheduler));
        }

        public async ValueTask DisposeAsync()
        {
            Baker.Dispose();
            Overlay.Dispose();
            await Scheduler.DisposeAsync();
        }

        private static string WriteSublayeredRoot(string directory, bool instanceable)
        {
            string path = Path.Combine(directory, "root.usda");
            // Only a prim with a composition arc is actually instanced, so the instanced variant
            // references a prototype instead of just setting the metadata.
            string body = instanceable
                ? "def Xform \"Proto\"\n" +
                  "{\n" +
                  "    def Xform \"Child\"\n" +
                  "    {\n" +
                  "    }\n" +
                  "}\n" +
                  "\n" +
                  "def Xform \"Body\" (\n" +
                  "    instanceable = true\n" +
                  "    references = </Proto>\n" +
                  ")\n" +
                  "{\n" +
                  "}\n"
                : "def Xform \"Body\"\n" +
                  "{\n" +
                  "    def Xform \"Child\"\n" +
                  "    {\n" +
                  "        def Xform \"Grandchild\"\n" +
                  "        {\n" +
                  "        }\n" +
                  "    }\n" +
                  "}\n";
            File.WriteAllText(
                path,
                "#usda 1.0\n" +
                "(\n" +
                "    subLayers = [\n" +
                "        @./bake.usda@\n" +
                "    ]\n" +
                "    timeCodesPerSecond = 24\n" +
                "    startTimeCode = 1\n" +
                "    endTimeCode = 3\n" +
                ")\n" +
                "\n" +
                body +
                "\n" +
                "def Mesh \"Cloth\"\n" +
                "{\n" +
                "    int[] faceVertexCounts = [3]\n" +
                "    int[] faceVertexIndices = [0, 1, 2]\n" +
                "    point3f[] points = [(0, 0, 0), (1, 0, 0), (0, 1, 0)]\n" +
                "}\n");
            return path;
        }
    }
}





