// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerTimelineModelTests
{
    [Test]
    public async Task FiniteTimingPreservesAuthoredValuesAndCalculatesPlaybackStep()
    {
        ViewerStageTimingSnapshot timing =
            ViewerStageTimingSnapshot.Create(101, 145, 24, 48);

        await Assert.That(timing.StartTimeCode).IsEqualTo(101);
        await Assert.That(timing.EndTimeCode).IsEqualTo(145);
        await Assert.That(timing.FramesPerSecond).IsEqualTo(24);
        await Assert.That(timing.TimeCodesPerSecond).IsEqualTo(48);
        await Assert.That(timing.PresentationStart).IsEqualTo(101);
        await Assert.That(timing.PresentationEnd).IsEqualTo(145);
        await Assert.That(timing.PlaybackPlan).IsNotNull();
        await Assert.That(timing.PlaybackPlan!.Step).IsEqualTo(2);
        await Assert.That(timing.PlaybackPlan.FrameInterval)
            .IsEqualTo(TimeSpan.FromSeconds(1d / 24));
        await Assert.That(timing.Diagnostic).IsNull();
    }

    [Test]
    public async Task ReversedFiniteRangeIsNormalizedOnlyForManualPresentation()
    {
        ViewerStageTimingSnapshot timing =
            ViewerStageTimingSnapshot.Create(20, 10, 24, 24);

        await Assert.That(timing.StartTimeCode).IsEqualTo(20);
        await Assert.That(timing.EndTimeCode).IsEqualTo(10);
        await Assert.That(timing.HasFiniteRange).IsTrue();
        await Assert.That(timing.PresentationStart).IsEqualTo(10);
        await Assert.That(timing.PresentationEnd).IsEqualTo(20);
        await Assert.That(timing.CanPlay).IsFalse();
        await Assert.That(timing.Diagnostic).Contains("start time 20 exceeds end time 10");
        await Assert.That(ViewerTimelineMath.Clamp(25, timing)).IsEqualTo(20);
        await Assert.That(ViewerTimelineMath.Clamp(5, timing)).IsEqualTo(10);
    }

    [Test]
    public async Task InvalidRangeAndRatesProduceExplicitPlaybackDiagnostics()
    {
        ViewerStageTimingSnapshot invalidRange =
            ViewerStageTimingSnapshot.Create(double.NaN, 10, 24, 24);
        ViewerStageTimingSnapshot invalidRates =
            ViewerStageTimingSnapshot.Create(0, 10, 0, double.PositiveInfinity);

        await Assert.That(invalidRange.HasFiniteRange).IsFalse();
        await Assert.That(invalidRange.CanPlay).IsFalse();
        await Assert.That(invalidRange.Diagnostic).Contains("finite authored start and end");
        await Assert.That(invalidRates.HasFiniteRange).IsTrue();
        await Assert.That(invalidRates.CanPlay).IsFalse();
        await Assert.That(invalidRates.Diagnostic).Contains("frames per second is 0");
        await Assert.That(invalidRates.Diagnostic).Contains("time codes per second is invalid");
    }

    [Test]
    public async Task SingleTimeCodeRangeAllowsManualTimeButDisablesPlayback()
    {
        ViewerStageTimingSnapshot timing =
            ViewerStageTimingSnapshot.Create(12, 12, 24, 24);

        await Assert.That(timing.HasFiniteRange).IsTrue();
        await Assert.That(timing.CanPlay).IsFalse();
        await Assert.That(timing.Diagnostic).Contains("only one time code");
        await Assert.That(ViewerTimelineMath.Clamp(20, timing)).IsEqualTo(12);
    }

    [Test]
    public async Task ClampAndAdvanceUseAuthoredRangeAndLoopEndToStart()
    {
        ViewerStageTimingSnapshot timing =
            ViewerStageTimingSnapshot.Create(10, 14, 2, 4);
        ViewerPlaybackPlan plan = timing.PlaybackPlan!;

        await Assert.That(ViewerTimelineMath.Clamp(12.5, timing)).IsEqualTo(12.5);
        await Assert.That(ViewerTimelineMath.Advance(10, timing, plan)).IsEqualTo(12);
        await Assert.That(ViewerTimelineMath.Advance(12, timing, plan)).IsEqualTo(14);
        await Assert.That(ViewerTimelineMath.Advance(14, timing, plan)).IsEqualTo(10);
        await Assert.That(ViewerTimelineMath.Advance(double.NaN, timing, plan)).IsEqualTo(10);
        await Assert.That(ViewerTimelineMath.SnapToFrame(13.1, timing)).IsEqualTo(14);
        await Assert.That(ViewerTimelineMath.SnapToFrame(11, timing)).IsEqualTo(12);
    }

    [Test]
    public async Task TimeParsingAndFormattingAreInvariantAndRejectNonFiniteValues()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            await Assert.That(ViewerTimelineMath.TryParse("12.5", out double parsed)).IsTrue();
            await Assert.That(parsed).IsEqualTo(12.5);
            await Assert.That(ViewerTimelineMath.TryParse("12,5", out _)).IsFalse();
            await Assert.That(ViewerTimelineMath.TryParse("NaN", out _)).IsFalse();
            await Assert.That(ViewerTimelineMath.Format(12.5)).IsEqualTo("12.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public async Task SelectionStateDeduplicatesPathsAndClearsMissingPrims()
    {
        var selection = new ViewerSelectionState();
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));

        await Assert.That(selection.TrySet("/World/Cube", out SelectionState selected)).IsTrue();
        StageRenderState selectedState = initial.WithSelection(selected);
        await Assert.That(selectedState.Revision).IsEqualTo(1UL);
        await Assert.That(selection.TrySet("/World/Cube", out SelectionState duplicate)).IsFalse();
        await Assert.That(selectedState.WithSelection(duplicate)).IsSameReferenceAs(selectedState);

        ViewerHierarchySnapshot surviving =
            ViewerHierarchySnapshot.Build(["/World", "/World/Cube"]);
        await Assert.That(selection.ClearIfMissing(surviving, out _)).IsFalse();

        ViewerHierarchySnapshot removed = ViewerHierarchySnapshot.Build(["/World"]);
        await Assert.That(selection.ClearIfMissing(removed, out SelectionState cleared)).IsTrue();
        await Assert.That(cleared).IsEqualTo(SelectionState.Empty);
        await Assert.That(selection.PrimPath).IsNull();

        selection.Restore("/World/Cube");
        selection.Synchronize(SelectionState.Empty);
        await Assert.That(selection.PrimPath).IsNull();
    }

    [Test]
    public async Task DocumentRefreshPreservesValidSelectionAndCurrentTime()
    {
        ViewerStageTimingSnapshot timing = ViewerStageTimingSnapshot.Create(10, 20, 24, 24);
        var document = new ViewerDocumentSnapshot(
            ViewerHierarchySnapshot.Build(["/World", "/World/Cube"]),
            timing,
            ViewerLayerStackSnapshot.Empty,
            ViewerStageStatisticsSnapshot.Empty,
            SelectedPrim: null);

        ViewerDocumentRefreshPlan plan = ViewerDocumentRefreshPlan.Create(
            timing,
            document,
            "/World/Cube",
            15);

        await Assert.That(plan.TimingChanged).IsFalse();
        await Assert.That(plan.SelectionSurvives).IsTrue();
        await Assert.That(plan.PreservedTimeCode).IsEqualTo(15);
        await Assert.That(plan.RequiresTimeUpdate).IsFalse();
    }

    [Test]
    public async Task DocumentRefreshClearsMissingSelectionAndClampsOnlyToNewFiniteRange()
    {
        ViewerStageTimingSnapshot previous = ViewerStageTimingSnapshot.Create(0, 100, 24, 24);
        ViewerStageTimingSnapshot revised = ViewerStageTimingSnapshot.Create(20, 40, 24, 24);
        var document = new ViewerDocumentSnapshot(
            ViewerHierarchySnapshot.Build(["/World"]),
            revised,
            ViewerLayerStackSnapshot.Empty,
            ViewerStageStatisticsSnapshot.Empty,
            SelectedPrim: null);

        ViewerDocumentRefreshPlan plan = ViewerDocumentRefreshPlan.Create(
            previous,
            document,
            "/World/Cube",
            75);

        await Assert.That(plan.TimingChanged).IsTrue();
        await Assert.That(plan.SelectionSurvives).IsFalse();
        await Assert.That(plan.PreservedTimeCode).IsEqualTo(40);
        await Assert.That(plan.RequiresTimeUpdate).IsTrue();
    }

    [Test]
    public async Task CompositionRefreshClearsRemovedSelectionAndPreservesUnchangedTime()
    {
        ViewerStageTimingSnapshot timing = ViewerStageTimingSnapshot.Create(0, 100, 24, 24);
        ViewerLayerStackSnapshot layers = ViewerLayerStackSnapshot.Create(
            "root.usda",
            "session.usda",
            "session.usda",
            ["session.usda", "root.usda"],
            []);
        var document = new ViewerDocumentSnapshot(
            ViewerHierarchySnapshot.Build(["/World"]),
            timing,
            layers,
            ViewerStageStatisticsSnapshot.Empty,
            SelectedPrim: null);

        ViewerDocumentRefreshPlan plan = ViewerDocumentRefreshPlan.Create(
            timing,
            document,
            "/World/VariantChild",
            24);

        await Assert.That(plan.TimingChanged).IsFalse();
        await Assert.That(plan.SelectionSurvives).IsFalse();
        await Assert.That(plan.PreservedTimeCode).IsEqualTo(24);
        await Assert.That(plan.RequiresTimeUpdate).IsFalse();
        await Assert.That(document.Layers).IsSameReferenceAs(layers);
    }

    [Test]
    public async Task TimeUpdatePumpCoalescesPendingValuesAndStopsAcceptingAfterDisposal()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new List<double>();
        var failures = new List<Exception>();
        var pump = new ViewerTimeUpdatePump(
            async (value, cancellationToken) =>
            {
                lock (applied)
                {
                    applied.Add(value);
                }
                if (value == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(cancellationToken);
                }
                if (value == 3)
                {
                    completed.TrySetResult();
                }
            },
            failures.Add,
            CancellationToken.None);

        await Assert.That(pump.TryPost(1)).IsTrue();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(pump.TryPost(2)).IsTrue();
        await Assert.That(pump.TryPost(3)).IsTrue();
        release.TrySetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await pump.DisposeAsync();

        await Assert.That(applied).IsEquivalentTo([1d, 3d]);
        await Assert.That(failures).IsEmpty();
        await Assert.That(pump.TryPost(4)).IsFalse();
    }

    [Test]
    public async Task TimeUpdatePumpStopsAcceptingAfterWorkerFailure()
    {
        var failed = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pump = new ViewerTimeUpdatePump(
            static (_, _) => throw new InvalidOperationException("update failed"),
            failed.SetResult,
            CancellationToken.None);

        await Assert.That(pump.TryPost(1)).IsTrue();
        Exception exception = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(pump.TryPost(2)).IsFalse();
        await pump.DisposeAsync();
    }
}
