// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class StormViewportOwnershipTests
{
    [Test]
    public async Task ExposesExplicitExternalStageOwnership()
    {
        Type control = typeof(StormViewportControl);
        var setSource = control.GetMethod(
            nameof(StormViewportControl.SetRenderSource),
            [
                typeof(UsdStageScheduler),
                typeof(UsdStageRenderSource),
                typeof(StormStageOwnership)
            ]);

        await Assert.That(setSource).IsNotNull();
        await Assert.That(control.GetMethod(nameof(StormViewportControl.ClearRenderSource)))
            .IsNotNull();
        string[] names = Enum.GetNames<StormStageOwnership>();
        await Assert.That(names).IsEquivalentTo(
        [
            nameof(StormStageOwnership.FullyBorrowed),
            nameof(StormStageOwnership.BorrowedSchedulerOwnedSource),
            nameof(StormStageOwnership.OwnedSchedulerAndSource)
        ]);
    }

    [Test]
    [Arguments(StormStageOwnership.FullyBorrowed, 0, 0)]
    [Arguments(StormStageOwnership.BorrowedSchedulerOwnedSource, 1, 0)]
    [Arguments(StormStageOwnership.OwnedSchedulerAndSource, 1, 1)]
    public async Task ReleasesAggregateOwnershipExactlyOnce(
        StormStageOwnership ownership,
        int expectedSourceReleases,
        int expectedSchedulerReleases)
    {
        int sourceReleases = 0;
        int schedulerReleases = 0;
        var lifetime = new StageBindingLifetime(
            ownership,
            () => sourceReleases++,
            () => schedulerReleases++);

        lifetime.ReleaseOwned();
        lifetime.ReleaseOwned();

        await Assert.That(sourceReleases).IsEqualTo(expectedSourceReleases);
        await Assert.That(schedulerReleases).IsEqualTo(expectedSchedulerReleases);
    }

    [Test]
    public async Task SchedulerReleaseCanRetryAfterActiveChildBlock()
    {
        int sourceReleases = 0;
        int schedulerAttempts = 0;
        var lifetime = new StageBindingLifetime(
            StormStageOwnership.OwnedSchedulerAndSource,
            () => sourceReleases++,
            () =>
            {
                schedulerAttempts++;
                if (schedulerAttempts == 1)
                {
                    throw new InvalidOperationException("active child");
                }
            });

        Exception? firstError = Capture(lifetime.ReleaseOwned);
        lifetime.ReleaseOwned();

        await Assert.That(firstError).IsTypeOf<InvalidOperationException>();
        await Assert.That(sourceReleases).IsEqualTo(1);
        await Assert.That(schedulerAttempts).IsEqualTo(2);
    }

    [Test]
    public async Task RejectsInvalidAggregateOwnership()
    {
        Exception? error = Capture(
            () =>
            {
                _ = new StageBindingLifetime(
                    (StormStageOwnership)99,
                    () => { },
                    () => { });
            });

        await Assert.That(error).IsTypeOf<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RapidPathAndSourceBindingReplacementReleasesOwnedResources()
    {
        int sourceReleases = 0;
        int schedulerReleases = 0;
        for (int index = 0; index < 60; index++)
        {
            StormStageOwnership ownership = (StormStageOwnership)(index % 3);
            var lifetime = new StageBindingLifetime(
                ownership,
                () => sourceReleases++,
                () => schedulerReleases++);
            lifetime.ReleaseOwned();
            lifetime.ReleaseOwned();
        }

        await Assert.That(sourceReleases).IsEqualTo(40);
        await Assert.That(schedulerReleases).IsEqualTo(20);
    }

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
