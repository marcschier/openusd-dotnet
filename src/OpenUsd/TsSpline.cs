// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Ts spline segment interpolation modes.</summary>
public enum TsInterpMode
{
    /// <summary>No value in the segment.</summary>
    ValueBlock = 0,
    /// <summary>Held value.</summary>
    Held = 1,
    /// <summary>Linear interpolation.</summary>
    Linear = 2,
    /// <summary>Curved interpolation.</summary>
    Curve = 3
}

/// <summary>Ts curve families.</summary>
public enum TsCurveType
{
    /// <summary>Bezier curve.</summary>
    Bezier = 0,
    /// <summary>Hermite curve.</summary>
    Hermite = 1
}

/// <summary>Ts spline extrapolation modes.</summary>
public enum TsExtrapMode
{
    /// <summary>No value.</summary>
    ValueBlock = 0,
    /// <summary>Held value.</summary>
    Held = 1,
    /// <summary>Linear extrapolation.</summary>
    Linear = 2,
    /// <summary>Sloped extrapolation.</summary>
    Sloped = 3,
    /// <summary>Repeating loop.</summary>
    LoopRepeat = 4,
    /// <summary>Resetting loop.</summary>
    LoopReset = 5,
    /// <summary>Oscillating loop.</summary>
    LoopOscillate = 6
}

/// <summary>Ts tangent calculation algorithms.</summary>
public enum TsTangentAlgorithm
{
    /// <summary>Use authored tangents.</summary>
    None = 0,
    /// <summary>Custom algorithm marker.</summary>
    Custom = 1,
    /// <summary>Automatic ease tangents.</summary>
    AutoEase = 2
}

/// <summary>Describes one Ts extrapolation region.</summary>
/// <param name="Mode">The extrapolation mode.</param>
/// <param name="Slope">The slope used by sloped extrapolation.</param>
public readonly record struct TsExtrapolation(TsExtrapMode Mode, double Slope);

/// <summary>Describes one double-valued Ts knot.</summary>
public readonly record struct TsKnot(
    double Time,
    double Value,
    double? PreValue,
    double PreTangentWidth,
    double PreTangentSlope,
    double PostTangentWidth,
    double PostTangentSlope,
    TsInterpMode NextInterpolation,
    TsTangentAlgorithm PreTangentAlgorithm,
    TsTangentAlgorithm PostTangentAlgorithm);

/// <summary>A detached snapshot of one double-valued Ts spline.</summary>
/// <param name="CurveType">The curve family used by curved segments.</param>
/// <param name="IsTimeValued">Whether the spline values are themselves time codes.</param>
/// <param name="PreExtrapolation">The extrapolation applied before the first knot.</param>
/// <param name="PostExtrapolation">The extrapolation applied after the last knot.</param>
/// <param name="Knots">The authored knots, ordered by time.</param>
/// <remarks>
/// <see cref="TsSpline"/> owns a native handle, so it can never leave a stage
/// scheduler callback. This record carries the same authored data with no
/// native ownership, which is what a UI needs in order to poll and diff.
/// </remarks>
public sealed record TsSplineData(
    TsCurveType CurveType,
    bool IsTimeValued,
    TsExtrapolation PreExtrapolation,
    TsExtrapolation PostExtrapolation,
    IReadOnlyList<TsKnot> Knots) : IUsdDetachedResult
{
    /// <inheritdoc />
    public bool Equals(TsSplineData? other) =>
        other is not null &&
        CurveType == other.CurveType &&
        IsTimeValued == other.IsTimeValued &&
        PreExtrapolation == other.PreExtrapolation &&
        PostExtrapolation == other.PostExtrapolation &&
        Knots.SequenceEqual(other.Knots);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            CurveType,
            IsTimeValued,
            PreExtrapolation,
            PostExtrapolation,
            RecordCollectionFormatting.SequenceHashCode(Knots));

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(TsSplineData)} {{ {nameof(CurveType)} = {CurveType}, " +
        $"{nameof(IsTimeValued)} = {IsTimeValued}, " +
        $"{nameof(PreExtrapolation)} = {PreExtrapolation}, " +
        $"{nameof(PostExtrapolation)} = {PostExtrapolation}, " +
        $"{nameof(Knots)} = {RecordCollectionFormatting.FormatSequence(Knots)} }}";
}

/// <summary>Owns a double-valued OpenUSD Ts spline and evaluates it.</summary>
public sealed class TsSpline : IDisposable
{
    private sealed class TsSplineHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public TsSplineHandle()
            : base(true)
        {
            SetHandle(OpenUsdNativeRuntime.CreateTsSpline());
        }

        public TsSplineHandle(nint handle)
            : base(true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
        {
            OpenUsdNativeRuntime.ReleaseTsSpline(handle);
            return true;
        }
    }

    private readonly TsSplineHandle _handle;

    /// <summary>Creates an empty double-valued Ts spline.</summary>
    public TsSpline()
    {
        _handle = new TsSplineHandle();
    }

    private TsSpline(nint handle)
    {
        _handle = new TsSplineHandle(handle);
    }

    internal nint DangerousGetHandle() => _handle.DangerousGetHandle();

    internal static TsSpline FromNativeHandle(nint handle) => new(handle);

    /// <summary>Replaces the spline contents with a bulk knot snapshot.</summary>
    public void SetData(
        IReadOnlyList<TsKnot> knots,
        TsCurveType curveType = TsCurveType.Bezier,
        TsExtrapolation? preExtrapolation = null,
        TsExtrapolation? postExtrapolation = null,
        bool isTimeValued = false)
    {
        ArgumentNullException.ThrowIfNull(knots);
        var nativeKnots = new OpenUsdNativeTsKnotRecord[knots.Count];
        for (int i = 0; i < nativeKnots.Length; i++)
        {
            TsKnot knot = knots[i];
            nativeKnots[i] = new OpenUsdNativeTsKnotRecord
            {
                Time = knot.Time,
                Value = knot.Value,
                PreValue = knot.PreValue.GetValueOrDefault(),
                PreTangentWidth = knot.PreTangentWidth,
                PreTangentSlope = knot.PreTangentSlope,
                PostTangentWidth = knot.PostTangentWidth,
                PostTangentSlope = knot.PostTangentSlope,
                NextInterpolation = (int)knot.NextInterpolation,
                PreTangentAlgorithm = (int)knot.PreTangentAlgorithm,
                PostTangentAlgorithm = (int)knot.PostTangentAlgorithm,
                Flags = knot.PreValue.HasValue ? 1U : 0U
            };
        }
        var pre = preExtrapolation ?? new TsExtrapolation(TsExtrapMode.Held, 0);
        var post = postExtrapolation ?? new TsExtrapolation(TsExtrapMode.Held, 0);
        OpenUsdNativeRuntime.SetTsSplineData(
            _handle.DangerousGetHandle(),
            new OpenUsdNativeTsSplineData(
                (int)curveType,
                isTimeValued,
                new OpenUsdNativeTsExtrapolation((int)pre.Mode, pre.Slope),
                new OpenUsdNativeTsExtrapolation((int)post.Mode, post.Slope),
                nativeKnots));
    }

    /// <summary>Replaces the spline contents with a detached snapshot.</summary>
    /// <remarks>
    /// This is the inverse of <see cref="GetData"/>: a snapshot read from one
    /// spline can be authored onto another without the caller having to
    /// re-supply the curve family, both extrapolations, and the time-valued
    /// flag as separate arguments and drop one of them by accident.
    /// </remarks>
    public void SetData(TsSplineData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        SetData(
            data.Knots,
            data.CurveType,
            data.PreExtrapolation,
            data.PostExtrapolation,
            data.IsTimeValued);
    }

    /// <summary>Returns all authored knots as a detached snapshot.</summary>
    public IReadOnlyList<TsKnot> GetKnots() => GetData().Knots;

    /// <summary>
    /// Returns the curve type, extrapolation, and knots as one detached snapshot.
    /// </summary>
    public TsSplineData GetData()
    {
        OpenUsdNativeTsSplineData data = OpenUsdNativeRuntime.GetTsSplineData(_handle.DangerousGetHandle());
        var knots = new TsKnot[data.Knots.Length];
        for (int i = 0; i < knots.Length; i++)
        {
            OpenUsdNativeTsKnotRecord knot = data.Knots[i];
            knots[i] = new TsKnot(
                knot.Time,
                knot.Value,
                (knot.Flags & 1U) != 0 ? knot.PreValue : null,
                knot.PreTangentWidth,
                knot.PreTangentSlope,
                knot.PostTangentWidth,
                knot.PostTangentSlope,
                (TsInterpMode)knot.NextInterpolation,
                (TsTangentAlgorithm)knot.PreTangentAlgorithm,
                (TsTangentAlgorithm)knot.PostTangentAlgorithm);
        }
        return new TsSplineData(
            (TsCurveType)data.CurveType,
            data.IsTimeValued,
            new TsExtrapolation(
                (TsExtrapMode)data.PreExtrapolation.Mode,
                data.PreExtrapolation.Slope),
            new TsExtrapolation(
                (TsExtrapMode)data.PostExtrapolation.Mode,
                data.PostExtrapolation.Slope),
            Array.AsReadOnly(knots));
    }

    /// <summary>Evaluates the spline at the given time, or returns null for a value block.</summary>
    public double? Evaluate(double time) => OpenUsdNativeRuntime.EvalTsSpline(_handle.DangerousGetHandle(), time);

    /// <inheritdoc />
    public void Dispose() => _handle.Dispose();
}
