// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts the physics authoring undo history: that it restores exactly what an edit replaced,
/// that one gesture is one step, and that a failed apply never leaves the history lying about the
/// stage.
/// </summary>
public sealed class ViewerPhysicsEditHistoryTests
{
    [Test]
    public async Task AnEmptyHistoryOffersNothingToUndoOrRedo()
    {
        var history = new ViewerPhysicsEditHistory();

        await Assert.That(history.CanUndo).IsFalse();
        await Assert.That(history.CanRedo).IsFalse();
        await Assert.That(history.UndoDescription).IsEmpty();
        await Assert.That(history.TryTakeUndo(out _)).IsFalse();
        await Assert.That(history.TryTakeRedo(out _)).IsFalse();
    }

    [Test]
    public async Task UndoRestoresTheExactValueTheEditReplacedIncludingTheUnauthoredState()
    {
        var history = new ViewerPhysicsEditHistory();
        ViewerPhysicsEditStep step = Step(
            "/World/Body",
            "openUsdPhysics:body:sleepThreshold",
            ViewerPhysicsValue.Unauthored(ViewerPhysicsValueKind.Number),
            ViewerPhysicsValue.FromNumber(0.5d));

        await Assert.That(history.Record(step, 0d)).IsTrue();
        await Assert.That(history.CanUndo).IsTrue();
        await Assert.That(history.TryTakeUndo(out ViewerPhysicsEditStep undo)).IsTrue();

        await Assert.That(undo.Edits.Count).IsEqualTo(1);
        await Assert.That(undo.Edits[0].After.IsAuthored).IsFalse();
        await Assert.That(undo.Edits[0].Before.NumberValue).IsEqualTo(0.5d);
        await Assert.That(history.CanUndo).IsFalse();
        await Assert.That(history.CanRedo).IsTrue();
    }

    [Test]
    public async Task RedoReplaysTheOriginalEditRatherThanItsReverse()
    {
        var history = new ViewerPhysicsEditHistory();
        ViewerPhysicsEditStep step = Step(
            "/World/Body",
            "openUsdPhysics:body:sleepThreshold",
            ViewerPhysicsValue.FromNumber(0.1d),
            ViewerPhysicsValue.FromNumber(0.5d));
        history.Record(step, 0d);
        history.TryTakeUndo(out _);

        await Assert.That(history.TryTakeRedo(out ViewerPhysicsEditStep redo)).IsTrue();
        await Assert.That(redo.Edits[0].After.NumberValue).IsEqualTo(0.5d);
        await Assert.That(history.CanUndo).IsTrue();
        await Assert.That(history.CanRedo).IsFalse();
    }

    [Test]
    public async Task OneDragOfOneSliderIsOneUndoStep()
    {
        var history = new ViewerPhysicsEditHistory(mergeSeconds: 0.5d);
        ViewerPhysicsValue start = ViewerPhysicsValue.FromNumber(0d);

        await Assert.That(history.Record(
            Step("/World/Body", "openUsdPhysics:body:sleepThreshold", start,
                ViewerPhysicsValue.FromNumber(1d)),
            0d)).IsTrue();
        for (int frame = 1; frame < 40; frame++)
        {
            await Assert.That(history.Record(
                Step(
                    "/World/Body",
                    "openUsdPhysics:body:sleepThreshold",
                    ViewerPhysicsValue.FromNumber(frame),
                    ViewerPhysicsValue.FromNumber(frame + 1)),
                frame * 0.01d)).IsFalse();
        }

        await Assert.That(history.UndoDepth).IsEqualTo(1);
        await Assert.That(history.TryTakeUndo(out ViewerPhysicsEditStep undo)).IsTrue();

        // The merged step reverses in the opposite order it was applied, so the last reversal
        // restores the value the gesture started from.
        await Assert.That(undo.Edits[^1].After.NumberValue).IsEqualTo(0d);
    }

    [Test]
    public async Task AGestureThatPausesLongEnoughStartsANewStep()
    {
        var history = new ViewerPhysicsEditHistory(mergeSeconds: 0.25d);
        history.Record(
            Step("/World/Body", "openUsdPhysics:body:sleepThreshold",
                ViewerPhysicsValue.FromNumber(0d), ViewerPhysicsValue.FromNumber(1d)),
            0d);

        await Assert.That(history.Record(
            Step("/World/Body", "openUsdPhysics:body:sleepThreshold",
                ViewerPhysicsValue.FromNumber(1d), ViewerPhysicsValue.FromNumber(2d)),
            5d)).IsTrue();
        await Assert.That(history.UndoDepth).IsEqualTo(2);
    }

    [Test]
    public async Task ADifferentPropertyNeverMergesIntoTheGestureBeforeIt()
    {
        var history = new ViewerPhysicsEditHistory();
        history.Record(
            Step("/World/Body", "openUsdPhysics:body:sleepThreshold",
                ViewerPhysicsValue.FromNumber(0d), ViewerPhysicsValue.FromNumber(1d)),
            0d);

        await Assert.That(history.Record(
            Step("/World/Body", "openUsdPhysics:body:maxLinearVelocity",
                ViewerPhysicsValue.FromNumber(0d), ViewerPhysicsValue.FromNumber(9d)),
            0.01d)).IsTrue();
        await Assert.That(history.Record(
            Step("/World/Other", "openUsdPhysics:body:sleepThreshold",
                ViewerPhysicsValue.FromNumber(0d), ViewerPhysicsValue.FromNumber(1d)),
            0.02d)).IsTrue();
        await Assert.That(history.UndoDepth).IsEqualTo(3);
    }

    [Test]
    public async Task AMultiPropertyStepNeverMergesWithAnything()
    {
        var history = new ViewerPhysicsEditHistory();
        var step = new ViewerPhysicsEditStep(
            "Preset",
            [
                Edit("/World/Body", "openUsdPhysics:body:sleepThreshold", 0d, 1d),
                Edit("/World/Body", "openUsdPhysics:body:maxLinearVelocity", 0d, 5d),
            ]);

        await Assert.That(history.Record(step, 0d)).IsTrue();
        await Assert.That(history.Record(step, 0.01d)).IsTrue();
        await Assert.That(history.UndoDepth).IsEqualTo(2);
    }

    [Test]
    public async Task RecordingAfterAnUndoDropsTheRedoBranch()
    {
        var history = new ViewerPhysicsEditHistory();
        history.Record(
            Step("/World/Body", "openUsdPhysics:body:sleepThreshold",
                ViewerPhysicsValue.FromNumber(0d), ViewerPhysicsValue.FromNumber(1d)),
            0d);
        history.TryTakeUndo(out _);
        await Assert.That(history.CanRedo).IsTrue();

        history.Record(
            Step("/World/Body", "openUsdPhysics:body:maxLinearVelocity",
                ViewerPhysicsValue.FromNumber(0d), ViewerPhysicsValue.FromNumber(3d)),
            1d);

        await Assert.That(history.CanRedo).IsFalse();
    }

    [Test]
    public async Task AFailedUndoIsPutBackSoTheHistoryStillMatchesTheStage()
    {
        var history = new ViewerPhysicsEditHistory();
        ViewerPhysicsEditStep step = Step(
            "/World/Body",
            "openUsdPhysics:body:sleepThreshold",
            ViewerPhysicsValue.FromNumber(0d),
            ViewerPhysicsValue.FromNumber(1d));
        history.Record(step, 0d);
        history.TryTakeUndo(out ViewerPhysicsEditStep undo);

        history.Restore(undo.Reversed(), wasUndo: true);

        await Assert.That(history.CanUndo).IsTrue();
        await Assert.That(history.CanRedo).IsFalse();
        await Assert.That(history.UndoDepth).IsEqualTo(1);
    }

    [Test]
    public async Task AFailedRedoIsPutBackOntoTheRedoStack()
    {
        var history = new ViewerPhysicsEditHistory();
        ViewerPhysicsEditStep step = Step(
            "/World/Body",
            "openUsdPhysics:body:sleepThreshold",
            ViewerPhysicsValue.FromNumber(0d),
            ViewerPhysicsValue.FromNumber(1d));
        history.Record(step, 0d);
        history.TryTakeUndo(out _);
        history.TryTakeRedo(out ViewerPhysicsEditStep redo);

        history.Restore(redo, wasUndo: false);

        await Assert.That(history.CanRedo).IsTrue();
        await Assert.That(history.CanUndo).IsFalse();
    }

    [Test]
    public async Task TheHistoryIsBoundedSoOneLongSessionCannotGrowWithoutLimit()
    {
        var history = new ViewerPhysicsEditHistory(capacity: 4, mergeSeconds: 0d);
        for (int index = 0; index < 32; index++)
        {
            history.Record(
                Step(
                    "/World/Body",
                    $"openUsdPhysics:body:property{index}",
                    ViewerPhysicsValue.FromNumber(index),
                    ViewerPhysicsValue.FromNumber(index + 1)),
                index);
        }

        await Assert.That(history.UndoDepth).IsEqualTo(4);
    }

    [Test]
    public async Task ClearingDiscardsBothStacksBecauseTheDocumentChanged()
    {
        var history = new ViewerPhysicsEditHistory();
        history.Record(
            Step("/World/Body", "openUsdPhysics:body:sleepThreshold",
                ViewerPhysicsValue.FromNumber(0d), ViewerPhysicsValue.FromNumber(1d)),
            0d);
        history.TryTakeUndo(out _);

        history.Clear();

        await Assert.That(history.CanUndo).IsFalse();
        await Assert.That(history.CanRedo).IsFalse();
    }

    [Test]
    public async Task AnEmptyStepIsNeverRecorded()
    {
        var history = new ViewerPhysicsEditHistory();

        await Assert.That(history.Record(new ViewerPhysicsEditStep("Nothing", []), 0d)).IsFalse();
        await Assert.That(history.CanUndo).IsFalse();
    }

    [Test]
    public async Task RecordingRefusesANonFiniteTime()
    {
        var history = new ViewerPhysicsEditHistory();
        ViewerPhysicsEditStep step = Step(
            "/World/Body",
            "openUsdPhysics:body:sleepThreshold",
            ViewerPhysicsValue.FromNumber(0d),
            ViewerPhysicsValue.FromNumber(1d));

        await Assert.That(() => history.Record(step, double.NaN))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static ViewerPhysicsEditStep Step(
        string primPath,
        string name,
        ViewerPhysicsValue before,
        ViewerPhysicsValue after) =>
        new(
            $"{name} on {primPath}",
            [new ViewerPhysicsEdit(primPath, name, name, before, after)]);

    private static ViewerPhysicsEdit Edit(
        string primPath,
        string name,
        double before,
        double after) =>
        new(
            primPath,
            name,
            name,
            ViewerPhysicsValue.FromNumber(before),
            ViewerPhysicsValue.FromNumber(after));
}
