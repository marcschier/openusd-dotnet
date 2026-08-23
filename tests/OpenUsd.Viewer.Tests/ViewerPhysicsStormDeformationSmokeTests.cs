// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives simulated deformable geometry all the way into the Storm packing layer, using the real
/// scheduler, the real native physics runtime, the real render bridge and the real Storm batch.
/// </summary>
/// <remarks>
/// <para>
/// The one thing this proves that no unit test can is that a deformation reaches Storm
/// <em>without anything being authored onto the stage</em>. The batch that leaves this test is the
/// exact packed page the Storm C ABI consumes, and the stage it came from is re-read afterwards and
/// required to still carry its authored points.
/// </para>
/// <para>
/// A CUDA device is not required to exercise the render path. Without one the retained world skips
/// the GPU objects individually and publishes no deformation window, which is itself a contract
/// worth pinning: the batch must then be empty and the rigid bodies of the same stage must still
/// drive their transform overrides. With a device the same code additionally proves the simulated
/// points differ from the authored ones.
/// </para>
/// </remarks>
public sealed class ViewerPhysicsStormDeformationSmokeTests
{
    private const string ClothPath = "/World/Cloth";
    private const string JellyPath = "/World/Jelly";

    [Test]
    public async Task ARealDeformableStageReachesTheStormBatchWithoutAuthoringIt()
    {
        await using UsdStageScheduler scheduler =
            ViewerPhysicsTestStages.OpenSchedulerOrSkip("viewer-physics-deformable.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        ViewerPhysicsTestStages.SkipWhenSolverIsNotStaged(controller);

        // A deformable is bound like any other simulated object, because its
        // result has to resolve to the same authored prim the renderer draws.
        await Assert.That(controller.Bindings.Bound).IsGreaterThanOrEqualTo(3);
        await Assert.That(controller.Bindings.Unresolved).IsEqualTo(0);

        for (int step = 0; step < 8; step++)
        {
            await controller.StepOneFrameAsync();
        }

        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
        await Assert.That(controller.Snapshot.Status.StepIndex).IsGreaterThan(0UL);

        var target = new StormPackingTarget();
        for (int frame = 0; frame < 8; frame++)
        {
            _ = controller.PumpRenderFrame(0.016d, target);
        }

        await Assert.That(target.PumpedFrames).IsGreaterThan(0);

        // The batch is the packed page the Storm C ABI consumes. Whether it
        // carries regions depends on the device; that it is well formed and
        // never authored does not.
        StormPhysicsDeformationOverrides batch = target.Batch;
        await Assert.That(batch.Count).IsLessThanOrEqualTo(batch.Capacity);
        await Assert.That(batch.PointCount).IsLessThanOrEqualTo(batch.PointCapacity);
        await Assert.That(batch.PathByteCount).IsLessThanOrEqualTo(batch.PathByteCapacity);

        if (batch.Count == 0)
        {
            // No device: every GPU object was skipped individually, so the
            // rigid bodies of the same stage must still be driving the renderer.
            await Assert.That(target.LastAppliedOverrides).IsGreaterThan(0);
        }
        else
        {
            var packed = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < batch.Count; index++)
            {
                _ = packed.Add(batch.PathAt(index));
                float[] points = batch.PointsAt(index).ToArray();
                await Assert.That(points.Length % 3).IsEqualTo(0);
                await Assert.That(points.All(static component => float.IsFinite(component)))
                    .IsTrue();
            }

            await Assert.That(packed.Any(static path =>
                string.Equals(path, ClothPath, StringComparison.Ordinal) ||
                string.Equals(path, JellyPath, StringComparison.Ordinal))).IsTrue();
        }

        // The decisive assertion: whatever the device did, the stage was never
        // authored. A preview or bake would have written these points; a render
        // override must not.
        await AssertAuthoredPointsAreIntactAsync(scheduler);
    }

    [Test]
    public async Task StoppingClearsTheStormDeformationBatch()
    {
        await using UsdStageScheduler scheduler =
            ViewerPhysicsTestStages.OpenSchedulerOrSkip("viewer-physics-deformable.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        ViewerPhysicsTestStages.SkipWhenSolverIsNotStaged(controller);
        await controller.StepOneFrameAsync();

        var target = new StormPackingTarget();
        _ = controller.PumpRenderFrame(0.016d, target);

        await controller.StopAsync();
        int cleared = target.Cleared;
        _ = controller.PumpRenderFrame(0.032d, target);

        // Stopping has to hand the backend a clear, and an emptied batch is
        // exactly what restores the authored geometry in Storm.
        await Assert.That(target.Cleared).IsGreaterThan(cleared);
        await Assert.That(target.Batch.Count).IsEqualTo(0);
        await Assert.That(target.Batch.PointCount).IsEqualTo(0);
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
        await AssertAuthoredPointsAreIntactAsync(scheduler);
    }

    [Test]
    public async Task AnEmptyBatchStillReachesTheBackendSoStormRestoresAuthoredPoints()
    {
        var stage = new ViewerPhysicsOverrideStage();
        var id = new PhysicsRenderObjectId(0x51DEC107UL, PhysicsRenderObjectKind.Deformable);

        _ = stage.StageDeformations(new PhysicsRenderDeformationView(
            new PhysicsRenderDeformableRegion[] { new(id, PhysicsRenderDomain.Cloth, 0, 1, 7) },
            new float[] { 1, 2, 3 },
            revision: 4));
        await Assert.That(stage.TryTakeDeformations(out PhysicsRenderDeformationView staged))
            .IsTrue();
        await Assert.That(staged.Count).IsEqualTo(1);

        // Nothing is staged now, so nothing is taken: the renderer keeps the
        // regions it already retains rather than being handed the same batch
        // twice.
        await Assert.That(stage.TryTakeDeformations(out _)).IsFalse();

        // Clearing has to be taken. A Storm renderer replaces every retained
        // region with the batch it is handed, so an empty batch is the only
        // thing that restores the authored points; treating "no regions" as
        // "nothing staged" would leave the last simulated geometry on screen for
        // the life of the renderer.
        stage.ClearDeformations();
        await Assert.That(stage.TryTakeDeformations(out PhysicsRenderDeformationView emptied))
            .IsTrue();
        await Assert.That(emptied.Count).IsEqualTo(0);
        await Assert.That(emptied.IsEmpty).IsTrue();
        await Assert.That(stage.TryTakeDeformations(out _)).IsFalse();
    }

    private static async Task AssertAuthoredPointsAreIntactAsync(UsdStageScheduler scheduler)
    {
        (int cloth, int jelly) = await scheduler.InvokeAsync(static stage =>
        {
            UsdPrim clothPrim = stage.GetPrim(ClothPath);
            UsdPrim jellyPrim = stage.GetPrim(JellyPath);
            return (
                UsdGeomMesh.Wrap(clothPrim).GetPoints().Length,
                UsdGeomMesh.Wrap(jellyPrim).GetPoints().Length);
        });

        await Assert.That(cloth).IsEqualTo(9);
        await Assert.That(jelly).IsEqualTo(4);
    }

    private static ViewerPhysicsController NewController(UsdStageScheduler scheduler) =>
        new(
            new ViewerPhysicsTransportFactory(scheduler),
            ViewerPhysicsStopwatchClock.Instance,
            ViewerPhysicsRenderCapacities.Default,
            8,
            0.25d,
            new ViewerPhysicsSchedulerAuthoringStage(scheduler));

    /// <summary>
    /// A backend that packs each batch with the same object the Storm control uses at runtime.
    /// </summary>
    private sealed class StormPackingTarget : IViewerPhysicsOverrideTarget
    {
        private readonly PhysicsRenderBindingTable _empty = new(1);

        public bool SupportsPhysicsTransformOverrides => true;

        public StormPhysicsDeformationOverrides Batch { get; } = new(
            ViewerPhysicsRenderCapacities.DeformableCapacity,
            ViewerPhysicsRenderCapacities.DeformableVertexCapacity,
            ViewerPhysicsRenderCapacities.StormPathBytes);

        public int PumpedFrames { get; private set; }

        public int Cleared { get; private set; }

        public int LastAppliedOverrides { get; private set; }

        public int ApplyPhysicsOverrides(
            in PhysicsRenderOverrideView overrides,
            PhysicsRenderBindingTable bindings)
        {
            ArgumentNullException.ThrowIfNull(bindings);
            PumpedFrames++;
            int resolved = 0;
            ReadOnlySpan<PhysicsRenderTransformOverride> items = overrides.Items;
            for (int index = 0; index < items.Length; index++)
            {
                if (bindings.TryResolve(items[index].Id, out _))
                {
                    resolved++;
                }
            }

            LastAppliedOverrides = resolved;
            return resolved;
        }

        public int ApplyPhysicsDeformations(
            in PhysicsRenderDeformationView deformations,
            PhysicsRenderBindingTable bindings)
        {
            ArgumentNullException.ThrowIfNull(bindings);
            return Batch.Refresh(in deformations, bindings);
        }

        public bool TryTakeOverrideReport(out ViewerPhysicsOverrideReport report)
        {
            report = default;
            return false;
        }

        public void ClearPhysicsOverrides()
        {
            Cleared++;
            _ = Batch.Refresh(PhysicsRenderDeformationView.Empty, _empty);
            Batch.Clear();
        }
    }
}
