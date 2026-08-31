// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.Viewer;

/// <summary>What a validation run covered.</summary>
internal enum ViewerValidationScope
{
    /// <summary>Every registered validator ran against the whole stage.</summary>
    Stage,

    /// <summary>The prim validators ran against one selected prim.</summary>
    Prim
}

/// <summary>What happened to the most recent validation request.</summary>
/// <remarks>
/// Every state the Validation tab can display lives here rather than in a
/// direct write to a control. A transient message written straight to a
/// TextBlock survives until something else overwrites it, so the next render
/// of unchanged state - a panel toggle, a busy-state change - would restore a
/// stale line that no longer describes anything. Rendering is therefore a pure
/// function of this snapshot.
/// </remarks>
internal enum ViewerValidationRunState
{
    /// <summary>No run has been requested yet.</summary>
    NotRun,

    /// <summary>A run is in flight.</summary>
    Running,

    /// <summary>A run finished and produced the retained results.</summary>
    Completed,

    /// <summary>A prim-scoped run was requested with no prim selected.</summary>
    NoSelection,

    /// <summary>A run failed; <see cref="ViewerValidationSnapshot.Message"/> says why.</summary>
    Failed,

    /// <summary>A run was cancelled, usually because the document closed.</summary>
    Cancelled
}

internal sealed record ViewerValidationErrorSnapshot(
    UsdValidationSeverity Severity,
    string ValidatorName,
    string ErrorName,
    string Message,
    string Sites);

internal sealed class ViewerValidationSnapshot : IUsdDetachedResult, IEquatable<ViewerValidationSnapshot>
{
    /// <summary>The most individual results one snapshot retains.</summary>
    /// <remarks>
    /// A stage can report thousands of validation errors, and this snapshot is
    /// copied out of the scheduler and then rendered as text on the UI thread,
    /// so an unbounded result set is an unbounded snapshot and an unbounded
    /// visual tree. The per-severity counts are taken before truncation, so a
    /// truncated snapshot still reports how many results actually exist.
    /// </remarks>
    internal const int MaxRetainedErrors = 200;

    /// <summary>The most characters one retained result keeps of its message.</summary>
    internal const int MaxMessageLength = 512;

    /// <summary>The most characters one retained result keeps of its site list.</summary>
    internal const int MaxSitesLength = 256;

    internal ViewerValidationSnapshot(
        int registeredValidatorCount,
        TimeSpan duration,
        ViewerValidationErrorSnapshot[] errors,
        int reportedCount = -1,
        int errorCount = -1,
        int warningCount = -1,
        int infoCount = -1,
        ViewerValidationScope scope = ViewerValidationScope.Stage,
        string scopePath = "",
        int unclassifiedCount = -1,
        ViewerValidationRunState runState = ViewerValidationRunState.Completed,
        string message = "",
        string diagnostics = "")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(registeredValidatorCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(scopePath);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(diagnostics);
        RegisteredValidatorCount = registeredValidatorCount;
        Duration = duration;
        Errors = errors;
        ReportedCount = reportedCount < 0 ? errors.Length : reportedCount;
        ArgumentOutOfRangeException.ThrowIfLessThan(ReportedCount, errors.Length);
        ErrorCount = errorCount < 0 ? Count(errors, UsdValidationSeverity.Error) : errorCount;
        WarningCount = warningCount < 0 ? Count(errors, UsdValidationSeverity.Warning) : warningCount;
        InfoCount = infoCount < 0 ? Count(errors, UsdValidationSeverity.Info) : infoCount;
        UnclassifiedCount = unclassifiedCount < 0
            ? ReportedCount - ErrorCount - WarningCount - InfoCount
            : unclassifiedCount;
        Scope = scope;
        ScopePath = scopePath;
        RunState = runState;
        Message = message;
        Diagnostics = diagnostics;
    }

    /// <summary>The state before any run was requested.</summary>
    internal static ViewerValidationSnapshot Empty { get; } =
        new(0, TimeSpan.Zero, [], runState: ViewerValidationRunState.NotRun);

    /// <summary>The number of validators in the registry when the run started.</summary>
    /// <remarks>
    /// This is what the registry reports, not what a run executed: a
    /// prim-scoped run only executes the prim validators among them.
    /// </remarks>
    internal int RegisteredValidatorCount { get; }

    /// <summary>How long the run took. Deliberately not part of equality.</summary>
    internal TimeSpan Duration { get; }

    /// <summary>The retained results, most severe first.</summary>
    internal ViewerValidationErrorSnapshot[] Errors { get; }

    /// <summary>The number of results the run reported, before truncation.</summary>
    internal int ReportedCount { get; }

    internal ViewerValidationScope Scope { get; }

    /// <summary>The prim a prim-scoped run covered, or the empty string.</summary>
    internal string ScopePath { get; }

    internal ViewerValidationRunState RunState { get; }

    /// <summary>A short reason for a failed or otherwise non-result state.</summary>
    internal string Message { get; }

    /// <summary>Long-form diagnostics for a failure, shown in the detail pane.</summary>
    internal string Diagnostics { get; }

    internal bool IsTruncated => ReportedCount > Errors.Length;

    internal int ErrorCount { get; }

    internal int WarningCount { get; }

    internal int InfoCount { get; }

    /// <summary>
    /// Results whose severity is neither error, warning, nor info - today
    /// <see cref="UsdValidationSeverity.None"/>, or any value a future
    /// OpenUSD adds that this build does not recognize.
    /// </summary>
    internal int UnclassifiedCount { get; }

    /// <summary>Ranks a severity for display and for truncation.</summary>
    /// <remarks>
    /// An unrecognized severity ranks last but is still ranked, so a value a
    /// newer OpenUSD introduces is retained and shown rather than silently
    /// dropped by an ordering that did not anticipate it.
    /// </remarks>
    internal static int Rank(UsdValidationSeverity severity) => severity switch
    {
        UsdValidationSeverity.Error => 0,
        UsdValidationSeverity.Warning => 1,
        UsdValidationSeverity.Info => 2,
        UsdValidationSeverity.None => 3,
        _ => 4
    };

    /// <summary>Projects a completed run.</summary>
    internal static ViewerValidationSnapshot Create(
        IReadOnlyList<UsdValidationValidatorInfo> validators,
        IReadOnlyList<UsdValidationError> errors,
        TimeSpan duration,
        ViewerValidationScope scope = ViewerValidationScope.Stage,
        string scopePath = "")
    {
        ArgumentNullException.ThrowIfNull(validators);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(scopePath);

        int errorCount = 0;
        int warningCount = 0;
        int infoCount = 0;
        int unclassifiedCount = 0;
        foreach (UsdValidationError error in errors)
        {
            switch (error.Severity)
            {
                case UsdValidationSeverity.Error:
                    errorCount++;
                    break;
                case UsdValidationSeverity.Warning:
                    warningCount++;
                    break;
                case UsdValidationSeverity.Info:
                    infoCount++;
                    break;
                default:
                    unclassifiedCount++;
                    break;
            }
        }

        // Truncation has to keep the results that matter, so results are
        // ordered by severity rank and the window is taken from the front.
        // The sort is made stable by the original index, so within one
        // severity the order stays the one UsdValidation already made stable
        // and two runs over an unchanged stage retain the same window.
        var order = new int[errors.Count];
        for (int index = 0; index < order.Length; index++)
        {
            order[index] = index;
        }
        Array.Sort(order, (left, right) =>
        {
            int compared = Rank(errors[left].Severity).CompareTo(Rank(errors[right].Severity));
            return compared != 0 ? compared : left.CompareTo(right);
        });

        int retainedCount = Math.Min(errors.Count, MaxRetainedErrors);
        var retained = new ViewerValidationErrorSnapshot[retainedCount];
        for (int index = 0; index < retainedCount; index++)
        {
            UsdValidationError error = errors[order[index]];
            retained[index] = new ViewerValidationErrorSnapshot(
                error.Severity,
                error.ValidatorName,
                error.ErrorName,
                ViewerScalarFormatter.Bound(error.Message, MaxMessageLength),
                ViewerScalarFormatter.Bound(string.Join(", ", error.Sites), MaxSitesLength));
        }

        return new ViewerValidationSnapshot(
            validators.Count,
            duration,
            retained,
            errors.Count,
            errorCount,
            warningCount,
            infoCount,
            scope,
            scopePath,
            unclassifiedCount);
    }

    /// <summary>The state while a run of the given scope is in flight.</summary>
    internal static ViewerValidationSnapshot Running(
        ViewerValidationScope scope,
        string scopePath) =>
        new(
            0,
            TimeSpan.Zero,
            [],
            scope: scope,
            scopePath: scopePath,
            runState: ViewerValidationRunState.Running);

    /// <summary>The state for a prim-scoped request with no prim selected.</summary>
    internal static ViewerValidationSnapshot NoSelection() =>
        new(
            0,
            TimeSpan.Zero,
            [],
            scope: ViewerValidationScope.Prim,
            runState: ViewerValidationRunState.NoSelection);

    /// <summary>The state for a run that failed.</summary>
    internal static ViewerValidationSnapshot Failed(
        ViewerValidationScope scope,
        string scopePath,
        string message,
        string diagnostics = "") =>
        new(
            0,
            TimeSpan.Zero,
            [],
            scope: scope,
            scopePath: scopePath,
            runState: ViewerValidationRunState.Failed,
            message: string.IsNullOrWhiteSpace(message) ? "no reason was reported" : message.Trim(),
            diagnostics: diagnostics ?? string.Empty);

    /// <summary>The state for a run abandoned because its document went away.</summary>
    internal static ViewerValidationSnapshot Cancelled(
        ViewerValidationScope scope,
        string scopePath) =>
        new(
            0,
            TimeSpan.Zero,
            [],
            scope: scope,
            scopePath: scopePath,
            runState: ViewerValidationRunState.Cancelled);

    /// <summary>
    /// Compares what a run found, not how long it took.
    /// </summary>
    /// <remarks>
    /// <see cref="Duration"/> is measured wall-clock time and is never equal
    /// between two runs, so including it would make every poll of an unchanged
    /// stage compare unequal and defeat the point of a detached snapshot.
    /// </remarks>
    public bool Equals(ViewerValidationSnapshot? other) =>
        other is not null &&
        RunState == other.RunState &&
        RegisteredValidatorCount == other.RegisteredValidatorCount &&
        ReportedCount == other.ReportedCount &&
        ErrorCount == other.ErrorCount &&
        WarningCount == other.WarningCount &&
        InfoCount == other.InfoCount &&
        UnclassifiedCount == other.UnclassifiedCount &&
        Scope == other.Scope &&
        string.Equals(ScopePath, other.ScopePath, StringComparison.Ordinal) &&
        string.Equals(Message, other.Message, StringComparison.Ordinal) &&
        string.Equals(Diagnostics, other.Diagnostics, StringComparison.Ordinal) &&
        Errors.SequenceEqual(other.Errors);

    public override bool Equals(object? obj) =>
        obj is ViewerValidationSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(RunState);
        hash.Add(RegisteredValidatorCount);
        hash.Add(ReportedCount);
        hash.Add(ErrorCount);
        hash.Add(WarningCount);
        hash.Add(InfoCount);
        hash.Add(UnclassifiedCount);
        hash.Add(Scope);
        hash.Add(ScopePath, StringComparer.Ordinal);
        hash.Add(Message, StringComparer.Ordinal);
        hash.Add(Diagnostics, StringComparer.Ordinal);
        hash.Add(SequenceHashCode(Errors));
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{nameof(ViewerValidationSnapshot)} {{ {nameof(RunState)} = {RunState}, " +
        $"{nameof(RegisteredValidatorCount)} = {RegisteredValidatorCount}, " +
        $"{nameof(Duration)} = {Duration}, {nameof(Scope)} = {Scope}, " +
        $"{nameof(ScopePath)} = {ScopePath}, {nameof(ReportedCount)} = {ReportedCount}, " +
        $"{nameof(Message)} = {Message}, {nameof(Errors)} = " +
        $"{FormatSequence(Errors)} }}";

    private static int Count(
        IReadOnlyList<ViewerValidationErrorSnapshot> errors,
        UsdValidationSeverity severity)
    {
        int count = 0;
        foreach (ViewerValidationErrorSnapshot error in errors)
        {
            if (error.Severity == severity)
            {
                count++;
            }
        }
        return count;
    }

    private static int SequenceHashCode(IEnumerable<ViewerValidationErrorSnapshot> values)
    {
        var hash = new HashCode();
        foreach (ViewerValidationErrorSnapshot value in values)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    private static string FormatSequence(IEnumerable<ViewerValidationErrorSnapshot> values) =>
        "[" + string.Join(", ", values) + "]";
}

internal static class ViewerValidationFormatter
{
    internal static string FormatState(ViewerValidationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        switch (snapshot.RunState)
        {
            case ViewerValidationRunState.NotRun:
                return "Open a USD stage to run UsdValidation.";
            case ViewerValidationRunState.Running:
                return $"Running UsdValidation ({FormatScope(snapshot)})...";
            case ViewerValidationRunState.NoSelection:
                return "Select a prim to validate, or switch the scope to the stage.";
            case ViewerValidationRunState.Cancelled:
                return $"UsdValidation ({FormatScope(snapshot)}) was cancelled.";
            case ViewerValidationRunState.Failed:
                return $"UsdValidation ({FormatScope(snapshot)}) failed: {snapshot.Message}";
            default:
                break;
        }

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"UsdValidation ({FormatScope(snapshot)}): ");
        builder.Append(
            CultureInfo.InvariantCulture,
            $"{snapshot.ReportedCount} result(s) from " +
            $"{snapshot.RegisteredValidatorCount} registered validator(s) ");
        builder.Append(CultureInfo.InvariantCulture, $"in {FormatDuration(snapshot.Duration)}; ");
        builder.Append(
            CultureInfo.InvariantCulture,
            $"errors: {snapshot.ErrorCount}; warnings: {snapshot.WarningCount}; " +
            $"info: {snapshot.InfoCount}.");
        if (snapshot.UnclassifiedCount > 0)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $" Unclassified severity: {snapshot.UnclassifiedCount}.");
        }
        if (snapshot.IsTruncated)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $" Showing the {snapshot.Errors.Length} most severe.");
        }
        return builder.ToString();
    }

    /// <summary>Names what a run covered, for the state line.</summary>
    internal static string FormatScope(ViewerValidationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Scope == ViewerValidationScope.Prim && snapshot.ScopePath.Length > 0
            ? $"prim {snapshot.ScopePath}"
            : "whole stage";
    }

    internal static string FormatDetails(ViewerValidationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        switch (snapshot.RunState)
        {
            case ViewerValidationRunState.Running:
                return "Running UsdValidation...";
            case ViewerValidationRunState.Failed:
                return snapshot.Diagnostics.Length > 0 ? snapshot.Diagnostics : snapshot.Message;
            case ViewerValidationRunState.NotRun:
            case ViewerValidationRunState.NoSelection:
            case ViewerValidationRunState.Cancelled:
                return "No validation results.";
            default:
                break;
        }

        if (snapshot.ReportedCount == 0)
        {
            return "No UsdValidation errors were reported.";
        }

        var builder = new StringBuilder();
        for (int index = 0; index < snapshot.Errors.Length; index++)
        {
            ViewerValidationErrorSnapshot error = snapshot.Errors[index];
            if (index > 0)
            {
                builder.AppendLine();
            }
            builder.Append(CultureInfo.InvariantCulture, $"{index + 1}. {error.Severity}");
            builder.Append(" [");
            builder.Append(error.ValidatorName);
            if (!string.IsNullOrEmpty(error.ErrorName))
            {
                builder.Append('/');
                builder.Append(error.ErrorName);
            }
            builder.Append("] ");
            builder.AppendLine(error.Message);
            if (!string.IsNullOrEmpty(error.Sites))
            {
                builder.Append("   Sites: ");
                builder.AppendLine(error.Sites);
            }
        }
        if (snapshot.IsTruncated)
        {
            builder.AppendLine();
            builder.Append(
                CultureInfo.InvariantCulture,
                $"... {snapshot.ReportedCount - snapshot.Errors.Length} more result(s) not shown; " +
                $"the {snapshot.Errors.Length} most severe are listed.");
        }
        return builder.ToString().TrimEnd();
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMilliseconds < 1
            ? "<1 ms"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{duration.TotalMilliseconds:0.###} ms");
}
