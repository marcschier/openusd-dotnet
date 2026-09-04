// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class StageStatisticsAnalyzerTests
{
    [Test]
    public async Task AnalysisIsTrustedDetachedResultWithNoStageBoundMembers()
    {
        UsdStageBoundResultGuard.ThrowIfForbiddenType(
            typeof(StageStatisticsAnalysis));

        await Assert.That(
                typeof(IUsdDetachedResult).IsAssignableFrom(
                    typeof(StageStatisticsAnalysis)))
            .IsTrue();
        await Assert.That(typeof(StageStatisticsAnalysis).GetProperties()
                .Any(static property =>
                    typeof(IUsdStageBound).IsAssignableFrom(property.PropertyType)))
            .IsFalse();
    }

    [Test]
    public async Task LimitsValidateConfigurationAndPublishBoundedProfiles()
    {
        await Assert.That(() => new StageStatisticsAnalysisLimits(-1, 0, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new StageStatisticsAnalysisLimits(0, -1, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new StageStatisticsAnalysisLimits(0, 0, -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(StageStatisticsAnalysisLimits.Inspection.MaximumPrimCount)
            .IsEqualTo(100_000);
        await Assert.That(
                StageStatisticsAnalysisLimits.Inspection
                    .MaximumRetainedHierarchyPathBytes)
            .IsEqualTo(16L * 1024 * 1024);
        await Assert.That(
                StageStatisticsAnalysisLimits.Inspection.MaximumGeometryElementCount)
            .IsEqualTo(10_000_000L);
        await Assert.That(
                StageStatisticsAnalysisLimits.Viewer.MaximumGeometryElementCount)
            .IsGreaterThan(
                StageStatisticsAnalysisLimits.Inspection.MaximumGeometryElementCount);
    }

    [Test]
    public async Task AccumulatorAccountsDistinctHierarchyAndGeometry()
    {
        var accumulator = new StageStatisticsAccumulator(
            new StageStatisticsAnalysisLimits(2, 18, 10));

        bool childAdded = accumulator.AddPrimPath("/World/Child");
        bool rootAdded = accumulator.AddPrimPath("/World");
        bool duplicateAdded = accumulator.AddPrimPath("/World/Child");
        long firstGeometryCount = accumulator.AddGeometryElements(4);
        long secondGeometryCount = accumulator.AddGeometryElements(6);

        await Assert.That(childAdded).IsTrue();
        await Assert.That(rootAdded).IsTrue();
        await Assert.That(duplicateAdded).IsFalse();
        await Assert.That(accumulator.PrimCount).IsEqualTo(2);
        await Assert.That(accumulator.RootPrimCount).IsEqualTo(1);
        await Assert.That(accumulator.LeafPrimCount).IsEqualTo(1);
        await Assert.That(accumulator.MaximumDepth).IsEqualTo(1);
        await Assert.That(accumulator.RetainedHierarchyPathBytes).IsEqualTo(18UL);
        await Assert.That(firstGeometryCount).IsEqualTo(4L);
        await Assert.That(secondGeometryCount).IsEqualTo(6L);
        await Assert.That(accumulator.GeometryElementCount).IsEqualTo(10L);
    }

    [Test]
    public async Task QuotaFailuresReportDeterministicDetachedScalars()
    {
        var primAccumulator = new StageStatisticsAccumulator(
            new StageStatisticsAnalysisLimits(1, 100, 100));
        primAccumulator.AddPrimPath("/First");
        StageStatisticsQuotaExceededException primException = CaptureQuota(
            () => primAccumulator.AddPrimPath("/Second"));

        var hierarchyAccumulator = new StageStatisticsAccumulator(
            new StageStatisticsAnalysisLimits(2, 17, 100));
        StageStatisticsQuotaExceededException hierarchyException = CaptureQuota(
            () => hierarchyAccumulator.AddPrimPath("/World/Child"));

        var geometryAccumulator = new StageStatisticsAccumulator(
            new StageStatisticsAnalysisLimits(1, 100, 3));
        geometryAccumulator.AddGeometryElements(2);
        StageStatisticsQuotaExceededException geometryException = CaptureQuota(
            () => geometryAccumulator.AddGeometryElements(2));

        await AssertQuotaAsync(
            primException,
            StageStatisticsLimitKind.PrimCount,
            limit: 1,
            observed: 2);
        await AssertQuotaAsync(
            hierarchyException,
            StageStatisticsLimitKind.RetainedHierarchyPathBytes,
            limit: 17,
            observed: 18);
        await AssertQuotaAsync(
            geometryException,
            StageStatisticsLimitKind.GeometryElements,
            limit: 3,
            observed: 4);
    }

    [Test]
    public async Task BoundsAnalysisRejectsNonFiniteStageTime()
    {
        double[] values =
        [
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity
        ];

        foreach (double value in values)
        {
            await Assert.That(
                    () => StageStatisticsAnalyzer.RequireFiniteBoundsTimeCode(value))
                .Throws<InvalidDataException>();
        }
    }

    [Test]
    public async Task AnalyzerReportsTraversalGeometryAndBounds()
    {
        using UsdStage stage = OpenStageOrSkip("native-usdgeom.usda");

        (ulong primCount, ulong totalPathBytes) =
            OpenUsdNativeRuntime.GetPrimPathStatistics(
                stage.Native,
                (ulong)StageStatisticsAnalysisLimits.Inspection.MaximumPrimCount,
                (ulong)StageStatisticsAnalysisLimits.Inspection
                    .MaximumRetainedHierarchyPathBytes);
        StageStatisticsAnalysis analysis = StageStatisticsAnalyzer.Analyze(stage);

        await Assert.That(primCount).IsEqualTo(3UL);
        await Assert.That(totalPathBytes).IsEqualTo(30UL);
        await Assert.That(analysis.PrimCount).IsEqualTo(3);
        await Assert.That(analysis.MeshCount).IsEqualTo(1);
        await Assert.That(analysis.MeshVertexCount).IsEqualTo(4);
        await Assert.That(analysis.FaceCount).IsEqualTo(1);
        await Assert.That(analysis.CurveVertexCount).IsEqualTo(0);
        await Assert.That(analysis.RootPrimCount).IsEqualTo(1);
        await Assert.That(analysis.LeafPrimCount).IsEqualTo(2);
        await Assert.That(analysis.MaximumDepth).IsEqualTo(1);
        await Assert.That(analysis.DefaultPrimPath).IsEqualTo(string.Empty);
        await Assert.That(analysis.WorldBounds.IsEmpty).IsFalse();
        await Assert.That(analysis.OrientedWorldBounds.IsEmpty).IsFalse();
        await Assert.That(analysis.BoundsQueryDuration >= TimeSpan.Zero).IsTrue();
    }

    [Test]
    public async Task ExplicitTraversalPreservesViewerHierarchySemantics()
    {
        using UsdStage stage = OpenStageOrSkip("native-usdgeom.usda");

        StageStatisticsAnalysis analysis = StageStatisticsAnalyzer.Analyze(
            stage,
            ["/World", "/World/Mesh"]);

        await Assert.That(analysis.PrimCount).IsEqualTo(2);
        await Assert.That(analysis.MeshCount).IsEqualTo(1);
        await Assert.That(analysis.RootPrimCount).IsEqualTo(1);
        await Assert.That(analysis.LeafPrimCount).IsEqualTo(1);
        await Assert.That(analysis.MaximumDepth).IsEqualTo(1);
    }

    [Test]
    public async Task PreCanceledAnalysisStopsBeforeTraversal()
    {
        using UsdStage stage = OpenStageOrSkip("native-usdgeom.usda");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(
                () => StageStatisticsAnalyzer.Analyze(stage, cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task CancellationDuringTraversalStopsAnalysis()
    {
        using UsdStage stage = OpenStageOrSkip("native-usdgeom.usda");
        using var cancellation = new CancellationTokenSource();
        bool traversalContinued = false;

        IEnumerable<string> traversalPaths = cancelAfterFirstPath();

        await Assert.That(
                () => StageStatisticsAnalyzer.Analyze(
                    stage,
                    traversalPaths,
                    StageStatisticsAnalysisLimits.Inspection,
                    cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(traversalContinued).IsTrue();

        IEnumerable<string> cancelAfterFirstPath()
        {
            yield return "/World";
            traversalContinued = true;
            cancellation.Cancel();
            yield return "/World/Mesh";
        }
    }

    private static async Task AssertQuotaAsync(
        StageStatisticsQuotaExceededException exception,
        StageStatisticsLimitKind expectedKind,
        ulong limit,
        ulong observed)
    {
        await Assert.That(exception.LimitKind).IsEqualTo(expectedKind);
        await Assert.That(exception.Limit).IsEqualTo(limit);
        await Assert.That(exception.Observed).IsEqualTo(observed);
        await Assert.That(exception.GetType().GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(static property =>
                    typeof(IUsdStageBound).IsAssignableFrom(property.PropertyType)))
            .IsFalse();
    }

    private static StageStatisticsQuotaExceededException CaptureQuota(Action action)
    {
        try
        {
            action();
        }
        catch (StageStatisticsQuotaExceededException exception)
        {
            return exception;
        }
        throw new InvalidOperationException("Expected a stage-statistics quota failure.");
    }

    private static UsdStage OpenStageOrSkip(string fileName)
    {
        try
        {
            return UsdStage.Open(Path.Combine(FindRepositoryRoot(), "test-assets", fileName));
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    [Test]
    public async Task TraversalPreflightAcceptsExactLimitsWithoutRetainingState()
    {
        var accumulator = new StageStatisticsAccumulator(
            new StageStatisticsAnalysisLimits(2, 16, 4));

        await Assert.That(
                () => accumulator.EnsureTraversalCanBeMaterialized(
                    primCount: 2,
                    totalPathBytes: 16))
            .ThrowsNothing();
        await Assert.That(accumulator.PrimCount).IsEqualTo(0);
        await Assert.That(accumulator.RetainedHierarchyPathBytes).IsEqualTo(0UL);
        await Assert.That(accumulator.GeometryElementCount).IsEqualTo(0L);
    }

    [Test]
    public async Task TraversalPreflightReportsPrimCountQuota()
    {
        var accumulator = new StageStatisticsAccumulator(
            new StageStatisticsAnalysisLimits(2, 16, 4));

        StageStatisticsQuotaExceededException exception = CaptureQuota(
            () => accumulator.EnsureTraversalCanBeMaterialized(
                primCount: 3,
                totalPathBytes: 16));

        await AssertQuotaAsync(
            exception,
            StageStatisticsLimitKind.PrimCount,
            limit: 2,
            observed: 3);
    }

    [Test]
    public async Task TraversalPreflightReportsRetainedPathByteQuota()
    {
        var accumulator = new StageStatisticsAccumulator(
            new StageStatisticsAnalysisLimits(2, 16, 4));

        StageStatisticsQuotaExceededException exception = CaptureQuota(
            () => accumulator.EnsureTraversalCanBeMaterialized(
                primCount: 2,
                totalPathBytes: 17));

        await AssertQuotaAsync(
            exception,
            StageStatisticsLimitKind.RetainedHierarchyPathBytes,
            limit: 16,
            observed: 17);
    }
}
