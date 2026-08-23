// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Tests;

public sealed class UsdPhysicsCheckpointCacheTests
{
    [Test]
    public async Task CheckpointsAreDisabledForWorldsThatAreNotReplayEquivalent()
    {
        var world = new FakePhysicsWorld { ReplayEquivalentCheckpoints = false };
        var cache = new UsdPhysicsCheckpointCache(4, 4, 4, replayEquivalent: false);

        await Assert.That(cache.IsEnabled).IsFalse();
        await Assert.That(cache.ShouldCapture(4)).IsFalse();
        await Assert.That(cache.TryCapture(world, 4)).IsFalse();
        await Assert.That(cache.TryRestore(world, 4, 1.0 / 60, out _)).IsFalse();
    }

    [Test]
    public async Task CheckpointsAreDisabledWhenNotConfigured()
    {
        await Assert.That(new UsdPhysicsCheckpointCache(0, 4, 4, true).IsEnabled).IsFalse();
        await Assert.That(new UsdPhysicsCheckpointCache(4, 0, 4, true).IsEnabled).IsFalse();
        await Assert.That(new UsdPhysicsCheckpointCache(4, 4, 0, true).IsEnabled).IsFalse();
        await Assert.That(new UsdPhysicsCheckpointCache(4, 4, 4, true).IsEnabled).IsTrue();
    }

    [Test]
    public async Task CapturesHappenOnlyAfterTheConfiguredInterval()
    {
        var world = new FakePhysicsWorld { ReplayEquivalentCheckpoints = true };
        var cache = new UsdPhysicsCheckpointCache(10, 4, 4, replayEquivalent: true);

        await Assert.That(cache.ShouldCapture(0)).IsFalse();
        await Assert.That(cache.ShouldCapture(5)).IsFalse();
        await Assert.That(cache.ShouldCapture(11)).IsTrue();

        cache.TryCapture(world, 11);

        await Assert.That(cache.ShouldCapture(15)).IsFalse();
        await Assert.That(cache.ShouldCapture(21)).IsTrue();
    }

    [Test]
    public async Task TheRingIsBoundedAndOverwritesTheOldestCheckpoint()
    {
        var world = new FakePhysicsWorld { ReplayEquivalentCheckpoints = true };
        var cache = new UsdPhysicsCheckpointCache(1, 2, 4, replayEquivalent: true);

        for (ulong step = 1; step <= 6; step++)
        {
            world.TryStep(1.0 / 60, 1, new UsdPhysicsFramePublisher(4).TryClaimWriteBuffer()!);
            await Assert.That(cache.TryCapture(world, step)).IsTrue();
        }

        await Assert.That(cache.Capacity).IsEqualTo(2);
        await Assert.That(cache.Count).IsEqualTo(2);
        await Assert.That(cache.TryRestore(world, 3, 1.0 / 60, out ulong restored)).IsFalse();
        await Assert.That(cache.TryRestore(world, 6, 1.0 / 60, out restored)).IsTrue();
        await Assert.That(restored).IsEqualTo(6ul);
    }

    [Test]
    public async Task RestoreSelectsTheNewestCheckpointAtOrBeforeTheTarget()
    {
        var world = new FakePhysicsWorld { ReplayEquivalentCheckpoints = true };
        var cache = new UsdPhysicsCheckpointCache(10, 8, 4, replayEquivalent: true);
        var publisher = new UsdPhysicsFramePublisher(4);
        UsdPhysicsFrame scratch = publisher.TryClaimWriteBuffer()!;

        for (ulong step = 10; step <= 40; step += 10)
        {
            world.TryStep(1.0 / 60, 10, scratch);
            await Assert.That(cache.TryCapture(world, step)).IsTrue();
        }

        await Assert.That(cache.TryRestore(world, 35, 1.0 / 60, out ulong restored)).IsTrue();
        await Assert.That(restored).IsEqualTo(30ul);
        await Assert.That(world.Counter).IsEqualTo(30ul);
    }

    [Test]
    public async Task ClearingDropsEveryRetainedCheckpoint()
    {
        var world = new FakePhysicsWorld { ReplayEquivalentCheckpoints = true };
        var cache = new UsdPhysicsCheckpointCache(1, 4, 4, replayEquivalent: true);
        cache.TryCapture(world, 1);

        cache.Clear();

        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.TryRestore(world, 1, 1.0 / 60, out _)).IsFalse();
    }
}
