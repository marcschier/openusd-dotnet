// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;
using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.NativeCoverage.Tests;

/// <remarks>
/// These tests run the real native traversal over an authored stage and then compose the page,
/// so the unit, basis, ownership and drive conversions are proven end to end rather than against
/// a hand built page. Extraction advances a process wide traversal counter, so the class shares
/// the extraction constraint key.
/// </remarks>
[NotInParallel("PhysicsExtraction")]
public sealed class UsdPhysicsExtractionComposeNativeCoverageTests
{
    private const double Tolerance = 1e-5;
    private const double DegreesToRadians = Math.PI / 180.0;

    // Centimetres, grams and a Z up basis, so every conversion the composer performs is visible.
    private const string ZUpGramStage = """
        #usda 1.0
        (
            metersPerUnit = 0.01
            kilogramsPerUnit = 0.001
            upAxis = "Z"
            timeCodesPerSecond = 48
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, 0, -1)
            float physics:gravityMagnitude = 981
        }

        def Material "Rubber" (
            prepend apiSchemas = ["PhysicsMaterialAPI"]
        )
        {
            float physics:density = 1
            float physics:staticFriction = 0.6
            float physics:dynamicFriction = 0.5
            float physics:restitution = 0.25
        }

        def Cube "Box" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI", "PhysicsMassAPI"]
        )
        {
            double size = 100
            double3 xformOp:translate = (0, 0, 200)
            uniform token[] xformOpOrder = ["xformOp:translate"]
            float physics:mass = 2000
            vector3f physics:velocity = (0, 0, 100)
            vector3f physics:angularVelocity = (0, 0, 180)
            rel physics:simulationOwner = </Scene>
            rel material:binding:physics = </Rubber>

            def Capsule "Cap" (
                prepend apiSchemas = ["PhysicsCollisionAPI"]
            )
            {
                uniform token axis = "Y"
                double radius = 10
                double height = 40
                double3 xformOp:translate = (0, 0, 100)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
        }

        def Cube "Ground" (
            prepend apiSchemas = ["PhysicsCollisionAPI"]
        )
        {
            double size = 1000
        }

        def Cube "Anchor" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]
        )
        {
            double size = 50
            bool physics:kinematicEnabled = 1
        }

        def PhysicsRevoluteJoint "Hinge" (
            prepend apiSchemas = ["PhysicsDriveAPI:angular", "PhysicsLimitAPI:angular"]
        )
        {
            rel physics:body0 = </Box>
            rel physics:body1 = </Anchor>
            uniform token physics:axis = "Y"
            float physics:lowerLimit = -45
            float physics:upperLimit = 45
            float drive:angular:physics:stiffness = 1000000
            float drive:angular:physics:damping = 2000000
            float drive:angular:physics:maxForce = 10000000
            float drive:angular:physics:targetPosition = 90
            float drive:angular:physics:targetVelocity = 30
        }
        """;

    // No authored gravity direction at all, so the composer must use the simulation space default.
    private const string XUpDefaultGravityStage = """
        #usda 1.0
        (
            metersPerUnit = 1
            upAxis = "X"
        )

        def PhysicsScene "Scene"
        {
        }

        def Cube "Box" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]
        )
        {
            double size = 2
            double3 xformOp:translate = (3, 0, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
            vector3f physics:velocity = (2, 0, 0)
            vector3f physics:angularVelocity = (90, 0, 0)
        }
        """;

    // A body that states its inertia about a rotated principal axis frame.
    private const string MassFrameStage = """
        #usda 1.0
        (
            metersPerUnit = 0.01
            kilogramsPerUnit = 0.001
            upAxis = "Z"
        )

        def PhysicsScene "Scene"
        {
        }

        def Cube "Spinner" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI", "PhysicsMassAPI"]
        )
        {
            double size = 100
            float physics:mass = 2000
            point3f physics:centerOfMass = (0, 0, 50)
            float3 physics:diagonalInertia = (1000, 2000, 3000)
            quatf physics:principalAxes = (0.70710678, 0, 0, 0.70710678)
        }
        """;

    // Two bodies and one joint that authors no limit at all, which every limit case edits.
    private const string FreeJointStage = """
        #usda 1.0
        (
            metersPerUnit = 1
            upAxis = "Y"
        )

        def PhysicsScene "Scene"
        {
        }

        def Cube "A" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]
        )
        {
            double size = 1
        }

        def Cube "B" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]
        )
        {
            double size = 1
            double3 xformOp:translate = (0, 2, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
        }

        def PhysicsRevoluteJoint "Hinge"
        {
            rel physics:body0 = </A>
            rel physics:body1 = </B>
            uniform token physics:axis = "Y"
        }
        """;

    // Values that are legal in USD but that the build page cannot take once they are converted.
    private const string ExtremeValueStage = """
        #usda 1.0
        (
            metersPerUnit = 100
            upAxis = "Z"
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, 0, -3e38)
            float physics:gravityMagnitude = 3e38
        }

        def Cube "Healthy" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]
        )
        {
            double size = 1
        }

        def Sphere "Huge" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]
        )
        {
            double radius = 1
            double3 xformOp:translate = (0, 0, 4)
            double3 xformOp:scale = (1e300, 1, 1)
            uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
        }

        def Sphere "Tiny" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]
        )
        {
            double radius = 1
            double3 xformOp:translate = (0, 0, 8)
            double3 xformOp:scale = (1, 1e-320, 1)
            uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
        }
        """;

    [Test]
    public async Task AStageWithExtremeButLegalValuesStillComposesAndValidates()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(AStageWithExtremeButLegalValuesStillComposesAndValidates), ExtremeValueStage);

        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();
        PhysxPageValidationResult validation = PhysxPageValidator.Validate(built.Bytes);
        await Assert.That(validation.IsValid).IsTrue().Because(validation.Message ?? "no message");

        // Three hundred metres per unit turns an authored magnitude that a float still holds into
        // one that it cannot, and the scene falls back rather than carrying an infinity.
        ComposedScene scene = ReadScene(built);
        await Assert.That((double)scene.Magnitude).IsEqualTo(9.81).Within(Tolerance);
        await Assert.That((double)scene.Direction.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)scene.Direction.Y).IsEqualTo(-1.0).Within(Tolerance);
        await Assert.That((double)scene.Direction.Z).IsEqualTo(0.0).Within(Tolerance);

        await Assert.That(report.Skipped.Any(
            note => note.Contains("/Scene", StringComparison.Ordinal) &&
                note.Contains("gravity magnitude", StringComparison.Ordinal))).IsTrue();

        // The healthy body keeps its shape whatever the other two bodies authored.
        ComposedShape healthy = ReadShape(built, "/Healthy.shape");
        await Assert.That((double)healthy.Scale.X).IsEqualTo(1.0).Within(Tolerance);
        await Assert.That((double)healthy.Scale.Y).IsEqualTo(1.0).Within(Tolerance);
        await Assert.That((double)healthy.Scale.Z).IsEqualTo(1.0).Within(Tolerance);

        // Both extreme scales are reduced to a unit scale, and only the collider that authored
        // them is named.
        await Assert.That((double)ReadShape(built, "/Huge.shape").Scale.X)
            .IsEqualTo(1.0).Within(Tolerance);
        await Assert.That((double)ReadShape(built, "/Tiny.shape").Scale.Y)
            .IsEqualTo(1.0).Within(Tolerance);
        await Assert.That(report.Skipped.Count(
            note => note.Contains("scale", StringComparison.Ordinal))).IsEqualTo(2);
    }

    [Test]
    public async Task AZUpGramStageComposesIntoSimulationUnitsInOneTraversal()
    {
        NativeCoverageRuntime.EnsureNativeLoaded();
        ulong before = UsdPhysicsStageExtractor.GetTraversalCount();

        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(AZUpGramStageComposesIntoSimulationUnitsInOneTraversal), ZUpGramStage);

        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);
        ulong after = UsdPhysicsStageExtractor.GetTraversalCount();

        // Composition never reads the stage again, so one extraction stays one traversal.
        await Assert.That(after - before).IsEqualTo(1UL);

        await Assert.That(report.Scenes).IsEqualTo(1);
        await Assert.That(report.Materials).IsEqualTo(1);
        await Assert.That(report.Shapes).IsEqualTo(4);
        await Assert.That(report.Actors).IsEqualTo(3);
        await Assert.That(report.Joints).IsEqualTo(1);

        using PhysxBuildPage built = builder.Build();
        PhysxPageValidationResult validation = PhysxPageValidator.Validate(built.Bytes);
        await Assert.That(validation.IsValid).IsTrue().Because(validation.Message ?? "no message");
    }

    [Test]
    public async Task TheSamePrimBodyAndColliderComposeIntoOneDynamicActor()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(TheSamePrimBodyAndColliderComposeIntoOneDynamicActor), ZUpGramStage);

        // The collider on the body prim belongs to that body rather than to the world.
        UsdPhysicsExtractionObject collider = PhysicsExtractionStages.Find(
            page, "/Box", UsdPhysicsExtractionObjectKind.Collider);
        UsdPhysicsExtractionObject body = PhysicsExtractionStages.Find(
            page, "/Box", UsdPhysicsExtractionObjectKind.RigidBody);
        await Assert.That(collider.ParentBodyIndex).IsEqualTo(body.Index);

        // The static fallback only names the ground plane, never the collider that found its body.
        await Assert.That(OrphanNames(page, collider.Index)).IsFalse();
        await Assert.That(OrphanNames(
            page,
            PhysicsExtractionStages.Find(
                page, "/Ground", UsdPhysicsExtractionObjectKind.Collider).Index)).IsTrue();

        using var builder = new PhysxPageBuilder();
        UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();

        ComposedActor box = ReadActor(built, "/Box");
        await Assert.That(box.Type).IsEqualTo((uint)PhysxActorType.Dynamic);

        // The cube on the body prim and the capsule below it both attach to that one actor.
        await Assert.That(box.ShapeCount).IsEqualTo(2u);

        ComposedActor ground = ReadActor(built, "/Ground");
        await Assert.That(ground.Type).IsEqualTo((uint)PhysxActorType.Static);
        await Assert.That(ground.ShapeCount).IsEqualTo(1u);

        ComposedActor anchor = ReadActor(built, "/Anchor");
        await Assert.That(anchor.Type).IsEqualTo((uint)PhysxActorType.Kinematic);
    }

    [Test]
    public async Task MassVelocitiesAndDensityReachTheBuilderInSimulationUnits()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(MassVelocitiesAndDensityReachTheBuilderInSimulationUnits), ZUpGramStage);

        using var builder = new PhysxPageBuilder();
        UsdPhysicsExtractionComposer.Compose(page, builder);

        // The build page always describes metres and kilograms.
        await Assert.That(builder.MetersPerUnit).IsEqualTo(1.0).Within(1e-12);
        await Assert.That(builder.KilogramsPerUnit).IsEqualTo(1.0).Within(1e-12);

        using PhysxBuildPage built = builder.Build();
        ComposedActor box = ReadActor(built, "/Box");

        // Two thousand grams is two kilograms.
        await Assert.That((double)box.Mass).IsEqualTo(2.0).Within(Tolerance);

        // One hundred centimetres per second along stage Z is one metre per second along Y.
        await Assert.That((double)box.LinearVelocity.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)box.LinearVelocity.Y).IsEqualTo(1.0).Within(Tolerance);
        await Assert.That((double)box.LinearVelocity.Z).IsEqualTo(0.0).Within(Tolerance);

        // One hundred and eighty degrees per second about stage Z is pi radians about Y.
        await Assert.That((double)box.AngularVelocity.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)box.AngularVelocity.Y).IsEqualTo(Math.PI).Within(Tolerance);
        await Assert.That((double)box.AngularVelocity.Z).IsEqualTo(0.0).Within(Tolerance);

        // One gram per cubic centimetre is one thousand kilograms per cubic metre.
        await Assert.That((double)ReadMaterialDensity(built)).IsEqualTo(1000.0).Within(1e-3);
    }

    [Test]
    public async Task AuthoredGravityRotatesIntoSimulationSpace()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(AuthoredGravityRotatesIntoSimulationSpace), ZUpGramStage);

        using var builder = new PhysxPageBuilder();
        UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();

        ComposedScene scene = ReadScene(built);
        await Assert.That((double)scene.Direction.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)scene.Direction.Y).IsEqualTo(-1.0).Within(Tolerance);
        await Assert.That((double)scene.Direction.Z).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)scene.Magnitude).IsEqualTo(9.81).Within(1e-4);
    }

    [Test]
    public async Task AnUnauthoredGravityStaysDownOnAnXUpStage()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(AnUnauthoredGravityStaysDownOnAnXUpStage), XUpDefaultGravityStage);

        await Assert.That(page.UpAxis).IsEqualTo(UsdPhysicsExtractionUpAxis.X);

        using var builder = new PhysxPageBuilder();
        UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();

        // The fallback is already stated in simulation space, so it must not be rotated.
        ComposedScene scene = ReadScene(built);
        await Assert.That((double)scene.Direction.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)scene.Direction.Y).IsEqualTo(-1.0).Within(Tolerance);
        await Assert.That((double)scene.Direction.Z).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)scene.Magnitude).IsEqualTo(9.81).Within(1e-4);

        // Two metres per second along stage X is two metres per second along simulation Y.
        ComposedActor box = ReadActor(built, "/Box");
        await Assert.That((double)box.LinearVelocity.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)box.LinearVelocity.Y).IsEqualTo(2.0).Within(Tolerance);
        await Assert.That((double)box.AngularVelocity.Y).IsEqualTo(Math.PI / 2.0).Within(Tolerance);
    }

    [Test]
    public async Task AnAuthoredGeometryAxisBecomesALocalShapePose()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(AnAuthoredGeometryAxisBecomesALocalShapePose), ZUpGramStage);

        UsdPhysicsExtractionObject capsule = PhysicsExtractionStages.Find(
            page, "/Box/Cap", UsdPhysicsExtractionObjectKind.Collider);
        await Assert.That(capsule.GeometryAxis).IsEqualTo(1);

        using var builder = new PhysxPageBuilder();
        UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();

        ComposedShape shape = ReadShape(built, "/Box/Cap.shape");
        await Assert.That(shape.Type).IsEqualTo((uint)PhysxShapeType.Capsule);
        await Assert.That((double)shape.Radius).IsEqualTo(0.1).Within(Tolerance);
        await Assert.That((double)shape.HalfHeight).IsEqualTo(0.2).Within(Tolerance);

        // A Y aligned capsule is a ninety degree turn about Z away from the simulation X axis.
        double half = Math.Sqrt(0.5);
        await Assert.That((double)shape.Rotation.W).IsEqualTo(half).Within(Tolerance);
        await Assert.That((double)shape.Rotation.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)shape.Rotation.Y).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)shape.Rotation.Z).IsEqualTo(half).Within(Tolerance);

        // The capsule sits one metre above the body, expressed in the frame of that body: the
        // actor carries the stage basis rotation, so the local offset stays on the authored up
        // axis and only becomes simulation up once the actor pose is applied.
        await Assert.That((double)shape.Position.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)shape.Position.Y).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)shape.Position.Z).IsEqualTo(1.0).Within(Tolerance);

        // Applying the actor pose puts the capsule back where the stage authored it: three metres
        // up the simulation axis, which proves the local pose maps back to the composed stage.
        ComposedActor owner = ReadActor(built, "/Box");
        (double X, double Y, double Z) world = RotateBy(
            owner.WorldPose.Rotation, shape.Position);
        await Assert.That(world.X + owner.WorldPose.Position.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That(world.Y + owner.WorldPose.Position.Y).IsEqualTo(3.0).Within(Tolerance);
        await Assert.That(world.Z + owner.WorldPose.Position.Z).IsEqualTo(0.0).Within(Tolerance);

        // A box carries no axis, so its shape pose stays at the body origin.
        ComposedShape box = ReadShape(built, "/Box.shape");
        await Assert.That((double)box.Rotation.W).IsEqualTo(1.0).Within(Tolerance);
        await Assert.That((double)box.HalfExtents.X).IsEqualTo(0.5).Within(Tolerance);
        await Assert.That((double)box.HalfExtents.Y).IsEqualTo(0.5).Within(Tolerance);
        await Assert.That((double)box.HalfExtents.Z).IsEqualTo(0.5).Within(Tolerance);
    }

    [Test]
    public async Task AnAuthoredDriveAndLimitInstanceComposeInSimulationUnits()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(AnAuthoredDriveAndLimitInstanceComposeInSimulationUnits), ZUpGramStage);

        UsdPhysicsExtractionObject joint = PhysicsExtractionStages.Find(
            page, "/Hinge", UsdPhysicsExtractionObjectKind.Joint);
        UsdPhysicsExtractionProperty stiffness = PhysicsExtractionStages.Property(
            page, joint, UsdPhysicsExtractionKey.DriveStiffness);
        await Assert.That(stiffness.Name).IsEqualTo("drive:angular:physics:stiffness");

        using var builder = new PhysxPageBuilder();
        UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();

        ComposedJoint hinge = ReadJoint(built);
        await Assert.That(hinge.Type).IsEqualTo((uint)PhysxJointType.Revolute);
        await Assert.That(hinge.Axis).IsEqualTo(1u);
        await Assert.That((hinge.Flags & (uint)PhysxJointFlags.DriveEnabled) != 0u).IsTrue();
        await Assert.That((hinge.Flags & (uint)PhysxJointFlags.LimitEnabled) != 0u).IsTrue();

        // Angular drives are authored per degree and produce a torque in stage units.
        double torque = 0.001 * 0.01 * 0.01;
        await Assert.That((double)hinge.DriveStiffness)
            .IsEqualTo(1000000.0 * torque / DegreesToRadians).Within(1e-3);
        await Assert.That((double)hinge.DriveDamping)
            .IsEqualTo(2000000.0 * torque / DegreesToRadians).Within(1e-3);
        await Assert.That((double)hinge.DriveMaxForce)
            .IsEqualTo(10000000.0 * torque).Within(Tolerance);
        await Assert.That((double)hinge.DriveTargetPosition)
            .IsEqualTo(90.0 * DegreesToRadians).Within(Tolerance);
        await Assert.That((double)hinge.DriveTargetVelocity)
            .IsEqualTo(30.0 * DegreesToRadians).Within(Tolerance);
        await Assert.That((double)hinge.LowerLimit)
            .IsEqualTo(-45.0 * DegreesToRadians).Within(Tolerance);
        await Assert.That((double)hinge.UpperLimit)
            .IsEqualTo(45.0 * DegreesToRadians).Within(Tolerance);

        // The same joint authored through the multi apply limit instance composes identically.
        UsdPhysicsExtractionPage instanced = PhysicsExtractionStages.Extract(
            nameof(AnAuthoredDriveAndLimitInstanceComposeInSimulationUnits),
            ZUpGramStage
                .Replace(
                    "float physics:lowerLimit = -45",
                    "float limit:angular:physics:low = -45",
                    StringComparison.Ordinal)
                .Replace(
                    "float physics:upperLimit = 45",
                    "float limit:angular:physics:high = 45",
                    StringComparison.Ordinal));

        UsdPhysicsExtractionObject instancedJoint = PhysicsExtractionStages.Find(
            instanced, "/Hinge", UsdPhysicsExtractionObjectKind.Joint);
        UsdPhysicsExtractionProperty low = PhysicsExtractionStages.Property(
            instanced, instancedJoint, UsdPhysicsExtractionKey.LimitLow);
        await Assert.That(low.Name).IsEqualTo("limit:angular:physics:low");

        using var instancedBuilder = new PhysxPageBuilder();
        UsdPhysicsExtractionComposer.Compose(instanced, instancedBuilder);
        using PhysxBuildPage instancedBuilt = instancedBuilder.Build();

        ComposedJoint instancedHinge = ReadJoint(instancedBuilt);
        await Assert.That((double)instancedHinge.LowerLimit)
            .IsEqualTo(-45.0 * DegreesToRadians).Within(Tolerance);
        await Assert.That((double)instancedHinge.UpperLimit)
            .IsEqualTo(45.0 * DegreesToRadians).Within(Tolerance);
        await Assert.That(
            (instancedHinge.Flags & (uint)PhysxJointFlags.LimitEnabled) != 0u).IsTrue();
    }

    [Test]
    public async Task SharedOwnershipAndMaterialBindingReachEveryObjectOnThePrim()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(SharedOwnershipAndMaterialBindingReachEveryObjectOnThePrim), ZUpGramStage);

        UsdPhysicsExtractionObject body = PhysicsExtractionStages.Find(
            page, "/Box", UsdPhysicsExtractionObjectKind.RigidBody);
        UsdPhysicsExtractionObject collider = PhysicsExtractionStages.Find(
            page, "/Box", UsdPhysicsExtractionObjectKind.Collider);

        // One prim wide consumption used to hand the shared relationships to the first object.
        await Assert.That(HasRelationship(
            page, body, UsdPhysicsExtractionKey.SimulationOwnerTargets)).IsTrue();
        await Assert.That(HasRelationship(
            page, collider, UsdPhysicsExtractionKey.MaterialBindingTargets)).IsTrue();

        UsdPhysicsExtractionObject scene = PhysicsExtractionStages.Find(
            page, "/Scene", UsdPhysicsExtractionObjectKind.Scene);
        await Assert.That(body.SceneIndex).IsEqualTo(scene.Index);

        using var builder = new PhysxPageBuilder();
        UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();

        // The bound material is the one the shapes of that body use.
        ComposedShape shape = ReadShape(built, "/Box.shape");
        await Assert.That(shape.MaterialIndex).IsEqualTo(0);
    }

    [Test]
    public async Task TheFingerprintCoversDriveLimitAndRelationshipEdits()
    {
        UsdPhysicsExtractionPage baseline = PhysicsExtractionStages.Extract(
            nameof(TheFingerprintCoversDriveLimitAndRelationshipEdits), ZUpGramStage);

        // A drive value only reaches the fingerprint when the traversal extracts it.
        UsdPhysicsExtractionPage drive = PhysicsExtractionStages.Extract(
            nameof(TheFingerprintCoversDriveLimitAndRelationshipEdits),
            ZUpGramStage.Replace(
                "float drive:angular:physics:stiffness = 1000000",
                "float drive:angular:physics:stiffness = 2000000",
                StringComparison.Ordinal));
        await Assert.That(drive.FingerprintLow).IsNotEqualTo(baseline.FingerprintLow);

        UsdPhysicsExtractionPage limit = PhysicsExtractionStages.Extract(
            nameof(TheFingerprintCoversDriveLimitAndRelationshipEdits),
            ZUpGramStage.Replace(
                "float physics:upperLimit = 45",
                "float physics:upperLimit = 60",
                StringComparison.Ordinal));
        await Assert.That(limit.FingerprintLow).IsNotEqualTo(baseline.FingerprintLow);

        UsdPhysicsExtractionPage owner = PhysicsExtractionStages.Extract(
            nameof(TheFingerprintCoversDriveLimitAndRelationshipEdits),
            ZUpGramStage.Replace(
                "rel physics:simulationOwner = </Scene>",
                "rel physics:simulationOwner = None",
                StringComparison.Ordinal));
        await Assert.That(owner.FingerprintLow).IsNotEqualTo(baseline.FingerprintLow);

        // A purely visual edit still leaves the fingerprint alone.
        UsdPhysicsExtractionPage visual = PhysicsExtractionStages.Extract(
            nameof(TheFingerprintCoversDriveLimitAndRelationshipEdits),
            ZUpGramStage.Replace(
                "double3 xformOp:translate = (0, 0, 200)",
                "double3 xformOp:translate = (0, 0, 200)\n"
                    + "        color3f[] primvars:displayColor = [(1, 0, 0)]",
                StringComparison.Ordinal));
        await Assert.That(visual.FingerprintLow).IsEqualTo(baseline.FingerprintLow);
        await Assert.That(visual.FingerprintHigh).IsEqualTo(baseline.FingerprintHigh);
    }

    [Test]
    public async Task AuthoredPrincipalAxesReachTheActorMassFrame()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(AuthoredPrincipalAxesReachTheActorMassFrame), MassFrameStage);

        // The rotation is part of the extracted page rather than being dropped on the floor.
        UsdPhysicsExtractionObject body = PhysicsExtractionStages.Find(
            page, "/Spinner", UsdPhysicsExtractionObjectKind.RigidBody);
        await Assert.That(PhysicsExtractionStages.TryFindProperty(
            page, body, UsdPhysicsExtractionKey.MassPrincipalAxes, out _)).IsTrue();

        using var builder = new PhysxPageBuilder();
        UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();
        PhysxPageValidationResult validation = PhysxPageValidator.Validate(built.Bytes);
        await Assert.That(validation.IsValid).IsTrue().Because(validation.Message ?? "no message");

        ComposedActor spinner = ReadActor(built, "/Spinner");

        // A centre of mass and an inertia are body local, so only the unit scale applies.
        await Assert.That((double)spinner.CenterOfMass.Z).IsEqualTo(0.5).Within(Tolerance);
        await Assert.That((double)spinner.Inertia.X).IsEqualTo(1e-4).Within(1e-9);
        await Assert.That((double)spinner.Inertia.Y).IsEqualTo(2e-4).Within(1e-9);
        await Assert.That((double)spinner.Inertia.Z).IsEqualTo(3e-4).Within(1e-9);

        // The principal axis frame is body local as well, so it keeps its authored components.
        double half = Math.Sqrt(0.5);
        await Assert.That((double)spinner.PrincipalAxes.W).IsEqualTo(half).Within(Tolerance);
        await Assert.That((double)spinner.PrincipalAxes.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)spinner.PrincipalAxes.Y).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)spinner.PrincipalAxes.Z).IsEqualTo(half).Within(Tolerance);

        // The first principal axis of a quarter turn about local Z is the local Y axis, which the
        // Z up stage basis turns into the simulation direction that points along negative Z.
        (double X, double Y, double Z) local = RotateBy(
            spinner.PrincipalAxes, new PhysxVec3f(1.0F, 0.0F, 0.0F));
        (double X, double Y, double Z) world = RotateBy(
            spinner.WorldPose.Rotation,
            new PhysxVec3f((float)local.X, (float)local.Y, (float)local.Z));
        await Assert.That(world.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That(world.Y).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That(world.Z).IsEqualTo(-1.0).Within(Tolerance);
    }

    [Test]
    public async Task AnUnauthoredPrincipalAxesFrameStaysTheIdentity()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(AnUnauthoredPrincipalAxesFrameStaysTheIdentity),
            MassFrameStage.Replace(
                "quatf physics:principalAxes = (0.70710678, 0, 0, 0.70710678)",
                string.Empty,
                StringComparison.Ordinal));

        using var builder = new PhysxPageBuilder();
        UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();

        ComposedActor spinner = ReadActor(built, "/Spinner");
        await Assert.That((double)spinner.PrincipalAxes.W).IsEqualTo(1.0).Within(Tolerance);
        await Assert.That((double)spinner.PrincipalAxes.X).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)spinner.PrincipalAxes.Y).IsEqualTo(0.0).Within(Tolerance);
        await Assert.That((double)spinner.PrincipalAxes.Z).IsEqualTo(0.0).Within(Tolerance);
    }

    [Test]
    public async Task AJointThatAuthorsNoLimitStaysFreeOnARealStage()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(AJointThatAuthorsNoLimitStaysFreeOnARealStage), FreeJointStage);

        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();
        PhysxPageValidationResult validation = PhysxPageValidator.Validate(built.Bytes);
        await Assert.That(validation.IsValid).IsTrue().Because(validation.Message ?? "no message");

        ComposedJoint hinge = ReadJoint(built);
        await Assert.That((hinge.Flags & (uint)PhysxJointFlags.LimitEnabled) != 0u).IsFalse();
        await Assert.That(hinge.LowerLimit).IsEqualTo(0.0F);
        await Assert.That(hinge.UpperLimit).IsEqualTo(0.0F);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task AHalfAuthoredOrLockedRangeIsReportedOnARealStage()
    {
        UsdPhysicsExtractionPage half = PhysicsExtractionStages.Extract(
            nameof(AHalfAuthoredOrLockedRangeIsReportedOnARealStage) + "Half",
            FreeJointStage.Replace(
                "    uniform token physics:axis = \"Y\"",
                "    uniform token physics:axis = \"Y\"\n"
                    + "    float physics:lowerLimit = -30",
                StringComparison.Ordinal));
        UsdPhysicsExtractionPage locked = PhysicsExtractionStages.Extract(
            nameof(AHalfAuthoredOrLockedRangeIsReportedOnARealStage) + "Locked",
            FreeJointStage.Replace(
                "    uniform token physics:axis = \"Y\"",
                "    uniform token physics:axis = \"Y\"\n"
                    + "    float physics:lowerLimit = 30\n"
                    + "    float physics:upperLimit = -30",
                StringComparison.Ordinal));
        UsdPhysicsExtractionPage ranged = PhysicsExtractionStages.Extract(
            nameof(AHalfAuthoredOrLockedRangeIsReportedOnARealStage) + "Ranged",
            FreeJointStage.Replace(
                "    uniform token physics:axis = \"Y\"",
                "    uniform token physics:axis = \"Y\"\n"
                    + "    float physics:lowerLimit = -30\n"
                    + "    float physics:upperLimit = 30",
                StringComparison.Ordinal));

        (ComposedJoint Joint, ImmutableArray<string> Skipped) halfResult = ComposeOne(half);
        (ComposedJoint Joint, ImmutableArray<string> Skipped) lockedResult = ComposeOne(locked);
        (ComposedJoint Joint, ImmutableArray<string> Skipped) rangedResult = ComposeOne(ranged);

        await Assert.That(
            (halfResult.Joint.Flags & (uint)PhysxJointFlags.LimitEnabled) != 0u).IsFalse();
        await Assert.That(halfResult.Skipped.Length).IsEqualTo(1);
        await Assert.That(halfResult.Skipped[0]).Contains("one side");

        await Assert.That(
            (lockedResult.Joint.Flags & (uint)PhysxJointFlags.LimitEnabled) != 0u).IsFalse();
        await Assert.That(lockedResult.Skipped.Length).IsEqualTo(1);
        await Assert.That(lockedResult.Skipped[0]).Contains("locks an axis");

        await Assert.That(
            (rangedResult.Joint.Flags & (uint)PhysxJointFlags.LimitEnabled) != 0u).IsTrue();
        await Assert.That((double)rangedResult.Joint.LowerLimit)
            .IsEqualTo(-30.0 * DegreesToRadians).Within(Tolerance);
        await Assert.That((double)rangedResult.Joint.UpperLimit)
            .IsEqualTo(30.0 * DegreesToRadians).Within(Tolerance);
        await Assert.That(rangedResult.Skipped.Length).IsEqualTo(0);
    }

    // Two bodies where one authors values the runtime cannot take, so the healthy body proves
    // that a page still composes and validates around the reduced one.
    private const string MalformedValueStage = """
        #usda 1.0
        (
            metersPerUnit = 1
            upAxis = "Y"
        )

        def PhysicsScene "Scene"
        {
        }

        def Cube "Healthy" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI", "PhysicsMassAPI"]
        )
        {
            double size = 1
            float physics:mass = 3
        }

        def Cube "Broken" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI", "PhysicsMassAPI"]
        )
        {
            double size = 1
            double3 xformOp:translate = (0, 4, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
            float physics:mass = -5
            vector3f physics:velocity = (nan, 0, 0)
            point3f physics:centerOfMass = (inf, 0, 0)
            float3 physics:diagonalInertia = (nan, 1, 1)
        }
        """;

    [Test]
    public async Task AMalformedBodyIsReducedWhileTheRestOfTheStageStillComposes()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(AMalformedBodyIsReducedWhileTheRestOfTheStageStillComposes),
            MalformedValueStage);

        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);

        // Building validates the whole page, so it only succeeds when no unusable value reached
        // it and the healthy body was not taken down with the broken one.
        using PhysxBuildPage built = builder.Build();
        PhysxPageValidationResult validation = PhysxPageValidator.Validate(built.Bytes);
        await Assert.That(validation.IsValid).IsTrue().Because(validation.Message ?? "no message");
        await Assert.That(report.Actors).IsEqualTo(2);

        ComposedActor healthy = ReadActor(built, "/Healthy");
        await Assert.That(healthy.Mass).IsEqualTo(3.0F);

        ComposedActor broken = ReadActor(built, "/Broken");
        await Assert.That(broken.Mass).IsEqualTo(0.0F);
        await Assert.That(broken.LinearVelocity).IsEqualTo(default(PhysxVec3f));
        await Assert.That(broken.CenterOfMass).IsEqualTo(default(PhysxVec3f));
        await Assert.That(broken.Inertia).IsEqualTo(default(PhysxVec3f));

        ImmutableArray<string> notes = report.Skipped;
        await Assert.That(notes.Length).IsEqualTo(4);
        await Assert.That(notes[0]).Contains("/Broken");
        await Assert.That(notes[0]).Contains("mass");
        await Assert.That(notes[1]).Contains("linear velocity");
        await Assert.That(notes[2]).Contains("center of mass");
        await Assert.That(notes[3]).Contains("diagonal inertia");
    }

    private static (ComposedJoint Joint, ImmutableArray<string> Skipped) ComposeOne(
        UsdPhysicsExtractionPage page)
    {
        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();
        return (ReadJoint(built), report.Skipped);
    }

    private static bool HasRelationship(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key)
    {
        for (int offset = 0; offset < item.RelationshipCount; offset++)
        {
            if (page.GetRelationship(item.RelationshipStart + offset).Key == key)
            {
                return true;
            }
        }
        return false;
    }

    private static ComposedActor ReadActor(PhysxBuildPage built, string path)
    {
        PhysxPageReader reader = built.CreateReader();
        ulong id = FindIdentity(reader, path);
        for (int index = 0; index < reader.Actors.Length; index++)
        {
            PhysxActorDesc actor = reader.Actors[index];
            if (actor.Id == id)
            {
                return new ComposedActor(
                    actor.Type,
                    actor.ShapeCount,
                    actor.Mass,
                    actor.LinearVelocity,
                    actor.AngularVelocity,
                    actor.WorldPose,
                    actor.CenterOfMass,
                    actor.Inertia,
                    actor.PrincipalAxes);
            }
        }
        throw new InvalidOperationException($"The build page has no actor for {path}.");
    }

    private static ComposedShape ReadShape(PhysxBuildPage built, string path)
    {
        PhysxPageReader reader = built.CreateReader();
        ulong id = FindIdentity(reader, path);
        for (int index = 0; index < reader.Shapes.Length; index++)
        {
            PhysxShapeDesc shape = reader.Shapes[index];
            if (shape.Id == id)
            {
                return new ComposedShape(
                    shape.Type,
                    shape.Radius,
                    shape.HalfHeight,
                    shape.HalfExtents,
                    shape.Scale,
                    shape.LocalPose.Position,
                    shape.LocalPose.Rotation,
                    shape.MaterialIndex);
            }
        }
        throw new InvalidOperationException($"The build page has no shape for {path}.");
    }

    private static ComposedScene ReadScene(PhysxBuildPage built)
    {
        PhysxPageReader reader = built.CreateReader();
        PhysxSceneDesc scene = reader.Scenes[0];
        return new ComposedScene(scene.GravityDirection, scene.GravityMagnitude);
    }

    private static ComposedJoint ReadJoint(PhysxBuildPage built)
    {
        PhysxPageReader reader = built.CreateReader();
        PhysxJointDesc joint = reader.Joints[0];
        return new ComposedJoint(
            joint.Type,
            joint.Flags,
            joint.Axis,
            joint.LowerLimit,
            joint.UpperLimit,
            joint.DriveStiffness,
            joint.DriveDamping,
            joint.DriveMaxForce,
            joint.DriveTargetPosition,
            joint.DriveTargetVelocity);
    }

    private static float ReadMaterialDensity(PhysxBuildPage built)
    {
        PhysxPageReader reader = built.CreateReader();
        return reader.Materials[0].Density;
    }

    private static bool OrphanNames(UsdPhysicsExtractionPage page, int objectIndex)
    {
        for (int index = 0; index < page.DiagnosticCount; index++)
        {
            UsdPhysicsExtractionDiagnostic diagnostic = page.GetDiagnostic(index);
            if (diagnostic.Code == UsdPhysicsExtractionCode.OrphanedCollider &&
                diagnostic.ObjectIndex == objectIndex)
            {
                return true;
            }
        }
        return false;
    }

    private static (double X, double Y, double Z) RotateBy(PhysxQuatf rotation, PhysxVec3f value)
    {
        double w = rotation.W;
        double x = rotation.X;
        double y = rotation.Y;
        double z = rotation.Z;
        double vx = value.X;
        double vy = value.Y;
        double vz = value.Z;
        double tx = (2.0 * ((y * vz) - (z * vy)));
        double ty = (2.0 * ((z * vx) - (x * vz)));
        double tz = (2.0 * ((x * vy) - (y * vx)));
        return (
            vx + (w * tx) + ((y * tz) - (z * ty)),
            vy + (w * ty) + ((z * tx) - (x * tz)),
            vz + (w * tz) + ((x * ty) - (y * tx)));
    }

    private static ulong FindIdentity(PhysxPageReader reader, string path)
    {
        for (int index = 0; index < reader.Identities.Length; index++)
        {
            PhysxIdentityRecord identity = reader.Identities[index];
            if (string.Equals(reader.GetPath(in identity), path, StringComparison.Ordinal))
            {
                return identity.Id;
            }
        }
        throw new InvalidOperationException($"The build page has no identity for {path}.");
    }

    private readonly record struct ComposedActor(
        uint Type,
        uint ShapeCount,
        float Mass,
        PhysxVec3f LinearVelocity,
        PhysxVec3f AngularVelocity,
        PhysxTransform WorldPose,
        PhysxVec3f CenterOfMass,
        PhysxVec3f Inertia,
        PhysxQuatf PrincipalAxes);

    private readonly record struct ComposedShape(
        uint Type,
        float Radius,
        float HalfHeight,
        PhysxVec3f HalfExtents,
        PhysxVec3f Scale,
        PhysxVec3f Position,
        PhysxQuatf Rotation,
        int MaterialIndex);

    private readonly record struct ComposedScene(PhysxVec3f Direction, float Magnitude);

    private readonly record struct ComposedJoint(
        uint Type,
        uint Flags,
        uint Axis,
        float LowerLimit,
        float UpperLimit,
        float DriveStiffness,
        float DriveDamping,
        float DriveMaxForce,
        float DriveTargetPosition,
        float DriveTargetVelocity);
}
