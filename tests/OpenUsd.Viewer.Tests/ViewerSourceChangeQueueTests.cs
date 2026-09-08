// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerSourceChangeQueueTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ChangesSurviveQueuedBeforePauseAndReceivedWhileSuppressed(bool queuedBeforePause)
    {
        var queue = new ViewerSourceChangeQueue();
        var first = new UsdStageChange(1, 2, UsdStageInvalidationKind.Property);
        if (queuedBeforePause)
        {
            await Assert.That(queue.Post(first)).IsTrue();
        }
        queue.Pause();
        if (!queuedBeforePause)
        {
            await Assert.That(queue.Post(first)).IsFalse();
        }

        await Assert.That(queue.TryTake(out _, out _)).IsFalse();
        await Assert.That(queue.Complete()).IsFalse();
        await Assert.That(queue.Post(new UsdStageChange(2, 3, UsdStageInvalidationKind.Topology))).IsFalse();
        await Assert.That(queue.Resume()).IsTrue();
        await Assert.That(queue.TryTake(out UsdStageChange replay, out bool refreshDocument)).IsTrue();
        await Assert.That(replay.BeforeChangeSerial).IsEqualTo(1UL);
        await Assert.That(replay.AfterChangeSerial).IsEqualTo(3UL);
        await Assert.That(replay.EditCount).IsEqualTo(2);
        await Assert.That(replay.Invalidation).IsEqualTo(UsdStageInvalidationKind.Topology);
        await Assert.That(refreshDocument).IsTrue();
        await Assert.That(queue.Complete()).IsFalse();
    }

    [Test]
    public async Task ANoticePublishedAfterResumeStillSchedulesTheNormalRefreshPath()
    {
        var queue = new ViewerSourceChangeQueue();
        queue.Pause();
        await Assert.That(queue.Resume()).IsFalse();
        var change = new UsdStageChange(5, 6, UsdStageInvalidationKind.Composition);
        await Assert.That(queue.Post(change)).IsTrue();
        await Assert.That(queue.TryTake(out UsdStageChange received, out bool refreshDocument)).IsTrue();
        await Assert.That(received).IsEqualTo(change);
        await Assert.That(refreshDocument).IsFalse();
        await Assert.That(queue.Complete()).IsFalse();
    }

    [Test]
    public async Task ANoticeDuringAnAwaitIsNotLostWhenTheCurrentDispatchCompletes()
    {
        var queue = new ViewerSourceChangeQueue();
        await Assert.That(queue.Post(new UsdStageChange(10, 11, UsdStageInvalidationKind.Property))).IsTrue();
        await Assert.That(queue.TryTake(out _, out _)).IsTrue();
        var next = new UsdStageChange(11, 13, UsdStageInvalidationKind.Full);
        await Assert.That(queue.Post(next)).IsFalse();
        await Assert.That(queue.Complete()).IsTrue();
        await Assert.That(queue.TryTake(out UsdStageChange received, out _)).IsTrue();
        await Assert.That(received).IsEqualTo(next);
        await Assert.That(queue.Complete()).IsFalse();
    }

    [Test]
    public async Task RetiringTheOldDocumentRefusesItsPendingAndLateNotifications()
    {
        var oldDocument = new ViewerSourceChangeQueue();
        var replacement = new ViewerSourceChangeQueue();
        await Assert.That(oldDocument.Post(new UsdStageChange(1, 2, UsdStageInvalidationKind.Full))).IsTrue();
        oldDocument.Pause();
        oldDocument.Retire();
        await Assert.That(oldDocument.Resume()).IsFalse();
        await Assert.That(oldDocument.Post(new UsdStageChange(2, 3, UsdStageInvalidationKind.Full))).IsFalse();
        await Assert.That(oldDocument.TryTake(out _, out _)).IsFalse();
        await Assert.That(oldDocument.Complete()).IsFalse();
        await Assert.That(replacement.TryTake(out _, out _)).IsFalse();
        await Assert.That(replacement.Post(new UsdStageChange(0, 1, UsdStageInvalidationKind.Property))).IsTrue();
    }

    [Test]
    public async Task UnchangedSourceDoesNotScheduleARefreshOnResume()
    {
        var queue = new ViewerSourceChangeQueue();
        queue.Pause();
        await Assert.That(queue.Resume()).IsFalse();
        await Assert.That(queue.TryTake(out _, out _)).IsFalse();
        await Assert.That(queue.Complete()).IsFalse();
    }
}
