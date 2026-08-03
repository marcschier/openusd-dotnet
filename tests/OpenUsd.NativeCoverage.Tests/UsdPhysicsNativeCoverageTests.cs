// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;

namespace OpenUsd.NativeCoverage.Tests;

public sealed class UsdPhysicsNativeCoverageTests
{
    [Test]
    public async Task RigidBodyMassLimitAndDriveRoundTripOnRealStage()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(RigidBodyMassLimitAndDriveRoundTripOnRealStage));
        string stagePath = Path.Combine(directory, "usdphysics-rigid-body-roundtrip.usda");
        using UsdStage stage = UsdStage.Create(stagePath);
        UsdPrim bodyPrim = stage.DefinePrim("/World/Body", "Xform");

        UsdPhysicsRigidBodyAPI rigidBody = UsdPhysicsRigidBodyAPI.Apply(bodyPrim);
        rigidBody.RigidBodyEnabled = true;
        rigidBody.KinematicEnabled = true;
        rigidBody.StartsAsleep = true;
        rigidBody.Velocity = new UsdVec3f(1.25F, 2.5F, 3.75F);
        rigidBody.AngularVelocity = new UsdVec3f(4.25F, 5.5F, 6.75F);

        UsdPhysicsMassAPI mass = UsdPhysicsMassAPI.Apply(bodyPrim);
        mass.Mass = 42.5F;
        mass.Density = 7.25F;
        mass.CenterOfMass = new UsdVec3f(0.25F, 0.5F, 0.75F);
        mass.DiagonalInertia = new UsdVec3f(8.5F, 9.5F, 10.5F);
        mass.PrincipalAxes = new UsdQuatf(0.5F, 0.25F, 0.125F, 0.0625F);

        UsdPhysicsLimitAPI limit = UsdPhysicsLimitAPI.Apply(bodyPrim, UsdPhysicsTokens.RotX);
        limit.Low = -15.5F;
        limit.High = 30.25F;

        UsdPhysicsDriveAPI drive = UsdPhysicsDriveAPI.Apply(bodyPrim, UsdPhysicsTokens.Angular);
        drive.Type = UsdPhysicsDriveType.Acceleration;
        drive.MaxForce = 100.5F;
        drive.TargetPosition = 12.25F;
        drive.TargetVelocity = 3.5F;
        drive.Damping = 4.75F;
        drive.Stiffness = 5.125F;

        IReadOnlyList<string> appliedSchemas = bodyPrim.GetAppliedSchemas();
        await Assert.That(appliedSchemas.Contains("PhysicsRigidBodyAPI")).IsTrue();
        await Assert.That(appliedSchemas.Contains("PhysicsMassAPI")).IsTrue();
        await Assert.That(appliedSchemas.Contains("PhysicsLimitAPI:rotX")).IsTrue();
        await Assert.That(appliedSchemas.Contains("PhysicsDriveAPI:angular")).IsTrue();
        await Assert.That(UsdPhysicsRigidBodyAPI.Has(bodyPrim)).IsTrue();
        await Assert.That(UsdPhysicsMassAPI.Has(bodyPrim)).IsTrue();
        await Assert.That(UsdPhysicsLimitAPI.Has(bodyPrim, UsdPhysicsTokens.RotX)).IsTrue();
        await Assert.That(UsdPhysicsDriveAPI.Has(bodyPrim, UsdPhysicsTokens.Angular)).IsTrue();

        UsdPhysicsRigidBodyAPI readRigidBody = UsdPhysicsRigidBodyAPI.Wrap(bodyPrim);
        await Assert.That(readRigidBody.RigidBodyEnabled).IsTrue();
        await Assert.That(readRigidBody.KinematicEnabled).IsTrue();
        await Assert.That(readRigidBody.StartsAsleep).IsTrue();
        await Assert.That(readRigidBody.Velocity).IsEqualTo(new UsdVec3f(1.25F, 2.5F, 3.75F));
        await Assert.That(readRigidBody.AngularVelocity).IsEqualTo(new UsdVec3f(4.25F, 5.5F, 6.75F));

        UsdPhysicsMassAPI readMass = UsdPhysicsMassAPI.Wrap(bodyPrim);
        await Assert.That(readMass.Mass).IsEqualTo(42.5F);
        await Assert.That(readMass.Density).IsEqualTo(7.25F);
        await Assert.That(readMass.CenterOfMass).IsEqualTo(new UsdVec3f(0.25F, 0.5F, 0.75F));
        await Assert.That(readMass.DiagonalInertia).IsEqualTo(new UsdVec3f(8.5F, 9.5F, 10.5F));
        await Assert.That(readMass.PrincipalAxes).IsEqualTo(new UsdQuatf(0.5F, 0.25F, 0.125F, 0.0625F));

        UsdPhysicsLimitAPI readLimit = UsdPhysicsLimitAPI.Wrap(bodyPrim, UsdPhysicsTokens.RotX);
        await Assert.That(readLimit.Low).IsEqualTo(-15.5F);
        await Assert.That(readLimit.High).IsEqualTo(30.25F);

        UsdPhysicsDriveAPI readDrive = UsdPhysicsDriveAPI.Wrap(bodyPrim, UsdPhysicsTokens.Angular);
        await Assert.That(readDrive.Type).IsEqualTo(UsdPhysicsDriveType.Acceleration);
        await Assert.That(readDrive.MaxForce).IsEqualTo(100.5F);
        await Assert.That(readDrive.TargetPosition).IsEqualTo(12.25F);
        await Assert.That(readDrive.TargetVelocity).IsEqualTo(3.5F);
        await Assert.That(readDrive.Damping).IsEqualTo(4.75F);
        await Assert.That(readDrive.Stiffness).IsEqualTo(5.125F);
    }

    [Test]
    public async Task JointRelationshipsAndFramesRoundTripOnRealStage()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(JointRelationshipsAndFramesRoundTripOnRealStage));
        string stagePath = Path.Combine(directory, "usdphysics-joint-roundtrip.usda");
        using UsdStage stage = UsdStage.Create(stagePath);
        stage.DefinePrim("/World/Body0", "Xform");
        stage.DefinePrim("/World/Body1", "Xform");
        UsdPhysicsRevoluteJoint revolute = stage.DefinePhysicsRevoluteJoint("/World/Hinge");
        UsdPhysicsJoint joint = revolute.Joint;

        joint.SetBody0("/World/Body0");
        joint.SetBody1("/World/Body1");
        joint.LocalPos0 = new UsdVec3f(1, 2, 3);
        joint.LocalPos1 = new UsdVec3f(4, 5, 6);
        joint.LocalRot0 = new UsdQuatf(1, 0.1F, 0.2F, 0.3F);
        joint.LocalRot1 = new UsdQuatf(0.9F, 0.4F, 0.5F, 0.6F);
        joint.JointEnabled = false;
        joint.CollisionEnabled = true;
        joint.ExcludeFromArticulation = true;
        joint.BreakForce = 123.5F;
        joint.BreakTorque = 456.75F;
        revolute.Axis = UsdPhysicsAxis.Z;
        revolute.LowerLimit = -45;
        revolute.UpperLimit = 90;

        UsdPhysicsRevoluteJoint readRevolute = UsdPhysicsRevoluteJoint.Wrap(stage.GetPrim("/World/Hinge"));
        UsdPhysicsJoint readJoint = readRevolute.Joint;
        await Assert.That(readJoint.GetBody0().SequenceEqual(["/World/Body0"])).IsTrue();
        await Assert.That(readJoint.GetBody1().SequenceEqual(["/World/Body1"])).IsTrue();
        await Assert.That(readJoint.LocalPos0).IsEqualTo(new UsdVec3f(1, 2, 3));
        await Assert.That(readJoint.LocalPos1).IsEqualTo(new UsdVec3f(4, 5, 6));
        await Assert.That(readJoint.LocalRot0).IsEqualTo(new UsdQuatf(1, 0.1F, 0.2F, 0.3F));
        await Assert.That(readJoint.LocalRot1).IsEqualTo(new UsdQuatf(0.9F, 0.4F, 0.5F, 0.6F));
        await Assert.That(readJoint.JointEnabled).IsFalse();
        await Assert.That(readJoint.CollisionEnabled).IsTrue();
        await Assert.That(readJoint.ExcludeFromArticulation).IsTrue();
        await Assert.That(readJoint.BreakForce).IsEqualTo(123.5F);
        await Assert.That(readJoint.BreakTorque).IsEqualTo(456.75F);
        await Assert.That(readRevolute.Axis).IsEqualTo(UsdPhysicsAxis.Z);
        await Assert.That(readRevolute.LowerLimit).IsEqualTo(-45);
        await Assert.That(readRevolute.UpperLimit).IsEqualTo(90);
    }

    [Test]
    public async Task TryWrapWrongTypeFailsPredictably()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(nameof(TryWrapWrongTypeFailsPredictably));
        string stagePath = Path.Combine(directory, "usdphysics-wrong-type.usda");
        using UsdStage stage = UsdStage.Create(stagePath);
        UsdPrim prim = stage.DefinePrim("/World/PlainXform", "Xform");

        bool wrappedScene = UsdPhysicsScene.TryWrap(prim, out UsdPhysicsScene scene);
        bool wrappedJoint = UsdPhysicsJoint.TryWrap(prim, out UsdPhysicsJoint joint);

        await Assert.That(wrappedScene).IsFalse();
        await Assert.That(wrappedJoint).IsFalse();
        await Assert.That(string.IsNullOrEmpty(scene.Path)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(joint.Path)).IsTrue();
    }
}
