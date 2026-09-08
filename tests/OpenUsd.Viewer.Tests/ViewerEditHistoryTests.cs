// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerEditHistoryTests
{
    [Test]
    public async Task ExplicitDragIdentityCoalescesAcrossPausesButNeverAcrossSeparateGestures()
    {
        var history = new ViewerEditHistory<ViewerPhysicsEditStep>();
        Guid drag = Guid.NewGuid();
        history.Record(Step(0, 1), 0, drag);

        bool merged = history.Record(Step(1, 2), 10, drag);
        bool separate = history.Record(Step(2, 3), 10.001, Guid.NewGuid());

        await Assert.That(merged).IsFalse();
        await Assert.That(separate).IsTrue();
        await Assert.That(history.UndoDepth).IsEqualTo(2);
        history.TryTakeUndo(out _);
        history.TryTakeUndo(out ViewerPhysicsEditStep? wholeDrag);
        await Assert.That(wholeDrag!.Edits.Count).IsEqualTo(1);
        await Assert.That(wholeDrag.Edits[0].Before.NumberValue).IsEqualTo(2d);
        await Assert.That(wholeDrag.Edits[0].After.NumberValue).IsEqualTo(0d);
    }

    [Test]
    public async Task ByteBudgetCoversBothUndoAndRedoAndEvictsOldestReach()
    {
        var history = new ViewerEditHistory<ViewerPhysicsEditStep>(
            capacity: 128, maximumRetainedBytes: 1024);
        for (int index = 0; index < 20; index++)
        {
            history.Record(Step(index, index + 1), index);
            await Assert.That(history.RetainedBytes).IsLessThanOrEqualTo(1024L);
        }
        await Assert.That(history.UndoDepth).IsLessThan(20);
        long retained = history.RetainedBytes;
        while (history.TryTakeUndo(out _))
        {
            await Assert.That(history.RetainedBytes).IsEqualTo(retained);
        }
        await Assert.That(history.RedoDepth).IsGreaterThan(0);
        history.Clear();
        await Assert.That(history.RetainedBytes).IsEqualTo(0L);
    }

    [Test]
    public async Task RefusedOrCancelledApplicationDoesNotAdvanceTheUndoCursor()
    {
        var history = new ViewerEditHistory<ViewerPhysicsEditStep>();
        history.Record(Step(0, 1), 0);

        await Assert.That(history.TryBeginUndo(out var pending)).IsTrue();
        await Assert.That(history.UndoDepth).IsEqualTo(1);
        await Assert.That(history.RedoDepth).IsEqualTo(0);
        await Assert.That(history.CanUndo).IsFalse();
        await Assert.That(() => history.Record(Step(1, 2), 1)).Throws<InvalidOperationException>();
        history.Complete(pending!, applied: false);

        await Assert.That(history.CanUndo).IsTrue();
        await Assert.That(history.UndoDepth).IsEqualTo(1);
        await Assert.That(history.RedoDepth).IsEqualTo(0);
        history.TryBeginUndo(out pending);
        history.Complete(pending!, applied: true);
        await Assert.That(history.UndoDepth).IsEqualTo(0);
        await Assert.That(history.RedoDepth).IsEqualTo(1);
    }

    [Test]
    public async Task DocumentReplacementInvalidatesPendingRedoWithoutChangingTheNewHistory()
    {
        var history = new ViewerEditHistory<ViewerPhysicsEditStep>();
        history.Record(Step(0, 1), 0);
        history.TryTakeUndo(out _);
        await Assert.That(history.TryBeginRedo(out var pending)).IsTrue();
        await Assert.That(history.RedoDepth).IsEqualTo(1);
        history.Clear();
        history.Record(Step(10, 20), 1);

        await Assert.That(() => history.Complete(pending!, applied: true)).Throws<InvalidOperationException>();
        await Assert.That(history.UndoDepth).IsEqualTo(1);
        await Assert.That(history.RedoDepth).IsEqualTo(0);
        history.TryTakeUndo(out ViewerPhysicsEditStep? undo);
        await Assert.That(undo!.Edits[0].After.NumberValue).IsEqualTo(10d);
    }

    private static ViewerPhysicsEditStep Step(double before, double after) => new(
        "Adjust value",
        [new ViewerPhysicsEdit("/Body", "review:value", "Value",
            ViewerPhysicsValue.FromNumber(before), ViewerPhysicsValue.FromNumber(after))]);
}
