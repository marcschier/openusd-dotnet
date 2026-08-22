// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Text;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd;

internal readonly record struct StageStatisticsAnalysis(
    string RootLayerIdentifier,
    string SessionLayerIdentifier,
    string DefaultPrimPath,
    int PrimCount,
    int MeshCount,
    long CurveVertexCount,
    long MeshVertexCount,
    long FaceCount,
    int RootPrimCount,
    int LeafPrimCount,
    int MaximumDepth,
    UsdBounds3d WorldBounds,
    UsdOrientedBounds3d OrientedWorldBounds,
    TimeSpan BoundsQueryDuration) : IUsdDetachedResult;

internal readonly record struct StageStatisticsAnalysisLimits
{
    internal StageStatisticsAnalysisLimits(
        int maximumPrimCount,
        long maximumRetainedHierarchyPathBytes,
        long maximumGeometryElementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPrimCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetainedHierarchyPathBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumGeometryElementCount);
        MaximumPrimCount = maximumPrimCount;
        MaximumRetainedHierarchyPathBytes = maximumRetainedHierarchyPathBytes;
        MaximumGeometryElementCount = maximumGeometryElementCount;
    }

    internal static StageStatisticsAnalysisLimits Inspection { get; } = new(
        maximumPrimCount: 100_000,
        maximumRetainedHierarchyPathBytes: 16 * 1024 * 1024,
        maximumGeometryElementCount: 10_000_000);

    internal static StageStatisticsAnalysisLimits Viewer { get; } = new(
        maximumPrimCount: 1_000_000,
        maximumRetainedHierarchyPathBytes: 128 * 1024 * 1024,
        maximumGeometryElementCount: 1_000_000_000);

    internal int MaximumPrimCount { get; }

    internal long MaximumRetainedHierarchyPathBytes { get; }

    internal long MaximumGeometryElementCount { get; }
}

internal enum StageStatisticsLimitKind
{
    PrimCount,
    RetainedHierarchyPathBytes,
    GeometryElements
}

internal sealed class StageStatisticsQuotaExceededException : InvalidOperationException
{
    internal StageStatisticsQuotaExceededException(
        StageStatisticsLimitKind limitKind,
        ulong limit,
        ulong observed)
        : base(
            $"Stage statistics exceeded the {limitKind} limit of {limit}; " +
            $"observed at least {observed}.")
    {
        LimitKind = limitKind;
        Limit = limit;
        Observed = observed;
    }

    internal StageStatisticsLimitKind LimitKind { get; }

    internal ulong Limit { get; }

    internal ulong Observed { get; }
}

internal sealed class StageStatisticsAccumulator
{
    private readonly StageStatisticsAnalysisLimits _limits;
    private readonly Dictionary<string, HierarchyPathState> _hierarchy =
        new(StringComparer.Ordinal);
    private ulong _geometryElementCount;
    private ulong _retainedHierarchyPathBytes;

    internal StageStatisticsAccumulator(StageStatisticsAnalysisLimits limits)
    {
        _limits = limits;
    }

    internal int PrimCount { get; private set; }

    internal int RootPrimCount { get; private set; }

    internal int MaximumDepth { get; private set; }

    internal long GeometryElementCount => checked((long)_geometryElementCount);

    internal ulong RetainedHierarchyPathBytes => _retainedHierarchyPathBytes;

    internal int LeafPrimCount
    {
        get
        {
            int count = 0;
            foreach (HierarchyPathState state in _hierarchy.Values)
            {
                if (state.IsPrim && !state.HasChild)
                {
                    count++;
                }
            }
            return count;
        }
    }

    internal void EnsureTraversalCanBeMaterialized(
        ulong primCount,
        ulong totalPathBytes)
    {
        ThrowIfExceeded(
            StageStatisticsLimitKind.PrimCount,
            (ulong)_limits.MaximumPrimCount,
            primCount);
        ThrowIfExceeded(
            StageStatisticsLimitKind.RetainedHierarchyPathBytes,
            (ulong)_limits.MaximumRetainedHierarchyPathBytes,
            totalPathBytes);
    }

    internal bool AddPrimPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        bool pathKnown = _hierarchy.TryGetValue(path, out HierarchyPathState state);
        if (pathKnown && state.IsPrim)
        {
            return false;
        }

        ulong observedPrimCount = (ulong)PrimCount + 1;
        ThrowIfExceeded(
            StageStatisticsLimitKind.PrimCount,
            (ulong)_limits.MaximumPrimCount,
            observedPrimCount);

        if (pathKnown)
        {
            state.IsPrim = true;
            _hierarchy[path] = state;
        }
        else
        {
            RetainPath(GetUtf8ByteCount(path));
            _hierarchy.Add(path, new HierarchyPathState(isPrim: true, hasChild: false));
        }

        PrimCount++;
        int separator = path.LastIndexOf('/');
        if (separator <= 0)
        {
            RootPrimCount++;
        }
        else
        {
            MarkParentAsHavingChild(path, separator);
        }
        MaximumDepth = Math.Max(MaximumDepth, GetDepth(path));
        return true;
    }

    internal long AddGeometryElements(ulong count)
    {
        ulong observed = AddSaturating(_geometryElementCount, count);
        ThrowIfExceeded(
            StageStatisticsLimitKind.GeometryElements,
            (ulong)_limits.MaximumGeometryElementCount,
            observed);
        _geometryElementCount = observed;
        return checked((long)count);
    }

    private void MarkParentAsHavingChild(string path, int separator)
    {
        string parentPath = path[..separator];
        if (_hierarchy.TryGetValue(parentPath, out HierarchyPathState parentState))
        {
            parentState.HasChild = true;
            _hierarchy[parentPath] = parentState;
            return;
        }

        ulong parentPathBytes = GetUtf8ByteCount(path.AsSpan(0, separator));
        RetainPath(parentPathBytes);
        _hierarchy.Add(
            parentPath,
            new HierarchyPathState(isPrim: false, hasChild: true));
    }

    private void RetainPath(ulong pathBytes)
    {
        ulong observed = AddSaturating(_retainedHierarchyPathBytes, pathBytes);
        ThrowIfExceeded(
            StageStatisticsLimitKind.RetainedHierarchyPathBytes,
            (ulong)_limits.MaximumRetainedHierarchyPathBytes,
            observed);
        _retainedHierarchyPathBytes = observed;
    }

    private static ulong GetUtf8ByteCount(string value) =>
        (ulong)Encoding.UTF8.GetByteCount(value);

    private static ulong GetUtf8ByteCount(ReadOnlySpan<char> value) =>
        (ulong)Encoding.UTF8.GetByteCount(value);

    private static ulong AddSaturating(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private static void ThrowIfExceeded(
        StageStatisticsLimitKind limitKind,
        ulong limit,
        ulong observed)
    {
        if (observed > limit)
        {
            throw new StageStatisticsQuotaExceededException(
                limitKind,
                limit,
                observed);
        }
    }

    private static int GetDepth(string path)
    {
        int depth = 0;
        foreach (char character in path)
        {
            if (character == '/')
            {
                depth++;
            }
        }
        return Math.Max(0, depth - 1);
    }

    private struct HierarchyPathState
    {
        internal HierarchyPathState(bool isPrim, bool hasChild)
        {
            IsPrim = isPrim;
            HasChild = hasChild;
        }

        internal bool IsPrim { get; set; }

        internal bool HasChild { get; set; }
    }
}

internal static class StageStatisticsAnalyzer
{
    internal static StageStatisticsAnalysis Analyze(UsdStage stage) =>
        Analyze(stage, StageStatisticsAnalysisLimits.Inspection, CancellationToken.None);

    internal static StageStatisticsAnalysis Analyze(
        UsdStage stage,
        CancellationToken cancellationToken) =>
        Analyze(stage, StageStatisticsAnalysisLimits.Inspection, cancellationToken);

    internal static StageStatisticsAnalysis Analyze(
        UsdStage stage,
        StageStatisticsAnalysisLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stage);
        cancellationToken.ThrowIfCancellationRequested();

        var accumulator = new StageStatisticsAccumulator(limits);
        (ulong primCount, ulong totalPathBytes) =
            OpenUsdNativeRuntime.GetPrimPathStatistics(
                stage.Native,
                (ulong)limits.MaximumPrimCount,
                (ulong)limits.MaximumRetainedHierarchyPathBytes);
        accumulator.EnsureTraversalCanBeMaterialized(primCount, totalPathBytes);

        cancellationToken.ThrowIfCancellationRequested();
        string[] paths = stage.Native.GetPrimPaths();
        if ((ulong)paths.LongLength != primCount)
        {
            throw new InvalidOperationException(
                "The stage traversal changed while statistics were being prepared.");
        }

        return AnalyzeCore(stage, paths, accumulator, cancellationToken);
    }

    internal static StageStatisticsAnalysis Analyze(
        UsdStage stage,
        IEnumerable<string> traversalPaths) =>
        Analyze(
            stage,
            traversalPaths,
            StageStatisticsAnalysisLimits.Viewer,
            CancellationToken.None);

    internal static StageStatisticsAnalysis Analyze(
        UsdStage stage,
        IEnumerable<string> traversalPaths,
        StageStatisticsAnalysisLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(traversalPaths);
        return AnalyzeCore(
            stage,
            traversalPaths,
            new StageStatisticsAccumulator(limits),
            cancellationToken);
    }

    internal static double RequireFiniteBoundsTimeCode(double timeCode)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new InvalidDataException(
                "The stage start time code must be finite for bounds analysis.");
        }
        return timeCode;
    }

    private static StageStatisticsAnalysis AnalyzeCore(
        UsdStage stage,
        IEnumerable<string> traversalPaths,
        StageStatisticsAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        int meshCount = 0;
        long curveVertexCount = 0;
        long meshVertexCount = 0;
        long faceCount = 0;
        foreach (string path in traversalPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!accumulator.AddPrimPath(path))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            UsdPrim prim = stage.GetPrim(path);
            cancellationToken.ThrowIfCancellationRequested();
            if (UsdGeomMesh.TryWrap(prim, out _))
            {
                meshCount++;
                cancellationToken.ThrowIfCancellationRequested();
                meshVertexCount += accumulator.AddGeometryElements(
                    OpenUsdNativeRuntime.GetGeomMeshPointCount(
                        stage.Native,
                        path,
                        timeCode: null));
                cancellationToken.ThrowIfCancellationRequested();
                faceCount += accumulator.AddGeometryElements(
                    OpenUsdNativeRuntime.GetGeomMeshFaceCount(stage.Native, path));
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            bool isCurve = UsdGeomBasisCurves.TryWrap(prim, out _);
            if (!isCurve)
            {
                cancellationToken.ThrowIfCancellationRequested();
                isCurve = UsdGeomHermiteCurves.TryWrap(prim, out _);
            }
            if (!isCurve)
            {
                cancellationToken.ThrowIfCancellationRequested();
                isCurve = UsdGeomNurbsCurves.TryWrap(prim, out _);
            }
            if (isCurve)
            {
                cancellationToken.ThrowIfCancellationRequested();
                curveVertexCount += accumulator.AddGeometryElements(
                    OpenUsdNativeRuntime.GetVec3fArrayCount(
                        stage.Native,
                        path,
                        "points",
                        timeCode: null));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        double boundsTimeCode = RequireFiniteBoundsTimeCode(stage.StartTimeCode);
        Stopwatch boundsTimer = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        UsdBounds3d worldBounds = stage.GetWorldBounds(
            boundsTimeCode,
            UsdGeomPurposeMask.All);
        cancellationToken.ThrowIfCancellationRequested();
        UsdOrientedBounds3d orientedWorldBounds = stage.GetWorldOrientedBounds(
            boundsTimeCode,
            UsdGeomPurposeMask.All);
        boundsTimer.Stop();

        cancellationToken.ThrowIfCancellationRequested();
        return new StageStatisticsAnalysis(
            stage.RootLayerIdentifier,
            stage.SessionLayerIdentifier,
            GetDefaultPrimPath(stage),
            accumulator.PrimCount,
            meshCount,
            curveVertexCount,
            meshVertexCount,
            faceCount,
            accumulator.RootPrimCount,
            accumulator.LeafPrimCount,
            accumulator.PrimCount == 0 ? 0 : accumulator.MaximumDepth,
            worldBounds,
            orientedWorldBounds,
            boundsTimer.Elapsed);
    }

    private static string GetDefaultPrimPath(UsdStage stage)
    {
        try
        {
            return stage.GetDefaultPrim().Path;
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.NotFound)
        {
            return string.Empty;
        }
    }
}
