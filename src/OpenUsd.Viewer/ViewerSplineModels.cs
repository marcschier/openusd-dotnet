// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.Viewer;

/// <summary>One evaluated point on a Ts spline, or a value block.</summary>
internal sealed record ViewerSplineSampleSnapshot(double Time, double? Value) : IUsdDetachedResult;

/// <summary>
/// A detached, bounded projection of one attribute's authored Ts spline.
/// </summary>
/// <remarks>
/// <see cref="TsSpline"/> owns a native handle and cannot leave the stage
/// scheduler, so the Viewer copies the authored knots plus a fixed, small
/// evaluated preview while it still holds stage access. Knots are capped so a
/// dense spline cannot make one inspector rebuild unbounded; the untruncated
/// count is kept so the UI can say so rather than silently showing less.
/// </remarks>
internal sealed class ViewerSplineSnapshot : IUsdDetachedResult, IEquatable<ViewerSplineSnapshot>
{
    /// <summary>The most knots one snapshot retains.</summary>
    internal const int MaxKnots = 32;

    /// <summary>The number of evaluated preview samples, including both endpoints.</summary>
    internal const int SampleCount = 9;

    /// <summary>The fraction of the knot span sampled outside the first and last knot.</summary>
    private const double ExtrapolationMarginFraction = 0.1;

    internal ViewerSplineSnapshot(
        TsCurveType curveType,
        bool isTimeValued,
        TsExtrapolation preExtrapolation,
        TsExtrapolation postExtrapolation,
        int knotCount,
        TsKnot[] knots,
        ViewerSplineSampleSnapshot[] samples,
        string? error = null)
    {
        ArgumentNullException.ThrowIfNull(knots);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegative(knotCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(knotCount, knots.Length);
        CurveType = curveType;
        IsTimeValued = isTimeValued;
        PreExtrapolation = preExtrapolation;
        PostExtrapolation = postExtrapolation;
        KnotCount = knotCount;
        Knots = knots;
        Samples = samples;
        Error = error;
    }

    internal TsCurveType CurveType { get; }

    internal bool IsTimeValued { get; }

    internal TsExtrapolation PreExtrapolation { get; }

    internal TsExtrapolation PostExtrapolation { get; }

    /// <summary>The authored knot count before truncation.</summary>
    internal int KnotCount { get; }

    internal TsKnot[] Knots { get; }

    internal ViewerSplineSampleSnapshot[] Samples { get; }

    internal bool IsTruncated => KnotCount > Knots.Length;

    /// <summary>
    /// The reason the spline was not projected, or null when it was read.
    /// </summary>
    /// <remarks>
    /// A spline the native runtime refuses is a property of that one
    /// attribute, so it is projected as an unavailable snapshot rather than
    /// thrown: an inspector that lost every other attribute because one
    /// spline failed would be strictly less useful than one that says so.
    /// </remarks>
    internal string? Error { get; }

    /// <summary>Whether this projection stands in for a spline that was never read.</summary>
    internal bool IsNotRead { get; private init; }

    /// <summary>The message used when the native runtime reports no reason.</summary>
    internal const string UnknownErrorMessage = "the native runtime reported no reason";

    /// <summary>Projects one attribute whose spline could not be read.</summary>
    /// <remarks>
    /// The message comes from a native error buffer, which is allowed to be
    /// empty. This is called from a catch block, so it must not throw: an
    /// argument check here would replace a contained failure with the
    /// uncontained one it exists to prevent.
    /// </remarks>
    internal static ViewerSplineSnapshot CreateUnreadable(string? message) =>
        CreateUnavailable(
            string.IsNullOrWhiteSpace(message) ? UnknownErrorMessage : message.Trim(),
            isNotRead: false);

    /// <summary>
    /// Projects one attribute whose spline was deliberately not read because
    /// the inspector's spline budget was already spent.
    /// </summary>
    internal static ViewerSplineSnapshot CreateNotRead(string reason) =>
        CreateUnavailable(
            string.IsNullOrWhiteSpace(reason) ? UnknownErrorMessage : reason.Trim(),
            isNotRead: true);

    private static ViewerSplineSnapshot CreateUnavailable(string message, bool isNotRead) =>
        new(
            TsCurveType.Bezier,
            isTimeValued: false,
            new TsExtrapolation(TsExtrapMode.Held, 0),
            new TsExtrapolation(TsExtrapMode.Held, 0),
            knotCount: 0,
            [],
            [],
            message)
        {
            IsNotRead = isNotRead
        };

    /// <summary>
    /// Returns the times the Viewer evaluates for the preview, spanning the
    /// authored knots plus a margin into both extrapolation regions.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="Create"/> so the sampling rule is testable
    /// without a native spline, and so the caller performs every evaluation
    /// itself while it holds stage access.
    ///
    /// Authored knot times are finite but need not be small: knots at opposite
    /// ends of the double range overflow both the span and the margin to
    /// infinity, and the native evaluator rejects a non-finite time. Every
    /// computed time is therefore checked, and a span that cannot be sampled
    /// finitely yields no preview rather than a throwing or fabricated one.
    /// </remarks>
    internal static double[] GetSampleTimes(TsSplineData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Knots.Count == 0)
        {
            return [];
        }

        double first = data.Knots[0].Time;
        double last = data.Knots[^1].Time;
        if (!double.IsFinite(first) || !double.IsFinite(last) || last < first)
        {
            return [];
        }

        double span = last - first;
        double margin = span > 0 ? span * ExtrapolationMarginFraction : 1;
        double start = first - margin;
        double end = last + margin;
        double extent = end - start;
        if (!double.IsFinite(span) || !double.IsFinite(margin) ||
            !double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(extent))
        {
            return [];
        }

        var times = new double[SampleCount];
        for (int index = 0; index < SampleCount; index++)
        {
            double fraction = (double)index / (SampleCount - 1);
            double time = start + (extent * fraction);
            if (!double.IsFinite(time))
            {
                return [];
            }
            times[index] = time;
        }
        times[^1] = end;
        return times;
    }

    /// <summary>Projects authored spline data and its evaluated preview.</summary>
    internal static ViewerSplineSnapshot Create(
        TsSplineData data,
        IReadOnlyList<ViewerSplineSampleSnapshot> samples)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(samples);
        int retained = Math.Min(data.Knots.Count, MaxKnots);
        var knots = new TsKnot[retained];
        for (int index = 0; index < retained; index++)
        {
            knots[index] = data.Knots[index];
        }
        return new ViewerSplineSnapshot(
            data.CurveType,
            data.IsTimeValued,
            data.PreExtrapolation,
            data.PostExtrapolation,
            data.Knots.Count,
            knots,
            [.. samples]);
    }

    public bool Equals(ViewerSplineSnapshot? other) =>
        other is not null &&
        CurveType == other.CurveType &&
        IsTimeValued == other.IsTimeValued &&
        PreExtrapolation == other.PreExtrapolation &&
        PostExtrapolation == other.PostExtrapolation &&
        KnotCount == other.KnotCount &&
        IsNotRead == other.IsNotRead &&
        string.Equals(Error, other.Error, StringComparison.Ordinal) &&
        Knots.SequenceEqual(other.Knots) &&
        Samples.SequenceEqual(other.Samples);

    public override bool Equals(object? obj) =>
        obj is ViewerSplineSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(CurveType);
        hash.Add(IsTimeValued);
        hash.Add(PreExtrapolation);
        hash.Add(PostExtrapolation);
        hash.Add(KnotCount);
        hash.Add(IsNotRead);
        hash.Add(Error, StringComparer.Ordinal);
        foreach (TsKnot knot in Knots)
        {
            hash.Add(knot);
        }
        foreach (ViewerSplineSampleSnapshot sample in Samples)
        {
            hash.Add(sample);
        }
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{nameof(ViewerSplineSnapshot)} {{ {nameof(CurveType)} = {CurveType}, " +
        $"{nameof(IsTimeValued)} = {IsTimeValued}, " +
        $"{nameof(PreExtrapolation)} = {PreExtrapolation}, " +
        $"{nameof(PostExtrapolation)} = {PostExtrapolation}, " +
        $"{nameof(KnotCount)} = {KnotCount}, " +
        $"{nameof(Error)} = {Error ?? "<none>"}, " +
        $"{nameof(IsNotRead)} = {IsNotRead}, " +
        $"{nameof(Knots)} = [{string.Join<TsKnot>(", ", Knots)}], " +
        $"{nameof(Samples)} = [{string.Join<ViewerSplineSampleSnapshot>(", ", Samples)}] }}";
}

/// <summary>One Value-tab spline block and the row budget it consumed.</summary>
/// <param name="Text">The whole block, already laid out as one text run.</param>
/// <param name="KnotsShown">The number of knot lines the block contains.</param>
internal readonly record struct ViewerSplineBlock(string Text, int KnotsShown);

/// <summary>Formats <see cref="ViewerSplineSnapshot"/> rows for the Value tab.</summary>
internal static class ViewerSplineFormatter
{
    internal static string FormatSummary(ViewerSplineSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Error is string error)
        {
            return snapshot.IsNotRead
                ? $"not read ({error})"
                : $"unreadable ({error})";
        }
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{snapshot.KnotCount} knot(s)");
        if (snapshot.IsTruncated)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $" (showing first {snapshot.Knots.Length})");
        }
        builder.Append(CultureInfo.InvariantCulture, $"; curve={snapshot.CurveType}");
        builder.Append(
            CultureInfo.InvariantCulture,
            $"; pre={FormatExtrapolation(snapshot.PreExtrapolation)}");
        builder.Append(
            CultureInfo.InvariantCulture,
            $"; post={FormatExtrapolation(snapshot.PostExtrapolation)}");
        if (snapshot.IsTimeValued)
        {
            builder.Append("; time-valued");
        }
        return builder.ToString();
    }

    internal static string FormatExtrapolation(TsExtrapolation extrapolation) =>
        extrapolation.Mode == TsExtrapMode.Sloped
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{extrapolation.Mode}({FormatNumber(extrapolation.Slope)})")
            : extrapolation.Mode.ToString();

    internal static string FormatKnot(TsKnot knot, int index)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{index + 1}. t={FormatNumber(knot.Time)}");
        builder.Append(CultureInfo.InvariantCulture, $"; value={FormatNumber(knot.Value)}");
        if (knot.PreValue.HasValue)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $"; preValue={FormatNumber(knot.PreValue.Value)}");
        }
        builder.Append(CultureInfo.InvariantCulture, $"; next={knot.NextInterpolation}");
        string preTangent = FormatTangent(
            knot.PreTangentWidth,
            knot.PreTangentSlope,
            knot.PreTangentAlgorithm);
        string postTangent = FormatTangent(
            knot.PostTangentWidth,
            knot.PostTangentSlope,
            knot.PostTangentAlgorithm);
        builder.Append(CultureInfo.InvariantCulture, $"; preTangent={preTangent}");
        builder.Append(CultureInfo.InvariantCulture, $"; postTangent={postTangent}");
        return builder.ToString();
    }

    internal static string FormatSamples(ViewerSplineSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Samples.Length == 0)
        {
            return "<none>";
        }
        var parts = new string[snapshot.Samples.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            ViewerSplineSampleSnapshot sample = snapshot.Samples[index];
            parts[index] = string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatNumber(sample.Time)}={FormatSampleValue(sample.Value)}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Lays one spline out as a single text run, spending at most
    /// <paramref name="knotBudget"/> knot lines.
    /// </summary>
    /// <remarks>
    /// The per-attribute knot cap alone does not bound the Value tab: a prim
    /// with hundreds of splined attributes still produced hundreds of controls
    /// per rebuild. The caller therefore carries one budget across the whole
    /// inspector and every spline becomes one control, so the visual tree cost
    /// of a prim is bounded by its attribute count rather than by its total
    /// authored knot count. A block that cannot show every retained knot says
    /// how many it omitted instead of ending silently.
    /// </remarks>
    internal static ViewerSplineBlock FormatBlock(ViewerSplineSnapshot snapshot, int knotBudget)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(knotBudget);
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"Spline: {FormatSummary(snapshot)}");
        int shown = Math.Min(snapshot.Knots.Length, knotBudget);
        for (int index = 0; index < shown; index++)
        {
            builder.AppendLine();
            builder.Append("   ");
            builder.Append(FormatKnot(snapshot.Knots[index], index));
        }
        int omitted = snapshot.KnotCount - shown;
        if (omitted > 0)
        {
            builder.AppendLine();
            builder.Append(
                CultureInfo.InvariantCulture,
                $"   ... {omitted} more knot(s) not shown");
        }
        if (snapshot.Samples.Length > 0)
        {
            builder.AppendLine();
            builder.Append(
                CultureInfo.InvariantCulture,
                $"   Evaluated: {FormatSamples(snapshot)}");
        }
        return new ViewerSplineBlock(builder.ToString(), shown);
    }

    private static string FormatSampleValue(double? value) =>
        value.HasValue ? FormatNumber(value.Value) : "<value block>";

    private static string FormatTangent(
        double width,
        double slope,
        TsTangentAlgorithm algorithm) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"width={FormatNumber(width)},slope={FormatNumber(slope)},algorithm={algorithm}");

    private static string FormatNumber(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);
}
