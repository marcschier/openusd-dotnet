// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Physics;

/// <summary>
/// Describes the authored playback range a <see cref="UsdPhysicsTransport"/> simulates over.
/// </summary>
/// <remarks>
/// <para>
/// The timeline is read once from the authored stage (<c>timeCodesPerSecond</c>, <c>startTimeCode</c>,
/// and <c>endTimeCode</c>) and is then treated as immutable for the lifetime of one built world.
/// Every conversion between simulated seconds and stage time codes goes through this type, so a
/// transport never mixes wall-clock seconds with authored time codes by accident.
/// </para>
/// <para>
/// The <see langword="default"/> value of this struct is deliberately invalid because a zero
/// <see cref="TimeCodesPerSecond"/> cannot describe a playback rate; use <see cref="Default"/>.
/// </para>
/// </remarks>
public readonly record struct UsdPhysicsTimeline
{
    /// <summary>Gets the fallback timeline used when a stage authors no usable range.</summary>
    public static UsdPhysicsTimeline Default { get; } = new(24.0, 0.0, 0.0);

    /// <summary>Initializes a validated timeline.</summary>
    /// <param name="timeCodesPerSecond">The authored <c>timeCodesPerSecond</c>; must be finite and positive.</param>
    /// <param name="startTimeCode">The authored start time code; must be finite.</param>
    /// <param name="endTimeCode">The authored end time code; must be finite and not before the start.</param>
    public UsdPhysicsTimeline(double timeCodesPerSecond, double startTimeCode, double endTimeCode)
    {
        if (!double.IsFinite(timeCodesPerSecond) || timeCodesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCodesPerSecond),
                timeCodesPerSecond,
                "The authored timeCodesPerSecond must be finite and positive.");
        }
        if (!double.IsFinite(startTimeCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(startTimeCode),
                startTimeCode,
                "The authored start time code must be finite.");
        }
        if (!double.IsFinite(endTimeCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(endTimeCode),
                endTimeCode,
                "The authored end time code must be finite.");
        }
        if (endTimeCode < startTimeCode)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endTimeCode),
                endTimeCode,
                "The authored end time code must not precede the authored start time code.");
        }

        TimeCodesPerSecond = timeCodesPerSecond;
        StartTimeCode = startTimeCode;
        EndTimeCode = endTimeCode;
    }

    /// <summary>Gets the authored <c>timeCodesPerSecond</c>.</summary>
    public double TimeCodesPerSecond { get; }

    /// <summary>Gets the authored start time code; simulation always begins here.</summary>
    public double StartTimeCode { get; }

    /// <summary>Gets the authored end time code.</summary>
    public double EndTimeCode { get; }

    /// <summary>Gets a value indicating whether the stage authors a non-empty playback range.</summary>
    /// <remarks>
    /// A stage whose end time code equals its start time code authors no range; such a transport
    /// runs indefinitely and never reaches a loop boundary.
    /// </remarks>
    public bool HasAuthoredRange => EndTimeCode > StartTimeCode;

    /// <summary>Gets the authored range length, in simulated seconds; zero when no range is authored.</summary>
    public double DurationSeconds =>
        HasAuthoredRange ? (EndTimeCode - StartTimeCode) / TimeCodesPerSecond : 0.0;

    /// <summary>Converts simulated seconds since the authored start into an authored time code.</summary>
    public double ToTimeCode(double simulationSeconds) =>
        StartTimeCode + (simulationSeconds * TimeCodesPerSecond);

    /// <summary>Converts an authored time code into simulated seconds since the authored start.</summary>
    public double ToSeconds(double timeCode) =>
        (timeCode - StartTimeCode) / TimeCodesPerSecond;

    /// <summary>
    /// Reads the authored timeline from a stage on its owner thread.
    /// </summary>
    /// <remarks>
    /// This is the only stage access a transport performs outside of retained extraction: it reads
    /// three composed metadata values and never retains a stage, layer, or prim. A stage that
    /// authors an unusable rate or an inverted range falls back to <see cref="Default"/> rather than
    /// failing the build, because playback must remain possible for a partially authored stage.
    /// </remarks>
    /// <param name="scheduler">The stage-owner scheduler.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    public static async ValueTask<UsdPhysicsTimeline> ReadAsync(
        UsdStageScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        (double rate, double start, double end) = await scheduler
            .InvokeAsync(
                static stage => (stage.TimeCodesPerSecond, stage.StartTimeCode, stage.EndTimeCode),
                cancellationToken)
            .ConfigureAwait(false);
        return TryCreate(rate, start, end, out UsdPhysicsTimeline timeline) ? timeline : Default;
    }

    /// <summary>Creates a timeline without throwing when the authored values are unusable.</summary>
    /// <param name="timeCodesPerSecond">The authored <c>timeCodesPerSecond</c>.</param>
    /// <param name="startTimeCode">The authored start time code.</param>
    /// <param name="endTimeCode">The authored end time code.</param>
    /// <param name="timeline">The created timeline, or <see langword="default"/> on failure.</param>
    public static bool TryCreate(
        double timeCodesPerSecond,
        double startTimeCode,
        double endTimeCode,
        out UsdPhysicsTimeline timeline)
    {
        if (!double.IsFinite(timeCodesPerSecond) || timeCodesPerSecond <= 0 ||
            !double.IsFinite(startTimeCode) || !double.IsFinite(endTimeCode) ||
            endTimeCode < startTimeCode)
        {
            timeline = default;
            return false;
        }

        timeline = new UsdPhysicsTimeline(timeCodesPerSecond, startTimeCode, endTimeCode);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{StartTimeCode}..{EndTimeCode} @ {TimeCodesPerSecond} tcps");
}

/// <summary>
/// Describes the fixed simulation step one <see cref="UsdPhysicsTransport"/> advances by.
/// </summary>
/// <remarks>
/// <para>
/// The requested frequency is the authored <see cref="UsdPhysicsTimeline.TimeCodesPerSecond"/>, or
/// <see cref="UsdPhysicsSessionOptions.FixedFrequencyOverrideHz"/> when one is configured. It is
/// then clamped into the <see cref="MinimumFrequencyHz"/>-<see cref="MaximumFrequencyHz"/> window the
/// retained native world accepts. Clamping is never silent: <see cref="WasClamped"/> is set and the
/// transport publishes a diagnostic so a stage authored at, for example, one time code per second
/// does not quietly simulate at a rate nothing agreed to.
/// </para>
/// <para>
/// The <see langword="default"/> value of this struct is deliberately invalid; resolve one with
/// <see cref="Resolve"/>.
/// </para>
/// </remarks>
public readonly record struct UsdPhysicsFixedStep
{
    /// <summary>The slowest fixed simulation frequency the retained world accepts, in hertz.</summary>
    public const double MinimumFrequencyHz = 24.0;

    /// <summary>The fastest fixed simulation frequency the retained world accepts, in hertz.</summary>
    public const double MaximumFrequencyHz = 240.0;

    /// <summary>The stable diagnostic code reported when the requested frequency is clamped.</summary>
    public const string ClampedDiagnosticCode = "OPENUSD_PHYSICS_FIXED_STEP_CLAMPED";

    private UsdPhysicsFixedStep(double requestedFrequencyHz, double frequencyHz)
    {
        RequestedFrequencyHz = requestedFrequencyHz;
        FrequencyHz = frequencyHz;
    }

    /// <summary>Gets the frequency that was requested before clamping, in hertz.</summary>
    public double RequestedFrequencyHz { get; }

    /// <summary>Gets the effective fixed simulation frequency, in hertz.</summary>
    public double FrequencyHz { get; }

    /// <summary>Gets the effective fixed step, in seconds.</summary>
    public double Seconds => 1.0 / FrequencyHz;

    /// <summary>Gets a value indicating whether the requested frequency was clamped.</summary>
    public bool WasClamped => RequestedFrequencyHz != FrequencyHz;

    /// <summary>
    /// Resolves the fixed step from an authored timeline and an optional explicit override.
    /// </summary>
    /// <param name="timeline">The authored timeline whose rate is used when no override is set.</param>
    /// <param name="frequencyOverrideHz">An explicit frequency that replaces the authored rate.</param>
    public static UsdPhysicsFixedStep Resolve(UsdPhysicsTimeline timeline, double? frequencyOverrideHz)
    {
        double requested = frequencyOverrideHz ?? timeline.TimeCodesPerSecond;
        if (!double.IsFinite(requested) || requested <= 0)
        {
            requested = UsdPhysicsTimeline.Default.TimeCodesPerSecond;
        }
        return new UsdPhysicsFixedStep(
            requested,
            Math.Clamp(requested, MinimumFrequencyHz, MaximumFrequencyHz));
    }

    /// <summary>Builds the diagnostic describing a clamped frequency, or <see langword="null"/>.</summary>
    public UsdPhysicsDiagnostic? CreateClampDiagnostic() =>
        WasClamped
            ? new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Warning,
                UsdPhysicsDiagnosticCategory.Step,
                ClampedDiagnosticCode,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The requested fixed simulation frequency {RequestedFrequencyHz} Hz was clamped to " +
                    $"{FrequencyHz} Hz, the supported {MinimumFrequencyHz}-{MaximumFrequencyHz} Hz range."))
            : null;

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{FrequencyHz} Hz ({Seconds} s)");
}
