// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// Describes what one transport tick may advance.
/// </summary>
/// <param name="SubSteps">The number of fixed sub-steps this tick may advance.</param>
/// <param name="ReachedEnd">Whether advancing them lands on or past the authored end time code.</param>
/// <param name="CatchUpLimited">
/// Whether more accumulated time was available than the per-tick sub-step bound allows. The surplus
/// stays in the backlog, so playback slows down instead of skipping simulated time.
/// </param>
internal readonly record struct UsdPhysicsTickPlan(int SubSteps, bool ReachedEnd, bool CatchUpLimited);

/// <summary>
/// Converts wall-clock time into a bounded number of fixed simulation sub-steps.
/// </summary>
/// <remarks>
/// <para>
/// The scheduler is deliberately free of threads, clocks, and native state so the transport's timing
/// rules are exactly reproducible in tests: the same sequence of elapsed intervals always produces
/// the same sequence of plans.
/// </para>
/// <para>
/// Three rules define it. Accepted wall time is never discarded, so simulated time is never skipped;
/// a tick advances at most <see cref="UsdPhysicsTransportOptions.MaxCatchUpSubStepLimit"/> sub-steps,
/// so a stalled host slows playback down instead of stalling the worker inside one tick; and
/// simulated time is always the exact product of the completed sub-step count and the fixed step, so
/// long sessions never accumulate floating-point drift.
/// </para>
/// </remarks>
internal sealed class UsdPhysicsTickScheduler
{
    /// <summary>
    /// Slack, in fractions of a fixed step, applied when converting the backlog into whole sub-steps.
    /// </summary>
    /// <remarks>
    /// The backlog is reduced by a floating-point multiple of the fixed step on every commit, so a
    /// backlog that is mathematically an exact multiple of the step can land a few ULPs below it.
    /// Without this slack the scheduler would repeatedly leave a sub-step worth of accepted time
    /// unsimulated and report a permanent phantom backlog. The slack is nine orders of magnitude
    /// smaller than one step, so it can never manufacture simulated time.
    /// </remarks>
    private const double StepCountTolerance = 1e-9;

    private readonly double _stepSeconds;
    private readonly int _maxSubSteps;
    private readonly double _durationSeconds;
    private readonly bool _hasAuthoredRange;
    private readonly double _endTolerance;

    private double _backlogSeconds;
    private ulong _stepIndex;
    private long _catchUpLimitedTicks;
    private long _loopCount;

    /// <summary>Initializes a scheduler for one authored timeline and fixed step.</summary>
    internal UsdPhysicsTickScheduler(
        UsdPhysicsTimeline timeline,
        UsdPhysicsFixedStep step,
        int maxSubSteps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSubSteps);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maxSubSteps,
            UsdPhysicsTransportOptions.MaxCatchUpSubStepLimit);

        Timeline = timeline;
        FixedStep = step;
        _stepSeconds = step.Seconds;
        _maxSubSteps = maxSubSteps;
        _durationSeconds = timeline.DurationSeconds;
        _hasAuthoredRange = timeline.HasAuthoredRange;
        _endTolerance = _stepSeconds * 1e-6;
    }

    /// <summary>Gets the authored timeline this scheduler advances over.</summary>
    internal UsdPhysicsTimeline Timeline { get; }

    /// <summary>Gets the fixed step every sub-step advances by.</summary>
    internal UsdPhysicsFixedStep FixedStep { get; }

    /// <summary>Gets the number of completed fixed sub-steps since the last reset or loop.</summary>
    internal ulong StepIndex => _stepIndex;

    /// <summary>Gets the simulated seconds advanced since the authored start time code.</summary>
    internal double SimulationSeconds => _stepIndex * _stepSeconds;

    /// <summary>Gets the authored time code the simulation currently holds.</summary>
    internal double TimeCode => Timeline.ToTimeCode(SimulationSeconds);

    /// <summary>Gets accepted wall-clock time that has not been simulated yet.</summary>
    internal double BacklogSeconds => _backlogSeconds;

    /// <summary>Gets the number of ticks whose catch-up hit the per-tick sub-step bound.</summary>
    internal long CatchUpLimitedTicks => _catchUpLimitedTicks;

    /// <summary>Gets the number of completed authored-range loops.</summary>
    internal long LoopCount => _loopCount;

    /// <summary>Gets the per-tick sub-step bound.</summary>
    internal int MaxSubSteps => _maxSubSteps;

    /// <summary>
    /// Accepts elapsed wall-clock time and reports how far this tick may advance.
    /// </summary>
    /// <remarks>
    /// Planning mutates only the backlog: the caller commits the plan after the world actually
    /// advanced, so a rejected or faulted step never silently consumes simulated time.
    /// </remarks>
    internal UsdPhysicsTickPlan Plan(double elapsedSeconds)
    {
        if (double.IsFinite(elapsedSeconds) && elapsedSeconds > 0)
        {
            _backlogSeconds += elapsedSeconds;
        }

        double availableSteps = Math.Floor((_backlogSeconds / _stepSeconds) + StepCountTolerance);
        int subSteps = availableSteps >= _maxSubSteps ? _maxSubSteps : (int)availableSteps;
        bool catchUpLimited = availableSteps > _maxSubSteps;

        if (_hasAuthoredRange)
        {
            double remaining = _durationSeconds - SimulationSeconds;
            int stepsToEnd = remaining <= _endTolerance
                ? 0
                : (int)Math.Ceiling((remaining - _endTolerance) / _stepSeconds);
            if (stepsToEnd <= subSteps)
            {
                return new UsdPhysicsTickPlan(Math.Max(stepsToEnd, 0), true, false);
            }
        }

        return new UsdPhysicsTickPlan(subSteps, false, catchUpLimited);
    }

    /// <summary>
    /// Plans an explicit request of whole sub-steps that never consumes accepted wall-clock time.
    /// </summary>
    /// <remarks>
    /// An explicit step is how a host that paces playback itself - such as an interactive viewer that
    /// applies a playback speed to wall-clock time - advances the world without letting the speed
    /// change the fixed step. The plan is still bounded by the per-tick sub-step bound and still stops
    /// on the authored end, so explicit stepping obeys exactly the same rules as timed ticking.
    /// </remarks>
    /// <param name="subSteps">The requested number of fixed sub-steps.</param>
    internal UsdPhysicsTickPlan PlanSteps(int subSteps)
    {
        if (subSteps <= 0)
        {
            return new UsdPhysicsTickPlan(0, false, false);
        }

        int bounded = Math.Min(subSteps, _maxSubSteps);
        if (_hasAuthoredRange)
        {
            double remaining = _durationSeconds - SimulationSeconds;
            int stepsToEnd = remaining <= _endTolerance
                ? 0
                : (int)Math.Ceiling((remaining - _endTolerance) / _stepSeconds);
            if (stepsToEnd <= bounded)
            {
                return new UsdPhysicsTickPlan(Math.Max(stepsToEnd, 0), true, false);
            }
        }

        return new UsdPhysicsTickPlan(bounded, false, false);
    }

    /// <summary>Records that the world actually advanced explicitly requested sub-steps.</summary>
    internal void CommitSteps(in UsdPhysicsTickPlan plan)
    {
        if (plan.SubSteps > 0)
        {
            _stepIndex += (ulong)plan.SubSteps;
        }
    }

    /// <summary>Records that the world actually advanced the planned sub-steps.</summary>
    internal void Commit(in UsdPhysicsTickPlan plan)
    {
        if (plan.SubSteps > 0)
        {
            _stepIndex += (ulong)plan.SubSteps;
            _backlogSeconds -= plan.SubSteps * _stepSeconds;
            if (_backlogSeconds < 0)
            {
                _backlogSeconds = 0;
            }
        }
        if (plan.CatchUpLimited)
        {
            _catchUpLimitedTicks++;
        }
    }

    /// <summary>Returns to the authored start time code and drops the backlog.</summary>
    internal void ResetToStart()
    {
        _stepIndex = 0;
        _backlogSeconds = 0;
    }

    /// <summary>Wraps to the authored start time code, preserving the unsimulated backlog.</summary>
    /// <remarks>
    /// The backlog survives a loop on purpose: wrapping is a continuation of the same playback, so
    /// discarding the remainder would skip simulated time at every loop boundary.
    /// </remarks>
    internal void CompleteLoop()
    {
        _stepIndex = 0;
        _loopCount++;
    }

    /// <summary>Stops playback at the authored end time code and drops the backlog.</summary>
    internal void CompleteWithoutLoop() => _backlogSeconds = 0;

    /// <summary>Drops accepted wall-clock time that was never simulated, such as while paused.</summary>
    internal void DiscardBacklog() => _backlogSeconds = 0;

    /// <summary>Positions the scheduler at an exact completed sub-step count.</summary>
    internal void SeekToStep(ulong stepIndex)
    {
        _stepIndex = stepIndex;
        _backlogSeconds = 0;
    }

    /// <summary>
    /// Converts an authored time code into the number of fixed sub-steps that reach it.
    /// </summary>
    /// <remarks>
    /// The result is clamped into the authored range so a seek never asks the world to run before the
    /// authored start or past the authored end.
    /// </remarks>
    internal ulong StepsToTimeCode(double timeCode)
    {
        double seconds = Timeline.ToSeconds(timeCode);
        if (_hasAuthoredRange)
        {
            seconds = Math.Clamp(seconds, 0.0, _durationSeconds);
        }
        else if (seconds < 0)
        {
            seconds = 0;
        }

        double steps = Math.Round(seconds / _stepSeconds, MidpointRounding.AwayFromZero);
        return steps <= 0 ? 0UL : (ulong)steps;
    }
}
