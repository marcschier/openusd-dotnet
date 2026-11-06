// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerMeasureSessionTests
{
    private static readonly UsdStageLinearUnits FallbackUnits =
        new(UsdStageLinearUnits.FallbackMetersPerUnit, false, UsdStageUpAxis.Y, false);

    private static readonly UsdStageLinearUnits AuthoredCentimeters =
        new(0.01, true, UsdStageUpAxis.Z, true);

    [Test]
    public async Task TwoHitsAtOneRevisionProduceAnAuthoredMeterReadout()
    {
        var session = new ViewerMeasureSession();
        await Assert.That(session.Activate()).Contains("pick the start point");

        RenderPickResult start = Hit(4, 2, "/World/A", Vector3.Zero);
        await Assert.That(session.NeedsUnits(start)).IsFalse();
        string startStatus = session.Accept(start, units: null);
        await Assert.That(startStatus).IsEqualTo("Measure start set at /World/A; pick the end point.");
        await Assert.That(session.HasStart).IsTrue();

        RenderPickResult end = Hit(4, 2, "/World/B", new Vector3(0, 1250, 0));
        await Assert.That(session.NeedsUnits(end)).IsTrue();
        string status = session.Accept(end, AuthoredCentimeters);

        await Assert.That(status).IsEqualTo(
            "Measured 12.500 m (1250 scene units, metersPerUnit 0.01 authored) from /World/A to /World/B.");
        await Assert.That(session.Measurement).IsNotNull();
        await Assert.That(session.Measurement!.DistanceInMeters).IsEqualTo(12.5d);
        await Assert.That(session.HasStart).IsFalse();
        await Assert.That(session.IsActive).IsTrue();
    }

    [Test]
    public async Task FallbackUnitsAreLabelledNotAuthored()
    {
        var session = new ViewerMeasureSession();
        session.Activate();
        session.Accept(Hit(1, null, "/A", Vector3.Zero), units: null);

        string status = session.Accept(Hit(1, null, "/B", new Vector3(3, 4, 0)), FallbackUnits);

        await Assert.That(status).Contains("0.050 m (5 scene units, metersPerUnit not authored, fallback 0.01)");
    }

    [Test]
    public async Task InstancedEndpointsNameTheirInstancerAndIndex()
    {
        var session = new ViewerMeasureSession();
        session.Activate();
        string status = session.Accept(
            RenderPickResult.Hit(
                Request(1, 1),
                1,
                1,
                new SelectionItem("/World/Proto", "/World/Instancer", instanceIndex: 3),
                Vector3.One),
            units: null);

        await Assert.That(status).Contains("/World/Proto (instance 3 of /World/Instancer)");
    }

    [Test]
    public async Task MissStaleAndUnsupportedPicksAreRefusedWithoutANumberAndKeepTheStart()
    {
        var session = new ViewerMeasureSession();
        session.Activate();
        session.Accept(Hit(5, 1, "/A", Vector3.Zero), units: null);
        RenderPickRequest request = Request(5, 1);

        string missed = session.Accept(RenderPickResult.Miss(request, 5, 1), AuthoredCentimeters);
        string stale = session.Accept(RenderPickResult.Stale(request, 6, 1), AuthoredCentimeters);
        string unsupported = session.Accept(RenderPickResult.Unsupported(request, 5, 1), AuthoredCentimeters);

        await Assert.That(missed).StartsWith("Measure refused: the end pick missed geometry");
        await Assert.That(stale).StartsWith("Measure refused: the end pick was stale");
        await Assert.That(unsupported).StartsWith("Measure refused: picking is unsupported");
        foreach (string refusal in new[] { missed, stale, unsupported })
        {
            await Assert.That(refusal).DoesNotContain(" m (");
        }
        await Assert.That(session.Measurement).IsNull();
        await Assert.That(session.HasStart).IsTrue();
    }

    [Test]
    public async Task HitWithoutWorldPositionIsRefused()
    {
        var session = new ViewerMeasureSession();
        session.Activate();
        RenderPickResult noPosition = RenderPickResult.Hit(Request(1, 1), 1, 1, new SelectionItem("/A"));

        string status = session.Accept(noPosition, units: null);

        await Assert.That(status).StartsWith("Measure refused: the start pick has no world position");
        await Assert.That(session.HasStart).IsFalse();
    }

    [Test]
    public async Task PicksAcrossStateOrSceneRevisionsAreRefusedAndResetTheStart()
    {
        var session = new ViewerMeasureSession();
        session.Activate();
        session.Accept(Hit(1, 1, "/A", Vector3.Zero), units: null);

        string stateMismatch = session.Accept(Hit(2, 1, "/B", Vector3.One), AuthoredCentimeters);

        await Assert.That(stateMismatch).IsEqualTo(
            "Measure refused: the two picks were made against different view states; " +
            "no distance was measured. Pick the start point again.");
        await Assert.That(session.HasStart).IsFalse();

        session.Accept(Hit(2, 1, "/A", Vector3.Zero), units: null);
        string sceneMismatch = session.Accept(Hit(2, 9, "/B", Vector3.One), AuthoredCentimeters);

        await Assert.That(sceneMismatch).Contains("different scene revisions");
        await Assert.That(session.Measurement).IsNull();
    }

    [Test]
    public async Task UnreadableUnitsRefuseTheEndPick()
    {
        var session = new ViewerMeasureSession();
        session.Activate();
        session.Accept(Hit(1, 1, "/A", Vector3.Zero), units: null);

        string missingUnits = session.Accept(Hit(1, 1, "/B", Vector3.One), units: null);
        session.Accept(Hit(1, 1, "/A", Vector3.Zero), units: null);
        string unsupported = session.RefuseUnits("upAxis must be Y or Z");

        await Assert.That(missingUnits).StartsWith("Measure refused: the stage linear units are unavailable");
        await Assert.That(unsupported).Contains("(upAxis must be Y or Z)");
        await Assert.That(session.HasStart).IsFalse();
        await Assert.That(session.Measurement).IsNull();
    }

    [Test]
    public async Task StateRevisionChangesClearPendingPointsAndMeasurements()
    {
        var session = new ViewerMeasureSession();
        session.Activate();
        session.Accept(Hit(3, 1, "/A", Vector3.Zero), units: null);

        await Assert.That(session.InvalidateForStateRevision(3)).IsNull();
        await Assert.That(session.HasStart).IsTrue();
        await Assert.That(session.InvalidateForStateRevision(4))
            .IsEqualTo("Measurement cleared because the view changed; pick the start point again.");
        await Assert.That(session.HasStart).IsFalse();

        session.Accept(Hit(4, 1, "/A", Vector3.Zero), units: null);
        session.Accept(Hit(4, 1, "/B", Vector3.One), AuthoredCentimeters);
        await Assert.That(session.Measurement).IsNotNull();
        await Assert.That(session.InvalidateForStateRevision(5)).IsNotNull();
        await Assert.That(session.Measurement).IsNull();
        await Assert.That(session.InvalidateForStateRevision(6)).IsNull();
    }

    [Test]
    public async Task StageChangesAndCancelDropEverything()
    {
        var session = new ViewerMeasureSession();
        await Assert.That(session.Cancel()).IsFalse();
        await Assert.That(session.Invalidate("the stage changed")).IsNull();

        session.Activate();
        session.Accept(Hit(1, 1, "/A", Vector3.Zero), units: null);
        session.Accept(Hit(1, 1, "/B", Vector3.One), AuthoredCentimeters);
        await Assert.That(session.Invalidate("the stage changed"))
            .IsEqualTo("Measurement cleared because the stage changed; pick the start point again.");
        await Assert.That(session.Measurement).IsNull();

        session.Accept(Hit(1, 1, "/A", Vector3.Zero), units: null);
        await Assert.That(session.Cancel()).IsTrue();
        await Assert.That(session.IsActive).IsFalse();
        await Assert.That(session.HasStart).IsFalse();
        await Assert.That(() => session.Accept(Hit(1, 1, "/A", Vector3.Zero), units: null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EveryRefusalHasAReadableDescription()
    {
        foreach (RenderPickMeasurementRefusal refusal in Enum.GetValues<RenderPickMeasurementRefusal>())
        {
            if (refusal == RenderPickMeasurementRefusal.None)
            {
                continue;
            }
            await Assert.That(ViewerMeasureSession.DescribeRefusal(refusal))
                .DoesNotStartWith("refusal ");
        }
    }

    [Test]
    public async Task MeasureCommandIsACheckableCtrlMToolWithAnAccessibleName()
    {
        ViewerCommandDescriptor command = ViewerCommandCatalog.Get(ViewerCommandIds.ToolsMeasureDistance);

        await Assert.That(command.Group).IsEqualTo(ViewerCommandGroup.Tools);
        await Assert.That(command.CheckKind).IsEqualTo(ViewerCommandCheckKind.Check);
        await Assert.That(command.Gesture).IsEqualTo("Ctrl+M");
        await Assert.That(command.AccessibleName).IsEqualTo("Measure distance between two picked points");
        await Assert.That(ViewerShortcutCatalog.All.Select(shortcut => shortcut.Gesture)).Contains("Ctrl+M");
    }

    private static RenderPickRequest Request(ulong stateRevision, ulong? sceneRevision) =>
        new(1, 1, new ViewportDimensions(8, 8), stateRevision, sceneRevision, RenderPickTarget.Primitive);

    private static RenderPickResult Hit(ulong stateRevision, ulong? sceneRevision, string path, Vector3 position) =>
        RenderPickResult.Hit(
            Request(stateRevision, sceneRevision),
            stateRevision,
            sceneRevision,
            new SelectionItem(path),
            position);
}
