// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;

namespace OpenUsd.Physics.Tests;

public sealed class UsdPhysicsBatchedExtractionTests
{
    [Test]
    public async Task RegeneratedPrototypeOrdinalsDoNotChangeThePhysicsContentFingerprint()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(
            RegeneratedPrototypeOrdinalsDoNotChangeThePhysicsContentFingerprint));
        fixture.WriteStage("""
            #usda 1.0
            (metersPerUnit = 1)
            def Xform "Prototype" {
                def Cube "Collider" (prepend apiSchemas = ["PhysicsCollisionAPI"]) {}
            }
            def Xform "Instance" (references = </Prototype>; instanceable = true) {}
            """);
        await using UsdStageScheduler scheduler = fixture.OpenScheduler();
        UsdPhysicsExtractionPage original = await UsdPhysicsStageExtractor.ExtractAsync(scheduler);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Instance").SetInstanceable(false));
        UsdPhysicsExtractionPage uninstanced = await UsdPhysicsStageExtractor.ExtractAsync(scheduler);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Instance").SetInstanceable(true));
        UsdPhysicsExtractionPage restored = await UsdPhysicsStageExtractor.ExtractAsync(scheduler);
        int index = Enumerable.Range(0, original.ObjectCount)
            .Single(i => original.GetObject(i).Path == "/Instance/Collider");
        await Assert.That(restored.GetObject(index).PrototypeId).IsNotEqualTo(original.GetObject(index).PrototypeId)
            .Because("The control must force the SDK to allocate a new transient prototype ordinal.");
        await Assert.That((restored.FingerprintHigh, restored.FingerprintLow))
            .IsEqualTo((original.FingerprintHigh, original.FingerprintLow));
        await Assert.That((uninstanced.FingerprintHigh, uninstanced.FingerprintLow))
            .IsNotEqualTo((original.FingerprintHigh, original.FingerprintLow));
    }

    [Test]
    [Arguments("(0,0,0), (1,0,0), (0,0,1)")]
    [Arguments("(0,0,0), (1,1,0), (0,1,1)")]
    [Arguments(
        "(19.743440628051758,-1.0493708848953247,13.306976318359375), " +
            "(19.62540626525879,-0.9385002851486206,13.532938957214355), " +
                "(19.73555564880371,-0.885985255241394,13.524455070495605)")]
    public async Task ThreePointConvexCollidersUseTheSdkCookerWithoutDroppingTheirIdentities(string points)
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(
            ThreePointConvexCollidersUseTheSdkCookerWithoutDroppingTheirIdentities));
        fixture.WriteStage($$"""
            #usda 1.0
            (metersPerUnit = 1)
            def Mesh "First" (prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsMeshCollisionAPI"]) {
                uniform token physics:approximation = "convexHull"
                point3f[] points = [{{points}}]
                int[] faceVertexCounts = [3]
                int[] faceVertexIndices = [0,2,1]
            }
            def Mesh "Second" (references = </First>) {
                double3 xformOp:translate = (3,0,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);
        await using UsdStageScheduler scheduler = fixture.OpenScheduler();
        UsdPhysicsExtractionPage page = await UsdPhysicsStageExtractor.PrepareAsync(scheduler);
        await Assert.That(page.HullCount).IsEqualTo(1);
        await Assert.That(page.PointCount).IsGreaterThanOrEqualTo(4);
        await Assert.That(page.ObjectCount).IsEqualTo(2);
        using UsdPhysicsSimulation simulation = UsdPhysicsSimulation.Create(page);
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Skipped).IsEmpty();
        var snapshot = simulation.World.InspectRigidShapes();
        await Assert.That(snapshot.Info.ActorCount).IsEqualTo(2u);
        await Assert.That(snapshot.Info.ShapeCount).IsEqualTo(2u);
    }

    [Test]
    public async Task ThinDecomposedSurfaceRetainsItsFiniteCollisionFootprint()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(ThinDecomposedSurfaceRetainsItsFiniteCollisionFootprint));
        fixture.WriteStage($$"""
            #usda 1.0
            (metersPerUnit = 1)
            def PhysicsScene "Scene" {
                vector3f physics:gravityDirection = (0,-1,0)
                float physics:gravityMagnitude = 9.81
            }
            def Mesh "Surface" (prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsMeshCollisionAPI"]) {
                uniform token physics:approximation = "convexDecomposition"
                point3f[] points = [(0,0,0), (2,0,0), (2,0,2), (0,0,2)]
                int[] faceVertexCounts = [3,3]
                int[] faceVertexIndices = [0,2,1,0,3,2]
            }
            def Sphere "Above" (prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI", "PhysicsMassAPI"]) {
                double radius = 0.1
                float physics:mass = 1
                double3 xformOp:translate = (1,2,1)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            {{"def Sphere \"Outside\" (prepend apiSchemas = [\"PhysicsRigidBodyAPI\", \"P" +
                "hysicsCollisionAPI\", \"PhysicsMassAPI\"]) {"}}
                double radius = 0.1
                float physics:mass = 1
                double3 xformOp:translate = (5,2,1)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);
        await using UsdStageScheduler scheduler = fixture.OpenScheduler();
        UsdPhysicsExtractionPage page = await UsdPhysicsStageExtractor.PrepareAsync(scheduler);
        await Assert.That(page.HullCount).IsGreaterThan(0);
        using UsdPhysicsSimulation simulation = UsdPhysicsSimulation.Create(page);
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Skipped).IsEmpty();
        simulation.Step(120);
        await Assert.That(simulation.RequirePose("/Above").Position.Y).IsEqualTo(0.1).Within(0.05);
        await Assert.That(simulation.RequirePose("/Outside").Position.Y).IsLessThan(-1);
    }

    [Test]
    public async Task RepeatedTriangleCollidersShareExactTopologyWithinTheOriginalIndexBound()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(
            RepeatedTriangleCollidersShareExactTopologyWithinTheOriginalIndexBound));
        fixture.WriteStage("""
            #usda 1.0
            (metersPerUnit = 1)
            def Mesh "First" (prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsMeshCollisionAPI"]) {
                uniform token physics:approximation = "none"
                point3f[] points = [(0,0,0), (1,0,0), (0,0,1)]
                int[] faceVertexCounts = [3]
                int[] faceVertexIndices = [0,2,1]
            }
            def Mesh "Second" (references = </First>) {
                double3 xformOp:translate = (3,0,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);
        await using UsdStageScheduler scheduler = fixture.OpenScheduler();
        UsdPhysicsExtractionPage page = await UsdPhysicsStageExtractor.PrepareAsync(
            scheduler, new UsdPhysicsExtractionOptions { MaxIndices = 3 });
        await Assert.That(page.ObjectCount).IsEqualTo(2);
        await Assert.That(page.PointCount).IsEqualTo(3);
        await Assert.That(page.IndexCount).IsEqualTo(3);
        await Assert.That(page.HullCount).IsEqualTo(0);
        await Assert.That(page.GetIndex(0)).IsEqualTo(0);
        await Assert.That(page.GetIndex(1)).IsEqualTo(2);
        await Assert.That(page.GetIndex(2)).IsEqualTo(1);
        using UsdPhysicsSimulation simulation = UsdPhysicsSimulation.Create(page);
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Actors).IsEqualTo(2);
        await Assert.That(simulation.Composition.Shapes).IsEqualTo(2);
    }

    [Test]
    public async Task SharedPreparedGeometryPreservesPlacementSpecificCookingScale()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(
            SharedPreparedGeometryPreservesPlacementSpecificCookingScale));
        fixture.WriteStage("""
            #usda 1.0
            (metersPerUnit = 1)
            def PhysicsScene "Scene" {
                vector3f physics:gravityDirection = (0,-1,0)
                float physics:gravityMagnitude = 9.81
            }
            def Mesh "Low" (prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsMeshCollisionAPI"]) {
                uniform token physics:approximation = "convexHull"
                point3f[] points = [
                    (-0.5,-0.5,-0.5), (0.5,-0.5,-0.5), (-0.5,0.5,-0.5), (0.5,0.5,-0.5),
                    (-0.5,-0.5,0.5), (0.5,-0.5,0.5), (-0.5,0.5,0.5), (0.5,0.5,0.5)]
            }
            def Mesh "High" (references = </Low>) {
                double3 xformOp:translate = (4,0,0)
                double3 xformOp:scale = (1,3,1)
                uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
            }
            def Sphere "A" (prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI", "PhysicsMassAPI"]) {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (0,5,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Sphere "B" (prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI", "PhysicsMassAPI"]) {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (4,5,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);
        await using UsdStageScheduler scheduler = fixture.OpenScheduler();
        UsdPhysicsExtractionPage page = await UsdPhysicsStageExtractor.PrepareAsync(scheduler);
        await Assert.That(page.PointCount).IsEqualTo(8);
        await Assert.That(page.HullCount).IsEqualTo(1);
        using UsdPhysicsSimulation simulation = UsdPhysicsSimulation.Create(page);
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Shapes).IsEqualTo(4);
        var snapshot = simulation.World.InspectRigidShapes();
        await Assert.That(snapshot.Info.ActorCount).IsEqualTo(4u);
        await Assert.That(snapshot.Info.ShapeCount).IsEqualTo(4u);
        await Assert.That(snapshot.Info.ConvexMeshCount).IsEqualTo(2u);
        simulation.Step(120);
        await Assert.That(simulation.RequirePose("/A").Position.Y).IsEqualTo(0.7).Within(0.05);
        await Assert.That(simulation.RequirePose("/B").Position.Y).IsEqualTo(1.7).Within(0.05);
    }

    [Test]
    public async Task CompletePreparationHonorsFinalGeometryBounds()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(CompletePreparationHonorsFinalGeometryBounds));
        fixture.WriteStage(Scene);
        await using UsdStageScheduler scheduler = fixture.OpenScheduler();
        await Assert.That(async () => await UsdPhysicsStageExtractor.PrepareAsync(
            scheduler, new UsdPhysicsExtractionOptions { MaxPoints = 4 }))
            .Throws<UsdPhysicsExtractionException>();
    }

    [Test]
    public async Task CancellationAfterABatchDoesNotPublishAPartialPage()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(CancellationAfterABatchDoesNotPublishAPartialPage));
        fixture.WriteStage(Scene);
        await using UsdStageScheduler scheduler = fixture.OpenScheduler();
        using var cancellation = new CancellationTokenSource();
        var progress = new CancellingProgress(cancellation);
        await Assert.That(async () => await UsdPhysicsStageExtractor.PrepareAsync(
            scheduler, UsdPhysicsExtractionOptions.Default, progress, cancellation.Token))
            .Throws<OperationCanceledException>();
        UsdPhysicsExtractionPage retry = await UsdPhysicsStageExtractor.PrepareAsync(scheduler);
        await Assert.That(retry.HullCount).IsEqualTo(2);
        await Assert.That(retry.Flags.HasFlag(UsdPhysicsExtractionPageTraits.PartialGeometry)).IsFalse();
    }

    [Test]
    public async Task PreparedGeometryRoundTripsAsACompleteDetachedPage()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(PreparedGeometryRoundTripsAsACompleteDetachedPage));
        fixture.WriteStage(Scene);
        UsdPhysicsExtractionPage original = fixture.Extract();
        await using UsdStageScheduler scheduler = fixture.OpenScheduler();
        UsdPhysicsExtractionPage prepared = await UsdPhysicsStageExtractor.PrepareAsync(scheduler);
        UsdPhysicsExtractionPage copied = UsdPhysicsExtractionPage.Create(prepared.ToArray());
        await Assert.That(copied.Flags.HasFlag(UsdPhysicsExtractionPageTraits.PartialGeometry)).IsFalse();
        await Assert.That(copied.Flags.HasFlag(UsdPhysicsExtractionPageTraits.Truncated)).IsFalse();
        await Assert.That(copied.HullCount).IsEqualTo(2);
        await Assert.That(copied.IndexCount).IsEqualTo(0);
        await Assert.That(copied.FingerprintLow).IsEqualTo(original.FingerprintLow);
        await Assert.That(copied.FingerprintHigh).IsEqualTo(original.FingerprintHigh);
        using UsdPhysicsSimulation simulation = UsdPhysicsSimulation.Create(copied);
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Skipped).IsEmpty();
        await Assert.That(simulation.Composition.Shapes).IsEqualTo(2);
        await Assert.That(simulation.Composition.Actors).IsEqualTo(2);
    }

    [Test]
    public async Task PreparationUsesMultipleBoundedBatchesInsteadOfPerColliderInterop()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(
            PreparationUsesMultipleBoundedBatchesInsteadOfPerColliderInterop));
        fixture.WriteStage("#usda 1.0\n(metersPerUnit = 1)\n" +
            string.Join("\n", Enumerable.Range(0, 520).Select(index => $$"""
                def Mesh "Mesh{{index}}" (prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsMeshCollisionAPI"]) {
                    uniform token physics:approximation = "convexHull"
                    point3f[] points = [(0,0,0), (1,0,0), (0,1,0), (0,0,1)]
                }
                """)));
        await using UsdStageScheduler scheduler = fixture.OpenScheduler();
        var progress = new ProgressRecorder();
        ulong before = UsdPhysicsStageExtractor.GetTraversalCount();
        UsdPhysicsExtractionPage page = await UsdPhysicsStageExtractor.PrepareAsync(
            scheduler, UsdPhysicsExtractionOptions.Default, progress);
        await Assert.That(UsdPhysicsStageExtractor.GetTraversalCount() - before).IsEqualTo(4ul);
        await Assert.That(progress.Values.Count).IsEqualTo(2);
        await Assert.That(progress.Values[0]).IsEqualTo(512d / 520);
        await Assert.That(progress.Values[1]).IsEqualTo(1);
        await Assert.That(page.ObjectCount).IsEqualTo(520);
        await Assert.That(page.HullCount).IsEqualTo(1);
        await Assert.That(page.PointCount).IsEqualTo(4);
        await Assert.That(page.IndexCount).IsEqualTo(0);
        await Assert.That(Enumerable.Range(0, page.ObjectCount).Select(index => page.GetObject(
            index).Path).Distinct().Count())
            .IsEqualTo(520);
        using UsdPhysicsSimulation simulation = UsdPhysicsSimulation.Create(page);
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Actors).IsEqualTo(520);
        await Assert.That(simulation.Composition.Shapes).IsEqualTo(520);
        var snapshot = simulation.World.InspectRigidShapes();
        await Assert.That(snapshot.Info.ActorCount).IsEqualTo(520u);
        await Assert.That(snapshot.Info.ShapeCount).IsEqualTo(520u);
        await Assert.That(snapshot.Info.ConvexMeshCount).IsEqualTo(1u);
        await Assert.That(snapshot.Identities.Select(static item => (item.ActorId, item.ShapeId)).Distinct().Count())
            .IsEqualTo(520);
    }

    [Test]
    public async Task GeometryWindowsRetainWholeSourceIdentityWithoutRetainingOtherMeshes()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(
            GeometryWindowsRetainWholeSourceIdentityWithoutRetainingOtherMeshes));
        fixture.WriteStage(Scene);
        UsdPhysicsExtractionPage full = fixture.Extract();
        int firstIndex = Enumerable.Range(0, full.ObjectCount).Single(index => full.GetObject(index).Path == "/First");
        int secondIndex = Enumerable.Range(0, full.ObjectCount).Single(index => full.GetObject(
            index).Path == "/Second");
        var options = new UsdPhysicsExtractionOptions
        {
            GeometryObjectStart = firstIndex,
            GeometryObjectCount = 1,
            MaxPoints = 4,
            MaxIndices = 12,
        };
        UsdPhysicsExtractionPage first = fixture.Extract(options);
        UsdPhysicsExtractionPage second = fixture.Extract(options with { GeometryObjectStart = secondIndex });
        UsdPhysicsExtractionPage fingerprint = fixture.Extract(new UsdPhysicsExtractionOptions
        {
            IncludeMeshData = false,
            FingerprintMeshData = true,
            MaxPoints = 1,
            MaxIndices = 1,
        });
        await Assert.That(full.PointCount).IsEqualTo(8);
        await Assert.That(first.PointCount).IsEqualTo(4);
        await Assert.That(second.PointCount).IsEqualTo(4);
        await Assert.That(first.GetObject(secondIndex).PointCount).IsEqualTo(0);
        await Assert.That(second.GetObject(firstIndex).PointCount).IsEqualTo(0);
        await Assert.That(fingerprint.PointCount).IsEqualTo(0);
        await Assert.That(fingerprint.IndexCount).IsEqualTo(0);
        foreach (UsdPhysicsExtractionPage page in new[] { first, second, fingerprint })
        {
            await Assert.That(page.ObjectCount).IsEqualTo(full.ObjectCount);
            await Assert.That(page.Flags.HasFlag(UsdPhysicsExtractionPageTraits.PartialGeometry)).IsTrue();
            await Assert.That(page.Flags.HasFlag(UsdPhysicsExtractionPageTraits.Truncated)).IsFalse();
            await Assert.That(page.FingerprintLow).IsEqualTo(full.FingerprintLow);
            await Assert.That(page.FingerprintHigh).IsEqualTo(full.FingerprintHigh);
        }
        for (int index = 0; index < 4; index++)
        {
            await Assert.That(second.GetPoint(second.GetObject(secondIndex).PointStart + index))
                .IsEqualTo(full.GetPoint(full.GetObject(secondIndex).PointStart + index));
        }
        UsdPhysicsExtractionPage limited = fixture.Extract(new UsdPhysicsExtractionOptions { MaxPoints = 4 });
        await Assert.That(limited.Flags.HasFlag(UsdPhysicsExtractionPageTraits.Truncated)).IsTrue();
        fixture.WriteStage(Scene.Replace("(9,0,0)", "(10,0,0)", StringComparison.Ordinal));
        UsdPhysicsExtractionPage changedOutsideWindow = fixture.Extract(options);
        await Assert.That(changedOutsideWindow.FingerprintLow != first.FingerprintLow ||
            changedOutsideWindow.FingerprintHigh != first.FingerprintHigh).IsTrue();
    }

    [Test]
    public async Task PartialGeometryCannotBuildAnApparentlyCompleteWorld()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(PartialGeometryCannotBuildAnApparentlyCompleteWorld));
        fixture.WriteStage(Scene);
        using var world = new UsdPhysicsNativeWorld();
        world.AttachExtraction(fixture.Extract(new UsdPhysicsExtractionOptions { GeometryObjectCount = 1 }));
        UsdPhysicsWorldBuildResult result = world.Build(UsdPhysicsTimeline.Default,
            UsdPhysicsFixedStep.Resolve(UsdPhysicsTimeline.Default, 60),
            UsdPhysicsSessionOptions.Default, CancellationToken.None);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Diagnostics.Entries.Any(entry =>
            entry.Message.Contains("partial geometry", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task HiddenUnmappedCookingOpinionsRemainVisibleAsAnUnsupportedInput()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(
            HiddenUnmappedCookingOpinionsRemainVisibleAsAnUnsupportedInput));
        fixture.WriteStage(Scene.Replace(
            "uniform token physics:approximation = \"convexHull\"",
            "uniform token physics:approximation = \"convexDecomposition\"\n" +
            "float physxConvexDecompositionCollision:unrecognizedControl = 5", StringComparison.Ordinal));
        UsdPhysicsExtractionPage page = fixture.Extract();
        await Assert.That(page.GetObject(0).Flags.HasFlag(UsdPhysicsExtractionObjectTraits.UnmappedOpinions)).IsTrue();
        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Composition!.Shapes).IsEqualTo(0);
        await Assert.That(simulation.Composition.Skipped.Any(message =>
            message.Contains("unmapped physics opinions", StringComparison.Ordinal))).IsTrue();
    }

    private const string Scene = """
        #usda 1.0
        (metersPerUnit = 1)
        def Mesh "First" (prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsMeshCollisionAPI"]) {
            uniform token physics:approximation = "convexHull"
            point3f[] points = [(0,0,0), (1,0,0), (0,1,0), (0,0,1)]
            int[] faceVertexCounts = [3,3,3,3]
            int[] faceVertexIndices = [0,2,1,0,1,3,1,2,3,2,0,3]
        }
        def Mesh "Second" (prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsMeshCollisionAPI"]) {
            uniform token physics:approximation = "convexHull"
            point3f[] points = [(8,0,0), (9,0,0), (8,1,0), (8,0,1)]
            int[] faceVertexCounts = [3,3,3,3]
            int[] faceVertexIndices = [0,2,1,0,1,3,1,2,3,2,0,3]
        }
        """;

    private sealed class ProgressRecorder : IProgress<double>
    {
        internal List<double> Values { get; } = [];
        public void Report(double value) => Values.Add(value);
    }

    private sealed class CancellingProgress(CancellationTokenSource cancellation) : IProgress<double>
    {
        public void Report(double value) => cancellation.Cancel();
    }
}
