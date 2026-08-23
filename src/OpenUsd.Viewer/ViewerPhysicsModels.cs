// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>Identifies what the physics transport is currently doing.</summary>
internal enum ViewerPhysicsRunState
{
    /// <summary>No physics controller exists for the open document.</summary>
    Disabled,

    /// <summary>A world is being built, reset, or replayed.</summary>
    Busy,

    /// <summary>A world exists and is parked at a time code.</summary>
    Paused,

    /// <summary>The viewer is pacing the world forward.</summary>
    Playing,

    /// <summary>Playback reached the authored end without looping.</summary>
    Ended,

    /// <summary>A physics-relevant authored edit invalidated the built world.</summary>
    Invalidated,

    /// <summary>The world faulted and cannot advance.</summary>
    Faulted,
}

/// <summary>Identifies one interactive physics transport command.</summary>
internal enum ViewerPhysicsCommand
{
    /// <summary>No command.</summary>
    None,

    /// <summary>Create the controller and build the world.</summary>
    Enable,

    /// <summary>Start pacing the world forward.</summary>
    Play,

    /// <summary>Stop pacing without discarding the world.</summary>
    Pause,

    /// <summary>Return to the authored start and clear render overrides.</summary>
    Stop,

    /// <summary>Advance exactly one fixed simulation step.</summary>
    StepOneFrame,

    /// <summary>Move the world to an authored time code.</summary>
    Seek,

    /// <summary>Rebuild the world from the authored stage.</summary>
    Rebuild,

    /// <summary>Write the simulated poses into the session overlay.</summary>
    ApplyPreview,

    /// <summary>Bake simulated poses into a file-backed layer.</summary>
    Bake,
}

/// <summary>Classifies why a physics command cannot run right now.</summary>
internal enum ViewerPhysicsCommandAvailability
{
    /// <summary>The command can run.</summary>
    Available,

    /// <summary>No physics controller exists yet.</summary>
    NotEnabled,

    /// <summary>Another command owns the transport.</summary>
    Busy,

    /// <summary>The command does not apply in the current run state.</summary>
    NotApplicable,

    /// <summary>The built world is invalid or faulted and must be rebuilt.</summary>
    NeedsRebuild,
}

/// <summary>Classifies an observed authored edit for physics purposes.</summary>
internal enum ViewerPhysicsEditKind
{
    /// <summary>The edit cannot change simulated behaviour.</summary>
    Visual,

    /// <summary>The edit changes simulated behaviour and invalidates the built world.</summary>
    Relevant,
}

/// <summary>Identifies how a physics operation failed.</summary>
internal enum ViewerPhysicsFailureKind
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>The bounded transport request queue refused the request.</summary>
    QueueFull,

    /// <summary>The transport was not in a state that accepts the request.</summary>
    InvalidState,

    /// <summary>The request was cancelled.</summary>
    Canceled,

    /// <summary>The world could not be built or advanced.</summary>
    Faulted,

    /// <summary>A preview or bake destination refused the write.</summary>
    Rejected,
}

/// <summary>
/// One viewer-level physics failure, translated away from the transport's exception types.
/// </summary>
/// <remarks>
/// The viewer never lets a physics exception type escape into UI code: a transport queue that is
/// full and a world that faulted are both just diagnostics a user has to be able to read, and the
/// deterministic tests drive the same translated kinds a real transport produces.
/// </remarks>
internal sealed class ViewerPhysicsException : Exception
{
    /// <summary>Initializes a translated physics failure.</summary>
    internal ViewerPhysicsException(ViewerPhysicsFailureKind kind, string message)
        : base(message) => Kind = kind;

    /// <summary>Initializes a translated physics failure that already authored changes.</summary>
    /// <param name="kind">The classified failure.</param>
    /// <param name="message">The message shown to the user.</param>
    /// <param name="edits">The changes the failed operation authored before it failed.</param>
    /// <remarks>
    /// A preview that fails partway has still authored every chunk it completed. Carrying those
    /// exact changes out with the failure is what lets the controller recognise them as its own
    /// instead of treating its own half-applied preview as an external edit and rebuilding.
    /// </remarks>
    internal ViewerPhysicsException(
        ViewerPhysicsFailureKind kind,
        string message,
        IReadOnlyList<ViewerPhysicsStageEdit> edits)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(edits);
        Kind = kind;
        Edits = edits;
    }

    /// <summary>Initializes a translated physics failure over an inner cause.</summary>
    internal ViewerPhysicsException(
        ViewerPhysicsFailureKind kind,
        string message,
        Exception innerException)
        : base(message, innerException) => Kind = kind;

    /// <summary>Gets the classified failure.</summary>
    internal ViewerPhysicsFailureKind Kind { get; }

    /// <summary>Gets the changes the failed operation authored before it failed.</summary>
    internal IReadOnlyList<ViewerPhysicsStageEdit> Edits { get; } = [];
}

/// <summary>One atomically consistent view of transport progress.</summary>
/// <param name="State">The transport run state.</param>
/// <param name="Revision">The publication revision of the latest complete frame.</param>
/// <param name="StepIndex">The completed fixed sub-step count since the last reset or loop.</param>
/// <param name="TimeCode">The authored time code the world holds.</param>
/// <param name="SimulationSeconds">The simulated seconds advanced since the authored start.</param>
/// <param name="BacklogSeconds">Accepted wall-clock time that has not been simulated yet.</param>
/// <param name="LoopCount">The number of completed authored-range loops.</param>
/// <param name="QueueDepth">The number of queued transport requests.</param>
internal readonly record struct ViewerPhysicsTransportStatus(
    ViewerPhysicsRunState State,
    ulong Revision,
    ulong StepIndex,
    double TimeCode,
    double SimulationSeconds,
    double BacklogSeconds,
    long LoopCount,
    int QueueDepth)
{
    /// <summary>Gets the status of a document that has no physics controller.</summary>
    internal static ViewerPhysicsTransportStatus Disabled =>
        new(ViewerPhysicsRunState.Disabled, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>One row of the capability matrix shown for the built world.</summary>
/// <param name="Name">The capability or domain name.</param>
/// <param name="IsSupported">Whether the built world provides it.</param>
/// <param name="IsRenderable">Whether the active render backend can draw it.</param>
/// <param name="Detail">A sentence explaining the state.</param>
internal sealed record ViewerPhysicsCapabilityRow(
    string Name,
    bool IsSupported,
    bool IsRenderable,
    string Detail)
{
    /// <summary>Gets the one-word state the matrix shows.</summary>
    internal string StatusText => !IsSupported
        ? "Unsupported"
        : IsRenderable ? "Ready" : "Not drawn";
}

/// <summary>One physics diagnostic shown in the inspector.</summary>
/// <param name="Severity">The severity word, such as Error or Warning.</param>
/// <param name="Category">The category word, such as Build or Seek.</param>
/// <param name="Code">The stable diagnostic code.</param>
/// <param name="Message">The human-readable message.</param>
internal sealed record ViewerPhysicsDiagnosticRow(
    string Severity,
    string Category,
    string Code,
    string Message)
{
    /// <summary>Formats the row as one inspector line.</summary>
    internal string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Severity} {Category} {Code}: {Message}");
}

/// <summary>One simulated object's status, shown by the inspector's query panel.</summary>
/// <param name="PrimPath">The authored prim path the identity is bound to.</param>
/// <param name="Kind">The simulated object kind.</param>
/// <param name="IsSimulated">Whether the built world simulates the object.</param>
/// <param name="IsRendered">Whether the active backend can draw an override for it.</param>
/// <param name="Detail">A sentence describing why the object is in this state.</param>
internal sealed record ViewerPhysicsObjectRow(
    string PrimPath,
    string Kind,
    bool IsSimulated,
    bool IsRendered,
    string Detail)
{
    /// <summary>Gets the path shown in the query panel.</summary>
    internal string Path => PrimPath.Length == 0 ? "(unbound)" : PrimPath;

    /// <summary>Gets the one-line state shown beside the path.</summary>
    internal string StatusText => string.Create(
        CultureInfo.InvariantCulture,
        $"{Kind}{(IsSimulated ? string.Empty : " · not simulated")}" +
        $"{(IsRendered ? string.Empty : " · not drawn")}");
}

/// <summary>
/// Identifies one observed authored change by the native serials that bracket it.
/// </summary>
/// <param name="BeforeSerial">The stage change serial before the first edit of the change.</param>
/// <param name="AfterSerial">The stage change serial after the last edit of the change.</param>
/// <remarks>
/// The pair identifies a change exactly, which is what lets the controller recognise the edit its
/// own preview authored without guessing from timing. A window of "recent" edits would either drop
/// a real edit that arrived inside it, or miss the controller's own edit when the notification is
/// delivered late; matching serials does neither.
/// </remarks>
internal readonly record struct ViewerPhysicsStageEdit(ulong BeforeSerial, ulong AfterSerial)
{
    /// <summary>Gets the identity of a change whose serials are not known.</summary>
    internal static ViewerPhysicsStageEdit Unknown => default;

    /// <summary>Gets a value indicating whether the change is identified by serials.</summary>
    internal bool IsKnown => AfterSerial > BeforeSerial;

    /// <summary>Reports whether this change is exactly the same authored change as another.</summary>
    /// <param name="other">The change to compare against.</param>
    /// <returns><see langword="true"/> when both serials match and are known.</returns>
    internal bool IsSameChangeAs(ViewerPhysicsStageEdit other) =>
        IsKnown && BeforeSerial == other.BeforeSerial && AfterSerial == other.AfterSerial;
}

/// <summary>One stable simulated identity and the authored prim it drives.</summary>
/// <param name="Id">The stable simulation identity.</param>
/// <param name="Kind">The renderer-neutral object kind.</param>
/// <param name="PrimPath">The absolute authored prim path.</param>
/// <param name="InstanceIndex">The zero-based instance ordinal.</param>
/// <param name="IsSimulated">Whether the built world simulates the object.</param>
/// <param name="Detail">A sentence describing why the object is or is not simulated.</param>
internal sealed record ViewerPhysicsBinding(
    ulong Id,
    PhysicsRenderObjectKind Kind,
    string PrimPath,
    int InstanceIndex,
    bool IsSimulated,
    string Detail);

/// <summary>
/// The identity map one extraction produced, which is the only thing that turns a simulated
/// identity into a prim a backend can move.
/// </summary>
/// <param name="Revision">The extraction revision the bindings were produced at.</param>
/// <param name="Bindings">The bindings in extraction order.</param>
/// <param name="SkippedObjects">Extracted objects that carry no renderable pose.</param>
/// <param name="Detail">A sentence describing how the map was produced, or why it is empty.</param>
internal sealed record ViewerPhysicsBindingSet(
    ulong Revision,
    IReadOnlyList<ViewerPhysicsBinding> Bindings,
    int SkippedObjects,
    string Detail)
{
    /// <summary>Gets the empty map of a document whose stage carries no physics.</summary>
    internal static ViewerPhysicsBindingSet Empty { get; } =
        new(0, [], 0, "No physics identities were extracted from this stage.");
}

/// <summary>Counts how completely the simulated identities reached the active backend.</summary>
/// <param name="Bound">The identities the binding table holds.</param>
/// <param name="Refused">The bindings the bounded table refused.</param>
/// <param name="Skipped">The extracted objects that carry no renderable pose.</param>
/// <param name="Unresolved">Overrides the backend could not resolve in the latest batch.</param>
internal readonly record struct ViewerPhysicsBindingStats(
    int Bound,
    int Refused,
    int Skipped,
    int Unresolved)
{
    /// <summary>Gets a value indicating whether any identity is bound at all.</summary>
    internal bool HasBindings => Bound > 0;

    /// <summary>Formats the binding state as one inspector line.</summary>
    internal string Describe() => Bound == 0
        ? "No simulated identity is bound to an authored prim."
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{Bound} bound · {Refused} refused · {Skipped} skipped · {Unresolved} unresolved");
}

/// <summary>What the built world reports it can simulate, before rendering is considered.</summary>
/// <param name="Name">The capability name.</param>
/// <param name="IsSupported">Whether the built world provides it.</param>
/// <param name="Domain">The render domain it draws through, or <see langword="null"/>.</param>
/// <param name="Detail">A sentence describing the simulated state.</param>
internal sealed record ViewerPhysicsCapabilitySupport(
    string Name,
    bool IsSupported,
    PhysicsRenderDomain? Domain,
    string Detail);

/// <summary>Reports what applying or clearing a preview did.</summary>
/// <param name="Message">A sentence describing the outcome.</param>
/// <param name="Edits">
/// Every change the preview authored, exactly as the stage published it.
/// </param>
/// <param name="AppliedCount">The number of poses authored into the session overlay.</param>
/// <remarks>
/// A preview authors one change per chunk, so the outcome carries the whole bounded set rather
/// than a bracketing pair. Bracketing two serial reads around a multi-chunk apply would name a
/// range instead of the individual changes, and any unrelated edit committed inside that range
/// would be swallowed with them.
/// </remarks>
internal readonly record struct ViewerPhysicsPreviewOutcome(
    string Message,
    IReadOnlyList<ViewerPhysicsStageEdit> Edits,
    int AppliedCount)
{
    /// <summary>Gets the outcome of a preview that did nothing.</summary>
    internal static ViewerPhysicsPreviewOutcome None => new(string.Empty, [], 0);
}

/// <summary>The playback speeds the transport toolbar offers.</summary>
internal static class ViewerPhysicsSpeeds
{
    /// <summary>Gets the slowest offered speed.</summary>
    internal const double Minimum = 0.125d;

    /// <summary>Gets the fastest offered speed.</summary>
    internal const double Maximum = 8d;

    /// <summary>Gets the offered speeds, in the order the toolbar presents them.</summary>
    internal static IReadOnlyList<double> All { get; } = [0.25d, 0.5d, 1d, 2d, 4d];

    /// <summary>Clamps an arbitrary speed into the offered range.</summary>
    /// <param name="speed">The requested speed.</param>
    /// <returns>The clamped speed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The speed is not finite.</exception>
    internal static double Clamp(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speed),
                speed,
                "The playback speed must be a positive finite multiplier.");
        }

        return Math.Clamp(speed, Minimum, Maximum);
    }

    /// <summary>Formats a speed for a toolbar label.</summary>
    internal static string Format(double speed) =>
        string.Create(CultureInfo.InvariantCulture, $"{speed:0.###}x");
}

/// <summary>
/// Converts wall-clock time and a playback speed into whole fixed simulation steps.
/// </summary>
/// <remarks>
/// <para>
/// The speed scales how much wall-clock time playback accepts, never the fixed step the world
/// advances by. That distinction is the whole point: scaling the step would change the solver's
/// results, so a user who slows playback down to inspect a collision would be watching a different
/// simulation than the one that was baked.
/// </para>
/// <para>
/// The accumulator is bounded so a stalled UI thread cannot ask the worker for an unbounded
/// catch-up burst. Surplus wall time is dropped and counted, which slows playback down instead of
/// freezing the shell.
/// </para>
/// </remarks>
internal sealed class ViewerPhysicsPacer
{
    private readonly double _fixedStepSeconds;
    private readonly int _maxStepsPerPump;
    private double _accumulatedSeconds;
    private long _droppedCatchUpSteps;

    /// <summary>Initializes a pacer for one fixed step.</summary>
    /// <param name="fixedStepSeconds">The world's fixed simulation step, in seconds.</param>
    /// <param name="maxStepsPerPump">The most steps one pump may request.</param>
    internal ViewerPhysicsPacer(double fixedStepSeconds, int maxStepsPerPump = 8)
    {
        if (!double.IsFinite(fixedStepSeconds) || fixedStepSeconds <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fixedStepSeconds),
                fixedStepSeconds,
                "The fixed simulation step must be positive and finite.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStepsPerPump);
        _fixedStepSeconds = fixedStepSeconds;
        _maxStepsPerPump = maxStepsPerPump;
    }

    /// <summary>Gets the fixed simulation step, in seconds.</summary>
    internal double FixedStepSeconds => _fixedStepSeconds;

    /// <summary>Gets accepted wall-clock time that has not produced a step yet.</summary>
    internal double PendingSeconds => _accumulatedSeconds;

    /// <summary>Gets the number of steps dropped because the accumulator was bounded.</summary>
    internal long DroppedCatchUpSteps => _droppedCatchUpSteps;

    /// <summary>Accepts elapsed wall time and reports how many whole steps to request.</summary>
    /// <param name="elapsedSeconds">The wall-clock seconds since the previous pump.</param>
    /// <param name="speed">The playback speed multiplier.</param>
    /// <returns>The number of fixed steps to request, possibly zero.</returns>
    internal int Advance(double elapsedSeconds, double speed)
    {
        if (double.IsFinite(elapsedSeconds) && elapsedSeconds > 0d)
        {
            _accumulatedSeconds += elapsedSeconds * ViewerPhysicsSpeeds.Clamp(speed);
        }

        double available = Math.Floor(_accumulatedSeconds / _fixedStepSeconds);
        if (available <= 0d)
        {
            return 0;
        }

        int steps = available >= _maxStepsPerPump ? _maxStepsPerPump : (int)available;
        if (available > _maxStepsPerPump)
        {
            _droppedCatchUpSteps += (long)(available - _maxStepsPerPump);
            _accumulatedSeconds = 0d;
        }
        else
        {
            _accumulatedSeconds -= steps * _fixedStepSeconds;
            if (_accumulatedSeconds < 0d)
            {
                _accumulatedSeconds = 0d;
            }
        }

        return steps;
    }

    /// <summary>Drops accepted wall time, such as when playback pauses or the world resets.</summary>
    internal void Reset() => _accumulatedSeconds = 0d;
}

/// <summary>
/// Debounces observed authored edits and decides when the built world must be invalidated.
/// </summary>
/// <remarks>
/// A user dragging a value authors a burst of edits. Rebuilding on each one would make the viewer
/// unusable, so a physics-relevant edit only arms a timer; it fires once the burst has been quiet
/// for the debounce window. Visual-only edits never arm it at all.
/// </remarks>
internal sealed class ViewerPhysicsEditDebouncer
{
    private readonly double _debounceSeconds;
    private double _armedAtSeconds;
    private bool _armed;
    private long _observedRelevantEdits;
    private long _observedVisualEdits;

    /// <summary>Initializes a debouncer.</summary>
    /// <param name="debounceSeconds">The quiet window a burst must reach before firing.</param>
    internal ViewerPhysicsEditDebouncer(double debounceSeconds = 0.25d)
    {
        if (!double.IsFinite(debounceSeconds) || debounceSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(debounceSeconds),
                debounceSeconds,
                "The debounce window must be a non-negative finite number of seconds.");
        }

        _debounceSeconds = debounceSeconds;
    }

    /// <summary>Gets a value indicating whether an invalidation is pending.</summary>
    internal bool IsArmed => _armed;

    /// <summary>Gets the number of physics-relevant edits observed.</summary>
    internal long ObservedRelevantEdits => _observedRelevantEdits;

    /// <summary>Gets the number of visual-only edits observed.</summary>
    internal long ObservedVisualEdits => _observedVisualEdits;

    /// <summary>Observes one classified edit.</summary>
    /// <param name="kind">The classification of the observed edit.</param>
    /// <param name="nowSeconds">The monotonic clock reading of the observation.</param>
    internal void Observe(ViewerPhysicsEditKind kind, double nowSeconds)
    {
        if (kind == ViewerPhysicsEditKind.Visual)
        {
            _observedVisualEdits++;
            return;
        }

        _observedRelevantEdits++;

        // The timestamp is written before the flag. Edits are observed from the stage-change
        // thread while the UI dispatcher reads this state, so a reader that saw the flag first
        // could pair it with the previous burst's timestamp, decide the quiet window had already
        // elapsed, and rebuild the world on the first edit of a burst - the exact behaviour the
        // debounce exists to prevent.
        _armedAtSeconds = nowSeconds;
        Volatile.Write(ref _armed, true);
    }

    /// <summary>Reports whether the armed invalidation should fire now.</summary>
    /// <param name="nowSeconds">The monotonic clock reading of the check.</param>
    /// <returns><see langword="true"/> exactly once per quiet burst.</returns>
    internal bool ShouldInvalidate(double nowSeconds)
    {
        if (!Volatile.Read(ref _armed) || nowSeconds - _armedAtSeconds < _debounceSeconds)
        {
            return false;
        }

        _armed = false;
        return true;
    }

    /// <summary>Disarms a pending invalidation, such as after an explicit rebuild.</summary>
    internal void Reset() => _armed = false;
}

/// <summary>
/// Decides whether an observed authored change can alter simulated behaviour.
/// </summary>
/// <remarks>
/// The classification is deliberately conservative in the direction that costs correctness least:
/// an unknown change is treated as physics-relevant, because rebuilding a world that did not need
/// it merely costs time, while continuing to simulate a stale world silently shows the user
/// something the stage no longer describes.
/// </remarks>
internal static class ViewerPhysicsEditClassifier
{
    private static readonly string[] VisualOnlyFields =
    [
        "primvars:displayColor",
        "primvars:displayOpacity",
        "visibility",
        "purpose",
        "material:binding",
        "doubleSided",
    ];

    /// <summary>Classifies one changed property or prim field.</summary>
    /// <param name="field">The changed field or property name, or an empty string.</param>
    /// <returns>The classification the controller acts on.</returns>
    internal static ViewerPhysicsEditKind Classify(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return ViewerPhysicsEditKind.Relevant;
        }

        foreach (string visual in VisualOnlyFields)
        {
            if (field.EndsWith(visual, StringComparison.Ordinal))
            {
                return ViewerPhysicsEditKind.Visual;
            }
        }

        return ViewerPhysicsEditKind.Relevant;
    }

    /// <summary>Classifies a whole set of changed fields.</summary>
    /// <param name="fields">The changed fields.</param>
    /// <returns>
    /// <see cref="ViewerPhysicsEditKind.Relevant"/> when any field is relevant, and
    /// <see cref="ViewerPhysicsEditKind.Visual"/> only when every field is visual-only.
    /// </returns>
    internal static ViewerPhysicsEditKind Classify(IReadOnlyList<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count == 0)
        {
            return ViewerPhysicsEditKind.Relevant;
        }

        for (int index = 0; index < fields.Count; index++)
        {
            if (Classify(fields[index]) == ViewerPhysicsEditKind.Relevant)
            {
                return ViewerPhysicsEditKind.Relevant;
            }
        }

        return ViewerPhysicsEditKind.Visual;
    }
}

/// <summary>How an existing authored time sample is treated by a bake.</summary>
internal enum ViewerPhysicsBakePolicy
{
    /// <summary>Replace the authored sample.</summary>
    Overwrite,

    /// <summary>Keep the authored sample and skip the simulated one.</summary>
    Skip,

    /// <summary>Refuse the whole bake.</summary>
    Reject,
}

/// <summary>One validated bake request produced by the bake dialog.</summary>
/// <param name="DestinationLayerIdentifier">The file-backed layer the samples are written to.</param>
/// <param name="StartTimeCode">The first authored time code to bake.</param>
/// <param name="EndTimeCode">The last authored time code to bake.</param>
/// <param name="SampleStride">The authored time-code stride between samples.</param>
/// <param name="Policy">How existing authored samples are treated.</param>
/// <param name="Save">Whether the destination layer is saved when the bake commits.</param>
internal sealed record ViewerPhysicsBakeRequest(
    string DestinationLayerIdentifier,
    double StartTimeCode,
    double EndTimeCode,
    double SampleStride,
    ViewerPhysicsBakePolicy Policy,
    bool Save);

/// <summary>Reports why a bake request cannot run.</summary>
/// <param name="IsValid">Whether the request may be submitted.</param>
/// <param name="Message">The reason it was refused, or an empty string.</param>
internal readonly record struct ViewerPhysicsBakeValidation(bool IsValid, string Message)
{
    /// <summary>Gets the validation of an acceptable request.</summary>
    internal static ViewerPhysicsBakeValidation Valid => new(true, string.Empty);
}

/// <summary>Validates bake requests before they reach the stage scheduler.</summary>
internal static class ViewerPhysicsBakeValidator
{
    private static readonly string[] FileBackedExtensions = [".usd", ".usda", ".usdc"];

    /// <summary>Validates one bake request.</summary>
    /// <param name="request">The request the dialog produced.</param>
    /// <returns>Whether the request may be submitted, and why not when it may not.</returns>
    internal static ViewerPhysicsBakeValidation Validate(ViewerPhysicsBakeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DestinationLayerIdentifier))
        {
            return new ViewerPhysicsBakeValidation(
                false,
                "Choose a file-backed destination layer for the bake.");
        }

        string extension = Path.GetExtension(request.DestinationLayerIdentifier);
        bool fileBacked = false;
        foreach (string candidate in FileBackedExtensions)
        {
            if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
            {
                fileBacked = true;
                break;
            }
        }

        if (!fileBacked)
        {
            return new ViewerPhysicsBakeValidation(
                false,
                "The bake destination must be a .usd, .usda, or .usdc file-backed layer.");
        }

        if (!double.IsFinite(request.StartTimeCode) || !double.IsFinite(request.EndTimeCode))
        {
            return new ViewerPhysicsBakeValidation(
                false,
                "The bake range must use finite authored time codes.");
        }

        if (request.EndTimeCode < request.StartTimeCode)
        {
            return new ViewerPhysicsBakeValidation(
                false,
                "The bake end time code must not precede the start time code.");
        }

        if (!double.IsFinite(request.SampleStride) || request.SampleStride <= 0d)
        {
            return new ViewerPhysicsBakeValidation(
                false,
                "The bake sample stride must be a positive number of time codes.");
        }

        return ViewerPhysicsBakeValidation.Valid;
    }
}

/// <summary>Reports bake progress to the dialog without exposing stage handles.</summary>
/// <param name="CompletedSamples">The number of committed samples.</param>
/// <param name="TotalSamples">The number of samples the range covers, or zero when unknown.</param>
/// <param name="TimeCode">The authored time code most recently committed.</param>
internal readonly record struct ViewerPhysicsBakeProgress(
    int CompletedSamples,
    int TotalSamples,
    double TimeCode)
{
    /// <summary>Formats the progress for the dialog's status line.</summary>
    internal string Describe() =>
        TotalSamples > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Baked {CompletedSamples}/{TotalSamples} samples (time {TimeCode:0.###}).")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Baked {CompletedSamples} samples (time {TimeCode:0.###}).");
}

/// <summary>Reports what a completed bake did.</summary>
/// <param name="Succeeded">Whether the transaction committed.</param>
/// <param name="WasSaved">Whether the destination layer was saved.</param>
/// <param name="CommittedSamples">The number of committed samples.</param>
/// <param name="Message">A sentence describing the outcome.</param>
internal sealed record ViewerPhysicsBakeOutcome(
    bool Succeeded,
    bool WasSaved,
    int CommittedSamples,
    string Message);

/// <summary>The complete state the physics UI renders from.</summary>
/// <param name="Status">The transport progress.</param>
/// <param name="IsEnabled">Whether a controller exists for the document.</param>
/// <param name="IsBusy">Whether a command owns the transport.</param>
/// <param name="IsPlaying">Whether the viewer is pacing the world forward.</param>
/// <param name="Loop">Whether playback wraps at the authored end.</param>
/// <param name="Speed">The playback speed multiplier.</param>
/// <param name="PreviewEnabled">Whether simulated poses are applied to the session overlay.</param>
/// <param name="StartTimeCode">The authored start time code.</param>
/// <param name="EndTimeCode">The authored end time code.</param>
/// <param name="Error">The most recent failure message, or an empty string.</param>
internal readonly record struct ViewerPhysicsStatusSnapshot(
    ViewerPhysicsTransportStatus Status,
    bool IsEnabled,
    bool IsBusy,
    bool IsPlaying,
    bool Loop,
    double Speed,
    bool PreviewEnabled,
    double StartTimeCode,
    double EndTimeCode,
    string Error)
{
    /// <summary>Gets the snapshot of a document with no physics controller.</summary>
    internal static ViewerPhysicsStatusSnapshot Disabled =>
        new(
            ViewerPhysicsTransportStatus.Disabled,
            IsEnabled: false,
            IsBusy: false,
            IsPlaying: false,
            Loop: false,
            Speed: 1d,
            PreviewEnabled: false,
            StartTimeCode: 0d,
            EndTimeCode: 0d,
            Error: "");

    /// <summary>Reports whether one command can run in this state.</summary>
    /// <param name="command">The command to test.</param>
    /// <returns>Whether the command can run, and why not when it cannot.</returns>
    internal ViewerPhysicsCommandAvailability GetAvailability(ViewerPhysicsCommand command)
    {
        if (command == ViewerPhysicsCommand.Enable)
        {
            return IsEnabled
                ? ViewerPhysicsCommandAvailability.NotApplicable
                : ViewerPhysicsCommandAvailability.Available;
        }

        if (!IsEnabled)
        {
            return ViewerPhysicsCommandAvailability.NotEnabled;
        }

        if (IsBusy)
        {
            return ViewerPhysicsCommandAvailability.Busy;
        }

        bool needsRebuild = Status.State
            is ViewerPhysicsRunState.Invalidated
            or ViewerPhysicsRunState.Faulted;
        if (needsRebuild)
        {
            return command == ViewerPhysicsCommand.Rebuild
                ? ViewerPhysicsCommandAvailability.Available
                : ViewerPhysicsCommandAvailability.NeedsRebuild;
        }

        return command switch
        {
            ViewerPhysicsCommand.Play when IsPlaying =>
                ViewerPhysicsCommandAvailability.NotApplicable,
            ViewerPhysicsCommand.Pause when !IsPlaying =>
                ViewerPhysicsCommandAvailability.NotApplicable,
            ViewerPhysicsCommand.StepOneFrame when IsPlaying =>
                ViewerPhysicsCommandAvailability.NotApplicable,
            _ => ViewerPhysicsCommandAvailability.Available,
        };
    }

    /// <summary>Reports whether one command can run in this state.</summary>
    /// <param name="command">The command to test.</param>
    /// <returns><see langword="true"/> when the command can run.</returns>
    internal bool CanRun(ViewerPhysicsCommand command) =>
        GetAvailability(command) == ViewerPhysicsCommandAvailability.Available;

    /// <summary>Explains a refused command in one sentence, for a tooltip.</summary>
    /// <param name="command">The refused command.</param>
    /// <returns>The explanation, or an empty string when the command can run.</returns>
    internal string DescribeUnavailable(ViewerPhysicsCommand command) =>
        GetAvailability(command) switch
        {
            ViewerPhysicsCommandAvailability.Available => string.Empty,
            ViewerPhysicsCommandAvailability.NotEnabled =>
                "Enable physics for this stage first.",
            ViewerPhysicsCommandAvailability.Busy =>
                "The physics worker is busy; the command runs once it finishes.",
            ViewerPhysicsCommandAvailability.NeedsRebuild =>
                "The built world is stale; rebuild it before simulating again.",
            _ => "The command does not apply in the current physics state.",
        };
}

/// <summary>Formats physics state for the toolbar and the inspector.</summary>
internal static class ViewerPhysicsStatusFormatter
{
    /// <summary>Formats the one-line toolbar status.</summary>
    /// <param name="snapshot">The state to describe.</param>
    /// <returns>The status line.</returns>
    internal static string FormatStatus(in ViewerPhysicsStatusSnapshot snapshot)
    {
        if (!snapshot.IsEnabled)
        {
            return "Physics: off";
        }

        string state = snapshot.IsBusy
            ? "Busy"
            : snapshot.IsPlaying
                ? "Playing"
                : snapshot.Status.State.ToString();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Physics: {state} · time {snapshot.Status.TimeCode:0.###} · " +
            $"step {snapshot.Status.StepIndex} · backlog {snapshot.Status.BacklogSeconds * 1000d:0} ms · " +
            $"queue {snapshot.Status.QueueDepth}");
    }

    /// <summary>Formats the accessible name of the play or pause button.</summary>
    /// <param name="isPlaying">Whether the viewer is pacing the world forward.</param>
    /// <returns>The accessible name.</returns>
    internal static string FormatPlayPauseName(bool isPlaying) =>
        isPlaying ? "Pause physics simulation" : "Play physics simulation";
}
