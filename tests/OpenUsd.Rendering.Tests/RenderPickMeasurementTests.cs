// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Reflection;

namespace OpenUsd.Rendering.Tests;

public sealed class RenderPickMeasurementTests
{
    private static readonly UsdStageLinearUnits Centimeters =
        new(UsdStageLinearUnits.FallbackMetersPerUnit, false, UsdStageUpAxis.Y, false);

    [Test]
    public async Task TwoHitsAtTheSameRevisionsProduceDoublePrecisionSceneAndMeterDistances()
    {
        var units = new UsdStageLinearUnits(0.01, true, UsdStageUpAxis.Z, true);
        RenderPickResult start = Hit(7, 3, new SelectionItem("/World/A"), new Vector3(1, 2, 3));
        RenderPickResult end = Hit(
            7,
            3,
            new SelectionItem("/World/Prototype", "/World/Instancer", instanceIndex: 4),
            new Vector3(4, 6, 3));

        bool created = RenderPickMeasurement.TryCreate(
            start, end, units, out RenderPickMeasurement? measurement, out RenderPickMeasurementRefusal refusal);

        await Assert.That(created).IsTrue();
        await Assert.That(refusal).IsEqualTo(RenderPickMeasurementRefusal.None);
        await Assert.That(measurement!.Start).IsEqualTo(start.Item!.Value);
        await Assert.That(measurement.End).IsEqualTo(end.Item!.Value);
        await Assert.That(measurement.End.InstancerPath).IsEqualTo("/World/Instancer");
        await Assert.That(measurement.StartWorldPosition).IsEqualTo(new Vector3(1, 2, 3));
        await Assert.That(measurement.EndWorldPosition).IsEqualTo(new Vector3(4, 6, 3));
        await Assert.That(measurement.Delta).IsEqualTo(new UsdVec3d(3, 4, 0));
        await Assert.That(measurement.DistanceInSceneUnits).IsEqualTo(5d);
        await Assert.That(measurement.DistanceInMeters).IsEqualTo(0.05d);
        await Assert.That(measurement.MetersPerUnit).IsEqualTo(0.01);
        await Assert.That(measurement.IsMetersPerUnitAuthored).IsTrue();
        await Assert.That(measurement.Units).IsEqualTo(units);
        await Assert.That(measurement.StateRevision).IsEqualTo(7UL);
        await Assert.That(measurement.SceneRevision).IsEqualTo(3UL);
    }

    [Test]
    public async Task FallbackUnitsAreReportedAsUnauthoredAndUnboundSceneRevisionsMatch()
    {
        RenderPickResult start = Hit(2, null, new SelectionItem("/World/A"), new Vector3(0, 0, 0));
        RenderPickResult end = Hit(2, null, new SelectionItem("/World/A"), new Vector3(0, 1250, 0));

        await Assert.That(RenderPickMeasurement.TryCreate(start, end, Centimeters, out var measurement, out _))
            .IsTrue();
        await Assert.That(measurement!.IsMetersPerUnitAuthored).IsFalse();
        await Assert.That(measurement.SceneRevision).IsNull();
        await Assert.That(measurement.DistanceInSceneUnits).IsEqualTo(1250d);
        await Assert.That(measurement.DistanceInMeters).IsEqualTo(12.5d);
    }

    [Test]
    public async Task MetersAreComputedInDoublePrecisionFromSinglePrecisionPositions()
    {
        var start = new Vector3(16_777_216f, 0, 0);
        var end = new Vector3(16_777_218f, 0, 0);
        var kilometers = new UsdStageLinearUnits(1000, true, UsdStageUpAxis.Y, true);

        await Assert.That(RenderPickMeasurement.TryCreate(
                Hit(1, 1, new SelectionItem("/A"), start),
                Hit(1, 1, new SelectionItem("/B"), end),
                kilometers,
                out var measurement,
                out _))
            .IsTrue();
        await Assert.That(measurement!.DistanceInSceneUnits).IsEqualTo(2d);
        await Assert.That(measurement.DistanceInMeters).IsEqualTo(2000d);
    }

    [Test]
    public async Task MissesStaleAndUnsupportedResultsAreRefused()
    {
        RenderPickRequest request = Request(5, 1);
        RenderPickResult hit = Hit(5, 1, new SelectionItem("/A"), Vector3.Zero);
        RenderPickResult miss = RenderPickResult.Miss(request, 5, 1);
        RenderPickResult stale = RenderPickResult.Stale(request, 6, 1);
        RenderPickResult unsupported = RenderPickResult.Unsupported(request, 5, 1);

        await AssertRefused(miss, hit, Centimeters, RenderPickMeasurementRefusal.StartNotHit);
        await AssertRefused(hit, miss, Centimeters, RenderPickMeasurementRefusal.EndNotHit);
        await AssertRefused(hit, stale, Centimeters, RenderPickMeasurementRefusal.EndNotHit);
        await AssertRefused(unsupported, hit, Centimeters, RenderPickMeasurementRefusal.StartNotHit);
        await AssertRefused(default, hit, Centimeters, RenderPickMeasurementRefusal.StartNotHit);
    }

    [Test]
    public async Task HitsWithoutWorldPositionsAreRefused()
    {
        RenderPickResult positioned = Hit(1, 1, new SelectionItem("/A"), Vector3.One);
        RenderPickResult unpositioned = RenderPickResult.Hit(Request(1, 1), 1, 1, new SelectionItem("/B"));

        await AssertRefused(
            unpositioned, positioned, Centimeters, RenderPickMeasurementRefusal.StartWithoutWorldPosition);
        await AssertRefused(
            positioned, unpositioned, Centimeters, RenderPickMeasurementRefusal.EndWithoutWorldPosition);
    }

    [Test]
    public async Task RevisionMismatchesAreRefusedBecauseTheyAreNotAuthoritative()
    {
        RenderPickResult start = Hit(1, 1, new SelectionItem("/A"), Vector3.Zero);

        await AssertRefused(
            start,
            Hit(2, 1, new SelectionItem("/B"), Vector3.One),
            Centimeters,
            RenderPickMeasurementRefusal.StateRevisionMismatch);
        await AssertRefused(
            start,
            Hit(1, 2, new SelectionItem("/B"), Vector3.One),
            Centimeters,
            RenderPickMeasurementRefusal.SceneRevisionMismatch);
        await AssertRefused(
            start,
            Hit(1, null, new SelectionItem("/B"), Vector3.One),
            Centimeters,
            RenderPickMeasurementRefusal.SceneRevisionMismatch);
    }

    [Test]
    [Arguments(float.NaN)]
    [Arguments(float.PositiveInfinity)]
    [Arguments(float.NegativeInfinity)]
    public async Task NonFinitePositionsAreRefused(float component)
    {
        RenderPickResult finite = Hit(1, 1, new SelectionItem("/A"), Vector3.Zero);
        RenderPickResult nonFinite = UncheckedHit(new Vector3(0, component, 0));

        await AssertRefused(finite, nonFinite, Centimeters, RenderPickMeasurementRefusal.NonFinitePosition);
        await AssertRefused(nonFinite, finite, Centimeters, RenderPickMeasurementRefusal.NonFinitePosition);
    }

    [Test]
    [Arguments(0d)]
    [Arguments(-1d)]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    public async Task InvalidMetersPerUnitIsRefused(double metersPerUnit)
    {
        RenderPickResult start = Hit(1, 1, new SelectionItem("/A"), Vector3.Zero);
        RenderPickResult end = Hit(1, 1, new SelectionItem("/B"), Vector3.One);

        await AssertRefused(
            start,
            end,
            new UsdStageLinearUnits(metersPerUnit, true, UsdStageUpAxis.Y, false),
            RenderPickMeasurementRefusal.InvalidMetersPerUnit);
    }

    private static async Task AssertRefused(
        RenderPickResult start,
        RenderPickResult end,
        UsdStageLinearUnits units,
        RenderPickMeasurementRefusal expected)
    {
        bool created = RenderPickMeasurement.TryCreate(
            start, end, units, out RenderPickMeasurement? measurement, out RenderPickMeasurementRefusal refusal);
        await Assert.That(created).IsFalse();
        await Assert.That(measurement).IsNull();
        await Assert.That(refusal).IsEqualTo(expected);
    }

    private static RenderPickRequest Request(ulong stateRevision, ulong? sceneRevision) =>
        new(1, 1, new ViewportDimensions(8, 8), stateRevision, sceneRevision, RenderPickTarget.Primitive);

    private static RenderPickResult Hit(
        ulong stateRevision,
        ulong? sceneRevision,
        SelectionItem item,
        Vector3 worldPosition) =>
        RenderPickResult.Hit(Request(stateRevision, sceneRevision), stateRevision, sceneRevision, item, worldPosition);

    private static RenderPickResult UncheckedHit(Vector3 worldPosition)
    {
        // The public factory already refuses non-finite positions; bypass it to prove the measurement
        // does not trust that every RenderPickResult came through the validated factory.
        ConstructorInfo constructor = typeof(RenderPickResult).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 11);
        return (RenderPickResult)constructor.Invoke(
        [
            RenderPickStatus.Hit,
            Request(1, 1),
            1UL,
            (ulong?)1UL,
            RenderPickStaleReason.None,
            (SelectionItem?)new SelectionItem("/B"),
            (Vector3?)worldPosition,
            null,
            null,
            null,
            null,
        ]);
    }
}
