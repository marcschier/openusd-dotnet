// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed record ViewerPlaybackPlan(double Step, TimeSpan FrameInterval);

internal sealed record ViewerStageTimingSnapshot(
    double StartTimeCode,
    double EndTimeCode,
    double FramesPerSecond,
    double TimeCodesPerSecond,
    double PresentationStart,
    double PresentationEnd,
    bool HasFiniteRange,
    ViewerPlaybackPlan? PlaybackPlan,
    string? Diagnostic)
{
    private static readonly TimeSpan MinimumPlaybackInterval = TimeSpan.FromMilliseconds(1);

    internal bool CanPlay => PlaybackPlan is not null;

    internal static ViewerStageTimingSnapshot Empty { get; } = Create(
        double.NaN,
        double.NaN,
        double.NaN,
        double.NaN);

    internal static ViewerStageTimingSnapshot Create(
        double startTimeCode,
        double endTimeCode,
        double framesPerSecond,
        double timeCodesPerSecond)
    {
        bool hasFiniteRange =
            double.IsFinite(startTimeCode) && double.IsFinite(endTimeCode);
        double presentationStart = hasFiniteRange
            ? Math.Min(startTimeCode, endTimeCode)
            : 0;
        double presentationEnd = hasFiniteRange
            ? Math.Max(startTimeCode, endTimeCode)
            : 1;
        var diagnostics = new List<string>();
        if (!hasFiniteRange)
        {
            diagnostics.Add(
                "Playback and manual time controls require finite authored start and end time codes.");
        }
        else if (startTimeCode > endTimeCode)
        {
            diagnostics.Add(
                $"Playback is disabled because authored start time {ViewerTimelineMath.Format(startTimeCode)} " +
                $"exceeds end time {ViewerTimelineMath.Format(endTimeCode)}.");
        }
        else if (startTimeCode == endTimeCode)
        {
            diagnostics.Add(
                "Playback is disabled because the authored range contains only one time code.");
        }

        ViewerPlaybackPlan? plan = null;
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0)
        {
            diagnostics.Add(
                "Playback is disabled because frames per second is " +
                $"{ViewerTimelineMath.FormatAuthored(framesPerSecond)}.");
        }
        if (!double.IsFinite(timeCodesPerSecond) || timeCodesPerSecond <= 0)
        {
            diagnostics.Add(
                "Playback is disabled because time codes per second is " +
                $"{ViewerTimelineMath.FormatAuthored(timeCodesPerSecond)}.");
        }

        if (hasFiniteRange &&
            startTimeCode < endTimeCode &&
            double.IsFinite(framesPerSecond) &&
            framesPerSecond > 0 &&
            double.IsFinite(timeCodesPerSecond) &&
            timeCodesPerSecond > 0)
        {
            double step = timeCodesPerSecond / framesPerSecond;
            double intervalSeconds = 1 / framesPerSecond;
            if (!double.IsFinite(step) || step <= 0)
            {
                diagnostics.Add("Playback is disabled because the authored rates produce an invalid time-code step.");
            }
            else if (!double.IsFinite(intervalSeconds) ||
                     intervalSeconds <= 0 ||
                     intervalSeconds < MinimumPlaybackInterval.TotalSeconds)
            {
                diagnostics.Add(
                    "Playback is disabled because the authored frame cadence is below the supported timer resolution.");
            }
            else
            {
                try
                {
                    TimeSpan interval = TimeSpan.FromSeconds(intervalSeconds);
                    if (interval <= TimeSpan.Zero)
                    {
                        diagnostics.Add(
                            "Playback is disabled because the authored frame cadence cannot be represented.");
                    }
                    else if (interval.TotalSeconds >
                             (double)long.MaxValue / Stopwatch.Frequency)
                    {
                        diagnostics.Add(
                            "Playback is disabled because the authored frame cadence exceeds " +
                            "the playback clock range.");
                    }
                    else
                    {
                        plan = new ViewerPlaybackPlan(step, interval);
                    }
                }
                catch (OverflowException)
                {
                    diagnostics.Add(
                        "Playback is disabled because the authored frame cadence cannot be represented.");
                }
            }
        }

        return new ViewerStageTimingSnapshot(
            startTimeCode,
            endTimeCode,
            framesPerSecond,
            timeCodesPerSecond,
            presentationStart,
            presentationEnd,
            hasFiniteRange,
            plan,
            diagnostics.Count == 0 ? null : string.Join(" ", diagnostics));
    }
}

internal static class ViewerTimelineMath
{
    internal static double Clamp(double value, ViewerStageTimingSnapshot timing)
    {
        ArgumentNullException.ThrowIfNull(timing);
        if (!timing.HasFiniteRange)
        {
            throw new InvalidOperationException("The authored timeline range is not finite.");
        }
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The time code must be finite.");
        }
        return Math.Clamp(value, timing.PresentationStart, timing.PresentationEnd);
    }

    internal static double Advance(
        double current,
        ViewerStageTimingSnapshot timing,
        ViewerPlaybackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(plan);
        if (!timing.CanPlay)
        {
            throw new InvalidOperationException("Playback is unavailable for the authored timing.");
        }
        if (!double.IsFinite(current))
        {
            return timing.StartTimeCode;
        }
        double next = current + plan.Step;
        return !double.IsFinite(next) || next > timing.EndTimeCode
            ? timing.StartTimeCode
            : Math.Max(timing.StartTimeCode, next);
    }

    internal static double SnapToFrame(
        double value,
        ViewerStageTimingSnapshot timing)
    {
        ArgumentNullException.ThrowIfNull(timing);
        double clamped = Clamp(value, timing);
        ViewerPlaybackPlan? plan = timing.PlaybackPlan;
        if (plan is null)
        {
            return clamped;
        }
        double frame = Math.Round(
            (clamped - timing.StartTimeCode) / plan.Step,
            MidpointRounding.AwayFromZero);
        double snapped = timing.StartTimeCode + (frame * plan.Step);
        return Clamp(snapped, timing);
    }

    internal static bool TryParse(string? text, out double value)
    {
        bool parsed = double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
        return parsed && double.IsFinite(value);
    }

    internal static string Format(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    internal static string FormatAuthored(double value) =>
        double.IsFinite(value)
            ? Format(value)
            : $"invalid ({value.ToString("G17", CultureInfo.InvariantCulture)})";
}

internal sealed record ViewerDocumentRefreshPlan(
    bool TimingChanged,
    bool SelectionSurvives,
    double PreservedTimeCode,
    bool RequiresTimeUpdate)
{
    internal static ViewerDocumentRefreshPlan Create(
        ViewerStageTimingSnapshot previousTiming,
        ViewerDocumentSnapshot document,
        string? selectedPrimPath,
        double currentTimeCode)
    {
        ArgumentNullException.ThrowIfNull(previousTiming);
        ArgumentNullException.ThrowIfNull(document);
        bool selectionSurvives = selectedPrimPath is null ||
            document.Hierarchy.Contains(selectedPrimPath);
        double preservedTimeCode = currentTimeCode;
        if (document.Timing.HasFiniteRange)
        {
            preservedTimeCode = double.IsFinite(currentTimeCode)
                ? ViewerTimelineMath.Clamp(currentTimeCode, document.Timing)
                : document.Timing.PresentationStart;
        }
        return new ViewerDocumentRefreshPlan(
            previousTiming != document.Timing,
            selectionSurvives,
            preservedTimeCode,
            preservedTimeCode != currentTimeCode);
    }
}

internal sealed class ViewerPlaybackClock
{
    private readonly long _intervalTimestampTicks;
    private long _nextTimestamp;
    private bool _started;

    internal ViewerPlaybackClock(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _intervalTimestampTicks = Math.Max(
            1,
            checked((long)Math.Round(interval.TotalSeconds * Stopwatch.Frequency)));
    }

    internal async ValueTask WaitForNextTickAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            _nextTimestamp = checked(Stopwatch.GetTimestamp() + _intervalTimestampTicks);
            _started = true;
        }

        while (true)
        {
            long now = Stopwatch.GetTimestamp();
            long remaining = _nextTimestamp - now;
            if (remaining > 0)
            {
                TimeSpan delay = TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            long missed = Math.Max(1, ((now - _nextTimestamp) / _intervalTimestampTicks) + 1);
            _nextTimestamp = checked(_nextTimestamp + (missed * _intervalTimestampTicks));
            return;
        }
    }
}

internal sealed class ViewerTimeUpdatePump : IAsyncDisposable
{
    private readonly Func<double, CancellationToken, ValueTask> _applyAsync;
    private readonly Action<Exception> _reportFailure;
    private readonly CancellationTokenSource _lifetime;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly object _gate = new();
    private readonly Task _worker;
    private double _pending;
    private bool _hasPending;
    private bool _disposed;
    private bool _accepting = true;

    internal ViewerTimeUpdatePump(
        Func<double, CancellationToken, ValueTask> applyAsync,
        Action<Exception> reportFailure,
        CancellationToken documentToken)
    {
        ArgumentNullException.ThrowIfNull(applyAsync);
        ArgumentNullException.ThrowIfNull(reportFailure);
        _applyAsync = applyAsync;
        _reportFailure = reportFailure;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(documentToken);
        _worker = RunAsync(_lifetime.Token);
    }

    internal bool TryPost(double timeCode)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode));
        }
        lock (_gate)
        {
            if (!_accepting)
            {
                return false;
            }
            _pending = timeCode;
            _hasPending = true;
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _accepting = false;
        }
        _lifetime.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        _signal.Dispose();
        _lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                double timeCode;
                lock (_gate)
                {
                    if (!_hasPending)
                    {
                        continue;
                    }
                    timeCode = _pending;
                    _hasPending = false;
                }
                await _applyAsync(timeCode, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _reportFailure(exception);
        }
        finally
        {
            lock (_gate)
            {
                _accepting = false;
            }
        }
    }
}

internal sealed class ViewerSelectionState
{
    internal SelectionItem? Item { get; private set; }

    internal string? PrimPath => Item?.PrimPath;

    internal bool TrySet(string? primPath, out SelectionState selection) =>
        TrySetItem(
            primPath is null ? null : new SelectionItem(primPath),
            out selection);

    internal bool TrySetItem(SelectionItem? item, out SelectionState selection)
    {
        if (Item == item)
        {
            selection = Item is null
                ? SelectionState.Empty
                : new SelectionState([Item.Value]);
            return false;
        }
        Item = item;
        selection = item is null
            ? SelectionState.Empty
            : new SelectionState([item.Value]);
        return true;
    }

    internal bool ClearIfMissing(
        ViewerHierarchySnapshot hierarchy,
        out SelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        return PrimPath is not null && !hierarchy.Contains(PrimPath)
            ? TrySet(null, out selection)
            : ReturnUnchanged(out selection);
    }

    internal void Restore(string? primPath) =>
        Item = primPath is null ? null : new SelectionItem(primPath);

    internal void Restore(SelectionItem? item) => Item = item;

    internal void Synchronize(SelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Item = selection.Items.Count == 0 ? null : selection.Items[0];
    }

    private bool ReturnUnchanged(out SelectionState selection)
    {
        selection = Item is null
            ? SelectionState.Empty
            : new SelectionState([Item.Value]);
        return false;
    }
}
