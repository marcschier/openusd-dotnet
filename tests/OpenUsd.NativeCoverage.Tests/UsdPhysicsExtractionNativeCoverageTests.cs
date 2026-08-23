// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;

namespace OpenUsd.NativeCoverage.Tests;

/// <remarks>
/// Extraction advances a process wide traversal counter, so every extraction test shares one
/// constraint key and never runs beside another extraction.
/// </remarks>
[NotInParallel("PhysicsExtraction")]
public sealed class UsdPhysicsExtractionNativeCoverageTests
{
    private const string ZUpCentimetreStage = """
        #usda 1.0
        (
            metersPerUnit = 0.01
            kilogramsPerUnit = 0.001
            upAxis = "Z"
            timeCodesPerSecond = 48
            startTimeCode = 2
            endTimeCode = 12
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, 0, -1)
            float physics:gravityMagnitude = 981
        }

        def Xform "Body" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsMassAPI"]
        )
        {
            double3 xformOp:translate = (0, 0, 100)
            uniform token[] xformOpOrder = ["xformOp:translate"]
            float physics:mass = 3
            rel physics:simulationOwner = </Scene>

            def Sphere "Shape" (
                prepend apiSchemas = ["PhysicsCollisionAPI"]
            )
            {
                double radius = 50
            }
        }
        """;

    [Test]
    public async Task OneExtractionPerformsExactlyOneComposedStageTraversal()
    {
        NativeCoverageRuntime.EnsureNativeLoaded();
        ulong before = UsdPhysicsStageExtractor.GetTraversalCount();

        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(OneExtractionPerformsExactlyOneComposedStageTraversal), ZUpCentimetreStage);

        ulong after = UsdPhysicsStageExtractor.GetTraversalCount();
        await Assert.That(after - before).IsEqualTo(1UL);
        await Assert.That(UsdPhysicsStageExtractor.GetVisitedPrimCount()).IsGreaterThan(0UL);
        await Assert.That(page.ObjectCount).IsGreaterThan(0);
    }

    [Test]
    public async Task StageUnitsAndUpAxisConvertIntoSimulationSpaceAndBack()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(StageUnitsAndUpAxisConvertIntoSimulationSpaceAndBack), ZUpCentimetreStage);

        await Assert.That(page.MetersPerUnit).IsEqualTo(0.01).Within(1e-12);
        await Assert.That(page.KilogramsPerUnit).IsEqualTo(0.001).Within(1e-12);
        await Assert.That(page.TimeCodesPerSecond).IsEqualTo(48.0).Within(1e-12);
        await Assert.That(page.StartTimeCode).IsEqualTo(2.0).Within(1e-12);
        await Assert.That(page.EndTimeCode).IsEqualTo(12.0).Within(1e-12);
        await Assert.That(page.UpAxis).IsEqualTo(UsdPhysicsExtractionUpAxis.Z);
        await Assert.That(page.Flags.HasFlag(UsdPhysicsExtractionPageTraits.UpAxisConverted))
            .IsTrue();

        UsdPhysicsExtractionObject body = PhysicsExtractionStages.Find(
            page, "/Body", UsdPhysicsExtractionObjectKind.RigidBody);

        // One hundred centimetres along stage Z becomes one metre along simulation Y.
        await Assert.That(body.Position.X).IsEqualTo(0.0).Within(1e-9);
        await Assert.That(body.Position.Y).IsEqualTo(1.0).Within(1e-9);
        await Assert.That(body.Position.Z).IsEqualTo(0.0).Within(1e-9);

        var space = UsdPhysicsExtractionSpace.FromPage(page);
        (double X, double Y, double Z) roundTrip = space.ToStage(body.Position);
        await Assert.That(roundTrip.X).IsEqualTo(0.0).Within(1e-9);
        await Assert.That(roundTrip.Y).IsEqualTo(0.0).Within(1e-9);
        await Assert.That(roundTrip.Z).IsEqualTo(100.0).Within(1e-9);

        UsdPhysicsExtractionObject collider = PhysicsExtractionStages.Find(
            page, "/Body/Shape", UsdPhysicsExtractionObjectKind.Collider);
        await Assert.That(collider.Geometry).IsEqualTo(UsdPhysicsExtractionGeometryKind.Sphere);
        await Assert.That(collider.Extent.X).IsEqualTo(0.5).Within(1e-9);
    }

    [Test]
    public async Task MissingStageUnitsFallBackAndAreReported()
    {
        const string usda = """
            #usda 1.0

            def PhysicsScene "Scene"
            {
            }
            """;

        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(MissingStageUnitsFallBackAndAreReported), usda);

        await Assert.That(page.Flags.HasFlag(UsdPhysicsExtractionPageTraits.MetersFallback))
            .IsTrue();
        await Assert.That(page.Flags.HasFlag(UsdPhysicsExtractionPageTraits.KilogramsFallback))
            .IsTrue();
        await Assert.That(page.KilogramsPerUnit).IsEqualTo(1.0).Within(1e-12);
        await Assert.That(page.MetersPerUnit).IsGreaterThan(0.0);
        await Assert.That(PhysicsExtractionStages.HasDiagnostic(
            page, UsdPhysicsExtractionCode.KilogramsPerUnitFallback)).IsTrue();
        await Assert.That(page.UpAxis).IsEqualTo(UsdPhysicsExtractionUpAxis.Y);
    }

    [Test]
    public async Task RigidBodiesAreClassifiedAsDynamicKinematicAnimatedOrStatic()
    {
        const string usda = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def PhysicsScene "Scene"
            {
            }

            def Xform "Dynamic" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI"]
            )
            {
            }

            def Xform "Kinematic" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI"]
            )
            {
                bool physics:kinematicEnabled = 1
            }

            def Xform "Disabled" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI"]
            )
            {
                bool physics:rigidBodyEnabled = 0
            }

            def Xform "Animated" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI"]
            )
            {
                double3 xformOp:translate.timeSamples = {
                    0: (0, 0, 0),
                    1: (0, 1, 0),
                }
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """;

        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(RigidBodiesAreClassifiedAsDynamicKinematicAnimatedOrStatic), usda);

        UsdPhysicsExtractionObject dynamic = PhysicsExtractionStages.Find(
            page, "/Dynamic", UsdPhysicsExtractionObjectKind.RigidBody);
        await Assert.That(dynamic.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.Dynamic)).IsTrue();

        UsdPhysicsExtractionObject kinematic = PhysicsExtractionStages.Find(
            page, "/Kinematic", UsdPhysicsExtractionObjectKind.RigidBody);
        await Assert.That(kinematic.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.Kinematic))
            .IsTrue();
        await Assert.That(kinematic.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.Dynamic))
            .IsFalse();

        UsdPhysicsExtractionObject disabled = PhysicsExtractionStages.Find(
            page, "/Disabled", UsdPhysicsExtractionObjectKind.RigidBody);
        await Assert.That(disabled.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.Static)).IsTrue();

        UsdPhysicsExtractionObject animated = PhysicsExtractionStages.Find(
            page, "/Animated", UsdPhysicsExtractionObjectKind.RigidBody);
        await Assert.That(animated.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.Animated))
            .IsTrue();
        await Assert.That(animated.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.Kinematic))
            .IsTrue();
        await Assert.That(PhysicsExtractionStages.HasDiagnostic(
            page, UsdPhysicsExtractionCode.AnimatedDynamicBody)).IsTrue();
    }

    [Test]
    public async Task NestedRigidBodiesAreRejectedAndDisabledIndividually()
    {
        const string usda = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def PhysicsScene "Scene"
            {
            }

            def Xform "Outer" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI"]
            )
            {
                def Xform "Inner" (
                    prepend apiSchemas = ["PhysicsRigidBodyAPI"]
                )
                {
                }
            }
            """;

        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(NestedRigidBodiesAreRejectedAndDisabledIndividually), usda);

        UsdPhysicsExtractionObject outer = PhysicsExtractionStages.Find(
            page, "/Outer", UsdPhysicsExtractionObjectKind.RigidBody);
        UsdPhysicsExtractionObject inner = PhysicsExtractionStages.Find(
            page, "/Outer/Inner", UsdPhysicsExtractionObjectKind.RigidBody);

        await Assert.That(outer.IsEnabled).IsTrue();
        await Assert.That(inner.IsEnabled).IsFalse();
        await Assert.That(inner.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.NestedBody)).IsTrue();
        await Assert.That(PhysicsExtractionStages.HasDiagnostic(
            page, UsdPhysicsExtractionCode.NestedRigidBody)).IsTrue();
        await Assert.That(page.Flags.HasFlag(UsdPhysicsExtractionPageTraits.HasDisabledObjects))
            .IsTrue();
    }

    [Test]
    public async Task ContradictoryOwnershipIsRejectedDeterministically()
    {
        const string usda = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def PhysicsScene "SceneA"
            {
            }

            def PhysicsScene "SceneB"
            {
            }

            def Xform "Body" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI"]
            )
            {
                rel physics:simulationOwner = </SceneA>

                def Sphere "Shape" (
                    prepend apiSchemas = ["PhysicsCollisionAPI"]
                )
                {
                    rel physics:simulationOwner = </SceneB>
                }
            }

            def Xform "Confused" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI"]
            )
            {
                rel physics:simulationOwner = [</SceneA>, </SceneB>]
            }
            """;

        UsdPhysicsExtractionPage first = PhysicsExtractionStages.Extract(
            nameof(ContradictoryOwnershipIsRejectedDeterministically), usda);
        UsdPhysicsExtractionPage second = PhysicsExtractionStages.Extract(
            nameof(ContradictoryOwnershipIsRejectedDeterministically), usda);

        await Assert.That(first.FingerprintLow).IsEqualTo(second.FingerprintLow);
        await Assert.That(first.DiagnosticCount).IsEqualTo(second.DiagnosticCount);

        UsdPhysicsExtractionObject confused = PhysicsExtractionStages.Find(
            first, "/Confused", UsdPhysicsExtractionObjectKind.RigidBody);
        await Assert.That(confused.IsEnabled).IsFalse();
        await Assert.That(PhysicsExtractionStages.HasDiagnostic(
            first, UsdPhysicsExtractionCode.MultipleSimulationOwners)).IsTrue();

        UsdPhysicsExtractionObject collider = PhysicsExtractionStages.Find(
            first, "/Body/Shape", UsdPhysicsExtractionObjectKind.Collider);
        await Assert.That(collider.IsEnabled).IsFalse();
        await Assert.That(PhysicsExtractionStages.HasDiagnostic(
            first, UsdPhysicsExtractionCode.ContradictoryOwnership)).IsTrue();

        UsdPhysicsExtractionObject body = PhysicsExtractionStages.Find(
            first, "/Body", UsdPhysicsExtractionObjectKind.RigidBody);
        await Assert.That(body.IsEnabled).IsTrue();
    }

    [Test]
    public async Task InstanceProxiesProduceStableDistinctIdentities()
    {
        const string usda = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def PhysicsScene "Scene"
            {
            }

            class Xform "Prototype"
            {
                def Sphere "Shape" (
                    prepend apiSchemas = ["PhysicsCollisionAPI"]
                )
                {
                }
            }

            def Xform "InstanceA" (
                instanceable = true
                prepend inherits = </Prototype>
            )
            {
            }

            def Xform "InstanceB" (
                instanceable = true
                prepend inherits = </Prototype>
            )
            {
            }
            """;

        UsdPhysicsExtractionPage first = PhysicsExtractionStages.Extract(
            nameof(InstanceProxiesProduceStableDistinctIdentities), usda);
        UsdPhysicsExtractionPage second = PhysicsExtractionStages.Extract(
            nameof(InstanceProxiesProduceStableDistinctIdentities), usda);

        UsdPhysicsExtractionObject a = PhysicsExtractionStages.Find(
            first, "/InstanceA/Shape", UsdPhysicsExtractionObjectKind.Collider);
        UsdPhysicsExtractionObject b = PhysicsExtractionStages.Find(
            first, "/InstanceB/Shape", UsdPhysicsExtractionObjectKind.Collider);
        UsdPhysicsExtractionObject repeated = PhysicsExtractionStages.Find(
            second, "/InstanceA/Shape", UsdPhysicsExtractionObjectKind.Collider);

        await Assert.That(a.Id).IsNotEqualTo(b.Id);
        await Assert.That(a.Id).IsEqualTo(repeated.Id);
        await Assert.That(a.PrototypeId).IsEqualTo(b.PrototypeId);
        await Assert.That(a.PrototypeId).IsNotEqualTo(0UL);
        await Assert.That(a.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.InstanceProxy)).IsTrue();
        await Assert.That(first.Flags.HasFlag(UsdPhysicsExtractionPageTraits.HasInstances)).IsTrue();
        await Assert.That(first.FingerprintHigh).IsEqualTo(second.FingerprintHigh);
    }

    [Test]
    public async Task DiagnosticsAreOrderedByTheObjectTheyName()
    {
        const string usda = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def PhysicsScene "Scene"
            {
            }

            def Xform "A" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI"]
            )
            {
                def Xform "Nested" (
                    prepend apiSchemas = ["PhysicsRigidBodyAPI"]
                )
                {
                }
            }

            def Sphere "Loose" (
                prepend apiSchemas = ["PhysicsCollisionAPI"]
            )
            {
            }
            """;

        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(DiagnosticsAreOrderedByTheObjectTheyName), usda);

        await Assert.That(page.DiagnosticCount).IsGreaterThan(0);
        int previous = int.MinValue;
        for (int index = 0; index < page.DiagnosticCount; index++)
        {
            UsdPhysicsExtractionDiagnostic diagnostic = page.GetDiagnostic(index);
            await Assert.That(diagnostic.ObjectIndex).IsGreaterThanOrEqualTo(previous);
            previous = diagnostic.ObjectIndex;
            if (diagnostic.ObjectIndex >= 0)
            {
                await Assert.That(diagnostic.ObjectId)
                    .IsEqualTo(page.GetObject(diagnostic.ObjectIndex).Id);
            }
        }
    }

    [Test]
    public async Task SchedulerExtractionReturnsADetachedPageAndHonoursCancellation()
    {
        string path = PhysicsExtractionStages.Write(
            nameof(SchedulerExtractionReturnsADetachedPageAndHonoursCancellation),
            ZUpCentimetreStage);

        await using var scheduler = UsdStageScheduler.Open(path);
        UsdPhysicsExtractionPage page = await UsdPhysicsStageExtractor.ExtractAsync(scheduler);
        await Assert.That(page.ObjectCount).IsGreaterThan(0);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.That(async () =>
                await UsdPhysicsStageExtractor.ExtractAsync(scheduler, cancelled.Token))
            .Throws<OperationCanceledException>();

        // The page keeps working after the scheduler and its stage are gone.
        await scheduler.DisposeAsync();
        await Assert.That(page.GetObject(0).Path).IsNotEmpty();
    }
}
