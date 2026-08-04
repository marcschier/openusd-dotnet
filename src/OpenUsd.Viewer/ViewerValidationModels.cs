// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.Viewer;

internal sealed record ViewerValidationErrorSnapshot(
    UsdValidationSeverity Severity,
    string ValidatorName,
    string ErrorName,
    string Message,
    string Sites);

internal sealed class ViewerValidationSnapshot : IUsdDetachedResult, IEquatable<ViewerValidationSnapshot>
{
    internal ViewerValidationSnapshot(
        int validatorCount,
        TimeSpan duration,
        ViewerValidationErrorSnapshot[] errors)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(validatorCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(errors);
        ValidatorCount = validatorCount;
        Duration = duration;
        Errors = errors;
    }

    internal static ViewerValidationSnapshot Empty { get; } = new(0, TimeSpan.Zero, []);

    internal int ValidatorCount { get; }

    internal TimeSpan Duration { get; }

    internal ViewerValidationErrorSnapshot[] Errors { get; }

    internal int ErrorCount => Count(UsdValidationSeverity.Error);

    internal int WarningCount => Count(UsdValidationSeverity.Warning);

    internal int InfoCount => Count(UsdValidationSeverity.Info);

    internal static ViewerValidationSnapshot Create(
        IReadOnlyList<UsdValidationValidatorInfo> validators,
        IReadOnlyList<UsdValidationError> errors,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(validators);
        ArgumentNullException.ThrowIfNull(errors);
        var snapshots = new ViewerValidationErrorSnapshot[errors.Count];
        for (int index = 0; index < snapshots.Length; index++)
        {
            UsdValidationError error = errors[index];
            snapshots[index] = new ViewerValidationErrorSnapshot(
                error.Severity,
                error.ValidatorName,
                error.ErrorName,
                error.Message,
                string.Join(", ", error.Sites));
        }
        return new ViewerValidationSnapshot(validators.Count, duration, snapshots);
    }

    public bool Equals(ViewerValidationSnapshot? other) =>
        other is not null &&
        ValidatorCount == other.ValidatorCount &&
        Duration == other.Duration &&
        Errors.SequenceEqual(other.Errors);

    public override bool Equals(object? obj) =>
        obj is ViewerValidationSnapshot other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            ValidatorCount,
            Duration,
            SequenceHashCode(Errors));

    public override string ToString() =>
        $"{nameof(ViewerValidationSnapshot)} {{ {nameof(ValidatorCount)} = {ValidatorCount}, " +
        $"{nameof(Duration)} = {Duration}, {nameof(Errors)} = " +
        $"{FormatSequence(Errors)} }}";

    private int Count(UsdValidationSeverity severity)
    {
        int count = 0;
        foreach (ViewerValidationErrorSnapshot error in Errors)
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
        if (snapshot.ValidatorCount == 0 && snapshot.Errors.Length == 0)
        {
            return "Open a USD stage to run UsdValidation.";
        }
        return $"UsdValidation: {snapshot.Errors.Length} result(s) from " +
            $"{snapshot.ValidatorCount} validator(s) in {FormatDuration(snapshot.Duration)}; " +
            $"errors: {snapshot.ErrorCount}; warnings: {snapshot.WarningCount}; info: {snapshot.InfoCount}.";
    }

    internal static string FormatDetails(ViewerValidationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ValidatorCount == 0 && snapshot.Errors.Length == 0)
        {
            return "No validation results.";
        }
        if (snapshot.Errors.Length == 0)
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
        return builder.ToString().TrimEnd();
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMilliseconds < 1
            ? "<1 ms"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{duration.TotalMilliseconds:0.###} ms");
}
