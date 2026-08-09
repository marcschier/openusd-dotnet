// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;

namespace OpenUsd.Tests;

public sealed class UsdStageSchedulerDiagnosticsTests
{
    [Test]
    [NotInParallel]
    public async Task DisposeCompletionWaitsUntilLiveSchedulerDiagnosticIsCleared()
    {
        int baseline = SharedStageManagedDiagnostics.LiveSchedulers;
        var scheduler = (UsdStageScheduler)Activator.CreateInstance(
            typeof(UsdStageScheduler),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                new Func<UsdStage>(() => throw new InvalidOperationException("boom")),
                1,
                1
            ],
            culture: null)!;

        await Assert.That(scheduler.DisposeAsync().AsTask())
            .Throws<InvalidOperationException>();
        await Assert.That(SharedStageManagedDiagnostics.LiveSchedulers)
            .IsEqualTo(baseline);
    }
}
