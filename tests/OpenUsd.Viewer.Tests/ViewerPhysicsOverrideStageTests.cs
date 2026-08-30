// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives the override stage the way the viewer does - one render loop staging batches while one
/// backend thread consumes them - because a batch that is read while it is being overwritten shows
/// bodies from two different simulated frames at once.
/// </summary>
public sealed class ViewerPhysicsOverrideStageTests
{
    [Test]
    public async Task AStagedBatchIsHandedOverWholeWithItsBindingTable()
    {
        var stage = new ViewerPhysicsOverrideStage();
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(
            new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody),
            "/World/Body");

        stage.Stage(Batch(revision: 7, count: 3), bindings);

        await Assert.That(stage.Revision).IsEqualTo(7UL);
        await Assert.That(stage.TryTake(out ViewerPhysicsOverrideBatch batch)).IsTrue();
        using (batch)
        {
            await Assert.That(batch.Overrides.Count).IsEqualTo(3);
            await Assert.That(batch.Overrides.Revision).IsEqualTo(7UL);
            await Assert.That(batch.Bindings).IsSameReferenceAs(bindings);
        }

        await Assert.That(stage.TryTake(out _)).IsFalse();
        await Assert.That(stage.ConsumedBatches).IsEqualTo(1L);
    }

    [Test]
    public async Task OnlyTheNewestBatchSurvivesBecauseStalePosesMustNeverBeDrawn()
    {
        var stage = new ViewerPhysicsOverrideStage();
        var bindings = new PhysicsRenderBindingTable(4);

        stage.Stage(Batch(revision: 1, count: 2), bindings);
        stage.Stage(Batch(revision: 2, count: 2), bindings);

        await Assert.That(stage.DroppedBatches).IsEqualTo(1L);
        await Assert.That(stage.TryTake(out ViewerPhysicsOverrideBatch batch)).IsTrue();
        using (batch)
        {
            await Assert.That(batch.Overrides.Revision).IsEqualTo(2UL);
        }
    }

    [Test]
    public async Task ABorrowedBatchIsNeverOverwrittenWhileTheConsumerIsReadingIt()
    {
        var stage = new ViewerPhysicsOverrideStage();
        var bindings = new PhysicsRenderBindingTable(4);
        stage.Stage(Batch(revision: 1, count: 4), bindings);
        await Assert.That(stage.TryTake(out ViewerPhysicsOverrideBatch batch)).IsTrue();

        // The render loop keeps producing while the consumer holds the batch, which is exactly the
        // case a single shared staging array gets wrong.
        stage.Stage(Batch(revision: 2, count: 4), bindings);
        stage.Stage(Batch(revision: 3, count: 4), bindings);

        await Assert.That(batch.Overrides.Revision).IsEqualTo(1UL);
        double[] xs = new double[batch.Overrides.Count];
        for (int index = 0; index < xs.Length; index++)
        {
            xs[index] = batch.Overrides.Items[index].Position.X;
        }

        foreach (double x in xs)
        {
            await Assert.That(x).IsEqualTo(1d);
        }

        batch.Dispose();
    }

    [Test]
    [NotInParallel("ViewerPhysicsAllocation")]
    public async Task ConcurrentStagingAndConsumingNeverProducesATornBatch()
    {
        var stage = new ViewerPhysicsOverrideStage();
        var bindings = new PhysicsRenderBindingTable(4);
        const int count = 64;
        const int iterations = 20000;
        int torn = 0;
        int taken = 0;
        using var done = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var producer = Task.Run(
            () =>
            {
                for (ulong revision = 1; revision <= iterations; revision++)
                {
                    stage.Stage(Batch(revision, count), bindings);
                }
            },
            done.Token);

        var consumer = Task.Run(
            () =>
            {
                while (!producer.IsCompleted || stage.Revision != 0)
                {
                    if (!stage.TryTake(out ViewerPhysicsOverrideBatch batch))
                    {
                        if (producer.IsCompleted)
                        {
                            return;
                        }

                        continue;
                    }

                    using (batch)
                    {
                        taken++;

                        // Every override in one staged batch carries the same marker, so a value
                        // from a different revision inside one batch is a torn read and nothing
                        // else.
                        double expected = batch.Overrides.Revision;
                        foreach (PhysicsRenderTransformOverride item in batch.Overrides.Items)
                        {
                            if (item.Position.X != expected)
                            {
                                Interlocked.Increment(ref torn);
                            }
                        }
                    }
                }
            },
            done.Token);

        await producer;
        await consumer;

        await Assert.That(torn).IsEqualTo(0);
        await Assert.That(taken).IsGreaterThan(0);
        await Assert.That(stage.StagedBatches).IsEqualTo((long)iterations);
    }

    [Test]
    [NotInParallel("ViewerPhysicsAllocation")]
    public async Task TheWarmStagingAndHandOverPathNeverAllocates()
    {
        var stage = new ViewerPhysicsOverrideStage();
        var bindings = new PhysicsRenderBindingTable(4);
        var items = new PhysicsRenderTransformOverride[16];
        long allocated = AllocationWarmup.MeasureQuiet(
            index => StageAndTake(stage, bindings, items, (ulong)index + 1),
            1000);

        await Assert.That(allocated).IsEqualTo(0L);
    }

    private static void StageAndTake(
        ViewerPhysicsOverrideStage stage,
        PhysicsRenderBindingTable bindings,
        PhysicsRenderTransformOverride[] items,
        ulong revision)
    {
        Fill(items, revision);
        _ = stage.Stage(new PhysicsRenderOverrideView(items, revision), bindings);
        if (stage.TryTake(out ViewerPhysicsOverrideBatch batch))
        {
            batch.Dispose();
        }
    }

    [Test]
    public async Task ClearingStagesAnEmptyBatchSoAuthoredTransformsComeBack()
    {
        var stage = new ViewerPhysicsOverrideStage();
        var bindings = new PhysicsRenderBindingTable(4);
        stage.Stage(Batch(revision: 5, count: 4), bindings);
        stage.Clear();

        await Assert.That(stage.TryTake(out ViewerPhysicsOverrideBatch batch)).IsTrue();
        using (batch)
        {
            await Assert.That(batch.Overrides.Count).IsEqualTo(0);
            await Assert.That(batch.Overrides.Revision).IsEqualTo(0UL);
        }
    }

    private static PhysicsRenderOverrideView Batch(ulong revision, int count)
    {
        var items = new PhysicsRenderTransformOverride[count];
        Fill(items, revision);
        return new PhysicsRenderOverrideView(items, revision);
    }

    private static void Fill(PhysicsRenderTransformOverride[] items, ulong revision)
    {
        for (int index = 0; index < items.Length; index++)
        {
            items[index] = new PhysicsRenderTransformOverride(
                new PhysicsRenderObjectId((ulong)index + 1, PhysicsRenderObjectKind.RigidBody),
                new UsdVec3d(revision, revision, revision),
                PhysicsRenderOrientation.Identity,
                Snapped: false);
        }
    }
}
