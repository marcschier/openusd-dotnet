// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Tests;

public sealed class UsdPhysicsContractsTests
{
    [Test]
    public async Task CapabilitiesReportSupportsForCombinedFlags()
    {
        var capabilities = new UsdPhysicsCapabilities(
            UsdPhysicsCapability.RigidBodies | UsdPhysicsCapability.SceneQueries);

        await Assert.That(capabilities.Supports(UsdPhysicsCapability.RigidBodies)).IsTrue();
        await Assert.That(capabilities.Supports(
            UsdPhysicsCapability.RigidBodies | UsdPhysicsCapability.SceneQueries)).IsTrue();
        await Assert.That(capabilities.Supports(UsdPhysicsCapability.Articulations)).IsFalse();
        await Assert.That(UsdPhysicsCapabilities.None.Supports(UsdPhysicsCapability.RigidBodies)).IsFalse();
    }

    [Test]
    public async Task DiagnosticRejectsBlankCodeOrMessage()
    {
        await Assert.That(() => _ = new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Error,
                UsdPhysicsDiagnosticCategory.General,
                string.Empty,
                "message"))
            .Throws<ArgumentException>();
        await Assert.That(() => _ = new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Error,
                UsdPhysicsDiagnosticCategory.General,
                "code",
                " "))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task DiagnosticsCollectionDefensivelyCopiesAndReportsHasErrors()
    {
        var warning = new UsdPhysicsDiagnostic(
            UsdPhysicsDiagnosticSeverity.Warning,
            UsdPhysicsDiagnosticCategory.Build,
            "code.warning",
            "warning message");
        var error = new UsdPhysicsDiagnostic(
            UsdPhysicsDiagnosticSeverity.Error,
            UsdPhysicsDiagnosticCategory.Build,
            "code.error",
            "error message");
        var entries = new List<UsdPhysicsDiagnostic> { warning };

        var diagnostics = new UsdPhysicsDiagnostics(entries);
        entries.Add(error);

        await Assert.That(diagnostics.Entries).Count().IsEqualTo(1);
        await Assert.That(diagnostics.HasErrors).IsFalse();
        await Assert.That(new UsdPhysicsDiagnostics([warning, error]).HasErrors).IsTrue();
        await Assert.That(diagnostics).IsEqualTo(new UsdPhysicsDiagnostics([warning]));
        await Assert.That(UsdPhysicsDiagnostics.Empty.Entries).IsEmpty();
        await Assert.That(UsdPhysicsDiagnostics.Empty.HasErrors).IsFalse();
    }

    [Test]
    public async Task ObjectIdDefaultsToNoneAndFormatsStably()
    {
        await Assert.That(UsdPhysicsObjectId.None.IsNone).IsTrue();
        await Assert.That(UsdPhysicsObjectId.None.Value).IsEqualTo(0ul);

        var id = new UsdPhysicsObjectId(42, UsdPhysicsObjectKind.RigidBody);

        await Assert.That(id.IsNone).IsFalse();
        await Assert.That(id.ToString()).IsEqualTo("RigidBody:0x000000000000002a");
    }

    [Test]
    public async Task SessionOptionsRejectInvalidCapacitiesAndFrequency()
    {
        await Assert.That(() => _ = new UsdPhysicsSessionOptions(maxRigidBodies: -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => _ = new UsdPhysicsSessionOptions(maxSubStepsPerTick: 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => _ = new UsdPhysicsSessionOptions(fixedFrequencyOverrideHz: 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => _ = new UsdPhysicsSessionOptions(fixedFrequencyOverrideHz: double.NaN))
            .Throws<ArgumentOutOfRangeException>();

        UsdPhysicsSessionOptions defaults = UsdPhysicsSessionOptions.Default;

        await Assert.That(defaults.RequestedCapabilities).IsEqualTo(UsdPhysicsCapability.All);
        await Assert.That(defaults.MaxSubStepsPerTick).IsEqualTo(8);
        await Assert.That(defaults.FixedFrequencyOverrideHz).IsNull();
    }

    [Test]
    public async Task SnapshotRejectsNonFiniteTimeCodeAndNullDiagnostics()
    {
        await Assert.That(() => _ = new UsdPhysicsSnapshot(0, double.NaN, 0, UsdPhysicsDiagnostics.Empty))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => _ = new UsdPhysicsSnapshot(0, 0, 0, null!))
            .Throws<ArgumentNullException>();

        await Assert.That(UsdPhysicsSnapshot.Empty.Revision).IsEqualTo(0ul);
        await Assert.That(UsdPhysicsSnapshot.Empty.Diagnostics).IsEqualTo(UsdPhysicsDiagnostics.Empty);
    }

    [Test]
    public async Task CommandRejectsNonFiniteMagnitude()
    {
        await Assert.That(() => _ = new UsdPhysicsCommand(
                UsdPhysicsCommandKind.Force,
                UsdPhysicsObjectId.None,
                default,
                double.PositiveInfinity))
            .Throws<ArgumentOutOfRangeException>();

        var command = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.Impulse,
            new UsdPhysicsObjectId(1, UsdPhysicsObjectKind.RigidBody),
            new UsdVec3d(1, 0, 0),
            5);

        await Assert.That(command.Magnitude).IsEqualTo(5d);
    }

    [Test]
    public async Task EventRejectsNonFiniteTimeCode()
    {
        await Assert.That(() => _ = new UsdPhysicsEvent(
                UsdPhysicsEventKind.ContactBegan,
                UsdPhysicsObjectId.None,
                null,
                0,
                double.NaN))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task EventBatchTracksDroppedCountAndOverflow()
    {
        var evt = new UsdPhysicsEvent(
            UsdPhysicsEventKind.SleepStateChanged,
            new UsdPhysicsObjectId(1, UsdPhysicsObjectKind.RigidBody),
            null,
            3,
            1.5);

        var batch = new UsdPhysicsEventBatch([evt], droppedCount: 2);

        await Assert.That(batch.Entries).Count().IsEqualTo(1);
        await Assert.That(batch.DroppedCount).IsEqualTo(2);
        await Assert.That(batch.IsOverflowed).IsTrue();
        await Assert.That(UsdPhysicsEventBatch.Empty.IsOverflowed).IsFalse();
        await Assert.That(() => _ = new UsdPhysicsEventBatch([evt], droppedCount: -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task QueryRequestValidatesRadiusForSweepAndOverlap()
    {
        await Assert.That(() => _ = new UsdPhysicsQueryRequest(
                UsdPhysicsQueryKind.Sweep,
                default,
                new UsdVec3d(1, 0, 0),
                maxDistance: 10,
                radius: 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => _ = new UsdPhysicsQueryRequest(
                UsdPhysicsQueryKind.Raycast,
                default,
                new UsdVec3d(1, 0, 0),
                maxDistance: double.NaN))
            .Throws<ArgumentOutOfRangeException>();

        var request = new UsdPhysicsQueryRequest(
            UsdPhysicsQueryKind.Raycast,
            default,
            new UsdVec3d(1, 0, 0),
            maxDistance: 10);

        await Assert.That(request.Filter).IsEqualTo(UsdPhysicsQueryFilter.Default);
    }

    [Test]
    public async Task QueryHitRejectsNegativeOrNonFiniteDistance()
    {
        await Assert.That(() => _ = new UsdPhysicsQueryHit(
                UsdPhysicsObjectId.None,
                default,
                default,
                -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task QueryResultTracksOverflowAndDefensivelyCopiesHits()
    {
        var hit = new UsdPhysicsQueryHit(
            new UsdPhysicsObjectId(7, UsdPhysicsObjectKind.Collider),
            default,
            default,
            2.5);
        var hits = new List<UsdPhysicsQueryHit> { hit };

        var result = new UsdPhysicsQueryResult(hits, droppedCount: 0);
        hits.Clear();

        await Assert.That(result.Hits).Count().IsEqualTo(1);
        await Assert.That(result.IsOverflowed).IsFalse();
        await Assert.That(UsdPhysicsQueryResult.Empty.Hits).IsEmpty();
    }

    [Test]
    public async Task BakeRequestValidatesTimeRangeAndSampleStep()
    {
        await Assert.That(() => _ = new UsdPhysicsBakeRequest(string.Empty, 0, 1, 0.1))
            .Throws<ArgumentException>();
        await Assert.That(() => _ = new UsdPhysicsBakeRequest("/target.usda", 5, 1, 0.1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => _ = new UsdPhysicsBakeRequest("/target.usda", 0, 1, 0))
            .Throws<ArgumentOutOfRangeException>();

        var request = new UsdPhysicsBakeRequest("/target.usda", 0, 10, 0.5);

        await Assert.That(request.TargetLayerPath).IsEqualTo("/target.usda");
    }

    [Test]
    public async Task BakeResultRejectsNegativeSampleCountOrNullDiagnostics()
    {
        await Assert.That(() => _ = new UsdPhysicsBakeResult(
                UsdPhysicsBakeStatus.Completed,
                -1,
                UsdPhysicsDiagnostics.Empty))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => _ = new UsdPhysicsBakeResult(
                UsdPhysicsBakeStatus.Completed,
                0,
                null!))
            .Throws<ArgumentNullException>();
    }
}
